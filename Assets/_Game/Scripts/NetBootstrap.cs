using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Friendslop
{
    /// <summary>
    /// Живёт на префабе NetworkManager (создаётся MenuUI один раз и переживает смену сцен).
    /// Одобрение подключений (лимит игроков), возврат в меню при потере связи.
    /// </summary>
    public class NetBootstrap : MonoBehaviour
    {
        public const string MenuScene = "Menu";
        public const string MotelScene = "Motel";

        public static NetBootstrap Instance;
        /// <summary>Сообщение для меню (почему вышли / не подключились).</summary>
        public static string LastMessage = "";

        public GameConfig config;
        NetworkManager nm;
        bool leaving;

        public static bool Arg(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs()) if (a == name) return true;
            return false;
        }

        public static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        void Awake()
        {
            Instance = this;
            nm = GetComponent<NetworkManager>();
        }

        void Start()
        {
            nm.ConnectionApprovalCallback = Approve;
            nm.OnClientDisconnectCallback += OnDisconnect;
            nm.OnClientConnectedCallback += id => Debug.Log("[NET] client connected: " + id);
        }

        void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
        {
            int max = config != null ? config.maxPlayers : 8;
            bool ok = nm.ConnectedClientsIds.Count < max;
            res.Approved = ok;
            res.CreatePlayerObject = ok;
            res.Pending = false;
            if (!ok) res.Reason = "Мотель полон";
        }

        void OnDisconnect(ulong id)
        {
            if (nm.IsServer) return;                 // хост: уход других клиентов не важен
            if (id != nm.LocalClientId) return;
            var r = nm.DisconnectReason;
            bool inMotel = SceneManager.GetActiveScene().name != MenuScene;
            string msg = !string.IsNullOrEmpty(r) ? r
                : inMotel ? "Соединение с хостом потеряно" : "Не удалось подключиться";
            Debug.Log("[NET] disconnected: " + msg);
            LeaveToMenu(msg);
        }

        /// <summary>Завершить сессию (хост или клиент) и вернуться в меню.</summary>
        public void LeaveToMenu(string message = "")
        {
            if (leaving) return;
            LastMessage = message ?? "";
            StartCoroutine(LeaveRoutine());
        }

        IEnumerator LeaveRoutine()
        {
            leaving = true;
            yield return null;
            if (nm.IsListening) nm.Shutdown();
            float t = 0f;
            while (nm.ShutdownInProgress && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (SceneManager.GetActiveScene().name != MenuScene) SceneManager.LoadScene(MenuScene, LoadSceneMode.Single);
            leaving = false;
        }
    }
}
