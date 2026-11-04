using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class IncomingCargoCollectionSpawner : MonoBehaviour
{
    private const float RetryInterval = 0.5f;

    private GameObject cargoTruckPrefab;
    private Transform spawnPoint;
    private Transform exitPoint;
    private GridManager gridManager;
    private RoadNetwork roadNetwork;
    private int roadModuleSize = 2;
    private float spawnHeightOffset;
    private bool logTruckSpawns;

    private readonly HashSet<CargoZone> pendingZones =
        new HashSet<CargoZone>();

    private readonly List<CargoZone> pendingZoneBuffer =
        new List<CargoZone>();

    private Vector2Int startCell;
    private Vector2Int exitCell;
    private Vector3 spawnPosition;
    private float nextRetryTime;
    private bool initialized;
    private bool roadSetupWarningShown;

    private void OnEnable()
    {
        CargoZone.AvailabilityChanged += QueueZone;

        if (initialized)
            QueueRegisteredZones();
    }

    private void OnDisable()
    {
        CargoZone.AvailabilityChanged -= QueueZone;
        pendingZones.Clear();
        pendingZoneBuffer.Clear();
    }

    public void Initialize(
        GameObject truckPrefab,
        Transform truckSpawnPoint,
        Transform roadExitPoint,
        GridManager assignedGridManager,
        RoadNetwork assignedRoadNetwork,
        int assignedRoadModuleSize,
        float assignedSpawnHeightOffset,
        bool logSpawns)
    {
        cargoTruckPrefab = truckPrefab;
        spawnPoint = truckSpawnPoint;
        exitPoint = roadExitPoint;
        gridManager = assignedGridManager;
        roadNetwork = assignedRoadNetwork;
        roadModuleSize = Mathf.Max(1, assignedRoadModuleSize);
        spawnHeightOffset = assignedSpawnHeightOffset;
        logTruckSpawns = logSpawns;

        if (cargoTruckPrefab == null ||
            spawnPoint == null ||
            exitPoint == null ||
            gridManager == null ||
            roadNetwork == null)
        {
            Debug.LogError(
                "IncomingCargoCollectionSpawner: Required references are missing.",
                this
            );

            enabled = false;
            return;
        }

        // These marker positions do not change during normal operation.
        // Road availability itself is still checked before each spawn.
        startCell = GetRoadModuleAnchor(
            gridManager.WorldToGrid(spawnPoint.position)
        );

        exitCell = GetRoadModuleAnchor(
            gridManager.WorldToGrid(exitPoint.position)
        );

        spawnPosition = GetRoadModuleCenter(startCell);
        spawnPosition.y += spawnHeightOffset;

        initialized = true;
        nextRetryTime = 0f;
        QueueRegisteredZones();
    }

    private void Update()
    {
        if (!initialized ||
            pendingZones.Count == 0 ||
            Time.unscaledTime < nextRetryTime)
        {
            return;
        }

        nextRetryTime = Time.unscaledTime + RetryInterval;
        ProcessPendingZones();
    }

    private void QueueRegisteredZones()
    {
        foreach (CargoZone zone in CargoZone.RegisteredZones)
            QueueZone(zone);
    }

    private void QueueZone(CargoZone zone)
    {
        if (initialized && zone != null)
            pendingZones.Add(zone);
    }

    private void ProcessPendingZones()
    {
        pendingZoneBuffer.Clear();
        pendingZoneBuffer.AddRange(pendingZones);

        foreach (CargoZone zone in pendingZoneBuffer)
        {
            if (zone == null)
            {
                pendingZones.Remove(zone);
                continue;
            }

            if (CargoDeliveryTruck.HasActiveIncomingCollection(zone) ||
                !zone.HasAvailablePackages(CargoDirection.Incoming))
            {
                pendingZones.Remove(zone);
                continue;
            }

            if (!roadNetwork.IsRoad(startCell) ||
                !roadNetwork.IsRoad(exitCell))
            {
                if (!roadSetupWarningShown)
                {
                    Debug.LogWarning(
                        "IncomingCargoCollectionSpawner: Spawn or exit marker " +
                        "is not on a valid road module. Incoming cargo remains queued.",
                        this
                    );

                    roadSetupWarningShown = true;
                }

                continue;
            }

            GameObject truckObject = Instantiate(
                cargoTruckPrefab,
                spawnPosition,
                spawnPoint.rotation
            );

            CargoDeliveryTruck truck =
                truckObject.GetComponent<CargoDeliveryTruck>();

            if (truck == null)
            {
                Debug.LogError(
                    "IncomingCargoCollectionSpawner: Cargo Truck Prefab " +
                    "does not have CargoDeliveryTruck attached. " +
                    "Disabling incoming collection spawning.",
                    truckObject
                );

                Destroy(truckObject);
                enabled = false;
                return;
            }

            if (!truck.InitializeIncomingCollectionRoute(
                    startCell,
                    exitCell,
                    zone))
            {
                Destroy(truckObject);

                if (CargoDeliveryTruck.HasActiveIncomingCollection(zone) ||
                    !zone.HasAvailablePackages(CargoDirection.Incoming))
                {
                    pendingZones.Remove(zone);
                }

                continue;
            }

            pendingZones.Remove(zone);

            if (logTruckSpawns)
            {
                int packageCount =
                    zone.CountAvailablePackages(CargoDirection.Incoming);

                Debug.Log(
                    "INCOMING CARGO COLLECTION VAN SPAWNED | " +
                    "Cargo zone: " + zone.name + " | " +
                    "Packages waiting: " + packageCount + " | " +
                    "Start module: " + startCell + " | " +
                    "Exit module: " + exitCell,
                    truckObject
                );
            }
        }
    }

    private Vector2Int GetRoadModuleAnchor(Vector2Int cell)
    {
        int anchorX =
            Mathf.FloorToInt((float)cell.x / roadModuleSize) *
            roadModuleSize;

        int anchorY =
            Mathf.FloorToInt((float)cell.y / roadModuleSize) *
            roadModuleSize;

        return new Vector2Int(anchorX, anchorY);
    }

    private Vector3 GetRoadModuleCenter(Vector2Int anchor)
    {
        Vector3 moduleOrigin = gridManager.GridToWorld(anchor);

        float moduleWorldSize =
            gridManager.CellSize * roadModuleSize;

        moduleOrigin.x += moduleWorldSize * 0.5f;
        moduleOrigin.z += moduleWorldSize * 0.5f;

        return moduleOrigin;
    }
}
