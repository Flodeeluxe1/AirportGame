
using UnityEngine;

public class WallMountedPlacementHandler : IPlacementHandler
{
    private BuildController controller;
    private GridManager gridManager;
    private EdgeOccupancy edgeOccupancy;
    private GridSurface gridSurface;

    private BuildableDefinition definition;
    private GameObject previewObject;
    private PlacementPreview placementPreview;

    private GridEdge currentEdge;
    private Vector3 currentWallNormal;

    private int currentRotation;
    private bool hasValidWallHit;
    private bool currentHeightValid;
    private float currentWallHitHeight;

    private const float WALL_GAP = 0.01f;
    private const float WALL_HEIGHT_SNAP = 0.5f;

    public void Initialize(
        BuildController controller,
        GridManager gridManager,
        GridOccupancy gridOccupancy,
        EdgeOccupancy edgeOccupancy)
    {
        this.controller = controller;
        this.gridManager = gridManager;
        this.edgeOccupancy = edgeOccupancy;

        gridSurface =
            gridManager.GetComponent<GridSurface>();
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
            EdgeDirection.Horizontal
        );

        currentWallNormal = Vector3.forward;
        currentRotation = 0;
        hasValidWallHit = false;
        currentHeightValid = false;
        currentWallHitHeight = 0f;

        placementPreview.SetValid(false);
    }

    // Required by IPlacementHandler.
    // BuildController uses the Ray overload below for wall-mounted objects.
    public void UpdatePlacement(Vector3 hitPoint)
    {
    }

    public void UpdatePlacement(Ray mouseRay)
    {
        if (previewObject == null ||
            placementPreview == null)
        {
            return;
        }

        hasValidWallHit = false;

        RaycastHit[] hits =
            Physics.RaycastAll(mouseRay, 1000f);

        System.Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance)
        );

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
                continue;

            PlacedBuilding wall =
                hit.collider.GetComponentInParent<PlacedBuilding>();

            if (wall == null ||
                wall.Definition == null)
            {
                continue;
            }

            if (wall.Definition.placementType !=
                    PlacementType.Edge ||
                wall.Definition.category !=
                    BuildCategory.Wall)
            {
                continue;
            }

            GridEdge hitEdge = new GridEdge(
                wall.Origin,
                wall.EdgeDirection
            );

            // Confirm that this exact wall still owns the edge.
            if (edgeOccupancy == null ||
                !edgeOccupancy.TryGetOccupant(
                    hitEdge,
                    out GameObject edgeOwner) ||
                edgeOwner != wall.gameObject)
            {
                continue;
            }

            currentEdge = hitEdge;
            currentWallHitHeight = hit.point.y;

            currentWallNormal =
                GetWallFaceNormal(
                    hit,
                    wall,
                    mouseRay
                );

            hasValidWallHit = true;
            break;
        }

        if (hasValidWallHit)
        {
            UpdatePreviewTransform();

            placementPreview.SetValid(
                IsEdgeValid(currentEdge) && currentHeightValid
            );
        }
        else
        {
            placementPreview.SetValid(false);
        }
    }

    private Vector3 GetWallFaceNormal(
        RaycastHit hit,
        PlacedBuilding wall,
        Ray mouseRay)
    {
        Vector3 normal = hit.normal;
        Vector3 wallCenter = hit.collider.bounds.center;

        if (currentEdge.direction ==
            EdgeDirection.Horizontal)
        {
            // A horizontal wall runs along X.
            // Its two mounting faces point along +Z and -Z.
            if (Mathf.Abs(normal.z) > 0.5f)
            {
                return normal.z >= 0f
                    ? Vector3.forward
                    : Vector3.back;
            }

            // If the ray hits the top of the wall,
            // use the camera's side of the wall.
            return mouseRay.origin.z >= wallCenter.z
                ? Vector3.forward
                : Vector3.back;
        }

        // A vertical wall runs along Z.
        // Its two mounting faces point along +X and -X.
        if (Mathf.Abs(normal.x) > 0.5f)
        {
            return normal.x >= 0f
                ? Vector3.right
                : Vector3.left;
        }

        // Fallback if the ray hits the top of the wall.
        return mouseRay.origin.x >= wallCenter.x
            ? Vector3.right
            : Vector3.left;
    }

    private void UpdatePreviewTransform()
    {
        if (previewObject == null)
            return;

        ApplyWallMountedTransform(previewObject);
    }

    private bool IsEdgeValid(GridEdge edge)
    {
        if (!IsEdgeInsideGrid(edge))
            return false;

        if (edgeOccupancy == null)
            return false;

        if (!edgeOccupancy.TryGetOccupant(
                edge,
                out GameObject occupant))
        {
            return false;
        }

        if (occupant == null)
            return false;

        PlacedBuilding building =
            occupant.GetComponent<PlacedBuilding>();

        if (building == null ||
            building.Definition == null)
        {
            return false;
        }

        if (building.Definition.placementType !=
                PlacementType.Edge ||
            building.Definition.category !=
                BuildCategory.Wall)
        {
            return false;
        }

        return HasRequiredFoundation(edge);
    }

    private bool HasRequiredFoundation(GridEdge edge)
    {
        if (definition.requiredSurface ==
            BuildSurface.Any)
        {
            return true;
        }

        if (gridSurface == null)
            return false;

        Vector2Int cell = edge.cell;

        if (edge.direction ==
            EdgeDirection.Horizontal)
        {
            Vector2Int firstCell = cell;
            Vector2Int secondCell =
                cell + Vector2Int.down;

            return
                gridSurface.GetSurface(firstCell) ==
                    definition.requiredSurface ||
                gridSurface.GetSurface(secondCell) ==
                    definition.requiredSurface;
        }

        Vector2Int firstSide = cell;
        Vector2Int secondSide =
            cell + Vector2Int.left;

        return
            gridSurface.GetSurface(firstSide) ==
                definition.requiredSurface ||
            gridSurface.GetSurface(secondSide) ==
                definition.requiredSurface;
    }

    private bool IsEdgeInsideGrid(GridEdge edge)
    {
        Vector2Int cell = edge.cell;

        if (edge.direction ==
            EdgeDirection.Horizontal)
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

    private void ApplyWallMountedTransform(GameObject target)
    {
        if (target == null)
            return;

        Vector3 position =
            gridManager.GridToWorld(currentEdge.cell);

        if (currentEdge.direction ==
            EdgeDirection.Horizontal)
        {
            position.x += gridManager.CellSize * 0.5f;
        }
        else
        {
            position.z += gridManager.CellSize * 0.5f;
        }

        target.transform.rotation =
            Quaternion.LookRotation(currentWallNormal, Vector3.up) *
            Quaternion.Euler(0f, currentRotation, 0f);

        currentHeightValid =
            TryGetSnappedWallHeight(target, out float snappedHeight);

        position.y = snappedHeight;

        float wallOffset =
            GetWallMountedOffset(target, currentWallNormal);

        position += currentWallNormal * wallOffset;
        target.transform.position = position;
    }

    private bool TryGetSnappedWallHeight(
        GameObject target,
        out float snappedHeight)
    {
        snappedHeight = currentWallHitHeight;

        if (edgeOccupancy == null ||
            !edgeOccupancy.TryGetOccupant(currentEdge, out GameObject wallObject) ||
            wallObject == null ||
            !TryGetCombinedBounds(wallObject, out Bounds wallBounds) ||
            !TryGetCombinedBounds(target, out Bounds objectBounds))
        {
            return false;
        }

        float wallBottom = wallBounds.min.y;
        float wallTop = wallBounds.max.y;

        float objectBottomOffset =
            objectBounds.min.y - target.transform.position.y;
        float objectTopOffset =
            objectBounds.max.y - target.transform.position.y;

        int minimumStep = Mathf.Max(
            1,
            Mathf.CeilToInt(-objectBottomOffset / WALL_HEIGHT_SNAP)
        );

        int maximumStep = Mathf.FloorToInt(
            (wallTop - wallBottom - objectTopOffset) / WALL_HEIGHT_SNAP
        );

        if (maximumStep < minimumStep)
            return false;

        int requestedStep = Mathf.RoundToInt(
            (currentWallHitHeight - wallBottom) / WALL_HEIGHT_SNAP
        );

        int snappedStep = Mathf.Clamp(
            requestedStep,
            minimumStep,
            maximumStep
        );

        snappedHeight = wallBottom + snappedStep * WALL_HEIGHT_SNAP;
        return true;
    }

    private bool TryGetCombinedBounds(
        GameObject target,
        out Bounds combinedBounds)
    {
        combinedBounds = default;
        if (target == null)
            return false;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                combinedBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
            return true;

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        foreach (Collider targetCollider in colliders)
        {
            if (targetCollider == null)
                continue;

            if (!hasBounds)
            {
                combinedBounds = targetCollider.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(targetCollider.bounds);
            }
        }

        return hasBounds;
    }

    private float GetWallMountedOffset(
        GameObject target,
        Vector3 wallNormal)
    {
        Debug.Log(
            $"[WallMount DEBUG] Edge: {currentEdge.cell}, " +
            $"Direction: {currentEdge.direction}, " +
            $"Normal: {wallNormal}"
        );

        if (edgeOccupancy == null ||
            !edgeOccupancy.TryGetOccupant(
                currentEdge,
                out GameObject wallObject) ||
            wallObject == null)
        {
            Debug.LogWarning(
                "[WallMount DEBUG] Wall object not found. " +
                $"Using fallback gap: {WALL_GAP:F4} m"
            );

            return WALL_GAP;
        }

        float wallThickness =
            GetObjectThickness(
                wallObject,
                wallNormal
            );

        float objectThickness =
            GetObjectThickness(
                target,
                wallNormal
            );

        float halfWall = wallThickness * 0.5f;
        float halfDisplay = objectThickness * 0.5f;

        float finalOffset =
            halfWall + halfDisplay + WALL_GAP;

        Debug.Log(
            $"[WallMount DEBUG] Wall: {wallObject.name}\n" +
            $"Wall thickness: {wallThickness:F4} m\n" +
            $"Display: {target.name}\n" +
            $"Display thickness: {objectThickness:F4} m\n" +
            $"Half wall: {halfWall:F4} m\n" +
            $"Half display: {halfDisplay:F4} m\n" +
            $"Gap: {WALL_GAP:F4} m\n" +
            $"FINAL OFFSET: {finalOffset:F4} m"
        );

        return finalOffset;
    }




    private float GetObjectThickness(
        GameObject target,
        Vector3 normal)
    {
        if (target == null)
            return 0f;

        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(
                true
            );

        if (renderers.Length == 0)
        {
            Debug.LogWarning(
                $"[WallMount DEBUG] {target.name} has no renderers."
            );

            return 0f;
        }

        float minProjection = float.PositiveInfinity;
        float maxProjection = float.NegativeInfinity;

        Debug.Log(
            $"[WallMount DEBUG] Measuring: {target.name}, " +
            $"Normal: {normal}, " +
            $"Renderer count: {renderers.Length}"
        );

        foreach (Renderer renderer in renderers)
        {
            Bounds bounds = renderer.bounds;

            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            float centerProjection =
                Vector3.Dot(center, normal);

            float projectedExtent =
                Mathf.Abs(normal.x) * extents.x +
                Mathf.Abs(normal.y) * extents.y +
                Mathf.Abs(normal.z) * extents.z;

            float rendererMin =
                centerProjection - projectedExtent;

            float rendererMax =
                centerProjection + projectedExtent;

            Debug.Log(
                $"[WallMount DEBUG] Renderer: {renderer.name}\n" +
                $"Bounds center: {center}\n" +
                $"Bounds size: {bounds.size}\n" +
                $"Bounds extents: {extents}\n" +
                $"Center projection: {centerProjection:F4}\n" +
                $"Projected extent: {projectedExtent:F4}\n" +
                $"Projection range: {rendererMin:F4} to {rendererMax:F4}"
            );

            minProjection = Mathf.Min(
                minProjection,
                rendererMin
            );

            maxProjection = Mathf.Max(
                maxProjection,
                rendererMax
            );
        }

        float thickness = maxProjection - minProjection;

        Debug.Log(
            $"[WallMount DEBUG] {target.name} " +
            $"MEASURED THICKNESS: {thickness:F4} m"
        );

        return thickness;
    }



    public void Rotate()
    {
        currentRotation += 180;

        if (currentRotation >= 360)
            currentRotation = 0;

        if (hasValidWallHit)
            UpdatePreviewTransform();
    }



    public void TryPlace()
    {
        if (!hasValidWallHit || !IsEdgeValid(currentEdge) || !currentHeightValid)
        {
            Debug.Log(
                "Cannot place display: aim directly at a valid wall."
            );

            return;
        }

        if (!ConstructionCostUtility.TryPay(
                definition, 1, "Wall-mounted construction"))
        {
            return;
        }

        GameObject placedObject = Object.Instantiate(
            definition.prefab,
            previewObject.transform.position,
            previewObject.transform.rotation
        );

        PlacedBuilding placedBuilding =
            placedObject.AddComponent<PlacedBuilding>();

        placedBuilding.InitializeEdge(
            definition,
            currentEdge.cell,
            1,
            currentEdge.direction
        );

        Debug.Log(
            $"Placed wall-mounted object: {definition.displayName}"
        );
    }



    public void Cancel()
    {
        hasValidWallHit = false;
        currentHeightValid = false;
    }

    public void Reset()
    {
        definition = null;
        previewObject = null;
        placementPreview = null;

        currentEdge = new GridEdge(
            Vector2Int.zero,
            EdgeDirection.Horizontal
        );

        currentWallNormal = Vector3.forward;
        currentRotation = 0;
        hasValidWallHit = false;
        currentHeightValid = false;
        currentWallHitHeight = 0f;
    }
}
