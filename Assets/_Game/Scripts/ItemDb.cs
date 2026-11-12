using UnityEngine;

namespace Friendslop
{
    /// <summary>Визуалы предметов (префабы без сети), собираются Builder'ом.</summary>
    public class ItemDb : ScriptableObject
    {
        public const string AssetPath = "Assets/_Game/Config/ItemDb.asset";
        public GameObject worldItemPrefab;      // сетевой префаб предмета на полу
        public GameObject[] visuals;            // по (int)ItemType
        public Vector3[] handOffset;            // положение в руке (в камере) по (int)ItemType
        public Vector3[] handEuler;
        public Vector3[] worldHandOffset;       // положение в правой руке модели (чужие копии, P2)
        public Vector3[] worldHandEuler;
    }
}
