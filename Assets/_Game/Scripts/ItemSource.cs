namespace Friendslop
{
    /// <summary>
    /// Бесконечный источник предмета (стеллаж, стопка бланков, стойка швабр/кувалд).
    /// E с пустыми руками — взять; E с тем же предметом — положить обратно.
    /// </summary>
    public class ItemSource : StaticInteractable
    {
        public ItemType type;

        public override string Hint(HeldItem held)
        {
            if (held.IsNone) return Interact.E("взять: " + Items.Name(type));
            if (held.type == type) return Interact.E("положить обратно");
            return Interact.Busy;
        }

        public override void ServerInteract(PlayerHands who, HeldItem held)
        {
            if (held.IsNone) who.ServerTake(new HeldItem(type));
            else if (held.type == type) who.ServerClear();
        }
    }
}
