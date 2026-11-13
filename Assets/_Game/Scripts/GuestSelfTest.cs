using System.Collections;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Самотест C4 (флаг -guesttest, только на хосте; таймеры гостей ускорены в 10 раз, гости бегают втрое быстрее).
    /// Сервер проходит те же серверные методы, что и игрок: заселение → проживание → выезд → обслуживание номера;
    /// неплательщик; «никто не обслужил выезд» (ключ и деньги на полу); тайм-аут заселения; очистка при перезапуске.
    /// Итог — строка "[GUEST] selftest OK/FAIL".
    /// </summary>
    public class GuestSelfTest : MonoBehaviour
    {
        GameState gs;
        GameConfig cfg;
        KeyBoard board;
        PlayerHands hands;
        GuestSpawner spawner;
        int fails;
        int lastRoom;

        void Start() { StartCoroutine(Run()); }

        void Check(bool ok, string what)
        {
            if (!ok) fails++;
            Debug.Log($"[GUEST] selftest {(ok ? "OK  " : "FAIL")} {what}");
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

        void HangKey(int room)
        {
            // ключ в руках → на доску (тот же путь, что у игрока: цель KeySlot)
            board.slots[room - 1].ServerInteract(hands, hands.Held.Value);
        }

        void ServiceRoom(Room r)
        {
            for (int i = 0; i < Room.SupplyTypes.Length; i++)
                if (r.Needs(i)) { hands.ServerTake(new HeldItem(Room.SupplyTypes[i])); if (r.ServerDeliver(hands.Held.Value.type)) hands.ServerClear(); }
            hands.ServerClear();
            if (r.HasStain) r.ServerMop(cfg.mopTime + 0.1f);
        }

        /// <summary>Гость уже у стойки (WaitForm): бланк → ключ готового номера. Результат — lastRoom (0 при неудаче).</summary>
        IEnumerator CheckIn(Guest g, string tag)
        {
            lastRoom = 0;
            hands.ServerClear();
            hands.ServerTake(new HeldItem(ItemType.Form));
            g.ServerInteract(hands, hands.Held.Value);
            Check(hands.Held.Value.IsNone && g.State.Value == GuestState.Signing, tag + " form accepted -> Signing");
            yield return WaitFor(() => g.State.Value == GuestState.WaitKey, 10f);
            Check(g.State.Value == GuestState.WaitKey, tag + " signed -> waits for key");
            var r = Room.PickReady(board);
            Check(r != null, tag + " a ready room with key on board exists");
            if (r == null) yield break;
            board.ServerTake(r.index);
            hands.ServerTake(new HeldItem(ItemType.Key, r.index));
            g.ServerInteract(hands, hands.Held.Value);
            Check(hands.Held.Value.IsNone && r.State == RoomState.Occupied && g.State.Value == GuestState.ToRoom && g.RoomNo.Value == r.index,
                tag + " key accepted -> room " + r.index + " Occupied, guest heads there");
            lastRoom = r.index;
        }

        /// <summary>P4: гость у стойки досягаем лучом прицела и из-за стойки (сотрудник), и спереди; луч не упирается в столешницу.</summary>
        void CheckDeskReach(Guest g)
        {
            var map = MotelMap.Instance;
            if (map == null || map.receptionDesk == null) return;
            var col = map.receptionDesk.GetComponent<Collider>();
            float zBack = map.receptionDesk.position.z + map.receptionDesk.localScale.z * 0.5f + 0.4f + 0.05f;    // вплотную к стойке с северной стороны
            float zFront = map.receptionDesk.position.z - map.receptionDesk.localScale.z * 0.5f - 0.5f;
            Physics.SyncTransforms();
            foreach (float aimY in new[] { 1.5f, 0.9f })       // грудь и ноги: низкий прицел идёт сквозь столешницу
            {
                var target = g.transform.position + Vector3.up * aimY;
                var eyeBack = new Vector3(map.queuePoints[0].position.x, 1.62f, zBack);
                var hitBack = PlayerHands.AimFrom(eyeBack, (target - eyeBack).normalized, cfg.interactDistance, null);
                Check(col != null && ReferenceEquals(hitBack, g), $"desk reach: guest in queue is aimable from behind the desk (aim y={aimY}, dist {(target - eyeBack).magnitude:0.0} of {cfg.interactDistance})");
                var eyeFront = new Vector3(map.queuePoints[0].position.x + 0.9f, 1.62f, zFront);
                var hitFront = PlayerHands.AimFrom(eyeFront, (target - eyeFront).normalized, cfg.interactDistance, null);
                Check(ReferenceEquals(hitFront, g), $"desk reach: guest in queue is aimable from the customer side (aim y={aimY})");
            }
        }

        IEnumerator Run()
        {
            gs = GameState.Instance;
            cfg = gs.config;
            board = MotelMap.Instance.keyBoard;
            spawner = GuestSpawner.Instance;
            GuestSpawner.Paused = true;
            yield return WaitFor(() => PlayerHands.All.Count >= 1 && gs.Phase.Value == GamePhase.Playing && Room.Get(1) != null && Room.Get(1).IsSpawned, 90f);
            yield return new WaitForSeconds(14f);          // даём отработать -handstest (ключи 3.. на доске)
            foreach (var h in PlayerHands.All) if (h.IsOwner) hands = h;
            if (hands == null) hands = PlayerHands.All[0];
            hands.ServerClear();
            Debug.Log("[GUEST] selftest start");

            Check(!cfg.roomsStartDirty && Room.PickReady(board) != null, "rooms start Ready (roomsStartDirty=false)");
            Check(Guest.All.Count == 0, "no guests while spawner paused");

            // ================= A: платящий гость, полный цикл
            var gA = spawner.ServerSpawn(false);
            Check(gA != null, "A spawned at entrance");
            yield return WaitFor(() => gA.State.Value == GuestState.WaitForm, 40f);
            Check(gA.State.Value == GuestState.WaitForm, "A walked to the desk, waits for form");
            CheckDeskReach(gA);
            hands.ServerTake(new HeldItem(ItemType.Towel));
            gA.ServerInteract(hands, hands.Held.Value);
            Check(gA.State.Value == GuestState.WaitForm && hands.Held.Value.type == ItemType.Towel, "A rejects wrong item");
            hands.ServerClear();
            yield return CheckIn(gA, "A");
            int rA = lastRoom;
            yield return WaitFor(() => gA.State.Value == GuestState.Living, 60f);
            Check(gA.State.Value == GuestState.Living, "A reached the room and lives there");
            yield return WaitFor(() => gA.State.Value == GuestState.WaitCheckOut, 60f);
            var roomA = Room.Get(rA);
            Check(gA.State.Value == GuestState.WaitCheckOut && roomA.State == RoomState.NeedsService, "A at desk for check-out, room -> NeedsService: " + roomA.NeedsText());
            int m0 = gs.Money.Value;
            hands.ServerTake(new HeldItem(ItemType.Towel));
            gA.ServerInteract(hands, hands.Held.Value);
            Check(gA.State.Value == GuestState.WaitCheckOut, "A check-out refused with full hands");
            hands.ServerClear();
            gA.ServerInteract(hands, hands.Held.Value);
            Check(hands.Held.Value == new HeldItem(ItemType.Key, rA) && gs.Money.Value == m0 + cfg.guestPays && gA.State.Value == GuestState.Leaving,
                "A checked out: key in hands, +" + cfg.guestPays + " $ in till");
            HangKey(rA);
            Check(board.Has(rA) && hands.Held.Value.IsNone, "key " + rA + " back on the board");
            yield return WaitFor(() => Gone(gA), 60f);
            Check(Gone(gA), "A left through the exit");

            // ================= B: неплательщик + отказ выдать ключ неготового номера + обслуживание номера
            var gB = spawner.ServerSpawn(true);
            yield return WaitFor(() => gB.State.Value == GuestState.WaitForm, 40f);
            Check(gB.State.Value == GuestState.WaitForm, "B waits for form");
            hands.ServerClear();
            hands.ServerTake(new HeldItem(ItemType.Form));
            gB.ServerInteract(hands, hands.Held.Value);
            yield return WaitFor(() => gB.State.Value == GuestState.WaitKey, 10f);
            // ключ неготового номера (rA ещё NeedsService)
            Check(roomA.State == RoomState.NeedsService, "room " + rA + " still needs service");
            board.ServerTake(rA);
            hands.ServerTake(new HeldItem(ItemType.Key, rA));
            gB.ServerInteract(hands, hands.Held.Value);
            Check(gB.State.Value == GuestState.WaitKey && hands.Held.Value == new HeldItem(ItemType.Key, rA) && roomA.State == RoomState.NeedsService,
                "B rejects key of a dirty room");
            HangKey(rA);
            ServiceRoom(roomA);
            Check(roomA.IsReady && Room.PickReady(board) != null, "room " + rA + " serviced -> Ready (supplies + mop)");
            var rB = Room.PickReady(board);
            board.ServerTake(rB.index);
            hands.ServerTake(new HeldItem(ItemType.Key, rB.index));
            gB.ServerInteract(hands, hands.Held.Value);
            Check(hands.Held.Value.IsNone && gB.State.Value == GuestState.ToRoom, "B got key " + rB.index);
            yield return WaitFor(() => gB.State.Value == GuestState.WaitCheckOut || gB.Marked.Value, 90f);
            yield return WaitFor(() => gB.Marked.Value, 10f);
            m0 = gs.Money.Value;
            Check(gB.Marked.Value && gB.State.Value == GuestState.Leaving && gs.Money.Value == m0 && gB.CarriedMoney == cfg.guestPays,
                "B (non-payer) refused to pay: marked '!', keeps " + gB.CarriedMoney + " $");
            var keyB = FindFloor(ItemType.Key, rB.index);
            Check(keyB != null, "B dropped key " + rB.index + " on the floor");
            yield return null;
            if (keyB != null) keyB.ServerInteract(hands, hands.Held.Value);
            Check(hands.Held.Value == new HeldItem(ItemType.Key, rB.index), "key picked up from floor");
            HangKey(rB.index);
            Check(board.Has(rB.index), "key " + rB.index + " back on the board");
            yield return WaitFor(() => Gone(gB), 60f);
            Check(Gone(gB), "B left");
            ServiceRoom(rB);
            Check(rB.IsReady, "room " + rB.index + " serviced");

            // ================= C: никто не обслужил выезд — ключ и деньги на полу
            var gC = spawner.ServerSpawn(false);
            yield return WaitFor(() => gC.State.Value == GuestState.WaitForm, 40f);
            yield return CheckIn(gC, "C");
            int rC = lastRoom;
            yield return WaitFor(() => gC.State.Value == GuestState.WaitCheckOut, 90f);
            Check(gC.State.Value == GuestState.WaitCheckOut, "C at desk for check-out");
            m0 = gs.Money.Value;
            yield return WaitFor(() => gC.State.Value == GuestState.Leaving, 30f);
            yield return null;
            var keyC = FindFloor(ItemType.Key, rC);
            var cashC = FindFloor(ItemType.Money, -1);
            Check(gC.State.Value == GuestState.Leaving && !gC.Marked.Value && keyC != null && cashC != null && cashC.Item.Value.arg == cfg.guestPays && gs.Money.Value == m0,
                "nobody served C: key " + rC + " and " + cfg.guestPays + " $ lie on the floor, till unchanged");
            hands.ServerClear();
            if (keyC != null) keyC.ServerInteract(hands, hands.Held.Value);
            if (cashC != null) cashC.ServerInteract(hands, hands.Held.Value);
            yield return null; yield return null;
            Check(hands.Held.Value == new HeldItem(ItemType.Key, rC) && gs.Money.Value == m0 + cfg.guestPays, "picked up key and floor money -> till +" + cfg.guestPays);
            HangKey(rC);
            yield return WaitFor(() => Gone(gC), 60f);
            ServiceRoom(Room.Get(rC));
            Check(Gone(gC) && Room.Get(rC).IsReady && FindFloor(ItemType.Key, -1) == null, "C left, room serviced, floor clean");

            // ================= D: гость не дождался заселения
            var gD = spawner.ServerSpawn(false);
            yield return WaitFor(() => gD.State.Value == GuestState.WaitForm, 40f);
            m0 = gs.Money.Value;
            yield return WaitFor(() => gD.State.Value == GuestState.Leaving, 15f);
            Check(gD.State.Value == GuestState.Leaving && gs.Money.Value == m0, "D waited too long at check-in and left, no money");
            yield return WaitFor(() => Gone(gD), 60f);
            Check(Gone(gD), "D despawned at the exit");

            // ================= E: спавнер сам создаёт гостей; перезапуск смены убирает всех
            GuestSpawner.Paused = false;
            yield return WaitFor(() => Guest.All.Count > 0, 40f);
            Check(Guest.All.Count > 0, "spawner creates guests by itself");
            GuestSpawner.Paused = true;
            yield return new WaitForSeconds(2f);
            Guest.ServerDespawnAll();                     // тот же вызов, что по GameState.ServerRestarted
            yield return null; yield return null;
            Check(Guest.All.Count == 0, "all guests removed (restart cleanup)");

            Debug.Log("[GUEST] selftest " + (fails == 0 ? "OK (all done)" : "FAIL count=" + fails));
        }
    }
}
