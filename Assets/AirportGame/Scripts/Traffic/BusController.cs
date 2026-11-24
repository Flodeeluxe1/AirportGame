using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives one public-transport bus through its assigned stop sequence and
/// then to the shared road-traffic exit. A bus never loops its route.
/// </summary>
[DisallowMultipleComponent]
public class BusController : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;

    [Header("Movement")]
    [Min(0.1f)] [SerializeField] private float movementSpeed = 5f;
    [Min(0.1f)] [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float heightOffset = 0.2f;
    [Min(0.01f)] [SerializeField] private float waypointTolerance = 0.08f;

    [Header("Road Turn Smoothing")]
    [Tooltip("Requested turn radius in grid-cell units.")]
    [SerializeField, Min(0f)] private float turningRadiusInCells = 2f;
    [Tooltip("Number of segments used to approximate each Bezier curve.")]
    [SerializeField, Min(1)] private int turnCurveResolution = 8;

    [Header("Stop Service")]
    [Min(0f)] [SerializeField] private float stopDwellSeconds = 5f;

    [Header("Intersection Control")]
    [Min(0)] [SerializeField] private int intersectionLookAheadModules = 1;
    [Min(1)] [SerializeField] private int intersectionClearModules = 2;

    [Header("Debug - Runtime Information")]
    public string debugState = "Waiting for journey";
    public float debugCurrentSpeed;
    [Tooltip("Closest vehicle currently blocking this bus.")]
    public string debugBlockingCollider = "None";
    public string debugBlockingVehicleState = "None";
    public float debugBlockingDistance;
    public string debugTrafficDecision = "Not tested";
    public int debugPathIndex;
    public int debugPathLength;

    private readonly List<Vector2Int> route = new List<Vector2Int>();
    private readonly List<Vector3> smoothedMovementWaypoints =
        new List<Vector3>();
    private readonly List<int> smoothedRouteIndices = new List<int>();
    private int currentSmoothedWaypointIndex;
    private readonly HashSet<int> stopPathIndices = new HashSet<int>();
    private readonly HashSet<Vector2Int> completedStopRoadCells =
        new HashSet<Vector2Int>();
    private RoadNetwork roadNetwork;
    private GridManager gridManager;
    private IntersectionManager intersectionManager;
    private TrafficFollowing trafficFollowing;
    private BusSpawner ownerSpawner;
    private int pathIndex;
    private float currentSpeed;
    private float stopTimeRemaining;
    private bool routeInitialized;
    private int routeTopologyVersion = -1;
    private bool waitingForIntersection;
    private bool hasIntersectionReservation;
    private Vector2Int requestedIntersection;
    private Vector2Int reservedIntersection;
    private int reservedIntersectionPathIndex = -1;

    public bool IsActiveOnRoad => routeInitialized && stopTimeRemaining <= 0f &&
                                  pathIndex < route.Count;
    public float CurrentSpeed => currentSpeed;
    public string CurrentStatus => debugState;

    private void Awake()
    {
        roadNetwork = FindAnyObjectByType<RoadNetwork>();
        gridManager = FindAnyObjectByType<GridManager>();
        intersectionManager = FindAnyObjectByType<IntersectionManager>();

        trafficFollowing = GetComponent<TrafficFollowing>();
        if (trafficFollowing == null)
            trafficFollowing = gameObject.AddComponent<TrafficFollowing>();

        VehicleSpawnID spawnID = GetComponent<VehicleSpawnID>();
        if (spawnID == null)
            spawnID = gameObject.AddComponent<VehicleSpawnID>();
        spawnID.InitializeID();
    }

    public bool AlignColliderBottomToGround(float groundY)
    {
        return VehicleGroundAlignmentUtility.AlignColliderBottomToGround(
            transform, ref heightOffset, groundY);
    }

    public bool InitializeJourney(
        List<Vector2Int> assignedRoute,
        List<int> assignedStopPathIndices,
        BusSpawner spawner)
    {
        if (routeInitialized || assignedRoute == null ||
            assignedRoute.Count < 2 || spawner == null ||
            roadNetwork == null || gridManager == null)
            return false;

        route.AddRange(assignedRoute);
        for (int i = 0; i < assignedStopPathIndices.Count; i++)
        {
            int index = assignedStopPathIndices[i];
            if (index > 0 && index < route.Count - 1)
                stopPathIndices.Add(index);
        }

        if (stopPathIndices.Count == 0)
        {
            route.Clear();
            return false;
        }

        ownerSpawner = spawner;
        pathIndex = 1;
        routeInitialized = true;
        routeTopologyVersion = roadNetwork.TopologyVersion;
        currentSpeed = 0f;
        debugPathLength = route.Count;
        InitializeSmoothedMovementPath();
        debugState = "Driving to first bus stop";
        transform.position = GetRoadWorldPosition(route[0]);
        return true;
    }

    private void Update()
    {
        if (!routeInitialized)
            return;

        if (roadNetwork != null &&
            routeTopologyVersion != roadNetwork.TopologyVersion)
        {
            RebuildRemainingJourney();
        }

        if (stopTimeRemaining > 0f)
        {
            currentSpeed = 0f;
            debugCurrentSpeed = 0f;
            stopTimeRemaining = Mathf.Max(
                0f, stopTimeRemaining - GameTimeManager.SimulationDeltaTime);
            if (stopTimeRemaining <= 0f)
                debugState = "Departing bus stop";
            return;
        }

        if (pathIndex >= route.Count)
        {
            CompleteJourney();
            return;
        }

        MoveAlongRoute();
    }

    private void MoveAlongRoute()
    {
        float remainingTime = GameTimeManager.SimulationDeltaTime;
        const int maxWaypointsPerUpdate = 64;

        for (int step = 0; step < maxWaypointsPerUpdate &&
                           remainingTime > 0.0001f &&
                           pathIndex < route.Count; step++)
        {
            if (ShouldWaitForIntersection())
            {
                debugState = "Waiting for intersection";
                currentSpeed = 0f;
                debugCurrentSpeed = 0f;
                debugTrafficDecision = "Waiting for intersection reservation";
                return;
            }

            currentSpeed = trafficFollowing.GetFollowingSpeed(
                movementSpeed, currentSpeed, route,
                roadNetwork, gridManager, hasIntersectionReservation,
                out float followingDistance);

            debugBlockingCollider = "None";
            debugBlockingVehicleState = "None";
            debugBlockingDistance = float.IsPositiveInfinity(followingDistance)
                ? 0f
                : followingDistance;
            debugTrafficDecision = float.IsPositiveInfinity(followingDistance)
                ? "No vehicle on the same route ahead"
                : $"Maintaining route gap: {followingDistance:F2}m";
            debugCurrentSpeed = currentSpeed;

            if (currentSpeed <= 0.001f)
                return;

            if (currentSmoothedWaypointIndex >= smoothedMovementWaypoints.Count)
            {
                CompleteJourney();
                return;
            }

            Vector3 target = smoothedMovementWaypoints[currentSmoothedWaypointIndex];
            Vector3 offset = target - transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;

            if (distance <= waypointTolerance)
            {
                transform.position = target;
                ArriveAtSmoothedWaypoint();
                if (stopTimeRemaining > 0f)
                    return;
                continue;
            }

            float timeToTarget = distance / currentSpeed;
            float segmentTime = Mathf.Min(remainingTime, timeToTarget);
            Vector3 direction = offset / distance;
            Quaternion targetRotation =
                Quaternion.LookRotation(direction, Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRotation,
                Mathf.Clamp01(rotationSpeed * segmentTime));

            if (segmentTime >= timeToTarget)
            {
                transform.position = target;
                ArriveAtSmoothedWaypoint();
                if (stopTimeRemaining > 0f)
                    return;
            }
            else
            {
                transform.position += direction * currentSpeed * segmentTime;
            }

            remainingTime -= segmentTime;
        }
    }

    private void ArriveAtSmoothedWaypoint()
    {
        int reachedRouteIndex = smoothedRouteIndices[
            currentSmoothedWaypointIndex];
        currentSmoothedWaypointIndex++;

        pathIndex = Mathf.Max(pathIndex, reachedRouteIndex);
        debugPathIndex = pathIndex;

        if (stopPathIndices.Contains(reachedRouteIndex))
        {
            completedStopRoadCells.Add(route[reachedRouteIndex]);
            currentSpeed = 0f;
            stopTimeRemaining = stopDwellSeconds;
            debugState = "At bus stop";
        }
        else if (currentSmoothedWaypointIndex >= smoothedMovementWaypoints.Count)
        {
            debugState = "Reached traffic exit";
        }
        else
        {
            debugState = "Driving to next bus stop / exit";
        }
    }

    private void RebuildRemainingJourney()
    {
        if (ownerSpawner == null ||
            !ownerSpawner.TryRebuildJourney(
                transform.position,
                completedStopRoadCells,
                out List<Vector2Int> rebuiltRoute,
                out List<int> rebuiltStopIndices))
        {
            routeTopologyVersion = roadNetwork.TopologyVersion;
            debugState = "Road topology changed; no replacement route found";
            currentSpeed = 0f;
            return;
        }

        if (intersectionManager != null)
            intersectionManager.CancelRequest(this);

        route.Clear();
        route.AddRange(rebuiltRoute);
        stopPathIndices.Clear();
        for (int i = 0; i < rebuiltStopIndices.Count; i++)
            stopPathIndices.Add(rebuiltStopIndices[i]);

        routeTopologyVersion = roadNetwork.TopologyVersion;
        pathIndex = route.Count > 1 ? 1 : route.Count;
        transform.position = GetRoadWorldPosition(route[0]);
        InitializeSmoothedMovementPath();
        currentSpeed = 0f;
        waitingForIntersection = false;
        hasIntersectionReservation = false;
        reservedIntersectionPathIndex = -1;
        stopTimeRemaining = 0f;
        debugPathLength = route.Count;
        debugPathIndex = pathIndex;
        debugState = stopPathIndices.Count > 0
            ? "Route recalculated after road changes"
            : "Road changed; proceeding to traffic exit";
    }

    private bool ShouldWaitForIntersection()
    {
        if (intersectionManager == null || pathIndex >= route.Count)
            return false;

        if (hasIntersectionReservation)
        {
            if (pathIndex >= reservedIntersectionPathIndex +
                             Mathf.Max(1, intersectionClearModules))
            {
                intersectionManager.Release(reservedIntersection, this);
                hasIntersectionReservation = false;
                reservedIntersectionPathIndex = -1;
            }
            else
            {
                return false;
            }
        }

        int nextIntersectionIndex = -1;
        Vector2Int nextIntersection = Vector2Int.zero;

        for (int i = pathIndex; i < route.Count; i++)
        {
            Vector2Int anchor = roadNetwork.GetRoadAnchor(route[i]);
            if (!roadNetwork.IsIntersection(anchor))
                continue;
            nextIntersectionIndex = i;
            nextIntersection = anchor;
            break;
        }

        if (nextIntersectionIndex < 0)
        {
            if (waitingForIntersection)
            {
                intersectionManager.CancelRequest(this);
                waitingForIntersection = false;
            }
            return false;
        }

        int holdingIndex = Mathf.Max(
            0, nextIntersectionIndex -
               Mathf.Max(0, intersectionLookAheadModules));

        if (pathIndex < holdingIndex)
            return false;

        if (waitingForIntersection &&
            requestedIntersection != nextIntersection)
        {
            intersectionManager.CancelRequest(this);
            waitingForIntersection = false;
        }

        if (!waitingForIntersection)
        {
            requestedIntersection = nextIntersection;
            waitingForIntersection = true;
            intersectionManager.RequestEntry(nextIntersection, this);
        }

        if (!intersectionManager.HasReservation(nextIntersection, this))
            return true;

        reservedIntersection = nextIntersection;
        reservedIntersectionPathIndex = nextIntersectionIndex;
        hasIntersectionReservation = true;
        waitingForIntersection = false;
        return false;
    }

    private void InitializeSmoothedMovementPath()
    {
        smoothedMovementWaypoints.Clear();
        smoothedRouteIndices.Clear();

        if (route.Count == 0)
            return;

        List<Vector3> original = new List<Vector3>(route.Count);
        for (int i = 0; i < route.Count; i++)
            original.Add(GetRoadWorldPosition(route[i]));

        AddSmoothedWaypoint(original[0], 0);

        float requestedRadius = turningRadiusInCells * gridManager.CellSize;
        const float bezierCircleFactor = 0.55228475f;
        int resolution = Mathf.Max(1, turnCurveResolution);

        for (int i = 1; i < original.Count - 1; i++)
        {
            Vector3 corner = original[i];
            Vector3 incoming = corner - original[i - 1];
            Vector3 outgoing = original[i + 1] - corner;
            incoming.y = 0f;
            outgoing.y = 0f;

            float radius = 0f;
            if (!stopPathIndices.Contains(i) &&
                incoming.sqrMagnitude > 0.001f &&
                outgoing.sqrMagnitude > 0.001f)
            {
                incoming.Normalize();
                outgoing.Normalize();
                float angle = Vector3.Angle(incoming, outgoing);
                if (angle >= 1f && angle <= 179f)
                {
                    float inLength = Vector3.Distance(original[i - 1], corner);
                    float outLength = Vector3.Distance(corner, original[i + 1]);
                    radius = Mathf.Min(requestedRadius, inLength * 0.5f,
                        outLength * 0.5f);
                }
            }

            if (radius <= 0.001f)
            {
                AddSmoothedWaypoint(corner, i);
                continue;
            }

            incoming = (corner - original[i - 1]).normalized;
            outgoing = (original[i + 1] - corner).normalized;
            incoming.y = 0f;
            outgoing.y = 0f;
            incoming.Normalize();
            outgoing.Normalize();

            Vector3 entry = corner - incoming * radius;
            Vector3 exit = corner + outgoing * radius;
            AddSmoothedWaypoint(entry, i);

            Vector3 control1 = entry + incoming * radius * bezierCircleFactor;
            Vector3 control2 = exit - outgoing * radius * bezierCircleFactor;

            for (int step = 1; step <= resolution; step++)
            {
                float t = step / (float)resolution;
                float u = 1f - t;
                Vector3 point =
                    u * u * u * entry +
                    3f * u * u * t * control1 +
                    3f * u * t * t * control2 +
                    t * t * t * exit;
                AddSmoothedWaypoint(point, i + 1);
            }
        }

        AddSmoothedWaypoint(original[original.Count - 1], original.Count - 1);
        currentSmoothedWaypointIndex =
            smoothedMovementWaypoints.Count > 1 ? 1 : 0;
    }

    private void AddSmoothedWaypoint(Vector3 position, int routeIndex)
    {
        if (smoothedMovementWaypoints.Count > 0 &&
            Vector3.Distance(smoothedMovementWaypoints[
                smoothedMovementWaypoints.Count - 1], position) <= 0.001f)
            return;

        smoothedMovementWaypoints.Add(position);
        smoothedRouteIndices.Add(routeIndex);
    }

    private Vector3 GetRoadWorldPosition(Vector2Int cell)
    {
        Vector3 position = roadNetwork.GetRoadCenterWorldPosition(
            cell, gridManager);
        position.y += heightOffset;
        return position;
    }

    private void CompleteJourney()
    {
        debugState = "Exited";
        routeInitialized = false;
        if (intersectionManager != null)
            intersectionManager.CancelRequest(this);
        if (ownerSpawner != null)
            ownerSpawner.NotifyBusExited(this);
        Destroy(gameObject);
    }

    private void OnDisable()
    {
        if (intersectionManager != null)
            intersectionManager.CancelRequest(this);
        if (trafficFollowing != null)
            trafficFollowing.enabled = false;
    }

    private void OnDestroy()
    {
        if (ownerSpawner != null)
            ownerSpawner.NotifyBusExited(this);
    }

    private void Log(object message)
    {
        if (logToConsole)
            Debug.Log(message, this);
    }
}
