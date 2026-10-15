
using System;
using UnityEngine;

public class TargetHealth : MonoBehaviour
{
    [Header("Health Settings")]
    [SerializeField, Min(1f)]
    private float maxHealth = 100f;

    private float currentHealth;
    private bool isDestroyed;
    private Collider mainCollider;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDestroyed => isDestroyed;

    /// <summary>Raised on every hit: damage, hit point, hit normal.</summary>
    public event Action<float, Vector3, Vector3> Damaged;

    /// <summary>Raised once, right before the object is destroyed.</summary>
    public event Action<TargetHealth> Died;

    private void Awake()
    {
        currentHealth = maxHealth;
        mainCollider = GetComponentInChildren<Collider>();
    }

    public void TakeDamage(float damage)
    {
        // No hit point given: use the centre of the target.
        Vector3 center = mainCollider != null
            ? mainCollider.bounds.center
            : transform.position;

        TakeDamage(damage, center, Vector3.up);
    }

    public void TakeDamage(
        float damage,
        Vector3 hitPoint,
        Vector3 hitNormal)
    {
        if (isDestroyed || damage <= 0f)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - damage);

        Debug.Log(
            $"{gameObject.name} took {damage} damage. " +
            $"Health: {currentHealth}/{maxHealth}"
        );

        Damaged?.Invoke(damage, hitPoint, hitNormal);

        if (currentHealth <= 0f)
        {
            isDestroyed = true;

            Debug.Log($"{gameObject.name} destroyed!");

            Died?.Invoke(this);

            Destroy(gameObject);
        }
    }
}
