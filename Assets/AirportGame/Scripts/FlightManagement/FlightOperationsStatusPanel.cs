
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

        // Insert before the existing gate timetable.
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
            statusList.Add(CreateAircraftStatusRow(identity));
    }

    private VisualElement CreateAircraftStatusRow(
        AircraftFlightIdentity identity)
    {
        string status = GetOperationalStatus(identity);

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
        AircraftFlightIdentity identity)
    {
        // Training aircraft
        if (identity.IsTrainingFlight)
            return GetTrainingStatus(identity);

        // Hobby aircraft
        if (identity.IsHobbyFlight)
            return GetHobbyStatus(identity);

        // Scheduled CargoPax aircraft
        return GetCargoPaxStatus(identity);
    }

    private string GetCargoPaxStatus(
        AircraftFlightIdentity identity)
    {
        AircraftCargoPaxLandingController landing =
            identity.GetComponent<AircraftCargoPaxLandingController>();

        AircraftCargoPaxLandingTaxiController arrivalTaxi =
            identity.GetComponent<AircraftCargoPaxLandingTaxiController>();

        AircraftCargoPaxDepartureTaxiController departureTaxi =
            identity.GetComponent<AircraftCargoPaxDepartureTaxiController>();

        if (landing != null && landing.IsGoingAround)
            return "Go-around";

        if (arrivalTaxi != null && arrivalTaxi.HasArrivedAtGate)
            return "At Gate";

        if (arrivalTaxi != null && arrivalTaxi.IsTaxiing)
            return "Taxiing to Gate";

        if (landing != null && landing.IsLandingComplete)
        {
            if (arrivalTaxi == null)
                return "Awaiting Arrival Taxi";

            if (!arrivalTaxi.HasRoute)
                return "Taxi Route Unavailable";

            return "Taxi Route Ready";
        }

        if (departureTaxi != null)
            return GetCargoPaxDepartureStatus(departureTaxi);

        if (landing != null)
            return "Approach / Landing";

        return "Scheduled Operation";
    }

    private string GetCargoPaxDepartureStatus(
        AircraftCargoPaxDepartureTaxiController taxi)
    {
        switch (taxi.CurrentState)
        {
            case AircraftCargoPaxDepartureTaxiController.DepartureState.WaitingForGateExit:
                return "Waiting for Gate Exit";

            case AircraftCargoPaxDepartureTaxiController.DepartureState.WaitingForRunway:
                return "Waiting for Runway";

            case AircraftCargoPaxDepartureTaxiController.DepartureState.TaxiingToHoldingPoint:
                return "Taxiing to Holding Point";

            case AircraftCargoPaxDepartureTaxiController.DepartureState.AtHoldingPoint:
                return "Holding for Runway";

            case AircraftCargoPaxDepartureTaxiController.DepartureState.TaxiingToRunwayEntry:
                return "Taxiing to Runway";

            case AircraftCargoPaxDepartureTaxiController.DepartureState.MovingOntoRunway:
                return "Entering Runway";

            case AircraftCargoPaxDepartureTaxiController.DepartureState.AligningOnRunway:
                return "Aligning on Runway";

            case AircraftCargoPaxDepartureTaxiController.DepartureState.LinedUpReadyForTakeoff:
                return "Ready for Takeoff";

            default:
                return "Departure Operation";
        }
    }

    private string GetTrainingStatus(
        AircraftFlightIdentity identity)
    {
        AircraftTrainingLandingController landing =
            identity.GetComponent<AircraftTrainingLandingController>();

        AircraftTrainingLandingTaxiController arrivalTaxi =
            identity.GetComponent<AircraftTrainingLandingTaxiController>();

        AircraftTrainingDepartureTaxiController departureTaxi =
            identity.GetComponent<AircraftTrainingDepartureTaxiController>();

        if (landing != null && landing.IsGoingAround)
            return "Training: Go-around";

        if (arrivalTaxi != null && arrivalTaxi.HasArrivedAtParking)
            return "Training: Parked";

        if (arrivalTaxi != null && arrivalTaxi.IsTaxiing)
            return "Training: Taxiing to Parking";

        if (landing != null && landing.IsLandingComplete)
        {
            if (arrivalTaxi == null)
                return "Training: Awaiting Taxi";

            return "Training: Taxiing to Parking";
        }

        if (departureTaxi != null)
            return GetTrainingDepartureStatus(departureTaxi);

        if (landing != null)
            return "Training: Approach / Landing";

        return "Training: Circuit";
    }

    private string GetTrainingDepartureStatus(
        AircraftTrainingDepartureTaxiController taxi)
    {
        switch (taxi.CurrentState)
        {
            case AircraftTrainingDepartureTaxiController.DepartureState.WaitingForDepartureRequest:
                return "Training: Waiting to Depart";

            case AircraftTrainingDepartureTaxiController.DepartureState.WaitingForRunway:
                return "Training: Waiting for Runway";

            case AircraftTrainingDepartureTaxiController.DepartureState.TaxiingToHoldingPoint:
                return "Training: Taxiing to Hold";

            case AircraftTrainingDepartureTaxiController.DepartureState.AtHoldingPoint:
                return "Training: Holding for Runway";

            case AircraftTrainingDepartureTaxiController.DepartureState.TaxiingToRunwayEntry:
                return "Training: Taxiing to Runway";

            case AircraftTrainingDepartureTaxiController.DepartureState.MovingOntoRunway:
                return "Training: Entering Runway";

            case AircraftTrainingDepartureTaxiController.DepartureState.AligningOnRunway:
                return "Training: Aligning on Runway";

            case AircraftTrainingDepartureTaxiController.DepartureState.LinedUpReadyForTakeoff:
                return "Training: Ready for Takeoff";

            default:
                return "Training: Departure";
        }
    }

    private string GetHobbyStatus(
        AircraftFlightIdentity identity)
    {
        AircraftHobbyLandingController landing =
            identity.GetComponent<AircraftHobbyLandingController>();

        AircraftHobbyLandingTaxiController arrivalTaxi =
            identity.GetComponent<AircraftHobbyLandingTaxiController>();

        AircraftHobbyDepartureTaxiController departureTaxi =
            identity.GetComponent<AircraftHobbyDepartureTaxiController>();

        if (landing != null && landing.IsGoingAround)
            return "Hobby: Go-around";

        if (arrivalTaxi != null && arrivalTaxi.HasArrivedAtParking)
            return "Hobby: Parked";

        if (arrivalTaxi != null && arrivalTaxi.IsTaxiing)
            return "Hobby: Taxiing to Parking";

        if (landing != null && landing.IsLandingComplete)
        {
            if (arrivalTaxi == null)
                return "Hobby: Awaiting Taxi";

            return "Hobby: Taxiing to Parking";
        }

        if (departureTaxi != null)
            return GetHobbyDepartureStatus(departureTaxi);

        if (landing != null)
            return "Hobby: Approach / Landing";

        return "Hobby: Visiting";
    }

    private string GetHobbyDepartureStatus(
        AircraftHobbyDepartureTaxiController taxi)
    {
        switch (taxi.CurrentState)
        {
            case AircraftHobbyDepartureTaxiController.DepartureState.WaitingForDepartureRequest:
                return "Hobby: Waiting to Depart";

            case AircraftHobbyDepartureTaxiController.DepartureState.WaitingForRunway:
                return "Hobby: Waiting for Runway";

            case AircraftHobbyDepartureTaxiController.DepartureState.TaxiingToHoldingPoint:
                return "Hobby: Taxiing to Hold";

            case AircraftHobbyDepartureTaxiController.DepartureState.AtHoldingPoint:
                return "Hobby: Holding for Runway";

            case AircraftHobbyDepartureTaxiController.DepartureState.TaxiingToRunwayEntry:
                return "Hobby: Taxiing to Runway";

            case AircraftHobbyDepartureTaxiController.DepartureState.MovingOntoRunway:
                return "Hobby: Entering Runway";

            case AircraftHobbyDepartureTaxiController.DepartureState.AligningOnRunway:
                return "Hobby: Aligning on Runway";

            case AircraftHobbyDepartureTaxiController.DepartureState.LinedUpReadyForTakeoff:
                return "Hobby: Ready for Takeoff";

            default:
                return "Hobby: Departure";
        }
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

        // Keep the identifier visible if the gate is no longer in the scene.
        return gateStableId;
    }
}
