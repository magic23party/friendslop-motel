using System.Collections.Generic;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Свет дня и ночи (V1, in-scene, только визуал). Фаза и номер дня — серверные NetworkVariable в GameState;
    /// здесь каждый клиент по ним (и по ServerTime) считает «темноту» 0 (день)..1 (ночь) и плавно ведёт солнце/луну, ambient, туман,
    /// фон камеры, уличные фонари, неон вывески и стёкла. Лобби и Game Over — ночной вид (как в прототипе).
    /// Сумерки: последние Twilight секунд дня темнеют, последние секунды ночи светлеют (рассвет).
    /// Внутри мотеля ночью свет такой же, как был (лампы-плафоны на 100%); днём лампы тусклее, ambient выше.
    /// </summary>
    public class DayNight : MonoBehaviour
    {
        public static DayNight Instance;

        static readonly Color DaySky = new Color(0.46f, 0.69f, 0.93f);
        static readonly Color DayFog = new Color(0.68f, 0.81f, 0.92f);
        static readonly Color DayAmbient = new Color(0.60f, 0.62f, 0.68f);
        static readonly Color SunColor = new Color(1.00f, 0.95f, 0.82f);
        static readonly Quaternion SunRot = Quaternion.Euler(58f, -28f, 0f);
        static readonly Quaternion MoonRot = Quaternion.Euler(38f, -28f, 0f);
        const float SunIntensity = 2.7f, MoonIntensity = 2.0f;
        const float MaxSpeed = 0.35f;                 // макс. скорость смены света, 1/с (старт смены: ночь -> утро за ~3 с)

        /// <summary>Темнота 0 (день)..1 (ночь).</summary>
        public float Dark { get; private set; } = 1f;
        float forced = -1f;                            // >=0: ShotRunner/отладка держит свет фиксированным

        Light sun;
        enum Kind { Indoor, Outdoor }
        struct LightRef { public Light l; public float baseI; public Kind kind; }
        readonly List<LightRef> lights = new List<LightRef>();

        class MatRef { public Material m; public Color baseCol, emit; public bool hasBase; }
        readonly Dictionary<string, MatRef> mats = new Dictionary<string, MatRef>();
        static readonly string[] MatNames = { "NeonRed", "NeonTeal", "StreetHead", "PaneNight", "MoonDisc", "Plafon" };

        Camera[] camBuf = new Camera[8];
        float nextCamApply, lastApplied = -1f;

        void Awake() { Instance = this; }

        void Start()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { if (sun == null) sun = l; continue; }
                string pn = l.transform.parent != null ? l.transform.parent.name : "";
                bool outdoor = l.name == "SignGlow" || l.name == "HeadGlow" || pn == "StreetLamp";
                lights.Add(new LightRef { l = l, baseI = l.intensity, kind = outdoor ? Kind.Outdoor : Kind.Indoor });
            }
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++)
                {
                    var m = sm[i];
                    if (m == null || mats.ContainsKey(m.name) || System.Array.IndexOf(MatNames, m.name) < 0) continue;
                    var mr = new MatRef { m = m, emit = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black, hasBase = m.HasProperty("_BaseColor") };
                    if (mr.hasBase) mr.baseCol = m.GetColor("_BaseColor");
                    mats[m.name] = mr;
                }
            }
            lastApplied = -1f;
            Apply(Dark);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var mr in mats.Values)       // материалы общие и живут между сценами: вернуть как было
            {
                if (mr.m == null) continue;
                mr.m.SetColor("_EmissionColor", mr.emit);
                if (mr.hasBase) mr.m.SetColor("_BaseColor", mr.baseCol);
            }
        }

        /// <summary>Отладка/скриншоты: зафиксировать свет (0 день .. 1 ночь) мгновенно; отрицательное значение — вернуть управление фазой.</summary>
        public void Force(float dark)
        {
            forced = dark;
            if (dark >= 0f) { Dark = dark; Apply(Dark); }
        }

        float Target(GameState gs)
        {
            if (forced >= 0f) return forced;
            if (gs == null || !gs.IsSpawned || gs.Phase.Value != GamePhase.Playing) return 1f;
            float tw = Mathf.Max(0.01f, gs.Twilight);
            float k = Mathf.Clamp01(1f - gs.PhaseLeft / tw);        // 0..1 в последние tw секунд фазы
            k = k * k * (3f - 2f * k);
            return gs.Night.Value ? 1f - k : k;                      // ночь: рассвет в конце; день: сумерки в конце
        }

        void Update()
        {
            if (forced < 0f) Dark = Mathf.MoveTowards(Dark, Target(GameState.Instance), MaxSpeed * Time.deltaTime);
            if (!Mathf.Approximately(Dark, lastApplied) || Time.unscaledTime >= nextCamApply) Apply(Dark);
        }

        void Apply(float t)
        {
            bool changed = !Mathf.Approximately(t, lastApplied);
            lastApplied = t;
            if (Time.unscaledTime >= nextCamApply || changed)
            {
                nextCamApply = Time.unscaledTime + 0.5f;                 // камеры игроков появляются позже сцены — красим фон периодически
                var sky = Color.Lerp(DaySky, Palette.Night, t);
                int n = Camera.GetAllCameras(camBuf);
                for (int i = 0; i < n; i++) if (camBuf[i].clearFlags == CameraClearFlags.SolidColor) camBuf[i].backgroundColor = sky;
            }
            if (!changed) return;

            if (sun != null)
            {
                sun.color = Color.Lerp(SunColor, Palette.Moon, t);
                sun.intensity = Mathf.Lerp(SunIntensity, MoonIntensity, t);
                sun.transform.rotation = Quaternion.Slerp(SunRot, MoonRot, t);
            }
            RenderSettings.ambientLight = Color.Lerp(DayAmbient, Palette.Ambient, t);
            RenderSettings.fogColor = Color.Lerp(DayFog, Palette.Night, t);
            RenderSettings.fogDensity = Mathf.Lerp(0.0016f, 0.003f, t);

            float night2 = t * t;                                       // фонари/неон разгораются ближе к полной ночи
            float indoor = Mathf.Lerp(0.6f, 1f, t);                     // внутри днём лампы тусклее, ночью — как раньше
            for (int i = 0; i < lights.Count; i++)
            {
                var r = lights[i];
                if (r.l == null) continue;
                r.l.intensity = r.baseI * (r.kind == Kind.Outdoor ? night2 : indoor);
            }

            Mat("NeonRed", night2 * 0.95f + 0.05f);
            Mat("NeonTeal", night2 * 0.95f + 0.05f);
            Mat("StreetHead", night2 * 0.9f + 0.1f);
            Mat("Plafon", Mathf.Lerp(0.45f, 1f, t));

            if (mats.TryGetValue("PaneNight", out var pane) && pane.m != null)       // стёкла окон: небо днём, тёмная синева ночью
            {
                if (pane.hasBase) pane.m.SetColor("_BaseColor", Color.Lerp(new Color(0.42f, 0.66f, 0.92f), pane.baseCol, t));
                pane.m.SetColor("_EmissionColor", Color.Lerp(new Color(0.50f, 0.72f, 0.98f) * 0.9f, pane.emit, t));
            }
            if (mats.TryGetValue("MoonDisc", out var disc) && disc.m != null)         // диск: луна ночью, солнце днём
            {
                var dayEmit = new Color(1.00f, 0.88f, 0.55f) * 3.0f;
                if (disc.hasBase) disc.m.SetColor("_BaseColor", Color.Lerp(new Color(1f, 0.95f, 0.7f), disc.baseCol, t));
                disc.m.SetColor("_EmissionColor", Color.Lerp(dayEmit, disc.emit, t));
            }
        }

        void Mat(string name, float k)
        {
            if (mats.TryGetValue(name, out var mr) && mr.m != null) mr.m.SetColor("_EmissionColor", mr.emit * k);
        }
    }
}
