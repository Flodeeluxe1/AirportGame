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
        hasSupport = false;
        isValid = false;

        // Preview colliders must not block the placement ray.
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
        hasSupport = false;
        isValid = false;

        RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

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
            currentPosition = hit.point;
            hasSupport = true;
            break;
        }

        if (!hasSupport)
        {
            placementPreview.SetValid(false);
            return;
        }

        previewObject.transform.rotation =
            Quaternion.Euler(0f, currentRotation, 0f);

        previewObject.transform.position = currentPosition;

        float lowestY = float.PositiveInfinity;

        if (previewRenderers != null)
        {
            foreach (Renderer renderer in previewRenderers)
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                lowestY = Mathf.Min(lowestY, renderer.bounds.min.y);
            }
        }

        if (!float.IsPositiveInfinity(lowestY))
        {
            Vector3 position = previewObject.transform.position;
            position.y += currentPosition.y - lowestY;
            previewObject.transform.position = position;
        }

        isValid = HasClearance();
        placementPreview.SetValid(isValid);
    }

    private bool HasClearance()
    {
        if (currentSupport == null || previewRenderers == null)
            return false;

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

        if (!hasBounds)
            return false;

        Collider[] overlaps = Physics.OverlapBox(
            bounds.center,
            bounds.extents * 0.95f,
            Quaternion.identity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        foreach (Collider overlap in overlaps)
        {
            if (overlap == null)
                continue;

            if (overlap.transform.IsChildOf(previewObject.transform))
                continue;

            PlacedBuilding otherBuilding =
                overlap.GetComponentInParent<PlacedBuilding>();

            // The supporting furniture is allowed to intersect the
            // bottom of the placed object's bounds.
            if (otherBuilding == currentSupport)
                continue;

            // Other placed objects, walls and obstacles block placement.
            return false;
        }

        return true;
    }

    public void Rotate()
    {
        if (definition == null || !definition.canRotate)
            return;

        currentRotation = (currentRotation + 90) % 360;

        if (hasSupport)
        {
            previewObject.transform.rotation =
                Quaternion.Euler(0f, currentRotation, 0f);

            isValid = HasClearance();
            placementPreview.SetValid(isValid);
        }
    }

    public void TryPlace()
    {
        if (!hasSupport || !isValid || currentSupport == null)
        {
            Debug.Log("Cannot place object: aim at the top of a supporting surface.");
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
        hasSupport = false;
        isValid = false;
        currentRotation = 0;
    }
}
