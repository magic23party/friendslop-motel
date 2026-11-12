using System.Collections.Generic;
using UnityEngine;

namespace Friendslop
{
    /// <summary>Точка появления игрока в Motel (index 0..7).</summary>
    public class SpawnPoint : MonoBehaviour
    {
        public static readonly List<SpawnPoint> All = new List<SpawnPoint>();
        public int index;
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static SpawnPoint For(ulong clientId)
        {
            if (All.Count == 0) return null;
            int want = (int)(clientId % (ulong)All.Count);
            foreach (var s in All) if (s.index == want) return s;
            return All[0];
        }
    }
}
