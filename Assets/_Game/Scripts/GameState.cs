using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    public enum GamePhase { Lobby, Playing, GameOver }

    /// <summary>
    /// Состояние смены (in-scene NetworkObject в Motel). Сервер-авторитет.
    /// Касса, налог (счёт каждые taxInterval, дедлайн taxDeadline), Game Over, перезапуск.
    /// Время налога — по NetworkManager.ServerTime (клиенты считают остаток локально, трафика нет).
    /// </summary>
    public class GameState : NetworkBehaviour
    {
        public static GameState Instance;
        /// <summary>Сервер: хост нажал «Заново» после Game Over — C4/C5 убирают гостей и драки.</summary>
        public static event Action ServerRestarted;

        static readonly bool FastTax = NetBootstrap.Arg("-fasttax");      // тест: налог и дедлайн по 20 с
        static readonly bool SelfTest = NetBootstrap.Arg("-roomtest");

        public GameConfig config;

        public NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.Lobby);
        /// <summary>0 пока смена не начата; потом 10 или 20 (фиксируется при старте).</summary>
        public NetworkVariable<int> RoomCount = new NetworkVariable<int>(0);
        public NetworkVariable<int> PlayerCount = new NetworkVariable<int>(0);

        /// <summary>Касса, $. Меняется только ServerAddMoney / ServerPayTax.</summary>
        public NetworkVariable<int> Money = new NetworkVariable<int>(0);
        /// <summary>Сколько всего заработано за смену (для экрана «Мотель закрыт»).</summary>
        public NetworkVariable<int> Earned = new NetworkVariable<int>(0);
        /// <summary>true — счёт выставлен и не оплачен.</summary>
        public NetworkVariable<bool> TaxDue = new NetworkVariable<bool>(false);
        /// <summary>Момент (ServerTime): TaxDue=false — следующий счёт, TaxDue=true — дедлайн оплаты.</summary>
        public NetworkVariable<double> TaxTime = new NetworkVariable<double>(0);
        public NetworkVariable<double> ShiftStart = new NetworkVariable<double>(0);
        /// <summary>Длительность смены, фиксируется при Game Over.</summary>
        public NetworkVariable<float> ShiftSeconds = new NetworkVariable<float>(0);

        public float TaxInterval => FastTax ? 20f : config.taxInterval;
        public float TaxDeadline => FastTax ? 20f : config.taxDeadline;
        public double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        /// <summary>Секунд до следующего счёта (TaxDue=false) или до дедлайна (TaxDue=true).</summary>
        public float TaxLeft => Mathf.Max(0f, (float)(TaxTime.Value - Now));
        public float ShiftElapsed => Phase.Value == GamePhase.Playing ? Mathf.Max(0f, (float)(Now - ShiftStart.Value)) : ShiftSeconds.Value;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                Money.Value = config.startMoney;
                if (SelfTest) gameObject.AddComponent<ShiftSelfTest>();
            }
        }

        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            PlayerCount.Value = NetworkManager.ConnectedClientsIds.Count;
            if (Phase.Value != GamePhase.Playing) return;

            double now = Now;
            if (!TaxDue.Value)
            {
                if (now >= TaxTime.Value)
                {
                    TaxDue.Value = true;
                    TaxTime.Value = now + TaxDeadline;
                    Debug.Log($"[TAX] invoice {config.taxAmount} $ issued, deadline {TaxDeadline:0}s, money={Money.Value}");
                }
            }
            else if (now >= TaxTime.Value)
            {
                GameOver();
            }
        }

        /// <summary>Только на сервере (кнопка хоста). Фиксирует число комнат и начинает смену.</summary>
        public bool StartShift()
        {
            if (!IsServer || Phase.Value != GamePhase.Lobby) return false;
            int players = NetworkManager.ConnectedClientsIds.Count;
            RoomCount.Value = players <= config.smallTeamMax ? config.smallRooms : config.bigRooms;
            BeginShift();
            Phase.Value = GamePhase.Playing;
            Debug.Log($"[NET] shift started: players={players} rooms={RoomCount.Value}");
            return true;
        }

        void BeginShift()
        {
            Money.Value = config.startMoney;
            Earned.Value = 0;
            TaxDue.Value = false;
            ShiftSeconds.Value = 0f;
            ShiftStart.Value = Now;
            TaxTime.Value = Now + TaxInterval;
            Room.ServerResetAll(config.roomsStartDirty);
            Debug.Log($"[TAX] shift begins: money={Money.Value}, first invoice in {TaxInterval:0}s");
        }

        // ------------------------------------------------------------ касса

        /// <summary>Сервер: положить в кассу (delta>0, учитывается в «заработано») или снять штраф (delta&lt;0). Касса не уходит в минус.</summary>
        public void ServerAddMoney(int delta)
        {
            if (!IsServer || delta == 0) return;
            Money.Value = Mathf.Max(0, Money.Value + delta);
            if (delta > 0) Earned.Value += delta;
            Debug.Log($"[MONEY] {delta:+0;-0} -> {Money.Value}");
        }

        /// <summary>Сервер: оплатить выставленный счёт. false — счёта нет или не хватает денег.</summary>
        public bool ServerPayTax()
        {
            if (!IsServer || Phase.Value != GamePhase.Playing || !TaxDue.Value || Money.Value < config.taxAmount) return false;
            Money.Value -= config.taxAmount;
            TaxDue.Value = false;
            TaxTime.Value = Now + TaxInterval;
            Debug.Log($"[TAX] paid {config.taxAmount} $, money={Money.Value}, next invoice in {TaxInterval:0}s");
            return true;
        }

        // ------------------------------------------------------------ конец смены

        /// <summary>Сервер: «Мотель закрыт». Вызывается при просроченном налоге.</summary>
        public void GameOver()
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            ShiftSeconds.Value = (float)(Now - ShiftStart.Value);
            Phase.Value = GamePhase.GameOver;
            Debug.Log($"[TAX] GAME OVER: tax unpaid, shift {ShiftSeconds.Value:0}s, earned {Earned.Value} $");
        }

        /// <summary>Сервер (кнопка хоста «Заново»): новая смена с тем же числом комнат.</summary>
        public bool ServerRestart()
        {
            if (!IsServer || Phase.Value != GamePhase.GameOver) return false;
            ServerRestarted?.Invoke();          // гости/драки убирают себя первыми (могут уронить предметы)
            foreach (var w in new List<WorldItem>(WorldItem.All)) if (w.IsSpawned) w.NetworkObject.Despawn();
            foreach (var h in PlayerHands.All) h.ServerClear();
            var board = MotelMap.Instance != null ? MotelMap.Instance.keyBoard : null;
            if (board != null) board.ServerReset();
            BeginShift();
            Phase.Value = GamePhase.Playing;
            Debug.Log("[TAX] shift restarted");
            return true;
        }
    }
}
