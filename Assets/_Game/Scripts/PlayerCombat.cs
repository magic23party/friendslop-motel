using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Friendslop
{
    /// <summary>
    /// Бой игрока (C5): удар кувалдой (серверный луч от камеры), «без сознания» (IsDown), удары от гостей.
    /// Клиент-владелец при ЛКМ с кувалдой шлёт HitRpc(позиция/направление камеры); сервер сам проверяет кулдаун,
    /// предмет в руках, дистанцию и решает, кого задело (гость → Guest.ServerHit, игрок → отключка на colleagueHitDownTime).
    /// Без сознания: IsDown (NetworkVariable) — сервер блокирует RPC рук, владелец не двигается, камера у пола;
    /// предмет из рук выпадает. Мах кувалдой и вспышка попадания — RPC всем (видно всем).
    /// </summary>
    public class PlayerCombat : NetworkBehaviour
    {
        public static readonly System.Collections.Generic.List<PlayerCombat> All = new System.Collections.Generic.List<PlayerCombat>();
        static readonly RaycastHit[] buf = new RaycastHit[24];
        const int HitMask = ~(1 << Items.ItemLayer);       // предметы на полу удар не перехватывают
        static readonly bool HammerTestLog = NetBootstrap.Arg("-hammertest");
        const float SwingTime = 0.28f, FlashTime = 0.3f, SwingAngle0 = 75f;

        public NetworkVariable<bool> IsDown = new NetworkVariable<bool>(false);
        /// <summary>V3: после пробуждения wakeInvuln секунд игрок неуязвим (защита от стан-лока); у всех он мерцает.</summary>
        public NetworkVariable<bool> Protected = new NetworkVariable<bool>(false);

        PlayerHands hands;
        PlayerController pc;
        Transform visual;                   // коллайдер-капсула + модель человечка: ложится на пол при отключке
        Humanoid humanoid;
        StunStars stars;

        // сервер
        float nextHitAt, downUntil, lastTakenAt, protectedUntil;
        int hitsTaken;

        // все машины (визуал)
        float swingT, flashT, lie, screenFlash;
        Renderer[] flickerRenderers;
        bool flickerHidden;
        /// <summary>Скриншоты: 0..1 — заморозить фазу взмаха, &lt;0 — обычная работа.</summary>
        public float ShotSwing = -1f;
        /// <summary>Текущий угол взмаха, градусы (для viewmodel владельца: PlayerHands).</summary>
        public float SwingAngle { get; private set; }
        static GUIStyle bigStyle;

        /// <summary>Сервер: неуязвим (только что очнулся). Удар, оглушение и удары гостей не проходят.</summary>
        public bool Invulnerable => Time.time < protectedUntil;
        /// <summary>Только самотесты: неуязвимость на seconds секунд (гость бьёт впустую, но счётчик Guest.Punches растёт).</summary>
        public void ServerTestShield(float seconds) { if (IsServer) protectedUntil = Time.time + seconds; }
        /// <summary>Сервер, для самотестов: сколько ударов/оглушений погасила неуязвимость.</summary>
        public static int InvulnBlocks;

        public float HitCooldown => hands != null && hands.config != null ? hands.config.hitCooldown : 0.6f;

        void Awake()
        {
            // коллайдер-капсула и модель (рука для чужих копий внутри неё) — под общий узел, чтобы одним поворотом «уложить» игрока
            var v = new GameObject("Visual").transform;
            v.SetParent(transform, false);
            foreach (var n in new[] { "Body", "Model" })
            {
                var c = transform.Find(n);
                if (c != null) c.SetParent(v, false);
            }
            visual = v;
            humanoid = v.GetComponentInChildren<Humanoid>(true);
        }

        public override void OnNetworkSpawn()
        {
            hands = GetComponent<PlayerHands>();
            pc = GetComponent<PlayerController>();
            All.Add(this);
            IsDown.OnValueChanged += OnDownChanged;
            if (IsOwner && NetBootstrap.Arg("-hammertest")) gameObject.AddComponent<HammerSelfTest>();
        }

        public override void OnNetworkDespawn() { All.Remove(this); IsDown.OnValueChanged -= OnDownChanged; }

        void OnDownChanged(bool was, bool now)
        {
            if (now) Sfx.Play3D(Snd.Thud, transform.position + Vector3.up * 0.4f, 1f);      // падение: слышно всем рядом
        }

        // ------------------------------------------------------------ сервер: удар кувалдой

        /// <summary>
        /// Сервер: удар кувалдой лучом от камеры. origin/dir присылает клиент; origin дальше slack от глаз игрока заменяется глазами.
        /// false — удар не состоялся (нет кувалды, кулдаун, без сознания, смена не идёт).
        /// </summary>
        public bool ServerStrike(Vector3 origin, Vector3 dir)
        {
            if (HammerTestLog) Debug.Log($"[HAMMER] ServerStrike enter {name} owner={OwnerClientId} held={hands?.Held.Value.type} down={IsDown.Value} phase={GameState.Instance?.Phase.Value} t={Time.time} next={nextHitAt}");
            if (!IsServer || !IsSpawned || hands == null || IsDown.Value) return false;
            if (hands.Held.Value.type != ItemType.Sledgehammer) return false;
            var gs = GameState.Instance;
            if (gs == null || gs.Phase.Value != GamePhase.Playing) return false;
            if (Time.time < nextHitAt) return false;
            var cfg = hands.config;
            nextHitAt = Time.time + cfg.hitCooldown * 0.9f;         // небольшой запас на джиттер сети

            var eye = hands.Eye;
            if (!(Vector3.SqrMagnitude(origin - eye) <= cfg.interactReachSlack * cfg.interactReachSlack)) origin = eye;
            dir = dir.sqrMagnitude < 0.01f ? transform.forward : dir.normalized;

            SwingRpc();         // мах виден всем, даже если промахнулся

            int n = Physics.RaycastNonAlloc(origin, dir, buf, cfg.hitDistance, HitMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 point = origin + dir * cfg.hitDistance;
            Guest guest = null; PlayerCombat other = null;
            var desk = MotelMap.Instance != null ? MotelMap.Instance.receptionDesk : null;
            for (int i = 0; i < n; i++)
            {
                var h = buf[i];
                if (h.distance >= best) continue;
                if (desk != null && h.collider.transform == desk) continue;       // стойка ресепшена удар не гасит (как и луч прицела, P4): бьём гостя в очереди через неё
                var self = h.collider.GetComponentInParent<PlayerCombat>();
                if (self == this) continue;
                best = h.distance;
                point = h.point;
                guest = h.collider.GetComponentInParent<Guest>();
                other = self;                // null, если это стена/мебель — она перекрывает луч
            }
            if (HammerTestLog) Debug.Log($"[HAMMER] ServerStrike {name} owner={OwnerClientId} origin={origin} eye={eye} dir={dir} n={n} guest={(guest != null)} other={(other != null)} point={point} dist={best}");
            if (guest != null || other != null) HitFxRpc(point);       // искры, звук и тряска у бьющего — у всех клиентов
            if (guest != null) { guest.ServerHit(hands); Debug.Log($"[FIGHT] {name} hit guest"); }
            else if (other != null)
            {
                Debug.Log($"[FIGHT] {name} hit player {other.name}");
                other.ServerKnockOut(cfg.colleagueHitDownTime);
            }
            return true;
        }

        /// <summary>Сервер: удар гостя по игроку. true — игрок потерял сознание (гость должен сбежать).</summary>
        public bool ServerGuestHit()
        {
            if (!IsServer || !IsSpawned || IsDown.Value || hands == null) return false;
            if (Invulnerable) { InvulnBlocks++; return false; }           // V3: только очнулся — гость бьёт впустую, счётчик ударов не растёт
            var cfg = hands.config;
            if (Time.time - lastTakenAt > cfg.playerHitForget) hitsTaken = 0;
            lastTakenAt = Time.time;
            hitsTaken++;
            HurtRpc();
            Debug.Log($"[FIGHT] {name} punched by guest ({hitsTaken}/{cfg.hitsToKnockPlayer})");
            if (hitsTaken < cfg.hitsToKnockPlayer) return false;
            ServerKnockOut(cfg.playerDownTime);
            return true;
        }

        /// <summary>Сервер: игрок без сознания на seconds секунд; предмет из рук выпадает.</summary>
        public void ServerKnockOut(float seconds)
        {
            if (!IsServer || !IsSpawned || IsDown.Value) return;
            if (Invulnerable) { InvulnBlocks++; Debug.Log($"[FIGHT] {name} is invulnerable after waking up, knock-out ignored"); return; }
            hitsTaken = 0;
            downUntil = Time.time + seconds;
            IsDown.Value = true;
            HurtRpc();
            if (hands != null)
            {
                var h = hands.ServerClear();
                if (!h.IsNone)
                    WorldItem.Spawn(h, transform.position + Vector3.up * 1.2f + transform.forward * 0.4f,
                        new Vector3(Random.Range(-1f, 1f), 1.5f, Random.Range(-1f, 1f)));
            }
            Debug.Log($"[FIGHT] {name} knocked out for {seconds:0.#}s");
        }

        /// <summary>Сервер: поднять игрока сразу (скриншоты).</summary>
        public void ServerWake() { if (IsServer && IsSpawned) { IsDown.Value = false; downUntil = 0f; } }

        // ------------------------------------------------------------ клиент-владелец: ввод

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void HitRpc(Vector3 origin, Vector3 dir) { ServerStrike(origin, dir); }

        [Rpc(SendTo.Everyone)]
        void SwingRpc()
        {
            swingT = SwingTime;
            Sfx.Play3D(Snd.Whoosh, transform.position + Vector3.up * 1.4f, 0.7f, Random.Range(0.92f, 1.08f));
        }

        /// <summary>Удар попал (RPC всем): искры и звук в точке попадания; бьющему — лёгкая тряска камеры.</summary>
        [Rpc(SendTo.Everyone)]
        void HitFxRpc(Vector3 point)
        {
            Fx.Hit(point);
            Sfx.Play3D(Snd.Hit, point, 1f, Random.Range(0.9f, 1.1f));
            var cfg = GameConfig.Active;
            if (IsOwner && cfg != null) CameraShake.Add(cfg.shakeHit);
        }

        /// <summary>Игрока ударили (RPC всем): вспышка модели, красный экран и тряска камеры — у пострадавшего.</summary>
        [Rpc(SendTo.Everyone)]
        void HurtRpc()
        {
            flashT = FlashTime;
            Sfx.Play3D(Snd.Hit, transform.position + Vector3.up * 1.2f, 0.9f, Random.Range(0.85f, 1.0f));
            if (!IsOwner) return;
            screenFlash = 0.35f;
            var cfg = GameConfig.Active;
            if (cfg != null) CameraShake.Add(cfg.shakeHurt);
        }

        float nextLocalHit;
        /// <summary>Самотест -hammertest: имитирует зажатую ЛКМ у владельца.</summary>
        public bool TestFire;

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer && IsDown.Value && Time.time >= downUntil)
            {
                float inv = hands != null && hands.config != null ? hands.config.wakeInvuln : 0.5f;
                IsDown.Value = false;
                if (inv > 0f) { protectedUntil = Time.time + inv; Protected.Value = true; }
                Debug.Log($"[FIGHT] {name} regained consciousness, invulnerable for {inv:0.##}s");
            }
            if (IsServer && Protected.Value && Time.time >= protectedUntil) Protected.Value = false;
            if (!IsOwner || pc == null || hands == null || !pc.Placed || pc.MenuOpen || IsDown.Value) return;
            bool pressed = TestFire;        // -hammertest: «нажатая ЛКМ» без мыши, дальше тот же путь, что у живого ввода
            if (!pressed)
            {
                var ms = Mouse.current;
                if (ms == null || !Application.isFocused) return;
                pressed = ms.leftButton.isPressed;
            }
            if (pc.cam == null) return;
            if (hands.Held.Value.type != ItemType.Sledgehammer) return;
            if (pressed && Time.time >= nextLocalHit)
            {
                nextLocalHit = Time.time + HitCooldown;
                var t = pc.cam.transform;
                HitRpc(t.position, t.forward);
            }
        }

        // ------------------------------------------------------------ визуал (все машины)

        void LateUpdate()
        {
            if (!IsSpawned || visual == null) return;
            float dt = Time.deltaTime;

            lie = Mathf.MoveTowards(lie, IsDown.Value ? 1f : 0f, dt * 6f);
            visual.localRotation = Quaternion.Euler(-90f * lie, 0f, 0f);
            visual.localPosition = new Vector3(0f, 0.4f * lie, 0f);      // капсула-коллайдер лежит на полу (радиус 0.4)

            // мах кувалдой: рука качнётся вперёд-вниз и вернётся (у чужих — рука модели, у владельца — viewmodel в PlayerHands)
            if (ShotSwing >= 0f) swingT = SwingTime * (1f - ShotSwing);
            else if (swingT > 0f) swingT = Mathf.Max(0f, swingT - dt);
            SwingAngle = Mathf.Sin((1f - swingT / SwingTime) * Mathf.PI) * (swingT > 0f ? SwingAngle0 : 0f);
            if (humanoid != null)
            {
                humanoid.lie = lie;
                humanoid.transform.localPosition = new Vector3(0f, -0.26f * lie, 0f);   // человечек тоньше капсулы: ложится на пол
                humanoid.swing = SwingAngle;
                // вспышка попадания у чужих копий
                if (flashT > 0f) flashT = Mathf.Max(0f, flashT - dt);
                humanoid.SetFlash(IsOwner ? 0f : flashT / FlashTime);
            }
            if (screenFlash > 0f) screenFlash = Mathf.Max(0f, screenFlash - dt);

            // V3: мерцание неуязвимого (у владельца тела от первого лица нет — у него лёгкая вспышка экрана в OnGUI)
            if (humanoid != null && !IsOwner)
            {
                bool flick = Protected.Value && !IsDown.Value;
                if (flick || flickerHidden)
                {
                    if (flickerRenderers == null) flickerRenderers = humanoid.GetComponentsInChildren<Renderer>(true);
                    bool show = !flick || (Mathf.FloorToInt(Time.time * 16f) & 1) == 0;
                    if (show == flickerHidden) { foreach (var r in flickerRenderers) if (r != null) r.enabled = show; flickerHidden = !show; }
                }
            }

            // «звёздочки» над оглушённым (видны всем: IsDown — NetworkVariable)
            if (stars == null && IsDown.Value) stars = StunStars.Attach(transform);
            if (stars != null) stars.Tick(IsDown.Value, lie, dt);
        }

        void OnGUI()
        {
            if (!IsSpawned || !IsOwner) return;
            bool down = IsDown.Value;
            bool prot = Protected.Value && !down;
            if (screenFlash <= 0f && !down && !prot) return;
            Ui.Begin();
            var full = new Rect(0, 0, Ui.W, Ui.H);
            if (prot)
            {
                var oldc = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.05f + 0.07f * (0.5f + 0.5f * Mathf.Sin(Time.time * 28f)));
                GUI.DrawTexture(full, Texture2D.whiteTexture);
                GUI.color = oldc;
            }
            if (screenFlash > 0f)
            {
                var old = GUI.color;
                GUI.color = new Color(0.9f, 0.05f, 0.05f, screenFlash);
                GUI.DrawTexture(full, Texture2D.whiteTexture);
                GUI.color = old;
            }
            if (down)
            {
                UiStyle.Dim(full, 0.45f);
                if (bigStyle == null) bigStyle = new GUIStyle(Ui.Title) { fontSize = 40 };
                Ui.Shadowed(new Rect(0, Ui.H / 2f - 80, Ui.W, 60), "Вы без сознания…", bigStyle, new Color(1f, 0.85f, 0.85f));
            }
            Ui.Reset();
        }
    }
}
