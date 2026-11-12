using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Предмет на полу: NetworkObject, физику считает сервер (у клиентов rigidbody кинематический,
    /// позицию присылает NetworkTransform). E с пустыми руками — поднять.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WorldItem : NetworkBehaviour, IInteractable
    {
        public static readonly List<WorldItem> All = new List<WorldItem>();

        public NetworkVariable<HeldItem> Item = new NetworkVariable<HeldItem>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        GameObject visual;
        Rigidbody rb;

        /// <summary>Сервер: создать предмет на полу (бросок, гость уронил ключ/деньги).</summary>
        public static WorldItem Spawn(HeldItem item, Vector3 pos, Vector3 velocity)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || Items.Db == null || Items.Db.worldItemPrefab == null || item.IsNone) return null;
            var go = Instantiate(Items.Db.worldItemPrefab, pos, Random.rotation);
            var wi = go.GetComponent<WorldItem>();
            wi.Item.Value = item;
            go.GetComponent<NetworkObject>().Spawn();
            var rb = go.GetComponent<Rigidbody>();
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.insideUnitSphere * 4f;
            return wi;
        }

        public static WorldItem Nearest(Vector3 p)
        {
            WorldItem best = null; float bd = float.MaxValue;
            foreach (var w in All)
            {
                float d = (w.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = w; }
            }
            return best;
        }

        public override void OnNetworkSpawn()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = !IsServer;
            if (IsServer) rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            All.Add(this);
            Item.OnValueChanged += OnItemChanged;
            Rebuild();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Item.OnValueChanged -= OnItemChanged;
        }

        void OnItemChanged(HeldItem a, HeldItem b) { Rebuild(); }

        void Rebuild()
        {
            if (visual != null) Destroy(visual);
            visual = Items.CreateVisual(Item.Value.type, transform, true);
            if (visual != null) Items.SetLayer(visual, Items.ItemLayer);
        }

        void FixedUpdate()
        {
            if (IsServer && IsSpawned && transform.position.y < -5f) NetworkObject.Despawn();   // выпал из мира
        }

        public string Hint(HeldItem held)
        {
            if (Item.Value.type == ItemType.Money) return Interact.E("собрать деньги (" + Item.Value.arg + " $)");   // руки не нужны
            if (!held.IsNone) return Interact.Busy;
            return Interact.E("поднять: " + Items.Name(Item.Value));
        }

        public void ServerInteract(PlayerHands who, HeldItem held)
        {
            if (!IsSpawned) return;
            if (Item.Value.type != ItemType.Money && !held.IsNone) return;     // деньги берутся и с полными руками
            if (!who.ServerTake(Item.Value)) return;
            NetworkObject.Despawn();
        }
    }
}
