using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Единый стиль IMGUI (P3): палитра арт-дирекшна, GUIStyle'ы, скруглённые плашки (процедурные 9-slice текстуры),
    /// кнопки со звуком клика, значки предметов (цветные квадраты), «неоновый» заголовок, клавиша-подсказка.
    /// Координаты во всех методах — виртуальные (720 px по высоте): масштаб под экран делает Ui.Begin().
    /// Всё, что рисуют MenuUI / MotelHud / PlayerCombat / PlayerController, берёт стили только отсюда.
    /// </summary>
    public static class UiStyle
    {
        // палитра интерфейса (из Palette мотеля)
        public static readonly Color Cream = new Color(0.97f, 0.93f, 0.82f);
        public static readonly Color Teal = new Color(0.14f, 0.64f, 0.63f);
        public static readonly Color TealDark = new Color(0.09f, 0.38f, 0.41f);
        public static readonly Color TealLight = new Color(0.40f, 0.80f, 0.76f);
        public static readonly Color Mustard = new Color(0.95f, 0.74f, 0.20f);
        public static readonly Color Alert = new Color(1f, 0.42f, 0.38f);
        public static readonly Color Good = new Color(0.50f, 1f, 0.55f);
        public static readonly Color Neon = new Color(1f, 0.30f, 0.46f);
        public static readonly Color Ink = new Color(0.07f, 0.09f, 0.12f);

        public enum Kind { Panel, Urgent, Card, Field, Key, Bar }

        static bool ready;
        static Texture2D tPanel, tUrgent, tCard, tField, tKey, tBar, tBtn, tBtnHover, tBtnDown, tPrimary, tPrimaryHover, tPrimaryDown, tSolid, tFadeLeft;
        static GUIStyle sPanel, sUrgent, sCard, sField, sKey, sBar, sBtn, sBtnPrimary, sLabel, sSmall, sCenter, sTitle, sHeader, sTextField, sIcon, sKeyText;

        // ------------------------------------------------------------ текстуры

        static Texture2D Round(Color fill, Color border, int r = 10, int bw = 2)
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var px = new Color[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Abs(x + 0.5f - half), cy = Mathf.Abs(y + 0.5f - half);
                    float qx = cx - (half - r), qy = cy - (half - r);
                    float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    float cover = Mathf.Clamp01(0.5f - d);
                    float edge = bw > 0 ? Mathf.Clamp01(d + bw + 0.5f) : 0f;
                    var c = Color.Lerp(fill, border, edge);
                    c.a *= cover;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static GUIStyle Box(Texture2D bg, int r)
        {
            var s = new GUIStyle { border = new RectOffset(r, r, r, r), padding = new RectOffset(0, 0, 0, 0) };
            s.normal.background = bg; s.hover.background = bg; s.active.background = bg; s.focused.background = bg;
            return s;
        }

        static void Init()
        {
            if (ready && tPanel != null) return;
            ready = true;
            tPanel = Round(new Color(0.05f, 0.08f, 0.11f, 0.80f), new Color(0.40f, 0.80f, 0.76f, 0.40f), 10, 2);
            tUrgent = Round(new Color(0.30f, 0.05f, 0.07f, 0.86f), new Color(1f, 0.42f, 0.38f, 0.95f), 10, 2);
            tCard = Round(new Color(0.05f, 0.08f, 0.11f, 0.90f), new Color(0.95f, 0.74f, 0.20f, 0.85f), 14, 2);
            tField = Round(new Color(0.02f, 0.04f, 0.06f, 0.92f), new Color(0.40f, 0.80f, 0.76f, 0.70f), 8, 2);
            tKey = Round(new Color(0.97f, 0.93f, 0.82f, 1f), new Color(0.95f, 0.74f, 0.20f, 1f), 7, 2);
            tBar = Round(new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f), 6, 0);
            tBtn = Round(new Color(0.09f, 0.38f, 0.41f, 0.95f), new Color(0.40f, 0.80f, 0.76f, 0.85f), 9, 2);
            tBtnHover = Round(new Color(0.14f, 0.55f, 0.56f, 1f), new Color(0.70f, 1f, 0.95f, 1f), 9, 2);
            tBtnDown = Round(new Color(0.06f, 0.25f, 0.28f, 1f), new Color(0.40f, 0.80f, 0.76f, 1f), 9, 2);
            tPrimary = Round(new Color(0.95f, 0.74f, 0.20f, 1f), new Color(1f, 0.92f, 0.55f, 1f), 9, 2);
            tPrimaryHover = Round(new Color(1f, 0.84f, 0.34f, 1f), new Color(1f, 1f, 0.8f, 1f), 9, 2);
            tPrimaryDown = Round(new Color(0.74f, 0.55f, 0.12f, 1f), new Color(1f, 0.92f, 0.55f, 1f), 9, 2);
            tSolid = Round(Color.white, Color.white, 6, 0);
            tFadeLeft = new Texture2D(64, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < 64; i++) tFadeLeft.SetPixel(i, 0, new Color(1f, 1f, 1f, Mathf.Pow(1f - i / 63f, 1.6f)));
            tFadeLeft.Apply();

            sPanel = Box(tPanel, 10); sUrgent = Box(tUrgent, 10); sCard = Box(tCard, 14); sField = Box(tField, 8); sKey = Box(tKey, 7); sBar = Box(tBar, 6);
            sIcon = Box(tSolid, 6);

            sBtn = new GUIStyle(GUI.skin.button)
            {
                fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, border = new RectOffset(9, 9, 9, 9),
                padding = new RectOffset(8, 8, 4, 6), margin = new RectOffset(0, 0, 0, 0)
            };
            SetBg(sBtn, tBtn, tBtnHover, tBtnDown, Cream, Color.white, Color.white);
            sBtnPrimary = new GUIStyle(sBtn);
            SetBg(sBtnPrimary, tPrimary, tPrimaryHover, tPrimaryDown, Ink, Ink, Ink);

            sLabel = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            sLabel.normal.textColor = Color.white;
            sSmall = new GUIStyle(sLabel) { fontSize = 15 };
            sCenter = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter };
            sTitle = new GUIStyle(sLabel) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            sHeader = new GUIStyle(sLabel) { fontSize = 19, fontStyle = FontStyle.Bold };
            sKeyText = new GUIStyle(sLabel) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            sKeyText.normal.textColor = Ink;

            sTextField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 20, alignment = TextAnchor.MiddleLeft, border = new RectOffset(8, 8, 8, 8), padding = new RectOffset(10, 10, 4, 4)
            };
            SetBg(sTextField, tField, tField, tField, Cream, Cream, Cream);
            sTextField.focused.background = tBtnDown;
            sTextField.focused.textColor = Color.white;
        }

        static void SetBg(GUIStyle s, Texture2D n, Texture2D h, Texture2D a, Color cn, Color ch, Color ca)
        {
            s.normal.background = n; s.hover.background = h; s.active.background = a; s.focused.background = h;
            s.normal.textColor = cn; s.hover.textColor = ch; s.active.textColor = ca; s.focused.textColor = ch;
        }

        // ------------------------------------------------------------ стили

        public static GUIStyle Label { get { Init(); return sLabel; } }
        public static GUIStyle Small { get { Init(); return sSmall; } }
        public static GUIStyle Center { get { Init(); return sCenter; } }
        public static GUIStyle Title { get { Init(); return sTitle; } }
        public static GUIStyle Header { get { Init(); return sHeader; } }
        public static GUIStyle ButtonStyle { get { Init(); return sBtn; } }
        public static GUIStyle Field { get { Init(); return sTextField; } }

        // ------------------------------------------------------------ рисование

        /// <summary>Скруглённая плашка. alpha множит непрозрачность (затемнение фона и т.п.).</summary>
        public static void Plate(Rect r, Kind kind = Kind.Panel, float alpha = 1f)
        {
            Init();
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha * old.a);
            GUI.Box(r, GUIContent.none, kind == Kind.Urgent ? sUrgent : kind == Kind.Card ? sCard : kind == Kind.Field ? sField : kind == Kind.Key ? sKey : kind == Kind.Bar ? sBar : sPanel);
            GUI.color = old;
        }

        /// <summary>Полупрозрачная тёмная вуаль на прямоугольник (затемнение сцены под меню).</summary>
        public static void Dim(Rect r, float alpha)
        {
            Init();
            var old = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.06f, alpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>Затемнение, плавно уходящее слева направо (читаемость меню на фоне сцены).</summary>
        public static void FadeLeft(Rect r, float alpha)
        {
            Init();
            var old = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.06f, alpha);
            GUI.DrawTexture(r, tFadeLeft);
            GUI.color = old;
        }

        /// <summary>Кнопка со звуком клика. primary — жёлтая (главное действие).</summary>
        public static bool Btn(Rect r, string text, bool primary = false)
        {
            Init();
            bool hit = GUI.Button(r, text, primary ? sBtnPrimary : sBtn);
            if (hit) Sfx.Play2D(Snd.Click);
            return hit;
        }

        /// <summary>Клавиша-подсказка («E»): кремовая плашка с тёмной буквой.</summary>
        public static void KeyCap(Rect r, string key)
        {
            Plate(r, Kind.Key);
            GUI.Label(r, key, sKeyText);
        }

        /// <summary>Полоса прогресса 0..1 с заливкой нужного цвета.</summary>
        public static void Bar(Rect r, float v, Color fill)
        {
            Init();
            Plate(r, Kind.Field);
            v = Mathf.Clamp01(v);
            float w = Mathf.Max(0f, (r.width - 4f) * v);
            if (w < 10f) return;
            var old = GUI.color;
            GUI.color = fill;
            GUI.Box(new Rect(r.x + 2f, r.y + 2f, w, r.height - 4f), GUIContent.none, sBar);
            GUI.color = old;
        }

        /// <summary>Тёмная точка-прицел с светлой серединой.</summary>
        public static void Dot(Vector2 c, float size)
        {
            Init();
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.Box(new Rect(c.x - size / 2f - 1.5f, c.y - size / 2f - 1.5f, size + 3f, size + 3f), GUIContent.none, sIcon);
            GUI.color = new Color(1f, 1f, 1f, 0.92f);
            GUI.Box(new Rect(c.x - size / 2f, c.y - size / 2f, size, size), GUIContent.none, sIcon);
            GUI.color = old;
        }

        // ------------------------------------------------------------ значки предметов

        public static Color ItemColor(ItemType t)
        {
            switch (t)
            {
                case ItemType.Form: return new Color(0.94f, 0.92f, 0.84f);
                case ItemType.Key: return Palette.Mustard;
                case ItemType.Towel: return Palette.Towel;
                case ItemType.Soap: return Palette.Soap;
                case ItemType.Napkins: return Palette.Napkins;
                case ItemType.Mop: return Palette.Mop;
                case ItemType.Sledgehammer: return Palette.Hammer;
                case ItemType.Money: return new Color(0.30f, 0.78f, 0.36f);
                default: return Color.gray;
            }
        }

        static string ItemGlyph(ItemType t)
        {
            switch (t)
            {
                case ItemType.Form: return "Б";
                case ItemType.Key: return "№";
                case ItemType.Towel: return "П";
                case ItemType.Soap: return "М";
                case ItemType.Napkins: return "С";
                case ItemType.Mop: return "Ш";
                case ItemType.Sledgehammer: return "К";
                case ItemType.Money: return "$";
                default: return "";
            }
        }

        /// <summary>Значок предмета: цветной квадрат цвета предмета (как на стеллаже) с буквой.</summary>
        public static void ItemIcon(Rect r, ItemType t)
        {
            Init();
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Box(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f), GUIContent.none, sIcon);
            GUI.color = ItemColor(t);
            GUI.Box(r, GUIContent.none, sIcon);
            GUI.color = old;
            sKeyText.fontSize = Mathf.RoundToInt(r.height * 0.62f);
            GUI.Label(r, ItemGlyph(t), sKeyText);
            sKeyText.fontSize = 17;
        }

        // ------------------------------------------------------------ текст

        /// <summary>Текст с тенью (читается на любом фоне).</summary>
        public static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            Init();
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.85f * old.a);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
            GUI.Label(r, text, style);
            GUI.color = old;
        }

        /// <summary>«Неоновая» надпись: мягкое свечение вокруг светлой сердцевины (название игры, «Мотель закрыт»).</summary>
        public static void NeonTitle(Rect r, string text, Color glow, Color core, int fontSize)
        {
            Init();
            int old = sTitle.fontSize;
            sTitle.fontSize = fontSize;
            var oc = GUI.color;
            for (int ring = 1; ring <= 2; ring++)
            {
                float d = ring * 3.2f;
                GUI.color = new Color(glow.r, glow.g, glow.b, 0.16f / ring);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    GUI.Label(new Rect(r.x + Mathf.Cos(a) * d, r.y + Mathf.Sin(a) * d, r.width, r.height), text, sTitle);
                }
            }
            GUI.color = new Color(glow.r, glow.g, glow.b, 0.9f);
            GUI.Label(new Rect(r.x, r.y + 1.5f, r.width, r.height), text, sTitle);
            GUI.color = core;
            GUI.Label(r, text, sTitle);
            GUI.color = oc;
            sTitle.fontSize = old;
        }
    }
}
