using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Read-only overview of pipeline reserves, incoming fuel deliveries,
/// and aircraft refuelling activity.
/// </summary>
public class FuelManagementUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private PipelineNetworkManager pipelineNetworkManager;
    [SerializeField] private FuelTruckSpawner fuelTruckSpawner;

    [Header("Display")]
    [Min(0.1f)]
    [SerializeField] private float refreshInterval = 1f;

    private VisualElement root;
    private VisualElement window;
    private VisualElement networkRows;
    private VisualElement tankerRows;
    private Label totalFuelLabel;
    private Label totalCapacityLabel;
    private Label totalPercentageLabel;
    private Label networkCountLabel;
    private Label activeTankerLabel;
    private Label incomingFuelLabel;
    private Label awaitingDeliveryLabel;
    private Label aircraftRequiringFuelLabel;
    private Label aircraftBeingRefuelledLabel;
    private Label waitingForTruckLabel;
    private Button openButton;
    private bool windowOpen;
    private float nextRefreshTime;

    private static readonly Color WindowColor =
        new Color32(27, 32, 39, 250);
    private static readonly Color PanelColor =
        new Color32(39, 47, 57, 255);
    private static readonly Color MutedTextColor =
        new Color32(174, 186, 199, 255);
    private static readonly Color BarBackgroundColor =
        new Color32(62, 71, 82, 255);

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
            uiDocument = FindFirstObjectByType<UIDocument>();

        ResolveDataSources();

        if (uiDocument == null)
        {
            Debug.LogError(
                "FuelManagementUIController: UIDocument is missing. " +
                "Assign the same UIDocument used by Flight Management.",
                this);
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
            CloseWindow();
            return;
        }

        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime =
            Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);

        RefreshWindow();
    }

    private void OnDestroy()
    {
        if (root == null)
            return;

        if (openButton != null)
            root.Remove(openButton);

        if (window != null)
            root.Remove(window);
    }

    private void ResolveDataSources()
    {
        if (pipelineNetworkManager == null)
            pipelineNetworkManager =
                FindFirstObjectByType<PipelineNetworkManager>();

        if (fuelTruckSpawner == null)
            fuelTruckSpawner =
                FindFirstObjectByType<FuelTruckSpawner>();
    }

    private void CreateOpenButton()
    {
        openButton = new Button(OpenWindow)
        {
            text = "Fuel Management"
        };

        openButton.name = "fuel-management-open-button";
        openButton.style.position = Position.Absolute;

        // Flight Management is at right = 16 and Flightstatus at right = 194.
        // Place Fuel Management immediately to the left of Flightstatus.
        openButton.style.right = 372;
        openButton.style.bottom = 14;
        openButton.style.height = 42;
        openButton.style.minWidth = 170;
        openButton.style.paddingLeft = 14;
        openButton.style.paddingRight = 14;
        openButton.style.backgroundColor =
            (Color)new Color32(43, 112, 78, 255);
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
        window.name = "fuel-management-window";

        window.style.position = Position.Absolute;
        window.style.left = new Length(14, LengthUnit.Percent);
        window.style.right = new Length(14, LengthUnit.Percent);
        window.style.top = 48;
        window.style.bottom = 68;
        window.style.flexDirection = FlexDirection.Column;
        window.style.paddingLeft = 14;
        window.style.paddingRight = 14;
        window.style.paddingTop = 12;
        window.style.paddingBottom = 12;
        window.style.backgroundColor = WindowColor;
        window.style.borderTopLeftRadius = 8;
        window.style.borderTopRightRadius = 8;
        window.style.borderBottomLeftRadius = 8;
        window.style.borderBottomRightRadius = 8;

        root.Add(window);

        CreateHeader();
        CreateNetworkSummary();
        CreateDeliverySection();
        CreateAircraftSection();
        CreateNetworkSection();
    }

    private void CreateHeader()
    {
        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.marginBottom = 10;

        Label title = new Label("FUEL MANAGEMENT");
        title.style.fontSize = 21;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;

        Button closeButton = new Button(CloseWindow)
        {
            text = "Close  X"
        };
        closeButton.style.minWidth = 80;

        header.Add(title);
        header.Add(closeButton);
        window.Add(header);
    }

    private void CreateNetworkSummary()
    {
        VisualElement summary = CreateCardRow();

        totalFuelLabel = CreateSummaryCard(
            summary, "TOTAL FUEL STORED", "0 L");
        totalCapacityLabel = CreateSummaryCard(
            summary, "TOTAL CAPACITY", "0 L");
        totalPercentageLabel = CreateSummaryCard(
            summary, "OVERALL RESERVE", "0%");

        window.Add(summary);
    }

    private void CreateDeliverySection()
    {
        AddSectionTitle("FUEL DELIVERIES");

        VisualElement summary = CreateCardRow();
        activeTankerLabel = CreateSummaryCard(
            summary, "ACTIVE TANKERS", "0");
        incomingFuelLabel = CreateSummaryCard(
            summary, "FUEL CURRENTLY INCOMING", "0 L");
        awaitingDeliveryLabel = CreateSummaryCard(
            summary, "NETWORKS AWAITING DELIVERY", "0");
        window.Add(summary);

        tankerRows = new VisualElement();
        tankerRows.style.flexDirection = FlexDirection.Column;
        tankerRows.style.marginTop = 5;
        window.Add(tankerRows);
    }

    private void CreateAircraftSection()
    {
        AddSectionTitle("AIRCRAFT REFUELLING");

        VisualElement summary = CreateCardRow();
        aircraftRequiringFuelLabel = CreateSummaryCard(
            summary, "AIRCRAFT REQUIRING FUEL", "0");
        aircraftBeingRefuelledLabel = CreateSummaryCard(
            summary, "AIRCRAFT BEING REFUELLED", "0");
        waitingForTruckLabel = CreateSummaryCard(
            summary, "WAITING FOR FUEL TRUCK", "0");
        window.Add(summary);
    }

    private void CreateNetworkSection()
    {
        AddSectionTitle("PIPELINE NETWORKS");

        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.name = "fuel-management-network-scroll";
        scroll.style.flexGrow = 1;
        scroll.style.minHeight = 0;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

        networkRows = scroll.contentContainer;
        networkRows.style.flexDirection = FlexDirection.Column;

        window.Add(scroll);
    }

    private VisualElement CreateCardRow()
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexShrink = 0;
        row.style.marginBottom = 6;
        return row;
    }

    private void AddSectionTitle(string title)
    {
        Label label = new Label(title);
        label.style.fontSize = 13;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = (Color)new Color32(215, 222, 231, 255);
        label.style.marginTop = 7;
        label.style.marginBottom = 5;
        window.Add(label);
    }

    private Label CreateSummaryCard(
        VisualElement parent,
        string heading,
        string initialValue)
    {
        VisualElement card = new VisualElement();
        card.style.flexGrow = 1;
        card.style.minWidth = 0;
        card.style.marginRight = 7;
        card.style.paddingLeft = 10;
        card.style.paddingRight = 10;
        card.style.paddingTop = 7;
        card.style.paddingBottom = 7;
        card.style.backgroundColor = PanelColor;
        card.style.borderTopLeftRadius = 6;
        card.style.borderTopRightRadius = 6;
        card.style.borderBottomLeftRadius = 6;
        card.style.borderBottomRightRadius = 6;

        Label headingLabel = new Label(heading);
        headingLabel.style.fontSize = 9;
        headingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        headingLabel.style.color = MutedTextColor;
        headingLabel.style.whiteSpace = WhiteSpace.Normal;

        Label valueLabel = new Label(initialValue);
        valueLabel.style.fontSize = 19;
        valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        valueLabel.style.color = Color.white;
        valueLabel.style.marginTop = 3;

        card.Add(headingLabel);
        card.Add(valueLabel);
        parent.Add(card);

        return valueLabel;
    }

    private void OpenWindow()
    {
        windowOpen = true;
        window.style.display = DisplayStyle.Flex;
        nextRefreshTime = 0f;
        RefreshWindow();
    }

    private void CloseWindow()
    {
        windowOpen = false;
        window.style.display = DisplayStyle.None;
    }

    private void RefreshWindow()
    {
        ResolveDataSources();
        RefreshNetworkSummary();
        RefreshDeliverySummary();
        RefreshAircraftSummary();
        RefreshNetworkRows();
    }

    private void RefreshNetworkSummary()
    {
        if (pipelineNetworkManager == null)
        {
            totalFuelLabel.text = "Unavailable";
            totalCapacityLabel.text = "Unavailable";
            totalPercentageLabel.text = "—";
            return;
        }

        IReadOnlyList<PipelineNetworkManager.PipelineNetwork> networks =
            pipelineNetworkManager.Networks;

        float totalFuel = 0f;
        float totalCapacity = 0f;

        foreach (PipelineNetworkManager.PipelineNetwork network in networks)
        {
            if (network == null)
                continue;

            totalFuel += network.CurrentFuelLitres;
            totalCapacity += network.CapacityLitres;
        }

        float percentage = totalCapacity > 0f
            ? totalFuel / totalCapacity * 100f
            : 0f;

        totalFuelLabel.text = FormatLitres(totalFuel);
        totalCapacityLabel.text = FormatLitres(totalCapacity);
        totalPercentageLabel.text = $"{percentage:0.#}%";
    }

    private void RefreshDeliverySummary()
    {
        int activeTankers = 0;
        float incomingFuel = 0f;

        if (fuelTruckSpawner != null)
        {
            activeTankers = fuelTruckSpawner.ActiveTankerCount;

            foreach (FuelTruckController truck in fuelTruckSpawner.ActiveTankers)
            {
                if (truck != null)
                    incomingFuel += truck.IncomingFuelLitres;
            }
        }

        int networksAwaitingDelivery = 0;

        if (pipelineNetworkManager != null)
        {
            foreach (PipelineNetworkManager.PipelineNetwork network
                     in pipelineNetworkManager.Networks)
            {
                if (network == null || network.CapacityLitres <= 0f)
                    continue;

                float percentage =
                    network.CurrentFuelLitres / network.CapacityLitres * 100f;

                bool deliveryActive =
                    fuelTruckSpawner != null &&
                    fuelTruckSpawner.HasActiveDelivery(network);

                if (percentage <=
                        pipelineNetworkManager.AutomaticRefillThresholdPercent &&
                    !deliveryActive)
                    networksAwaitingDelivery++;
            }
        }

        activeTankerLabel.text = activeTankers.ToString();
        incomingFuelLabel.text = FormatLitres(incomingFuel);
        awaitingDeliveryLabel.text = networksAwaitingDelivery.ToString();

        tankerRows.Clear();

        if (fuelTruckSpawner == null ||
            fuelTruckSpawner.ActiveTankers.Count == 0)
        {
            AddEmptyMessage(tankerRows, "No active fuel deliveries.");
            return;
        }

        foreach (FuelTruckController truck in fuelTruckSpawner.ActiveTankers)
        {
            if (truck == null)
                continue;

            VisualElement row = CreateDetailRow(
                truck.name,
                $"{truck.CurrentDeliveryStatus}  |  " +
                $"Station: {truck.DeliveryStationName}  |  " +
                $"Incoming: {FormatLitres(truck.IncomingFuelLitres)}");
            tankerRows.Add(row);
        }
    }

    private void RefreshAircraftSummary()
    {
        AircraftTurnaroundController[] aircraft =
            FindObjectsByType<AircraftTurnaroundController>(
                FindObjectsSortMode.None);

        int requiringFuel = 0;

        foreach (AircraftTurnaroundController turnaround in aircraft)
        {
            if (turnaround != null &&
                turnaround.FuelingStatus ==
                    AircraftTurnaroundTaskStatus.InProgress)
                requiringFuel++;
        }

        int beingRefuelled =
            AirportFuelTruckController.CurrentlyRefuellingAircraftCount;

        int waitingForTruck = Mathf.Max(0, requiringFuel - beingRefuelled);

        aircraftRequiringFuelLabel.text = requiringFuel.ToString();
        aircraftBeingRefuelledLabel.text = beingRefuelled.ToString();
        waitingForTruckLabel.text = waitingForTruck.ToString();
    }

    private void RefreshNetworkRows()
    {
        networkRows.Clear();

        if (pipelineNetworkManager == null)
        {
            AddEmptyMessage(networkRows, "Pipeline network manager not found.");
            return;
        }

        IReadOnlyList<PipelineNetworkManager.PipelineNetwork> networks =
            pipelineNetworkManager.Networks;

        if (networks.Count == 0)
        {
            AddEmptyMessage(
                networkRows,
                "No connected pipeline networks are currently available.");
            return;
        }

        for (int i = 0; i < networks.Count; i++)
        {
            PipelineNetworkManager.PipelineNetwork network = networks[i];
            if (network != null)
                networkRows.Add(CreateNetworkRow(network, i));
        }
    }

    private VisualElement CreateNetworkRow(
        PipelineNetworkManager.PipelineNetwork network,
        int index)
    {
        float percentage = network.CapacityLitres > 0f
            ? Mathf.Clamp01(
                network.CurrentFuelLitres / network.CapacityLitres)
            : 0f;

        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Column;
        row.style.paddingLeft = 11;
        row.style.paddingRight = 11;
        row.style.paddingTop = 8;
        row.style.paddingBottom = 8;
        row.style.marginBottom = 5;
        row.style.backgroundColor = PanelColor;
        row.style.borderTopLeftRadius = 6;
        row.style.borderTopRightRadius = 6;
        row.style.borderBottomLeftRadius = 6;
        row.style.borderBottomRightRadius = 6;

        VisualElement heading = new VisualElement();
        heading.style.flexDirection = FlexDirection.Row;
        heading.style.justifyContent = Justify.SpaceBetween;
        heading.style.alignItems = Align.Center;

        Label nameLabel = new Label(
            $"NETWORK {index + 1}  •  {GetNetworkDescription(network)}");
        nameLabel.style.fontSize = 12;
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.color = Color.white;
        nameLabel.style.whiteSpace = WhiteSpace.Normal;

        Label percentageLabel = new Label($"{percentage * 100f:0.#}%");
        percentageLabel.style.fontSize = 12;
        percentageLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        percentageLabel.style.color = GetReserveColor(percentage);

        heading.Add(nameLabel);
        heading.Add(percentageLabel);
        row.Add(heading);

        Label amountLabel = new Label(
            $"{FormatLitres(network.CurrentFuelLitres)} stored  /  " +
            $"{FormatLitres(network.CapacityLitres)} capacity  •  " +
            $"{FormatLitres(network.RemainingCapacityLitres)} available");
        amountLabel.style.fontSize = 10;
        amountLabel.style.color = MutedTextColor;
        amountLabel.style.marginTop = 3;
        amountLabel.style.marginBottom = 6;
        row.Add(amountLabel);

        VisualElement barBackground = new VisualElement();
        barBackground.style.height = 9;
        barBackground.style.backgroundColor = BarBackgroundColor;
        barBackground.style.borderTopLeftRadius = 5;
        barBackground.style.borderTopRightRadius = 5;
        barBackground.style.borderBottomLeftRadius = 5;
        barBackground.style.borderBottomRightRadius = 5;

        VisualElement barFill = new VisualElement();
        barFill.style.height = 9;
        barFill.style.width = new Length(percentage * 100f, LengthUnit.Percent);
        barFill.style.backgroundColor = GetReserveColor(percentage);
        barFill.style.borderTopLeftRadius = 5;
        barFill.style.borderTopRightRadius = 5;
        barFill.style.borderBottomLeftRadius = 5;
        barFill.style.borderBottomRightRadius = 5;

        barBackground.Add(barFill);
        row.Add(barBackground);

        return row;
    }

    private VisualElement CreateDetailRow(string title, string detail)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Column;
        row.style.paddingLeft = 10;
        row.style.paddingRight = 10;
        row.style.paddingTop = 5;
        row.style.paddingBottom = 5;
        row.style.marginBottom = 3;
        row.style.backgroundColor = PanelColor;

        Label titleLabel = new Label(title);
        titleLabel.style.fontSize = 11;
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.style.color = Color.white;

        Label detailLabel = new Label(detail);
        detailLabel.style.fontSize = 10;
        detailLabel.style.color = MutedTextColor;
        detailLabel.style.whiteSpace = WhiteSpace.Normal;

        row.Add(titleLabel);
        row.Add(detailLabel);
        return row;
    }

    private void AddEmptyMessage(VisualElement parent, string message)
    {
        Label label = new Label(message);
        label.style.fontSize = 10;
        label.style.color = MutedTextColor;
        label.style.paddingTop = 5;
        label.style.paddingBottom = 5;
        parent.Add(label);
    }

    private string GetNetworkDescription(
        PipelineNetworkManager.PipelineNetwork network)
    {
        HashSet<string> names = new HashSet<string>();

        foreach (FuelConnector connector in network.Connectors)
        {
            if (connector == null || connector.BuildingRoot == null)
                continue;

            string buildingName = connector.BuildingRoot.name;
            if (!string.IsNullOrWhiteSpace(buildingName))
                names.Add(buildingName);
        }

        if (names.Count == 0)
            return "No connected buildings";

        return string.Join(", ", names);
    }

    private string FormatLitres(float litres)
    {
        return $"{litres:N0} L";
    }

    private Color GetReserveColor(float normalizedReserve)
    {
        if (normalizedReserve <= 0.25f)
            return new Color32(205, 75, 68, 255);

        if (normalizedReserve <= 0.5f)
            return new Color32(218, 160, 65, 255);

        return new Color32(76, 170, 115, 255);
    }
}
