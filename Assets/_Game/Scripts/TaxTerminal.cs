using UnityEngine;

namespace Friendslop
{
    /// <summary>Налоговый терминал: E — оплатить выставленный счёт из кассы. На экране — статус счёта.</summary>
    public class TaxTerminal : StaticInteractable
    {
        public TextMesh screen;
        string lastText;

        public override string Hint(HeldItem held)
        {
            var gs = GameState.Instance;
            if (gs == null || gs.Phase.Value != GamePhase.Playing) return "";
            int amount = gs.TaxAmount;
            if (!gs.TaxDue.Value)
                return gs.Night.Value ? "Налоги: счёт оплачен (утро через " + TaskBoard.Fmt(gs.PhaseLeft) + ")"
                                      : $"Налоги: счёт {amount} $ выставят ночью (через " + TaskBoard.Fmt(gs.PhaseLeft) + ")";
            if (gs.Money.Value < amount) return $"Счёт {amount} $ — в кассе только {gs.Money.Value} $";
            return Interact.E($"оплатить налог {amount} $");
        }

        public override void ServerInteract(PlayerHands who, HeldItem held)
        {
            var gs = GameState.Instance;
            if (gs != null) gs.ServerPayTax();
        }

        void Update()
        {
            if (screen == null) return;
            var gs = GameState.Instance;
            string txt = "";
            if (gs != null && gs.Phase.Value == GamePhase.Playing)
                txt = gs.TaxDue.Value ? $"СЧЁТ {gs.TaxAmount} $\n{TaskBoard.Fmt(gs.PhaseLeft)}"
                    : gs.Night.Value ? "Оплачено\n" + TaskBoard.Fmt(gs.PhaseLeft) : "Счёта нет\n" + TaskBoard.Fmt(gs.PhaseLeft);
            if (txt == lastText) return;
            lastText = txt;
            screen.text = txt;
            screen.color = gs != null && gs.TaxDue.Value ? new Color(1f, 0.35f, 0.3f) : new Color(0.1f, 0.25f, 0.3f);
        }
    }
}
