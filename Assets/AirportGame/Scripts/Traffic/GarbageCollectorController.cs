using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collects waste from multiple WasteDisposal locations using the airport's
/// shared road, traffic-following and intersection systems.
/// </summary>
[DisallowMultipleComponent]
public class GarbageCollectorController : MonoBehaviour
{
    public enum CollectorState
    {
        WaitingForRoute,
        DrivingToDisposal,
        ApproachingDisposal,
        Collecting,
        ReturningToRoad,
        DrivingToExit,
        Exited
    }

    [Header("Road Movement")]
    [Min(0.1f)]
    [SerializeField] private float movementSpeed = 4.5f;
    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float heightOffset = 0.2f;
    [Min(0.01f)]
    [SerializeField] private float waypointTolerance = 0.08f;

    [Header("Waste Collection")]
    [Min(1f)]
    [SerializeField] private float maximumWasteCapacity = 500f;
    [Tooltip("Waste units collected per game-time second.")]
    [Min(0.01f)]
    [SerializeField] private float collectionRatePerSecond = 5f;

    [Header("Intersection Control")]
    [Min(0)]
    [SerializeField] private int intersectionLookAheadModules = 1;
    [Min(1)]
    [SerializeField] private int intersectionClearModules = 2;

    [Header("Runtime")]
    [SerializeField] private CollectorState state = CollectorState.WaitingForRoute;
    [SerializeField] private float currentWasteLoad;
    [SerializeField] private string debugState = "Waiting for route";
    [SerializeField] private int debugPathIndex;
    [SerializeField] private int debugPathLength;

    private GarbageCollectorSpawner ownerSpawner;
    private RoadNetwork roadNetwork;
    private GridManager gridManager;
    private IntersectionManager intersectionManager;
    private TrafficFollowing trafficFollowing;

    private readonly List<Vector2Int> route = new List<Vector2Int>();
    private int pathIndex;
    private Vector2Int currentRoadCell;
    private Vector2Int disposalRoadCell;

    private WasteDisposal currentDisposal;
    private Transform stopPoint;
    private Vector3 stopWorldPosition;
    private Vector3 disposalRoadWorldPosition;

    private float collectionRemaining;
    private bool initialized;
    private bool hasIntersectionReservation;
    private bool waitingForIntersection;
    private Vector2Int requestedIntersection;
    private Vector2Int reservedIntersection;
    private int reservedIntersectionPathIndex = -1;
    private float currentSpeed;
    private bool notifiedSpawner;

    public CollectorState CurrentState => state;
    public WasteDisposal CurrentDisposal => currentDisposal;
    public float CurrentWasteLoad => currentWasteLoad;
    public float MaximumWasteCapacity => Mathf.Max(1f, maximumWasteCapacity);
    public float RemainingWasteCapacity =>
        Mathf.Max(0f, MaximumWasteCapacity - currentWasteLoad);
    public bool IsFull => RemainingWasteCapacity <= 0.001f;

    public bool IsActiveOnRoad =>
        initialized &&
        (state == CollectorState.DrivingToDisposal ||
         state == CollectorState.DrivingToExit);

    private void Awake()
    {
        roadNetwork = FindFirstObjectByType<RoadNetwork>();
        gridManager = FindFirstObjectByType<GridManager>();
        intersectionManager = FindFirstObjectByType<IntersectionManager>();

        trafficFollowing = GetComponent<TrafficFollowing>();
        if (trafficFollowing == null)
            trafficFollowing = gameObject.AddComponent<TrafficFollowing>();

        if (GetComponent<VehicleSpawnID>() == null)
            gameObject.AddComponent<VehicleSpawnID>();
    }

    public bool Initialize(
        GarbageCollectorSpawner spawner,
        Vector2Int startCell,
        WasteDisposal firstDisposal,
        Vector2Int firstDisposalRoadCell,
        List<Vector2Int> firstRoute)
    {
        if (initialized)
            return false;

        ownerSpawner = spawner;

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();
        if (intersectionManager == null)
            intersectionManager = FindFirstObjectByType<IntersectionManager>();

        if (ownerSpawner == null || roadNetwork == null || gridManager == null ||
            firstDisposal == null || firstDisposal.GarbageCollectorStopPoint == null ||
            firstRoute == null || firstRoute.Count == 0)
        {
            Debug.LogError(
                "GarbageCollectorController: Missing spawner, road references, disposal stop point or route.",
                this);
            return false;
        }

        currentRoadCell = roadNetwork.GetRoadAnchor(startCell);
        transform.position = GetRoadWorldPosition(currentRoadCell);

        initialized = true;
        AssignDisposal(firstDisposal, firstDisposalRoadCell, firstRoute);
        return true;
    }

    private void Update()
    {
        if (!initialized)
            return;

        switch (state)
        {
            case CollectorState.DrivingToDisposal:
            case CollectorState.DrivingToExit:
                UpdateRoadTravel();
                break;

            case CollectorState.ApproachingDisposal:
                UpdateDisposalApproach();
                break;

            case CollectorState.Collecting:
                UpdateWasteCollection();
                break;

            case CollectorState.ReturningToRoad:
                UpdateReturnToRoad();
                break;
        }
    }

    private void AssignDisposal(
        WasteDisposal disposal,
        Vector2Int roadCell,
        List<Vector2Int> newRoute)
    {
        currentDisposal = disposal;
        disposalRoadCell = roadNetwork.GetRoadAnchor(roadCell);
        stopPoint = disposal.GarbageCollectorStopPoint;
        disposalRoadWorldPosition = GetRoadWorldPosition(disposalRoadCell);

        stopWorldPosition = stopPoint.position;
        stopWorldPosition.y += heightOffset;

        route.Clear();
        route.AddRange(newRoute);
        pathIndex = 0;
        currentSpeed = 0f;
        debugPathIndex = 0;
        debugPathLength = route.Count;
        state = CollectorState.DrivingToDisposal;
        debugState = $"Driving to {disposal.name}";
    }

    private void UpdateRoadTravel()
    {
        if (pathIndex >= route.Count)
        {
            if (state == CollectorState.DrivingToDisposal)
            {
                state = CollectorState.ApproachingDisposal;
                debugState = "Approaching GarbageCollectorStopPoint";
            }
            else
            {
                FinishAndDespawn();
            }
            return;
        }

        if (ShouldWaitForIntersection())
        {
            debugState = "Waiting for intersection";
            return;
        }

        Vector3 target = GetRoadWorldPosition(route[pathIndex]);
        float followingSpeed = trafficFollowing.GetFollowingSpeed(
            movementSpeed,
            currentSpeed,
            ShouldBlockTrafficCollider,
            hasIntersectionReservation,
            out _);

        currentSpeed = followingSpeed;
        MoveTowards(target, followingSpeed);

        if (HasReached(target))
        {
            transform.position = target;
            pathIndex++;
            debugPathIndex = pathIndex;

            if (pathIndex >= route.Count)
            {
                if (state == CollectorState.DrivingToDisposal)
                {
                    state = CollectorState.ApproachingDisposal;
                    debugState = "Approaching GarbageCollectorStopPoint";
                }
                else
                {
                    FinishAndDespawn();
                }
            }
        }
    }

    private void UpdateDisposalApproach()
    {
        if (currentDisposal == null || stopPoint == null)
        {
            BeginReturnToRoad();
            return;
        }

        MoveTowards(stopWorldPosition, movementSpeed);

        if (!HasReached(stopWorldPosition))
            return;

        transform.position = stopWorldPosition;
        FaceStopPoint();

        collectionRemaining = Mathf.Min(
            RemainingWasteCapacity,
            currentDisposal.CurrentWaste);

        if (collectionRemaining <= 0.001f)
        {
            BeginReturnToRoad();
            return;
        }

        state = CollectorState.Collecting;
        debugState = $"Collecting from {currentDisposal.name}";
    }

    private void UpdateWasteCollection()
    {
        if (currentDisposal == null || IsFull || collectionRemaining <= 0.001f)
        {
            BeginReturnToRoad();
            return;
        }

        float amountThisFrame = Mathf.Min(
            collectionRemaining,
            collectionRatePerSecond * Time.deltaTime);

        float collected = currentDisposal.CollectWaste(amountThisFrame);
        currentWasteLoad += collected;
        collectionRemaining -= collected;

        if (collected <= 0.0001f ||
            collectionRemaining <= 0.001f ||
            IsFull)
        {
            BeginReturnToRoad();
            return;
        }

        debugState =
            $"Collecting from {currentDisposal.name}: " +
            $"{currentWasteLoad:F1}/{MaximumWasteCapacity:F1} load";
    }

    private void BeginReturnToRoad()
    {
        if (state == CollectorState.ReturningToRoad)
            return;

        state = CollectorState.ReturningToRoad;
        debugState = "Returning to road";
    }

    private void UpdateReturnToRoad()
    {
        MoveTowards(disposalRoadWorldPosition, movementSpeed);

        if (!HasReached(disposalRoadWorldPosition))
            return;

        transform.position = disposalRoadWorldPosition;
        currentRoadCell = disposalRoadCell;

        if (ownerSpawner != null)
            ownerSpawner.ReleaseDisposal(currentDisposal);

        currentDisposal = null;
        stopPoint = null;

        if (!IsFull &&
            ownerSpawner != null &&
            ownerSpawner.TryReserveNextDisposal(
                currentRoadCell,
                false,
                0f,
                out WasteDisposal nextDisposal,
                out Vector2Int nextRoadCell,
                out List<Vector2Int> nextRoute))
        {
            AssignDisposal(nextDisposal, nextRoadCell, nextRoute);
            return;
        }

        BeginExitRoute();
    }

    private void BeginExitRoute()
    {
        if (ownerSpawner == null ||
            !ownerSpawner.TryGetExitRoute(currentRoadCell, out List<Vector2Int> exitRoute))
        {
            Debug.LogError(
                $"GarbageCollectorController: No road route from {currentRoadCell} to the shared exit point.",
                this);
            FinishAndDespawn();
            return;
        }

        route.Clear();
        route.AddRange(exitRoute);
        pathIndex = 0;
        currentSpeed = 0f;
        debugPathIndex = 0;
        debugPathLength = route.Count;
        state = CollectorState.DrivingToExit;
        debugState = $"Driving to exit with {currentWasteLoad:F1} waste units";
    }

    private bool ShouldBlockTrafficCollider(Collider collider)
    {
        if (collider == null)
            return false;

        TrafficTestVehicle normalVehicle =
            collider.GetComponentInParent<TrafficTestVehicle>();

        if (normalVehicle != null)
        {
            TrafficTestVehicle.VehicleState otherState = normalVehicle.CurrentState;
            return otherState != TrafficTestVehicle.VehicleState.Idle &&
                   otherState != TrafficTestVehicle.VehicleState.Parked &&
                   otherState != TrafficTestVehicle.VehicleState.Arrived;
        }

        CargoDeliveryTruck cargoTruck =
            collider.GetComponentInParent<CargoDeliveryTruck>();

        if (cargoTruck != null)
        {
            CargoDeliveryTruck.TruckState otherState = cargoTruck.CurrentState;
            return otherState != CargoDeliveryTruck.TruckState.Idle &&
                   otherState != CargoDeliveryTruck.TruckState.WaitingForCargoZone &&
                   otherState != CargoDeliveryTruck.TruckState.Unloading &&
                   otherState != CargoDeliveryTruck.TruckState.Arrived;
        }

        FuelTruckController fuelTruck =
            collider.GetComponentInParent<FuelTruckController>();

        if (fuelTruck != null)
            return fuelTruck != null && fuelTruck.IsActiveOnRoad;

        GarbageCollectorController otherCollector =
            collider.GetComponentInParent<GarbageCollectorController>();

        return otherCollector != null &&
               otherCollector != this &&
               otherCollector.IsActiveOnRoad;
    }

    private bool ShouldWaitForIntersection()
    {
        if (intersectionManager == null || route.Count == 0)
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
            0,
            nextIntersectionIndex - Mathf.Max(0, intersectionLookAheadModules));

        if (pathIndex < holdingIndex)
            return false;

        if (waitingForIntersection && requestedIntersection != nextIntersection)
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

        if (intersectionManager.HasReservation(nextIntersection, this))
        {
            reservedIntersection = nextIntersection;
            reservedIntersectionPathIndex = nextIntersectionIndex;
            hasIntersectionReservation = true;
            waitingForIntersection = false;
            return false;
        }

        return true;
    }

    private void MoveTowards(Vector3 target, float speed)
    {
        Vector3 offset = target - transform.position;
        offset.y = 0f;

        if (offset.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(offset.normalized, Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }

        Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
        transform.position = Vector3.MoveTowards(
            transform.position,
            flatTarget,
            Mathf.Max(0f, speed) * Time.deltaTime);
    }

    private void FaceStopPoint()
    {
        if (stopPoint == null)
            return;

        Vector3 forward = stopPoint.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private bool HasReached(Vector3 target)
    {
        Vector3 offset = target - transform.position;
        offset.y = 0f;
        return offset.magnitude <= waypointTolerance;
    }

    private Vector3 GetRoadWorldPosition(Vector2Int cell)
    {
        Vector3 position =
            roadNetwork.GetRoadCenterWorldPosition(cell, gridManager);
        position.y += heightOffset;
        return position;
    }

    private void FinishAndDespawn()
    {
        if (state == CollectorState.Exited)
            return;

        state = CollectorState.Exited;
        debugState = $"Exited with {currentWasteLoad:F1} waste units";
        ReleaseIntersectionReservation();

        if (!notifiedSpawner && ownerSpawner != null)
        {
            notifiedSpawner = true;
            ownerSpawner.NotifyCollectorFinished(this);
        }

        Destroy(gameObject);
    }

    private void ReleaseIntersectionReservation()
    {
        if (intersectionManager != null)
            intersectionManager.CancelRequest(this);

        hasIntersectionReservation = false;
        waitingForIntersection = false;
        reservedIntersectionPathIndex = -1;
    }

    private void OnDisable()
    {
        ReleaseIntersectionReservation();

        if (ownerSpawner != null)
        {
            ownerSpawner.ReleaseDisposal(currentDisposal);

            if (!notifiedSpawner)
            {
                notifiedSpawner = true;
                ownerSpawner.NotifyCollectorFinished(this);
            }
        }
    }

    private void OnDestroy()
    {
        ReleaseIntersectionReservation();

        if (ownerSpawner != null)
        {
            ownerSpawner.ReleaseDisposal(currentDisposal);

            if (!notifiedSpawner)
            {
                notifiedSpawner = true;
                ownerSpawner.NotifyCollectorFinished(this);
            }
        }
    }
}
