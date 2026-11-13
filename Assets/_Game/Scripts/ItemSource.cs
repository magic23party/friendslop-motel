namespace Friendslop
{
    /// <summary>
    /// Бесконечный источник предмета (стеллаж, стопка бланков, стойка швабр/кувалд).
    /// E с пустыми руками — взять; E с тем же предметом — положить обратно.
    /// </summary>
    public class ItemSource : StaticInteractable
    {
        public ItemType type;

        /// <summary>V2: с тележкой горничной расходник того же типа добавляется в стопку (до cartMax); полная стопка — E кладёт обратно.</summary>
        bool CanAddToStack(HeldItem held)
        {
            var gs = GameState.Instance;
            return held.type == type && Items.IsStackable(type) && gs != null && gs.HasCart && Items.StackCount(held) < gs.config.cartMax;
        }

        public override string Hint(HeldItem held)
        {
            if (held.IsNone) return Interact.E("взять: " + Items.Name(type));
            if (CanAddToStack(held)) return Interact.E($"взять ещё: {Items.Name(type).ToLower()} ({Items.StackCount(held)}/{GameState.Instance.config.cartMax})");
            if (held.type == type) return Interact.E("положить обратно");
            return Interact.Busy;
        }

        public override void ServerInteract(PlayerHands who, HeldItem held)
        {
            if (held.IsNone) who.ServerTake(new HeldItem(type));
            else if (CanAddToStack(held)) who.Held.Value = Items.Stack(type, Items.StackCount(held) + 1);
            else if (held.type == type) who.ServerClear();
        }
    }
}
