using System;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Friendslop.EditorTools
{
    /// <summary>
    /// V3: мир вместо HUD и дальний вход. Тамбур у входа и табло номеров (внутри и снаружи), общий туалет в лобби,
    /// электронная таблица задач на стене за ресепшеном (TaskWall). Вызывается в конце BuildMotelLayout, чтобы не сдвигать sceneId остального.
    /// </summary>
    public static partial class Builder
    {
        /// <summary>Вход в мотель (X двери и точки появления гостей): далеко от стойки, путь через тамбур ~15 м.</summary>
        public const float EntranceX = 12.5f;

        /// <summary>Объект меняет материал/позицию/видимость в игре — вынуть из статического батчинга (Box() ставит BatchingStatic).</summary>
        static GameObject Dyn(GameObject g)
        {
            GameObjectUtility.SetStaticEditorFlags(g, 0);
            return g;
        }

        static TextMesh LeftLabel(Transform parent, string text, Vector3 pos, float height, Color color, string name)
        {
            var tm = Label(parent, text, pos, Vector3.back, height, color, name);
            tm.anchor = TextAnchor.MiddleLeft;
            tm.alignment = TextAlignment.Left;
            return tm;
        }

        static void BuildWorldV3(Transform root, MotelMap map, Color white)
        {
            BuildVestibule(root);
            BuildToilet(root, white);
            BuildTaskWall(root);
        }

        // ---------------------------------------------------------------- тамбур и табло у входа

        static void BuildVestibule(Transform root)
        {
            var wall = Mat("Wall", Palette.Wall);
            var t = Empty("Vestibule", root, new Vector3(EntranceX, 0, -18.4f)).transform;
            // две боковые стенки тамбура (с коллайдерами: NavMesh и игроки обходят), север открыт — выход в лобби
            Prim(PrimitiveType.Cube, "Wall_W", t, new Vector3(-1.5f, 1.5f, 0f), new Vector3(0.15f, 3f, 3.2f), wall);
            Prim(PrimitiveType.Cube, "Wall_E", t, new Vector3(1.5f, 1.5f, 0f), new Vector3(0.15f, 3f, 3.2f), wall);
            // коврик у двери и дорожка до выхода из тамбура
            Box(t, "DoorMat", new Vector3(0, 0.02f, -1.1f), new Vector3(1.8f, 0.03f, 0.9f), Mat("DoorMat", Palette.WoodDark));
            Box(t, "Runner", new Vector3(0, 0.014f, 0.2f), new Vector3(1.4f, 0.01f, 2.4f), Mat("RugOuter", Palette.DarkRed));
            Box(t, "RunnerEdge", new Vector3(0, 0.0125f, 0.2f), new Vector3(1.6f, 0.008f, 2.6f), Mat("RugBorder", Palette.Mustard));
            Plafon(t, new Vector3(0, 2.97f, 0f), 0.6f, 7f, 7f, false);
            // табло номеров на лобби-стороне западной стенки (читается из лобби и с очереди) и на фасаде справа от двери (с улицы)
            BuildRoomBoard(root, "RoomBoard_Lobby", new Vector3(EntranceX - 1.5f - 0.075f - 0.01f, 1.55f, -18.4f), 90f);
            BuildRoomBoard(root, "RoomBoard_Street", new Vector3(16.4f, 1.6f, -20.34f), 0f);
        }

        /// <summary>Табло номеров 3.0×1.5 м: «СВОБОДНО N из M» и сетка 2×10 клеток цветом. Лицом к -Z (локально), yaw поворачивает.</summary>
        static void BuildRoomBoard(Transform root, string name, Vector3 pos, float yaw)
        {
            var g = Empty(name, root, pos).transform;
            g.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var board = g.gameObject.AddComponent<RoomBoard>();

            Box(g, "Frame", new Vector3(0, 0, 0.012f), new Vector3(3.14f, 1.64f, 0.03f), Mat("BoardFrameTeal", Palette.Teal));
            Box(g, "Back", new Vector3(0, 0, -0.015f), new Vector3(3.0f, 1.5f, 0.03f), MatE("BoardBack", new Color(0.04f, 0.10f, 0.13f), new Color(0.04f, 0.12f, 0.16f), 0.6f));

            var cellColors = new[] { new Color(0.12f, 0.14f, 0.16f), new Color(0.16f, 0.78f, 0.28f), new Color(0.84f, 0.20f, 0.18f), new Color(0.98f, 0.74f, 0.10f) };
            board.cellMats = new Material[4];
            for (int i = 0; i < 4; i++)
                board.cellMats[i] = i == 0 ? Mat("BoardCell0", cellColors[0]) : MatE("BoardCell" + i, cellColors[i], cellColors[i], 0.9f);
            board.numberColors = new[] { new Color(0.42f, 0.45f, 0.5f), new Color(0.02f, 0.14f, 0.05f), Color.white, new Color(0.16f, 0.10f, 0f) };

            LeftLabel(g, "СВОБОДНО", new Vector3(-1.42f, 0.52f, -0.05f), 0.20f, new Color(0.88f, 0.95f, 0.9f), "FreeLabel");
            board.countText = Label(g, "10", new Vector3(0.50f, 0.50f, -0.05f), Vector3.back, 0.62f, new Color(0.35f, 1f, 0.5f), "FreeCount");
            board.ofText = LeftLabel(g, "из 10", new Vector3(0.98f, 0.46f, -0.05f), 0.22f, new Color(0.8f, 0.88f, 0.9f), "OfOpen");

            board.cells = new Renderer[20];
            board.numbers = new TextMesh[20];
            for (int i = 0; i < 20; i++)
            {
                int col = i % 10, row = i / 10;
                float x = -1.282f + 0.285f * col, y = 0.02f - 0.42f * row;
                var cell = Dyn(Box(g, "Cell_" + (i + 1), new Vector3(x, y, -0.05f), new Vector3(0.255f, 0.35f, 0.02f), board.cellMats[i < 10 ? 1 : 0]));      // как в начале смены: 10 открытых номеров (меню видит то же)
                board.cells[i] = cell.GetComponent<Renderer>();
                board.numbers[i] = Label(g, (i + 1).ToString(), new Vector3(x, y, -0.068f), Vector3.back, 0.15f, board.numberColors[i < 10 ? 1 : 0], "Num_" + (i + 1));
            }
            // подпись-легенда: цвет — состояние
            var lg = new[] { ("свободен", 1), ("занят", 2), ("убрать", 3) };
            for (int k = 0; k < lg.Length; k++)
            {
                float x = -1.30f + 0.98f * k;
                Box(g, "LegendDot", new Vector3(x, -0.69f, -0.05f), new Vector3(0.09f, 0.09f, 0.02f), board.cellMats[lg[k].Item2]);
                LeftLabel(g, lg[k].Item1, new Vector3(x + 0.08f, -0.69f, -0.062f), 0.095f, new Color(0.85f, 0.9f, 0.9f), "LegendText");
            }
        }

        // ---------------------------------------------------------------- туалет

        static void BuildToilet(Transform root, Color white)
        {
            // внутри 4×4.5 м в юго-западной части лобби: x -13..-9, z -20..-15.5; вход с севера из холла (проём 1.4 м)
            var go = Empty("Toilet", root, new Vector3(-11f, 0f, -17.75f));
            var t = go.transform;
            go.AddComponent<NetworkObject>();
            var wc = go.AddComponent<Toilet>();
            var wall = Mat("Wall", Palette.Wall);
            var tileMat = Mat("WcTile", new Color(0.74f, 0.86f, 0.88f));
            var porcelain = Mat("WcPorcelain", new Color(0.93f, 0.95f, 0.96f));

            Prim(PrimitiveType.Cube, "Wall_W", t, new Vector3(-2.0f, 1.5f, 0f), new Vector3(0.15f, 3f, 4.5f), wall);
            Prim(PrimitiveType.Cube, "Wall_E", t, new Vector3(2.0f, 1.5f, 0f), new Vector3(0.15f, 3f, 4.5f), wall);
            Prim(PrimitiveType.Cube, "Front_L", t, new Vector3(-1.35f, 1.5f, 2.25f), new Vector3(1.3f, 3f, 0.15f), wall);
            Prim(PrimitiveType.Cube, "Front_R", t, new Vector3(1.35f, 1.5f, 2.25f), new Vector3(1.3f, 3f, 0.15f), wall);
            Prim(PrimitiveType.Cube, "Lintel", t, new Vector3(0f, 2.6f, 2.25f), new Vector3(1.4f, 0.8f, 0.15f), wall);
            Box(t, "Floor", new Vector3(0f, 0.013f, 0f), new Vector3(3.84f, 0.02f, 4.34f), tileMat);
            // кафель на стенах (нижняя панель) и плинтус двери
            var panel = Mat("WcPanel", new Color(0.55f, 0.78f, 0.82f));
            Box(t, "PanelW", new Vector3(-1.915f, 0.6f, 0f), new Vector3(0.03f, 1.2f, 4.3f), panel);
            Box(t, "PanelE", new Vector3(1.915f, 0.6f, 0f), new Vector3(0.03f, 1.2f, 4.3f), panel);
            Box(t, "PanelS", new Vector3(0f, 0.6f, -2.14f), new Vector3(3.8f, 1.2f, 0.03f), panel);

            // унитаз у южной стены (коллайдеры: не пройти сквозь), раковина и зеркало у западной
            Prim(PrimitiveType.Cube, "Bowl", t, new Vector3(0f, 0.21f, -1.78f), new Vector3(0.42f, 0.42f, 0.56f), porcelain);
            Box(t, "Seat", new Vector3(0f, 0.44f, -1.74f), new Vector3(0.40f, 0.04f, 0.5f), Mat("WcSeat", new Color(0.8f, 0.86f, 0.9f)));
            Prim(PrimitiveType.Cube, "Tank", t, new Vector3(0f, 0.66f, -2.0f), new Vector3(0.5f, 0.5f, 0.18f), porcelain);
            Prim(PrimitiveType.Cube, "SinkBase", t, new Vector3(-1.72f, 0.42f, 0.3f), new Vector3(0.4f, 0.84f, 0.5f), Mat("WcSink", new Color(0.82f, 0.88f, 0.9f)));
            Box(t, "SinkTop", new Vector3(-1.69f, 0.88f, 0.3f), new Vector3(0.44f, 0.06f, 0.56f), porcelain);
            Box(t, "Mirror", new Vector3(-1.9f, 1.6f, 0.3f), new Vector3(0.03f, 0.8f, 0.8f), MatE("WcMirror", new Color(0.55f, 0.75f, 0.85f), new Color(0.35f, 0.55f, 0.65f), 0.7f));
            Box(t, "Paper", new Vector3(0.8f, 0.9f, -2.12f), new Vector3(0.14f, 0.14f, 0.1f), Mat("WcPaper", Color.white));

            // пятно: цель для швабры (как в номере), скрыто пока чисто
            var sp = Empty("StainPoint", t, new Vector3(0.7f, 0.025f, 0.2f));
            var stain = Prim(PrimitiveType.Cylinder, "Stain", sp.transform, Vector3.zero, new Vector3(1.0f, 0.01f, 1.0f), Mat("Stain", new Color(0.22f, 0.13f, 0.06f)), false);
            var stainCol = stain.AddComponent<BoxCollider>();
            stainCol.size = new Vector3(1f, 2f, 1f);
            stain.SetActive(false);
            wc.stainVisual = stain;
            var stainSpot = Interactable<RoomSpot>(sp);
            stainSpot.mess = wc; stainSpot.kind = SpotKind.Stain;
            wc.stainSpot = stainSpot;

            // засор: лужа у унитаза, цель для вантуза
            var cp = Empty("ClogPoint", t, new Vector3(0f, 0.03f, -0.95f));
            var clogVis = Empty("ClogVisual", cp.transform, Vector3.zero);
            var puddle = Prim(PrimitiveType.Cylinder, "Puddle", clogVis.transform, Vector3.zero, new Vector3(0.9f, 0.006f, 0.9f), Mat("ClogPuddle", Palette.Water), false);
            puddle.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var clogCol = clogVis.AddComponent<BoxCollider>();
            clogCol.center = new Vector3(0, 0.04f, 0); clogCol.size = new Vector3(0.9f, 0.1f, 0.9f);
            clogVis.SetActive(false);
            wc.clogVisual = clogVis;
            var clogSpot = Interactable<RoomSpot>(cp);
            clogSpot.mess = wc; clogSpot.kind = SpotKind.Clog;
            wc.clogSpot = clogSpot;

            wc.inside = Empty("Inside", t, new Vector3(0f, 0f, 1.2f)).transform;

            // вывеска над дверью (читается из холла): «ТУАЛЕТ», статус и лампа-светофор
            Box(t, "SignPlate", new Vector3(0f, 2.55f, 2.35f), new Vector3(2.2f, 0.72f, 0.05f), MatE("WcSign", Palette.TealDark, Palette.Teal, 0.18f));
            Label(t, "ТУАЛЕТ", new Vector3(-0.2f, 2.70f, 2.39f), Vector3.forward, 0.30f, Color.white, "SignTitle");
            wc.signStatus = Label(t, "ЧИСТО", new Vector3(-0.2f, 2.38f, 2.39f), Vector3.forward, 0.20f, new Color(0.4f, 0.95f, 0.45f), "SignStatus");
            wc.lampMats = new[]
            {
                MatE("WcLampOk", new Color(0.2f, 0.9f, 0.35f), new Color(0.2f, 0.9f, 0.35f), 1.6f),
                MatE("WcLampDirty", new Color(1f, 0.85f, 0.2f), new Color(1f, 0.8f, 0.15f), 1.6f),
                MatE("WcLampClog", new Color(1f, 0.4f, 0.15f), new Color(1f, 0.35f, 0.1f), 1.8f),
            };
            var lamp = Dyn(Box(t, "SignLamp", new Vector3(0.82f, 2.55f, 2.39f), new Vector3(0.34f, 0.34f, 0.04f), wc.lampMats[0]));
            wc.signLamp = lamp.GetComponent<Renderer>();
        }

        // ---------------------------------------------------------------- электронная таблица задач

        /// <summary>Большой экран на южной стороне стенки доски ключей (за ресепшеном, видно из лобби): два столбца по четыре строки.</summary>
        static void BuildTaskWall(Transform root)
        {
            const float W = 10.4f, H = 1.66f, CY = 1.95f;
            const int PerCol = 4;                      // 8 строк: крупный текст читается с 5–6 м, лишнее — «…и ещё N»
            const float rowPitch = 0.325f, headerH = 0.34f;
            var g = Empty("TaskWall", root, new Vector3(0f, 0f, -10.085f)).transform;
            var wall = g.gameObject.AddComponent<TaskWall>();

            Box(g, "Frame", new Vector3(0, CY, 0.005f), new Vector3(W + 0.16f, H + 0.14f, 0.03f), Mat("BoardFrameTeal", Palette.Teal));
            Box(g, "Screen", new Vector3(0, CY, -0.02f), new Vector3(W, H, 0.03f), MatE("WallScreen", new Color(0.03f, 0.09f, 0.12f), new Color(0.04f, 0.11f, 0.15f), 0.7f));
            Box(g, "Divider", new Vector3(0, CY - headerH / 2f, -0.04f), new Vector3(0.025f, H - headerH - 0.06f, 0.01f), Mat("WallDivider", Palette.TealLight));
            float top = CY + H / 2f - 0.03f;
            wall.header = LeftLabel(g, "ЗАДАЧИ", new Vector3(-W / 2f + 0.16f, top - headerH / 2f, -0.06f), 0.28f, new Color(0.96f, 0.80f, 0.30f), "Header");

            // значки: те же цвета, что у предметов, стеллажей и значков номеров; по типу предмета свой материал
            int n = Enum.GetValues(typeof(ItemType)).Length;
            wall.iconMats = new Material[n];
            for (int i = 1; i < n; i++)
            {
                var c = UiStyle.ItemColor((ItemType)i);
                wall.iconMats[i] = MatE("WallIcon_" + (ItemType)i, c, c, 0.9f);
            }
            var urgentMat = MatE("WallUrgent", new Color(0.55f, 0.07f, 0.07f), new Color(0.6f, 0.05f, 0.05f), 0.9f);
            var calmMat = wall.iconMats[(int)ItemType.Key];
            const int maxIcons = 6;

            wall.rows = new TaskWall.Row[PerCol * 2];
            for (int col = 0; col < 2; col++)
            {
                float left = col == 0 ? -W / 2f + 0.1f : 0.1f, right = col == 0 ? -0.1f : W / 2f - 0.1f;
                for (int r = 0; r < PerCol; r++)
                {
                    float y = top - headerH - (r + 0.5f) * rowPitch;
                    var row = new TaskWall.Row { left = left, right = right, y = y };
                    var bg = Dyn(Box(g, "Urgent", new Vector3((left + right) / 2f, y, -0.04f), new Vector3(right - left, rowPitch - 0.025f, 0.01f), urgentMat));
                    bg.SetActive(false);
                    row.urgentBg = bg;
                    row.text = LeftLabel(g, "", new Vector3(left + 0.12f, y, -0.06f), 0.27f, Color.white, "RowText");
                    row.icons = new Renderer[maxIcons];
                    row.glyphs = new TextMesh[maxIcons];
                    for (int k = 0; k < maxIcons; k++)
                    {
                        var ic = Dyn(Box(g, "Icon", new Vector3(right - 0.2f, y, -0.05f), new Vector3(0.25f, 0.25f, 0.012f), calmMat));
                        ic.SetActive(false);
                        row.icons[k] = ic.GetComponent<Renderer>();
                        var gl = Label(g, "", new Vector3(right - 0.2f, y, -0.065f), Vector3.back, 0.19f, new Color(0.04f, 0.05f, 0.08f), "Glyph");
                        gl.gameObject.SetActive(false);
                        row.glyphs[k] = gl;
                    }
                    wall.rows[col * PerCol + r] = row;
                }
            }
        }
    }
}
