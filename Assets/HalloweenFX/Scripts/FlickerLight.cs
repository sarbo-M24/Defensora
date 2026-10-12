using UnityEngine;

namespace Defensora.FX
{
    /// <summary>
    /// Organic flame flicker for a point light (candle, oil lamp, lantern).
    /// Drop the Lamp / Candle prefab onto any model; the light flickers in intensity,
    /// colour and range, occasionally "gutters" (a short dip as if caught by a draught)
    /// and can jitter / sway its position so shadows dance.
    /// </summary>
    [RequireComponent(typeof(Light))]
    [DisallowMultipleComponent]
    public class FlickerLight : MonoBehaviour
    {
        [Header("Intensity")]
        [SerializeField, Min(0f)] private float baseIntensity = 2.5f;
        [SerializeField, Range(0f, 1f)] private float flickerAmount = 0.3f;
        [SerializeField, Min(0f)] private float flickerSpeed = 8f;

        [Header("Gutter (draught dips)")]
        [Tooltip("Average dips per second.")]
        [SerializeField, Min(0f)] private float gutterRate = 0.08f;
        [SerializeField, Range(0f, 1f)] private float gutterDepth = 0.55f;
        [SerializeField, Min(0.01f)] private float gutterDuration = 0.35f;

        [Header("Colour")]
        [Tooltip("Colour when the flame is at its dimmest.")]
        [SerializeField] private Color dimColor = new Color(1f, 0.38f, 0.1f);
        [Tooltip("Colour when the flame is at its brightest.")]
        [SerializeField] private Color brightColor = new Color(1f, 0.68f, 0.32f);

        [Header("Range")]
        [SerializeField, Range(0f, 0.5f)] private float rangeVariation = 0.06f;

        [Header("Motion")]
        [Tooltip("Random positional jitter in metres (moves the shadows).")]
        [SerializeField, Min(0f)] private float positionJitter = 0.012f;
        [Tooltip("Pendulum sway in metres, for hanging lanterns.")]
        [SerializeField, Min(0f)] private float swayAmount;
        [SerializeField, Min(0f)] private float swaySpeed = 0.8f;

        private Light _light;
        private Vector3 _origin;
        private float _baseRange;
        private float _seed;
        private float _gutterTimer;

        public float BaseIntensity
        {
            get => baseIntensity;
            set => baseIntensity = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            _light = GetComponent<Light>();
            _seed = Random.value * 100f;
        }

        private void OnEnable()
        {
            _origin = transform.localPosition;
            if (_light != null)
                _baseRange = _light.range;
        }

        private void OnDisable()
        {
            transform.localPosition = _origin;
            _light.intensity = baseIntensity;
            _light.range = _baseRange;
        }

        private void Update()
        {
            float t = Time.time;

            // Two octaves of noise: slow breathing + fast lick of the flame.
            float slow = Mathf.PerlinNoise(_seed, t * flickerSpeed * 0.25f);
            float fast = Mathf.PerlinNoise(_seed + 17.3f, t * flickerSpeed);
            float flame = Mathf.Clamp01(slow * 0.6f + fast * 0.4f);

            if (_gutterTimer <= 0f && Random.value < gutterRate * Time.deltaTime)
                _gutterTimer = gutterDuration;

            float gutter = 1f;
            if (_gutterTimer > 0f)
            {
                _gutterTimer -= Time.deltaTime;
                float k = 1f - _gutterTimer / gutterDuration;      // 0 -> 1 across the dip
                gutter = 1f - gutterDepth * Mathf.Sin(k * Mathf.PI);
            }

            float brightness = (1f - flickerAmount + flickerAmount * flame) * gutter;
            _light.intensity = baseIntensity * brightness;
            _light.color = Color.Lerp(dimColor, brightColor, brightness);
            _light.range = _baseRange * (1f + rangeVariation * (flame - 0.5f) * 2f);

            Vector3 offset = Vector3.zero;
            if (positionJitter > 0f)
            {
                offset += new Vector3(
                    Mathf.PerlinNoise(_seed + 3.1f, t * flickerSpeed * 0.5f) - 0.5f,
                    Mathf.PerlinNoise(_seed + 7.7f, t * flickerSpeed * 0.5f) - 0.5f,
                    Mathf.PerlinNoise(_seed + 11.9f, t * flickerSpeed * 0.5f) - 0.5f) * (positionJitter * 2f);
            }
            if (swayAmount > 0f)
            {
                offset += new Vector3(
                    Mathf.Sin(t * swaySpeed * Mathf.PI * 2f + _seed),
                    0f,
                    Mathf.Sin(t * swaySpeed * 1.37f * Mathf.PI * 2f + _seed * 0.5f) * 0.6f) * swayAmount;
            }
            transform.localPosition = _origin + offset;
        }
    }
}
