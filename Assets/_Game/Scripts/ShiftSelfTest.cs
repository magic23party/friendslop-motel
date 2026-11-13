using System.Collections;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Самотест C3 (флаг -roomtest, только на хосте; с -fasttax проходит и налоговую часть):
    /// номер 1 приводится в Ready теми же серверными методами, что и игрок (расходники, швабра, цели RoomSpot),
    /// деньги идут в кассу, счёт оплачивается на терминале, неоплата даёт Game Over, «Заново» сбрасывает смену.
    /// С V1 «-fasttax» = день 20 с, ночь 20 с: счёт выставляется с наступлением ночи (день 1 — 100 $, день 2 — 150 $).
    /// Результат — строки "[ROOM] selftest OK/FAIL ..." и "[TAX] selftest OK/FAIL ...".
    /// </summary>
    public class ShiftSelfTest : MonoBehaviour
    {
        GameState gs;
        int fails;

        void Start() { StartCoroutine(Run()); }

        void Check(string tag, bool ok, string what)
        {
            if (!ok) fails++;
            Debug.Log($"[{tag}] selftest {(ok ? "OK  " : "FAIL")} {what}");
        }

        IEnumerator WaitFor(System.Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
        }

        PlayerHands FreeHands()
        {
            foreach (var h in PlayerHands.All) if (h.Held.Value.IsNone) return h;
            return null;
        }

        IEnumerator Run()
        {
            gs = GameState.Instance;
            yield return WaitFor(() => PlayerHands.All.Count >= 1 && Room.Get(1) != null && Room.Get(1).IsSpawned, 60f);
            if (gs.Phase.Value == GamePhase.Lobby)
            {
                yield return new WaitForSeconds(3f);
                gs.StartShift();
            }
            yield return WaitFor(() => gs.Phase.Value == GamePhase.Playing, 60f);
            float t0 = Time.time;
            var cfg = gs.config;
            Debug.Log("[ROOM] selftest start");

            // ---- номер 1: состояния и сервер-методы
            var r1 = Room.Get(1);
            Check("ROOM", r1 != null && !r1.Locked, "room 1 exists and open");
            Check("ROOM", (r1.State == RoomState.NeedsService) == cfg.roomsStartDirty, "initial state matches roomsStartDirty=" + cfg.roomsStartDirty);

            r1.ServerSetNeedsService(7, true);
            yield return null; yield return null;
            Check("ROOM", r1.State == RoomState.NeedsService && r1.Missing == 7 && r1.HasStain, "needs: towel+soap+napkins+stain");
            Check("ROOM", r1.signStatus != null && r1.signStatus.text.Contains("Грязь") && r1.signStatus.text.Contains("Мыло"), "sign lists needs: " + (r1.signStatus != null ? r1.signStatus.text.Replace("\n", "/") : "null"));
            Check("ROOM", !r1.ServerDeliver(ItemType.Mop) && !r1.ServerDeliver(ItemType.Key), "reject non-supply items");

            r1.ServerMop(cfg.mopTime * 0.5f);
            Check("ROOM", r1.MopPct.Value >= 40 && r1.MopPct.Value <= 60, "mop half -> " + r1.MopPct.Value + "%");
            r1.ServerMopReset();
            Check("ROOM", r1.MopPct.Value == 0 && r1.HasStain, "mop reset");
            bool cleaned = r1.ServerMop(cfg.mopTime + 0.1f);
            Check("ROOM", cleaned && !r1.HasStain && r1.State == RoomState.NeedsService, "stain cleaned, supplies still missing");

            // ---- то же через цели (RoomSpot), как делает игрок: ждём, пока тест рук (-handstest) отработает
            yield return new WaitForSeconds(Mathf.Max(0f, 14f - (Time.time - t0)));
            PlayerHands hands = null;
            yield return WaitFor(() => (hands = FreeHands()) != null, 20f);
            Check("ROOM", hands != null, "have free hands");
            if (hands != null)
            {
                RoomSpot bed = null, towel = null, soap = null;
                foreach (var sp in r1.GetComponentsInChildren<RoomSpot>(true))
                {
                    if (sp.kind == SpotKind.Bed) bed = sp;
                    else if (sp.kind == SpotKind.Supply && sp.supplyIndex == 0) towel = sp;
                    else if (sp.kind == SpotKind.Supply && sp.supplyIndex == 1) soap = sp;
                }
                Check("ROOM", bed != null && towel != null && soap != null && r1.stainSpot != null, "room spots built");

                hands.ServerTake(new HeldItem(ItemType.Towel));
                towel.ServerInteract(hands, hands.Held.Value);
                Check("ROOM", hands.Held.Value.IsNone && !r1.Needs(0) && r1.Needs(1), "towel carried to towel spot");

                hands.ServerTake(new HeldItem(ItemType.Soap));
                towel.ServerInteract(hands, hands.Held.Value);          // не то место
                Check("ROOM", hands.Held.Value.type == ItemType.Soap && r1.Needs(1), "soap rejected at towel spot");
                soap.ServerInteract(hands, hands.Held.Value);
                Check("ROOM", hands.Held.Value.IsNone && !r1.Needs(1), "soap carried to soap spot");

                hands.ServerTake(new HeldItem(ItemType.Napkins));
                bed.ServerInteract(hands, hands.Held.Value);            // кровать принимает любое недостающее
                Check("ROOM", hands.Held.Value.IsNone && r1.State == RoomState.Ready && r1.Missing == 0 && !r1.HasStain, "napkins at bed -> Ready");
                yield return null; yield return null;
                Check("ROOM", r1.signStatus.text == "ГОТОВ", "sign says ГОТОВ");

                // ---- деньги: в кассу сразу, руки не занимают
                hands.ServerTake(new HeldItem(ItemType.Towel));         // полные руки
                int m0 = gs.Money.Value, e0 = gs.Earned.Value;
                bool took = hands.ServerTake(new HeldItem(ItemType.Money, 30));
                Check("TAX", took && gs.Money.Value == m0 + 30 && hands.Held.Value.type == ItemType.Towel, "money into till with full hands");
                var wi = WorldItem.Spawn(new HeldItem(ItemType.Money, 20), hands.transform.position + Vector3.up, Vector3.zero);
                yield return null; yield return null;
                bool spawned = wi != null;
                if (spawned) wi.ServerInteract(hands, hands.Held.Value);
                yield return null; yield return null;
                Check("TAX", spawned && (wi == null || !wi.IsSpawned) && gs.Money.Value == m0 + 50 && gs.Earned.Value == e0 + 50, "floor money picked up -> till " + gs.Money.Value);
                hands.ServerClear();

                r1.ServerSetOccupied();
                Check("ROOM", r1.State == RoomState.Occupied && !r1.IsReady, "occupied");
                r1.ServerSetNeedsService();
                Check("ROOM", r1.State != RoomState.Occupied, "random needs -> " + r1.State + " " + r1.NeedsText());
                r1.ServerSetReady();
                Check("ROOM", r1.IsReady && Room.PickReady() != null, "ready again, PickReady works");
            }
            Debug.Log("[ROOM] selftest " + (fails == 0 ? "OK (rooms done)" : "FAIL count=" + fails));

            // ---- налог (нужен -fasttax: день 20 с, ночь 20 с; счёт выставляется с наступлением ночи)
            if (!NetBootstrap.Arg("-fasttax")) { Debug.Log("[TAX] selftest skipped (no -fasttax)"); yield break; }
            yield return WaitFor(() => gs.TaxDue.Value, 40f);
            Check("TAX", gs.TaxDue.Value && gs.Night.Value && gs.Day.Value == 1 && gs.TaxAmount == cfg.taxBase, "invoice issued at nightfall, day 1 = " + gs.TaxAmount + " $");

            var term = MotelMap.Instance != null && MotelMap.Instance.taxTerminal != null ? MotelMap.Instance.taxTerminal.GetComponent<TaxTerminal>() : null;
            Check("TAX", term != null, "terminal present");
            if (term != null && hands != null)
            {
                int have = gs.Money.Value;
                gs.ServerAddMoney(-(have - 10));                         // в кассе 10 $ — не хватает
                term.ServerInteract(hands, hands.Held.Value);
                Check("TAX", gs.TaxDue.Value && gs.Money.Value == 10, "cannot pay without money");
                gs.ServerAddMoney(100);
                int before = gs.Money.Value, amount = gs.TaxAmount;
                term.ServerInteract(hands, hands.Held.Value);
                Check("TAX", !gs.TaxDue.Value && gs.Money.Value == before - amount, "paid at terminal, till=" + gs.Money.Value);
            }

            // утро: день 2, налог вырос; второй счёт не платим -> Game Over по дедлайну (конец ночи)
            yield return WaitFor(() => !gs.Night.Value && gs.Day.Value == 2, 40f);
            Check("TAX", !gs.Night.Value && gs.Day.Value == 2 && !gs.TaxDue.Value, "morning of day 2 after paid night");
            yield return WaitFor(() => gs.Night.Value && gs.TaxDue.Value, 40f);
            Check("TAX", gs.TaxDue.Value && gs.TaxAmount == cfg.taxBase + cfg.taxStep, "day 2 invoice grew to " + gs.TaxAmount + " $");
            yield return WaitFor(() => gs.Phase.Value == GamePhase.GameOver, 90f);
            Check("TAX", gs.Phase.Value == GamePhase.GameOver, "game over after unpaid deadline (shift " + gs.ShiftSeconds.Value.ToString("0") + "s, earned " + gs.Earned.Value + ")");

            gs.ServerRestart();
            yield return null;
            Check("TAX", gs.Phase.Value == GamePhase.Playing && gs.Money.Value == cfg.startMoney && !gs.TaxDue.Value && gs.Earned.Value == 0 && gs.Day.Value == 1 && !gs.Night.Value, "restart resets shift and day");
            Check("TAX", MotelMap.Instance.keyBoard.Has(1) && (Room.Get(1).State == RoomState.NeedsService) == cfg.roomsStartDirty, "restart resets keys and rooms");
            Debug.Log("[TAX] selftest " + (fails == 0 ? "OK (all done)" : "FAIL count=" + fails));
        }
    }
}
