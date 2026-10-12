using UnityEngine;
using UnityEngine.InputSystem;

public class CellBuildingMoveController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridOccupancy gridOccupancy;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private BuildController buildController;

    [Header("Movement")]
    [SerializeField] private LayerMask groundLayer;

    [Header("Input")]
    [SerializeField] private float dragThreshold = 8f;

    private GameObject movingObject;
    private PlacedBuilding movingBuilding;

    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private BuildableDefinition definition;

    private Vector2 mouseDownPosition;

    private Vector2Int originalGridPosition;
    private Vector3 originalWorldPosition;
    private Quaternion originalRotation;

    private Vector2Int currentGridPosition;

    private int currentWidth;
    private int currentDepth;
    private int currentRotation;

    private bool mouseDownOnCellBuilding;
    private bool isDragging;

    public bool IsMoving => isDragging;

    private void Awake()
    {
        if (mainCamera == null)
        {
            mainCamera =
                Camera.main;
        }

        if (gridSurface == null &&
            gridManager != null)
        {
            gridSurface =
                gridManager.GetComponent<GridSurface>();
        }
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;

        if (mouse == null)
            return;

        if (buildController != null &&
            buildController.IsBuilding)
        {
            return;
        }

        if (mouseDownOnCellBuilding &&
            isDragging)
        {
            if (mouse.rightButton.wasPressedThisFrame ||
                (keyboard != null &&
                 keyboard.escapeKey.wasPressedThisFrame))
            {
                CancelMove();
                return;
            }

            if (keyboard != null &&
                keyboard.rKey.wasPressedThisFrame)
            {
                RotateMove();
            }
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            HandleMouseDown(mouse);
        }

        if (mouseDownOnCellBuilding &&
            mouse.leftButton.isPressed)
        {
            HandleDragging(mouse);
        }

        if (mouseDownOnCellBuilding &&
            mouse.leftButton.wasReleasedThisFrame)
        {
            HandleMouseRelease();
        }
    }

    private void HandleMouseDown(Mouse mouse)
    {
        if (IsPointerOverUI())
            return;

        Vector2 mousePosition =
            mouse.position.ReadValue();

        GameObject building =
            GetCellBuildingUnderMouse(
                mousePosition
            );

        if (building == null)
        {
            return;
        }

        movingObject =
            building;

        movingBuilding =
            building.GetComponent<PlacedBuilding>();

        if (movingBuilding == null)
        {
            CancelMoveState();
            return;
        }

        definition =
            movingBuilding.Definition;

        if (definition == null)
        {
            CancelMoveState();
            return;
        }

        if (definition.placementType !=
            PlacementType.Cell)
        {
            CancelMoveState();
            return;
        }

        mouseDownPosition =
            mousePosition;

        originalGridPosition =
            movingBuilding.Origin;

        originalWorldPosition =
            movingObject.transform.position;

        originalRotation =
            movingObject.transform.rotation;

        currentGridPosition =
            originalGridPosition;

        currentRotation =
            movingBuilding.Rotation;

        GetRotatedFootprint(
            out currentWidth,
            out currentDepth
        );

        mouseDownOnCellBuilding = true;
        isDragging = false;
    }

    private void HandleDragging(Mouse mouse)
    {
        Vector2 mousePosition =
            mouse.position.ReadValue();

        if (!isDragging)
        {
            float distance =
                Vector2.Distance(
                    mouseDownPosition,
                    mousePosition
                );

            if (distance < dragThreshold)
                return;

            BeginMove();
        }

        if (!TryGetGridPosition(
                mousePosition,
                out Vector2Int gridPosition))
        {
            return;
        }

        currentGridPosition =
            gridPosition;

        UpdatePreview();
    }

    private void BeginMove()
    {
        if (movingObject == null ||
            movingBuilding == null ||
            definition == null)
        {
            CancelMoveState();
            return;
        }

        isDragging = true;

        previewObject =
            Instantiate(
                definition.prefab
            );

        placementPreview =
            previewObject.AddComponent<PlacementPreview>();

        placementPreview.Initialize();

        movingObject.SetActive(false);

        ApplyPreviewTransform();
        UpdatePreviewValidity();
    }

    private void RotateMove()
    {
        if (!isDragging ||
            definition == null)
        {
            return;
        }

        if (!definition.canRotate)
        {
            return;
        }

        currentRotation += 90;

        if (currentRotation >= 360)
        {
            currentRotation = 0;
        }

        GetRotatedFootprint(
            out currentWidth,
            out currentDepth
        );

        ApplyPreviewTransform();
        UpdatePreviewValidity();

        Debug.Log(
            $"Rotated moving building: " +
            $"{definition.displayName} " +
            $"to {currentRotation} degrees."
        );
    }

    private void UpdatePreview()
    {
        if (previewObject == null)
            return;

        ApplyPreviewTransform();
        UpdatePreviewValidity();
    }

    private void ApplyPreviewTransform()
    {
        if (previewObject == null)
            return;

        Vector3 position =
            gridManager.GridToWorld(
                currentGridPosition
            );

        position +=
            new Vector3(
                currentWidth *
                    gridManager.CellSize *
                    0.5f,
                0f,
                currentDepth *
                    gridManager.CellSize *
                    0.5f
            );

        previewObject.transform.position =
            position;

        previewObject.transform.rotation =
            Quaternion.Euler(
                0f,
                currentRotation,
                0f
            );
    }

    private void UpdatePreviewValidity()
    {
        if (placementPreview == null)
            return;

        bool inside =
            IsInsideGrid(
                currentGridPosition,
                currentWidth,
                currentDepth
            );

        bool free =
            gridOccupancy != null &&
            gridOccupancy.CanOccupy(
                currentGridPosition,
                currentWidth,
                currentDepth,
                movingObject
            );

        bool correctSurface =
            HasRequiredSurface(
                currentGridPosition,
                currentWidth,
                currentDepth
            );

        placementPreview.SetValid(
            inside &&
            free &&
            correctSurface
        );
    }

    private bool HasRequiredSurface(
        Vector2Int origin,
        int width,
        int depth)
    {
        if (definition == null)
            return false;

        if (definition.requiredSurface ==
            BuildSurface.Any)
        {
            return true;
        }

        if (gridSurface == null)
            return false;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell =
                    origin +
                    new Vector2Int(
                        x,
                        z
                    );

                if (!gridSurface.IsSurface(
                        cell,
                        definition.requiredSurface))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool IsInsideGrid(
        Vector2Int origin,
        int width,
        int depth)
    {
        if (gridManager == null)
            return false;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell =
                    origin +
                    new Vector2Int(
                        x,
                        z
                    );

                if (!gridManager.IsInsideGrid(
                        cell))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void GetRotatedFootprint(
        out int width,
        out int depth)
    {
        width =
            definition.width;

        depth =
            definition.depth;

        if (currentRotation == 90 ||
            currentRotation == 270)
        {
            int temp = width;

            width = depth;
            depth = temp;
        }
    }

    private void HandleMouseRelease()
    {
        if (!isDragging)
        {
            CancelMoveState();
            return;
        }

        FinishMove();
    }

    private void FinishMove()
    {
        bool valid =
            IsInsideGrid(
                currentGridPosition,
                currentWidth,
                currentDepth
            ) &&
            gridOccupancy != null &&
            gridOccupancy.CanOccupy(
                currentGridPosition,
                currentWidth,
                currentDepth,
                movingObject
            ) &&
            HasRequiredSurface(
                currentGridPosition,
                currentWidth,
                currentDepth
            );

        if (!valid)
        {
            CancelMove();
            return;
        }

        MoveBuilding();
    }

    private void MoveBuilding()
    {
        if (movingObject == null ||
            movingBuilding == null ||
            previewObject == null)
        {
            CancelMove();
            return;
        }

        gridOccupancy.ReleaseBuilding(
            movingObject
        );

        movingObject.transform.position =
            previewObject.transform.position;

        movingObject.transform.rotation =
            previewObject.transform.rotation;

        gridOccupancy.Occupy(
            currentGridPosition,
            currentWidth,
            currentDepth,
            movingObject
        );

        movingBuilding.Initialize(
            definition,
            currentGridPosition,
            currentWidth,
            currentDepth,
            currentRotation
        );

        BuildingSelectionController selectionController =
            FindFirstObjectByType<BuildingSelectionController>();

        if (selectionController != null)
        {
            selectionController.RefreshSelectionHighlights();
        }

        DestroyPreview();

        movingObject.SetActive(true);

        Debug.Log(
            $"Moved building: " +
            $"{definition.displayName}"
        );

        ClearMoveState();
    }

    private void CancelMove()
    {
        if (movingObject != null)
        {
            movingObject.transform.position =
                originalWorldPosition;

            movingObject.transform.rotation =
                originalRotation;

            movingObject.SetActive(true);
        }

        DestroyPreview();

        Debug.Log(
            "Building move cancelled."
        );

        ClearMoveState();
    }

    private void CancelMoveState()
    {
        DestroyPreview();

        if (movingObject != null)
        {
            movingObject.SetActive(true);
        }

        ClearMoveState();
    }

    private void DestroyPreview()
    {
        if (previewObject != null)
        {
            Destroy(
                previewObject
            );
        }

        previewObject = null;
        placementPreview = null;
    }

    private void ClearMoveState()
    {
        movingObject = null;
        movingBuilding = null;
        definition = null;

        mouseDownOnCellBuilding = false;
        isDragging = false;

        currentGridPosition =
            Vector2Int.zero;

        originalGridPosition =
            Vector2Int.zero;

        currentWidth = 0;
        currentDepth = 0;
        currentRotation = 0;
    }

    private GameObject GetCellBuildingUnderMouse(
        Vector2 screenPosition)
    {
        if (mainCamera == null)
            return null;

        Ray ray =
            mainCamera.ScreenPointToRay(
                screenPosition
            );

        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                1000f
            );

        System.Array.Sort(
            hits,
            (a, b) =>
                a.distance.CompareTo(
                    b.distance
                )
        );

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
                continue;

            PlacedBuilding building =
                hit.collider.GetComponentInParent<PlacedBuilding>();

            if (building == null)
                continue;

            if (building.Definition == null)
                continue;

            if (building.Definition.placementType !=
                PlacementType.Cell)
            {
                continue;
            }

            return building.gameObject;
        }

        return null;
    }

    public bool HasCellBuildingUnderMouse(
        Vector2 screenPosition)
    {
        return GetCellBuildingUnderMouse(
            screenPosition
        ) != null;
    }

    private bool TryGetGridPosition(
        Vector2 screenPosition,
        out Vector2Int gridPosition)
    {
        gridPosition =
            Vector2Int.zero;

        if (mainCamera == null ||
            gridManager == null)
        {
            return false;
        }

        Ray ray =
            mainCamera.ScreenPointToRay(
                screenPosition
            );

        Plane gridPlane =
            new Plane(
                Vector3.up,
                gridManager.transform.position
            );

        if (!gridPlane.Raycast(
                ray,
                out float distance))
        {
            return false;
        }

        Vector3 worldPoint =
            ray.GetPoint(distance);

        gridPosition =
            gridManager.WorldToGrid(
                worldPoint
            );

        return true;
    }

    private bool IsPointerOverUI()
    {
        return false;
    }
}