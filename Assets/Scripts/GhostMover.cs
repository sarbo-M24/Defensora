using System;
using UnityEngine;

/// <summary>
/// Flying enemy movement. Rises straight up from the spawner, curves over at cruise
/// altitude and flies to the goal, then spirals in (and down) once it is within
/// spiral range. Raises <see cref="ReachedGoal"/> and destroys itself on arrival.
///
/// Every value is read each frame, so it can be tuned live in Play mode. To test
/// without a spawner, drop a ghost in the scene and assign Test Goal.
/// </summary>
public class GhostMover : MonoBehaviour
{
    public enum Phase
    {
        Rising,
        Cruising,
        Spiraling,
        Arrived
    }

    [Header("Rise")]
    [SerializeField, Min(0.1f)] private float riseSpeed = 4f;

    [Tooltip("Height the ghost climbs to before heading for the goal.")]
    [SerializeField] private float cruiseAltitude = 6f;

    [Tooltip("On: Cruise Altitude is measured up from the spawn height. Off: it is a world Y value.")]
    [SerializeField] private bool altitudeRelativeToSpawn;

    [Tooltip("Random +/- metres per ghost so a wave spreads out vertically.")]
    [SerializeField, Min(0f)] private float altitudeJitter = 0.75f;

    [Header("Cruise")]
    [SerializeField, Min(0.1f)] private float cruiseSpeed = 3.5f;

    [Tooltip("How fast the ghost changes speed, in m/s per second.")]
    [SerializeField, Min(0.1f)] private float acceleration = 6f;

    [Tooltip("How sharply it can change direction, in degrees per second. Lower = wider curve from rising into cruising.")]
    [SerializeField, Min(1f)] private float turnRate = 140f;

    [Tooltip("How hard it corrects back to cruise altitude (vertical m/s per metre off).")]
    [SerializeField, Min(0f)] private float altitudeHoldStrength = 2f;

    [Tooltip("Fly over the spawner's waypoints on the way. Off: straight line to the goal.")]
    [SerializeField] private bool followWaypoints;

    [Tooltip("Horizontal distance at which a waypoint counts as passed.")]
    [SerializeField, Min(0.1f)] private float waypointArriveDistance = 1.5f;

    [Header("Spiral")]
    [Tooltip("Horizontal distance from the goal where the spiral starts. 0 = no spiral, fly straight in.")]
    [SerializeField, Min(0f)] private float spiralStartDistance = 8f;

    [Tooltip("Orbit speed when the spiral starts, in degrees per second.")]
    [SerializeField, Min(0f)] private float spiralAngularSpeed = 90f;

    [Tooltip("How much the orbit speeds up as the radius shrinks. 0 = constant degrees/s, 1 = constant m/s (spins up a lot near the centre).")]
    [SerializeField, Range(0f, 1.5f)] private float spiralTightening = 0.6f;

    [SerializeField, Min(0f)] private float maxSpiralAngularSpeed = 540f;

    [Tooltip("How fast the radius shrinks, in m/s. Lower = more loops.")]
    [SerializeField, Min(0.05f)] private float spiralInwardSpeed = 1.2f;

    [Tooltip("Seconds to blend from straight flight into the full spiral.")]
    [SerializeField, Min(0f)] private float spiralBlendTime = 0.75f;

    [SerializeField] private bool clockwise;
    [SerializeField] private bool randomizeDirection = true;

    [Tooltip("Height above the goal where the spiral ends.")]
    [SerializeField] private float spiralEndHeight = 1f;

    [Tooltip("Shape of the descent during the spiral. 1 = linear, >1 = stays high then drops, <1 = drops early.")]
    [SerializeField, Range(0.2f, 5f)] private float descentEase = 1.5f;

    [Tooltip("Radius at which the ghost counts as having reached the goal.")]
    [SerializeField, Min(0.05f)] private float arriveDistance = 0.4f;

    [Header("Hover")]
    [Tooltip("Visual child that bobs. The root (collider) stays on the flight path.")]
    [SerializeField] private Transform visual;
    [SerializeField, Min(0f)] private float bobHeight = 0.15f;
    [SerializeField, Min(0f)] private float bobSpeed = 2f;
    [SerializeField, Min(0f)] private float faceTurnSpeed = 8f;

    [Header("Testing")]
    [Tooltip("Used when nothing calls SetPath (e.g. a ghost placed directly in the scene).")]
    [SerializeField] private Transform testGoal;
    [Tooltip("When testing without a spawner: restart from the spawn point on arrival instead of being destroyed.")]
    [SerializeField] private bool loopWhenTesting = true;
    [SerializeField] private bool drawGizmos = true;

    private Transform[] waypoints;
    private int waypointIndex;
    private bool usingTestGoal;

    private Phase phase;
    private Vector3 velocity;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float jitter;
    private float bobPhase;
    private Vector3 visualBasePosition;

    // Spiral state (polar around the goal).
    private float spiralTime;
    private float spiralRadius;
    private float spiralStartRadius;
    private float spiralAngle;
    private float spiralStartY;
    private float spiralEntrySpeed;
    private float spiralSign;

    public Phase CurrentPhase => phase;
    public Vector3 Velocity => velocity;

    public float CruiseSpeed
    {
        get => cruiseSpeed;
        set => cruiseSpeed = Mathf.Max(0.1f, value);
    }

    /// <summary>Raised when the ghost reaches the goal.</summary>
    public event Action<GhostMover> ReachedGoal;

    /// <summary>Altitude this ghost cruises at, including its random jitter.</summary>
    public float TargetAltitude =>
        (altitudeRelativeToSpawn ? spawnPosition.y + cruiseAltitude : cruiseAltitude) + jitter;

    private Transform Goal => waypoints != null && waypoints.Length > 0 ? waypoints[waypoints.Length - 1] : null;

    private void Awake()
    {
        if (visual != null)
            visualBasePosition = visual.localPosition;

        bobPhase = UnityEngine.Random.value * 100f;
        ResetFlight();
    }

    private void Start()
    {
        if (waypoints == null && testGoal != null)
        {
            usingTestGoal = true;
            waypoints = new[] { testGoal };
        }
    }

    public void SetPath(Transform[] path)
    {
        waypoints = path;
        usingTestGoal = false;
        ResetFlight();
    }

    [ContextMenu("Restart Flight")]
    public void RestartFlight()
    {
        if (Application.isPlaying)
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);

        ResetFlight();
    }

    private void ResetFlight()
    {
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        waypointIndex = 0;
        velocity = Vector3.zero;
        jitter = UnityEngine.Random.Range(-altitudeJitter, altitudeJitter);
        phase = Phase.Rising;

        if (!followWaypoints && waypoints != null && waypoints.Length > 0)
            waypointIndex = waypoints.Length - 1;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (dt <= 0f || phase == Phase.Arrived || waypoints == null || waypoints.Length == 0)
            return;

        if (Goal == null)
            return;

        Vector3 previous = transform.position;

        switch (phase)
        {
            case Phase.Rising:
                UpdateRising(dt);
                break;
            case Phase.Cruising:
                UpdateCruising(dt);
                break;
            case Phase.Spiraling:
                UpdateSpiraling(dt);
                break;
        }

        if (phase == Phase.Spiraling)
            velocity = (transform.position - previous) / dt;

        UpdateFacing(dt);
        UpdateBob();
    }

    private void UpdateRising(float dt)
    {
        velocity = Vector3.MoveTowards(velocity, Vector3.up * riseSpeed, acceleration * dt);
        transform.position += velocity * dt;

        // Start turning early by roughly one turn radius so it curves onto the cruise altitude.
        float turnRadius = cruiseSpeed / (turnRate * Mathf.Deg2Rad);

        if (transform.position.y >= TargetAltitude - turnRadius)
            phase = Phase.Cruising;
    }

    private void UpdateCruising(float dt)
    {
        Transform target = waypoints[waypointIndex];
        bool targetIsGoal = waypointIndex == waypoints.Length - 1;

        if (target == null)
        {
            AdvanceWaypoint();
            return;
        }

        Vector3 position = transform.position;
        Vector3 flat = target.position - position;
        flat.y = 0f;
        float horizontalDistance = flat.magnitude;

        Vector3 desired;

        if (targetIsGoal && spiralStartDistance <= 0f)
        {
            // No spiral: dive straight at the goal.
            Vector3 end = target.position + Vector3.up * spiralEndHeight;
            Vector3 toEnd = end - position;

            if (toEnd.magnitude <= arriveDistance)
            {
                Arrive();
                return;
            }

            desired = toEnd.normalized * cruiseSpeed;
        }
        else
        {
            if (targetIsGoal && horizontalDistance <= spiralStartDistance)
            {
                BeginSpiral();
                UpdateSpiraling(dt);
                return;
            }

            if (!targetIsGoal && horizontalDistance <= waypointArriveDistance)
            {
                AdvanceWaypoint();
                return;
            }

            Vector3 direction = horizontalDistance > 0.001f ? flat / horizontalDistance : transform.forward;
            float climb = Mathf.Clamp((TargetAltitude - position.y) * altitudeHoldStrength, -riseSpeed, riseSpeed);
            desired = direction * cruiseSpeed + Vector3.up * climb;
        }

        velocity = Vector3.RotateTowards(velocity, desired, turnRate * Mathf.Deg2Rad * dt, acceleration * dt);
        transform.position += velocity * dt;
    }

    private void BeginSpiral()
    {
        Vector3 center = Goal.position;
        Vector3 offset = transform.position - center;
        offset.y = 0f;

        phase = Phase.Spiraling;
        spiralTime = 0f;
        spiralRadius = offset.magnitude;
        spiralStartRadius = Mathf.Max(spiralRadius, 0.01f);
        spiralAngle = Mathf.Atan2(offset.z, offset.x);
        spiralStartY = transform.position.y;

        // Keep the inward speed it already had, then ease to the spiral's own.
        Vector3 inward = -offset.normalized;
        spiralEntrySpeed = Mathf.Max(0f, Vector3.Dot(velocity, inward));

        bool cw = randomizeDirection ? UnityEngine.Random.value < 0.5f : clockwise;
        spiralSign = cw ? -1f : 1f;
    }

    private void UpdateSpiraling(float dt)
    {
        spiralTime += dt;

        float blend = spiralBlendTime > 0f ? Mathf.Clamp01(spiralTime / spiralBlendTime) : 1f;
        blend = blend * blend * (3f - 2f * blend);

        float omega = spiralAngularSpeed * Mathf.Pow(spiralStartRadius / Mathf.Max(spiralRadius, 0.25f), spiralTightening);
        omega = Mathf.Min(omega, maxSpiralAngularSpeed);
        spiralAngle += spiralSign * omega * Mathf.Deg2Rad * dt * blend;

        float inwardSpeed = Mathf.Lerp(spiralEntrySpeed, spiralInwardSpeed, blend);
        spiralRadius = Mathf.Max(0f, spiralRadius - inwardSpeed * dt);

        Vector3 center = Goal.position;
        float progress = 1f - spiralRadius / spiralStartRadius;
        float y = Mathf.Lerp(spiralStartY, center.y + spiralEndHeight, Mathf.Pow(progress, descentEase));

        transform.position = new Vector3(
            center.x + Mathf.Cos(spiralAngle) * spiralRadius,
            y,
            center.z + Mathf.Sin(spiralAngle) * spiralRadius
        );

        if (spiralRadius <= arriveDistance)
            Arrive();
    }

    private void AdvanceWaypoint()
    {
        waypointIndex = Mathf.Min(waypointIndex + 1, waypoints.Length - 1);
    }

    private void Arrive()
    {
        if (usingTestGoal && loopWhenTesting)
        {
            RestartFlight();
            return;
        }

        phase = Phase.Arrived;
        ReachedGoal?.Invoke(this);
        Destroy(gameObject);
    }

    private void UpdateFacing(float dt)
    {
        // Face the direction of travel so the trails and the tail stream out behind.
        if (velocity.sqrMagnitude < 0.0001f)
            return;

        Quaternion look = Quaternion.LookRotation(velocity.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-faceTurnSpeed * dt));
    }

    private void UpdateBob()
    {
        if (visual == null || bobHeight <= 0f)
            return;

        float t = Time.time * bobSpeed + bobPhase;
        Vector3 bob = transform.InverseTransformDirection(Vector3.up) * (Mathf.Sin(t) * bobHeight);
        visual.localPosition = visualBasePosition + bob;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Transform goal = Application.isPlaying ? Goal : testGoal;
        Vector3 origin = Application.isPlaying ? spawnPosition : transform.position;
        float altitude = altitudeRelativeToSpawn ? origin.y + cruiseAltitude : cruiseAltitude;

        // Rise column and cruise altitude.
        Gizmos.color = new Color(0.4f, 0.9f, 1f);
        Vector3 top = new Vector3(origin.x, altitude, origin.z);
        Gizmos.DrawLine(origin, top);
        DrawCircle(top, 0.6f);

        if (goal == null)
            return;

        Vector3 goalTop = new Vector3(goal.position.x, altitude, goal.position.z);
        Gizmos.DrawLine(top, goalTop);

        // Spiral range and end point.
        Gizmos.color = new Color(1f, 0.5f, 0.9f);
        DrawCircle(goalTop, spiralStartDistance);
        Vector3 end = goal.position + Vector3.up * spiralEndHeight;
        DrawCircle(end, arriveDistance);
        Gizmos.DrawLine(goalTop, end);
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        const int segments = 48;
        Vector3 previous = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
