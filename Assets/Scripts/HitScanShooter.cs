
using UnityEngine;

public class HitscanShooter : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Camera playerCamera;

    [Header("Performance")]
    [SerializeField, Min(1)] private int hitBufferSize = 16;

    private RaycastHit[] hitBuffer;
    private Transform playerRoot;

    private void Awake()
    {
        playerRoot = transform.root;

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

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

            int hitCount = Physics.RaycastNonAlloc(
                origin,
                direction,
                hitBuffer,
                range,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            // Process hits from nearest to farthest without sorting.
            float nearestDistance = float.MaxValue;
            int nearestIndex = -1;

            for (int j = 0; j < hitCount; j++)
            {
                Transform hitTransform = hitBuffer[j].transform;

                if (hitTransform == null)
                    continue;

                // Ignore the player's own colliders.
                if (hitTransform.root == playerRoot)
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
                {
                    target.TakeDamage(damage);
                }

                // Uncomment temporarily when debugging hit detection:
                // Debug.Log("Shot hit: " + hit.collider.name);
            }
            else if (hitCount >= hitBuffer.Length)
            {
                // A full buffer may mean some hits were omitted.
                Debug.LogWarning(
                    "HitscanShooter: Hit buffer full. Consider increasing Hit Buffer Size.",
                    this
                );
            }

            // Draw rays only in the Editor when debugging.
            Debug.DrawRay(origin, direction * range, Color.red, 0.1f);
        }
    }
}
