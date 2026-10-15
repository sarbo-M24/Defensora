using UnityEngine;

namespace Defensora.FX
{
    /// <summary>
    /// Settings for scattering bouquets around the altar (a ring at the mother's feet plus a
    /// border outside the altar's candles), around the lanterns and along the houses' walls.
    /// Each bouquet leans slightly towards what it surrounds. Placement is editor-only: use
    /// the Scatter / Clear buttons (or Tools/Defensora FX/7. Scatter Bouquets In Open Scene).
    /// </summary>
    public class BouquetScatter : MonoBehaviour
    {
        [Header("Prefabs")]
        [Tooltip("Picked at random for each bouquet.")]
        public GameObject[] bouquetPrefabs;

        [Header("Altar")]
        [Tooltip("The altar platform. Its bounds set the outer border.")]
        public Transform altar;
        [Tooltip("The figure on the altar. The inner ring is centred on it.")]
        public Transform mother;
        [Min(0)] public int bouquetsAtFeet = 7;
        public Vector2 feetRadius = new Vector2(0.45f, 0.6f);
        [Tooltip("Distance outside the altar's edge for the outer border.")]
        [Min(0f)] public float altarBorderOffset = 0.75f;
        [Tooltip("Spacing between bouquets along the altar border.")]
        [Min(0.2f)] public float altarBorderSpacing = 0.55f;

        [Header("Lanterns")]
        public Transform lanternRoot;
        public Vector2Int bouquetsPerLantern = new Vector2Int(2, 3);
        public Vector2 lanternRadius = new Vector2(0.75f, 0.9f);

        [Header("Houses")]
        public string houseNamePrefix = "house";
        [Tooltip("Gap between the wall face and the bouquet's base.")]
        [Min(0f)] public float wallOffset = 0.15f;
        [Tooltip("Chance a plain ground-floor wall piece gets a bouquet.")]
        [Range(0f, 1f)] public float wallChance = 0.45f;
        [Tooltip("Put a bouquet on each side of every door, just outside the door candles.")]
        public bool flankDoors = true;
        [Min(0f)] public float doorFlankDistance = 1.15f;

        [Header("Look")]
        public Vector2 scaleRange = new Vector2(0.8f, 1.15f);
        [Tooltip("How far each bouquet leans towards what it surrounds, in degrees.")]
        public Vector2 leanRange = new Vector2(6f, 16f);

        [Header("Spacing")]
        [Tooltip("Skip a bouquet closer than this to another bouquet.")]
        [Min(0f)] public float minSpacing = 0.35f;
        [Tooltip("Skip a bouquet closer than this to a candle.")]
        [Min(0f)] public float candleClearance = 0.12f;

        [Header("Random")]
        public int seed = 4321;
    }
}
