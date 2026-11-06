using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a fuel tanker from the road spawn point to the Fuel Delivery
/// Station, transfers fuel to the requesting AircraftFuelStation, and exits.
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

    private AircraftFuelStation targetStation;
    private FuelTruckSpawner ownerSpawner;
    private float deliveryLitres;

    private bool waitingForIntersection;
    private bool hasIntersectionReservation;
    private Vector2Int requestedIntersection;
    private Vector2Int reservedIntersection;
    private int reservedIntersectionPathIndex = -1;

    private float currentSpeed;

    private void Awake()
    {
        roadNetwork = FindFirstObjectByType<RoadNetwork>();
        gridManager = FindFirstObjectByType<GridManager>();
        intersectionManager = FindFirstObjectByType<IntersectionManager>();

        if (GetComponent<VehicleSpawnID>() == null)
            gameObject.AddComponent<VehicleSpawnID>();

        currentSpeed = movementSpeed;
    }

    public void InitializeDelivery(
        Vector2Int startCell,
        Vector2Int deliveryCell,
        Vector2Int exitCell,
        AircraftFuelStation station,
        float amountLitres,
        FuelTruckSpawner spawner)
    {
        if (routeInitialized)
            return;

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (roadNetwork == null || gridManager == null ||
            station == null || amountLitres <= 0f)
        {
            CloseDeliveryRequest();
            Destroy(gameObject);
            return;
        }

        List<Vector2Int> outbound =
            roadNetwork.FindPath(startCell, deliveryCell);

        List<Vector2Int> returning =
            roadNetwork.FindPath(deliveryCell, exitCell);

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

        targetStation = station;
        deliveryLitres = amountLitres;
        ownerSpawner = spawner;
        routeInitialized = true;
        pathIndex = 0;
        debugPathLength = route.Count;
        debugState = "Driving to Fuel Delivery Station";

        transform.position =
            GetRoadWorldPosition(route[0]);
    }

    private void Update()
    {
        if (!routeInitialized || route.Count == 0)
            return;

        if (pathIndex == deliveryPathIndex && !delivered)
        {
            PerformFuelDelivery();
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
            currentSpeed = 0f;
            debugState = "Waiting for intersection";
            return;
        }

        MoveToCurrentWaypoint();

        debugPathIndex = pathIndex;
        if (pathIndex < deliveryPathIndex)
            debugState = "Driving to Fuel Delivery Station";
        else
            debugState = "Driving to exit";
    }

    private void MoveToCurrentWaypoint()
    {
        Vector3 target = GetRoadWorldPosition(route[pathIndex]);
        Vector3 offset = target - transform.position;
        offset.y = 0f;

        if (offset.magnitude <= waypointTolerance)
        {
            transform.position = target;
            pathIndex++;
            currentSpeed = movementSpeed;
            return;
        }

        if (offset.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(offset.normalized, Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            target,
            movementSpeed * Time.deltaTime);
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
        if (!delivered)
        {
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

        // The next Update continues along the return portion of the route.
        pathIndex++;
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
    }

    private void OnDestroy()
    {
        CloseDeliveryRequest();
    }
}
