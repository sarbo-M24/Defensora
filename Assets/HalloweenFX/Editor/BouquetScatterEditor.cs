using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Defensora.FX.Editor
{
    /// <summary>Places bouquets for <see cref="BouquetScatter"/>. Shares the house/ground helpers with the candle scatter.</summary>
    [CustomEditor(typeof(BouquetScatter))]
    public class BouquetScatterEditor : UnityEditor.Editor
    {
        private const string PrefabsDir = "Assets/Prefabs/Bouquets";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var scatter = (BouquetScatter)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox($"{scatter.transform.childCount} bouquets.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scatter"))
                    Scatter(scatter);
                if (GUILayout.Button("New Seed + Scatter"))
                {
                    Undo.RecordObject(scatter, "New Bouquet Seed");
                    scatter.seed = Random.Range(0, 100000);
                    Scatter(scatter);
                }
                if (GUILayout.Button("Clear"))
                    Clear(scatter);
            }
        }

        [MenuItem("Tools/Defensora FX/7. Scatter Bouquets In Open Scene", priority = 7)]
        public static void ScatterInOpenScene()
        {
            var scatter = Object.FindFirstObjectByType<BouquetScatter>();
            if (scatter == null)
            {
                var go = new GameObject("Bouquets (Scattered)");
                Undo.RegisterCreatedObjectUndo(go, "Scatter Bouquets");
                scatter = go.AddComponent<BouquetScatter>();

                var prefabs = new List<GameObject>();
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab Bouquet_", new[] { PrefabsDir }))
                    prefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)));
                scatter.bouquetPrefabs = prefabs.ToArray();

                scatter.altar = Find("Altar");
                scatter.mother = Find("Mother_Model");
                scatter.lanternRoot = Find("lanterns");
            }

            Scatter(scatter);
            Selection.activeGameObject = scatter.gameObject;
        }

        private static Transform Find(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.transform : null;
        }

        public static void Clear(BouquetScatter scatter)
        {
            for (int i = scatter.transform.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(scatter.transform.GetChild(i).gameObject);
            EditorSceneManager.MarkSceneDirty(scatter.gameObject.scene);
        }

        // ---------------------------------------------------------------- placement

        private struct Spot
        {
            public Vector3 Position;
            public Vector3 LeanTowards;
            public string Group;
        }

        public static void Scatter(BouquetScatter s)
        {
            if (s.bouquetPrefabs == null || s.bouquetPrefabs.Length == 0)
            {
                Debug.LogError("[Defensora FX] BouquetScatter: assign at least one bouquet prefab.", s);
                return;
            }

            Clear(s);
            var rng = new System.Random(s.seed);
            var spots = new List<Spot>();

            CollectAltarSpots(s, rng, spots);
            CollectLanternSpots(s, rng, spots);
            var houses = CandleScatterEditor.FindHouses(s.gameObject.scene, s.houseNamePrefix);
            foreach (var house in houses)
                CollectWallSpots(s, house, rng, spots);

            // Candles to keep clear of.
            var candles = new List<Vector3>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("CandleLow"))
                    candles.Add(mf.transform.position);

            var placed = new List<Vector3>();
            var counts = new Dictionary<string, int>();

            foreach (var spot in spots)
            {
                if (IsInsideAnyHouse(spot.Position, houses) || TooClose(spot.Position, placed, s.minSpacing) || TooClose(spot.Position, candles, s.candleClearance))
                    continue;
                if (!CandleScatterEditor.TryGround(spot.Position, out Vector3 ground))
                    continue;

                PlaceBouquet(s, ground, spot.LeanTowards, rng);
                placed.Add(ground);
                counts[spot.Group] = counts.TryGetValue(spot.Group, out int c) ? c + 1 : 1;
            }

            EditorSceneManager.MarkSceneDirty(s.gameObject.scene);

            var summary = new List<string>();
            foreach (var pair in counts)
                summary.Add($"{pair.Value} {pair.Key}");
            Debug.Log($"[Defensora FX] Scattered {placed.Count} bouquets: {string.Join(", ", summary)}.");
        }

        private static void CollectAltarSpots(BouquetScatter s, System.Random rng, List<Spot> spots)
        {
            // Inner ring at the mother's feet, leaning in towards her.
            if (s.mother != null)
            {
                Vector3 center = s.mother.position;
                float start = Range(rng, 0f, 360f);
                for (int i = 0; i < s.bouquetsAtFeet; i++)
                {
                    float angle = (start + i * 360f / s.bouquetsAtFeet + Range(rng, -10f, 10f)) * Mathf.Deg2Rad;
                    float radius = Range(rng, s.feetRadius.x, s.feetRadius.y);
                    Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    spots.Add(new Spot { Position = p, LeanTowards = center, Group = "at the mother's feet" });
                }
            }

            // Border just outside the altar platform (and its candles), leaning in towards the altar.
            if (s.altar != null && TryGetBounds(s.altar, out Bounds b))
            {
                Vector3 center = s.mother != null ? s.mother.position : b.center;
                float hx = b.extents.x + s.altarBorderOffset;
                float hz = b.extents.z + s.altarBorderOffset;
                float perimeter = 4f * (hx + hz);
                int count = Mathf.Max(4, Mathf.RoundToInt(perimeter / s.altarBorderSpacing));
                float offset = Range(rng, 0f, 1f);

                for (int i = 0; i < count; i++)
                {
                    float d = ((i + offset + Range(rng, -0.2f, 0.2f)) / count) * perimeter;
                    Vector3 p = b.center + PointOnRectangle(d, hx, hz);
                    spots.Add(new Spot { Position = p, LeanTowards = center, Group = "around the altar" });
                }
            }
        }

        private static void CollectLanternSpots(BouquetScatter s, System.Random rng, List<Spot> spots)
        {
            if (s.lanternRoot == null)
                return;

            foreach (Transform lantern in s.lanternRoot)
            {
                int count = Next(rng, s.bouquetsPerLantern.x, s.bouquetsPerLantern.y);
                float start = Range(rng, 0f, 360f);
                for (int i = 0; i < count; i++)
                {
                    float angle = (start + i * 360f / count + Range(rng, -20f, 20f)) * Mathf.Deg2Rad;
                    float radius = Range(rng, s.lanternRadius.x, s.lanternRadius.y);
                    Vector3 p = lantern.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    spots.Add(new Spot { Position = p, LeanTowards = lantern.position, Group = "around lanterns" });
                }
            }
        }

        private static void CollectWallSpots(BouquetScatter s, CandleScatterEditor.House house, System.Random rng, List<Spot> spots)
        {
            Vector3 center = house.LocalFootprint.center;

            foreach (var (name, b) in house.Walls)
            {
                bool normalIsX = b.size.x < b.size.z;
                float diff = normalIsX ? b.center.x - center.x : b.center.z - center.z;
                if (Mathf.Abs(diff) < 0.5f)
                    continue;

                float sign = Mathf.Sign(diff);
                Vector3 normal = normalIsX ? new Vector3(sign, 0f, 0f) : new Vector3(0f, 0f, sign);
                Vector3 along = normalIsX ? Vector3.forward : Vector3.right;
                float half = (normalIsX ? b.size.z : b.size.x) * 0.5f;
                float thin = (normalIsX ? b.size.x : b.size.z) * 0.5f;

                Vector3 wall = new Vector3(b.center.x, 0f, b.center.z) + normal * thin;
                Vector3 face = wall + normal * s.wallOffset;

                if (name.Contains("door"))
                {
                    if (!s.flankDoors)
                        continue;
                    float d = Mathf.Min(s.doorFlankDistance, half + 0.4f);
                    foreach (float side in new[] { 1f, -1f })
                    {
                        spots.Add(new Spot
                        {
                            Position = house.Root.TransformPoint(face + along * (d * side)),
                            LeanTowards = house.Root.TransformPoint(wall + along * (d * side) - normal),
                            Group = "by doors",
                        });
                    }
                    continue;
                }

                if (rng.NextDouble() >= s.wallChance)
                    continue;

                float t = Range(rng, -0.7f, 0.7f) * half;
                spots.Add(new Spot
                {
                    Position = house.Root.TransformPoint(face + along * t),
                    LeanTowards = house.Root.TransformPoint(wall + along * t - normal),
                    Group = "along house walls",
                });
            }
        }

        private static void PlaceBouquet(BouquetScatter s, Vector3 position, Vector3 leanTowards, System.Random rng)
        {
            var prefab = s.bouquetPrefabs[rng.Next(s.bouquetPrefabs.Length)];
            if (prefab == null)
                return;

            var bouquet = (GameObject)PrefabUtility.InstantiatePrefab(prefab, s.transform);

            // Spin it, then tip the top towards the target.
            Quaternion spin = Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f);
            Vector3 toTarget = leanTowards - position;
            toTarget.y = 0f;
            Quaternion lean = Quaternion.identity;
            if (toTarget.sqrMagnitude > 0.0001f)
                lean = Quaternion.AngleAxis(Range(rng, s.leanRange.x, s.leanRange.y), Vector3.Cross(Vector3.up, toTarget.normalized));

            bouquet.transform.SetPositionAndRotation(position, lean * spin * prefab.transform.rotation);
            bouquet.transform.localScale = prefab.transform.localScale * Range(rng, s.scaleRange.x, s.scaleRange.y);
            Undo.RegisterCreatedObjectUndo(bouquet, "Scatter Bouquets");
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Walks a distance <paramref name="d"/> around a rectangle of half-sizes hx, hz.</summary>
        private static Vector3 PointOnRectangle(float d, float hx, float hz)
        {
            float w = 2f * hx, h = 2f * hz;
            if (d < w) return new Vector3(-hx + d, 0f, -hz);
            d -= w;
            if (d < h) return new Vector3(hx, 0f, -hz + d);
            d -= h;
            if (d < w) return new Vector3(hx - d, 0f, hz);
            d -= w;
            return new Vector3(-hx, 0f, hz - d);
        }

        private static bool TryGetBounds(Transform t, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        private static bool IsInsideAnyHouse(Vector3 p, List<CandleScatterEditor.House> houses) =>
            CandleScatterEditor.IsInsideAnyHouse(p, houses);

        private static bool TooClose(Vector3 p, List<Vector3> others, float spacing)
        {
            float sq = spacing * spacing;
            foreach (var o in others)
            {
                Vector3 d = o - p;
                d.y = 0f;
                if (d.sqrMagnitude < sq)
                    return true;
            }
            return false;
        }

        private static float Range(System.Random rng, float min, float max) => CandleScatterEditor.Range(rng, min, max);
        private static int Next(System.Random rng, int min, int max) => CandleScatterEditor.Next(rng, min, max);
    }
}
