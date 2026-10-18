
using System.Collections.Generic;
using UnityEngine;

public class DoorPlacementHandler : IPlacementHandler
{
    private BuildController controller;
    private GridManager gridManager;
    private EdgeOccupancy edgeOccupancy;
    private GridSurface gridSurface;

    private BuildableDefinition definition;
    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private GridEdge currentEdge;

    private readonly List<GridEdge> previewEdges =
        new List<GridEdge>();

    public void Initialize(
        BuildController controller,
        GridManager gridManager,
        GridOccupancy gridOccupancy,
        EdgeOccupancy edgeOccupancy)
    {
        this.controller = controller;
        this.gridManager = gridManager;
        this.edgeOccupancy = edgeOccupancy;

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

        currentEdge = new GridEdge(
            Vector2Int.zero,
            EdgeDirection.Horizontal);

        previewEdges.Clear();
    }

    // Required by IPlacementHandler.
    public void UpdatePlacement(Vector3 hitPoint)
    {
    }

    public void UpdatePlacement(Ray mouseRay)
    {
        if (previewObject == null || placementPreview == null)
            return;

        bool foundWall = false;

        RaycastHit[] hits = Physics.RaycastAll(mouseRay, 1000f);

        System.Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
                continue;

            PlacedBuilding wall =
                hit.collider.GetComponentInParent<PlacedBuilding>();

            if (wall == null || wall.Definition == null)
                continue;

            if (wall.Definition.placementType != PlacementType.Edge ||
                wall.Definition.category != BuildCategory.Wall)
            {
                continue;
            }

            GridEdge wallEdge = GetWallEdgeFromHit(hit, wall);

            if (!IsWallEdgeOwnedByWall(wallEdge, wall.gameObject))
                continue;

            currentEdge = GetDoorStartEdge(wallEdge, hit.point);

            BuildPreviewEdgeList();
            UpdatePreview();

            foundWall = true;
            break;
        }

        if (!foundWall)
        {
            previewEdges.Clear();
            placementPreview.SetValid(false);
        }
    }

    private GridEdge GetWallEdgeFromHit(
        RaycastHit hit,
        PlacedBuilding wall)
    {
        return new GridEdge(wall.Origin, wall.EdgeDirection);
    }

    private bool IsWallEdgeOwnedByWall(
        GridEdge edge,
        GameObject wall)
    {
        if (edgeOccupancy == null || wall == null)
            return false;

        if (!edgeOccupancy.TryGetOccupant(edge, out GameObject occupant))
            return false;

        return occupant == wall;
    }

    private GridEdge GetDoorStartEdge(
        GridEdge wallEdge,
        Vector3 hitPoint)
    {
        int doorLength = Mathf.Max(1, definition.edgeLength);

        Vector3 gridOrigin = gridManager.transform.position;
        Vector3 localHit = hitPoint - gridOrigin;
        float cellSize = gridManager.CellSize;

        float hitCoordinate;
        int wallStartCoordinate;

        if (wallEdge.direction == EdgeDirection.Horizontal)
        {
            hitCoordinate = localHit.x / cellSize;
            wallStartCoordinate = wallEdge.cell.x;
        }
        else
        {
            hitCoordinate = localHit.z / cellSize;
            wallStartCoordinate = wallEdge.cell.y;
        }

        float relativeHit = hitCoordinate - wallStartCoordinate;
        float desiredStart = relativeHit - doorLength * 0.5f;

        int offset = Mathf.RoundToInt(desiredStart);

        if (doorLength == 1)
            offset = Mathf.FloorToInt(relativeHit);

        int startCoordinate = wallStartCoordinate + offset;
        Vector2Int startCell = wallEdge.cell;

        if (wallEdge.direction == EdgeDirection.Horizontal)
            startCell.x = startCoordinate;
        else
            startCell.y = startCoordinate;

        return new GridEdge(startCell, wallEdge.direction);
    }

    private void BuildPreviewEdgeList()
    {
        previewEdges.Clear();

        if (definition == null)
            return;

        int length = Mathf.Max(1, definition.edgeLength);

        for (int i = 0; i < length; i++)
        {
            GridEdge edge = currentEdge;

            if (currentEdge.direction == EdgeDirection.Horizontal)
                edge.cell += new Vector2Int(i, 0);
            else
                edge.cell += new Vector2Int(0, i);

            previewEdges.Add(edge);
        }
    }

    private void UpdatePreview()
    {
        if (previewObject == null)
            return;

        ApplyDoorTransform(previewObject, currentEdge);
        placementPreview.SetValid(AreAllPreviewEdgesValid());
    }

    private void ApplyDoorTransform(
        GameObject target,
        GridEdge firstEdge)
    {
        if (target == null)
            return;

        Vector3 position = gridManager.GridToWorld(firstEdge.cell);
        float prefabHeight = GetPrefabHeight(target);
        position.y = prefabHeight * 0.5f;

        float halfLengthOffset =
            (Mathf.Max(1, definition.edgeLength) - 1) *
            gridManager.CellSize * 0.5f;

        if (firstEdge.direction == EdgeDirection.Horizontal)
        {
            position += new Vector3(
                gridManager.CellSize * 0.5f + halfLengthOffset,
                0f,
                0f);

            target.transform.position = position;
            target.transform.rotation = Quaternion.identity;
        }
        else
        {
            position += new Vector3(
                0f,
                0f,
                gridManager.CellSize * 0.5f + halfLengthOffset);

            target.transform.position = position;
            target.transform.rotation =
                Quaternion.Euler(0f, 90f, 0f);
        }
    }

    private float GetPrefabHeight(GameObject target)
    {
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
            return 0f;

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds.size.y;
    }

    private bool AreAllPreviewEdgesValid()
    {
        if (previewEdges.Count == 0)
            return false;

        foreach (GridEdge edge in previewEdges)
        {
            if (!IsEdgeValid(edge))
                return false;
        }

        return true;
    }

    private bool IsEdgeValid(GridEdge edge)
    {
        if (!IsEdgeInsideGrid(edge) || edgeOccupancy == null)
            return false;

        if (!edgeOccupancy.TryGetOccupant(edge, out GameObject occupant))
            return false;

        if (occupant == null)
            return false;

        PlacedBuilding building = occupant.GetComponent<PlacedBuilding>();

        if (building == null || building.Definition == null)
            return false;

        if (building.Definition.placementType != PlacementType.Edge ||
            building.Definition.category != BuildCategory.Wall)
        {
            return false;
        }

        return HasRequiredFoundation(edge);
    }

    private bool HasRequiredFoundation(GridEdge edge)
    {
        if (definition.requiredSurface == BuildSurface.Any)
            return true;

        if (gridSurface == null)
            return false;

        Vector2Int cell = edge.cell;

        if (edge.direction == EdgeDirection.Horizontal)
        {
            Vector2Int firstCell = cell;
            Vector2Int secondCell = cell + Vector2Int.down;

            return
                gridSurface.GetSurface(firstCell) == definition.requiredSurface ||
                gridSurface.GetSurface(secondCell) == definition.requiredSurface;
        }

        Vector2Int firstSide = cell;
        Vector2Int secondSide = cell + Vector2Int.left;

        return
            gridSurface.GetSurface(firstSide) == definition.requiredSurface ||
            gridSurface.GetSurface(secondSide) == definition.requiredSurface;
    }

    private bool IsEdgeInsideGrid(GridEdge edge)
    {
        Vector2Int cell = edge.cell;

        if (edge.direction == EdgeDirection.Horizontal)
        {
            return
                cell.x >= 0 &&
                cell.x < gridManager.Width &&
                cell.y >= 0 &&
                cell.y <= gridManager.Height;
        }

        return
            cell.x >= 0 &&
            cell.x <= gridManager.Width &&
            cell.y >= 0 &&
            cell.y < gridManager.Height;
    }

    public void Rotate()
    {
        // Door orientation comes from the wall edge.
    }

    public void TryPlace()
    {
        if (!AreAllPreviewEdgesValid())
        {
            Debug.Log(
                "Cannot place door: all required wall edges must be valid.");
            return;
        }

        List<GameObject> wallsToRemove = new List<GameObject>();

        // Capture the walls and their foundation owners before changing
        // occupancy or destroying any wall objects.
        foreach (GridEdge edge in previewEdges)
        {
            GameObject wall = edgeOccupancy.GetOccupant(edge);

            if (wall == null)
            {
                Debug.LogError(
                    "Door placement failed: a required wall disappeared.");
                return;
            }

            if (!wallsToRemove.Contains(wall))
                wallsToRemove.Add(wall);
        }

        List<FoundationBoundaryOwnership> ownersToInherit =
            new List<FoundationBoundaryOwnership>();

        foreach (GameObject wall in wallsToRemove)
        {
            foreach (FoundationBoundaryOwnership owner
                     in FoundationBoundaryOwnership.GetOwners(wall))
            {
                if (owner != null && !ownersToInherit.Contains(owner))
                    ownersToInherit.Add(owner);
            }
        }

        // Charge before changing or removing any existing wall.
        if (!ConstructionCostUtility.TryPay(
                definition, 1, "Door construction"))
        {
            return;
        }

        GameObject placedDoor = Object.Instantiate(definition.prefab);
        ApplyDoorTransform(placedDoor, currentEdge);

        PlacedBuilding placedBuilding =
            placedDoor.AddComponent<PlacedBuilding>();

        placedBuilding.InitializeEdge(
            definition,
            currentEdge.cell,
            definition.edgeLength,
            currentEdge.direction);

        // Register the door with each foundation that owned at least
        // one replaced wall. Do this before destroying those walls.
        foreach (FoundationBoundaryOwnership owner in ownersToInherit)
            owner.RegisterBoundaryObject(placedDoor);

        // Release the replaced wall segments from edge occupancy.
        foreach (GridEdge edge in previewEdges)
            edgeOccupancy.Release(edge);

        foreach (GameObject wall in wallsToRemove)
        {
            if (wall != null)
                Object.Destroy(wall);
        }

        // The door now owns all edges across its configured length.
        foreach (GridEdge edge in previewEdges)
            edgeOccupancy.Occupy(edge, placedDoor);

        Debug.Log(
            $"Placed door: {definition.displayName} " +
            $"({previewEdges.Count} edge(s)).");
    }

    public void Cancel()
    {
    }

    public void Reset()
    {
        definition = null;
        previewObject = null;
        placementPreview = null;

        currentEdge = new GridEdge(
            Vector2Int.zero,
            EdgeDirection.Horizontal);

        previewEdges.Clear();
    }
}
