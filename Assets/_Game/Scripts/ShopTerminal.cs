using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// V2: терминал «Магазин» в лобби (рядом с налоговым). E открывает IMGUI-меню (курсор свободен, как пауза) —
    /// это делает PlayerHands у владельца (ShopUi.Open); покупку из общей кассы решает сервер (GameState.ServerBuy).
    /// </summary>
    public class ShopTerminal : StaticInteractable
    {
        public override string Hint(HeldItem held)
        {
            var gs = GameState.Instance;
            if (gs == null || gs.Phase.Value != GamePhase.Playing) return "";
            return Interact.E("открыть магазин");
        }

        public override void ServerInteract(PlayerHands who, HeldItem held) { }       // меню открывается локально у игрока
    }
}
