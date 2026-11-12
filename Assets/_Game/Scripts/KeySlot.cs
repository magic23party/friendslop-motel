using UnityEngine;

namespace Friendslop
{
    /// <summary>Один крючок доски. E: снять ключ / повесить свой ключ.</summary>
    public class KeySlot : StaticInteractable
    {
        public int room;
        public KeyBoard board;
        public GameObject keyVisual;

        bool shown = true;
        public void RefreshVisual(bool on)
        {
            if (keyVisual != null && shown != on) { shown = on; keyVisual.SetActive(on); }
        }

        public override string Hint(HeldItem held)
        {
            if (!Room.IsActive(room)) return "Номер " + room + " закрыт";
            bool has = board.Has(room);
            if (held.IsNone) return has ? Interact.E("взять ключ №" + room) : "Ключа №" + room + " нет на месте";
            if (held.type != ItemType.Key) return Interact.Busy;
            if (held.arg != room) return "Это ключ №" + held.arg + ", не сюда";
            return has ? "" : Interact.E("повесить ключ №" + room);
        }

        public override void ServerInteract(PlayerHands who, HeldItem held)
        {
            if (!Room.IsActive(room)) return;
            if (held.IsNone)
            {
                if (board.ServerTake(room)) who.ServerTake(new HeldItem(ItemType.Key, room));
            }
            else if (held.type == ItemType.Key && held.arg == room && board.ServerReturn(room))
            {
                who.ServerClear();
            }
        }
    }
}
