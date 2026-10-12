using UnityEngine;

namespace Defensora.FX
{
    /// <summary>
    /// VFX test rig only (not gameplay): in Play Mode it fires the hit effect at this
    /// object's position on a loop so you can tune the look without any gun code.
    /// The effect sprays along this object's blue (forward) axis.
    /// </summary>
    public class HitEffectPreviewer : MonoBehaviour
    {
        [SerializeField] private HitEffect effectPrefab;
        [SerializeField, Min(0.1f)] private float interval = 1.2f;
        [Tooltip("Random scatter of the hit point, in metres.")]
        [SerializeField, Min(0f)] private float scatter = 0.25f;
        [Tooltip("Random wobble of the surface normal, in degrees.")]
        [SerializeField, Range(0f, 60f)] private float normalJitter = 20f;

        private float _timer;

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = interval;
                Fire();
            }
        }

        private void Fire()
        {
            Vector3 position = transform.position + Random.insideUnitSphere * scatter;
            Quaternion wobble = Quaternion.Euler(
                Random.Range(-normalJitter, normalJitter),
                Random.Range(-normalJitter, normalJitter),
                0f);
            HitEffect.Spawn(effectPrefab, position, transform.rotation * wobble * Vector3.forward);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.05f, scatter));
            Gizmos.DrawRay(transform.position, transform.forward * 0.6f);
        }
    }
}
