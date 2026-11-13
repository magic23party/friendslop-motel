using System.Collections;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Самотест V2 (флаг -shoptest, только на хосте; гости в 10 раз быстрее, кофе и сантехник — секунды).
    /// Серверные методы те же, что у игрока: ItemSource / RoomSpot / PlayerHands.ServerApplyUse / GameState.ServerBuy.
    /// Покупка каждого товара списывает деньги и даёт эффект (стопка из 3, кофе +10% и его конец, номера 11–15, вантуз, сантехник,
    /// лампочка); нехватка денег — отказ; засор не даёт заселить, гость с лампой платит на lampPenalty меньше; «Заново» сбрасывает покупки.
    /// Итог — строка "[SHOP] selftest OK (all done)" или "FAIL count=N".
    /// </summary>
    public class ShopSelfTest : MonoBehaviour
    {
        GameState gs;
        GameConfig cfg;
        KeyBoard board;
        PlayerHands hands;
        GuestSpawner spawner;
        int fails;

        void Start() { StartCoroutine(Run()); }

        void Check(bool ok, string what)
        {
            if (!ok) fails++;
            Debug.Log($"[SHOP] selftest {(ok ? "OK  " : "FAIL")} {what}");
        }

        IEnumerator WaitFor(System.Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
        }

        static ItemSource Src(ItemType t)
        {
            foreach (var s in FindObjectsByType<ItemSource>(FindObjectsInactive.Include))
                if (s.type == t) return s;
            return null;
        }

        /// <summary>Игрок хоста на месте: проверка дистанции серверной швабры/вантуза идёт от глаз игрока.</summary>
        void Tp(Vector3 p)
        {
            var cc = hands.GetComponent<CharacterController>();
            cc.enabled = false;
            hands.transform.position = p;
            cc.enabled = true;
            Physics.SyncTransforms();
        }

        void Press(ItemType shelf) { var s = Src(shelf); s.ServerInteract(hands, hands.Held.Value); }

        bool Buy(ShopItem it, int money)
        {
            gs.Money.Value = money;
            return gs.ServerBuy(it, hands);
        }

        IEnumerator Run()
        {
            gs = GameState.Instance;
            cfg = gs.config;
            board = MotelMap.Instance.keyBoard;
            spawner = GuestSpawner.Instance;
            GuestSpawner.Paused = true;
            yield return WaitFor(() => PlayerHands.All.Count >= 1 && gs.Phase.Value == GamePhase.Playing && Room.Get(1) != null && Room.Get(1).IsSpawned && Room.Get(20) != null && Room.Get(20).IsSpawned, 90f);
            yield return new WaitForSeconds(2f);
            foreach (var h in PlayerHands.All) if (h.IsOwner) hands = h;
            if (hands == null) hands = PlayerHands.All[0];
            hands.ServerClear();
            Debug.Log("[SHOP] selftest start");

            // ================= 0: новые проблемы появляются по дням (шанс при выезде)
            var r8 = Room.Get(8);
            int clog1 = 0, lamp1 = 0, clog2 = 0, lamp2 = 0, clog3 = 0, lamp3 = 0;
            for (int i = 0; i < 150; i++) { r8.ServerSetReady(); r8.ServerSetNeedsService(); if (r8.HasClog) clog1++; if (r8.LampBroken) lamp1++; }
            gs.Day.Value = 2;
            for (int i = 0; i < 300; i++) { r8.ServerSetReady(); r8.ServerSetNeedsService(); if (r8.HasClog) clog2++; if (r8.LampBroken) lamp2++; }
            gs.Day.Value = 3;
            for (int i = 0; i < 300; i++) { r8.ServerSetReady(); r8.ServerSetNeedsService(); if (r8.HasClog) clog3++; if (r8.LampBroken) lamp3++; }
            gs.Day.Value = 1;
            r8.ServerSetReady();
            Check(clog1 == 0 && lamp1 == 0, $"day 1: no clogs, no lamps (150 check-outs)");
            Check(clog2 > 0 && clog2 < 150 && lamp2 == 0, $"day 2: clogs appear ({clog2}/300 ~20%), lamps not yet ({lamp2})");
            Check(clog3 > 0 && lamp3 > 0, $"day 3: clogs ({clog3}/300) and lamps ({lamp3}/300) both appear");
            Check(r8.State == RoomState.Ready && !r8.LampBroken && !r8.HasClog, "room 8 reset to Ready");

            // ================= A: старт — ничего не куплено, пристройка закрыта, купленное скрыто
            Check(gs.Upgrades.Value == 0 && !gs.HasCart, "nothing bought at shift start");
            Check(gs.RoomCount.Value == cfg.smallRooms && Room.Get(11).Locked && Room.Get(15).Locked, $"small team: rooms 1..{cfg.smallRooms} open, 11..15 closed");
            Check(Src(ItemType.Coffee) != null && !Src(ItemType.Coffee).gameObject.activeInHierarchy, "coffee machine hidden before purchase");
            Check(Src(ItemType.Plunger) != null && !Src(ItemType.Plunger).gameObject.activeInHierarchy && Src(ItemType.Bulb) != null && !Src(ItemType.Bulb).gameObject.activeInHierarchy, "plunger and bulb shelves hidden before purchase");

            // ================= B: без тележки в руках один предмет (E по тому же стеллажу — положить обратно)
            Press(ItemType.Towel);
            Check(hands.Held.Value == new HeldItem(ItemType.Towel), "no cart: take 1 towel");
            Press(ItemType.Towel);
            Check(hands.Held.Value.IsNone, "no cart: second E puts it back (no stack)");

            // ================= C: нехватка денег — отказ для всех товаров
            bool anyBought = false;
            foreach (ShopItem it in new[] { ShopItem.Cart, ShopItem.Coffee, ShopItem.Annex, ShopItem.Plunger, ShopItem.Bulbs })
                anyBought |= Buy(it, gs.Price(it) - 1);
            Check(!anyBought && gs.Money.Value == gs.Price(ShopItem.Bulbs) - 1 && gs.Upgrades.Value == 0 && gs.RoomCount.Value == cfg.smallRooms, "1 $ short: every purchase refused, money and upgrades untouched");
            Check(gs.ShopBlock(ShopItem.Cart) == "Не хватает денег", "shop menu says why: " + gs.ShopBlock(ShopItem.Cart));

            // ================= D: тележка — стопка из 3, занос по одному
            Check(Buy(ShopItem.Cart, cfg.cartPrice + 50) && gs.Money.Value == 50 && gs.HasCart, $"cart bought: -{cfg.cartPrice} $ -> till 50 $");
            Press(ItemType.Towel); Press(ItemType.Towel); Press(ItemType.Towel);
            Check(hands.Held.Value == new HeldItem(ItemType.Towel, 3) && Items.StackCount(hands.Held.Value) == 3, "cart: stack of 3 towels in hands");
            Press(ItemType.Soap);
            Check(hands.Held.Value == new HeldItem(ItemType.Towel, 3), "cart: different item is not added to the stack (hands busy)");
            Press(ItemType.Towel);
            Check(hands.Held.Value.IsNone, "cart: full stack, E puts it back");
            Press(ItemType.Towel); Press(ItemType.Towel); Press(ItemType.Towel);
            for (int n = 1; n <= 3; n++)
            {
                var r = Room.Get(n);
                r.ServerSetNeedsService(1, false);          // нужно только полотенце
                var spot = r.supplyPoints[0].GetComponent<RoomSpot>();
                spot.ServerInteract(hands, hands.Held.Value);
                int left = Items.StackCount(hands.Held.Value);
                Check(left == 3 - n && r.IsReady, $"stack: delivered 1 towel to room {n} -> {left} left in hands, room Ready");
            }

            // ================= E: кофемашина — кофе бесплатно, ЛКМ выпить: +10% скорости на время, потом конец
            Check(Buy(ShopItem.Coffee, cfg.coffeePrice + 10) && gs.Money.Value == 10, $"coffee machine bought: -{cfg.coffeePrice} $");
            yield return null;
            Check(Src(ItemType.Coffee).gameObject.activeInHierarchy, "coffee machine appeared in the lobby");
            int m0 = gs.Money.Value;
            Press(ItemType.Coffee);
            Check(hands.Held.Value == new HeldItem(ItemType.Coffee) && gs.Money.Value == m0, "coffee is free and occupies the hands");
            hands.ServerApplyUse(true, null);              // ЛКМ: выпить
            Check(hands.Held.Value.IsNone && hands.CoffeeActive && Mathf.Abs(hands.SpeedMul - (1f + cfg.coffeeBonus)) < 0.001f, $"drunk: speed x{hands.SpeedMul:0.00}, {hands.CoffeeLeft:0.0}s left");
            yield return new WaitForSeconds(1.5f);
            Check(hands.CoffeeActive && hands.CoffeeLeft < gs.CoffeeSeconds, "buff still active after 1.5s");
            yield return WaitFor(() => !hands.CoffeeActive, gs.CoffeeSeconds + 3f);
            Check(!hands.CoffeeActive && Mathf.Abs(hands.SpeedMul - 1f) < 0.001f, "buff ended: speed x1.00");

            // ================= F: пристройка — номера 11–15 открыты и выдаются
            Check(Buy(ShopItem.Annex, cfg.annexPrice + 5) && gs.Money.Value == 5 && gs.RoomCount.Value == cfg.smallRooms + cfg.annexRooms, $"annex bought: -{cfg.annexPrice} $, rooms {gs.RoomCount.Value}");
            yield return null; yield return null;
            bool open = true;
            for (int n = 11; n <= 15; n++) open &= !Room.Get(n).Locked && Room.Get(n).IsReady && !Room.Get(n).blocker.activeSelf;
            Check(open && Room.Get(16).Locked && Room.Get(16).blocker.activeSelf, "rooms 11..15 open (door walls gone, Ready), 16 still closed");
            var picked = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 400; i++) { var pr = Room.PickReady(board); if (pr != null) picked.Add(pr.index); }
            bool anyAnnex = false, anyBeyond = false;
            foreach (int idx in picked) { if (idx >= 11 && idx <= 15) anyAnnex = true; if (idx > 15) anyBeyond = true; }
            Check(anyAnnex && !anyBeyond, "guests can be given rooms 11..15 (and never 16+)");
            Check(!Buy(ShopItem.Annex, 1000) && gs.Money.Value == 1000 && gs.ShopBlock(ShopItem.Annex) == "Куплено", "annex cannot be bought twice");

            // ================= G: засор не даёт заселить; гость в номере с лампой платит на lampPenalty меньше (в номере из пристройки)
            Room.Get(13).ServerSetNeedsService(0, false, true);
            Check(!Room.Get(13).IsReady && Room.Get(13).State == RoomState.NeedsService && Room.Get(13).HasClog, "room 13 clogged: NeedsService, not Ready");
            bool pickedClogged = false;
            for (int i = 0; i < 300; i++) { var pr = Room.PickReady(board); if (pr != null && pr.index == 13) pickedClogged = true; }
            Check(!pickedClogged, "clogged room is never offered to a guest");
            Room.Get(12).ServerBreakLamp();
            var g = spawner.ServerSpawn(false);
            yield return WaitFor(() => g.State.Value == GuestState.WaitForm, 40f);
            hands.ServerClear();
            hands.ServerTake(new HeldItem(ItemType.Form));
            g.ServerInteract(hands, hands.Held.Value);
            yield return WaitFor(() => g.State.Value == GuestState.WaitKey, 10f);
            board.ServerTake(13);
            hands.ServerTake(new HeldItem(ItemType.Key, 13));
            g.ServerInteract(hands, hands.Held.Value);
            Check(g.State.Value == GuestState.WaitKey && hands.Held.Value == new HeldItem(ItemType.Key, 13), "guest refuses the key of the clogged room");
            board.slots[12].ServerInteract(hands, hands.Held.Value);
            board.ServerTake(12);
            hands.ServerTake(new HeldItem(ItemType.Key, 12));
            g.ServerInteract(hands, hands.Held.Value);
            Check(hands.Held.Value.IsNone && Room.Get(12).State == RoomState.Occupied && Room.Get(12).LampBroken && g.RoomNo.Value == 12, "guest settles in room 12 (annex) with a broken lamp: lamp stays broken");
            yield return WaitFor(() => g.State.Value == GuestState.Living, 60f);
            Check(g.State.Value == GuestState.Living, "guest reached the annex room");
            yield return WaitFor(() => g.State.Value == GuestState.WaitCheckOut, 60f);
            hands.ServerClear();
            m0 = gs.Money.Value;
            g.ServerInteract(hands, hands.Held.Value);
            int expect = cfg.guestPays - cfg.lampPenalty;
            Check(gs.Money.Value == m0 + expect && hands.Held.Value == new HeldItem(ItemType.Key, 12), $"guest with a broken lamp paid {gs.Money.Value - m0} $ (expected {expect} $ = {cfg.guestPays} - {cfg.lampPenalty})");
            board.slots[11].ServerInteract(hands, hands.Held.Value);
            Room.Get(13).ServerSetReady();

            // ================= H: вантуз — ЛКМ держать, засор прочищается
            Check(Buy(ShopItem.Plunger, cfg.plungerPrice + 5) && gs.Money.Value == 5, $"plunger bought: -{cfg.plungerPrice} $");
            yield return null;
            Check(Src(ItemType.Plunger).gameObject.activeInHierarchy, "plunger shelf appeared in the warehouse");
            Press(ItemType.Plunger);
            Check(hands.Held.Value == new HeldItem(ItemType.Plunger), "plunger taken from the shelf");
            var r5 = Room.Get(5);
            r5.ServerSetNeedsService(0, false, true);
            Check(r5.HasClog && !r5.IsReady, "room 5 clogged");
            Tp(r5.inside.position);
            hands.ServerApplyUse(true, r5.clogSpot);       // как RPC: ЛКМ зажата на засоре
            yield return new WaitForSeconds(cfg.plungeTime * 0.4f);
            Check(r5.HasClog && r5.PlungePct.Value > 0, $"plunging: {r5.PlungePct.Value}% after {cfg.plungeTime * 0.4f:0.0}s, clog still there");
            hands.ServerApplyUse(false, null);             // отпустил ЛКМ — прогресс сбрасывается
            yield return new WaitForSeconds(0.3f);
            Check(r5.HasClog && r5.PlungePct.Value == 0, "released LMB: progress reset");
            hands.ServerApplyUse(true, r5.clogSpot);
            yield return WaitFor(() => !r5.HasClog, cfg.plungeTime + 3f);
            Check(!r5.HasClog && r5.IsReady, $"held {cfg.plungeTime:0}s: clog cleared, room 5 Ready, plunger kept in hands: " + hands.Held.Value.type);
            hands.ServerApplyUse(false, null);
            hands.ServerClear();

            // ================= I: сантехник — за плату чинит сам
            var r6 = Room.Get(6);
            Check(!Buy(ShopItem.Plumber, 1000) && gs.Money.Value == 1000 && gs.ShopBlock(ShopItem.Plumber) == "Засоров нет", "no clogged room: plumber cannot be called");
            r6.ServerSetNeedsService(0, false, true);
            Check(!Buy(ShopItem.Plumber, cfg.plumberPrice - 1) && r6.HasClog && !r6.PlumberPending, "plumber 1 $ short: refused");
            Check(Buy(ShopItem.Plumber, cfg.plumberPrice + 10) && gs.Money.Value == 10 && r6.PlumberPending, $"plumber called: -{cfg.plumberPrice} $, comes in {gs.PlumberSeconds:0}s");
            Check(!Buy(ShopItem.Plumber, 1000) && gs.Money.Value == 1000, "second call for the same clog refused, no money taken");
            Check(r6.HasClog, "clog still there until the plumber finishes");
            yield return WaitFor(() => !r6.HasClog, gs.PlumberSeconds + 5f);
            Check(!r6.HasClog && r6.IsReady && !r6.PlumberPending, "plumber fixed the clog: room 6 Ready");

            // ================= J: ящик лампочек — лампочка чинит лампу
            Check(Buy(ShopItem.Bulbs, cfg.bulbsPrice + 5) && gs.Money.Value == 5, $"bulbs bought: -{cfg.bulbsPrice} $");
            yield return null;
            Check(Src(ItemType.Bulb).gameObject.activeInHierarchy, "bulb shelf appeared in the warehouse");
            Press(ItemType.Bulb);
            Check(hands.Held.Value == new HeldItem(ItemType.Bulb), "bulb taken from the shelf");
            var r7 = Room.Get(7);
            r7.lampSpot.ServerInteract(hands, hands.Held.Value);
            Check(hands.Held.Value == new HeldItem(ItemType.Bulb), "working lamp: bulb not taken");
            r7.ServerBreakLamp();
            yield return null; yield return null;
            Check(r7.LampBroken && r7.lampOff.activeSelf && !r7.lampOn.activeSelf, "lamp burnt out: dark shade shown");
            r7.lampSpot.ServerInteract(hands, hands.Held.Value);
            yield return null; yield return null;
            Check(!r7.LampBroken && hands.Held.Value.IsNone && r7.lampOn.activeSelf && !r7.lampOff.activeSelf, "bulb screwed in: lamp lit, bulb used up");

            // ================= K: значки табличек и задач
            r7.ServerSetNeedsService(5, true, true, true);        // полотенце + салфетки + пятно + засор + лампа
            yield return null; yield return null;
            int icons = 0; foreach (var ic in r7.signIcons) if (ic != null && ic.activeSelf) icons++;
            var tasks = new System.Collections.Generic.List<TaskLine>();
            TaskBoard.Collect(tasks);
            bool taskHas = false;
            foreach (var t in tasks) if (t.text.StartsWith("Номер 7") && t.icons != null && t.icons.Length == 5) taskHas = true;
            Check(icons == 5 && taskHas, $"sign shows {icons} icons and the task list has room 7 with 5 icons (supplies + stain + clog + lamp)");
            r7.ServerSetReady();

            // ================= L: «Заново» — покупки, номера и кофе сбрасываются
            gs.GameOver();
            gs.ServerRestart();
            yield return null; yield return null;
            Check(gs.Phase.Value == GamePhase.Playing && gs.Upgrades.Value == 0 && !hands.CoffeeActive && gs.RoomCount.Value == cfg.smallRooms && Room.Get(12).Locked && !Room.Get(12).LampBroken && !Room.Get(13).HasClog,
                "restart: purchases, annex, lamps and clogs reset");

            // ================= M: путь игрока — RPC покупки проверяет дистанцию до терминала
            var term = MotelMap.Instance.shopTerminal;
            gs.Money.Value = 1000;
            Tp(new Vector3(0f, 0.05f, -3f));
            hands.RequestBuy(ShopItem.Cart);
            yield return new WaitForSeconds(0.5f);
            Check(!gs.HasCart && gs.Money.Value == 1000, "RPC buy from the other end of the lobby is refused (too far from the terminal)");
            Tp(term.position + new Vector3(1.6f, 0.05f, 0f));
            hands.RequestBuy(ShopItem.Cart);
            yield return new WaitForSeconds(0.5f);
            Check(gs.HasCart && gs.Money.Value == 1000 - cfg.cartPrice, "RPC buy next to the terminal works: cart bought for " + cfg.cartPrice + " $");

            Debug.Log("[SHOP] selftest " + (fails == 0 ? "OK (all done)" : "FAIL count=" + fails));
        }
    }
}
