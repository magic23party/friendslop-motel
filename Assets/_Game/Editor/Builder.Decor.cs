using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Friendslop.EditorTools
{
    /// <summary>
    /// P1: арт-дирекшн — ночной придорожный мотель, low-poly, плоские цвета (<see cref="Palette"/>).
    /// Декор — только визуал (без коллайдеров), чтобы не менять NavMesh и проходы; исключение — автомат с напитками (простой бокс у стены).
    /// </summary>
    public static partial class Builder
    {
        const string PostPath = MatDir + "/MotelPost.asset";

        // ---------------------------------------------------------------- помощники

        static Material MatE(string name, Color c, Color emit, float k)
        {
            var m = Mat(name, c);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emit * k);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Куб без коллайдера (визуал). Тени не отбрасывает, статический батчинг.</summary>
        static GameObject Box(Transform p, string n, Vector3 pos, Vector3 size, Material m, bool shadows = false)
        {
            var g = Prim(PrimitiveType.Cube, n, p, pos, size, m, false);
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
            if (!shadows) g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        static GameObject BoxR(Transform p, string n, Vector3 pos, Vector3 size, Material m, Vector3 euler)
        {
            var g = Box(p, n, pos, size, m);
            g.transform.localRotation = Quaternion.Euler(euler);
            return g;
        }

        static GameObject Cyl(Transform p, string n, Vector3 pos, float dia, float h, Material m, Vector3? euler = null, bool shadows = false)
        {
            var g = Prim(PrimitiveType.Cylinder, n, p, pos, new Vector3(dia, h / 2f, dia), m, false);
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
            if (!shadows) g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
            return g;
        }

        static GameObject Ball(Transform p, string n, Vector3 pos, Vector3 size, Material m, bool shadows = false)
        {
            var g = Prim(PrimitiveType.Sphere, n, p, pos, size, m, false);
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
            if (!shadows) g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        static Light AddLight(Transform p, string n, Vector3 pos, Color c, float intensity, float range, LightType type = LightType.Point)
        {
            var go = Empty(n, p, pos);
            var l = go.AddComponent<Light>();
            l.type = type; l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>Отрезок толщиной s между двумя точками в плоскости XY (для неоновых букв).</summary>
        static void Seg(Transform g, Vector2 a, Vector2 b, float s, float depth, Material m)
        {
            var mid = (a + b) / 2f;
            float len = (b - a).magnitude + s;
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            BoxR(g, "seg", new Vector3(mid.x, mid.y, 0), new Vector3(len, s, depth), m, new Vector3(0, 0, ang));
        }

        // ---------------------------------------------------------------- свет и пост-эффекты (сцена Motel)

        static void BuildLightingAndPost()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Palette.Ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Palette.Night;
            RenderSettings.fogDensity = 0.003f;

            // холодный «лунный» направленный свет — единственный свет с тенями (доп. источники теней не дают)
            var sun = new GameObject("Moon");
            sun.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Palette.Moon;
            light.intensity = 2.0f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1.0f;
            // P2-fix: плоскость ближнего отсечения теней по умолчанию 0.2 м — крыша/потолок выше объёма каскада отсекались и не давали тени,
            // лунный свет «протекал» внутрь (голубой градиент на стенах). 10 м хватает, чтобы крыша всегда была в карте теней.
            light.shadowNearPlane = 10f;

            // глобальный Volume: тонмаппинг, bloom (неон, лампы), vignette, цвет
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath) != null) AssetDatabase.DeleteAsset(PostPath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "MotelPost";
            AssetDatabase.CreateAsset(profile, PostPath);

            var tm = AddVolComp<Tonemapping>(profile);
            tm.mode.Override(TonemappingMode.Neutral);

            var bloom = AddVolComp<Bloom>(profile);
            bloom.threshold.Override(0.8f);
            bloom.intensity.Override(1.6f);
            bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(1f, 0.92f, 0.85f));

            var vig = AddVolComp<Vignette>(profile);
            vig.intensity.Override(0.28f);
            vig.smoothness.Override(0.45f);

            var ca = AddVolComp<ColorAdjustments>(profile);
            ca.postExposure.Override(0.0f);
            ca.contrast.Override(10f);
            ca.saturation.Override(10f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var vol = new GameObject("PostFX").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10;
            vol.sharedProfile = profile;
        }

        static T AddVolComp<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = ScriptableObject.CreateInstance<T>();
            c.name = typeof(T).Name;
            c.active = true;
            AssetDatabase.AddObjectToAsset(c, profile);
            profile.components.Add(c);
            return c;
        }

        /// <summary>Настройка камеры под URP: post-processing, SMAA, ночной фон.</summary>
        static void SetupCamera(Camera cam)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Night;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }

        // ---------------------------------------------------------------- общий декор

        static void BuildDecor()
        {
            var root = new GameObject("Decor").transform;
            DecorShell(root);
            DecorLobbyFloor(root);
            DecorLobby(root);
            DecorReception(root);
            DecorCorridor(root);
            BuildExterior(root);
        }

        static Material PlafonMat => MatE("Plafon", new Color(1f, 0.93f, 0.75f), Palette.LampWarm, 2.5f);

        static void Plafon(Transform p, Vector3 pos, float dia, float intensity, float range, bool light = true)
        {
            var g = Empty("Plafon", p, pos);
            Cyl(g.transform, "Ring", new Vector3(0, 0.03f, 0), dia * 1.18f, 0.05f, Mat("PlafonRing", Palette.WoodDark));
            Cyl(g.transform, "Shade", new Vector3(0, -0.01f, 0), dia, 0.05f, PlafonMat);
            if (light)
            {
                // конус вниз: потолок над лампой не пересвечивается, пол и стены освещены
                var l = AddLight(g.transform, "Light", new Vector3(0, -0.12f, 0), Palette.LampWarm, intensity, range, LightType.Spot);
                l.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                l.spotAngle = 172f; l.innerSpotAngle = 110f;
            }
        }

        /// <summary>Плинтус, панели-вайнскот и поручень вдоль стены. basePos — на лицевой плоскости стены, yaw: локальный +Z смотрит в помещение.</summary>
        static void WallTrim(Transform r, Vector3 basePos, float yaw, float len, string name)
        {
            var g = Empty(name, r, basePos);
            g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var t = g.transform;
            Box(t, "Wainscot", new Vector3(0, 0.5f, 0.015f), new Vector3(len, 1.0f, 0.03f), Mat("Wainscot", Palette.TealDark));
            Box(t, "Rail", new Vector3(0, 1.03f, 0.03f), new Vector3(len, 0.06f, 0.06f), Mat("Rail", Palette.Cream));
            Box(t, "Baseboard", new Vector3(0, 0.09f, 0.04f), new Vector3(len, 0.18f, 0.05f), Mat("Baseboard", Palette.WoodDark));
        }

        /// <summary>Окно: рама, переплёт, подоконник, стекло. Локальный +Z смотрит на зрителя.</summary>
        static void Window(Transform r, Vector3 pos, float yaw, float w, float h, Material pane, bool curtains)
        {
            var g = Empty("Window", r, pos);
            g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var t = g.transform;
            var frame = Mat("WindowFrame", Palette.WoodDark);
            Box(t, "Pane", new Vector3(0, 0, 0.005f), new Vector3(w, h, 0.01f), pane);
            Box(t, "Top", new Vector3(0, h / 2 + 0.04f, 0.03f), new Vector3(w + 0.2f, 0.09f, 0.06f), frame);
            Box(t, "Bottom", new Vector3(0, -h / 2 - 0.04f, 0.03f), new Vector3(w + 0.2f, 0.09f, 0.06f), frame);
            Box(t, "Left", new Vector3(-w / 2 - 0.04f, 0, 0.03f), new Vector3(0.09f, h + 0.18f, 0.06f), frame);
            Box(t, "Right", new Vector3(w / 2 + 0.04f, 0, 0.03f), new Vector3(0.09f, h + 0.18f, 0.06f), frame);
            Box(t, "MullionV", new Vector3(0, 0, 0.03f), new Vector3(0.05f, h, 0.05f), frame);
            Box(t, "MullionH", new Vector3(0, 0, 0.03f), new Vector3(w, 0.05f, 0.05f), frame);
            Box(t, "Sill", new Vector3(0, -h / 2 - 0.1f, 0.08f), new Vector3(w + 0.4f, 0.05f, 0.18f), Mat("WindowSill", Palette.Cream));
            if (curtains)
            {
                var cm = Mat("Curtain", Palette.DarkRed);
                Box(t, "CurtainL", new Vector3(-w / 2 + 0.12f, 0, 0.07f), new Vector3(0.26f, h + 0.1f, 0.06f), cm);
                Box(t, "CurtainR", new Vector3(w / 2 - 0.12f, 0, 0.07f), new Vector3(0.26f, h + 0.1f, 0.06f), cm);
            }
        }

        static void DecorShell(Transform r)
        {
            // потолок (кремовый снизу) и крыша (тёмная сверху, отбрасывает тень — внутрь лунный свет не попадает)
            Box(r, "Ceiling", new Vector3(0, 3.02f, 0), new Vector3(40.4f, 0.04f, 40.4f), MatE("Ceiling", Palette.Ceiling, new Color(0.9f, 0.62f, 0.34f), 0.20f));
            Box(r, "Roof", new Vector3(0, 3.14f, 0), new Vector3(41.2f, 0.2f, 41.2f), Mat("Roof", Palette.Roof), true);
            // парапет на крыше
            var par = Mat("Parapet", Palette.WoodDark);
            Box(r, "Parapet_S", new Vector3(0, 3.4f, -20.5f), new Vector3(41.2f, 0.3f, 0.2f), par, true);
            Box(r, "Parapet_N", new Vector3(0, 3.4f, 20.5f), new Vector3(41.2f, 0.3f, 0.2f), par, true);
            Box(r, "Parapet_W", new Vector3(-20.5f, 3.4f, 0), new Vector3(0.2f, 0.3f, 41.2f), par, true);
            Box(r, "Parapet_E", new Vector3(20.5f, 3.4f, 0), new Vector3(0.2f, 0.3f, 41.2f), par, true);
            var hv = Mat("Hvac", Palette.Metal);
            Box(r, "Hvac1", new Vector3(-10f, 3.9f, 6f), new Vector3(2.6f, 1.0f, 1.8f), hv, true);
            Box(r, "Hvac2", new Vector3(8f, 3.75f, -4f), new Vector3(1.8f, 0.7f, 1.4f), hv, true);
            Cyl(r, "Vent", new Vector3(-4f, 3.7f, -8f), 0.7f, 0.6f, hv, null, true);

            // внутренние панели и плинтусы лобби и боковых проходов
            WallTrim(r, new Vector3(0, 0, -20.0f), 0f, 40f, "Trim_S");
            WallTrim(r, new Vector3(-20.0f, 0, 0), 90f, 40f, "Trim_W");
            WallTrim(r, new Vector3(20.0f, 0, 0), -90f, 40f, "Trim_E");
            // P4: стенка комнат со стороны лобби разорвана центральным проходом (x ±1.55)
            WallTrim(r, new Vector3(-10.5f, 0, 5.0f), 180f, 17.9f, "Trim_RoomsBackW");
            WallTrim(r, new Vector3(10.5f, 0, 5.0f), 180f, 17.9f, "Trim_RoomsBackE");
            WallTrim(r, new Vector3(-1.5f, 0, 8.0f), 90f, 6f, "Trim_PassageW");
            WallTrim(r, new Vector3(1.5f, 0, 8.0f), -90f, 6f, "Trim_PassageE");

            // окна: изнутри — холодная ночь, снаружи — тёплый свет интерьера
            var night = MatE("PaneNight", new Color(0.05f, 0.09f, 0.20f), new Color(0.07f, 0.14f, 0.34f), 1.1f);
            var warm = MatE("PaneWarm", new Color(1f, 0.75f, 0.40f), new Color(1f, 0.68f, 0.30f), 1.1f);
            foreach (float x in new[] { -14f, -8f, 9f })      // V3: на месте окна x=15 — табло номеров на фасаде
            {
                Window(r, new Vector3(x, 1.75f, -20.0f), 0f, 1.6f, 1.2f, night, true);
                Window(r, new Vector3(x, 1.75f, -20.2f), 180f, 1.6f, 1.2f, warm, false);
            }
            foreach (float z in new[] { -14f, -3f })
            {
                Window(r, new Vector3(-20.0f, 1.75f, z), 90f, 1.6f, 1.2f, night, true);
                Window(r, new Vector3(-20.2f, 1.75f, z), -90f, 1.6f, 1.2f, warm, false);
            }
            foreach (float z in new[] { -3f })
            {
                Window(r, new Vector3(20.0f, 1.75f, z), -90f, 1.6f, 1.2f, night, true);
                Window(r, new Vector3(20.2f, 1.75f, z), 90f, 1.6f, 1.2f, warm, false);
            }
        }

        static void DecorLobbyFloor(Transform r)
        {
            // шахматная плитка 2×2 м: светлая основа (Floor) + тёмные бирюзовые плитки. Коридор и номера — свой пол.
            var tiles = Empty("Tiles", r, Vector3.zero).transform;
            var dark = Mat("TileDark", Palette.FloorTile);
            for (int i = 0; i < 20; i++)
                for (int j = 0; j < 20; j++)
                {
                    if ((i + j) % 2 != 0) continue;
                    float cx = -19f + 2f * i, cz = -19f + 2f * j;
                    if (cz + 1f > 5.01f && Mathf.Abs(cx) < 18f && Mathf.Abs(cx) > 1.5f) continue;     // P4: плитка продолжается по центральному проходу
                    Box(tiles, "Tile", new Vector3(cx, 0.005f, cz), new Vector3(1.97f, 0.012f, 1.97f), dark);
                }
        }

        static void DecorLobby(Transform r)
        {
            // ковёр у стойки (очередь гостей)
            Box(r, "RugA", new Vector3(0, 0.014f, -17.0f), new Vector3(4.6f, 0.008f, 5.4f), Mat("RugOuter", Palette.DarkRed));
            Box(r, "RugB", new Vector3(0, 0.0175f, -17.0f), new Vector3(4.2f, 0.004f, 5.0f), Mat("RugBorder", Palette.Mustard));
            Box(r, "RugC", new Vector3(0, 0.020f, -17.0f), new Vector3(4.0f, 0.004f, 4.8f), Mat("RugInner", Palette.Carpet));

            // потолочные плафоны лобби (тёплый свет, без теней)
            foreach (float x in new[] { -12f, 0f, 12f })
                foreach (float z in new[] { -16f, -8f, 0f })
                    Plafon(r, new Vector3(x, 2.97f, z), 0.7f, 12f, 12f);
            Plafon(r, new Vector3(16.5f, 2.97f, -9f), 0.6f, 9f, 10f);
            Plafon(r, new Vector3(16.5f, 2.97f, -15f), 0.6f, 9f, 10f);

            // картины на стене комнат (со стороны лобби) и колонны с карнизом
            for (int i = 0; i < 6; i++) Picture(r, new Vector3((i < 3 ? -15f + 6f * i : 3f + 6f * (i - 3)), 1.85f, 4.96f), 180f, i);     // P4: по три с каждой стороны прохода
            // над проходом — балка с указателем «НОМЕРА» (читается из лобби)
            Box(r, "PassageHeader", new Vector3(0, 2.62f, 5.0f), new Vector3(3.2f, 0.76f, 0.2f), Mat("PassageHeader", Palette.TealDark));
            Box(r, "PassageHeaderTrim", new Vector3(0, 2.23f, 4.9f), new Vector3(3.2f, 0.06f, 0.05f), Mat("PassageHeaderTrim", Palette.Cream));
            Label(r, "НОМЕРА", new Vector3(0, 2.62f, 4.88f), Vector3.back, 0.42f, Palette.Cream, "Label_Rooms");
            foreach (var cp in new[] { new Vector2(-17f, 2.5f), new Vector2(17f, 2.5f), new Vector2(-17f, -17f), new Vector2(17f, -17f) })
            {
                Box(r, "ColumnBase", new Vector3(cp.x, 0.15f, cp.y), new Vector3(1.4f, 0.3f, 1.4f), Mat("ColumnTrim", Palette.WoodDark));
                Box(r, "ColumnCap", new Vector3(cp.x, 2.88f, cp.y), new Vector3(1.4f, 0.24f, 1.4f), Mat("ColumnCap", Palette.Cream));
            }

            // растения в углах лобби
            Plant(r, new Vector3(-18.9f, 0, -18.9f), 1.3f);
            Plant(r, new Vector3(18.8f, 0, 3.8f), 1.2f);
            Plant(r, new Vector3(-18.9f, 0, 3.6f), 1.2f);
            Plant(r, new Vector3(18.9f, 0, -19.0f), 1.0f);       // V3: переехало из-под табло номеров у входа

            // автомат с напитками в западном проходе (единственный декор с коллайдером — простой бокс у стены)
            VendingMachine(r, new Vector3(-19.6f, 0, 12.5f));       // P4: боковой проход закрыт, автомат переехал в западный конец коридора
        }

        /// <summary>Картина на стене: рама, холст, «солнце» и «холм» (плоские цвета). Локальный +Z смотрит на зрителя.</summary>
        static void Picture(Transform r, Vector3 pos, float yaw, int seed)
        {
            var g = Empty("Picture", r, pos).transform;
            g.localRotation = Quaternion.Euler(0, yaw, 0);
            var canvases = new[] { Palette.Teal, Palette.DarkRed, Palette.Mustard, Palette.TealDark, new Color(0.25f, 0.35f, 0.75f) };
            var hills = new[] { Palette.TealDark, Palette.WoodDark, Palette.Red, Palette.Green, Palette.WoodDark };
            int k = seed % canvases.Length;
            Box(g, "Frame", new Vector3(0, 0, 0.02f), new Vector3(1.4f, 0.95f, 0.04f), Mat("PictureWood", Palette.WoodDark));
            Box(g, "Canvas", new Vector3(0, 0, 0.045f), new Vector3(1.26f, 0.81f, 0.02f), Mat("PictureC" + k, canvases[k]));
            Ball(g, "Sun", new Vector3(0.3f, 0.15f, 0.06f), new Vector3(0.28f, 0.28f, 0.02f), Mat("PictureSun", Palette.Mustard));
            Box(g, "Hill", new Vector3(-0.1f, -0.25f, 0.06f), new Vector3(0.9f, 0.22f, 0.02f), Mat("PictureH" + k, hills[k]));
        }

        static void VendingMachine(Transform r, Vector3 pos)
        {
            var vm = Empty("VendingMachine", r, pos);
            var body = Prim(PrimitiveType.Cube, "Body", vm.transform, new Vector3(0, 0.95f, 0), new Vector3(0.72f, 1.9f, 1.0f), Mat("VendBody", Palette.Red));
            body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var t = vm.transform;
            Box(t, "Glass", new Vector3(0.37f, 1.2f, -0.05f), new Vector3(0.04f, 1.15f, 0.72f), MatE("VendGlass", new Color(0.55f, 0.75f, 0.8f), new Color(0.5f, 0.85f, 0.9f), 0.9f));
            var cans = new[] { Palette.Teal, Palette.Mustard, Palette.Towel, Palette.Soap, Palette.Mop, Palette.Cream };
            for (int row = 0; row < 4; row++)
                for (int c = 0; c < 5; c++)
                    Box(t, "Can", new Vector3(0.40f, 0.82f + row * 0.28f, -0.29f + c * 0.145f), new Vector3(0.04f, 0.17f, 0.085f), Mat("Can" + ((row + c) % cans.Length), cans[(row + c) % cans.Length]));
            Box(t, "Top", new Vector3(0.37f, 1.78f, 0), new Vector3(0.05f, 0.2f, 0.9f), MatE("VendSign", Palette.Cream, Palette.Teal, 2.2f));
            Box(t, "Panel", new Vector3(0.37f, 0.62f, 0.38f), new Vector3(0.05f, 0.5f, 0.2f), Mat("VendPanel", Palette.MetalDark));
            Box(t, "Slot", new Vector3(0.375f, 0.25f, -0.05f), new Vector3(0.05f, 0.22f, 0.6f), Mat("VendSlot", Palette.MetalDark));
        }

        // ---------------------------------------------------------------- ресепшен

        static void DecorReception(Transform r)
        {
            // стойка: фасад из бирюзовых панелей, цоколь и кант
            var panel = Mat("DeskPanel", Palette.Teal);
            var trim = Mat("DeskTrim", Palette.WoodDark);
            for (int k = 0; k < 4; k++)
                Box(r, "DeskPanel", new Vector3(-3f + 2f * k, 0.52f, -13.61f), new Vector3(1.8f, 0.78f, 0.03f), panel);
            Box(r, "DeskKick", new Vector3(0, 0.06f, -13.62f), new Vector3(8.1f, 0.12f, 0.05f), trim);
            Box(r, "DeskCap", new Vector3(0, 1.0f, -13.62f), new Vector3(8.1f, 0.07f, 0.05f), trim);
            Box(r, "DeskSideW", new Vector3(-4.01f, 0.55f, -13f), new Vector3(0.03f, 1.0f, 1.0f), panel);
            Box(r, "DeskSideE", new Vector3(4.01f, 0.55f, -13f), new Vector3(0.03f, 1.0f, 1.0f), panel);

            // звонок
            var brass = Mat("Brass", Palette.Mustard);
            var bell = Empty("Bell", r, new Vector3(-1.2f, 1.1f, -13.15f));
            Cyl(bell.transform, "Base", new Vector3(0, 0.015f, 0), 0.24f, 0.03f, brass);
            Ball(bell.transform, "Dome", new Vector3(0, 0.05f, 0), new Vector3(0.2f, 0.14f, 0.2f), brass);
            Ball(bell.transform, "Button", new Vector3(0, 0.13f, 0), Vector3.one * 0.055f, Mat("BellButton", Palette.Red));

            // компьютер-коробка (экран смотрит на сотрудника, т.е. на север)
            var beige = Mat("PcBeige", new Color(0.76f, 0.73f, 0.63f));
            var pc = Empty("Computer", r, new Vector3(-2.8f, 1.1f, -13.0f));
            Box(pc.transform, "Base", new Vector3(0, 0.015f, 0), new Vector3(0.3f, 0.03f, 0.22f), beige);
            Box(pc.transform, "Neck", new Vector3(0, 0.1f, 0), new Vector3(0.06f, 0.16f, 0.05f), beige);
            Box(pc.transform, "Monitor", new Vector3(0, 0.34f, 0), new Vector3(0.62f, 0.42f, 0.12f), beige);
            Box(pc.transform, "Screen", new Vector3(0, 0.34f, 0.065f), new Vector3(0.54f, 0.34f, 0.01f), MatE("PcScreen", new Color(0.1f, 0.5f, 0.5f), new Color(0.2f, 0.9f, 0.85f), 1.2f));
            Box(pc.transform, "Keyboard", new Vector3(0, 0.02f, 0.4f), new Vector3(0.46f, 0.025f, 0.16f), beige);
            // стаканчик с ручками
            var cup = Empty("PenCup", r, new Vector3(-3.7f, 1.1f, -13.0f));
            Cyl(cup.transform, "Cup", new Vector3(0, 0.07f, 0), 0.1f, 0.14f, Mat("PenCup", Palette.TealDark));
            for (int i = 0; i < 3; i++)
                Cyl(cup.transform, "Pen", new Vector3(-0.02f + 0.02f * i, 0.2f, 0), 0.015f, 0.14f, Mat("Pen" + i, i == 0 ? Palette.Red : i == 1 ? Palette.Teal : Palette.Mustard), new Vector3(0, 0, -8f + 8f * i));

            // табличка над стойкой (читается с юга, от очереди)
            var plate = Mat("SignWood", Palette.WoodDark);
            Box(r, "SignFrame", new Vector3(0, 2.63f, -13.0f), new Vector3(3.2f, 0.52f, 0.08f), plate);      // V3: табличка поднята и уже — не закрывает электронную таблицу на стене за стойкой
            Box(r, "SignFace", new Vector3(0, 2.63f, -13.05f), new Vector3(3.0f, 0.44f, 0.03f), Mat("SignFace", Palette.TealDark));
            Cyl(r, "Rod1", new Vector3(-1.3f, 2.95f, -13f), 0.03f, 0.14f, plate);
            Cyl(r, "Rod2", new Vector3(1.3f, 2.95f, -13f), 0.03f, 0.14f, plate);

            // доска ключей: рама и стойки
            var wood = Mat("BoardFrame", Palette.WoodDark);
            Box(r, "BoardPostL", new Vector3(-5.5f, 1.46f, -10f), new Vector3(0.2f, 2.92f, 0.3f), wood);
            Box(r, "BoardPostR", new Vector3(5.5f, 1.46f, -10f), new Vector3(0.2f, 2.92f, 0.3f), wood);
            Box(r, "BoardTop", new Vector3(0, 2.86f, -10f), new Vector3(11.2f, 0.12f, 0.3f), wood);
            Box(r, "BoardBottom", new Vector3(0, 0.34f, -10f), new Vector3(11.2f, 0.12f, 0.22f), wood);

            // табличка у входа и налоговый терминал
            Box(r, "EntranceSign", new Vector3(EntranceX, 2.5f, -19.96f), new Vector3(2.3f, 0.6f, 0.04f), Mat("EntranceSign", Palette.TealDark));
            Box(r, "TaxCap", new Vector3(-19.3f, 1.3f, -10f), new Vector3(0.74f, 0.08f, 1.04f), Mat("TaxCap", Palette.Mustard));
            Box(r, "TaxPlate", new Vector3(-19.93f, 2.0f, -10f), new Vector3(0.05f, 0.5f, 1.8f), Mat("TaxPlate", Palette.DarkRed));
            Box(r, "StoragePlate", new Vector3(19.93f, 2.7f, -11.75f), new Vector3(0.05f, 0.5f, 3.4f), Mat("StoragePlate", Palette.TealDark));
            Box(r, "ShopCap", new Vector3(-19.3f, 1.3f, -7.0f), new Vector3(0.74f, 0.08f, 1.04f), Mat("ShopCap", Palette.Mustard));      // V2: магазин рядом с налоговым терминалом
            Box(r, "ShopPlate", new Vector3(-19.93f, 2.0f, -7.0f), new Vector3(0.05f, 0.5f, 1.8f), Mat("ShopPlate", Palette.TealDark));
        }

        // ---------------------------------------------------------------- стеллаж склада

        /// <summary>Стойка склада: настоящий стеллаж (полки, коробки, стойки цвета предмета) + цветная табличка на стене. Коллайдер-куб Rack остаётся.</summary>
        static void DecorShelf(Transform sh, ItemType type, string title, Color col)
        {
            var rack = sh.Find("Rack");
            if (rack != null) rack.GetComponent<Renderer>().enabled = false;      // коллайдер и интерактивность — прежние

            var item = Mat("ShelfItem" + type, col);
            var board = Mat("ShelfBoard", Palette.Metal);
            var card = Mat("Cardboard", Palette.Cardboard);
            foreach (float px in new[] { -0.47f, 0.47f })
                foreach (float pz in new[] { -0.87f, 0.87f })
                    Box(sh, "Post", new Vector3(px, 0.52f, pz), new Vector3(0.07f, 1.04f, 0.07f), item);
            foreach (float py in new[] { 0.03f, 0.52f })
                Box(sh, "Board", new Vector3(0, py, 0), new Vector3(1.0f, 0.04f, 1.8f), board);
            Box(sh, "TopBoard", new Vector3(0, 1.02f, 0), new Vector3(1.04f, 0.05f, 1.84f), item);
            // коробки на нижних полках: картон + лента цвета предмета
            for (int k = -1; k <= 1; k++)
            {
                Box(sh, "BoxLow", new Vector3(0.0f, 0.26f, k * 0.58f), new Vector3(0.72f, 0.40f, 0.5f), card);
                Box(sh, "TapeLow", new Vector3(0.0f, 0.465f, k * 0.58f), new Vector3(0.12f, 0.012f, 0.52f), item);
            }
            for (int k = -1; k <= 1; k += 2)
            {
                Box(sh, "BoxMid", new Vector3(0.05f, 0.75f, k * 0.42f), new Vector3(0.7f, 0.42f, 0.7f), card);
                Box(sh, "TapeMid", new Vector3(0.05f, 0.965f, k * 0.42f), new Vector3(0.12f, 0.012f, 0.72f), item);
                Box(sh, "TapeMidF", new Vector3(-0.301f, 0.75f, k * 0.42f), new Vector3(0.012f, 0.42f, 0.12f), item);
            }
            // табличка на стене (локально +X — к стене), крупный текст
            Box(sh, "HeaderFrame", new Vector3(0.86f, 2.1f, 0), new Vector3(0.04f, 0.64f, 1.84f), item);
            Box(sh, "Header", new Vector3(0.83f, 2.1f, 0), new Vector3(0.05f, 0.5f, 1.68f), Mat("ShelfHeader" + type, new Color(col.r * 0.7f, col.g * 0.7f, col.b * 0.7f)));
                        Label(sh, title, new Vector3(0.79f, 2.1f, 0), Vector3.left, 0.3f, Color.white, "ShelfTitle");
        }

        // ---------------------------------------------------------------- коридор

        static void DecorCorridor(Transform r)
        {
            // кант ковра
            var edge = Mat("CarpetEdge", Palette.Mustard);
            foreach (float z in new[] { 11.2f, 13.8f })
                Box(r, "CarpetEdge", new Vector3(0, 0.0225f, z), new Vector3(36f, 0.005f, 0.07f), edge);
            var runner = Mat("CarpetRunner", Palette.DarkRed * 0.8f);
            Box(r, "CarpetRunner", new Vector3(0, 0.0215f, 12.5f), new Vector3(36f, 0.004f, 1.1f), runner);

            // потолочные лампы коридора
            for (int k = 0; k < 6; k++) Plafon(r, new Vector3(-15f + 6f * k, 2.97f, 12.5f), 0.6f, 10f, 10f);
            // боковые проходы
            Plafon(r, new Vector3(0f, 2.97f, 8f), 0.5f, 7f, 8f);          // P4: лампа в центральном проходе
            Plafon(r, new Vector3(-19f, 2.97f, 17f), 0.5f, 6f, 8f);
            Plafon(r, new Vector3(19f, 2.97f, 17f), 0.5f, 6f, 8f);

            // растения у стыков номеров (чередуем стороны)
            for (int m = 0; m < 10; m += 2)
            {
                float xj = -14.4f + 3.6f * m;
                bool south = (m / 2) % 2 == 0;
                if (south) xj = m == 4 ? -1.95f : xj + (xj < 0 ? -SouthGap : SouthGap);      // P4: южный ряд сдвинут; горшок не в створе прохода
                Plant(r, new Vector3(xj, 0, south ? 11.5f : 13.5f), 0.9f);
            }
            Plant(r, new Vector3(17.3f, 0, 13.5f), 1.0f);
            Plant(r, new Vector3(-18.9f, 0, 11.4f), 1.0f);       // P4: не перед дверью номера 11 (он сдвинут)
        }

        static void Plant(Transform r, Vector3 pos, float s)
        {
            var g = Empty("Plant", r, pos).transform;
            Cyl(g, "Pot", new Vector3(0, 0.22f * s, 0), 0.44f * s, 0.44f * s, Mat("PotTerracotta", Palette.Terracotta));
            Cyl(g, "Soil", new Vector3(0, 0.44f * s, 0), 0.38f * s, 0.02f, Mat("Soil", Palette.WoodDark));
            var dark = Mat("LeafDark", Palette.Green);
            var light = Mat("LeafLight", Palette.GreenLight);
            Ball(g, "Leaf", new Vector3(0, 0.80f * s, 0), new Vector3(0.62f, 0.5f, 0.62f) * s, dark);
            Ball(g, "Leaf", new Vector3(0.14f * s, 1.12f * s, 0.05f * s), new Vector3(0.46f, 0.42f, 0.46f) * s, light);
            Ball(g, "Leaf", new Vector3(-0.15f * s, 1.0f * s, -0.08f * s), new Vector3(0.4f, 0.4f, 0.4f) * s, dark);
            Ball(g, "Leaf", new Vector3(0.05f * s, 1.38f * s, -0.05f * s), new Vector3(0.3f, 0.3f, 0.3f) * s, light);
        }

        // ---------------------------------------------------------------- декор номера (локальные координаты Room_N)

        static void DecorRoom(Transform t, int index, GameObject blocker)
        {
            var wain = Mat("Wainscot", Palette.TealDark);
            var rail = Mat("Rail", Palette.Cream);
            var baseb = Mat("Baseboard", Palette.WoodDark);
            var wood = Mat("RoomWood", Palette.WoodDark);

            // панели по стенам изнутри
            Box(t, "WainL", new Vector3(-1.68f, 0.5f, 3.0f), new Vector3(0.04f, 1.0f, 5.8f), wain);
            Box(t, "WainR", new Vector3(1.68f, 0.5f, 3.0f), new Vector3(0.04f, 1.0f, 5.8f), wain);
            Box(t, "WainB", new Vector3(0, 0.5f, 5.88f), new Vector3(3.4f, 1.0f, 0.04f), wain);
            Box(t, "RailL", new Vector3(-1.67f, 1.03f, 3.0f), new Vector3(0.06f, 0.06f, 5.8f), rail);
            Box(t, "RailR", new Vector3(1.67f, 1.03f, 3.0f), new Vector3(0.06f, 0.06f, 5.8f), rail);
            Box(t, "RailB", new Vector3(0, 1.03f, 5.87f), new Vector3(3.4f, 0.06f, 0.06f), rail);

            // кровать: рама (коллайдер Bed остаётся), матрас, покрывало, подушки, изголовье
            Box(t, "Mattress", new Vector3(-0.85f, 0.67f, 4.92f), new Vector3(1.24f, 0.14f, 1.94f), Mat("Mattress", Palette.Cream));
            Box(t, "Blanket", new Vector3(-0.85f, 0.745f, 4.45f), new Vector3(1.30f, 0.06f, 1.25f), Mat("Blanket", Palette.DarkRed));
            Box(t, "BlanketFold", new Vector3(-0.85f, 0.745f, 5.12f), new Vector3(1.28f, 0.065f, 0.16f), Mat("BlanketFold", Palette.Mustard));
            Box(t, "Headboard", new Vector3(-0.85f, 0.75f, 5.86f), new Vector3(1.42f, 1.0f, 0.08f), wood);
            Box(t, "Footboard", new Vector3(-0.85f, 0.46f, 3.87f), new Vector3(1.32f, 0.5f, 0.06f), wood);
            // картина над кроватью
            Box(t, "PictureFrame", new Vector3(-0.85f, 1.75f, 5.86f), new Vector3(1.25f, 0.75f, 0.03f), wood);
            Box(t, "PictureCanvas", new Vector3(-0.85f, 1.75f, 5.845f), new Vector3(1.1f, 0.6f, 0.02f), Mat("PictureTeal", Palette.Teal));
            Ball(t, "PictureSun", new Vector3(-0.55f, 1.85f, 5.83f), new Vector3(0.24f, 0.24f, 0.02f), Mat("PictureSun", Palette.Mustard));
            Box(t, "PictureHill", new Vector3(-1.05f, 1.56f, 5.83f), new Vector3(0.7f, 0.18f, 0.02f), Mat("PictureHill", Palette.TealDark));

            // тумба с лампой между кроватью и столиком
            Box(t, "Nightstand", new Vector3(0.25f, 0.25f, 5.62f), new Vector3(0.5f, 0.5f, 0.4f), Mat("RoomWoodMid", Palette.Wood));
            Cyl(t, "LampBase", new Vector3(0.25f, 0.56f, 5.62f), 0.12f, 0.12f, Mat("LampBase", Palette.Mustard));
            Cyl(t, "LampShade", new Vector3(0.25f, 0.76f, 5.62f), 0.26f, 0.22f, MatE("LampShade", new Color(1f, 0.9f, 0.65f), Palette.LampWarm, 1.8f));

            // окно в задней стене со шторами
            Window(t, new Vector3(0.95f, 1.8f, 5.89f), 180f, 0.8f, 1.0f, MatE("PaneNight", new Color(0.05f, 0.09f, 0.20f), new Color(0.07f, 0.14f, 0.34f), 1.1f), true);

            // ковёр под пятном (пятно — чуть выше ковра)
            Box(t, "RugOuter", new Vector3(0.1f, 0.0215f, 2.7f), new Vector3(2.4f, 0.003f, 2.1f), Mat("RoomRugOuter", Palette.DarkRed));
            Box(t, "RugInner", new Vector3(0.1f, 0.0245f, 2.7f), new Vector3(2.2f, 0.003f, 1.9f), Mat("RoomRugInner", new Color(0.82f, 0.74f, 0.55f)));

            // потолочный плафон
            Plafon(t, new Vector3(0, 2.97f, 3.0f), 0.5f, 9f, 9f);

            // дверной проём: коробка и панели у стены коридора (лицо стены — z = -0.05)
            Box(t, "JambL", new Vector3(-0.66f, 1.1f, -0.07f), new Vector3(0.12f, 2.2f, 0.16f), wood);
            Box(t, "JambR", new Vector3(0.66f, 1.1f, -0.07f), new Vector3(0.12f, 2.2f, 0.16f), wood);
            Box(t, "Header", new Vector3(0, 2.2f, -0.07f), new Vector3(1.44f, 0.1f, 0.16f), wood);
            Box(t, "CorrWainL", new Vector3(-1.2f, 0.5f, -0.07f), new Vector3(1.3f, 1.0f, 0.03f), wain);
            Box(t, "CorrWainR", new Vector3(1.2f, 0.5f, -0.07f), new Vector3(1.3f, 1.0f, 0.03f), wain);
            Box(t, "CorrRailL", new Vector3(-1.2f, 1.03f, -0.085f), new Vector3(1.3f, 0.06f, 0.06f), rail);
            Box(t, "CorrRailR", new Vector3(1.2f, 1.03f, -0.085f), new Vector3(1.3f, 0.06f, 0.06f), rail);
            Box(t, "CorrBaseL", new Vector3(-1.2f, 0.09f, -0.085f), new Vector3(1.3f, 0.18f, 0.05f), baseb);
            Box(t, "CorrBaseR", new Vector3(1.2f, 0.09f, -0.085f), new Vector3(1.3f, 0.18f, 0.05f), baseb);

            // дверь (заглушка Blocker): панели и ручка — дочерние объекты, включаются вместе с дверью
            if (blocker != null)
            {
                var sc = blocker.transform.localScale;           // (1.2, 2.2, 0.12)
                Vector3 Inv(float x, float y, float z) => new Vector3(x / sc.x, y / sc.y, z / sc.z);
                var panel = Mat("DoorPanel", Palette.WoodDark);
                var p1 = Prim(PrimitiveType.Cube, "DoorPanel", blocker.transform, Inv(0, 0.55f, -0.07f), Inv(0.85f, 0.8f, 0.02f), panel, false);
                var p2 = Prim(PrimitiveType.Cube, "DoorPanel", blocker.transform, Inv(0, -0.5f, -0.07f), Inv(0.85f, 0.8f, 0.02f), panel, false);
                var h = Prim(PrimitiveType.Sphere, "DoorHandle", blocker.transform, Inv(0.42f, 0.0f, -0.12f), Inv(0.1f, 0.1f, 0.1f), Mat("Brass", Palette.Mustard), false);
                foreach (var g in new[] { p1, p2, h }) g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ---------------------------------------------------------------- улица

        static void BuildExterior(Transform r)
        {
            var o = Empty("Exterior", r, Vector3.zero).transform;
            // земля: тёмная трава, асфальт парковки, тротуар у фасада, дорога
            Box(o, "Ground", new Vector3(0, -0.16f, 0), new Vector3(240f, 0.3f, 240f), Mat("Grass", Palette.Grass));
            Box(o, "Lot", new Vector3(0, 0.0f, -41f), new Vector3(90f, 0.012f, 42f), Mat("Asphalt", Palette.Asphalt));
            Box(o, "Sidewalk", new Vector3(0, 0.03f, -21.4f), new Vector3(44f, 0.07f, 2.4f), Mat("Sidewalk", Palette.Concrete));
            Box(o, "WalkPath", new Vector3(EntranceX, 0.025f, -23.4f), new Vector3(3.0f, 0.06f, 2.0f), Mat("Sidewalk", Palette.Concrete));
            Box(o, "Road", new Vector3(0, 0.0f, -64f), new Vector3(240f, 0.014f, 9f), Mat("Road", Palette.Asphalt * 0.8f));
            var lineMat = Mat("StallLine", new Color(0.85f, 0.82f, 0.62f));
            foreach (float x in new[] { -18f, -14.8f, -11.6f, -8.4f, -5.2f, -2f, 8f, 11.2f, 14.4f, 17.6f })
                Box(o, "StallLine", new Vector3(x, 0.014f, -28.5f), new Vector3(0.12f, 0.004f, 5.4f), lineMat);
            var dash = Mat("RoadDash", Palette.Mustard);
            for (int i = -9; i <= 9; i++) Box(o, "Dash", new Vector3(i * 6f, 0.017f, -64f), new Vector3(3f, 0.004f, 0.18f), dash);

            // фасад: полоса-карниз, цоколь, дверь и козырёк над входом
            var teal = Mat("FacadeTeal", Palette.Teal);
            var plinth = Mat("FacadePlinth", Palette.WoodDark);
            Box(o, "Band_S", new Vector3(0, 2.62f, -20.24f), new Vector3(40.6f, 0.34f, 0.08f), teal);
            Box(o, "Band_N", new Vector3(0, 2.62f, 20.24f), new Vector3(40.6f, 0.34f, 0.08f), teal);
            Box(o, "Band_W", new Vector3(-20.24f, 2.62f, 0), new Vector3(0.08f, 0.34f, 40.6f), teal);
            Box(o, "Band_E", new Vector3(20.24f, 2.62f, 0), new Vector3(0.08f, 0.34f, 40.6f), teal);
            Box(o, "Plinth_S", new Vector3(0, 0.2f, -20.25f), new Vector3(40.6f, 0.4f, 0.1f), plinth);
            Box(o, "Plinth_N", new Vector3(0, 0.2f, 20.25f), new Vector3(40.6f, 0.4f, 0.1f), plinth);
            Box(o, "Plinth_W", new Vector3(-20.25f, 0.2f, 0), new Vector3(0.1f, 0.4f, 40.6f), plinth);
            Box(o, "Plinth_E", new Vector3(20.25f, 0.2f, 0), new Vector3(0.1f, 0.4f, 40.6f), plinth);

            var doorFrame = Mat("DoorFrame", Palette.WoodDark);
            var glass = MatE("DoorGlass", new Color(1f, 0.75f, 0.40f), new Color(1f, 0.68f, 0.30f), 1.1f);
            Box(o, "DoorGlassL", new Vector3(EntranceX - 0.75f, 1.1f, -20.23f), new Vector3(1.0f, 2.1f, 0.03f), glass);
            Box(o, "DoorGlassR", new Vector3(EntranceX + 0.75f, 1.1f, -20.23f), new Vector3(1.0f, 2.1f, 0.03f), glass);
            Box(o, "DoorMid", new Vector3(EntranceX, 1.1f, -20.26f), new Vector3(0.08f, 2.2f, 0.08f), doorFrame);
            Box(o, "DoorTop", new Vector3(EntranceX, 2.2f, -20.26f), new Vector3(2.4f, 0.1f, 0.1f), doorFrame);
            Box(o, "DoorJambL", new Vector3(EntranceX - 1.28f, 1.1f, -20.26f), new Vector3(0.1f, 2.2f, 0.1f), doorFrame);
            Box(o, "DoorJambR", new Vector3(EntranceX + 1.28f, 1.1f, -20.26f), new Vector3(0.1f, 2.2f, 0.1f), doorFrame);
            // козырёк в полоску
            var awn = Empty("Awning", o, new Vector3(EntranceX, 2.55f, -20.3f)).transform;
            awn.localRotation = Quaternion.Euler(-16f, 0, 0);
            for (int i = 0; i < 6; i++)
                Box(awn, "Stripe", new Vector3(-1.25f + 0.5f * i, 0, -0.75f), new Vector3(0.5f, 0.06f, 1.5f), Mat(i % 2 == 0 ? "AwningRed" : "AwningCream", i % 2 == 0 ? Palette.DarkRed : Palette.Cream), true);

            // неоновая вывеска MOTEL на крыше + стойки
            NeonSign(o, new Vector3(0, 4.55f, -20.0f));

            // машины-коробки на парковке (носом к мотелю)
            Car(o, new Vector3(-13.2f, 0, -27.8f), 3f, Palette.DarkRed);
            Car(o, new Vector3(-6.8f, 0, -28.2f), -2f, Palette.Teal);
            Car(o, new Vector3(16.0f, 0, -27.6f), 0f, Palette.Cream);

            // фонари: тёплый конус света на асфальт
            StreetLamp(o, new Vector3(-21f, 0, -26f), 90f);
            StreetLamp(o, new Vector3(5f, 0, -35f), 0f);
            StreetLamp(o, new Vector3(21f, 0, -27f), -90f);

            // деревья по краям (силуэты)
            var trunk = Mat("TreeTrunk", Palette.WoodDark);
            var leaf = Mat("TreeLeaf", new Color(0.07f, 0.24f, 0.16f));
            var leaf2 = Mat("TreeLeaf2", new Color(0.10f, 0.32f, 0.20f));
            foreach (var v in new[] { new Vector2(-34, -30), new Vector2(-30, -48), new Vector2(34, -32), new Vector2(31, -47), new Vector2(-30, 0), new Vector2(32, 6), new Vector2(-28, 26), new Vector2(30, 28) })
            {
                var tg = Empty("Tree", o, new Vector3(v.x, 0, v.y)).transform;
                Cyl(tg, "Trunk", new Vector3(0, 1.2f, 0), 0.5f, 2.4f, trunk);
                Ball(tg, "Crown", new Vector3(0, 3.6f, 0), new Vector3(3.6f, 3.2f, 3.6f), leaf, true);
                Ball(tg, "Crown2", new Vector3(0.3f, 5.1f, 0.2f), new Vector3(2.4f, 2.2f, 2.4f), leaf2, true);
            }
            // луна
            Ball(o, "MoonDisc", new Vector3(-34f, 44f, 100f), Vector3.one * 8f, MatE("MoonDisc", new Color(0.8f, 0.85f, 1f), new Color(0.7f, 0.8f, 1f), 2.2f));
        }

        static void NeonSign(Transform o, Vector3 pos)
        {
            var g = Empty("NeonSign", o, pos).transform;
            var dark = Mat("SignBoard", new Color(0.05f, 0.05f, 0.07f));
            var trim = Mat("SignTrim", Palette.Teal);
            Box(g, "Board", new Vector3(0, 0, 0), new Vector3(8.4f, 2.5f, 0.16f), dark, true);
            Box(g, "TrimTop", new Vector3(0, 1.27f, -0.02f), new Vector3(8.6f, 0.1f, 0.22f), trim);
            Box(g, "TrimBot", new Vector3(0, -1.27f, -0.02f), new Vector3(8.6f, 0.1f, 0.22f), trim);
            Box(g, "TrimL", new Vector3(-4.25f, 0, -0.02f), new Vector3(0.1f, 2.64f, 0.22f), trim);
            Box(g, "TrimR", new Vector3(4.25f, 0, -0.02f), new Vector3(0.1f, 2.64f, 0.22f), trim);
            Box(g, "LegL", new Vector3(-3.4f, -1.5f, 0.0f), new Vector3(0.18f, 1.0f, 0.18f), dark);
            Box(g, "LegR", new Vector3(3.4f, -1.5f, 0.0f), new Vector3(0.18f, 1.0f, 0.18f), dark);
            var neon = MatE("NeonRed", Palette.Neon, Palette.Neon, 2.0f);
            var neonT = MatE("NeonTeal", Palette.TealLight, Palette.Teal, 3.5f);
            string word = "MOTEL";
            float h = 1.55f, adv = 1.55f;
            float x0 = -adv * (word.Length - 1) / 2f;
            var letters = Empty("Letters", g, new Vector3(0, 0.12f, -0.2f)).transform;
            for (int i = 0; i < word.Length; i++) NeonLetter(letters, word[i], new Vector3(x0 + adv * i, 0, 0), h, neon);
            Box(g, "Underline", new Vector3(0, -0.88f, -0.2f), new Vector3(6.6f, 0.08f, 0.08f), neonT);
            AddLight(g, "SignGlow", new Vector3(0, -0.5f, -3.0f), Palette.Neon, 14f, 15f);
        }

        static void NeonLetter(Transform p, char c, Vector3 center, float h, Material m)
        {
            float w = h * 0.62f, s = h * 0.14f, d = s;
            var g = Empty("L_" + c, p, center).transform;
            float hx = w / 2f - s / 2f, hy = h / 2f - s / 2f;
            switch (c)
            {
                case 'M':
                    Seg(g, new Vector2(-hx, -hy), new Vector2(-hx, hy), s, d, m);
                    Seg(g, new Vector2(hx, -hy), new Vector2(hx, hy), s, d, m);
                    Seg(g, new Vector2(-hx, hy), new Vector2(0, 0.05f * h), s, d, m);
                    Seg(g, new Vector2(hx, hy), new Vector2(0, 0.05f * h), s, d, m);
                    break;
                case 'O':
                    Seg(g, new Vector2(-hx, -hy), new Vector2(-hx, hy), s, d, m);
                    Seg(g, new Vector2(hx, -hy), new Vector2(hx, hy), s, d, m);
                    Seg(g, new Vector2(-hx, hy), new Vector2(hx, hy), s, d, m);
                    Seg(g, new Vector2(-hx, -hy), new Vector2(hx, -hy), s, d, m);
                    break;
                case 'T':
                    Seg(g, new Vector2(-hx, hy), new Vector2(hx, hy), s, d, m);
                    Seg(g, new Vector2(0, -hy), new Vector2(0, hy), s, d, m);
                    break;
                case 'E':
                    Seg(g, new Vector2(-hx, -hy), new Vector2(-hx, hy), s, d, m);
                    Seg(g, new Vector2(-hx, hy), new Vector2(hx, hy), s, d, m);
                    Seg(g, new Vector2(-hx, 0), new Vector2(hx * 0.7f, 0), s, d, m);
                    Seg(g, new Vector2(-hx, -hy), new Vector2(hx, -hy), s, d, m);
                    break;
                case 'L':
                    Seg(g, new Vector2(-hx, -hy), new Vector2(-hx, hy), s, d, m);
                    Seg(g, new Vector2(-hx, -hy), new Vector2(hx, -hy), s, d, m);
                    break;
            }
        }

        static void Car(Transform o, Vector3 pos, float yaw, Color body)
        {
            var g = Empty("Car", o, pos).transform;
            g.localRotation = Quaternion.Euler(0, yaw, 0);
            var paint = Mat("CarPaint" + ColorUtility.ToHtmlStringRGB(body), body);
            var glass = Mat("CarGlass", new Color(0.05f, 0.07f, 0.10f));
            var rubber = Mat("CarTire", new Color(0.04f, 0.04f, 0.05f));
            Box(g, "Body", new Vector3(0, 0.58f, 0), new Vector3(1.95f, 0.6f, 4.4f), paint, true);
            Box(g, "Cabin", new Vector3(0, 1.1f, -0.25f), new Vector3(1.66f, 0.5f, 2.3f), glass, true);
            Box(g, "Roof", new Vector3(0, 1.37f, -0.25f), new Vector3(1.74f, 0.07f, 1.8f), paint);
            Box(g, "Hood", new Vector3(0, 0.9f, 1.45f), new Vector3(1.9f, 0.05f, 1.3f), paint);
            foreach (float sx in new[] { -0.95f, 0.95f })
                foreach (float sz in new[] { -1.4f, 1.4f })
                    Cyl(g, "Wheel", new Vector3(sx, 0.34f, sz), 0.68f, 0.26f, rubber, new Vector3(0, 0, 90f));
            var head = MatE("Headlight", new Color(1f, 0.95f, 0.8f), new Color(1f, 0.92f, 0.7f), 3f);
            var tail = MatE("Taillight", new Color(0.6f, 0.05f, 0.05f), new Color(1f, 0.1f, 0.1f), 1.5f);
            foreach (float sx in new[] { -0.68f, 0.68f })
            {
                Box(g, "Head", new Vector3(sx, 0.62f, 2.21f), new Vector3(0.4f, 0.16f, 0.05f), head);
                Box(g, "Tail", new Vector3(sx, 0.66f, -2.21f), new Vector3(0.4f, 0.14f, 0.05f), tail);
            }
            AddLight(g, "HeadGlow", new Vector3(0, 0.7f, 3.2f), new Color(1f, 0.9f, 0.7f), 3f, 6f);
        }

        static void StreetLamp(Transform o, Vector3 pos, float yaw)
        {
            var g = Empty("StreetLamp", o, pos).transform;
            g.localRotation = Quaternion.Euler(0, yaw, 0);
            var pole = Mat("LampPole", Palette.MetalDark);
            Cyl(g, "Pole", new Vector3(0, 2.6f, 0), 0.14f, 5.2f, pole, null, true);
            Box(g, "Arm", new Vector3(0.55f, 5.15f, 0), new Vector3(1.1f, 0.1f, 0.1f), pole);
            Box(g, "Head", new Vector3(1.1f, 5.08f, 0), new Vector3(0.7f, 0.12f, 0.36f), MatE("StreetHead", new Color(1f, 0.9f, 0.65f), Palette.LampWarm, 3f));
            var l = AddLight(g, "Light", new Vector3(1.1f, 4.9f, 0), Palette.LampWarm, 170f, 20f, LightType.Spot);
            l.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            l.spotAngle = 115f; l.innerSpotAngle = 60f;
        }
    }
}
