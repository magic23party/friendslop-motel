using UnityEngine;

namespace Friendslop
{
    /// <summary>Ссылки на ключевые точки локации — для гостей (C4), терминала (C3) и т.п.</summary>
    public class MotelMap : MonoBehaviour
    {
        public static MotelMap Instance;

        public ItemDb items;
        public KeyBoard keyBoard;
        public Transform receptionDesk;
        public Transform formsStack;
        public Transform guestSpawn;          // вход, где появляются гости
        public Transform guestExit;
        public Transform[] queuePoints;       // очередь у стойки: [0] — первый
        public Transform taxTerminal;
        public Transform corridorPoint;       // середина коридора номеров (C5: место драк)

        void Awake()
        {
            Instance = this;
            Items.Db = items;
            // страховка: у TextMesh без материала текст не рисуется
            foreach (var tm in FindObjectsByType<TextMesh>(FindObjectsInactive.Include))
            {
                var mr = tm.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial == null && tm.font != null) mr.sharedMaterial = tm.font.material;
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
