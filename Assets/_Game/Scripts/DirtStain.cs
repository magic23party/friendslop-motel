using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// V6: пятно грязи на полу холла/коридора — след «грязного» гостя. Сетевой объект (префаб из Builder): спавнит и удаляет только сервер.
    /// Моется шваброй тем же путём, что пятно в номере: ЛКМ (держать) на пятне, прогресс MopPct уменьшает пятно. Пятно целимо прицелом
    /// только со шваброй в руках (AimFrom пропускает его иначе), поэтому не перекрывает лучи E к другим целям; NavMesh не трогает.
    /// Пока пятен ≥ dirtThreshold, терпение гостей у стойки тает быстрее (PatienceMul; складывается с туалетным). Строка «Грязь на полу: N» — в TaskBoard.
    /// </summary>
    public class DirtStain : NetworkBehaviour, IInteractable
    {
        public static readonly List<DirtStain> All = new List<DirtStain>();
        const int Cap = 60;                 // страховка: не больше стольких пятен на полу

        public Transform visual;            // плоский «блин»; уменьшается по ходу мытья
        public NetworkVariable<byte> MopPct = new NetworkVariable<byte>(0);

        float mopProgress;
        Vector3 baseScale = Vector3.one;

        /// <summary>Сколько пятен сейчас на полу (видно на всех машинах).</summary>
        public static int Count
        {
            get { int n = 0; foreach (var d in All) if (d != null && d.IsSpawned) n++; return n; }
        }

        /// <summary>Во сколько раз быстрее тает терпение гостей из-за грязи на полу (1 — пятен меньше порога).</summary>
        public static float PatienceMul
        {
            get
            {
                var gs = GameState.Instance;
                if (gs == null || gs.config == null) return 1f;
                return Count >= gs.config.dirtThreshold ? Mathf.Max(1f, gs.config.dirtPatienceMul) : 1f;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterTasks() { TaskBoard.Register(CollectTask); }

        static void CollectTask(List<TaskLine> into)
        {
            int n = Count;
            if (n <= 0) return;
            var gs = GameState.Instance;
            int th = gs != null && gs.config != null ? gs.config.dirtThreshold : 6;
            into.Add(new TaskLine("Грязь на полу: " + n, n >= th, new[] { ItemType.Mop }));
        }

        /// <summary>Сервер: пятно грязи под ногами гостя.</summary>
        public static DirtStain ServerSpawn(GameConfig cfg, Vector3 pos)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || cfg == null || cfg.dirtPrefab == null || All.Count >= Cap) return null;
            pos.y = Mathf.Max(0f, pos.y) + 0.005f;
            var go = Instantiate(cfg.dirtPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            var d = go.GetComponent<DirtStain>();
            go.GetComponent<NetworkObject>().Spawn();
            return d;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            var rnd = new System.Random((int)NetworkObjectId * 7919 + 13);          // одинаковый «разлёт» размера у всех машин
            baseScale = new Vector3(0.8f + (float)rnd.NextDouble() * 0.35f, 1f, 0.8f + (float)rnd.NextDouble() * 0.35f);
            if (IsServer) GameState.ServerRestarted += ServerRemove;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsServer) GameState.ServerRestarted -= ServerRemove;
        }

        /// <summary>Сервер: убрать пятно (смыто / новая смена).</summary>
        public void ServerRemove()
        {
            if (IsServer && IsSpawned) NetworkObject.Despawn(true);
        }

        /// <summary>Сервер: убрать все пятна (самотесты, перезапуск смены).</summary>
        public static void ServerRemoveAll()
        {
            foreach (var d in new List<DirtStain>(All)) if (d != null) d.ServerRemove();
        }

        // ------------------------------------------------------------ сервер: мытье

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            if (visual != null)
            {
                float k = Mathf.Lerp(1f, 0.35f, MopPct.Value / 100f);
                visual.localScale = new Vector3(0.9f * baseScale.x * k, 0.01f, 0.9f * baseScale.z * k);
            }
        }

        void ServerTick()
        {
            var gs = GameState.Instance;
            if (gs == null || gs.config == null) return;
            float reach = gs.config.interactDistance + gs.config.interactReachSlack;
            bool mopping = false;
            foreach (var h in PlayerHands.All)
            {
                if (!h.UseDown || h.Held.Value.type != ItemType.Mop || !ReferenceEquals(h.UseTarget, this)) continue;
                if (Interact.Distance(this, h.Eye) > reach) continue;
                mopping = true;
                break;
            }
            if (mopping) ServerMop(Time.deltaTime);
            else if (mopProgress > 0f) { mopProgress = 0f; MopPct.Value = 0; }
        }

        /// <summary>Сервер: швабра трёт пятно dt секунд. true — смыто (пятно удалено).</summary>
        public bool ServerMop(float dt)
        {
            if (!IsServer || !IsSpawned) return false;
            var gs = GameState.Instance;
            float need = gs != null && gs.config != null ? gs.config.dirtMopTime : 2f;
            mopProgress += dt;
            if (mopProgress >= need)
            {
                Debug.Log("[DIRT] stain cleaned, left " + (Count - 1));
                NetworkObject.Despawn(true);
                return true;
            }
            MopPct.Value = (byte)Mathf.Clamp(Mathf.FloorToInt(mopProgress / need * 100f), 0, 99);
            return false;
        }

        // ------------------------------------------------------------ IInteractable

        public string Hint(HeldItem held)
        {
            if (held.type != ItemType.Mop) return "Грязь на полу — нужна швабра";
            int pct = MopPct.Value;
            return pct > 0 ? "Моешь… " + pct + "%" : "ЛКМ (держать) — мыть грязь";
        }

        public void ServerInteract(PlayerHands who, HeldItem held) { }
    }
}
