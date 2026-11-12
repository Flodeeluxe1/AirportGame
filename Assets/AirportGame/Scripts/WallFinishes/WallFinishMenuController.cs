using UnityEngine;
using UnityEngine.UIElements;

public class WallFinishMenuController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private WallFinishCatalogue catalogue;
    [SerializeField] private WallFinishPainter painter;
    [SerializeField] private FloorFinishPainter floorPainter;

    private VisualElement categoryPanel;
    private ScrollView categoryList;
    private ScrollView wallSubcategoryList;
    private ScrollView buildableList;
    private Button wallFinishesButton;
    private Button eraseButton;
    private bool showingWallFinishes;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
            uiDocument = FindFirstObjectByType<UIDocument>();

        if (painter == null)
            painter = FindFirstObjectByType<WallFinishPainter>();

        if (floorPainter == null)
            floorPainter = FindFirstObjectByType<FloorFinishPainter>();

        if (uiDocument == null)
        {
            Debug.LogError(
                "WallFinishMenuController: UIDocument is missing.",
                this);
            enabled = false;
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        categoryPanel = root.Q<VisualElement>("category-panel");
        categoryList = root.Q<ScrollView>("category-list");
        wallSubcategoryList = root.Q<ScrollView>("wall-subcategory-list");
        buildableList = root.Q<ScrollView>("buildable-list");
        wallFinishesButton = root.Q<Button>("category-wall-finishes");

        if (categoryPanel == null ||
            categoryList == null ||
            wallSubcategoryList == null ||
            buildableList == null ||
            wallFinishesButton == null)
        {
            Debug.LogError(
                "WallFinishMenuController: Required build menu elements are missing.",
                this);
            enabled = false;
            return;
        }

        CreateMenuControls();
    }

    private void Start()
    {
        foreach (Button button in categoryList.Query<Button>().ToList())
        {
            if (button != wallFinishesButton)
                button.clicked += ReturnToBuildMenu;
        }

        foreach (Button button in wallSubcategoryList.Query<Button>().ToList())
        {
            if (button != wallFinishesButton)
                button.clicked += ReturnToBuildMenu;
        }

        ScrollView floorSubcategoryList =
            uiDocument.rootVisualElement.Q<ScrollView>("floor-subcategory-list");

        if (floorSubcategoryList != null)
        {
            foreach (Button button in floorSubcategoryList.Query<Button>().ToList())
                button.clicked += ReturnToBuildMenu;
        }

        ScrollView airportSubcategoryList =
            uiDocument.rootVisualElement.Q<ScrollView>("airport-subcategory-list");

        if (airportSubcategoryList != null)
        {
            foreach (Button button in airportSubcategoryList.Query<Button>().ToList())
                button.clicked += ReturnToBuildMenu;
        }

        TextField searchField =
            uiDocument.rootVisualElement.Q<TextField>("build-search-field");

        if (searchField != null)
            searchField.RegisterValueChangedCallback(_ => ReturnToBuildMenu());
    }

    private void CreateMenuControls()
    {
        wallFinishesButton.clicked += ShowWallFinishes;

        eraseButton = new Button
        {
            text = "ERASE WALL FINISH"
        };

        eraseButton.style.display = DisplayStyle.None;
        eraseButton.style.height = 34;
        eraseButton.style.marginBottom = 8;
        eraseButton.style.backgroundColor =
            new StyleColor(new Color(0.35f, 0.18f, 0.18f));
        eraseButton.style.color = Color.white;

        eraseButton.clicked += () =>
        {
            if (painter != null)
                painter.BeginErasing();
        };

        categoryPanel.Add(eraseButton);
    }

    private void ShowWallFinishes()
    {
        showingWallFinishes = true;

        if (floorPainter != null)
            floorPainter.CancelPainting();

        if (painter != null)
            painter.CancelPainting();

        foreach (Button button in categoryList.Query<Button>().ToList())
            button.RemoveFromClassList("category-selected");

        foreach (Button button in wallSubcategoryList.Query<Button>().ToList())
            button.RemoveFromClassList("category-selected");

        wallFinishesButton.AddToClassList("category-selected");
        eraseButton.style.display = DisplayStyle.Flex;

        buildableList.Clear();

        if (catalogue == null || catalogue.Count == 0)
        {
            Label emptyLabel = new Label("No wall finishes are assigned.");
            emptyLabel.style.color = Color.white;
            buildableList.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < catalogue.Count; i++)
        {
            WallFinishDefinition finish = catalogue.GetAt(i);

            if (finish == null || finish.material == null)
                continue;

            VisualElement card = new VisualElement();
            card.AddToClassList("buildable-card");
            card.AddToClassList("floor-finish-card");

            Image icon = new Image
            {
                image = finish.icon,
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList("buildable-icon");
            icon.AddToClassList("floor-finish-icon");
            card.Add(icon);

            Label name = new Label(finish.displayName);
            name.AddToClassList("buildable-name");
            name.AddToClassList("floor-finish-name");
            card.Add(name);

            card.RegisterCallback<ClickEvent>(evt =>
            {
                if (painter != null)
                    painter.BeginPainting(finish);

                foreach (VisualElement child in buildableList.Children())
                    child.RemoveFromClassList("buildable-selected");

                card.AddToClassList("buildable-selected");
                evt.StopPropagation();
            });

            buildableList.Add(card);
        }
    }

    public void ReturnToBuildMenu()
    {
        if (!showingWallFinishes)
        {
            if (painter != null && painter.IsActive)
                painter.CancelPainting();

            if (floorPainter != null && floorPainter.IsActive)
                floorPainter.CancelPainting();

            return;
        }

        showingWallFinishes = false;
        eraseButton.style.display = DisplayStyle.None;
        wallFinishesButton.RemoveFromClassList("category-selected");

        if (painter != null)
            painter.CancelPainting();

        if (floorPainter != null)
            floorPainter.CancelPainting();
    }
}
