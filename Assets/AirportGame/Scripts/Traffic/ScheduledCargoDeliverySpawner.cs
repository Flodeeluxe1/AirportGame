
using System;
using System.Collections.Generic;
using UnityEngine;

public class ScheduledCargoDeliverySpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject cargoTruckPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private FlightManagementUIController flightManagement;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Road Module Settings")]
    [Tooltip("Road modules cover 2x2 grid cells.")]
    [Min(1)]
    [SerializeField] private int roadModuleSize = 2;

    [Header("Schedule")]
    [Min(0)]
    [SerializeField] private int minutesBeforeDeparture = 30;

    [Min(0.1f)]
    [SerializeField] private float scheduleRefreshInterval = 1f;

    [Header("Spawn Queue")]
    [Tooltip("Minimum game-time interval between cargo truck spawns.")]
    [Min(1)]
    [SerializeField] private int spawnIntervalGameSeconds = 2;

    [Header("Spawn Position")]
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Diagnostics")]
    [SerializeField] private bool logScheduleChanges = true;
    [SerializeField] private bool logTruckSpawns = true;
    [SerializeField] private bool logQueueChanges = true;

    private const int SecondsPerDay = 86400;
    private const int MinutesPerDay = 1440;

    private class ScheduledDelivery
    {
        public string FlightNumber;
        public string GateStableId;
        public int StartMinute;
        public int TriggerMinute;
    }

    private class QueuedDelivery
    {
        public ScheduledDelivery Scheduled;
        public long ScheduledDayIndex;
        public string OperationId;
    }

    private readonly List<ScheduledDelivery> scheduledDeliveries =
        new List<ScheduledDelivery>();

    private readonly Queue<QueuedDelivery> deliveryQueue =
        new Queue<QueuedDelivery>();

    private readonly HashSet<string> spawnedOperationIds =
        new HashSet<string>();

    private readonly HashSet<string> queuedOperationIds =
        new HashSet<string>();

    private RoadNetwork roadNetwork;

    private bool initialized;
    private int lastGameSecond;
    private long gameDayIndex;
    private long nextAllowedSpawnGameSecond = -1;

    private float scheduleRefreshTimer;
    private string lastScheduleSignature = string.Empty;

    private void Start()
    {
        if (cargoTruckPrefab == null)
        {
            Debug.LogError(
                "ScheduledCargoDeliverySpawner: Cargo Truck Prefab is missing.",
                this
            );

            enabled = false;
            return;
        }

        if (spawnPoint == null || exitPoint == null)
        {
            Debug.LogError(
                "ScheduledCargoDeliverySpawner: Assign both Spawn Point " +
                "and Exit Point.",
                this
            );

            enabled = false;
            return;
        }

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (flightManagement == null)
        {
            flightManagement =
                FindFirstObjectByType<FlightManagementUIController>();
        }

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (gridManager == null ||
            flightManagement == null ||
            gameTimeManager == null ||
            roadNetwork == null)
        {
            Debug.LogError(
                "ScheduledCargoDeliverySpawner: GridManager, " +
                "FlightManagementUIController, GameTimeManager, and " +
                "RoadNetwork are required.",
                this
            );

            enabled = false;
            return;
        }

        roadModuleSize = Mathf.Max(1, roadModuleSize);
        spawnIntervalGameSeconds = Mathf.Max(1, spawnIntervalGameSeconds);
        scheduleRefreshInterval = Mathf.Max(0.1f, scheduleRefreshInterval);

        RefreshSchedule();

        if (!TryReadGameSecond(out lastGameSecond))
        {
            Debug.LogError(
                "ScheduledCargoDeliverySpawner: Could not read game time.",
                this
            );

            enabled = false;
            return;
        }

        gameDayIndex = 0;
        initialized = true;

        Debug.Log(
            "ScheduledCargoDeliverySpawner ready. " +
            $"Game time: {gameTimeManager.FormattedTime}. " +
            $"Scheduled deliveries: {scheduledDeliveries.Count}. " +
            $"Truck lead time: {minutesBeforeDeparture} in-game minutes. " +
            $"Spawn interval: {spawnIntervalGameSeconds} game seconds. " +
            $"Road module size: {roadModuleSize}x{roadModuleSize}.",
            this
        );
    }

    private void Update()
    {
        if (!initialized)
            return;

        scheduleRefreshTimer += Time.unscaledDeltaTime;

        if (scheduleRefreshTimer >= scheduleRefreshInterval)
        {
            scheduleRefreshTimer = 0f;
            RefreshSchedule();
        }

        if (!TryReadGameSecond(out int currentGameSecond))
            return;

        if (currentGameSecond != lastGameSecond)
        {
            if (currentGameSecond > lastGameSecond)
            {
                ProcessTriggerRange(
                    lastGameSecond,
                    currentGameSecond,
                    gameDayIndex
                );
            }
            else
            {
                long previousDayIndex = gameDayIndex;

                ProcessTriggerRange(
                    lastGameSecond,
                    SecondsPerDay - 1,
                    previousDayIndex
                );

                gameDayIndex++;

                ProcessTriggerRange(
                    -1,
                    currentGameSecond,
                    gameDayIndex
                );
            }

            lastGameSecond = currentGameSecond;
        }

        ProcessDeliveryQueue();
    }

    private void RefreshSchedule()
    {
        if (flightManagement == null)
            return;

        List<FlightScheduleSaveData> activeSchedule =
            flightManagement.GetScheduleForAirportSave();

        string signature = BuildScheduleSignature(activeSchedule);

        if (signature == lastScheduleSignature)
            return;

        lastScheduleSignature = signature;
        scheduledDeliveries.Clear();

        if (activeSchedule != null)
        {
            foreach (FlightScheduleSaveData saved in activeSchedule)
            {
                if (saved == null ||
                    string.IsNullOrWhiteSpace(saved.flightNumber))
                {
                    continue;
                }

                if (saved.startMinute < 0 ||
                    saved.startMinute >= MinutesPerDay)
                {
                    Debug.LogWarning(
                        "ScheduledCargoDeliverySpawner: Skipping " +
                        $"{saved.flightNumber}; invalid departure minute " +
                        $"{saved.startMinute}.",
                        this
                    );

                    continue;
                }

                int triggerMinute = ModuloDay(
                    saved.startMinute - minutesBeforeDeparture
                );

                scheduledDeliveries.Add(new ScheduledDelivery
                {
                    FlightNumber = saved.flightNumber,
                    GateStableId = saved.gateStableId,
                    StartMinute = saved.startMinute,
                    TriggerMinute = triggerMinute
                });
            }
        }

        if (logScheduleChanges)
        {
            Debug.Log(
                "ScheduledCargoDeliverySpawner: Active airport schedule " +
                $"refreshed. Flights: {scheduledDeliveries.Count}.",
                this
            );
        }
    }

    private string BuildScheduleSignature(
        List<FlightScheduleSaveData> schedule)
    {
        if (schedule == null || schedule.Count == 0)
            return string.Empty;

        List<string> entries = new List<string>();

        foreach (FlightScheduleSaveData saved in schedule)
        {
            if (saved == null)
                continue;

            entries.Add(
                $"{saved.flightNumber}|{saved.gateStableId}|" +
                $"{saved.startMinute}"
            );
        }

        entries.Sort(StringComparer.Ordinal);

        return string.Join(";", entries);
    }

    private bool TryReadGameSecond(out int seconds)
    {
        seconds = 0;

        if (gameTimeManager == null ||
            !TimeSpan.TryParse(
                gameTimeManager.FormattedTime,
                out TimeSpan time))
        {
            return false;
        }

        seconds = (int)time.TotalSeconds;
        seconds %= SecondsPerDay;

        if (seconds < 0)
            seconds += SecondsPerDay;

        return true;
    }

    private void ProcessTriggerRange(
        int previousSecondExclusive,
        int currentSecondInclusive,
        long triggerDayIndex)
    {
        if (currentSecondInclusive <= previousSecondExclusive)
            return;

        List<ScheduledDelivery> triggeredDeliveries =
            new List<ScheduledDelivery>();

        foreach (ScheduledDelivery scheduled in scheduledDeliveries)
        {
            int triggerSecond = scheduled.TriggerMinute * 60;

            if (triggerSecond <= previousSecondExclusive ||
                triggerSecond > currentSecondInclusive)
            {
                continue;
            }

            triggeredDeliveries.Add(scheduled);
        }

        triggeredDeliveries.Sort(
            (a, b) => a.TriggerMinute.CompareTo(b.TriggerMinute)
        );

        foreach (ScheduledDelivery scheduled in triggeredDeliveries)
        {
            EnqueueScheduledDelivery(scheduled, triggerDayIndex);
        }
    }

    private void EnqueueScheduledDelivery(
        ScheduledDelivery scheduled,
        long triggerDayIndex)
    {
        long scheduledDayIndex =
            scheduled.StartMinute < minutesBeforeDeparture
                ? triggerDayIndex + 1
                : triggerDayIndex;

        string operationId =
            $"{scheduledDayIndex}:{scheduled.FlightNumber}";

        if (spawnedOperationIds.Contains(operationId) ||
            queuedOperationIds.Contains(operationId))
        {
            return;
        }

        QueuedDelivery queuedDelivery = new QueuedDelivery
        {
            Scheduled = scheduled,
            ScheduledDayIndex = scheduledDayIndex,
            OperationId = operationId
        };

        deliveryQueue.Enqueue(queuedDelivery);
        queuedOperationIds.Add(operationId);

        if (logQueueChanges)
        {
            Debug.Log(
                "Cargo delivery added to queue | " +
                $"Flight: {scheduled.FlightNumber} | " +
                $"Departure: {FormatTime(scheduled.StartMinute)} | " +
                $"Queue position: {deliveryQueue.Count}",
                this
            );
        }
    }

    private void ProcessDeliveryQueue()
    {
        if (deliveryQueue.Count == 0)
            return;

        long currentGameTime =
            gameDayIndex * SecondsPerDay + lastGameSecond;

        if (nextAllowedSpawnGameSecond >= 0 &&
            currentGameTime < nextAllowedSpawnGameSecond)
        {
            return;
        }

        // Process no more than one truck spawn per Update.
        QueuedDelivery queuedDelivery = deliveryQueue.Peek();

        // If the road setup is temporarily unavailable, retain the delivery
        // and retry it on a later Update.
        if (!TrySpawnQueuedDelivery(queuedDelivery))
            return;

        deliveryQueue.Dequeue();
        queuedOperationIds.Remove(queuedDelivery.OperationId);

        nextAllowedSpawnGameSecond =
            currentGameTime + spawnIntervalGameSeconds;

        if (logQueueChanges)
        {
            Debug.Log(
                "Cargo truck spawn interval started. Next truck can " +
                $"spawn after {spawnIntervalGameSeconds} game seconds. " +
                $"Trucks remaining in queue: {deliveryQueue.Count}.",
                this
            );
        }
    }

    private bool TrySpawnQueuedDelivery(QueuedDelivery queuedDelivery)
    {
        if (queuedDelivery == null ||
            queuedDelivery.Scheduled == null)
        {
            return true;
        }

        if (spawnedOperationIds.Contains(queuedDelivery.OperationId))
            return true;

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (roadNetwork == null || gridManager == null)
        {
            LogSpawnRetry(
                "RoadNetwork or GridManager is unavailable.",
                queuedDelivery
            );

            return false;
        }

        // Convert marker positions to their containing 2x2 road module.
        Vector2Int startCell = GetRoadModuleAnchor(
            gridManager.WorldToGrid(spawnPoint.position)
        );

        Vector2Int exitCell = GetRoadModuleAnchor(
            gridManager.WorldToGrid(exitPoint.position)
        );

        if (!IsRoadModule(startCell) || !IsRoadModule(exitCell))
        {
            LogSpawnRetry(
                $"Spawn module {startCell} or exit module {exitCell} " +
                "is not a road.",
                queuedDelivery
            );

            return false;
        }

        // Position at the center of the full road module, not the center
        // of its first grid cell.
        Vector3 spawnPosition = GetRoadModuleCenter(startCell);
        spawnPosition.y += spawnHeightOffset;

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
                "ScheduledCargoDeliverySpawner: The Cargo Truck Prefab " +
                "does not have CargoDeliveryTruck attached. Destroying truck. " +
                $"Flight: {queuedDelivery.Scheduled.FlightNumber} | " +
                $"Operation: {queuedDelivery.OperationId}",
                truckObject
            );

            Destroy(truckObject);

            // This is a prefab setup error, not a temporary road issue.
            // Remove the failed operation from the queue so it does not
            // instantiate and destroy a truck every frame.
            spawnedOperationIds.Add(queuedDelivery.OperationId);

            return true;
        }

        truck.InitializeRoute(
            startCell,
            exitCell,
            queuedDelivery.OperationId,
            queuedDelivery.Scheduled.FlightNumber
        );

        spawnedOperationIds.Add(queuedDelivery.OperationId);

        if (logTruckSpawns)
        {
            Debug.Log(
                "SCHEDULED CARGO TRUCK SPAWNED | " +
                $"Flight: {queuedDelivery.Scheduled.FlightNumber} | " +
                $"Operation: {queuedDelivery.OperationId} | " +
                $"Start module: {startCell} | " +
                $"Exit module: {exitCell} | " +
                $"Departure: {FormatTime(queuedDelivery.Scheduled.StartMinute)} | " +
                $"Triggered: {gameTimeManager.FormattedTime}",
                truckObject
            );
        }

        return true;
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

    private bool IsRoadModule(Vector2Int anchor)
    {
        // Check the anchor node used by the road network.
        return roadNetwork != null && roadNetwork.IsRoad(anchor);
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

    private void LogSpawnRetry(
        string reason,
        QueuedDelivery queuedDelivery)
    {
        // Avoid producing an error message every frame while the road setup
        // is unavailable. The queued delivery remains pending.
        if (!logQueueChanges)
            return;

        Debug.LogWarning(
            "ScheduledCargoDeliverySpawner: Delivery remains queued. " +
            $"Flight: {queuedDelivery.Scheduled.FlightNumber}. " +
            $"Reason: {reason}",
            this
        );
    }

    private int ModuloDay(int minutes)
    {
        return ((minutes % MinutesPerDay) + MinutesPerDay) %
               MinutesPerDay;
    }

    private string FormatTime(int minutes)
    {
        minutes = Mathf.Clamp(minutes, 0, MinutesPerDay);

        if (minutes == MinutesPerDay)
            return "24:00";

        return $"{minutes / 60:00}:{minutes % 60:00}";
    }
}
