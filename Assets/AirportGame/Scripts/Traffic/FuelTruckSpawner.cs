using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds aircraft fuel stations requesting stock and dispatches fuel tankers
/// through the road network to the shared Fuel Delivery Station location.
/// </summary>
public class FuelTruckSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject fuelTruckPrefab;
    [SerializeField] private Transform spawnPoint;
    [Tooltip("Place this marker on the road module where the tanker should stop beside the Fuel Delivery Station.")]
    [SerializeField] private Transform fuelDeliveryRoadPoint;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private RoadNetwork roadNetwork;

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
    }

    private void Start()
    {
        if (fuelTruckPrefab == null ||
            spawnPoint == null ||
            fuelDeliveryRoadPoint == null ||
            exitPoint == null ||
            gridManager == null ||
            roadNetwork == null)
        {
            Debug.LogError(
                "FuelTruckSpawner: Assign the tanker prefab, spawn point, " +
                "fuel-delivery road point, exit point, GridManager and RoadNetwork.",
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
                    out Vector2Int deliveryCell,
                    out Vector2Int endCell))
            {
                station.CompleteFuelDelivery();
                continue;
            }

            List<Vector2Int> outboundPath =
                roadNetwork.FindPath(startCell, deliveryCell);

            List<Vector2Int> returnPath =
                roadNetwork.FindPath(deliveryCell, endCell);

            if (outboundPath == null || outboundPath.Count == 0 ||
                returnPath == null || returnPath.Count == 0)
            {
                Debug.LogWarning(
                    "FuelTruckSpawner: No complete road route to the Fuel " +
                    "Delivery Station and exit. Delivery will be retried.",
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
                    "FuelTruckController component.",
                    truckObject);
                Destroy(truckObject);
                station.CompleteFuelDelivery();
                continue;
            }

            activeStations.Add(station);

            truck.InitializeDelivery(
                startCell,
                deliveryCell,
                endCell,
                station,
                Mathf.Min(tankerCapacityLitres, station.RemainingFuelCapacity),
                this);

            if (logDispatches)
            {
                Debug.Log(
                    $"FUEL TANKER DISPATCHED | Station: {station.name} | " +
                    $"Fuel: {Mathf.Min(tankerCapacityLitres, station.RemainingFuelCapacity):F0} L",
                    truckObject);
            }
        }
    }

    private bool TryGetRouteCells(
        out Vector2Int startCell,
        out Vector2Int deliveryCell,
        out Vector2Int endCell)
    {
        startCell = Vector2Int.zero;
        deliveryCell = Vector2Int.zero;
        endCell = Vector2Int.zero;

        if (gridManager == null || roadNetwork == null)
            return false;

        startCell = roadNetwork.GetRoadAnchor(
            gridManager.WorldToGrid(spawnPoint.position));

        deliveryCell = roadNetwork.GetRoadAnchor(
            gridManager.WorldToGrid(fuelDeliveryRoadPoint.position));

        endCell = roadNetwork.GetRoadAnchor(
            gridManager.WorldToGrid(exitPoint.position));

        return roadNetwork.IsRoad(startCell) &&
               roadNetwork.IsRoad(deliveryCell) &&
               roadNetwork.IsRoad(endCell);
    }

    public void NotifyTruckFinished(AircraftFuelStation station)
    {
        if (station != null)
            activeStations.Remove(station);
    }
}
