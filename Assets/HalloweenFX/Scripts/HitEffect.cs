using System.Collections.Generic;
using UnityEngine;

namespace Defensora.FX
{
    /// <summary>
    /// Bullet-impact effect: a burst of particle systems plus a short coloured light flash.
    /// Instances are pooled per prefab, so spawn them with <see cref="Spawn"/> rather than Instantiate.
    ///
    /// Usage from gun code (not included here):
    ///     HitEffect.Spawn(confettiHitPrefab, hit.point, hit.normal);
    /// </summary>
    [DisallowMultipleComponent]
    public class HitEffect : MonoBehaviour
    {
        [Header("Light Flash")]
        [SerializeField] private Light flashLight;
        [SerializeField, Min(0f)] private float flashIntensity = 18f;
        [SerializeField, Min(0.01f)] private float flashDuration = 0.14f;
        [SerializeField] private AnimationCurve flashCurve = new AnimationCurve(
            new Keyframe(0f, 1f, 0f, -6f),
            new Keyframe(1f, 0f, 0f, 0f));
        [Tooltip("Each hit picks one of these for the flash light. Leave empty to keep the light's colour.")]
        [SerializeField] private Color[] flashPalette =
        {
            new Color(1f, 0.12f, 0.56f),  // rosa mexicano
            new Color(1f, 0.62f, 0.11f),  // marigold
            new Color(1f, 0.88f, 0.1f),   // sun yellow
            new Color(0.1f, 0.9f, 0.84f), // turquoise
            new Color(0.56f, 0.23f, 1f),  // violet
        };

        [Header("Lifetime")]
        [Tooltip("Seconds before the effect returns to the pool. Should cover the longest particle.")]
        [SerializeField, Min(0.1f)] private float lifetime = 2.8f;

        private static readonly Dictionary<HitEffect, Stack<HitEffect>> Pools = new Dictionary<HitEffect, Stack<HitEffect>>();

        private ParticleSystem[] _systems;
        private HitEffect _sourcePrefab;
        private float _age;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPools() => Pools.Clear();

        /// <summary>Plays a pooled copy of <paramref name="prefab"/> at a surface hit.</summary>
        /// <param name="normal">Surface normal; the burst sprays out along it.</param>
        public static HitEffect Spawn(HitEffect prefab, Vector3 position, Vector3 normal)
        {
            if (prefab == null)
                return null;

            if (!Pools.TryGetValue(prefab, out var pool))
            {
                pool = new Stack<HitEffect>();
                Pools.Add(prefab, pool);
            }

            HitEffect effect = null;
            while (pool.Count > 0 && effect == null)
                effect = pool.Pop(); // skips instances destroyed by a scene unload

            if (effect == null)
            {
                effect = Instantiate(prefab);
                effect._sourcePrefab = prefab;
            }

            effect.Play(position, normal);
            return effect;
        }

        private void Awake()
        {
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            if (flashLight != null)
                flashLight.enabled = false;
        }

        /// <summary>Restarts the effect at a position, facing along <paramref name="normal"/>.</summary>
        public void Play(Vector3 position, Vector3 normal)
        {
            if (normal.sqrMagnitude < 1e-6f)
                normal = Vector3.up;

            transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal));
            gameObject.SetActive(true);
            _age = 0f;

            foreach (var system in _systems)
            {
                system.Clear(false);
                system.Play(false);
            }

            if (flashLight != null)
            {
                if (flashPalette != null && flashPalette.Length > 0)
                    flashLight.color = flashPalette[Random.Range(0, flashPalette.Length)];
                flashLight.intensity = flashIntensity * flashCurve.Evaluate(0f);
                flashLight.enabled = true;
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;

            if (flashLight != null && flashLight.enabled)
            {
                float k = _age / flashDuration;
                if (k >= 1f)
                    flashLight.enabled = false;
                else
                    flashLight.intensity = flashIntensity * flashCurve.Evaluate(k);
            }

            if (_age >= lifetime)
                Release();
        }

        private void Release()
        {
            if (_sourcePrefab == null)
            {
                // Placed directly in a scene rather than spawned: just stop.
                enabled = false;
                return;
            }

            gameObject.SetActive(false);
            if (Pools.TryGetValue(_sourcePrefab, out var pool))
                pool.Push(this);
            else
                Destroy(gameObject);
        }

        private void OnEnable() => _age = 0f;

        [ContextMenu("Preview Hit (Play Mode)")]
        private void PreviewHit()
        {
            if (Application.isPlaying)
            {
                enabled = true;
                Play(transform.position, transform.forward);
            }
        }
    }
}
