using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// V2: показывает объект, пока куплено улучшение (кофемашина, стеллажи вантузов и лампочек, тележка).
    /// Сам компонент сидит на всегда активном родителе, а target по умолчанию выключен Builder'ом.
    /// </summary>
    public class UpgradeGate : MonoBehaviour
    {
        public ShopItem item;
        public GameObject target;

        void Update()
        {
            var gs = GameState.Instance;
            bool on = gs != null && gs.Has(item);
            if (target != null && target.activeSelf != on) target.SetActive(on);
        }
    }
}
