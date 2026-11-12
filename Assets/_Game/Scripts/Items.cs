using System;
using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    public enum ItemType : byte { None = 0, Form, Key, Towel, Soap, Napkins, Mop, Sledgehammer, Money }

    /// <summary>
    /// Предмет в руках / на полу: тип + доп. число (для Key — номер комнаты, для Money — сумма).
    /// </summary>
    public struct HeldItem : INetworkSerializeByMemcpy, IEquatable<HeldItem>
    {
        public ItemType type;
        public int arg;

        public HeldItem(ItemType t, int a = 0) { type = t; arg = a; }
        public bool IsNone => type == ItemType.None;
        public bool Is(ItemType t) => type == t;

        public bool Equals(HeldItem o) => type == o.type && arg == o.arg;
        public override bool Equals(object obj) => obj is HeldItem o && Equals(o);
        public override int GetHashCode() => ((int)type << 24) ^ arg;
        public static bool operator ==(HeldItem a, HeldItem b) => a.Equals(b);
        public static bool operator !=(HeldItem a, HeldItem b) => !a.Equals(b);
    }

    public static class Items
    {
        public const int ItemLayer = 8;         // предметы на полу
        public const int PlayerLayer = 9;       // игроки
        /// <summary>Что считается препятствием для лучей (стены, пол): без игроков и предметов.</summary>
        public const int WorldMask = ~((1 << ItemLayer) | (1 << PlayerLayer));

        public static ItemDb Db;                // ставит MotelMap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitPhysics()
        {
            Physics.IgnoreLayerCollision(ItemLayer, PlayerLayer, true);   // предметы не мешают ходить
        }

        public static string Name(ItemType t)
        {
            switch (t)
            {
                case ItemType.Form: return "Бланк";
                case ItemType.Key: return "Ключ";
                case ItemType.Towel: return "Полотенце";
                case ItemType.Soap: return "Мыло";
                case ItemType.Napkins: return "Салфетки";
                case ItemType.Mop: return "Швабра";
                case ItemType.Sledgehammer: return "Кувалда";
                case ItemType.Money: return "Деньги";
                default: return "";
            }
        }

        public static string Name(HeldItem h)
        {
            if (h.type == ItemType.Key && h.arg > 0) return "Ключ №" + h.arg;
            if (h.type == ItemType.Money && h.arg > 0) return "Деньги (" + h.arg + " $)";
            return Name(h.type);
        }

        /// <summary>Визуал предмета с учётом arg (номер на бирке ключа). withColliders=false — для руки.</summary>
        public static GameObject CreateVisual(HeldItem h, Transform parent, bool withColliders)
        {
            var go = CreateVisual(h.type, parent, withColliders);
            if (go != null && h.type == ItemType.Key)
                foreach (var tm in go.GetComponentsInChildren<TextMesh>(true)) tm.text = h.arg > 0 ? h.arg.ToString() : "";
            return go;
        }

        /// <summary>Создаёт визуал предмета под parent. withColliders=false — для руки.</summary>
        public static GameObject CreateVisual(ItemType t, Transform parent, bool withColliders)
        {
            if (Db == null || Db.visuals == null || (int)t >= Db.visuals.Length || Db.visuals[(int)t] == null) return null;
            var go = UnityEngine.Object.Instantiate(Db.visuals[(int)t], parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            if (!withColliders)
                foreach (var c in go.GetComponentsInChildren<Collider>()) { c.enabled = false; UnityEngine.Object.Destroy(c); }
            return go;
        }

        public static void SetLayer(GameObject go, int layer)
        {
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
        }
    }
}
