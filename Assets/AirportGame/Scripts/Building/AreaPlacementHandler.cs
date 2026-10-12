using UnityEngine;

public class AreaPlacementHandler : IPlacementHandler
{
    private BuildController controller;
    private GridManager gridManager;
    private GridOccupancy gridOccupancy;
    private EdgeOccupancy edgeOccupancy;
    private GridSurface gridSurface;
    private FoundationTilemapController foundationTilemapController;

    private BuildableDefinition definition;
    private BuildableDefinition automaticWallDefinition;

    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private Vector2Int currentGridPosition;

    private Vector2Int areaStartPosition;
    private Vector2Int areaEndPosition;

    private bool isDragging;

    private Vector3 originalPrefabScale;

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

        gridSurface =
            gridManager.GetComponent<GridSurface>();
    }

    public void SetFoundationTilemapController(
        FoundationTilemapController controller)
    {
        foundationTilemapController = controller;
    }

    public void SetAutomaticWallDefinition(
        BuildableDefinition wallDefinition)
    {
        automaticWallDefinition =
            wallDefinition;
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
        areaStartPosition = Vector2Int.zero;
        areaEndPosition = Vector2Int.zero;

        isDragging = false;

        originalPrefabScale =
            previewObject.transform.localScale;
    }

    public void UpdatePlacement(Vector3 hitPoint)
    {
        currentGridPosition =
            gridManager.WorldToGrid(hitPoint);

        if (!isDragging)
        {
            previewObject.transform.position =
                gridManager.GridToWorldCenter(
                    currentGridPosition
                );

            previewObject.transform.rotation =
                Quaternion.identity;

            previewObject.transform.localScale =
                originalPrefabScale;

            placementPreview.SetValid(
                gridManager.IsInsideGrid(
                    currentGridPosition
                )
            );

            return;
        }

        areaEndPosition =
            currentGridPosition;

        UpdatePreview();
        UpdatePreviewValidity();
    }

    public void BeginArea()
    {
        areaStartPosition =
            currentGridPosition;

        areaEndPosition =
            currentGridPosition;

        isDragging = true;

        UpdatePreview();
        UpdatePreviewValidity();
    }

    public void EndArea()
    {
        if (!isDragging)
            return;

        isDragging = false;

        TryPlace();
    }

    private void UpdatePreview()
    {
        GetArea(
            out Vector2Int origin,
            out int width,
            out int depth
        );

        Vector3 position =
            gridManager.GridToWorld(origin);

        position += new Vector3(
            width * gridManager.CellSize * 0.5f,
            0f,
            depth * gridManager.CellSize * 0.5f
        );

        previewObject.transform.position =
            position;

        previewObject.transform.rotation =
            Quaternion.identity;

        previewObject.transform.localScale =
            new Vector3(
                width,
                originalPrefabScale.y,
                depth
            );
    }

    private void UpdatePreviewValidity()
    {
        GetArea(
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
            gridOccupancy.CanOccupy(
                origin,
                width,
                depth
            );

        bool correctSurface =
            gridSurface != null &&
            gridSurface.CanSetSurfaceArea(
                origin,
                width,
                depth,
                BuildSurface.Grass
            );

        placementPreview.SetValid(
            inside &&
            free &&
            correctSurface
        );
    }

    private void GetArea(
        out Vector2Int origin,
        out int width,
        out int depth)
    {
        int minX =
            Mathf.Min(
                areaStartPosition.x,
                areaEndPosition.x
            );

        int maxX =
            Mathf.Max(
                areaStartPosition.x,
                areaEndPosition.x
            );

        int minZ =
            Mathf.Min(
                areaStartPosition.y,
                areaEndPosition.y
            );

        int maxZ =
            Mathf.Max(
                areaStartPosition.y,
                areaEndPosition.y
            );

        origin =
            new Vector2Int(
                minX,
                minZ
            );

        width =
            maxX - minX + 1;

        depth =
            maxZ - minZ + 1;
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
                    new Vector2Int(x, z);

                if (!gridManager.IsInsideGrid(cell))
                    return false;
            }
        }

        return true;
    }

    public void Rotate()
    {
        // Area placement does not need rotation.
    }

    public void TryPlace()
    {
        GetArea(
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
            gridOccupancy.CanOccupy(
                origin,
                width,
                depth
            );

        bool correctSurface =
            gridSurface != null &&
            gridSurface.CanSetSurfaceArea(
                origin,
                width,
                depth,
                BuildSurface.Grass
            );

        if (!inside)
        {
            Debug.Log(
                "Cannot place area: outside grid."
            );

            return;
        }

        if (!free)
        {
            Debug.Log(
                "Cannot place area: cells contain buildings."
            );

            return;
        }

        if (!correctSurface)
        {
            Debug.Log(
                "Cannot place area: foundation requires Grass."
            );

            return;
        }

        GameObject placedObject =
            Object.Instantiate(
                definition.prefab,
                previewObject.transform.position,
                previewObject.transform.rotation
            );

        placedObject.transform.localScale =
            new Vector3(
                width,
                originalPrefabScale.y,
                depth
            );

        DisableFoundationVisuals(placedObject);

        PlacedBuilding placedBuilding =
            placedObject.AddComponent<PlacedBuilding>();

        placedBuilding.Initialize(
            definition,
            origin,
            width,
            depth,
            0
        );

        gridSurface.SetSurfaceArea(
            origin,
            width,
            depth,
            BuildSurface.Foundation,
            placedObject
        );

        PaintFoundationTiles(
            origin,
            width,
            depth
        );

        BuildAutomaticFoundationWalls(
            origin,
            width,
            depth
        );

        Debug.Log(
            $"Placed foundation: " +
            $"{definition.displayName} " +
            $"({width} x {depth})"
        );

        isDragging = false;

        areaStartPosition =
            currentGridPosition;

        areaEndPosition =
            currentGridPosition;

        previewObject.transform.localScale =
            originalPrefabScale;
    }

    private void BuildAutomaticFoundationWalls(
        Vector2Int origin,
        int width,
        int depth)
    {
        if (automaticWallDefinition == null)
        {
            Debug.LogWarning(
                "AreaPlacementHandler: No automatic wall " +
                "definition has been assigned."
            );

            return;
        }

        if (automaticWallDefinition.prefab == null)
        {
            Debug.LogWarning(
                "AreaPlacementHandler: Automatic wall " +
                "definition has no prefab assigned."
            );

            return;
        }

        if (edgeOccupancy == null)
        {
            Debug.LogWarning(
                "AreaPlacementHandler: EdgeOccupancy is missing."
            );

            return;
        }

        // Bottom edge.
        for (int x = 0; x < width; x++)
        {
            GridEdge edge =
                new GridEdge(
                    new Vector2Int(
                        origin.x + x,
                        origin.y
                    ),
                    EdgeDirection.Horizontal
                );

            CreateAutomaticWall(edge);
        }

        // Top edge.
        for (int x = 0; x < width; x++)
        {
            GridEdge edge =
                new GridEdge(
                    new Vector2Int(
                        origin.x + x,
                        origin.y + depth
                    ),
                    EdgeDirection.Horizontal
                );

            CreateAutomaticWall(edge);
        }

        // Left edge.
        for (int z = 0; z < depth; z++)
        {
            GridEdge edge =
                new GridEdge(
                    new Vector2Int(
                        origin.x,
                        origin.y + z
                    ),
                    EdgeDirection.Vertical
                );

            CreateAutomaticWall(edge);
        }

        // Right edge.
        for (int z = 0; z < depth; z++)
        {
            GridEdge edge =
                new GridEdge(
                    new Vector2Int(
                        origin.x + width,
                        origin.y + z
                    ),
                    EdgeDirection.Vertical
                );

            CreateAutomaticWall(edge);
        }
    }

    private void CreateAutomaticWall(
        GridEdge edge)
    {
        if (edgeOccupancy.IsOccupied(edge))
        {
            return;
        }

        GameObject wallObject =
            Object.Instantiate(
                automaticWallDefinition.prefab
            );

        ApplyWallTransform(
            wallObject,
            edge
        );

        PlacedBuilding placedWall =
            wallObject.AddComponent<PlacedBuilding>();

        placedWall.InitializeEdge(
            automaticWallDefinition,
            edge.cell,
            1,
            edge.direction
        );

        edgeOccupancy.Occupy(
            edge,
            wallObject
        );
    }

    private void ApplyWallTransform(
        GameObject wallObject,
        GridEdge edge)
    {
        Vector3 position =
            gridManager.GridToWorld(
                edge.cell
            );

        float wallHeight =
            GetPrefabHeight(
                wallObject
            );

        position.y =
            wallHeight * 0.5f;

        if (edge.direction ==
            EdgeDirection.Horizontal)
        {
            position += new Vector3(
                gridManager.CellSize * 0.5f,
                0f,
                0f
            );

            wallObject.transform.position =
                position;

            wallObject.transform.rotation =
                Quaternion.identity;
        }
        else
        {
            position += new Vector3(
                0f,
                0f,
                gridManager.CellSize * 0.5f
            );

            wallObject.transform.position =
                position;

            wallObject.transform.rotation =
                Quaternion.Euler(
                    0f,
                    90f,
                    0f
                );
        }
    }

    private float GetPrefabHeight(
        GameObject target)
    {
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(
                true
            );

        if (renderers.Length == 0)
        {
            return 0f;
        }

        Bounds bounds =
            renderers[0].bounds;

        for (
            int i = 1;
            i < renderers.Length;
            i++)
        {
            bounds.Encapsulate(
                renderers[i].bounds
            );
        }

        return bounds.size.y;
    }

    public void Cancel()
    {
        isDragging = false;

        if (previewObject != null)
        {
            previewObject.transform.localScale =
                originalPrefabScale;
        }
    }

    public void Reset()
    {
        definition = null;
        previewObject = null;
        placementPreview = null;

        currentGridPosition = Vector2Int.zero;
        areaStartPosition = Vector2Int.zero;
        areaEndPosition = Vector2Int.zero;

        isDragging = false;

        originalPrefabScale = Vector3.one;
    }

    private void PaintFoundationTiles(
        Vector2Int origin,
        int width,
        int depth)
    {
        if (foundationTilemapController == null)
            return;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell =
                    origin +
                    new Vector2Int(x, z);

                foundationTilemapController.SetFoundationTile(
                    cell
                );
            }
        }
    }

    private void DisableFoundationVisuals(
    GameObject foundationObject)
    {
        if (foundationObject == null)
            return;

        Renderer[] renderers =
            foundationObject.GetComponentsInChildren<Renderer>(
                true
            );

        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = false;
        }
    }
}