using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Friendslop
{
    /// <summary>
    /// Главное меню (IMGUI, P3): название игры «неоном», карточка с ником/IP и кнопками, фон — живая камера, облетающая мотель
    /// (сцена Menu содержит геометрию мотеля без скриптов, её собирает Builder). Стили — UiStyle.
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        public GameObject netPrefab;   // префаб NetworkManager + UnityTransport + NetBootstrap
        public GameConfig config;

        string nick, ip, status = "";

        // фоновая камера: плавные пролёты по мотелю с затемнением на стыках
        struct Seg
        {
            public Vector3 p0, l0, p1, l1; public float fov;
            public Seg(Vector3 p0, Vector3 l0, Vector3 p1, Vector3 l1, float fov) { this.p0 = p0; this.l0 = l0; this.p1 = p1; this.l1 = l1; this.fov = fov; }
        }

        static readonly Seg[] Segs =
        {
            new Seg(new Vector3(-9f, 2.0f, -41f), new Vector3(0f, 2.6f, -20f), new Vector3(-2.5f, 2.3f, -39f), new Vector3(0f, 2.6f, -20f), 64f),         // вывеска и фасад
            new Seg(new Vector3(14f, 1.9f, 1f), new Vector3(-3f, 1.2f, -13f), new Vector3(11f, 1.8f, -2f), new Vector3(-2f, 1.2f, -13f), 72f),            // лобби
            new Seg(new Vector3(2.5f, 1.8f, -19f), new Vector3(0.5f, 1.25f, -13f), new Vector3(4.2f, 1.75f, -18f), new Vector3(0f, 1.3f, -13f), 68f),    // ресепшен
            new Seg(new Vector3(-16.5f, 1.7f, 12.5f), new Vector3(10f, 1.5f, 12.5f), new Vector3(-11f, 1.7f, 12.5f), new Vector3(10f, 1.5f, 12.5f), 66f), // коридор
        };
        const float SegTime = 13f, FadeTime = 0.8f;
        Camera bgCam;
        float camT;
        static GUIStyle sub;

        void Awake()
        {
            // NetworkManager переживает смену сцен: при возврате в меню второй не создаём.
            if (NetworkManager.Singleton == null) Instantiate(netPrefab);
            GameConfig.Active = config;
            bgCam = Camera.main;
            // страховка (как в MotelMap): у TextMesh без материала текст не рисуется
            foreach (var tm in FindObjectsByType<TextMesh>(FindObjectsInactive.Include))
            {
                var mr = tm.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial == null && tm.font != null) mr.sharedMaterial = tm.font.material;
            }
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
            if (NetBootstrap.Arg("-shotmenu")) StartCoroutine(MenuShots());                       // P3: скриншоты меню
            else if (NetBootstrap.Arg("-shot")) { ShotRunner.Create(); CreateHost(); }            // P1: режим скриншотов
            else if (NetBootstrap.Arg("-autohost")) CreateHost();
            else if (NetBootstrap.Arg("-autojoin")) { ip = NetBootstrap.ArgValue("-autojoin") ?? ip; Join(); }
        }

        void Update()
        {
            if (!string.IsNullOrEmpty(NetBootstrap.LastMessage)) { status = NetBootstrap.LastMessage; NetBootstrap.LastMessage = ""; }
            camT += Time.unscaledDeltaTime;
            MoveCamera();
        }

        void MoveCamera()
        {
            if (bgCam == null) return;
            int i = Mathf.FloorToInt(camT / SegTime) % Segs.Length;
            float k = Mathf.SmoothStep(0f, 1f, (camT % SegTime) / SegTime);
            var sg = Segs[i];
            bgCam.transform.position = Vector3.Lerp(sg.p0, sg.p1, k);
            bgCam.transform.rotation = Quaternion.LookRotation(Vector3.Lerp(sg.l0, sg.l1, k) - bgCam.transform.position, Vector3.up);
            bgCam.fieldOfView = sg.fov;
        }

        /// <summary>Скриншоты меню: -shotmenu &lt;папка&gt; — по кадру из разных пролётов камеры, с IMGUI.</summary>
        System.Collections.IEnumerator MenuShots()
        {
            var dir = NetBootstrap.ArgValue("-shotmenu");
            if (string.IsNullOrEmpty(dir)) dir = "shots";
            System.IO.Directory.CreateDirectory(dir);
            var names = new[] { "menu_facade", "menu_lobby", "menu_reception" };
            for (int i = 0; i < names.Length; i++)
            {
                camT = i * SegTime + 4.5f;
                yield return new WaitForSeconds(0.8f);
                camT = i * SegTime + 4.5f;
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, names[i] + ".png"), tex.EncodeToPNG());
                Destroy(tex);
                Debug.Log("[SHOT] " + names[i] + " ok");
            }
            Application.Quit(0);
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
            float screenW = Ui.W;
            // затемнение слева — под название и карточку; плавный фейд на стыках пролётов камеры
            UiStyle.FadeLeft(new Rect(0, 0, screenW * 0.62f, Ui.H), 0.88f);
            float inSeg = camT % SegTime;
            float fade = Mathf.Max(1f - inSeg / FadeTime, (inSeg - (SegTime - FadeTime)) / FadeTime);
            if (fade > 0f && camT > FadeTime) UiStyle.Dim(new Rect(0, 0, screenW, Ui.H), Mathf.Clamp01(fade));

            // название игры — вывеской
            const float x0 = 64f;
            UiStyle.NeonTitle(new Rect(x0 - 40, 46, 560, 78), "Friendslop", UiStyle.Neon, new Color(1f, 0.92f, 0.94f), 68);
            UiStyle.NeonTitle(new Rect(x0 - 40, 116, 560, 58), "M O T E L", UiStyle.Teal, new Color(0.88f, 1f, 0.97f), 40);
            if (sub == null) sub = new GUIStyle(UiStyle.Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic };
            Ui.Shadowed(new Rect(x0 - 40, 168, 560, 26), "ночная смена на придорожном мотеле", sub, new Color(1f, 0.93f, 0.8f, 0.85f));

            const float w = 440f;
            float x = x0, y = 214f;
            var nm = NetworkManager.Singleton;
            bool connecting = Busy && nm != null && !nm.IsServer;
            float h = connecting ? 372f : 344f;
            UiStyle.Plate(new Rect(x - 20, y - 6, w + 40, h), UiStyle.Kind.Card);
            y += 12;

            GUI.enabled = !Busy;
            Ui.Shadowed(new Rect(x, y, w, 24), "Ник", UiStyle.Small, UiStyle.TealLight); y += 24;
            nick = GUI.TextField(new Rect(x, y, w, 38), nick ?? "", 16, UiStyle.Field); y += 48;
            Ui.Shadowed(new Rect(x, y, w, 24), "IP хоста", UiStyle.Small, UiStyle.TealLight); y += 24;
            ip = GUI.TextField(new Rect(x, y, w, 38), ip ?? "", 45, UiStyle.Field); y += 54;

            if (UiStyle.Btn(new Rect(x, y, w, 46), "Создать", true)) CreateHost();
            y += 56;
            if (UiStyle.Btn(new Rect(x, y, w, 46), "Подключиться")) Join();
            y += 56;
            GUI.enabled = true;

            if (connecting)
            {
                Ui.Shadowed(new Rect(x, y, w, 30), "Подключение…", UiStyle.Center, UiStyle.Mustard); y += 34;
                if (UiStyle.Btn(new Rect(x, y, w, 40), "Отмена")) { nm.Shutdown(); status = ""; }
                y += 46;
            }
            else
            {
                if (UiStyle.Btn(new Rect(x, y, w, 40), "Выход")) Application.Quit();
                y += 46;
            }

            if (!string.IsNullOrEmpty(status))
                Ui.Shadowed(new Rect(x, y + 14, w, 60), status, UiStyle.Center, new Color(1f, 0.55f, 0.45f));
        }
    }
}
