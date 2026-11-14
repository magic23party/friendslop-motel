using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Самотест V3 (флаг -toilettest, только на хосте; гости в 10 раз быстрее, сантехник — секунды).
    /// Туалет: чистый на старте, пачкается от визитов гостей (V6; без засора в день 1, со 2-го дня засор бывает), моется шваброй (ЛКМ держать),
    /// засор прочищается вантузом или вызванным сантехником (общая механика DirtySite), пока он грязный — терпение гостей у стойки тает
    /// в toiletPatienceMul раз быстрее, задача «Туалет» в списке (срочная, если у стойки ждут), электронная таблица и табло номеров показывают
    /// данные, «Заново» чистит туалет. Итог — строка "[TOILET] selftest OK (all done)" или "FAIL count=N".
    /// </summary>
    public class ToiletSelfTest : MonoBehaviour
    {
        GameState gs;
        GameConfig cfg;
        PlayerHands hands;
        GuestSpawner spawner;
        Toilet wc;
        int fails;

        void Start() { StartCoroutine(Run()); }

        void Check(bool ok, string what)
        {
            if (!ok) fails++;
            Debug.Log($"[TOILET] selftest {(ok ? "OK  " : "FAIL")} {what}");
        }

        IEnumerator WaitFor(System.Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
        }

        void Tp(Vector3 p)
        {
            var cc = hands.GetComponent<CharacterController>();
            cc.enabled = false;
            hands.transform.position = p;
            cc.enabled = true;
            Physics.SyncTransforms();
        }

        static TaskLine? Find(string start)
        {
            var list = new List<TaskLine>();
            TaskBoard.Collect(list);
            foreach (var t in list) if (t.text.StartsWith(start)) return t;
            return null;
        }

        /// <summary>Во сколько раз дедлайн гостя приближается к «сейчас» быстрее реального времени за window секунд.</summary>
        IEnumerator DrainRatio(Guest g, float window, System.Action<float> result)
        {
            g.Until.Value = gs.Now + 60.0;
            yield return null;
            double u0 = g.Until.Value, n0 = gs.Now;
            yield return new WaitForSeconds(window);
            double u1 = g.Until.Value, n1 = gs.Now;
            result((float)((u0 - n0) - (u1 - n1)) / (float)(n1 - n0));
        }

        IEnumerator Run()
        {
            gs = GameState.Instance;
            cfg = gs.config;
            spawner = GuestSpawner.Instance;
            GuestSpawner.Paused = true;
            BrawlDirector.Paused = true;
            yield return WaitFor(() => PlayerHands.All.Count >= 1 && Room.Get(1) != null && Room.Get(1).IsSpawned && Toilet.Instance != null && Toilet.Instance.IsSpawned, 90f);
            yield return WaitFor(() => PlayerHands.All.Count >= 2, 15f);
            foreach (var h in PlayerHands.All) if (h.IsOwner) hands = h;
            if (hands == null) hands = PlayerHands.All[0];
            var ctrl = hands.GetComponent<PlayerController>();
            yield return WaitFor(() => ctrl.Placed, 30f);
            if (gs.Phase.Value == GamePhase.Lobby) { yield return new WaitForSeconds(2f); gs.StartShift(); }
            yield return WaitFor(() => gs.Phase.Value == GamePhase.Playing, 30f);
            yield return new WaitForSeconds(1f);
            wc = Toilet.Instance;
            hands.ServerClear();
            Debug.Log("[TOILET] selftest start");

            // ================= A: чистый на старте, ничего не давит
            Check(wc != null && !wc.Dirty && Toilet.PatienceMul == 1f && Find("Туалет") == null, "toilet clean at shift start: no task, patience multiplier x1");
            Check(wc.stainSpot != null && wc.clogSpot != null && wc.stainSpot.sceneId > 0 && wc.stainSpot.sceneId != wc.clogSpot.sceneId, "toilet has its own mop and plunger targets (scene ids " + wc.stainSpot.sceneId + ", " + wc.clogSpot.sceneId + ")");

            // ================= B: сам по себе не пачкается (V6: нет таймера), пачкается от визитов гостей (день 1 — только пятно)
            yield return new WaitForSeconds(4f);
            Check(!wc.Dirty, "no timer any more: clean toilet stays clean without visits");
            int stains = 0, clogs1 = 0;
            for (int i = 0; i < 300; i++) { wc.ServerSetDirty(false, false, false); wc.ServerGuestVisited(false); if (wc.HasStain) stains++; if (wc.HasClog) clogs1++; }
            Check(stains > 0 && stains < 300 && clogs1 == 0, $"visits dirty the toilet by chance: {stains}/300 ~{cfg.toiletDirtyChance * 100f:0}% stain, no clog on day 1");
            wc.ServerSetDirty(true, false);
            yield return new WaitForSeconds(0.3f);
            var tl = Find("Туалет");
            Check(tl.HasValue && tl.Value.icons != null && tl.Value.icons.Length == 1 && tl.Value.icons[0] == ItemType.Mop, "task list: «Туалет» with a mop icon");
            Check(Toilet.PatienceMul == cfg.toiletPatienceMul, $"dirty toilet: patience multiplier x{Toilet.PatienceMul:0.0#}");

            // ================= C: швабра — ЛКМ держать, пятно смывается; отпустил — прогресс сбрасывается
            hands.ServerTake(new HeldItem(ItemType.Mop));
            Tp(wc.stainSpot.transform.position + new Vector3(-0.9f, 0.05f, 0f));
            hands.ServerApplyUse(true, wc.stainSpot);
            yield return new WaitForSeconds(cfg.mopTime * 0.4f);
            Check(wc.HasStain && wc.MopPct.Value > 0, $"mopping: {wc.MopPct.Value}% after {cfg.mopTime * 0.4f:0.0}s, stain still there");
            hands.ServerApplyUse(false, null);
            yield return new WaitForSeconds(0.3f);
            Check(wc.HasStain && wc.MopPct.Value == 0, "released LMB: progress reset");
            hands.ServerApplyUse(true, wc.stainSpot);
            yield return WaitFor(() => !wc.HasStain, cfg.mopTime + 3f);
            Check(!wc.HasStain && !wc.Dirty && Toilet.PatienceMul == 1f, "held the mop: stain washed, toilet clean, multiplier back to x1");
            hands.ServerApplyUse(false, null);
            hands.ServerClear();
            yield return new WaitForSeconds(0.3f);
            Check(Find("Туалет") == null, "task «Туалет» disappeared");

            // ================= D: терпение гостей у стойки тает в toiletPatienceMul раз быстрее
            var g = spawner.ServerSpawn(false);
            yield return WaitFor(() => g.State.Value == GuestState.WaitForm, 60f);
            Check(g.State.Value == GuestState.WaitForm, "guest reached the desk (path through the vestibule)");
            float clean = 0f, dirty = 0f, back = 0f;
            yield return DrainRatio(g, 2f, v => clean = v);
            Check(Mathf.Abs(clean - 1f) < 0.12f, $"clean toilet: guest's deadline moves x{clean:0.00} (expected x1.00)");
            wc.ServerSetDirty(true, false);
            yield return DrainRatio(g, 2f, v => dirty = v);
            Check(Mathf.Abs(dirty - cfg.toiletPatienceMul) < 0.2f, $"dirty toilet: deadline moves x{dirty:0.00} (expected x{cfg.toiletPatienceMul:0.00})");
            tl = Find("Туалет");
            Check(tl.HasValue && tl.Value.urgent, "task «Туалет» is urgent while a guest waits at the desk");
            wc.ServerSetDirty(false, false);
            yield return DrainRatio(g, 2f, v => back = v);
            Check(Mathf.Abs(back - 1f) < 0.12f, $"toilet cleaned again: deadline moves x{back:0.00}");
            wc.ServerSetDirty(true, false);
            g.ServerDespawn();
            yield return null; yield return null;
            tl = Find("Туалет");
            Check(tl.HasValue && !tl.Value.urgent, "no guest at the desk: the toilet task is not urgent");
            wc.ServerSetDirty(false, false);

            // ================= E: засор — вантуз, ЛКМ держать
            wc.ServerSetDirty(false, true);
            tl = Find("Туалет");
            Check(wc.HasClog && wc.Dirty && Toilet.PatienceMul == cfg.toiletPatienceMul && tl.HasValue && tl.Value.icons[0] == ItemType.Plunger, "clog: toilet dirty, plunger icon in the task list, multiplier x" + cfg.toiletPatienceMul);
            hands.ServerTake(new HeldItem(ItemType.Plunger));
            Tp(wc.clogSpot.transform.position + new Vector3(0.9f, 0.05f, 0f));
            hands.ServerApplyUse(true, wc.clogSpot);
            yield return new WaitForSeconds(cfg.plungeTime * 0.4f);
            Check(wc.HasClog && wc.PlungePct.Value > 0, $"plunging: {wc.PlungePct.Value}% after {cfg.plungeTime * 0.4f:0.0}s, clog still there");
            hands.ServerApplyUse(false, null);
            yield return new WaitForSeconds(0.3f);
            Check(wc.HasClog && wc.PlungePct.Value == 0, "released LMB: progress reset");
            hands.ServerApplyUse(true, wc.clogSpot);
            yield return WaitFor(() => !wc.HasClog, cfg.plungeTime + 3f);
            Check(!wc.HasClog && !wc.Dirty && Toilet.PatienceMul == 1f, "held the plunger: clog cleared, toilet clean");
            hands.ServerApplyUse(false, null);
            hands.ServerClear();

            // ================= F: сантехник (магазин) чинит засор туалета за плату; номер с засором — раньше туалета
            wc.ServerSetDirty(false, true);
            gs.Money.Value = 1000;
            Check(ReferenceEquals(GameState.ClogWithoutPlumber(), wc), "plumber target: the toilet (no clogged rooms)");
            var r3 = Room.Get(3);
            r3.ServerSetNeedsService(0, false, true);
            Check(ReferenceEquals(GameState.ClogWithoutPlumber(), r3), "plumber target: a clogged room goes before the toilet");
            r3.ServerSetReady();
            Check(gs.ServerBuy(ShopItem.Plumber, hands) && wc.PlumberPending && gs.Money.Value == 1000 - cfg.plumberPrice, $"plumber called to the toilet: -{cfg.plumberPrice} $, comes in {gs.PlumberSeconds:0}s");
            Check(!gs.ServerBuy(ShopItem.Plumber, hands) && gs.ShopBlock(ShopItem.Plumber) == "Засоров нет", "second call for the same clog refused");
            Check(wc.HasClog, "clog still there until the plumber finishes");
            yield return WaitFor(() => !wc.HasClog, gs.PlumberSeconds + 5f);
            Check(!wc.HasClog && !wc.PlumberPending && !wc.Dirty, "plumber fixed the toilet clog");

            // ================= G: засор туалета — со 2-го дня
            int clog1 = 0, clog2 = 0;
            gs.Day.Value = 1;
            for (int i = 0; i < 200; i++) { wc.ServerSetDirty(false, false, false); wc.ServerGuestVisited(false); if (wc.HasClog) clog1++; }
            gs.Day.Value = 2;
            for (int i = 0; i < 600; i++) { wc.ServerSetDirty(false, false, false); wc.ServerGuestVisited(false); if (wc.HasClog) clog2++; }
            gs.Day.Value = 1;
            wc.ServerSetDirty(false, false);
            Check(clog1 == 0, "day 1: toilet never clogs (200 rolls)");
            Check(clog2 > 0 && clog2 < 600, $"day 2: toilet clogs sometimes ({clog2}/600 ~{cfg.toiletVisitClogChance * 100f:0}%)");

            // ================= H: электронная таблица и табло номеров показывают состояние мира
            var wall = FindAnyObjectByType<TaskWall>();
            var board = FindAnyObjectByType<RoomBoard>();
            Check(wall != null && board != null, "world boards exist: TaskWall + RoomBoard");
            wc.ServerSetDirty(true, false);
            Room.Get(2).ServerSetNeedsService(1, false);
            Room.Get(4).ServerSetOccupied();
            yield return new WaitForSeconds(0.7f);
            bool onWall = false;
            if (wall != null) foreach (var r in wall.rows) if (r != null && r.text != null && r.text.gameObject.activeSelf && r.text.text.StartsWith("Туалет")) onWall = true;
            Check(onWall, "task wall behind the reception shows the toilet task");
            int free = 0;
            foreach (var r in Room.All) if (r.IsReady) free++;
            Check(board != null && board.countText.text == free.ToString() && board.ofText.text == "из " + Room.ActiveCount() && board.cells[3].sharedMaterial == board.cellMats[2] && board.cells[1].sharedMaterial == board.cellMats[3],
                $"room board: {free} free of {Room.ActiveCount()}, room 4 occupied (red), room 2 needs service (amber)");
            Room.Get(2).ServerSetReady(); Room.Get(4).ServerSetReady();

            // ================= I: «Заново» — туалет снова чистый
            wc.ServerSetDirty(true, true);
            gs.GameOver();
            gs.ServerRestart();
            yield return null; yield return null;
            Check(gs.Phase.Value == GamePhase.Playing && !wc.Dirty && !wc.PlumberPending, "restart: toilet is clean again");

            Debug.Log("[TOILET] selftest " + (fails == 0 ? "OK (all done)" : "FAIL count=" + fails));
        }
    }
}
