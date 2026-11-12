using System;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Friendslop.EditorTools
{
    /// <summary>C2: предметы, локация (лобби, ресепшен, доска ключей, склад, терминал, коридор с номерами).</summary>
    public static partial class Builder
    {
        const string ItemDir = PrefabDir + "/Items";
        const string WorldItemPath = PrefabDir + "/WorldItem.prefab";
        static int sceneIdCounter;
        static Font labelFont;

        static void MigrateConfig(GameConfig cfg)
        {
            // C2: дальность взаимодействия по карточке — 2.5 м (старый ассет мог хранить 3)
            if (Mathf.Approximately(cfg.interactDistance, 3f)) { cfg.interactDistance = 2.5f; EditorUtility.SetDirty(cfg); }
            EditorUtility.SetDirty(cfg);              // C4: дописать в ассет новые поля GameConfig (roomsStartDirty=false и др.)
        }

        // ---------------------------------------------------------------- предметы

        static ItemDb BuildItems()
        {
            Directory.CreateDirectory(ItemDir);
            var db = AssetDatabase.LoadAssetAtPath<ItemDb>(ItemDb.AssetPath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<ItemDb>();
                AssetDatabase.CreateAsset(db, ItemDb.AssetPath);
            }
            int n = Enum.GetValues(typeof(ItemType)).Length;
            db.visuals = new GameObject[n];
            db.handOffset = new Vector3[n];
            db.handEuler = new Vector3[n];
            for (int i = 1; i < n; i++) db.visuals[i] = BuildItemVisual((ItemType)i);

            // положение в руке (относительно HandView под камерой, от первого лица) — подобрано по скриншотам P2
            db.worldHandOffset = new Vector3[n];
            db.worldHandEuler = new Vector3[n];
            SetHand(db, ItemType.Form, new Vector3(0f, 0.10f, 0.08f), new Vector3(-60f, 10f, 0f));
            SetHand(db, ItemType.Key, new Vector3(0f, 0.02f, 0.10f), new Vector3(60f, 20f, 0f));
            SetHand(db, ItemType.Towel, new Vector3(0f, 0.03f, 0.06f), new Vector3(-15f, -20f, 0f));
            SetHand(db, ItemType.Soap, new Vector3(0f, 0.03f, 0.05f), new Vector3(-10f, 0f, 0f));
            SetHand(db, ItemType.Napkins, new Vector3(0f, 0.05f, 0.05f), new Vector3(-5f, -20f, 0f));
            SetHand(db, ItemType.Mop, new Vector3(-0.085f, 0f, 0.124f), new Vector3(304.5f, 55.5f, 270f));
            SetHand(db, ItemType.Sledgehammer, new Vector3(0.03f, 0.163f, 0.188f), new Vector3(48.6f, 355.3f, 349.5f));
            SetHand(db, ItemType.Money, new Vector3(0f, 0.03f, 0.06f), new Vector3(-20f, 15f, 0f));
            // в правой руке модели (рука вперёд на Humanoid.CarryPitch = -65): euler = желаемая ориентация в мире + 65 по X
            SetWorldHand(db, ItemType.Form, new Vector3(0f, 0.02f, 0f), new Vector3(65f - 80f, 0f, 0f));
            SetWorldHand(db, ItemType.Key, Vector3.zero, new Vector3(65f, 0f, 0f));
            SetWorldHand(db, ItemType.Towel, new Vector3(0f, 0.04f, 0f), new Vector3(65f, 0f, 0f));
            SetWorldHand(db, ItemType.Soap, new Vector3(0f, 0.03f, 0f), new Vector3(65f, 0f, 0f));
            SetWorldHand(db, ItemType.Napkins, new Vector3(0f, 0.05f, 0f), new Vector3(65f, 0f, 0f));
            SetWorldHand(db, ItemType.Mop, new Vector3(0f, 0.12f, 0f), new Vector3(65f + 12f, 0f, 0f));
            SetWorldHand(db, ItemType.Sledgehammer, new Vector3(0f, 0.16f, 0f), new Vector3(65f + 25f, 0f, 0f));
            SetWorldHand(db, ItemType.Money, new Vector3(0f, 0.03f, 0f), new Vector3(65f, 0f, 0f));

            db.worldItemPrefab = BuildWorldItemPrefab();
            EditorUtility.SetDirty(db);
            return db;
        }

        static void SetHand(ItemDb db, ItemType t, Vector3 pos, Vector3 euler) { db.handOffset[(int)t] = pos; db.handEuler[(int)t] = euler; }
        static void SetWorldHand(ItemDb db, ItemType t, Vector3 pos, Vector3 euler) { db.worldHandOffset[(int)t] = pos; db.worldHandEuler[(int)t] = euler; }

        /// <summary>Один меш на все места: пол, стеллаж, стол номера, рука (P2: каждый предмет — свой силуэт и цвет).</summary>
        static GameObject BuildItemVisual(ItemType t)
        {
            var root = new GameObject("Item_" + t);
            var tr = root.transform;
            switch (t)
            {
                case ItemType.Form:      // бланк на планшете: доска, лист, бирюзовая шапка, строки, зажим
                    Prim(PrimitiveType.Cube, "Board", tr, Vector3.zero, new Vector3(0.27f, 0.014f, 0.35f), Mat("ItemFormBoard", Palette.WoodLight));
                    Prim(PrimitiveType.Cube, "Sheet", tr, new Vector3(0, 0.010f, -0.005f), new Vector3(0.23f, 0.006f, 0.30f), Mat("ItemForm", new Color(0.97f, 0.97f, 0.93f)), false);
                    Prim(PrimitiveType.Cube, "Header", tr, new Vector3(0, 0.0135f, 0.095f), new Vector3(0.19f, 0.002f, 0.04f), Mat("ItemFormHead", Palette.Teal), false);
                    for (int i = 0; i < 4; i++)
                        Prim(PrimitiveType.Cube, "Line", tr, new Vector3(0, 0.0135f, 0.03f - 0.05f * i), new Vector3(i == 3 ? 0.10f : 0.18f, 0.002f, 0.012f), Mat("ItemFormLine", new Color(0.55f, 0.57f, 0.6f)), false);
                    Prim(PrimitiveType.Cube, "Clip", tr, new Vector3(0, 0.018f, 0.165f), new Vector3(0.09f, 0.022f, 0.035f), Mat("ItemClip", Palette.Metal), false);
                    break;
                case ItemType.Key:       // ключ с красной пластиковой биркой-номером (номер ставит Items.CreateVisual)
                {
                    var gold = Mat("ItemKey", new Color(0.95f, 0.78f, 0.15f));
                    Prim(PrimitiveType.Cube, "Bar", tr, new Vector3(0, 0, 0.03f), new Vector3(0.035f, 0.025f, 0.17f), gold);
                    Prim(PrimitiveType.Cube, "Tooth1", tr, new Vector3(0.025f, 0, 0.10f), new Vector3(0.035f, 0.025f, 0.02f), gold, false);
                    Prim(PrimitiveType.Cube, "Tooth2", tr, new Vector3(0.025f, 0, 0.06f), new Vector3(0.035f, 0.025f, 0.02f), gold, false);
                    Prim(PrimitiveType.Cylinder, "Ring", tr, new Vector3(0, 0, -0.085f), new Vector3(0.085f, 0.012f, 0.085f), gold, false);
                    Prim(PrimitiveType.Cylinder, "Hole", tr, new Vector3(0, 0.002f, -0.085f), new Vector3(0.04f, 0.014f, 0.04f), Mat("ItemKeyHole", Palette.MetalDark), false);
                    Prim(PrimitiveType.Cube, "Tag", tr, new Vector3(0, 0, -0.19f), new Vector3(0.085f, 0.014f, 0.105f), Mat("ItemKeyTag", Palette.Red));
                    Prim(PrimitiveType.Cube, "TagEdge", tr, new Vector3(0, -0.002f, -0.19f), new Vector3(0.093f, 0.012f, 0.113f), Mat("ItemKeyTagEdge", Palette.Red * 0.85f), false);
                    LabelUp(tr, "", new Vector3(0, 0.0085f, -0.19f), 0.062f, Color.white, "TagNum");
                    var back = LabelUp(tr, "", new Vector3(0, -0.0105f, -0.19f), 0.062f, Color.white, "TagNumBack");     // номер и на обратной стороне бирки
                    back.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    break;
                }
                case ItemType.Towel:     // сложенное полотенце: две стопки разного оттенка и белая полоса
                    Prim(PrimitiveType.Cube, "Towel", tr, new Vector3(0, -0.03f, 0), new Vector3(0.32f, 0.055f, 0.22f), Mat("ItemTowel", Palette.Towel));
                    Prim(PrimitiveType.Cube, "Fold", tr, new Vector3(0.012f, 0.025f, 0.004f), new Vector3(0.30f, 0.05f, 0.21f), Mat("ItemTowel2", Palette.Towel * 0.78f), false);
                    Prim(PrimitiveType.Cube, "Stripe", tr, new Vector3(0.09f, -0.002f, 0), new Vector3(0.045f, 0.116f, 0.224f), Mat("ItemTowelStripe", new Color(0.93f, 0.96f, 1f)), false);
                    break;
                case ItemType.Soap:      // розовый брусок со светлой этикеткой
                {
                    var soap = Prim(PrimitiveType.Capsule, "Soap", tr, Vector3.zero, new Vector3(0.055f, 0.07f, 0.075f), Mat("ItemSoap", Palette.Soap));
                    soap.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                    Prim(PrimitiveType.Cube, "Label", tr, new Vector3(0, 0.026f, 0), new Vector3(0.07f, 0.004f, 0.045f), Mat("ItemSoapLabel", new Color(1f, 0.88f, 0.94f)), false);
                    Prim(PrimitiveType.Sphere, "Bubble", tr, new Vector3(0.07f, 0.045f, 0.01f), Vector3.one * 0.03f, Mat("ItemSoapBubble", new Color(0.8f, 0.95f, 1f)), false);
                    break;
                }
                case ItemType.Napkins:   // коробка салфеток: красная лента, щель и торчащая салфетка
                {
                    Prim(PrimitiveType.Cube, "Box", tr, Vector3.zero, new Vector3(0.15f, 0.07f, 0.15f), Mat("ItemNapkins", Palette.Napkins));
                    Prim(PrimitiveType.Cube, "Band", tr, Vector3.zero, new Vector3(0.152f, 0.022f, 0.152f), Mat("ItemNapkinsBand", Palette.DarkRed), false);
                    Prim(PrimitiveType.Cube, "Slot", tr, new Vector3(0, 0.0355f, 0), new Vector3(0.10f, 0.004f, 0.022f), Mat("ItemNapkinsSlot", Palette.MetalDark), false);
                    var sh = Prim(PrimitiveType.Cube, "Sheet", tr, new Vector3(0.01f, 0.062f, 0), new Vector3(0.08f, 0.003f, 0.075f), Mat("ItemNapkinsSheet", Color.white), false);
                    sh.transform.localRotation = Quaternion.Euler(0, 15f, 18f);
                    break;
                }
                case ItemType.Mop:       // швабра: зелёная ручка, металлический зажим, лохматая голова
                {
                    Prim(PrimitiveType.Cylinder, "Handle", tr, new Vector3(0, 0.1f, 0), new Vector3(0.04f, 0.55f, 0.04f), Mat("ItemMopHandle", Palette.Mop));
                    Prim(PrimitiveType.Cube, "Clamp", tr, new Vector3(0, -0.47f, 0), new Vector3(0.14f, 0.05f, 0.06f), Mat("ItemMopClamp", Palette.Metal), false);
                    var strand = Mat("ItemMopHead", new Color(0.93f, 0.92f, 0.84f));
                    var strand2 = Mat("ItemMopHead2", new Color(0.78f, 0.78f, 0.7f));
                    for (int i = 0; i < 7; i++)
                    {
                        float x = -0.12f + 0.04f * i;
                        var st = Prim(PrimitiveType.Cube, "Strand", tr, new Vector3(x, -0.60f, i % 2 == 0 ? 0.015f : -0.015f), new Vector3(0.032f, 0.21f, 0.04f), i % 2 == 0 ? strand : strand2, i == 3);
                        st.transform.localRotation = Quaternion.Euler(0, 0, (i - 3) * -4f);
                    }
                    break;
                }
                case ItemType.Sledgehammer: // кувалда: деревянная ручка с красной накладкой, тёмная головка со светлыми торцами
                {
                    var red = Mat("ItemHammerGrip", Palette.Hammer);
                    Prim(PrimitiveType.Cylinder, "Handle", tr, new Vector3(0, -0.05f, 0), new Vector3(0.05f, 0.38f, 0.05f), Mat("ItemWood", new Color(0.6f, 0.42f, 0.22f)));
                    Prim(PrimitiveType.Cylinder, "Grip", tr, new Vector3(0, -0.30f, 0), new Vector3(0.058f, 0.13f, 0.058f), red, false);
                    Prim(PrimitiveType.Sphere, "Knob", tr, new Vector3(0, -0.435f, 0), new Vector3(0.07f, 0.045f, 0.07f), red, false);
                    // P3: головка и боёк — тёмный металл (раньше торцы были светло-серыми и от лампы выгорали в кремово-белый, как пластик)
                    var iron = Mat("ItemIron", new Color(0.12f, 0.13f, 0.16f));
                    iron.SetFloat("_Metallic", 0.55f); iron.SetFloat("_Smoothness", 0.4f);
                    Prim(PrimitiveType.Cube, "Head", tr, new Vector3(0, 0.36f, 0), new Vector3(0.28f, 0.14f, 0.14f), iron);
                    var ironFace = Mat("ItemIronFace", new Color(0.10f, 0.105f, 0.13f));
                    ironFace.SetFloat("_Metallic", 0.7f); ironFace.SetFloat("_Smoothness", 0.5f);
                    foreach (float sx in new[] { -0.155f, 0.155f })
                        Prim(PrimitiveType.Cube, "Face", tr, new Vector3(sx, 0.36f, 0), new Vector3(0.04f, 0.16f, 0.16f), ironFace, false);
                    Prim(PrimitiveType.Cube, "Band", tr, new Vector3(0, 0.36f, 0), new Vector3(0.05f, 0.15f, 0.15f), red, false);
                    break;
                }
                case ItemType.Money:     // пачка купюр: две стопки зелёных, бумажная лента, «$»
                {
                    Prim(PrimitiveType.Cube, "Cash", tr, new Vector3(0, -0.012f, 0), new Vector3(0.22f, 0.03f, 0.11f), Mat("ItemMoney", new Color(0.25f, 0.72f, 0.3f)));
                    var c2 = Prim(PrimitiveType.Cube, "Cash2", tr, new Vector3(0.005f, 0.018f, 0.002f), new Vector3(0.21f, 0.03f, 0.105f), Mat("ItemMoney2", new Color(0.20f, 0.60f, 0.27f)), false);
                    c2.transform.localRotation = Quaternion.Euler(0, 6f, 0);
                    Prim(PrimitiveType.Cube, "Band", tr, Vector3.zero, new Vector3(0.05f, 0.066f, 0.115f), Mat("ItemMoneyBand", new Color(0.94f, 0.90f, 0.78f)), false);
                    LabelUp(tr, "$", new Vector3(0, 0.0345f, 0), 0.05f, new Color(0.15f, 0.4f, 0.2f), "Dollar");
                    break;
                }
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, ItemDir + "/Item_" + t + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject BuildWorldItemPrefab()
        {
            var go = new GameObject("WorldItem");
            go.AddComponent<NetworkObject>();
            var nt = go.AddComponent<NetworkTransform>();          // авторитет — сервер (по умолчанию)
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f; rb.linearDamping = 0.2f; rb.angularDamping = 0.6f;
            go.AddComponent<WorldItem>();
            go.layer = Items.ItemLayer;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, WorldItemPath);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        // ---------------------------------------------------------------- помощники сцены

        static T Interactable<T>(GameObject go) where T : StaticInteractable
        {
            var c = go.AddComponent<T>();
            c.sceneId = ++sceneIdCounter;
            return c;
        }

        static GameObject Empty(string name, Transform parent, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go;
        }

        /// <summary>Материал для TextMesh с проверкой глубины (P1): текст не просвечивает сквозь стены.</summary>
        static Material LabelMaterial()
        {
            if (labelFont == null) labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            string path = MatDir + "/Label3D.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Friendslop/Label3D"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.mainTexture = labelFont.material.mainTexture;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Текст в мире. faceDir — куда смотрит читатель (локально к parent); height — высота строки, м.</summary>
        static TextMesh Label(Transform parent, string text, Vector3 pos, Vector3 faceDir, float height, Color color, string name = "Label")
        {
            if (labelFont == null) labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = Empty(name, parent, pos);
            go.transform.localRotation = Quaternion.LookRotation(-faceDir, Vector3.up);
            var tm = go.AddComponent<TextMesh>();
            tm.font = labelFont;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = height / 6.4f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = LabelMaterial();
            return tm;
        }

        /// <summary>Текст, лежащий плашмя и читаемый сверху (верх строки смотрит в +Z родителя).</summary>
        static TextMesh LabelUp(Transform parent, string text, Vector3 pos, float height, Color color, string name = "Label")
        {
            var tm = Label(parent, text, pos, Vector3.back, height, color, name);
            tm.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return tm;
        }

        /// <summary>Надпись, читаемая с обеих сторон (две надписи спина к спине: шейдер рисует только лицевую сторону).</summary>
        static void LabelBoth(Transform parent, string text, Vector3 pos, Vector3 axis, float height, Color color, string name = "Label")
        {
            Label(parent, text, pos + axis * 0.004f, axis, height, color, name);
            Label(parent, text, pos - axis * 0.004f, -axis, height, color, name + "_Back");
        }

        // ---------------------------------------------------------------- локация

        static void BuildMotelLayout(GameConfig cfg, ItemDb db)
        {
            sceneIdCounter = 0;
            var root = new GameObject("Motel").transform;
            var map = new GameObject("MotelMap").AddComponent<MotelMap>();
            map.items = db;
            var white = new Color(0.95f, 0.95f, 0.9f);

            // ковёр коридора
            Prim(PrimitiveType.Cube, "Carpet", root, new Vector3(0, 0.01f, 12.5f), new Vector3(36f, 0.02f, 3f), Mat("Carpet", Palette.Carpet), false);

            // --- ресепшен
            var desk = Prim(PrimitiveType.Cube, "ReceptionDesk", root, new Vector3(0, 0.55f, -13f), new Vector3(8f, 1.1f, 1.2f), Mat("Desk", Palette.WoodLight));
            Label(root, "РЕСЕПШЕН", new Vector3(0, 2.45f, -13.08f), Vector3.back, 0.48f, white, "Label_Reception");
            map.receptionDesk = desk.transform;

            var forms = Empty("FormsStack", root, new Vector3(2.8f, 1.1f, -13f));
            for (int i = 0; i < 5; i++)
                Prim(PrimitiveType.Cube, "Sheet", forms.transform, new Vector3(0, 0.012f + i * 0.024f, 0), new Vector3(0.30f, 0.022f, 0.40f), Mat("ItemForm", new Color(0.97f, 0.97f, 0.93f)), false);
            var fc = forms.AddComponent<BoxCollider>(); fc.center = new Vector3(0, 0.06f, 0); fc.size = new Vector3(0.34f, 0.14f, 0.44f);
            Interactable<ItemSource>(forms).type = ItemType.Form;
            LabelBoth(forms.transform, "Бланки", new Vector3(0, 0.4f, 0), Vector3.forward, 0.16f, white);     // читается и с клиентской стороны стойки
            map.formsStack = forms.transform;

            // очередь гостей, вход и выход (юг лобби)
            map.guestSpawn = Empty("GuestSpawn", root, new Vector3(3f, 0, -19f)).transform;
            map.guestExit = Empty("GuestExit", root, new Vector3(3f, 0, -19.4f)).transform;
            map.queuePoints = new Transform[4];
            for (int i = 0; i < 4; i++) map.queuePoints[i] = Empty("Queue_" + i, root, new Vector3(0, 0, -14.8f - 1.1f * i)).transform;
            Label(root, "ВХОД", new Vector3(3f, 2.5f, -19.8f), Vector3.forward, 0.5f, white, "Label_Entrance");

            // --- доска ключей (отдельная стенка, крючки с +Z стороны)
            BuildKeyBoard(root, map);

            // --- склад (восток лобби): стеллажи и стойки с бесконечным запасом
            var shelves = new[]
            {
                (ItemType.Towel, "Полотенца", -6.5f, Palette.Towel),
                (ItemType.Soap, "Мыло", -9f, Palette.Soap),
                (ItemType.Napkins, "Салфетки", -11.5f, Palette.Napkins),
                (ItemType.Mop, "Швабры", -14.5f, Palette.Mop),
                (ItemType.Sledgehammer, "Кувалды", -17f, Palette.Hammer),
            };
            Label(root, "СКЛАД", new Vector3(19.88f, 2.7f, -11.75f), Vector3.left, 0.4f, white, "Label_Storage");
            foreach (var s in shelves)
            {
                var sh = Empty("Shelf_" + s.Item1, root, new Vector3(19.1f, 0, s.Item3));
                Prim(PrimitiveType.Cube, "Rack", sh.transform, new Vector3(0, 0.5f, 0), new Vector3(1.0f, 1.0f, 1.8f), Mat("Shelf", Palette.Metal));
                float lift = s.Item1 == ItemType.Mop ? 0.62f : s.Item1 == ItemType.Sledgehammer ? 0.42f : 0.05f;
                for (int k = -1; k <= 1; k++)
                {
                    var vis = UnityEngine.Object.Instantiate(db.visuals[(int)s.Item1], sh.transform);
                    vis.name = "Stock";
                    vis.transform.localPosition = new Vector3(0, 1.0f + lift, k * 0.5f);
                    foreach (var c in vis.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
                }
                Interactable<ItemSource>(sh).type = s.Item1;
                DecorShelf(sh.transform, s.Item1, s.Item2, s.Item4);          // P1: стеллаж, коробки, цветная табличка
            }

            // --- налоговый терминал (запад лобби)
            var term = Empty("TaxTerminal", root, new Vector3(-19.3f, 0, -10f));
            Prim(PrimitiveType.Cube, "Body", term.transform, new Vector3(0, 0.65f, 0), new Vector3(0.7f, 1.3f, 1.0f), Mat("Terminal", Palette.MetalDark));
            Prim(PrimitiveType.Cube, "Screen", term.transform, new Vector3(0.36f, 0.95f, 0), new Vector3(0.04f, 0.5f, 0.7f), MatE("TerminalScreen", new Color(0.3f, 0.8f, 0.9f), new Color(0.3f, 0.8f, 0.9f), 1.0f), false);
            Label(term.transform, "НАЛОГИ", new Vector3(0.1f, 1.9f, 0), Vector3.right, 0.35f, white);
            map.taxTerminal = term.transform;
            var tax = Interactable<TaxTerminal>(term);
            tax.screen = Label(term.transform, "", new Vector3(0.40f, 0.95f, 0), Vector3.right, 0.11f, new Color(0.1f, 0.25f, 0.3f), "ScreenText");

            // --- коридор и номера: 1..10 на север (z 14..20), 11..20 на юг (z 5..11), коридор z 11..14
            
            for (int k = 0; k < 10; k++)
            {
                float cx = -16.2f + 3.6f * k;
                BuildRoom(root, 1 + k, new Vector3(cx, 0, 14f), 0f, db);
                BuildRoom(root, 11 + k, new Vector3(cx, 0, 11f), 180f, db);
            }
            // стенка между лобби и южными номерами не нужна: задние стены номеров (z=5) её заменяют

            map.corridorPoint = Empty("CorridorMid", root, new Vector3(0, 0, 12.5f)).transform;     // C5: место драк

            // подключаем MotelMap к KeyBoard
            map.keyBoard = root.GetComponentInChildren<KeyBoard>();
        }

        static void BuildKeyBoard(Transform root, MotelMap map)
        {
            var bg = Empty("KeyBoard", root, new Vector3(0, 0, -10f));
            bg.AddComponent<NetworkObject>();
            var board = bg.AddComponent<KeyBoard>();
            Prim(PrimitiveType.Cube, "Panel", bg.transform, new Vector3(0, 1.6f, 0), new Vector3(10.8f, 2.4f, 0.15f), Mat("BoardPanel", Palette.Wood));
            Label(bg.transform, "КЛЮЧИ", new Vector3(0, 2.66f, 0.1f), Vector3.forward, 0.3f, Palette.Cream, "Label_Keys");
            var plateMat = Mat("KeyPlate", Palette.TealDark);
            var gold = Mat("ItemKey", new Color(0.95f, 0.78f, 0.15f));
            board.slots = new KeySlot[KeyBoard.Slots];
            for (int i = 0; i < KeyBoard.Slots; i++)
            {
                int row = i / 10, col = i % 10;
                var sl = Empty("KeySlot_" + (i + 1), bg.transform, new Vector3(-4.5f + col * 1.0f, row == 0 ? 2.1f : 1.1f, 0.1f));
                Prim(PrimitiveType.Cube, "Plate", sl.transform, Vector3.zero, new Vector3(0.78f, 0.78f, 0.05f), plateMat);
                var kv = Empty("Key", sl.transform, new Vector3(0, 0.08f, 0.05f));
                Prim(PrimitiveType.Cube, "Bar", kv.transform, new Vector3(0, -0.04f, 0), new Vector3(0.07f, 0.26f, 0.03f), gold, false);
                Prim(PrimitiveType.Cylinder, "Ring", kv.transform, new Vector3(0, 0.13f, 0), new Vector3(0.16f, 0.015f, 0.16f), gold, false).transform.localRotation = Quaternion.Euler(90f, 0, 0);
                Cyl(sl.transform, "Peg", new Vector3(0, 0.22f, 0.06f), 0.05f, 0.08f, Mat("KeyPeg", Palette.Metal), new Vector3(90f, 0, 0));
                Label(sl.transform, (i + 1).ToString(), new Vector3(0, -0.28f, 0.04f), Vector3.forward, 0.16f, Color.white, "Num");
                var ks = Interactable<KeySlot>(sl);
                ks.room = i + 1;
                ks.board = board;
                ks.keyVisual = kv;
                board.slots[i] = ks;
            }
        }

        static void BuildRoom(Transform root, int index, Vector3 pos, float yRot, ItemDb db)
        {
            const float D = 6f, H = 3f, T = 0.1f;
            var wall = Mat("Wall", Palette.Wall);
            var go = new GameObject("Room_" + index);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yRot, 0);
            var t = go.transform;
            go.AddComponent<NetworkObject>();                      // C3: состояние номера синхронизируется
            var room = go.AddComponent<Room>();
            room.index = index;

            Prim(PrimitiveType.Cube, "Wall_R", t, new Vector3(1.75f, H / 2, D / 2), new Vector3(T, H, D), wall);
            Prim(PrimitiveType.Cube, "Wall_L", t, new Vector3(-1.75f, H / 2, D / 2), new Vector3(T, H, D), wall);
            Prim(PrimitiveType.Cube, "Wall_Back", t, new Vector3(0, H / 2, D - T / 2), new Vector3(3.5f, H, T), wall);
            Prim(PrimitiveType.Cube, "Front_R", t, new Vector3(1.2f, H / 2, 0), new Vector3(1.2f, H, T), wall);
            Prim(PrimitiveType.Cube, "Front_L", t, new Vector3(-1.2f, H / 2, 0), new Vector3(1.2f, H, T), wall);
            Prim(PrimitiveType.Cube, "Lintel", t, new Vector3(0, 2.6f, 0), new Vector3(1.2f, 0.8f, T), wall);
            Prim(PrimitiveType.Cube, "FloorTile", t, new Vector3(0, 0.01f, D / 2), new Vector3(3.4f, 0.02f, D - 0.2f), Mat("RoomFloor", Palette.RoomCarpet), false);

            room.bed = Prim(PrimitiveType.Cube, "Bed", t, new Vector3(-0.85f, 0.3f, D - 1.1f), new Vector3(1.3f, 0.6f, 2.0f), Mat("Bed", Palette.WoodDark)).transform;
            var bedSpot = Interactable<RoomSpot>(room.bed.gameObject);
            bedSpot.room = room; bedSpot.kind = SpotKind.Bed;
            Prim(PrimitiveType.Cube, "Pillow", t, new Vector3(-1.12f, 0.82f, D - 0.5f), new Vector3(0.5f, 0.14f, 0.38f), Mat("Pillow", Palette.Cream), false);
            Prim(PrimitiveType.Cube, "Pillow2", t, new Vector3(-0.58f, 0.82f, D - 0.5f), new Vector3(0.5f, 0.14f, 0.38f), Mat("Pillow", Palette.Cream), false);
            Prim(PrimitiveType.Cube, "Table", t, new Vector3(1.35f, 0.4f, 4.6f), new Vector3(0.6f, 0.8f, 1.8f), Mat("Table", Palette.Wood));

            // точки под расходники на столике: полотенце, мыло, салфетки
            var supplyColors = new[] { new Color(0.35f, 0.65f, 0.95f), new Color(0.95f, 0.55f, 0.75f), new Color(0.98f, 0.95f, 0.7f) };
            var supplyNames = new[] { "Towel", "Soap", "Napkins" };
            room.supplyPoints = new Transform[3];
            room.supplyStock = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                var p = Prim(PrimitiveType.Cube, "Supply_" + supplyNames[i], t, new Vector3(1.35f, 0.82f, 3.95f + 0.65f * i), new Vector3(0.4f, 0.04f, 0.4f), Mat("Supply" + supplyNames[i], supplyColors[i]));
                room.supplyPoints[i] = p.transform;
                var spot = Interactable<RoomSpot>(p);
                spot.room = room; spot.kind = SpotKind.Supply; spot.supplyIndex = i;
                // сам расходник на точке (виден, пока его не надо заносить)
                var stock = UnityEngine.Object.Instantiate(db.visuals[(int)Room.SupplyTypes[i]], t);
                stock.name = "Stock_" + supplyNames[i];
                stock.transform.localPosition = new Vector3(1.35f, 0.86f, 3.95f + 0.65f * i);
                foreach (var c in stock.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
                room.supplyStock[i] = stock;
            }

            // пятно: точка + скрытый визуал
            var sp = Empty("StainPoint", t, new Vector3(0.2f, 0.02f, 2.6f));
            var stain = Prim(PrimitiveType.Cylinder, "Stain", sp.transform, Vector3.zero, new Vector3(1.0f, 0.01f, 1.0f), Mat("Stain", new Color(0.22f, 0.13f, 0.06f)), false);
            var stainCol = stain.AddComponent<BoxCollider>();      // плоский, чтобы в пятно можно было прицелиться шваброй
            stainCol.size = new Vector3(1f, 2f, 1f);
            stain.SetActive(false);
            room.stainPoint = sp.transform;
            room.stainVisual = stain;
            var stainSpot = Interactable<RoomSpot>(sp);            // на StainPoint (всегда активен), коллайдер — на скрытом пятне
            stainSpot.room = room; stainSpot.kind = SpotKind.Stain;
            room.stainSpot = stainSpot;

            room.door = Empty("Door", t, Vector3.zero).transform;
            room.outside = Empty("Outside", t, new Vector3(0, 0, -1.0f)).transform;
            room.inside = Empty("Inside", t, new Vector3(0, 0, 1.5f)).transform;

            // табличка над дверью (лицом в коридор: читатель на -Z): крупный номер слева, справа — слово ГОТОВ/ЗАНЯТ или цветные значки нужного
            Prim(PrimitiveType.Cube, "SignPlate", t, new Vector3(0, 2.6f, -0.07f), new Vector3(2.2f, 0.7f, 0.04f), MatE("Sign", Palette.TealDark, Palette.Teal, 0.18f), false);
            Prim(PrimitiveType.Cube, "SignDivider", t, new Vector3(-0.1f, 2.6f, -0.095f), new Vector3(0.02f, 0.5f, 0.01f), Mat("SignDivider", Palette.TealLight), false);
            room.signNumber = Label(t, index.ToString(), new Vector3(-0.64f, 2.6f, -0.1f), Vector3.back, 0.6f, Color.white, "SignNumber");
            room.signStatus = Label(t, "", new Vector3(0.55f, 2.6f, -0.1f), Vector3.back, 0.2f, new Color(1f, 0.9f, 0.5f), "SignStatus");
            // значки «нужно занести / убрать» 2x2: полотенце, мыло, салфетки, грязь — те же цвета, что у предметов и стеллажей
            var iconPos = new[] { new Vector2(0.32f, 2.76f), new Vector2(0.78f, 2.76f), new Vector2(0.32f, 2.44f), new Vector2(0.78f, 2.44f) };
            room.signIcons = new GameObject[4];
            for (int k = 0; k < 4; k++)
            {
                var ic = Empty("SignIcon_" + k, t, new Vector3(iconPos[k].x, iconPos[k].y, -0.095f));
                var backing = Prim(PrimitiveType.Cube, "Backing", ic.transform, Vector3.zero, new Vector3(0.42f, 0.30f, 0.012f), Mat("SignIconBack", new Color(0.03f, 0.10f, 0.12f)), false);
                var ink = Palette.Towel;
                if (k == 1) ink = Palette.Soap; else if (k == 2) ink = Palette.Napkins; else if (k == 3) ink = Palette.Dirt;
                var fill = MatE("SignIcon" + k, ink, ink, 0.5f);
                switch (k)
                {
                    case 0:     // полотенце: сложенный прямоугольник с белой полосой
                        Prim(PrimitiveType.Cube, "Towel", ic.transform, new Vector3(0, 0, -0.012f), new Vector3(0.28f, 0.19f, 0.016f), fill, false);
                        Prim(PrimitiveType.Cube, "Stripe", ic.transform, new Vector3(0.07f, 0, -0.02f), new Vector3(0.05f, 0.19f, 0.01f), Mat("SignIconStripe", Color.white), false);
                        break;
                    case 1:     // мыло: розовый брусок с пузырьком
                        Prim(PrimitiveType.Cube, "Soap", ic.transform, new Vector3(0, 0, -0.012f), new Vector3(0.28f, 0.15f, 0.016f), fill, false);
                        Prim(PrimitiveType.Sphere, "Bubble", ic.transform, new Vector3(0.09f, 0.085f, -0.02f), new Vector3(0.07f, 0.07f, 0.012f), Mat("SignIconBubble", new Color(0.8f, 0.95f, 1f)), false);
                        break;
                    case 2:     // салфетки: светлая коробка с тёмной щелью
                        Prim(PrimitiveType.Cube, "Box", ic.transform, new Vector3(0, 0, -0.012f), new Vector3(0.25f, 0.20f, 0.016f), fill, false);
                        Prim(PrimitiveType.Cube, "Slot", ic.transform, new Vector3(0, 0.03f, -0.02f), new Vector3(0.17f, 0.03f, 0.01f), Mat("SignIconSlot", Palette.DarkRed), false);
                        break;
                    default:    // грязь: коричневое пятно с брызгами
                        Prim(PrimitiveType.Sphere, "Stain", ic.transform, new Vector3(0, -0.01f, -0.012f), new Vector3(0.24f, 0.17f, 0.016f), fill, false);
                        Prim(PrimitiveType.Sphere, "Splash1", ic.transform, new Vector3(0.12f, 0.08f, -0.012f), new Vector3(0.08f, 0.07f, 0.016f), fill, false);
                        Prim(PrimitiveType.Sphere, "Splash2", ic.transform, new Vector3(-0.12f, 0.07f, -0.012f), new Vector3(0.05f, 0.045f, 0.016f), fill, false);
                        break;
                }
                ic.SetActive(false);
                room.signIcons[k] = ic;
            }

            // стена-заглушка в проёме, пока номер закрыт (Room сам включает/выключает)
            var blocker = Prim(PrimitiveType.Cube, "Blocker", t, new Vector3(0, 1.1f, 0), new Vector3(1.2f, 2.2f, 0.12f), Mat("Blocker", Palette.Wood));
            var obs = blocker.AddComponent<NavMeshObstacle>();
            obs.carving = true; obs.shape = NavMeshObstacleShape.Box; obs.size = Vector3.one;
            blocker.SetActive(false);
            room.blocker = blocker;
            DecorRoom(t, index, blocker);                          // P1: интерьер номера, дверная коробка, ручка
        }
    }
}
