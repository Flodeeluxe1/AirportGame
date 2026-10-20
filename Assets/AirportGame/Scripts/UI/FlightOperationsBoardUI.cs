
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;

public class FlightOperationsBoardUI : MonoBehaviour
{
    public static FlightOperationsBoardUI Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private TMP_Text boardText;
    [Min(0.1f)]
    [SerializeField] private float refreshIntervalSeconds = 0.5f;

    [Header("References")]
    [SerializeField] private TestFlightCatalog flightCatalog;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Delay Settings")]
    [Tooltip("Flights delayed by more than this many minutes are marked delayed.")]
    [Min(0)]
    [SerializeField] private int onTimeToleranceMinutes = 5;

    private const int SecondsPerDay = 86400;
    private const int MinutesPerDay = 1440;
    private const string ScheduleFileName = "flight_schedule.json";
    private const int ScheduleFileVersion = 1;

    [Serializable]
    private class ScheduleSaveData
    {
        public int version;
        public List<SavedFlightData> flights = new List<SavedFlightData>();
    }

    [Serializable]
    private class SavedFlightData
    {
        public string flightNumber;
        public string gateStableId;
        public int startMinute;
    }

    private class FlightEntry
    {
        public string OperationId;
        public long ScheduledDayIndex;
        public string FlightNumber;
        public string GateStableId;
        public FlightDefinition Definition;
        public int ScheduledArrivalMinute;

        public string Status = "Scheduled";
        public long ActualArrivalSeconds = -1;
        public long ActualDepartureSeconds = -1;
    }

    private readonly List<SavedFlightData> savedFlights =
        new List<SavedFlightData>();

    private readonly List<FlightEntry> entries =
        new List<FlightEntry>();

    private readonly Dictionary<string, FlightEntry> entriesByOperationId =
        new Dictionary<string, FlightEntry>();

    private string ScheduleFilePath =>
        Path.Combine(Application.persistentDataPath, ScheduleFileName);

    private long observedWriteTicks = long.MinValue;
    private long observedFileLength = -1;
    private long entriesDayIndex = long.MinValue;

    private bool scheduleLoaded;
    private float refreshTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError(
                "Multiple FlightOperationsBoardUI instances exist. " +
                "Only one should be present in the scene.",
                this);
            enabled = false;
            return;
        }

        Instance = this;

        if (flightCatalog == null)
            flightCatalog = FindFirstObjectByType<TestFlightCatalog>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();
    }

    private void Start()
    {
        if (boardText == null)
        {
            Debug.LogError(
                "FlightOperationsBoardUI: Assign a TMP_Text component.",
                this);
            enabled = false;
            return;
        }

        if (flightCatalog == null || gameTimeManager == null)
        {
            Debug.LogError(
                "FlightOperationsBoardUI: TestFlightCatalog or " +
                "GameTimeManager is missing.",
                this);
            enabled = false;
            return;
        }

        ReloadScheduleIfChanged();
        RebuildEntriesForCurrentDay();
        RenderBoard();
    }

    private void Update()
    {
        if (!enabled || gameTimeManager == null)
            return;

        ReloadScheduleIfChanged();

        long currentDayIndex = gameTimeManager.CurrentDay - 1;

        if (entriesDayIndex != currentDayIndex)
            RebuildEntriesForCurrentDay();

        UpdateActiveAircraft();

        refreshTimer += Time.unscaledDeltaTime;

        if (refreshTimer >= refreshIntervalSeconds)
        {
            refreshTimer = 0f;
            RenderBoard();
        }
    }

    private void ReloadScheduleIfChanged()
    {
        if (!File.Exists(ScheduleFilePath))
        {
            if (scheduleLoaded)
            {
                savedFlights.Clear();
                scheduleLoaded = false;
                observedWriteTicks = long.MinValue;
                observedFileLength = -1;
                RebuildEntriesForCurrentDay();
            }

            return;
        }

        long writeTicks;
        long fileLength;

        try
        {
            FileInfo info = new FileInfo(ScheduleFilePath);
            writeTicks = info.LastWriteTimeUtc.Ticks;
            fileLength = info.Length;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"Flight board could not inspect the schedule: {exception.Message}",
                this);
            return;
        }

        if (writeTicks == observedWriteTicks &&
            fileLength == observedFileLength)
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(ScheduleFilePath);
            ScheduleSaveData data =
                JsonUtility.FromJson<ScheduleSaveData>(json);

            if (data == null || data.version != ScheduleFileVersion)
            {
                Debug.LogWarning(
                    "Flight board found an invalid or unsupported flight schedule.",
                    this);
                return;
            }

            savedFlights.Clear();

            if (data.flights != null)
            {
                foreach (SavedFlightData flight in data.flights)
                {
                    if (flight == null ||
                        string.IsNullOrWhiteSpace(flight.flightNumber) ||
                        string.IsNullOrWhiteSpace(flight.gateStableId) ||
                        flight.startMinute < 0 ||
                        flight.startMinute >= MinutesPerDay)
                    {
                        continue;
                    }

                    savedFlights.Add(flight);
                }
            }

            observedWriteTicks = writeTicks;
            observedFileLength = fileLength;
            scheduleLoaded = true;

            RebuildEntriesForCurrentDay();

            Debug.Log(
                $"Flight board loaded {savedFlights.Count} scheduled flight(s).",
                this);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"Flight board could not read the flight schedule: {exception.Message}",
                this);
        }
    }

    private void RebuildEntriesForCurrentDay()
    {
        if (gameTimeManager == null || flightCatalog == null)
            return;

        long dayIndex = gameTimeManager.CurrentDay - 1;

        // Preserve live statuses and recorded times if the schedule reloads.
        Dictionary<string, FlightEntry> previousEntries =
            new Dictionary<string, FlightEntry>(entriesByOperationId);

        entries.Clear();
        entriesByOperationId.Clear();
        entriesDayIndex = dayIndex;

        if (!scheduleLoaded)
            return;

        HashSet<string> seenOperations = new HashSet<string>();

        foreach (SavedFlightData saved in savedFlights)
        {
            FlightDefinition definition =
                FindFlightDefinition(saved.flightNumber);

            if (definition == null)
                continue;

            string operationId =
                $"{dayIndex}:{definition.FlightNumber}";

            if (!seenOperations.Add(operationId))
                continue;

            FlightEntry entry = new FlightEntry
            {
                OperationId = operationId,
                ScheduledDayIndex = dayIndex,
                FlightNumber = definition.FlightNumber,
                GateStableId = saved.gateStableId,
                Definition = definition,
                ScheduledArrivalMinute = saved.startMinute
            };

            if (previousEntries.TryGetValue(
                    operationId, out FlightEntry previous))
            {
                entry.Status = previous.Status;
                entry.ActualArrivalSeconds = previous.ActualArrivalSeconds;
                entry.ActualDepartureSeconds = previous.ActualDepartureSeconds;
            }

            entries.Add(entry);
            entriesByOperationId.Add(operationId, entry);
        }

        entries.Sort(
            (a, b) =>
                a.ScheduledArrivalMinute.CompareTo(
                    b.ScheduledArrivalMinute));
    }

    private FlightDefinition FindFlightDefinition(string flightNumber)
    {
        foreach (FlightDefinition flight in flightCatalog.Flights)
        {
            if (flight != null && flight.FlightNumber == flightNumber)
                return flight;
        }

        return null;
    }

    private void UpdateActiveAircraft()
    {
        AircraftFlightIdentity[] aircraft =
            FindObjectsByType<AircraftFlightIdentity>(
                FindObjectsSortMode.None);

        long now = GetCurrentAbsoluteSeconds();

        foreach (AircraftFlightIdentity identity in aircraft)
        {
            if (identity == null || !identity.IsInitialized)
                continue;

            if (!entriesByOperationId.TryGetValue(
                    identity.OperationId, out FlightEntry entry))
            {
                continue;
            }

            AircraftStatus aircraftStatus =
                identity.GetComponent<AircraftStatus>();

            AircraftCargoPaxTakeoffController takeoff =
                identity.GetComponent<AircraftCargoPaxTakeoffController>();

            if (aircraftStatus != null &&
                aircraftStatus.HasCompletedLanding &&
                entry.ActualArrivalSeconds < 0)
            {
                entry.ActualArrivalSeconds = now;
            }

            if (takeoff != null && takeoff.IsTakingOff)
            {
                entry.Status = "Taking off";
            }
            else if (aircraftStatus == null ||
                     !aircraftStatus.HasCompletedLanding)
            {
                entry.Status = "Approaching";
            }
            else if (aircraftStatus.IsTaxiing)
            {
                entry.Status = "Taxiing";
            }
            else if (aircraftStatus.IsParked)
            {
                entry.Status = "Turnaround";
            }
            else
            {
                entry.Status = "Landed";
            }
        }
    }

    /// <summary>
    /// Call this immediately before the aircraft is destroyed after takeoff.
    /// </summary>
    public void MarkDeparted(AircraftFlightIdentity identity)
    {
        if (identity == null || !identity.IsInitialized)
            return;

        if (!entriesByOperationId.TryGetValue(
                identity.OperationId, out FlightEntry entry))
        {
            return;
        }

        long now = GetCurrentAbsoluteSeconds();

        if (entry.ActualArrivalSeconds < 0)
            entry.ActualArrivalSeconds = now;

        if (entry.ActualDepartureSeconds < 0)
            entry.ActualDepartureSeconds = now;

        entry.Status = "Departed";

        RenderBoard();
    }

    private long GetCurrentAbsoluteSeconds()
    {
        long dayIndex = gameTimeManager.CurrentDay - 1;

        return dayIndex * SecondsPerDay +
               gameTimeManager.GetGameTimeSeconds();
    }

    private long GetScheduledArrivalSeconds(FlightEntry entry)
    {
        return entry.ScheduledDayIndex * SecondsPerDay +
               entry.ScheduledArrivalMinute * 60L;
    }

    private long GetScheduledDepartureSeconds(FlightEntry entry)
    {
        return GetScheduledArrivalSeconds(entry) +
               entry.Definition.TurnaroundMinutes * 60L;
    }

    private string GetDelayLabel(FlightEntry entry)
    {
        long now = GetCurrentAbsoluteSeconds();
        long delaySeconds = 0;
        string label = "On time";

        if (entry.ActualDepartureSeconds >= 0)
        {
            delaySeconds =
                entry.ActualDepartureSeconds -
                GetScheduledDepartureSeconds(entry);

            label = "DEP";
        }
        else if (entry.ActualArrivalSeconds >= 0)
        {
            // Once the scheduled departure time passes, report departure delay
            // until takeoff actually occurs.
            if (now > GetScheduledDepartureSeconds(entry))
            {
                delaySeconds =
                    now - GetScheduledDepartureSeconds(entry);
                label = "DEP";
            }
            else
            {
                delaySeconds =
                    entry.ActualArrivalSeconds -
                    GetScheduledArrivalSeconds(entry);
                label = "ARR";
            }
        }
        else if (now > GetScheduledArrivalSeconds(entry))
        {
            delaySeconds =
                now - GetScheduledArrivalSeconds(entry);
            label = "ARR";
        }
        else
        {
            return "On time";
        }

        int delayMinutes = Mathf.RoundToInt(
            Math.Abs(delaySeconds) / 60f);

        if (Math.Abs(delaySeconds) <= onTimeToleranceMinutes * 60L)
            return "On time";

        if (delaySeconds < 0)
            return $"{label} {delayMinutes}m early";

        return $"{label} +{delayMinutes}m";
    }

    private void RenderBoard()
    {
        if (boardText == null)
            return;

        if (!scheduleLoaded)
        {
            boardText.text =
                "<b>AIRPORT OPERATIONS</b>\n\n" +
                "No saved flight schedule found.\n" +
                "Create or save a schedule to display flights.";
            return;
        }

        StringBuilder builder = new StringBuilder();

        builder.AppendLine("<b>AIRPORT OPERATIONS</b>");
        builder.AppendLine(
            $"DAY {gameTimeManager.CurrentDay}  |  " +
            $"{gameTimeManager.FormattedTime}  |  " +
            $"SCHEDULED FLIGHTS: {entries.Count}");
        builder.AppendLine(
            "ARR SCH / ACT       DEP SCH / ACT       STATUS / DELAY");
        builder.AppendLine(
            "------------------------------------------------------------");

        if (entries.Count == 0)
        {
            builder.AppendLine("No flights scheduled for today.");
            boardText.text = builder.ToString();
            return;
        }

        int delayedCount = 0;
        int activeCount = 0;

        foreach (FlightEntry entry in entries)
        {
            if (entry.Status != "Scheduled" &&
                entry.Status != "Departed")
            {
                activeCount++;
            }

            string delay = GetDelayLabel(entry);

            if (delay.Contains("+"))
                delayedCount++;

            string scheduledArrival =
                FormatClock(GetScheduledArrivalSeconds(entry));

            string actualArrival =
                entry.ActualArrivalSeconds >= 0
                    ? FormatClock(entry.ActualArrivalSeconds)
                    : "--:--";

            string scheduledDeparture =
                FormatClock(GetScheduledDepartureSeconds(entry));

            string actualDeparture =
                entry.ActualDepartureSeconds >= 0
                    ? FormatClock(entry.ActualDepartureSeconds)
                    : "--:--";

            string statusColor = GetStatusColor(entry.Status);
            string delayColor = delay.Contains("+")
                ? "#FF7777"
                : "#83DCA5";

            builder.AppendLine(
                $"<b>{entry.FlightNumber}</b>  " +
                $"<color=#B9C8D8>{entry.Definition.OperationLabel}</color>  " +
                $"<color={statusColor}>{entry.Status}</color>");

            builder.AppendLine(
                $"  ARR {scheduledArrival} / {actualArrival}    " +
                $"DEP {scheduledDeparture} / {actualDeparture}");

            builder.AppendLine(
                $"  Gate: {entry.GateStableId}    " +
                $"<color={delayColor}>{delay}</color>");

            builder.AppendLine();
        }

        builder.AppendLine(
            $"ACTIVE: {activeCount}    DELAYED: {delayedCount}");
        builder.AppendLine(
            "<size=80%>ARR/DEP = scheduled time / actual time. " +
            "Departure is estimated from scheduled arrival + turnaround.</size>");

        boardText.text = builder.ToString();
    }

    private string GetStatusColor(string status)
    {
        switch (status)
        {
            case "Departed":
                return "#83DCA5";

            case "Taking off":
            case "Taxiing":
            case "Turnaround":
                return "#FFD166";

            case "Approaching":
            case "Landed":
                return "#7DD3FC";

            default:
                return "#D7E0EA";
        }
    }

    private string FormatClock(long absoluteSeconds)
    {
        int secondsOfDay =
            (int)((absoluteSeconds % SecondsPerDay + SecondsPerDay) %
                  SecondsPerDay);

        int hours = secondsOfDay / 3600;
        int minutes = (secondsOfDay % 3600) / 60;

        return $"{hours:00}:{minutes:00}";
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
