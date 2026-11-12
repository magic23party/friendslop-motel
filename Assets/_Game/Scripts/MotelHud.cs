using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    /// <summary>IMGUI-интерфейс сцены Motel (C1): лобби, «Начать смену», пауза-меню по Esc, прицел.</summary>
    public class MotelHud : MonoBehaviour
    {
        public GameConfig config;

        void Update()
        {
            // отладка: автостарт смены, когда подключился второй игрок
            var gs = GameState.Instance;
            var nm = NetworkManager.Singleton;
            if (gs != null && nm != null && nm.IsServer && NetBootstrap.Arg("-autostart")
                && gs.Phase.Value == GamePhase.Lobby && gs.PlayerCount.Value >= 2)
                gs.StartShift();
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
                    Ui.Panel(new Rect(12, 12, 300, 34));
                    string ph = gs.Phase.Value == GamePhase.GameOver ? "Мотель закрыт" : "Смена идёт";
                    Ui.Shadowed(new Rect(18, 14, 290, 30), $"{ph} · комнат: {gs.RoomCount.Value} · игроков: {gs.PlayerCount.Value}", Ui.Label, Color.white);
                }
            }

            if (me != null && me.Placed && !me.MenuOpen)
                Ui.Shadowed(new Rect(12, Ui.H - 34, 400, 28), "WASD — ходьба · Shift — бег · Esc — меню", Ui.Label, new Color(1, 1, 1, 0.8f));

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
    }
}
