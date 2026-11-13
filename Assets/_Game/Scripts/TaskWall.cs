using System;
using System.Collections.Generic;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// V3: электронная таблица за ресепшеном — список задач отеля (TaskBoard) прямо в мире, а не в HUD. Большой экран на стене за стойкой,
    /// видно из лобби: срочные строки на красной плашке, значки предметов цветом тех же стеллажей, что и в меню/на табличках номеров.
    /// Никаких новых RPC: данные берутся из уже синхронизированных состояний (TaskBoard.Collect читает GameState, Room, Guest, Toilet).
    /// Обновляется не чаще boardRefresh (≈4 раза в секунду). Строки и значки (TextMesh Label3D + цветные плитки) создаёт Builder.
    /// </summary>
    public class TaskWall : MonoBehaviour
    {
        [Serializable]
        public class Row
        {
            public TextMesh text;
            public GameObject urgentBg;
            public Renderer[] icons;
            public TextMesh[] glyphs;
            public float left, right, y;      // локальные координаты колонки и строки
            [NonSerialized] public string lastText;
            [NonSerialized] public int lastIcons = -1;
        }

        public TextMesh header;
        public Row[] rows;
        public Material[] iconMats;           // по ItemType
        public float textSize = 0.27f;        // высота строки текста, м
        public float iconPitch = 0.29f, rowGap = 0.04f;

        readonly List<TaskLine> list = new List<TaskLine>();
        float next;
        string lastHeader;
        static readonly Color Calm = Color.white, Hot = new Color(1f, 0.88f, 0.84f);

        void Update()
        {
            float now = Time.unscaledTime;
            if (now < next) return;
            var cfg = GameConfig.Active;
            next = now + (cfg != null ? Mathf.Max(0.05f, cfg.boardRefresh) : 0.25f);
            Refresh();
        }

        void Refresh()
        {
            TaskBoard.Collect(list);
            // срочные выше (порядок внутри групп сохраняется)
            var sorted = new List<TaskLine>(list.Count);
            foreach (var t in list) if (t.urgent) sorted.Add(t);
            foreach (var t in list) if (!t.urgent) sorted.Add(t);

            int n = sorted.Count, cap = rows.Length;
            var gs = GameState.Instance;
            string head = gs == null || gs.Phase.Value != GamePhase.Playing ? "ЗАДАЧИ"
                : n == 0 ? "ЗАДАЧИ  ·  всё чисто" : "ЗАДАЧИ  ·  " + n;
            if (head != lastHeader && header != null) { lastHeader = head; header.text = head; }

            for (int i = 0; i < cap; i++)
            {
                var row = rows[i];
                bool more = i == cap - 1 && n > cap;
                if (i >= n || row == null) { Set(row, null, false, null); continue; }
                if (more) Set(row, $"…и ещё {n - i}", false, null);
                else Set(row, sorted[i].text, sorted[i].urgent, sorted[i].icons);
            }
        }

        void Set(Row row, string text, bool urgent, ItemType[] icons)
        {
            if (row == null) return;
            int sig = 0;
            if (icons != null) foreach (var ic in icons) sig = sig * 13 + (int)ic + 1;
            string cache = text == null ? "" : text + (urgent ? "!" : "");
            if (cache == row.lastText && sig == row.lastIcons) return;
            row.lastText = cache; row.lastIcons = sig;

            if (row.urgentBg != null) row.urgentBg.SetActive(text != null && urgent);
            int ni = icons == null ? 0 : Mathf.Min(icons.Length, row.icons.Length);
            for (int k = 0; k < row.icons.Length; k++)
            {
                bool on = k < ni;
                if (row.icons[k] != null) row.icons[k].gameObject.SetActive(on);
                if (row.glyphs[k] != null) row.glyphs[k].gameObject.SetActive(on);
                if (!on) continue;
                var type = icons[k];
                if (iconMats != null && (int)type < iconMats.Length && iconMats[(int)type] != null) row.icons[k].sharedMaterial = iconMats[(int)type];
                float x = row.right - 0.12f - (ni - 1 - k) * iconPitch;
                var p = row.icons[k].transform.localPosition; p.x = x; row.icons[k].transform.localPosition = p;
                row.glyphs[k].transform.localPosition = new Vector3(x, row.y, row.glyphs[k].transform.localPosition.z);
                row.glyphs[k].text = UiStyle.ItemGlyph(type);
            }
            if (row.text == null) return;
            row.text.gameObject.SetActive(text != null);
            if (text == null) return;
            row.text.text = text;
            row.text.color = urgent ? Hot : Calm;
            float room = (row.right - row.left) - 0.28f - ni * iconPitch;
            FitText(row.text, text, textSize, room);
        }

        /// <summary>Подогнать высоту строки под доступную ширину (TextMesh не умеет «уменьшить по ширине» сам).</summary>
        public static void FitText(TextMesh tm, string text, float height, float maxWidth)
        {
            float cs = height / 6.4f;
            float w = Measure(tm, text, cs);
            if (w > maxWidth && w > 0.01f) cs *= Mathf.Max(0.55f, maxWidth / w);
            tm.characterSize = cs;
        }

        static float Measure(TextMesh tm, string text, float cs)
        {
            var f = tm.font;
            float adv = 0f;
            if (f != null && !Application.isBatchMode)       // без графики (самотесты в batchmode) шрифтовой атлас недоступен — грубая оценка
            {
                f.RequestCharactersInTexture(text, 64, FontStyle.Normal);
                foreach (char c in text)
                    adv += f.GetCharacterInfo(c, out var ci, 64, FontStyle.Normal) ? ci.advance : 35f;
            }
            else adv = text.Length * 35f;
            return adv * cs * 0.1f;
        }
    }
}
