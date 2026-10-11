
using UnityEngine;

public class TargetHealth : MonoBehaviour
{
    [Header("Health Settings")]
    [SerializeField, Min(1f)]
    private float maxHealth = 100f;

    private float currentHealth;
    private bool isDestroyed;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDestroyed => isDestroyed;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage)
    {
        if (isDestroyed || damage <= 0f)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - damage);

        Debug.Log(
            $"{gameObject.name} took {damage} damage. " +
            $"Health: {currentHealth}/{maxHealth}"
        );

        if (currentHealth <= 0f)
        {
            isDestroyed = true;

            Debug.Log($"{gameObject.name} destroyed!");

            Destroy(gameObject);
        }
    }
}
