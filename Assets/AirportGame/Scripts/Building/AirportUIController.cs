
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class AirportUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private BuildCatalog buildCatalog;
    [SerializeField] private BuildController buildController;
    [SerializeField] private BuildingSelectionController buildingSelectionController;

    private VisualElement root;

    private VisualElement buildWindow;
    private ScrollView buildableList;

    private Button buildButton;
    private Button zoneButton;
    private Button deleteAllButton;

    private readonly Dictionary<BuildCategory, Button> categoryButtons =
        new();

    private readonly Dictionary<BuildableDefinition, VisualElement> buildableCards =
        new();

    private BuildCategory currentCategory;
    private BuildableDefinition selectedBuildable;

    private void Awake()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (uiDocument == null)
        {
            Debug.LogError(
                "AirportUIController: UIDocument is not assigned."
            );
            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;

        FindElements();
        RegisterEvents();

        currentCategory = BuildCategory.Floor;

        HideBuildWindow();
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (buildController != null &&
                buildController.IsBuilding)
            {
                buildController.CancelBuilding();
            }

            HideBuildWindow();
        }
    }

    private void FindElements()
    {
        buildWindow = root.Q<VisualElement>("build-window");
        buildableList = root.Q<ScrollView>("buildable-list");

        if (buildableList != null)
        {
            buildableList.contentContainer.style.flexDirection =
                FlexDirection.Row;

            buildableList.contentContainer.style.flexWrap =
                Wrap.Wrap;

            buildableList.contentContainer.style.alignContent =
                Align.FlexStart;

            buildableList.contentContainer.style.alignItems =
                Align.FlexStart;
        }

        buildButton = root.Q<Button>("build-button");
        zoneButton = root.Q<Button>("zone-button");
        deleteAllButton = root.Q<Button>("delete-all-button");

        if (buildingSelectionController == null)
        {
            buildingSelectionController =
                FindFirstObjectByType<BuildingSelectionController>();
        }

        categoryButtons[BuildCategory.Floor] =
            root.Q<Button>("category-floor");

        categoryButtons[BuildCategory.Wall] =
            root.Q<Button>("category-wall");

        categoryButtons[BuildCategory.Door] =
            root.Q<Button>("category-door");

        categoryButtons[BuildCategory.Airport] =
            root.Q<Button>("category-airport");

        categoryButtons[BuildCategory.Roads] =
            root.Q<Button>("category-roads");

        categoryButtons[BuildCategory.Taxiways] =
            root.Q<Button>("category-taxiways");

        categoryButtons[BuildCategory.Tarmac] =
            root.Q<Button>("category-tarmac");

        categoryButtons[BuildCategory.Commercial] =
            root.Q<Button>("category-commercial");

        if (buildWindow == null)
            Debug.LogError("AirportUIController: 'build-window' was not found.");

        if (buildableList == null)
            Debug.LogError("AirportUIController: 'buildable-list' was not found.");

        if (buildButton == null)
            Debug.LogError("AirportUIController: 'build-button' was not found.");

        if (zoneButton == null)
            Debug.LogError("AirportUIController: 'zone-button' was not found.");

        if (deleteAllButton == null)
            Debug.LogError("AirportUIController: 'delete-all-button' was not found.");
    }

    private void RegisterEvents()
    {
        if (buildButton != null)
            buildButton.clicked += ToggleBuildWindow;

        if (zoneButton != null)
            zoneButton.clicked += OnZoneButtonClicked;

        if (deleteAllButton != null)
            deleteAllButton.clicked += OnDeleteAllButtonClicked;

        foreach (KeyValuePair<BuildCategory, Button> pair in categoryButtons)
        {
            if (pair.Value == null)
                continue;

            BuildCategory category = pair.Key;

            pair.Value.clicked += () =>
            {
                SelectCategory(category);
            };
        }
    }

    private void ToggleBuildWindow()
    {
        if (buildWindow == null)
            return;

        if (IsBuildWindowVisible())
        {
            HideBuildWindow();

            if (buildController != null &&
                buildController.IsBuilding)
            {
                buildController.CancelBuilding();
            }
        }
        else
        {
            ShowBuildWindow();
        }
    }

    private void ShowBuildWindow()
    {
        if (buildWindow == null)
            return;

        buildWindow.style.display = DisplayStyle.Flex;
        SelectCategory(currentCategory);
    }

    private void HideBuildWindow()
    {
        if (buildWindow != null)
            buildWindow.style.display = DisplayStyle.None;
    }

    private bool IsBuildWindowVisible()
    {
        return buildWindow != null &&
               buildWindow.resolvedStyle.display != DisplayStyle.None;
    }

    private void SelectCategory(BuildCategory category)
    {
        currentCategory = category;
        UpdateCategoryButtonStates();
        PopulateBuildableList();
    }

    private void UpdateCategoryButtonStates()
    {
        foreach (KeyValuePair<BuildCategory, Button> pair in categoryButtons)
        {
            if (pair.Value == null)
                continue;

            if (pair.Key == currentCategory)
            {
                pair.Value.AddToClassList("category-selected");
            }
            else
            {
                pair.Value.RemoveFromClassList("category-selected");
            }
        }
    }

    private void PopulateBuildableList()
    {
        if (buildableList == null)
            return;

        buildableList.Clear();
        buildableCards.Clear();

        if (buildCatalog == null ||
            buildCatalog.buildables == null)
        {
            return;
        }

        foreach (BuildableDefinition definition in buildCatalog.buildables)
        {
            if (definition == null ||
                definition.category != currentCategory)
            {
                continue;
            }

            CreateBuildableButton(definition);
        }
    }

    private void CreateBuildableButton(BuildableDefinition definition)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("buildable-card");

        Label nameLabel = new Label(definition.displayName);
        nameLabel.AddToClassList("buildable-name");
        card.Add(nameLabel);

        Label costLabel = new Label($"${definition.buildCost}");
        costLabel.AddToClassList("buildable-cost");
        card.Add(costLabel);

        buildableCards[definition] = card;

        if (selectedBuildable == definition)
        {
            card.AddToClassList("buildable-selected");
        }

        card.RegisterCallback<ClickEvent>(evt =>
        {
            SelectBuildable(definition);
            evt.StopPropagation();
        });

        buildableList.Add(card);
    }

    private void SelectBuildable(BuildableDefinition definition)
    {
        if (buildController == null)
        {
            Debug.LogError(
                "AirportUIController: BuildController is not assigned."
            );
            return;
        }

        selectedBuildable = definition;
        UpdateBuildableSelectionVisuals();
        buildController.StartBuilding(definition);
    }

    private void UpdateBuildableSelectionVisuals()
    {
        foreach (
            KeyValuePair<BuildableDefinition, VisualElement> pair
            in buildableCards)
        {
            if (pair.Key == selectedBuildable)
            {
                pair.Value.AddToClassList("buildable-selected");
            }
            else
            {
                pair.Value.RemoveFromClassList("buildable-selected");
            }
        }
    }

    private void OnZoneButtonClicked()
    {
        Debug.Log("Zones UI will be implemented later.");
    }

    private void OnDeleteAllButtonClicked()
    {
        if (buildingSelectionController == null)
        {
            buildingSelectionController =
                FindFirstObjectByType<BuildingSelectionController>();
        }

        if (buildingSelectionController == null)
        {
            Debug.LogError(
                "AirportUIController: BuildingSelectionController " +
                "could not be found."
            );
            return;
        }

        if (buildController != null &&
            buildController.IsBuilding)
        {
            buildController.CancelBuilding();
        }

        HideBuildWindow();
        buildingSelectionController.BeginDeleteSelectionMode();
    }
}
