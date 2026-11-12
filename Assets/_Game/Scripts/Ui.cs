using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// IMGUI-масштаб: координаты в виртуальных 720 px по высоте экрана. Стили и рисование — в UiStyle (единое место);
    /// свойства ниже оставлены для старых вызовов и просто отдают стили UiStyle.
    /// </summary>
    public static class Ui
    {
        public static float Scale => Mathf.Max(0.5f, Screen.height / 720f);
        public static float W => Screen.width / Scale;
        public const float H = 720f;

        /// <summary>Вызывать в начале OnGUI: координаты дальше — в виртуальных 720 px по высоте.</summary>
        public static void Begin() { var s = Scale; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f)); }
        public static void Reset() { GUI.matrix = Matrix4x4.identity; }

        public static GUIStyle Label => UiStyle.Label;
        public static GUIStyle Center => UiStyle.Center;
        public static GUIStyle Title => UiStyle.Title;
        public static GUIStyle Button => UiStyle.ButtonStyle;
        public static GUIStyle Field => UiStyle.Field;

        /// <summary>Текст с тенью (читается на любом фоне).</summary>
        public static void Shadowed(Rect r, string text, GUIStyle style, Color color) { UiStyle.Shadowed(r, text, style, color); }

        /// <summary>Тёмная скруглённая плашка под текст.</summary>
        public static void Panel(Rect r) { UiStyle.Plate(r); }
    }
}
