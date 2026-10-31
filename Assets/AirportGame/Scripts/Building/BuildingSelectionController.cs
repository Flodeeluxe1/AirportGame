
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BuildingSelectionController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridOccupancy gridOccupancy;
    [SerializeField] private EdgeOccupancy edgeOccupancy;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private BuildController buildController;
    [SerializeField] private CellBuildingMoveController moveController;

    [Header("Foundation Selection")]
    [SerializeField] private FoundationTilemapController foundationTilemapController;

    [Header("Road Selection")]
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private RoadTilemapController roadTilemapController;
    [SerializeField] private RoadModuleRuleManager roadModuleRuleManager;

    [Header("Taxiway Selection")]
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;
    [SerializeField] private TaxiwayPrefabRuleManager taxiwayPrefabRuleManager;

    [Header("Selection Highlight")]
    [SerializeField]
    private Color highlightColor = new Color(0.1f, 0.85f, 1f, 0.45f);

    [Header("Wall Highlight")]
    [SerializeField]
    private Color wallHighlightColor = new Color(0.1f, 0.85f, 1f, 1f);

    [Range(0f, 1f)]
    [SerializeField] private float wallHighlightStrength = 0.75f;

    [Header("Selection Box")]
    [SerializeField]
    private Color selectionBoxColor = new Color(0.1f, 0.85f, 1f, 0.15f);

    [SerializeField]
    private Color selectionBorderColor = new Color(0.1f, 0.85f, 1f, 0.9f);

    private readonly List<GameObject> selectedObjects =
        new List<GameObject>();

    private readonly HashSet<Vector2Int> selectedRoadCells =
        new HashSet<Vector2Int>();

    private readonly HashSet<Vector2Int> selectedTaxiwayCells =
        new HashSet<Vector2Int>();

    private readonly List<GameObject> highlights =
        new List<GameObject>();

    private readonly Dictionary<Renderer, MaterialPropertyBlock>
        originalWallPropertyBlocks =
            new Dictionary<Renderer, MaterialPropertyBlock>();

    private Material highlightMaterial;
    private Material selectionBoxMaterial;

    private GameObject selectionBox;
    private LineRenderer selectionBorder;

    private bool isDragging;
    private bool hasValidDragStart;
    private bool mouseDownOnCellBuilding;
    private bool deleteSelectionMode;

    private Vector2 dragStartScreenPosition;
    private Vector2 dragCurrentScreenPosition;

    private Vector2Int dragStartGridPosition;
    private Vector2Int dragCurrentGridPosition;

    private static readonly int BaseColorId =
        Shader.PropertyToID("_BaseColor");

    private static readonly int ColorId =
        Shader.PropertyToID("_Color");

    private const float DRAG_THRESHOLD = 8f;
    private const float SELECTION_HEIGHT_OFFSET = 0.05f;
    private const float SELECTION_BORDER_WIDTH = 0.025f;

    private void Awake()
    {
        if (gridSurface == null && gridManager != null)
            gridSurface = gridManager.GetComponent<GridSurface>();

        if (moveController == null)
            moveController = FindFirstObjectByType<CellBuildingMoveController>();

        if (foundationTilemapController == null)
        {
            foundationTilemapController =
                FindFirstObjectByType<FoundationTilemapController>();
        }

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (roadModuleRuleManager == null)
        {
            roadModuleRuleManager =
                FindFirstObjectByType<RoadModuleRuleManager>();
        }

        if (roadTilemapController == null)
            roadTilemapController = FindFirstObjectByType<RoadTilemapController>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindFirstObjectByType<TaxiwayNetwork>();

        if (taxiwayPrefabRuleManager == null)
        {
            taxiwayPrefabRuleManager =
                FindFirstObjectByType<TaxiwayPrefabRuleManager>();
        }

        CreateHighlightMaterial();
        CreateSelectionBoxMaterial();
        CreateSelectionVisuals();
    }

    public void BeginDeleteSelectionMode()
    {
        if (buildController != null && buildController.IsBuilding)
            buildController.CancelBuilding();

        deleteSelectionMode = true;
        isDragging = false;
        hasValidDragStart = false;
        mouseDownOnCellBuilding = false;

        ClearSelection();

        Debug.Log(
            "Delete mode active. Drag to select buildings, foundations, " +
            "roads and taxiways, then press Delete or Backspace."
        );
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard == null || mouse == null)
            return;

        if (buildController != null && buildController.IsBuilding)
            return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            dragStartScreenPosition = mouse.position.ReadValue();
            dragCurrentScreenPosition = dragStartScreenPosition;

            mouseDownOnCellBuilding =
                moveController != null &&
                moveController.HasCellBuildingUnderMouse(
                    dragStartScreenPosition
                );

            hasValidDragStart = TryGetGridPositionFromMouse(
                dragStartScreenPosition,
                out dragStartGridPosition
            );

            dragCurrentGridPosition = dragStartGridPosition;
            isDragging = false;

            HideSelectionVisuals();

            if (mouseDownOnCellBuilding)
                return;
        }

        if (mouse.leftButton.isPressed &&
            hasValidDragStart &&
            !mouseDownOnCellBuilding)
        {
            dragCurrentScreenPosition = mouse.position.ReadValue();

            if (!TryGetGridPositionFromMouse(
                    dragCurrentScreenPosition,
                    out dragCurrentGridPosition))
            {
                return;
            }

            if (!isDragging)
            {
                float distance = Vector2.Distance(
                    dragStartScreenPosition,
                    dragCurrentScreenPosition
                );

                if (distance >= DRAG_THRESHOLD)
                {
                    isDragging = true;
                    ClearSelection();
                }
            }

            if (isDragging)
            {
                UpdateDragSelection();
                UpdateSelectionVisuals();
            }
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (mouseDownOnCellBuilding)
            {
                if (moveController != null && moveController.IsMoving)
                {
                    mouseDownOnCellBuilding = false;
                    return;
                }

                HandleSingleSelection();
                mouseDownOnCellBuilding = false;
                return;
            }

            if (!hasValidDragStart)
            {
                isDragging = false;
                HideSelectionVisuals();
                return;
            }

            if (!isDragging)
            {
                HandleSingleSelection();
            }
            else if (TryGetGridPositionFromMouse(
                         mouse.position.ReadValue(),
                         out dragCurrentGridPosition))
            {
                UpdateDragSelection();
                UpdateSelectionVisuals();
            }

            isDragging = false;
            HideSelectionVisuals();
        }

        if (keyboard.deleteKey.wasPressedThisFrame ||
            keyboard.backspaceKey.wasPressedThisFrame)
        {
            DeleteSelectedBuildings();
        }
    }

    private void HandleSingleSelection()
    {
        if (mainCamera == null)
        {
            ClearSelection();
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(
            Mouse.current.position.ReadValue()
        );

        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);

        System.Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance)
        );

        foreach (RaycastHit hit in hits)
        {
            PlacedBuilding building =
                hit.collider.GetComponentInParent<PlacedBuilding>();

            if (!IsSelectable(building))
                continue;

            SelectSingleBuilding(building.gameObject);
            return;
        }

        if (TryGetGridPositionFromMouse(
                Mouse.current.position.ReadValue(),
                out Vector2Int gridPosition))
        {
            if (roadNetwork != null && roadNetwork.IsRoad(gridPosition))
            {
                SelectSingleRoad(gridPosition);
                return;
            }

            if (taxiwayNetwork != null &&
                taxiwayNetwork.IsTaxiway(gridPosition))
            {
                SelectSingleTaxiway(gridPosition);
                return;
            }
        }

        ClearSelection();
    }

    private void UpdateDragSelection()
    {
        GetSelectionGridArea(
            out Vector2Int origin,
            out int width,
            out int depth
        );

        List<GameObject> newSelection = new List<GameObject>();
        HashSet<Vector2Int> newRoadSelection = new HashSet<Vector2Int>();
        HashSet<Vector2Int> newTaxiwaySelection = new HashSet<Vector2Int>();

        PlacedBuilding[] buildings =
            FindObjectsByType<PlacedBuilding>(FindObjectsSortMode.None);

        foreach (PlacedBuilding building in buildings)
        {
            if (!IsSelectable(building))
                continue;

            if (IsBuildingInsideGridSelection(
                    building, origin, width, depth))
            {
                newSelection.Add(building.gameObject);
            }
        }

        if (roadNetwork != null)
        {
            for (int x = origin.x; x < origin.x + width; x++)
            {
                for (int z = origin.y; z < origin.y + depth; z++)
                {
                    Vector2Int cell = new Vector2Int(x, z);

                    if (roadNetwork.IsRoad(cell))
                        newRoadSelection.Add(cell);
                }
            }
        }

        if (taxiwayNetwork != null)
        {
            for (int x = origin.x; x < origin.x + width; x++)
            {
                for (int z = origin.y; z < origin.y + depth; z++)
                {
                    Vector2Int cell = new Vector2Int(x, z);

                    if (taxiwayNetwork.IsTaxiway(cell))
                    {
                        AddTaxiwayModuleCells(
                            cell,
                            newTaxiwaySelection);
                    }
                }
            }
        }

        SetSelection(newSelection, newRoadSelection, newTaxiwaySelection);
    }

    private bool IsSelectable(PlacedBuilding building)
    {
        if (building == null || building.Definition == null)
            return false;

        if (building.Definition.placementType == PlacementType.Area)
            return deleteSelectionMode;

        return true;
    }

    private bool IsBuildingInsideGridSelection(
        PlacedBuilding building,
        Vector2Int selectionOrigin,
        int selectionWidth,
        int selectionDepth)
    {
        if (building == null)
            return false;

        int minX = selectionOrigin.x;
        int maxX = selectionOrigin.x + selectionWidth - 1;
        int minZ = selectionOrigin.y;
        int maxZ = selectionOrigin.y + selectionDepth - 1;

        if (building.IsEdgeBuilding)
        {
            return DoesEdgeBuildingOverlapSelection(
                building, minX, maxX, minZ, maxZ
            );
        }

        int buildingMinX = building.Origin.x;
        int buildingMaxX = building.Origin.x + building.Width - 1;
        int buildingMinZ = building.Origin.y;
        int buildingMaxZ = building.Origin.y + building.Depth - 1;

        return buildingMaxX >= minX &&
               buildingMinX <= maxX &&
               buildingMaxZ >= minZ &&
               buildingMinZ <= maxZ;
    }

    private bool DoesEdgeBuildingOverlapSelection(
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

    private bool IsCellInsideSelection(
        int x, int z,
        int minX, int maxX,
        int minZ, int maxZ)
    {
        return x >= minX && x <= maxX &&
               z >= minZ && z <= maxZ;
    }

    private void SelectSingleBuilding(GameObject building)
    {
        ClearSelection();
        selectedObjects.Add(building);
        RefreshHighlights();

        PlacedBuilding placedBuilding =
            building.GetComponent<PlacedBuilding>();

        if (placedBuilding != null && placedBuilding.Definition != null)
        {
            Debug.Log($"Selected: {placedBuilding.Definition.displayName}");
        }
    }

    private void SelectSingleRoad(Vector2Int cell)
    {
        ClearSelection();

        if (roadNetwork == null)
            return;

        Vector2Int anchor = roadNetwork.GetRoadAnchor(cell);

        for (int x = 0; x < RoadNetwork.RoadModuleSize; x++)
        {
            for (int z = 0; z < RoadNetwork.RoadModuleSize; z++)
            {
                selectedRoadCells.Add(
                    anchor + new Vector2Int(x, z));
            }
        }

        RefreshHighlights();
        Debug.Log($"Selected road module: {anchor}");
    }


    private void SelectSingleTaxiway(Vector2Int cell)
    {
        ClearSelection();

        AddTaxiwayModuleCells(cell, selectedTaxiwayCells);

        RefreshHighlights();

        Debug.Log(
            $"Selected taxiway module containing cell {cell}. " +
            $"Selected cells: {selectedTaxiwayCells.Count}");
    }


    private void AddTaxiwayModuleCells(
        Vector2Int cell,
        HashSet<Vector2Int> destination)
    {
        if (taxiwayPrefabRuleManager != null &&
            taxiwayPrefabRuleManager.TryGetVisualModuleAtCell(
                cell,
                out Vector2Int origin,
                out int width,
                out int depth))
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < depth; z++)
                {
                    destination.Add(
                        origin + new Vector2Int(x, z));
                }
            }

            return;
        }

        // Fallback if the cell has network data but no visual module.
        destination.Add(cell);
    }



    private void SetSelection(
        List<GameObject> newSelection,
        HashSet<Vector2Int> newRoadSelection,
        HashSet<Vector2Int> newTaxiwaySelection)
    {
        selectedObjects.Clear();

        foreach (GameObject building in newSelection)
        {
            if (building != null && !selectedObjects.Contains(building))
                selectedObjects.Add(building);
        }

        selectedRoadCells.Clear();

        foreach (Vector2Int cell in newRoadSelection)
            selectedRoadCells.Add(cell);

        selectedTaxiwayCells.Clear();

        foreach (Vector2Int cell in newTaxiwaySelection)
            selectedTaxiwayCells.Add(cell);

        RefreshHighlights();
    }


    private void RefreshHighlights()
    {
        ClearHighlights();

        bool hasDragSelection = isDragging || deleteSelectionMode;

        Vector2Int selectionOrigin = Vector2Int.zero;
        int selectionWidth = 0;
        int selectionDepth = 0;

        if (hasDragSelection)
        {
            GetSelectionGridArea(
                out selectionOrigin,
                out selectionWidth,
                out selectionDepth
            );
        }

        foreach (GameObject building in selectedObjects)
        {
            if (building == null)
                continue;

            PlacedBuilding placedBuilding =
                building.GetComponent<PlacedBuilding>();

            if (placedBuilding == null)
                continue;

            bool isFoundation =
                placedBuilding.Definition != null &&
                placedBuilding.Definition.placementType == PlacementType.Area;

            if (isFoundation && deleteSelectionMode && hasDragSelection)
            {
                for (int x = 0; x < selectionWidth; x++)
                {
                    for (int z = 0; z < selectionDepth; z++)
                    {
                        Vector2Int cell =
                            selectionOrigin + new Vector2Int(x, z);

                        if (cell.x < placedBuilding.Origin.x ||
                            cell.y < placedBuilding.Origin.y ||
                            cell.x >= placedBuilding.Origin.x + placedBuilding.Width ||
                            cell.y >= placedBuilding.Origin.y + placedBuilding.Depth)
                        {
                            continue;
                        }

                        if (gridSurface != null &&
                            !gridSurface.IsSurfaceOwner(cell, building))
                        {
                            continue;
                        }

                        CreateCellHighlight(cell);
                    }
                }

                continue;
            }

            if (placedBuilding.IsEdgeBuilding)
                HighlightWallObject(building);
            else
                CreateHighlights(placedBuilding);
        }

        foreach (Vector2Int cell in selectedRoadCells)
            CreateCellHighlight(cell);

        foreach (Vector2Int cell in selectedTaxiwayCells)
            CreateCellHighlight(cell);
    }


    private void HighlightWallObject(GameObject wall)
    {
        Renderer[] renderers =
            wall.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer wallRenderer in renderers)
        {
            if (wallRenderer == null ||
                originalWallPropertyBlocks.ContainsKey(wallRenderer))
                continue;

            Material[] materials = wallRenderer.sharedMaterials;

            bool supportsBaseColor = false;
            bool supportsColor = false;
            Color originalColor = Color.white;

            foreach (Material material in materials)
            {
                if (material == null)
                    continue;

                if (!supportsBaseColor && material.HasProperty(BaseColorId))
                {
                    supportsBaseColor = true;
                    originalColor = material.GetColor(BaseColorId);
                }
                else if (!supportsColor && material.HasProperty(ColorId))
                {
                    supportsColor = true;

                    if (!supportsBaseColor)
                        originalColor = material.GetColor(ColorId);
                }
            }

            if (!supportsBaseColor && !supportsColor)
                continue;

            MaterialPropertyBlock originalBlock =
                new MaterialPropertyBlock();

            wallRenderer.GetPropertyBlock(originalBlock);
            originalWallPropertyBlocks.Add(wallRenderer, originalBlock);

            MaterialPropertyBlock highlightBlock =
                new MaterialPropertyBlock();

            wallRenderer.GetPropertyBlock(highlightBlock);

            if (supportsBaseColor && originalBlock.HasColor(BaseColorId))
                originalColor = originalBlock.GetColor(BaseColorId);
            else if (!supportsBaseColor &&
                     supportsColor &&
                     originalBlock.HasColor(ColorId))
                originalColor = originalBlock.GetColor(ColorId);

            Color targetColor = wallHighlightColor;
            targetColor.a = originalColor.a;

            Color tintedColor = Color.Lerp(
                originalColor,
                targetColor,
                wallHighlightStrength
            );

            if (supportsBaseColor)
                highlightBlock.SetColor(BaseColorId, tintedColor);

            if (supportsColor)
                highlightBlock.SetColor(ColorId, tintedColor);

            wallRenderer.SetPropertyBlock(highlightBlock);
        }
    }

    private void RestoreWallHighlights()
    {
        foreach (
            KeyValuePair<Renderer, MaterialPropertyBlock> entry
            in originalWallPropertyBlocks)
        {
            if (entry.Key != null)
                entry.Key.SetPropertyBlock(entry.Value);
        }

        originalWallPropertyBlocks.Clear();
    }

    private void CreateHighlights(PlacedBuilding building)
    {
        int highlightWidth = building.Width;
        int highlightDepth = building.Depth;

        if (building.IsEdgeBuilding)
        {
            if (building.EdgeDirection == EdgeDirection.Horizontal)
            {
                highlightWidth = Mathf.Max(1, building.EdgeLength);
                highlightDepth = 1;
            }
            else
            {
                highlightWidth = 1;
                highlightDepth = Mathf.Max(1, building.EdgeLength);
            }
        }

        for (int x = 0; x < highlightWidth; x++)
        {
            for (int z = 0; z < highlightDepth; z++)
            {
                Vector2Int cell =
                    building.Origin + new Vector2Int(x, z);

                CreateCellHighlight(cell);
            }
        }
    }

    private void CreateCellHighlight(Vector2Int cell)
    {
        if (gridManager == null || highlightMaterial == null)
            return;

        GameObject highlight =
            GameObject.CreatePrimitive(PrimitiveType.Quad);

        highlight.name = "Selection Highlight";

        Collider collider = highlight.GetComponent<Collider>();

        if (collider != null)
            Destroy(collider);

        highlight.transform.position =
            gridManager.GridToWorldCenter(cell) + Vector3.up * 0.03f;

        highlight.transform.rotation =
            Quaternion.Euler(90f, 0f, 0f);

        highlight.transform.localScale = new Vector3(
            gridManager.CellSize * 0.94f,
            gridManager.CellSize * 0.94f,
            1f
        );

        Renderer renderer = highlight.GetComponent<Renderer>();
        renderer.material = highlightMaterial;

        highlights.Add(highlight);
    }

    public void RefreshSelectionHighlights()
    {
        RefreshHighlights();
    }

    private void ClearSelection()
    {
        selectedObjects.Clear();
        selectedRoadCells.Clear();
        selectedTaxiwayCells.Clear();
        ClearHighlights();
    }

    private void ClearHighlights()
    {
        RestoreWallHighlights();

        foreach (GameObject highlight in highlights)
        {
            if (highlight != null)
                Destroy(highlight);
        }

        highlights.Clear();
    }

    private void DeleteSelectedBuildings()
    {
        List<GameObject> buildingsToDelete =
            new List<GameObject>();

        foreach (GameObject building in selectedObjects)
        {
            if (building != null)
                buildingsToDelete.Add(building);
        }

        List<Vector2Int> roadsToDelete =
            new List<Vector2Int>(selectedRoadCells);

        List<Vector2Int> taxiwaysToDelete =
            new List<Vector2Int>(selectedTaxiwayCells);

        if (buildingsToDelete.Count == 0 &&
            roadsToDelete.Count == 0 &&
            taxiwaysToDelete.Count == 0)
        {
            ClearSelection();
            deleteSelectionMode = false;
            return;
        }

        Debug.Log(
            $"Deleting {buildingsToDelete.Count} building(s), " +
            $"{roadsToDelete.Count} road cell(s), and " +
            $"{taxiwaysToDelete.Count} taxiway cell(s)."
        );

        foreach (GameObject building in buildingsToDelete)
            DeleteBuilding(building);


        foreach (Vector2Int cell in roadsToDelete)
            DeleteRoadCell(cell);

        // Remove all selected visual modules as one batch before changing
        // the network. This prevents a rebuild from merging neighboring
        // modules partway through the deletion.
        if (taxiwaysToDelete.Count > 0)
        {
            bool visualsRemoved =
                taxiwayPrefabRuleManager != null &&
                taxiwayPrefabRuleManager.RemoveVisualModulesAtCells(
                    taxiwaysToDelete);

            if (visualsRemoved)
            {
                foreach (Vector2Int cell in taxiwaysToDelete)
                    DeleteTaxiwayCell(cell);
            }
            else
            {
                Debug.LogWarning(
                    "Taxiway deletion was cancelled because the visual " +
                    "modules could not be removed safely.");
            }
        }


        ClearSelection();
        deleteSelectionMode = false;
    }


    private void DeleteRoadCell(Vector2Int cell)
    {
        if (roadNetwork != null)
            roadNetwork.RemoveRoad(cell);

        // Clean up any legacy tile at this position, if one remains.
        if (roadTilemapController != null)
            roadTilemapController.RemoveRoadTile(cell);

        // The removed cell disappears, and neighboring modules
        // change shape to reflect their new connections.
        if (roadModuleRuleManager != null)
            roadModuleRuleManager.RefreshCellAndNeighbors(cell);

        Debug.Log($"Deleted road cell: {cell}");
    }




    private void DeleteTaxiwayCell(Vector2Int cell)
    {
        if (taxiwayNetwork != null)
            taxiwayNetwork.RemoveTaxiwayCell(cell);

        if (gridSurface != null)
            gridSurface.UnregisterTaxiwayCell(cell);

        Debug.Log($"Deleted taxiway cell: {cell}");
    }







    private void DeleteBuilding(GameObject building)
    {
        if (building == null)
            return;

        PlacedBuilding placedBuilding =
            building.GetComponent<PlacedBuilding>();

        string displayName =
            placedBuilding != null && placedBuilding.Definition != null
                ? placedBuilding.Definition.displayName
                : building.name;

        bool isArea =
            placedBuilding != null &&
            placedBuilding.Definition != null &&
            placedBuilding.Definition.placementType == PlacementType.Area;

        bool isTarmac =
            isArea &&
            placedBuilding.Definition.category == BuildCategory.Tarmac;

        if (isArea && deleteSelectionMode && gridSurface != null)
        {
            GetSelectionGridArea(
                out Vector2Int selectionOrigin,
                out int selectionWidth,
                out int selectionDepth
            );

            int removedCells = 0;

            TarmacTilemapController tarmacTilemapController = null;

            if (isTarmac)
            {
                tarmacTilemapController =
                    FindFirstObjectByType<TarmacTilemapController>();
            }

            for (int x = 0; x < selectionWidth; x++)
            {
                for (int z = 0; z < selectionDepth; z++)
                {
                    Vector2Int cell =
                        selectionOrigin + new Vector2Int(x, z);

                    if (!gridSurface.IsSurfaceOwner(cell, building))
                        continue;

                    if (!gridSurface.RemoveSurfaceCell(cell, building))
                        continue;

                    if (isTarmac)
                    {
                        if (tarmacTilemapController != null)
                            tarmacTilemapController.RemoveTarmacTile(cell);
                    }
                    else
                    {
                        if (foundationTilemapController != null)
                        {
                            foundationTilemapController.RemoveFoundationTile(cell);
                        }
                    }

                    removedCells++;
                }
            }

            if (removedCells == 0)
                return;

            Debug.Log(
                $"Removed {removedCells} {(isTarmac ? "tarmac" : "foundation")} " +
                $"tile(s) from {displayName}."
            );

            if (gridSurface.HasSurfaceCellsOwnedBy(building))
                return;

            if (gridOccupancy != null)
                gridOccupancy.ReleaseBuilding(building);

            if (edgeOccupancy != null)
                edgeOccupancy.ReleaseBuilding(building);

            gridSurface.ReleaseObstacleOwner(building);

            Debug.Log($"Deleted remaining area object: {displayName}");
            Destroy(building);
            return;
        }

        AircraftGate aircraftGate = building.GetComponent<AircraftGate>();

        if (aircraftGate != null)
        {
            string gateStableId = aircraftGate.StableId;

            if (!string.IsNullOrWhiteSpace(gateStableId))
            {
                FlightManagementUIController flightManagement =
                    FindFirstObjectByType<FlightManagementUIController>();

                if (flightManagement != null)
                {
                    flightManagement.CancelScheduledFlightsForGate(
                        gateStableId
                    );
                }
                else
                {
                    Debug.LogWarning(
                        $"Deleting {displayName}, but no " +
                        "FlightManagementUIController was found."
                    );
                }
            }
        }

        if (gridOccupancy != null)
            gridOccupancy.ReleaseBuilding(building);

        if (edgeOccupancy != null)
            edgeOccupancy.ReleaseBuilding(building);

        if (gridSurface != null)
        {
            gridSurface.ReleaseSurfaceOwner(building);
            gridSurface.ReleaseObstacleOwner(building);
        }

        Debug.Log($"Deleted building: {displayName}");
        Destroy(building);
    }





    private bool TryGetGridPositionFromMouse(
        Vector2 screenPosition,
        out Vector2Int gridPosition)
    {
        gridPosition = Vector2Int.zero;

        if (mainCamera == null || gridManager == null)
            return false;

        Ray ray = mainCamera.ScreenPointToRay(screenPosition);

        Plane gridPlane = new Plane(
            Vector3.up,
            gridManager.transform.position
        );

        if (!gridPlane.Raycast(ray, out float distance))
            return false;

        Vector3 worldPoint = ray.GetPoint(distance);
        gridPosition = gridManager.WorldToGrid(worldPoint);

        return gridManager.IsInsideGrid(gridPosition);
    }

    private void GetSelectionGridArea(
        out Vector2Int origin,
        out int width,
        out int depth)
    {
        int minX = Mathf.Min(
            dragStartGridPosition.x,
            dragCurrentGridPosition.x
        );

        int maxX = Mathf.Max(
            dragStartGridPosition.x,
            dragCurrentGridPosition.x
        );

        int minZ = Mathf.Min(
            dragStartGridPosition.y,
            dragCurrentGridPosition.y
        );

        int maxZ = Mathf.Max(
            dragStartGridPosition.y,
            dragCurrentGridPosition.y
        );

        origin = new Vector2Int(minX, minZ);
        width = maxX - minX + 1;
        depth = maxZ - minZ + 1;
    }

    private void CreateSelectionVisuals()
    {
        selectionBox = GameObject.CreatePrimitive(PrimitiveType.Quad);
        selectionBox.name = "Grid Selection Box";

        Collider collider = selectionBox.GetComponent<Collider>();

        if (collider != null)
            Destroy(collider);

        Renderer renderer = selectionBox.GetComponent<Renderer>();
        renderer.material = selectionBoxMaterial;

        selectionBorder = selectionBox.AddComponent<LineRenderer>();
        selectionBorder.useWorldSpace = true;
        selectionBorder.loop = true;
        selectionBorder.startWidth = SELECTION_BORDER_WIDTH;
        selectionBorder.endWidth = SELECTION_BORDER_WIDTH;
        selectionBorder.material = CreateSelectionBorderMaterial();
        selectionBorder.positionCount = 4;

        HideSelectionVisuals();
    }

    private void UpdateSelectionVisuals()
    {
        if (selectionBox == null ||
            selectionBorder == null ||
            gridManager == null)
            return;

        GetSelectionGridArea(
            out Vector2Int origin,
            out int width,
            out int depth
        );

        Vector3 bottomLeft = gridManager.GridToWorld(origin);

        Vector3 topRight = gridManager.GridToWorld(
            origin + new Vector2Int(width, depth)
        );

        Vector3 bottomRight = new Vector3(
            topRight.x, bottomLeft.y, bottomLeft.z
        );

        Vector3 topLeft = new Vector3(
            bottomLeft.x, bottomLeft.y, topRight.z
        );

        float y =
            gridManager.transform.position.y + SELECTION_HEIGHT_OFFSET;

        bottomLeft.y = y;
        bottomRight.y = y;
        topRight.y = y;
        topLeft.y = y;

        Vector3 center = (bottomLeft + topRight) * 0.5f;

        selectionBox.transform.position = center;
        selectionBox.transform.rotation =
            Quaternion.Euler(90f, 0f, 0f);

        selectionBox.transform.localScale = new Vector3(
            width * gridManager.CellSize,
            depth * gridManager.CellSize,
            1f
        );

        selectionBorder.SetPosition(0, bottomLeft);
        selectionBorder.SetPosition(1, bottomRight);
        selectionBorder.SetPosition(2, topRight);
        selectionBorder.SetPosition(3, topLeft);

        selectionBox.SetActive(true);
        selectionBorder.enabled = true;
    }

    private void HideSelectionVisuals()
    {
        if (selectionBox != null)
            selectionBox.SetActive(false);

        if (selectionBorder != null)
            selectionBorder.enabled = false;
    }

    private void CreateSelectionBoxMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            Debug.LogError("Could not find URP Unlit shader.");
            return;
        }

        selectionBoxMaterial = new Material(shader);
        selectionBoxMaterial.name = "Grid Selection Box Material";

        selectionBoxMaterial.SetFloat("_Surface", 1.0f);
        selectionBoxMaterial.SetFloat("_Blend", 0.0f);
        selectionBoxMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        selectionBoxMaterial.DisableKeyword("_ALPHATEST_ON");
        selectionBoxMaterial.DisableKeyword("_ALPHAMODULATE_ON");

        if (selectionBoxMaterial.HasProperty("_BaseColor"))
        {
            Color color = selectionBoxColor;
            color.a = 0.15f;
            selectionBoxMaterial.SetColor("_BaseColor", color);
        }

        selectionBoxMaterial.SetFloat("_ReceiveShadows", 0.0f);
        selectionBoxMaterial.SetInt(
            "_SrcBlend",
            (int)UnityEngine.Rendering.BlendMode.SrcAlpha
        );
        selectionBoxMaterial.SetInt(
            "_DstBlend",
            (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
        );

        selectionBoxMaterial.SetInt("_ZWrite", 0);
        selectionBoxMaterial.renderQueue = 3000;
    }

    private Material CreateSelectionBorderMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            Debug.LogError("Could not find URP Unlit shader.");
            return null;
        }

        Material material = new Material(shader);
        material.name = "Grid Selection Border Material";

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", selectionBorderColor);

        material.renderQueue = 3000;
        return material;
    }

    private void CreateHighlightMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            Debug.LogError("Could not find URP Unlit shader.");
            return;
        }

        highlightMaterial = new Material(shader);
        highlightMaterial.name = "Building Selection Material";

        if (highlightMaterial.HasProperty("_BaseColor"))
            highlightMaterial.SetColor("_BaseColor", highlightColor);

        highlightMaterial.renderQueue = 3000;
    }

    private void OnDestroy()
    {
        ClearSelection();

        if (selectionBox != null)
            Destroy(selectionBox);

        if (highlightMaterial != null)
            Destroy(highlightMaterial);

        if (selectionBoxMaterial != null)
            Destroy(selectionBoxMaterial);
    }
}
