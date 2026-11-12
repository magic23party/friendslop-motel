using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    public enum GamePhase { Lobby, Playing, GameOver }

    /// <summary>
    /// Состояние смены (in-scene NetworkObject в Motel). Сервер-авторитет.
    /// Дальше сюда добавятся касса, налоги, Game Over (C3).
    /// </summary>
    public class GameState : NetworkBehaviour
    {
        public static GameState Instance;

        public GameConfig config;

        public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Lobby);
        /// <summary>0 пока смена не начата; потом 10 или 20 (фиксируется при старте).</summary>
        public NetworkVariable<int> RoomCount = new NetworkVariable<int>(0);
        public NetworkVariable<int> PlayerCount = new NetworkVariable<int>(0);

        public override void OnNetworkSpawn() { Instance = this; }
        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (IsServer && IsSpawned) PlayerCount.Value = NetworkManager.ConnectedClientsIds.Count;
        }

        /// <summary>Только на сервере (кнопка хоста). Фиксирует число комнат и начинает смену.</summary>
        public bool StartShift()
        {
            if (!IsServer || Phase.Value != GamePhase.Lobby) return false;
            int players = NetworkManager.ConnectedClientsIds.Count;
            RoomCount.Value = players <= config.smallTeamMax ? config.smallRooms : config.bigRooms;
            Phase.Value = GamePhase.Playing;
            Debug.Log($"[NET] shift started: players={players} rooms={RoomCount.Value}");
            return true;
        }
    }
}
