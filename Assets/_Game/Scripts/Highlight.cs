using System.Collections.Generic;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Подсветка объекта под прицелом (P3), только у владельца: общие материалы не трогаем —
    /// каждому рендереру цели ставится MaterialPropertyBlock (тёплая пульсирующая добавка к цвету + emission).
    /// Люди (Humanoid) красятся через собственный блок Humanoid.SetHighlight (чтобы не ломать вспышку попадания).
    /// </summary>
    public static class Highlight
    {
        const int MaxRenderers = 40;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly Color Tint = new Color(1f, 0.88f, 0.45f);

        static Component cur;
        static Humanoid hum;
        static readonly List<Renderer> rs = new List<Renderer>();
        static readonly List<Color> orig = new List<Color>();
        static MaterialPropertyBlock mpb;

        public static bool Active => cur != null;

        /// <summary>Цель подсветки (null или on=false — снять). Дёшево вызывать каждый кадр с той же целью.</summary>
        public static void Set(Component target, bool on)
        {
            if (!on) target = null;
            if (target == cur) return;
            Clear();
            if (target == null) return;
            cur = target;
            hum = target.GetComponentInChildren<Humanoid>(true);
            if (hum != null) { hum.SetHighlight(1f); return; }
            foreach (var r in target.GetComponentsInChildren<MeshRenderer>())
            {
                if (rs.Count >= MaxRenderers) break;
                if (r == null || !r.enabled || r.sharedMaterial == null || !r.sharedMaterial.HasProperty(BaseColor) || r.HasPropertyBlock()) continue;
                rs.Add(r);
                orig.Add(r.sharedMaterial.GetColor(BaseColor));
            }
        }

        /// <summary>Пульс подсветки; вызывать раз в кадр.</summary>
        public static void Tick()
        {
            if (cur == null) { if (rs.Count > 0 || hum != null) Clear(); return; }
            if (hum != null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            float k = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f);
            for (int i = 0; i < rs.Count; i++)
            {
                var r = rs[i];
                if (r == null) continue;
                mpb.Clear();
                mpb.SetColor(BaseColor, Color.Lerp(orig[i], Color.white, 0.30f * k));
                mpb.SetColor(EmissionColor, Tint * (1.05f * k));
                r.SetPropertyBlock(mpb);
            }
        }

        static void Clear()
        {
            foreach (var r in rs) if (r != null) r.SetPropertyBlock(null);
            rs.Clear(); orig.Clear();
            if (hum != null) hum.SetHighlight(0f);
            hum = null; cur = null;
        }
    }
}
