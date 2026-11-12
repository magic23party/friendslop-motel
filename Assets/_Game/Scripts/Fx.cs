using System.Collections.Generic;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Визуальные эффекты отклика (P3), без сетевого кода: вызываются из RPC / реакций на NetworkVariable на каждой машине.
    /// Hit — искры и вспышка удара, Money — всплывающее «+50 $» с кассовым звуком, тряска камеры — CameraShake (только у владельца).
    /// Объекты пулятся (примитивы с общим материалом из GameConfig + MaterialPropertyBlock для цвета), ничего не аллоцируется на лету.
    /// </summary>
    public static class Fx
    {
        static FxHost host;
        static FxHost Host
        {
            get
            {
                if (host == null)
                {
                    var go = new GameObject("Fx");
                    Object.DontDestroyOnLoad(go);
                    host = go.AddComponent<FxHost>();
                }
                return host;
            }
        }

        public static GameConfig Cfg => GameConfig.Active;

        /// <summary>Искры и вспышка в точке удара.</summary>
        public static void Hit(Vector3 p)
        {
            if (Cfg == null || Cfg.fxChipMat == null || Application.isBatchMode) return;
            Host.SpawnHit(p);
        }

        /// <summary>Всплывашка денег в мире: delta &gt; 0 — зелёная «+50 $» и звук кассы, &lt; 0 — красная штрафная.</summary>
        public static void Money(int delta, Vector3 p)
        {
            if (delta == 0) return;
            if (delta > 0) Sfx.Play3D(Snd.Cash, p, 0.9f);
            else Sfx.Play3D(Snd.Fine, p, 0.9f);
            if (Cfg == null || Cfg.fxLabelMat == null || Application.isBatchMode) return;
            Host.SpawnText((delta > 0 ? "+" : "-") + Mathf.Abs(delta) + " $", p, delta > 0 ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 0.4f, 0.35f));
        }

        /// <summary>Камера, на которую смотрит игрок сейчас (для поворота всплывашек).</summary>
        public static Camera ViewCam()
        {
            var c = PlayerController.LocalCamera;
            if (c != null && c.enabled) return c;
            c = Camera.main;
            if (c != null) return c;
            foreach (var cam in Camera.allCameras) if (cam != null && cam.enabled) return cam;
            return null;
        }
    }

    /// <summary>Тряска камеры владельца: Add — «травма» 0..1, PlayerController применяет её к камере каждый кадр.</summary>
    public static class CameraShake
    {
        static float trauma;

        public static void Add(float t)
        {
            var cfg = GameConfig.Active;
            float k = cfg != null ? cfg.shakeScale : 1f;
            trauma = Mathf.Clamp01(trauma + t * k);
        }

        public static void Apply(Transform cam, float dt)
        {
            trauma = Mathf.Max(0f, trauma - dt * 1.7f);
            if (trauma <= 0.002f)
            {
                cam.localPosition = Vector3.zero;
                cam.localRotation = Quaternion.identity;
                return;
            }
            float s = trauma * trauma;
            float t = Time.unscaledTime * 31f;
            float nx = Mathf.PerlinNoise(t, 0.3f) - 0.5f, ny = Mathf.PerlinNoise(t, 7.7f) - 0.5f, nz = Mathf.PerlinNoise(t, 15.1f) - 0.5f;
            cam.localRotation = Quaternion.Euler(nx * 12f * s, ny * 12f * s, nz * 14f * s);
            cam.localPosition = new Vector3(ny, nx, 0f) * 0.12f * s;
        }
    }

    /// <summary>Аниматор пулов эффектов (один на игру, переживает смену сцен).</summary>
    public class FxHost : MonoBehaviour
    {
        class Chip
        {
            public Transform t; public Renderer r;
            public Vector3 v, spin; public float age, life, size; public bool active, flash;
        }

        class FloatText
        {
            public Transform t; public TextMesh main, shadow;
            public Vector3 start; public float age, life; public bool active; public Color color;
        }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        readonly List<Chip> chips = new List<Chip>();
        readonly List<FloatText> texts = new List<FloatText>();
        MaterialPropertyBlock mpb;
        Font font;

        const int ChipPool = 48, TextPool = 8;

        void Awake() { mpb = new MaterialPropertyBlock(); }

        Chip NewChip(bool sphere)
        {
            var go = GameObject.CreatePrimitive(sphere ? PrimitiveType.Sphere : PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Fx.Cfg.fxChipMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            var c = new Chip { t = go.transform, r = r, flash = sphere };
            chips.Add(c);
            return c;
        }

        Chip Take(bool sphere)
        {
            foreach (var c in chips) if (!c.active && c.flash == sphere) return c;
            if (chips.Count < ChipPool) return NewChip(sphere);
            return null;
        }

        void Paint(Chip c, Color col, float emission)
        {
            mpb.Clear();
            mpb.SetColor(BaseColor, col);
            mpb.SetColor(EmissionColor, col * emission);
            c.r.SetPropertyBlock(mpb);
        }

        public void SpawnHit(Vector3 p)
        {
            var f = Take(true);
            if (f != null)
            {
                f.active = true; f.age = 0f; f.life = 0.18f; f.size = 0.34f;
                f.t.position = p; f.t.localScale = Vector3.one * 0.1f; f.t.gameObject.SetActive(true);
                Paint(f, new Color(1f, 0.93f, 0.6f), 1.6f);
            }
            for (int i = 0; i < 12; i++)
            {
                var c = Take(false);
                if (c == null) break;
                c.active = true; c.age = 0f; c.life = Random.Range(0.45f, 0.75f);
                c.size = Random.Range(0.045f, 0.085f);
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.9f + 0.25f;
                c.v = dir.normalized * Random.Range(2.2f, 5.0f);
                c.spin = Random.insideUnitSphere * 540f;
                c.t.position = p; c.t.rotation = Random.rotation; c.t.localScale = Vector3.one * c.size;
                c.t.gameObject.SetActive(true);
                Paint(c, i % 3 == 0 ? new Color(1f, 0.95f, 0.75f) : i % 3 == 1 ? new Color(1f, 0.78f, 0.25f) : new Color(1f, 0.5f, 0.2f), 2.2f);
            }
        }

        public void SpawnText(string text, Vector3 p, Color col)
        {
            FloatText ft = null;
            foreach (var x in texts) if (!x.active) { ft = x; break; }
            if (ft == null)
            {
                if (texts.Count >= TextPool) ft = texts[0];
                else
                {
                    if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    ft = new FloatText();
                    var go = new GameObject("FloatText");
                    go.transform.SetParent(transform, false);
                    ft.t = go.transform;
                    ft.main = MakeText(go.transform, "Main", font);
                    ft.shadow = MakeText(go.transform, "Shadow", font);
                    texts.Add(ft);
                }
            }
            ft.active = true; ft.age = 0f; ft.life = Fx.Cfg != null ? Fx.Cfg.floatTextLife : 1.5f; ft.start = p; ft.color = col;
            ft.main.text = text; ft.shadow.text = text;
            ft.t.position = p;
            ft.t.gameObject.SetActive(true);
        }

        TextMesh MakeText(Transform parent, string name, Font f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = f;
            tm.fontSize = 64;
            tm.characterSize = 0.1f;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            go.GetComponent<MeshRenderer>().sharedMaterial = Fx.Cfg.fxLabelMat;
            return tm;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var c in chips)
            {
                if (!c.active) continue;
                c.age += dt;
                if (c.age >= c.life) { c.active = false; c.t.gameObject.SetActive(false); continue; }
                float k = c.age / c.life;
                if (c.flash)
                {
                    c.t.localScale = Vector3.one * (c.size * Mathf.Sin(Mathf.Clamp01(k * 0.9f + 0.1f) * Mathf.PI));
                    continue;
                }
                c.v.y -= 9.8f * dt;
                var pos = c.t.position + c.v * dt;
                if (pos.y < 0.03f) { pos.y = 0.03f; c.v.y *= -0.35f; c.v.x *= 0.6f; c.v.z *= 0.6f; }
                c.t.position = pos;
                c.t.Rotate(c.spin * dt, Space.Self);
                c.t.localScale = Vector3.one * (c.size * (1f - k * k));
            }

            var cam = Fx.ViewCam();
            foreach (var x in texts)
            {
                if (!x.active) continue;
                x.age += dt;
                if (x.age >= x.life) { x.active = false; x.t.gameObject.SetActive(false); continue; }
                float k = x.age / x.life;
                float rise = 1f - (1f - k) * (1f - k);                       // быстро всплывает, затухает подъём
                x.t.position = x.start + Vector3.up * (rise * 1.1f);
                float pop = k < 0.12f ? Mathf.Lerp(0.4f, 1.25f, k / 0.12f) : k < 0.25f ? Mathf.Lerp(1.25f, 1f, (k - 0.12f) / 0.13f) : 1f;
                x.t.localScale = Vector3.one * pop;
                float a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
                x.main.color = new Color(x.color.r, x.color.g, x.color.b, a);
                x.shadow.color = new Color(0f, 0f, 0f, a * 0.9f);
                if (cam != null)
                {
                    var look = x.t.position - cam.transform.position;
                    if (look.sqrMagnitude > 0.001f) x.t.rotation = Quaternion.LookRotation(look);
                    x.shadow.transform.localPosition = new Vector3(0.025f, -0.025f, 0.02f);
                }
            }
        }
    }

    /// <summary>«Звёздочки» над оглушённым: вращаются над головой стоящего или у головы лежащего (lie 0..1).</summary>
    public class StunStars : MonoBehaviour
    {
        const int N = 4;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        readonly Transform[] stars = new Transform[N];
        float t, vis;
        bool built;

        public static StunStars Attach(Transform host)
        {
            var go = new GameObject("Stars");
            go.transform.SetParent(host, false);
            return go.AddComponent<StunStars>();
        }

        void Build()
        {
            built = true;
            var cfg = Fx.Cfg;
            if (cfg == null || cfg.fxChipMat == null) return;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColor, new Color(1f, 0.85f, 0.2f));
            mpb.SetColor(EmissionColor, new Color(1f, 0.8f, 0.15f) * 2.5f);
            for (int i = 0; i < N; i++)
            {
                var star = new GameObject("Star" + i).transform;
                star.SetParent(transform, false);
                // «звезда» — три скрещённые тонкие пластины
                for (int a = 0; a < 3; a++)
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(p.GetComponent<Collider>());
                    p.transform.SetParent(star, false);
                    p.transform.localScale = new Vector3(0.22f, 0.05f, 0.05f);
                    p.transform.localRotation = Quaternion.Euler(0f, 0f, a * 60f);
                    var r = p.GetComponent<Renderer>();
                    r.sharedMaterial = cfg.fxChipMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.SetPropertyBlock(mpb);
                }
                stars[i] = star;
            }
        }

        /// <summary>Вызывать каждый кадр: on — оглушён, lie — как сильно лежит (0 стоит, 1 лежит).</summary>
        public void Tick(bool on, float lie, float dt)
        {
            vis = Mathf.MoveTowards(vis, on ? 1f : 0f, dt * 5f);
            if (vis <= 0.001f)
            {
                if (built && stars[0] != null && stars[0].gameObject.activeSelf) foreach (var s in stars) s.gameObject.SetActive(false);
                return;
            }
            if (!built) Build();
            if (stars[0] == null) return;
            t += dt;
            // стоя — над головой, лёжа — у головы (тело ложится назад: голова в −Z, выше пола на ~0.5 м)
            var center = Vector3.Lerp(new Vector3(0f, 2.05f, 0f), new Vector3(0f, 1.0f, -1.45f), lie);
            for (int i = 0; i < N; i++)
            {
                var s = stars[i];
                if (!s.gameObject.activeSelf) s.gameObject.SetActive(true);
                float ang = t * 3.2f + i * (Mathf.PI * 2f / N);
                s.localPosition = center + new Vector3(Mathf.Cos(ang) * 0.34f, Mathf.Sin(t * 5f + i) * 0.05f, Mathf.Sin(ang) * 0.34f);
                s.localRotation = Quaternion.Euler(0f, t * 220f + i * 40f, 0f);
                s.localScale = Vector3.one * vis;
            }
        }
    }
}
