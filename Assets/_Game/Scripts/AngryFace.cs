using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// V6: злой смайлик над головой гостя и подпись «−50% $» / «−100% $». Рисуется на каждой машине по NetworkVariable Guest.PayCut
    /// (примитивы с общим материалом из GameConfig + MaterialPropertyBlock, как «звёздочки» StunStars). Разворачивается к камере.
    /// В batchmode подпись (TextMesh) не создаётся — Caption хранит текст для самотестов.
    /// </summary>
    public class AngryFace : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        const float FaceSize = 0.62f;       // 0,52 м головы × 0,62 ≈ 0,32 м
        Transform face;          // смайлик (лицо — на стороне −Z, как у надписей)
        TextMesh label, shadow;
        Transform labelRoot;
        float vis;
        bool built;

        /// <summary>Текст подписи, который сейчас должен быть виден ("" — смайлика нет).</summary>
        public string Caption { get; private set; } = "";
        public bool Shown => vis > 0.01f && Caption != "";

        public static AngryFace Attach(Transform host)
        {
            var go = new GameObject("AngryFace");
            go.transform.SetParent(host, false);
            return go.AddComponent<AngryFace>();
        }

        Renderer Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color col, float emission, Vector3? euler = null)
        {
            var cfg = Fx.Cfg;
            var p = GameObject.CreatePrimitive(type);
            Destroy(p.GetComponent<Collider>());
            p.name = name;
            p.transform.SetParent(face, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            if (euler.HasValue) p.transform.localRotation = Quaternion.Euler(euler.Value);
            var r = p.GetComponent<Renderer>();
            r.sharedMaterial = cfg.fxChipMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColor, col);
            mpb.SetColor(EmissionColor, col * emission);
            r.SetPropertyBlock(mpb);
            return r;
        }

        void Build()
        {
            built = true;
            var cfg = Fx.Cfg;
            if (cfg == null || cfg.fxChipMat == null) return;
            face = new GameObject("Face").transform;
            face.SetParent(transform, false);
            face.localPosition = new Vector3(0f, 2.83f, 0f);        // потолок на 3,0 м: смайлик целиком ниже
            var skin = new Color(1f, 0.30f, 0.16f);
            var dark = new Color(0.12f, 0.03f, 0.03f);
            Part(PrimitiveType.Sphere, "Head", Vector3.zero, Vector3.one * 0.52f, skin, 1.1f);
            Part(PrimitiveType.Sphere, "EyeL", new Vector3(-0.105f, 0.03f, -0.225f), Vector3.one * 0.085f, Color.white, 0.6f);
            Part(PrimitiveType.Sphere, "EyeR", new Vector3(0.105f, 0.03f, -0.225f), Vector3.one * 0.085f, Color.white, 0.6f);
            Part(PrimitiveType.Sphere, "PupilL", new Vector3(-0.095f, 0.025f, -0.262f), Vector3.one * 0.04f, dark, 0f);
            Part(PrimitiveType.Sphere, "PupilR", new Vector3(0.095f, 0.025f, -0.262f), Vector3.one * 0.04f, dark, 0f);
            // брови домиком вниз к переносице (злые)
            Part(PrimitiveType.Cube, "BrowL", new Vector3(-0.11f, 0.115f, -0.235f), new Vector3(0.15f, 0.035f, 0.03f), dark, 0f, new Vector3(0f, 0f, -26f));
            Part(PrimitiveType.Cube, "BrowR", new Vector3(0.11f, 0.115f, -0.235f), new Vector3(0.15f, 0.035f, 0.03f), dark, 0f, new Vector3(0f, 0f, 26f));
            // рот-«подкова» вниз
            Part(PrimitiveType.Cube, "MouthMid", new Vector3(0f, -0.085f, -0.24f), new Vector3(0.11f, 0.03f, 0.03f), dark, 0f);
            Part(PrimitiveType.Cube, "MouthL", new Vector3(-0.075f, -0.11f, -0.235f), new Vector3(0.07f, 0.03f, 0.03f), dark, 0f, new Vector3(0f, 0f, 35f));
            Part(PrimitiveType.Cube, "MouthR", new Vector3(0.075f, -0.11f, -0.235f), new Vector3(0.07f, 0.03f, 0.03f), dark, 0f, new Vector3(0f, 0f, -35f));

            if (!Application.isBatchMode && cfg.fxLabelMat != null)
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var root = new GameObject("PayCutText");
                root.transform.SetParent(transform, false);
                root.transform.localPosition = new Vector3(0f, 2.57f, 0f);
                labelRoot = root.transform;
                shadow = MakeText(labelRoot, "Shadow", font, new Color(0f, 0f, 0f, 0.9f), new Vector3(0.02f, -0.02f, 0.015f));
                label = MakeText(labelRoot, "Main", font, new Color(1f, 0.22f, 0.16f), Vector3.zero);
            }
        }

        TextMesh MakeText(Transform parent, string name, Font font, Color col, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 64;
            tm.characterSize = 0.038f;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = col;
            go.GetComponent<MeshRenderer>().sharedMaterial = Fx.Cfg.fxLabelMat;
            return tm;
        }

        /// <summary>Каждый кадр (из Guest.LateUpdate): on — показывать, caption — подпись, cam — куда разворачивать.</summary>
        public void Tick(bool on, string caption, Camera cam, float dt)
        {
            if (on) Caption = caption;
            vis = Mathf.MoveTowards(vis, on ? 1f : 0f, dt * 6f);
            if (vis <= 0.001f)
            {
                if (!on) Caption = "";
                if (built && face != null && face.gameObject.activeSelf)
                {
                    face.gameObject.SetActive(false);
                    if (labelRoot != null) labelRoot.gameObject.SetActive(false);
                }
                return;
            }
            if (!built) Build();
            if (face == null) return;
            if (!face.gameObject.activeSelf) { face.gameObject.SetActive(true); if (labelRoot != null) labelRoot.gameObject.SetActive(true); }
            float bounce = 1f + 0.08f * Mathf.Sin(Time.time * 9f);      // чуть «кипит»
            face.localScale = Vector3.one * (FaceSize * vis * bounce);
            if (label != null)
            {
                if (label.text != Caption) { label.text = Caption; shadow.text = Caption; }
                labelRoot.localScale = Vector3.one * vis;
            }
            if (cam != null)
            {
                var look = transform.position + new Vector3(0f, 2.8f, 0f) - cam.transform.position;
                if (look.sqrMagnitude > 0.001f)
                {
                    var rot = Quaternion.LookRotation(look);
                    face.rotation = rot;
                    if (labelRoot != null) labelRoot.rotation = rot;
                }
            }
        }
    }
}
