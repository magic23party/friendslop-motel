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
        Brawl,          // (C5) дерётся с другим гостем в коридоре, пока игроки не уложат обоих
        ToToilet,       // (V6) идёт в туалет (сначала к двери, потом внутрь)
        AtToilet        // (V6) стоит в туалете (или у двери засорённого туалета — уйдёт недовольным)
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
        public static readonly bool Fast = NetBootstrap.Arg("-guesttest") || NetBootstrap.Arg("-fighttest") || NetBootstrap.Arg("-hammertest") || NetBootstrap.Arg("-shoptest") || NetBootstrap.Arg("-toilettest") || NetBootstrap.Arg("-dirttest");
        public static float TimeScale => Fast ? 10f : 1f;

        static readonly string[] TestFlags = { "-guesttest", "-fighttest", "-hammertest", "-shoptest", "-roomtest", "-daytest", "-toilettest", "-dirttest", "-shot" };
        static bool? inTest;
        static bool InTest
        {
            get
            {
                if (inTest == null) { bool t = false; foreach (var f in TestFlags) t |= NetBootstrap.Arg(f); inTest = t; }
                return inTest.Value;
            }
        }
        /// <summary>V6: случайные «грязные» гости. В самотестах и скриншотах выключено (гостей задают явно).</summary>
        public static bool RandomDirty => !InTest;
        /// <summary>V6: гости ходят в туалет. Выключено в чужих самотестах (они считают гостей и задачи), включено в -dirttest; null — по флагам.</summary>
        public static bool? VisitsOverride;
        public static bool ToiletVisitsEnabled => VisitsOverride ?? (!InTest || NetBootstrap.Arg("-dirttest"));
        /// <summary>V6: гость в туалете/у его двери (сервер и клиенты).</summary>
        public static int ToiletGuestCount()
        {
            int n = 0;
            foreach (var g in All) if (g != null && g.IsSpawned && (g.State.Value == GuestState.ToToilet || g.State.Value == GuestState.AtToilet)) n++;
            return n;
        }
        /// <summary>Сколько визитов в туалет состоялось / сколько гостей ушли от двери засорённого (лог и самотесты).</summary>
        public static int StatToiletVisits, StatToiletBlocked;

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
        /// <summary>V6: на сколько % гость недоплатит за номер с проблемами (0 / 50 / 100); над головой — злой смайлик и «−N% $».</summary>
        public NetworkVariable<byte> PayCut = new NetworkVariable<byte>(0);
        /// <summary>V6: гость ушёл от двери засорённого туалета недовольным (над головой «Туалет засорён!»).</summary>
        public NetworkVariable<bool> Unhappy = new NetworkVariable<bool>(false);

        // сервер
        PlayerCombat aggroTarget;   // кого бьёт (ударил гостя)
        double aggroUntil;
        float nextAttack, nextChase, baseSpeed;
        Guest brawlPartner;
        bool brawlArrived;
        Vector3 brawlSpot;
        bool nonPayer;
        bool dirty;             // V6: «грязный» гость — оставляет пятна на полу по пути
        int dirtLeft;           // сколько пятен ещё может оставить
        float dirtAcc;          // пройдено с прошлого пятна, м
        Vector3 lastPos;
        int roomProblems;       // V6: сколько проблем гость насчитал в номере при входе
        double nextToiletAt, toiletEnd;
        int toiletLeg;          // 0 — идёт к двери, 1 — внутрь
        int toiletSlot;
        bool toiletBlocked;     // туалет засорён: гость стоит у двери
        Vector3 toiletPoint;
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
        AngryFace face;
        float penaltyFlash;
        float lie, flashT, punchT, phase;
        int pendingLook;
        /// <summary>Скриншоты (P2): гость замёрз на месте (не ходит, не уходит).</summary>
        public bool ShotFrozen { get; private set; }

        public bool IsLiving => State.Value == GuestState.Living;
        /// <summary>V6: злой смайлик над головой (самотесты).</summary>
        public AngryFace Face => face;
        public static string PayCutText(int pct) => "\u2212" + pct + "% $";
        /// <summary>Сервер: можно втянуть в драку (живёт в номере, не отвлечён на агрессию, не побит).</summary>
        public bool CanBrawl => IsServer && IsSpawned && State.Value == GuestState.Living && aggroTarget == null && Hp.Value > 0;
        public int CarriedMoney => carriedMoney;
        public bool IsNonPayer => nonPayer;
        public bool IsDirty => dirty;
        /// <summary>Сервер: сколько проблем гость насчитал в номере (самотесты).</summary>
        public int RoomProblems => roomProblems;
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
        public void ServerInit(bool isNonPayer, bool isDirty = false)
        {
            nonPayer = isNonPayer;
            dirty = isDirty;
            dirtLeft = isDirty && config != null ? config.dirtMaxPerGuest : 0;
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

        static bool WaitsAtDesk(GuestState s) { return s == GuestState.WaitForm || s == GuestState.WaitKey || s == GuestState.WaitCheckOut; }

        /// <summary>Сколько гостей сейчас ждут у стойки (бланк, ключ, выписка): их терпение тает, пока туалет грязный.</summary>
        public static int WaitingCount()
        {
            int n = 0;
            foreach (var g in All) if (g != null && g.IsSpawned && WaitsAtDesk(g.State.Value)) n++;
            return n;
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

        float patienceDebt;

        /// <summary>V6: во сколько раз быстрее тает терпение у стойки: избытки над 1 от грязного туалета (V3) и грязи на полу складываются.</summary>
        public static float PatienceMul => 1f + (Toilet.PatienceMul - 1f) + (DirtStain.PatienceMul - 1f);

        /// <summary>
        /// V3/V6: пока туалет грязный/засорён или пятен на полу много, терпение у стойки тает в PatienceMul раз быстрее: дедлайн (Until)
        /// подтягивается к «сейчас» на (mul-1)*dt. Двигаем пачками по 0,25 с, чтобы не слать NetworkVariable каждый кадр.
        /// </summary>
        void ToiletDrain(GuestState s)
        {
            float mul = PatienceMul;
            if (mul <= 1f || !WaitsAtDesk(s)) { patienceDebt = 0f; return; }
            patienceDebt += Time.deltaTime * (mul - 1f);
            if (patienceDebt < 0.25f) return;
            Until.Value -= patienceDebt;
            patienceDebt = 0f;
        }

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
            ToiletDrain(State.Value);
            TickDirt();

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
                        nextToiletAt = now + ToiletInterval();
                        StatSettled++;
                        Debug.Log($"[GUEST] #{ticket} settled in room {RoomNo.Value}");
                        CountRoomProblems();
                    }
                    break;

                case GuestState.Living:
                    if (now >= stayEnd) CheckOutFromRoom(now);
                    else TryToiletVisit(now);
                    break;

                case GuestState.ToToilet:
                case GuestState.AtToilet:
                    if (now >= stayEnd) { Debug.Log($"[GUEST] #{ticket} time is up, leaves for check-out from the toilet"); CheckOutFromRoom(now); }
                    else TickToilet(now, s);
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

        // ------------------------------------------------------------ V6: грязь, проблемы номера, туалет

        /// <summary>Грязный гость оставляет пятно каждые dirtStep метров пути (от входа до номера), не больше dirtMaxPerGuest.</summary>
        void TickDirt()
        {
            var p = transform.position;
            var s = State.Value;
            if (dirtLeft > 0 && (s == GuestState.Arriving || s == GuestState.ToRoom))
            {
                var d = p - lastPos; d.y = 0f;
                if (d.magnitude < 2f) dirtAcc += d.magnitude;            // скачок (телепорт «застрял») не считаем
                if (dirtAcc >= Mathf.Max(0.5f, config.dirtStep))
                {
                    dirtAcc = 0f;
                    if (DirtStain.ServerSpawn(config, p) != null) dirtLeft--;
                }
            }
            lastPos = p;
        }

        /// <summary>Сервер: гость вошёл в номер и считает проблемы (недостающие расходники, грязь, засор, лампа): 1 — минус 50 %, 2+ — не платит.</summary>
        void CountRoomProblems()
        {
            var room = Room.Get(RoomNo.Value);
            roomProblems = room != null ? room.ProblemCount : 0;
            byte cut = 0;
            if (roomProblems >= Mathf.Max(1, config.noPayProblems)) cut = 100;
            else if (roomProblems >= 1) cut = (byte)Mathf.RoundToInt(Mathf.Clamp01(config.oneProblemPayCut) * 100f);
            PayCut.Value = cut;
            if (cut > 0)
            {
                Debug.Log($"[DIRT] guest #{ticket} found {roomProblems} problem(s) in room {RoomNo.Value}: pays {(cut >= 100 ? "nothing" : (100 - cut) + "%")}");
                PayCutRpc(cut, true);
            }
        }

        int PayFor(int cutPct) => Mathf.RoundToInt(config.guestPays * Mathf.Clamp(100 - cutPct, 0, 100) / 100f);

        float ToiletInterval() => Random.Range(config.toiletVisitMin, Mathf.Max(config.toiletVisitMin, config.toiletVisitMax)) / TimeScale;

        void TryToiletVisit(double now)
        {
            if (!ToiletVisitsEnabled || now < nextToiletAt) return;
            var wc = Toilet.Instance;
            if (wc == null || !wc.IsSpawned) { nextToiletAt = now + 5.0 / TimeScale; return; }
            if (stayEnd - now < 12.0 / TimeScale) { nextToiletAt = stayEnd + 1.0; return; }       // скоро выезд — не до туалета
            int inside = ToiletGuestCount();
            if (inside >= Mathf.Max(1, config.toiletMaxGuests)) { nextToiletAt = now + 2.0 / TimeScale; return; }
            toiletSlot = inside;
            toiletLeg = 0;
            toiletBlocked = false;
            toiletPoint = wc.DoorPoint;
            Enter(GuestState.ToToilet);
            SetDest(toiletPoint);
            Debug.Log($"[DIRT] guest #{ticket} goes to the toilet");
        }

        void TickToilet(double now, GuestState s)
        {
            var wc = Toilet.Instance;
            if (s == GuestState.ToToilet)
            {
                if (!Reached()) return;
                if (toiletLeg == 0)
                {
                    if (wc != null && wc.IsSpawned && wc.HasClog)         // засорён: постоит у двери и уйдёт недовольным
                    {
                        toiletBlocked = true;
                        toiletEnd = now + config.toiletBlockedStay / TimeScale;
                        Unhappy.Value = true;
                        StatToiletBlocked++;
                        Enter(GuestState.AtToilet);
                        Debug.Log($"[DIRT] guest #{ticket} found the toilet clogged, stands at the door");
                        return;
                    }
                    toiletLeg = 1;
                    toiletPoint = wc != null ? wc.VisitSpot(toiletSlot) : toiletPoint;
                    SetDest(toiletPoint);
                    return;
                }
                toiletEnd = now + Random.Range(config.toiletStayMin, Mathf.Max(config.toiletStayMin, config.toiletStayMax)) / TimeScale;
                Enter(GuestState.AtToilet);
                return;
            }
            if (now < toiletEnd) return;
            if (!toiletBlocked)
            {
                StatToiletVisits++;
                bool worse = wc != null && wc.ServerGuestVisited(false);
                Debug.Log($"[DIRT] guest #{ticket} used the toilet" + (worse ? $" and left a mess (stain={wc.HasStain} clog={wc.HasClog})" : ""));
            }
            else Debug.Log($"[DIRT] guest #{ticket} walks away from the clogged toilet unhappy (no fine)");
            Unhappy.Value = false;
            nextToiletAt = now + ToiletInterval();
            Enter(GuestState.Living);
            var room = Room.Get(RoomNo.Value);
            if (room != null) SetDest(Vector3.Lerp(room.inside.position, room.bed.position, 0.35f));
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
            int pay = PayFor(PayCut.Value);          // V6: оплата уже срезана по проблемам, найденным при входе
            if (room != null) room.ServerSetNeedsService();      // проблемы, с которыми номер заселяли, остаются + новые по старым правилам
            carriedMoney = pay;
            ticket = ++ticketCounter;            // в конец очереди
            Debug.Log($"[GUEST] #{ticket} leaves room {RoomNo.Value}, heads to check-out with {carriedMoney} $" + (PayCut.Value > 0 ? $" (-{PayCut.Value}% for {roomProblems} problem(s))" : ""));
            Enter(GuestState.ToDesk);
            if (PayCut.Value > 0) PayCutRpc(PayCut.Value, false);        // «при выезде»: смайлик и «−N% $» ещё раз
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
            var who = attacker != null ? attacker.GetComponent<PlayerCombat>() : null;
            bool canAggro = who != null && !who.IsDown.Value;
            // V5: удар по одному из драчунов — оба бросают драку друг с другом и вдвоём нападают на ударившего
            // (State остаётся Brawl: драка как событие и штраф идут, пока оба на ногах)
            if (s == GuestState.Brawl && canAggro && brawlPartner != null && brawlPartner.IsSpawned
                && brawlPartner.State.Value == GuestState.Brawl && brawlPartner.Hp.Value > 0) brawlPartner.StartAggro(who);
            if (Hp.Value <= 0) { GoDown(); return; }
            if (canAggro) StartAggro(who);
        }

        /// <summary>Только самотесты: гость остывает (как по таймауту агрессии) и возвращается к прежнему делу.</summary>
        public void ServerCalm() { if (!IsServer) return; EndAggro(); ResumeAfterAggro(); }
        public PlayerCombat AggroTarget => aggroTarget;
        /// <summary>Сколько раз гость ударил игрока в агрессии (самотесты).</summary>
        public int Punches { get; private set; }

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
            if (from == GuestState.ToRoom || from == GuestState.Living || from == GuestState.Brawl || from == GuestState.ToToilet || from == GuestState.AtToilet)
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
                Punches++;
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
                case GuestState.Brawl:          // V5: остыл после удара по драчуну — возвращается в драку
                    brawlArrived = false;
                    SetDest(brawlSpot);
                    break;
                case GuestState.ToRoom:
                case GuestState.Living:
                {
                    var room = Room.Get(RoomNo.Value);
                    if (room != null) SetDest(Vector3.Lerp(room.inside.position, room.bed.position, 0.35f));
                    break;
                }
                case GuestState.ToToilet:       // V6: остыл — продолжает путь в туалет
                case GuestState.AtToilet:
                    SetDest(toiletPoint);
                    break;
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
            brawlSpot = spot;
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

        /// <summary>V6: гость недоволен номером (discovery — только что вошёл и насчитал проблемы) или платит урезанную сумму (выезд): смайлик и «−N% $» видят все.</summary>
        [Rpc(SendTo.Everyone)]
        void PayCutRpc(byte pct, bool discovery)
        {
            penaltyFlash = discovery ? 6f : 4f;
            var p = transform.position + Vector3.up * 2.7f;
            Fx.Text(PayCutText(pct), p, new Color(1f, 0.3f, 0.25f));
            Sfx.Play3D(Snd.Fine, p, 0.8f);
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
            else if (flag == "checkout50" || flag == "checkout100") { PayCut.Value = (byte)(flag == "checkout100" ? 100 : 50); Enter(GuestState.WaitCheckOut, Gs.Now + 25); }
            else if (flag == "toilet" || flag == "toiletclog") { Unhappy.Value = flag == "toiletclog"; Enter(GuestState.AtToilet); }
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
                        if (r == null || !r.CanAssign) return "Номер " + held.arg + " занят или закрыт";
                        return r.IsReady ? Interact.E("выдать ключ №" + held.arg) : Interact.E("выдать ключ №" + held.arg + " — номер НЕ готов, гость недоплатит");
                    }
                    return held.IsNone ? "Гостю нужен ключ свободного номера (" + LeftSeconds() + " с)" : Interact.Busy;
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
                    if (room == null || !room.CanAssign) return;        // V6: занят/закрыт нельзя; неготовый — можно (проблемы считаются при входе)
                    who.ServerClear();
                    keyRoom = room.index;
                    RoomNo.Value = room.index;
                    room.ServerSetOccupied();
                    Enter(GuestState.ToRoom);
                    SetDest(Vector3.Lerp(room.inside.position, room.bed.position, 0.35f));
                    Debug.Log($"[GUEST] #{ticket} got key {keyRoom}{(room.IsReady ? "" : " of an UNREADY room")}, heads to room");
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
                    if (PayCut.Value > 0) PayCutRpc(PayCut.Value, false);           // выезд: «−N% $» над головой, пока гость уходит
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
                    case GuestState.ToToilet: txt = "Идёт в туалет"; col = new Color(0.75f, 0.85f, 0.9f); break;
                    case GuestState.AtToilet:
                        if (Unhappy.Value) { txt = "Туалет засорён!"; col = new Color(1f, 0.55f, 0.25f); }
                        else { txt = "В туалете"; col = new Color(0.75f, 0.85f, 0.9f); }
                        break;
                    case GuestState.Brawl: txt = "Драка!"; col = new Color(1f, 0.35f, 0.25f); break;
                }
                if ((s == GuestState.WaitForm || s == GuestState.WaitKey || s == GuestState.WaitCheckOut) && LeftSeconds() <= 8.0 / TimeScale)
                    col = new Color(1f, 0.85f, 0.3f);
            }
            // V6: злой смайлик и «−N% $» над головой (видно всем): при обнаружении проблем, затем снова при выезде
            penaltyFlash = Mathf.Max(0f, penaltyFlash - Time.deltaTime);
            int cut = PayCut.Value;
            bool showFace = cut > 0 && (penaltyFlash > 0f || s == GuestState.ToDesk || s == GuestState.WaitCheckOut || s == GuestState.Leaving);
            if (showFace && face == null) face = AngryFace.Attach(transform);
            if (face != null) face.Tick(showFace, showFace ? PayCutText(cut) : "", Camera.main, Time.deltaTime);
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
