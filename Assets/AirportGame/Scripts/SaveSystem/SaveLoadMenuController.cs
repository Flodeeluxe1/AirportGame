
using UnityEngine;
using UnityEngine.UIElements;

public class SaveLoadMenuController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private AirportWorldResetController airportWorldResetController;

    [Header("Slot Settings")]
    [SerializeField, Min(1)] private int numberOfSlots = 10;

    [Header("Appearance")]
    [SerializeField] private float panelWidth = 440f;

    private VisualElement root;
    private VisualElement overlay;
    private VisualElement panel;
    private Label statusLabel;

    private bool isOpen;
    private bool isSaveScreen;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (airportWorldResetController == null)
        {
            airportWorldResetController =
                FindFirstObjectByType<AirportWorldResetController>();
        }
    }

    private void Start()
    {
        if (uiDocument == null)
        {
            Debug.LogError(
                "SaveLoadMenuController: UIDocument is missing.",
                this
            );

            enabled = false;
            return;
        }

        if (airportWorldResetController == null)
        {
            Debug.LogError(
                "SaveLoadMenuController: AirportWorldResetController " +
                "could not be found. Assign it in the Inspector.",
                this
            );

            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;
    }

    private void OnDisable()
    {
        CloseSlotScreen();
        root = null;
    }

    // Connect GameMenuUIController.onSaveRequested to this method.
    public void OpenSaveSlots()
    {
        OpenSlotScreen(true);
    }

    // Connect GameMenuUIController.onLoadRequested to this method.
    public void OpenLoadSlots()
    {
        OpenSlotScreen(false);
    }

    private void OpenSlotScreen(bool saveScreen)
    {
        if (root == null)
        {
            if (uiDocument != null)
                root = uiDocument.rootVisualElement;

            if (root == null)
            {
                Debug.LogError(
                    "SaveLoadMenuController: UI root is unavailable.",
                    this
                );

                return;
            }
        }

        if (airportWorldResetController == null)
        {
            Debug.LogError(
                "SaveLoadMenuController: AirportWorldResetController " +
                "reference is missing.",
                this
            );

            return;
        }

        CloseSlotScreen();

        isSaveScreen = saveScreen;
        isOpen = true;

        CreateOverlay();
        CreatePanel();
        CreateHeader();

        CreateDescription(
            saveScreen
                ? "Choose a slot for your current airport and flight schedule."
                : "Choose a slot to restore its airport and flight schedule."
        );

        CreateSlotButtons();
        CreateStatusLabel();
        CreateBackButton();
    }

    private void CreateOverlay()
    {
        overlay = new VisualElement();
        overlay.name = "save-load-overlay";

        overlay.style.position = Position.Absolute;
        overlay.style.left = 0;
        overlay.style.right = 0;
        overlay.style.top = 0;
        overlay.style.bottom = 0;

        overlay.style.backgroundColor =
            new Color(0f, 0f, 0f, 0.82f);

        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;

        overlay.pickingMode = PickingMode.Position;

        root.Add(overlay);
    }

    private void CreatePanel()
    {
        panel = new VisualElement();
        panel.name = "save-load-panel";

        panel.style.width = panelWidth;
        panel.style.maxWidth =
            new Length(92f, LengthUnit.Percent);

        panel.style.maxHeight =
            new Length(90f, LengthUnit.Percent);

        panel.style.paddingLeft = 22f;
        panel.style.paddingRight = 22f;
        panel.style.paddingTop = 20f;
        panel.style.paddingBottom = 20f;

        panel.style.backgroundColor =
            (Color)new Color32(27, 35, 47, 255);

        panel.style.flexDirection = FlexDirection.Column;

        SetBorder(
            panel,
            1f,
            new Color32(82, 98, 119, 255)
        );

        SetRadius(panel, 12f);

        overlay.Add(panel);
    }

    private void CreateHeader()
    {
        VisualElement header = new VisualElement();

        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.marginBottom = 12f;

        panel.Add(header);

        Label title = new Label(
            isSaveScreen ? "SAVE AIRPORT" : "LOAD AIRPORT"
        );

        title.style.color = Color.white;
        title.style.fontSize = 22f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;

        header.Add(title);

        Button closeButton = new Button(CloseSlotScreen)
        {
            text = "X"
        };

        closeButton.tooltip = "Close slot selection";
        closeButton.style.width = 36f;
        closeButton.style.height = 32f;
        closeButton.style.color = Color.white;
        closeButton.style.backgroundColor =
            (Color)new Color32(51, 63, 79, 255);

        SetRadius(closeButton, 6f);

        header.Add(closeButton);
    }

    private void CreateDescription(string description)
    {
        Label label = new Label(description);

        label.style.color =
            (Color)new Color32(176, 188, 202, 255);

        label.style.fontSize = 13f;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.marginBottom = 16f;

        panel.Add(label);
    }

    private void CreateSlotButtons()
    {
        ScrollView scrollView = new ScrollView();
        scrollView.name = "save-load-slot-list";

        scrollView.style.flexGrow = 1f;
        scrollView.style.flexShrink = 1f;
        scrollView.style.minHeight = 100f;

        panel.Add(scrollView);

        int slots = Mathf.Clamp(numberOfSlots, 1, 10);

        for (int slot = 1; slot <= slots; slot++)
        {
            int selectedSlot = slot;

            Button slotButton = new Button(
                () => OnSlotSelected(selectedSlot)
            );

            slotButton.name = $"save-load-slot-{slot}";
            slotButton.text =
                $"Slot {slot}  —  " +
                (isSaveScreen ? "Save Airport" : "Load Airport");

            slotButton.style.height = 44f;
            slotButton.style.marginBottom = 7f;

            slotButton.style.backgroundColor =
                (Color)new Color32(48, 61, 78, 255);

            slotButton.style.color = Color.white;
            slotButton.style.fontSize = 14f;
            slotButton.style.unityFontStyleAndWeight =
                FontStyle.Bold;

            slotButton.style.unityTextAlign =
                TextAnchor.MiddleCenter;

            SetBorder(
                slotButton,
                1f,
                new Color32(81, 96, 115, 255)
            );

            SetRadius(slotButton, 7f);

            scrollView.Add(slotButton);
        }
    }

    private void CreateStatusLabel()
    {
        statusLabel = new Label("");

        statusLabel.name = "save-load-status";
        statusLabel.style.color =
            (Color)new Color32(220, 225, 232, 255);

        statusLabel.style.fontSize = 12f;
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        statusLabel.style.marginTop = 10f;
        statusLabel.style.marginBottom = 8f;

        panel.Add(statusLabel);
    }

    private void CreateBackButton()
    {
        Button backButton = new Button(CloseSlotScreen)
        {
            text = "Back to Game Menu"
        };

        backButton.name = "save-load-back";
        backButton.style.height = 42f;
        backButton.style.marginTop = 6f;

        backButton.style.backgroundColor =
            (Color)new Color32(40, 112, 85, 255);

        backButton.style.color = Color.white;
        backButton.style.fontSize = 14f;
        backButton.style.unityFontStyleAndWeight =
            FontStyle.Bold;

        SetRadius(backButton, 7f);

        panel.Add(backButton);
    }

    private void OnSlotSelected(int slot)
    {
        if (airportWorldResetController == null)
        {
            SetStatus("The airport save controller is missing.");
            return;
        }

        if (isSaveScreen)
        {
            bool saved = airportWorldResetController.TrySaveSlot(
                slot,
                "Airport Slot " + slot
            );

            if (saved)
            {
                Debug.Log(
                    $"Save/load menu: saved airport to slot {slot}.",
                    this
                );

                CloseSlotScreen();
            }
            else
            {
                SetStatus(
                    $"Could not save Slot {slot}. " +
                    "Check the Unity Console for details."
                );
            }
        }
        else
        {
            bool loaded =
                airportWorldResetController
                    .TryRestoreBuildingsAndFoundations(slot);

            if (loaded)
            {
                Debug.Log(
                    $"Save/load menu: loaded airport from slot {slot}.",
                    this
                );

                CloseSlotScreen();
            }
            else
            {
                SetStatus(
                    $"Could not load Slot {slot}. " +
                    "It may be empty or the save may be invalid. " +
                    "Check the Unity Console for details."
                );
            }
        }
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null)
            statusLabel.text = message;

        Debug.LogWarning(
            $"Save/load menu: {message}",
            this
        );
    }

    public void CloseSlotScreen()
    {
        isOpen = false;

        if (overlay != null)
        {
            overlay.RemoveFromHierarchy();
            overlay = null;
            panel = null;
            statusLabel = null;
        }
    }

    private static void SetBorder(
        VisualElement element,
        float width,
        Color color)
    {
        element.style.borderTopWidth = width;
        element.style.borderBottomWidth = width;
        element.style.borderLeftWidth = width;
        element.style.borderRightWidth = width;

        element.style.borderTopColor = color;
        element.style.borderBottomColor = color;
        element.style.borderLeftColor = color;
        element.style.borderRightColor = color;
    }

    private static void SetRadius(
        VisualElement element,
        float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }
}
