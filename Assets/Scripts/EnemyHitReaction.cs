
using Defensora.FX;
using UnityEngine;

/// <summary>
/// Visual feedback for an enemy: plays a pooled HitEffect where it was hit,
/// briefly flashes its renderers, and plays a death effect when it dies.
/// </summary>
[RequireComponent(typeof(TargetHealth))]
public class EnemyHitReaction : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Effects")]
    [SerializeField] private HitEffect hitEffectPrefab;

    [Tooltip("Played at the enemy's centre when it dies. Leave empty for none.")]
    [SerializeField] private HitEffect deathEffectPrefab;

    [Header("Damage Flash")]
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField, Min(0f)] private float flashDuration = 0.08f;

    [Header("Hit Punch")]
    [Tooltip("How much the body squashes on a hit. 0 disables it.")]
    [SerializeField, Range(0f, 0.5f)] private float punchAmount = 0.12f;
    [SerializeField, Min(0.01f)] private float punchDuration = 0.15f;
    [SerializeField] private Transform body;

    private TargetHealth health;
    private Renderer[] renderers;
    private MaterialPropertyBlock propertyBlock;

    private float flashTimer;
    private float punchTimer;
    private Vector3 bodyBaseScale;

    private void Awake()
    {
        health = GetComponent<TargetHealth>();
        renderers = GetComponentsInChildren<Renderer>();
        propertyBlock = new MaterialPropertyBlock();

        if (body == null)
            body = transform;

        bodyBaseScale = body.localScale;
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
    }

    private void OnDamaged(float damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        HitEffect.Spawn(hitEffectPrefab, hitPoint, hitNormal);

        if (flashDuration > 0f)
        {
            flashTimer = flashDuration;
            SetFlash(true);
        }

        if (punchAmount > 0f)
            punchTimer = punchDuration;
    }

    private void OnDied(TargetHealth target)
    {
        if (deathEffectPrefab == null)
            return;

        Collider col = GetComponentInChildren<Collider>();
        Vector3 center = col != null ? col.bounds.center : transform.position;

        HitEffect.Spawn(deathEffectPrefab, center, Vector3.up);
    }

    private void Update()
    {
        if (flashTimer > 0f)
        {
            flashTimer -= Time.deltaTime;

            if (flashTimer <= 0f)
                SetFlash(false);
        }

        if (punchTimer > 0f)
        {
            punchTimer = Mathf.Max(0f, punchTimer - Time.deltaTime);

            // Squash down and out, then ease back to the rest shape.
            float t = punchTimer / punchDuration;
            float squash = punchAmount * Mathf.Sin(t * Mathf.PI);

            body.localScale = Vector3.Scale(
                bodyBaseScale,
                new Vector3(1f + squash, 1f - squash, 1f + squash)
            );
        }
    }

    private void SetFlash(bool on)
    {
        foreach (Renderer rend in renderers)
        {
            if (rend == null)
                continue;

            if (!on)
            {
                rend.SetPropertyBlock(null);
                continue;
            }

            rend.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, flashColor);
            propertyBlock.SetColor(ColorId, flashColor);
            rend.SetPropertyBlock(propertyBlock);
        }
    }
}
