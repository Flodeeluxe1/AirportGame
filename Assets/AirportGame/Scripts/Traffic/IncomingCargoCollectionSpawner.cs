using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class IncomingCargoCollectionSpawner : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    private const long RetryIntervalGameSeconds = 1L;

    private GameObject cargoTruckPrefab;
    private Transform spawnPoint;
    private Transform exitPoint;
    private GridManager gridManager;
    private RoadNetwork roadNetwork;
    private int roadModuleSize = 2;
    private float spawnHeightOffset;
    private bool logTruckSpawns;
    private GameTimeManager gameTimeManager;

    private readonly HashSet<CargoZone> pendingZones =
        new HashSet<CargoZone>();

    private readonly List<CargoZone> pendingZoneBuffer =
        new List<CargoZone>();

    private readonly HashSet<string> pendingOutgoingRecoveryOperations =
        new HashSet<string>();

    private Vector2Int startCell;
    private Vector2Int exitCell;
    private Vector3 spawnPosition;
    private long nextRetryGameSecond;
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
            LogError(
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

        gameTimeManager = FindAnyObjectByType<GameTimeManager>();
        if (gameTimeManager == null)
        {
            LogError(
                "IncomingCargoCollectionSpawner: GameTimeManager is required for retry timing.",
                this);
            enabled = false;
            return;
        }

        initialized = true;
        nextRetryGameSecond = GetAbsoluteGameTimeSeconds();
        QueueRegisteredZones();
    }

    public static void QueueOutgoingRecoveryForFlight(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            return;

        IncomingCargoCollectionSpawner spawner =
            FindAnyObjectByType<IncomingCargoCollectionSpawner>();

        if (spawner != null)
            spawner.pendingOutgoingRecoveryOperations.Add(operationId);
        else
            Debug.LogWarning(
                "Cannot recover undelivered cargo: IncomingCargoCollectionSpawner " +
                "was not found.");
    }

    private void Update()
    {
        if (!initialized || gameTimeManager == null)
            return;

        long currentGameSecond = GetAbsoluteGameTimeSeconds();
        if (currentGameSecond < nextRetryGameSecond)
            return;

        nextRetryGameSecond = currentGameSecond + RetryIntervalGameSeconds;

        if (pendingOutgoingRecoveryOperations.Count > 0)
            ProcessOutgoingRecoveryOperations();

        if (pendingZones.Count > 0)
            ProcessPendingZones();
    }

    private long GetAbsoluteGameTimeSeconds()
    {
        return
            (gameTimeManager.CurrentDay - 1) * 86400L +
            gameTimeManager.GetGameTimeSeconds();
    }

    private void ProcessOutgoingRecoveryOperations()
    {
        List<string> operations =
            new List<string>(pendingOutgoingRecoveryOperations);

        foreach (string operationId in operations)
        {
            bool packagesRemain = false;

            foreach (CargoZone zone in CargoZone.RegisteredZones)
            {
                if (zone == null ||
                    !zone.HasAvailablePackages(
                        CargoDirection.Outgoing, operationId))
                {
                    continue;
                }

                packagesRemain = true;

                if (CargoDeliveryTruck.HasActiveIncomingCollection(zone))
                    continue;

                if (!roadNetwork.IsRoad(startCell) ||
                    !roadNetwork.IsRoad(exitCell))
                    continue;

                GameObject truckObject = Instantiate(
                    cargoTruckPrefab, spawnPosition, spawnPoint.rotation);

                CargoDeliveryTruck truck =
                    truckObject.GetComponent<CargoDeliveryTruck>();

                if (truck == null)
                {
                    LogError(
                        "IncomingCargoCollectionSpawner: Cargo Truck Prefab " +
                        "does not have CargoDeliveryTruck attached.", truckObject);
                    Destroy(truckObject);
                    continue;
                }

                if (!truck.InitializeOutgoingRecoveryRoute(
                        startCell, exitCell, zone, operationId))
                {
                    Destroy(truckObject);
                    continue;
                }

                Log(
                    "UNDELIVERED OUTGOING CARGO RECOVERY VAN SPAWNED | " +
                    $"Zone: {zone.name} | Operation: {operationId} | " +
                    $"Packages: {zone.CountAvailablePackages(CargoDirection.Outgoing, operationId)}",
                    truckObject);
            }

            if (!packagesRemain)
                pendingOutgoingRecoveryOperations.Remove(operationId);
        }
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

            if (zone.HasFlightReservation ||
                CargoDeliveryTruck.HasActiveIncomingCollection(zone) ||
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
                    LogWarning(
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
                LogError(
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

                Log(
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

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
