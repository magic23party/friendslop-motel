using System.Collections.Generic;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Номер мотеля (1..20): геометрия и точки. Логику статусов (Ready/Occupied/NeedsService,
    /// заносы, пятна) делает C3 — здесь только ссылки на точки и табличка.
    /// Локальные оси: +Z — вглубь номера, дверной проём на z=0 (лицом в коридор).
    /// </summary>
    public class Room : MonoBehaviour
    {
        public static readonly List<Room> All = new List<Room>();

        public int index;                 // 1..20
        public Transform door;            // центр проёма
        public Transform outside;         // точка в коридоре у двери
        public Transform inside;          // точка в номере у двери (куда идёт гость)
        public Transform bed;             // кровать (цель для гостя и для заноса)
        public Transform stainPoint;      // место пятна
        public GameObject stainVisual;    // пятно (скрыто, показывает C3)
        public Transform[] supplyPoints;  // точки под расходники: 0 полотенце, 1 мыло, 2 салфетки
        public TextMesh signNumber;       // табличка над дверью: номер
        public TextMesh signStatus;       // табличка: статус / список нужного (пишет C3)
        public GameObject blocker;        // стена-заглушка в проёме, если номер закрыт

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static Room Get(int index)
        {
            foreach (var r in All) if (r.index == index) return r;
            return null;
        }

        /// <summary>Сколько номеров открыто: до старта смены — как для малой команды, потом по GameState.RoomCount.</summary>
        public static int ActiveCount()
        {
            var gs = GameState.Instance;
            if (gs == null) return 10;
            if (gs.RoomCount.Value > 0) return gs.RoomCount.Value;
            return gs.config != null ? gs.config.smallRooms : 10;
        }

        public static bool IsActive(int index) => index >= 1 && index <= ActiveCount();

        public bool Locked => !IsActive(index);

        public void SetSign(string status)
        {
            if (signStatus != null) signStatus.text = status ?? "";
        }

        void Update()
        {
            bool locked = Locked;
            if (blocker != null && blocker.activeSelf != locked) blocker.SetActive(locked);
        }
    }
}
