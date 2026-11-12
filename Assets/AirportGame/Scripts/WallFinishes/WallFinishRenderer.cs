using UnityEngine;

public class WallFinishRenderer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WallFinishCatalogue catalogue;
    [SerializeField] private GridSurface gridSurface;

    public WallFinishCatalogue Catalogue => catalogue;

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (gridSurface == null)
        {
            GridManager gridManager =
                FindFirstObjectByType<GridManager>();

            if (gridManager != null)
                gridSurface = gridManager.GetComponent<GridSurface>();
        }
    }

    public bool SetFinish(
        PlacedBuilding wall,
        int visibleFaceSign,
        WallFinishDefinition finish)
    {
        WallFinishSurface surface = GetOrCreateSurface(wall);
        if (surface == null)
            return false;

        surface.SetFinish(
            surface.IsInnerFace(visibleFaceSign),
            finish);

        return true;
    }

    public void ShowPreview(
        PlacedBuilding wall,
        int visibleFaceSign,
        WallFinishDefinition finish,
        bool erase)
    {
        WallFinishSurface surface = GetOrCreateSurface(wall);
        if (surface == null)
            return;

        surface.ShowPreview(
            visibleFaceSign,
            finish,
            erase);
    }

    public void HidePreview(PlacedBuilding wall)
    {
        if (wall == null)
            return;

        WallFinishSurface surface =
            wall.GetComponent<WallFinishSurface>();

        if (surface != null)
            surface.HidePreview();
    }

    public void RestoreFinish(
        PlacedBuilding wall,
        string innerFinishId,
        string outerFinishId)
    {
        if (string.IsNullOrWhiteSpace(innerFinishId) &&
            string.IsNullOrWhiteSpace(outerFinishId))
        {
            return;
        }

        WallFinishSurface surface = GetOrCreateSurface(wall);
        if (surface != null)
            surface.SetFinishIds(innerFinishId, outerFinishId);
    }

    public string GetInnerFinishId(PlacedBuilding wall)
    {
        WallFinishSurface surface =
            wall != null ? wall.GetComponent<WallFinishSurface>() : null;

        return surface != null ? surface.InnerFinishId : "";
    }

    public string GetOuterFinishId(PlacedBuilding wall)
    {
        WallFinishSurface surface =
            wall != null ? wall.GetComponent<WallFinishSurface>() : null;

        return surface != null ? surface.OuterFinishId : "";
    }

    public int GetVisibleSideSign(
        PlacedBuilding wall,
        Vector3 cameraPosition)
    {
        if (wall == null)
            return 1;

        return Vector3.Dot(
            cameraPosition - wall.transform.position,
            wall.transform.forward) >= 0f ? 1 : -1;
    }

    public int GetInnerSideSign(PlacedBuilding wall)
    {
        if (wall == null)
            return 1;

        ResolveReferences();

        if (gridSurface == null || !wall.IsEdgeBuilding)
            return 1;

        Vector2Int positiveSideCell = wall.Origin;
        Vector2Int negativeSideCell = wall.Origin;

        if (wall.EdgeDirection == EdgeDirection.Horizontal)
            negativeSideCell += Vector2Int.down;
        else
            negativeSideCell += Vector2Int.left;

        bool positiveHasFoundation =
            gridSurface.GetSurface(positiveSideCell) ==
            BuildSurface.Foundation;

        bool negativeHasFoundation =
            gridSurface.GetSurface(negativeSideCell) ==
            BuildSurface.Foundation;

        if (positiveHasFoundation && !negativeHasFoundation)
            return 1;

        if (negativeHasFoundation && !positiveHasFoundation)
            return -1;

        // When both adjacent cells are foundations (an interior partition),
        // use the positive face as the stable inner-side convention.
        return 1;
    }

    public void ClearAllFinishes()
    {
        WallFinishSurface[] surfaces =
            FindObjectsByType<WallFinishSurface>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        foreach (WallFinishSurface surface in surfaces)
        {
            if (surface != null)
                surface.SetFinishIds("", "");
        }
    }

    private WallFinishSurface GetOrCreateSurface(PlacedBuilding wall)
    {
        if (wall == null ||
            wall.Definition == null ||
            wall.Definition.category != BuildCategory.Wall ||
            !wall.IsEdgeBuilding)
        {
            return null;
        }

        if (catalogue == null || catalogue.SharedRenderMaterial == null)
        {
            Debug.LogError(
                "WallFinishRenderer: WallFinishCatalogue or its shared render " +
                "material is missing. Run Airport/Wall Finishes/Build Catalogue " +
                "From Project Materials in the Unity Editor.",
                this);
            return null;
        }

        WallFinishSurface surface =
            wall.GetComponent<WallFinishSurface>();

        if (surface == null)
            surface = wall.gameObject.AddComponent<WallFinishSurface>();

        surface.Initialize(
            catalogue,
            catalogue.SharedRenderMaterial,
            GetInnerSideSign(wall));

        return surface;
    }
}
