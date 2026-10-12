
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class FlightManagementUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private TestFlightCatalog flightCatalog;
    [SerializeField] private AircraftGateRegistry gateRegistry;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Layout")]
    [Min(8f)]
    [SerializeField] private float quarterHourWidth = 16f;

    [Min(36f)]
    [SerializeField] private float gateRowHeight = 48f;

    [Min(120f)]
    [SerializeField] private float gateColumnWidth = 165f;

    private const float MinimumQuarterHourWidth = 16f;
    private const float EndOfDayMarkerWidth = 48f;
    private const float DragThreshold = 8f;
    private const int ScheduleFileVersion = 1;
    private const string ScheduleFileName = "flight_schedule.json";

    private float EffectiveQuarterHourWidth =>
        Mathf.Max(MinimumQuarterHourWidth, quarterHourWidth);

    private float TimelineWidth =>
        EffectiveQuarterHourWidth * 96f;

    private float TimelineContentWidth =>
        TimelineWidth + EndOfDayMarkerWidth;

    [Serializable]
    private class ScheduleSaveData
    {
        public int version = ScheduleFileVersion;
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

    private class ScheduledFlight
    {
        public FlightDefinition Definition;
        public string GateStableId;
        public int StartMinute;
    }

    private readonly List<ScheduledFlight> scheduledFlights =
        new List<ScheduledFlight>();

    private VisualElement root;
    private VisualElement window;

    private Label availableFlightsHeading;
    private ScrollView flightStripScroll;
    private ScrollView gateLabelScroll;
    private ScrollView timelineHeaderScroll;
    private ScrollView timelineBodyScroll;

    private Label statusLabel;
    private Label dragPreviewLabel;
    private Label dragPreviewOverflowLabel;

    private VisualElement dragPreview;
    private VisualElement dragPreviewOverflow;

    private Button openButton;
    private Button closeButton;

    private int previousGameSpeed = 1;
    private bool windowOpen;
    private bool syncingScroll;
    private bool scheduleLoaded;

    private string scheduleLoadMessage =
        "No saved schedule loaded yet.";

    private FlightDefinition draggedFlight;
    private ScheduledFlight draggedScheduledFlight;
    private VisualElement draggedElement;
    private Vector2 dragStartPosition;
    private int dragPointerId = -1;
    private bool dragMoved;

    private Color InvalidPreviewColor =>
        (Color)new Color32(225, 65, 65, 245);

    private Color PreviewTextColor =>
        (Color)new Color32(35, 42, 48, 255);

    private string ScheduleFilePath =>
        Path.Combine(
            Application.persistentDataPath,
            ScheduleFileName
        );

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (flightCatalog == null)
            flightCatalog = FindFirstObjectByType<TestFlightCatalog>();

        if (gateRegistry == null)
            gateRegistry = FindFirstObjectByType<AircraftGateRegistry>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (uiDocument == null)
        {
            Debug.LogError(
                "FlightManagementUIController: UIDocument is missing.");
            enabled = false;
            return;
        }

        if (flightCatalog == null)
        {
            Debug.LogError(
                "FlightManagementUIController: TestFlightCatalog is missing.");
            enabled = false;
            return;
        }

        if (gateRegistry == null)
        {
            Debug.LogError(
                "FlightManagementUIController: AircraftGateRegistry is missing.");
            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;

        CreateOpenButton();
        CreateWindow();

        window.style.display = DisplayStyle.None;
    }

    private void Update()
    {
        if (!windowOpen)
            return;

        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelDrag();
            CloseWindow();
        }
    }

    private void CreateOpenButton()
    {
        openButton = new Button(OpenWindow)
        {
            text = "Flight Management"
        };

        openButton.style.position = Position.Absolute;
        openButton.style.right = 16;
        openButton.style.bottom = 14;
        openButton.style.height = 42;
        openButton.style.minWidth = 170;
        openButton.style.paddingLeft = 14;
        openButton.style.paddingRight = 14;
        openButton.style.backgroundColor =
            (Color)new Color32(45, 78, 105, 255);
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
        window.name = "flight-management-window";

        window.style.position = Position.Absolute;
        window.style.left = new Length(2, LengthUnit.Percent);
        window.style.right = new Length(0.5f, LengthUnit.Percent);
        window.style.top = 52;
        window.style.bottom = 66;
        window.style.flexDirection = FlexDirection.Column;
        window.style.backgroundColor =
            (Color)new Color32(27, 32, 39, 250);
        window.style.borderTopLeftRadius = 8;
        window.style.borderTopRightRadius = 8;
        window.style.borderBottomLeftRadius = 8;
        window.style.borderBottomRightRadius = 8;
        window.style.paddingLeft = 12;
        window.style.paddingRight = 12;
        window.style.paddingTop = 10;
        window.style.paddingBottom = 10;

        root.Add(window);

        CreateTitleBar();
        CreateAvailableFlightsSection();
        CreateScheduleSection();
    }

    private void CreateTitleBar()
    {
        VisualElement titleBar = new VisualElement();
        titleBar.style.flexDirection = FlexDirection.Row;
        titleBar.style.alignItems = Align.Center;
        titleBar.style.justifyContent = Justify.SpaceBetween;
        titleBar.style.marginBottom = 8;

        Label title = new Label("FLIGHT MANAGEMENT");
        title.style.fontSize = 21;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;

        closeButton = new Button(CloseWindow)
        {
            text = "Close  X"
        };

        closeButton.style.minWidth = 80;

        titleBar.Add(title);
        titleBar.Add(closeButton);
        window.Add(titleBar);
    }

    private void CreateAvailableFlightsSection()
    {
        availableFlightsHeading = new Label("AVAILABLE FLIGHTS");
        availableFlightsHeading.style.fontSize = 14;
        availableFlightsHeading.style.unityFontStyleAndWeight =
            FontStyle.Bold;
        availableFlightsHeading.style.color =
            (Color)new Color32(215, 222, 231, 255);
        availableFlightsHeading.style.marginBottom = 2;

        window.Add(availableFlightsHeading);

        Label description = new Label(
            "Drag a flight onto a gate and time. " +
            "Drop a scheduled flight here to unschedule it."
        );

        description.style.fontSize = 11;
        description.style.color =
            (Color)new Color32(165, 176, 189, 255);
        description.style.marginBottom = 4;

        window.Add(description);

        statusLabel = new Label("No flights scheduled yet.");
        statusLabel.style.fontSize = 11;
        statusLabel.style.marginBottom = 4;
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        statusLabel.style.color =
            (Color)new Color32(190, 205, 218, 255);

        window.Add(statusLabel);

        flightStripScroll =
            new ScrollView(ScrollViewMode.Horizontal);

        flightStripScroll.name = "available-flight-strips";
        flightStripScroll.style.height = 114;
        flightStripScroll.style.minHeight = 114;
        flightStripScroll.style.flexShrink = 0;
        flightStripScroll.contentContainer.style.flexDirection =
            FlexDirection.Row;
        flightStripScroll.contentContainer.style.alignItems =
            Align.Stretch;

        window.Add(flightStripScroll);

        RebuildFlightStrips();

        VisualElement separator = new VisualElement();
        separator.style.height = 1;
        separator.style.backgroundColor =
            (Color)new Color32(76, 84, 94, 255);
        separator.style.marginTop = 8;
        separator.style.marginBottom = 8;

        window.Add(separator);
    }

    private void RebuildFlightStrips()
    {
        flightStripScroll.Clear();

        IReadOnlyList<FlightDefinition> flights =
            flightCatalog.Flights;

        foreach (FlightDefinition flight in flights)
        {
            if (IsFlightScheduled(flight))
                continue;

            flightStripScroll.Add(CreateFlightStrip(flight));
        }
    }

    private bool IsFlightScheduled(FlightDefinition flight)
    {
        foreach (ScheduledFlight scheduled in scheduledFlights)
        {
            if (scheduled.Definition.FlightNumber ==
                flight.FlightNumber)
            {
                return true;
            }
        }

        return false;
    }

    private VisualElement CreateFlightStrip(FlightDefinition flight)
    {
        VisualElement strip = new VisualElement();
        strip.name = $"flight-strip-{flight.FlightNumber}";

        float stripWidth =
            Mathf.Max(105f, flight.TurnaroundMinutes * 3f);

        strip.style.width = stripWidth;
        strip.style.minWidth = stripWidth;
        strip.style.height = 78;
        strip.style.flexShrink = 0;
        strip.style.backgroundColor = (Color)flight.StripColor;
        strip.style.paddingLeft = 8;
        strip.style.paddingRight = 8;
        strip.style.paddingTop = 5;
        strip.style.paddingBottom = 5;
        strip.style.marginRight = 6;

        strip.style.borderTopLeftRadius = 5;
        strip.style.borderTopRightRadius = 5;
        strip.style.borderBottomLeftRadius = 5;
        strip.style.borderBottomRightRadius = 5;

        Label flightNumber = new Label(flight.FlightNumber);
        flightNumber.style.fontSize = 15;
        flightNumber.style.unityFontStyleAndWeight = FontStyle.Bold;
        flightNumber.style.color = PreviewTextColor;

        Label turnaround =
            new Label($"{flight.TurnaroundMinutes} min");
        turnaround.style.fontSize = 12;
        turnaround.style.color = PreviewTextColor;

        Label timeframe = new Label(flight.TimeWindow.ToString());
        timeframe.style.fontSize = 10;
        timeframe.style.color = PreviewTextColor;

        Label gateSize =
            new Label($"Gate size: {flight.RequiredGateSize}");
        gateSize.style.fontSize = 10;
        gateSize.style.color = PreviewTextColor;

        strip.tooltip =
            $"{flight.FlightNumber} | {flight.TurnaroundMinutes} min | " +
            $"{flight.TimeWindow} | Requires {flight.RequiredGateSize} gate";

        strip.Add(flightNumber);
        strip.Add(turnaround);
        strip.Add(timeframe);
        strip.Add(gateSize);

        RegisterDragEvents(strip, flight, null);

        return strip;
    }

    private void CreateScheduleSection()
    {
        Label heading = new Label("GATE SCHEDULE — 00:00–24:00");
        heading.style.fontSize = 14;
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.color =
            (Color)new Color32(215, 222, 231, 255);
        heading.style.marginBottom = 6;

        window.Add(heading);

        VisualElement table = new VisualElement();
        table.style.flexGrow = 1;
        table.style.flexDirection = FlexDirection.Column;
        table.style.minHeight = 100;

        window.Add(table);

        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.height = 34;
        header.style.minHeight = 34;

        Label gateHeader = new Label("GATE / STAND");
        gateHeader.style.width = gateColumnWidth;
        gateHeader.style.minWidth = gateColumnWidth;
        gateHeader.style.flexShrink = 0;
        gateHeader.style.color = Color.white;
        gateHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
        gateHeader.style.justifyContent = Justify.Center;
        gateHeader.style.paddingLeft = 4;

        header.Add(gateHeader);

        timelineHeaderScroll =
            new ScrollView(ScrollViewMode.Horizontal);

        timelineHeaderScroll.name = "schedule-time-header";
        timelineHeaderScroll.style.flexGrow = 1;
        timelineHeaderScroll.style.height = 34;
        timelineHeaderScroll.horizontalScrollerVisibility =
            ScrollerVisibility.Auto;
        timelineHeaderScroll.verticalScrollerVisibility =
            ScrollerVisibility.Hidden;
        timelineHeaderScroll.contentContainer.style.flexDirection =
            FlexDirection.Row;

        BuildHourHeader();

        header.Add(timelineHeaderScroll);
        table.Add(header);

        VisualElement body = new VisualElement();
        body.style.flexDirection = FlexDirection.Row;
        body.style.flexGrow = 1;

        gateLabelScroll = new ScrollView(ScrollViewMode.Vertical);
        gateLabelScroll.name = "schedule-gate-labels";
        gateLabelScroll.style.width = gateColumnWidth;
        gateLabelScroll.style.minWidth = gateColumnWidth;
        gateLabelScroll.style.flexShrink = 0;
        gateLabelScroll.verticalScrollerVisibility =
            ScrollerVisibility.Auto;
        gateLabelScroll.horizontalScrollerVisibility =
            ScrollerVisibility.Hidden;

        body.Add(gateLabelScroll);

        timelineBodyScroll =
            new ScrollView(ScrollViewMode.VerticalAndHorizontal);

        timelineBodyScroll.name = "schedule-timeline-body";
        timelineBodyScroll.style.flexGrow = 1;
        timelineBodyScroll.verticalScrollerVisibility =
            ScrollerVisibility.Auto;
        timelineBodyScroll.horizontalScrollerVisibility =
            ScrollerVisibility.Auto;
        timelineBodyScroll.contentContainer.style.flexDirection =
            FlexDirection.Column;

        body.Add(timelineBodyScroll);
        table.Add(body);

        ConnectScrollbars();
        RebuildGateRows();
    }

    private void BuildHourHeader()
    {
        timelineHeaderScroll.Clear();

        VisualElement headerContent =
            timelineHeaderScroll.contentContainer;

        headerContent.style.width = TimelineContentWidth;
        headerContent.style.flexDirection = FlexDirection.Row;
        headerContent.style.flexShrink = 0;

        for (int quarter = 0; quarter < 96; quarter++)
        {
            VisualElement cell = new VisualElement();

            cell.style.position = Position.Relative;
            cell.style.width = EffectiveQuarterHourWidth;
            cell.style.minWidth = EffectiveQuarterHourWidth;
            cell.style.height = 30;
            cell.style.flexShrink = 0;

            cell.style.borderLeftWidth = 1;
            cell.style.borderLeftColor =
                quarter % 4 == 0
                    ? (Color)new Color32(83, 94, 107, 255)
                    : (Color)new Color32(53, 60, 70, 0);

            if (quarter % 4 == 0)
            {
                int hour = quarter / 4;
                Label label = new Label($"{hour:00}:00");

                label.style.position = Position.Absolute;
                label.style.left = 3;
                label.style.top = 0;
                label.style.width = EffectiveQuarterHourWidth * 4f;
                label.style.height = 30;
                label.style.flexShrink = 0;
                label.style.fontSize = 11;
                label.style.color =
                    (Color)new Color32(215, 222, 231, 255);
                label.style.unityTextAlign = TextAnchor.MiddleLeft;

                cell.Add(label);
            }

            headerContent.Add(cell);
        }

        Label endOfDayLabel = new Label("24:00");
        endOfDayLabel.style.width = EndOfDayMarkerWidth;
        endOfDayLabel.style.minWidth = EndOfDayMarkerWidth;
        endOfDayLabel.style.height = 30;
        endOfDayLabel.style.flexShrink = 0;
        endOfDayLabel.style.fontSize = 11;
        endOfDayLabel.style.color =
            (Color)new Color32(215, 222, 231, 255);
        endOfDayLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        endOfDayLabel.style.borderLeftWidth = 1;
        endOfDayLabel.style.borderLeftColor =
            (Color)new Color32(120, 137, 153, 255);
        endOfDayLabel.style.paddingLeft = 3;

        headerContent.Add(endOfDayLabel);
    }

    private void RebuildGateRows()
    {
        gateLabelScroll.Clear();
        timelineBodyScroll.Clear();

        timelineBodyScroll.contentContainer.style.width =
            TimelineContentWidth;

        gateRegistry.RefreshGates();

        IReadOnlyList<AircraftGate> gates = gateRegistry.Gates;

        if (gates.Count == 0)
        {
            Label emptyLabel = new Label(
                "No built aircraft gates found. Build gates, " +
                "then close and reopen Flight Management."
            );

            emptyLabel.style.color =
                (Color)new Color32(220, 220, 220, 255);
            emptyLabel.style.whiteSpace = WhiteSpace.Normal;
            emptyLabel.style.paddingTop = 12;
            emptyLabel.style.paddingRight = 8;

            gateLabelScroll.Add(emptyLabel);
            return;
        }

        foreach (AircraftGate gate in gates)
        {
            gateLabelScroll.Add(CreateGateLabel(gate));
            timelineBodyScroll.Add(CreateTimelineRow(gate));
        }
    }

    private VisualElement CreateGateLabel(AircraftGate gate)
    {
        VisualElement row = new VisualElement();
        row.style.height = gateRowHeight;
        row.style.minHeight = gateRowHeight;
        row.style.width = gateColumnWidth;
        row.style.paddingLeft = 5;
        row.style.justifyContent = Justify.Center;

        row.style.backgroundColor =
            gateLabelScroll.childCount % 2 == 0
                ? (Color)new Color32(39, 45, 54, 255)
                : (Color)new Color32(33, 39, 47, 255);

        row.style.borderBottomWidth = 1;
        row.style.borderBottomColor =
            (Color)new Color32(65, 73, 83, 255);

        Label name = new Label(gate.DisplayName);
        name.style.fontSize = 12;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = Color.white;

        Label size = new Label(gate.Size.ToString());
        size.style.fontSize = 10;
        size.style.color =
            (Color)new Color32(173, 184, 196, 255);

        row.Add(name);
        row.Add(size);

        return row;
    }

    private VisualElement CreateTimelineRow(AircraftGate gate)
    {
        int rowIndex = gateLabelScroll.childCount;

        Color32 rowColor =
            rowIndex % 2 == 0
                ? new Color32(39, 45, 54, 255)
                : new Color32(33, 39, 47, 255);

        VisualElement row = new VisualElement();
        row.name = $"timeline-row-{gate.StableId}";
        row.style.position = Position.Relative;
        row.style.flexDirection = FlexDirection.Row;
        row.style.height = gateRowHeight;
        row.style.minHeight = gateRowHeight;
        row.style.width = TimelineContentWidth;
        row.style.flexShrink = 0;
        row.style.overflow = Overflow.Hidden;
        row.style.backgroundColor = (Color)rowColor;

        row.style.borderBottomWidth = 1;
        row.style.borderBottomColor =
            (Color)new Color32(65, 73, 83, 255);

        for (int quarter = 0; quarter < 96; quarter++)
        {
            VisualElement cell = new VisualElement();

            cell.style.width = EffectiveQuarterHourWidth;
            cell.style.minWidth = EffectiveQuarterHourWidth;
            cell.style.height = gateRowHeight;
            cell.style.flexShrink = 0;

            cell.style.borderRightWidth = 1;
            cell.style.borderRightColor =
                quarter % 4 == 3
                    ? (Color)new Color32(83, 94, 107, 255)
                    : (Color)new Color32(53, 60, 70, 255);

            row.Add(cell);
        }

        VisualElement endOfDayMarker = new VisualElement();
        endOfDayMarker.style.width = EndOfDayMarkerWidth;
        endOfDayMarker.style.minWidth = EndOfDayMarkerWidth;
        endOfDayMarker.style.height = gateRowHeight;
        endOfDayMarker.style.flexShrink = 0;
        endOfDayMarker.style.backgroundColor = (Color)rowColor;
        endOfDayMarker.style.borderLeftWidth = 1;
        endOfDayMarker.style.borderLeftColor =
            (Color)new Color32(120, 137, 153, 255);

        row.Add(endOfDayMarker);

        foreach (ScheduledFlight scheduled in scheduledFlights)
        {
            if (scheduled.GateStableId != gate.StableId)
                continue;

            row.Add(CreateScheduledFlightStrip(scheduled));

            int overflowMinutes = GetOverflowMinutes(scheduled);

            if (overflowMinutes > 0)
            {
                row.Add(
                    CreateScheduledFlightOverflowStrip(
                        scheduled,
                        overflowMinutes
                    )
                );
            }
        }

        return row;
    }

    private int GetOverflowMinutes(ScheduledFlight scheduled)
    {
        return Mathf.Clamp(
            scheduled.StartMinute +
            scheduled.Definition.TurnaroundMinutes - 1440,
            0,
            1440
        );
    }

    private VisualElement CreateScheduledFlightStrip(
        ScheduledFlight scheduled)
    {
        FlightDefinition flight = scheduled.Definition;

        float left =
            scheduled.StartMinute / 15f * EffectiveQuarterHourWidth;

        int visibleMinutes = Mathf.Min(
            flight.TurnaroundMinutes,
            1440 - scheduled.StartMinute
        );

        float visibleWidth =
            visibleMinutes / 15f * EffectiveQuarterHourWidth;

        VisualElement strip = new VisualElement();
        strip.name = $"scheduled-{flight.FlightNumber}";
        strip.style.position = Position.Absolute;
        strip.style.left = left;
        strip.style.top = 4;
        strip.style.width = visibleWidth;
        strip.style.minWidth = visibleWidth;
        strip.style.height = gateRowHeight - 8;
        strip.style.paddingLeft = 2;
        strip.style.paddingRight = 2;
        strip.style.justifyContent = Justify.Center;
        strip.style.backgroundColor = (Color)flight.StripColor;

        strip.style.borderTopLeftRadius = 3;
        strip.style.borderTopRightRadius = 3;
        strip.style.borderBottomLeftRadius = 3;
        strip.style.borderBottomRightRadius = 3;

        string endInfo =
            scheduled.StartMinute + flight.TurnaroundMinutes >= 1440
                ? " (continues after midnight)"
                : string.Empty;

        strip.tooltip =
            $"{flight.FlightNumber} | " +
            $"{FormatTime(scheduled.StartMinute)} | " +
            $"{flight.TurnaroundMinutes} min | " +
            $"{flight.RequiredGateSize} gate{endInfo}";

        Label label = new Label(flight.FlightNumber);
        label.style.fontSize = 9;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = PreviewTextColor;
        label.style.overflow = Overflow.Hidden;
        label.style.whiteSpace = WhiteSpace.NoWrap;

        strip.Add(label);

        RegisterDragEvents(strip, flight, scheduled);

        return strip;
    }

    private VisualElement CreateScheduledFlightOverflowStrip(
        ScheduledFlight scheduled,
        int overflowMinutes)
    {
        FlightDefinition flight = scheduled.Definition;

        float overflowWidth =
            overflowMinutes / 15f * EffectiveQuarterHourWidth;

        VisualElement strip = new VisualElement();
        strip.name = $"scheduled-overflow-{flight.FlightNumber}";
        strip.style.position = Position.Absolute;
        strip.style.left = 0;
        strip.style.top = 4;
        strip.style.width = overflowWidth;
        strip.style.minWidth = overflowWidth;
        strip.style.height = gateRowHeight - 8;
        strip.style.paddingLeft = 2;
        strip.style.paddingRight = 2;
        strip.style.justifyContent = Justify.Center;
        strip.style.backgroundColor = (Color)flight.StripColor;

        strip.style.borderTopLeftRadius = 3;
        strip.style.borderTopRightRadius = 3;
        strip.style.borderBottomLeftRadius = 3;
        strip.style.borderBottomRightRadius = 3;

        Label label = new Label($"{overflowMinutes}m left");
        label.style.fontSize = 8;
        label.style.color = PreviewTextColor;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;

        strip.tooltip =
            $"{flight.FlightNumber} — {overflowMinutes} minutes " +
            "remaining after midnight.";

        strip.Add(label);

        RegisterDragEvents(strip, flight, scheduled);

        return strip;
    }

    // ------------------------------------------------------------
    // Dragging and live preview
    // ------------------------------------------------------------

    private void RegisterDragEvents(
        VisualElement element,
        FlightDefinition flight,
        ScheduledFlight scheduled)
    {
        element.RegisterCallback<PointerDownEvent>(
            evt => BeginDrag(element, flight, scheduled, evt));

        element.RegisterCallback<PointerMoveEvent>(OnDragPointerMove);
        element.RegisterCallback<PointerUpEvent>(OnDragPointerUp);
    }

    private void BeginDrag(
        VisualElement element,
        FlightDefinition flight,
        ScheduledFlight scheduled,
        PointerDownEvent evt)
    {
        if (evt.button != 0 || draggedElement != null)
            return;

        draggedElement = element;
        draggedFlight = flight;
        draggedScheduledFlight = scheduled;
        dragStartPosition = new Vector2(evt.position.x, evt.position.y);
        dragPointerId = evt.pointerId;
        dragMoved = false;

        element.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private void OnDragPointerMove(PointerMoveEvent evt)
    {
        if (draggedElement == null || evt.pointerId != dragPointerId)
            return;

        Vector2 currentPosition =
            new Vector2(evt.position.x, evt.position.y);

        if (!dragMoved &&
            (currentPosition - dragStartPosition).sqrMagnitude >=
            DragThreshold * DragThreshold)
        {
            dragMoved = true;
            draggedElement.style.opacity = 0.3f;

            CreateDragPreview(draggedFlight);
        }

        if (dragMoved)
            UpdateDragPreview(currentPosition);

        evt.StopPropagation();
    }

    private void CreateDragPreview(FlightDefinition flight)
    {
        RemoveDragPreview();

        dragPreview = new VisualElement();
        dragPreview.name = $"drag-preview-{flight.FlightNumber}";
        dragPreview.pickingMode = PickingMode.Ignore;

        dragPreview.style.position = Position.Absolute;
        dragPreview.style.height = gateRowHeight - 8;
        dragPreview.style.flexShrink = 0;
        dragPreview.style.paddingLeft = 5;
        dragPreview.style.paddingRight = 5;
        dragPreview.style.justifyContent = Justify.Center;
        dragPreview.style.backgroundColor = (Color)flight.StripColor;
        dragPreview.style.opacity = 0.96f;

        dragPreview.style.borderTopLeftRadius = 4;
        dragPreview.style.borderTopRightRadius = 4;
        dragPreview.style.borderBottomLeftRadius = 4;
        dragPreview.style.borderBottomRightRadius = 4;

        dragPreviewLabel = new Label(
            $"{flight.FlightNumber}  |  {flight.TurnaroundMinutes} min"
        );

        dragPreviewLabel.style.fontSize = 10;
        dragPreviewLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        dragPreviewLabel.style.color = PreviewTextColor;
        dragPreviewLabel.style.whiteSpace = WhiteSpace.NoWrap;

        dragPreview.Add(dragPreviewLabel);
        root.Add(dragPreview);

        dragPreviewOverflow = new VisualElement();
        dragPreviewOverflow.name = "flight-drag-overflow-preview";
        dragPreviewOverflow.pickingMode = PickingMode.Ignore;

        dragPreviewOverflow.style.position = Position.Absolute;
        dragPreviewOverflow.style.height = gateRowHeight - 8;
        dragPreviewOverflow.style.paddingLeft = 2;
        dragPreviewOverflow.style.paddingRight = 2;
        dragPreviewOverflow.style.justifyContent = Justify.Center;
        dragPreviewOverflow.style.borderTopLeftRadius = 4;
        dragPreviewOverflow.style.borderTopRightRadius = 4;
        dragPreviewOverflow.style.borderBottomLeftRadius = 4;
        dragPreviewOverflow.style.borderBottomRightRadius = 4;
        dragPreviewOverflow.style.display = DisplayStyle.None;

        dragPreviewOverflowLabel = new Label();
        dragPreviewOverflowLabel.style.fontSize = 8;
        dragPreviewOverflowLabel.style.whiteSpace = WhiteSpace.NoWrap;
        dragPreviewOverflowLabel.style.overflow = Overflow.Hidden;

        dragPreviewOverflow.Add(dragPreviewOverflowLabel);
        root.Add(dragPreviewOverflow);
    }

    private void UpdateDragPreview(Vector2 panelPosition)
    {
        if (dragPreview == null || draggedFlight == null)
            return;

        // Returning to the Available Flights area is a valid drop
        // for an already scheduled flight.
        if (IsOverAvailableFlights(panelPosition))
        {
            dragPreview.style.backgroundColor =
                (Color)draggedFlight.StripColor;

            dragPreviewLabel.style.color = PreviewTextColor;

            dragPreviewOverflow.style.display = DisplayStyle.None;

            float availableStripWidth =
                Mathf.Max(105f, draggedFlight.TurnaroundMinutes * 3f);

            dragPreview.style.width = availableStripWidth;
            dragPreview.style.minWidth = availableStripWidth;
            dragPreview.style.height = 78;

            Vector2 rootPosition = root.WorldToLocal(panelPosition);

            dragPreview.style.left =
                rootPosition.x - availableStripWidth * 0.5f;

            dragPreview.style.top = rootPosition.y - 39f;

            dragPreview.tooltip =
                draggedScheduledFlight != null
                    ? $"Release to return {draggedFlight.FlightNumber} " +
                      "to Available Flights."
                    : $"{draggedFlight.FlightNumber} is already available.";

            return;
        }

        EvaluateDropTarget(
            panelPosition,
            draggedFlight,
            draggedScheduledFlight,
            out bool hasTarget,
            out int rowIndex,
            out AircraftGate gate,
            out int startMinute,
            out bool compatible,
            out string reason
        );

        Color previewColor = compatible
            ? (Color)draggedFlight.StripColor
            : InvalidPreviewColor;

        Color previewTextColor = compatible
            ? PreviewTextColor
            : Color.white;

        dragPreview.style.backgroundColor = previewColor;
        dragPreviewLabel.style.color = previewTextColor;
        dragPreview.style.height = gateRowHeight - 8;

        if (hasTarget)
        {
            int visibleMinutes = Mathf.Min(
                draggedFlight.TurnaroundMinutes,
                1440 - startMinute
            );

            float visibleWidth =
                visibleMinutes / 15f * EffectiveQuarterHourWidth;

            dragPreview.style.width = visibleWidth;
            dragPreview.style.minWidth = visibleWidth;

            Vector2 timelineLocalPosition = new Vector2(
                startMinute / 15f * EffectiveQuarterHourWidth,
                rowIndex * gateRowHeight + 4f
            );

            Vector2 panelTargetPosition =
                timelineBodyScroll.contentContainer.LocalToWorld(
                    timelineLocalPosition
                );

            Vector2 rootPosition =
                root.WorldToLocal(panelTargetPosition);

            dragPreview.style.left = rootPosition.x;
            dragPreview.style.top = rootPosition.y;

            dragPreview.tooltip = compatible
                ? $"{draggedFlight.FlightNumber} → {gate.DisplayName}, " +
                  $"{FormatTime(startMinute)}"
                : reason;

            int overflowMinutes = Mathf.Clamp(
                draggedFlight.TurnaroundMinutes -
                (1440 - startMinute),
                0,
                1440
            );

            if (overflowMinutes > 0)
            {
                float overflowWidth =
                    overflowMinutes / 15f * EffectiveQuarterHourWidth;

                Vector2 overflowTimelinePosition =
                    timelineBodyScroll.contentContainer.LocalToWorld(
                        new Vector2(
                            0,
                            rowIndex * gateRowHeight + 4f
                        )
                    );

                Vector2 overflowRootPosition =
                    root.WorldToLocal(overflowTimelinePosition);

                dragPreviewOverflow.style.display = DisplayStyle.Flex;
                dragPreviewOverflow.style.left = overflowRootPosition.x;
                dragPreviewOverflow.style.top = overflowRootPosition.y;
                dragPreviewOverflow.style.width = overflowWidth;
                dragPreviewOverflow.style.minWidth = overflowWidth;
                dragPreviewOverflow.style.height = gateRowHeight - 8;

                dragPreviewOverflow.style.backgroundColor = previewColor;
                dragPreviewOverflowLabel.style.color = previewTextColor;
                dragPreviewOverflowLabel.text =
                    $"{overflowMinutes}m left";

                dragPreviewOverflow.tooltip =
                    $"{draggedFlight.FlightNumber} — " +
                    $"{overflowMinutes} minutes remaining after midnight.";
            }
            else
            {
                dragPreviewOverflow.style.display = DisplayStyle.None;
            }
        }
        else
        {
            dragPreviewOverflow.style.display = DisplayStyle.None;

            float width = Mathf.Max(
                90f,
                draggedFlight.TurnaroundMinutes / 15f *
                EffectiveQuarterHourWidth
            );

            Vector2 rootPosition = root.WorldToLocal(panelPosition);

            dragPreview.style.width = width;
            dragPreview.style.minWidth = width;
            dragPreview.style.left = rootPosition.x - width * 0.5f;
            dragPreview.style.top =
                rootPosition.y - (gateRowHeight - 8) * 0.5f;

            dragPreview.tooltip =
                string.IsNullOrEmpty(reason)
                    ? "Drop onto a gate row."
                    : reason;
        }
    }

    private bool IsOverAvailableFlights(Vector2 panelPosition)
    {
        if (window == null ||
            availableFlightsHeading == null ||
            flightStripScroll == null)
        {
            return false;
        }

        // The drop target covers the Available Flights heading through
        // the bottom of the strips area, including its scroll region.
        Rect windowBounds = window.worldBound;
        Rect headingBounds = availableFlightsHeading.worldBound;
        Rect stripBounds = flightStripScroll.worldBound;

        float top = headingBounds.yMin;
        float bottom = stripBounds.yMax;

        if (bottom <= top)
            return false;

        Rect dropZone = new Rect(
            windowBounds.xMin,
            top,
            windowBounds.width,
            bottom - top
        );

        return dropZone.Contains(panelPosition);
    }

    private void OnDragPointerUp(PointerUpEvent evt)
    {
        if (draggedElement == null || evt.pointerId != dragPointerId)
            return;

        VisualElement sourceElement = draggedElement;
        FlightDefinition flight = draggedFlight;
        ScheduledFlight existing = draggedScheduledFlight;
        bool moved = dragMoved;

        Vector2 dropPosition =
            new Vector2(evt.position.x, evt.position.y);

        if (sourceElement.HasPointerCapture(evt.pointerId))
            sourceElement.ReleasePointer(evt.pointerId);

        sourceElement.style.opacity = 1f;

        ResetDragState();

        if (!moved)
        {
            evt.StopPropagation();
            return;
        }

        // A scheduled flight dropped in Available Flights is unscheduled.
        if (IsOverAvailableFlights(dropPosition))
        {
            if (existing != null)
            {
                scheduledFlights.Remove(existing);

                RebuildFlightStrips();
                RebuildGateRows();

                SetStatus(
                    $"{flight.FlightNumber} returned to Available Flights.",
                    true
                );
            }
            else
            {
                SetStatus(
                    $"{flight.FlightNumber} remains in Available Flights.",
                    true
                );
            }

            evt.StopPropagation();
            return;
        }

        TryScheduleDrop(flight, existing, dropPosition);

        evt.StopPropagation();
    }

    private void RemoveDragPreview()
    {
        if (dragPreview != null)
        {
            dragPreview.RemoveFromHierarchy();
            dragPreview = null;
            dragPreviewLabel = null;
        }

        if (dragPreviewOverflow != null)
        {
            dragPreviewOverflow.RemoveFromHierarchy();
            dragPreviewOverflow = null;
            dragPreviewOverflowLabel = null;
        }
    }

    private void ResetDragState()
    {
        if (draggedElement != null)
            draggedElement.style.opacity = 1f;

        RemoveDragPreview();

        draggedFlight = null;
        draggedScheduledFlight = null;
        draggedElement = null;
        dragPointerId = -1;
        dragMoved = false;
    }

    private void CancelDrag()
    {
        if (draggedElement != null)
        {
            if (dragPointerId >= 0 &&
                draggedElement.HasPointerCapture(dragPointerId))
            {
                draggedElement.ReleasePointer(dragPointerId);
            }

            draggedElement.style.opacity = 1f;
        }

        ResetDragState();
    }

    // ------------------------------------------------------------
    // Drop validation and scheduling
    // ------------------------------------------------------------

    private void EvaluateDropTarget(
        Vector2 panelPosition,
        FlightDefinition flight,
        ScheduledFlight existing,
        out bool hasTarget,
        out int rowIndex,
        out AircraftGate gate,
        out int startMinute,
        out bool compatible,
        out string reason)
    {
        hasTarget = false;
        rowIndex = -1;
        gate = null;
        startMinute = 0;
        compatible = false;
        reason = "Drop the flight onto a gate row.";

        if (timelineBodyScroll == null)
            return;

        Vector2 localPosition =
            timelineBodyScroll.contentContainer.WorldToLocal(panelPosition);

        if (localPosition.x < 0 || localPosition.x >= TimelineWidth)
        {
            reason = "Drop the flight inside the 00:00–24:00 timeline.";
            return;
        }

        IReadOnlyList<AircraftGate> gates = gateRegistry.Gates;

        rowIndex = Mathf.FloorToInt(
            localPosition.y / gateRowHeight
        );

        if (rowIndex < 0 || rowIndex >= gates.Count)
        {
            reason = "Drop the flight onto a built gate row.";
            return;
        }

        gate = gates[rowIndex];

        int quarter = Mathf.Clamp(
            Mathf.RoundToInt(
                localPosition.x / EffectiveQuarterHourWidth
            ),
            0,
            95
        );

        startMinute = quarter * 15;
        hasTarget = true;

        compatible = CanPlaceFlight(
            flight,
            gate,
            startMinute,
            existing,
            out reason
        );
    }

    private void TryScheduleDrop(
        FlightDefinition flight,
        ScheduledFlight existing,
        Vector2 panelPosition)
    {
        EvaluateDropTarget(
            panelPosition,
            flight,
            existing,
            out bool hasTarget,
            out int rowIndex,
            out AircraftGate gate,
            out int startMinute,
            out bool compatible,
            out string reason
        );

        if (!hasTarget || !compatible)
        {
            SetStatus(reason, false);
            return;
        }

        if (existing == null)
        {
            existing = new ScheduledFlight
            {
                Definition = flight,
                GateStableId = gate.StableId,
                StartMinute = startMinute
            };

            scheduledFlights.Add(existing);
        }
        else
        {
            existing.GateStableId = gate.StableId;
            existing.StartMinute = startMinute;
        }

        RebuildFlightStrips();
        RebuildGateRows();

        SetStatus(
            $"{flight.FlightNumber} scheduled at {gate.DisplayName}, " +
            $"{FormatTime(startMinute)}. Drag it to move it.",
            true
        );
    }

    private bool CanPlaceFlight(
        FlightDefinition flight,
        AircraftGate gate,
        int startMinute,
        ScheduledFlight ignore,
        out string reason)
    {
        if (!gate.CanAccommodate(flight.RequiredGateSize))
        {
            reason =
                $"{gate.DisplayName} is {gate.Size}; " +
                $"{flight.FlightNumber} requires " +
                $"{flight.RequiredGateSize} or larger.";

            return false;
        }

        if (!flight.AllowsStartTime(startMinute))
        {
            FlightDefinition.GetWindowBounds(
                flight.TimeWindow,
                out int windowStart,
                out int windowEnd
            );

            reason =
                $"{flight.FlightNumber} must start during " +
                $"{FormatTime(windowStart)}–{FormatTime(windowEnd)} " +
                $"({flight.TimeWindow}).";

            return false;
        }

        foreach (ScheduledFlight other in scheduledFlights)
        {
            if (ReferenceEquals(other, ignore))
                continue;

            if (other.GateStableId != gate.StableId)
                continue;

            if (IntervalsOverlapDaily(
                    startMinute,
                    flight.TurnaroundMinutes,
                    other.StartMinute,
                    other.Definition.TurnaroundMinutes))
            {
                reason =
                    $"{gate.DisplayName} conflicts with " +
                    $"{other.Definition.FlightNumber} at " +
                    $"{FormatTime(other.StartMinute)}.";

                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private bool IntervalsOverlapDaily(
        int startA,
        int durationA,
        int startB,
        int durationB)
    {
        for (int dayOffset = -1440;
             dayOffset <= 1440;
             dayOffset += 1440)
        {
            int shiftedStartB = startB + dayOffset;

            int endA = startA + durationA;
            int endB = shiftedStartB + durationB;

            if (startA < endB && shiftedStartB < endA)
                return true;
        }

        return false;
    }

    private string FormatTime(int minutes)
    {
        minutes = Mathf.Clamp(minutes, 0, 1440);

        if (minutes == 1440)
            return "24:00";

        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private void SetStatus(string message, bool success)
    {
        if (statusLabel == null)
            return;

        statusLabel.text = message;
        statusLabel.style.color = success
            ? (Color)new Color32(165, 210, 165, 255)
            : (Color)new Color32(245, 155, 145, 255);
    }

    // ------------------------------------------------------------
    // Schedule persistence
    // ------------------------------------------------------------

    private void LoadScheduleIfNeeded()
    {
        if (scheduleLoaded)
            return;

        gateRegistry.RefreshGates();
        scheduledFlights.Clear();

        if (!File.Exists(ScheduleFilePath))
        {
            scheduleLoaded = true;
            scheduleLoadMessage = "No saved schedule found; starting fresh.";

            Debug.Log(
                $"No saved flight schedule found at: {ScheduleFilePath}"
            );

            return;
        }

        try
        {
            string json = File.ReadAllText(ScheduleFilePath);

            ScheduleSaveData saveData =
                JsonUtility.FromJson<ScheduleSaveData>(json);

            if (saveData == null)
                throw new InvalidDataException(
                    "The schedule file could not be parsed."
                );

            if (saveData.version != ScheduleFileVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported schedule version {saveData.version}."
                );
            }

            int restoredCount = 0;
            int skippedCount = 0;

            if (saveData.flights != null)
            {
                foreach (SavedFlightData saved in saveData.flights)
                {
                    if (saved == null ||
                        string.IsNullOrWhiteSpace(saved.flightNumber) ||
                        string.IsNullOrWhiteSpace(saved.gateStableId))
                    {
                        skippedCount++;
                        continue;
                    }

                    FlightDefinition flight =
                        FindFlightDefinition(saved.flightNumber);

                    if (flight == null)
                    {
                        Debug.LogWarning(
                            $"Skipping saved flight '{saved.flightNumber}': " +
                            "no matching flight definition exists."
                        );

                        skippedCount++;
                        continue;
                    }

                    if (!gateRegistry.TryGetGate(
                            saved.gateStableId,
                            out AircraftGate gate))
                    {
                        Debug.LogWarning(
                            $"Skipping {saved.flightNumber}: gate " +
                            $"'{saved.gateStableId}' was not found."
                        );

                        skippedCount++;
                        continue;
                    }

                    string reason = string.Empty;

                    if (saved.startMinute < 0 ||
                        saved.startMinute >= 1440 ||
                        saved.startMinute % 15 != 0 ||
                        IsFlightScheduled(flight) ||
                        !CanPlaceFlight(
                            flight,
                            gate,
                            saved.startMinute,
                            null,
                            out reason))
                    {
                        Debug.LogWarning(
                            $"Skipping saved flight {saved.flightNumber} " +
                            $"at {saved.startMinute}: schedule entry is " +
                            $"invalid or conflicts. {reason}"
                        );

                        skippedCount++;
                        continue;
                    }

                    scheduledFlights.Add(new ScheduledFlight
                    {
                        Definition = flight,
                        GateStableId = gate.StableId,
                        StartMinute = saved.startMinute
                    });

                    restoredCount++;
                }
            }

            scheduleLoaded = true;

            scheduleLoadMessage =
                $"Loaded {restoredCount} scheduled flight(s).";

            if (skippedCount > 0)
            {
                scheduleLoadMessage +=
                    $" Skipped {skippedCount} invalid or unavailable " +
                    "entry/entries.";
            }

            Debug.Log(
                $"Flight schedule loaded: {restoredCount} flight(s), " +
                $"{skippedCount} skipped. File: {ScheduleFilePath}"
            );
        }
        catch (Exception exception)
        {
            scheduleLoaded = false;
            scheduleLoadMessage =
                "Could not load the saved schedule. See the Console.";

            Debug.LogError(
                $"Could not load flight schedule from " +
                $"{ScheduleFilePath}. The existing file has not been " +
                $"overwritten.\n{exception}"
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

    private void SaveSchedule()
    {
        if (!scheduleLoaded)
        {
            Debug.LogWarning(
                "Flight schedule was not loaded successfully. " +
                "Not saving, to avoid overwriting the existing file."
            );

            return;
        }

        ScheduleSaveData saveData = new ScheduleSaveData
        {
            version = ScheduleFileVersion,
            flights = new List<SavedFlightData>()
        };

        foreach (ScheduledFlight scheduled in scheduledFlights)
        {
            saveData.flights.Add(new SavedFlightData
            {
                flightNumber = scheduled.Definition.FlightNumber,
                gateStableId = scheduled.GateStableId,
                startMinute = scheduled.StartMinute
            });
        }

        try
        {
            Directory.CreateDirectory(Application.persistentDataPath);

            string json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(ScheduleFilePath, json);

            Debug.Log(
                $"Flight schedule saved: {saveData.flights.Count} flight(s). " +
                $"File: {ScheduleFilePath}"
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Could not save flight schedule to {ScheduleFilePath}.\n" +
                exception
            );

            SetStatus(
                "Could not save the flight schedule. Check the Console.",
                false
            );
        }
    }

    // ------------------------------------------------------------
    // Scroll synchronization
    // ------------------------------------------------------------

    private void ConnectScrollbars()
    {
        timelineBodyScroll.horizontalScroller.valueChanged +=
            OnBodyHorizontalChanged;

        timelineHeaderScroll.horizontalScroller.valueChanged +=
            OnHeaderHorizontalChanged;

        timelineBodyScroll.verticalScroller.valueChanged +=
            OnBodyVerticalChanged;

        gateLabelScroll.verticalScroller.valueChanged +=
            OnGateVerticalChanged;
    }

    private void OnBodyHorizontalChanged(float value)
    {
        if (syncingScroll)
            return;

        syncingScroll = true;
        timelineHeaderScroll.horizontalScroller.value = value;
        syncingScroll = false;
    }

    private void OnHeaderHorizontalChanged(float value)
    {
        if (syncingScroll)
            return;

        syncingScroll = true;
        timelineBodyScroll.horizontalScroller.value = value;
        syncingScroll = false;
    }

    private void OnBodyVerticalChanged(float value)
    {
        if (syncingScroll)
            return;

        syncingScroll = true;
        gateLabelScroll.verticalScroller.value = value;
        syncingScroll = false;
    }

    private void OnGateVerticalChanged(float value)
    {
        if (syncingScroll)
            return;

        syncingScroll = true;
        timelineBodyScroll.verticalScroller.value = value;
        syncingScroll = false;
    }

    // ------------------------------------------------------------
    // Window lifecycle and game-time pause/restore
    // ------------------------------------------------------------

    private void OpenWindow()
    {
        if (windowOpen)
            return;

        windowOpen = true;

        if (gameTimeManager != null)
        {
            previousGameSpeed =
                gameTimeManager.CurrentSpeedMultiplier;

            gameTimeManager.Pause();
        }

        gateRegistry.RefreshGates();
        LoadScheduleIfNeeded();

        RebuildFlightStrips();
        RebuildGateRows();

        window.style.display = DisplayStyle.Flex;
        openButton.style.display = DisplayStyle.None;

        SetStatus(
            $"{scheduleLoadMessage} " +
            "Drag flights to schedule or return them to Available Flights.",
            scheduleLoaded
        );
    }

    private void CloseWindow()
    {
        if (!windowOpen)
            return;

        CancelDrag();

        SaveSchedule();

        windowOpen = false;
        window.style.display = DisplayStyle.None;
        openButton.style.display = DisplayStyle.Flex;

        if (gameTimeManager != null)
            gameTimeManager.SetSpeed(previousGameSpeed);
    }

    private void OnApplicationQuit()
    {
        if (scheduleLoaded)
            SaveSchedule();
    }

    private void OnDestroy()
    {
        CancelDrag();

        if (timelineBodyScroll != null)
        {
            timelineBodyScroll.horizontalScroller.valueChanged -=
                OnBodyHorizontalChanged;

            timelineBodyScroll.verticalScroller.valueChanged -=
                OnBodyVerticalChanged;
        }

        if (timelineHeaderScroll != null)
        {
            timelineHeaderScroll.horizontalScroller.valueChanged -=
                OnHeaderHorizontalChanged;
        }

        if (gateLabelScroll != null)
        {
            gateLabelScroll.verticalScroller.valueChanged -=
                OnGateVerticalChanged;
        }
    }
}