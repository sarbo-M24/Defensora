
using UnityEngine;

public class HitscanShooter : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Camera playerCamera;

    [Header("Performance")]
    [SerializeField, Min(1)] private int hitBufferSize = 16;

    [Header("Projectile Mode")]
    [SerializeField] private bool useProjectileShots = false;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField, Min(0.1f)] private float bulletSpeed = 80f;

    private RaycastHit[] hitBuffer;
    private Transform playerRoot;

    private void Awake()
    {
        playerRoot = transform.root;

        if (playerCamera == null)
            playerCamera = Camera.main;

        hitBuffer = new RaycastHit[Mathf.Max(1, hitBufferSize)];

        if (playerCamera == null)
        {
            Debug.LogError(
                "HitscanShooter: Assign the Player Camera in the Inspector.",
                this
            );
        }
    }

    public void Fire(
        int pellets,
        float damage,
        float range,
        float spreadAngle)
    {
        if (playerCamera == null)
            return;

        if (useProjectileShots && bulletPrefab == null)
        {
            Debug.LogWarning(
                "HitscanShooter: Projectile mode is enabled, " +
                "but no bullet prefab is assigned.",
                this
            );
            return;
        }

        pellets = Mathf.Max(1, pellets);
        range = Mathf.Max(0.1f, range);
        damage = Mathf.Max(0f, damage);
        spreadAngle = Mathf.Clamp(spreadAngle, 0f, 45f);

        Transform cameraTransform = playerCamera.transform;
        Vector3 origin = cameraTransform.position;
        Vector3 forward = cameraTransform.forward;

        for (int i = 0; i < pellets; i++)
        {
            Vector3 direction = forward;

            if (spreadAngle > 0f)
            {
                direction = Quaternion.Euler(
                    Random.Range(-spreadAngle, spreadAngle),
                    Random.Range(-spreadAngle, spreadAngle),
                    0f
                ) * forward;
            }

            if (useProjectileShots)
            {
                FireProjectile(origin, direction, damage);
                continue;
            }

            int hitCount = Physics.RaycastNonAlloc(
                origin,
                direction,
                hitBuffer,
                range,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            float nearestDistance = float.MaxValue;
            int nearestIndex = -1;

            for (int j = 0; j < hitCount; j++)
            {
                Transform hitTransform = hitBuffer[j].transform;

                if (hitTransform == null ||
                    hitTransform.root == playerRoot)
                    continue;

                if (hitBuffer[j].distance < nearestDistance)
                {
                    nearestDistance = hitBuffer[j].distance;
                    nearestIndex = j;
                }
            }

            if (nearestIndex >= 0)
            {
                RaycastHit hit = hitBuffer[nearestIndex];

                TargetHealth target =
                    hit.collider.GetComponentInParent<TargetHealth>();

                if (target != null)
                    target.TakeDamage(damage);
            }
            else if (hitCount >= hitBuffer.Length)
            {
                Debug.LogWarning(
                    "HitscanShooter: Hit buffer full. Increase Hit Buffer Size.",
                    this
                );
            }

            Debug.DrawRay(origin, direction * range, Color.red, 0.1f);
        }
    }

    private void FireProjectile(
        Vector3 origin,
        Vector3 direction,
        float damage)
    {
        GameObject bullet = Instantiate(
            bulletPrefab,
            origin,
            Quaternion.LookRotation(direction)
        );

        TurretProjectile projectile =
            bullet.GetComponent<TurretProjectile>();

        if (projectile == null)
        {
            Debug.LogError(
                "The bullet prefab needs the TurretProjectile component.",
                bullet
            );

            Destroy(bullet);
            return;
        }

        projectile.Initialize(
            playerRoot.gameObject,
            direction,
            damage,
            bulletSpeed
        );
    }
}
