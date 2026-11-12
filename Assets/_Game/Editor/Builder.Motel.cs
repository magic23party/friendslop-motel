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

            // положение в руке (относительно HandView под камерой)
            db.handOffset[(int)ItemType.Mop] = new Vector3(0.05f, -0.10f, 0.15f);
            db.handEuler[(int)ItemType.Mop] = new Vector3(35f, 0f, -20f);
            db.handOffset[(int)ItemType.Sledgehammer] = new Vector3(0.0f, 0.0f, 0.1f);
            db.handEuler[(int)ItemType.Sledgehammer] = new Vector3(30f, 0f, -15f);
            db.handEuler[(int)ItemType.Key] = new Vector3(0f, 30f, 0f);

            db.worldItemPrefab = BuildWorldItemPrefab();
            EditorUtility.SetDirty(db);
            return db;
        }

        static GameObject BuildItemVisual(ItemType t)
        {
            var root = new GameObject("Item_" + t);
            var tr = root.transform;
            switch (t)
            {
                case ItemType.Form:
                    Prim(PrimitiveType.Cube, "Sheet", tr, Vector3.zero, new Vector3(0.24f, 0.02f, 0.32f), Mat("ItemForm", new Color(0.97f, 0.97f, 0.93f)));
                    break;
                case ItemType.Key:
                    var gold = Mat("ItemKey", new Color(0.95f, 0.78f, 0.15f));
                    Prim(PrimitiveType.Cube, "Bar", tr, Vector3.zero, new Vector3(0.04f, 0.03f, 0.16f), gold);
                    Prim(PrimitiveType.Cylinder, "Ring", tr, new Vector3(0, 0, -0.1f), new Vector3(0.09f, 0.015f, 0.09f), gold).transform.localRotation = Quaternion.identity;
                    break;
                case ItemType.Towel:
                    Prim(PrimitiveType.Cube, "Towel", tr, Vector3.zero, new Vector3(0.32f, 0.09f, 0.22f), Mat("ItemTowel", new Color(0.35f, 0.65f, 0.95f)));
                    break;
                case ItemType.Soap:
                    Prim(PrimitiveType.Cube, "Soap", tr, Vector3.zero, new Vector3(0.12f, 0.05f, 0.08f), Mat("ItemSoap", new Color(0.95f, 0.55f, 0.75f)));
                    break;
                case ItemType.Napkins:
                    Prim(PrimitiveType.Cube, "Napkins", tr, Vector3.zero, new Vector3(0.15f, 0.09f, 0.15f), Mat("ItemNapkins", new Color(0.98f, 0.95f, 0.7f)));
                    break;
                case ItemType.Mop:
                    Prim(PrimitiveType.Cylinder, "Handle", tr, new Vector3(0, 0.1f, 0), new Vector3(0.04f, 0.55f, 0.04f), Mat("ItemWood", new Color(0.6f, 0.42f, 0.22f)));
                    Prim(PrimitiveType.Cube, "Head", tr, new Vector3(0, -0.58f, 0), new Vector3(0.30f, 0.07f, 0.12f), Mat("ItemMopHead", new Color(0.85f, 0.85f, 0.8f)));
                    break;
                case ItemType.Sledgehammer:
                    Prim(PrimitiveType.Cylinder, "Handle", tr, new Vector3(0, -0.05f, 0), new Vector3(0.05f, 0.38f, 0.05f), Mat("ItemWood", new Color(0.6f, 0.42f, 0.22f)));
                    Prim(PrimitiveType.Cube, "Head", tr, new Vector3(0, 0.36f, 0), new Vector3(0.28f, 0.14f, 0.14f), Mat("ItemIron", new Color(0.35f, 0.36f, 0.4f)));
                    break;
                case ItemType.Money:
                    Prim(PrimitiveType.Cube, "Cash", tr, Vector3.zero, new Vector3(0.22f, 0.03f, 0.11f), Mat("ItemMoney", new Color(0.25f, 0.72f, 0.3f)));
                    break;
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
            go.GetComponent<MeshRenderer>().sharedMaterial = labelFont.material;
            return tm;
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
            Prim(PrimitiveType.Cube, "Carpet", root, new Vector3(0, 0.01f, 12.5f), new Vector3(36f, 0.02f, 3f), Mat("Carpet", new Color(0.45f, 0.18f, 0.2f)), false);

            // --- ресепшен
            var desk = Prim(PrimitiveType.Cube, "ReceptionDesk", root, new Vector3(0, 0.55f, -13f), new Vector3(8f, 1.1f, 1.2f), Mat("Desk", new Color(0.4f, 0.28f, 0.18f)));
            Label(root, "РЕСЕПШЕН", new Vector3(0, 2.4f, -13f), Vector3.back, 0.5f, white, "Label_Reception");
            map.receptionDesk = desk.transform;

            var forms = Empty("FormsStack", root, new Vector3(2.8f, 1.1f, -13f));
            for (int i = 0; i < 5; i++)
                Prim(PrimitiveType.Cube, "Sheet", forms.transform, new Vector3(0, 0.012f + i * 0.024f, 0), new Vector3(0.30f, 0.022f, 0.40f), Mat("ItemForm", new Color(0.97f, 0.97f, 0.93f)), false);
            var fc = forms.AddComponent<BoxCollider>(); fc.center = new Vector3(0, 0.06f, 0); fc.size = new Vector3(0.34f, 0.14f, 0.44f);
            Interactable<ItemSource>(forms).type = ItemType.Form;
            Label(forms.transform, "Бланки", new Vector3(0, 0.4f, 0), Vector3.forward, 0.16f, white);
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
            var shelfMat = Mat("Shelf", new Color(0.45f, 0.45f, 0.5f));
            var shelves = new[]
            {
                (ItemType.Towel, "Полотенца", -6.5f),
                (ItemType.Soap, "Мыло", -9f),
                (ItemType.Napkins, "Салфетки", -11.5f),
                (ItemType.Mop, "Швабры", -14.5f),
                (ItemType.Sledgehammer, "Кувалды", -17f),
            };
            Label(root, "СКЛАД", new Vector3(19.1f, 3.0f, -12f), Vector3.left, 0.6f, white, "Label_Storage");
            foreach (var s in shelves)
            {
                var sh = Empty("Shelf_" + s.Item1, root, new Vector3(19.1f, 0, s.Item3));
                Prim(PrimitiveType.Cube, "Rack", sh.transform, new Vector3(0, 0.5f, 0), new Vector3(1.0f, 1.0f, 1.8f), shelfMat);
                float lift = s.Item1 == ItemType.Mop ? 0.62f : s.Item1 == ItemType.Sledgehammer ? 0.42f : 0.05f;
                for (int k = -1; k <= 1; k++)
                {
                    var vis = UnityEngine.Object.Instantiate(db.visuals[(int)s.Item1], sh.transform);
                    vis.name = "Stock";
                    vis.transform.localPosition = new Vector3(0, 1.0f + lift, k * 0.5f);
                    foreach (var c in vis.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
                }
                Interactable<ItemSource>(sh).type = s.Item1;
                Label(sh.transform, s.Item2, new Vector3(-0.1f, 2.7f, 0), Vector3.left, 0.3f, white);
            }

            // --- налоговый терминал (запад лобби); логику вешает C3
            var term = Empty("TaxTerminal", root, new Vector3(-19.3f, 0, -10f));
            Prim(PrimitiveType.Cube, "Body", term.transform, new Vector3(0, 0.65f, 0), new Vector3(0.7f, 1.3f, 1.0f), Mat("Terminal", new Color(0.2f, 0.22f, 0.3f)));
            Prim(PrimitiveType.Cube, "Screen", term.transform, new Vector3(0.36f, 0.95f, 0), new Vector3(0.04f, 0.5f, 0.7f), Mat("TerminalScreen", new Color(0.3f, 0.8f, 0.9f)), false);
            Label(term.transform, "НАЛОГИ", new Vector3(0.1f, 1.9f, 0), Vector3.right, 0.35f, white);
            map.taxTerminal = term.transform;

            // --- коридор и номера: 1..10 на север (z 14..20), 11..20 на юг (z 5..11), коридор z 11..14
            var wallMat = Mat("Wall", new Color(0.75f, 0.72f, 0.66f));
            for (int k = 0; k < 10; k++)
            {
                float cx = -16.2f + 3.6f * k;
                BuildRoom(root, 1 + k, new Vector3(cx, 0, 14f), 0f);
                BuildRoom(root, 11 + k, new Vector3(cx, 0, 11f), 180f);
            }
            // стенка между лобби и южными номерами не нужна: задние стены номеров (z=5) её заменяют

            // подключаем MotelMap к KeyBoard
            map.keyBoard = root.GetComponentInChildren<KeyBoard>();
        }

        static void BuildKeyBoard(Transform root, MotelMap map)
        {
            var bg = Empty("KeyBoard", root, new Vector3(0, 0, -10f));
            bg.AddComponent<NetworkObject>();
            var board = bg.AddComponent<KeyBoard>();
            Prim(PrimitiveType.Cube, "Panel", bg.transform, new Vector3(0, 1.6f, 0), new Vector3(10.8f, 2.4f, 0.15f), Mat("BoardPanel", new Color(0.30f, 0.20f, 0.12f)));
            Label(bg.transform, "КЛЮЧИ", new Vector3(0, 3.05f, 0.1f), Vector3.forward, 0.5f, Color.white, "Label_Keys");
            var plateMat = Mat("KeyPlate", new Color(0.55f, 0.55f, 0.58f));
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
                Label(sl.transform, (i + 1).ToString(), new Vector3(0, -0.28f, 0.04f), Vector3.forward, 0.16f, Color.white, "Num");
                var ks = Interactable<KeySlot>(sl);
                ks.room = i + 1;
                ks.board = board;
                ks.keyVisual = kv;
                board.slots[i] = ks;
            }
        }

        static void BuildRoom(Transform root, int index, Vector3 pos, float yRot)
        {
            const float D = 6f, H = 3f, T = 0.1f;
            var wall = Mat("Wall", new Color(0.75f, 0.72f, 0.66f));
            var go = new GameObject("Room_" + index);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yRot, 0);
            var t = go.transform;
            var room = go.AddComponent<Room>();
            room.index = index;

            Prim(PrimitiveType.Cube, "Wall_R", t, new Vector3(1.75f, H / 2, D / 2), new Vector3(T, H, D), wall);
            Prim(PrimitiveType.Cube, "Wall_L", t, new Vector3(-1.75f, H / 2, D / 2), new Vector3(T, H, D), wall);
            Prim(PrimitiveType.Cube, "Wall_Back", t, new Vector3(0, H / 2, D - T / 2), new Vector3(3.5f, H, T), wall);
            Prim(PrimitiveType.Cube, "Front_R", t, new Vector3(1.2f, H / 2, 0), new Vector3(1.2f, H, T), wall);
            Prim(PrimitiveType.Cube, "Front_L", t, new Vector3(-1.2f, H / 2, 0), new Vector3(1.2f, H, T), wall);
            Prim(PrimitiveType.Cube, "Lintel", t, new Vector3(0, 2.6f, 0), new Vector3(1.2f, 0.8f, T), wall);
            Prim(PrimitiveType.Cube, "FloorTile", t, new Vector3(0, 0.01f, D / 2), new Vector3(3.4f, 0.02f, D - 0.2f), Mat("RoomFloor", new Color(0.62f, 0.56f, 0.5f)), false);

            room.bed = Prim(PrimitiveType.Cube, "Bed", t, new Vector3(-0.85f, 0.3f, D - 1.1f), new Vector3(1.3f, 0.6f, 2.0f), Mat("Bed", new Color(0.35f, 0.4f, 0.65f))).transform;
            Prim(PrimitiveType.Cube, "Pillow", t, new Vector3(-0.85f, 0.66f, D - 0.4f), new Vector3(0.9f, 0.12f, 0.4f), Mat("Pillow", new Color(0.95f, 0.95f, 0.95f)), false);
            Prim(PrimitiveType.Cube, "Table", t, new Vector3(1.35f, 0.4f, 4.6f), new Vector3(0.6f, 0.8f, 1.8f), Mat("Table", new Color(0.5f, 0.36f, 0.22f)));

            // точки под расходники на столике: полотенце, мыло, салфетки
            var supplyColors = new[] { new Color(0.35f, 0.65f, 0.95f), new Color(0.95f, 0.55f, 0.75f), new Color(0.98f, 0.95f, 0.7f) };
            var supplyNames = new[] { "Towel", "Soap", "Napkins" };
            room.supplyPoints = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                var p = Prim(PrimitiveType.Cube, "Supply_" + supplyNames[i], t, new Vector3(1.35f, 0.82f, 3.95f + 0.65f * i), new Vector3(0.4f, 0.04f, 0.4f), Mat("Supply" + supplyNames[i], supplyColors[i]));
                room.supplyPoints[i] = p.transform;
            }

            // пятно: точка + скрытый визуал
            var sp = Empty("StainPoint", t, new Vector3(0.2f, 0.02f, 2.6f));
            var stain = Prim(PrimitiveType.Cylinder, "Stain", sp.transform, Vector3.zero, new Vector3(1.0f, 0.01f, 1.0f), Mat("Stain", new Color(0.25f, 0.16f, 0.08f)), false);
            stain.SetActive(false);
            room.stainPoint = sp.transform;
            room.stainVisual = stain;

            room.door = Empty("Door", t, Vector3.zero).transform;
            room.outside = Empty("Outside", t, new Vector3(0, 0, -1.0f)).transform;
            room.inside = Empty("Inside", t, new Vector3(0, 0, 1.5f)).transform;

            // табличка над дверью (лицом в коридор: читатель на -Z)
            Prim(PrimitiveType.Cube, "SignPlate", t, new Vector3(0, 2.6f, -0.07f), new Vector3(1.2f, 0.7f, 0.04f), Mat("Sign", new Color(0.12f, 0.12f, 0.15f)), false);
            room.signNumber = Label(t, index.ToString(), new Vector3(-0.38f, 2.6f, -0.1f), Vector3.back, 0.45f, Color.white, "SignNumber");
            room.signStatus = Label(t, "", new Vector3(0.22f, 2.6f, -0.1f), Vector3.back, 0.11f, new Color(1f, 0.9f, 0.5f), "SignStatus");

            // стена-заглушка в проёме, пока номер закрыт (Room сам включает/выключает)
            var blocker = Prim(PrimitiveType.Cube, "Blocker", t, new Vector3(0, 1.1f, 0), new Vector3(1.2f, 2.2f, 0.12f), Mat("Blocker", new Color(0.45f, 0.4f, 0.38f)));
            var obs = blocker.AddComponent<NavMeshObstacle>();
            obs.carving = true; obs.shape = NavMeshObstacleShape.Box; obs.size = Vector3.one;
            blocker.SetActive(false);
            room.blocker = blocker;
        }
    }
}
