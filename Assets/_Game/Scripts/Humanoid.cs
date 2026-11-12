using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Процедурный low-poly человечек (P2). Иерархию из примитивов собирает Builder (без коллайдеров, дочерний узел «Model»).
    /// Рантайм: цвета по роли (униформа игрока / вид гостя по сиду), аксессуары, ходьба/покой/поза с предметом/взмах/удар.
    /// Падение (лежит) делает родительский узел Visual (PlayerCombat / Guest) — здесь только поза конечностей.
    /// </summary>
    public class Humanoid : MonoBehaviour
    {
        /// <summary>Наклон правой руки с предметом (градусы вокруг X, минус — вперёд).</summary>
        public const float CarryPitch = -65f;

        public Transform head, armL, armR, legL, legR;
        public Transform handR;                       // точка в правой руке (HandWorld): сюда ставится предмет у чужих копий
        public Renderer[] shirtR, pantsR, skinR, hairR, hatR, caseR;
        public GameObject hatCap, hatFedora, hatBeanie, glasses, suitcase, hairLong, badge;

        // --- ввод анимации (выставляют PlayerHands / PlayerCombat / Guest)
        public bool carry;          // предмет в правой руке: рука вперёд
        public bool armsUp;         // обе руки вверх (драка)
        public float swing;         // добавка к наклону правой руки, градусы (мах кувалдой)
        public float punch;         // 0..1 выпад правой рукой
        public float lie;           // 0..1 лежит (поза конечностей)

        public Color ShirtColor { get; private set; } = new Color(0.8f, 0.2f, 0.2f);
        public Color SkinColor { get; private set; } = new Color(0.93f, 0.74f, 0.58f);

        Color pantsC = new Color(0.16f, 0.2f, 0.32f), hairC = new Color(0.2f, 0.12f, 0.07f), hatC = Color.white;
        Transform root;
        Vector3 lastPos;
        float speed, phase, idle, flash = -1f;
        bool inited;
        static MaterialPropertyBlock mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color FlashColor = new Color(1f, 0.15f, 0.1f);

        // ------------------------------------------------------------ цвета и вид

        static readonly Color[] Shirts =
        {
            new Color(0.80f, 0.20f, 0.18f), new Color(0.92f, 0.70f, 0.16f), new Color(0.20f, 0.35f, 0.75f), new Color(0.22f, 0.60f, 0.32f),
            new Color(0.58f, 0.30f, 0.75f), new Color(0.95f, 0.55f, 0.20f), new Color(0.88f, 0.88f, 0.82f), new Color(0.30f, 0.32f, 0.36f),
            new Color(0.92f, 0.52f, 0.68f), new Color(0.14f, 0.64f, 0.63f),
        };
        static readonly Color[] Pants =
        {
            new Color(0.17f, 0.23f, 0.40f), new Color(0.12f, 0.12f, 0.14f), new Color(0.55f, 0.48f, 0.32f),
            new Color(0.28f, 0.20f, 0.14f), new Color(0.35f, 0.36f, 0.40f),
        };
        public static readonly Color[] Skins =
        {
            new Color(0.99f, 0.86f, 0.72f), new Color(0.93f, 0.74f, 0.58f), new Color(0.80f, 0.60f, 0.42f),
            new Color(0.63f, 0.43f, 0.28f), new Color(0.45f, 0.29f, 0.19f), new Color(0.32f, 0.20f, 0.14f),
        };
        static readonly Color[] Hairs =
        {
            new Color(0.08f, 0.07f, 0.07f), new Color(0.25f, 0.15f, 0.08f), new Color(0.85f, 0.70f, 0.35f),
            new Color(0.70f, 0.30f, 0.12f), new Color(0.62f, 0.62f, 0.64f), new Color(0.40f, 0.26f, 0.14f),
        };
        static readonly Color[] Hats =
        {
            new Color(0.78f, 0.16f, 0.18f), new Color(0.12f, 0.17f, 0.35f), new Color(0.92f, 0.70f, 0.16f),
            new Color(0.16f, 0.46f, 0.25f), new Color(0.12f, 0.12f, 0.14f), new Color(0.94f, 0.90f, 0.78f),
        };
        static readonly Color[] Cases =
        {
            new Color(0.60f, 0.30f, 0.16f), new Color(0.14f, 0.34f, 0.50f), new Color(0.50f, 0.12f, 0.16f),
            new Color(0.22f, 0.22f, 0.25f), new Color(0.14f, 0.50f, 0.46f), new Color(0.80f, 0.60f, 0.15f),
        };

        struct Rng
        {
            uint s;
            public Rng(int seed)
            {
                s = (uint)seed * 2654435761u ^ 0x9E3779B9u;
                if (s == 0) s = 1;
                for (int i = 0; i < 6; i++) Next();
            }
            public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
            public float F() => (Next() & 0xFFFFFF) / 16777216f;
            public int I(int n) => Mathf.Min(n - 1, (int)(F() * n));
        }

        /// <summary>Вид гостя целиком по сиду (все машины получают один сид → один и тот же гость). Неплательщик от других не отличается.</summary>
        public void ApplyLook(int seed)
        {
            var r = new Rng(seed);
            float h = Mathf.Lerp(0.90f, 1.10f, r.F());
            float b = Mathf.Lerp(0.88f, 1.28f, r.F());
            transform.localScale = new Vector3(b, h, Mathf.Lerp(1f, b, 0.6f));
            if (head != null) head.localScale = new Vector3(Mathf.Lerp(1f, 1f / b, 0.7f), 1f, Mathf.Lerp(1f, 1f / b, 0.7f));
            var shirt = Shirts[r.I(Shirts.Length)];
            var pants = Pants[r.I(Pants.Length)];
            var skin = Skins[r.I(Skins.Length)];
            var hair = Hairs[r.I(Hairs.Length)];
            var hat = Hats[r.I(Hats.Length)];
            int hatKind = r.F() < 0.55f ? 1 + r.I(3) : 0;           // 0 нет, 1 кепка, 2 шляпа, 3 шапка
            bool glassesOn = r.F() < 0.38f;
            bool caseOn = r.F() < 0.55f;
            bool longHair = r.F() < 0.35f;
            var caseCol = Cases[r.I(Cases.Length)];
            SetColors(shirt, pants, skin, hair, hat);
            if (hatCap != null) hatCap.SetActive(hatKind == 1);
            if (hatFedora != null) hatFedora.SetActive(hatKind == 2);
            if (hatBeanie != null) hatBeanie.SetActive(hatKind == 3);
            if (glasses != null) glasses.SetActive(glassesOn);
            if (suitcase != null) suitcase.SetActive(caseOn);
            if (hairLong != null) hairLong.SetActive(longHair);
            if (badge != null) badge.SetActive(false);
            Paint(caseR, caseCol);
        }

        /// <summary>Игрок: униформа своего цвета, кепка в цвет, бейдж; кожа и волосы по номеру клиента.</summary>
        public void ApplyPlayer(int index, Color uniform)
        {
            var r = new Rng(index + 101);
            SetColors(uniform, new Color(0.14f, 0.17f, 0.26f), Skins[(index * 2 + 2) % Skins.Length], Hairs[r.I(Hairs.Length)], uniform * 0.8f);
            if (hatCap != null) hatCap.SetActive(true);
            if (badge != null) badge.SetActive(true);
            if (suitcase != null) suitcase.SetActive(false);
        }

        void SetColors(Color shirt, Color pants, Color skin, Color hair, Color hat)
        {
            ShirtColor = shirt; pantsC = pants; SkinColor = skin; hairC = hair; hatC = hat;
            Repaint(0f);
        }

        void Repaint(float f)
        {
            Paint(shirtR, Color.Lerp(ShirtColor, FlashColor, f));
            Paint(skinR, Color.Lerp(SkinColor, FlashColor, f));
            Paint(pantsR, pantsC);
            Paint(hairR, hairC);
            Paint(hatR, hatC);
        }

        public static void Paint(Renderer[] rs, Color c)
        {
            if (rs == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.Clear();
            mpb.SetColor(BaseColor, c);
            foreach (var r in rs) if (r != null) r.SetPropertyBlock(mpb);
        }

        /// <summary>Вспышка попадания: 0..1 — доля красного на коже и одежде.</summary>
        public void SetFlash(float f)
        {
            f = Mathf.Clamp01(f);
            if (Mathf.Approximately(f, flash)) return;
            flash = f;
            Repaint(f);
        }

        // ------------------------------------------------------------ анимация

        void OnEnable()
        {
            var no = GetComponentInParent<Unity.Netcode.NetworkObject>();
            root = no != null ? no.transform : transform.root;
            lastPos = root.position;
            inited = true;
        }

        void LateUpdate()
        {
            if (!inited) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            var p = root.position;
            var d = p - lastPos; d.y = 0f;
            lastPos = p;
            float sp = d.magnitude / dt;
            if (sp > 12f) sp = 0f;                                         // телепорт, не ходьба
            speed = Mathf.Lerp(speed, sp, 1f - Mathf.Exp(-12f * dt));
            float walk = lie > 0.05f ? 0f : Mathf.Clamp01(speed / 1.6f);
            phase += speed * dt * 4.2f;
            idle += dt;

            float legA = Mathf.Sin(phase) * 38f * walk;
            float armA = Mathf.Sin(phase) * 32f * walk;
            float sway = Mathf.Sin(idle * 1.6f) * 2.5f * (1f - walk);

            if (legL != null) legL.localRotation = Quaternion.Euler(legA, 0f, -lie * 6f);
            if (legR != null) legR.localRotation = Quaternion.Euler(-legA, 0f, lie * 6f);

            // левая рука: ходьба / вверх в драке / лежит; чемодан оттягивает руку
            float lPitch = -armA + sway;
            float lRoll = suitcase != null && suitcase.activeSelf ? -5f : 0f;
            if (armsUp) lPitch = -75f + Mathf.Sin(idle * 14f) * 10f;
            if (armL != null) armL.localRotation = Quaternion.Euler(lPitch, 0f, lRoll - lie * 35f);

            // правая рука: ходьба / предмет / вверх / выпад / мах
            float rPitch = armA - sway;
            if (carry) rPitch = CarryPitch + Mathf.Sin(phase * 2f) * 2f * walk;
            if (armsUp) rPitch = -75f + Mathf.Sin(idle * 14f + 2f) * 10f;
            rPitch += swing - punch * 70f;
            if (armR != null) armR.localRotation = Quaternion.Euler(rPitch, 0f, lie * 35f);

            if (head != null) head.localRotation = Quaternion.Euler(Mathf.Sin(idle * 0.9f) * 1.5f, 0f, 0f);
        }
    }
}
