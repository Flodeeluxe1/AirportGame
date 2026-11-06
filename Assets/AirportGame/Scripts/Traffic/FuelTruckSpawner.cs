using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds aircraft fuel stations requesting stock and dispatches fuel tankers
/// through the road network to the Fuel Delivery Station's stop point.
/// </summary>
public class FuelTruckSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject fuelTruckPrefab;
    [SerializeField] private Transform spawnPoint;
    [Tooltip("Child object named FuelTruckStopPoint on the Fuel Delivery Station.")]
    [SerializeField] private Transform fuelTruckStopPoint;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private RoadNetwork roadNetwork;

    [Header("Road Approach")]
    [Tooltip("Maximum distance from FuelTruckStopPoint to the road module used for the final approach.")]
    [Min(0.1f)]
    [SerializeField] private float maximumRoadApproachDistance = 8f;

    [Header("Fuel Delivery")]
    [Min(1f)]
    [SerializeField] private float tankerCapacityLitres = 5000f;

    [Header("Spawner")]
    [Min(0.1f)]
    [SerializeField] private float scanInterval = 2f;
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Diagnostics")]
    [SerializeField] private bool logDispatches = true;

    private readonly HashSet<AircraftFuelStation> activeStations =
        new HashSet<AircraftFuelStation>();

    private float scanTimer;

    private void Awake()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (fuelTruckStopPoint == null)
        {
            GameObject stopPointObject = GameObject.Find("FuelTruckStopPoint");
            if (stopPointObject != null)
                fuelTruckStopPoint = stopPointObject.transform;
        }
    }

    private void Start()
    {
        if (fuelTruckPrefab == null ||
            spawnPoint == null ||
            fuelTruckStopPoint == null ||
            exitPoint == null ||
            gridManager == null ||
            roadNetwork == null)
        {
            Debug.LogError(
                "FuelTruckSpawner: Assign the tanker prefab, spawn point, " +
                "FuelTruckStopPoint, exit point, GridManager and RoadNetwork.",
                this);
            enabled = false;
            return;
        }

        scanTimer = 0f;
    }

    private void Update()
    {
        scanTimer -= Time.deltaTime;
        if (scanTimer > 0f)
            return;

        scanTimer = Mathf.Max(0.1f, scanInterval);
        DispatchPendingDeliveries();
    }

    private void DispatchPendingDeliveries()
    {
        AircraftFuelStation[] stations =
            FindObjectsByType<AircraftFuelStation>(FindObjectsSortMode.None);

        foreach (AircraftFuelStation station in stations)
        {
            if (station == null ||
                !station.NeedsFuelDelivery ||
                activeStations.Contains(station))
            {
                continue;
            }

            if (station.RemainingFuelCapacity <= 0f)
                continue;

            if (!station.TryBeginFuelDelivery())
                continue;

            if (!TryGetRouteCells(
                    out Vector2Int startCell,
                    out Vector2Int stationRoadCell,
                    out Vector2Int endCell))
            {
                station.CompleteFuelDelivery();
                continue;
            }

            List<Vector2Int> outboundPath =
                roadNetwork.FindPath(startCell, stationRoadCell);

            List<Vector2Int> returnPath =
                roadNetwork.FindPath(stationRoadCell, endCell);

            if (outboundPath == null || outboundPath.Count == 0 ||
                returnPath == null || returnPath.Count == 0)
            {
                Debug.LogWarning(
                    "FuelTruckSpawner: No complete road route to the road " +
                    "module beside FuelTruckStopPoint and the exit. " +
                    "Delivery will be retried.",
                    this);
                station.CompleteFuelDelivery();
                continue;
            }

            Vector3 spawnPosition =
                roadNetwork.GetRoadCenterWorldPosition(startCell, gridManager);
            spawnPosition.y += spawnHeightOffset;

            GameObject truckObject = Instantiate(
                fuelTruckPrefab,
                spawnPosition,
                spawnPoint.rotation);

            FuelTruckController truck =
                truckObject.GetComponent<FuelTruckController>();

            if (truck == null)
            {
                Debug.LogError(
                    "FuelTruckSpawner: The assigned tanker prefab needs a " +
                    "FuelTruckController component on its root object.",
                    truckObject);
                Destroy(truckObject);
                station.CompleteFuelDelivery();
                continue;
            }

            float amountToDeliver =
                Mathf.Min(tankerCapacityLitres, station.RemainingFuelCapacity);

            activeStations.Add(station);

            truck.InitializeDelivery(
                startCell,
                stationRoadCell,
                endCell,
                fuelTruckStopPoint,
                station,
                amountToDeliver,
                this);

            if (logDispatches)
            {
                Debug.Log(
                    $"FUEL TANKER DISPATCHED | Station: {station.name} | " +
                    $"Fuel: {amountToDeliver:F0} L | " +
                    $"Approach road: {stationRoadCell}",
                    truckObject);
            }
        }
    }

    private bool TryGetRouteCells(
        out Vector2Int startCell,
        out Vector2Int stationRoadCell,
        out Vector2Int endCell)
    {
        startCell = Vector2Int.zero;
        stationRoadCell = Vector2Int.zero;
        endCell = Vector2Int.zero;

        if (gridManager == null || roadNetwork == null ||
            fuelTruckStopPoint == null)
            return false;

        startCell = roadNetwork.GetRoadAnchor(
            gridManager.WorldToGrid(spawnPoint.position));

        endCell = roadNetwork.GetRoadAnchor(
            gridManager.WorldToGrid(exitPoint.position));

        if (!roadNetwork.IsRoad(startCell) || !roadNetwork.IsRoad(endCell))
            return false;

        List<Vector2Int> roadPositions = roadNetwork.GetRoadPositions();
        float bestDistance = float.MaxValue;
        bool foundCandidate = false;

        foreach (Vector2Int candidate in roadPositions)
        {
            Vector3 candidatePosition =
                roadNetwork.GetRoadCenterWorldPosition(candidate, gridManager);

            float distance = Vector3.Distance(
                candidatePosition,
                fuelTruckStopPoint.position);

            if (distance > maximumRoadApproachDistance ||
                distance >= bestDistance)
                continue;

            List<Vector2Int> outbound =
                roadNetwork.FindPath(startCell, candidate);

            if (outbound == null || outbound.Count == 0)
                continue;

            List<Vector2Int> returning =
                roadNetwork.FindPath(candidate, endCell);

            if (returning == null || returning.Count == 0)
                continue;

            bestDistance = distance;
            stationRoadCell = candidate;
            foundCandidate = true;
        }

        return foundCandidate;
    }

    public void NotifyTruckFinished(AircraftFuelStation station)
    {
        if (station != null)
            activeStations.Remove(station);
    }
}
