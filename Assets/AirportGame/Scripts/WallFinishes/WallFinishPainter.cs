using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class WallFinishPainter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private WallFinishRenderer wallFinishRenderer;
    [SerializeField] private BuildController buildController;
    [SerializeField] private UIDocument uiDocument;

    private VisualElement buildWindow;
    private WallFinishDefinition selectedFinish;
    private bool isActive;
    private bool isDragging;

    private Vector2Int dragStartCell;
    private Vector2Int dragCurrentCell;

    private PlacedBuilding clickedWall;
    private int clickedFaceSign = 1;

    private readonly List<PlacedBuilding> previewedWalls =
        new List<PlacedBuilding>();

    private readonly List<PlacedBuilding> currentWalls =
        new List<PlacedBuilding>();

    private readonly Dictionary<PlacedBuilding, int> previewSideSigns =
        new Dictionary<PlacedBuilding, int>();

    public bool IsActive => isActive;

    private void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (wallFinishRenderer == null)
            wallFinishRenderer = FindFirstObjectByType<WallFinishRenderer>();

        if (buildController == null)
            buildController = FindFirstObjectByType<BuildController>();

        if (uiDocument == null)
            uiDocument = FindFirstObjectByType<UIDocument>();

        if (uiDocument != null)
            buildWindow = uiDocument.rootVisualElement.Q<VisualElement>("build-window");
    }

    private void OnDisable()
    {
        CancelPainting();
    }

    private void Update()
    {
        if (!isActive)
            return;

        if (buildWindow != null &&
            buildWindow.resolvedStyle.display == DisplayStyle.None)
        {
            CancelPainting();
            return;
        }

        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;

        if (mouse == null || keyboard == null)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame ||
            mouse.rightButton.wasPressedThisFrame)
        {
            CancelPainting();
            return;
        }

        if (!isDragging)
            UpdateHoverPreview();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (IsPointerOverBuildUI())
                return;

            if (!TryGetGridCell(mouse.position.ReadValue(), out dragStartCell))
                return;

            dragCurrentCell = dragStartCell;
            clickedWall = FindWallUnderPointer(
                mouse.position.ReadValue(),
                out RaycastHit clickedHit);

            clickedFaceSign = clickedWall != null
                ? GetFaceSignFromHit(clickedWall, clickedHit)
                : 1;

            isDragging = true;
            UpdateDragPreview();
        }

        if (isDragging && mouse.leftButton.isPressed)
        {
            if (!IsPointerOverBuildUI() &&
                TryGetGridCell(mouse.position.ReadValue(), out Vector2Int currentCell))
            {
                dragCurrentCell = currentCell;
            }

            UpdateDragPreview();
        }

        if (mouse.leftButton.wasReleasedThisFrame && isDragging)
        {
            if (!IsPointerOverBuildUI() &&
                TryGetGridCell(mouse.position.ReadValue(), out Vector2Int releaseCell))
            {
                dragCurrentCell = releaseCell;
                UpdateDragPreview();
                ApplyCurrentSelection();
            }

            HideAllPreviews();
            isDragging = false;
            clickedWall = null;
            previewSideSigns.Clear();
        }
    }

    public void BeginPainting(WallFinishDefinition finish)
    {
        if (wallFinishRenderer == null ||
            wallFinishRenderer.Catalogue == null ||
            wallFinishRenderer.Catalogue.SharedRenderMaterial == null)
        {
            Debug.LogError(
                "WallFinishPainter: Add a WallFinishRenderer to the scene and assign its generated WallFinishCatalogue.",
                this);
            return;
        }

        if (finish == null || finish.material == null)
        {
            Debug.LogWarning(
                "WallFinishPainter: Selected finish or its material is missing.",
                this);
            return;
        }

        if (buildController != null && buildController.IsBuilding)
            buildController.CancelBuilding();

        CancelPreviewOnly();
        selectedFinish = finish;
        isActive = true;
        isDragging = false;
    }

    public void BeginErasing()
    {
        if (wallFinishRenderer == null ||
            wallFinishRenderer.Catalogue == null ||
            wallFinishRenderer.Catalogue.SharedRenderMaterial == null)
        {
            Debug.LogError(
                "WallFinishPainter: Add a WallFinishRenderer to the scene and assign its generated WallFinishCatalogue.",
                this);
            return;
        }

        if (buildController != null && buildController.IsBuilding)
            buildController.CancelBuilding();

        CancelPreviewOnly();
        selectedFinish = null;
        isActive = true;
        isDragging = false;
    }

    public void CancelPainting()
    {
        CancelPreviewOnly();
        isActive = false;
        isDragging = false;
        clickedWall = null;
        selectedFinish = null;
        previewSideSigns.Clear();
    }

    private void UpdateHoverPreview()
    {
        if (IsPointerOverBuildUI())
        {
            HideAllPreviews();
            return;
        }

        PlacedBuilding wall = FindWallUnderPointer(
            Mouse.current.position.ReadValue(),
            out RaycastHit hit);

        if (wall == null)
        {
            HideAllPreviews();
            return;
        }

        int sideSign = GetFaceSignFromHit(wall, hit);

        HideAllPreviews();
        wallFinishRenderer?.ShowPreview(
            wall,
            sideSign,
            selectedFinish,
            selectedFinish == null);

        previewedWalls.Add(wall);
    }

    private void UpdateDragPreview()
    {
        HideAllPreviews();
        FindWallsInSelection(dragStartCell, dragCurrentCell, currentWalls);

        foreach (PlacedBuilding wall in currentWalls)
        {
            if (wall == null)
                continue;

            int sideSign =
                wall == clickedWall
                    ? clickedFaceSign
                    : wallFinishRenderer != null
                        ? wallFinishRenderer.GetVisibleSideSign(
                            wall,
                            mainCamera.transform.position)
                        : 1;

            previewSideSigns[wall] = sideSign;

            wallFinishRenderer?.ShowPreview(
                wall,
                sideSign,
                selectedFinish,
                selectedFinish == null);

            previewedWalls.Add(wall);
        }
    }

    private void ApplyCurrentSelection()
    {
        foreach (PlacedBuilding wall in currentWalls)
        {
            if (wall == null)
                continue;

            if (!previewSideSigns.TryGetValue(wall, out int sideSign))
            {
                sideSign = wallFinishRenderer != null
                    ? wallFinishRenderer.GetVisibleSideSign(
                        wall,
                        mainCamera.transform.position)
                    : 1;
            }

            wallFinishRenderer?.SetFinish(
                wall,
                sideSign,
                selectedFinish);
        }
    }

    private void FindWallsInSelection(
        Vector2Int from,
        Vector2Int to,
        List<PlacedBuilding> results)
    {
        results.Clear();

        int minX = Mathf.Min(from.x, to.x);
        int maxX = Mathf.Max(from.x, to.x);
        int minZ = Mathf.Min(from.y, to.y);
        int maxZ = Mathf.Max(from.y, to.y);

        IReadOnlyList<PlacedBuilding> activeBuildings =
            PlacedBuilding.ActiveBuildings;

        foreach (PlacedBuilding building in activeBuildings)
        {
            if (building == null ||
                building.Definition == null ||
                building.Definition.category != BuildCategory.Wall ||
                !building.IsEdgeBuilding)
            {
                continue;
            }

            if (DoesEdgeBuildingOverlapSelection(
                    building,
                    minX,
                    maxX,
                    minZ,
                    maxZ))
            {
                results.Add(building);
            }
        }
    }

    private static bool DoesEdgeBuildingOverlapSelection(
        PlacedBuilding building,
        int minX,
        int maxX,
        int minZ,
        int maxZ)
    {
        int edgeLength = Mathf.Max(1, building.EdgeLength);

        for (int i = 0; i < edgeLength; i++)
        {
            Vector2Int edgeCell = building.Origin;

            if (building.EdgeDirection == EdgeDirection.Horizontal)
            {
                edgeCell.x += i;

                if (IsCellInsideSelection(
                        edgeCell.x, edgeCell.y,
                        minX, maxX, minZ, maxZ))
                    return true;

                Vector2Int oppositeSide = edgeCell + Vector2Int.down;

                if (IsCellInsideSelection(
                        oppositeSide.x, oppositeSide.y,
                        minX, maxX, minZ, maxZ))
                    return true;
            }
            else
            {
                edgeCell.y += i;

                if (IsCellInsideSelection(
                        edgeCell.x, edgeCell.y,
                        minX, maxX, minZ, maxZ))
                    return true;

                Vector2Int oppositeSide = edgeCell + Vector2Int.left;

                if (IsCellInsideSelection(
                        oppositeSide.x, oppositeSide.y,
                        minX, maxX, minZ, maxZ))
                    return true;
            }
        }

        return false;
    }

    private static bool IsCellInsideSelection(
        int x,
        int z,
        int minX,
        int maxX,
        int minZ,
        int maxZ)
    {
        return x >= minX && x <= maxX &&
               z >= minZ && z <= maxZ;
    }

    private PlacedBuilding FindWallUnderPointer(
        Vector2 screenPosition,
        out RaycastHit wallHit)
    {
        wallHit = default;

        if (mainCamera == null)
            return null;

        Ray ray = mainCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);

        System.Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            PlacedBuilding building =
                hit.collider.GetComponentInParent<PlacedBuilding>();

            if (building == null ||
                building.Definition == null ||
                building.Definition.category != BuildCategory.Wall ||
                !building.IsEdgeBuilding)
            {
                continue;
            }

            wallHit = hit;
            return building;
        }

        return null;
    }

    private int GetFaceSignFromHit(
        PlacedBuilding wall,
        RaycastHit hit)
    {
        float sideDot = Vector3.Dot(
            hit.normal,
            wall.transform.forward);

        if (Mathf.Abs(sideDot) < 0.5f && wallFinishRenderer != null)
        {
            return wallFinishRenderer.GetVisibleSideSign(
                wall,
                mainCamera.transform.position);
        }

        return sideDot >= 0f ? 1 : -1;
    }

    private bool TryGetGridCell(
        Vector2 screenPosition,
        out Vector2Int cell)
    {
        cell = default;

        if (mainCamera == null || gridManager == null)
            return false;

        Ray ray = mainCamera.ScreenPointToRay(screenPosition);
        Plane gridPlane = new Plane(
            Vector3.up,
            new Vector3(0f, gridManager.transform.position.y, 0f));

        if (!gridPlane.Raycast(ray, out float distance))
            return false;

        Vector3 hitPoint = ray.GetPoint(distance);
        cell = gridManager.WorldToGrid(hitPoint);

        return gridManager.IsInsideGrid(cell);
    }

    private void HideAllPreviews()
    {
        foreach (PlacedBuilding wall in previewedWalls)
        {
            if (wall != null)
                wallFinishRenderer?.HidePreview(wall);
        }

        previewedWalls.Clear();
    }

    private void CancelPreviewOnly()
    {
        HideAllPreviews();
        currentWalls.Clear();
        previewSideSigns.Clear();
    }

    private bool IsPointerOverBuildUI()
    {
        return buildWindow != null &&
               buildWindow.panel != null &&
               buildWindow.worldBound.Contains(
                   Mouse.current.position.ReadValue());
    }
}
