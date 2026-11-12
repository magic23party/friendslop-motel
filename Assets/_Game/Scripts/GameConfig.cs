using UnityEngine;

namespace Friendslop
{
    /// <summary>Все числа баланса прототипа (GDD). Крутить в инспекторе, без кода.</summary>
    [CreateAssetMenu(menuName = "Friendslop/GameConfig", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        public const string AssetPath = "Assets/_Game/Config/GameConfig.asset";

        [Header("Сеть / старт")]
        public int port = 7777;
        public int maxPlayers = 8;
        public int smallTeamMax = 4;          // 1..4 игрока -> smallRooms
        public int smallRooms = 10;
        public int bigRooms = 20;

        [Header("Поток гостей (сек)")]
        public float spawnSmallBase = 15f, spawnSmallJitter = 10f;
        public float spawnBigBase = 8f, spawnBigJitter = 5f;

        [Header("Ресепшен (сек)")]
        public float checkInWait = 30f;
        public float signDuration = 2f;
        public float stayMinSmall = 30f, stayMaxSmall = 50f;
        public float stayMinBig = 20f, stayMaxBig = 40f;
        public float checkOutWait = 30f;
        [Range(0f, 1f)] public float nonPayerChance = 0.2f;
        public float refuseDelay = 2f;           // неплательщик у стойки: через сколько отказывается платить
        public float firstGuestDelay = 5f;       // первый гость после начала смены
        public float guestSpeed = 4f;            // м/с

        [Header("Обслуживание номера")]
        public int needsMin = 1, needsMax = 3;
        [Range(0f, 1f)] public float stainChance = 0.4f;
        public float mopTime = 3f;
        public bool roomsStartDirty = false;  // C3-тест: true = номера стартуют NeedsService. С C4 — false (старт Ready)

        [Header("Драки и кувалда")]
        public int npcHp = 3;
        public float npcDownTime = 5f;
        public int hitsToKnockPlayer = 5;
        public float playerDownTime = 5f;
        public float colleagueHitDownTime = 1f;
        public float brawlInterval = 90f;
        public float brawlDrainAmount = 5f;
        public float brawlDrainInterval = 5f;
        public float brawlJitter = 20f;          // разброс интервала между драками, с
        public float brawlSpread = 10f;          // драка в коридоре на случайном x в [-spread; +spread]
        public float npcAttackInterval = 1f;     // ударенный гость бьёт ударившего раз в N с
        public float npcAttackRange = 1.5f;      // дистанция удара гостя, м
        public float npcChaseSpeedMul = 1.5f;    // ударенный гость бежит быстрее обычного шага
        public float npcAggroTimeout = 20f;      // через сколько гость остывает и возвращается к делам
        public float playerHitForget = 10f;      // счётчик ударов по игроку сбрасывается, если не били N с

        [Header("Деньги и налоги")]
        public int startMoney = 150;
        public int guestPays = 50;
        public float taxInterval = 480f;      // 8 мин
        public int taxAmount = 100;
        public float taxDeadline = 180f;      // 3 мин

        [Header("Игрок / взаимодействие")]
        public float moveSpeed = 5f;
        public float runMultiplier = 1.6f;
        public float mouseSensitivity = 0.12f;
        public float gravity = -20f;
        public float interactDistance = 2.5f;
        public float interactReachSlack = 1.5f;   // запас серверной проверки дистанции (лаг)
        public float jumpForce = 6.5f;            // начальная вертикальная скорость прыжка (Space)
        public float throwUp = 1.5f;              // подброс вверх при броске (G)
        public float throwForce = 6f;
        public float hitCooldown = 0.6f;
        public float hitDistance = 2.2f;

        [Header("Звук и эффекты (P3)")]
        [Range(0f, 1f)] public float sfxVolume = 0.7f;      // мир (3D): удары, звонок, касса, шаги
        [Range(0f, 1f)] public float uiVolume = 0.6f;       // интерфейс (2D): клики
        public float sfxMinDistance = 2.5f;                 // до этого расстояния громкость полная
        public float sfxMaxDistance = 26f;                  // дальше — тишина (линейное затухание)
        public float stepDistance = 1.7f;                   // шаг каждые N м пути
        [Range(0f, 2f)] public float shakeScale = 1f;       // сила тряски камеры (0 — выключить)
        public float shakeHit = 0.32f;                      // тряска у бьющего (0..1)
        public float shakeHurt = 0.6f;                      // тряска у пострадавшего
        public float floatTextLife = 1.5f;                  // «+50 $» живёт N с

        // материалы эффектов (ссылки нужны, чтобы шейдеры попали в билд; ставит Builder)
        [HideInInspector] public Material fxLabelMat, fxChipMat;

        /// <summary>Активный конфиг (ставят GameState и меню): им пользуются звук и эффекты.</summary>
        public static GameConfig Active;
    }
}
