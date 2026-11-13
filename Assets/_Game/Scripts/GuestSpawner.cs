using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Спавнер гостей (in-scene, не сетевой; работает только на сервере/хосте).
    /// Интервал из GameConfig: ≤ smallTeamMax игроков — 15±10 с, больше — 8±5 с. Не спавнит, пока очередь у стойки полна.
    /// Ночью (V1) новые гости не приходят; утром первый гость — через firstGuestDelay.
    /// С флагом -guesttest стартует на паузе: гостей создаёт GuestSelfTest.
    /// </summary>
    public class GuestSpawner : MonoBehaviour
    {
        public static GuestSpawner Instance;
        public static bool Paused = NetBootstrap.Arg("-guesttest") || NetBootstrap.Arg("-fighttest") || NetBootstrap.Arg("-hammertest") || NetBootstrap.Arg("-shoptest");

        public GameConfig config;
        public GameObject guestPrefab;

        float nextAt, nextStats;
        bool wasPlaying, wasNight;
        bool testAttached;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            var gs = GameState.Instance;
            if (nm == null || !nm.IsServer || gs == null || !gs.IsSpawned) return;

            if (Guest.Fast && !testAttached)
            {
                testAttached = true;
                if (NetBootstrap.Arg("-guesttest")) gameObject.AddComponent<GuestSelfTest>();
                if (NetBootstrap.Arg("-fighttest")) gameObject.AddComponent<FightSelfTest>();
                if (NetBootstrap.Arg("-hammertest")) gameObject.AddComponent<HammerServerTest>();
                if (NetBootstrap.Arg("-shoptest")) gameObject.AddComponent<ShopSelfTest>();
            }

            if (gs.Phase.Value != GamePhase.Playing) { wasPlaying = false; wasNight = false; return; }
            if (!wasPlaying) { wasPlaying = true; wasNight = false; nextAt = Time.time + config.firstGuestDelay / Guest.TimeScale; }
            if (wasNight && !gs.Night.Value) nextAt = Time.time + config.firstGuestDelay / Guest.TimeScale;   // утро: гости снова идут
            wasNight = gs.Night.Value;
            if (Time.time >= nextStats)
            {
                nextStats = Time.time + 60f;
                Debug.Log($"[STATS] guests spawned={Guest.StatSpawned} settled={Guest.StatSettled} paid={Guest.StatPaid} left={Guest.StatLeft} now={Guest.All.Count} money={gs.Money.Value}");
            }
            if (Paused || wasNight || Time.time < nextAt) return;       // ночью новые гости не приходят

            var map = MotelMap.Instance;
            if (map == null || map.queuePoints == null || Guest.QueueCount() >= map.queuePoints.Length) { nextAt = Time.time + 1f; return; }

            ServerSpawn(null);
            nextAt = Time.time + NextInterval(gs);
        }

        float NextInterval(GameState gs)
        {
            bool big = gs.PlayerCount.Value > config.smallTeamMax;
            float b = big ? config.spawnBigBase : config.spawnSmallBase;
            float j = big ? config.spawnBigJitter : config.spawnSmallJitter;
            return Mathf.Max(1f, b + Random.Range(-j, j)) / Guest.TimeScale;
        }

        /// <summary>Сервер: создать гостя у входа. nonPayer=null — по шансу из GameConfig.</summary>
        public Guest ServerSpawn(bool? nonPayer = null)
        {
            var nm = NetworkManager.Singleton;
            var map = MotelMap.Instance;
            if (nm == null || !nm.IsServer || map == null || guestPrefab == null) return null;
            var go = Instantiate(guestPrefab, map.guestSpawn.position, map.guestSpawn.rotation);
            var g = go.GetComponent<Guest>();
            g.ServerInit(nonPayer ?? Random.value < config.nonPayerChance);
            Guest.StatSpawned++;
            go.GetComponent<NetworkObject>().Spawn();
            return g;
        }
    }
}
