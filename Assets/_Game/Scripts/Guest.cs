using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Friendslop
{
    public enum GuestState : byte
    {
        Arriving,       // идёт от входа к очереди
        WaitForm,       // у стойки, ждёт бланк
        Signing,        // подписывает (signDuration)
        WaitKey,        // подписал, ждёт ключ готового номера
        ToRoom,         // идёт в номер
        Living,         // живёт в номере
        ToDesk,         // идёт выписываться
        WaitCheckOut,   // у стойки, ждёт выписки
        Leaving,        // идёт к выходу (деспавн на выходе)
        Down,           // (C5) лежит после ударов (npcDownTime), потом уходит
        Brawl           // (C5) дерётся с другим гостем в коридоре, пока игроки не уложат обоих
    }

    /// <summary>
    /// Гость (C4). Сетевой префаб: NetworkTransform (авторитет сервера), NavMeshAgent включён только на сервере.
    /// Жизненный цикл: вход → очередь → бланк → подпись → ключ → номер → проживание → выписка → выход.
    /// Состояние и дедлайн (ServerTime) — NetworkVariable: клиенты рисуют статус над головой и подсказки без трафика.
    /// Всё решает сервер; игрок взаимодействует через IInteractable (E с предметом в руках).
    /// Бой (C5): ServerHit(attacker) — Hp--, агрессия (догоняет и бьёт ударившего), 0 Hp → Down (роняет деньги/ключ), потом уходит;
    /// Brawl — драка двух гостей (BrawlDirector). Визуал (лежит/подпрыгивает/выпад/драка) — на всех машинах по State и FxRpc.
    /// </summary>
    public class Guest : NetworkBehaviour, IInteractable
    {
        public static readonly List<Guest> All = new List<Guest>();

        /// <summary>Самотест -guesttest: все интервалы короче в 10 раз, гости бегают втрое быстрее.</summary>
        public static readonly bool Fast = NetBootstrap.Arg("-guesttest") || NetBootstrap.Arg("-fighttest") || NetBootstrap.Arg("-hammertest");
        public static float TimeScale => Fast ? 10f : 1f;

        /// <summary>Счётчики для лога [STATS] (сервер): пришло / заселено / выехало и заплатило / ушло из мотеля.</summary>
        public static int StatSpawned, StatSettled, StatPaid, StatLeft;
        const float WalkBoost = 3f;
        static int ticketCounter;

        public GameConfig config;
        public NavMeshAgent agent;
        public TextMesh status;

        public NetworkVariable<GuestState> State = new NetworkVariable<GuestState>(GuestState.Arriving);
        /// <summary>Дедлайн текущего ожидания (ServerTime). Для WaitForm/WaitKey/WaitCheckOut — «ждёт N с».</summary>
        public NetworkVariable<double> Until = new NetworkVariable<double>(0);
        /// <summary>true — отказался платить; над головой «!».</summary>
        public NetworkVariable<bool> Marked = new NetworkVariable<bool>(false);
        /// <summary>Номер, который занимает гость (0 — нет).</summary>
        public NetworkVariable<int> RoomNo = new NetworkVariable<int>(0);
        /// <summary>ХП (C5: 1 удар кувалдой = 1 ХП).</summary>
        public NetworkVariable<int> Hp = new NetworkVariable<int>(3);
        /// <summary>Сид внешнего вида (P2): рост, телосложение, цвета, шляпа/очки/чемодан. Один сид — одинаковый гость у всех клиентов.</summary>
        public NetworkVariable<int> Look = new NetworkVariable<int>(0);

        // сервер
        PlayerCombat aggroTarget;   // кого бьёт (ударил гостя)
        double aggroUntil;
        float nextAttack, nextChase, baseSpeed;
        Guest brawlPartner;
        bool brawlArrived;
        bool nonPayer;
        int keyRoom;            // номер ключа, который гость держит (0 — нет)
        int carriedMoney;       // сколько денег несёт (неплательщик уносит их с собой)
        int ticket;
        int lastQueueIdx = -1;
        double refuseAt;
        float destSetTime;
        float leavingSince;
        Vector3 curDest;
        double stayEnd;

        /// <summary>Сервер, для самотестов: продлить проживание (игровые секунды; в быстром режиме делится на TimeScale).</summary>
        public void ServerExtendStay(float seconds) { stayEnd += seconds / TimeScale; }

        string lastText = "";
        float statusBaseSize;

        // визуал (все машины)
        const float FlashTime = 0.3f, PunchTime = 0.25f;
        Transform vis;
        Humanoid humanoid;
        StunStars stars;
        float lie, flashT, punchT, phase;
        int pendingLook;
        /// <summary>Скриншоты (P2): гость замёрз на месте (не ходит, не уходит).</summary>
        public bool ShotFrozen { get; private set; }

        public bool IsLiving => State.Value == GuestState.Living;
        /// <summary>Сервер: можно втянуть в драку (живёт в номере, не отвлечён на агрессию, не побит).</summary>
        public bool CanBrawl => IsServer && IsSpawned && State.Value == GuestState.Living && aggroTarget == null && Hp.Value > 0;
        public int CarriedMoney => carriedMoney;
        public bool IsNonPayer => nonPayer;
        static GameState Gs => GameState.Instance;

        // ------------------------------------------------------------ задачи для HUD

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterTasks() { TaskBoard.Register(CollectTasks); }

        static void CollectTasks(List<TaskLine> into)
        {
            var gs = Gs;
            if (gs == null) return;
            double now = gs.Now;
            int inN = 0, outN = 0; bool inUrg = false, outUrg = false;
            foreach (var g in All)
            {
                if (g == null || !g.IsSpawned) continue;
                var s = g.State.Value;
                bool soon = g.Until.Value - now < 10.0 / TimeScale;
                if (s == GuestState.WaitForm || s == GuestState.WaitKey) { inN++; inUrg |= soon; }
                else if (s == GuestState.WaitCheckOut && !g.Marked.Value) { outN++; outUrg |= soon; }
            }
            if (inN > 0) into.Add(new TaskLine("Гость у стойки (заселение)" + (inN > 1 ? " ×" + inN : ""), inUrg, new[] { ItemType.Form, ItemType.Key }));
            if (outN > 0) into.Add(new TaskLine("Гость у стойки (выезд)" + (outN > 1 ? " ×" + outN : ""), outUrg, new[] { ItemType.Key }));
            if (BrawlCount() > 0) into.Add(new TaskLine("Драка в коридоре!", true));
            foreach (var w in WorldItem.All)
            {
                if (w == null || !w.IsSpawned) continue;
                var it = w.Item.Value;
                if (it.type == ItemType.Key) into.Add(new TaskLine("Ключ " + it.arg + " на полу", false, new[] { ItemType.Key }));
            }
        }

        // ------------------------------------------------------------ жизненный цикл объекта

        /// <summary>Сервер: до Spawn(). Определяет, неплательщик ли гость.</summary>
        public void ServerInit(bool isNonPayer)
        {
            nonPayer = isNonPayer;
            ticket = ++ticketCounter;
            pendingLook = Random.Range(1, int.MaxValue);          // вид не зависит от nonPayer: неплательщик внешне неотличим
        }

        void Awake()
        {
            if (status != null) statusBaseSize = status.characterSize;
            // тело и глаза — под общий узел Visual: его одним поворотом укладываем на пол, подбрасываем при ударе
            var v = new GameObject("Visual").transform;
            v.SetParent(transform, false);
            var kids = new List<Transform>();
            foreach (Transform c in transform) if (c != v && (status == null || c != status.transform)) kids.Add(c);
            foreach (var c in kids) c.SetParent(v, false);
            vis = v;
            humanoid = v.GetComponentInChildren<Humanoid>(true);
            phase = Random.value * 6.28f;
        }

        void OnLookChanged(int prev, int now)
        {
            if (humanoid != null) humanoid.ApplyLook(now);
            Debug.Log($"[GUEST] look changed to {now} (server={IsServer})");
        }

        /// <summary>Звуки по смене состояния (на всех машинах): звонок на ресепшене, падение, тревога драки.</summary>
        void OnStateChanged(GuestState prev, GuestState now)
        {
            var map = MotelMap.Instance;
            if ((now == GuestState.WaitForm || now == GuestState.WaitCheckOut) && map != null && map.receptionDesk != null)
                Sfx.Play3D(Snd.Bell, map.receptionDesk.position + Vector3.up * 1.1f, 0.9f);
            else if (now == GuestState.Down) Sfx.Play3D(Snd.Thud, transform.position + Vector3.up * 0.4f, 1f);
            else if (now == GuestState.Brawl) Sfx.Play3D(Snd.Alarm, transform.position + Vector3.up * 2f, 0.8f);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Look.OnValueChanged += OnLookChanged;
            State.OnValueChanged += OnStateChanged;
            if (IsServer && Look.Value == 0) Look.Value = pendingLook != 0 ? pendingLook : Random.Range(1, int.MaxValue);
            if (humanoid != null) humanoid.ApplyLook(Look.Value);
            Debug.Log($"[GUEST] look seed={Look.Value} server={IsServer}");
            if (IsServer)
            {
                Hp.Value = config != null ? config.npcHp : 3;
                GameState.ServerRestarted += ServerDespawn;
                if (agent != null)
                {
                    baseSpeed = (config != null ? config.guestSpeed : 4f) * (Fast ? WalkBoost : 1f);
                agent.speed = baseSpeed;
                    agent.enabled = true;
                    if (agent.isOnNavMesh) agent.Warp(transform.position);
                    else Debug.LogError("[GUEST] spawned off NavMesh at " + transform.position);
                }
                Debug.Log($"[GUEST] spawned #{ticket} {(nonPayer ? "non-payer" : "payer")}");
            }
            else if (agent != null) agent.enabled = false;
        }

        public override void OnNetworkDespawn()
        {
            Look.OnValueChanged -= OnLookChanged;
            State.OnValueChanged -= OnStateChanged;
            All.Remove(this);
            if (IsServer) GameState.ServerRestarted -= ServerDespawn;
        }

        /// <summary>Сервер: убрать гостя без последствий (перезапуск смены, тест).</summary>
        public void ServerDespawn()
        {
            if (IsServer && IsSpawned) NetworkObject.Despawn(true);
        }

        /// <summary>Сервер: убрать всех гостей.</summary>
        public static void ServerDespawnAll()
        {
            foreach (var g in new List<Guest>(All)) if (g != null) g.ServerDespawn();
        }

        // ------------------------------------------------------------ очередь

        static bool InQueue(GuestState s)
        {
            return s == GuestState.Arriving || s == GuestState.WaitForm || s == GuestState.Signing
                || s == GuestState.WaitKey || s == GuestState.ToDesk || s == GuestState.WaitCheckOut;
        }

        /// <summary>Сколько гостей сейчас занимают места у стойки (спавнер не спавнит выше числа точек).</summary>
        public static int QueueCount()
        {
            int n = 0;
            foreach (var g in All) if (g != null && g.IsSpawned && InQueue(g.State.Value)) n++;
            return n;
        }

        int QueueIndex()
        {
            int idx = 0;
            foreach (var g in All)
                if (g != this && g != null && g.IsSpawned && InQueue(g.State.Value) && g.ticket < ticket) idx++;
            return idx;
        }

        static Vector3 QueueSpot(int idx)
        {
            var pts = MotelMap.Instance.queuePoints;
            int n = pts.Length;
            var p = pts[Mathf.Min(idx, n - 1)].position;
            if (idx >= n) p += Vector3.right * 0.9f * (idx - n + 1);
            return p;
        }

        // ------------------------------------------------------------ сервер: движение

        void SetDest(Vector3 p)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            if (NavMesh.SamplePosition(p, out var hit, 2.5f, NavMesh.AllAreas)) p = hit.position;
            curDest = p;
            destSetTime = Time.time;
            agent.isStopped = false;
            agent.SetDestination(p);
        }

        bool Reached()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return true;
            float since = Time.time - destSetTime;
            if (since < 0.2f) return false;
            if (since > 45f / (Fast ? WalkBoost : 1f))        // застрял — телепорт к цели (прототип: лучше так, чем завис)
            {
                Debug.LogWarning($"[GUEST] #{ticket} stuck in {State.Value}, warping to {curDest}");
                agent.Warp(curDest);
                return true;
            }
            return !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f;
        }

        void Enter(GuestState s, double until = 0)
        {
            State.Value = s;
            Until.Value = until;
            lastQueueIdx = -1;
        }

        double Wait(float seconds) => Gs.Now + seconds / TimeScale;

        // ------------------------------------------------------------ сервер: тик

        void Update()
        {
            if (!IsSpawned || !IsServer) return;
            ServerTick();
        }

        void ServerTick()
        {
            var gs = Gs;
            var map = MotelMap.Instance;
            if (gs == null || map == null || config == null || ShotFrozen) return;
            if (gs.Phase.Value != GamePhase.Playing)
            {
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;      // Game Over / лобби — заморозка
                return;
            }
            if (agent.enabled && agent.isOnNavMesh && agent.isStopped && agent.hasPath) agent.isStopped = false;

            if (aggroTarget != null && TickAggro(gs.Now)) return;      // ударенный гость гонится за обидчиком, остальное ждёт

            double now = gs.Now;
            var s = State.Value;

            if (InQueue(s))
            {
                int idx = QueueIndex();
                if (idx != lastQueueIdx) { lastQueueIdx = idx; SetDest(QueueSpot(idx)); }
            }

            switch (s)
            {
                case GuestState.Arriving:
                    if (Reached()) Enter(GuestState.WaitForm, Wait(config.checkInWait));
                    break;

                case GuestState.WaitForm:
                case GuestState.WaitKey:
                    if (now >= Until.Value)
                    {
                        Debug.Log($"[GUEST] #{ticket} gave up waiting at check-in ({s})");
                        StartLeaving();
                    }
                    break;

                case GuestState.Signing:
                    if (now >= Until.Value) Enter(GuestState.WaitKey, Wait(config.checkInWait));
                    break;

                case GuestState.ToRoom:
                    if (Reached())
                    {
                        Enter(GuestState.Living);
                        stayEnd = now + StayTime() / TimeScale;
                        StatSettled++;
                        Debug.Log($"[GUEST] #{ticket} settled in room {RoomNo.Value}");
                    }
                    break;

                case GuestState.Living:
                    if (now >= stayEnd) CheckOutFromRoom(now);
                    break;

                case GuestState.ToDesk:
                    if (Reached())
                    {
                        Enter(GuestState.WaitCheckOut, Wait(config.checkOutWait));
                        refuseAt = now + config.refuseDelay / TimeScale;
                    }
                    break;

                case GuestState.WaitCheckOut:
                    if (nonPayer && now >= refuseAt) Refuse();
                    else if (now >= Until.Value)
                    {
                        Debug.Log($"[GUEST] #{ticket} nobody served check-out: dropping key {keyRoom} and {carriedMoney} $");
                        DropKey(); DropMoney();
                        StartLeaving();
                    }
                    break;

                case GuestState.Down:
                    if (now >= Until.Value)
                    {
                        Debug.Log($"[FIGHT] guest #{ticket} gets up and leaves");
                        StartLeaving();
                    }
                    break;

                case GuestState.Brawl:
                    TickBrawl();
                    break;

                case GuestState.Leaving:
                    if ((transform.position - map.guestExit.position).sqrMagnitude < 0.8f * 0.8f
                        || Reached() || Time.time - leavingSince > 60f / (Fast ? WalkBoost : 1f))
                    {
                        StatLeft++;
                        Debug.Log($"[GUEST] #{ticket} left the motel");
                        ServerDespawn();
                    }
                    break;
            }

            FaceDeskWhenIdle(s);
        }

        float StayTime()
        {
            bool big = Gs.PlayerCount.Value > config.smallTeamMax;
            return big ? Random.Range(config.stayMinBig, config.stayMaxBig) : Random.Range(config.stayMinSmall, config.stayMaxSmall);
        }

        void FaceDeskWhenIdle(GuestState s)
        {
            if (s != GuestState.WaitForm && s != GuestState.Signing && s != GuestState.WaitKey && s != GuestState.WaitCheckOut) return;
            if (agent.velocity.sqrMagnitude > 0.05f) return;
            var desk = MotelMap.Instance.receptionDesk;
            if (desk == null) return;
            var d = desk.position - transform.position; d.y = 0;
            if (d.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * Time.deltaTime);
        }

        // ------------------------------------------------------------ сервер: события

        /// <summary>Гость отжил своё: номер → «нужно обслужить», идёт выписываться с ключом и деньгами.</summary>
        void CheckOutFromRoom(double now)
        {
            var room = Room.Get(RoomNo.Value);
            if (room != null) room.ServerSetNeedsService();
            carriedMoney = config.guestPays;
            ticket = ++ticketCounter;            // в конец очереди
            Debug.Log($"[GUEST] #{ticket} leaves room {RoomNo.Value}, heads to check-out with {carriedMoney} $");
            Enter(GuestState.ToDesk);
        }

        /// <summary>Неплательщик у стойки: бросает ключ, платить не хочет, уходит с деньгами. Над головой «!».</summary>
        void Refuse()
        {
            Marked.Value = true;
            Debug.Log($"[GUEST] #{ticket} refuses to pay (key {keyRoom} dropped, keeps {carriedMoney} $)");
            DropKey();
            StartLeaving();
        }

        void StartLeaving()
        {
            Enter(GuestState.Leaving);
            leavingSince = Time.time;
            var map = MotelMap.Instance;
            SetDest(map.guestExit.position);
        }

        Vector3 DropPos => transform.position + Vector3.up * 1.0f + transform.forward * 0.5f;

        void DropKey()
        {
            if (keyRoom <= 0) return;
            WorldItem.Spawn(new HeldItem(ItemType.Key, keyRoom), DropPos, RandomToss());
            keyRoom = 0;
        }

        /// <summary>Сервер: гость роняет деньги, которые несёт (выписка не обслужена, или C5: гостя положили).</summary>
        public void ServerDropMoney()
        {
            if (!IsServer) return;
            DropMoney();
        }

        void DropMoney()
        {
            if (carriedMoney <= 0) return;
            WorldItem.Spawn(new HeldItem(ItemType.Money, carriedMoney), DropPos, RandomToss());
            carriedMoney = 0;
        }

        static Vector3 RandomToss() => new Vector3(Random.Range(-1f, 1f), 1.5f, Random.Range(-1f, 1f));

        // ------------------------------------------------------------ бой (C5)

        public static int BrawlCount()
        {
            int n = 0;
            foreach (var g in All) if (g != null && g.IsSpawned && g.State.Value == GuestState.Brawl) n++;
            return n;
        }

        /// <summary>Сервер: удар кувалдой. Hp--; пока жив — гонится за ударившим; 0 Hp — Down (роняет деньги и ключ).</summary>
        public virtual void ServerHit(PlayerHands attacker)
        {
            if (!IsServer || !IsSpawned) return;
            var s = State.Value;
            if (s == GuestState.Down || Hp.Value <= 0) return;           // лежачего и уже побитого не добиваем
            Hp.Value--;
            FxRpc(0);
            Debug.Log($"[FIGHT] guest #{ticket} hit by {(attacker != null ? attacker.name : "?")}, hp={Hp.Value} ({s})");
            if (Hp.Value <= 0) { GoDown(); return; }
            var who = attacker != null ? attacker.GetComponent<PlayerCombat>() : null;
            if (s != GuestState.Brawl && who != null && !who.IsDown.Value) StartAggro(who);      // дерущиеся друг с другом на игрока не отвлекаются
        }

        /// <summary>C5: гость сбегает (игрок потерял сознание / гость побеждён): ключ роняет, номер освобождает, идёт к выходу.</summary>
        public void ServerFlee(bool dropMoney)
        {
            if (!IsServer) return;
            var s = State.Value;
            if (s == GuestState.Leaving || s == GuestState.Down) return;
            EndAggro();
            ReleaseRoomAndKey(s);
            if (dropMoney) DropMoney();
            StartLeaving();
        }

        /// <summary>Гость уходит насовсем: ключ на пол, а если он занимал номер — номер требует уборки.</summary>
        void ReleaseRoomAndKey(GuestState from)
        {
            if (from == GuestState.ToRoom || from == GuestState.Living || from == GuestState.Brawl)
            {
                var room = Room.Get(RoomNo.Value);
                if (room != null) room.ServerSetNeedsService();
            }
            DropKey();
        }

        void GoDown()
        {
            var prev = State.Value;
            EndAggro();
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.ResetPath();
            ReleaseRoomAndKey(prev);
            // Деньги роняет только тот, кто уже выехал из номера и несёт оплату (идёт/ждёт на выезде, неплательщик). До выезда — ничего.
            DropMoney();
            Enter(GuestState.Down, Gs.Now + config.npcDownTime / TimeScale);
            Debug.Log($"[FIGHT] guest #{ticket} is down (was {prev})");
        }

        // --- агрессия: догнать ударившего и бить раз в npcAttackInterval

        void StartAggro(PlayerCombat target)
        {
            if (agent == null || !agent.enabled) return;
            bool fresh = aggroTarget == null;
            aggroTarget = target;
            aggroUntil = Gs.Now + config.npcAggroTimeout / TimeScale;
            if (fresh)
            {
                nextAttack = Time.time + config.npcAttackInterval * 0.5f / TimeScale;
                nextChase = 0f;
                agent.speed = baseSpeed * config.npcChaseSpeedMul;
                Debug.Log($"[FIGHT] guest #{ticket} is angry at {target.name}");
            }
        }

        void EndAggro()
        {
            if (aggroTarget == null) return;
            aggroTarget = null;
            if (agent != null) agent.speed = baseSpeed;
        }

        /// <summary>true — гость занят погоней/дракой с игроком в этот тик.</summary>
        bool TickAggro(double now)
        {
            var t = aggroTarget;
            if (t == null || !t.IsSpawned || t.IsDown.Value || now >= aggroUntil)
            {
                EndAggro();
                ResumeAfterAggro();
                return false;
            }
            var to = t.transform.position - transform.position; to.y = 0f;
            if (to.magnitude > config.npcAttackRange)
            {
                if (Time.time >= nextChase) { nextChase = Time.time + 0.2f; SetDest(t.transform.position); }
                return true;
            }
            if (agent.enabled && agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 720f * Time.deltaTime);
            if (Time.time >= nextAttack)
            {
                nextAttack = Time.time + config.npcAttackInterval / TimeScale;
                FxRpc(1);
                if (t.ServerGuestHit())        // игрок потерял сознание — гость сбегает, не заплатив
                {
                    EndAggro();
                    if (State.Value == GuestState.Leaving) ResumeAfterAggro();
                    else ServerFlee(false);
                    Debug.Log($"[FIGHT] guest #{ticket} knocked {t.name} out and flees");
                    return true;
                }
            }
            return true;
        }

        /// <summary>После агрессии вернуть гостя к тому, что он делал: идти в номер / к выходу / в очередь.</summary>
        void ResumeAfterAggro()
        {
            switch (State.Value)
            {
                case GuestState.Leaving:
                    SetDest(MotelMap.Instance.guestExit.position);
                    break;
                case GuestState.ToRoom:
                case GuestState.Living:
                {
                    var room = Room.Get(RoomNo.Value);
                    if (room != null) SetDest(Vector3.Lerp(room.inside.position, room.bed.position, 0.35f));
                    break;
                }
                default:
                    lastQueueIdx = -1;          // очередь: ServerTick назначит место заново
                    break;
            }
        }

        // --- драка двух гостей (запускает BrawlDirector)

        /// <summary>Сервер: гость выходит из номера и идёт драться в коридор. Драка идёт, пока гость не будет уложен (Down).</summary>
        public void ServerStartBrawl(Vector3 spot, Guest partner)
        {
            if (!IsServer || !IsSpawned) return;
            EndAggro();
            brawlPartner = partner;
            brawlArrived = false;
            Enter(GuestState.Brawl);
            SetDest(spot);
            Debug.Log($"[FIGHT] guest #{ticket} goes to brawl at {spot}");
        }

        void TickBrawl()
        {
            if (!brawlArrived) { if (Reached()) brawlArrived = true; return; }
            if (brawlPartner == null || !brawlPartner.IsSpawned) return;
            var d = brawlPartner.transform.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * Time.deltaTime);
        }

        // --- визуальные эффекты (RPC всем): 0 — получил удар, 1 — сам ударил

        [Rpc(SendTo.Everyone)]
        void FxRpc(byte kind)
        {
            if (kind == 0) flashT = FlashTime; else punchT = PunchTime;
        }

        void UpdateVisual()
        {
            if (vis == null) return;
            float dt = Time.deltaTime;
            var s = State.Value;
            lie = Mathf.MoveTowards(lie, s == GuestState.Down ? 1f : 0f, dt * 6f);
            flashT = Mathf.Max(0f, flashT - dt);
            punchT = Mathf.Max(0f, punchT - dt);
            float hop = flashT / FlashTime * 0.18f;
            float lunge = Mathf.Sin(punchT / PunchTime * Mathf.PI) * 0.35f;
            float wob = 0f, lean = 0f;
            if (s == GuestState.Brawl)
            {
                wob = Mathf.Abs(Mathf.Sin(Time.time * 12f + phase)) * 0.12f;
                lean = Mathf.Sin(Time.time * 9f + phase) * 10f;
            }
            vis.localPosition = new Vector3(0f, 0.4f * lie + hop + wob, lunge);
            vis.localRotation = Quaternion.Euler(-90f * lie, 0f, lean);
            if (humanoid != null)
            {
                humanoid.lie = lie;
                humanoid.transform.localPosition = new Vector3(0f, -0.26f * lie, 0f);   // человечек тоньше капсулы: ложится на пол
                humanoid.armsUp = s == GuestState.Brawl;
                humanoid.punch = Mathf.Sin(punchT / PunchTime * Mathf.PI);
                humanoid.SetFlash(flashT / FlashTime);
            }
            // «звёздочки» над оглушённым (State — NetworkVariable, видно всем)
            if (stars == null && s == GuestState.Down) stars = StunStars.Attach(transform);
            if (stars != null) stars.Tick(s == GuestState.Down, lie, dt);
        }

        /// <summary>Скриншоты (P2): сервер ставит замороженного гостя с видом по сиду. flag: "" | "down" | "brawl".</summary>
        public void ShotStage(int lookSeed, Vector3 pos, float yawDeg, string flag)
        {
            if (!IsServer || !IsSpawned) return;
            ShotFrozen = true;
            Look.Value = lookSeed;
            if (humanoid != null) humanoid.ApplyLook(lookSeed);
            if (agent != null && agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.Warp(pos); }
            else transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            if (flag == "down") Enter(GuestState.Down, Gs.Now + 100000);
            else if (flag == "brawl") Enter(GuestState.Brawl);
        }

        // ------------------------------------------------------------ взаимодействие (IInteractable)

        int LeftSeconds()
        {
            var gs = Gs;
            if (gs == null) return 0;
            return Mathf.Max(0, Mathf.CeilToInt((float)(Until.Value - gs.Now)));
        }

        public string Hint(HeldItem held)
        {
            switch (State.Value)
            {
                case GuestState.WaitForm:
                    if (held.type == ItemType.Form) return Interact.E("дать бланк");
                    return held.IsNone ? "Гость ждёт бланк (" + LeftSeconds() + " с)" : Interact.Busy;
                case GuestState.Signing:
                    return "Гость подписывает бланк…";
                case GuestState.WaitKey:
                    if (held.type == ItemType.Key)
                    {
                        var r = Room.Get(held.arg);
                        return r != null && r.IsReady ? Interact.E("выдать ключ №" + held.arg) : "Номер " + held.arg + " не готов";
                    }
                    return held.IsNone ? "Гостю нужен ключ готового номера (" + LeftSeconds() + " с)" : Interact.Busy;
                case GuestState.WaitCheckOut:
                    return held.IsNone ? Interact.E("выписать гостя") : Interact.Busy;
                default:
                    return "";
            }
        }

        public void ServerInteract(PlayerHands who, HeldItem held)
        {
            if (!IsServer || !IsSpawned || who == null) return;
            switch (State.Value)
            {
                case GuestState.WaitForm:
                    if (held.type != ItemType.Form) return;
                    who.ServerClear();
                    Enter(GuestState.Signing, Wait(config.signDuration));
                    Debug.Log($"[GUEST] #{ticket} signing form");
                    break;

                case GuestState.WaitKey:
                {
                    if (held.type != ItemType.Key) return;
                    var room = Room.Get(held.arg);
                    if (room == null || !room.IsReady) return;          // номер занят/грязный/закрыт
                    who.ServerClear();
                    keyRoom = room.index;
                    RoomNo.Value = room.index;
                    room.ServerSetOccupied();
                    Enter(GuestState.ToRoom);
                    SetDest(Vector3.Lerp(room.inside.position, room.bed.position, 0.35f));
                    Debug.Log($"[GUEST] #{ticket} got key {keyRoom}, heads to room");
                    break;
                }

                case GuestState.WaitCheckOut:
                    if (!held.IsNone) return;                           // нужны свободные руки под ключ
                    if (nonPayer) { Refuse(); return; }
                    if (keyRoom > 0 && !who.ServerTake(new HeldItem(ItemType.Key, keyRoom))) return;
                    int pay = carriedMoney;
                    keyRoom = 0; carriedMoney = 0;
                    if (pay > 0) who.ServerTake(new HeldItem(ItemType.Money, pay));     // деньги сразу в кассу
                    StatPaid++;
                    Debug.Log($"[GUEST] #{ticket} checked out, paid {pay} $");
                    StartLeaving();
                    break;
            }
        }

        // ------------------------------------------------------------ все машины: статус над головой

        void LateUpdate()
        {
            if (!IsSpawned) return;
            UpdateVisual();
            if (status == null) return;
            string txt = "";
            Color col = Color.white;
            bool marked = Marked.Value;
            var s = State.Value;
            if (marked && (s == GuestState.WaitCheckOut || s == GuestState.Leaving)) { txt = "!"; col = new Color(1f, 0.25f, 0.2f); }
            else
            {
                switch (s)
                {
                    case GuestState.WaitForm: txt = "Бланк · " + LeftSeconds() + " с"; break;
                    case GuestState.WaitKey: txt = "Ключ · " + LeftSeconds() + " с"; break;
                    case GuestState.WaitCheckOut: txt = "Выезд · " + LeftSeconds() + " с"; break;
                    case GuestState.Signing: txt = "Подписывает…"; col = new Color(0.7f, 0.9f, 1f); break;
                    case GuestState.Down: txt = "Без сознания"; col = new Color(0.8f, 0.8f, 0.8f); break;
                    case GuestState.Brawl: txt = "Драка!"; col = new Color(1f, 0.35f, 0.25f); break;
                }
                if ((s == GuestState.WaitForm || s == GuestState.WaitKey || s == GuestState.WaitCheckOut) && LeftSeconds() <= 8.0 / TimeScale)
                    col = new Color(1f, 0.85f, 0.3f);
            }
            if (txt != lastText)
            {
                lastText = txt;
                status.text = txt;
                status.characterSize = statusBaseSize * (txt == "!" ? 2.2f : 1f);
            }
            status.color = col;
            var cam = Camera.main;
            if (cam != null && txt != "")
                status.transform.rotation = Quaternion.LookRotation(status.transform.position - cam.transform.position);
        }
    }
}
