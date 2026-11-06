using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a fuel tanker along the road network, makes a short direct approach
/// to FuelTruckStopPoint, delivers fuel, then reverses that approach and exits.
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

    private readonly List<Vector2Int> route = new List<Vector2Int>();
    private int deliveryPathIndex = -1;
    private int pathIndex;
    private bool routeInitialized;
    private bool delivered;
    private bool deliveryClosed;

    private Transform stopPoint;
    private Vector3 stopWorldPosition;
    private Vector3 stationRoadWorldPosition;
    private bool approachingStopPoint;
    private bool returningToRoad;

    private AircraftFuelStation targetStation;
    private FuelTruckSpawner ownerSpawner;
    private float deliveryLitres;

    private bool waitingForIntersection;
    private bool hasIntersectionReservation;
    private Vector2Int requestedIntersection;
    private Vector2Int reservedIntersection;
    private int reservedIntersectionPathIndex = -1;

    private void Awake()
    {
        roadNetwork = FindFirstObjectByType<RoadNetwork>();
        gridManager = FindFirstObjectByType<GridManager>();
        intersectionManager = FindFirstObjectByType<IntersectionManager>();

        if (GetComponent<VehicleSpawnID>() == null)
            gameObject.AddComponent<VehicleSpawnID>();
    }

    public void InitializeDelivery(
        Vector2Int startCell,
        Vector2Int stationRoadCell,
        Vector2Int exitCell,
        Transform fuelTruckStopPoint,
        AircraftFuelStation station,
        float amountLitres,
        FuelTruckSpawner spawner)
    {
        if (routeInitialized)
            return;

        targetStation = station;
        ownerSpawner = spawner;
        deliveryLitres = amountLitres;
        stopPoint = fuelTruckStopPoint;

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (roadNetwork == null || gridManager == null ||
            targetStation == null || stopPoint == null ||
            deliveryLitres <= 0f)
        {
            CloseDeliveryRequest();
            Destroy(gameObject);
            return;
        }

        List<Vector2Int> outbound =
            roadNetwork.FindPath(startCell, stationRoadCell);

        List<Vector2Int> returning =
            roadNetwork.FindPath(stationRoadCell, exitCell);

        if (outbound == null || outbound.Count == 0 ||
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

        stationRoadWorldPosition = GetRoadWorldPosition(stationRoadCell);
        stopWorldPosition = stopPoint.position;
        stopWorldPosition.y += heightOffset;

        routeInitialized = true;
        pathIndex = 0;
        debugPathLength = route.Count + 2;
        debugState = "Driving to Fuel Delivery Station";

        transform.position = GetRoadWorldPosition(route[0]);
    }

    private void Update()
    {
        if (!routeInitialized || route.Count == 0)
            return;

        if (approachingStopPoint)
        {
            MoveToDirectPoint(stopWorldPosition);

            if (HasReachedDirectPoint(stopWorldPosition))
            {
                approachingStopPoint = false;
                returningToRoad = true;
                PerformFuelDelivery();
            }

            return;
        }

        if (returningToRoad)
        {
            MoveToDirectPoint(stationRoadWorldPosition);

            if (HasReachedDirectPoint(stationRoadWorldPosition))
            {
                returningToRoad = false;
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
            approachingStopPoint = true;
            debugState = "Approaching FuelTruckStopPoint";
            return;
        }

        MoveToCurrentWaypoint();

        debugPathIndex = pathIndex;
        if (pathIndex <= deliveryPathIndex)
            debugState = "Driving to Fuel Delivery Station";
        else
            debugState = "Driving to exit";
    }

    private void MoveToCurrentWaypoint()
    {
        Vector3 target = GetRoadWorldPosition(route[pathIndex]);
        MoveTowards(target);

        Vector3 offset = target - transform.position;
        offset.y = 0f;

        if (offset.magnitude <= waypointTolerance)
        {
            transform.position = target;
            pathIndex++;
        }
    }

    private void MoveToDirectPoint(Vector3 target)
    {
        MoveTowards(target);

        if (HasReachedDirectPoint(target))
            transform.position = target;
    }

    private void MoveTowards(Vector3 target)
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
            movementSpeed * Time.deltaTime);
    }

    private bool HasReachedDirectPoint(Vector3 target)
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
            nextIntersectionIndex -
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

    private void PerformFuelDelivery()
    {
        if (delivered)
            return;

        float accepted = targetStation != null
            ? targetStation.ReceiveFuelDelivery(deliveryLitres)
            : 0f;

        delivered = true;
        CloseDeliveryRequest();

        debugState = $"Delivered {accepted:F0} L";
        Debug.Log(
            $"FUEL TANKER DELIVERY COMPLETE | " +
            $"Station: {(targetStation != null ? targetStation.name : "Missing")} | " +
            $"Accepted: {accepted:F0} L",
            this);
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

        if (targetStation != null)
            targetStation.CompleteFuelDelivery();

        if (ownerSpawner != null)
            ownerSpawner.NotifyTruckFinished(targetStation);
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
