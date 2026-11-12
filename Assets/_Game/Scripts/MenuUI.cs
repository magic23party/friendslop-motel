using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Friendslop
{
    /// <summary>Главное меню (IMGUI): ник, IP, Создать / Подключиться / Выход.</summary>
    public class MenuUI : MonoBehaviour
    {
        public GameObject netPrefab;   // префаб NetworkManager + UnityTransport + NetBootstrap
        public GameConfig config;

        string nick, ip, status = "";

        void Awake()
        {
            // NetworkManager переживает смену сцен: при возврате в меню второй не создаём.
            if (NetworkManager.Singleton == null) Instantiate(netPrefab);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            nick = PlayerPrefs.GetString("nick", "");
            if (string.IsNullOrWhiteSpace(nick)) nick = "Игрок" + Random.Range(100, 1000);
            ip = PlayerPrefs.GetString("ip", "127.0.0.1");
        }

        void Start()
        {
            status = NetBootstrap.LastMessage;
            NetBootstrap.LastMessage = "";
            // отладочные ключи командной строки (для автотеста двух экземпляров)
            var n = NetBootstrap.ArgValue("-nick");
            if (!string.IsNullOrEmpty(n)) nick = n;
            if (NetBootstrap.Arg("-shot")) { ShotRunner.Create(); CreateHost(); }       // P1: режим скриншотов
            else if (NetBootstrap.Arg("-autohost")) CreateHost();
            else if (NetBootstrap.Arg("-autojoin")) { ip = NetBootstrap.ArgValue("-autojoin") ?? ip; Join(); }
        }

        void Update()
        {
            if (!string.IsNullOrEmpty(NetBootstrap.LastMessage)) { status = NetBootstrap.LastMessage; NetBootstrap.LastMessage = ""; }
        }

        bool Busy => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        void Save()
        {
            PlayerController.LocalNick = nick;
            PlayerPrefs.SetString("nick", nick);
            PlayerPrefs.SetString("ip", ip);
            PlayerPrefs.Save();
        }

        void CreateHost()
        {
            Save();
            var nm = NetworkManager.Singleton;
            var tr = nm.GetComponent<UnityTransport>();
            tr.SetConnectionData("127.0.0.1", (ushort)config.port, "0.0.0.0");
            if (!nm.StartHost()) { status = "Не удалось создать сервер (порт " + config.port + " занят?)"; return; }
            nm.SceneManager.LoadScene(NetBootstrap.MotelScene, LoadSceneMode.Single);
        }

        void Join()
        {
            var addr = (ip ?? "").Trim();
            if (!IPAddress.TryParse(addr, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            { status = "Неверный IP-адрес (нужен вид 192.168.0.5)"; return; }
            Save();
            var nm = NetworkManager.Singleton;
            var tr = nm.GetComponent<UnityTransport>();
            tr.SetConnectionData(addr, (ushort)config.port);
            status = "";
            if (!nm.StartClient()) status = "Не удалось начать подключение";
        }

        void OnGUI()
        {
            Ui.Begin();
            const float w = 420f;
            float x = (Ui.W - w) / 2f, y = 110f;
            Ui.Shadowed(new Rect(x, y, w, 50), "Friendslop Motel", Ui.Title, Color.white);
            y += 80;

            var nm = NetworkManager.Singleton;
            bool connecting = Busy && nm != null && !nm.IsServer;

            GUI.enabled = !Busy;
            GUI.Label(new Rect(x, y, w, 26), "Ник", Ui.Label); y += 28;
            nick = GUI.TextField(new Rect(x, y, w, 36), nick ?? "", 16, Ui.Field); y += 46;
            GUI.Label(new Rect(x, y, w, 26), "IP хоста", Ui.Label); y += 28;
            ip = GUI.TextField(new Rect(x, y, w, 36), ip ?? "", 45, Ui.Field); y += 56;

            if (GUI.Button(new Rect(x, y, w, 44), "Создать", Ui.Button)) CreateHost(); y += 54;
            if (GUI.Button(new Rect(x, y, w, 44), "Подключиться", Ui.Button)) Join(); y += 54;
            GUI.enabled = true;

            if (connecting)
            {
                Ui.Shadowed(new Rect(x, y, w, 30), "Подключение…", Ui.Center, Color.yellow); y += 34;
                if (GUI.Button(new Rect(x, y, w, 36), "Отмена", Ui.Button)) { nm.Shutdown(); status = ""; }
                y += 46;
            }
            else
            {
                if (GUI.Button(new Rect(x, y, w, 44), "Выход", Ui.Button)) Application.Quit();
                y += 54;
            }

            if (!string.IsNullOrEmpty(status))
                Ui.Shadowed(new Rect(x, y + 6, w, 60), status, Ui.Center, new Color(1f, 0.55f, 0.45f));
        }
    }
}
