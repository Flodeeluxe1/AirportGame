using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Follows the road network to a selected Fuel Delivery Station, queues for
/// its stop point, then returns to the road and follows the route to the exit.
/// </summary>
[DisallowMultipleComponent]
public class FuelTruckController : MonoBehaviour
{
    [Header("Road Movement")]
    [Min(0.1f)]
    [SerializeField] private float movementSpeed = 5f;
    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float heightOffset = 0.2f;
    [Min(0.01f)]
    [SerializeField] private float waypointTolerance = 0.08f;

    [Header("Fuel Offloading")]
    [Tooltip("Fuel pumped into the airport fuel station per minute.")]
    [Min(0.1f)]
    [SerializeField] private float offloadRateLitresPerMinute = 1000f;

    [Header("Intersection Control")]
    [Min(0)]
    [SerializeField] private int intersectionLookAheadModules = 1;
    [Min(1)]
    [SerializeField] private int intersectionClearModules = 2;

    [Header("Runtime")]
    [SerializeField] private string debugState = "Waiting for route";
    [SerializeField] private int debugPathIndex;
    [SerializeField] private int debugPathLength;

    private RoadNetwork roadNetwork;
    private GridManager gridManager;
    private IntersectionManager intersectionManager;
    private TrafficFollowing trafficFollowing;

    private readonly List<Vector2Int> route = new List<Vector2Int>();
    private int deliveryPathIndex = -1;
    private int pathIndex;
    private bool routeInitialized;
    private bool delivered;
    private bool deliverySucceeded;
    private bool offloading;
    private bool deliveryClosed;
    private bool approachingStopPoint;
    private bool returningToRoad;

    private FuelDeliveryStation deliveryStation;
    private Transform stopPoint;
    private Vector3 stopWorldPosition;
    private Vector3 stationRoadWorldPosition;

    private PipelineNetworkManager.PipelineNetwork targetFuelNetwork;
    private PipelineNetworkManager networkManager;
    private int targetTopologyVersion;
    private float dispatchThresholdPercent;
    private FuelTruckSpawner ownerSpawner;
    private float deliveryLitres;
    private float deliveryLitresOffloaded;
    private float fuelPricePerLitre = 1f;
    private int prepaidFuelCost;
    private AirportEconomyManager economyManager;

    private bool waitingForIntersection;
    private bool hasIntersectionReservation;
    private float currentSpeed;

    public bool IsActiveOnRoad =>
        routeInitialized &&
        !approachingStopPoint &&
        !offloading &&
        !returningToRoad &&
        pathIndex < route.Count;
    private Vector2Int requestedIntersection;
    private Vector2Int reservedIntersection;
    private int reservedIntersectionPathIndex = -1;

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

    public void InitializeDelivery(
        List<Vector2Int> outbound,
        List<Vector2Int> returning,
        FuelDeliveryStation selectedStation,
        PipelineNetworkManager.PipelineNetwork fuelNetwork,
        float amountLitres,
        float pricePerLitre,
        FuelTruckSpawner spawner,
        PipelineNetworkManager manager,
        float refillThreshold,
        int topologyVersion)
    {
        if (routeInitialized)
            return;

        deliveryStation = selectedStation;
        targetFuelNetwork = fuelNetwork;
        networkManager = manager;
        dispatchThresholdPercent = refillThreshold;
        targetTopologyVersion = topologyVersion;
        ownerSpawner = spawner;
        deliveryLitres = amountLitres;
        fuelPricePerLitre = Mathf.Max(0.01f, pricePerLitre);
        economyManager = FindFirstObjectByType<AirportEconomyManager>();

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        stopPoint = deliveryStation != null
            ? deliveryStation.FuelTruckStopPoint
            : null;

        if (roadNetwork == null || gridManager == null ||
            deliveryStation == null || stopPoint == null ||
            targetFuelNetwork == null ||
            networkManager == null ||
            !IsTargetNetworkCurrent() ||
            deliveryLitres <= 0f ||
            outbound == null || outbound.Count == 0 ||
            returning == null || returning.Count == 0)
        {
            CloseDeliveryRequest();
            Destroy(gameObject);
            return;
        }

        route.AddRange(outbound);
        deliveryPathIndex = route.Count - 1;

        for (int i = 1; i < returning.Count; i++)
            route.Add(returning[i]);

        stationRoadWorldPosition = GetRoadWorldPosition(route[deliveryPathIndex]);
        stopWorldPosition = stopPoint.position;
        stopWorldPosition.y += heightOffset;

        routeInitialized = true;
        pathIndex = 0;
        currentSpeed = 0f;
        debugPathLength = route.Count + 2;
        debugState = "Driving to delivery station";

        transform.position = GetRoadWorldPosition(route[0]);
    }

    private void Update()
    {
        if (!routeInitialized || route.Count == 0)
            return;

        if (approachingStopPoint)
        {
            MoveTowards(stopWorldPosition);
            if (HasReached(stopWorldPosition))
            {
                transform.position = stopWorldPosition;
                FaceStopPoint();
                approachingStopPoint = false;
                BeginFuelOffloading();
            }
            return;
        }

        if (offloading)
        {
            UpdateFuelOffloading();
            return;
        }

        if (returningToRoad)
        {
            if (!TurnTowardsRoad(stationRoadWorldPosition))
                return;

            MoveTowards(stationRoadWorldPosition);
            if (HasReached(stationRoadWorldPosition))
            {
                transform.position = stationRoadWorldPosition;
                returningToRoad = false;
                deliveryStation.ReleaseTruck(this);
                pathIndex = deliveryPathIndex + 1;
                debugPathIndex = pathIndex;
                debugState = "Driving to exit";
            }
            return;
        }

        if (pathIndex >= route.Count)
        {
            debugState = "Exited";
            CloseDeliveryRequest();
            Destroy(gameObject);
            return;
        }

        if (ShouldWaitForIntersection())
        {
            debugState = "Waiting for intersection";
            return;
        }

        if (pathIndex == deliveryPathIndex && !delivered)
        {
            if (!deliveryStation.TryOccupyStop(this))
            {
                debugState = "Queued for delivery station";
                return;
            }

            approachingStopPoint = true;
            debugState = "Approaching FuelTruckStopPoint";
            return;
        }

        MoveToCurrentWaypoint();
        debugPathIndex = pathIndex;

        if (pathIndex <= deliveryPathIndex)
            debugState = "Driving to delivery station";
        else
            debugState = "Driving to exit";
    }

    private void MoveToCurrentWaypoint()
    {
        Vector3 target = GetRoadWorldPosition(route[pathIndex]);
        float trafficAwareSpeed = GetTrafficFollowingSpeed();
        MoveTowards(target, trafficAwareSpeed);

        if (HasReached(target))
        {
            transform.position = target;
            pathIndex++;
        }
    }

    private float GetTrafficFollowingSpeed()
    {
        currentSpeed = trafficFollowing.GetFollowingSpeed(
            movementSpeed,
            currentSpeed,
            ShouldBlockTrafficCollider,
            hasIntersectionReservation,
            out _);

        return currentSpeed;
    }

    private bool ShouldBlockTrafficCollider(Collider collider)
    {
        if (collider == null)
            return false;

        TrafficTestVehicle normalVehicle =
            collider.GetComponentInParent<TrafficTestVehicle>();

        if (normalVehicle != null)
        {
            TrafficTestVehicle.VehicleState otherState =
                normalVehicle.CurrentState;

            return otherState != TrafficTestVehicle.VehicleState.Idle &&
                   otherState != TrafficTestVehicle.VehicleState.Parked &&
                   otherState != TrafficTestVehicle.VehicleState.Arrived;
        }

        CargoDeliveryTruck cargoTruck =
            collider.GetComponentInParent<CargoDeliveryTruck>();

        if (cargoTruck != null)
        {
            CargoDeliveryTruck.TruckState otherState =
                cargoTruck.CurrentState;

            return otherState != CargoDeliveryTruck.TruckState.Idle &&
                   otherState != CargoDeliveryTruck.TruckState.WaitingForCargoZone &&
                   otherState != CargoDeliveryTruck.TruckState.Unloading &&
                   otherState != CargoDeliveryTruck.TruckState.Arrived;
        }

        GarbageCollectorController garbageCollector =
            collider.GetComponentInParent<GarbageCollectorController>();

        if (garbageCollector != null)
            return garbageCollector.IsActiveOnRoad;

        FuelTruckController fuelTruck =
            collider.GetComponentInParent<FuelTruckController>();

        return fuelTruck != null && fuelTruck != this &&
               fuelTruck.IsActiveOnRoad;
    }

    public float CurrentSpeed => currentSpeed;

    public PipelineNetworkManager.PipelineNetwork TargetNetwork =>
        targetFuelNetwork;

    public string DeliveryStationName =>
        deliveryStation != null ? deliveryStation.name : "Unassigned";

    public string CurrentDeliveryStatus => debugState;

    public bool HasActiveDelivery =>
        !deliveryClosed && targetFuelNetwork != null;

    public float IncomingFuelLitres =>
        !deliveryClosed && !delivered
            ? Mathf.Max(0f, deliveryLitres - deliveryLitresOffloaded)
            : 0f;

    private void MoveTowards(Vector3 target)
    {
        MoveTowards(target, movementSpeed);
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
            speed * Time.deltaTime);
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

    private bool TurnTowardsRoad(Vector3 roadPosition)
    {
        Vector3 direction = roadPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return true;

        Quaternion targetRotation =
            Quaternion.LookRotation(direction.normalized, Vector3.up);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * 90f * Time.deltaTime);

        return Quaternion.Angle(transform.rotation, targetRotation) <= 1f;
    }

    private bool HasReached(Vector3 target)
    {
        Vector3 offset = target - transform.position;
        offset.y = 0f;
        return offset.magnitude <= waypointTolerance;
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
        int lastIndexToCheck = pathIndex <= deliveryPathIndex
            ? deliveryPathIndex
            : route.Count - 1;

        for (int i = pathIndex; i <= lastIndexToCheck; i++)
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

    private bool IsTargetNetworkCurrent()
    {
        return networkManager != null &&
               networkManager.TopologyVersion == targetTopologyVersion &&
               networkManager.IsNetworkCurrent(targetFuelNetwork);
    }

    private void BeginFuelOffloading()
    {
        if (delivered || offloading)
            return;

        if (!IsTargetNetworkCurrent())
        {
            debugState = "Delivery cancelled: pipeline topology changed";
            FinishFuelOffloading(0f);
            return;
        }

        float currentFuelPercentage =
            targetFuelNetwork.CapacityLitres > 0f
                ? targetFuelNetwork.CurrentFuelLitres /
                  targetFuelNetwork.CapacityLitres * 100f
                : 100f;

        // Do not offload if the network recovered while this tanker was travelling.
        if (currentFuelPercentage > dispatchThresholdPercent)
        {
            debugState = "Delivery cancelled: network no longer needs fuel";
            FinishFuelOffloading(0f);
            return;
        }

        if (economyManager == null)
            economyManager = FindFirstObjectByType<AirportEconomyManager>();

        if (economyManager == null || targetFuelNetwork == null)
        {
            Debug.LogError(
                "FuelTruckController: AirportEconomyManager or target pipeline network is missing.",
                this);
            FinishFuelOffloading(0f);
            return;
        }

        float affordableLitres = Mathf.Min(
            deliveryLitres,
            targetFuelNetwork.RemainingCapacityLitres,
            Mathf.Floor(economyManager.Balance / fuelPricePerLitre));

        if (affordableLitres <= 0f)
        {
            debugState = "Delivery cancelled: insufficient airport funds";
            FinishFuelOffloading(0f);
            return;
        }

        int cost = Mathf.CeilToInt(affordableLitres * fuelPricePerLitre);
        if (!economyManager.TrySpend(cost, $"Fuel delivery ({affordableLitres:F0} litres)"))
        {
            FinishFuelOffloading(0f);
            return;
        }

        deliveryLitres = affordableLitres;
        prepaidFuelCost = cost;
        deliveryLitresOffloaded = 0f;
        offloading = true;
        debugState = $"Offloading fuel: 0/{deliveryLitres:F0} L";
    }

    private void UpdateFuelOffloading()
    {
        if (!IsTargetNetworkCurrent())
        {
            // Any fuel already transferred is reconciled by the network manager
            // during its rebuild, so charge only for that accepted amount.
            debugState = "Delivery stopped: pipeline topology changed";
            FinishFuelOffloading(deliveryLitresOffloaded);
            return;
        }

        float remaining = deliveryLitres - deliveryLitresOffloaded;
        float amountThisFrame = Mathf.Min(
            remaining,
            offloadRateLitresPerMinute * Time.deltaTime / 60f);

        float acceptedThisFrame = 0f;

        if (amountThisFrame > 0f)
        {
            acceptedThisFrame =
                targetFuelNetwork.AddFuel(amountThisFrame);
            deliveryLitresOffloaded += acceptedThisFrame;
        }

        bool requestedAmountDelivered =
            deliveryLitresOffloaded >= deliveryLitres - 0.05f;

        bool networkFull =
            targetFuelNetwork.RemainingCapacityLitres <= 0.05f;

        bool transferCannotContinue =
            amountThisFrame > 0f && acceptedThisFrame <= 0.0001f;

        if (requestedAmountDelivered || networkFull || transferCannotContinue)
            FinishFuelOffloading(deliveryLitresOffloaded);
        else
            debugState = $"Offloading fuel: {deliveryLitresOffloaded:F0}/{deliveryLitres:F0} L";
    }

    private void FinishFuelOffloading(float deliveredLitres)
    {
        offloading = false;
        delivered = true;
        deliverySucceeded = deliveredLitres > 0.05f;

        RefundUndeliveredFuel(deliveredLitres);
        returningToRoad = true;
        debugState = deliverySucceeded
            ? $"Delivered {deliveredLitres:F0} L"
            : "Delivery cancelled without transferring fuel";

        if (deliveredLitres > 0f)
        {
            Debug.Log(
                $"FUEL TANKER DELIVERY COMPLETE | Pipeline network reserve replenished | " +
                $"Delivery station: {deliveryStation.name} | " +
                $"Accepted: {deliveredLitres:F0} L | " +
                $"Fuel cost: ${(deliveredLitres * fuelPricePerLitre):N0}",
                this);
        }
    }

    private void RefundUndeliveredFuel(float deliveredLitres)
    {
        if (prepaidFuelCost <= 0)
            return;

        int actualCost = Mathf.CeilToInt(
            Mathf.Max(0f, deliveredLitres) * fuelPricePerLitre);
        int refund = Mathf.Max(0, prepaidFuelCost - actualCost);

        if (refund > 0 && economyManager != null)
            economyManager.AddRevenue(refund, "Refund for undelivered fuel");

        prepaidFuelCost = 0;
    }

    private Vector3 GetRoadWorldPosition(Vector2Int cell)
    {
        Vector3 position =
            roadNetwork.GetRoadCenterWorldPosition(cell, gridManager);
        position.y += heightOffset;
        return position;
    }

    private void CloseDeliveryRequest()
    {
        if (deliveryClosed)
            return;

        deliveryClosed = true;

        if (!delivered)
        {
            RefundUndeliveredFuel(deliveryLitresOffloaded);
        }

        if (deliveryStation != null)
            deliveryStation.ReleaseTruck(this);

        if (ownerSpawner != null)
            ownerSpawner.NotifyTruckFinished(
                targetFuelNetwork,
                this,
                deliverySucceeded);
    }

    private void OnDisable()
    {
        if (intersectionManager != null)
            intersectionManager.CancelRequest(this);

        hasIntersectionReservation = false;
        waitingForIntersection = false;
        CloseDeliveryRequest();
    }

    private void OnDestroy()
    {
        CloseDeliveryRequest();
    }
}
