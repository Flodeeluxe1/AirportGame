
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class BuildController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridOccupancy gridOccupancy;
    [SerializeField] private EdgeOccupancy edgeOccupancy;
    [SerializeField] private FoundationTilemapController foundationTilemapController;
    [SerializeField] private UIDocument uiDocument;

    [Header("Placement")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Testing")]
    [SerializeField] private BuildableDefinition testBuildable;

    [SerializeField] private BuildableDefinition automaticFoundationWall;

    private BuildableDefinition currentDefinition;

    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private bool isBuilding;

    public bool IsBuilding => isBuilding;

    private IPlacementHandler currentHandler;

    private CellPlacementHandler cellHandler;
    private EdgePlacementHandler edgeHandler;
    private AreaPlacementHandler areaHandler;
    private DoorPlacementHandler doorHandler;
    private WallMountedPlacementHandler wallMountedHandler;
    private RoadPlacementHandler roadHandler;
    private TaxiwayPlacementHandler taxiwayHandler;

    private VisualElement buildWindow;

    private void Awake()
    {
        if (uiDocument == null)
        {
            uiDocument =
                FindFirstObjectByType<UIDocument>();
        }

        if (uiDocument != null)
        {
            buildWindow =
                uiDocument.rootVisualElement.Q<VisualElement>(
                    "build-window"
                );
        }

        cellHandler = new CellPlacementHandler();
        edgeHandler = new EdgePlacementHandler();
        areaHandler = new AreaPlacementHandler();
        doorHandler = new DoorPlacementHandler();
        wallMountedHandler = new WallMountedPlacementHandler();
        roadHandler = new RoadPlacementHandler();
        taxiwayHandler = new TaxiwayPlacementHandler();

        cellHandler.Initialize(
            this, gridManager, gridOccupancy, edgeOccupancy
        );

        edgeHandler.Initialize(
            this, gridManager, gridOccupancy, edgeOccupancy
        );

        areaHandler.Initialize(
            this, gridManager, gridOccupancy, edgeOccupancy
        );

        areaHandler.SetFoundationTilemapController(
            foundationTilemapController
        );

        areaHandler.SetAutomaticWallDefinition(
            automaticFoundationWall
        );

        doorHandler.Initialize(
            this, gridManager, gridOccupancy, edgeOccupancy
        );

        wallMountedHandler.Initialize(
            this, gridManager, gridOccupancy, edgeOccupancy
        );

        roadHandler.Initialize(
            this, gridManager, gridOccupancy, edgeOccupancy
        );

        taxiwayHandler.Initialize(
            this, gridManager, gridOccupancy, edgeOccupancy
        );
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard == null || mouse == null)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame)
        {
            StartBuilding(testBuildable);
        }

        if (!isBuilding)
            return;

        if (currentDefinition != null &&
            currentDefinition.isWallMounted)
        {
            UpdateWallMountedPlacement();
        }
        else if (currentDefinition != null &&
                 currentDefinition.requiresExistingWall)
        {
            UpdateDoorPlacement();
        }
        else
        {
            UpdatePlacementPosition();
        }

        if (keyboard.rKey.wasPressedThisFrame)
        {
            currentHandler?.Rotate();
        }

        if (currentDefinition.category == BuildCategory.Taxiways)
        {
            HandleTaxiwayInput(mouse);
        }
        else if (currentDefinition.placementType == PlacementType.Area)
        {
            HandleAreaInput(mouse);
        }
        else if (currentDefinition.placementType == PlacementType.Edge)
        {
            if (currentDefinition.isWallMounted)
            {
                HandleWallMountedInput(mouse);
            }
            else if (currentDefinition.requiresExistingWall)
            {
                HandleDoorInput(mouse);
            }
            else
            {
                HandleEdgeInput(mouse);
            }
        }
        else if (currentDefinition.category == BuildCategory.Roads)
        {
            HandleRoadInput(mouse);
        }
        else
        {
            if (mouse.leftButton.wasPressedThisFrame &&
                !IsPointerOverBuildUI())
            {
                currentHandler?.TryPlace();
            }
        }

        if (mouse.rightButton.wasPressedThisFrame ||
            keyboard.escapeKey.wasPressedThisFrame)
        {
            CancelBuilding();
        }
    }

    private void UpdateWallMountedPlacement()
    {
        if (mainCamera == null ||
            wallMountedHandler == null)
        {
            return;
        }

        Ray mouseRay =
            mainCamera.ScreenPointToRay(GetMousePosition());

        wallMountedHandler.UpdatePlacement(mouseRay);
    }

    private void UpdateDoorPlacement()
    {
        if (mainCamera == null ||
            doorHandler == null)
        {
            return;
        }

        Ray mouseRay =
            mainCamera.ScreenPointToRay(GetMousePosition());

        doorHandler.UpdatePlacement(mouseRay);
    }

    public void StartBuilding(
        BuildableDefinition definition)
    {
        if (definition == null)
        {
            Debug.LogWarning(
                "BuildController: No BuildableDefinition was provided."
            );

            return;
        }

        if (definition.prefab == null)
        {
            Debug.LogError(
                $"BuildController: Buildable '{definition.displayName}' " +
                "does not have a prefab assigned."
            );

            return;
        }

        CancelBuilding();

        currentDefinition = definition;
        isBuilding = true;

        previewObject = Instantiate(
            currentDefinition.prefab
        );

        placementPreview =
            previewObject.AddComponent<PlacementPreview>();

        placementPreview.Initialize();

        currentHandler = GetHandler(currentDefinition);

        currentHandler.BeginPlacement(
            currentDefinition,
            previewObject,
            placementPreview
        );
    }

    private IPlacementHandler GetHandler(
        BuildableDefinition definition)
    {
        if (definition == null)
            return null;

        if (definition.category == BuildCategory.Taxiways)
            return taxiwayHandler;

        if (definition.placementType == PlacementType.Area)
            return areaHandler;

        if (definition.placementType == PlacementType.Edge)
        {
            if (definition.isWallMounted)
                return wallMountedHandler;

            if (definition.requiresExistingWall)
                return doorHandler;

            return edgeHandler;
        }

        if (definition.category == BuildCategory.Roads)
            return roadHandler;

        return cellHandler;
    }

    private void UpdatePlacementPosition()
    {
        if (currentHandler == null ||
            previewObject == null ||
            mainCamera == null)
        {
            return;
        }

        Ray ray =
            mainCamera.ScreenPointToRay(GetMousePosition());

        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                1000f,
                groundLayer))
        {
            return;
        }

        currentHandler.UpdatePlacement(hit.point);
    }

    private Vector2 GetMousePosition()
    {
        if (Mouse.current == null)
            return Vector2.zero;

        return Mouse.current.position.ReadValue();
    }

    private void HandleAreaInput(Mouse mouse)
    {
        if (IsPointerOverBuildUI())
            return;

        if (mouse.leftButton.wasPressedThisFrame)
            areaHandler.BeginArea();

        if (mouse.leftButton.wasReleasedThisFrame)
            areaHandler.EndArea();
    }

    private void HandleEdgeInput(Mouse mouse)
    {
        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (IsPointerOverBuildUI())
                return;

            edgeHandler.BeginWall();
        }

        if (mouse.leftButton.wasReleasedThisFrame)
            edgeHandler.EndWall();
    }

    private void HandleDoorInput(Mouse mouse)
    {
        if (mouse.leftButton.wasPressedThisFrame &&
            !IsPointerOverBuildUI())
        {
            doorHandler.TryPlace();
        }
    }

    private void HandleWallMountedInput(Mouse mouse)
    {
        if (mouse.leftButton.wasPressedThisFrame &&
            !IsPointerOverBuildUI())
        {
            wallMountedHandler.TryPlace();
        }
    }

    private void HandleRoadInput(Mouse mouse)
    {
        if (mouse.leftButton.wasPressedThisFrame &&
            !IsPointerOverBuildUI())
        {
            roadHandler.BeginRoad();
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            roadHandler.EndRoad();
        }
    }

    private void HandleTaxiwayInput(Mouse mouse)
    {
        if (mouse.leftButton.wasPressedThisFrame &&
            !IsPointerOverBuildUI())
        {
            taxiwayHandler.BeginTaxiway();
        }

        // Always handle release, even if the cursor is
        // now over the build UI.
        if (mouse.leftButton.wasReleasedThisFrame)
        {
            taxiwayHandler.EndTaxiway();
        }
    }

    private bool IsPointerOverBuildUI()
    {
        if (buildWindow == null ||
            buildWindow.panel == null)
        {
            return false;
        }

        Vector2 mousePosition = GetMousePosition();

        return buildWindow.worldBound.Contains(mousePosition);
    }

    public void CancelBuilding()
    {
        if (currentHandler != null)
        {
            currentHandler.Cancel();
            currentHandler.Reset();
        }

        if (previewObject != null)
            Destroy(previewObject);

        previewObject = null;
        placementPreview = null;

        currentDefinition = null;
        currentHandler = null;

        isBuilding = false;
    }
}