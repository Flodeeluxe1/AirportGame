using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places one-cell pipeline segments by dragging a straight or L-shaped route.
/// The first drag axis can be toggled with R before or during placement.
/// </summary>
public sealed class PipelinePlacementHandler : IPlacementHandler
{
    private GridManager gridManager;
    private PipelinePrefabRuleManager pipelineManager;

    private BuildableDefinition definition;
    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private Vector2Int currentCell;
    private Vector2Int startCell;
    private Vector2Int endCell;

    private bool isDragging;
    private bool horizontalFirst;

    private readonly List<Vector2Int> routeCells = new();
    private readonly List<Vector2Int> lastPreviewRoute = new();
    private readonly List<GameObject> routePreviewObjects = new();
    private readonly HashSet<Vector2Int> newCells = new();

    private GameObject routePreviewParent;
    private bool hasPreviewState;
    private bool lastPreviewValid;

    public void Initialize(
        BuildController controller,
        GridManager gridManager,
        GridOccupancy gridOccupancy,
        EdgeOccupancy edgeOccupancy)
    {
        this.gridManager = gridManager;
        pipelineManager = Object.FindFirstObjectByType<PipelinePrefabRuleManager>();

        if (this.gridManager == null)
            Debug.LogError("PipelinePlacementHandler: GridManager was not found.");

        if (pipelineManager == null)
            Debug.LogError("PipelinePlacementHandler: PipelinePrefabRuleManager was not found.");
    }

    public void BeginPlacement(
        BuildableDefinition definition,
        GameObject previewObject,
        PlacementPreview placementPreview)
    {
        this.definition = definition;
        this.previewObject = previewObject;
        this.placementPreview = placementPreview;

        if (routePreviewParent == null)
        {
            routePreviewParent = new GameObject("PipelineRoutePreview");
        }

        currentCell = Vector2Int.zero;
        startCell = Vector2Int.zero;
        endCell = Vector2Int.zero;
        isDragging = false;
        horizontalFirst = true;
    }

    public void UpdatePlacement(Vector3 hitPoint)
    {
        if (gridManager == null || definition == null)
            return;

        currentCell = gridManager.WorldToGrid(hitPoint);

        if (isDragging)
            endCell = currentCell;
        else
        {
            startCell = currentCell;
            endCell = currentCell;
        }

        UpdatePreview();
    }

    public void BeginPipeline()
    {
        if (definition == null || gridManager == null)
            return;

        startCell = currentCell;
        endCell = currentCell;
        isDragging = true;
        UpdatePreview();
    }

    public void EndPipeline()
    {
        if (!isDragging)
            return;

        endCell = currentCell;
        isDragging = false;
        TryPlace();

        ClearRoutePreview();

        if (previewObject != null)
            previewObject.SetActive(true);

        UpdatePreview();
    }

    public void Rotate()
    {
        horizontalFirst = !horizontalFirst;
        UpdatePreview();
    }

    private void BuildRoute()
    {
        routeCells.Clear();

        Vector2Int corner = horizontalFirst
            ? new Vector2Int(endCell.x, startCell.y)
            : new Vector2Int(startCell.x, endCell.y);

        AddSegment(startCell, corner);

        if (corner != endCell)
            AddSegment(corner, endCell);
    }

    private void AddSegment(Vector2Int from, Vector2Int to)
    {
        Vector2Int step = new Vector2Int(
            to.x == from.x ? 0 : (to.x > from.x ? 1 : -1),
            to.y == from.y ? 0 : (to.y > from.y ? 1 : -1));

        Vector2Int cell = from;

        while (true)
        {
            if (routeCells.Count == 0 || routeCells[routeCells.Count - 1] != cell)
                routeCells.Add(cell);

            if (cell == to)
                break;

            cell += step;
        }
    }

    private void UpdatePreview()
    {
        if (gridManager == null)
            return;

        BuildRoute();

        bool valid = pipelineManager != null && ValidateRoute();

        if (isDragging)
        {
            if (previewObject != null)
                previewObject.SetActive(false);

            UpdateRoutePreview(valid);
            return;
        }

        ClearRoutePreview();

        if (previewObject == null)
            return;

        previewObject.SetActive(true);

        Vector3 position = gridManager.GridToWorldCenter(currentCell);
        previewObject.transform.position = position;

        // Align the visible preview mesh with the grid surface instead of
        // assuming the prefab pivot is at the bottom of the model.
        Renderer[] renderers =
            previewObject.GetComponentsInChildren<Renderer>(true);

        float lowestY = float.PositiveInfinity;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
                continue;

            lowestY = Mathf.Min(lowestY, renderer.bounds.min.y);
        }

        if (!float.IsPositiveInfinity(lowestY))
        {
            position.y += position.y - lowestY + 0.02f;
            previewObject.transform.position = position;
        }

        if (placementPreview != null)
            placementPreview.SetValid(valid);
    }

    private void UpdateRoutePreview(bool valid)
    {
        if (pipelineManager == null || routePreviewParent == null)
            return;

        bool unchanged =
            hasPreviewState &&
            lastPreviewValid == valid &&
            lastPreviewRoute.Count == routeCells.Count;

        if (unchanged)
        {
            for (int i = 0; i < routeCells.Count; i++)
            {
                if (lastPreviewRoute[i] != routeCells[i])
                {
                    unchanged = false;
                    break;
                }
            }
        }

        if (unchanged)
            return;

        ClearRoutePreview();

        foreach (Vector2Int cell in routeCells)
        {
            GameObject previewPiece = pipelineManager.CreatePreviewVisual(
                cell,
                routeCells,
                routePreviewParent.transform,
                valid);

            if (previewPiece != null)
                routePreviewObjects.Add(previewPiece);
        }

        lastPreviewRoute.AddRange(routeCells);
        lastPreviewValid = valid;
        hasPreviewState = true;
    }

    private void ClearRoutePreview()
    {
        foreach (GameObject previewPiece in routePreviewObjects)
        {
            if (previewPiece != null)
                Object.Destroy(previewPiece);
        }

        routePreviewObjects.Clear();
        lastPreviewRoute.Clear();
        hasPreviewState = false;
    }

    private bool ValidateRoute()
    {
        if (routeCells.Count == 0)
            return false;

        foreach (Vector2Int cell in routeCells)
        {
            if (!pipelineManager.CanPlacePipe(cell))
                return false;
        }

        return true;
    }

    public void TryPlace()
    {
        if (pipelineManager == null || definition == null)
            return;

        BuildRoute();

        if (!ValidateRoute())
        {
            Debug.Log("Cannot place pipeline: one or more cells are invalid.");
            UpdatePreview();
            return;
        }

        newCells.Clear();

        foreach (Vector2Int cell in routeCells)
        {
            if (!pipelineManager.IsPipe(cell))
                newCells.Add(cell);
        }

        if (newCells.Count == 0)
            return;

        if (!ConstructionCostUtility.TryPay(
                definition,
                newCells.Count,
                "Pipeline construction"))
        {
            return;
        }

        if (!pipelineManager.TryAddPipes(routeCells))
        {
            Debug.LogWarning("Pipeline placement failed after validation.");
            return;
        }

        Debug.Log(
            $"PIPELINE PLACED | Added {newCells.Count} cells | " +
            $"Route length: {routeCells.Count}");
    }

    public void Cancel()
    {
        isDragging = false;
        ClearRoutePreview();

        if (previewObject != null)
            previewObject.SetActive(true);
    }

    public void Reset()
    {
        ClearRoutePreview();

        if (routePreviewParent != null)
            Object.Destroy(routePreviewParent);

        routePreviewParent = null;
        definition = null;
        previewObject = null;
        placementPreview = null;
        currentCell = Vector2Int.zero;
        startCell = Vector2Int.zero;
        endCell = Vector2Int.zero;
        isDragging = false;
        horizontalFirst = true;
        routeCells.Clear();
        newCells.Clear();
    }
}