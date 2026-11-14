using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    public enum RoomState : byte { Ready = 0, Occupied = 1, NeedsService = 2 }

    /// <summary>
    /// Номер мотеля (1..20). In-scene NetworkObject: состояние (Ready / Occupied / NeedsService), список
    /// недостающего (маска 3 расходников), пятно и прогресс мытья — NetworkVariable, пишет только сервер.
    /// Локальные оси: +Z — вглубь номера, дверной проём на z=0 (лицом в коридор).
    /// Серверный API для гостей (C4): PickReady / ServerSetOccupied / ServerSetNeedsService.
    /// </summary>
    public class Room : DirtySite
    {
        public static readonly List<Room> All = new List<Room>();

        /// <summary>Расходники по индексам маски Missing и supplyPoints: 0 полотенце, 1 мыло, 2 салфетки.</summary>
        public static readonly ItemType[] SupplyTypes = { ItemType.Towel, ItemType.Soap, ItemType.Napkins };
        public static readonly string[] SupplyNames = { "полотенце", "мыло", "салфетки" };
        const string StainName = "грязь";
        const string ClogName = "засор";

        public int index;                 // 1..20
        public Transform door;            // центр проёма
        public Transform outside;         // точка в коридоре у двери
        public Transform inside;          // точка в номере у двери (куда идёт гость)
        public Transform bed;             // кровать (цель для гостя и для заноса)
        public Transform stainPoint;      // место пятна
        public Transform[] supplyPoints;  // точки под расходники: 0 полотенце, 1 мыло, 2 салфетки
        public GameObject[] supplyStock;  // предметы на этих точках (видны, если расходник на месте)
        public TextMesh signNumber;       // табличка над дверью: номер
        public TextMesh signStatus;       // табличка: статус / список нужного (текст остаётся для самотестов; при «нужно» вместо него рисуются значки)
        public GameObject[] signIcons;    // значки на табличке: 0 полотенце, 1 мыло, 2 салфетки, 3 грязь, 4 засор (V2), 5 перегоревшая лампа (V2; виден и в Ready/Occupied)
        public RoomSpot lampSpot;         // V2: прикроватная лампа — цель для лампочки
        public GameObject lampOn, lampOff;// V2: абажур горит / перегорел
        public GameObject blocker;        // стена-заглушка в проёме, если номер закрыт

        public NetworkVariable<byte> StateVar = new NetworkVariable<byte>((byte)RoomState.Ready);
        /// <summary>Бит i = расходник SupplyTypes[i] ещё надо занести.</summary>
        public NetworkVariable<byte> MissingVar = new NetworkVariable<byte>(0);
        /// <summary>V2 «Лампа»: переживает заселение; для гостя это одна из проблем номера (V6).</summary>
        public NetworkVariable<bool> LampVar = new NetworkVariable<bool>(false);

        public RoomState State => (RoomState)StateVar.Value;
        public int Missing => MissingVar.Value;
        public bool LampBroken => LampVar.Value;
        public bool Needs(int supplyIndex) => (MissingVar.Value & (1 << supplyIndex)) != 0;
        public bool Locked => !IsActive(index);
        /// <summary>Номер открыт, чистый и готов принять гостя (ключ на доске проверяет вызывающий).</summary>
        public bool IsReady => !Locked && State == RoomState.Ready;

        public override string SiteName => "Номер " + index;
        public override bool CanClean => State == RoomState.NeedsService;

        int lastKey = -1;
        Vector3 signStatusBase;
        bool signStatusBaseSet;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static Room Get(int index)
        {
            foreach (var r in All) if (r.index == index) return r;
            return null;
        }

        /// <summary>Сколько номеров открыто: до старта смены — как для малой команды, потом по GameState.RoomCount.</summary>
        public static int ActiveCount()
        {
            var gs = GameState.Instance;
            if (gs == null) return 10;
            if (gs.RoomCount.Value > 0) return gs.RoomCount.Value;
            return gs.config != null ? gs.config.smallRooms : 10;
        }

        public static bool IsActive(int index) => index >= 1 && index <= ActiveCount();

        public static int SupplyIndex(ItemType t)
        {
            for (int i = 0; i < SupplyTypes.Length; i++) if (SupplyTypes[i] == t) return i;
            return -1;
        }

        public void SetSign(string status)
        {
            if (signStatus != null) signStatus.text = status ?? "";
        }

        /// <summary>Что надо сделать: "полотенце, мыло, грязь" (пусто, если ничего).</summary>
        public string NeedsText(string sep = ", ")
        {
            var sb = new StringBuilder();
            for (int i = 0; i < SupplyNames.Length; i++)
                if (Needs(i)) { if (sb.Length > 0) sb.Append(sep); sb.Append(SupplyNames[i]); }
            if (HasStain) { if (sb.Length > 0) sb.Append(sep); sb.Append(StainName); }
            if (HasClog) { if (sb.Length > 0) sb.Append(sep); sb.Append(ClogName); }
            return sb.ToString();
        }

        /// <summary>Что надо занести, значками (P3): расходники и швабра для пятна.</summary>
        public ItemType[] NeedIcons()
        {
            int n = (HasStain ? 1 : 0) + (HasClog ? 1 : 0) + (LampBroken ? 1 : 0);
            for (int i = 0; i < SupplyTypes.Length; i++) if (Needs(i)) n++;
            var a = new ItemType[n];
            int k = 0;
            for (int i = 0; i < SupplyTypes.Length; i++) if (Needs(i)) a[k++] = SupplyTypes[i];
            if (HasStain) a[k++] = ItemType.Mop;
            if (HasClog) a[k++] = ItemType.Plunger;
            if (LampBroken) a[k++] = ItemType.Bulb;
            return a;
        }

        // ------------------------------------------------------------ сервер: API для гостей (C4) и тестов

        /// <summary>Сервер: случайный номер, готовый принять гостя (открыт, Ready, ключ висит на доске, если board задан). null — нет.</summary>
        public static Room PickReady(KeyBoard board = null)
        {
            Room pick = null; int n = 0;
            foreach (var r in All)
            {
                if (!r.IsReady) continue;
                if (board != null && !board.Has(r.index)) continue;
                if (Random.Range(0, ++n) == 0) pick = r;     // reservoir sampling
            }
            return pick;
        }

        /// <summary>Сервер: новая смена / перезапуск. dirty=true — все открытые номера нуждаются в обслуживании (тест C3).</summary>
        public static void ServerResetAll(bool dirty)
        {
            foreach (var r in All)
            {
                if (dirty && !r.Locked) r.ServerSetNeedsService(); else r.ServerSetReady();
            }
        }

        /// <summary>Сервер: всё починено и убрано (новая смена): сбрасывает и засор, и лампу.</summary>
        public void ServerSetReady() { ServerSetState(RoomState.Ready, 0, false, false, false); }

        /// <summary>
        /// Сервер: в номер заехал гость. V6: проблемы неготового номера (недостающее, пятно, засор, лампа) остаются как были —
        /// гость сочтёт их при входе (ProblemCount), а после выезда они сольются с новыми.
        /// </summary>
        public void ServerSetOccupied() { ServerSetState(RoomState.Occupied, Missing, HasStain, HasClog, LampBroken); }

        /// <summary>V6: сколько проблем увидит гость: каждый недостающий расходник, грязь, засор и лампа — по одной.</summary>
        public int ProblemCount
        {
            get
            {
                int n = (HasStain ? 1 : 0) + (HasClog ? 1 : 0) + (LampBroken ? 1 : 0);
                for (int i = 0; i < SupplyTypes.Length; i++) if (Needs(i)) n++;
                return n;
            }
        }

        /// <summary>V6: номер открыт и не занят — его ключ можно отдать гостю, даже если он не готов (риск: проблемы считаются при входе).</summary>
        public bool CanAssign => !Locked && State != RoomState.Occupied;

        /// <summary>Сервер: гость выехал — случайный набор недостающего (GameConfig) + шанс пятна; прежние проблемы (V6) остаются.</summary>
        public void ServerSetNeedsService()
        {
            var cfg = Cfg;
            int min = cfg != null ? cfg.needsMin : 1, max = cfg != null ? cfg.needsMax : 3;
            float stainChance = cfg != null ? cfg.stainChance : 0.4f;
            int count = Mathf.Clamp(Random.Range(min, max + 1), 0, SupplyTypes.Length);
            var order = new List<int> { 0, 1, 2 };
            int mask = 0;
            for (int k = 0; k < count; k++)
            {
                int pick = Random.Range(0, order.Count);
                mask |= 1 << order[pick];
                order.RemoveAt(pick);
            }
            // V2: новые проблемы растят давление по дням
            var gs = GameState.Instance;
            int day = gs != null ? gs.Day.Value : 1;
            bool clog = cfg != null && day >= cfg.clogFromDay && Random.value < cfg.clogChance;
            bool lamp = LampBroken || (cfg != null && day >= cfg.lampFromDay && Random.value < cfg.lampChance);
            // V6: проблемы, с которыми номер заселяли (Occupied хранит их в тех же полях), остаются и складываются с новыми
            ServerSetNeedsService(mask | Missing, HasStain || Random.value < stainChance, HasClog || clog, lamp);
        }

        /// <summary>Сервер: детерминированный вариант (тесты). Пустой набор без пятна и засора — сразу Ready (лампа на готовность не влияет; lamp=null — не менять).</summary>
        public void ServerSetNeedsService(int mask, bool stain, bool clog = false, bool? lamp = null)
        {
            mask &= 7;
            ServerSetState(mask == 0 && !stain && !clog ? RoomState.Ready : RoomState.NeedsService, mask, stain, clog, lamp ?? LampBroken);
        }

        void ServerSetState(RoomState s, int mask, bool stain, bool clog, bool lamp)
        {
            if (!IsServer || !IsSpawned) return;
            ResetMessProgress();
            MissingVar.Value = (byte)mask;
            StainVar.Value = stain;
            ClogVar.Value = clog;
            LampVar.Value = lamp;
            StateVar.Value = (byte)s;
            Debug.Log($"[ROOM] {index} -> {s}" + (s == RoomState.NeedsService ? $" needs: {NeedsText()}" : "") + (lamp ? " (lamp broken)" : ""));
        }

        /// <summary>Сервер: перегорела лампа (тесты, ShotRunner).</summary>
        public void ServerBreakLamp() { if (IsServer && IsSpawned) LampVar.Value = true; }

        /// <summary>Сервер: вкрутить лампочку. true — лампа была сломана (лампочку из рук надо убрать).</summary>
        public bool ServerFixLamp()
        {
            if (!IsServer || !IsSpawned || Locked || !LampBroken) return false;
            LampVar.Value = false;
            Debug.Log($"[ROOM] {index} lamp fixed");
            return true;
        }

        /// <summary>Сервер: занести расходник. true — принят (из рук надо убрать), false — не нужен.</summary>
        public bool ServerDeliver(ItemType t)
        {
            int i = SupplyIndex(t);
            if (!IsServer || i < 0 || State != RoomState.NeedsService || !Needs(i)) return false;
            MissingVar.Value = (byte)(MissingVar.Value & ~(1 << i));
            Debug.Log($"[ROOM] {index} delivered {SupplyNames[i]}, left: {(NeedsText() == "" ? "-" : NeedsText())}");
            CheckDone();
            return true;
        }

        protected override void OnMessCleared() { CheckDone(); }

        void CheckDone()
        {
            if (State == RoomState.NeedsService && MissingVar.Value == 0 && !StainVar.Value && !ClogVar.Value)
            {
                StateVar.Value = (byte)RoomState.Ready;
                Debug.Log($"[ROOM] {index} -> Ready");
            }
        }

        // ------------------------------------------------------------ каждый кадр

        void Update()
        {
            bool locked = Locked;
            if (blocker != null && blocker.activeSelf != locked) blocker.SetActive(locked);
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            RefreshView(locked);
        }

        void ServerTick() { ServerTickMess(); }

        void RefreshView(bool locked)
        {
            int key = (locked ? 1 : 0) | (StateVar.Value << 1) | (MissingVar.Value << 4) | ((HasStain ? 1 : 0) << 8) | (MopPct.Value << 9)
                | ((HasClog ? 1 : 0) << 16) | ((LampBroken ? 1 : 0) << 17) | (PlungePct.Value << 18) | ((PlumberPending ? 1 : 0) << 25);
            if (key == lastKey) return;
            lastKey = key;

            RefreshMessVisuals();
            if (supplyStock != null)
                for (int i = 0; i < supplyStock.Length; i++)
                    if (supplyStock[i] != null) supplyStock[i].SetActive(!Needs(i));
            if (lampOn != null && lampOn.activeSelf == LampBroken) lampOn.SetActive(!LampBroken);
            if (lampOff != null && lampOff.activeSelf != LampBroken) lampOff.SetActive(LampBroken);

            if (signStatus == null) return;
            if (!signStatusBaseSet) { signStatusBaseSet = true; signStatusBase = signStatus.transform.localPosition; }
            bool needs = !locked && State == RoomState.NeedsService;
            bool lampIcon = !locked && LampBroken;
            var smr = signStatus.GetComponent<MeshRenderer>();
            if (smr != null) smr.enabled = !needs;              // список «нужно» мелкий — вместо него цветные значки
            // слово ГОТОВ/ЗАНЯТ поднимается, если внизу справа горит значок лампы
            signStatus.transform.localPosition = signStatusBase + (lampIcon && !needs ? new Vector3(0f, 0.19f, 0f) : Vector3.zero);
            if (signIcons != null)
                for (int i = 0; i < signIcons.Length; i++)
                {
                    if (signIcons[i] == null) continue;
                    bool on = i < SupplyTypes.Length ? needs && Needs(i) : i == 3 ? needs && HasStain : i == 4 ? needs && HasClog : lampIcon;
                    signIcons[i].SetActive(on);
                }
            if (locked) { signStatus.text = ""; return; }
            switch (State)
            {
                case RoomState.Ready:
                    signStatus.text = "ГОТОВ"; signStatus.color = new Color(0.4f, 0.95f, 0.45f); break;
                case RoomState.Occupied:
                    signStatus.text = "ЗАНЯТ"; signStatus.color = new Color(1f, 0.55f, 0.4f); break;
                default:
                    var sb = new StringBuilder();
                    for (int i = 0; i < SupplyNames.Length; i++)
                        if (Needs(i)) { if (sb.Length > 0) sb.Append('\n'); sb.Append(Cap(SupplyNames[i])); }
                    if (HasStain) { if (sb.Length > 0) sb.Append('\n'); sb.Append(Cap(StainName)); }
                    if (HasClog) { if (sb.Length > 0) sb.Append('\n'); sb.Append(Cap(ClogName)); }
                    signStatus.text = sb.ToString();
                    signStatus.color = new Color(1f, 0.9f, 0.5f);
                    break;
            }
        }

        static string Cap(string s) => char.ToUpper(s[0]) + s.Substring(1);
    }
}
