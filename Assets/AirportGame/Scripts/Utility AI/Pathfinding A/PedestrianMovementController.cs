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
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;

    private static readonly List<PedestrianMovementController> NearbyPedestrians =
        new List<PedestrianMovementController>(16);

    [Header("Pathfinding")]
    [SerializeField] private PedestrianPathfinder pathfinder;
    private PedestrianAccessProfile accessProfile;

    private PedestrianAccessProfile AccessProfile
    {
        get
        {
            if (accessProfile == null)
                accessProfile = GetComponent<PedestrianAccessProfile>();

            return accessProfile;
        }
    }

    [Header("Movement")]
    [Min(0.1f)]
    [SerializeField] private float movementSpeed = 1.5f;
    [SerializeField] private Vector2 individualSpeedMultiplierRange = new Vector2(0.9f, 1.1f);
    [Min(0.1f)]
    [SerializeField] private float acceleration = 5f;
    [Min(0.1f)]
    [SerializeField] private float deceleration = 3f;
    [Min(0.1f)]
    [SerializeField] private float slowdownDistance = 0.8f;
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

    private struct TravelEstimateCacheEntry
    {
        public float CachedAt;
        public Vector3 StartPosition;
        public Vector3 TargetPosition;
        public float TravelTime;
    }

    // These caches are per pedestrian because route reachability depends on
    // this pedestrian's position and access profile. All AI systems using
    // this controller share the same recent estimates.
    private readonly Dictionary<InteractionProvider, TravelEstimateCacheEntry>
        providerTravelEstimateCache =
            new Dictionary<InteractionProvider, TravelEstimateCacheEntry>();

    private readonly Dictionary<Vector3Int, TravelEstimateCacheEntry>
        destinationTravelEstimateCache =
            new Dictionary<Vector3Int, TravelEstimateCacheEntry>();

    private const float TravelEstimateCacheLifetime = 1.5f;
    private const float TravelEstimateStartMovementThreshold = 0.75f;
    private const float TravelEstimateTargetMovementThreshold = 0.25f;
    private Vector3 destination;
    private float movementHeight;
    private float lastWalkableHeight;
    private float individualSpeedMultiplier = 1f;
    private float currentSpeed;
    private float pendingMovementTime;
    private Vector3 currentAvoidanceOffset;
    private Collider[] alignmentColliders;
    private Renderer[] alignmentRenderers;
    private InteractionProvider lastApproachProvider;
    private Vector3 lastApproachPosition;
    private PlacedBuilding allowedStartFootprint;
    private PlacedBuilding allowedTargetFootprint;

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
        UtilityAISpatialRegistry<PedestrianMovementController>.Register(this);
        individualSpeedMultiplier = UnityEngine.Random.Range(
            individualSpeedMultiplierRange.x,
            individualSpeedMultiplierRange.y);
        currentSpeed = 0f;
    }

    private void OnDisable()
    {
        UtilityAISpatialRegistry<PedestrianMovementController>.Unregister(this);
    }

    private void Awake()
    {
        ResolvePathfinder();
        movementHeight = transform.position.y;
        lastWalkableHeight = movementHeight;
        alignmentColliders = GetComponentsInChildren<Collider>(true);
        alignmentRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Update()
    {
        UtilityAISpatialRegistry<PedestrianMovementController>.UpdatePosition(this);

        if (isMoving)
            UpdateMovement();
    }

    /// <summary>
    /// Places the pedestrian at an interaction's animation anchor while
    /// keeping the bottom of its collider bounds on that anchor.
    /// </summary>
    public void AlignToAnimationPoint(InteractionProvider provider)
    {
        if (provider == null)
            return;

        AlignToPoint(provider.AnimationPosition, provider.AnimationRotation);
    }

    /// <summary>
    /// Returns the pedestrian to the provider's reachable approach point after
    /// an interaction has finished, freeing the object's occupied footprint.
    /// </summary>
    public void AlignToInteractionPoint(InteractionProvider provider)
    {
        if (provider == null)
            return;

        Vector3 approachPosition =
            lastApproachProvider == provider
                ? lastApproachPosition
                : provider.InteractionPosition;

        // Interaction approach positions are already pedestrian-root positions.
        // If no matching approach was recorded (for example, a caller used a
        // nearby-position route), the provider's raw point may be below the floor.
        // Always restore the last known walking height rather than trusting the
        // interaction anchor's Y coordinate.
        approachPosition.y = lastWalkableHeight;
        transform.SetPositionAndRotation(
            approachPosition,
            provider.InteractionRotation);

        movementHeight = transform.position.y;
        UtilityAISpatialRegistry<PedestrianMovementController>.UpdatePosition(this);
    }

    private void AlignToPoint(Vector3 anchorPosition, Quaternion anchorRotation)
    {
        float halfHeight = GetBodyHalfHeight();
        Vector3 rootPosition =
            anchorPosition + anchorRotation * Vector3.up * halfHeight;

        transform.SetPositionAndRotation(rootPosition, anchorRotation);
        movementHeight = transform.position.y;
        UtilityAISpatialRegistry<PedestrianMovementController>.UpdatePosition(this);
    }

    /// <summary>
    /// Aligns the bottom of the pedestrian's visible/physical body with a
    /// supplied ground point while preserving its horizontal position.
    /// </summary>
    public void AlignFeetToGround(Vector3 groundPosition)
    {
        if (alignmentColliders == null || alignmentRenderers == null)
            return;

        bool hasBounds = false;
        Bounds bodyBounds = default;

        if (alignmentColliders != null)
        {
            for (int i = 0; i < alignmentColliders.Length; i++)
            {
                Collider bodyCollider = alignmentColliders[i];
                if (bodyCollider == null || !bodyCollider.enabled || bodyCollider.isTrigger)
                    continue;

                if (!hasBounds)
                {
                    bodyBounds = bodyCollider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bodyBounds.Encapsulate(bodyCollider.bounds);
                }
            }
        }

        if (!hasBounds && alignmentRenderers != null)
        {
            for (int i = 0; i < alignmentRenderers.Length; i++)
            {
                Renderer bodyRenderer = alignmentRenderers[i];
                if (bodyRenderer == null || !bodyRenderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bodyBounds = bodyRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bodyBounds.Encapsulate(bodyRenderer.bounds);
                }
            }
        }

        if (hasBounds)
        {
            Vector3 position = transform.position;
            position.y += groundPosition.y - bodyBounds.min.y;
            transform.position = position;
        }
        else
        {
            Vector3 position = transform.position;
            position.y = groundPosition.y;
            transform.position = position;
        }

        movementHeight = transform.position.y;
        lastWalkableHeight = movementHeight;
        UtilityAISpatialRegistry<PedestrianMovementController>.UpdatePosition(this);
    }

    private float GetBodyHalfHeight()
    {
        bool hasBounds = false;
        Bounds combinedBounds = default;

        if (alignmentColliders != null)
        {
            for (int i = 0; i < alignmentColliders.Length; i++)
            {
                Collider bodyCollider = alignmentColliders[i];
                if (bodyCollider == null || !bodyCollider.enabled || bodyCollider.isTrigger)
                    continue;

                if (!hasBounds)
                {
                    combinedBounds = bodyCollider.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(bodyCollider.bounds);
                }
            }
        }

        if (!hasBounds && alignmentRenderers != null)
        {
            for (int i = 0; i < alignmentRenderers.Length; i++)
            {
                Renderer bodyRenderer = alignmentRenderers[i];
                if (bodyRenderer == null || !bodyRenderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    combinedBounds = bodyRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(bodyRenderer.bounds);
                }
            }
        }

        return hasBounds ? combinedBounds.extents.y : 0f;
    }

    /// <summary>
    /// Estimates travel time using the same pathfinder and individual walking
    /// speed used by actual movement. Returns PositiveInfinity when no route exists.
    /// </summary>
    public float EstimateTravelTime(Vector3 worldDestination)
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        if (pathfinder == null)
            ResolvePathfinder();

        if (pathfinder == null)
            return float.PositiveInfinity;

        Vector3 start = transform.position;
        Vector3 target = worldDestination;
        target.y = start.y;

        Vector3Int cacheKey = new Vector3Int(
            Mathf.RoundToInt(target.x * 2f),
            0,
            Mathf.RoundToInt(target.z * 2f));

        if (TryGetCachedTravelEstimate(
                destinationTravelEstimateCache,
                cacheKey,
                target,
                out float cachedTravelTime))
        {
            return cachedTravelTime;
        }

        List<Vector3> estimatedPath =
            pathfinder.FindWorldPath(start, target, AccessProfile);

        float travelTime = EstimateTravelTime(start, target, estimatedPath);
        StoreTravelEstimate(
            destinationTravelEstimateCache,
            cacheKey,
            start,
            target,
            travelTime);

        return travelTime;
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("PedestrianMovementController.EstimateTravelTime(Vector3)", performanceTimingStart);
        }
    }

    /// <summary>
    /// Estimates travel time to an interaction using the same preferred-point
    /// and fallback-approach selection used by MoveTo(provider).
    /// </summary>
    public float EstimateTravelTime(InteractionProvider provider)
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        if (provider == null)
            return float.PositiveInfinity;

        if (pathfinder == null)
            ResolvePathfinder();

        if (pathfinder == null)
            return float.PositiveInfinity;

        Vector3 providerTarget = provider.InteractionPosition;
        if (TryGetCachedTravelEstimate(
                providerTravelEstimateCache,
                provider,
                providerTarget,
                out float cachedTravelTime))
        {
            return cachedTravelTime;
        }

        Vector3 selectedDestination;
        List<Vector3> estimatedPath;

        bool foundPath = pathfinder.TryFindBestApproachPath(
            transform.position,
            provider.InteractionPosition,
            provider.transform.position,
            provider.GetComponentInParent<PlacedBuilding>(),
            out selectedDestination,
            out estimatedPath,
            false,
            AccessProfile);

        if (!foundPath)
        {
            StoreTravelEstimate(
                providerTravelEstimateCache,
                provider,
                transform.position,
                providerTarget,
                float.PositiveInfinity);

            return float.PositiveInfinity;
        }

        selectedDestination.y = transform.position.y;
        float travelTime = EstimateTravelTime(
            transform.position,
            selectedDestination,
            estimatedPath);

        StoreTravelEstimate(
            providerTravelEstimateCache,
            provider,
            transform.position,
            providerTarget,
            travelTime);

        return travelTime;
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("PedestrianMovementController.EstimateTravelTime(InteractionProvider)", performanceTimingStart);
        }
    }

    private bool TryGetCachedTravelEstimate<TKey>(
        Dictionary<TKey, TravelEstimateCacheEntry> cache,
        TKey key,
        Vector3 targetPosition,
        out float travelTime)
    {
        travelTime = float.PositiveInfinity;

        if (!cache.TryGetValue(key, out TravelEstimateCacheEntry entry))
            return false;

        bool expired =
            Time.unscaledTime - entry.CachedAt > TravelEstimateCacheLifetime;
        bool pedestrianMoved =
            HorizontalDistance(transform.position, entry.StartPosition) >=
            TravelEstimateStartMovementThreshold;
        bool targetMoved =
            HorizontalDistance(targetPosition, entry.TargetPosition) >=
            TravelEstimateTargetMovementThreshold;

        if (expired || pedestrianMoved || targetMoved)
        {
            cache.Remove(key);
            return false;
        }

        travelTime = entry.TravelTime;
        return true;
    }

    private void StoreTravelEstimate<TKey>(
        Dictionary<TKey, TravelEstimateCacheEntry> cache,
        TKey key,
        Vector3 startPosition,
        Vector3 targetPosition,
        float travelTime)
    {
        // Keep the cache bounded if a pedestrian evaluates many unique
        // temporary destinations, such as conversation meeting points.
        if (cache.Count >= 128)
            cache.Clear();

        cache[key] = new TravelEstimateCacheEntry
        {
            CachedAt = Time.unscaledTime,
            StartPosition = startPosition,
            TargetPosition = targetPosition,
            TravelTime = travelTime
        };
    }

    private float EstimateTravelTime(
        Vector3 start,
        Vector3 target,
        List<Vector3> estimatedPath)
    {
        if (estimatedPath == null || estimatedPath.Count == 0)
            return float.PositiveInfinity;

        float distance = 0f;
        Vector3 previous = start;

        foreach (Vector3 waypoint in estimatedPath)
        {
            distance += HorizontalDistance(previous, waypoint);
            previous = waypoint;
        }

        distance += HorizontalDistance(previous, target);

        float effectiveSpeed =
            Mathf.Max(0.1f, movementSpeed * individualSpeedMultiplier);

        float accelerationTime = effectiveSpeed / Mathf.Max(0.1f, acceleration);
        float accelerationDistance = 0.5f * effectiveSpeed * accelerationTime;

        if (distance <= accelerationDistance)
        {
            return Mathf.Sqrt(
                2f * distance / Mathf.Max(0.1f, acceleration));
        }

        return accelerationTime +
               (distance - accelerationDistance) / effectiveSpeed;
    }

    /// <summary>
    /// Requests a route to a world-space destination.
    /// Returns false if the shared pathfinder cannot find a route.
    /// </summary>
    public bool MoveTo(Vector3 worldDestination)
    {
        return MoveToWorldPosition(worldDestination, true, false);
    }

    /// <summary>
    /// Routes to a destination while allowing the start and destination cells
    /// to be occupied by their associated building (for example a gate or stop).
    /// Occupied cells elsewhere remain blocked.
    /// </summary>
    public bool MoveToAllowingOccupiedEndpoints(
        Vector3 worldDestination,
        PlacedBuilding allowedStartFootprint = null,
        PlacedBuilding allowedTargetFootprint = null)
    {
        return MoveToWorldPosition(
            worldDestination, true, true,
            allowedStartFootprint, allowedTargetFootprint);
    }

    /// <summary>
    /// Attempts a route without raising PathFailed when the initial route
    /// cannot be calculated. The caller owns handling the false result.
    /// Runtime movement failures continue to use the normal event.
    /// </summary>
    public bool TryMoveTo(Vector3 worldDestination)
    {
        return MoveToWorldPosition(worldDestination, false, false);
    }

    private bool MoveToWorldPosition(
        Vector3 worldDestination,
        bool notifyOnImmediateFailure,
        bool allowOccupiedEndpoints,
        PlacedBuilding allowedStartFootprint = null,
        PlacedBuilding allowedTargetFootprint = null)
    {
        StopInternal("Calculating route");
        lastApproachProvider = null;
        this.allowedStartFootprint = allowedStartFootprint;
        this.allowedTargetFootprint = allowedTargetFootprint;

        if (pathfinder == null)
            ResolvePathfinder();

        if (pathfinder == null)
        {
            movementStatus = "Pathfinder missing";
            if (notifyOnImmediateFailure)
                PathFailed?.Invoke();
            return false;
        }

        Vector3 target = worldDestination;
        target.y = transform.position.y;

        List<Vector3> calculatedPath =
            pathfinder.FindWorldPath(
                transform.position, target, AccessProfile, allowOccupiedEndpoints,
                allowedStartFootprint, allowedTargetFootprint);

        if (calculatedPath == null || calculatedPath.Count == 0)
        {
            movementStatus = "No path found";
            if (notifyOnImmediateFailure)
                PathFailed?.Invoke();
            return false;
        }

        return StartMovement(target, calculatedPath);
    }

    /// <summary>
    /// Attempts to reach a world-space target, searching nearby walkable
    /// approach cells when the preferred destination is blocked or unreachable.
    /// </summary>
    public bool TryMoveToNearby(
        Vector3 preferredWorld,
        Vector3 approachAnchorWorld,
        PlacedBuilding targetBuilding = null)
    {
        StopInternal("Calculating nearby approach route");
        lastApproachProvider = null;
        allowedStartFootprint = null;
        allowedTargetFootprint = targetBuilding;

        if (pathfinder == null)
            ResolvePathfinder();

        if (pathfinder == null)
        {
            movementStatus = "Pathfinder missing";
            return false;
        }

        Vector3 selectedDestination;
        List<Vector3> calculatedPath;

        bool foundPath = pathfinder.TryFindBestApproachPath(
            transform.position,
            preferredWorld,
            approachAnchorWorld,
            targetBuilding,
            out selectedDestination,
            out calculatedPath,
            true,
            AccessProfile);

        if (!foundPath)
        {
            movementStatus = "No nearby approach path found";
            return false;
        }

        selectedDestination.y = transform.position.y;

        Log(
            $"PedestrianMovementController {name}: Selected nearby approach " +
            $"point {selectedDestination} for preferred target {preferredWorld}.",
            this);

        return StartMovement(selectedDestination, calculatedPath);
    }

    /// <summary>
    /// Walks to an interaction provider. The configured InteractionPoint is
    /// preferred; the pathfinder only searches surrounding footprint cells
    /// when that point cannot be reached.
    /// </summary>
    public bool MoveTo(InteractionProvider provider)
    {
        return MoveToInteraction(provider, true);
    }

    /// <summary>
    /// Attempts to reach an interaction without raising PathFailed for an
    /// immediate route-calculation failure. The caller handles the false result.
    /// </summary>
    public bool TryMoveTo(InteractionProvider provider)
    {
        return MoveToInteraction(provider, false);
    }

    private bool MoveToInteraction(
        InteractionProvider provider,
        bool notifyOnImmediateFailure)
    {
        StopInternal("Calculating interaction route");
        lastApproachProvider = null;

        if (provider == null)
        {
            movementStatus = "Interaction provider missing";
            if (notifyOnImmediateFailure)
                PathFailed?.Invoke();
            return false;
        }

        if (pathfinder == null)
            ResolvePathfinder();

        if (pathfinder == null)
        {
            movementStatus = "Pathfinder missing";
            if (notifyOnImmediateFailure)
                PathFailed?.Invoke();
            return false;
        }

        PlacedBuilding targetBuilding =
            provider.GetComponentInParent<PlacedBuilding>();

        Vector3 selectedDestination;
        List<Vector3> calculatedPath;

        bool foundPath = pathfinder.TryFindBestApproachPath(
            transform.position,
            provider.InteractionPosition,
            provider.transform.position,
            targetBuilding,
            out selectedDestination,
            out calculatedPath,
            true,
            AccessProfile);

        if (!foundPath)
        {
            movementStatus = "No interaction approach path found";
            if (notifyOnImmediateFailure)
                PathFailed?.Invoke();
            return false;
        }

        selectedDestination.y = transform.position.y;
        lastApproachProvider = provider;
        lastApproachPosition = selectedDestination;

        return StartMovement(selectedDestination, calculatedPath);
    }

    public bool IsAtInteractionApproach(InteractionProvider provider)
    {
        return provider != null &&
               lastApproachProvider == provider &&
               !isMoving &&
               movementStatus == "Arrived" &&
               HorizontalDistance(transform.position, lastApproachPosition) <= 0.12f;
    }

    private bool StartMovement(
        Vector3 worldDestination,
        List<Vector3> calculatedPath)
    {
        destination = worldDestination;
        pendingMovementTime = 0f;
        movementHeight = transform.position.y;
        lastWalkableHeight = movementHeight;
        destination.y = movementHeight;

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
            pathfinder = FindAnyObjectByType<PedestrianPathfinder>();
    }

    private void UpdateMovement()
    {
        // Keep movement steps small enough for reliable walkable-grid and
        // local-avoidance checks, even when simulation time advances quickly.
        const float maximumSimulationStep = 0.2f;
        // The larger step budget preserves high-speed catch-up capacity while
        // retaining the finer movement and path-validation granularity.
        const int maximumMovementStepsPerFrame = 512;

        SimulationMovementUtility.AccumulateTime(
            ref pendingMovementTime,
            GameTimeManager.SimulationDeltaTime);

        int movementSteps = 0;
        while (isMoving &&
               movementSteps < maximumMovementStepsPerFrame &&
               SimulationMovementUtility.TryConsumeStep(
                   ref pendingMovementTime,
                   maximumSimulationStep,
                   out float step))
        {
            if (!UpdateMovementStep(step))
            {
                // Do not bank time while blocked; otherwise the pedestrian
                // could make a large catch-up move when the route clears.
                pendingMovementTime = 0f;
                return;
            }

            movementSteps++;

            SkipReachedWaypoints();

            if (currentWaypointIndex >= movementPath.Count)
            {
                CompleteMovement();
                return;
            }
        }
    }

    private bool UpdateMovementStep(float deltaTime)
    {
        if (currentWaypointIndex >= movementPath.Count)
            return true;

        Vector3 target = movementPath[currentWaypointIndex];
        target.y = movementHeight;

        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;

        Vector3 desiredDirection = toTarget.normalized;

        // Give each pedestrian a slightly different walking pace and ease
        // into movement instead of starting at full speed immediately.
        float targetSpeed = movementSpeed * individualSpeedMultiplier;
        bool approachingFinalDestination =
            currentWaypointIndex == movementPath.Count - 1;

        if (approachingFinalDestination &&
            slowdownDistance > 0f &&
            toTarget.magnitude < slowdownDistance)
        {
            float approachFactor = Mathf.Clamp01(toTarget.magnitude / slowdownDistance);
            targetSpeed *= Mathf.Lerp(0.65f, 1f, approachFactor);
        }

        float speedChange = targetSpeed > currentSpeed ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            speedChange * deltaTime);

        if (currentSpeed <= 0.001f)
            return true;

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
                nextPosition,
                AccessProfile,
                allowedStartFootprint,
                allowedTargetFootprint))
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
                    nextPosition,
                    AccessProfile,
                    allowedStartFootprint,
                    allowedTargetFootprint))
            {
                return false;
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
                rotationSpeed * deltaTime);
        }

        return true;
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
            avoidanceStrength <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 separation = Vector3.zero;
        Vector3 ownPosition = transform.position;
        ownPosition.y = 0f;

        UtilityAISpatialRegistry<PedestrianMovementController>.GetNearby(
            ownPosition, avoidanceRadius, NearbyPedestrians);

        for (int i = 0; i < NearbyPedestrians.Count; i++)
        {
            PedestrianMovementController other = NearbyPedestrians[i];
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
        pendingMovementTime = 0f;
        currentSpeed = 0f;
        currentAvoidanceOffset = Vector3.zero;
        currentWaypointIndex = movementPath.Count;
        movementStatus = "Arrived";
        Arrived?.Invoke();
    }

    private void StopInternal(string status)
    {
        isMoving = false;
        pendingMovementTime = 0f;
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

    private void Log(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.Log(message, context); else Debug.Log(message);
    }
    private void LogWarning(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message);
    }
    private void LogError(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogError(message, context); else Debug.LogError(message);
    }

}