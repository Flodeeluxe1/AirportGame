using UnityEngine;

public class CellPlacementHandler : IPlacementHandler
{
    private BuildController controller;
    private GridManager gridManager;
    private GridOccupancy gridOccupancy;
    private GridSurface gridSurface;

    private BuildableDefinition definition;
    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private Vector2Int currentGridPosition;
    private int currentRotation;

    public void Initialize(
        BuildController controller,
        GridManager gridManager,
        GridOccupancy gridOccupancy,
        EdgeOccupancy edgeOccupancy)
    {
        this.controller = controller;
        this.gridManager = gridManager;
        this.gridOccupancy = gridOccupancy;

        gridSurface = gridManager.GetComponent<GridSurface>();
    }

    public void BeginPlacement(
        BuildableDefinition definition,
        GameObject previewObject,
        PlacementPreview placementPreview)
    {
        this.definition = definition;
        this.previewObject = previewObject;
        this.placementPreview = placementPreview;

        currentRotation = 0;
        currentGridPosition = Vector2Int.zero;
    }

    public void UpdatePlacement(Vector3 hitPoint)
    {
        currentGridPosition = gridManager.WorldToGrid(hitPoint);

        UpdatePreviewTransform();
        UpdatePreviewValidity();
    }

    private void UpdatePreviewTransform()
    {
        int width = definition.width;
        int depth = definition.depth;

        if (currentRotation == 90 || currentRotation == 270)
        {
            int temp = width;
            width = depth;
            depth = temp;
        }

        Vector3 position = gridManager.GridToWorld(currentGridPosition);

        position += new Vector3(
            width * gridManager.CellSize * 0.5f,
            0f,
            depth * gridManager.CellSize * 0.5f
        );

        previewObject.transform.position = position;
        previewObject.transform.rotation =
            Quaternion.Euler(0f, currentRotation, 0f);
    }

    private void UpdatePreviewValidity()
    {
        int width = definition.width;
        int depth = definition.depth;

        if (currentRotation == 90 || currentRotation == 270)
        {
            int temp = width;
            width = depth;
            depth = temp;
        }

        bool inside = IsInsideGrid(currentGridPosition, width, depth);

        bool free = gridOccupancy.CanOccupy(
            currentGridPosition,
            width,
            depth
        );

        bool correctSurface = HasRequiredSurface(
            currentGridPosition,
            width,
            depth
        );

        placementPreview.SetValid(inside && free && correctSurface);
    }

    private bool HasRequiredSurface(
        Vector2Int origin,
        int width,
        int depth)
    {
        if (gridSurface == null)
            return definition.requiredSurface == BuildSurface.Any;

        if (definition.requiredSurface == BuildSurface.Any)
            return true;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                if (!gridSurface.IsSurface(cell, definition.requiredSurface))
                    return false;
            }
        }

        return true;
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
                Vector2Int cell = origin + new Vector2Int(x, z);

                if (!gridManager.IsInsideGrid(cell))
                    return false;
            }
        }

        return true;
    }

    public void Rotate()
    {
        if (!definition.canRotate)
            return;

        currentRotation += 90;

        if (currentRotation >= 360)
            currentRotation = 0;

        UpdatePreviewTransform();
        UpdatePreviewValidity();
    }

    public void TryPlace()
    {
        int width = definition.width;
        int depth = definition.depth;

        if (currentRotation == 90 || currentRotation == 270)
        {
            int temp = width;
            width = depth;
            depth = temp;
        }

        if (!IsInsideGrid(currentGridPosition, width, depth))
        {
            Debug.Log("Cannot place building: outside grid.");
            return;
        }

        if (!gridOccupancy.CanOccupy(currentGridPosition, width, depth))
        {
            Debug.Log("Cannot place building: cells are occupied.");
            return;
        }

        if (!HasRequiredSurface(currentGridPosition, width, depth))
        {
            Debug.Log(
                $"Cannot place {definition.displayName}: " +
                $"requires {definition.requiredSurface}."
            );
            return;
        }

        GameObject placedObject = Object.Instantiate(
            definition.prefab,
            previewObject.transform.position,
            previewObject.transform.rotation
        );

        gridOccupancy.Occupy(
            currentGridPosition,
            width,
            depth,
            placedObject
        );

        PlacedBuilding placedBuilding =
            placedObject.AddComponent<PlacedBuilding>();

        placedBuilding.Initialize(
            definition,
            currentGridPosition,
            width,
            depth,
            currentRotation
        );

        if (definition.blocksPathfinding && gridSurface != null)
        {
            GridPathfindingObstacle obstacle =
                placedObject.AddComponent<GridPathfindingObstacle>();

            obstacle.Initialize(gridSurface, placedBuilding);
        }

        Debug.Log($"Placed: {definition.displayName}");
    }

    public void Cancel()
    {
    }

    public void Reset()
    {
        definition = null;
        previewObject = null;
        placementPreview = null;

        currentGridPosition = Vector2Int.zero;
        currentRotation = 0;
    }
}