using UnityEngine;

/// <summary>
/// Places small furniture and decorations freely on top of placed buildings
/// that explicitly allow surface placement.
/// </summary>
public class SurfacePlacementHandler : IPlacementHandler
{
    private BuildableDefinition definition;
    private GameObject previewObject;
    private PlacementPreview placementPreview;
    private Renderer[] previewRenderers;
    private PlacedBuilding currentSupport;
    private Collider currentSupportCollider;
    private Vector3 currentPosition;
    private int currentRotation;
    private bool hasSupport;
    private bool isValid;

    public void Initialize(
        BuildController controller,
        GridManager gridManager,
        GridOccupancy gridOccupancy,
        EdgeOccupancy edgeOccupancy)
    {
    }

    public void BeginPlacement(
        BuildableDefinition definition,
        GameObject previewObject,
        PlacementPreview placementPreview)
    {
        this.definition = definition;
        this.previewObject = previewObject;
        this.placementPreview = placementPreview;
        previewRenderers = previewObject.GetComponentsInChildren<Renderer>(true);
        currentRotation = 0;
        currentSupport = null;
        currentSupportCollider = null;
        hasSupport = false;
        isValid = false;

        // Preview colliders must not block the placement ray or overlap tests.
        foreach (Collider previewCollider in
                 previewObject.GetComponentsInChildren<Collider>(true))
        {
            previewCollider.enabled = false;
        }

        placementPreview.SetValid(false);
    }

    public void UpdatePlacement(Vector3 hitPoint)
    {
        // Not used by this handler. BuildController supplies the full ray.
    }

    public void UpdatePlacementFromRay(Ray ray)
    {
        currentSupport = null;
        currentSupportCollider = null;
        hasSupport = false;
        isValid = false;

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            1000f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        System.Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.normal.y < 0.5f)
                continue;

            PlacedBuilding candidate =
                hit.collider.GetComponentInParent<PlacedBuilding>();

            if (candidate == null ||
                candidate.Definition == null ||
                !candidate.Definition.canSupportSurfaceObjects)
            {
                continue;
            }

            currentSupport = candidate;
            currentSupportCollider = hit.collider;
            currentPosition = hit.point;
            hasSupport = true;
            break;
        }

        if (!hasSupport)
        {
            placementPreview.SetValid(false);
            return;
        }

        UpdatePreviewTransform();

        isValid = HasClearance();
        placementPreview.SetValid(isValid);
    }

    private void UpdatePreviewTransform()
    {
        if (previewObject == null || currentSupport == null)
            return;

        // Surface objects rotate relative to the supporting furniture.
        previewObject.transform.rotation =
            currentSupport.transform.rotation *
            Quaternion.Euler(0f, currentRotation, 0f);

        previewObject.transform.position = currentPosition;

        if (!TryGetPreviewLocalBounds(out Bounds localBounds))
            return;

        // Keep the lowest visible point resting on the hit surface.
        Vector3 lowestLocalPoint = new Vector3(
            localBounds.center.x,
            localBounds.min.y,
            localBounds.center.z);

        float lowestWorldY =
            previewObject.transform.TransformPoint(lowestLocalPoint).y;

        Vector3 position = previewObject.transform.position;
        position.y += currentPosition.y - lowestWorldY;
        previewObject.transform.position = position;
    }

    private bool TryGetPreviewLocalBounds(out Bounds localBounds)
    {
        localBounds = new Bounds();
        bool hasBounds = false;

        if (previewObject == null || previewRenderers == null)
            return false;

        Transform previewTransform = previewObject.transform;

        foreach (Renderer renderer in previewRenderers)
        {
            if (renderer == null || !renderer.enabled)
                continue;

            Bounds rendererLocalBounds = renderer.localBounds;

            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                Vector3 localCorner = new Vector3(
                    (cornerIndex & 1) == 0
                        ? rendererLocalBounds.min.x
                        : rendererLocalBounds.max.x,
                    (cornerIndex & 2) == 0
                        ? rendererLocalBounds.min.y
                        : rendererLocalBounds.max.y,
                    (cornerIndex & 4) == 0
                        ? rendererLocalBounds.min.z
                        : rendererLocalBounds.max.z);

                Vector3 worldCorner =
                    renderer.transform.TransformPoint(localCorner);

                Vector3 previewLocalCorner =
                    previewTransform.InverseTransformPoint(worldCorner);

                if (!hasBounds)
                {
                    localBounds = new Bounds(previewLocalCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(previewLocalCorner);
                }
            }
        }

        return hasBounds;
    }

    private bool HasClearance()
    {
        if (currentSupport == null ||
            currentSupportCollider == null ||
            previewRenderers == null ||
            !TryGetPreviewLocalBounds(out Bounds localBounds))
        {
            return false;
        }

        if (!FitsOnSupportingSurface(localBounds))
            return false;

        Bounds worldBounds = GetPreviewWorldBounds();

        if (worldBounds.size == Vector3.zero)
            return false;

        Bounds candidateBounds =
            new Bounds(worldBounds.center, worldBounds.size * 0.95f);

        // Prevent overlap with other surface-placed objects, including objects
        // whose imported prefabs do not contain colliders.
        foreach (PlacedBuilding other in PlacedBuilding.ActiveBuildings)
        {
            if (other == null ||
                other == currentSupport ||
                !other.IsSurfacePlaced)
            {
                continue;
            }

            Renderer[] otherRenderers =
                other.GetComponentsInChildren<Renderer>(true);

            bool hasOtherBounds = false;
            Bounds otherBounds = new Bounds(
                other.transform.position,
                Vector3.zero);

            foreach (Renderer otherRenderer in otherRenderers)
            {
                if (otherRenderer == null || !otherRenderer.enabled)
                    continue;

                if (!hasOtherBounds)
                {
                    otherBounds = otherRenderer.bounds;
                    hasOtherBounds = true;
                }
                else
                {
                    otherBounds.Encapsulate(otherRenderer.bounds);
                }
            }

            if (hasOtherBounds && candidateBounds.Intersects(otherBounds))
                return false;
        }

        // Check physical colliders as well, so surface objects cannot overlap
        // walls or other placed furniture. The supporting furniture itself is
        // allowed to intersect the bottom of the preview.
        Collider[] overlaps = Physics.OverlapBox(
            worldBounds.center,
            worldBounds.extents * 0.95f,
            Quaternion.identity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        foreach (Collider overlap in overlaps)
        {
            if (overlap == null ||
                overlap.transform.IsChildOf(previewObject.transform))
            {
                continue;
            }

            PlacedBuilding otherBuilding =
                overlap.GetComponentInParent<PlacedBuilding>();

            if (otherBuilding == currentSupport)
                continue;

            return false;
        }

        return true;
    }

    private Bounds GetPreviewWorldBounds()
    {
        Bounds bounds = new Bounds(
            previewObject.transform.position,
            Vector3.zero);

        bool hasBounds = false;

        foreach (Renderer renderer in previewRenderers)
        {
            if (renderer == null || !renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

    private bool FitsOnSupportingSurface(Bounds localBounds)
    {
        if (currentSupportCollider == null || previewObject == null)
            return false;

        // Test the actual oriented footprint instead of the world-axis-aligned
        // renderer bounds. This allows placement on rotated tables.
        Vector3[] localCorners =
        {
            new Vector3(localBounds.min.x, localBounds.min.y, localBounds.min.z),
            new Vector3(localBounds.min.x, localBounds.min.y, localBounds.max.z),
            new Vector3(localBounds.max.x, localBounds.min.y, localBounds.min.z),
            new Vector3(localBounds.max.x, localBounds.min.y, localBounds.max.z),
            new Vector3(localBounds.center.x, localBounds.min.y, localBounds.center.z)
        };

        const float horizontalTolerance = 0.02f;
        float toleranceSquared =
            horizontalTolerance * horizontalTolerance;

        foreach (Vector3 localCorner in localCorners)
        {
            Vector3 worldCorner =
                previewObject.transform.TransformPoint(localCorner);

            // ClosestPoint returns the original point when it is outside a
            // collider in any axis. Move the test point into the collider's
            // vertical range so its X/Z footprint can be tested correctly.
            worldCorner.y = currentSupportCollider.bounds.center.y;

            Vector3 closest =
                currentSupportCollider.ClosestPoint(worldCorner);

            float dx = worldCorner.x - closest.x;
            float dz = worldCorner.z - closest.z;

            if (dx * dx + dz * dz > toleranceSquared)
                return false;
        }

        return true;
    }

    public void Rotate()
    {
        if (definition == null || !definition.canRotate)
            return;

        currentRotation = (currentRotation + 90) % 360;

        if (!hasSupport)
            return;

        UpdatePreviewTransform();

        isValid = HasClearance();
        placementPreview.SetValid(isValid);
    }

    public void TryPlace()
    {
        if (!hasSupport ||
            !isValid ||
            currentSupport == null ||
            currentSupportCollider == null ||
            currentSupport.Definition == null ||
            !currentSupport.Definition.canSupportSurfaceObjects)
        {
            Debug.Log(
                "Cannot place object: aim at the top of a supporting surface.");
            return;
        }

        if (!ConstructionCostUtility.TryPay(
                definition, 1, "Building construction"))
        {
            return;
        }

        GameObject placedObject = Object.Instantiate(
            definition.prefab,
            previewObject.transform.position,
            previewObject.transform.rotation);

        PlacedBuilding placedBuilding =
            placedObject.GetComponent<PlacedBuilding>();

        if (placedBuilding == null)
            placedBuilding = placedObject.AddComponent<PlacedBuilding>();

        placedBuilding.InitializeSurface(definition, currentRotation);
        placedBuilding.SetSupportingSurface(currentSupport);

        Debug.Log(
            $"Placed {definition.displayName} on " +
            $"{currentSupport.Definition.displayName}. " +
            $"Cost: ${definition.buildCost:N0}.");
    }

    public void Cancel()
    {
    }

    public void Reset()
    {
        definition = null;
        previewObject = null;
        placementPreview = null;
        previewRenderers = null;
        currentSupport = null;
        currentSupportCollider = null;
        hasSupport = false;
        isValid = false;
        currentRotation = 0;
    }
}
