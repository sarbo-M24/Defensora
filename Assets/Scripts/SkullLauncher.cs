using Defensora.FX;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keeps a <see cref="FloatingSkull"/> hovering at a hold point, fires it at the crosshair on
/// left click, then summons a fresh one with a swirl effect.
/// </summary>
public class SkullLauncher : MonoBehaviour
{
    [Header("Skull")]
    [SerializeField] private FloatingSkull skullPrefab;
    [Tooltip("Optional skull already placed in the scene; used as the first one.")]
    [SerializeField] private FloatingSkull startingSkull;
    [SerializeField] private Transform holdPoint;
    [Tooltip("The skull never collides with colliders under this (the player).")]
    [SerializeField] private Transform ignoreCollisionsWith;

    [Header("Shooting")]
    [SerializeField] private Camera aimCamera;
    [SerializeField, Min(0.1f)] private float launchSpeed = 40f;
    [SerializeField, Min(1f)] private float aimRange = 200f;
    [SerializeField, Min(0f)] private float respawnDelay = 0.35f;

    [Header("Effects")]
    [SerializeField] private GameObject summonEffectPrefab;
    [SerializeField] private HitEffect impactEffectPrefab;

    private readonly RaycastHit[] aimHits = new RaycastHit[16];
    private FloatingSkull current;
    private float respawnTimer = -1f;

    private void Start()
    {
        if (aimCamera == null)
            aimCamera = Camera.main;

        if (startingSkull != null)
        {
            current = startingSkull;
            current.IgnoreCollisionsWith(ignoreCollisionsWith);
            current.Hold(holdPoint, summon: false);
        }
        else
        {
            Summon();
        }
    }

    private void Update()
    {
        if (respawnTimer >= 0f)
        {
            respawnTimer -= Time.deltaTime;
            if (respawnTimer < 0f)
                Summon();
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && current != null && current.IsReady)
            Fire();
    }

    private void Fire()
    {
        Vector3 origin = current.transform.position;
        Vector3 direction = (AimPoint() - origin).normalized;

        current.Launch(direction, launchSpeed, impactEffectPrefab);
        current = null;
        respawnTimer = respawnDelay;
    }

    /// <summary>What the crosshair is over, ignoring the player and the held skull.</summary>
    private Vector3 AimPoint()
    {
        Ray ray = new Ray(aimCamera.transform.position, aimCamera.transform.forward);
        int count = Physics.RaycastNonAlloc(ray, aimHits, aimRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float nearest = aimRange;
        for (int i = 0; i < count; i++)
        {
            Transform hit = aimHits[i].transform;
            if (hit.IsChildOf(current.transform) ||
                (ignoreCollisionsWith != null && hit.IsChildOf(ignoreCollisionsWith)))
                continue;

            nearest = Mathf.Min(nearest, aimHits[i].distance);
        }

        return ray.GetPoint(nearest);
    }

    private void Summon()
    {
        respawnTimer = -1f;
        if (skullPrefab == null || holdPoint == null)
            return;

        current = Instantiate(skullPrefab, holdPoint.position, holdPoint.rotation);
        current.IgnoreCollisionsWith(ignoreCollisionsWith);
        current.Hold(holdPoint, summon: true);

        // Face the swirl towards the camera so it reads as a vortex around the skull.
        if (summonEffectPrefab != null)
            Instantiate(summonEffectPrefab, holdPoint.position, aimCamera.transform.rotation, holdPoint);
    }
}
