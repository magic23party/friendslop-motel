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
        /// <summary>V3: пятно/засор общего туалета (номера берут их у room). Для Bed/Supply/Lamp не используется.</summary>
        public DirtySite mess;
        public SpotKind kind;
        public int supplyIndex;      // для Supply: 0 полотенце, 1 мыло, 2 салфетки

        DirtySite Site => mess != null ? mess : room;

        public override string Hint(HeldItem held)
        {
            if (kind == SpotKind.Stain || kind == SpotKind.Clog) return MessHint(held);
            if (room == null || room.Locked) return "";
            if (kind == SpotKind.Lamp)
            {
                if (!room.LampBroken) return "";
                if (held.type == ItemType.Bulb) return Interact.E("вкрутить лампочку");
                return held.IsNone ? "Лампа перегорела — нужна лампочка (гость платит на 20 $ меньше)" : "";
            }
            if (room.State != RoomState.NeedsService) return "";
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

        /// <summary>Пятно и засор — общий путь для номера и туалета (DirtySite).</summary>
        string MessHint(HeldItem held)
        {
            var site = Site;
            if (site == null || (room != null && room.Locked) || !site.CanClean) return "";
            if (kind == SpotKind.Stain)
            {
                if (!site.HasStain) return "";
                if (held.type != ItemType.Mop) return "Пятно — нужна швабра";
                int pct = site.MopPct.Value;
                return pct > 0 ? "Моешь… " + pct + "%" : "ЛКМ (держать) — мыть пятно";
            }
            if (!site.HasClog) return "";
            if (held.type == ItemType.Plunger)
            {
                int pct = site.PlungePct.Value;
                return pct > 0 ? "Прочищаешь… " + pct + "%" : "ЛКМ (держать) — прочистить засор";
            }
            if (site.PlumberPending) return "Сантехник уже идёт (" + TaskBoard.Fmt(site.PlumberLeft) + ")";
            return "Засор — нужен вантуз (или вызови сантехника в магазине)";
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
