using System.Collections.Generic;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// V3: общий туалет в лобби (in-scene NetworkObject). Не сдаётся и ключей не имеет — пачкается и засоряется так же, как номер
    /// (общая механика DirtySite: пятно — швабра, засор — вантуз или сантехник из магазина). V6: сам по таймеру не пачкается — его посещают
    /// живущие гости (Guest): за визит с шансом toiletDirtyChance остаётся пятно, со 2-го дня с шансом toiletVisitClogChance — засор
    /// (ServerGuestVisited). К засорённому туалету гость подходит, стоит у двери и уходит недовольным. Пока туалет грязный или засорён,
    /// терпение гостей у стойки тает в toiletPatienceMul раз быстрее (Guest). Задача — в списке TaskBoard (электронная таблица за ресепшеном).
    /// </summary>
    public class Toilet : DirtySite
    {
        public static Toilet Instance;

        public Transform inside;          // точка внутри у двери (проверка достижимости, самотест)

        /// <summary>V6: точка в холле перед дверью (куда идёт гость; у засорённого туалета он стоит здесь).</summary>
        public Vector3 DoorPoint => transform.TransformPoint(new Vector3(0f, 0f, 3.3f));
        /// <summary>V6: где стоит гость внутри (два места, чтобы двое не слипались).</summary>
        public Vector3 VisitSpot(int slot) => transform.TransformPoint(slot % 2 == 0 ? new Vector3(0f, 0f, -0.2f) : new Vector3(-0.6f, 0f, 0.6f));
        public TextMesh signStatus;       // табличка над дверью: ЧИСТО / ГРЯЗНО / ЗАСОР
        public Renderer signLamp;         // «светофор» над дверью: цвет по состоянию
        public Material[] lampMats;       // 0 чисто, 1 грязно, 2 засор

        public override string SiteName => "Туалет";
        public override bool CanClean { get { var gs = GameState.Instance; return gs != null && gs.Phase.Value == GamePhase.Playing; } }
        public bool Dirty => HasStain || HasClog;

        /// <summary>Во сколько раз быстрее тает терпение гостей у стойки (1 — туалет чистый).</summary>
        public static float PatienceMul
        {
            get
            {
                var t = Instance;
                var gs = GameState.Instance;
                if (t == null || !t.IsSpawned || gs == null || gs.config == null || !t.Dirty) return 1f;
                return Mathf.Max(1f, gs.config.toiletPatienceMul);
            }
        }

        TaskBoard.Provider provider;
        int lastKey = -1;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            provider = CollectTask;
            TaskBoard.Register(provider);
            lastKey = -1;
        }

        public override void OnNetworkDespawn()
        {
            TaskBoard.Unregister(provider);
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------ сервер

        /// <summary>Сервер: туалет чистый (новая смена, «Заново»).</summary>
        public void ServerReset()
        {
            if (!IsServer || !IsSpawned) return;
            ResetMessProgress();
            StainVar.Value = false;
            ClogVar.Value = false;
        }

        /// <summary>Сервер: детерминированно испачкать (тесты, скриншоты).</summary>
        public void ServerSetDirty(bool stain, bool clog, bool log = true)
        {
            if (!IsServer || !IsSpawned) return;
            ResetMessProgress();
            StainVar.Value = stain;
            ClogVar.Value = clog;
            if (log) Debug.Log($"[TOILET] dirty: stain={stain} clog={clog}");
        }

        /// <summary>
        /// Сервер: гость побывал в туалете. С шансом toiletDirtyChance остаётся пятно, со 2-го дня с шансом toiletVisitClogChance — засор
        /// (уже имеющееся не убирается). Возвращает true, если туалет стал грязнее.
        /// </summary>
        public bool ServerGuestVisited(bool log = true)
        {
            if (!IsServer || !IsSpawned) return false;
            var cfg = Cfg;
            var gs = GameState.Instance;
            if (cfg == null) return false;
            int day = gs != null ? gs.Day.Value : 1;
            bool stain = !HasStain && Random.value < cfg.toiletDirtyChance;
            bool clog = !HasClog && day >= cfg.toiletClogFromDay && Random.value < cfg.toiletVisitClogChance;
            if (!stain && !clog) return false;
            bool wasStain = HasStain, wasClog = HasClog;
            ServerSetDirty(wasStain || stain, wasClog || clog, log);
            return true;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            RefreshView();
        }

        void ServerTick() { ServerTickMess(); }

        // ------------------------------------------------------------ список задач и вид

        void CollectTask(List<TaskLine> into)
        {
            if (!Dirty) return;
            string what = HasStain && HasClog ? "грязь, засор" : HasClog ? "засор" : "грязь";
            string plumber = HasClog && PlumberPending ? $" · сантехник {TaskBoard.Fmt(PlumberLeft)}" : "";
            var icons = new List<ItemType>();
            if (HasStain) icons.Add(ItemType.Mop);
            if (HasClog) icons.Add(ItemType.Plunger);
            into.Add(new TaskLine($"Туалет: {what}{plumber}", Guest.WaitingCount() > 0, icons.ToArray()));
        }

        void RefreshView()
        {
            int key = (HasStain ? 1 : 0) | ((HasClog ? 1 : 0) << 1) | (MopPct.Value << 2) | (PlungePct.Value << 9) | ((PlumberPending ? 1 : 0) << 16);
            if (key == lastKey) return;
            lastKey = key;
            RefreshMessVisuals();
            int st = HasClog ? 2 : HasStain ? 1 : 0;
            if (signStatus != null)
            {
                signStatus.text = st == 2 ? "ЗАСОР" : st == 1 ? "ГРЯЗНО" : "ЧИСТО";
                signStatus.color = st == 2 ? new Color(1f, 0.45f, 0.25f) : st == 1 ? new Color(1f, 0.9f, 0.5f) : new Color(0.4f, 0.95f, 0.45f);
            }
            if (signLamp != null && lampMats != null && st < lampMats.Length && lampMats[st] != null) signLamp.sharedMaterial = lampMats[st];
        }
    }
}
