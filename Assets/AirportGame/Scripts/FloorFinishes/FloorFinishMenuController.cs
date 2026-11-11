using UnityEngine;
using UnityEngine.UIElements;

public class FloorFinishMenuController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private FloorFinishCatalogue catalogue;
    [SerializeField] private FloorFinishPainter painter;

    private VisualElement buildWindow;
    private VisualElement categoryPanel;
    private ScrollView categoryList;
    private ScrollView buildableList;
    private Button floorFinishesButton;
    private Button eraseButton;
    private bool showingFloorFinishes;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
            uiDocument = FindFirstObjectByType<UIDocument>();

        if (painter == null)
            painter = FindFirstObjectByType<FloorFinishPainter>();

        if (uiDocument == null)
        {
            Debug.LogError("FloorFinishMenuController: UIDocument is missing.", this);
            enabled = false;
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        buildWindow = root.Q<VisualElement>("build-window");
        categoryPanel = root.Q<VisualElement>("category-panel");
        categoryList = root.Q<ScrollView>("category-list");
        buildableList = root.Q<ScrollView>("buildable-list");

        if (buildWindow == null || categoryPanel == null ||
            categoryList == null || buildableList == null)
        {
            Debug.LogError("FloorFinishMenuController: Build menu elements are missing.", this);
            enabled = false;
            return;
        }

        CreateMenuControls();
    }

    private void Start()
    {
        ScrollView subcategoryList =
            uiDocument.rootVisualElement.Q<ScrollView>("airport-subcategory-list");

        if (subcategoryList != null)
        {
            foreach (Button button in subcategoryList.Query<Button>().ToList())
                button.clicked += ReturnToBuildMenu;
        }

        TextField searchField =
            uiDocument.rootVisualElement.Q<TextField>("build-search-field");

        if (searchField != null)
            searchField.RegisterValueChangedCallback(_ => ReturnToBuildMenu());
    }

    private void CreateMenuControls()
    {
        foreach (Button existingButton in categoryList.Query<Button>().ToList())
            existingButton.clicked += ReturnToBuildMenu;

        floorFinishesButton = new Button
        {
            text = "Floor Finishes"
        };
        floorFinishesButton.AddToClassList("category-button");
        floorFinishesButton.clicked += ShowFloorFinishes;
        categoryList.Add(floorFinishesButton);

        eraseButton = new Button
        {
            text = "ERASE FLOOR FINISH"
        };
        eraseButton.style.display = DisplayStyle.None;
        eraseButton.style.height = 34;
        eraseButton.style.marginBottom = 8;
        eraseButton.style.backgroundColor = new StyleColor(new Color(0.35f, 0.18f, 0.18f));
        eraseButton.style.color = Color.white;
        eraseButton.clicked += () =>
        {
            if (painter != null)
                painter.BeginErasing();
        };

        categoryPanel.Add(eraseButton);
    }

    private void ShowFloorFinishes()
    {
        showingFloorFinishes = true;

        if (painter != null)
            painter.CancelPainting();

        foreach (Button button in categoryList.Query<Button>().ToList())
            button.RemoveFromClassList("category-selected");

        floorFinishesButton.AddToClassList("category-selected");
        eraseButton.style.display = DisplayStyle.Flex;

        buildableList.Clear();

        if (catalogue == null || catalogue.Count == 0)
        {
            Label emptyLabel = new Label("No floor finishes are assigned.");
            emptyLabel.style.color = Color.white;
            buildableList.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < catalogue.Count; i++)
        {
            FloorFinishDefinition finish = catalogue.GetAt(i);

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
        if (!showingFloorFinishes)
            return;

        showingFloorFinishes = false;
        eraseButton.style.display = DisplayStyle.None;
        floorFinishesButton.RemoveFromClassList("category-selected");

        if (painter != null)
            painter.CancelPainting();
    }
}
