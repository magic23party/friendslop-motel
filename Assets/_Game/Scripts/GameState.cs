using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    public enum GamePhase { Lobby, Playing, GameOver }

    /// <summary>
    /// Состояние смены (in-scene NetworkObject в Motel). Сервер-авторитет.
    /// Касса, день/ночь (V1), налог, Game Over, перезапуск.
    /// Цикл: день (dayLength) -> ночь (nightLength) -> день... В момент наступления ночи выставляется счёт налога
    /// (taxBase + taxStep * (день-1)), дедлайн — конец ночи; не оплачен -> Game Over.
    /// Время фаз — по NetworkManager.ServerTime (клиенты считают остаток локально, трафика нет).
    /// </summary>
    public class GameState : NetworkBehaviour
    {
        public static GameState Instance;
        /// <summary>Сервер: хост нажал «Заново» после Game Over — C4/C5 убирают гостей и драки.</summary>
        public static event Action ServerRestarted;

        static readonly bool FastTax = NetBootstrap.Arg("-fasttax");      // тест: день и ночь по 20 с
        static readonly bool FastDay = NetBootstrap.Arg("-fastday") || NetBootstrap.Arg("-daytest");   // тест: день 40 с, ночь 20 с
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
        /// <summary>true — счёт выставлен (ночью) и не оплачен.</summary>
        public NetworkVariable<bool> TaxDue = new NetworkVariable<bool>(false);
        /// <summary>Номер дня (с 1). Ночь N принадлежит дню N.</summary>
        public NetworkVariable<int> Day = new NetworkVariable<int>(1);
        /// <summary>true — сейчас ночь (новые гости не приходят, счёт выставлен).</summary>
        public NetworkVariable<bool> Night = new NetworkVariable<bool>(false);
        /// <summary>Момент (ServerTime), когда текущая фаза (день/ночь) заканчивается.</summary>
        public NetworkVariable<double> PhaseEnd = new NetworkVariable<double>(0);
        public NetworkVariable<double> ShiftStart = new NetworkVariable<double>(0);
        /// <summary>Длительность смены, фиксируется при Game Over.</summary>
        public NetworkVariable<float> ShiftSeconds = new NetworkVariable<float>(0);

        public float DayLength => FastDay ? 40f : FastTax ? 20f : config.dayLength;
        public float NightLength => FastDay ? 20f : FastTax ? 20f : config.nightLength;
        public float Warning => Mathf.Min(config.nightWarning, DayLength * 0.5f);
        public float Twilight => Mathf.Min(config.twilight, Mathf.Min(DayLength, NightLength) * 0.5f);
        public double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        /// <summary>Налог дня N: taxBase + taxStep * (N-1).</summary>
        public int TaxFor(int day) => config.taxBase + config.taxStep * Mathf.Max(0, day - 1);
        /// <summary>Сумма текущего счёта (по номеру дня).</summary>
        public int TaxAmount => TaxFor(Day.Value);
        /// <summary>Секунд до конца текущей фазы (до ночи днём, до утра ночью).</summary>
        public float PhaseLeft => Mathf.Max(0f, (float)(PhaseEnd.Value - Now));
        public float PhaseTotal => Night.Value ? NightLength : DayLength;
        /// <summary>Секунд до дедлайна счёта (= до конца ночи); днём — до ночи.</summary>
        public float TaxLeft => PhaseLeft;
        /// <summary>Скоро ночь: днём осталось меньше Warning секунд.</summary>
        public bool NightSoon => Phase.Value == GamePhase.Playing && !Night.Value && PhaseLeft <= Warning;
        public float ShiftElapsed => Phase.Value == GamePhase.Playing ? Mathf.Max(0f, (float)(Now - ShiftStart.Value)) : ShiftSeconds.Value;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            GameConfig.Active = config;
            TaxDue.OnValueChanged += OnTaxDueChanged;
            Night.OnValueChanged += OnNightChanged;
            if (IsServer)
            {
                Money.Value = config.startMoney;
                if (SelfTest) gameObject.AddComponent<ShiftSelfTest>();
                if (NetBootstrap.Arg("-daytest")) gameObject.AddComponent<DaySelfTest>();
            }
        }

        public override void OnNetworkDespawn()
        {
            TaxDue.OnValueChanged -= OnTaxDueChanged;
            Night.OnValueChanged -= OnNightChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>Счёт выставлен — тревога у всех (2D: сигнал не привязан к месту).</summary>
        void OnTaxDueChanged(bool was, bool now)
        {
            if (now) Invoke(nameof(PlayAlarm), 1.1f);        // после сигнала смены фазы
        }

        void PlayAlarm() { if (TaxDue.Value) Sfx.Play2D(Snd.Alarm, 0.8f); }

        /// <summary>Смена дня/ночи — короткий сигнал у всех (ночь — низкий нисходящий, утро — светлый восходящий).</summary>
        void OnNightChanged(bool was, bool now) { Sfx.Play2D(now ? Snd.Dusk : Snd.Dawn, 0.85f); }

        [Rpc(SendTo.Everyone)]
        void WarnRpc() { Sfx.Play2D(Snd.Warn, 0.8f); }

        /// <summary>Место всплывашки денег по умолчанию — над стойкой ресепшена (касса).</summary>
        Vector3 CashPos()
        {
            var map = MotelMap.Instance;
            return map != null && map.receptionDesk != null ? map.receptionDesk.position + new Vector3(-1.2f, 1.0f, 0.7f) : Vector3.up * 2f;
        }

        [Rpc(SendTo.Everyone)]
        void MoneyFxRpc(int delta, Vector3 pos) { Fx.Money(delta, pos); }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            PlayerCount.Value = NetworkManager.ConnectedClientsIds.Count;
            if (Phase.Value != GamePhase.Playing) return;

            double now = Now;
            if (now < PhaseEnd.Value)
            {
                if (!Night.Value && !warned && PhaseLeft <= Warning) { warned = true; WarnRpc(); Debug.Log($"[DAY] night soon (day {Day.Value})"); }
                return;
            }
            if (!Night.Value)
            {
                // наступила ночь: выставляем счёт, дедлайн — конец ночи
                Night.Value = true;
                TaxDue.Value = true;
                PhaseEnd.Value = now + NightLength;
                Debug.Log($"[DAY] night {Day.Value} begins, invoice {TaxAmount} $ issued, deadline {NightLength:0}s, money={Money.Value}");
            }
            else if (TaxDue.Value)
            {
                GameOver();
            }
            else
            {
                // утро: следующий день, налог растёт
                Night.Value = false;
                Day.Value++;
                warned = false;
                PhaseEnd.Value = now + DayLength;
                Debug.Log($"[DAY] day {Day.Value} begins, tax will be {TaxAmount} $, money={Money.Value}");
            }
        }

        bool warned;

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
            Night.Value = false;
            Day.Value = 1;
            warned = false;
            ShiftSeconds.Value = 0f;
            ShiftStart.Value = Now;
            PhaseEnd.Value = Now + DayLength;
            Room.ServerResetAll(config.roomsStartDirty);
            Debug.Log($"[DAY] shift begins: day 1, money={Money.Value}, night in {DayLength:0}s, tax {TaxAmount} $");
        }

        // ------------------------------------------------------------ касса

        /// <summary>Сервер: положить в кассу (delta>0, учитывается в «заработано») или снять штраф (delta&lt;0). Касса не уходит в минус.</summary>
        public void ServerAddMoney(int delta, Vector3? at = null)
        {
            if (!IsServer || delta == 0) return;
            Money.Value = Mathf.Max(0, Money.Value + delta);
            if (delta > 0) Earned.Value += delta;
            MoneyFxRpc(delta, at ?? CashPos());          // «+50 $» / «-5 $» в мире и звук кассы — у всех клиентов
            Debug.Log($"[MONEY] {delta:+0;-0} -> {Money.Value}");
        }

        /// <summary>Сервер: оплатить выставленный счёт. false — счёта нет или не хватает денег.</summary>
        public bool ServerPayTax()
        {
            int amount = TaxAmount;
            if (!IsServer || Phase.Value != GamePhase.Playing || !TaxDue.Value || Money.Value < amount) return false;
            Money.Value -= amount;
            var map = MotelMap.Instance;
            MoneyFxRpc(-amount, map != null && map.taxTerminal != null ? map.taxTerminal.position + Vector3.up * 1.7f : CashPos());
            TaxDue.Value = false;
            Debug.Log($"[TAX] paid {amount} $ (day {Day.Value}), money={Money.Value}, morning in {PhaseLeft:0}s");
            return true;
        }

        // ------------------------------------------------------------ конец смены

        /// <summary>Сервер: «Мотель закрыт». Вызывается при просроченном налоге.</summary>
        public void GameOver()
        {
            if (!IsServer || Phase.Value != GamePhase.Playing) return;
            ShiftSeconds.Value = (float)(Now - ShiftStart.Value);
            Phase.Value = GamePhase.GameOver;
            Debug.Log($"[TAX] GAME OVER: tax {TaxAmount} $ unpaid on day {Day.Value}, shift {ShiftSeconds.Value:0}s, earned {Earned.Value} $");
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
