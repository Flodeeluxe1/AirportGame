using System;
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
    private ScrollView airportSubcategoryList;
    private ScrollView floorSubcategoryList;
    private ScrollView wallSubcategoryList;
    private Button floorFoundationButton;
    private Button wallBuildingButton;
    private TextField buildSearchField;
    private string buildSearchQuery = string.Empty;

    private Button buildButton;
    private Button zoneButton;
    private Button deleteAllButton;

    private readonly Dictionary<BuildCategory, Button> categoryButtons =
        new();

    private readonly Dictionary<BuildableDefinition, VisualElement> buildableCards =
        new();

    private readonly Dictionary<string, Button> airportSubcategoryButtons =
        new(StringComparer.Ordinal);

    private BuildCategory currentCategory;
    private string currentAirportMenuCategory;
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
        PopulateAirportSubcategories();
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
        airportSubcategoryList = root.Q<ScrollView>("airport-subcategory-list");
        floorSubcategoryList = root.Q<ScrollView>("floor-subcategory-list");
        wallSubcategoryList = root.Q<ScrollView>("wall-subcategory-list");
        floorFoundationButton = root.Q<Button>("category-foundation");
        wallBuildingButton = root.Q<Button>("category-wall-building");
        buildSearchField = root.Q<TextField>("build-search-field");

        if (buildSearchField != null)
        {
            buildSearchField.RegisterValueChangedCallback(evt =>
            {
                buildSearchQuery = evt.newValue?.Trim() ?? string.Empty;
                PopulateBuildableList();
            });
        }

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

        categoryButtons[BuildCategory.Pipelines] =
            root.Q<Button>("category-pipelines");

        categoryButtons[BuildCategory.Commercial] =
            root.Q<Button>("category-commercial");

        if (buildWindow == null)
            Debug.LogError("AirportUIController: 'build-window' was not found.");

        if (buildableList == null)
            Debug.LogError("AirportUIController: 'buildable-list' was not found.");

        if (airportSubcategoryList == null)
            Debug.LogError("AirportUIController: 'airport-subcategory-list' was not found.");

        if (floorSubcategoryList == null)
            Debug.LogError("AirportUIController: 'floor-subcategory-list' was not found.");

        if (wallSubcategoryList == null)
            Debug.LogError("AirportUIController: 'wall-subcategory-list' was not found.");

        if (buildButton == null)
            Debug.LogError("AirportUIController: 'build-button' was not found.");

        if (zoneButton == null)
            Debug.LogError("AirportUIController: 'zone-button' was not found.");

        if (deleteAllButton == null)
            Debug.LogError("AirportUIController: 'delete-all-button' was not found.");
    }

    private void PopulateAirportSubcategories()
    {
        if (airportSubcategoryList == null)
            return;

        airportSubcategoryList.Clear();
        airportSubcategoryButtons.Clear();

        if (buildCatalog == null || buildCatalog.buildables == null)
            return;

        SortedSet<string> categories = new(StringComparer.Ordinal);

        foreach (BuildableDefinition definition in buildCatalog.buildables)
        {
            if (definition == null ||
                definition.category != BuildCategory.Airport ||
                string.IsNullOrWhiteSpace(definition.menuCategory))
            {
                continue;
            }

            categories.Add(definition.menuCategory);
        }

        foreach (string category in categories)
        {
            Button button = new Button();
            button.text = category;
            button.AddToClassList("airport-subcategory-button");

            string selectedCategory = category;
            button.clicked += () =>
            {
                SelectAirportMenuCategory(selectedCategory);
            };

            airportSubcategoryButtons.Add(category, button);
            airportSubcategoryList.Add(button);
        }

        airportSubcategoryList.style.display =
            DisplayStyle.None;
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
            if (pair.Value == null ||
                pair.Key == BuildCategory.Floor ||
                pair.Key == BuildCategory.Wall)
                continue;

            BuildCategory category = pair.Key;

            pair.Value.clicked += () =>
            {
                SelectCategory(category);
            };
        }

        if (categoryButtons[BuildCategory.Floor] != null)
            categoryButtons[BuildCategory.Floor].clicked += ToggleFloorSubcategories;

        if (floorFoundationButton != null)
            floorFoundationButton.clicked += () => SelectCategory(BuildCategory.Floor);

        if (categoryButtons[BuildCategory.Wall] != null)
            categoryButtons[BuildCategory.Wall].clicked += ToggleWallSubcategories;

        if (wallBuildingButton != null)
            wallBuildingButton.clicked += () => SelectCategory(BuildCategory.Wall);
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

        // Open the build menu with category submenus collapsed.
        if (floorSubcategoryList != null)
            floorSubcategoryList.style.display = DisplayStyle.None;

        if (wallSubcategoryList != null)
            wallSubcategoryList.style.display = DisplayStyle.None;
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

    private void ToggleFloorSubcategories()
    {
        bool shouldShow = floorSubcategoryList != null &&
                          floorSubcategoryList.resolvedStyle.display == DisplayStyle.None;

        SelectCategory(BuildCategory.Floor);

        if (floorSubcategoryList != null)
        {
            floorSubcategoryList.style.display =
                shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void ToggleWallSubcategories()
    {
        bool shouldShow = wallSubcategoryList != null &&
                          wallSubcategoryList.resolvedStyle.display == DisplayStyle.None;

        SelectCategory(BuildCategory.Wall);

        if (wallSubcategoryList != null)
        {
            wallSubcategoryList.style.display =
                shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void SelectCategory(BuildCategory category)
    {
        currentCategory = category;

        if (category == BuildCategory.Airport)
        {
            if (string.IsNullOrEmpty(currentAirportMenuCategory) &&
                airportSubcategoryButtons.Count > 0)
            {
                foreach (string menuCategory in airportSubcategoryButtons.Keys)
                {
                    currentAirportMenuCategory = menuCategory;
                    break;
                }
            }

            if (airportSubcategoryList != null)
            {
                airportSubcategoryList.style.display =
                    airportSubcategoryButtons.Count > 0
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
            }
        }
        else
        {
            if (airportSubcategoryList != null)
                airportSubcategoryList.style.display = DisplayStyle.None;
        }

        if (floorSubcategoryList != null)
        {
            floorSubcategoryList.style.display =
                category == BuildCategory.Floor
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }

        if (wallSubcategoryList != null)
        {
            wallSubcategoryList.style.display =
                category == BuildCategory.Wall
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }

        UpdateCategoryButtonStates();
        UpdateAirportSubcategoryButtonStates();
        PopulateBuildableList();
    }

    private void SelectAirportMenuCategory(string menuCategory)
    {
        currentCategory = BuildCategory.Airport;
        currentAirportMenuCategory = menuCategory;

        if (floorSubcategoryList != null)
            floorSubcategoryList.style.display = DisplayStyle.None;

        if (wallSubcategoryList != null)
            wallSubcategoryList.style.display = DisplayStyle.None;

        if (airportSubcategoryList != null)
            airportSubcategoryList.style.display = DisplayStyle.Flex;

        UpdateCategoryButtonStates();
        UpdateAirportSubcategoryButtonStates();
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

    private void UpdateAirportSubcategoryButtonStates()
    {
        foreach (KeyValuePair<string, Button> pair in airportSubcategoryButtons)
        {
            if (pair.Value == null)
                continue;

            if (currentCategory == BuildCategory.Airport &&
                string.Equals(
                    pair.Key,
                    currentAirportMenuCategory,
                    StringComparison.Ordinal))
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
            if (definition == null)
                continue;

            if (!string.IsNullOrEmpty(buildSearchQuery))
            {
                if (!MatchesBuildSearch(definition, buildSearchQuery))
                    continue;
            }
            else
            {
                if (definition.category != currentCategory)
                    continue;

                if (currentCategory == BuildCategory.Airport &&
                    !string.IsNullOrEmpty(currentAirportMenuCategory) &&
                    !string.Equals(
                        definition.menuCategory,
                        currentAirportMenuCategory,
                        StringComparison.Ordinal))
                {
                    continue;
                }
            }

            CreateBuildableButton(definition);
        }
    }

    private bool MatchesBuildSearch(BuildableDefinition definition, string query)
    {
        return ContainsSearchText(definition.displayName, query) ||
               ContainsSearchText(definition.id, query) ||
               ContainsSearchText(definition.menuCategory, query) ||
               ContainsSearchText(definition.category.ToString(), query);
    }

    private static bool ContainsSearchText(string value, string query)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void CreateBuildableButton(BuildableDefinition definition)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("buildable-card");

        Image iconImage = new Image();
        iconImage.AddToClassList("buildable-icon");
        iconImage.scaleMode = ScaleMode.ScaleToFit;
        iconImage.image = definition.icon;
        card.Add(iconImage);

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
