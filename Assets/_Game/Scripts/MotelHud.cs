using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Friendslop
{
    /// <summary>
    /// IMGUI-интерфейс сцены Motel: лобби, «Начать смену», пауза по Esc, прицел и подсказка, предмет в руках,
    /// касса/налог/день, баннер, лента покупок, Game Over. Список задач здесь не рисуется (V3): он на электронной таблице за ресепшеном (TaskWall). Весь стиль — UiStyle (единый для меню, HUD и Game Over).
    /// </summary>
    public class MotelHud : MonoBehaviour
    {
        public GameConfig config;

        GamePhase prevPhase;
        bool daySeen, prevNight;
        int prevDay;
        float bannerUntil;
        string bannerText = "";
        bool bannerNight;
        static GUIStyle money, valueRight, moneyBig;
        /// <summary>V4: карточка «Как играть» (в лобби открыта по умолчанию, H — показать/скрыть; закрывается кнопкой при свободном курсоре).</summary>
        public static bool ShowHowTo = true;
        public static bool ShotKeep;            // режим скриншотов: карточку открывает/закрывает только команда howto, а не смена фазы
        GamePhase howtoPhase = GamePhase.Lobby;
        static readonly List<KeyValuePair<string, float>> toasts = new List<KeyValuePair<string, float>>();

        /// <summary>V2: короткое сообщение всем («Имя купил: Тележка горничной»), живёт ~5 с.</summary>
        public static void Toast(string text)
        {
            toasts.Add(new KeyValuePair<string, float>(text, Time.unscaledTime + 5f));
            if (toasts.Count > 3) toasts.RemoveAt(0);
        }

        void Update()
        {
            // отладка: автостарт смены, когда подключился второй игрок
            var gs = GameState.Instance;
            var nm = NetworkManager.Singleton;
            if (gs != null && nm != null && nm.IsServer && NetBootstrap.Arg("-autostart")
                && gs.Phase.Value == GamePhase.Lobby && gs.PlayerCount.Value >= 2)
                gs.StartShift();

            // хост: Enter в лобби — начать смену (как кнопка)
            var kb = Keyboard.current;
            if (gs != null && nm != null && nm.IsServer && kb != null && gs.Phase.Value == GamePhase.Lobby
                && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                && !(PlayerController.Local != null && PlayerController.Local.MenuOpen))
                gs.StartShift();

            // V4: карточка «Как играть»: H — показать/скрыть, начало смены — спрятать, возврат в лобби — показать
            if (gs != null)
            {
                var ph0 = gs.Phase.Value;
                if (ph0 != howtoPhase) { if (!ShotKeep) { if (ph0 == GamePhase.Playing) ShowHowTo = false; if (ph0 == GamePhase.Lobby) ShowHowTo = true; } howtoPhase = ph0; }
                if (kb != null && kb.hKey.wasPressedThisFrame && ph0 != GamePhase.GameOver && !ShopUi.IsOpen) ShowHowTo = !ShowHowTo;
            }

            // баннер смены дня/ночи (V1): «День N» / «Наступила ночь»
            if (gs != null && gs.Phase.Value == GamePhase.Playing)
            {
                bool night = gs.Night.Value; int day = gs.Day.Value;
                if (!daySeen || night != prevNight || day != prevDay)
                {
                    daySeen = true; prevNight = night; prevDay = day;
                    bannerNight = night;
                    bannerText = night ? $"Наступила ночь · счёт {gs.TaxAmount} $" : $"День {day}";
                    bannerUntil = Time.unscaledTime + 3.5f;
                }
            }
            else daySeen = false;

            // Game Over: курсор свободен у всех (кнопки экрана); после «Заново» — снова в игру
            var me = PlayerController.Local;
            if (gs != null && me != null && me.Placed)
            {
                var ph = gs.Phase.Value;
                if (ph == GamePhase.GameOver && !me.MenuOpen) me.SetMenu(true);
                else if (prevPhase == GamePhase.GameOver && ph != GamePhase.GameOver) me.SetMenu(false);
                prevPhase = ph;
            }
        }

        void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;
            Ui.Begin();
            if (money == null)
            {
                money = new GUIStyle(UiStyle.Title) { fontSize = 30, alignment = TextAnchor.MiddleRight };
                valueRight = new GUIStyle(UiStyle.Header) { alignment = TextAnchor.MiddleRight };
                moneyBig = new GUIStyle(UiStyle.Title) { fontSize = 30 };
            }
            var gs = GameState.Instance;
            var me = PlayerController.Local;
            float cx = Ui.W / 2f;
            bool playing = me != null && me.Placed && !me.MenuOpen;

            if (playing) UiStyle.Dot(new Vector2(cx, Ui.H / 2f), 6f);

            var hands = PlayerHands.Local;
            if (playing && hands != null)
            {
                if (!string.IsNullOrEmpty(hands.HintText)) DrawHint(cx, Ui.H / 2f + 30f, hands.HintText);
                var held = hands.Held.Value;
                if (!held.IsNone) DrawHeld(cx, Ui.H - 70f, held);
            }

            if (gs != null)
            {
                int max = config != null ? config.maxPlayers : 8;
                if (gs.Phase.Value == GamePhase.Lobby)
                {
                    float h = nm.IsServer ? 128f : 86f;
                    UiStyle.Plate(new Rect(cx - 230, 12, 460, h), UiStyle.Kind.Card);
                    Ui.Shadowed(new Rect(cx - 220, 20, 440, 30), $"Лобби — игроков: {gs.PlayerCount.Value}/{max}", UiStyle.Center, Color.white);
                    if (nm.IsServer)
                    {
                        if (me != null && me.MenuOpen) GUI.enabled = false;
                        if (UiStyle.Btn(new Rect(cx - 130, 62, 260, 48), "Начать смену (Enter)", true)) gs.StartShift();
                        GUI.enabled = true;
                    }
                    else Ui.Shadowed(new Rect(cx - 220, 52, 440, 30), "Ждём, пока хост начнёт смену…", UiStyle.Center, UiStyle.Mustard);
                }
                else
                {
                    DrawShiftPanel(gs);
                    if (gs.Phase.Value == GamePhase.Playing) { DrawBanner(cx); DrawToasts(cx); DrawBuff(); }
                }
            }

            if (playing)
                Ui.Shadowed(new Rect(12, Ui.H - 30, 900, 24), "WASD — ходьба · Shift — бег · Пробел — прыжок · E — действие · G — бросить · H — как играть · Esc — меню", UiStyle.Small, new Color(1, 1, 1, 0.7f));
            if (gs != null && gs.Phase.Value == GamePhase.Playing && playing && !ShowHowTo) DrawTip(cx, gs, hands);
            if (gs != null && gs.Phase.Value != GamePhase.GameOver && ShowHowTo) DrawHowTo(gs.Phase.Value == GamePhase.Lobby ? 150f : 196f, me != null && me.MenuOpen);

            if (gs != null && gs.Phase.Value == GamePhase.GameOver)
            {
                DrawGameOver(gs, nm);
                return;
            }

            if (ShopUi.IsOpen) { ShopUi.Draw(); return; }          // V2: магазин вместо паузы

            if (me != null && me.MenuOpen)
            {
                UiStyle.Dim(new Rect(0, 0, Ui.W, Ui.H), 0.55f);
                float x = cx - 190, y = Ui.H / 2f - 140;
                UiStyle.Plate(new Rect(x, y, 380, 344), UiStyle.Kind.Card);
                UiStyle.NeonTitle(new Rect(x, y + 22, 380, 56), "Пауза", UiStyle.Teal, UiStyle.Cream, 38);
                y += 100;
                if (UiStyle.Btn(new Rect(x + 30, y, 320, 50), "Продолжить", true)) me.SetMenu(false);
                y += 64;
                if (UiStyle.Btn(new Rect(x + 30, y, 320, 50), "Как играть (H)")) { ShowHowTo = true; me.SetMenu(false); }
                y += 64;
                if (UiStyle.Btn(new Rect(x + 30, y, 320, 50), nm.IsServer ? "Закрыть мотель и выйти" : "Выйти в меню"))
                    NetBootstrap.Instance.LeaveToMenu();
            }
        }

        // ------------------------------------------------------------ V4: «Как играть» и подсказки первых минут

        static readonly string[] HowToText =
        {
            "Цель: держите мотель на плаву — заселяйте гостей, убирайте номера и вовремя платите налог.",
            "Гость у стойки: дайте бланк (со стойки), потом ключ свободного номера (с доски за стойкой). Неготовый номер — риск: 1 проблема — гость платит вдвое меньше, 2+ — не платит. Выезд — E с пустыми руками: ключ вернётся, оплата уйдёт в кассу.",
            "Роли — договоритесь сами: кто на стойке, кто носит со склада полотенца/мыло/салфетки и моет номера, кто бьёт кувалдой неплательщиков и дерущихся.",
            "День 5:30 — идут гости. Ночь 3 мин — гостей нет, выставлен счёт: оплатите на терминале налога, иначе мотель закроется. Налог растёт каждый день.",
            "Магазин (терминал в лобби): улучшения из общей кассы — тележка, кофе, пристройка, вантуз, лампочки. Туалет, засоры и грязь на полу (следы гостей — мыть шваброй) тоже требуют рук.",
            "Что делать — на электронной таблице за стойкой; свободные номера — на табло у входа.",
        };
        const string HowToKeys = "WASD — ходьба · Shift — бег · Пробел — прыжок · мышь — камера · E — действие · G — бросить · ЛКМ — использовать (швабра, кувалда, кофе) · Esc — меню · Enter — старт смены (хост) · H — эта карточка";

        void DrawHowTo(float y, bool cursorFree)
        {
            const float w = 480f, pad = 18f, tw = w - pad * 2f;
            float h = 10f + 36f;
            foreach (var t in HowToText) h += UiStyle.Small.CalcHeight(new GUIContent(t), tw) + 6f;
            h += 4f + UiStyle.Small.CalcHeight(new GUIContent(HowToKeys), tw) + 6f + 34f;
            var r = new Rect(12f, y, w, h);
            UiStyle.Plate(r, UiStyle.Kind.Card);
            float yy = y + 10f;
            Ui.Shadowed(new Rect(r.x + pad, yy, tw, 30f), "Как играть", UiStyle.Header, UiStyle.Mustard);
            yy += 36f;
            foreach (var t in HowToText)
            {
                float lh = UiStyle.Small.CalcHeight(new GUIContent(t), tw);
                Ui.Shadowed(new Rect(r.x + pad, yy, tw, lh), t, UiStyle.Small, Color.white);
                yy += lh + 6f;
            }
            yy += 4f;
            float kh = UiStyle.Small.CalcHeight(new GUIContent(HowToKeys), tw);
            Ui.Shadowed(new Rect(r.x + pad, yy, tw, kh), HowToKeys, UiStyle.Small, UiStyle.TealLight);
            yy += kh + 6f;
            if (cursorFree)
            {
                if (UiStyle.Btn(new Rect(r.x + pad, yy, 150f, 28f), "Закрыть (H)")) ShowHowTo = false;
            }
            else Ui.Shadowed(new Rect(r.x + pad, yy, tw, 24f), "H — закрыть", UiStyle.Small, UiStyle.Mustard);
        }

        /// <summary>Подсказка по текущей ситуации: первые 2 минуты первого дня. Читает только синхронизированное состояние (гости, номера, предмет в руках).</summary>
        static string TipText(GameState gs, PlayerHands hands)
        {
            if (gs.Day.Value != 1 || gs.Night.Value || gs.ShiftElapsed > 120f) return "";
            var held = hands != null ? hands.Held.Value : default;
            if (gs.ShiftElapsed < 10f) return "Смотри таблицу задач за стойкой и табло номеров у входа";
            Guest form = null, key = null, outg = null;
            foreach (var g in Guest.All)
            {
                if (g == null || !g.IsSpawned) continue;
                var st = g.State.Value;
                if (st == GuestState.WaitForm) form = g;
                else if (st == GuestState.WaitKey) key = g;
                else if (st == GuestState.WaitCheckOut && !g.Marked.Value) outg = g;
            }
            if (form != null)
                return held.type == ItemType.Form ? "Подойди к гостю и нажми E — отдай бланк"
                    : held.IsNone ? "Гость у стойки → возьми бланк на стойке (E)" : "Брось предмет (G) и возьми бланк на стойке";
            if (key != null)
                return held.type == ItemType.Key ? "Нажми E на госте — выдай ключ"
                    : held.IsNone ? "Гость ждёт ключ → возьми ключ свободного (лучше готового) номера с доски за стойкой (E)" : "Брось предмет (G) и возьми ключ с доски за стойкой";
            if (outg != null)
                return held.IsNone ? "Гость выезжает → нажми E с пустыми руками" : "Освободи руки (G) и выпиши гостя (E)";
            foreach (var r in Room.All)
                if (!r.Locked && r.State == RoomState.NeedsService)
                    return $"Номер {r.index} ждёт уборки: принеси со склада то, что на таблице, и вымой пятно шваброй (держи ЛКМ)";
            return "Ждём гостей — таблица задач за стойкой покажет, что делать";
        }

        static void DrawTip(float cx, GameState gs, PlayerHands hands)
        {
            string tip = TipText(gs, hands);
            if (tip == "") return;
            float tw = UiStyle.Label.CalcSize(new GUIContent(tip)).x;
            var r = new Rect(cx - tw / 2f - 18f, 14f, tw + 36f, 40f);
            UiStyle.Plate(r, UiStyle.Kind.Card);
            Ui.Shadowed(new Rect(r.x + 18f, r.y, tw + 8f, 40f), tip, UiStyle.Label, UiStyle.Cream);
        }

        static string DaysWord(int n)
        {
            int a = n % 100, b = n % 10;
            if (a >= 11 && a <= 14) return n + " дней";
            return n + (b == 1 ? " день" : b >= 2 && b <= 4 ? " дня" : " дней");
        }

        // ------------------------------------------------------------ подсказка под прицелом и предмет в руках

        /// <summary>Подсказка под прицелом: «E — действие» рисуется клавишей-кепкой и текстом на плашке.</summary>
        static void DrawHint(float cx, float y, string hint)
        {
            const string ePrefix = "E — ";
            bool key = hint.StartsWith(ePrefix);
            string text = key ? hint.Substring(ePrefix.Length) : hint;
            float tw = UiStyle.Label.CalcSize(new GUIContent(text)).x;
            float w = tw + 28f + (key ? 40f : 0f);
            var r = new Rect(cx - w / 2f, y, w, 40f);
            UiStyle.Plate(r);
            float x = r.x + 14f;
            if (key) { UiStyle.KeyCap(new Rect(x, y + 7f, 26f, 26f), "E"); x += 36f; }
            Ui.Shadowed(new Rect(x, y, tw + 6f, 40f), text, UiStyle.Label, Color.white);
        }

        static void DrawHeld(float cx, float y, HeldItem held)
        {
            string name = Items.Name(held);
            string tip = held.type == ItemType.Sledgehammer ? "ЛКМ — удар  ·  G — бросить"
                : held.type == ItemType.Coffee ? "ЛКМ — выпить  ·  G — бросить"
                : "G — бросить";
            float nw = UiStyle.Header.CalcSize(new GUIContent(name)).x;
            float tw = UiStyle.Small.CalcSize(new GUIContent(tip)).x;
            float w = 58f + nw + 22f + tw + 16f;
            var r = new Rect(cx - w / 2f, y, w, 50f);
            UiStyle.Plate(r);
            UiStyle.ItemIcon(new Rect(r.x + 12f, y + 8f, 34f, 34f), held.type);
            Ui.Shadowed(new Rect(r.x + 58f, y, nw + 6f, 50f), name, UiStyle.Header, UiStyle.Cream);
            Ui.Shadowed(new Rect(r.x + 58f + nw + 22f, y, tw + 8f, 50f), tip, UiStyle.Small, UiStyle.Mustard);
        }

        // ------------------------------------------------------------ касса, налоги, задачи, Game Over

        /// <summary>V2: лента покупок/вызовов под баннером дня (кто что купил).</summary>
        static void DrawToasts(float cx)
        {
            toasts.RemoveAll(t => Time.unscaledTime >= t.Value);
            float y = 214f;
            foreach (var t in toasts)
            {
                float tw = UiStyle.Label.CalcSize(new GUIContent(t.Key)).x;
                var r = new Rect(cx - tw / 2f - 16f, y, tw + 32f, 36f);
                UiStyle.Plate(r);
                Ui.Shadowed(new Rect(r.x + 16f, y, tw + 8f, 36f), t.Key, UiStyle.Label, UiStyle.Mustard);
                y += 42f;
            }
        }

        /// <summary>V2: иконка-таймер кофе (+скорость) под панелью смены.</summary>
        static void DrawBuff()
        {
            var h = PlayerHands.Local;
            if (h == null || !h.CoffeeActive) return;
            var gs = GameState.Instance;
            float total = gs != null ? gs.CoffeeSeconds : 20f;
            var r = new Rect(12f, 142f, 330f, 46f);
            UiStyle.Plate(r);
            UiStyle.ItemIcon(new Rect(r.x + 10f, r.y + 6f, 34f, 34f), ItemType.Coffee);
            Ui.Shadowed(new Rect(r.x + 54f, r.y + 2f, 190f, 24f), $"Кофе: скорость +{Mathf.RoundToInt((h.SpeedMul - 1f) * 100f)}%", UiStyle.Small, UiStyle.Cream);
            Ui.Shadowed(new Rect(r.x + 250f, r.y + 2f, 70f, 24f), TaskBoard.Fmt(h.CoffeeLeft), UiStyle.Small, UiStyle.Mustard);
            UiStyle.Bar(new Rect(r.x + 54f, r.y + 28f, 262f, 10f), h.CoffeeLeft / Mathf.Max(0.1f, total), Palette.Coffee);
        }

        void DrawBanner(float cx)
        {
            if (Time.unscaledTime >= bannerUntil) return;
            var glow = bannerNight ? UiStyle.Neon : UiStyle.Mustard;
            UiStyle.NeonTitle(new Rect(cx - 360, 150, 720, 60), bannerText, glow, new Color(1f, 0.95f, 0.9f), 38);
        }

        void DrawShiftPanel(GameState gs)
        {
            bool playing = gs.Phase.Value == GamePhase.Playing;
            bool due = gs.TaxDue.Value;
            bool night = gs.Night.Value;
            bool soon = gs.NightSoon;
            float pulse = due || soon ? 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
            var alert = new Color(UiStyle.Alert.r * pulse, UiStyle.Alert.g * pulse, UiStyle.Alert.b * pulse, 1f);
            UiStyle.Plate(new Rect(12, 12, 330, 122), due ? UiStyle.Kind.Urgent : UiStyle.Kind.Panel, due ? 0.9f : 1f);
            Ui.Shadowed(new Rect(24, 16, 150, 24), night ? $"День {gs.Day.Value} · ночь" : $"День {gs.Day.Value}", UiStyle.Header, night ? UiStyle.TealLight : UiStyle.Mustard);
            Ui.Shadowed(new Rect(178, 16, 160, 24), $"комнат {gs.RoomCount.Value} · игроков {gs.PlayerCount.Value}", UiStyle.Small, new Color(1, 1, 1, 0.75f));
            Ui.Shadowed(new Rect(24, 42, 100, 36), "Касса", UiStyle.Label, UiStyle.Cream);
            Ui.Shadowed(new Rect(110, 40, 218, 40), $"{gs.Money.Value} $", money, UiStyle.Good);

            string tax;
            if (!playing) tax = "Мотель закрыт";
            else if (due) tax = $"Ночь · счёт {gs.TaxAmount} $ · {TaskBoard.Fmt(gs.PhaseLeft)}";
            else if (night) tax = $"Ночь · счёт оплачен · {TaskBoard.Fmt(gs.PhaseLeft)}";
            else if (soon) tax = $"Скоро ночь! Счёт {gs.TaxAmount} $ · {TaskBoard.Fmt(gs.PhaseLeft)}";
            else tax = $"До ночи {TaskBoard.Fmt(gs.PhaseLeft)} · налог {gs.TaxAmount} $";
            Ui.Shadowed(new Rect(24, 82, 304, 22), tax, UiStyle.Small, due ? new Color(1f, 0.86f, 0.82f) : soon ? UiStyle.Mustard : Color.white);
            float total = gs.PhaseTotal;
            Color barColor = due ? alert : night ? UiStyle.Good : soon ? UiStyle.Mustard : UiStyle.Teal;
            UiStyle.Bar(new Rect(24, 108, 292, 14), playing && total > 0f ? gs.PhaseLeft / total : 0f, barColor);
        }

        void DrawGameOver(GameState gs, NetworkManager nm)
        {
            float cx = Ui.W / 2f;
            UiStyle.Dim(new Rect(0, 0, Ui.W, Ui.H), 0.72f);
            float w = 580f, h = 510f, x = cx - w / 2f, y = Ui.H / 2f - h / 2f;
            UiStyle.Plate(new Rect(x, y, w, h), UiStyle.Kind.Card);
            UiStyle.NeonTitle(new Rect(x, y + 18, w, 60), "Мотель закрыт", UiStyle.Alert, new Color(1f, 0.9f, 0.88f), 42);
            Ui.Shadowed(new Rect(x, y + 80, w, 28), $"Налог {gs.TaxAmount} $ не оплачен вовремя", UiStyle.Center, Color.white);
            Ui.Shadowed(new Rect(x, y + 112, w, 44), $"Продержались {DaysWord(gs.DaysSurvived.Value)}", moneyBig, UiStyle.Mustard);

            UiStyle.Plate(new Rect(x + 40, y + 166, w - 80, 166));
            float ry = y + 172f;
            void Row(string label, string value, Color c)
            {
                Ui.Shadowed(new Rect(x + 60, ry, 240, 34), label, UiStyle.Label, UiStyle.Cream);
                Ui.Shadowed(new Rect(x + w - 300, ry, 240, 34), value, valueRight, c);
                ry += 36f;
            }
            Row("Заработано всего", $"{gs.Earned.Value} $", UiStyle.Good);
            Row("Лучший день", gs.BestDayNo.Value > 0 ? $"день {gs.BestDayNo.Value} · {gs.BestDayEarned.Value} $" : "—", UiStyle.Good);
            Row("Смена длилась", TaskBoard.Fmt(gs.ShiftSeconds.Value), Color.white);
            Row("Рекорд хоста", gs.NewRecord.Value ? $"{DaysWord(gs.RecordDays.Value)} · новый!" : DaysWord(gs.RecordDays.Value), gs.NewRecord.Value ? UiStyle.Mustard : Color.white);

            float by = y + 354f;
            if (nm.IsServer)
            {
                if (UiStyle.Btn(new Rect(x + 90, by, w - 180, 52), "Заново", true)) gs.ServerRestart();
                by += 64f;
            }
            else
            {
                Ui.Shadowed(new Rect(x, by, w, 30), "Ждём, пока хост перезапустит смену…", UiStyle.Center, UiStyle.Mustard);
                by += 52f;
            }
            if (UiStyle.Btn(new Rect(x + 90, by, w - 180, 52), nm.IsServer ? "Закрыть мотель и выйти" : "Выйти в меню"))
                NetBootstrap.Instance.LeaveToMenu();
        }
    }
}
