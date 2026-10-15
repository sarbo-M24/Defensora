using Defensora.FX;
using UnityEngine;

/// <summary>
/// Physics-driven skull that hovers at a hold point (spring forces, so it sways, lags and
/// bumps into things), chatters its jaw, and can be launched as a projectile that bursts
/// into an impact effect on whatever it hits.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FloatingSkull : MonoBehaviour
{
    private enum State { Idle, Summoning, Floating, Flying }

    [Header("Jaw")]
    [SerializeField] private Transform jaw;
    [Tooltip("Hinge point in the skull's local space. The jaw swivels around the local X axis through it.")]
    [SerializeField] private Vector3 jawHinge = new Vector3(0f, -0.15f, 0.05f);
    [SerializeField] private float jawRestAngle = 3f;
    [SerializeField] private float jawChatterAngle = 12f;
    [SerializeField] private float jawChatterSpeed = 2.5f;
    [SerializeField] private float jawFlyingAngle = 35f;
    [SerializeField] private float jawChompSpeed = 16f;
    [SerializeField, Min(0.01f)] private float jawSmoothing = 18f;

    [Header("Floating")]
    [Tooltip("Spring stiffness pulling the skull to its hold point.")]
    [SerializeField, Min(0f)] private float followStrength = 160f;
    [Tooltip("Fraction of critical damping. Below 1 overshoots a little, which reads as floaty.")]
    [SerializeField, Range(0f, 2f)] private float followDamping = 0.65f;
    [SerializeField, Min(0f)] private float turnStrength = 90f;
    [SerializeField, Range(0f, 2f)] private float turnDamping = 0.6f;
    [SerializeField] private float bobHeight = 0.025f;
    [SerializeField] private float bobSpeed = 1.5f;
    [SerializeField] private float wobbleAngle = 7f;
    [Tooltip("Snap back to the hold point if knocked further away than this.")]
    [SerializeField, Min(0.1f)] private float maxDistance = 2f;

    [Header("Flying")]
    [SerializeField, Min(0f)] private float damage = 25f;
    [SerializeField, Min(0.1f)] private float maxFlightTime = 5f;

    [Header("Summoning")]
    [SerializeField, Min(0.01f)] private float summonDuration = 0.7f;
    [SerializeField] private AnimationCurve summonScale = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.7f, 1.12f, 0f, 0f),
        new Keyframe(1f, 1f, 0f, 0f));

    public bool IsReady => state == State.Floating;

    private Rigidbody body;
    private Transform holdPoint;
    private Vector3 lastHoldPosition;
    private Vector3 holdVelocity;
    private Vector3 fullScale;
    private Vector3 jawBasePosition;
    private Quaternion jawBaseRotation;
    private float jawAngle;
    private float stateTime;
    private float phase;
    private State state = State.Idle;
    private HitEffect impactEffect;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.maxAngularVelocity = 50f;

        fullScale = transform.localScale;
        phase = Random.value * 100f;

        if (jaw != null)
        {
            jawBasePosition = jaw.localPosition;
            jawBaseRotation = jaw.localRotation;
        }
    }

    /// <summary>Starts hovering at <paramref name="point"/>, optionally growing in first.</summary>
    public void Hold(Transform point, bool summon)
    {
        holdPoint = point;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(point.position, point.rotation);
        lastHoldPosition = point.position;
        holdVelocity = Vector3.zero;

        body.isKinematic = summon;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        SetState(summon ? State.Summoning : State.Floating);
        transform.localScale = summon ? Vector3.zero : fullScale;
    }

    /// <summary>Fires the skull along <paramref name="direction"/>.</summary>
    public void Launch(Vector3 direction, float speed, HitEffect onImpact)
    {
        impactEffect = onImpact;
        holdPoint = null;
        transform.localScale = fullScale;

        body.isKinematic = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.rotation = Quaternion.LookRotation(direction);
        body.linearVelocity = direction * speed;
        body.angularVelocity = Vector3.zero;

        SetState(State.Flying);
    }

    /// <summary>Stops the skull from colliding with anything under <paramref name="root"/>.</summary>
    public void IgnoreCollisionsWith(Transform root)
    {
        if (root == null)
            return;

        var own = GetComponentsInChildren<Collider>();
        foreach (var other in root.GetComponentsInChildren<Collider>())
            foreach (var mine in own)
                Physics.IgnoreCollision(mine, other);
    }

    private void SetState(State next)
    {
        state = next;
        stateTime = 0f;
    }

    private void Update()
    {
        stateTime += Time.deltaTime;

        if (holdPoint != null && Time.deltaTime > 0f)
        {
            // The hold point moves in Update (camera), so measure its velocity per frame.
            Vector3 measured = (holdPoint.position - lastHoldPosition) / Time.deltaTime;
            holdVelocity = Vector3.Lerp(holdVelocity, measured, 0.5f);
            lastHoldPosition = holdPoint.position;
        }

        switch (state)
        {
            case State.Summoning:
                float k = Mathf.Clamp01(stateTime / summonDuration);
                transform.localScale = fullScale * summonScale.Evaluate(k);
                if (k >= 1f)
                {
                    transform.localScale = fullScale;
                    body.isKinematic = false;
                    SetState(State.Floating);
                }
                break;

            case State.Flying:
                if (stateTime >= maxFlightTime)
                    Burst(transform.position, -body.linearVelocity, null);
                break;
        }

        UpdateJaw();
    }

    private void FixedUpdate()
    {
        if (holdPoint == null)
            return;

        float t = Time.time * bobSpeed + phase;
        Vector3 targetPosition = holdPoint.position
            + holdPoint.up * (Mathf.Sin(t) * bobHeight)
            + holdPoint.right * (Mathf.Sin(t * 0.63f) * bobHeight * 0.5f);
        Quaternion targetRotation = holdPoint.rotation * Quaternion.Euler(
            Mathf.Sin(t * 0.8f) * wobbleAngle,
            Mathf.Sin(t * 0.55f + 1.3f) * wobbleAngle,
            Mathf.Sin(t * 0.7f + 2.1f) * wobbleAngle * 0.5f);

        if (state == State.Summoning)
        {
            body.MovePosition(targetPosition);
            body.MoveRotation(targetRotation);
            return;
        }

        if (state != State.Floating)
            return;

        if ((body.position - targetPosition).sqrMagnitude > maxDistance * maxDistance)
        {
            body.position = targetPosition;
            body.linearVelocity = holdVelocity;
        }

        // Damped spring towards the hold point, relative to its own motion so walking doesn't drag it behind.
        float followDamper = 2f * Mathf.Sqrt(followStrength) * followDamping;
        Vector3 acceleration = (targetPosition - body.position) * followStrength
            - (body.linearVelocity - holdVelocity) * followDamper;
        body.AddForce(acceleration, ForceMode.Acceleration);

        Quaternion delta = targetRotation * Quaternion.Inverse(body.rotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
            angle -= 360f;
        if (float.IsFinite(axis.x) && Mathf.Abs(angle) > 0.01f)
        {
            float turnDamper = 2f * Mathf.Sqrt(turnStrength) * turnDamping;
            Vector3 angular = axis * (angle * Mathf.Deg2Rad * turnStrength)
                - body.angularVelocity * turnDamper;
            body.AddTorque(angular, ForceMode.Acceleration);
        }
    }

    private void UpdateJaw()
    {
        if (jaw == null)
            return;

        float target;
        switch (state)
        {
            case State.Flying:
                target = jawFlyingAngle * (0.55f + 0.45f * Mathf.Sin(stateTime * jawChompSpeed));
                break;
            case State.Summoning:
                target = jawFlyingAngle * (1f - stateTime / summonDuration);
                break;
            default:
                float t = Time.time * jawChatterSpeed + phase;
                float chatter = Mathf.PerlinNoise(t, phase) * 0.6f + (Mathf.Sin(t * 3f) * 0.5f + 0.5f) * 0.4f;
                target = jawRestAngle + jawChatterAngle * chatter;
                break;
        }

        jawAngle = Mathf.Lerp(jawAngle, target, 1f - Mathf.Exp(-jawSmoothing * Time.deltaTime));

        Quaternion swivel = Quaternion.AngleAxis(jawAngle, Vector3.right);
        jaw.localPosition = jawHinge + swivel * (jawBasePosition - jawHinge);
        jaw.localRotation = swivel * jawBaseRotation;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (state != State.Flying)
            return;

        ContactPoint contact = collision.GetContact(0);
        Burst(contact.point, contact.normal, collision.collider);
    }

    private void Burst(Vector3 point, Vector3 normal, Collider hit)
    {
        if (hit != null)
        {
            TargetHealth target = hit.GetComponentInParent<TargetHealth>();
            if (target != null)
                target.TakeDamage(damage, point, normal);
        }

        HitEffect.Spawn(impactEffect, point, normal);
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(jawHinge), 0.01f);
    }
}
