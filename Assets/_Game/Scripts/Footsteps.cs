using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Шаги игрока (P3): процедурный звук каждые stepDistance метров пути, у любого игрока (по смещению корпуса, поэтому слышно и чужие шаги).
    /// В прыжке и без сознания не звучит; свои шаги тише; дальность шагов короче, чем у остальных звуков.
    /// </summary>
    public class Footsteps : MonoBehaviour
    {
        const float Range = 12f;
        Vector3 last;
        float acc;
        bool init;
        PlayerController pc;
        PlayerCombat combat;

        void Awake() { pc = GetComponent<PlayerController>(); combat = GetComponent<PlayerCombat>(); }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            var p = transform.position;
            if (!init || dt <= 0f) { last = p; init = true; return; }
            var d = p - last; last = p;
            float dy = Mathf.Abs(d.y) / dt;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist > 1.5f || dy > 1.2f || (combat != null && combat.IsDown.Value)) { acc = 0f; return; }      // телепорт / в воздухе / лежит
            acc += dist;
            var cfg = GameConfig.Active;
            float stride = cfg != null ? cfg.stepDistance : 1.7f;
            if (acc < stride) return;
            acc -= stride;
            bool own = pc != null && pc.IsOwner;
            Sfx.Play3D(Snd.Step, p + Vector3.up * 0.1f, own ? 0.35f : 0.7f, Random.Range(0.85f, 1.15f), Range);
        }
    }
}
