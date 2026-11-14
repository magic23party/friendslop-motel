using System.Collections;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Самотест V1 (флаг -daytest, только на хосте; сам включает быстрые время: день 40 с, ночь 20 с).
    /// 2 цикла: день 1 (гости идут) -> ночь 1 (счёт 100 $, новые гости не приходят, платим) -> день 2 -> ночь 2 (счёт 150 $, не платим)
    /// -> Game Over -> «Заново» сбрасывает день. Результат — строки "[DAY] selftest OK/FAIL ..." и итог "[DAY] selftest OK (all done)".
    /// </summary>
    public class DaySelfTest : MonoBehaviour
    {
        GameState gs;
        int fails;

        void Start() { StartCoroutine(Run()); }

        void Check(bool ok, string what)
        {
            if (!ok) fails++;
            Debug.Log($"[DAY] selftest {(ok ? "OK  " : "FAIL")} {what}");
        }

        IEnumerator WaitFor(System.Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
        }

        IEnumerator Run()
        {
            gs = GameState.Instance;
            var cfg = gs.config;
            yield return WaitFor(() => gs.PlayerCount.Value >= 2 || gs.Phase.Value != GamePhase.Lobby, 20f);
            if (gs.Phase.Value == GamePhase.Lobby) { yield return new WaitForSeconds(1f); gs.StartShift(); }
            yield return WaitFor(() => gs.Phase.Value == GamePhase.Playing, 30f);
            Debug.Log("[DAY] selftest start");

            // ---- день 1
            Check(gs.Phase.Value == GamePhase.Playing && !gs.Night.Value && gs.Day.Value == 1 && !gs.TaxDue.Value, "day 1 starts: day, no invoice");
            Check(gs.TaxAmount == cfg.taxBase && gs.TaxFor(2) == cfg.taxBase + cfg.taxStep, $"tax grows: day1={gs.TaxFor(1)} day2={gs.TaxFor(2)}");
            Check(Mathf.Approximately(cfg.dayLength, 330f) && Mathf.Approximately(cfg.nightLength, 180f), "V5: config day 5:30 / night 3:00 (" + cfg.dayLength + "/" + cfg.nightLength + ")");
            Check(Mathf.Abs(gs.PhaseLeft - gs.DayLength) < 3f, "day length ~" + gs.DayLength + "s, left=" + gs.PhaseLeft.ToString("0"));
            yield return WaitFor(() => gs.Night.Value, gs.DayLength + 15f);
            Check(Guest.StatSpawned > 0, "guests spawned during day 1: " + Guest.StatSpawned);

            // ---- ночь 1: счёт 100, гостей нет, платим
            float nightStart = Time.time;
            Check(gs.Night.Value && gs.TaxDue.Value && gs.Day.Value == 1 && gs.TaxAmount == cfg.taxBase, "night 1: invoice " + gs.TaxAmount + " $ issued at nightfall");
            Check(Mathf.Abs(gs.PhaseLeft - gs.NightLength) < 3f, "night length ~" + gs.NightLength + "s, left=" + gs.PhaseLeft.ToString("0"));
            yield return new WaitForSeconds(1f);
            int spawned = Guest.StatSpawned;
            yield return new WaitForSeconds(6f);
            gs.ServerAddMoney(300);
            int before = gs.Money.Value;
            Check(gs.ServerPayTax() && !gs.TaxDue.Value && gs.Money.Value == before - cfg.taxBase, "paid night-1 invoice, till=" + gs.Money.Value);
            Check(!gs.ServerPayTax(), "cannot pay twice");
            yield return WaitFor(() => !gs.Night.Value, gs.NightLength + 10f);
            Check(Guest.StatSpawned == spawned, "no guests spawned during night 1 (" + spawned + " -> " + Guest.StatSpawned + ")");
            Check(Mathf.Abs((Time.time - nightStart) - gs.NightLength) < 4f, "night 1 lasted " + (Time.time - nightStart).ToString("0.0") + "s");

            // ---- день 2: налог вырос, гости снова идут
            float dayStart = Time.time;
            Check(!gs.Night.Value && gs.Day.Value == 2 && !gs.TaxDue.Value && gs.Phase.Value == GamePhase.Playing, "morning: day 2, invoice cleared");
            Check(gs.TaxAmount == cfg.taxBase + cfg.taxStep, "day 2 tax = " + gs.TaxAmount + " $");
            int morning = Guest.StatSpawned;
            yield return WaitFor(() => gs.Night.Value, gs.DayLength + 15f);
            Check(Guest.StatSpawned > morning, "guests resumed in day 2 (" + morning + " -> " + Guest.StatSpawned + ")");
            Check(Mathf.Abs((Time.time - dayStart) - gs.DayLength) < 4f, "day 2 lasted " + (Time.time - dayStart).ToString("0.0") + "s");

            // ---- ночь 2: счёт 150, не платим -> Game Over в конце ночи
            Check(gs.Night.Value && gs.TaxDue.Value && gs.Day.Value == 2 && gs.TaxAmount == cfg.taxBase + cfg.taxStep, "night 2: invoice " + gs.TaxAmount + " $");
            yield return new WaitForSeconds(1f);
            spawned = Guest.StatSpawned;
            yield return WaitFor(() => gs.Phase.Value == GamePhase.GameOver, gs.NightLength + 10f);
            Check(gs.Phase.Value == GamePhase.GameOver && gs.Day.Value == 2, "game over at end of night 2 (unpaid)");
            Check(Guest.StatSpawned == spawned, "no guests spawned during night 2");
            // V4: итоги — продержались 1 полный день (налог 1 уплачен), лучший день включает 300 $ из ночи 1, рекорд хоста (в batchmode PlayerPrefs не трогаем)
            Check(gs.DaysSurvived.Value == 1 && gs.NewRecord.Value && gs.RecordDays.Value == 1, $"V4 stats: survived {gs.DaysSurvived.Value}, record {gs.RecordDays.Value}, new={gs.NewRecord.Value}");
            Check(gs.BestDayNo.Value >= 1 && gs.BestDayNo.Value <= 2 && gs.BestDayEarned.Value >= 300 && gs.Earned.Value >= gs.BestDayEarned.Value, $"V4 stats: best day {gs.BestDayNo.Value} (+{gs.BestDayEarned.Value} $), earned {gs.Earned.Value}");

            // ---- «Заново»
            gs.ServerRestart();
            yield return null;
            Check(gs.Phase.Value == GamePhase.Playing && gs.Day.Value == 1 && !gs.Night.Value && !gs.TaxDue.Value && gs.TaxAmount == cfg.taxBase && gs.Money.Value == cfg.startMoney,
                "restart: day 1, tax " + gs.TaxAmount + " $, money " + gs.Money.Value);
            Check(gs.DaysSurvived.Value == 0 && gs.BestDayNo.Value == 0 && !gs.NewRecord.Value && gs.Earned.Value == 0, "restart: stats reset");

            Debug.Log("[DAY] selftest " + (fails == 0 ? "OK (all done)" : "FAIL count=" + fails));
        }
    }
}
