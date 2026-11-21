using System.Collections.Generic;
using UnityEngine;

public class RoadPlacementHandler : IPlacementHandler
{
    private BuildController controller;
    private GridManager gridManager;
    private GridOccupancy gridOccupancy;
    private EdgeOccupancy edgeOccupancy;

    private RoadNetwork roadNetwork;
    private RoadModuleRuleManager roadModuleRuleManager;
    private GridSurface gridSurface;

    private BuildableDefinition definition;
    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private readonly List<GameObject> additionalPreviewObjects =
        new List<GameObject>();

    private Vector2Int currentGridPosition;
    private Vector2Int startPosition;
    private Vector2Int endPosition;

    private bool isDragging;

    private const int ModuleSize = RoadNetwork.RoadModuleSize;

    public void Initialize(
        BuildController controller,
        GridManager gridManager,
        GridOccupancy gridOccupancy,
        EdgeOccupancy edgeOccupancy)
    {
        this.controller = controller;
        this.gridManager = gridManager;
        this.gridOccupancy = gridOccupancy;
        this.edgeOccupancy = edgeOccupancy;

        gridSurface = gridManager.GetComponent<GridSurface>();
        roadNetwork = Object.FindAnyObjectByType<RoadNetwork>();
        roadModuleRuleManager =
            Object.FindAnyObjectByType<RoadModuleRuleManager>();

        if (roadNetwork == null)
            Debug.LogError("RoadPlacementHandler: No RoadNetwork was found.");

        if (roadModuleRuleManager == null)
            Debug.LogError("RoadPlacementHandler: No RoadModuleRuleManager was found.");
    }

    public void BeginPlacement(
        BuildableDefinition definition,
        GameObject previewObject,
        PlacementPreview placementPreview)
    {
        this.definition = definition;
        this.previewObject = previewObject;
        this.placementPreview = placementPreview;

        currentGridPosition = Vector2Int.zero;
        startPosition = Vector2Int.zero;
        endPosition = Vector2Int.zero;
        isDragging = false;

        ClearAdditionalPreviews();
        UpdateSingleModulePreview();
    }

    public void UpdatePlacement(Vector3 hitPoint)
    {
        currentGridPosition = roadNetwork != null
            ? roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(hitPoint))
            : SnapToModule(gridManager.WorldToGrid(hitPoint));

        if (!isDragging)
        {
            UpdateSingleModulePreview();

            bool valid =
                IsModuleInsideGrid(currentGridPosition) &&
                IsModuleFree(currentGridPosition);

            placementPreview.SetValid(valid);
            return;
        }

        endPosition = currentGridPosition;
        UpdateRoutePreview();
        UpdatePreviewValidity();
    }

    public void BeginRoad()
    {
        startPosition = currentGridPosition;
        endPosition = currentGridPosition;
        isDragging = true;

        UpdateRoutePreview();
        UpdatePreviewValidity();
    }

    public void EndRoad()
    {
        if (!isDragging)
            return;

        endPosition = currentGridPosition;
        isDragging = false;
        TryPlace();
    }

    private Vector2Int SnapToModule(Vector2Int cell)
    {
        return new Vector2Int(
            Mathf.FloorToInt(cell.x / (float)ModuleSize) * ModuleSize,
            Mathf.FloorToInt(cell.y / (float)ModuleSize) * ModuleSize);
    }

    private void UpdateSingleModulePreview()
    {
        ClearAdditionalPreviews();

        if (previewObject == null || gridManager == null)
            return;

        previewObject.transform.position =
            roadNetwork != null
                ? roadNetwork.GetRoadCenterWorldPosition(
                    currentGridPosition, gridManager)
                : GetModuleCenter(currentGridPosition);

        previewObject.transform.rotation = Quaternion.identity;
        previewObject.transform.localScale =
            new Vector3(ModuleSize, 1f, ModuleSize);
    }

    private Vector3 GetModuleCenter(Vector2Int anchor)
    {
        Vector3 position = gridManager.GridToWorld(anchor);
        position.x += gridManager.CellSize;
        position.z += gridManager.CellSize;
        return position;
    }

    private List<Vector2Int> BuildRoute()
    {
        var route = new List<Vector2Int> { startPosition };

        int deltaX = endPosition.x - startPosition.x;
        int deltaZ = endPosition.y - startPosition.y;

        bool horizontalFirst =
            Mathf.Abs(deltaX) >= Mathf.Abs(deltaZ);

        if (horizontalFirst)
        {
            AddHorizontalSteps(route, endPosition.x);
            AddVerticalSteps(route, endPosition.y);
        }
        else
        {
            AddVerticalSteps(route, endPosition.y);
            AddHorizontalSteps(route, endPosition.x);
        }

        return route;
    }

    private void AddHorizontalSteps(
        List<Vector2Int> route,
        int targetX)
    {
        Vector2Int current = route[route.Count - 1];
        int step = targetX >= current.x ? ModuleSize : -ModuleSize;

        while (current.x != targetX)
        {
            current.x += step;
            route.Add(current);
        }
    }

    private void AddVerticalSteps(
        List<Vector2Int> route,
        int targetZ)
    {
        Vector2Int current = route[route.Count - 1];
        int step = targetZ >= current.y ? ModuleSize : -ModuleSize;

        while (current.y != targetZ)
        {
            current.y += step;
            route.Add(current);
        }
    }

    private RoadDirection GetTravelDirection(
        List<Vector2Int> route,
        int index)
    {
        Vector2Int from;
        Vector2Int to;

        if (index < route.Count - 1)
        {
            from = route[index];
            to = route[index + 1];
        }
        else if (route.Count > 1)
        {
            from = route[index - 1];
            to = route[index];
        }
        else
        {
            return RoadDirection.South;
        }

        Vector2Int delta = to - from;

        if (delta.x > 0) return RoadDirection.East;
        if (delta.x < 0) return RoadDirection.West;
        if (delta.y > 0) return RoadDirection.North;
        return RoadDirection.South;
    }

    private void UpdateRoutePreview()
    {
        if (previewObject == null || gridManager == null)
            return;

        List<Vector2Int> route = BuildRoute();
        EnsurePreviewCount(route.Count - 1);

        for (int i = 0; i < route.Count; i++)
        {
            GameObject preview = i == 0
                ? previewObject
                : additionalPreviewObjects[i - 1];

            if (preview == null)
                continue;

            preview.transform.position =
                roadNetwork != null
                    ? roadNetwork.GetRoadCenterWorldPosition(
                        route[i], gridManager)
                    : GetModuleCenter(route[i]);

            preview.transform.rotation = Quaternion.identity;
            preview.transform.localScale =
                new Vector3(ModuleSize, 1f, ModuleSize);
        }
    }

    private void EnsurePreviewCount(int requiredCount)
    {
        requiredCount = Mathf.Max(0, requiredCount);

        while (additionalPreviewObjects.Count > requiredCount)
        {
            int lastIndex = additionalPreviewObjects.Count - 1;
            GameObject preview = additionalPreviewObjects[lastIndex];

            if (preview != null)
            {
                preview.SetActive(false);
                Object.Destroy(preview);
            }

            additionalPreviewObjects.RemoveAt(lastIndex);
        }

        while (additionalPreviewObjects.Count < requiredCount)
        {
            if (definition == null || definition.prefab == null)
                return;

            GameObject preview = Object.Instantiate(
                definition.prefab);

            PlacementPreview previewState =
                preview.GetComponent<PlacementPreview>();

            if (previewState == null)
                previewState = preview.AddComponent<PlacementPreview>();

            previewState.Initialize();
            additionalPreviewObjects.Add(preview);
        }
    }

    private void ClearAdditionalPreviews()
    {
        foreach (GameObject preview in additionalPreviewObjects)
        {
            if (preview == null)
                continue;

            preview.SetActive(false);
            Object.Destroy(preview);
        }

        additionalPreviewObjects.Clear();
    }

    private void SetAllPreviewsValid(bool valid)
    {
        if (placementPreview != null)
            placementPreview.SetValid(valid);

        foreach (GameObject preview in additionalPreviewObjects)
        {
            if (preview == null)
                continue;

            PlacementPreview previewState =
                preview.GetComponent<PlacementPreview>();

            if (previewState != null)
                previewState.SetValid(valid);
        }
    }

    private void UpdatePreviewValidity()
    {
        List<Vector2Int> route = BuildRoute();
        bool valid = AreRouteModulesValid(route);
        SetAllPreviewsValid(valid);
    }

    private bool AreRouteModulesValid(List<Vector2Int> route)
    {
        if (roadNetwork == null || gridManager == null)
            return false;

        for (int i = 0; i < route.Count; i++)
        {
            if (!IsModuleInsideGrid(route[i]) ||
                !IsModuleFree(route[i]))
                return false;
        }

        return true;
    }

    private bool IsModuleInsideGrid(Vector2Int anchor)
    {
        for (int x = 0; x < ModuleSize; x++)
        {
            for (int z = 0; z < ModuleSize; z++)
            {
                if (!gridManager.IsInsideGrid(
                        anchor + new Vector2Int(x, z)))
                    return false;
            }
        }

        return true;
    }

    private bool IsModuleFree(Vector2Int anchor)
    {
        return roadNetwork != null && !roadNetwork.IsRoad(anchor);
    }

    public void Rotate()
    {
        // Road travel direction and route shape are determined by dragging.
    }

    public void TryPlace()
    {
        if (roadNetwork == null || roadModuleRuleManager == null)
        {
            Debug.LogError(
                "Cannot place road: RoadNetwork or RoadModuleRuleManager is missing.");
            return;
        }

        List<Vector2Int> route = BuildRoute();

        if (!AreRouteModulesValid(route))
        {
            Debug.Log("Cannot place road: one or more route modules are outside the grid or already occupied.");
            SetAllPreviewsValid(false);
            return;
        }

        // Each road module occupies a 2x2 group of grid cells.
        int quantity = route.Count * ModuleSize * ModuleSize;

        if (!ConstructionCostUtility.TryPay(
                definition, quantity, "Road construction"))
            return;

        // Assign each module the direction of travel toward the next
        // module. At the bend, this changes to the direction of the
        // second leg, allowing the existing pathfinder to follow the turn
        // without allowing traffic to travel backwards.
        for (int i = 0; i < route.Count; i++)
        {
            RoadDirection direction = GetTravelDirection(route, i);
            roadNetwork.AddRoad(route[i], direction);

            if (gridSurface != null)
            {
                gridSurface.RegisterRoadArea(
                    route[i],
                    ModuleSize,
                    ModuleSize);
            }
        }

        // Refresh after the complete route exists so all connections
        // and visual modules are calculated from the finished shape.
        foreach (Vector2Int anchor in route)
            roadModuleRuleManager.RefreshCellAndNeighbors(anchor);

        Debug.Log(
            $"Placed one-way road route with {route.Count} modules, " +
            $"from {startPosition} to {endPosition}.");

        startPosition = currentGridPosition;
        endPosition = currentGridPosition;

        UpdateSingleModulePreview();
        bool valid =
            IsModuleInsideGrid(currentGridPosition) &&
            IsModuleFree(currentGridPosition);
        placementPreview.SetValid(valid);
    }

    public void Cancel()
    {
        isDragging = false;
        ClearAdditionalPreviews();

        if (previewObject != null)
            previewObject.transform.localScale =
                new Vector3(ModuleSize, 1f, ModuleSize);
    }

    public void Reset()
    {
        ClearAdditionalPreviews();

        definition = null;
        previewObject = null;
        placementPreview = null;

        currentGridPosition = Vector2Int.zero;
        startPosition = Vector2Int.zero;
        endPosition = Vector2Int.zero;

        isDragging = false;
    }
}
