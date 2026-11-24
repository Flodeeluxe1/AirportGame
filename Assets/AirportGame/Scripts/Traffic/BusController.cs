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

    [Header("Road Movement")]
    [Min(0.1f)] [SerializeField] private float movementSpeed = 5f;
    [Min(0.1f)] [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float heightOffset = 0.2f;
    [Min(0.01f)] [SerializeField] private float waypointTolerance = 0.08f;

    [Header("Stop Service")]
    [Min(0f)] [SerializeField] private float stopDwellSeconds = 5f;

    [Header("Intersection Control")]
    [Min(0)] [SerializeField] private int intersectionLookAheadModules = 1;
    [Min(1)] [SerializeField] private int intersectionClearModules = 2;

    [Header("Runtime")]
    [SerializeField] private string debugState = "Waiting for journey";
    [SerializeField] private int debugPathIndex;
    [SerializeField] private int debugPathLength;

    private readonly List<Vector2Int> route = new List<Vector2Int>();
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
                return;
            }

            currentSpeed = trafficFollowing.GetFollowingSpeed(
                movementSpeed, currentSpeed, route,
                roadNetwork, gridManager, hasIntersectionReservation, out _);

            if (currentSpeed <= 0.001f)
                return;

            Vector3 target = GetRoadWorldPosition(route[pathIndex]);
            Vector3 offset = target - transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;

            if (distance <= waypointTolerance)
            {
                transform.position = target;
                ArriveAtWaypoint();
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
                ArriveAtWaypoint();
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

    private void ArriveAtWaypoint()
    {
        int reachedIndex = pathIndex;
        pathIndex++;
        debugPathIndex = pathIndex;

        if (stopPathIndices.Contains(reachedIndex))
        {
            completedStopRoadCells.Add(route[reachedIndex]);
            currentSpeed = 0f;
            stopTimeRemaining = stopDwellSeconds;
            debugState = "At bus stop";
        }
        else if (pathIndex >= route.Count)
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
