using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Доска ключей: 20 крючков. Маска присутствующих ключей — NetworkVariable (бит i = ключ №(i+1) висит).
    /// Ключ, не висящий на доске (у гостя, на полу, в руках), выдать нельзя.
    /// </summary>
    public class KeyBoard : NetworkBehaviour
    {
        public const int Slots = 20;
        public KeySlot[] slots;                  // слоты 0..19 = номера 1..20
        public NetworkVariable<int> Mask = new NetworkVariable<int>(0);

        public override void OnNetworkSpawn()
        {
            if (IsServer) Mask.Value = (1 << Slots) - 1;
            Mask.OnValueChanged += OnMask;
            Refresh();
        }

        public override void OnNetworkDespawn() { Mask.OnValueChanged -= OnMask; }
        void OnMask(int a, int b) { Refresh(); }
        void Update() { Refresh(); }              // замок комнат зависит от GameState.RoomCount

        public bool Has(int room) => room >= 1 && room <= Slots && (Mask.Value & (1 << (room - 1))) != 0;

        /// <summary>Сервер: снять ключ с доски.</summary>
        public bool ServerTake(int room)
        {
            if (!IsServer || !Has(room)) return false;
            Mask.Value &= ~(1 << (room - 1));
            return true;
        }

        /// <summary>Сервер: повесить ключ на доску.</summary>
        public bool ServerReturn(int room)
        {
            if (!IsServer || room < 1 || room > Slots || Has(room)) return false;
            Mask.Value |= 1 << (room - 1);
            return true;
        }

        void Refresh()
        {
            if (slots == null) return;
            foreach (var s in slots) if (s != null) s.RefreshVisual(Has(s.room) && Room.IsActive(s.room));
        }
    }
}
