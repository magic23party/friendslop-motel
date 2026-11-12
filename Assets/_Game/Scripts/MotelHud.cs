using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// IMGUI-интерфейс сцены Motel: лобби, «Начать смену», пауза по Esc, прицел и подсказка, предмет в руках,
    /// касса/налог, список задач со значками, Game Over. Весь стиль — UiStyle (единый для меню, HUD и Game Over).
    /// </summary>
    public class MotelHud : MonoBehaviour
    {
        public GameConfig config;

        readonly List<TaskLine> tasks = new List<TaskLine>();
        GamePhase prevPhase;
        static GUIStyle money, valueRight;

        void Update()
        {
            // отладка: автостарт смены, когда подключился второй игрок
            var gs = GameState.Instance;
            var nm = NetworkManager.Singleton;
            if (gs != null && nm != null && nm.IsServer && NetBootstrap.Arg("-autostart")
                && gs.Phase.Value == GamePhase.Lobby && gs.PlayerCount.Value >= 2)
                gs.StartShift();

            // Game Over: курсор свободен у всех (кнопки экрана); после «Заново» — снова в игру
            var me = PlayerController.Local;
            if (gs != null && me != null && me.Placed)
            {
                var ph = gs.Phase.Value;
                if (ph == GamePhase.GameOver && !me.MenuOpen) me.SetMenu(true);
                else if (prevPhase == GamePhase.GameOver && ph != GamePhase.GameOver) me.SetMenu(false);
                prevPhase = ph;
            }
        }

        void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;
            Ui.Begin();
            if (money == null)
            {
                money = new GUIStyle(UiStyle.Title) { fontSize = 30, alignment = TextAnchor.MiddleRight };
                valueRight = new GUIStyle(UiStyle.Header) { alignment = TextAnchor.MiddleRight };
            }
            var gs = GameState.Instance;
            var me = PlayerController.Local;
            float cx = Ui.W / 2f;
            bool playing = me != null && me.Placed && !me.MenuOpen;

            if (playing) UiStyle.Dot(new Vector2(cx, Ui.H / 2f), 6f);

            var hands = PlayerHands.Local;
            if (playing && hands != null)
            {
                if (!string.IsNullOrEmpty(hands.HintText)) DrawHint(cx, Ui.H / 2f + 30f, hands.HintText);
                var held = hands.Held.Value;
                if (!held.IsNone) DrawHeld(cx, Ui.H - 70f, held);
            }

            if (gs != null)
            {
                int max = config != null ? config.maxPlayers : 8;
                if (gs.Phase.Value == GamePhase.Lobby)
                {
                    float h = nm.IsServer ? 128f : 86f;
                    UiStyle.Plate(new Rect(cx - 230, 12, 460, h), UiStyle.Kind.Card);
                    Ui.Shadowed(new Rect(cx - 220, 20, 440, 30), $"Лобби — игроков: {gs.PlayerCount.Value}/{max}", UiStyle.Center, Color.white);
                    if (nm.IsServer)
                    {
                        if (me != null && me.MenuOpen) GUI.enabled = false;
                        if (UiStyle.Btn(new Rect(cx - 130, 62, 260, 48), "Начать смену", true)) gs.StartShift();
                        GUI.enabled = true;
                    }
                    else Ui.Shadowed(new Rect(cx - 220, 52, 440, 30), "Ждём, пока хост начнёт смену…", UiStyle.Center, UiStyle.Mustard);
                }
                else
                {
                    DrawShiftPanel(gs);
                    if (gs.Phase.Value == GamePhase.Playing) DrawTasks();
                }
            }

            if (playing)
                Ui.Shadowed(new Rect(12, Ui.H - 30, 900, 24), "WASD — ходьба · Shift — бег · Пробел — прыжок · E — действие · G — бросить · Esc — меню", UiStyle.Small, new Color(1, 1, 1, 0.7f));

            if (gs != null && gs.Phase.Value == GamePhase.GameOver)
            {
                DrawGameOver(gs, nm);
                return;
            }

            if (me != null && me.MenuOpen)
            {
                UiStyle.Dim(new Rect(0, 0, Ui.W, Ui.H), 0.55f);
                float x = cx - 190, y = Ui.H / 2f - 140;
                UiStyle.Plate(new Rect(x, y, 380, 280), UiStyle.Kind.Card);
                UiStyle.NeonTitle(new Rect(x, y + 22, 380, 56), "Пауза", UiStyle.Teal, UiStyle.Cream, 38);
                y += 100;
                if (UiStyle.Btn(new Rect(x + 30, y, 320, 50), "Продолжить", true)) me.SetMenu(false);
                y += 64;
                if (UiStyle.Btn(new Rect(x + 30, y, 320, 50), nm.IsServer ? "Закрыть мотель и выйти" : "Выйти в меню"))
                    NetBootstrap.Instance.LeaveToMenu();
            }
        }

        // ------------------------------------------------------------ подсказка под прицелом и предмет в руках

        /// <summary>Подсказка под прицелом: «E — действие» рисуется клавишей-кепкой и текстом на плашке.</summary>
        static void DrawHint(float cx, float y, string hint)
        {
            const string ePrefix = "E — ";
            bool key = hint.StartsWith(ePrefix);
            string text = key ? hint.Substring(ePrefix.Length) : hint;
            float tw = UiStyle.Label.CalcSize(new GUIContent(text)).x;
            float w = tw + 28f + (key ? 40f : 0f);
            var r = new Rect(cx - w / 2f, y, w, 40f);
            UiStyle.Plate(r);
            float x = r.x + 14f;
            if (key) { UiStyle.KeyCap(new Rect(x, y + 7f, 26f, 26f), "E"); x += 36f; }
            Ui.Shadowed(new Rect(x, y, tw + 6f, 40f), text, UiStyle.Label, Color.white);
        }

        static void DrawHeld(float cx, float y, HeldItem held)
        {
            string name = Items.Name(held);
            string tip = held.type == ItemType.Sledgehammer ? "ЛКМ — удар  ·  G — бросить" : "G — бросить";
            float nw = UiStyle.Header.CalcSize(new GUIContent(name)).x;
            float tw = UiStyle.Small.CalcSize(new GUIContent(tip)).x;
            float w = 58f + nw + 22f + tw + 16f;
            var r = new Rect(cx - w / 2f, y, w, 50f);
            UiStyle.Plate(r);
            UiStyle.ItemIcon(new Rect(r.x + 12f, y + 8f, 34f, 34f), held.type);
            Ui.Shadowed(new Rect(r.x + 58f, y, nw + 6f, 50f), name, UiStyle.Header, UiStyle.Cream);
            Ui.Shadowed(new Rect(r.x + 58f + nw + 22f, y, tw + 8f, 50f), tip, UiStyle.Small, UiStyle.Mustard);
        }

        // ------------------------------------------------------------ касса, налоги, задачи, Game Over

        void DrawShiftPanel(GameState gs)
        {
            bool due = gs.TaxDue.Value;
            float pulse = due ? 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
            var alert = new Color(UiStyle.Alert.r * pulse, UiStyle.Alert.g * pulse, UiStyle.Alert.b * pulse, 1f);
            UiStyle.Plate(new Rect(12, 12, 330, 122), due ? UiStyle.Kind.Urgent : UiStyle.Kind.Panel, due ? 0.9f : 1f);
            Ui.Shadowed(new Rect(24, 16, 90, 24), "Смена", UiStyle.Header, UiStyle.Mustard);
            Ui.Shadowed(new Rect(110, 16, 220, 24), $"комнат {gs.RoomCount.Value}  ·  игроков {gs.PlayerCount.Value}", UiStyle.Small, new Color(1, 1, 1, 0.75f));
            Ui.Shadowed(new Rect(24, 42, 100, 36), "Касса", UiStyle.Label, UiStyle.Cream);
            Ui.Shadowed(new Rect(110, 40, 218, 40), $"{gs.Money.Value} $", money, UiStyle.Good);

            string tax = due
                ? $"СЧЁТ {gs.config.taxAmount} $! До закрытия: {TaskBoard.Fmt(gs.TaxLeft)}"
                : $"Налог {gs.config.taxAmount} $ через {TaskBoard.Fmt(gs.TaxLeft)}";
            Ui.Shadowed(new Rect(24, 82, 304, 22), tax, UiStyle.Small, due ? new Color(1f, 0.86f, 0.82f) : Color.white);
            float total = due ? gs.TaxDeadline : gs.TaxInterval;
            UiStyle.Bar(new Rect(24, 108, 292, 14), total > 0f ? gs.TaxLeft / total : 0f, due ? alert : UiStyle.Teal);
        }

        void DrawTasks()
        {
            TaskBoard.Collect(tasks);
            const int maxLines = 10;
            const float iconSize = 20f, iconGap = 4f;
            float w = 360f, x = Ui.W - w - 12f, y = 12f;
            int shown = Mathf.Min(tasks.Count, maxLines);
            var style = UiStyle.Label;
            float h = 44f;
            for (int i = 0; i < shown; i++) h += LineHeight(tasks[i], w, iconSize, iconGap, style) + 4f;
            if (tasks.Count > shown) h += 26f;
            if (tasks.Count == 0) h += 28f;
            UiStyle.Plate(new Rect(x, y, w, h));
            Ui.Shadowed(new Rect(x + 14, y + 8, w - 28, 26), $"Задачи ({tasks.Count})", UiStyle.Header, UiStyle.Mustard);
            float cy = y + 40f;
            for (int i = 0; i < shown; i++)
            {
                var t = tasks[i];
                float lh = LineHeight(t, w, iconSize, iconGap, style);
                float iw = t.icons != null ? t.icons.Length * (iconSize + iconGap) : 0f;
                if (t.urgent) UiStyle.Plate(new Rect(x + 6, cy - 2, w - 12, lh + 4), UiStyle.Kind.Urgent, 0.85f);
                Ui.Shadowed(new Rect(x + 14, cy, w - 28 - iw, lh), t.text, style, t.urgent ? new Color(1f, 0.85f, 0.8f) : Color.white);
                if (t.icons != null)
                    for (int k = 0; k < t.icons.Length; k++)
                        UiStyle.ItemIcon(new Rect(x + w - 14 - iw + iconGap + k * (iconSize + iconGap), cy + (lh - iconSize) / 2f, iconSize, iconSize), t.icons[k]);
                cy += lh + 4f;
            }
            if (tasks.Count > shown) Ui.Shadowed(new Rect(x + 14, cy, w - 28, 24), $"…и ещё {tasks.Count - shown}", UiStyle.Small, new Color(1, 1, 1, 0.7f));
            else if (tasks.Count == 0) Ui.Shadowed(new Rect(x + 14, cy, w - 28, 24), "Всё чисто", style, UiStyle.Good);
        }

        static float LineHeight(TaskLine t, float w, float iconSize, float iconGap, GUIStyle style)
        {
            float iw = t.icons != null ? t.icons.Length * (iconSize + iconGap) : 0f;
            return Mathf.Max(iconSize + 4f, style.CalcHeight(new GUIContent(t.text), w - 28f - iw));
        }

        void DrawGameOver(GameState gs, NetworkManager nm)
        {
            float cx = Ui.W / 2f;
            UiStyle.Dim(new Rect(0, 0, Ui.W, Ui.H), 0.72f);
            float w = 560f, h = 440f, x = cx - w / 2f, y = Ui.H / 2f - h / 2f;
            UiStyle.Plate(new Rect(x, y, w, h), UiStyle.Kind.Card);
            UiStyle.NeonTitle(new Rect(x, y + 22, w, 60), "Мотель закрыт", UiStyle.Alert, new Color(1f, 0.9f, 0.88f), 42);
            Ui.Shadowed(new Rect(x, y + 88, w, 30), "Налог не оплачен вовремя", UiStyle.Center, Color.white);

            UiStyle.Plate(new Rect(x + 40, y + 130, w - 80, 86));
            Ui.Shadowed(new Rect(x + 60, y + 138, 240, 34), "Смена длилась", UiStyle.Label, UiStyle.Cream);
            Ui.Shadowed(new Rect(x + w - 260, y + 138, 200, 34), TaskBoard.Fmt(gs.ShiftSeconds.Value), valueRight, Color.white);
            Ui.Shadowed(new Rect(x + 60, y + 174, 240, 34), "Заработано", UiStyle.Label, UiStyle.Cream);
            Ui.Shadowed(new Rect(x + w - 260, y + 174, 200, 34), $"{gs.Earned.Value} $", valueRight, UiStyle.Good);

            float by = y + 242f;
            if (nm.IsServer)
            {
                if (UiStyle.Btn(new Rect(x + 90, by, w - 180, 52), "Заново", true)) gs.ServerRestart();
                by += 64f;
            }
            else
            {
                Ui.Shadowed(new Rect(x, by, w, 30), "Ждём, пока хост перезапустит смену…", UiStyle.Center, UiStyle.Mustard);
                by += 52f;
            }
            if (UiStyle.Btn(new Rect(x + 90, by, w - 180, 52), nm.IsServer ? "Закрыть мотель и выйти" : "Выйти в меню"))
                NetBootstrap.Instance.LeaveToMenu();
        }
    }
}
