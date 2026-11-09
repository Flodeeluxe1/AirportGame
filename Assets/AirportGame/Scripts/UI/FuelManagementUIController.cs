using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Read-only overview of the airport's connected pipeline fuel reserves.
/// Attach this component to the same scene object as FlightManagementUIController
/// and assign the same UIDocument.
/// </summary>
public class FuelManagementUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private PipelineNetworkManager pipelineNetworkManager;

    [Header("Display")]
    [Min(0.1f)]
    [SerializeField] private float refreshInterval = 1f;

    private VisualElement root;
    private VisualElement window;
    private VisualElement networkRows;
    private Label totalFuelLabel;
    private Label totalCapacityLabel;
    private Label totalPercentageLabel;
    private Label networkCountLabel;
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

        if (pipelineNetworkManager == null)
            pipelineNetworkManager =
                FindFirstObjectByType<PipelineNetworkManager>();

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
        window.style.left = new Length(18, LengthUnit.Percent);
        window.style.right = new Length(18, LengthUnit.Percent);
        window.style.top = 65;
        window.style.bottom = 70;
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

        // Add directly to the UIDocument root, not inside another window.
        root.Add(window);

        CreateHeader();
        CreateSummary();
        CreateNetworkSection();
    }

    private void CreateHeader()
    {
        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.marginBottom = 12;

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

    private void CreateSummary()
    {
        VisualElement summary = new VisualElement();
        summary.style.flexDirection = FlexDirection.Row;
        summary.style.flexShrink = 0;
        summary.style.marginBottom = 14;

        totalFuelLabel = CreateSummaryCard(
            summary, "TOTAL FUEL STORED", "0 L");
        totalCapacityLabel = CreateSummaryCard(
            summary, "TOTAL CAPACITY", "0 L");
        totalPercentageLabel = CreateSummaryCard(
            summary, "OVERALL RESERVE", "0%");
        
        window.Add(summary);

        networkCountLabel = new Label("PIPELINE NETWORKS");
        networkCountLabel.style.fontSize = 14;
        networkCountLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        networkCountLabel.style.color =
            (Color)new Color32(215, 222, 231, 255);
        networkCountLabel.style.marginBottom = 6;
        window.Add(networkCountLabel);
    }

    private Label CreateSummaryCard(
        VisualElement parent,
        string heading,
        string initialValue)
    {
        VisualElement card = new VisualElement();
        card.style.flexGrow = 1;
        card.style.marginRight = 8;
        card.style.paddingLeft = 12;
        card.style.paddingRight = 12;
        card.style.paddingTop = 10;
        card.style.paddingBottom = 10;
        card.style.backgroundColor = PanelColor;
        card.style.borderTopLeftRadius = 6;
        card.style.borderTopRightRadius = 6;
        card.style.borderBottomLeftRadius = 6;
        card.style.borderBottomRightRadius = 6;

        Label headingLabel = new Label(heading);
        headingLabel.style.fontSize = 10;
        headingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        headingLabel.style.color = MutedTextColor;

        Label valueLabel = new Label(initialValue);
        valueLabel.style.fontSize = 22;
        valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        valueLabel.style.color = Color.white;
        valueLabel.style.marginTop = 4;

        card.Add(headingLabel);
        card.Add(valueLabel);
        parent.Add(card);

        return valueLabel;
    }

    private void CreateNetworkSection()
    {
        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.name = "fuel-management-network-scroll";
        scroll.style.flexGrow = 1;
        scroll.style.minHeight = 0;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

        networkRows = scroll.contentContainer;
        networkRows.style.flexDirection = FlexDirection.Column;

        window.Add(scroll);
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
        if (pipelineNetworkManager == null)
            pipelineNetworkManager =
                FindFirstObjectByType<PipelineNetworkManager>();

        if (pipelineNetworkManager == null)
        {
            totalFuelLabel.text = "Unavailable";
            totalCapacityLabel.text = "Unavailable";
            totalPercentageLabel.text = "—";
            networkCountLabel.text = "PIPELINE NETWORKS  |  Manager not found";
            networkRows.Clear();
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

        float totalPercentage = totalCapacity > 0f
            ? totalFuel / totalCapacity * 100f
            : 0f;

        totalFuelLabel.text = FormatLitres(totalFuel);
        totalCapacityLabel.text = FormatLitres(totalCapacity);
        totalPercentageLabel.text = $"{totalPercentage:0.#}%";
        networkCountLabel.text =
            $"PIPELINE NETWORKS  |  {networks.Count} connected network(s)";

        networkRows.Clear();

        if (networks.Count == 0)
        {
            Label emptyLabel = new Label(
                "No connected pipeline networks are currently available.");
            emptyLabel.style.color = MutedTextColor;
            emptyLabel.style.paddingTop = 12;
            networkRows.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < networks.Count; i++)
        {
            PipelineNetworkManager.PipelineNetwork network = networks[i];
            if (network == null)
                continue;

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
        row.style.paddingLeft = 12;
        row.style.paddingRight = 12;
        row.style.paddingTop = 10;
        row.style.paddingBottom = 10;
        row.style.marginBottom = 6;
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
        nameLabel.style.fontSize = 13;
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.color = Color.white;
        nameLabel.style.whiteSpace = WhiteSpace.Normal;

        Label percentageLabel = new Label($"{percentage * 100f:0.#}%");
        percentageLabel.style.fontSize = 13;
        percentageLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        percentageLabel.style.color = GetReserveColor(percentage);

        heading.Add(nameLabel);
        heading.Add(percentageLabel);
        row.Add(heading);

        Label amountLabel = new Label(
            $"{FormatLitres(network.CurrentFuelLitres)} stored  /  " +
            $"{FormatLitres(network.CapacityLitres)} capacity  •  " +
            $"{FormatLitres(network.RemainingCapacityLitres)} available");
        amountLabel.style.fontSize = 11;
        amountLabel.style.color = MutedTextColor;
        amountLabel.style.marginTop = 4;
        amountLabel.style.marginBottom = 7;
        row.Add(amountLabel);

        VisualElement barBackground = new VisualElement();
        barBackground.style.height = 10;
        barBackground.style.backgroundColor = BarBackgroundColor;
        barBackground.style.borderTopLeftRadius = 5;
        barBackground.style.borderTopRightRadius = 5;
        barBackground.style.borderBottomLeftRadius = 5;
        barBackground.style.borderBottomRightRadius = 5;

        VisualElement barFill = new VisualElement();
        barFill.style.height = 10;
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

    private string GetNetworkDescription(
        PipelineNetworkManager.PipelineNetwork network)
    {
        HashSet<string> names = new HashSet<string>();
        StringBuilder description = new StringBuilder();

        foreach (FuelConnector connector in network.Connectors)
        {
            if (connector == null || connector.BuildingRoot == null)
                continue;

            string buildingName = connector.BuildingRoot.name;
            if (string.IsNullOrWhiteSpace(buildingName) ||
                !names.Add(buildingName))
                continue;

            if (description.Length > 0)
                description.Append(", ");

            description.Append(buildingName);
        }

        return description.Length > 0
            ? description.ToString()
            : "No connected buildings";
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
