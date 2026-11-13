using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// V2: меню магазина (IMGUI, виртуальные 720 px по высоте). Открывается локально по E у терминала, курсор свободен
    /// (как пауза: PlayerController.MenuOpen), закрывается Esc / кнопкой. Рисует MotelHud.
    /// </summary>
    public static class ShopUi
    {
        static bool open;
        /// <summary>Скриншоты: показать меню без игрока.</summary>
        public static bool Shot;

        public static bool IsOpen => Shot || (open && PlayerController.Local != null && PlayerController.Local.MenuOpen);

        public static void Open()
        {
            var me = PlayerController.Local;
            if (me == null) return;
            me.SetMenu(true);
            open = true;
        }

        public static void Close() { open = false; }

        static ItemType IconOf(ShopItem i)
        {
            switch (i)
            {
                case ShopItem.Cart: return ItemType.Towel;
                case ShopItem.Coffee: return ItemType.Coffee;
                case ShopItem.Annex: return ItemType.Key;
                case ShopItem.Bulbs: return ItemType.Bulb;
                default: return ItemType.Plunger;
            }
        }

        static readonly ShopItem[] Rows = { ShopItem.Cart, ShopItem.Coffee, ShopItem.Annex, ShopItem.Plunger, ShopItem.Bulbs, ShopItem.Plumber };
        static GUIStyle price;

        public static void Draw()
        {
            var gs = GameState.Instance;
            var me = PlayerController.Local;
            if (gs == null) return;
            if (!Shot && gs.Phase.Value != GamePhase.Playing) { if (me != null) me.SetMenu(false); return; }
            if (price == null) price = new GUIStyle(UiStyle.Header) { alignment = TextAnchor.MiddleRight, fontSize = 22 };

            float cx = Ui.W / 2f;
            const float w = 900f, rowH = 72f;
            float h = 150f + rowH * Rows.Length + 70f;
            float x = cx - w / 2f, y = Mathf.Max(8f, (Ui.H - h) / 2f);
            UiStyle.Dim(new Rect(0, 0, Ui.W, Ui.H), 0.55f);
            UiStyle.Plate(new Rect(x, y, w, h), UiStyle.Kind.Card);
            UiStyle.NeonTitle(new Rect(x, y + 14, w, 52), "Магазин", UiStyle.Teal, UiStyle.Cream, 36);
            Ui.Shadowed(new Rect(x + 30, y + 72, w - 60, 30), $"В общей кассе: {gs.Money.Value} $  ·  покупка видна всем", UiStyle.Label, UiStyle.Mustard);

            float ry = y + 112f;
            foreach (var it in Rows)
            {
                string block = gs.ShopBlock(it);
                bool owned = block == "Куплено";
                UiStyle.Plate(new Rect(x + 16, ry, w - 32, rowH - 6), owned ? UiStyle.Kind.Field : UiStyle.Kind.Panel);
                UiStyle.ItemIcon(new Rect(x + 30, ry + 14, 38, 38), IconOf(it));
                Ui.Shadowed(new Rect(x + 84, ry + 4, 470, 26), GameState.ShopName(it), UiStyle.Header, UiStyle.Cream);
                Ui.Shadowed(new Rect(x + 84, ry + 28, 520, 36), gs.ShopInfo(it), UiStyle.Small, new Color(1, 1, 1, 0.78f));
                Ui.Shadowed(new Rect(x + w - 330, ry + 4, 120, 56), $"{gs.Price(it)} $", price, owned ? new Color(1, 1, 1, 0.4f) : UiStyle.Good);
                var br = new Rect(x + w - 196, ry + 8, 164, 46);
                if (owned) Ui.Shadowed(br, "Куплено", UiStyle.Center, UiStyle.Good);
                else if (block == "")
                {
                    if (UiStyle.Btn(br, "Купить", true) && PlayerHands.Local != null) PlayerHands.Local.RequestBuy(it);
                }
                else
                {
                    GUI.enabled = false;
                    UiStyle.Btn(br, block == "Не хватает денег" ? "Не хватает" : block == "Нет закрытых номеров" ? "Нет номеров" : block);
                    GUI.enabled = true;
                }
                ry += rowH;
            }

            if (UiStyle.Btn(new Rect(cx - 110, y + h - 62, 220, 46), "Закрыть (Esc)") && me != null) me.SetMenu(false);
        }
    }
}
