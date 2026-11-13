using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// V3: табло у входа — сетка из 20 номеров цветом (свободен / занят / нужно обслужить / закрыт) и крупное число свободных.
    /// Данные — Room (уже синхронизированные NetworkVariable), без новых RPC; обновляется ≈4 раза в секунду.
    /// </summary>
    public class RoomBoard : MonoBehaviour
    {
        public Renderer[] cells;              // 20 клеток: номера 1..20
        public TextMesh[] numbers;
        public TextMesh countText;            // число свободных, крупно
        public TextMesh ofText;               // «из N открытых»
        public Material[] cellMats;           // 0 закрыт, 1 свободен, 2 занят, 3 нужно обслужить
        public Color[] numberColors;
        float next;
        int lastSig = -1;

        void Update()
        {
            float now = Time.unscaledTime;
            if (now < next) return;
            var cfg = GameConfig.Active;
            next = now + (cfg != null ? Mathf.Max(0.05f, cfg.boardRefresh) : 0.25f);
            Refresh();
        }

        void Refresh()
        {
            int free = 0, open = 0, sig = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                var r = Room.Get(i + 1);
                int st = r == null || r.Locked ? 0 : r.State == RoomState.Ready ? 1 : r.State == RoomState.Occupied ? 2 : 3;
                if (st != 0) open++;
                if (st == 1) free++;
                sig = sig * 5 + st;
                if (cells[i] != null && cellMats[st] != null) cells[i].sharedMaterial = cellMats[st];
                if (numbers[i] != null) numbers[i].color = numberColors[st];
            }
            int total = free * 100 + open;
            if (total == lastSig) return;
            lastSig = total;
            if (countText != null) countText.text = free.ToString();
            if (ofText != null) ofText.text = "из " + open;
        }
    }
}
