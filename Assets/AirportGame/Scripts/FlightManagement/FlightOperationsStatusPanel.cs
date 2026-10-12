
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class FlightOperationsStatusPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private AircraftGateRegistry gateRegistry;

    [Header("Layout")]
    [Min(0.1f)]
    [SerializeField] private float refreshInterval = 0.5f;

    [Min(30f)]
    [SerializeField] private float statusListHeight = 54f;

    private VisualElement flightManagementWindow;
    private VisualElement panel;
    private ScrollView statusList;

    private float nextRefreshTime;

    private readonly List<AircraftFlightIdentity> activeAircraft =
        new List<AircraftFlightIdentity>();

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (gateRegistry == null)
            gateRegistry = FindFirstObjectByType<AircraftGateRegistry>();

        if (uiDocument == null)
        {
            Debug.LogError(
                "FlightOperationsStatusPanel: UIDocument is missing.",
                this
            );

            enabled = false;
            return;
        }
    }

    private void Update()
    {
        if (uiDocument == null)
            return;

        if (flightManagementWindow == null)
        {
            flightManagementWindow =
                uiDocument.rootVisualElement.Q<VisualElement>(
                    "flight-management-window"
                );

            if (flightManagementWindow == null)
                return;
        }

        if (panel == null || panel.parent == null)
        {
            CreatePanel();

            if (panel == null)
                return;
        }

        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime =
            Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);

        RefreshStatusList();
    }

    private void CreatePanel()
    {
        panel = new VisualElement();
        panel.name = "active-flight-operations-panel";

        panel.style.flexDirection = FlexDirection.Column;
        panel.style.flexShrink = 0;
        panel.style.marginBottom = 5;
        panel.style.paddingLeft = 8;
        panel.style.paddingRight = 8;
        panel.style.paddingTop = 5;
        panel.style.paddingBottom = 5;

        panel.style.backgroundColor =
            (Color)new Color32(33, 39, 47, 255);

        panel.style.borderTopLeftRadius = 4;
        panel.style.borderTopRightRadius = 4;
        panel.style.borderBottomLeftRadius = 4;
        panel.style.borderBottomRightRadius = 4;

        Label heading = new Label("ACTIVE OPERATIONS");
        heading.style.fontSize = 11;
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.color =
            (Color)new Color32(215, 222, 231, 255);
        heading.style.marginBottom = 3;

        panel.Add(heading);

        statusList = new ScrollView(ScrollViewMode.Vertical);
        statusList.name = "active-flight-status-list";
        statusList.style.height = statusListHeight;
        statusList.style.minHeight = statusListHeight;
        statusList.style.flexShrink = 0;
        statusList.verticalScrollerVisibility =
            ScrollerVisibility.Auto;
        statusList.horizontalScrollerVisibility =
            ScrollerVisibility.Hidden;

        statusList.contentContainer.style.flexDirection =
            FlexDirection.Column;

        panel.Add(statusList);

        // Insert the panel immediately before the existing gate timetable,
        // leaving the working FlightManagementUIController unchanged.
        int insertIndex = flightManagementWindow.childCount;

        for (int i = 0; i < flightManagementWindow.childCount; i++)
        {
            VisualElement child =
                flightManagementWindow.hierarchy.ElementAt(i);

            if (child is Label label &&
                label.text.StartsWith("GATE SCHEDULE"))
            {
                insertIndex = i;
                break;
            }
        }

        flightManagementWindow.hierarchy.Insert(insertIndex, panel);
    }

    private void RefreshStatusList()
    {
        statusList.Clear();
        activeAircraft.Clear();

        AircraftFlightIdentity[] identities =
            FindObjectsByType<AircraftFlightIdentity>(
                FindObjectsSortMode.None
            );

        foreach (AircraftFlightIdentity identity in identities)
        {
            if (identity == null ||
                !identity.IsInitialized ||
                string.IsNullOrEmpty(identity.FlightNumber))
            {
                continue;
            }

            activeAircraft.Add(identity);
        }

        activeAircraft.Sort(
            (a, b) =>
                string.CompareOrdinal(a.FlightNumber, b.FlightNumber)
        );

        if (activeAircraft.Count == 0)
        {
            Label emptyLabel =
                new Label("No active scheduled aircraft.");

            emptyLabel.style.fontSize = 10;
            emptyLabel.style.color =
                (Color)new Color32(173, 184, 196, 255);

            statusList.Add(emptyLabel);
            return;
        }

        foreach (AircraftFlightIdentity identity in activeAircraft)
        {
            statusList.Add(CreateAircraftStatusRow(identity));
        }
    }

    private VisualElement CreateAircraftStatusRow(
        AircraftFlightIdentity identity)
    {
        AircraftLandingController landing =
            identity.GetComponent<AircraftLandingController>();

        AircraftTaxiController taxi =
            identity.GetComponent<AircraftTaxiController>();

        string status = GetOperationalStatus(landing, taxi);

        string plannedGate =
            GetGateDisplayName(identity.PlannedGateStableId);

        string actualGate = identity.HasActualGate
            ? GetGateDisplayName(identity.ActualGateStableId)
            : "Not assigned";

        bool gateChanged =
            identity.HasActualGate &&
            identity.ActualGateStableId != identity.PlannedGateStableId;

        Label row = new Label(
            $"{identity.FlightNumber}  |  {status}\n" +
            $"Planned: {plannedGate}  ·  Actual: {actualGate}"
        );

        row.style.fontSize = 10;
        row.style.whiteSpace = WhiteSpace.Normal;
        row.style.paddingTop = 2;
        row.style.paddingBottom = 3;
        row.style.paddingLeft = 2;
        row.style.paddingRight = 2;

        row.style.color = gateChanged
            ? (Color)new Color32(245, 190, 115, 255)
            : (Color)new Color32(205, 215, 225, 255);

        row.tooltip =
            $"Operation: {identity.OperationId}\n" +
            $"Status: {status}\n" +
            $"Planned gate ID: {identity.PlannedGateStableId}\n" +
            $"Actual gate ID: {identity.ActualGateStableId}";

        return row;
    }

    private string GetOperationalStatus(
        AircraftLandingController landing,
        AircraftTaxiController taxi)
    {
        if (landing != null && landing.IsGoingAround)
            return "Go-around";

        if (taxi != null && taxi.HasArrivedAtGate)
            return "At Gate";

        if (taxi != null && taxi.IsTaxiing)
            return "Taxiing";

        if (landing != null && landing.IsLandingComplete)
        {
            if (taxi == null)
                return "Awaiting Taxi Controller";

            if (!taxi.HasRoute)
                return "Taxi Route Unavailable";

            return "Taxi Route Ready";
        }

        return "Approach / Landing";
    }

    private string GetGateDisplayName(string gateStableId)
    {
        if (string.IsNullOrEmpty(gateStableId))
            return "Not assigned";

        if (gateRegistry != null &&
            gateRegistry.TryGetGate(
                gateStableId,
                out AircraftGate gate))
        {
            return gate.DisplayName;
        }

        // Retain a useful identifier if a gate was removed from the scene.
        return gateStableId;
    }
}