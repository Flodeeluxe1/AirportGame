
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public class FlightScheduleBoardPanel : MonoBehaviour
{
    private const string ScheduleFileName = "flight_schedule.json";

    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private TestFlightCatalog flightCatalog;
    [SerializeField] private AircraftGateRegistry gateRegistry;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Display")]
    [SerializeField, Min(0.1f)] private float refreshInterval = 0.5f;
    [SerializeField, Min(0)] private int onTimeToleranceMinutes = 5;

    [Serializable]
    private class ScheduleFile
    {
        public int version = 1;
        public List<ScheduledFlight> flights = new List<ScheduledFlight>();
    }

    [Serializable]
    private class ScheduledFlight
    {
        public string flightNumber;
        public string gateStableId;
        public int startMinute;
    }

    private class FlightRecord
    {
        public int? actualArrivalMinute;
        public int? actualDepartureMinute;
    }

    private readonly Dictionary<string, FlightDefinition> definitions =
        new Dictionary<string, FlightDefinition>();

    private readonly Dictionary<string, FlightRecord> records =
        new Dictionary<string, FlightRecord>();

    private readonly List<ScheduledFlight> scheduledFlights =
        new List<ScheduledFlight>();

    private VisualElement root;
    private VisualElement window;
    private VisualElement rows;
    private Label clockLabel;
    private Label summaryLabel;
    private Button openButton;

    private string selectedFilter = "ALL";
    private float nextRefreshTime;
    private long observedWriteTicks = long.MinValue;
    private long observedDay = -1;
    private bool windowOpen;

    private static FlightScheduleBoardPanel instance;

    private string ScheduleFilePath =>
        Path.Combine(Application.persistentDataPath, ScheduleFileName);

    private void Awake()
    {
        instance = this;

        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (flightCatalog == null)
            flightCatalog = FindFirstObjectByType<TestFlightCatalog>();

        if (gateRegistry == null)
            gateRegistry = FindFirstObjectByType<AircraftGateRegistry>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (uiDocument == null || gameTimeManager == null)
        {
            Debug.LogError(
                "FlightScheduleBoardPanel requires a UIDocument and GameTimeManager.",
                this);

            enabled = false;
            return;
        }

        RefreshDefinitions();
        root = uiDocument.rootVisualElement;

        CreateOpenButton();
        CreateWindow();

        window.style.display = DisplayStyle.None;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        if (root == null || gameTimeManager == null)
            return;

        if (observedDay != gameTimeManager.CurrentDay)
        {
            observedDay = gameTimeManager.CurrentDay;
            records.Clear();
        }

        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime =
            Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);

        ReloadScheduleIfChanged();
        CaptureArrivalTimes();

        if (windowOpen)
            RefreshBoard();
    }

    // ------------------------------------------------------------
    // Button and independent window
    // ------------------------------------------------------------

    private void CreateOpenButton()
    {
        openButton = new Button(ToggleWindow)
        {
            text = "Flightstatus"
        };

        openButton.name = "flightstatus-open-button";
        openButton.style.position = Position.Absolute;

        // Flight Management currently sits at right = 16,
        // with a width of approximately 170 pixels.
        // This places Flightstatus immediately to its left.
        openButton.style.right = 194;
        openButton.style.bottom = 14;
        openButton.style.height = 42;
        openButton.style.minWidth = 170;
        openButton.style.paddingLeft = 14;
        openButton.style.paddingRight = 14;

        openButton.style.backgroundColor =
            (Color)new Color32(35, 105, 120, 255);

        openButton.style.color = Color.white;
        openButton.style.borderTopLeftRadius = 6;
        openButton.style.borderTopRightRadius = 6;
        openButton.style.borderBottomLeftRadius = 6;
        openButton.style.borderBottomRightRadius = 6;

        root.Add(openButton);
    }

    private void CreateWindow()
    {
        window = new VisualElement();
        window.name = "flightstatus-window";

        window.style.position = Position.Absolute;
        window.style.left = new Length(12, LengthUnit.Percent);
        window.style.right = new Length(12, LengthUnit.Percent);
        window.style.top = 65;
        window.style.bottom = 70;

        window.style.flexDirection = FlexDirection.Column;
        window.style.paddingLeft = 14;
        window.style.paddingRight = 14;
        window.style.paddingTop = 12;
        window.style.paddingBottom = 12;

        window.style.backgroundColor =
            (Color)new Color32(24, 34, 46, 250);

        window.style.borderTopLeftRadius = 8;
        window.style.borderTopRightRadius = 8;
        window.style.borderBottomLeftRadius = 8;
        window.style.borderBottomRightRadius = 8;

        // Add directly to the document root, NOT the
        // Flight Management window.
        root.Add(window);

        CreateHeader();
        CreateFilters();
        CreateColumnHeader();

        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.name = "flightstatus-scroll";
        scroll.style.flexGrow = 1;
        scroll.style.minHeight = 0;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

        rows = scroll.contentContainer;
        rows.style.flexDirection = FlexDirection.Column;

        window.Add(scroll);
    }

    private void CreateHeader()
    {
        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.marginBottom = 6;

        Label title = new Label("FLIGHT STATUS");
        title.style.fontSize = 21;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;

        VisualElement rightSide = new VisualElement();
        rightSide.style.flexDirection = FlexDirection.Row;
        rightSide.style.alignItems = Align.Center;

        clockLabel = new Label("--:--");
        clockLabel.style.fontSize = 13;
        clockLabel.style.color = (Color)new Color32(125, 211, 252, 255);
        clockLabel.style.marginRight = 12;

        Button closeButton = new Button(CloseWindow)
        {
            text = "Close  X"
        };

        rightSide.Add(clockLabel);
        rightSide.Add(closeButton);

        header.Add(title);
        header.Add(rightSide);
        window.Add(header);

        summaryLabel = new Label("Loading scheduled flights...");
        summaryLabel.style.fontSize = 11;
        summaryLabel.style.color = (Color)new Color32(190, 203, 218, 255);
        summaryLabel.style.marginBottom = 8;

        window.Add(summaryLabel);
    }

    private void CreateFilters()
    {
        VisualElement filters = new VisualElement();
        filters.style.flexDirection = FlexDirection.Row;
        filters.style.marginBottom = 8;

        AddFilterButton(filters, "ALL");
        AddFilterButton(filters, "ARRIVALS");
        AddFilterButton(filters, "DEPARTURES");

        window.Add(filters);
    }

    private void AddFilterButton(VisualElement parent, string filter)
    {
        Button button = new Button(() =>
        {
            selectedFilter = filter;
            RefreshBoard();
        });

        button.text = filter;
        button.style.marginRight = 5;
        button.style.fontSize = 11;

        parent.Add(button);
    }

    private void CreateColumnHeader()
    {
        Label heading = new Label(
            "FLIGHT / TYPE                 STATUS                  " +
            "ARRIVAL SCHEDULED / ACTUAL          " +
            "DEPARTURE SCHEDULED / ACTUAL          DELAY");

        heading.style.fontSize = 10;
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.color = (Color)new Color32(125, 211, 252, 255);
        heading.style.marginBottom = 6;

        window.Add(heading);
    }

    private void ToggleWindow()
    {
        if (windowOpen)
            CloseWindow();
        else
            OpenWindow();
    }

    private void OpenWindow()
    {
        windowOpen = true;
        window.style.display = DisplayStyle.Flex;
        RefreshBoard();
    }

    private void CloseWindow()
    {
        windowOpen = false;
        window.style.display = DisplayStyle.None;
    }

    // ------------------------------------------------------------
    // Schedule and flight data
    // ------------------------------------------------------------

    private void RefreshDefinitions()
    {
        definitions.Clear();

        if (flightCatalog == null)
            return;

        foreach (FlightDefinition definition in flightCatalog.Flights)
        {
            if (definition != null &&
                !string.IsNullOrWhiteSpace(definition.FlightNumber))
            {
                definitions[definition.FlightNumber] = definition;
            }
        }
    }

    private void ReloadScheduleIfChanged()
    {
        if (!File.Exists(ScheduleFilePath))
        {
            if (observedWriteTicks != long.MinValue)
            {
                scheduledFlights.Clear();
                observedWriteTicks = long.MinValue;
            }

            return;
        }

        try
        {
            long writeTicks =
                File.GetLastWriteTimeUtc(ScheduleFilePath).Ticks;

            if (writeTicks == observedWriteTicks)
                return;

            string json = File.ReadAllText(ScheduleFilePath);
            ScheduleFile file = JsonUtility.FromJson<ScheduleFile>(json);

            if (file == null || file.flights == null)
                return;

            scheduledFlights.Clear();
            scheduledFlights.AddRange(file.flights);
            observedWriteTicks = writeTicks;

            RefreshDefinitions();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"Flight status board could not read the schedule: " +
                exception.Message,
                this);
        }
    }

    private FlightRecord GetOrCreateRecord(string flightNumber)
    {
        if (!records.TryGetValue(flightNumber, out FlightRecord record))
        {
            record = new FlightRecord();
            records.Add(flightNumber, record);
        }

        return record;
    }

    private void CaptureArrivalTimes()
    {
        AircraftFlightIdentity[] identities =
            FindObjectsByType<AircraftFlightIdentity>(
                FindObjectsSortMode.None);

        foreach (AircraftFlightIdentity identity in identities)
        {
            if (identity == null ||
                !identity.IsInitialized ||
                string.IsNullOrEmpty(identity.FlightNumber))
            {
                continue;
            }

            AircraftCargoPaxLandingController landing =
                identity.GetComponent<AircraftCargoPaxLandingController>();

            if (landing == null || !landing.IsLandingComplete)
                continue;

            FlightRecord record = GetOrCreateRecord(identity.FlightNumber);

            if (!record.actualArrivalMinute.HasValue)
                record.actualArrivalMinute = CurrentMinuteOfDay();
        }
    }

    /// <summary>
    /// Call this before the aircraft is destroyed in CompleteTakeoff().
    /// </summary>
    public static void RecordDeparture(AircraftFlightIdentity identity)
    {
        if (instance == null ||
            identity == null ||
            !identity.IsInitialized ||
            string.IsNullOrEmpty(identity.FlightNumber))
        {
            return;
        }

        FlightRecord record =
            instance.GetOrCreateRecord(identity.FlightNumber);

        if (!record.actualDepartureMinute.HasValue)
            record.actualDepartureMinute = instance.CurrentMinuteOfDay();
    }

    private int CurrentMinuteOfDay()
    {
        return gameTimeManager.GetGameTimeSeconds() / 60;
    }

    // ------------------------------------------------------------
    // Board refresh and status
    // ------------------------------------------------------------

    private void RefreshBoard()
    {
        if (rows == null)
            return;

        rows.Clear();

        clockLabel.text =
            $"DAY {gameTimeManager.CurrentDay}  " +
            gameTimeManager.FormattedTime;

        int arrivals = 0;
        int departures = 0;
        int delayed = 0;

        foreach (ScheduledFlight flight in scheduledFlights)
        {
            if (flight == null ||
                string.IsNullOrWhiteSpace(flight.flightNumber))
            {
                continue;
            }

            definitions.TryGetValue(
                flight.flightNumber,
                out FlightDefinition definition);

            int turnaround =
                definition != null ? definition.TurnaroundMinutes : 0;

            int scheduledArrival = flight.startMinute;
            int scheduledDeparture =
                (scheduledArrival + turnaround) % 1440;

            records.TryGetValue(
                flight.flightNumber,
                out FlightRecord record);

            AircraftFlightIdentity identity =
                FindActiveIdentity(flight.flightNumber);

            string status = GetStatus(identity, record);

            bool hasArrived =
                record != null && record.actualArrivalMinute.HasValue;

            bool isDeparture =
                hasArrived ||
                status == "Taxiing out" ||
                status == "Ready for takeoff" ||
                status == "Departed";

            if (isDeparture)
                departures++;
            else
                arrivals++;

            int? actualArrival =
                record != null ? record.actualArrivalMinute : null;

            int? actualDeparture =
                record != null ? record.actualDepartureMinute : null;

            int? delay = CalculateDelay(
                scheduledArrival,
                scheduledDeparture,
                actualArrival,
                actualDeparture);

            if (delay.HasValue &&
                delay.Value > onTimeToleranceMinutes)
            {
                delayed++;
            }

            if (selectedFilter == "ARRIVALS" && isDeparture)
                continue;

            if (selectedFilter == "DEPARTURES" && !isDeparture)
                continue;

            rows.Add(CreateFlightRow(
                flight,
                definition,
                status,
                scheduledArrival,
                actualArrival,
                scheduledDeparture,
                actualDeparture,
                delay));
        }

        summaryLabel.text =
            $"{scheduledFlights.Count} scheduled  |  " +
            $"{arrivals} arrivals  |  {departures} departures  |  " +
            $"{delayed} delayed";
    }

    private AircraftFlightIdentity FindActiveIdentity(string flightNumber)
    {
        AircraftFlightIdentity[] identities =
            FindObjectsByType<AircraftFlightIdentity>(
                FindObjectsSortMode.None);

        foreach (AircraftFlightIdentity identity in identities)
        {
            if (identity != null &&
                identity.IsInitialized &&
                identity.FlightNumber == flightNumber)
            {
                return identity;
            }
        }

        return null;
    }

    private string GetStatus(
        AircraftFlightIdentity identity,
        FlightRecord record)
    {
        if (identity == null)
        {
            if (record != null && record.actualDepartureMinute.HasValue)
                return "Departed";

            if (record != null && record.actualArrivalMinute.HasValue)
                return "Arrived";

            return "Scheduled";
        }

        AircraftCargoPaxLandingController landing =
            identity.GetComponent<AircraftCargoPaxLandingController>();

        AircraftCargoPaxLandingTaxiController arrivalTaxi =
            identity.GetComponent<AircraftCargoPaxLandingTaxiController>();

        AircraftCargoPaxDepartureTaxiController departureTaxi =
            identity.GetComponent<AircraftCargoPaxDepartureTaxiController>();

        if (landing != null && landing.IsGoingAround)
            return "Go-around";

        if (arrivalTaxi != null && arrivalTaxi.HasArrivedAtGate)
            return "At gate";

        if (arrivalTaxi != null && arrivalTaxi.IsTaxiing)
            return "Taxiing in";

        if (landing != null && landing.IsLandingComplete)
            return "Landed";

        if (departureTaxi != null)
        {
            switch (departureTaxi.CurrentState)
            {
                case AircraftCargoPaxDepartureTaxiController.DepartureState.WaitingForGateExit:
                    return "At gate";

                case AircraftCargoPaxDepartureTaxiController.DepartureState.WaitingForRunway:
                case AircraftCargoPaxDepartureTaxiController.DepartureState.TaxiingToHoldingPoint:
                case AircraftCargoPaxDepartureTaxiController.DepartureState.AtHoldingPoint:
                case AircraftCargoPaxDepartureTaxiController.DepartureState.TaxiingToRunwayEntry:
                case AircraftCargoPaxDepartureTaxiController.DepartureState.MovingOntoRunway:
                case AircraftCargoPaxDepartureTaxiController.DepartureState.AligningOnRunway:
                    return "Taxiing out";

                case AircraftCargoPaxDepartureTaxiController.DepartureState.LinedUpReadyForTakeoff:
                    return "Ready for takeoff";
            }
        }

        if (landing != null)
            return "Approaching";

        return "Scheduled";
    }

    private int? CalculateDelay(
        int scheduledArrival,
        int scheduledDeparture,
        int? actualArrival,
        int? actualDeparture)
    {
        if (actualDeparture.HasValue)
        {
            return DifferenceInMinutes(
                actualDeparture.Value,
                scheduledDeparture);
        }

        if (actualArrival.HasValue)
        {
            return DifferenceInMinutes(
                actualArrival.Value,
                scheduledArrival);
        }

        int delay = DifferenceInMinutes(
            CurrentMinuteOfDay(),
            scheduledArrival);

        return delay > 0 ? delay : (int?)null;
    }

    private int DifferenceInMinutes(int actual, int scheduled)
    {
        int difference = actual - scheduled;

        if (difference < -720)
            difference += 1440;
        else if (difference > 720)
            difference -= 1440;

        return difference;
    }

    // ------------------------------------------------------------
    // Flight row display
    // ------------------------------------------------------------

    private VisualElement CreateFlightRow(
        ScheduledFlight flight,
        FlightDefinition definition,
        string status,
        int scheduledArrival,
        int? actualArrival,
        int scheduledDeparture,
        int? actualDeparture,
        int? delay)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Column;
        row.style.paddingTop = 7;
        row.style.paddingBottom = 7;
        row.style.paddingLeft = 8;
        row.style.paddingRight = 8;
        row.style.marginBottom = 4;

        row.style.backgroundColor =
            (Color)new Color32(35, 47, 61, 255);

        VisualElement top = new VisualElement();
        top.style.flexDirection = FlexDirection.Row;
        top.style.justifyContent = Justify.SpaceBetween;

        string operationType =
            definition != null ? definition.OperationLabel : "FLIGHT";

        Label flightLabel =
            new Label($"{flight.flightNumber}  ·  {operationType}");

        flightLabel.style.fontSize = 12;
        flightLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        flightLabel.style.color = Color.white;

        Label statusLabel = new Label(status.ToUpperInvariant());
        statusLabel.style.fontSize = 11;
        statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        statusLabel.style.color = GetStatusColor(status);

        top.Add(flightLabel);
        top.Add(statusLabel);
        row.Add(top);

        string arrivalText =
            $"ARR  {FormatTime(scheduledArrival)} / " +
            $"{(actualArrival.HasValue ? FormatTime(actualArrival.Value) : "--:--")}";

        string departureText =
            $"DEP  {FormatTime(scheduledDeparture)} / " +
            $"{(actualDeparture.HasValue ? FormatTime(actualDeparture.Value) : "--:--")}";

        string delayText = "Pending";

        if (delay.HasValue)
        {
            if (delay.Value > onTimeToleranceMinutes)
                delayText = $"+{delay.Value} min";
            else if (delay.Value < -onTimeToleranceMinutes)
                delayText = $"{Mathf.Abs(delay.Value)} min early";
            else
                delayText = "On time";
        }

        Label timeLabel =
            new Label($"{arrivalText}     {departureText}     {delayText}");

        timeLabel.style.fontSize = 11;
        timeLabel.style.whiteSpace = WhiteSpace.Normal;
        timeLabel.style.color = (Color)new Color32(197, 209, 222, 255);
        timeLabel.style.marginTop = 4;

        row.Add(timeLabel);

        string gateName = flight.gateStableId;

        if (gateRegistry != null &&
            !string.IsNullOrEmpty(flight.gateStableId) &&
            gateRegistry.TryGetGate(
                flight.gateStableId,
                out AircraftGate gate))
        {
            gateName = gate.DisplayName;
        }

        Label gateLabel = new Label(
            $"Gate: {(string.IsNullOrEmpty(gateName) ? "Unassigned" : gateName)}");

        gateLabel.style.fontSize = 10;
        gateLabel.style.color = (Color)new Color32(145, 164, 184, 255);
        gateLabel.style.marginTop = 2;

        row.Add(gateLabel);

        return row;
    }

    private string FormatTime(int minute)
    {
        minute = ((minute % 1440) + 1440) % 1440;
        return $"{minute / 60:00}:{minute % 60:00}";
    }

    private Color GetStatusColor(string status)
    {
        switch (status)
        {
            case "Departed":
            case "At gate":
                return new Color32(134, 239, 172, 255);

            case "Ready for takeoff":
                return new Color32(253, 224, 71, 255);

            case "Go-around":
                return new Color32(252, 165, 165, 255);

            case "Approaching":
            case "Landed":
            case "Taxiing in":
            case "Taxiing out":
                return new Color32(125, 211, 252, 255);

            default:
                return new Color32(203, 213, 225, 255);
        }
    }
}
