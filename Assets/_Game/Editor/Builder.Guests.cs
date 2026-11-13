using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Friendslop.EditorTools
{
    /// <summary>C4: префаб гостя, спавнер в сцене, запекание NavMesh (NavMeshSurface).</summary>
    public static partial class Builder
    {
        const string GuestPath = PrefabDir + "/Guest.prefab";
        const string NavDataPath = SceneDir + "/MotelNavMesh.asset";
        static GameObject guestPrefabBuilt;

        static GameObject BuildGuestPrefab(GameConfig cfg)
        {
            var root = new GameObject("Guest");
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();          // авторитет — сервер (по умолчанию)
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.SyncRotAngleX = false; nt.SyncRotAngleZ = false;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f; agent.height = 1.8f;
            agent.speed = cfg.guestSpeed; agent.acceleration = 16f; agent.angularSpeed = 480f;
            agent.stoppingDistance = 0.3f; agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.enabled = false;                                   // включает только сервер в OnNetworkSpawn

            BodyCollider(root.transform);                            // P2: невидимая капсула-коллайдер
            BuildHumanoid(root.transform);                           // P2: человечек; вид задаёт сид (Guest.Look), аксессуары включает Humanoid
            var status = Label(root.transform, "", new Vector3(0, 2.35f, 0), Vector3.back, 0.16f, Color.white, "Status");

            var guest = root.AddComponent<Guest>();
            guest.config = cfg;
            guest.agent = agent;
            guest.status = status;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, GuestPath);
            Object.DestroyImmediate(root);
            guestPrefabBuilt = prefab;
            return prefab;
        }

        static void BuildGuestSystems(GameConfig cfg)
        {
            var sp = new GameObject("GuestSpawner").AddComponent<GuestSpawner>();
            sp.config = cfg;
            sp.guestPrefab = guestPrefabBuilt;
            new GameObject("BrawlDirector").AddComponent<BrawlDirector>().config = cfg;     // C5: драки в коридоре
        }

        /// <summary>
        /// Запекает NavMesh по физическим коллайдерам сцены (пол, стены, мебель). Радиус агента 0.3 (проём двери 1.2 м).
        /// Заглушки закрытых номеров неактивны при запекании: на рантайме они прорезают дыры (NavMeshObstacle carving).
        /// </summary>
        static void BakeNavMesh()
        {
            var go = new GameObject("NavMesh");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;

            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.3f;
            settings.agentHeight = 1.8f;
            settings.agentClimb = 0.3f;
            var bounds = new Bounds(new Vector3(0, 1.5f, 0), new Vector3(44f, 6f, 44f));
            var sources = new List<NavMeshBuildSource>();
            UnityEngine.AI.NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavDataPath) != null) AssetDatabase.DeleteAsset(NavDataPath);
            AssetDatabase.CreateAsset(data, NavDataPath);
            surface.navMeshData = data;

            // проверка: от входа можно дойти до каждого открытого номера и обратно к выходу
            var inst = NavMesh.AddNavMeshData(data);
            int bad = 0, total = 0;
            var path = new NavMeshPath();
            var map = Object.FindAnyObjectByType<MotelMap>();
            foreach (var r in Object.FindObjectsByType<Room>())
            {
                total++;
                bool ok = NavMesh.SamplePosition(r.inside.position, out var hit, 2f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(map.guestSpawn.position, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                if (!ok) { bad++; Debug.LogError("[NAVMESH] room " + r.index + " unreachable from entrance"); }
            }
            for (int i = 0; i < map.queuePoints.Length; i++)
            {
                total++;
                bool ok = NavMesh.SamplePosition(map.queuePoints[i].position, out var hit, 2f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(map.guestSpawn.position, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                if (!ok) { bad++; Debug.LogError("[NAVMESH] queue point " + i + " unreachable"); }
            }
            // P4: длина пути гостя от стойки (очередь [0]) до номера и от входа до дальнего номера — для сравнения до/после правки прохода
            float sum = 0f, max = 0f; int maxRoom = 0, cnt = 0;
            if (NavMesh.SamplePosition(map.queuePoints[0].position, out var qh, 2f, NavMesh.AllAreas))
                foreach (var r in Object.FindObjectsByType<Room>())
                    if (NavMesh.SamplePosition(r.inside.position, out var rh, 2f, NavMesh.AllAreas)
                        && NavMesh.CalculatePath(qh.position, rh.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        float len = PathLength(path);
                        sum += len; cnt++;
                        if (len > max) { max = len; maxRoom = r.index; }
                    }
            Debug.Log($"[NAVMESH] path desk->room: avg {(cnt > 0 ? sum / cnt : 0f):0.0} m, max {max:0.0} m (room {maxRoom}), rooms {cnt}");
            NavMesh.RemoveNavMeshData(inst);
            Debug.Log($"[NAVMESH] baked, {sources.Count} sources, reachability checked {total - bad}/{total}");
        }

        static float PathLength(NavMeshPath p)
        {
            float l = 0f;
            for (int i = 1; i < p.corners.Length; i++) l += Vector3.Distance(p.corners[i - 1], p.corners[i]);
            return l;
        }
    }
}
