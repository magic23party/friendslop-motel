using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// V5: звонок на стойке ресепшена. E (с любыми предметами в руках, с любой стороны стойки) — «дзынь» (Snd.Bell, 3D, слышат все)
    /// и короткая анимация нажатия кнопки. Кулдаун 0,3 с проверяет сервер; звук и анимацию рассылает RPC игрока (PlayerHands.BellRpc).
    /// Коллайдер маленький (только сам звонок), чтобы не перехватывать луч E у бланков, ключей и гостя.
    /// </summary>
    public class Bell : StaticInteractable
    {
        public static Bell Instance;
        public const float Cooldown = 0.3f;

        public Transform button, dome;
        float nextRingAt, pressAt = -10f;
        Vector3 buttonRest, domeRest;

        /// <summary>Сколько раз звонок принят сервером / сыгран на этой машине (самотесты).</summary>
        public int ServerRings { get; private set; }
        public int LocalRings { get; private set; }

        protected override void OnEnable() { base.OnEnable(); Instance = this; }
        protected override void OnDisable() { base.OnDisable(); if (Instance == this) Instance = null; }

        void Awake()
        {
            if (button != null) buttonRest = button.localPosition;
            if (dome != null) domeRest = dome.localPosition;
        }

        public override string Hint(HeldItem held)
        {
            var gs = GameState.Instance;
            if (gs == null || gs.Phase.Value != GamePhase.Playing) return "";
            return Interact.E("позвонить");
        }

        public override void ServerInteract(PlayerHands who, HeldItem held)
        {
            if (who == null || Time.time < nextRingAt) return;
            nextRingAt = Time.time + Cooldown;
            ServerRings++;
            who.BellRpc();
        }

        /// <summary>На всех машинах (из RPC): звук и нажатие кнопки.</summary>
        public void PlayLocal()
        {
            LocalRings++;
            pressAt = Time.time;
            Sfx.Play3D(Snd.Bell, transform.position + Vector3.up * 0.1f, 1f);
        }

        void Update()
        {
            if (button == null) return;
            float k = 1f - (Time.time - pressAt) / 0.2f;          // 1 → 0 за 0,2 с
            if (k < 0f) k = 0f;
            float dip = Mathf.Sin(k * Mathf.PI) * 0.04f;        // кнопка уходит вниз и возвращается
            button.localPosition = buttonRest + Vector3.down * dip;
            if (dome != null) dome.localPosition = domeRest + Vector3.down * dip * 0.25f;
        }
    }
}
