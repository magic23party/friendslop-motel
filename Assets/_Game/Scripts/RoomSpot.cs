namespace Friendslop
{
    public enum SpotKind { Bed, Supply, Stain }

    /// <summary>
    /// Цель внутри номера. Кровать и точки на столике: E с нужным расходником — занести.
    /// Пятно: швабра в руках + ЛКМ (держать) — мыть (прогресс считает Room на сервере).
    /// </summary>
    public class RoomSpot : StaticInteractable
    {
        public Room room;
        public SpotKind kind;
        public int supplyIndex;      // для Supply: 0 полотенце, 1 мыло, 2 салфетки

        public override string Hint(HeldItem held)
        {
            if (room == null || room.Locked || room.State != RoomState.NeedsService) return "";
            if (kind == SpotKind.Stain)
            {
                if (!room.HasStain) return "";
                if (held.type != ItemType.Mop) return "Пятно — нужна швабра";
                int pct = room.MopPct.Value;
                return pct > 0 ? "Моешь… " + pct + "%" : "ЛКМ (держать) — мыть пятно";
            }
            int idx = Room.SupplyIndex(held.type);
            if (idx < 0)
            {
                if (!held.IsNone) return "";
                string need = room.NeedsText();
                return need.Length > 0 ? "Нужно: " + need : "";
            }
            if (kind == SpotKind.Supply && idx != supplyIndex) return "Здесь место для: " + Room.SupplyNames[supplyIndex];
            if (!room.Needs(idx)) return "Это уже есть в номере";
            return Interact.E("занести: " + Room.SupplyNames[idx]);
        }

        public override void ServerInteract(PlayerHands who, HeldItem held)
        {
            if (room == null || kind == SpotKind.Stain || room.Locked) return;
            int idx = Room.SupplyIndex(held.type);
            if (idx < 0) return;
            if (kind == SpotKind.Supply && idx != supplyIndex) return;
            if (room.ServerDeliver(held.type)) who.ServerClear();
        }
    }
}
