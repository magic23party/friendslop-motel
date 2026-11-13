using System.Collections;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Самотест «кувалда бьёт» (флаг -hammertest, у хоста И у клиента): проверяет реальный путь ввода владельца.
    /// Владелец ставит себя в точку перед целью, наводит реальную камеру (pc.cam) и «зажимает ЛКМ» через PlayerCombat.TestFire —
    /// дальше тот же код, что у живой мыши (Update → HitRpc(позиция/направление камеры) → ServerStrike).
    /// Сервер (HammerServerTest) по очереди выдаёт кувалду каждому игроку и проверяет: гость потерял Hp / коллега оглушён.
    /// </summary>
    public class HammerSelfTest : MonoBehaviour
    {
        PlayerController pc;
        PlayerCombat combat;
        PlayerHands hands;
        static readonly bool Desk = NetBootstrap.Arg("-hammerdesk");      // стоим за стойкой ресепшена (по ту сторону от гостя)
        static readonly bool RealMouse = NetBootstrap.Arg("-hammermouse");
        ulong doneGuest = ulong.MaxValue;
        bool doneColleague;
        float holdSince = -1f, noGuestSince = -1f;

        IEnumerator Start()
        {
            pc = GetComponent<PlayerController>();
            combat = GetComponent<PlayerCombat>();
            hands = GetComponent<PlayerHands>();
            while (!pc.Placed) yield return null;
            while (true)
            {
                yield return null;
                if (Guest.All.Count > 0) noGuestSince = -1f; else if (noGuestSince < 0f) noGuestSince = Time.time;
                if (combat.IsDown.Value || hands.Held.Value.type != ItemType.Sledgehammer) { holdSince = -1f; continue; }
                if (holdSince < 0f) holdSince = Time.time;
                var gs = GameState.Instance;
                if (gs == null || gs.Phase.Value != GamePhase.Playing) continue;

                Guest g = null;
                foreach (var x in Guest.All)
                    if (x != null && x.IsSpawned && x.State.Value == GuestState.WaitForm && x.NetworkObjectId != doneGuest) { g = x; break; }
                if (g != null)
                {
                    doneGuest = g.NetworkObjectId;
                    var gg = g;
                    yield return Fire(() => { var gp = gg.transform.position; float sx = gp.x + 1.2f * combat.OwnerClientId;      // игроки не толкают друг друга в одной точке
                        return Desk ? new Vector3(sx, gp.y + 0.05f, MotelMap.Instance.receptionDesk.position.z + 0.6f + 0.5f) : new Vector3(sx, gp.y + 0.05f, gp.z - 1.5f); }, () => gg.transform.position + Vector3.up * 0.9f, "guest");
                    continue;
                }
                if (Guest.All.Count > 0 || doneColleague || Time.time - holdSince < 1f || noGuestSince < 0f || Time.time - noGuestSince < 1f) continue;      // кувалда в руках не меньше секунды (репликация после прошлого раунда)
                PlayerCombat other = null;
                foreach (var c in PlayerCombat.All) if (c != combat && !c.IsDown.Value) other = c;
                if (other == null) continue;
                doneColleague = true;
                var oc = other;
                yield return Fire(() =>
                {
                    var op = oc.transform.position;
                    foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
                    {
                        var s = op + d * 1.5f;
                        if (!Physics.Raycast(op + Vector3.up, d, 1.7f, ~0, QueryTriggerInteraction.Ignore)
                            && !Physics.CheckCapsule(s + Vector3.up * 0.5f, s + Vector3.up * 1.4f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) return s;
                    }
                    return op + Vector3.forward * 1.5f;
                }, () => oc.transform.position + Vector3.up * 1.0f, "colleague");
            }
        }

        void Pose(System.Func<Vector3> standFn, System.Func<Vector3> aimFn)
        {
            var stand = standFn();
            var eye = stand + Vector3.up * 1.62f;
            var d = aimFn() - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            pc.ShotPose(stand, yaw, pitch);
        }

        IEnumerator Fire(System.Func<Vector3> standFn, System.Func<Vector3> aimFn, string what)
        {
            Pose(standFn, aimFn);
            yield return new WaitForSeconds(0.6f);      // цель (интерполяция по сети) и позиция игрока доезжают до сервера
            Pose(standFn, aimFn);
            yield return new WaitForSeconds(0.6f);
            var t = pc.cam.transform;
            Debug.Log($"[HAMMER] t={Time.time} owner={combat.OwnerClientId} fires at {what}: cam={t.position} fwd={t.forward} held={hands.Held.Value.type} guestsSeen=" + string.Join(",", System.Linq.Enumerable.Select(Guest.All, x => x.transform.position.ToString())));
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (RealMouse && mouse != null)        // -hammermouse: настоящее событие нажатия ЛКМ через Input System (проверяет и фокус окна)
            {
                Debug.Log($"[HAMMER] real mouse press, isFocused={Application.isFocused}");
                UnityEngine.InputSystem.LowLevel.MouseState ms = default;
                ms.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, true);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, ms);
                yield return new WaitForSeconds(0.25f);
                ms.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, false);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, ms);
            }
            else
            {
                combat.TestFire = true;
                yield return new WaitForSeconds(0.25f);
                combat.TestFire = false;
            }
        }
    }

    /// <summary>Сервер для -hammertest: выдаёт кувалду по очереди, проверяет эффект удара владельца (гость → Hp, игрок → IsDown).</summary>
    public class HammerServerTest : MonoBehaviour
    {
        int fails;

        void Start() { StartCoroutine(Run()); }

        void Check(bool ok, string what)
        {
            if (!ok) fails++;
            Debug.Log($"[HAMMER] selftest {(ok ? "OK  " : "FAIL")} {what}");
        }

        IEnumerator WaitFor(System.Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
        }

        IEnumerator Run()
        {
            var gs = GameState.Instance;
            var spawner = GuestSpawner.Instance;
            GuestSpawner.Paused = true;
            BrawlDirector.Paused = true;
            yield return WaitFor(() => PlayerHands.All.Count >= 1, 90f);
            yield return WaitFor(() => PlayerHands.All.Count >= 2, 25f);
            yield return WaitFor(() => { foreach (var h in PlayerHands.All) if (!h.GetComponent<PlayerController>().Placed) return false; return true; }, 30f);
            if (gs.Phase.Value == GamePhase.Lobby) { yield return new WaitForSeconds(2f); gs.StartShift(); }
            yield return WaitFor(() => gs.Phase.Value == GamePhase.Playing, 30f);
            yield return new WaitForSeconds(3f);
            var hammer = new HeldItem(ItemType.Sledgehammer);
            var players = new System.Collections.Generic.List<PlayerHands>(PlayerHands.All);
            Debug.Log("[HAMMER] selftest start, players=" + players.Count);

            // A: каждый владелец бьёт гостя
            foreach (var p in players)
            {
                foreach (var h in players) h.ServerClear();
                var g = spawner.ServerSpawn(false);
                yield return WaitFor(() => g.State.Value == GuestState.WaitForm, 40f);
                g.Until.Value += 120.0;          // не уходит, пока владелец целится (в быстром режиме терпение 3 с)
                int hp0 = g.Hp.Value;
                p.ServerTake(hammer);
                yield return WaitFor(() => g.Hp.Value < hp0, 12f);
                Check(g.Hp.Value < hp0, $"owner {p.OwnerClientId} (host={p.IsServer && p.IsOwner}) hit the guest through the real input path: hp {hp0} -> {g.Hp.Value}");
                p.ServerClear();
                g.ServerDespawn();
                yield return WaitFor(() => Guest.All.Count == 0, 5f);
                yield return new WaitForSeconds(1.5f);
                var pc = p.GetComponent<PlayerCombat>();
                yield return WaitFor(() => !pc.IsDown.Value, 10f);
            }

            // B: каждый владелец бьёт коллегу
            if (players.Count >= 2)
            {
                foreach (var p in players)
                {
                    foreach (var h in players) h.ServerClear();
                    PlayerCombat other = null;
                    foreach (var h in players) if (h != p) other = h.GetComponent<PlayerCombat>();
                    yield return WaitFor(() => !other.IsDown.Value, 5f);
                    p.ServerTake(hammer);
                    Debug.Log($"[HAMMER] t={Time.time} B: hammer to {p.OwnerClientId}, colleague down={other.IsDown.Value}");
                    yield return WaitFor(() => other.IsDown.Value, 12f);
                    Debug.Log($"[HAMMER] t={Time.time} B: wait over, colleague down={other.IsDown.Value}");
                    Check(other.IsDown.Value, $"owner {p.OwnerClientId} knocked the colleague {other.OwnerClientId} out through the real input path");
                    p.ServerClear();
                    yield return WaitFor(() => !other.IsDown.Value, 5f);
                    yield return new WaitForSeconds(0.5f);
                }
            }
            Debug.Log("[HAMMER] selftest " + (fails == 0 ? "OK (all done)" : "FAIL count=" + fails));
        }
    }
}
