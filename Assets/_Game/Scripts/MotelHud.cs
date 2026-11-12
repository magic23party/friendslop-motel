using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    /// <summary>IMGUI-интерфейс сцены Motel (C1): лобби, «Начать смену», пауза-меню по Esc, прицел.</summary>
    public class MotelHud : MonoBehaviour
    {
        public GameConfig config;

        readonly List<TaskLine> tasks = new List<TaskLine>();
        GamePhase prevPhase;

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
            var gs = GameState.Instance;
            var me = PlayerController.Local;
            float cx = Ui.W / 2f;

            if (me != null && me.Placed && !me.MenuOpen)
                GUI.Label(new Rect(cx - 15, Ui.H / 2f - 15, 30, 30), "+", Ui.Center);

            var hands = PlayerHands.Local;
            if (me != null && me.Placed && !me.MenuOpen && hands != null)
            {
                if (!string.IsNullOrEmpty(hands.HintText))
                    Ui.Shadowed(new Rect(cx - 250, Ui.H / 2f + 22, 500, 30), hands.HintText, Ui.Center, Color.white);
                var held = hands.Held.Value;
                if (!held.IsNone)
                    Ui.Shadowed(new Rect(cx - 250, Ui.H - 64, 500, 30), "В руках: " + Items.Name(held) + "  ·  G — бросить", Ui.Center, Color.yellow);
            }

            if (gs != null)
            {
                int max = config != null ? config.maxPlayers : 8;
                if (gs.Phase.Value == GamePhase.Lobby)
                {
                    Ui.Panel(new Rect(cx - 220, 12, 440, nm.IsServer ? 110 : 76));
                    Ui.Shadowed(new Rect(cx - 210, 18, 420, 30), $"Лобби — игроков: {gs.PlayerCount.Value}/{max}", Ui.Center, Color.white);
                    if (nm.IsServer)
                    {
                        if (me != null && me.MenuOpen) GUI.enabled = false;
                        if (GUI.Button(new Rect(cx - 120, 56, 240, 44), "Начать смену", Ui.Button)) gs.StartShift();
                        GUI.enabled = true;
                    }
                    else Ui.Shadowed(new Rect(cx - 210, 48, 420, 30), "Ждём, пока хост начнёт смену…", Ui.Center, Color.yellow);
                }
                else
                {
                    DrawShiftPanel(gs);
                    if (gs.Phase.Value == GamePhase.Playing) DrawTasks();
                }
            }

            if (me != null && me.Placed && !me.MenuOpen)
                Ui.Shadowed(new Rect(12, Ui.H - 34, 800, 28), "WASD — ходьба · Shift — бег · Пробел — прыжок · E — действие · G — бросить · Esc — меню", Ui.Label, new Color(1, 1, 1, 0.8f));

            if (gs != null && gs.Phase.Value == GamePhase.GameOver)
            {
                DrawGameOver(gs, nm, me);
                return;
            }

            if (me != null && me.MenuOpen)
            {
                Ui.Panel(new Rect(0, 0, Ui.W, Ui.H), 0.5f);
                float x = cx - 160, y = Ui.H / 2f - 90;
                Ui.Shadowed(new Rect(x, y, 320, 50), "Пауза", Ui.Title, Color.white); y += 70;
                if (GUI.Button(new Rect(x, y, 320, 46), "Продолжить", Ui.Button)) me.SetMenu(false); y += 56;
                if (GUI.Button(new Rect(x, y, 320, 46), nm.IsServer ? "Закрыть мотель и выйти" : "Выйти в меню", Ui.Button))
                    NetBootstrap.Instance.LeaveToMenu();
            }
        }

        // ------------------------------------------------------------ C3: касса, налоги, задачи, Game Over

        void DrawShiftPanel(GameState gs)
        {
            bool due = gs.TaxDue.Value;
            Ui.Panel(new Rect(12, 12, 330, 100));
            Ui.Shadowed(new Rect(18, 14, 320, 26), $"Смена идёт · комнат: {gs.RoomCount.Value} · игроков: {gs.PlayerCount.Value}", Ui.Label, Color.white);
            Ui.Shadowed(new Rect(18, 42, 320, 26), $"Касса: {gs.Money.Value} $", Ui.Label, new Color(0.5f, 1f, 0.5f));
            string tax = due
                ? $"СЧЁТ {gs.config.taxAmount} $! До закрытия: {TaskBoard.Fmt(gs.TaxLeft)}"
                : $"Налог {gs.config.taxAmount} $ через {TaskBoard.Fmt(gs.TaxLeft)}";
            Ui.Shadowed(new Rect(18, 70, 320, 26), tax, Ui.Label, due ? new Color(1f, 0.4f, 0.35f) : Color.white);
        }

        void DrawTasks()
        {
            TaskBoard.Collect(tasks);
            const int maxLines = 12;
            float w = 360f, x = Ui.W - w - 12f, y = 12f;
            int shown = Mathf.Min(tasks.Count, maxLines);
            var style = Ui.Label;
            float h = 34f;
            for (int i = 0; i < shown; i++) h += style.CalcHeight(new GUIContent(tasks[i].text), w - 16f) + 2f;
            if (tasks.Count > shown) h += 26f;
            if (tasks.Count == 0) h += 26f;
            Ui.Panel(new Rect(x, y, w, h));
            Ui.Shadowed(new Rect(x + 8, y + 4, w - 16, 26), $"Задачи ({tasks.Count})", Ui.Label, new Color(1f, 0.9f, 0.5f));
            float cy = y + 32f;
            for (int i = 0; i < shown; i++)
            {
                float lh = style.CalcHeight(new GUIContent(tasks[i].text), w - 16f);
                Ui.Shadowed(new Rect(x + 8, cy, w - 16, lh), tasks[i].text, style, tasks[i].urgent ? new Color(1f, 0.4f, 0.35f) : Color.white);
                cy += lh + 2f;
            }
            if (tasks.Count > shown) Ui.Shadowed(new Rect(x + 8, cy, w - 16, 24), $"…и ещё {tasks.Count - shown}", style, new Color(1, 1, 1, 0.7f));
            else if (tasks.Count == 0) Ui.Shadowed(new Rect(x + 8, cy, w - 16, 24), "Всё чисто", style, new Color(0.6f, 1f, 0.6f));
        }

        void DrawGameOver(GameState gs, NetworkManager nm, PlayerController me)
        {
            float cx = Ui.W / 2f;
            Ui.Panel(new Rect(0, 0, Ui.W, Ui.H), 0.78f);
            float y = Ui.H / 2f - 170;
            Ui.Shadowed(new Rect(cx - 300, y, 600, 50), "Мотель закрыт", Ui.Title, new Color(1f, 0.4f, 0.35f)); y += 62;
            Ui.Shadowed(new Rect(cx - 300, y, 600, 30), "Налог не оплачен вовремя", Ui.Center, Color.white); y += 40;
            Ui.Shadowed(new Rect(cx - 300, y, 600, 30), "Смена длилась: " + TaskBoard.Fmt(gs.ShiftSeconds.Value), Ui.Center, Color.white); y += 32;
            Ui.Shadowed(new Rect(cx - 300, y, 600, 30), $"Заработано: {gs.Earned.Value} $", Ui.Center, new Color(0.5f, 1f, 0.5f)); y += 56;
            if (nm.IsServer)
            {
                if (GUI.Button(new Rect(cx - 160, y, 320, 46), "Заново", Ui.Button)) gs.ServerRestart();
                y += 56;
            }
            else
            {
                Ui.Shadowed(new Rect(cx - 300, y, 600, 30), "Ждём, пока хост перезапустит смену…", Ui.Center, Color.yellow);
                y += 46;
            }
            if (GUI.Button(new Rect(cx - 160, y, 320, 46), nm.IsServer ? "Закрыть мотель и выйти" : "Выйти в меню", Ui.Button))
                NetBootstrap.Instance.LeaveToMenu();
        }
    }
}
