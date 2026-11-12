using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Friendslop
{
    /// <summary>
    /// Руки игрока: 1 предмет (Held), луч взаимодействия, E / G / ЛКМ.
    /// Клиент-владелец только целится и шлёт RPC; исход решает сервер по (Held, цель):
    /// цель реализует IInteractable.ServerInteract / ServerUse. Игрок сам тоже цель (передача предмета).
    /// </summary>
    public class PlayerHands : NetworkBehaviour, IInteractable
    {
        public static PlayerHands Local;
        public static readonly System.Collections.Generic.List<PlayerHands> All = new System.Collections.Generic.List<PlayerHands>();
        /// <summary>Сервер: ЛКМ (down/up) любого игрока, цель может быть null (удар в воздух). Для кувалды/швабры.</summary>
        public static event Action<PlayerHands, HeldItem, IInteractable, bool> ServerUsed;

        static readonly bool TestMode = NetBootstrap.Arg("-handstest");
        static readonly RaycastHit[] hits = new RaycastHit[24];
        const float EyeHeight = 1.62f;

        public GameConfig config;
        public Transform handView;      // в руке от первого лица (под камерой)
        public Transform handWorld;     // для чужих копий (у корпуса)

        public NetworkVariable<HeldItem> Held = new NetworkVariable<HeldItem>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // клиент-владелец
        public IInteractable Target { get; private set; }
        public string HintText { get; private set; } = "";

        // сервер: состояние ЛКМ этого игрока (для удержания швабры)
        public bool UseDown { get; private set; }
        public IInteractable UseTarget { get; private set; }

        PlayerController pc;
        PlayerCombat combat;
        GameObject visual;
        bool sentDown;
        IInteractable sentTarget;
        int testStage;
        float testTimer;

        /// <summary>Игрок без сознания (C5): ввод и RPC рук заблокированы.</summary>
        public bool Downed => combat != null && combat.IsDown.Value;

        public Vector3 Eye => transform.position + Vector3.up * EyeHeight;

        public override void OnNetworkSpawn()
        {
            pc = GetComponent<PlayerController>();
            combat = GetComponent<PlayerCombat>();
            All.Add(this);
            if (IsOwner) Local = this;
            Held.OnValueChanged += OnHeldChanged;
            RefreshVisual();
        }

        public override void OnNetworkDespawn()
        {
            Held.OnValueChanged -= OnHeldChanged;
            All.Remove(this);
            if (Local == this) Local = null;
            // игрок вышел с предметом в руках (ключ!) — оставить его на полу
            if (IsServer && !Held.Value.IsNone && NetworkManager != null && !NetworkManager.ShutdownInProgress)
                WorldItem.Spawn(Held.Value, transform.position + Vector3.up * 1.2f, Vector3.zero);
        }

        void OnHeldChanged(HeldItem prev, HeldItem now) { RefreshVisual(); }

        void RefreshVisual()
        {
            if (visual != null) Destroy(visual);
            visual = null;
            var h = Held.Value;
            if (h.IsNone) return;
            var anchor = IsOwner ? handView : handWorld;
            if (anchor == null) return;
            visual = Items.CreateVisual(h.type, anchor, false);
            if (visual == null) return;
            if (IsOwner && Items.Db != null && (int)h.type < Items.Db.handOffset.Length)
            {
                visual.transform.localPosition = Items.Db.handOffset[(int)h.type];
                visual.transform.localRotation = Quaternion.Euler(Items.Db.handEuler[(int)h.type]);
            }
        }

        // ------------------------------------------------------------ сервер: API для целей

        /// <summary>Сервер: положить предмет в руки (если пусто). Деньги (Money) уходят в кассу и руки не занимают.</summary>
        public bool ServerTake(HeldItem item)
        {
            if (!IsServer || item.IsNone) return false;
            if (item.type == ItemType.Money)         // деньги сразу в кассу, руки не занимают
            {
                var gs = GameState.Instance;
                if (gs == null) return false;
                gs.ServerAddMoney(item.arg);
                return true;
            }
            if (!Held.Value.IsNone) return false;
            Held.Value = item;
            return true;
        }

        /// <summary>Сервер: забрать предмет из рук (съеден, отдан гостю, занесён в номер).</summary>
        public HeldItem ServerClear()
        {
            var h = Held.Value;
            if (IsServer) Held.Value = default;
            return h;
        }

        // ------------------------------------------------------------ игрок как цель (передать предмет)

        public string Hint(HeldItem held)
        {
            if (held.IsNone || !Held.Value.IsNone || pc == null) return "";
            return Interact.E("передать (" + pc.Nick.Value.ToString() + ")");
        }

        public void ServerInteract(PlayerHands who, HeldItem held)
        {
            if (who == this || held.IsNone || !Held.Value.IsNone) return;
            Held.Value = held;
            who.Held.Value = default;
        }

        // ------------------------------------------------------------ клиент-владелец: ввод

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer && UseDown && Held.Value.IsNone) UseDown = false;
            if (!IsOwner || pc == null) return;

            if (TestMode && pc.Placed) SelfTestStep();       // в batchmode нет фокуса/ввода
            var kb = Keyboard.current;
            var ms = Mouse.current;
            if (!pc.Placed || pc.MenuOpen || Downed || !Application.isFocused || kb == null || ms == null || pc.cam == null)
            {
                Target = null; HintText = "";
                SendUse(false, null);
                return;
            }

            Target = Aim();
            HintText = Target != null ? (Target.Hint(Held.Value) ?? "") : "";

            if (kb.eKey.wasPressedThisFrame && Target != null)
            {
                Interact.Describe(Target, out var r, out var id);
                InteractRpc(r, id);
            }
            if (kb.gKey.wasPressedThisFrame && !Held.Value.IsNone)
            {
                var t = pc.cam.transform;
                DropRpc(t.position, t.forward);
            }
            bool down = ms.leftButton.isPressed && !Held.Value.IsNone;
            SendUse(down, down ? Target : null);

        }

        IInteractable Aim()
        {
            var t = pc.cam.transform;
            int n = Physics.RaycastNonAlloc(t.position, t.forward, hits, config.interactDistance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider hit = null;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.distance >= best) continue;
                if (h.collider.GetComponentInParent<PlayerHands>() == this) continue;   // свой корпус
                best = h.distance; hit = h.collider;
            }
            return hit != null ? hit.GetComponentInParent<IInteractable>() : null;
        }

        void SendUse(bool down, IInteractable target)
        {
            if (down == sentDown && (!down || target == sentTarget)) return;
            sentDown = down; sentTarget = target;
            NetworkObjectReference r = default; int id = 0;
            if (target != null) Interact.Describe(target, out r, out id);
            UseRpc(down, r, id);
        }

        // ------------------------------------------------------------ RPC (выполняются на сервере)

        bool InReach(IInteractable t)
        {
            if (t == null) return false;
            if (TestMode) return true;
            return Interact.Distance(t, Eye) <= config.interactDistance + config.interactReachSlack;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void InteractRpc(NetworkObjectReference netRef, int sceneId)
        {
            if (Downed) return;
            var t = Interact.Resolve(netRef, sceneId);
            if (!InReach(t)) return;
            t.ServerInteract(this, Held.Value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void UseRpc(bool down, NetworkObjectReference netRef, int sceneId)
        {
            var held = Held.Value;
            if (held.IsNone || Downed) { UseDown = false; UseTarget = null; return; }
            var t = down ? Interact.Resolve(netRef, sceneId) : null;
            if (t != null && !InReach(t)) t = null;
            UseDown = down;
            UseTarget = t;
            t?.ServerUse(this, held, down);
            ServerUsed?.Invoke(this, held, t, down);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void DropRpc(Vector3 origin, Vector3 dir)
        {
            if (Downed) return;
            var h = Held.Value;
            if (h.IsNone) return;
            dir = dir.sqrMagnitude < 0.01f ? transform.forward : dir.normalized;
            var eye = Eye;
            if ((origin - eye).sqrMagnitude > 9f) origin = eye;
            var pos = origin + dir * 0.7f;
            if (Physics.Raycast(origin, dir, out var hit, 0.7f, Items.WorldMask, QueryTriggerInteraction.Ignore))
                pos = hit.point - dir * 0.2f;      // перед стеной — не за ней
            Held.Value = default;
            WorldItem.Spawn(h, pos, dir * config.throwForce + Vector3.up * config.throwUp);
        }

        // ------------------------------------------------------------ самотест (-handstest): ключ №(3+id) с доски, бросить, поднять, повесить

        void SelfTestStep()
        {
            testTimer += Time.deltaTime;
            if (testTimer < 1.5f) return;
            testTimer = 0f;
            var map = MotelMap.Instance;
            if (map == null || map.keyBoard == null || testStage > 4) return;
            int room = 3 + (int)OwnerClientId;
            var slot = map.keyBoard.slots[room - 1];
            var key = new HeldItem(ItemType.Key, room);
            switch (testStage++)
            {
                case 0: InteractRpc(default, slot.sceneId); break;
                case 1:
                    Check(Held.Value == key, "take key from board");
                    var t = pc.cam.transform;
                    DropRpc(t.position, t.forward);
                    break;
                case 2:
                    var wi = WorldItem.Nearest(transform.position);
                    Check(Held.Value.IsNone && wi != null, "drop key");
                    if (wi != null) { Interact.Describe(wi, out var r, out var id); InteractRpc(r, id); }
                    break;
                case 3:
                    Check(Held.Value == key, "pick key from floor");
                    InteractRpc(default, slot.sceneId);
                    break;
                case 4:
                    Check(Held.Value.IsNone && map.keyBoard.Has(room), "hang key back");
                    Debug.Log("[HANDS] selftest done owner=" + OwnerClientId);
                    break;
            }
        }

        void Check(bool ok, string what)
        {
            Debug.Log("[HANDS] selftest " + (ok ? "OK " : "FAIL ") + what + " owner=" + OwnerClientId);
        }
    }
}
