namespace Friendslop
{
    public enum SpotKind { Bed, Supply, Stain, Clog, Lamp }

    /// <summary>
    /// Цель внутри номера. Кровать и точки на столике: E с нужным расходником — занести (из стопки — по одному).
    /// Пятно: швабра в руках + ЛКМ (держать) — мыть. Засор (V2): вантуз + ЛКМ (держать) — прочищать.
    /// Лампа (V2): E с лампочкой — вкрутить. Прогресс считает Room на сервере.
    /// </summary>
    public class RoomSpot : StaticInteractable
    {
        public Room room;
        public SpotKind kind;
        public int supplyIndex;      // для Supply: 0 полотенце, 1 мыло, 2 салфетки

        public override string Hint(HeldItem held)
        {
            if (room == null || room.Locked) return "";
            if (kind == SpotKind.Lamp)
            {
                if (!room.LampBroken) return "";
                if (held.type == ItemType.Bulb) return Interact.E("вкрутить лампочку");
                return held.IsNone ? "Лампа перегорела — нужна лампочка (гость платит на 20 $ меньше)" : "";
            }
            if (room.State != RoomState.NeedsService) return "";
            if (kind == SpotKind.Stain)
            {
                if (!room.HasStain) return "";
                if (held.type != ItemType.Mop) return "Пятно — нужна швабра";
                int pct = room.MopPct.Value;
                return pct > 0 ? "Моешь… " + pct + "%" : "ЛКМ (держать) — мыть пятно";
            }
            if (kind == SpotKind.Clog)
            {
                if (!room.HasClog) return "";
                if (held.type == ItemType.Plunger)
                {
                    int pct = room.PlungePct.Value;
                    return pct > 0 ? "Прочищаешь… " + pct + "%" : "ЛКМ (держать) — прочистить засор";
                }
                if (room.PlumberPending) return "Сантехник уже идёт (" + TaskBoard.Fmt(PlumberLeft(room)) + ")";
                return "Засор — нужен вантуз (или вызови сантехника в магазине)";
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

        public static float PlumberLeft(Room r)
        {
            var gs = GameState.Instance;
            return gs == null ? 0f : UnityEngine.Mathf.Max(0f, (float)(r.PlumberEnd.Value - gs.Now));
        }

        public override void ServerInteract(PlayerHands who, HeldItem held)
        {
            if (room == null || room.Locked) return;
            if (kind == SpotKind.Lamp)
            {
                if (held.type == ItemType.Bulb && room.ServerFixLamp()) who.ServerClear();
                return;
            }
            if (kind == SpotKind.Stain || kind == SpotKind.Clog) return;
            int idx = Room.SupplyIndex(held.type);
            if (idx < 0) return;
            if (kind == SpotKind.Supply && idx != supplyIndex) return;
            if (room.ServerDeliver(held.type)) who.ServerConsumeOne();       // из стопки (тележка) — по одному
        }
    }
}
