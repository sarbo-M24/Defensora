
using System;
using UnityEngine;

/// <summary>
/// Walks an enemy along a list of waypoints on the XZ plane. When it reaches
/// the last one it raises <see cref="ReachedGoal"/> and destroys itself.
/// </summary>
public class EnemyMover : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 2.5f;
    [SerializeField, Min(0f)] private float turnSpeed = 540f;

    [Tooltip("How close the enemy must get to a waypoint to move on to the next.")]
    [SerializeField, Min(0.05f)] private float arriveDistance = 0.3f;

    private Transform[] waypoints;
    private int waypointIndex;
    private bool reachedGoal;

    public float MoveSpeed
    {
        get => moveSpeed;
        set => moveSpeed = Mathf.Max(0.1f, value);
    }

    /// <summary>Raised when the enemy reaches the end of its path.</summary>
    public event Action<EnemyMover> ReachedGoal;

    public void SetPath(Transform[] path)
    {
        waypoints = path;
        waypointIndex = 0;
        reachedGoal = false;
    }

    private void Update()
    {
        if (reachedGoal || waypoints == null || waypoints.Length == 0)
            return;

        Transform target = waypoints[waypointIndex];

        if (target == null)
        {
            AdvanceWaypoint();
            return;
        }

        // Stay at the current height; only move across the ground.
        Vector3 position = transform.position;
        Vector3 destination = target.position;
        destination.y = position.y;

        Vector3 toTarget = destination - position;

        if (toTarget.sqrMagnitude <= arriveDistance * arriveDistance)
        {
            AdvanceWaypoint();
            return;
        }

        transform.position = Vector3.MoveTowards(
            position,
            destination,
            moveSpeed * Time.deltaTime
        );

        Quaternion look = Quaternion.LookRotation(toTarget);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            look,
            turnSpeed * Time.deltaTime
        );
    }

    private void AdvanceWaypoint()
    {
        waypointIndex++;

        if (waypointIndex < waypoints.Length)
            return;

        reachedGoal = true;
        ReachedGoal?.Invoke(this);

        Destroy(gameObject);
    }
}
