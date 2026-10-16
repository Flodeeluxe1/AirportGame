
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ScheduledAircraftSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject aircraftPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private TestFlightCatalog flightCatalog;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Spawn Queue")]
    [Tooltip("Minimum game-time interval between aircraft spawns, in seconds.")]
    [Min(1)]
    [SerializeField] private int spawnIntervalSeconds = 120;

    [Header("Diagnostics")]
    [SerializeField] private bool logScheduleReloads = true;
    [SerializeField] private bool logAircraftSpawns = true;
    [SerializeField] private bool logQueueChanges = true;

    private const int ScheduleFileVersion = 1;
    private const int MinutesBeforeScheduledTime = 10;
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
    }

    private class ScheduledSpawn
    {
        public FlightDefinition Definition;
        public string GateStableId;
        public int StartMinute;
        public int TriggerMinute;
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

    private long observedWriteTicks = long.MinValue;
    private long observedFileLength = -1;

    private string ScheduleFilePath =>
        Path.Combine(
            Application.persistentDataPath,
            ScheduleFileName
        );

    private void Start()
    {
        if (flightCatalog == null)
            flightCatalog = FindFirstObjectByType<TestFlightCatalog>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (aircraftPrefab == null)
        {
            Debug.LogError(
                "ScheduledAircraftSpawner: Assign an aircraft prefab.",
                this
            );
            enabled = false;
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError(
                "ScheduledAircraftSpawner: Assign a spawn point Transform.",
                this
            );
            enabled = false;
            return;
        }

        if (flightCatalog == null)
        {
            Debug.LogError(
                "ScheduledAircraftSpawner: TestFlightCatalog is missing.",
                this
            );
            enabled = false;
            return;
        }

        if (gameTimeManager == null)
        {
            Debug.LogError(
                "ScheduledAircraftSpawner: GameTimeManager is missing.",
                this
            );
            enabled = false;
            return;
        }

        spawnIntervalSeconds = Mathf.Max(1, spawnIntervalSeconds);

        TryReloadScheduleIfChanged();

        if (!TryReadGameSecond(out lastGameSecond))
        {
            Debug.LogError(
                "ScheduledAircraftSpawner could not read " +
                "GameTimeManager.FormattedTime.",
                this
            );
            enabled = false;
            return;
        }

        gameDayIndex = 0;
        initialized = true;

        Debug.Log(
            $"ScheduledAircraftSpawner ready. Game time: " +
            $"{gameTimeManager.FormattedTime}. " +
            $"Saved flights loaded: {scheduledFlights.Count}. " +
            $"Spawn interval: {spawnIntervalSeconds} game seconds.",
            this
        );
    }

    private void Update()
    {
        if (!initialized)
            return;

        TryReloadScheduleIfChanged();

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

        ProcessSpawnQueue();
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

        string operationId =
            $"{scheduledDayIndex}:{scheduled.Definition.FlightNumber}";

        if (spawnedOperationIds.Contains(operationId) ||
            queuedOperationIds.Contains(operationId))
        {
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

        if (logQueueChanges)
        {
            Debug.Log(
                $"Aircraft added to spawn queue | " +
                $"Flight: {scheduled.Definition.FlightNumber} | " +
                $"Planned time: {FormatTime(scheduled.StartMinute)} | " +
                $"Queue position: {spawnQueue.Count}",
                this
            );
        }
    }

    private void ProcessSpawnQueue()
    {
        if (spawnQueue.Count == 0)
            return;

        long currentGameTime =
            gameDayIndex * SecondsPerDay + lastGameSecond;

        if (nextAllowedSpawnGameSecond >= 0 &&
            currentGameTime < nextAllowedSpawnGameSecond)
        {
            return;
        }

        // Spawn at most one aircraft during this Update.
        QueuedAircraftSpawn queuedFlight = spawnQueue.Dequeue();

        queuedOperationIds.Remove(queuedFlight.OperationId);

        SpawnQueuedAircraft(queuedFlight);

        // The next aircraft cannot spawn until the configured interval
        // has elapsed in game time.
        nextAllowedSpawnGameSecond =
            currentGameTime + spawnIntervalSeconds;

        if (logQueueChanges)
        {
            Debug.Log(
                $"Aircraft spawn interval started. Next aircraft " +
                $"can spawn after {spawnIntervalSeconds} game seconds. " +
                $"Aircraft remaining in queue: {spawnQueue.Count}.",
                this
            );
        }
    }

    private void SpawnQueuedAircraft(QueuedAircraftSpawn queuedFlight)
    {
        if (queuedFlight == null ||
            queuedFlight.Scheduled == null)
        {
            return;
        }

        if (spawnedOperationIds.Contains(queuedFlight.OperationId))
            return;

        ScheduledSpawn scheduled = queuedFlight.Scheduled;

        GameObject aircraft = Instantiate(
            aircraftPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        AircraftFlightIdentity identity =
            aircraft.GetComponent<AircraftFlightIdentity>();

        if (identity == null)
        {
            identity = aircraft.AddComponent<AircraftFlightIdentity>();

            Debug.LogWarning(
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

        spawnedOperationIds.Add(queuedFlight.OperationId);

        if (logAircraftSpawns)
        {
            Debug.Log(
                $"Scheduled flight spawned | " +
                $"Flight: {scheduled.Definition.FlightNumber} | " +
                $"Planned gate: {scheduled.GateStableId} | " +
                $"Planned time: {FormatTime(scheduled.StartMinute)} | " +
                $"Game time: {gameTimeManager.FormattedTime}",
                aircraft
            );
        }
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
                    Debug.Log(
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
            Debug.LogWarning(
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
                        Debug.LogWarning(
                            "Skipping an incomplete saved schedule entry.",
                            this
                        );
                        continue;
                    }

                    if (saved.startMinute < 0 ||
                        saved.startMinute >= MinutesPerDay ||
                        saved.startMinute % 15 != 0)
                    {
                        Debug.LogWarning(
                            $"Skipping {saved.flightNumber}: invalid " +
                            $"start minute {saved.startMinute}.",
                            this
                        );
                        continue;
                    }

                    if (!seenFlightNumbers.Add(saved.flightNumber))
                    {
                        Debug.LogWarning(
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
                        Debug.LogWarning(
                            $"Skipping {saved.flightNumber}: no matching " +
                            "definition exists in TestFlightCatalog.",
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
                        TriggerMinute = triggerMinute
                    });
                }
            }

            scheduledFlights.Clear();
            scheduledFlights.AddRange(loadedFlights);

            scheduleLoaded = true;

            if (logScheduleReloads)
            {
                Debug.Log(
                    $"Scheduled aircraft spawner loaded " +
                    $"{scheduledFlights.Count} flight(s) from " +
                    $"{ScheduleFilePath}.",
                    this
                );
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
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
}
