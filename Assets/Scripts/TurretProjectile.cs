
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class TurretProjectile : MonoBehaviour
{
    [Header("Projectile Settings")]
    [SerializeField, Min(0.1f)] private float lifetime = 5f;

    private GameObject owner;
    private float damage;
    private bool initialized;
    private bool hasHit;

    public void Initialize(
        GameObject projectileOwner,
        Vector3 direction,
        float projectileDamage,
        float speed)
    {
        owner = projectileOwner;
        damage = Mathf.Max(0f, projectileDamage);
        initialized = true;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;

        rb.linearVelocity = direction.normalized * speed;

        Collider[] projectileColliders =
            GetComponentsInChildren<Collider>();

        Collider[] ownerColliders =
            owner.GetComponentsInChildren<Collider>();

        foreach (Collider projectileCollider in projectileColliders)
        {
            foreach (Collider ownerCollider in ownerColliders)
            {
                if (projectileCollider != ownerCollider)
                {
                    Physics.IgnoreCollision(
                        projectileCollider,
                        ownerCollider
                    );
                }
            }
        }

        Destroy(gameObject, lifetime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        Vector3 point = transform.position;
        Vector3 normal = -transform.forward;

        if (collision.contactCount > 0)
        {
            ContactPoint contact = collision.GetContact(0);
            point = contact.point;
            normal = contact.normal;
        }

        HandleHit(collision.collider, point, normal);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleHit(
            other,
            other.ClosestPoint(transform.position),
            -transform.forward
        );
    }

    private void HandleHit(
        Collider other,
        Vector3 hitPoint,
        Vector3 hitNormal)
    {
        if (!initialized || hasHit)
            return;

        if (owner != null &&
            other.transform.root == owner.transform.root)
            return;

        hasHit = true;

        TargetHealth target =
            other.GetComponentInParent<TargetHealth>();

        if (target != null && !target.IsDestroyed)
            target.TakeDamage(damage, hitPoint, hitNormal);

        Destroy(gameObject);
    }
}
