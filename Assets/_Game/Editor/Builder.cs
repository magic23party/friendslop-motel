using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Friendslop.EditorTools
{
    public static partial class Builder
    {
        const string PrefabDir = "Assets/_Game/Prefabs";
        const string MatDir = "Assets/_Game/Materials";
        const string SceneDir = "Assets/_Game/Scenes";
        const string PlayerPath = PrefabDir + "/Player.prefab";
        const string NetPath = PrefabDir + "/Net.prefab";
        const string ListPath = PrefabDir + "/NetworkPrefabs.asset";
        const string MenuPath = SceneDir + "/Menu.unity";
        const string MotelPath = SceneDir + "/Motel.unity";

        [MenuItem("Tools/Friendslop/Build All")]
        public static void BuildAll()
        {
            EnsureConfig();
            var cfg = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfig.AssetPath);
            ApplyPlayerSettings();

            // C1: сеть, игрок, сцены Menu / Motel
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(SceneDir);
            var player = BuildPlayerPrefab(cfg);
            var db = BuildItems();                       // C2: визуалы предметов + сетевой префаб предмета на полу
            BuildGuestPrefab(cfg);                       // C4: сетевой префаб гостя
            var list = BuildNetworkPrefabsList(player, db.worldItemPrefab);
            var net = BuildNetPrefab(cfg, player, list);
            BuildMenuScene(cfg, net);
            BuildMotelScene(cfg, db);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuPath, true),
                new EditorBuildSettingsScene(MotelPath, true),
            };

            MigrateConfig(cfg);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BUILDALL OK]");
        }

        [MenuItem("Tools/Friendslop/Build Windows")]
        public static void BuildWindows()
        {
            BuildAll();
            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
            Directory.CreateDirectory("Build");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = "Build/Friendslop.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded) Debug.Log("[BUILD OK]");
            else { Debug.LogError("[BUILD FAIL] " + report.summary.result); EditorApplication.Exit(1); }
        }

        static void EnsureConfig()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GameConfig.AssetPath));
            if (AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfig.AssetPath) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameConfig>(), GameConfig.AssetPath);
        }

        // ---------------------------------------------------------------- настройки билда

        static void ApplyPlayerSettings()
        {
            // два экземпляра на одном ПК: оконный режим + работа в фоне
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
        }

        // ---------------------------------------------------------------- материалы

        static Material Mat(string name, Color color)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // ---------------------------------------------------------------- префаб игрока

        static GameObject BuildPlayerPrefab(GameConfig cfg)
        {
            var root = new GameObject("Player");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.4f; cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.3f; cc.skinWidth = 0.05f;

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.SyncRotAngleX = false; nt.SyncRotAngleZ = false;

            var body = Prim(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0, 0.9f, 0), new Vector3(0.8f, 0.9f, 0.8f), Mat("PlayerBody", Color.white));
            var white = Mat("Eye", Color.white);
            var black = Mat("Pupil", new Color(0.05f, 0.05f, 0.05f));
            var face = new GameObject("Face").transform;
            face.SetParent(root.transform, false);
            var renderers = new List<Renderer> { body.GetComponent<Renderer>() };
            foreach (float sx in new[] { -0.14f, 0.14f })
            {
                var eye = Prim(PrimitiveType.Sphere, "Eye", face, new Vector3(sx, 1.55f, 0.30f), Vector3.one * 0.17f, white, false);
                var pup = Prim(PrimitiveType.Sphere, "Pupil", face, new Vector3(sx, 1.55f, 0.385f), Vector3.one * 0.08f, black, false);
                renderers.Add(eye.GetComponent<Renderer>()); renderers.Add(pup.GetComponent<Renderer>());
            }
            var mouth = Prim(PrimitiveType.Cube, "Mouth", face, new Vector3(0, 1.30f, 0.34f), new Vector3(0.22f, 0.05f, 0.05f), black, false);
            renderers.Add(mouth.GetComponent<Renderer>());

            var pivot = new GameObject("CamPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0, 1.62f, 0);
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(pivot, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 75f; cam.enabled = false;
            camGo.tag = "MainCamera";
            var listener = camGo.AddComponent<AudioListener>();
            listener.enabled = false;

            // C2: руки — вид от первого лица (под камерой) и для чужих копий (у корпуса)
            var handView = new GameObject("HandView").transform;
            handView.SetParent(pivot, false);
            handView.localPosition = new Vector3(0.32f, -0.30f, 0.55f);
            var handWorld = new GameObject("HandWorld").transform;
            handWorld.SetParent(root.transform, false);
            handWorld.localPosition = new Vector3(0.35f, 1.15f, 0.45f);
            var hands = root.AddComponent<PlayerHands>();
            hands.config = cfg;
            hands.handView = handView;
            hands.handWorld = handWorld;

            var pc = root.AddComponent<PlayerController>();
            pc.config = cfg;
            pc.camPivot = pivot;
            pc.cam = cam;
            pc.listener = listener;
            pc.bodyRenderers = renderers.ToArray();
            pc.tintRenderer = body.GetComponent<Renderer>();
            pc.bodyCollider = body.GetComponent<Collider>();

            Items.SetLayer(root, Items.PlayerLayer);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static NetworkPrefabsList BuildNetworkPrefabsList(GameObject player, GameObject worldItem)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(ListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, ListPath);
            }
            foreach (var p in new List<NetworkPrefab>(list.PrefabList)) list.Remove(p);
            list.Add(new NetworkPrefab { Prefab = player });
            list.Add(new NetworkPrefab { Prefab = worldItem });   // сюда же дописывают свои сетевые префабы следующие карточки
            if (guestPrefabBuilt != null) list.Add(new NetworkPrefab { Prefab = guestPrefabBuilt });
            EditorUtility.SetDirty(list);
            return list;
        }

        static GameObject BuildNetPrefab(GameConfig cfg, GameObject player, NetworkPrefabsList list)
        {
            var go = new GameObject("Net");
            var tr = go.AddComponent<UnityTransport>();
            tr.ConnectTimeoutMS = 1000;
            tr.MaxConnectAttempts = 5;           // ~5 с до ошибки вместо минуты
            var nm = go.AddComponent<NetworkManager>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = tr;
            nm.NetworkConfig.PlayerPrefab = player;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.EnableSceneManagement = true;
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            nm.RunInBackground = true;
            var boot = go.AddComponent<NetBootstrap>();
            boot.config = cfg;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, NetPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---------------------------------------------------------------- сцены

        static void BuildMenuScene(GameConfig cfg, GameObject netPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("MenuCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.15f);
            cam.cullingMask = 0;
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";

            var ui = new GameObject("MenuUI").AddComponent<MenuUI>();
            ui.netPrefab = netPrefab;
            ui.config = cfg;
            EditorSceneManager.SaveScene(scene, MenuPath);
        }

        static void BuildMotelScene(GameConfig cfg, ItemDb db)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.50f, 0.50f, 0.55f);

            var lightGo = new GameObject("Sun");
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.0f; light.shadows = LightShadows.Soft;

            var floor = Mat("Floor", new Color(0.55f, 0.50f, 0.42f));
            var wall = Mat("Wall", new Color(0.75f, 0.72f, 0.66f));
            Prim(PrimitiveType.Cube, "Floor", null, new Vector3(0, -0.1f, 0), new Vector3(40, 0.2f, 40), floor);
            const float h = 3f;
            Prim(PrimitiveType.Cube, "Wall_N", null, new Vector3(0, h / 2, 20.1f), new Vector3(40.4f, h, 0.2f), wall);
            Prim(PrimitiveType.Cube, "Wall_S", null, new Vector3(0, h / 2, -20.1f), new Vector3(40.4f, h, 0.2f), wall);
            Prim(PrimitiveType.Cube, "Wall_E", null, new Vector3(20.1f, h / 2, 0), new Vector3(0.2f, h, 40.4f), wall);
            Prim(PrimitiveType.Cube, "Wall_W", null, new Vector3(-20.1f, h / 2, 0), new Vector3(0.2f, h, 40.4f), wall);
            // цветные столбы-ориентиры в углах
            var cols = new[] { Color.red, Color.blue, Color.green, Color.yellow };
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Cube, "Pillar_" + i, null, new Vector3(i % 2 == 0 ? -17f : 17f, 1.5f, i < 2 ? 2.5f : -17f),
                    new Vector3(1.2f, 3f, 1.2f), Mat("Pillar" + i, cols[i]));

            // точки спавна: 2 ряда по 4, лицом к +Z
            var sps = new GameObject("SpawnPoints").transform;
            for (int i = 0; i < 8; i++)
            {
                var sp = new GameObject("Spawn_" + i);
                sp.transform.SetParent(sps, false);
                sp.transform.position = new Vector3((i % 4 - 1.5f) * 2.2f, 0.05f, -4f - (i / 4) * 2.2f);
                sp.AddComponent<SpawnPoint>().index = i;
            }

            var gsGo = new GameObject("GameState");
            gsGo.AddComponent<NetworkObject>();
            gsGo.AddComponent<GameState>().config = cfg;

            var hud = new GameObject("MotelHud").AddComponent<MotelHud>();
            hud.config = cfg;

            BuildMotelLayout(cfg, db);                   // C2: лобби, склад, доска ключей, коридор с номерами
            BuildGuestSystems(cfg);                      // C4: спавнер гостей
            BakeNavMesh();                               // C4: NavMeshSurface (после всей геометрии)

            var fb = new GameObject("FallbackCamera");
            fb.transform.position = new Vector3(0, 6f, -14f);
            fb.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
            fb.AddComponent<Camera>();
            fb.AddComponent<AudioListener>();
            fb.AddComponent<FallbackCamera>();

            EditorSceneManager.SaveScene(scene, MotelPath);
        }
    }
}
