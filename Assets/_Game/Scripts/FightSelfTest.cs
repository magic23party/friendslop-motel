using System.Collections;
using System.Collections.Generic;
using Unity.Netcode.Components;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Самотест C5 (флаг -fighttest, только на хосте; таймеры гостей ускорены в 10 раз, гости бегают втрое быстрее).
    /// 1) неплательщика бьют 3 раза → Down, деньги на полу, поднимаются в кассу, гость встаёт и уходит;
    /// 2) удар лучом по гостю → гость в ответ 5 раз бьёт игрока → игрок без сознания, кувалда выпала, гость сбегает, игрок встаёт;
    /// 3) удар игрок→игрок (нужен второй игрок, иначе пункт пропускается) → отключка 1 с, предмет выпал;
    /// 4) драка двух гостей: штраф −5 $, строка в списке задач, продолжается, пока не уложены оба, потом кончается.
    /// Итог — строка "[FIGHT] selftest OK/FAIL".
    /// </summary>
    public class FightSelfTest : MonoBehaviour
    {
        GameState gs;
        GameConfig cfg;
        KeyBoard board;
        PlayerHands hands;
        PlayerCombat me, other;
        GuestSpawner spawner;
        int fails;
        int lastRoom;
        static readonly HeldItem Hammer = new HeldItem(ItemType.Sledgehammer);

        void Start() { StartCoroutine(Run()); }

        void Check(bool ok, string what)
        {
            if (!ok) fails++;
            Debug.Log($"[FIGHT] selftest {(ok ? "OK  " : "FAIL")} {what}");
        }

        IEnumerator WaitFor(System.Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
        }

        static bool Gone(Guest g) => g == null || !g.IsSpawned;

        WorldItem FindFloor(ItemType type, int arg)
        {
            foreach (var w in WorldItem.All)
                if (w != null && w.IsSpawned && w.Item.Value.type == type && (arg < 0 || w.Item.Value.arg == arg)) return w;
            return null;
        }

        void Teleport(PlayerCombat p, Vector3 pos)
        {
            var cc = p.GetComponent<CharacterController>();
            var nt = p.GetComponent<NetworkTransform>();
            bool was = cc.enabled;
            cc.enabled = false;
            p.transform.position = pos;
            if (nt != null && nt.CanCommitToTransform) nt.Teleport(pos, p.transform.rotation, p.transform.localScale);
            cc.enabled = was;
            Physics.SyncTransforms();
        }

        /// <summary>Гость уже у стойки (WaitForm): бланк → ключ готового номера. Результат — lastRoom (0 при неудаче).</summary>
        IEnumerator CheckIn(Guest g, string tag)
        {
            lastRoom = 0;
            hands.ServerClear();
            hands.ServerTake(new HeldItem(ItemType.Form));
            g.ServerInteract(hands, hands.Held.Value);
            yield return WaitFor(() => g.State.Value == GuestState.WaitKey, 10f);
            var r = Room.PickReady(board);
            Check(g.State.Value == GuestState.WaitKey && r != null, tag + " signed, a ready room with key on board exists");
            if (r == null || g.State.Value != GuestState.WaitKey) yield break;
            board.ServerTake(r.index);
            hands.ServerTake(new HeldItem(ItemType.Key, r.index));
            g.ServerInteract(hands, hands.Held.Value);
            Check(hands.Held.Value.IsNone && g.State.Value == GuestState.ToRoom, tag + " got key " + r.index);
            lastRoom = r.index;
        }

        IEnumerator Run()
        {
            gs = GameState.Instance;
            cfg = gs.config;
            board = MotelMap.Instance.keyBoard;
            spawner = GuestSpawner.Instance;
            GuestSpawner.Paused = true;
            BrawlDirector.Paused = true;
            yield return WaitFor(() => PlayerHands.All.Count >= 1 && Room.Get(1) != null && Room.Get(1).IsSpawned, 90f);
            yield return WaitFor(() => PlayerHands.All.Count >= 2, 25f);       // клиент из смоука (если есть)
            foreach (var h in PlayerHands.All) if (h.IsOwner) hands = h;
            if (hands == null) hands = PlayerHands.All[0];
            me = hands.GetComponent<PlayerCombat>();
            foreach (var h in PlayerHands.All) if (h != hands) other = h.GetComponent<PlayerCombat>();
            var myCtrl = hands.GetComponent<PlayerController>();
            yield return WaitFor(() => myCtrl.Placed, 30f);
            if (gs.Phase.Value == GamePhase.Lobby) { yield return new WaitForSeconds(2f); gs.StartShift(); }
            yield return WaitFor(() => gs.Phase.Value == GamePhase.Playing, 30f);
            yield return new WaitForSeconds(3f);          // клиент встаёт на точку спавна
            hands.ServerClear();
            var home = me.transform.position;
            Debug.Log("[FIGHT] selftest start, players=" + PlayerHands.All.Count);

            // ================= 1: неплательщика кладут тремя ударами, деньги на полу
            var gA = spawner.ServerSpawn(true);
            yield return WaitFor(() => gA.State.Value == GuestState.WaitForm, 40f);
            yield return CheckIn(gA, "A");
            yield return WaitFor(() => gA.Marked.Value, 90f);
            Check(gA.Marked.Value && gA.State.Value == GuestState.Leaving && gA.CarriedMoney == cfg.guestPays,
                "A (non-payer) refused to pay and keeps " + gA.CarriedMoney + " $");
            hands.ServerClear();
            hands.ServerTake(Hammer);
            int m0 = gs.Money.Value;
            gA.ServerHit(hands);
            Check(gA.Hp.Value == cfg.npcHp - 1 && gA.State.Value == GuestState.Leaving, "A: 1st hit -> hp " + gA.Hp.Value + ", still on his feet");
            gA.ServerHit(hands); gA.ServerHit(hands);
            yield return null;
            Check(gA.Hp.Value == 0 && gA.State.Value == GuestState.Down, "A: 3rd hit -> Down");
            var cash = FindFloor(ItemType.Money, -1);
            Check(cash != null && cash.Item.Value.arg == cfg.guestPays, "A dropped his " + cfg.guestPays + " $ on the floor");
            gA.ServerHit(hands);
            Check(gA.Hp.Value == 0 && gA.State.Value == GuestState.Down, "a lying guest can't be hit again");
            if (cash != null) cash.ServerInteract(hands, hands.Held.Value);
            yield return null; yield return null;
            Check(gs.Money.Value == m0 + cfg.guestPays, "floor money picked up -> till +" + cfg.guestPays);
            yield return WaitFor(() => gA.State.Value == GuestState.Leaving, cfg.npcDownTime / Guest.TimeScale + 3f);
            Check(gA.State.Value == GuestState.Leaving, "A got up after npcDownTime and walks out");
            yield return WaitFor(() => Gone(gA), 40f);
            Check(Gone(gA), "A left the motel");

            // ================= 2: удар лучом по гостю; гость в ответ кладёт игрока
            hands.ServerClear();
            hands.ServerTake(Hammer);
            var gB = spawner.ServerSpawn(false);
            yield return WaitFor(() => gB.State.Value == GuestState.WaitForm, 40f);
            yield return null;
            var gp = gB.transform.position;
            Teleport(me, new Vector3(gp.x, gp.y + 0.05f, gp.z - 1.5f));
            yield return new WaitForSeconds(0.2f);
            Physics.SyncTransforms();
            var eye = hands.Eye;
            var aim = gB.transform.position + Vector3.up * 0.9f;
            var dir = (aim - eye).normalized;
            bool swung = me.ServerStrike(eye, dir);
            Check(swung && gB.Hp.Value == cfg.npcHp - 1, "ray strike from the camera hit guest B: hp -> " + gB.Hp.Value);
            Check(!me.ServerStrike(eye, dir) && gB.Hp.Value == cfg.npcHp - 1, "second strike inside hitCooldown is refused");
            yield return WaitFor(() => me.IsDown.Value, 8f);
            Check(me.IsDown.Value && hands.Downed, "B angrily punched the host " + cfg.hitsToKnockPlayer + " times -> host is unconscious");
            Check(hands.Held.Value.IsNone && FindFloor(ItemType.Sledgehammer, -1) != null, "hammer fell out of the unconscious host's hands");
            Check(gB.State.Value == GuestState.Leaving, "B flees without paying");
            hands.ServerTake(Hammer);
            Check(!me.ServerStrike(eye, dir), "unconscious player can't strike");
            hands.ServerClear();
            yield return WaitFor(() => !me.IsDown.Value, cfg.playerDownTime + 4f);
            Check(!me.IsDown.Value, "host regained consciousness after playerDownTime");
            gB.ServerDespawn();
            var hw = FindFloor(ItemType.Sledgehammer, -1);
            if (hw != null) hw.ServerInteract(hands, hands.Held.Value);
            Check(hands.Held.Value == Hammer, "hammer picked up again");
            Teleport(me, home);
            yield return new WaitForSeconds(0.3f);

            // ================= 3: игрок бьёт игрока
            if (other == null) Debug.Log("[FIGHT] selftest SKIP player->player (only one player connected)");
            else
            {
                var oh = other.GetComponent<PlayerHands>();
                oh.ServerClear();
                oh.ServerTake(new HeldItem(ItemType.Towel));
                Physics.SyncTransforms();
                eye = hands.Eye;
                var tgt = other.transform.position + Vector3.up * 1.0f;
                float dist = Vector3.Distance(me.transform.position, other.transform.position);
                Debug.Log($"[FIGHT] selftest players are {dist:0.0} m apart");
                float t0 = Time.time;
                bool ok = me.ServerStrike(eye, (tgt - eye).normalized);
                Check(ok && other.IsDown.Value, "player->player strike knocks the colleague out");
                Check(oh.Held.Value.IsNone && FindFloor(ItemType.Towel, -1) != null, "colleague's item fell out of his hands");
                oh.ServerTake(Hammer);
                Check(!other.ServerStrike(oh.Eye, Vector3.forward), "knocked-out colleague can't strike back");
                oh.ServerClear();
                yield return new WaitForSeconds(0.5f);
                Check(other.IsDown.Value, "colleague is still down after 0.5 s");
                yield return WaitFor(() => !other.IsDown.Value, 3f);
                float dt = Time.time - t0;
                Check(!other.IsDown.Value && dt >= cfg.colleagueHitDownTime * 0.9f && dt < cfg.colleagueHitDownTime + 1f,
                    $"colleague got up after {dt:0.00}s (colleagueHitDownTime={cfg.colleagueHitDownTime})");
            }

            // ================= 4: драка двух гостей
            hands.ServerClear();
            hands.ServerTake(Hammer);
            var gC = spawner.ServerSpawn(false);
            var gD = spawner.ServerSpawn(false);
            yield return WaitFor(() => gC.State.Value == GuestState.WaitForm, 40f);
            hands.ServerClear();
            yield return CheckIn(gC, "C");
            int rC = lastRoom;
            yield return WaitFor(() => gD.State.Value == GuestState.WaitForm, 20f);
            yield return CheckIn(gD, "D");
            int rD = lastRoom;
            yield return WaitFor(() => gC.State.Value == GuestState.Living && gD.State.Value == GuestState.Living, 40f);
            bool bothLiving = gC.State.Value == GuestState.Living && gD.State.Value == GuestState.Living;
            Check(bothLiving, "C and D both live in their rooms");
            hands.ServerClear();
            hands.ServerTake(Hammer);
            bool started = BrawlDirector.Instance != null && BrawlDirector.Instance.ServerTryStartBrawl();
            Check(started && gC.State.Value == GuestState.Brawl && gD.State.Value == GuestState.Brawl, "brawl started: both guests are in GuestState.Brawl");
            var tasks = new List<TaskLine>();
            TaskBoard.Collect(tasks);
            Check(tasks.Exists(t => t.text.Contains("Драка") && t.urgent), "task list shows the urgent 'brawl in the corridor'");
            m0 = gs.Money.Value;
            yield return new WaitForSeconds(DrainSec(cfg) * 2.6f);
            int m1 = gs.Money.Value;
            Check(m1 <= m0 - Mathf.RoundToInt(cfg.brawlDrainAmount), $"brawl fine: till {m0} -> {m1}");

            gC.ServerHit(hands); gC.ServerHit(hands); gC.ServerHit(hands);
            yield return null;
            Check(gC.State.Value == GuestState.Down && Guest.BrawlCount() == 1, "C laid down, brawl goes on with D");
            Check(Room.Get(rC).State == RoomState.NeedsService && FindFloor(ItemType.Key, rC) != null, "C's room needs service, his key lies on the floor");
            m0 = gs.Money.Value;
            yield return new WaitForSeconds(DrainSec(cfg) * 2.6f);
            Check(gs.Money.Value < m0, $"the fine continues while D is still fighting: {m0} -> {gs.Money.Value}");

            gD.ServerHit(hands); gD.ServerHit(hands); gD.ServerHit(hands);
            yield return null;
            Check(gD.State.Value == GuestState.Down && Guest.BrawlCount() == 0, "D laid down, brawl is over");
            tasks.Clear(); TaskBoard.Collect(tasks);
            Check(!tasks.Exists(t => t.text.Contains("Драка")), "brawl task disappeared from the list");
            yield return new WaitForSeconds(0.2f);
            m0 = gs.Money.Value;
            yield return new WaitForSeconds(DrainSec(cfg) * 3f);
            Check(gs.Money.Value == m0, "no more fines after the brawl: till " + m0);
            yield return WaitFor(() => Gone(gC) || gC.State.Value == GuestState.Leaving, cfg.npcDownTime / Guest.TimeScale + 3f);
            yield return WaitFor(() => Gone(gD) || gD.State.Value == GuestState.Leaving, 3f);
            Check((Gone(gC) || gC.State.Value == GuestState.Leaving) && (Gone(gD) || gD.State.Value == GuestState.Leaving) && Room.Get(rD).State == RoomState.NeedsService,
                "beaten brawlers got up and leave; D's room needs service");

            Guest.ServerDespawnAll();
            yield return null; yield return null;
            Check(Guest.All.Count == 0, "all guests removed");
            Debug.Log("[FIGHT] selftest " + (fails == 0 ? "OK (all done)" : "FAIL count=" + fails));
        }

        /// <summary>Штрафной интервал драки в секундах реального времени (с учётом ускорения гостей).</summary>
        static float DrainSec(GameConfig c) => c.brawlDrainInterval / Guest.TimeScale;
    }
}
