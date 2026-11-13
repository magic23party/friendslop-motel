using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Место, которое пачкается и засоряется: номер (Room) и общий туалет (Toilet). Вся механика «пятно — швабра, засор — вантуз
    /// или сантехник» живёт здесь один раз; ключи, гости и заселение — только в Room (туалет не сдаётся).
    /// Пятно и засор, прогресс мытья/прочистки и вызов сантехника — NetworkVariable, пишет только сервер.
    /// </summary>
    public abstract class DirtySite : NetworkBehaviour
    {
        public GameObject stainVisual;    // пятно (показывается, пока HasStain)
        public GameObject clogVisual;     // лужа засора (показывается, пока HasClog)
        public RoomSpot stainSpot;        // цель для швабры
        public RoomSpot clogSpot;         // цель для вантуза

        public NetworkVariable<bool> StainVar = new NetworkVariable<bool>(false);
        /// <summary>Прогресс мытья пятна, 0..100.</summary>
        public NetworkVariable<byte> MopPct = new NetworkVariable<byte>(0);
        /// <summary>V2 «Засор»: пока не прочищен, номер нельзя выдать (туалет — терпение гостей тает быстрее).</summary>
        public NetworkVariable<bool> ClogVar = new NetworkVariable<bool>(false);
        /// <summary>Прогресс прочистки засора вантузом, 0..100.</summary>
        public NetworkVariable<byte> PlungePct = new NetworkVariable<byte>(0);
        /// <summary>Когда сантехник закончит (ServerTime); 0 — не вызван.</summary>
        public NetworkVariable<double> PlumberEnd = new NetworkVariable<double>(0);

        public bool HasStain => StainVar.Value;
        public bool HasClog => ClogVar.Value;
        public bool PlumberPending => PlumberEnd.Value > 0;

        /// <summary>Название места для подсказок и логов («Номер 7», «Туалет»).</summary>
        public abstract string SiteName { get; }
        /// <summary>Можно ли сейчас мыть/прочищать (номер — только в NeedsService, туалет — пока идёт смена).</summary>
        public abstract bool CanClean { get; }
        /// <summary>Сервер: пятно смыто / засор прочищен (номер проверяет, не готов ли он).</summary>
        protected virtual void OnMessCleared() { }

        protected float mopProgress, plungeProgress;

        protected GameConfig Cfg => GameState.Instance != null ? GameState.Instance.config : null;

        /// <summary>Сервер: сбросить прогресс мытья/прочистки и вызов сантехника (новое состояние места).</summary>
        protected void ResetMessProgress()
        {
            mopProgress = 0f; plungeProgress = 0f;
            MopPct.Value = 0; PlungePct.Value = 0;
            PlumberEnd.Value = 0;
        }

        /// <summary>Сервер: швабра трёт пятно dt секунд. Возвращает true, когда пятно смыто.</summary>
        public bool ServerMop(float dt)
        {
            if (!IsServer || !CanClean || !HasStain) return false;
            var cfg = Cfg;
            float need = cfg != null ? cfg.mopTime : 3f;
            mopProgress += dt;
            if (mopProgress >= need)
            {
                mopProgress = 0f;
                MopPct.Value = 0;
                StainVar.Value = false;
                Debug.Log($"[ROOM] {SiteName}: stain cleaned");
                OnMessCleared();
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

        /// <summary>Сервер: вантуз прочищает засор dt секунд. true — засор прочищен.</summary>
        public bool ServerPlunge(float dt)
        {
            if (!IsServer || !HasClog) return false;
            var cfg = Cfg;
            float need = cfg != null ? cfg.plungeTime : 3f;
            plungeProgress += dt;
            if (plungeProgress >= need) { ClearClog("plunger"); return true; }
            PlungePct.Value = (byte)Mathf.Clamp(Mathf.FloorToInt(plungeProgress / need * 100f), 0, 99);
            return false;
        }

        public void ServerPlungeReset()
        {
            if (!IsServer) return;
            plungeProgress = 0f;
            if (PlungePct.Value != 0) PlungePct.Value = 0;
        }

        /// <summary>Сервер: сантехник вызван (деньги списывает GameState). false — нет засора или уже вызван.</summary>
        public bool ServerCallPlumber()
        {
            var gs = GameState.Instance;
            if (!IsServer || !HasClog || PlumberPending || gs == null) return false;
            PlumberEnd.Value = gs.Now + gs.PlumberSeconds;
            Debug.Log($"[ROOM] {SiteName}: plumber called, done in {gs.PlumberSeconds:0}s");
            return true;
        }

        protected void ClearClog(string by)
        {
            plungeProgress = 0f;
            PlungePct.Value = 0;
            PlumberEnd.Value = 0;
            ClogVar.Value = false;
            Debug.Log($"[ROOM] {SiteName}: clog cleared by {by}");
            OnMessCleared();
        }

        /// <summary>Сервер, каждый кадр: сантехник доделал; швабра/вантуз в руках у игрока, который держит ЛКМ на цели.</summary>
        protected void ServerTickMess()
        {
            var gs = GameState.Instance;
            if (HasClog && PlumberEnd.Value > 0 && gs != null && gs.Now >= PlumberEnd.Value) ClearClog("plumber");   // V2: сантехник доделал
            if (!CanClean) return;
            var cfg = Cfg;
            float reach = cfg != null ? cfg.interactDistance + cfg.interactReachSlack : 4f;
            if (HasStain && stainSpot != null)
            {
                bool mopping = Holding(ItemType.Mop, stainSpot, reach);
                if (mopping) ServerMop(Time.deltaTime);
                else if (mopProgress > 0f) ServerMopReset();
            }
            if (HasClog && clogSpot != null)
            {
                bool plunging = Holding(ItemType.Plunger, clogSpot, reach);
                if (plunging) ServerPlunge(Time.deltaTime);
                else if (plungeProgress > 0f) ServerPlungeReset();
            }
        }

        /// <summary>Сервер: кто-то держит ЛКМ с предметом type на цели spot и достаёт до неё.</summary>
        static bool Holding(ItemType type, RoomSpot spot, float reach)
        {
            foreach (var h in PlayerHands.All)
            {
                if (!h.UseDown || h.Held.Value.type != type || !ReferenceEquals(h.UseTarget, spot)) continue;
                if (Interact.Distance(spot, h.Eye) > reach) continue;
                return true;
            }
            return false;
        }

        /// <summary>Пятно и лужа засора на экране (клиент и сервер): масштаб уменьшается по ходу мытья/прочистки.</summary>
        protected void RefreshMessVisuals()
        {
            if (stainVisual != null)
            {
                if (stainVisual.activeSelf != HasStain) stainVisual.SetActive(HasStain);
                float s = Mathf.Lerp(1f, 0.35f, MopPct.Value / 100f);
                stainVisual.transform.localScale = new Vector3(s, 0.01f, s);
            }
            if (clogVisual != null)
            {
                if (clogVisual.activeSelf != HasClog) clogVisual.SetActive(HasClog);
                float cs = Mathf.Lerp(1f, 0.4f, PlungePct.Value / 100f);
                clogVisual.transform.localScale = new Vector3(cs, 1f, cs);
            }
        }

        /// <summary>Сколько осталось до конца работы сантехника, с.</summary>
        public float PlumberLeft
        {
            get
            {
                var gs = GameState.Instance;
                return gs == null ? 0f : Mathf.Max(0f, (float)(PlumberEnd.Value - gs.Now));
            }
        }
    }
}
