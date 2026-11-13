using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Friendslop
{
    /// <summary>
    /// Полировка P1/P2: режим скриншотов (флаг <c>-shot &lt;папка&gt;</c>, обычный запуск, не batchmode).
    /// Хостит, стартует смену, пишет PNG 1280x720 (рендер в RenderTexture, без IMGUI), логирует FPS и выходит.
    /// Без файла — 6 пресетов окружения. <c>-shotfile &lt;путь&gt;</c> — сценарий (строки по порядку, # — комментарий):
    ///   имя x y z lx ly lz [fov]            снимок камерой в x y z, смотрящей в l* (то же: shot имя ...)
    ///   shotfp имя                          снимок камерой игрока (вид от 1-го лица, viewmodel)
    ///   wait с                              подождать
    ///   spawner off                         не спавнить гостей
    ///   guest сид x y z yaw [down|brawl]    поставить замороженного гостя с видом по сиду
    ///   guestlive                           обычный гость (идёт к очереди, видна ходьба)
    ///   clearguests                         убрать поставленных гостей
    ///   held Тип [arg] | held none          предмет в руках хоста (Key 7, Money 50, Sledgehammer...)
    ///   view tp|fp                          tp — виден корпус игрока и предмет в руке модели, fp — viewmodel
    ///   player x y z yaw [pitch]            поставить локального игрока
    ///   knock 0|1                           уложить/поднять игрока
    ///   swing 0..1 | swing -1               заморозить фазу взмаха (для fp-кадра)
    ///   item Тип arg x y z yaw [pitch roll]  предмет на полу (неподвижный)
    ///   room N ready|occupied|needs [маска] [stain]  состояние номера (таблички)
    ///   clearitems                          убрать такие предметы
    ///   moon интенсивность режим сила       эксперименты со светом: режим 0 нет / 1 hard / 2 soft теней
    ///   handpose Тип fp|tp px py pz ex ey ez  положение предмета в руке (подбор без пересборки; ставит и вид fp/tp)
    ///   toggle ИмяОбъекта 0|1               включить/выключить объект сцены по имени
    ///   cam x y z lx ly lz [fov]            поставить камеру (без снимка) — для uishot
    ///   uishot имя [задержка_с]             снимок С интерфейсом (IMGUI): камера рисует прямо на экран, PNG берётся с экрана
    ///   uishotfp имя [задержка_с]           то же камерой игрока (вид от 1-го лица, viewmodel, подсказка под прицелом)
    ///   hl none|item|guest                  подсветка и подсказка у владельца: ближайший предмет / гость (как при наведении прицела)
    ///   fx hit x y z | fx money дельта x y z   эффекты: искры удара / всплывашка денег со звуком
    ///   taxdue 0|1                          выставить/снять налоговый счёт (HUD, тревога; 1 = ночь)
    ///   daynight day|night|dusk|auto        зафиксировать свет дня/ночи/сумерек (V1) или вернуть управление фазой (auto)
    ///   gameover                            «Мотель закрыт» (экран Game Over)
    /// <c>-shotwait N</c> — подождать N с смены до первого кадра (чтобы пришли гости).
    /// </summary>
    public class ShotRunner : MonoBehaviour
    {
        struct Preset
        {
            public string name; public Vector3 pos, look; public float fov;
            public Preset(string n, Vector3 p, Vector3 l, float f = 62f) { name = n; pos = p; look = l; fov = f; }
        }

        const int W = 1280, H = 720;

        static readonly Preset[] Defaults =
        {
            new Preset("1_lobby",     new Vector3(14f, 1.9f, 1f),       new Vector3(-3f, 1.2f, -13f), 72f),
            new Preset("2_reception", new Vector3(2.5f, 1.8f, -19f), new Vector3(0.5f, 1.25f, -13f), 68f),
            new Preset("3_storage",   new Vector3(9f, 1.9f, -3f),       new Vector3(19f, 1.0f, -12f),  68f),
            new Preset("4_corridor",  new Vector3(-16.5f, 1.7f, 12.5f), new Vector3(10f, 1.5f, 12.5f), 66f),
            new Preset("5_room",      new Vector3(-15.0f, 1.9f, 14.4f), new Vector3(-16.6f, 0.6f, 18.5f), 78f),
            new Preset("6_outside",   new Vector3(-8f, 2.2f, -40f),     new Vector3(0f, 2.4f, -20f),  64f),
        };

        public static void Create()
        {
            var go = new GameObject("ShotRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<ShotRunner>();
        }

        string outDir;
        Camera cam;
        RenderTexture rt;
        Texture2D tex;
        PlayerController pl;
        readonly List<Guest> staged = new List<Guest>();
        readonly List<WorldItem> stagedItems = new List<WorldItem>();
        float allSum; int allN, passN;

        void Start()
        {
            outDir = NetBootstrap.ArgValue("-shot");
            if (string.IsNullOrEmpty(outDir)) outDir = "shots";
            Directory.CreateDirectory(outDir);
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            StartCoroutine(Run());
        }

        void Fail(string why)
        {
            Debug.Log("[SHOT] FAIL " + why);
            Application.Quit(1);
        }

        IEnumerator Run()
        {
            float t = 0f;
            while ((GameState.Instance == null || !GameState.Instance.IsSpawned || PlayerController.Local == null || !PlayerController.Local.Placed) && t < 40f)
            { t += Time.unscaledDeltaTime; yield return null; }
            if (PlayerController.Local == null || GameState.Instance == null) { Fail("no player/state after 40 s"); yield break; }

            GameState.Instance.StartShift();
            var hud = FindAnyObjectByType<MotelHud>();
            if (hud != null) hud.enabled = false;
            pl = PlayerController.Local;
            pl.cam.enabled = false;

            var go = new GameObject("ShotCamera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Night;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);

            var script = LoadScript();
            Debug.Log($"[SHOT] start, {script.Count} commands -> {Path.GetFullPath(outDir)}");
            float extra = 0f;
            var w = NetBootstrap.ArgValue("-shotwait");                  // подождать N секунд смены (чтобы пришли гости)
            if (!string.IsNullOrEmpty(w)) float.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out extra);
            yield return new WaitForSeconds(1.5f + extra);

            foreach (var c in script)
            {
                var e = Exec(c);
                while (e.MoveNext()) yield return e.Current;
            }
            Debug.Log($"[SHOT] done {passN} shots, fps avg={(allN / Mathf.Max(0.0001f, allSum)):0}");
            yield return null;
            Application.Quit(0);
        }

        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        static float F(string[] s, int i, float def = 0f) => i < s.Length && float.TryParse(s[i], NumberStyles.Float, CI, out var v) ? v : def;

        IEnumerator Exec(string[] s)
        {
            switch (s[0])
            {
                case "shot":
                    yield return Snap(s[1], new Vector3(F(s, 2), F(s, 3), F(s, 4)), new Vector3(F(s, 5), F(s, 6), F(s, 7)), F(s, 8, 62f));
                    break;
                case "shotfp":
                    yield return SnapFp(s[1]);
                    break;
                case "wait":
                    yield return new WaitForSeconds(F(s, 1, 0.5f));
                    break;
                case "spawner":
                    GuestSpawner.Paused = true;
                    break;
                case "guest":
                {
                    var sp = GuestSpawner.Instance;
                    var g = sp != null ? sp.ServerSpawn(false) : null;
                    if (g == null) { Debug.Log("[SHOT] guest: spawn failed"); break; }
                    string flag = s.Length > 6 ? s[6] : "";
                    g.ShotStage((int)F(s, 1), new Vector3(F(s, 2), F(s, 3), F(s, 4)), F(s, 5), flag);
                    staged.Add(g);
                    yield return null;
                    break;
                }
                case "guestlive":     // обычный незамороженный гость (идёт к очереди: поза ходьбы)
                {
                    var sp = GuestSpawner.Instance;
                    var g = sp != null ? sp.ServerSpawn(false) : null;
                    if (g != null) staged.Add(g);
                    yield return null;
                    break;
                }
                case "clearguests":
                    foreach (var g in staged) if (g != null && g.IsSpawned) g.ServerDespawn();
                    staged.Clear();
                    break;
                case "held":
                {
                    var h = PlayerHands.Local;
                    if (h == null) break;
                    if (s.Length < 2 || s[1] == "none") h.Held.Value = default;
                    else if (Enum.TryParse<ItemType>(s[1], true, out var it)) h.Held.Value = new HeldItem(it, (int)F(s, 2));
                    yield return null;
                    break;
                }
                case "view":
                {
                    bool tp = s.Length > 1 && s[1] == "tp";
                    foreach (var r in pl.bodyRenderers) if (r != null) r.enabled = tp;
                    var h = PlayerHands.Local;
                    if (h != null) h.ShotThirdPerson(tp);
                    yield return null;
                    break;
                }
                case "player":
                    pl.ShotPose(new Vector3(F(s, 1), F(s, 2), F(s, 3)), F(s, 4), F(s, 5));
                    yield return null;
                    break;
                case "knock":
                {
                    var c = pl.GetComponent<PlayerCombat>();
                    if (c != null) { if (F(s, 1) > 0f) c.ServerKnockOut(600f); else c.ServerWake(); }
                    yield return null;
                    break;
                }
                case "swing":
                {
                    var c = pl.GetComponent<PlayerCombat>();
                    if (c != null) c.ShotSwing = F(s, 1, -1f);
                    yield return null;
                    break;
                }
                case "item":
                {
                    if (Enum.TryParse<ItemType>(s[1], true, out var it))
                    {
                        var pos = new Vector3(F(s, 3), F(s, 4), F(s, 5));
                        var wi = WorldItem.Spawn(new HeldItem(it, (int)F(s, 2)), pos, Vector3.zero);
                        if (wi != null)
                        {
                            wi.GetComponent<Rigidbody>().isKinematic = true;
                            wi.transform.SetPositionAndRotation(pos, Quaternion.Euler(F(s, 7), F(s, 6), F(s, 8)));
                            stagedItems.Add(wi);
                        }
                    }
                    yield return null;
                    break;
                }
                case "clearitems":
                    foreach (var w in stagedItems) if (w != null && w.IsSpawned) w.NetworkObject.Despawn();
                    stagedItems.Clear();
                    break;
                case "moon":
                {
                    var moon = GameObject.Find("Moon");
                    var l = moon != null ? moon.GetComponent<Light>() : null;
                    if (l == null) break;
                    l.intensity = F(s, 1, l.intensity);
                    int mode = (int)F(s, 2, 2);
                    l.shadows = mode == 0 ? LightShadows.None : mode == 1 ? LightShadows.Hard : LightShadows.Soft;
                    l.shadowStrength = F(s, 3, 1f);
                    if (s.Length > 4) l.shadowNearPlane = F(s, 4);
                    break;
                }
                case "handpose":      // handpose Тип fp|tp px py pz ex ey ez — подбор положения предмета в руке без пересборки
                {
                    var db = Items.Db;
                    if (db != null && Enum.TryParse<ItemType>(s[1], true, out var it))
                    {
                        bool fp = s[2] == "fp";
                        var off = fp ? db.handOffset : db.worldHandOffset;
                        var eul = fp ? db.handEuler : db.worldHandEuler;
                        off[(int)it] = new Vector3(F(s, 3), F(s, 4), F(s, 5));
                        eul[(int)it] = new Vector3(F(s, 6), F(s, 7), F(s, 8));
                        var h = PlayerHands.Local;
                        if (h != null) h.ShotThirdPerson(!fp);
                    }
                    yield return null;
                    break;
                }
                case "room":          // room N ready|occupied|needs [маска] [stain] — состояние номера (таблички)
                {
                    var r = Room.Get((int)F(s, 1));
                    if (r != null)
                    {
                        string st = s.Length > 2 ? s[2] : "ready";
                        if (st == "occupied") r.ServerSetOccupied();
                        else if (st == "needs") r.ServerSetNeedsService((int)F(s, 3), s.Length > 4 && s[4] == "stain");
                        else r.ServerSetReady();
                    }
                    yield return null;
                    break;
                }
                case "slab":          // slab y толщина — невидимая плита-тень над мотелем (эксперимент с протечкой лунного света)
                {
                    var sl = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(sl.GetComponent<Collider>());
                    sl.transform.position = new Vector3(0f, F(s, 1, 3.3f), 0f);
                    sl.transform.localScale = new Vector3(41.2f, F(s, 2, 0.2f), 41.2f);
                    sl.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                    break;
                }
                case "handaim":       // handaim Тип fp|tp dx dy dz gripY [roll] — направление «верха» предмета и точка хвата; в лог идут итоговые числа для Builder
                {
                    var db = Items.Db;
                    if (db != null && Enum.TryParse<ItemType>(s[1], true, out var it))
                    {
                        bool fp = s[2] == "fp";
                        var q = Quaternion.FromToRotation(Vector3.up, new Vector3(F(s, 3), F(s, 4), F(s, 5)).normalized) * Quaternion.Euler(0f, F(s, 7), 0f);
                        var pos = -(q * new Vector3(0f, F(s, 6), 0f));
                        var off = fp ? db.handOffset : db.worldHandOffset;
                        var eul = fp ? db.handEuler : db.worldHandEuler;
                        off[(int)it] = pos; eul[(int)it] = q.eulerAngles;
                        Debug.Log($"[SHOT] handaim {it} {(fp ? "fp" : "tp")} pos=({pos.x:0.###}, {pos.y:0.###}, {pos.z:0.###}) euler=({q.eulerAngles.x:0.#}, {q.eulerAngles.y:0.#}, {q.eulerAngles.z:0.#})");
                        var h = PlayerHands.Local;
                        if (h != null) h.ShotThirdPerson(!fp);
                    }
                    yield return null;
                    break;
                }
                case "cam":
                    cam.transform.position = new Vector3(F(s, 1), F(s, 2), F(s, 3));
                    cam.transform.rotation = Quaternion.LookRotation(new Vector3(F(s, 4), F(s, 5), F(s, 6)) - cam.transform.position, Vector3.up);
                    cam.fieldOfView = F(s, 7, 62f);
                    yield return null;
                    break;
                case "uishot":
                    yield return SnapUi(s[1], F(s, 2, 0.8f), false);
                    break;
                case "uishotfp":
                    yield return SnapUi(s[1], F(s, 2, 0.8f), true);
                    break;
                case "hl":
                {
                    var h = PlayerHands.Local;
                    if (h == null) break;
                    h.ShotAim = null;
                    if (s.Length > 1 && s[1] == "item") h.ShotAim = WorldItem.Nearest(pl.transform.position);
                    else if (s.Length > 1 && s[1] == "guest" && Guest.All.Count > 0) h.ShotAim = Guest.All[0];
                    yield return null;
                    break;
                }
                case "fx":
                {
                    if (s.Length > 1 && s[1] == "hit")
                    {
                        var p = new Vector3(F(s, 2), F(s, 3), F(s, 4));
                        Fx.Hit(p); Sfx.Play3D(Snd.Hit, p);
                    }
                    else if (s.Length > 1 && s[1] == "money") Fx.Money((int)F(s, 2), new Vector3(F(s, 3), F(s, 4), F(s, 5)));
                    yield return null;
                    break;
                }
                case "taxdue":
                {
                    var gs = GameState.Instance;
                    if (gs != null) { gs.TaxDue.Value = F(s, 1) > 0f; gs.Night.Value = gs.TaxDue.Value; gs.PhaseEnd.Value = gs.Now + 100.0; }
                    yield return null;
                    break;
                }
                case "daynight":
                {
                    var dn = DayNight.Instance;
                    if (dn != null)
                    {
                        string m = s.Length > 1 ? s[1] : "auto";
                        dn.Force(m == "day" ? 0f : m == "night" ? 1f : m == "dusk" ? 0.5f : -1f);
                    }
                    yield return null;
                    break;
                }
                case "gameover":
                    if (GameState.Instance != null) GameState.Instance.GameOver();
                    yield return null;
                    break;
                case "toggle":
                {
                    var o = GameObject.Find(s[1]);
                    if (o != null) o.SetActive(F(s, 2) > 0f);
                    else Debug.Log("[SHOT] toggle: no object " + s[1]);
                    break;
                }
                default:
                    Debug.Log("[SHOT] unknown command: " + s[0]);
                    break;
            }
        }

        IEnumerator Snap(string name, Vector3 pos, Vector3 look, float fov)
        {
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
            cam.fieldOfView = fov;
            yield return Capture(name);
        }

        IEnumerator SnapFp(string name)
        {
            var pc = pl.cam;
            pc.targetTexture = rt;
            pc.enabled = true;
            yield return Capture(name);
            pc.enabled = false;
            pc.targetTexture = null;
        }

        /// <summary>Снимок с IMGUI: рисуем камерой прямо на экран и забираем кадр с экрана (RenderTexture интерфейс не содержит).</summary>
        IEnumerator SnapUi(string name, float delay, bool fp)
        {
            var hud = FindAnyObjectByType<MotelHud>();
            if (hud != null) hud.enabled = true;
            var c = fp ? pl.cam : cam;
            if (fp) { cam.enabled = false; pl.cam.enabled = true; }
            c.targetTexture = null;
            yield return new WaitForSeconds(delay);
            yield return new WaitForEndOfFrame();
            var t = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), t.EncodeToPNG());
            Destroy(t);
            passN++;
            Debug.Log($"[SHOT] {name} ok (ui {Screen.width}x{Screen.height})");
            if (fp) { pl.cam.enabled = false; cam.enabled = true; }
            else cam.targetTexture = rt;
            if (hud != null) hud.enabled = false;
        }

        IEnumerator Capture(string name)
        {
            yield return new WaitForSeconds(0.6f);
            float sum = 0f, min = 1e9f; int n = 0, frames = 0;
            while (frames < 60)
            {
                yield return null;
                frames++;
                float dt = Time.unscaledDeltaTime;
                sum += dt; n++; if (dt > 0f) min = Mathf.Min(min, 1f / dt);
            }
            yield return new WaitForEndOfFrame();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            float fps = n / Mathf.Max(0.0001f, sum);
            allSum += sum; allN += n; passN++;
            Debug.Log($"[SHOT] {name} ok, fps avg={fps:0} min={min:0}");
        }

        List<string[]> LoadScript()
        {
            var list = new List<string[]>();
            var file = NetBootstrap.ArgValue("-shotfile");
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                foreach (var p in Defaults)
                    list.Add(new[] { "shot", p.name, p.pos.x.ToString(CI), p.pos.y.ToString(CI), p.pos.z.ToString(CI), p.look.x.ToString(CI), p.look.y.ToString(CI), p.look.z.ToString(CI), p.fov.ToString(CI) });
                return list;
            }
            var known = new HashSet<string> { "shot", "shotfp", "wait", "spawner", "guest", "clearguests", "held", "view", "player", "knock", "swing", "item", "clearitems", "moon", "toggle", "handpose", "slab", "room", "handaim", "guestlive", "cam", "uishot", "uishotfp", "hl", "fx", "taxdue", "gameover", "daynight" };
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var s = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (!known.Contains(s[0]))
                {
                    if (s.Length < 7) continue;                       // старый формат: имя x y z lx ly lz [fov]
                    var l2 = new List<string> { "shot" }; l2.AddRange(s);
                    s = l2.ToArray();
                }
                list.Add(s);
            }
            return list;
        }
    }
}
