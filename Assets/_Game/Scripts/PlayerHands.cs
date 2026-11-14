using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Friendslop
{
    /// <summary>
    /// Руки игрока: 1 предмет (Held), луч взаимодействия, E / G / ЛКМ.
    /// Клиент-владелец только целится и шлёт RPC; исход решает сервер по (Held, цель):
    /// цель реализует IInteractable.ServerInteract / ServerUse. Игрок сам тоже цель (передача предмета).
    /// </summary>
    public class PlayerHands : NetworkBehaviour, IInteractable
    {
        public static PlayerHands Local;
        public static readonly System.Collections.Generic.List<PlayerHands> All = new System.Collections.Generic.List<PlayerHands>();
        /// <summary>Сервер: ЛКМ (down/up) любого игрока, цель может быть null (удар в воздух). Для кувалды/швабры.</summary>
        public static event Action<PlayerHands, HeldItem, IInteractable, bool> ServerUsed;

        static readonly bool TestMode = NetBootstrap.Arg("-handstest");
        static readonly RaycastHit[] hits = new RaycastHit[24];
        const float EyeHeight = 1.62f;

        public GameConfig config;
        public Transform handView;      // в руке от первого лица (под камерой)
        public Transform handWorld;     // для чужих копий (правая рука модели)
        public GameObject fpArm;        // рука от первого лица (P2), видна владельцу, пока в руке предмет
        public Renderer[] fpSleeve, fpSkin;

        public NetworkVariable<HeldItem> Held = new NetworkVariable<HeldItem>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>V2: до какого момента (ServerTime) действует кофе (+скорость); 0 — не пил.</summary>
        public NetworkVariable<double> CoffeeEnd = new NetworkVariable<double>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // клиент-владелец
        public IInteractable Target { get; private set; }
        /// <summary>Скриншоты (P3): принудительная цель под прицелом (подсветка и подсказка без мыши).</summary>
        public IInteractable ShotAim;
        public string HintText { get; private set; } = "";

        // сервер: состояние ЛКМ этого игрока (для удержания швабры)
        public bool UseDown { get; private set; }
        public IInteractable UseTarget { get; private set; }

        PlayerController pc;
        PlayerCombat combat;
        Humanoid humanoid;
        GameObject visual;
        bool shotTp;                    // скриншоты: предмет в руке модели и у владельца
        Transform handItem;             // предмет в руке от первого лица: вращается взмахом/шваброй вокруг кисти, рука-модель при этом не крутится
        Vector3 viewBase, lastPos;
        float viewSpeed, bobPhase, mopT;
        bool sentDown;
        IInteractable sentTarget;
        int testStage;
        float testTimer;

        /// <summary>Игрок без сознания (C5): ввод и RPC рук заблокированы.</summary>
        public bool Downed => combat != null && combat.IsDown.Value;

        public Vector3 Eye => transform.position + Vector3.up * EyeHeight;

        double NowS => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        /// <summary>V2: действует ли кофе и сколько секунд осталось.</summary>
        public bool CoffeeActive => CoffeeEnd.Value > 0 && NowS < CoffeeEnd.Value;
        public float CoffeeLeft => CoffeeActive ? (float)(CoffeeEnd.Value - NowS) : 0f;
        /// <summary>Множитель скорости ходьбы/бега (кофе: +coffeeBonus).</summary>
        public float SpeedMul => CoffeeActive ? 1f + (config != null ? config.coffeeBonus : 0.1f) : 1f;

        public override void OnNetworkSpawn()
        {
            pc = GetComponent<PlayerController>();
            combat = GetComponent<PlayerCombat>();
            humanoid = GetComponentInChildren<Humanoid>(true);
            if (handView != null)
            {
                viewBase = handView.localPosition;
                var hi = new GameObject("HandItem").transform;
                hi.SetParent(handView, false);
                handItem = hi;
            }
            lastPos = transform.position;
            All.Add(this);
            if (IsOwner) Local = this;
            Held.OnValueChanged += OnHeldChanged;
            RefreshVisual();
        }

        public override void OnNetworkDespawn()
        {
            Held.OnValueChanged -= OnHeldChanged;
            All.Remove(this);
            if (Local == this) { Local = null; Highlight.Set(null, false); }
            // игрок вышел с предметом в руках (ключ!) — оставить его на полу
            if (IsServer && !Held.Value.IsNone && NetworkManager != null && !NetworkManager.ShutdownInProgress)
                WorldItem.Spawn(Held.Value, transform.position + Vector3.up * 1.2f, Vector3.zero);
        }

        void OnHeldChanged(HeldItem prev, HeldItem now)
        {
            RefreshVisual();
            var at = transform.position + Vector3.up * 1.2f;
            if (!now.IsNone && (prev.IsNone || (now.type == prev.type && Items.StackCount(now) > Items.StackCount(prev)))) Sfx.Play3D(Snd.Pickup, at, 0.7f, UnityEngine.Random.Range(0.95f, 1.08f));      // взял / добавил в стопку
            else if (now.IsNone && !prev.IsNone) Sfx.Play3D(Snd.Put, at, 0.7f, UnityEngine.Random.Range(0.95f, 1.05f));   // положил / отдал / бросил
        }

        void RefreshVisual()
        {
            if (visual != null) Destroy(visual);
            visual = null;
            var h = Held.Value;
            bool fp = IsOwner && !shotTp;
            if (humanoid != null) humanoid.carry = !h.IsNone;
            if (fpArm != null) fpArm.SetActive(fp && !h.IsNone);
            if (h.IsNone) return;
            var anchor = fp ? handItem : handWorld;
            if (anchor == null) return;
            visual = Items.CreateVisual(h, anchor, false);
            if (visual == null) return;
            var db = Items.Db;
            var off = db != null ? (fp ? db.handOffset : db.worldHandOffset) : null;
            var eul = db != null ? (fp ? db.handEuler : db.worldHandEuler) : null;
            int ti = (int)h.type;
            if (off != null && eul != null && ti < off.Length && ti < eul.Length)
            {
                visual.transform.localPosition = off[ti];
                visual.transform.localRotation = Quaternion.Euler(eul[ti]);
            }
        }

        /// <summary>Цвет рукава и кожи руки от первого лица (как у модели).</summary>
        public void PaintFpArm(Color sleeve, Color skin)
        {
            Humanoid.Paint(fpSleeve, sleeve * 0.9f);
            Humanoid.Paint(fpSkin, skin * 0.8f);          // рука у самой лампы: чуть темнее, чтобы не выгорала
        }

        /// <summary>Скриншоты (P2): true — предмет показывать в руке модели (вид со стороны), false — viewmodel.</summary>
        public void ShotThirdPerson(bool tp) { shotTp = tp; RefreshVisual(); }

        // viewmodel владельца: покачивание при ходьбе, дыхание, мах кувалдой (угол от PlayerCombat), взмахи шваброй при зажатой ЛКМ
        void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || handView == null) return;
            Highlight.Tick();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var p = transform.position;
            var d = p - lastPos; d.y = 0f;
            lastPos = p;
            float sp = Mathf.Min(d.magnitude / dt, 12f);
            viewSpeed = Mathf.Lerp(viewSpeed, sp, 1f - Mathf.Exp(-10f * dt));
            float k = Mathf.Clamp01(viewSpeed / Mathf.Max(0.1f, config != null ? config.moveSpeed : 5f));
            bobPhase += dt * (3f + viewSpeed * 1.5f);

            var pos = viewBase;
            pos.x += Mathf.Sin(bobPhase) * 0.014f * k;
            pos.y += -Mathf.Abs(Mathf.Sin(bobPhase)) * 0.026f * k + Mathf.Sin(Time.time * 1.4f) * 0.004f;
            float swing = combat != null ? combat.SwingAngle : 0f;
            var rot = Quaternion.Euler(swing, 0f, 0f);

            var ms = Mouse.current;
            bool mopping = ms != null && ms.leftButton.isPressed && Held.Value.type == ItemType.Mop && pc != null && pc.Placed && !pc.MenuOpen && !Downed && Application.isFocused;
            if (mopping)
            {
                mopT += dt * 7f;
                rot *= Quaternion.Euler(Mathf.Sin(mopT * 0.5f) * 6f, Mathf.Sin(mopT) * 20f, Mathf.Sin(mopT) * 7f);
                pos.x += Mathf.Sin(mopT) * 0.07f;
            }
            // взмах: предмет поворачивается в кисти, кисть чуть уходит вперёд-вниз (рука остаётся на месте)
            float sw = Mathf.Clamp01(swing / 75f);
            pos += new Vector3(0f, -0.10f * sw, 0.10f * sw);
            handView.localPosition = Vector3.Lerp(handView.localPosition, pos, 1f - Mathf.Exp(-20f * dt));
            if (handItem != null) handItem.localRotation = rot;
        }

        // ------------------------------------------------------------ сервер: API для целей

        /// <summary>Сервер: положить предмет в руки (если пусто). Деньги (Money) уходят в кассу и руки не занимают.</summary>
        public bool ServerTake(HeldItem item)
        {
            if (!IsServer || item.IsNone) return false;
            if (item.type == ItemType.Money)         // деньги сразу в кассу, руки не занимают
            {
                var gs = GameState.Instance;
                if (gs == null) return false;
                gs.ServerAddMoney(item.arg, Eye + transform.forward * 1.3f + Vector3.up * 0.2f);
                return true;
            }
            if (!Held.Value.IsNone) return false;
            Held.Value = item;
            return true;
        }

        /// <summary>Сервер: забрать предмет из рук (съеден, отдан гостю, занесён в номер).</summary>
        public HeldItem ServerClear()
        {
            var h = Held.Value;
            if (IsServer) Held.Value = default;
            return h;
        }

        /// <summary>Сервер (V2): занесён один расходник — из стопки (тележка) убирается один, иначе руки пустеют.</summary>
        public void ServerConsumeOne()
        {
            if (!IsServer) return;
            var h = Held.Value;
            int n = Items.StackCount(h);
            Held.Value = n > 1 ? Items.Stack(h.type, n - 1) : default;
        }

        /// <summary>Сервер (V2): выпить кофе из рук — +скорость на coffeeSeconds. false — в руках не кофе.</summary>
        public bool ServerDrink()
        {
            if (!IsServer || Held.Value.type != ItemType.Coffee) return false;
            var gs = GameState.Instance;
            Held.Value = default;
            CoffeeEnd.Value = NowS + (gs != null ? gs.CoffeeSeconds : 20f);
            Debug.Log($"[SHOP] owner={OwnerClientId} drinks coffee: speed x{SpeedMul:0.00} for {(gs != null ? gs.CoffeeSeconds : 20f):0}s");
            return true;
        }

        public void ServerResetBuff() { if (IsServer && CoffeeEnd.Value != 0) CoffeeEnd.Value = 0; }

        // ------------------------------------------------------------ игрок как цель (передать предмет)

        public string Hint(HeldItem held)
        {
            if (held.IsNone || !Held.Value.IsNone || pc == null) return "";
            return Interact.E("передать (" + pc.Nick.Value.ToString() + ")");
        }

        public void ServerInteract(PlayerHands who, HeldItem held)
        {
            if (who == this || held.IsNone || !Held.Value.IsNone) return;
            Held.Value = held;
            who.Held.Value = default;
        }

        // ------------------------------------------------------------ клиент-владелец: ввод

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer && UseDown && Held.Value.IsNone) UseDown = false;
            if (!IsOwner || pc == null) return;

            if (ShotAim != null)         // скриншоты (P3): цель зафиксирована командой, фокус окна не нужен
            {
                Target = ShotAim;
                HintText = Target.Hint(Held.Value) ?? "";
                Highlight.Set(Target as Component, HintText != "" && HintText != Interact.Busy);
                return;
            }
            if (TestMode && pc.Placed) SelfTestStep();       // в batchmode нет фокуса/ввода
            var kb = Keyboard.current;
            var ms = Mouse.current;
            if (!pc.Placed || pc.MenuOpen || Downed || !Application.isFocused || kb == null || ms == null || pc.cam == null)
            {
                Target = null; HintText = "";
                Highlight.Set(null, false);
                SendUse(false, null);
                return;
            }

            Target = Aim();
            HintText = Target != null ? (Target.Hint(Held.Value) ?? "") : "";
            Highlight.Set(Target as Component, HintText != "" && HintText != Interact.Busy);      // подсветка у владельца (P3)

            if (kb.eKey.wasPressedThisFrame && Target != null)
            {
                if (Target is ShopTerminal) ShopUi.Open();         // V2: меню магазина — локально, покупка уходит RPC
                else
                {
                    Interact.Describe(Target, out var r, out var id);
                    InteractRpc(r, id);
                }
            }
            if (kb.gKey.wasPressedThisFrame && !Held.Value.IsNone)
            {
                var t = pc.cam.transform;
                DropRpc(t.position, t.forward);
            }
            bool down = ms.leftButton.isPressed && !Held.Value.IsNone;
            SendUse(down, down ? Target : null);

        }

        IInteractable Aim()
        {
            var t = pc.cam.transform;
            return AimFrom(t.position, t.forward, config.interactDistance, this);
        }

        /// <summary>
        /// Луч прицела: ближайший коллайдер с целью. Столешница ресепшена луч не гасит (P4): гостя в очереди
        /// можно обслужить и из-за стойки, и спереди; свой корпус тоже пропускается.
        /// </summary>
        public static IInteractable AimFrom(Vector3 origin, Vector3 dir, float distance, PlayerHands self)
        {
            int n = Physics.RaycastNonAlloc(origin, dir, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            var desk = MotelMap.Instance != null ? MotelMap.Instance.receptionDesk : null;
            float best = float.MaxValue;
            Collider hit = null;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.distance >= best) continue;
                if (self != null && h.collider.GetComponentInParent<PlayerHands>() == self) continue;   // свой корпус
                if (desk != null && h.collider.transform == desk) continue;              // столешница ресепшена
                best = h.distance; hit = h.collider;
            }
            return hit != null ? hit.GetComponentInParent<IInteractable>() : null;
        }

        void SendUse(bool down, IInteractable target)
        {
            if (down == sentDown && (!down || target == sentTarget)) return;
            sentDown = down; sentTarget = target;
            NetworkObjectReference r = default; int id = 0;
            if (target != null) Interact.Describe(target, out r, out id);
            UseRpc(down, r, id);
        }

        // ------------------------------------------------------------ RPC (выполняются на сервере)

        bool InReach(IInteractable t)
        {
            if (t == null) return false;
            if (TestMode) return true;
            return Interact.Distance(t, Eye) <= config.interactDistance + config.interactReachSlack;
        }

        /// <summary>Сервер: звонок на стойке принят — «дзынь» и нажатие кнопки у всех (V5, Bell).</summary>
        [Rpc(SendTo.Everyone)]
        public void BellRpc() { if (Bell.Instance != null) Bell.Instance.PlayLocal(); }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void InteractRpc(NetworkObjectReference netRef, int sceneId)
        {
            if (Downed) return;
            var t = Interact.Resolve(netRef, sceneId);
            if (!InReach(t)) return;
            t.ServerInteract(this, Held.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void UseRpc(bool down, NetworkObjectReference netRef, int sceneId)
        {
            var t = down ? Interact.Resolve(netRef, sceneId) : null;
            if (t != null && !InReach(t)) t = null;
            ServerApplyUse(down, t);
        }

        /// <summary>Сервер: ЛКМ игрока (down — нажал/держит) по цели t. Вызывается из UseRpc и самотестов.</summary>
        public void ServerApplyUse(bool down, IInteractable t)
        {
            var held = Held.Value;
            if (held.IsNone || Downed) { UseDown = false; UseTarget = null; return; }
            if (held.type == ItemType.Coffee)            // V2: кофе — ЛКМ выпить (цель не нужна)
            {
                if (down) ServerDrink();
                UseDown = false; UseTarget = null;
                return;
            }
            UseDown = down;
            UseTarget = t;
            t?.ServerUse(this, held, down);
            ServerUsed?.Invoke(this, held, t, down);
        }

        /// <summary>Клиент-владелец (V2): купить товар (меню магазина). Сервер проверяет дистанцию до терминала и деньги.</summary>
        public void RequestBuy(ShopItem item) { if (IsOwner) BuyRpc((byte)item); }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BuyRpc(byte item)
        {
            if (Downed) return;
            var gs = GameState.Instance;
            var map = MotelMap.Instance;
            if (gs == null || map == null || map.shopTerminal == null) return;
            var term = map.shopTerminal.GetComponent<ShopTerminal>();
            if (term == null || !InReach(term)) return;
            gs.ServerBuy((ShopItem)item, this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void DropRpc(Vector3 origin, Vector3 dir)
        {
            if (Downed) return;
            var h = Held.Value;
            if (h.IsNone) return;
            dir = dir.sqrMagnitude < 0.01f ? transform.forward : dir.normalized;
            var eye = Eye;
            if ((origin - eye).sqrMagnitude > 9f) origin = eye;
            var pos = origin + dir * 0.7f;
            if (Physics.Raycast(origin, dir, out var hit, 0.7f, Items.WorldMask, QueryTriggerInteraction.Ignore))
                pos = hit.point - dir * 0.2f;      // перед стеной — не за ней
            Held.Value = default;
            WorldItem.Spawn(h, pos, dir * config.throwForce + Vector3.up * config.throwUp);
        }

        // ------------------------------------------------------------ самотест (-handstest): ключ №(3+id) с доски, бросить, поднять, повесить

        void SelfTestStep()
        {
            testTimer += Time.deltaTime;
            if (testTimer < 1.5f) return;
            testTimer = 0f;
            var map = MotelMap.Instance;
            if (map == null || map.keyBoard == null || testStage > 4) return;
            int room = 3 + (int)OwnerClientId;
            var slot = map.keyBoard.slots[room - 1];
            var key = new HeldItem(ItemType.Key, room);
            switch (testStage++)
            {
                case 0: InteractRpc(default, slot.sceneId); break;
                case 1:
                    Check(Held.Value == key, "take key from board");
                    var t = pc.cam.transform;
                    DropRpc(t.position, t.forward);
                    break;
                case 2:
                    var wi = WorldItem.Nearest(transform.position);
                    Check(Held.Value.IsNone && wi != null, "drop key");
                    if (wi != null) { Interact.Describe(wi, out var r, out var id); InteractRpc(r, id); }
                    break;
                case 3:
                    Check(Held.Value == key, "pick key from floor");
                    InteractRpc(default, slot.sceneId);
                    break;
                case 4:
                    Check(Held.Value.IsNone && map.keyBoard.Has(room), "hang key back");
                    Debug.Log("[HANDS] selftest done owner=" + OwnerClientId);
                    break;
            }
        }

        void Check(bool ok, string what)
        {
            Debug.Log("[HANDS] selftest " + (ok ? "OK " : "FAIL ") + what + " owner=" + OwnerClientId);
        }
    }
}
