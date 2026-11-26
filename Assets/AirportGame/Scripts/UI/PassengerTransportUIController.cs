using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Displays live passenger transport demand, completed journeys, waiting
/// passengers, parking utilisation and public-transport service capacity.
/// </summary>
[DisallowMultipleComponent]
public class PassengerTransportUIController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;

    private VisualElement root;
    private VisualElement window;
    private Button openButton;
    private Label statusLabel;
    private Label busPassengersLabel;
    private Label carPassengersLabel;
    private Label waitingPassengersLabel;
    private Label averageWaitLabel;
    private Label parkingLabel;
    private Label parkingFeeLabel;
    private Label busServiceLabel;
    private Label busCapacityLabel;
    private Label trainCapacityLabel;
    private Label undergroundCapacityLabel;

    private PassengerCoordinator passengerCoordinator;
    private PublicTransportManager publicTransportManager;
    private bool windowOpen;
    private bool refreshPending;

    private static readonly Color WindowColor =
        new Color32(27, 32, 39, 250);
    private static readonly Color CardColor =
        new Color32(39, 47, 57, 255);
    private static readonly Color MutedTextColor =
        new Color32(174, 186, 199, 255);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureControllerExists()
    {
        if (FindAnyObjectByType<PassengerTransportUIController>() != null)
            return;

        UIDocument document = FindAnyObjectByType<UIDocument>();
        if (document != null)
            document.gameObject.AddComponent<PassengerTransportUIController>();
    }

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogWarning(
                "PassengerTransportUIController: No UIDocument was found. Passenger transport statistics UI is disabled.",
                this);
            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;
        CreateOpenButton();
        CreateWindow();
        window.style.display = DisplayStyle.None;
    }

    private void OnEnable()
    {
        ResolveReferences();
    }

    private void Start()
    {
        ResolveReferences();
        RefreshWindow();
    }

    private void OnDisable()
    {
        UnsubscribeFromReferences();
        passengerCoordinator = null;
        publicTransportManager = null;
    }

    private void OnDestroy()
    {
        UnsubscribeFromReferences();

        if (root == null)
            return;

        if (openButton != null)
            root.Remove(openButton);
        if (window != null)
            root.Remove(window);
    }

    private void ResolveReferences()
    {
        PassengerCoordinator coordinator =
            FindAnyObjectByType<PassengerCoordinator>();
        if (coordinator != passengerCoordinator)
        {
            if (passengerCoordinator != null)
                passengerCoordinator.TransportStatisticsChanged -= HandleStatisticsChanged;

            passengerCoordinator = coordinator;
            if (isActiveAndEnabled && passengerCoordinator != null)
                passengerCoordinator.TransportStatisticsChanged += HandleStatisticsChanged;
        }

        PublicTransportManager manager =
            passengerCoordinator != null
                ? passengerCoordinator.TransportManager
                : null;
        if (manager == null)
            manager = FindAnyObjectByType<PublicTransportManager>();

        if (manager == publicTransportManager)
            return;

        UnsubscribeFromTransportManager();
        publicTransportManager = manager;
        SubscribeToTransportManager();
    }

    private void UnsubscribeFromReferences()
    {
        if (passengerCoordinator != null)
            passengerCoordinator.TransportStatisticsChanged -= HandleStatisticsChanged;

        UnsubscribeFromTransportManager();
    }

    private void SubscribeToTransportManager()
    {
        if (!isActiveAndEnabled || publicTransportManager == null)
            return;

        publicTransportManager.BusFrequencyChanged += HandleStatisticsChanged;
        publicTransportManager.TrainServiceChanged += HandleStatisticsChanged;
        publicTransportManager.UndergroundServiceChanged += HandleStatisticsChanged;
        publicTransportManager.FleetChanged += HandleStatisticsChanged;
        publicTransportManager.StopsChanged += HandleStatisticsChanged;
    }

    private void UnsubscribeFromTransportManager()
    {
        if (publicTransportManager == null)
            return;

        publicTransportManager.BusFrequencyChanged -= HandleStatisticsChanged;
        publicTransportManager.TrainServiceChanged -= HandleStatisticsChanged;
        publicTransportManager.UndergroundServiceChanged -= HandleStatisticsChanged;
        publicTransportManager.FleetChanged -= HandleStatisticsChanged;
        publicTransportManager.StopsChanged -= HandleStatisticsChanged;
    }

    private void HandleStatisticsChanged()
    {
        if (windowOpen)
            refreshPending = true;
    }

    private void Update()
    {
        if (!windowOpen || !refreshPending)
            return;

        RefreshWindow();
    }

    private void CreateOpenButton()
    {
        openButton = new Button(OpenWindow)
        {
            text = "Transport"
        };
        openButton.name = "passenger-transport-open-button";
        openButton.style.position = Position.Absolute;
        // Place immediately left of the 170 px Airline Management button (right = 697), with a 12 px gap.
        openButton.style.right = 879;
        openButton.style.bottom = 14;
        openButton.style.height = 42;
        openButton.style.minWidth = 135;
        openButton.style.paddingLeft = 14;
        openButton.style.paddingRight = 14;
        openButton.style.backgroundColor = (Color)new Color32(43, 91, 128, 255);
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
        window.name = "passenger-transport-window";
        window.style.position = Position.Absolute;
        window.style.left = new Length(22, LengthUnit.Percent);
        window.style.right = new Length(22, LengthUnit.Percent);
        window.style.top = 58;
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

        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;

        Label title = new Label("PASSENGER TRANSPORT");
        title.style.fontSize = 21;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        header.Add(title);

        Button closeButton = new Button(CloseWindow) { text = "Close  X" };
        closeButton.style.minWidth = 80;
        header.Add(closeButton);
        window.Add(header);

        statusLabel = new Label();
        statusLabel.style.fontSize = 11;
        statusLabel.style.color = MutedTextColor;
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        statusLabel.style.marginTop = 5;
        statusLabel.style.marginBottom = 10;
        window.Add(statusLabel);

        AddSectionTitle("PASSENGER JOURNEYS");
        VisualElement journeyCard = CreateCard();
        busPassengersLabel = AddMetric(journeyCard, "Bus passengers transported");
        carPassengersLabel = AddMetric(journeyCard, "Car passengers transported");
        waitingPassengersLabel = AddMetric(journeyCard, "Waiting for a bus");
        averageWaitLabel = AddMetric(journeyCard, "Average bus waiting time");
        window.Add(journeyCard);

        AddSectionTitle("SERVICE CAPACITY");
        VisualElement serviceCard = CreateCard();
        busServiceLabel = AddMetric(serviceCard, "Bus fleet and frequency");
        busCapacityLabel = AddMetric(serviceCard, "Bus capacity per hour");
        trainCapacityLabel = AddMetric(serviceCard, "Train capacity per hour");
        undergroundCapacityLabel = AddMetric(serviceCard, "Underground capacity per hour");
        window.Add(serviceCard);

        AddSectionTitle("PARKING");
        VisualElement parkingCard = CreateCard();
        parkingLabel = AddMetric(parkingCard, "Parking utilisation");
        parkingFeeLabel = AddMetric(parkingCard, "Passenger parking fee");
        window.Add(parkingCard);
    }

    private void AddSectionTitle(string title)
    {
        Label label = new Label(title);
        label.style.fontSize = 12;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = (Color)new Color32(215, 222, 231, 255);
        label.style.marginTop = 8;
        label.style.marginBottom = 5;
        window.Add(label);
    }

    private VisualElement CreateCard()
    {
        VisualElement card = new VisualElement();
        card.style.flexDirection = FlexDirection.Column;
        card.style.paddingLeft = 10;
        card.style.paddingRight = 10;
        card.style.paddingTop = 8;
        card.style.paddingBottom = 8;
        card.style.backgroundColor = CardColor;
        card.style.borderTopLeftRadius = 5;
        card.style.borderTopRightRadius = 5;
        card.style.borderBottomLeftRadius = 5;
        card.style.borderBottomRightRadius = 5;
        return card;
    }

    private static Label AddMetric(VisualElement parent, string title)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 3;

        Label name = new Label(title);
        name.style.fontSize = 11;
        name.style.color = MutedTextColor;
        row.Add(name);

        Label value = new Label("--");
        value.style.fontSize = 12;
        value.style.unityFontStyleAndWeight = FontStyle.Bold;
        value.style.color = Color.white;
        row.Add(value);

        parent.Add(row);
        return value;
    }

    private void OpenWindow()
    {
        windowOpen = true;
        window.style.display = DisplayStyle.Flex;
        ResolveReferences();
        RefreshWindow();
    }

    private void CloseWindow()
    {
        windowOpen = false;
        window.style.display = DisplayStyle.None;
    }

    private void RefreshWindow()
    {
        refreshPending = false;
        if (statusLabel == null)
            return;

        ResolveReferences();
        if (passengerCoordinator == null)
        {
            statusLabel.text = "PassengerCoordinator is not available.";
            return;
        }

        PublicTransportManager manager = publicTransportManager;
        busPassengersLabel.text =
            passengerCoordinator.BusPassengersTransported.ToString("N0");
        carPassengersLabel.text =
            passengerCoordinator.CarPassengersTransported.ToString("N0");
        waitingPassengersLabel.text =
            passengerCoordinator.WaitingForBusPassengers.ToString("N0");
        averageWaitLabel.text =
            passengerCoordinator.AverageBusWaitMinutes.ToString("F1") + " min";

        int totalSpaces = passengerCoordinator.TotalParkingSpaces;
        int occupiedSpaces = passengerCoordinator.OccupiedParkingSpaces;
        parkingLabel.text =
            $"{occupiedSpaces:N0} / {totalSpaces:N0} occupied";
        parkingFeeLabel.text =
            $"{passengerCoordinator.PassengerCarParkingFeePerMinute:F2} / simulated min";

        if (manager == null)
        {
            statusLabel.text = "PublicTransportManager is not available.";
            busServiceLabel.text = "Unavailable";
            busCapacityLabel.text = "0 passengers/hour";
            trainCapacityLabel.text = "0 passengers/hour";
            undergroundCapacityLabel.text = "0 passengers/hour";
            return;
        }

        busServiceLabel.text =
            $"{manager.ActiveBuses:N0} active / {manager.OwnedBuses:N0} owned · every {manager.BusFrequencyMinutes:F0} min · {manager.RegisteredStopCount:N0} stops";
        busCapacityLabel.text =
            $"{passengerCoordinator.BusServiceCapacityPerHour:F0} passengers/hour";
        trainCapacityLabel.text =
            $"{passengerCoordinator.TrainServiceCapacityPerHour:F0} passengers/hour";
        undergroundCapacityLabel.text =
            $"{passengerCoordinator.UndergroundServiceCapacityPerHour:F0} passengers/hour";

        statusLabel.text =
            "Live passenger totals cover completed bus and car journeys. Train and underground capacity inputs are prepared, but passenger assignment remains disabled until those journeys are implemented.";
    }
}
