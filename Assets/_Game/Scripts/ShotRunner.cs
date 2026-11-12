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
    /// Полировка P1: режим скриншотов (флаг <c>-shot &lt;папка&gt;</c>, обычный запуск, не batchmode).
    /// Хостит, стартует смену, ставит свою камеру по пресетам, пишет PNG 1280x720 (рендер в RenderTexture, без IMGUI),
    /// логирует FPS на каждом пресете и выходит. Пресеты можно заменить файлом <c>-shotfile &lt;путь&gt;</c>:
    /// строки "имя x y z lx ly lz [fov]" (камера в x y z смотрит в l*). <c>-shotwait N</c> — подождать N с смены до первого кадра.
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
        readonly List<string> fpsLines = new List<string>();

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
            var nm = NetworkManager.Singleton;
            while ((GameState.Instance == null || !GameState.Instance.IsSpawned || PlayerController.Local == null || !PlayerController.Local.Placed) && t < 40f)
            { t += Time.unscaledDeltaTime; yield return null; }
            if (PlayerController.Local == null || GameState.Instance == null) { Fail("no player/state after 40 s"); yield break; }

            GameState.Instance.StartShift();
            var hud = FindAnyObjectByType<MotelHud>();
            if (hud != null) hud.enabled = false;
            var pl = PlayerController.Local;
            pl.cam.enabled = false;

            var go = new GameObject("ShotCamera");
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

            var presets = LoadPresets();
            Debug.Log($"[SHOT] start, {presets.Count} presets -> {Path.GetFullPath(outDir)}");
            float extra = 0f;
            var w = NetBootstrap.ArgValue("-shotwait");                  // подождать N секунд смены (чтобы пришли гости)
            if (!string.IsNullOrEmpty(w)) float.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out extra);
            yield return new WaitForSeconds(1.5f + extra);

            float allSum = 0f; int allN = 0, passN = 0;
            foreach (var p in presets)
            {
                cam.transform.position = p.pos;
                cam.transform.rotation = Quaternion.LookRotation(p.look - p.pos, Vector3.up);
                cam.fieldOfView = p.fov;
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
                File.WriteAllBytes(Path.Combine(outDir, p.name + ".png"), tex.EncodeToPNG());
                float fps = n / Mathf.Max(0.0001f, sum);
                allSum += sum; allN += n; passN++;
                Debug.Log($"[SHOT] {p.name} ok, fps avg={fps:0} min={min:0}");
            }
            Debug.Log($"[SHOT] done {passN} shots, fps avg={(allN / Mathf.Max(0.0001f, allSum)):0}");
            yield return null;
            Application.Quit(0);
        }

        List<Preset> LoadPresets()
        {
            var file = NetBootstrap.ArgValue("-shotfile");
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return new List<Preset>(Defaults);
            var list = new List<Preset>();
            var ci = CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var s = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (s.Length < 7) continue;
                float F(int i) => float.Parse(s[i], ci);
                list.Add(new Preset(s[0], new Vector3(F(1), F(2), F(3)), new Vector3(F(4), F(5), F(6)), s.Length > 7 ? F(7) : 62f));
            }
            return list.Count > 0 ? list : new List<Preset>(Defaults);
        }
    }
}
