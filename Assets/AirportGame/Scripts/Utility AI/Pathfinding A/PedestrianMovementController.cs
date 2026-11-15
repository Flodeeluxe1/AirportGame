using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Moves a pedestrian along routes produced by the shared PedestrianPathfinder.
/// Also applies lightweight local separation from other active pedestrians.
/// </summary>
[DisallowMultipleComponent]
public class PedestrianMovementController : MonoBehaviour
{
    private static readonly HashSet<PedestrianMovementController> ActivePedestrians =
        new HashSet<PedestrianMovementController>();

    [Header("Pathfinding")]
    [SerializeField] private PedestrianPathfinder pathfinder;

    [Header("Movement")]
    [Min(0.1f)]
    [SerializeField] private float movementSpeed = 1.5f;
    [SerializeField] private Vector2 individualSpeedMultiplierRange = new Vector2(0.9f, 1.1f);
    [Min(0.1f)]
    [SerializeField] private float acceleration = 2.5f;
    [Min(0.1f)]
    [SerializeField] private float deceleration = 4f;
    [Min(0.1f)]
    [SerializeField] private float slowdownDistance = 0.6f;
    [Min(1f)]
    [SerializeField] private float rotationSpeed = 540f;
    [Min(0.01f)]
    [SerializeField] private float waypointArrivalDistance = 0.08f;

    [Header("Pedestrian Avoidance")]
    [SerializeField] private bool avoidOtherPedestrians = true;
    [Min(0f)]
    [SerializeField] private float avoidanceRadius = 0.65f;
    [Range(0f, 1f)]
    [SerializeField] private float avoidanceStrength = 0.25f;
    [Min(0.1f)]
    [SerializeField] private float avoidanceResponse = 3.5f;

    [Header("Runtime")]
    [SerializeField] private bool isMoving;
    [SerializeField] private string movementStatus = "Idle";
    [SerializeField] private int currentWaypointIndex;
    [SerializeField] private int waypointCount;

    private readonly List<Vector3> movementPath = new List<Vector3>();
    private Vector3 destination;
    private float movementHeight;
    private float individualSpeedMultiplier = 1f;
    private float currentSpeed;
    private Vector3 currentAvoidanceOffset;

    public bool IsMoving => isMoving;
    public bool HasPath => movementPath.Count > 0;
    public bool HasArrived => movementStatus == "Arrived";
    public string MovementStatus => movementStatus;
    public Vector3 Destination => destination;
    public int CurrentWaypointIndex => currentWaypointIndex;
    public int WaypointCount => waypointCount;
    public IReadOnlyList<Vector3> MovementPath => movementPath;

    public event Action Arrived;
    public event Action PathFailed;

    private void OnEnable()
    {
        ActivePedestrians.Add(this);
        individualSpeedMultiplier = UnityEngine.Random.Range(
            individualSpeedMultiplierRange.x,
            individualSpeedMultiplierRange.y);
        currentSpeed = 0f;
    }

    private void OnDisable()
    {
        ActivePedestrians.Remove(this);
    }

    private void Awake()
    {
        ResolvePathfinder();
        movementHeight = transform.position.y;
    }

    private void Update()
    {
        if (isMoving)
            UpdateMovement();
    }

    /// <summary>
    /// Requests a route to a world-space destination.
    /// Returns false if the shared pathfinder cannot find a route.
    /// </summary>
    public bool MoveTo(Vector3 worldDestination)
    {
        StopInternal("Calculating route");

        if (pathfinder == null)
            ResolvePathfinder();

        if (pathfinder == null)
        {
            movementStatus = "Pathfinder missing";
            PathFailed?.Invoke();
            return false;
        }

        destination = worldDestination;
        movementHeight = transform.position.y;
        destination.y = movementHeight;

        List<Vector3> calculatedPath =
            pathfinder.FindWorldPath(transform.position, destination);

        if (calculatedPath == null || calculatedPath.Count == 0)
        {
            movementStatus = "No path found";
            PathFailed?.Invoke();
            return false;
        }

        for (int i = 0; i < calculatedPath.Count; i++)
        {
            Vector3 waypoint = calculatedPath[i];
            waypoint.y = movementHeight;

            if (movementPath.Count == 0 ||
                HorizontalDistance(
                    movementPath[movementPath.Count - 1],
                    waypoint) > 0.001f)
            {
                movementPath.Add(waypoint);
            }
        }

        if (movementPath.Count == 0 ||
            HorizontalDistance(
                movementPath[movementPath.Count - 1],
                destination) > waypointArrivalDistance)
        {
            movementPath.Add(destination);
        }

        currentWaypointIndex = 0;
        waypointCount = movementPath.Count;

        SkipReachedWaypoints();

        isMoving = currentWaypointIndex < movementPath.Count;

        if (!isMoving)
        {
            CompleteMovement();
            return true;
        }

        movementStatus = "Moving";
        return true;
    }

    /// <summary>
    /// Stops movement and discards the current route.
    /// </summary>
    public void Stop()
    {
        StopInternal("Stopped");
    }

    /// <summary>
    /// Recalculates the route to the current destination from the current position.
    /// </summary>
    public bool RecalculatePath()
    {
        if (movementStatus == "Idle" ||
            movementStatus == "Stopped" ||
            movementStatus == "Arrived")
        {
            return false;
        }

        return MoveTo(destination);
    }

    private void ResolvePathfinder()
    {
        if (pathfinder == null)
            pathfinder = FindFirstObjectByType<PedestrianPathfinder>();
    }

    private void UpdateMovement()
    {
        if (currentWaypointIndex >= movementPath.Count)
        {
            CompleteMovement();
            return;
        }

        Vector3 target = movementPath[currentWaypointIndex];
        target.y = movementHeight;

        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude <= waypointArrivalDistance)
        {
            currentWaypointIndex++;
            SkipReachedWaypoints();

            if (currentWaypointIndex >= movementPath.Count)
                CompleteMovement();

            return;
        }

        Vector3 desiredDirection = toTarget.normalized;
        float deltaTime = Time.deltaTime;

        // Give each pedestrian a slightly different walking pace and ease
        // into movement instead of starting at full speed immediately.
        float targetSpeed = movementSpeed * individualSpeedMultiplier;
        if (slowdownDistance > 0f && toTarget.magnitude < slowdownDistance)
        {
            float approachFactor = Mathf.Clamp01(toTarget.magnitude / slowdownDistance);
            targetSpeed *= Mathf.Lerp(0.25f, 1f, approachFactor);
        }

        float speedChange = targetSpeed > currentSpeed ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            speedChange * deltaTime);

        if (currentSpeed <= 0.001f)
            return;

        Vector3 targetAvoidanceOffset = GetAvoidanceOffset(desiredDirection);
        float avoidanceLerp = 1f - Mathf.Exp(-avoidanceResponse * deltaTime);

        currentAvoidanceOffset = Vector3.Lerp(
            currentAvoidanceOffset,
            targetAvoidanceOffset,
            avoidanceLerp);

        // Keep the steering offset perpendicular to the route direction.
        currentAvoidanceOffset -=
            Vector3.Dot(currentAvoidanceOffset, desiredDirection) * desiredDirection;

        Vector3 movementDirection =
            (desiredDirection + currentAvoidanceOffset).normalized;
        Vector3 nextPosition =
            transform.position + movementDirection * currentSpeed * deltaTime;

        // Local avoidance may only steer through cells and edges that the
        // pedestrian pathfinder considers traversable. If the sidestep is
        // blocked, fall back to the planned route direction.
        if (pathfinder != null &&
            !pathfinder.CanMoveBetweenWorldPositions(
                transform.position,
                nextPosition))
        {
            currentAvoidanceOffset = Vector3.Lerp(
                currentAvoidanceOffset,
                Vector3.zero,
                avoidanceLerp);

            movementDirection = desiredDirection;
            nextPosition =
                transform.position + movementDirection * currentSpeed * deltaTime;

            if (!pathfinder.CanMoveBetweenWorldPositions(
                    transform.position,
                    nextPosition))
            {
                return;
            }
        }

        transform.position = nextPosition;

        if (movementDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(movementDirection, Vector3.up);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Adds a small sideways steering force when another pedestrian is nearby.
    /// The forward component is preserved so pedestrians continue progressing
    /// along their planned route instead of being pushed backwards.
    /// </summary>
    private Vector3 GetAvoidanceOffset(Vector3 desiredDirection)
    {
        if (!avoidOtherPedestrians ||
            avoidanceRadius <= 0f ||
            avoidanceStrength <= 0f ||
            ActivePedestrians.Count <= 1)
        {
            return Vector3.zero;
        }

        Vector3 separation = Vector3.zero;
        Vector3 ownPosition = transform.position;
        ownPosition.y = 0f;

        foreach (PedestrianMovementController other in ActivePedestrians)
        {
            if (other == null || other == this || !other.isActiveAndEnabled)
                continue;

            Vector3 offset = ownPosition - other.transform.position;
            offset.y = 0f;

            float distance = offset.magnitude;
            if (distance >= avoidanceRadius)
                continue;

            Vector3 awayDirection;

            if (distance > 0.001f)
            {
                awayDirection = offset / distance;
            }
            else
            {
                // If two pedestrians spawn at exactly the same position,
                // make them choose opposite sideways directions.
                float side = GetEntityId() < other.GetEntityId() ? 1f : -1f;
                awayDirection = Vector3.Cross(
                    Vector3.up,
                    desiredDirection) * side;
            }

            float influence = 1f - Mathf.Clamp01(distance / avoidanceRadius);
            separation += awayDirection * influence;
        }

        if (separation.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        // Prefer sidestepping rather than moving backwards off the route.
        Vector3 lateralSeparation =
            separation - Vector3.Dot(separation, desiredDirection) * desiredDirection;

        if (lateralSeparation.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        float combinedInfluence = Mathf.Clamp01(separation.magnitude);
        return lateralSeparation.normalized * avoidanceStrength * combinedInfluence;
    }

    private void SkipReachedWaypoints()
    {
        while (currentWaypointIndex < movementPath.Count &&
               HorizontalDistance(
                   transform.position,
                   movementPath[currentWaypointIndex]) <=
               waypointArrivalDistance)
        {
            currentWaypointIndex++;
        }
    }

    private void CompleteMovement()
    {
        isMoving = false;
        currentSpeed = 0f;
        currentAvoidanceOffset = Vector3.zero;
        currentWaypointIndex = movementPath.Count;
        movementStatus = "Arrived";
        Arrived?.Invoke();
    }

    private void StopInternal(string status)
    {
        isMoving = false;
        currentSpeed = 0f;
        currentAvoidanceOffset = Vector3.zero;
        movementPath.Clear();
        currentWaypointIndex = 0;
        waypointCount = 0;
        movementStatus = status;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnValidate()
    {
        movementSpeed = Mathf.Max(0.1f, movementSpeed);
        individualSpeedMultiplierRange.x = Mathf.Max(0.5f, individualSpeedMultiplierRange.x);
        individualSpeedMultiplierRange.y = Mathf.Max(
            individualSpeedMultiplierRange.x,
            individualSpeedMultiplierRange.y);
        acceleration = Mathf.Max(0.1f, acceleration);
        deceleration = Mathf.Max(0.1f, deceleration);
        slowdownDistance = Mathf.Max(0.1f, slowdownDistance);
        rotationSpeed = Mathf.Max(1f, rotationSpeed);
        waypointArrivalDistance =
            Mathf.Max(0.01f, waypointArrivalDistance);
        avoidanceRadius = Mathf.Max(0f, avoidanceRadius);
        avoidanceStrength = Mathf.Clamp01(avoidanceStrength);
        avoidanceResponse = Mathf.Max(0.1f, avoidanceResponse);
    }
}