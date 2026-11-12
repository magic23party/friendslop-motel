using UnityEngine;

namespace Friendslop
{
    /// <summary>Общие IMGUI-хелперы: масштаб под высоту экрана 720 и стили.</summary>
    public static class Ui
    {
        static GUIStyle label, button, field, title, center;

        public static float Scale => Mathf.Max(0.5f, Screen.height / 720f);
        public static float W => Screen.width / Scale;
        public const float H = 720f;

        /// <summary>Вызывать в начале OnGUI: координаты дальше — в виртуальных 720 px по высоте.</summary>
        public static void Begin() { var s = Scale; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f)); }
        public static void Reset() { GUI.matrix = Matrix4x4.identity; }

        public static GUIStyle Label => label ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
        public static GUIStyle Center => center ??= new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        public static GUIStyle Title => title ??= new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        public static GUIStyle Button => button ??= new GUIStyle(GUI.skin.button) { fontSize = 20 };
        public static GUIStyle Field => field ??= new GUIStyle(GUI.skin.textField) { fontSize = 20, alignment = TextAnchor.MiddleLeft };

        /// <summary>Текст с тенью (читается на любом фоне).</summary>
        public static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.85f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = old;
        }

        public static void Panel(Rect r, float alpha = 0.6f)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, alpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
