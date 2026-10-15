using UnityEngine;

namespace Defensora.FX
{
    /// <summary>
    /// Settings for scattering candle clusters along the outside of the houses' ground-floor
    /// walls (flanking every door) and in rings around the lanterns. The placement itself is
    /// editor-only: use the Scatter / Clear buttons on this component (or
    /// Tools/Defensora FX/5. Scatter Candles In Open Scene). Generated candles become children.
    /// </summary>
    public class CandleScatter : MonoBehaviour
    {
        [Header("Prefabs")]
        public GameObject candlePrefab;
        [Tooltip("Optional flickering light added to some clusters.")]
        public GameObject lightPrefab;

        [Header("Targets")]
        [Tooltip("Scene root objects whose name starts with this are treated as houses.")]
        public string houseNamePrefix = "house";
        [Tooltip("Each child gets a ring of candles around its base.")]
        public Transform lanternRoot;

        [Header("Along House Walls")]
        [Tooltip("Gap between the wall face and the cluster centre, in metres.")]
        [Min(0f)] public float wallOffset = 0.3f;
        [Tooltip("Chance a plain ground-floor wall piece gets a cluster.")]
        [Range(0f, 1f)] public float wallClusterChance = 0.8f;
        [Tooltip("Chance a wall piece gets a second cluster.")]
        [Range(0f, 1f)] public float secondClusterChance = 0.35f;
        [Tooltip("Always put clusters on both sides of each door.")]
        public bool flankDoors = true;
        [Min(0f)] public float doorFlankDistance = 0.8f;

        [Header("Around Lanterns")]
        public Vector2Int candlesAroundLantern = new Vector2Int(5, 8);
        public Vector2 lanternRingRadius = new Vector2(0.38f, 0.6f);

        [Header("Clusters")]
        public Vector2Int candlesPerCluster = new Vector2Int(2, 5);
        [Tooltip("Door clusters are bigger.")]
        public Vector2Int candlesPerDoorCluster = new Vector2Int(4, 7);
        [Min(0.02f)] public float clusterRadius = 0.18f;
        [Tooltip("Clusters closer than this to another cluster are skipped.")]
        [Min(0f)] public float minClusterSpacing = 0.7f;

        [Header("Candles")]
        [Tooltip("Random scale range, relative to the prefab's own scale.")]
        public Vector2 scaleRange = new Vector2(0.6f, 1.15f);
        [Range(0f, 15f)] public float maxTilt = 3f;

        [Header("Lights")]
        [Tooltip("Every Nth cluster gets a light. 0 = no lights.")]
        [Min(0)] public int lightEveryNthCluster = 3;
        [Min(0f)] public float lightIntensity = 1.6f;
        [Min(0.1f)] public float lightRange = 3f;
        public bool lightShadows;

        [Header("Random")]
        public int seed = 1234;
    }
}
