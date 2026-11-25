
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ScheduledAircraftSpawner : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    [Header("References")]
    [SerializeField] private GameObject cargoOnlyAircraftPrefab;
    [SerializeField] private GameObject passengerOnlyAircraftPrefab;
    [SerializeField] private GameObject cargoAndPassengersAircraftPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private TestFlightCatalog flightCatalog;
    [SerializeField] private GameTimeManager gameTimeManager;
    [SerializeField] private AirportManagementController airportManagementController;
    [SerializeField] private AirlineContractOfferManager contractManager;

    [Header("Spawn Queue")]
    [Tooltip("Minimum game-time interval between aircraft spawns, in seconds.")]
    [Min(1)]
    [SerializeField] private int spawnIntervalSeconds = 120;

    [Header("Diagnostics")]
    [SerializeField] private bool logScheduleReloads = true;
    [SerializeField] private bool logAircraftSpawns = true;
    [SerializeField] private bool logQueueChanges = true;

    public GameObject CargoAircraftPrefab => cargoOnlyAircraftPrefab;
    public GameObject PassengerAircraftPrefab => passengerOnlyAircraftPrefab;
    public GameObject CargoAndPassengersAircraftPrefab => cargoAndPassengersAircraftPrefab;

    private const int ScheduleFileVersion = 1;
    private const int MinutesBeforeScheduledTime = 1;
    private const string ScheduleFileName = "flight_schedule.json";
    private const int SecondsPerDay = 86400;
    private const int MinutesPerDay = 1440;

    [Serializable]
    private class ScheduleSaveData
    {
        public int version;
        public List<SavedFlightData> flights =
            new List<SavedFlightData>();
    }

    [Serializable]
    private class SavedFlightData
    {
        public string flightNumber;
        public string gateStableId;
        public int startMinute;
        public string contractId;
    }

    private class ScheduledSpawn
    {
        public FlightDefinition Definition;
        public string GateStableId;
        public int StartMinute;
        public int TriggerMinute;
        public string ContractId;
    }

    private class QueuedAircraftSpawn
    {
        public ScheduledSpawn Scheduled;
        public long ScheduledDayIndex;
        public string OperationId;
    }

    private readonly List<ScheduledSpawn> scheduledFlights =
        new List<ScheduledSpawn>();

    private readonly Queue<QueuedAircraftSpawn> spawnQueue =
        new Queue<QueuedAircraftSpawn>();

    private readonly HashSet<string> spawnedOperationIds =
        new HashSet<string>();

    private readonly HashSet<string> queuedOperationIds =
        new HashSet<string>();

    private bool initialized;
    private bool scheduleLoaded;
    private int lastGameSecond;
    private long gameDayIndex;

    private long nextAllowedSpawnGameSecond = -1;
    private long queueProcessingEventId;
    private double queueProcessingDueTime = double.PositiveInfinity;

    private long observedWriteTicks = long.MinValue;
    private long observedFileLength = -1;

    private string ScheduleFilePath =>
        Path.Combine(
            Application.persistentDataPath,
            ScheduleFileName
        );

    private void OnEnable()
    {
        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();

        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;

        FlightManagementUIController.AirportScheduleChanged += HandleAirportScheduleChanged;
        if (initialized)
            ScheduleQueueProcessing();
    }

    private void OnDisable()
    {
        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;

        FlightManagementUIController.AirportScheduleChanged -= HandleAirportScheduleChanged;
        CancelQueueProcessingEvent();
    }

    private void Start()
    {
        if (flightCatalog == null)
            flightCatalog = FindAnyObjectByType<TestFlightCatalog>();

        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();

        if (contractManager == null)
            contractManager = FindAnyObjectByType<AirlineContractOfferManager>();

        if (spawnPoint == null)
        {
            LogError(
                "ScheduledAircraftSpawner: Assign a spawn point Transform.",
                this
            );
            enabled = false;
            return;
        }

        if (flightCatalog == null)
        {
            LogError(
                "ScheduledAircraftSpawner: TestFlightCatalog is missing.",
                this
            );
            enabled = false;
            return;
        }

        if (gameTimeManager == null)
        {
            LogError(
                "ScheduledAircraftSpawner: GameTimeManager is missing.",
                this
            );
            enabled = false;
            return;
        }

        spawnIntervalSeconds = Mathf.Max(1, spawnIntervalSeconds);

        TryReloadScheduleIfChanged();

        lastGameSecond = gameTimeManager.GetGameTimeSeconds();
        gameDayIndex = gameTimeManager.CurrentDay - 1;
        initialized = true;
        ScheduleQueueProcessing();

        Log(
            $"ScheduledAircraftSpawner ready. Game time: " +
            $"{gameTimeManager.FormattedTime}. " +
            $"Saved flights loaded: {scheduledFlights.Count}. " +
            $"Spawn interval: {spawnIntervalSeconds} game seconds.",
            this
        );
    }

    private void HandleAirportScheduleChanged()
    {
        if (initialized)
            TryReloadScheduleIfChanged();
    }

    private void HandleGameTimeAdvanced(
        long previousAbsoluteSecond,
        long currentAbsoluteSecond)
    {
        if (!initialized)
            return;

        if (currentAbsoluteSecond <= previousAbsoluteSecond)
        {
            gameDayIndex = currentAbsoluteSecond / SecondsPerDay;
            lastGameSecond = (int)(currentAbsoluteSecond % SecondsPerDay);
            if (spawnQueue.Count > 0)
                ScheduleQueueProcessing();
            return;
        }

        long previousDay = previousAbsoluteSecond / SecondsPerDay;
        long currentDay = currentAbsoluteSecond / SecondsPerDay;

        for (long day = previousDay; day <= currentDay; day++)
        {
            int fromSecond = day == previousDay
                ? (int)(previousAbsoluteSecond % SecondsPerDay)
                : -1;
            int toSecond = day == currentDay
                ? (int)(currentAbsoluteSecond % SecondsPerDay)
                : SecondsPerDay - 1;

            ProcessTriggerRange(fromSecond, toSecond, day);
        }

        gameDayIndex = currentDay;
        lastGameSecond = (int)(currentAbsoluteSecond % SecondsPerDay);
        if (spawnQueue.Count > 0)
            ScheduleQueueProcessing();
    }

    private bool IsAirportOperational()
    {
        if (airportManagementController == null)
        {
            airportManagementController =
                FindAnyObjectByType<AirportManagementController>();
        }

        return airportManagementController != null &&
               airportManagementController.IsAirportOperational;
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
        if (!scheduleLoaded)
            return;

        if (currentSecondInclusive <= previousSecondExclusive)
            return;

        List<ScheduledSpawn> triggeredFlights =
            new List<ScheduledSpawn>();

        foreach (ScheduledSpawn scheduled in scheduledFlights)
        {
            int triggerSecond = scheduled.TriggerMinute * 60;

            if (triggerSecond <= previousSecondExclusive ||
                triggerSecond > currentSecondInclusive)
            {
                continue;
            }

            triggeredFlights.Add(scheduled);
        }

        // Preserve chronological order when several triggers are crossed
        // during a single game-time update.
        triggeredFlights.Sort(
            (a, b) => a.TriggerMinute.CompareTo(b.TriggerMinute)
        );

        foreach (ScheduledSpawn scheduled in triggeredFlights)
        {
            EnqueueScheduledFlight(scheduled, triggerDayIndex);
        }
    }

    private void EnqueueScheduledFlight(
        ScheduledSpawn scheduled,
        long triggerDayIndex)
    {
        // Flights scheduled shortly after midnight are triggered
        // on the previous game day.
        long scheduledDayIndex =
            scheduled.StartMinute < MinutesBeforeScheduledTime
                ? triggerDayIndex + 1
                : triggerDayIndex;

        if (!string.IsNullOrWhiteSpace(scheduled.ContractId))
        {
            if (contractManager == null)
                contractManager = FindAnyObjectByType<AirlineContractOfferManager>();

            if (contractManager == null ||
                !contractManager.IsContractActiveOnDay(
                    scheduled.ContractId,
                    scheduledDayIndex + 1))
            {
                return;
            }
        }

        string operationId =
            $"{scheduledDayIndex}:{scheduled.Definition.FlightNumber}";

        if (spawnedOperationIds.Contains(operationId) ||
            queuedOperationIds.Contains(operationId))
        {
            return;
        }

        // A flight whose arrival time passes while the airport has no valid
        // CEO Office is skipped rather than held for a later arrival.
        if (!IsAirportOperational())
        {
            if (!string.IsNullOrWhiteSpace(scheduled.ContractId))
            {
                AirlineContractPerformanceTracker tracker =
                    FindAnyObjectByType<AirlineContractPerformanceTracker>();
                if (tracker != null)
                {
                    tracker.RecordOperationalFailure(
                        scheduled.ContractId,
                        scheduled.Definition.ObligationId,
                        scheduledDayIndex + 1);
                }
            }

            spawnedOperationIds.Add(operationId);

            if (logQueueChanges)
            {
                Log(
                    $"Scheduled arrival skipped | Flight: " +
                    $"{scheduled.Definition.FlightNumber} | " +
                    "No valid CEO Office.",
                    this
                );
            }

            return;
        }

        QueuedAircraftSpawn queuedFlight = new QueuedAircraftSpawn
        {
            Scheduled = scheduled,
            ScheduledDayIndex = scheduledDayIndex,
            OperationId = operationId
        };

        spawnQueue.Enqueue(queuedFlight);
        queuedOperationIds.Add(operationId);
        ScheduleQueueProcessing();

        if (logQueueChanges)
        {
            Log(
                $"Aircraft added to spawn queue | " +
                $"Flight: {scheduled.Definition.FlightNumber} | " +
                $"Planned time: {FormatTime(scheduled.StartMinute)} | " +
                $"Queue position: {spawnQueue.Count}",
                this
            );
        }
    }

    private void ScheduleQueueProcessing()
    {
        if (!initialized || !isActiveAndEnabled || spawnQueue.Count == 0 ||
            gameTimeManager == null || gameTimeManager.EventScheduler == null)
            return;

        double now = GameTimeManager.SimulationTime;
        double due = nextAllowedSpawnGameSecond >= 0
            ? System.Math.Max(now, nextAllowedSpawnGameSecond)
            : now;
        ScheduleQueueProcessingAt(due);
    }

    private void ScheduleQueueProcessingAt(double dueTime)
    {
        if (!initialized || !isActiveAndEnabled || spawnQueue.Count == 0 ||
            gameTimeManager == null || gameTimeManager.EventScheduler == null)
            return;

        if (queueProcessingEventId != 0 && queueProcessingDueTime <= dueTime)
            return;

        CancelQueueProcessingEvent();
        queueProcessingDueTime = dueTime;
        queueProcessingEventId = gameTimeManager.EventScheduler.ScheduleAt(
            dueTime, HandleScheduledQueueProcessing);
    }

    private void HandleScheduledQueueProcessing()
    {
        queueProcessingEventId = 0;
        queueProcessingDueTime = double.PositiveInfinity;
        if (!initialized || !isActiveAndEnabled)
            return;
        ProcessSpawnQueue();
    }

    private void CancelQueueProcessingEvent()
    {
        if (queueProcessingEventId != 0 && gameTimeManager != null &&
            gameTimeManager.EventScheduler != null)
            gameTimeManager.EventScheduler.Cancel(queueProcessingEventId);

        queueProcessingEventId = 0;
        queueProcessingDueTime = double.PositiveInfinity;
    }

    private void ProcessSpawnQueue()
    {
        // Expire contract flights before attempting the next queued arrival.
        while (spawnQueue.Count > 0)
        {
            QueuedAircraftSpawn queued = spawnQueue.Peek();
            if (queued == null)
            {
                spawnQueue.Dequeue();
                continue;
            }

            string contractId = queued.Scheduled != null
                ? queued.Scheduled.ContractId
                : null;
            if (string.IsNullOrWhiteSpace(contractId))
                break;

            if (contractManager == null)
                contractManager = FindAnyObjectByType<AirlineContractOfferManager>();

            if (contractManager != null && contractManager.IsContractActiveOnDay(
                contractId, queued.ScheduledDayIndex + 1))
                break;

            spawnQueue.Dequeue();
            queuedOperationIds.Remove(queued.OperationId);
            spawnedOperationIds.Add(queued.OperationId);
        }

        if (spawnQueue.Count == 0)
            return;

        // Do not hold arrivals if the airport is no longer operational.
        if (!IsAirportOperational())
        {
            while (spawnQueue.Count > 0)
            {
                QueuedAircraftSpawn skipped = spawnQueue.Dequeue();
                if (skipped == null)
                    continue;
                queuedOperationIds.Remove(skipped.OperationId);
                spawnedOperationIds.Add(skipped.OperationId);
            }
            return;
        }

        long currentGameTime = (long)System.Math.Floor(GameTimeManager.SimulationTime);
        if (nextAllowedSpawnGameSecond >= 0 &&
            currentGameTime < nextAllowedSpawnGameSecond)
        {
            ScheduleQueueProcessingAt(nextAllowedSpawnGameSecond);
            return;
        }

        QueuedAircraftSpawn queuedFlight = spawnQueue.Peek();
        if (!SpawnQueuedAircraft(queuedFlight))
        {
            // Retry at the next whole game second, preserving queue order.
            nextAllowedSpawnGameSecond = currentGameTime + 1;
            ScheduleQueueProcessingAt(nextAllowedSpawnGameSecond);
            return;
        }

        spawnQueue.Dequeue();
        queuedOperationIds.Remove(queuedFlight.OperationId);
        nextAllowedSpawnGameSecond = currentGameTime + spawnIntervalSeconds;

        if (logQueueChanges)
        {
            Log(
                $"Aircraft spawn interval started. Next aircraft " +
                $"can spawn after {spawnIntervalSeconds} game seconds. " +
                $"Aircraft remaining in queue: {spawnQueue.Count}.",
                this
            );
        }

        if (spawnQueue.Count > 0)
            ScheduleQueueProcessingAt(nextAllowedSpawnGameSecond);
    }

    private bool SpawnQueuedAircraft(QueuedAircraftSpawn queuedFlight)
    {
        if (queuedFlight == null ||
            queuedFlight.Scheduled == null)
        {
            return false;
        }

        if (spawnedOperationIds.Contains(queuedFlight.OperationId))
            return true;

        ScheduledSpawn scheduled = queuedFlight.Scheduled;

        GameObject prefabToSpawn;

        switch (scheduled.Definition.OperationType)
        {
            case FlightOperationType.None:
                LogError(
                    $"Cannot spawn flight {scheduled.Definition.FlightNumber}: " +
                    "operation type is None.",
                    this
                );
                return false;

            case FlightOperationType.CargoOnly:
                prefabToSpawn = cargoOnlyAircraftPrefab;
                break;

            case FlightOperationType.PassengerOnly:
                prefabToSpawn = passengerOnlyAircraftPrefab;

                if (prefabToSpawn == null)
                {
                    LogError(
                        $"Cannot spawn passenger flight " +
                        $"{scheduled.Definition.FlightNumber}: assign " +
                        "Passenger Only Aircraft Prefab on ScheduledAircraftSpawner.",
                        this
                    );
                    return false;
                }
                break;

            case FlightOperationType.CargoAndPassengers:
                prefabToSpawn = cargoAndPassengersAircraftPrefab;
                break;

            default:
                LogError(
                    $"Cannot spawn flight {scheduled.Definition.FlightNumber}: " +
                    $"unknown operation type {scheduled.Definition.OperationType}.",
                    this
                );
                return false;
        }

        if (prefabToSpawn == null)
        {
            LogError(
                $"Cannot spawn flight {scheduled.Definition.FlightNumber}: " +
                "the selected aircraft prefab is not assigned.",
                this
            );
            return false;
        }

        GameObject aircraft = Instantiate(
            prefabToSpawn,
            spawnPoint.position,
            spawnPoint.rotation
        );

        AircraftAirportFees airportFees =
            aircraft.GetComponent<AircraftAirportFees>();
        if (airportFees != null)
        {
            airportFees.SetFeeCategory(
                scheduled.Definition.OperationType == FlightOperationType.CargoOnly
                    ? AirportFeeCategory.Cargo
                    : AirportFeeCategory.Passenger);
        }

        AircraftFlightIdentity identity =
            aircraft.GetComponent<AircraftFlightIdentity>();

        if (identity == null)
        {
            identity = aircraft.AddComponent<AircraftFlightIdentity>();

            LogWarning(
                "The aircraft prefab had no AircraftFlightIdentity. " +
                "One was added at runtime; add it to the prefab as well.",
                aircraft
            );
        }

        identity.Initialize(
            scheduled.Definition,
            scheduled.GateStableId,
            scheduled.StartMinute,
            queuedFlight.ScheduledDayIndex
        );

        if (scheduled.Definition.OperationType == FlightOperationType.PassengerOnly ||
            scheduled.Definition.OperationType == FlightOperationType.CargoAndPassengers)
        {
            AircraftStats aircraftStats = aircraft.GetComponent<AircraftStats>();
            PassengerCoordinator passengerCoordinator = PassengerCoordinator.Instance;

            if (aircraftStats == null || passengerCoordinator == null ||
                !passengerCoordinator.ConfigureScheduledFlightPassengers(
                    aircraftStats,
                    identity))
            {
                LogWarning(
                    $"Could not configure booked passengers for flight " +
                    $"{scheduled.Definition.FlightNumber}: " +
                    $"{(aircraftStats == null ? "AircraftStats is missing. " : string.Empty)}" +
                    $"{(passengerCoordinator == null ? "PassengerCoordinator is missing." : string.Empty)}",
                    aircraft);
            }
        }

        GateAssignmentController gateAssignmentController =
            GateAssignmentController.Instance;

        AircraftGate reservedGate = null;
        string reservationMessage = string.Empty;

        if (gateAssignmentController == null ||
            !gateAssignmentController.TryReserveGateAtSpawn(
                identity,
                out reservedGate,
                out reservationMessage))
        {
            string reason = gateAssignmentController == null
                ? "GateAssignmentController was not found."
                : reservationMessage;

            LogWarning(
                $"Aircraft spawn postponed | Flight: " +
                $"{scheduled.Definition.FlightNumber} | {reason}",
                this
            );

            Destroy(aircraft);
            return false;
        }

        spawnedOperationIds.Add(queuedFlight.OperationId);

        if (logAircraftSpawns)
        {
            Log(
                $"Scheduled flight spawned | " +
                $"Flight: {scheduled.Definition.FlightNumber} | " +
                $"Operation: {scheduled.Definition.OperationLabel} | " +
                $"Aircraft prefab: {prefabToSpawn.name} | " +
                $"Planned gate: {scheduled.GateStableId} | " +
                $"Planned time: {FormatTime(scheduled.StartMinute)} | " +
                $"Game time: {gameTimeManager.FormattedTime}",
                aircraft
            );
        }

        return true;
    }

    private void TryReloadScheduleIfChanged()
    {
        if (!File.Exists(ScheduleFilePath))
        {
            if (scheduleLoaded)
            {
                scheduledFlights.Clear();
                scheduleLoaded = false;

                if (logScheduleReloads)
                {
                    Log(
                        "Saved flight schedule file was removed. " +
                        "Scheduled spawning is paused until a schedule exists.",
                        this
                    );
                }
            }

            observedWriteTicks = long.MinValue;
            observedFileLength = -1;
            return;
        }

        long currentWriteTicks;
        long currentFileLength;

        try
        {
            FileInfo info = new FileInfo(ScheduleFilePath);
            currentWriteTicks = info.LastWriteTimeUtc.Ticks;
            currentFileLength = info.Length;
        }
        catch (Exception exception)
        {
            LogWarning(
                $"Could not inspect the flight schedule file: " +
                $"{exception.Message}",
                this
            );
            return;
        }

        if (currentWriteTicks == observedWriteTicks &&
            currentFileLength == observedFileLength)
        {
            return;
        }

        observedWriteTicks = currentWriteTicks;
        observedFileLength = currentFileLength;

        try
        {
            string json = File.ReadAllText(ScheduleFilePath);

            ScheduleSaveData saveData =
                JsonUtility.FromJson<ScheduleSaveData>(json);

            if (saveData == null)
            {
                throw new InvalidDataException(
                    "The schedule file could not be parsed."
                );
            }

            if (saveData.version != ScheduleFileVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported schedule version {saveData.version}."
                );
            }

            List<ScheduledSpawn> loadedFlights =
                new List<ScheduledSpawn>();

            HashSet<string> seenFlightNumbers =
                new HashSet<string>();

            if (saveData.flights != null)
            {
                foreach (SavedFlightData saved in saveData.flights)
                {
                    if (saved == null ||
                        string.IsNullOrWhiteSpace(saved.flightNumber) ||
                        string.IsNullOrWhiteSpace(saved.gateStableId))
                    {
                        LogWarning(
                            "Skipping an incomplete saved schedule entry.",
                            this
                        );
                        continue;
                    }

                    if (saved.startMinute < 0 ||
                        saved.startMinute >= MinutesPerDay ||
                        saved.startMinute % 15 != 0)
                    {
                        LogWarning(
                            $"Skipping {saved.flightNumber}: invalid " +
                            $"start minute {saved.startMinute}.",
                            this
                        );
                        continue;
                    }

                    if (!seenFlightNumbers.Add(saved.flightNumber))
                    {
                        LogWarning(
                            $"Skipping duplicate scheduled flight " +
                            $"{saved.flightNumber}.",
                            this
                        );
                        continue;
                    }

                    FlightDefinition definition =
                        FindFlightDefinition(saved.flightNumber);

                    if (definition == null)
                    {
                        LogWarning(
                            $"Skipping {saved.flightNumber}: no matching " +
                            "definition exists in TestFlightCatalog.",
                            this
                        );
                        continue;
                    }

                    if (definition.OperationType == FlightOperationType.None)
                    {
                        LogWarning(
                            $"Skipping scheduled flight {saved.flightNumber}: " +
                            "its operation type is None.",
                            this
                        );
                        continue;
                    }

                    int triggerMinute = ModuloDay(
                        saved.startMinute -
                        MinutesBeforeScheduledTime
                    );

                    loadedFlights.Add(new ScheduledSpawn
                    {
                        Definition = definition,
                        GateStableId = saved.gateStableId,
                        StartMinute = saved.startMinute,
                        TriggerMinute = triggerMinute,
                        ContractId = saved.contractId
                    });
                }
            }

            scheduledFlights.Clear();
            scheduledFlights.AddRange(loadedFlights);

            scheduleLoaded = true;

            if (logScheduleReloads)
            {
                Log(
                    $"Scheduled aircraft spawner loaded " +
                    $"{scheduledFlights.Count} flight(s) from " +
                    $"{ScheduleFilePath}.",
                    this
                );
            }
        }
        catch (Exception exception)
        {
            LogError(
                $"Could not load flight schedule from " +
                $"{ScheduleFilePath}. The last successfully loaded " +
                $"schedule will be retained.\n{exception}",
                this
            );
        }
    }

    private FlightDefinition FindFlightDefinition(string flightNumber)
    {
        foreach (FlightDefinition flight in flightCatalog.Flights)
        {
            if (flight.FlightNumber == flightNumber)
                return flight;
        }

        return null;
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

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
