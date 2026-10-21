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

        roadNetwork =
            Object.FindFirstObjectByType<RoadNetwork>();

        if (roadNetwork == null)
        {
            Debug.LogError(
                "RoadPlacementHandler: " +
                "No RoadNetwork was found in the scene."
            );
        }

        roadModuleRuleManager =
            Object.FindFirstObjectByType<RoadModuleRuleManager>();

        if (roadModuleRuleManager == null)
        {
            Debug.LogError(
                "RoadPlacementHandler: No RoadModuleRuleManager was found in the scene.");
        }
    }

    public void BeginPlacement(
        BuildableDefinition definition,
        GameObject previewObject,
        PlacementPreview placementPreview)
    {
        this.definition = definition;
        this.previewObject = previewObject;
        this.placementPreview = placementPreview;

        currentGridPosition =
            Vector2Int.zero;

        startPosition =
            Vector2Int.zero;

        endPosition =
            Vector2Int.zero;

        isDragging = false;

        // Prefab arrow initially points South.
        rotation = 0;

        previewObject.transform.rotation =
            GetPreviewRotation();
    }

    public void UpdatePlacement(
        Vector3 hitPoint)
    {
        currentGridPosition =
            gridManager.WorldToGrid(
                hitPoint
            );

        if (!isDragging)
        {
            previewObject.transform.position =
                gridManager.GridToWorldCenter(
                    currentGridPosition
                );

            previewObject.transform.rotation =
                GetPreviewRotation();

            placementPreview.SetValid(
                gridManager.IsInsideGrid(
                    currentGridPosition
                )
            );

            return;
        }

        endPosition =
            GetDirectionalEndPosition();

        UpdatePreview();
        UpdatePreviewValidity();
    }

    public void BeginRoad()
    {
        startPosition =
            currentGridPosition;

        endPosition =
            currentGridPosition;

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

    private Vector2Int GetDirectionalEndPosition()
    {
        int distance;

        switch (GetRoadDirection())
        {
            case RoadDirection.East:

                distance =
                    currentGridPosition.x -
                    startPosition.x;

                distance =
                    Mathf.Max(
                        0,
                        distance
                    );

                return startPosition +
                       new Vector2Int(
                           distance,
                           0
                       );

            case RoadDirection.West:

                distance =
                    startPosition.x -
                    currentGridPosition.x;

                distance =
                    Mathf.Max(
                        0,
                        distance
                    );

                return startPosition +
                       new Vector2Int(
                           -distance,
                           0
                       );

            case RoadDirection.North:

                distance =
                    currentGridPosition.y -
                    startPosition.y;

                distance =
                    Mathf.Max(
                        0,
                        distance
                    );

                return startPosition +
                       new Vector2Int(
                           0,
                           distance
                       );

            case RoadDirection.South:

                distance =
                    startPosition.y -
                    currentGridPosition.y;

                distance =
                    Mathf.Max(
                        0,
                        distance
                    );

                return startPosition +
                       new Vector2Int(
                           0,
                           -distance
                       );
        }

        return startPosition;
    }

    private void UpdatePreview()
    {
        GetRoadArea(
            out Vector2Int origin,
            out int width,
            out int depth
        );

        Vector3 position =
            gridManager.GridToWorld(
                origin
            );

        position += new Vector3(
            width *
            gridManager.CellSize *
            0.5f,
            0f,
            depth *
            gridManager.CellSize *
            0.5f
        );

        previewObject.transform.position =
            position;

        previewObject.transform.rotation =
            GetPreviewRotation();

        previewObject.transform.localScale =
            new Vector3(
                width,
                1f,
                depth
            );
    }

    private void UpdatePreviewValidity()
    {
        GetRoadArea(
            out Vector2Int origin,
            out int width,
            out int depth
        );

        bool inside =
            IsInsideGrid(
                origin,
                width,
                depth
            );

        bool free =
            AreRoadCellsFree(
                origin,
                width,
                depth
            );

        placementPreview.SetValid(
            inside &&
            free
        );
    }

    private void GetRoadArea(
        out Vector2Int origin,
        out int width,
        out int depth)
    {
        int minX =
            Mathf.Min(
                startPosition.x,
                endPosition.x
            );

        int minZ =
            Mathf.Min(
                startPosition.y,
                endPosition.y
            );

        int maxX =
            Mathf.Max(
                startPosition.x,
                endPosition.x
            );

        int maxZ =
            Mathf.Max(
                startPosition.y,
                endPosition.y
            );

        origin =
            new Vector2Int(
                minX,
                minZ
            );

        width =
            maxX -
            minX +
            1;

        depth =
            maxZ -
            minZ +
            1;
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
                Vector2Int cell =
                    origin +
                    new Vector2Int(
                        x,
                        z
                    );

                if (roadNetwork.IsRoad(cell))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public void Rotate()
    {
        rotation += 90;

        if (rotation >= 360)
            rotation = 0;

        if (previewObject != null)
        {
            previewObject.transform.rotation =
                GetPreviewRotation();
        }

        if (isDragging)
        {
            endPosition =
                GetDirectionalEndPosition();

            UpdatePreview();
        }

        UpdatePreviewValidity();
    }

    private Quaternion GetPreviewRotation()
    {
        /*
         * The prefab arrow points South at 0°.
         *
         * Logical directions:
         *
         * 0°   = South
         * 90°  = West
         * 180° = North
         * 270° = East
         *
         * North/South previews are already correct.
         * West/East require a 90° visual correction.
         */

        switch (rotation)
        {
            case 90:
                return Quaternion.Euler(
                    0f,
                    180f,
                    0f
                );

            case 270:
                return Quaternion.Euler(
                    0f,
                    0f,
                    0f
                );

            case 180:
                return Quaternion.Euler(
                    0f,
                    180f,
                    0f
                );

            default:
                return Quaternion.Euler(
                    0f,
                    0f,
                    0f
                );
        }
    }



    public void TryPlace()
    {
        if (roadNetwork == null)
        {
            Debug.LogError("Cannot place road: RoadNetwork is missing.");
            return;
        }

        if (roadModuleRuleManager == null)
        {
            Debug.LogError(
                "Cannot place road: RoadModuleRuleManager is missing.");
            return;
        }

        endPosition = GetDirectionalEndPosition();

        GetRoadArea(
            out Vector2Int origin,
            out int width,
            out int depth);

        bool inside = IsInsideGrid(origin, width, depth);
        bool free = AreRoadCellsFree(origin, width, depth);

        if (!inside)
        {
            Debug.Log("Cannot place road: outside grid.");
            return;
        }

        if (!free)
        {
            Debug.Log(
                "Cannot place road: one or more cells already contain a road.");
            return;
        }

        int quantity = width * depth;

        if (!ConstructionCostUtility.TryPay(
                definition, quantity, "Road construction"))
        {
            return;
        }

        RoadDirection direction = GetRoadDirection();

        // First create every logical road cell.
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                roadNetwork.AddRoad(cell, direction);

                Debug.Log(
                    $"ROAD PLACED | Cell: {cell} | Direction: {direction}");
            }
        }

        // Then update each new module and its neighboring modules.
        // This lets the manager select the correct connection shapes.
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);
                roadModuleRuleManager.RefreshCellAndNeighbors(cell);
            }
        }

        Debug.Log(
            $"Placed road: {width} x {depth}, Direction: {direction}");

        startPosition = currentGridPosition;
        endPosition = currentGridPosition;

        if (previewObject != null)
            previewObject.transform.localScale = Vector3.one;
    }



    private RoadDirection GetRoadDirection()
    {
        /*
         * Prefab arrow starts pointing South.
         */

        switch (rotation)
        {
            case 0:
                return RoadDirection.South;

            case 90:
                return RoadDirection.West;

            case 180:
                return RoadDirection.North;

            case 270:
                return RoadDirection.East;
        }

        return RoadDirection.South;
    }

    public void Cancel()
    {
        isDragging = false;

        if (previewObject != null)
        {
            previewObject.transform.localScale =
                Vector3.one;
        }
    }

    public void Reset()
    {
        definition = null;
        previewObject = null;
        placementPreview = null;

        currentGridPosition =
            Vector2Int.zero;

        startPosition =
            Vector2Int.zero;

        endPosition =
            Vector2Int.zero;

        isDragging = false;

        rotation = 0;
    }
}