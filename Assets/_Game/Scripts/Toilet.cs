using System.Collections.Generic;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// V3: общий туалет в лобби (in-scene NetworkObject). Не сдаётся и ключей не имеет — пачкается и засоряется так же, как номер
    /// (общая механика DirtySite: пятно — швабра, засор — вантуз или сантехник из магазина). Чистый туалет через toiletDirtyMin..Max
    /// секунд пачкается (пятно); со 2-го дня при этом может быть ещё и засор. Пока он грязный или засорён, терпение гостей у стойки
    /// тает в toiletPatienceMul раз быстрее (Guest). Задача — в списке TaskBoard (электронная таблица за ресепшеном).
    /// </summary>
    public class Toilet : DirtySite
    {
        public static Toilet Instance;

        static readonly string[] TestFlags = { "-guesttest", "-fighttest", "-hammertest", "-shoptest", "-roomtest", "-daytest" };
        static bool? autoDirty;
        /// <summary>Самотест: принудительно включить/выключить таймер загрязнения (null — по флагам запуска).</summary>
        public static bool? AutoOverride;
        /// <summary>Само пачкается по таймеру. Выключено в чужих самотестах (они считают задачи и терпение гостей), включено в -toilettest.</summary>
        public static bool AutoDirty
        {
            get
            {
                if (AutoOverride.HasValue) return AutoOverride.Value;
                if (autoDirty == null)
                {
                    bool off = false;
                    foreach (var f in TestFlags) off |= NetBootstrap.Arg(f);
                    autoDirty = !off || NetBootstrap.Arg("-toilettest");
                }
                return autoDirty.Value;
            }
        }

        public Transform inside;          // точка внутри у двери (проверка достижимости, самотест)
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
        float nextDirtyAt = -1f;
        bool wasDirty;
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

        float Interval()
        {
            var cfg = Cfg;
            float a = cfg != null ? cfg.toiletDirtyMin : 60f, b = cfg != null ? cfg.toiletDirtyMax : 90f;
            return Random.Range(a, Mathf.Max(a, b)) / Guest.TimeScale;
        }

        /// <summary>Сервер: туалет чистый, таймер заново (новая смена, «Заново»).</summary>
        public void ServerReset()
        {
            if (!IsServer || !IsSpawned) return;
            ResetMessProgress();
            StainVar.Value = false;
            ClogVar.Value = false;
            wasDirty = false;
            nextDirtyAt = -1f;
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

        /// <summary>Сервер: одно «загрязнение» как по таймеру — пятно, со 2-го дня иногда и засор (самотест гоняет это много раз).</summary>
        public void ServerRollDirty() { if (IsServer && IsSpawned) MakeDirty(false); }

        void MakeDirty(bool log = true)
        {
            var cfg = Cfg;
            var gs = GameState.Instance;
            int day = gs != null ? gs.Day.Value : 1;
            bool clog = cfg != null && day >= cfg.toiletClogFromDay && Random.value < cfg.toiletClogChance;
            ServerSetDirty(true, clog, log);
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            RefreshView();
        }

        void ServerTick()
        {
            ServerTickMess();
            var gs = GameState.Instance;
            if (gs == null || gs.Phase.Value != GamePhase.Playing) { nextDirtyAt = -1f; return; }
            bool dirty = Dirty;
            if (nextDirtyAt < 0f || (wasDirty && !dirty)) nextDirtyAt = Time.time + Interval();
            wasDirty = dirty;
            if (!dirty && AutoDirty && Time.time >= nextDirtyAt) MakeDirty();
        }

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
