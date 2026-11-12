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
    public class Room : NetworkBehaviour
    {
        public static readonly List<Room> All = new List<Room>();

        /// <summary>Расходники по индексам маски Missing и supplyPoints: 0 полотенце, 1 мыло, 2 салфетки.</summary>
        public static readonly ItemType[] SupplyTypes = { ItemType.Towel, ItemType.Soap, ItemType.Napkins };
        public static readonly string[] SupplyNames = { "полотенце", "мыло", "салфетки" };
        const string StainName = "грязь";

        public int index;                 // 1..20
        public Transform door;            // центр проёма
        public Transform outside;         // точка в коридоре у двери
        public Transform inside;          // точка в номере у двери (куда идёт гость)
        public Transform bed;             // кровать (цель для гостя и для заноса)
        public Transform stainPoint;      // место пятна
        public GameObject stainVisual;    // пятно (показывается, пока HasStain)
        public Transform[] supplyPoints;  // точки под расходники: 0 полотенце, 1 мыло, 2 салфетки
        public GameObject[] supplyStock;  // предметы на этих точках (видны, если расходник на месте)
        public RoomSpot stainSpot;        // цель для швабры
        public TextMesh signNumber;       // табличка над дверью: номер
        public TextMesh signStatus;       // табличка: статус / список нужного (текст остаётся для самотестов; при «нужно» вместо него рисуются значки)
        public GameObject[] signIcons;    // значки нужного на табличке (P2): 0 полотенце, 1 мыло, 2 салфетки, 3 грязь
        public GameObject blocker;        // стена-заглушка в проёме, если номер закрыт

        public NetworkVariable<byte> StateVar = new NetworkVariable<byte>((byte)RoomState.Ready);
        /// <summary>Бит i = расходник SupplyTypes[i] ещё надо занести.</summary>
        public NetworkVariable<byte> MissingVar = new NetworkVariable<byte>(0);
        public NetworkVariable<bool> StainVar = new NetworkVariable<bool>(false);
        /// <summary>Прогресс мытья пятна, 0..100.</summary>
        public NetworkVariable<byte> MopPct = new NetworkVariable<byte>(0);

        public RoomState State => (RoomState)StateVar.Value;
        public int Missing => MissingVar.Value;
        public bool HasStain => StainVar.Value;
        public bool Needs(int supplyIndex) => (MissingVar.Value & (1 << supplyIndex)) != 0;
        public bool Locked => !IsActive(index);
        /// <summary>Номер открыт, чистый и готов принять гостя (ключ на доске проверяет вызывающий).</summary>
        public bool IsReady => !Locked && State == RoomState.Ready;

        float mopProgress;
        int lastKey = -1;

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
            return sb.ToString();
        }

        // ------------------------------------------------------------ сервер: API для гостей (C4) и тестов

        GameConfig Cfg => GameState.Instance != null ? GameState.Instance.config : null;

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

        public void ServerSetReady() { ServerSetState(RoomState.Ready, 0, false); }

        /// <summary>Сервер: в номер заехал гость.</summary>
        public void ServerSetOccupied() { ServerSetState(RoomState.Occupied, 0, false); }

        /// <summary>Сервер: гость выехал — случайный набор недостающего (GameConfig) + шанс пятна.</summary>
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
            ServerSetNeedsService(mask, Random.value < stainChance);
        }

        /// <summary>Сервер: детерминированный вариант (тесты). Пустой набор без пятна — сразу Ready.</summary>
        public void ServerSetNeedsService(int mask, bool stain)
        {
            mask &= 7;
            ServerSetState(mask == 0 && !stain ? RoomState.Ready : RoomState.NeedsService, mask, stain);
        }

        void ServerSetState(RoomState s, int mask, bool stain)
        {
            if (!IsServer || !IsSpawned) return;
            mopProgress = 0f;
            MopPct.Value = 0;
            MissingVar.Value = (byte)mask;
            StainVar.Value = stain;
            StateVar.Value = (byte)s;
            Debug.Log($"[ROOM] {index} -> {s}" + (s == RoomState.NeedsService ? $" needs: {NeedsText()}" : ""));
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

        /// <summary>Сервер: швабра трёт пятно dt секунд. Возвращает true, когда пятно смыто.</summary>
        public bool ServerMop(float dt)
        {
            if (!IsServer || State != RoomState.NeedsService || !HasStain) return false;
            var cfg = Cfg;
            float need = cfg != null ? cfg.mopTime : 3f;
            mopProgress += dt;
            if (mopProgress >= need)
            {
                mopProgress = 0f;
                MopPct.Value = 0;
                StainVar.Value = false;
                Debug.Log($"[ROOM] {index} stain cleaned");
                CheckDone();
                return true;
            }
            MopPct.Value = (byte)Mathf.Clamp(Mathf.FloorToInt(mopProgress / need * 100f), 0, 99);
            return false;
        }

        /// <summary>Сервер: швабру убрали (отпустили ЛКМ / отошли) — прогресс сбрасывается.</summary>
        public void ServerMopReset()
        {
            if (!IsServer) return;
            mopProgress = 0f;
            if (MopPct.Value != 0) MopPct.Value = 0;
        }

        void CheckDone()
        {
            if (State == RoomState.NeedsService && MissingVar.Value == 0 && !StainVar.Value)
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

        void ServerTick()
        {
            if (State != RoomState.NeedsService || !HasStain || stainSpot == null) return;
            var cfg = Cfg;
            float reach = cfg != null ? cfg.interactDistance + cfg.interactReachSlack : 4f;
            bool mopping = false;
            foreach (var h in PlayerHands.All)
            {
                if (!h.UseDown || h.Held.Value.type != ItemType.Mop || !ReferenceEquals(h.UseTarget, stainSpot)) continue;
                if (Interact.Distance(stainSpot, h.Eye) > reach) continue;
                mopping = true; break;
            }
            if (mopping) ServerMop(Time.deltaTime);
            else if (mopProgress > 0f) ServerMopReset();
        }

        void RefreshView(bool locked)
        {
            int key = (locked ? 1 : 0) | (StateVar.Value << 1) | (MissingVar.Value << 4) | ((HasStain ? 1 : 0) << 8) | (MopPct.Value << 9);
            if (key == lastKey) return;
            lastKey = key;

            if (stainVisual != null)
            {
                if (stainVisual.activeSelf != HasStain) stainVisual.SetActive(HasStain);
                float s = Mathf.Lerp(1f, 0.35f, MopPct.Value / 100f);
                stainVisual.transform.localScale = new Vector3(s, 0.01f, s);
            }
            if (supplyStock != null)
                for (int i = 0; i < supplyStock.Length; i++)
                    if (supplyStock[i] != null) supplyStock[i].SetActive(!Needs(i));

            if (signStatus == null) return;
            bool needs = !locked && State == RoomState.NeedsService;
            var smr = signStatus.GetComponent<MeshRenderer>();
            if (smr != null) smr.enabled = !needs;              // список «нужно» мелкий — вместо него цветные значки
            if (signIcons != null)
                for (int i = 0; i < signIcons.Length; i++)
                    if (signIcons[i] != null) signIcons[i].SetActive(needs && (i < SupplyTypes.Length ? Needs(i) : HasStain));
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
                    signStatus.text = sb.ToString();
                    signStatus.color = new Color(1f, 0.9f, 0.5f);
                    break;
            }
        }

        static string Cap(string s) => char.ToUpper(s[0]) + s.Substring(1);
    }
}
