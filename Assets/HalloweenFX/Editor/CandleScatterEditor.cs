using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Defensora.FX.Editor
{
    /// <summary>
    /// Places candle clusters for <see cref="CandleScatter"/>. Houses are found by name, their
    /// ground-floor wall pieces are measured in the house's own space (so rotated houses work),
    /// and a cluster goes just outside each wall's outer face, snapped to the ground.
    /// </summary>
    [CustomEditor(typeof(CandleScatter))]
    public class CandleScatterEditor : UnityEditor.Editor
    {
        private const string CandlePrefabPath = "Assets/Prefabs/Candle.prefab";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var scatter = (CandleScatter)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox($"{CountCandles(scatter)} candles in {scatter.transform.childCount} clusters.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scatter"))
                    Scatter(scatter);
                if (GUILayout.Button("New Seed + Scatter"))
                {
                    Undo.RecordObject(scatter, "New Candle Seed");
                    scatter.seed = Random.Range(0, 100000);
                    Scatter(scatter);
                }
                if (GUILayout.Button("Clear"))
                    Clear(scatter);
            }
        }

        [MenuItem("Tools/Defensora FX/5. Scatter Candles In Open Scene", priority = 5)]
        public static void ScatterInOpenScene()
        {
            var scatter = Object.FindFirstObjectByType<CandleScatter>();
            if (scatter == null)
            {
                var go = new GameObject("Candles (Scattered)");
                Undo.RegisterCreatedObjectUndo(go, "Scatter Candles");
                scatter = go.AddComponent<CandleScatter>();
                scatter.candlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CandlePrefabPath);
                scatter.lightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HalloweenFXBuilder.CandlePrefabPath);
                var lanterns = GameObject.Find("lanterns");
                if (lanterns != null)
                    scatter.lanternRoot = lanterns.transform;
            }

            Scatter(scatter);
            Selection.activeGameObject = scatter.gameObject;
        }

        // ---------------------------------------------------------------- placement

        private struct Spot
        {
            public Vector3 Position;
            public bool Door;
        }

        internal struct House
        {
            public Transform Root;
            public Bounds LocalFootprint;
            public List<(string name, Bounds local)> Walls;
        }

        public static void Clear(CandleScatter scatter)
        {
            for (int i = scatter.transform.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(scatter.transform.GetChild(i).gameObject);
            EditorSceneManager.MarkSceneDirty(scatter.gameObject.scene);
        }

        public static void Scatter(CandleScatter s)
        {
            if (s.candlePrefab == null)
            {
                Debug.LogError("[Defensora FX] CandleScatter: assign a Candle Prefab.", s);
                return;
            }

            Clear(s);
            var rng = new System.Random(s.seed);
            var houses = FindHouses(s);
            var spots = new List<Spot>();

            foreach (var house in houses)
                CollectWallSpots(s, house, rng, spots);

            var lanterns = new List<Vector3>();
            if (s.lanternRoot != null)
                foreach (Transform lantern in s.lanternRoot)
                    lanterns.Add(lantern.position);

            // Existing candles elsewhere in the scene count as taken space.
            var taken = new List<Vector3>(lanterns);
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("CandleLow") && !mf.transform.IsChildOf(s.transform))
                    taken.Add(mf.transform.position);

            int clusters = 0, candles = 0;

            foreach (var spot in spots)
            {
                if (IsInsideAnyHouse(spot.Position, houses) || IsTooClose(spot.Position, taken, s.minClusterSpacing))
                    continue;
                if (!TryGround(spot.Position, out Vector3 ground))
                    continue;

                taken.Add(ground);
                var range = spot.Door ? s.candlesPerDoorCluster : s.candlesPerCluster;
                candles += BuildCluster(s, ground, Next(rng, range.x, range.y), s.clusterRadius, rng, clusters);
                clusters++;
            }

            // Rings around the lanterns: individual candles, no extra lights (the lantern has one).
            for (int l = 0; l < lanterns.Count; l++)
            {
                var ring = new GameObject($"Lantern Candles {l + 1}");
                Undo.RegisterCreatedObjectUndo(ring, "Scatter Candles");
                ring.transform.SetParent(s.transform, false);
                ring.transform.position = lanterns[l];

                int count = Next(rng, s.candlesAroundLantern.x, s.candlesAroundLantern.y);
                float start = Range(rng, 0f, 360f);

                for (int i = 0; i < count; i++)
                {
                    float angle = (start + i * 360f / count + Range(rng, -12f, 12f)) * Mathf.Deg2Rad;
                    float radius = Range(rng, s.lanternRingRadius.x, s.lanternRingRadius.y);
                    Vector3 p = lanterns[l] + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (!TryGround(p, out Vector3 ground))
                        continue;
                    PlaceCandle(s, ring.transform, ground, Range(rng, s.scaleRange.x, s.scaleRange.y), rng);
                    candles++;
                }
            }

            EditorSceneManager.MarkSceneDirty(s.gameObject.scene);
            Debug.Log($"[Defensora FX] Scattered {candles} candles: {clusters} wall clusters around {houses.Count} houses, rings around {lanterns.Count} lanterns.");
        }

        internal static List<House> FindHouses(CandleScatter s) => FindHouses(s.gameObject.scene, s.houseNamePrefix);

        internal static List<House> FindHouses(UnityEngine.SceneManagement.Scene scene, string houseNamePrefix)
        {
            var houses = new List<House>();

            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.activeInHierarchy || !root.name.StartsWith(houseNamePrefix, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var house = new House { Root = root.transform, Walls = new List<(string, Bounds)>() };
                bool any = false;
                float baseY = root.transform.position.y;

                foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    string n = mf.name.ToLowerInvariant();
                    if (mf.sharedMesh == null || !n.StartsWith("wall") || n.Contains("half") || n.Contains("detail"))
                        continue;

                    Bounds local = LocalBounds(root.transform, mf);
                    // Ground floor only: pieces whose bottom sits at the house base.
                    if (local.min.y > 0.5f || mf.GetComponent<Renderer>().bounds.min.y > baseY + 0.5f)
                        continue;

                    house.Walls.Add((n, local));
                    if (!any) { house.LocalFootprint = local; any = true; }
                    else house.LocalFootprint.Encapsulate(local);
                }

                if (any)
                    houses.Add(house);
            }

            return houses;
        }

        private static void CollectWallSpots(CandleScatter s, House house, System.Random rng, List<Spot> spots)
        {
            Vector3 center = house.LocalFootprint.center;

            foreach (var (name, b) in house.Walls)
            {
                // A wall piece is thin along its normal axis.
                bool normalIsX = b.size.x < b.size.z;
                float diff = normalIsX ? b.center.x - center.x : b.center.z - center.z;
                if (Mathf.Abs(diff) < 0.5f)
                    continue; // interior

                float sign = Mathf.Sign(diff);
                Vector3 normal = normalIsX ? new Vector3(sign, 0f, 0f) : new Vector3(0f, 0f, sign);
                Vector3 along = normalIsX ? Vector3.forward : Vector3.right;
                float half = (normalIsX ? b.size.z : b.size.x) * 0.5f;
                float thin = (normalIsX ? b.size.x : b.size.z) * 0.5f;

                Vector3 face = new Vector3(b.center.x, 0f, b.center.z) + normal * (thin + s.wallOffset);

                if (name.Contains("door"))
                {
                    if (!s.flankDoors)
                        continue;
                    float d = Mathf.Min(s.doorFlankDistance, half);
                    spots.Add(new Spot { Position = house.Root.TransformPoint(face + along * d), Door = true });
                    spots.Add(new Spot { Position = house.Root.TransformPoint(face - along * d), Door = true });
                    continue;
                }

                if (rng.NextDouble() < s.wallClusterChance)
                    spots.Add(new Spot { Position = house.Root.TransformPoint(face + along * (Range(rng, -0.6f, 0.6f) * half)) });
                if (rng.NextDouble() < s.secondClusterChance)
                    spots.Add(new Spot { Position = house.Root.TransformPoint(face + along * (Range(rng, -0.9f, 0.9f) * half)) });
            }
        }

        private static int BuildCluster(CandleScatter s, Vector3 ground, int count, float radius, System.Random rng, int index)
        {
            var cluster = new GameObject("Candle Cluster");
            Undo.RegisterCreatedObjectUndo(cluster, "Scatter Candles");
            cluster.transform.SetParent(s.transform, false);
            cluster.transform.SetPositionAndRotation(ground, Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f));

            var used = new List<Vector2>();
            int placed = 0;

            for (int i = 0; i < count; i++)
            {
                // First candle in the middle and tallest; the rest shorter around it.
                Vector2 offset = Vector2.zero;
                if (i > 0)
                {
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        float a = Range(rng, 0f, Mathf.PI * 2f);
                        offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range(rng, radius * 0.35f, radius);
                        if (!used.Exists(u => (u - offset).sqrMagnitude < 0.06f * 0.06f))
                            break;
                    }
                }
                used.Add(offset);

                float scale = i == 0
                    ? Range(rng, Mathf.Lerp(s.scaleRange.x, s.scaleRange.y, 0.7f), s.scaleRange.y)
                    : Range(rng, s.scaleRange.x, Mathf.Lerp(s.scaleRange.x, s.scaleRange.y, 0.8f));

                Vector3 world = cluster.transform.TransformPoint(new Vector3(offset.x, 0f, offset.y));
                if (TryGround(world, out Vector3 g))
                    world = g;
                PlaceCandle(s, cluster.transform, world, scale, rng);
                placed++;
            }

            if (s.lightPrefab != null && s.lightEveryNthCluster > 0 && index % s.lightEveryNthCluster == 0)
                AddLight(s, cluster.transform);

            return placed;
        }

        private static void PlaceCandle(CandleScatter s, Transform parent, Vector3 position, float scale, System.Random rng)
        {
            var candle = (GameObject)PrefabUtility.InstantiatePrefab(s.candlePrefab, parent);
            // Keep the prefab's own rotation (the FBX is authored lying down and rotated upright),
            // then spin and tilt it slightly in world space on top of that.
            Quaternion jitter = Quaternion.Euler(
                Range(rng, -s.maxTilt, s.maxTilt), Range(rng, 0f, 360f), Range(rng, -s.maxTilt, s.maxTilt));
            candle.transform.SetPositionAndRotation(position, jitter * s.candlePrefab.transform.rotation);
            candle.transform.localScale = s.candlePrefab.transform.localScale * scale;
        }

        private static void AddLight(CandleScatter s, Transform cluster)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(s.lightPrefab, cluster);
            go.transform.localPosition = new Vector3(0f, 0.35f, 0f);

            var light = go.GetComponent<Light>();
            if (light != null)
            {
                light.intensity = s.lightIntensity;
                light.range = s.lightRange;
                light.shadows = s.lightShadows ? LightShadows.Soft : LightShadows.None;
            }

            var flicker = go.GetComponent<FlickerLight>();
            if (flicker != null)
            {
                var so = new SerializedObject(flicker);
                var p = so.FindProperty("baseIntensity");
                if (p != null)
                    p.floatValue = s.lightIntensity;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ---------------------------------------------------------------- helpers

        private static Bounds LocalBounds(Transform root, MeshFilter mf)
        {
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 min = mb.min, max = mb.max;
            Bounds result = default;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                Vector3 local = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (i == 0) result = new Bounds(local, Vector3.zero);
                else result.Encapsulate(local);
            }

            return result;
        }

        internal static bool IsInsideAnyHouse(Vector3 world, List<House> houses)
        {
            foreach (var house in houses)
            {
                Vector3 local = house.Root.InverseTransformPoint(world);
                Bounds f = house.LocalFootprint;
                if (local.x > f.min.x && local.x < f.max.x && local.z > f.min.z && local.z < f.max.z)
                    return true;
            }
            return false;
        }

        private static bool IsTooClose(Vector3 p, List<Vector3> taken, float spacing)
        {
            float sq = spacing * spacing;
            foreach (var t in taken)
            {
                Vector3 d = t - p;
                d.y = 0f;
                if (d.sqrMagnitude < sq)
                    return true;
            }
            return false;
        }

        /// <summary>Snaps to the ground (terrain, road, floors), ignoring the houses' box colliders.</summary>
        internal static bool TryGround(Vector3 p, out Vector3 ground)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider is BoxCollider && hit.collider.transform.parent == null && hit.collider.name.StartsWith("house", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (hit.collider.GetComponentInParent<CandleScatter>() != null)
                    continue;
                ground = hit.point;
                return true;
            }

            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                ground = new Vector3(p.x, terrain.SampleHeight(p) + terrain.transform.position.y, p.z);
                return true;
            }

            ground = p;
            return false;
        }

        private static int CountCandles(CandleScatter s)
        {
            int count = 0;
            foreach (var mf in s.GetComponentsInChildren<MeshFilter>())
                count++;
            return count;
        }

        internal static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
        internal static int Next(System.Random rng, int min, int maxInclusive) => rng.Next(min, maxInclusive + 1);
    }
}
