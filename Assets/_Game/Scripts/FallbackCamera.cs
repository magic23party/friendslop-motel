using UnityEngine;

namespace Friendslop
{
    /// <summary>Резервная камера сцены (пока у игрока нет своей). Выключается, когда камера игрока включилась.</summary>
    public class FallbackCamera : MonoBehaviour
    {
        public static void Hide()
        {
            foreach (var f in FindObjectsByType<FallbackCamera>())
                f.gameObject.SetActive(false);
        }
    }
}
