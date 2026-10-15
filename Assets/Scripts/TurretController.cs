
using UnityEngine;

public class TurretController : MonoBehaviour
{
    public enum TurretType
    {
        Bullet,
        Fireball,
        Laser
    }

    [Header("Turret Settings")]
    [SerializeField] private TurretType turretType = TurretType.Bullet;
    [SerializeField, Min(0.1f)] private float detectionRange = 10f;
    [SerializeField, Min(0.1f)] private float fireRate = 3f;
    [SerializeField, Min(0f)] private float damage = 10f;

    [Header("Target Detection")]
    [SerializeField] private LayerMask enemyLayers;
    [SerializeField, Min(0.05f)] private float scanInterval = 0.2f;

    [Header("Projectile Settings")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField, Min(0.1f)] private float projectileSpeed = 20f;
    [SerializeField] private Transform firePoint;

    private TargetHealth currentTarget;

    public TurretType Type => turretType;
    public float DetectionRange => detectionRange;
    public float FireRate => fireRate;
    public float Damage => damage;
    private float nextFireTime;
    private float nextScanTime;

    private void Update()
    {
        if (Time.time >= nextScanTime)
        {
            FindTarget();
            nextScanTime = Time.time + scanInterval;
        }

        if (currentTarget == null || currentTarget.IsDestroyed)
            return;

        Vector3 origin = GetAttackOrigin();
        Vector3 targetPosition = currentTarget.transform.position;

        if (Vector3.Distance(origin, targetPosition) > detectionRange)
        {
            currentTarget = null;
            return;
        }

        if (Time.time >= nextFireTime)
        {
            AttackTarget(origin, targetPosition);
            nextFireTime = Time.time + (1f / fireRate);
        }
    }

    private Vector3 GetAttackOrigin()
    {
        return firePoint != null
            ? firePoint.position
            : transform.position + Vector3.up * 0.5f;
    }

    private void FindTarget()
    {
        currentTarget = null;

        Collider[] nearbyColliders = Physics.OverlapSphere(
            transform.position,
            detectionRange,
            enemyLayers,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.MaxValue;

        foreach (Collider nearbyCollider in nearbyColliders)
        {
            TargetHealth health =
                nearbyCollider.GetComponentInParent<TargetHealth>();

            if (health == null || health.IsDestroyed)
                continue;

            float distance = Vector3.Distance(
                transform.position,
                health.transform.position
            );

            if (distance < closestDistance)
            {
                closestDistance = distance;
                currentTarget = health;
            }
        }
    }

    private void AttackTarget(Vector3 origin, Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - origin).normalized;

        switch (turretType)
        {
            case TurretType.Bullet:
            case TurretType.Fireball:
                FireProjectile(origin, direction);
                break;

            case TurretType.Laser:
                FireLaser(origin, direction);
                break;
        }
    }

    private void FireProjectile(Vector3 origin, Vector3 direction)
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning(
                $"{name}: Assign a projectile prefab.",
                this
            );
            return;
        }

        GameObject projectile = Instantiate(
            projectilePrefab,
            origin,
            Quaternion.LookRotation(direction)
        );

        TurretProjectile projectileScript =
            projectile.GetComponent<TurretProjectile>();

        if (projectileScript == null)
        {
            Debug.LogError(
                $"{name}: Projectile prefab needs TurretProjectile.",
                projectile
            );

            Destroy(projectile);
            return;
        }

        projectileScript.Initialize(
            gameObject,
            direction,
            damage,
            projectileSpeed
        );
    }

    private void FireLaser(Vector3 origin, Vector3 direction)
    {
        if (Physics.Raycast(
            origin,
            direction,
            out RaycastHit hit,
            detectionRange,
            enemyLayers,
            QueryTriggerInteraction.Ignore))
        {
            TargetHealth health =
                hit.collider.GetComponentInParent<TargetHealth>();

            if (health != null && !health.IsDestroyed)
                health.TakeDamage(damage, hit.point, hit.normal);
        }

        Debug.DrawRay(
            origin,
            direction * detectionRange,
            Color.red,
            0.15f
        );
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
