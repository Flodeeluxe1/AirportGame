
using UnityEngine;

public class RoadPlacementHandler : IPlacementHandler
{
    private BuildController controller;
    private GridManager gridManager;
    private GridOccupancy gridOccupancy;
    private EdgeOccupancy edgeOccupancy;

    private RoadNetwork roadNetwork;
    private RoadModuleRuleManager roadModuleRuleManager;

    private BuildableDefinition definition;
    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private Vector2Int currentGridPosition;
    private Vector2Int startPosition;
    private Vector2Int endPosition;

    private bool isDragging;
    private int rotation;

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

        roadNetwork = Object.FindFirstObjectByType<RoadNetwork>();
        roadModuleRuleManager =
            Object.FindFirstObjectByType<RoadModuleRuleManager>();

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
        rotation = 0;

        if (previewObject != null)
            previewObject.transform.rotation = GetPreviewRotation();
    }

    public void UpdatePlacement(Vector3 hitPoint)
    {
        currentGridPosition = roadNetwork != null
            ? roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(hitPoint))
            : SnapToModule(gridManager.WorldToGrid(hitPoint));

        if (!isDragging)
        {
            if (previewObject != null)
            {
                previewObject.transform.position =
                    roadNetwork != null
                        ? roadNetwork.GetRoadCenterWorldPosition(
                            currentGridPosition, gridManager)
                        : GetModuleCenter(currentGridPosition);

                previewObject.transform.rotation = GetPreviewRotation();
                previewObject.transform.localScale =
                    new Vector3(ModuleSize, 1f, ModuleSize);
            }

            bool inside = IsInsideGrid(
                currentGridPosition, 1, 1);

            bool free = AreRoadCellsFree(
                currentGridPosition, 1, 1);

            placementPreview.SetValid(inside && free);
            return;
        }

        endPosition = GetDirectionalEndPosition();
        UpdatePreview();
        UpdatePreviewValidity();
    }

    public void BeginRoad()
    {
        startPosition = currentGridPosition;
        endPosition = currentGridPosition;
        isDragging = true;

        UpdatePreview();
        UpdatePreviewValidity();
    }

    public void EndRoad()
    {
        if (!isDragging)
            return;

        isDragging = false;
        TryPlace();
    }

    private Vector2Int SnapToModule(Vector2Int cell)
    {
        return new Vector2Int(
            Mathf.FloorToInt(cell.x / (float)ModuleSize) * ModuleSize,
            Mathf.FloorToInt(cell.y / (float)ModuleSize) * ModuleSize);
    }

    private Vector3 GetModuleCenter(Vector2Int anchor)
    {
        Vector3 position = gridManager.GridToWorld(anchor);
        position.x += gridManager.CellSize;
        position.z += gridManager.CellSize;
        return position;
    }

    private Vector2Int GetDirectionalEndPosition()
    {
        int distance;

        switch (GetRoadDirection())
        {
            case RoadDirection.East:
                distance = Mathf.Max(0,
                    currentGridPosition.x - startPosition.x);
                return startPosition + new Vector2Int(
                    distance / ModuleSize * ModuleSize, 0);

            case RoadDirection.West:
                distance = Mathf.Max(0,
                    startPosition.x - currentGridPosition.x);
                return startPosition + new Vector2Int(
                    -(distance / ModuleSize * ModuleSize), 0);

            case RoadDirection.North:
                distance = Mathf.Max(0,
                    currentGridPosition.y - startPosition.y);
                return startPosition + new Vector2Int(
                    0, distance / ModuleSize * ModuleSize);

            case RoadDirection.South:
                distance = Mathf.Max(0,
                    startPosition.y - currentGridPosition.y);
                return startPosition + new Vector2Int(
                    0, -(distance / ModuleSize * ModuleSize));
        }

        return startPosition;
    }

    private void UpdatePreview()
    {
        GetRoadArea(out Vector2Int origin, out int width, out int depth);

        if (previewObject == null)
            return;

        Vector3 position = gridManager.GridToWorld(origin);
        position.x += width * ModuleSize * gridManager.CellSize * 0.5f;
        position.z += depth * ModuleSize * gridManager.CellSize * 0.5f;

        previewObject.transform.position = position;
        previewObject.transform.rotation = GetPreviewRotation();
        previewObject.transform.localScale =
            new Vector3(width * ModuleSize, 1f, depth * ModuleSize);
    }

    private void UpdatePreviewValidity()
    {
        GetRoadArea(out Vector2Int origin, out int width, out int depth);

        placementPreview.SetValid(
            IsInsideGrid(origin, width, depth) &&
            AreRoadCellsFree(origin, width, depth));
    }

    private void GetRoadArea(
        out Vector2Int origin,
        out int width,
        out int depth)
    {
        int minX = Mathf.Min(startPosition.x, endPosition.x);
        int minZ = Mathf.Min(startPosition.y, endPosition.y);
        int maxX = Mathf.Max(startPosition.x, endPosition.x);
        int maxZ = Mathf.Max(startPosition.y, endPosition.y);

        origin = new Vector2Int(minX, minZ);
        width = (maxX - minX) / ModuleSize + 1;
        depth = (maxZ - minZ) / ModuleSize + 1;
    }

    private bool IsInsideGrid(
        Vector2Int origin,
        int width,
        int depth)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int anchor = origin +
                    new Vector2Int(x * ModuleSize, z * ModuleSize);

                for (int dx = 0; dx < ModuleSize; dx++)
                {
                    for (int dz = 0; dz < ModuleSize; dz++)
                    {
                        if (!gridManager.IsInsideGrid(
                                anchor + new Vector2Int(dx, dz)))
                            return false;
                    }
                }
            }
        }

        return true;
    }

    private bool AreRoadCellsFree(
        Vector2Int origin,
        int width,
        int depth)
    {
        if (roadNetwork == null)
            return false;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int anchor = origin +
                    new Vector2Int(x * ModuleSize, z * ModuleSize);

                if (roadNetwork.IsRoad(anchor))
                    return false;
            }
        }

        return true;
    }

    public void Rotate()
    {
        rotation = (rotation + 90) % 360;

        if (previewObject != null)
            previewObject.transform.rotation = GetPreviewRotation();

        if (isDragging)
        {
            endPosition = GetDirectionalEndPosition();
            UpdatePreview();
        }

        UpdatePreviewValidity();
    }

    private Quaternion GetPreviewRotation()
    {
        switch (rotation)
        {
            case 90:
            case 180:
                return Quaternion.Euler(0f, 180f, 0f);

            default:
                return Quaternion.Euler(0f, 0f, 0f);
        }
    }

    public void TryPlace()
    {
        if (roadNetwork == null || roadModuleRuleManager == null)
        {
            Debug.LogError(
                "Cannot place road: RoadNetwork or RoadModuleRuleManager is missing.");
            return;
        }

        endPosition = GetDirectionalEndPosition();
        GetRoadArea(out Vector2Int origin, out int width, out int depth);

        if (!IsInsideGrid(origin, width, depth))
        {
            Debug.Log("Cannot place road: outside grid.");
            return;
        }

        if (!AreRoadCellsFree(origin, width, depth))
        {
            Debug.Log("Cannot place road: one or more modules are already occupied.");
            return;
        }

        // Preserve the previous per-grid-cell construction cost.
        int quantity = width * depth * ModuleSize * ModuleSize;

        if (!ConstructionCostUtility.TryPay(
                definition, quantity, "Road construction"))
            return;

        RoadDirection direction = GetRoadDirection();

        // Create all logical modules first so connections can be calculated.
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int anchor = origin +
                    new Vector2Int(x * ModuleSize, z * ModuleSize);

                roadNetwork.AddRoad(anchor, direction);
                Debug.Log($"ROAD MODULE PLACED | Anchor: {anchor} | Direction: {direction}");
            }
        }

        // Refresh after the entire logical road is present.
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int anchor = origin +
                    new Vector2Int(x * ModuleSize, z * ModuleSize);

                roadModuleRuleManager.RefreshCellAndNeighbors(anchor);
            }
        }

        Debug.Log(
            $"Placed road modules: {width} x {depth}, Direction: {direction}");

        startPosition = currentGridPosition;
        endPosition = currentGridPosition;

        if (previewObject != null)
            previewObject.transform.localScale =
                new Vector3(ModuleSize, 1f, ModuleSize);
    }

    private RoadDirection GetRoadDirection()
    {
        switch (rotation)
        {
            case 0: return RoadDirection.South;
            case 90: return RoadDirection.West;
            case 180: return RoadDirection.North;
            case 270: return RoadDirection.East;
            default: return RoadDirection.South;
        }
    }

    public void Cancel()
    {
        isDragging = false;

        if (previewObject != null)
            previewObject.transform.localScale =
                new Vector3(ModuleSize, 1f, ModuleSize);
    }

    public void Reset()
    {
        definition = null;
        previewObject = null;
        placementPreview = null;

        currentGridPosition = Vector2Int.zero;
        startPosition = Vector2Int.zero;
        endPosition = Vector2Int.zero;

        isDragging = false;
        rotation = 0;
    }
}
