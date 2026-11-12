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
        Down            // (C5) лежит после ударов
    }

    /// <summary>
    /// Гость (C4). Сетевой префаб: NetworkTransform (авторитет сервера), NavMeshAgent включён только на сервере.
    /// Жизненный цикл: вход → очередь → бланк → подпись → ключ → номер → проживание → выписка → выход.
    /// Состояние и дедлайн (ServerTime) — NetworkVariable: клиенты рисуют статус над головой и подсказки без трафика.
    /// Всё решает сервер; игрок взаимодействует через IInteractable (E с предметом в руках).
    /// API для C5 (бой): Hp, ServerHit(attacker), ServerFlee, ServerDropMoney, IsLiving, CarriedMoney, Marked.
    /// </summary>
    public class Guest : NetworkBehaviour, IInteractable
    {
        public static readonly List<Guest> All = new List<Guest>();

        /// <summary>Самотест -guesttest: все интервалы короче в 10 раз, гости бегают втрое быстрее.</summary>
        public static readonly bool Fast = NetBootstrap.Arg("-guesttest");
        public static float TimeScale => Fast ? 10f : 1f;
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

        // сервер
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

        string lastText = "";
        float statusBaseSize;

        public bool IsLiving => State.Value == GuestState.Living;
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
            if (inN > 0) into.Add(new TaskLine("Гость у стойки (заселение)" + (inN > 1 ? " ×" + inN : ""), inUrg));
            if (outN > 0) into.Add(new TaskLine("Гость у стойки (выезд)" + (outN > 1 ? " ×" + outN : ""), outUrg));
            foreach (var w in WorldItem.All)
            {
                if (w == null || !w.IsSpawned) continue;
                var it = w.Item.Value;
                if (it.type == ItemType.Key) into.Add(new TaskLine("Ключ " + it.arg + " на полу"));
            }
        }

        // ------------------------------------------------------------ жизненный цикл объекта

        /// <summary>Сервер: до Spawn(). Определяет, неплательщик ли гость.</summary>
        public void ServerInit(bool isNonPayer)
        {
            nonPayer = isNonPayer;
            ticket = ++ticketCounter;
        }

        void Awake()
        {
            if (status != null) statusBaseSize = status.characterSize;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer)
            {
                Hp.Value = config != null ? config.npcHp : 3;
                GameState.ServerRestarted += ServerDespawn;
                if (agent != null)
                {
                    agent.speed = (config != null ? config.guestSpeed : 4f) * (Fast ? WalkBoost : 1f);
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
            if (gs == null || map == null || config == null) return;
            if (gs.Phase.Value != GamePhase.Playing)
            {
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;      // Game Over / лобби — заморозка
                return;
            }
            if (agent.enabled && agent.isOnNavMesh && agent.isStopped && agent.hasPath) agent.isStopped = false;

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

                case GuestState.Leaving:
                    if ((transform.position - map.guestExit.position).sqrMagnitude < 0.8f * 0.8f
                        || Reached() || Time.time - leavingSince > 60f / (Fast ? WalkBoost : 1f))
                    {
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

        // ------------------------------------------------------------ API для C5 (бой)

        /// <summary>C5: удар по гостю. Заглушка C4 — ничего не делает. Здесь: Hp--, агрессия, Down (npcDownTime), выпадение денег.</summary>
        public virtual void ServerHit(PlayerHands attacker)
        {
            if (!IsServer) return;
            Debug.Log($"[GUEST] #{ticket} hit by {(attacker != null ? attacker.name : "?")} (C5 stub, hp={Hp.Value})");
        }

        /// <summary>C5: гость сбегает (игрок потерял сознание / гость побеждён): ключ роняет, номер освобождает, идёт к выходу.</summary>
        public void ServerFlee(bool dropMoney)
        {
            if (!IsServer || State.Value == GuestState.Leaving) return;
            if (State.Value == GuestState.ToRoom || State.Value == GuestState.Living)
            {
                var room = Room.Get(RoomNo.Value);
                if (room != null) room.ServerSetNeedsService();
            }
            DropKey();
            if (dropMoney) DropMoney();
            StartLeaving();
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
                    Debug.Log($"[GUEST] #{ticket} checked out, paid {pay} $");
                    StartLeaving();
                    break;
            }
        }

        // ------------------------------------------------------------ все машины: статус над головой

        void LateUpdate()
        {
            if (!IsSpawned || status == null) return;
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
