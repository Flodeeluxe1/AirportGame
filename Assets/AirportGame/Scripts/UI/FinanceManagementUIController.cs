using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Allows the player to inspect airport fee defaults and edit custom fees
/// while a valid CFO Office is assigned.
/// </summary>
public class FinanceManagementUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private AirportManagementController airportManagementController;
    private AirportManagementController subscribedController;

    private VisualElement root;
    private VisualElement window;
    private ScrollView settingsScroll;
    private Label officeStatusLabel;
    private Label availabilityMessage;
    private Button openButton;
    private Button saveButton;
    private bool windowOpen;

    private sealed class FeeFieldBinding
    {
        public TextField Field;
        public Action<AirportManagementController.FeeSettings, int> Apply;
    }

    private readonly List<FeeFieldBinding> feeFields = new();

    private static readonly Color WindowColor =
        new Color32(27, 32, 39, 250);
    private static readonly Color CardColor =
        new Color32(39, 47, 57, 255);
    private static readonly Color MutedTextColor =
        new Color32(174, 186, 199, 255);

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
            uiDocument = FindFirstObjectByType<UIDocument>();

        if (airportManagementController == null)
            airportManagementController =
                FindFirstObjectByType<AirportManagementController>();

        if (uiDocument == null)
        {
            Debug.LogError(
                "FinanceManagementUIController: UIDocument is missing.",
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
        ResolveAirportManagementController();
    }

    private void OnDisable()
    {
        if (subscribedController != null)
            subscribedController.StateChanged -= HandleAirportStateChanged;

        subscribedController = null;
    }

    private void ResolveAirportManagementController()
    {
        if (airportManagementController == null)
            airportManagementController =
                FindFirstObjectByType<AirportManagementController>();

        if (subscribedController == airportManagementController)
            return;

        if (subscribedController != null)
            subscribedController.StateChanged -= HandleAirportStateChanged;

        subscribedController = airportManagementController;

        if (isActiveAndEnabled && subscribedController != null)
            subscribedController.StateChanged += HandleAirportStateChanged;
    }

    private void Update()
    {
        if (windowOpen &&
            Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseWindow();
        }
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

    private void HandleAirportStateChanged()
    {
        if (windowOpen)
            RefreshWindow();
    }

    private void CreateOpenButton()
    {
        openButton = new Button(OpenWindow)
        {
            text = "Finance"
        };

        openButton.name = "finance-management-open-button";
        openButton.style.position = Position.Absolute;
        openButton.style.right = 550;
        openButton.style.bottom = 14;
        openButton.style.height = 42;
        openButton.style.minWidth = 135;
        openButton.style.paddingLeft = 14;
        openButton.style.paddingRight = 14;
        openButton.style.backgroundColor =
            (Color)new Color32(43, 91, 128, 255);
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
        window.name = "finance-management-window";
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
        CreateStatusArea();

        settingsScroll = new ScrollView(ScrollViewMode.Vertical);
        settingsScroll.name = "finance-fees-scroll";
        settingsScroll.style.flexGrow = 1;
        settingsScroll.style.minHeight = 0;
        settingsScroll.horizontalScrollerVisibility =
            ScrollerVisibility.Hidden;
        settingsScroll.contentContainer.style.flexDirection =
            FlexDirection.Column;
        window.Add(settingsScroll);

        VisualElement footer = new VisualElement();
        footer.style.flexDirection = FlexDirection.Row;
        footer.style.justifyContent = Justify.FlexEnd;
        footer.style.alignItems = Align.Center;
        footer.style.marginTop = 8;

        saveButton = new Button(SaveChanges)
        {
            text = "Save Changes"
        };
        saveButton.style.height = 42;
        saveButton.style.minWidth = 150;
        saveButton.style.backgroundColor =
            (Color)new Color32(43, 112, 78, 255);
        saveButton.style.color = Color.white;
        footer.Add(saveButton);
        window.Add(footer);
    }

    private void CreateHeader()
    {
        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;

        Label title = new Label("AIRPORT FEES");
        title.style.fontSize = 22;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;

        officeStatusLabel = new Label("CFO OFFICE ACTIVE");
        officeStatusLabel.style.fontSize = 11;
        officeStatusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        officeStatusLabel.style.color = (Color)new Color32(100, 210, 140, 255);

        Button closeButton = new Button(CloseWindow)
        {
            text = "Close  X"
        };
        closeButton.style.minWidth = 80;

        header.Add(title);
        header.Add(officeStatusLabel);
        header.Add(closeButton);
        window.Add(header);

        Label subtitle = new Label("CUSTOM FEE MANAGEMENT");
        subtitle.style.fontSize = 11;
        subtitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        subtitle.style.color = MutedTextColor;
        subtitle.style.marginTop = 3;
        subtitle.style.marginBottom = 8;
        window.Add(subtitle);
    }

    private void CreateStatusArea()
    {
        availabilityMessage = new Label();
        availabilityMessage.style.whiteSpace = WhiteSpace.Normal;
        availabilityMessage.style.fontSize = 11;
        availabilityMessage.style.marginBottom = 8;
        availabilityMessage.style.paddingLeft = 9;
        availabilityMessage.style.paddingRight = 9;
        availabilityMessage.style.paddingTop = 7;
        availabilityMessage.style.paddingBottom = 7;
        availabilityMessage.style.backgroundColor = CardColor;
        availabilityMessage.style.color = MutedTextColor;
        window.Add(availabilityMessage);
    }

    private void OpenWindow()
    {
        windowOpen = true;
        window.style.display = DisplayStyle.Flex;
        RefreshWindow();
    }

    private void CloseWindow()
    {
        windowOpen = false;
        window.style.display = DisplayStyle.None;
    }

    private void RefreshWindow()
    {
        ResolveAirportManagementController();

        if (airportManagementController == null)
        {
            officeStatusLabel.text = "CFO CONTROLLER MISSING";
            officeStatusLabel.style.color = (Color)new Color32(235, 105, 95, 255);
            availabilityMessage.text =
                "AirportManagementController was not found. Fee settings are unavailable.";
            saveButton.SetEnabled(false);
            return;
        }

        bool canEdit = airportManagementController.CanAdjustFees;
        officeStatusLabel.text = canEdit
            ? "CFO OFFICE ACTIVE"
            : "CFO OFFICE REQUIRED";
        officeStatusLabel.style.color = canEdit
            ? (Color)new Color32(100, 210, 140, 255)
            : (Color)new Color32(235, 175, 85, 255);

        availabilityMessage.text = canEdit
            ? "Custom fee management is available. Changes take effect after saving."
            : "A functional CFO Office is required to change fees. Default airport fees are currently being used.";

        AirportManagementController.FeeSettings settings =
            airportManagementController.GetFeeSettingsForDisplay();
        BuildFeeFields(settings, canEdit);
        saveButton.SetEnabled(canEdit);
    }

    private void FeeFieldsEnabled(bool enabled)
    {
        foreach (FeeFieldBinding binding in feeFields)
            binding.Field.SetEnabled(enabled);
    }

    private void BuildFeeFields(
        AirportManagementController.FeeSettings settings,
        bool canEdit)
    {
        settingsScroll.Clear();
        feeFields.Clear();

        AddSectionTitle("LANDING FEES");
        AddCategoryFeeField(
            "Hobby",
            settings.hobby.landingFee,
            (s, v) => s.hobby.landingFee = v);
        AddCategoryFeeField(
            "Flight School",
            settings.flightSchool.landingFee,
            (s, v) => s.flightSchool.landingFee = v);
        AddCategoryFeeField(
            "Sightseeing",
            settings.sightseeing.landingFee,
            (s, v) => s.sightseeing.landingFee = v);
        AddCategoryFeeField(
            "Cargo",
            settings.cargo.landingFee,
            (s, v) => s.cargo.landingFee = v);
        AddCategoryFeeField(
            "Passenger",
            settings.passenger.landingFee,
            (s, v) => s.passenger.landingFee = v);

        AddSectionTitle("PARKING FEES");
        AddDescription("Each category has its own price per completed billing interval.");
        AddParkingCategory(
            "Hobby",
            settings.hobby,
            (s, v) => s.hobby.parkingFee = v,
            (s, v) => s.hobby.parkingIntervalMinutes = v);
        AddParkingCategory(
            "Flight School",
            settings.flightSchool,
            (s, v) => s.flightSchool.parkingFee = v,
            (s, v) => s.flightSchool.parkingIntervalMinutes = v);
        AddParkingCategory(
            "Sightseeing",
            settings.sightseeing,
            (s, v) => s.sightseeing.parkingFee = v,
            (s, v) => s.sightseeing.parkingIntervalMinutes = v);
        AddParkingCategory(
            "Cargo",
            settings.cargo,
            (s, v) => s.cargo.parkingFee = v,
            (s, v) => s.cargo.parkingIntervalMinutes = v);
        AddParkingCategory(
            "Passenger",
            settings.passenger,
            (s, v) => s.passenger.parkingFee = v,
            (s, v) => s.passenger.parkingIntervalMinutes = v);

        AddSectionTitle("FUEL SERVICE FEES");
        AddDescription("A flat service charge is added once per completed refuelling session. Fuel price per litre remains configured at the fuel station.");
        AddCleaningField(
            "Per refuelling session",
            settings.fuelServiceFee,
            (s, v) => s.fuelServiceFee = v);

        AddSectionTitle("CLEANING FEES");
        AddDescription("Cleaning price is selected using the aircraft's maximum passenger capacity.");
        AddCleaningField(
            "1–10 passengers",
            settings.cleaningFeeUpTo10Passengers,
            (s, v) => s.cleaningFeeUpTo10Passengers = v);
        AddCleaningField(
            "11–50 passengers",
            settings.cleaningFeeUpTo50Passengers,
            (s, v) => s.cleaningFeeUpTo50Passengers = v);
        AddCleaningField(
            "51–100 passengers",
            settings.cleaningFeeUpTo100Passengers,
            (s, v) => s.cleaningFeeUpTo100Passengers = v);
        AddCleaningField(
            "101–200 passengers",
            settings.cleaningFeeUpTo200Passengers,
            (s, v) => s.cleaningFeeUpTo200Passengers = v);
        AddCleaningField(
            "201–300 passengers",
            settings.cleaningFeeUpTo300Passengers,
            (s, v) => s.cleaningFeeUpTo300Passengers = v);
        AddCleaningField(
            "More than 300 passengers",
            settings.cleaningFeeAbove300Passengers,
            (s, v) => s.cleaningFeeAbove300Passengers = v);

        FeeFieldsEnabled(canEdit);
    }

    private void AddCategoryFeeField(
        string category,
        int value,
        Action<AirportManagementController.FeeSettings, int> apply)
    {
        VisualElement row = CreateCard();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.SpaceBetween;

        Label title = new Label(category);
        title.style.fontSize = 11;
        title.style.color = Color.white;
        row.Add(title);

        AddNumberField(row, "Landing fee (€)", value, apply, 135);
        settingsScroll.Add(row);
    }

    private void AddParkingCategory(
        string category,
        AirportManagementController.AircraftFeeProfile profile,
        Action<AirportManagementController.FeeSettings, int> setParking,
        Action<AirportManagementController.FeeSettings, int> setInterval)
    {
        VisualElement card = CreateCard();
        Label title = new Label(category.ToUpperInvariant());
        title.style.fontSize = 12;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        title.style.marginBottom = 5;
        card.Add(title);

        VisualElement fields = new VisualElement();
        fields.style.flexDirection = FlexDirection.Row;
        fields.style.flexWrap = Wrap.Wrap;
        AddNumberField(fields, "Parking fee (€)", profile.parkingFee, setParking);
        AddNumberField(fields, "Every (minutes)", profile.parkingIntervalMinutes, setInterval);
        card.Add(fields);
        settingsScroll.Add(card);
    }

    private void AddCleaningField(
        string label,
        int value,
        Action<AirportManagementController.FeeSettings, int> apply)
    {
        VisualElement row = CreateCard();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.SpaceBetween;

        Label title = new Label(label);
        title.style.fontSize = 11;
        title.style.color = Color.white;
        row.Add(title);

        AddNumberField(row, "Fee (€)", value, apply, 115);
        settingsScroll.Add(row);
    }

    private void AddNumberField(
        VisualElement parent,
        string label,
        int value,
        Action<AirportManagementController.FeeSettings, int> apply,
        float width = 145)
    {
        VisualElement container = new VisualElement();
        container.style.width = width;
        container.style.marginRight = 8;
        container.style.marginBottom = 5;

        Label title = new Label(label);
        title.style.fontSize = 9;
        title.style.color = MutedTextColor;

        TextField field = new TextField();
        field.value = Mathf.Max(0, value).ToString();
        field.style.height = 28;
        field.style.color = Color.white;
        field.style.backgroundColor = (Color)new Color32(235, 238, 242, 255);
        field.style.marginTop = 2;

        container.Add(title);
        container.Add(field);
        parent.Add(container);

        feeFields.Add(new FeeFieldBinding
        {
            Field = field,
            Apply = apply
        });
    }

    private VisualElement CreateCard()
    {
        VisualElement card = new VisualElement();
        card.style.flexDirection = FlexDirection.Column;
        card.style.paddingLeft = 9;
        card.style.paddingRight = 9;
        card.style.paddingTop = 7;
        card.style.paddingBottom = 7;
        card.style.marginBottom = 5;
        card.style.backgroundColor = CardColor;
        card.style.borderTopLeftRadius = 5;
        card.style.borderTopRightRadius = 5;
        card.style.borderBottomLeftRadius = 5;
        card.style.borderBottomRightRadius = 5;
        return card;
    }

    private void AddSectionTitle(string title)
    {
        Label label = new Label(title);
        label.style.fontSize = 13;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = (Color)new Color32(215, 222, 231, 255);
        label.style.marginTop = 7;
        label.style.marginBottom = 5;
        settingsScroll.Add(label);
    }

    private void AddDescription(string message)
    {
        Label label = new Label(message);
        label.style.fontSize = 10;
        label.style.color = MutedTextColor;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.marginBottom = 5;
        settingsScroll.Add(label);
    }

    private void SaveChanges()
    {
        if (airportManagementController == null ||
            !airportManagementController.CanAdjustFees)
        {
            RefreshWindow();
            return;
        }

        AirportManagementController.FeeSettings settings =
            airportManagementController.GetFeeSettingsForDisplay();

        foreach (FeeFieldBinding binding in feeFields)
        {
            if (!int.TryParse(binding.Field.value, out int value) ||
                value < 0)
            {
                binding.Field.AddToClassList("unity-invalid");
                availabilityMessage.text =
                    "Enter a whole number of zero or greater in every field.";
                return;
            }

            binding.Field.RemoveFromClassList("unity-invalid");
            binding.Apply(settings, value);
        }

        if (airportManagementController.SaveCustomFeeSettings(settings))
        {
            availabilityMessage.text =
                "Fee changes applied. Save your airport to store them in this save slot.";
        }
        else
        {
            availabilityMessage.text =
                "Could not save the airport fees. Check the Console for details.";
        }
    }
}
