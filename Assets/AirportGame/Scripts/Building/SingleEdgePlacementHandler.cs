using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places one edge-based buildable across its configured edge length.
/// Unlike wall drawing, placement is a single click on any free grid edge.
/// </summary>
public sealed class SingleEdgePlacementHandler : IPlacementHandler
{
    private BuildController controller;
    private GridManager gridManager;
    private EdgeOccupancy edgeOccupancy;
    private GridSurface gridSurface;

    private BuildableDefinition definition;
    private GameObject previewObject;
    private PlacementPreview placementPreview;
    private Renderer[] previewRenderers;

    private GridEdge currentEdge;
    private EdgeDirection manualDirection;
    private bool hasManualDirection;
    private readonly RaycastHit[] fenceHitBuffer = new RaycastHit[32];
    private readonly List<GridEdge> previewEdges = new List<GridEdge>();
    private readonly List<GameObject> fencesToReplace = new List<GameObject>();

    public void Initialize(
        BuildController controller,
        GridManager gridManager,
        GridOccupancy gridOccupancy,
        EdgeOccupancy edgeOccupancy)
    {
        this.controller = controller;
        this.gridManager = gridManager;
        this.edgeOccupancy = edgeOccupancy;
        gridSurface = gridManager != null
            ? gridManager.GetComponent<GridSurface>()
            : null;
    }

    public void BeginPlacement(
        BuildableDefinition definition,
        GameObject previewObject,
        PlacementPreview placementPreview)
    {
        this.definition = definition;
        this.previewObject = previewObject;
        this.placementPreview = placementPreview;
        previewRenderers = previewObject != null
            ? previewObject.GetComponentsInChildren<Renderer>(true)
            : null;

        currentEdge = new GridEdge(Vector2Int.zero, EdgeDirection.Horizontal);
        manualDirection = EdgeDirection.Horizontal;
        hasManualDirection = false;
        previewEdges.Clear();

        if (placementPreview != null)
            placementPreview.SetValid(false);
    }

    public void UpdatePlacement(Vector3 hitPoint)
    {
        UpdatePlacement(hitPoint, null);
    }

    public void UpdatePlacement(Ray mouseRay, Vector3 hitPoint)
    {
        EdgeDirection? fenceDirection = FindFenceDirection(mouseRay);
        UpdatePlacement(hitPoint, fenceDirection);
    }

    private void UpdatePlacement(Vector3 hitPoint, EdgeDirection? fenceDirection)
    {
        if (definition == null ||
            previewObject == null ||
            placementPreview == null ||
            gridManager == null)
        {
            return;
        }

        float cellSize = gridManager.CellSize;
        if (cellSize <= 0f)
        {
            placementPreview.SetValid(false);
            return;
        }

        Vector3 localHit = hitPoint - gridManager.transform.position;
        float gridX = localHit.x / cellSize;
        float gridZ = localHit.z / cellSize;

        // Keep a stable default orientation until the player rotates.
        // Fence alignment temporarily overrides this while a fence is hit.
        EdgeDirection direction = fenceDirection ??
            (hasManualDirection ? manualDirection : EdgeDirection.Horizontal);

        currentEdge = GetCenteredStartEdge(gridX, gridZ, direction);

        BuildPreviewEdges();
        ApplyEdgeTransform(previewObject, currentEdge, previewRenderers);

        placementPreview.SetValid(AreAllEdgesValid());
    }

    private GridEdge GetCenteredStartEdge(
        float gridX,
        float gridZ,
        EdgeDirection direction)
    {
        int length = Mathf.Max(1, definition.edgeLength);
        int startCoordinate;

        if (direction == EdgeDirection.Horizontal)
        {
            float desiredStart = gridX - length * 0.5f;
            startCoordinate = Mathf.RoundToInt(desiredStart);

            return new GridEdge(
                new Vector2Int(startCoordinate, Mathf.RoundToInt(gridZ)),
                direction);
        }

        {
            float desiredStart = gridZ - length * 0.5f;
            startCoordinate = Mathf.RoundToInt(desiredStart);

            return new GridEdge(
                new Vector2Int(Mathf.RoundToInt(gridX), startCoordinate),
                direction);
        }
    }

    private void BuildPreviewEdges()
    {
        previewEdges.Clear();

        int length = Mathf.Max(1, definition.edgeLength);
        for (int i = 0; i < length; i++)
        {
            Vector2Int cell = currentEdge.cell;

            if (currentEdge.direction == EdgeDirection.Horizontal)
                cell.x += i;
            else
                cell.y += i;

            previewEdges.Add(new GridEdge(cell, currentEdge.direction));
        }
    }

    private bool AreAllEdgesValid()
    {
        if (previewEdges.Count == 0 ||
            edgeOccupancy == null ||
            gridSurface == null)
        {
            return false;
        }

        for (int i = 0; i < previewEdges.Count; i++)
        {
            GridEdge edge = previewEdges[i];

            if (!IsEdgeInsideGrid(edge) ||
                !HasRequiredFoundation(edge))
            {
                return false;
            }

            if (!edgeOccupancy.CanOccupy(edge))
            {
                GameObject occupant = edgeOccupancy.GetOccupant(edge);
                if (!IsReplaceableFence(occupant))
                    return false;
            }
        }

        return true;
    }

    private bool IsReplaceableFence(GameObject candidate)
    {
        if (candidate == null)
            return false;

        PlacedBuilding building =
            candidate.GetComponent<PlacedBuilding>();

        return building != null &&
               building.IsEdgeBuilding &&
               building.Definition != null &&
               building.Definition.category == BuildCategory.Fence;
    }

    private bool IsEdgeInsideGrid(GridEdge edge)
    {
        Vector2Int cell = edge.cell;

        if (edge.direction == EdgeDirection.Horizontal)
        {
            return cell.x >= 0 &&
                   cell.x < gridManager.Width &&
                   cell.y >= 0 &&
                   cell.y <= gridManager.Height;
        }

        return cell.x >= 0 &&
               cell.x <= gridManager.Width &&
               cell.y >= 0 &&
               cell.y < gridManager.Height;
    }

    private bool HasRequiredFoundation(GridEdge edge)
    {
        Vector2Int firstCell = edge.cell;
        Vector2Int secondCell = edge.direction == EdgeDirection.Horizontal
            ? edge.cell + Vector2Int.down
            : edge.cell + Vector2Int.left;

        return definition.AllowsSurface(gridSurface.GetSurface(firstCell)) ||
               definition.AllowsSurface(gridSurface.GetSurface(secondCell));
    }

    private void ApplyEdgeTransform(
        GameObject target,
        GridEdge firstEdge,
        Renderer[] renderers)
    {
        if (target == null)
            return;

        Vector3 position = gridManager.GridToWorld(firstEdge.cell);
        float floorY = position.y;

        float halfLengthOffset =
            (Mathf.Max(1, definition.edgeLength) - 1) *
            gridManager.CellSize * 0.5f;

        Quaternion edgeRotation;

        if (firstEdge.direction == EdgeDirection.Horizontal)
        {
            position += new Vector3(
                gridManager.CellSize * 0.5f + halfLengthOffset,
                0f,
                0f);
            edgeRotation = Quaternion.identity;
        }
        else
        {
            position += new Vector3(
                0f,
                0f,
                gridManager.CellSize * 0.5f + halfLengthOffset);
            edgeRotation = Quaternion.Euler(0f, 90f, 0f);
        }

        target.transform.rotation = edgeRotation;

        // Some long edge prefabs (including FenceGate1) have their root
        // pivot at one end rather than at the visual/collider center.
        // Offset the root so the collider center lands on the grid-edge center.
        BoxCollider boxCollider = target.GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            Vector3 localCenterXZ = new Vector3(
                boxCollider.center.x,
                0f,
                boxCollider.center.z);
            position -= edgeRotation * localCenterXZ;
        }

        target.transform.position = position;

        if (renderers == null || renderers.Length == 0)
            return;

        float bottomY = renderers[0].bounds.min.y;
        for (int i = 1; i < renderers.Length; i++)
            bottomY = Mathf.Min(bottomY, renderers[i].bounds.min.y);

        if (!float.IsNaN(bottomY) && !float.IsInfinity(bottomY))
        {
            position.y += floorY - bottomY;
            target.transform.position = position;
        }
    }

    private EdgeDirection? FindFenceDirection(Ray mouseRay)
    {
        int hitCount = Physics.RaycastNonAlloc(
            mouseRay,
            fenceHitBuffer,
            1000f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        EdgeDirection? foundDirection = null;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = fenceHitBuffer[i];
            Collider hitCollider = hit.collider;

            if (hitCollider == null ||
                (previewObject != null &&
                 hitCollider.transform.IsChildOf(previewObject.transform)))
            {
                continue;
            }

            PlacedBuilding building =
                hitCollider.GetComponentInParent<PlacedBuilding>();

            if (building == null ||
                building.Definition == null ||
                building.Definition.category != BuildCategory.Fence ||
                !building.IsEdgeBuilding ||
                hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            foundDirection = building.EdgeDirection;
        }

        return foundDirection;
    }

    public void Rotate()
    {
        EdgeDirection baseDirection = hasManualDirection
            ? manualDirection
            : currentEdge.direction;

        manualDirection = baseDirection == EdgeDirection.Horizontal
            ? EdgeDirection.Vertical
            : EdgeDirection.Horizontal;

        hasManualDirection = true;
    }

    public void TryPlace()
    {
        if (!AreAllEdgesValid())
        {
            if (placementPreview != null)
                placementPreview.SetValid(false);
            return;
        }

        if (!ConstructionCostUtility.TryPay(
                definition,
                1,
                "Fence gate construction"))
        {
            return;
        }

        // Gather each existing fence only once, even if it occupies
        // multiple edges covered by this gate.
        fencesToReplace.Clear();
        for (int i = 0; i < previewEdges.Count; i++)
        {
            GameObject occupant = edgeOccupancy.GetOccupant(previewEdges[i]);
            if (IsReplaceableFence(occupant) &&
                !fencesToReplace.Contains(occupant))
            {
                fencesToReplace.Add(occupant);
            }
        }

        GameObject placedObject = Object.Instantiate(definition.prefab);
        Renderer[] placedRenderers =
            placedObject.GetComponentsInChildren<Renderer>(true);
        ApplyEdgeTransform(placedObject, currentEdge, placedRenderers);

        PlacedBuilding placedBuilding =
            placedObject.AddComponent<PlacedBuilding>();

        placedBuilding.InitializeEdge(
            definition,
            currentEdge.cell,
            definition.edgeLength,
            currentEdge.direction);

        // Release all edges owned by each replaced fence before assigning
        // the gate's edges. Destroy removes the complete fence object.
        for (int i = 0; i < fencesToReplace.Count; i++)
        {
            GameObject fence = fencesToReplace[i];
            edgeOccupancy.ReleaseBuilding(fence);
            Object.Destroy(fence);
        }

        for (int i = 0; i < previewEdges.Count; i++)
            edgeOccupancy.Occupy(previewEdges[i], placedObject);

        fencesToReplace.Clear();

        Debug.Log(
            $"Placed {definition.displayName} across {previewEdges.Count} edge(s).");
    }

    public void Cancel()
    {
        previewEdges.Clear();
        fencesToReplace.Clear();
    }

    public void Reset()
    {
        definition = null;
        previewObject = null;
        placementPreview = null;
        previewRenderers = null;
        currentEdge = new GridEdge(Vector2Int.zero, EdgeDirection.Horizontal);
        manualDirection = EdgeDirection.Horizontal;
        hasManualDirection = false;
        previewEdges.Clear();
        fencesToReplace.Clear();
    }
}
