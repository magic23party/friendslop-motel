using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Самотест V6 (флаг -dirttest, только на хосте; гости в 10 раз быстрее): грязь и гости.
    /// A: «грязный» гость оставляет пятна (≤ dirtMaxPerGuest), швабра их моет (ЛКМ держать; отпустил — сброс); пятна видны прицелу только со шваброй.
    /// B: порог пятен включает множитель терпения (складывается с туалетным), строка «Грязь на полу: N» в списке задач и на таблице.
    /// C: неготовые номера: ключ выдаётся, гость считает проблемы при входе (1 → −50 %, 2+ → 0), смайлик и «−N% $», оплата при выезде,
    ///    проблемы остаются и после выезда. D: гость ходит в туалет, визит пачкает его по шансу (в тесте 100 %), E: засорённый — уходит от двери.
    /// F: «Заново» убирает пятна. Итог — строка "[DIRT] selftest OK (all done)" или "FAIL count=N".
    /// </summary>
    public class DirtSelfTest : MonoBehaviour
    {
        GameState gs;
        GameConfig cfg;
        PlayerHands hands;
        GuestSpawner spawner;
        KeyBoard board;
        Toilet wc;
        int fails;

        void Start() { StartCoroutine(Run()); }

        void Check(bool ok, string what)
        {
            if (!ok) fails++;
            Debug.Log($"[DIRT] selftest {(ok ? "OK  " : "FAIL")} {what}");
        }

        IEnumerator WaitFor(System.Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
        }

        static bool Gone(Guest g) => g == null || !g.IsSpawned;

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

        IEnumerator DrainRatio(Guest g, float window, System.Action<float> result)
        {
            g.Until.Value = gs.Now + 60.0;
            yield return null;
            double u0 = g.Until.Value, n0 = gs.Now;
            yield return new WaitForSeconds(window);
            double u1 = g.Until.Value, n1 = gs.Now;
            result((float)((u0 - n0) - (u1 - n1)) / (float)(n1 - n0));
        }

        /// <summary>Гость у стойки → бланк → ключ номера r (даже неготового) → идёт в номер.</summary>
        IEnumerator CheckIn(Guest g, Room r)
        {
            yield return WaitFor(() => g.State.Value == GuestState.WaitForm, 60f);
            hands.ServerClear();
            hands.ServerTake(new HeldItem(ItemType.Form));
            g.ServerInteract(hands, hands.Held.Value);
            yield return WaitFor(() => g.State.Value == GuestState.WaitKey, 10f);
            board.ServerTake(r.index);
            hands.ServerTake(new HeldItem(ItemType.Key, r.index));
            g.ServerInteract(hands, hands.Held.Value);
            hands.ServerClear();
        }

        void Fill(int n)
        {
            DirtStain.ServerRemoveAll();
            for (int i = 0; i < n; i++) DirtStain.ServerSpawn(cfg, new Vector3(-4f + 1.2f * i, 0f, -3f));
        }

        IEnumerator Run()
        {
            gs = GameState.Instance;
            cfg = gs.config;
            spawner = GuestSpawner.Instance;
            board = MotelMap.Instance.keyBoard;
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
            Debug.Log("[DIRT] selftest start");

            Check(DirtStain.Count == 0 && Find("Грязь на полу") == null && DirtStain.PatienceMul == 1f && cfg.dirtPrefab != null, "start: no dirt, no task, patience x1, dirt prefab registered in the config");
            Check(Guest.ToiletVisitsEnabled, "toilet visits are enabled in -dirttest");

            // ================= A: грязный гость оставляет пятна по пути от входа до номера
            var r1 = Room.Get(1);
            var gA = spawner.ServerSpawn(false, true);
            Check(gA != null && gA.IsDirty, "dirty guest spawned");
            yield return CheckIn(gA, r1);
            yield return WaitFor(() => gA.State.Value == GuestState.Living, 60f);
            gA.ServerExtendStay(150f);                          // /10 = +15 с: хватит на проверки со шваброй
            int n = DirtStain.Count;
            Check(n >= 3 && n <= cfg.dirtMaxPerGuest, $"dirty guest left {n} stain(s) on the way to room 1 (3..{cfg.dirtMaxPerGuest})");
            Check(gA.RoomProblems == 0 && gA.PayCut.Value == 0, "room 1 was ready: no problems, full pay");

            // швабра: цель — только со шваброй; ЛКМ держать — пятно смывается; отпустил — прогресс сбрасывается
            DirtStain d = null;
            foreach (var x in DirtStain.All) { d = x; break; }
            Tp(d.transform.position + new Vector3(0.9f, 0.05f, 0f));
            yield return new WaitForSeconds(0.3f);
            var above = d.transform.position + new Vector3(0f, 1.2f, 0f);
            hands.ServerClear();
            var aimNoMop = PlayerHands.AimFrom(above, Vector3.down, 3f, hands);
            hands.ServerTake(new HeldItem(ItemType.Mop));
            var aimMop = PlayerHands.AimFrom(above, Vector3.down, 3f, hands);
            Check(!(aimNoMop is DirtStain) && ReferenceEquals(aimMop, d), "aim ray sees a dirt stain only with a mop in hands (other targets are not blocked)");
            hands.ServerApplyUse(true, d);
            yield return new WaitForSeconds(cfg.dirtMopTime * 0.4f);
            Check(d != null && d.IsSpawned && d.MopPct.Value > 0, $"mopping a stain: {(d != null ? d.MopPct.Value : 0)}% after {cfg.dirtMopTime * 0.4f:0.0}s");
            hands.ServerApplyUse(false, null);
            yield return new WaitForSeconds(0.3f);
            Check(d != null && d.IsSpawned && d.MopPct.Value == 0, "released LMB: progress reset");
            hands.ServerApplyUse(true, d);
            yield return WaitFor(() => d == null || !d.IsSpawned, cfg.dirtMopTime + 3f);
            Check(DirtStain.Count == n - 1, $"held the mop {cfg.dirtMopTime:0.#}s: the stain is gone ({DirtStain.Count} left of {n})");
            hands.ServerApplyUse(false, null);
            hands.ServerClear();
            DirtStain.ServerRemoveAll();
            wc.ServerSetDirty(false, false, false);
            Guest.VisitsOverride = false;                       // пока гость A живёт — в туалет не ходит
            yield return WaitFor(() => gA.State.Value == GuestState.WaitCheckOut, 40f);
            hands.ServerClear();
            gA.ServerInteract(hands, hands.Held.Value);
            hands.ServerClear();
            board.ServerReturn(1);
            yield return WaitFor(() => Gone(gA), 60f);
            yield return null;

            // ================= B: порог пятен — множитель терпения (складывается с туалетным), строка задачи
            Fill(cfg.dirtThreshold - 1);
            yield return new WaitForSeconds(0.3f);
            var tl = Find("Грязь на полу");
            Check(DirtStain.Count == cfg.dirtThreshold - 1 && DirtStain.PatienceMul == 1f && tl.HasValue && tl.Value.text == "Грязь на полу: " + (cfg.dirtThreshold - 1) && !tl.Value.urgent,
                $"below the threshold ({cfg.dirtThreshold}): task «{(tl.HasValue ? tl.Value.text : "-")}» is not urgent, patience x1");
            var gB = spawner.ServerSpawn(false, false);
            yield return WaitFor(() => gB.State.Value == GuestState.WaitForm, 60f);
            float r0 = 0f, r1v = 0f, r2 = 0f;
            yield return DrainRatio(gB, 2f, v => r0 = v);
            Check(Mathf.Abs(r0 - 1f) < 0.12f, $"few stains: deadline moves x{r0:0.00} (expected x1.00)");
            Fill(cfg.dirtThreshold);
            yield return new WaitForSeconds(0.3f);
            tl = Find("Грязь на полу");
            Check(DirtStain.Count == cfg.dirtThreshold && tl.HasValue && tl.Value.urgent && tl.Value.icons != null && tl.Value.icons[0] == ItemType.Mop, "at the threshold: the task is urgent (mop icon)");
            yield return DrainRatio(gB, 2f, v => r1v = v);
            Check(Mathf.Abs(r1v - cfg.dirtPatienceMul) < 0.2f, $"{cfg.dirtThreshold} stains: deadline moves x{r1v:0.00} (expected x{cfg.dirtPatienceMul:0.00})");
            wc.ServerSetDirty(true, false, false);
            float expectBoth = 1f + (cfg.dirtPatienceMul - 1f) + (cfg.toiletPatienceMul - 1f);
            yield return DrainRatio(gB, 2f, v => r2 = v);
            Check(Mathf.Abs(r2 - expectBoth) < 0.25f, $"dirt + dirty toilet add up: deadline moves x{r2:0.00} (expected x{expectBoth:0.00})");
            wc.ServerSetDirty(false, false, false);
            var wall = FindAnyObjectByType<TaskWall>();
            yield return new WaitForSeconds(0.7f);
            bool onWall = false;
            if (wall != null) foreach (var row in wall.rows) if (row != null && row.text != null && row.text.gameObject.activeSelf && row.text.text.StartsWith("Грязь на полу: " + cfg.dirtThreshold)) onWall = true;
            Check(onWall, "task wall behind the reception shows «Грязь на полу: " + cfg.dirtThreshold + "»");
            gB.ServerDespawn();
            DirtStain.ServerRemoveAll();
            yield return null; yield return null;
            Check(DirtStain.Count == 0 && Find("Грязь на полу") == null, "all stains removed: the task is gone");

            // ================= C: неготовые номера — гость считает проблемы при входе, платит 50 % / 0
            var r2room = Room.Get(2);
            r2room.ServerSetNeedsService(1, false);              // полотенца нет: 1 проблема
            Check(!r2room.IsReady && r2room.CanAssign && r2room.ProblemCount == 1, "room 2 is not ready (1 problem) but can be assigned");
            var gC1 = spawner.ServerSpawn(false, false);
            yield return CheckIn(gC1, r2room);
            Check(gC1.State.Value == GuestState.ToRoom && r2room.State == RoomState.Occupied && r2room.ProblemCount == 1, "guest takes the key of the unready room: Occupied, the problem is still there");
            yield return WaitFor(() => gC1.State.Value == GuestState.Living, 60f);
            yield return new WaitForSeconds(0.5f);
            Check(gC1.RoomProblems == 1 && gC1.PayCut.Value == 50, $"1 problem found at entry: pay cut {gC1.PayCut.Value}%");
            Check(gC1.Face != null && gC1.Face.Shown && gC1.Face.Caption == Guest.PayCutText(50), $"angry face over the head: «{(gC1.Face != null ? gC1.Face.Caption : "-")}»");
            yield return WaitFor(() => gC1.State.Value == GuestState.WaitCheckOut, 60f);
            Check(r2room.State == RoomState.NeedsService && (r2room.Missing & 1) != 0, "after check-out the old problem (no towel) is still there + new ones: " + r2room.NeedsText());
            Check(gC1.Face != null && gC1.Face.Shown && gC1.Face.Caption == Guest.PayCutText(50), "at check-out the face and «-50% $» are shown again");
            hands.ServerClear();
            int m0 = gs.Money.Value;
            gC1.ServerInteract(hands, hands.Held.Value);
            int half = Mathf.RoundToInt(cfg.guestPays * (1f - cfg.oneProblemPayCut));
            Check(gs.Money.Value == m0 + half && hands.Held.Value == new HeldItem(ItemType.Key, 2), $"1 problem: guest paid {gs.Money.Value - m0} $ (expected {half} $ = 50% of {cfg.guestPays})");
            hands.ServerClear();
            board.ServerReturn(2);
            yield return WaitFor(() => Gone(gC1), 60f);

            var r3room = Room.Get(3);
            r3room.ServerSetNeedsService(0, true, true, true);   // грязь + засор + лампа: 3 проблемы
            int clogProblems = r3room.ProblemCount;
            var gC2 = spawner.ServerSpawn(false, false);
            yield return CheckIn(gC2, r3room);
            Check(gC2.State.Value == GuestState.ToRoom && r3room.HasClog, "a clogged room can be given out too (clog is just a problem)");
            yield return WaitFor(() => gC2.State.Value == GuestState.Living, 60f);
            yield return new WaitForSeconds(0.5f);
            Check(gC2.RoomProblems == clogProblems && clogProblems >= 2 && gC2.PayCut.Value == 100 && gC2.Face != null && gC2.Face.Caption == Guest.PayCutText(100),
                $"{clogProblems} problems (stain, clog, lamp): pay cut {gC2.PayCut.Value}%, «{(gC2.Face != null ? gC2.Face.Caption : "-")}»");
            yield return WaitFor(() => gC2.State.Value == GuestState.WaitCheckOut, 60f);
            Check(r3room.HasStain && r3room.HasClog && r3room.LampBroken && r3room.State == RoomState.NeedsService, "after check-out the stain, the clog and the lamp stay (+ new supplies needed)");
            hands.ServerClear();
            m0 = gs.Money.Value;
            gC2.ServerInteract(hands, hands.Held.Value);
            Check(gs.Money.Value == m0 && hands.Held.Value == new HeldItem(ItemType.Key, 3), "2+ problems: guest paid nothing, the key came back");
            hands.ServerClear();
            board.ServerReturn(3);
            yield return WaitFor(() => Gone(gC2), 60f);
            r2room.ServerSetReady(); r3room.ServerSetReady();

            // ================= D: гость ходит в туалет, визит пачкает его по шансу (в тесте 100 %)
            cfg.toiletDirtyChance = 1f;
            cfg.toiletVisitClogChance = 0f;
            cfg.toiletVisitMin = cfg.toiletVisitMax = 10f;      // /10 = раз в секунду
            wc.ServerSetDirty(false, false, false);
            Guest.VisitsOverride = true;
            int visits0 = Guest.StatToiletVisits;
            var r4 = Room.Get(4);
            var gD = spawner.ServerSpawn(false, false);
            yield return CheckIn(gD, r4);
            yield return WaitFor(() => gD.State.Value == GuestState.Living, 60f);
            gD.ServerExtendStay(600f);                          // /10 = +60 с: хватит на визиты
            yield return WaitFor(() => gD.State.Value == GuestState.ToToilet, 20f);
            Check(gD.State.Value == GuestState.ToToilet, "living guest goes to the toilet");
            yield return WaitFor(() => gD.State.Value == GuestState.AtToilet, 30f);
            Check(gD.State.Value == GuestState.AtToilet && !gD.Unhappy.Value && Vector3.Distance(gD.transform.position, wc.VisitSpot(0)) < 3f, "guest stands inside the toilet");
            Check(!wc.Dirty, "the toilet stays clean while the guest is there (mess comes after the visit)");
            yield return WaitFor(() => gD.State.Value == GuestState.Living, 15f);
            yield return null;
            Check(gD.State.Value == GuestState.Living && Guest.StatToiletVisits > visits0 && wc.HasStain && wc.Dirty, "after the visit the toilet is dirty (chance 100% in the test) and the guest walks back");

            // ================= E: туалет засорён — гость постоит у двери и уйдёт недовольным, без денежного штрафа
            wc.ServerSetDirty(true, true, false);
            int blocked0 = Guest.StatToiletBlocked;
            m0 = gs.Money.Value;
            yield return WaitFor(() => gD.State.Value == GuestState.AtToilet && gD.Unhappy.Value, 30f);
            Check(gD.State.Value == GuestState.AtToilet && gD.Unhappy.Value && Guest.StatToiletBlocked > blocked0, "clogged toilet: the guest stands at the door, unhappy");
            Check(Vector3.Distance(gD.transform.position, wc.DoorPoint) < 2.5f && wc.transform.InverseTransformPoint(gD.transform.position).z > 2.0f, "he stays outside, by the door (not inside)");
            bool stainKept = wc.HasStain && wc.HasClog;
            yield return WaitFor(() => gD.State.Value == GuestState.Living, 15f);
            Check(gD.State.Value == GuestState.Living && !gD.Unhappy.Value && gs.Money.Value == m0 && stainKept && wc.HasClog, "the guest walks away from the clogged toilet back to the room: no fine, the toilet stays clogged");
            Guest.VisitsOverride = false;
            wc.ServerSetDirty(false, false, false);
            gD.ServerDespawn();
            yield return null;
            Room.Get(4).ServerSetReady();
            board.ServerReturn(4);

            // ================= F: «Заново» убирает пятна
            Fill(4);
            Check(DirtStain.Count == 4, "4 stains on the floor");
            gs.GameOver();
            gs.ServerRestart();
            yield return null; yield return null;
            Check(gs.Phase.Value == GamePhase.Playing && DirtStain.Count == 0, "restart: the floor is clean again");

            Debug.Log("[DIRT] selftest " + (fails == 0 ? "OK (all done)" : "FAIL count=" + fails));
        }
    }
}
