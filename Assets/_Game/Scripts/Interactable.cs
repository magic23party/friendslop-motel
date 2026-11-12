using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Всё, на что можно навести прицел. Подсказка считается на клиенте, исход решает сервер
    /// по (предмет в руках, цель). Реализуется на компоненте на объекте или его родителе
    /// (коллайдер ищется через GetComponentInParent).
    /// Сетевые цели (предметы, гости, игроки) — NetworkBehaviour на объекте с NetworkObject;
    /// статичные цели сцены — наследники StaticInteractable (id задаёт Builder).
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Клиент: что показать под прицелом ("E — взять полотенце", "Руки заняты"). Пусто — ничего.</summary>
        string Hint(HeldItem held);
        /// <summary>Сервер: игрок нажал E.</summary>
        void ServerInteract(PlayerHands who, HeldItem held);
        /// <summary>Сервер: ЛКМ по цели (down=true — нажал/держит, false — отпустил). Удар кувалдой, швабра.</summary>
        void ServerUse(PlayerHands who, HeldItem held, bool down) { }
    }

    /// <summary>Статичная цель в сцене. sceneId уникален, ставится Builder'ом (одинаков на всех машинах).</summary>
    public abstract class StaticInteractable : MonoBehaviour, IInteractable
    {
        static readonly Dictionary<int, StaticInteractable> map = new Dictionary<int, StaticInteractable>();
        public int sceneId;

        protected virtual void OnEnable()
        {
            if (sceneId <= 0) return;
            if (map.TryGetValue(sceneId, out var other) && other != null && other != this)
                Debug.LogError($"[HANDS] duplicate sceneId {sceneId}: {name} / {other.name}");
            map[sceneId] = this;
        }

        protected virtual void OnDisable()
        {
            if (map.TryGetValue(sceneId, out var o) && o == this) map.Remove(sceneId);
        }

        public static StaticInteractable ById(int id) => map.TryGetValue(id, out var s) ? s : null;

        public abstract string Hint(HeldItem held);
        public abstract void ServerInteract(PlayerHands who, HeldItem held);
        public virtual void ServerUse(PlayerHands who, HeldItem held, bool down) { }
    }

    public static class Interact
    {
        public const string Busy = "Руки заняты";
        public static string E(string action) => "E — " + action;

        /// <summary>Адрес цели для RPC: статичная — по sceneId, сетевая — по NetworkObject.</summary>
        public static void Describe(IInteractable t, out NetworkObjectReference netRef, out int sceneId)
        {
            netRef = default; sceneId = 0;
            if (t is StaticInteractable s) { sceneId = s.sceneId; return; }
            if (t is Component c)
            {
                var no = c.GetComponentInParent<NetworkObject>();
                if (no != null && no.IsSpawned) netRef = no;
            }
        }

        public static IInteractable Resolve(NetworkObjectReference netRef, int sceneId)
        {
            if (sceneId > 0) return StaticInteractable.ById(sceneId);
            if (netRef.TryGet(out var no)) return no.GetComponent<IInteractable>() ?? no.GetComponentInChildren<IInteractable>();
            return null;
        }

        /// <summary>Расстояние от точки до ближайшего коллайдера цели (примитивы).</summary>
        public static float Distance(IInteractable t, Vector3 from)
        {
            if (!(t is Component c)) return float.MaxValue;
            float best = float.MaxValue;
            foreach (var col in c.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || col.isTrigger) continue;
                best = Mathf.Min(best, (col.ClosestPoint(from) - from).magnitude);
            }
            return best < float.MaxValue ? best : (c.transform.position - from).magnitude;
        }
    }
}
