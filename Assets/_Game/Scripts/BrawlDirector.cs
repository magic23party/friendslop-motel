using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Драки гостей (C5, только сервер). Раз в ~brawlInterval двое живущих гостей выходят в коридор и дерутся (GuestState.Brawl).
    /// Пока хоть один дерётся — штраф brawlDrainAmount $ каждые brawlDrainInterval с; драка кончается, когда игроки уложили обоих
    /// (Guest → Down). Строку «Драка в коридоре!» в списке задач даёт Guest (по State всех гостей, видна всем).
    /// С флагами -guesttest / -fighttest расписание выключено (драки запускает тест через ServerTryStartBrawl).
    /// </summary>
    public class BrawlDirector : MonoBehaviour
    {
        public static BrawlDirector Instance;
        public static bool Paused = NetBootstrap.Arg("-guesttest") || NetBootstrap.Arg("-fighttest");

        public GameConfig config;

        float nextBrawlAt, nextDrainAt;
        Vector3 brawlSpot;
        bool wasPlaying, wasActive;
        readonly List<Guest> pool = new List<Guest>();

        void Awake() { Instance = this; GameState.ServerRestarted += OnRestart; }
        void OnDestroy() { if (Instance == this) Instance = null; GameState.ServerRestarted -= OnRestart; }

        void OnRestart() { wasPlaying = false; wasActive = false; }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            var gs = GameState.Instance;
            if (nm == null || !nm.IsServer || gs == null || !gs.IsSpawned) return;
            if (gs.Phase.Value != GamePhase.Playing) { wasPlaying = false; wasActive = false; return; }
            if (!wasPlaying) { wasPlaying = true; nextBrawlAt = Time.time + NextInterval(); }

            // штраф за идущую драку
            bool active = Guest.BrawlCount() > 0;
            float drainEvery = config.brawlDrainInterval / Guest.TimeScale;
            if (active && !wasActive) { nextDrainAt = Time.time + drainEvery; Debug.Log("[FIGHT] brawl is on"); }
            if (!active && wasActive) Debug.Log("[FIGHT] brawl is over");
            wasActive = active;
            if (active && Time.time >= nextDrainAt)
            {
                nextDrainAt += drainEvery;
                int fine = Mathf.RoundToInt(config.brawlDrainAmount);
                gs.ServerAddMoney(-fine, brawlSpot + Vector3.up * 2.4f);
                Debug.Log($"[FIGHT] brawl fine -{fine} $ (till {gs.Money.Value})");
            }

            if (Paused || Time.time < nextBrawlAt) return;
            if (!active && ServerTryStartBrawl()) nextBrawlAt = Time.time + NextInterval();
            else nextBrawlAt = Time.time + 5f / Guest.TimeScale;       // не нашлось пары — попробуем чуть позже
        }

        float NextInterval()
        {
            return Mathf.Max(5f, config.brawlInterval + Random.Range(-config.brawlJitter, config.brawlJitter)) / Guest.TimeScale;
        }

        /// <summary>Сервер: выбрать двух живущих гостей и отправить их драться в коридор. false — пары нет.</summary>
        public bool ServerTryStartBrawl()
        {
            var map = MotelMap.Instance;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || map == null || map.corridorPoint == null) return false;
            pool.Clear();
            foreach (var g in Guest.All) if (g != null && g.CanBrawl) pool.Add(g);
            if (pool.Count < 2) return false;
            int i = Random.Range(0, pool.Count);
            var a = pool[i];
            pool.RemoveAt(i);
            var b = pool[Random.Range(0, pool.Count)];

            var c = map.corridorPoint.position + Vector3.right * Random.Range(-config.brawlSpread, config.brawlSpread);
            brawlSpot = c;
            a.ServerStartBrawl(c + Vector3.left * 0.45f, b);
            b.ServerStartBrawl(c + Vector3.right * 0.45f, a);
            Debug.Log($"[FIGHT] brawl starts in the corridor (x={c.x:0.0})");
            return true;
        }
    }
}
