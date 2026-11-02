
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class CargoTugLotShopUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private BuildController buildController;

    [Header("Double Click")]
    [SerializeField, Min(0.1f)]
    private float doubleClickSeconds = 0.35f;

    [Header("Panel")]
    [SerializeField] private float panelWidth = 320f;
    [SerializeField] private float panelPadding = 18f;

    [Header("UI Colors")]
    [SerializeField]
    private Color panelBackgroundColor =
        new Color(0.10f, 0.12f, 0.15f, 0.98f);

    [SerializeField]
    private Color primaryTextColor =
        new Color(0.95f, 0.96f, 0.98f, 1f);

    [SerializeField]
    private Color secondaryTextColor =
        new Color(0.72f, 0.76f, 0.82f, 1f);

    [SerializeField]
    private Color buyButtonColor =
        new Color(0.18f, 0.48f, 0.28f, 1f);

    [SerializeField]
    private Color disabledButtonColor =
        new Color(0.25f, 0.28f, 0.33f, 1f);

    private VisualElement root;
    private VisualElement panel;

    private Label titleLabel;
    private Label balanceLabel;

    private readonly Label[] slotLabels = new Label[4];
    private readonly Button[] buyButtons = new Button[4];

    private CargoTugLotSpawner selectedLot;
    private AirportEconomyManager economyManager;

    private CargoTugLotSpawner previousClickedLot;
    private float previousClickTime = -10f;

    private bool isOpen;

    private void Awake()
    {
        ResolveReferences();

        if (uiDocument == null)
        {
            Debug.LogError(
                "CargoTugLotShopUIController: UIDocument is missing.",
                this
            );
            return;
        }

        root = uiDocument.rootVisualElement;

        BuildPanel();
        ClosePanel();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (economyManager != null)
            economyManager.OnBalanceChanged += HandleBalanceChanged;
    }

    private void OnDisable()
    {
        if (economyManager != null)
            economyManager.OnBalanceChanged -= HandleBalanceChanged;
    }

    private void Update()
    {
        if (Mouse.current == null ||
            !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        Vector2 mousePosition =
            Mouse.current.position.ReadValue();

        if (IsPointerOverPanel(mousePosition))
            return;

        if (buildController != null && buildController.IsBuilding)
            return;

        CargoTugLotSpawner clickedLot =
            GetCargoTugLotAtMouse(mousePosition);

        if (clickedLot == null)
        {
            previousClickedLot = null;
            previousClickTime = -10f;
            return;
        }

        float now = Time.unscaledTime;

        bool isDoubleClick =
            clickedLot == previousClickedLot &&
            now - previousClickTime <= doubleClickSeconds;

        previousClickedLot = clickedLot;
        previousClickTime = now;

        if (isDoubleClick)
            OpenForLot(clickedLot);
    }

    private void ResolveReferences()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (buildController == null)
            buildController = FindFirstObjectByType<BuildController>();

        if (economyManager == null)
        {
            economyManager =
                FindFirstObjectByType<AirportEconomyManager>();
        }
    }

    private CargoTugLotSpawner GetCargoTugLotAtMouse(
        Vector2 screenPosition)
    {
        if (mainCamera == null)
            return null;

        Ray ray = mainCamera.ScreenPointToRay(screenPosition);

        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);

        System.Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance)
        );

        foreach (RaycastHit hit in hits)
        {
            CargoTugLotSpawner lot =
                hit.collider.GetComponentInParent<CargoTugLotSpawner>();

            if (lot != null)
                return lot;
        }

        return null;
    }

    private void BuildPanel()
    {
        panel = new VisualElement();
        panel.name = "CargoTugLotShopPanel";

        panel.style.position = Position.Absolute;
        panel.style.top = 0;
        panel.style.right = 0;
        panel.style.bottom = 0;
        panel.style.width = panelWidth;

        panel.style.paddingLeft = panelPadding;
        panel.style.paddingRight = panelPadding;
        panel.style.paddingTop = panelPadding;
        panel.style.paddingBottom = panelPadding;

        panel.style.backgroundColor = panelBackgroundColor;

        panel.style.borderLeftWidth = 1;
        panel.style.borderLeftColor =
            new Color(1f, 1f, 1f, 0.12f);

        panel.style.display = DisplayStyle.None;

        ScrollView scrollView = new ScrollView();
        scrollView.style.flexGrow = 1;
        panel.Add(scrollView);

        VisualElement header = new VisualElement();

        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.marginBottom = 18;

        scrollView.Add(header);

        titleLabel = CreateLabel("Cargo Tug Lot", 20, true);
        titleLabel.style.flexGrow = 1;
        header.Add(titleLabel);

        Button closeButton = new Button(ClosePanel);
        closeButton.text = "X";
        closeButton.style.width = 32;
        closeButton.style.height = 32;
        closeButton.style.color = primaryTextColor;
        closeButton.style.backgroundColor = disabledButtonColor;

        header.Add(closeButton);

        balanceLabel = CreateLabel("Airport balance: --", 14, true);
        balanceLabel.style.marginBottom = 20;
        scrollView.Add(balanceLabel);

        AddSectionTitle(scrollView, "Cargo Trailers");

        Label descriptionLabel = CreateLabel(
            "Purchase additional trailers and park them in the available lot spaces.",
            13,
            false
        );

        descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
        descriptionLabel.style.marginBottom = 18;

        scrollView.Add(descriptionLabel);

        for (int i = 0; i < 4; i++)
        {
            int slotIndex = i;

            VisualElement slot = new VisualElement();

            slot.style.paddingLeft = 12;
            slot.style.paddingRight = 12;
            slot.style.paddingTop = 12;
            slot.style.paddingBottom = 12;
            slot.style.marginBottom = 12;

            slot.style.backgroundColor =
                new Color(0.16f, 0.18f, 0.22f, 1f);

            slot.style.borderTopLeftRadius = 5;
            slot.style.borderTopRightRadius = 5;
            slot.style.borderBottomLeftRadius = 5;
            slot.style.borderBottomRightRadius = 5;

            scrollView.Add(slot);

            slotLabels[slotIndex] = CreateLabel(
                "P" + (slotIndex + 1) + " - Available",
                15,
                true
            );

            slotLabels[slotIndex].style.marginBottom = 8;
            slot.Add(slotLabels[slotIndex]);

            Button buyButton = new Button(
                () => PurchaseTrailer(slotIndex)
            );

            buyButton.text = "Buy - $1,000";
            buyButton.style.height = 36;
            buyButton.style.color = primaryTextColor;
            buyButton.style.backgroundColor = buyButtonColor;

            slot.Add(buyButton);

            buyButtons[slotIndex] = buyButton;
        }

        root.Add(panel);
    }

    private Label CreateLabel(
        string text,
        int fontSize,
        bool bold)
    {
        Label label = new Label(text);

        label.style.color = primaryTextColor;
        label.style.fontSize = fontSize;

        label.style.unityFontStyleAndWeight =
            bold ? FontStyle.Bold : FontStyle.Normal;

        return label;
    }

    private void AddSectionTitle(
        VisualElement parent,
        string text)
    {
        Label label = CreateLabel(text, 15, true);
        label.style.marginBottom = 8;
        parent.Add(label);
    }

    public void OpenForLot(CargoTugLotSpawner lot)
    {
        if (lot == null)
            return;

        ResolveReferences();

        selectedLot = lot;
        isOpen = true;

        if (panel != null)
            panel.style.display = DisplayStyle.Flex;

        UpdatePanel();
    }

    public void ClosePanel()
    {
        isOpen = false;
        selectedLot = null;

        if (panel != null)
            panel.style.display = DisplayStyle.None;
    }

    public bool IsPointerOverPanel(Vector2 screenPosition)
    {
        if (!isOpen || panel == null)
            return false;

        return screenPosition.x >= Screen.width - panelWidth;
    }

    private void PurchaseTrailer(int slotIndex)
    {
        if (selectedLot == null)
            return;

        bool purchased =
            selectedLot.TryPurchaseTrailer(slotIndex);

        if (purchased)
            UpdatePanel();
        else
            UpdatePanel();
    }

    private void HandleBalanceChanged(int newBalance)
    {
        UpdatePanel();
    }

    private void UpdatePanel()
    {
        if (!isOpen || selectedLot == null)
            return;

        if (economyManager == null)
        {
            economyManager =
                FindFirstObjectByType<AirportEconomyManager>();
        }

        int balance = economyManager != null
            ? economyManager.Balance
            : 0;

        balanceLabel.text =
            "Airport balance: $" + balance.ToString("N0");

        int price = selectedLot.TrailerPrice;

        for (int i = 0; i < 4; i++)
        {
            bool purchased =
                selectedLot.IsTrailerPurchased(i);

            slotLabels[i].text = purchased
                ? "P" + (i + 1) + " - Purchased"
                : "P" + (i + 1) + " - Available";

            if (purchased)
            {
                buyButtons[i].text = "Purchased";
                buyButtons[i].style.backgroundColor =
                    disabledButtonColor;

                buyButtons[i].SetEnabled(false);
            }
            else
            {
                bool canAfford = economyManager != null &&
                    balance >= price;

                buyButtons[i].text = canAfford
                    ? "Buy - $" + price.ToString("N0")
                    : "Insufficient funds";

                buyButtons[i].style.backgroundColor =
                    canAfford
                        ? buyButtonColor
                        : disabledButtonColor;

                buyButtons[i].SetEnabled(canAfford);
            }
        }
    }

    private void OnDestroy()
    {
        if (panel != null && panel.parent != null)
            panel.RemoveFromHierarchy();
    }
}
