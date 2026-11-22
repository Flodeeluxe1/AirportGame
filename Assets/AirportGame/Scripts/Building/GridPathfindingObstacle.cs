using UnityEngine;

public class GridPathfindingObstacle : MonoBehaviour
{
    private GridSurface gridSurface;
    private PlacedBuilding placedBuilding;

    private bool isRegistered;
    private Vector2Int registeredOrigin;
    private int registeredWidth;
    private int registeredDepth;

    public void Initialize(
        GridSurface surface,
        PlacedBuilding building)
    {
        gridSurface = surface;
        placedBuilding = building;

        SynchronizeObstacle();
    }

    private void OnEnable()
    {
        PlacedBuilding.BuildingChanged += HandleBuildingChanged;
        SynchronizeObstacle();
    }

    private void OnDisable()
    {
        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
        ReleaseObstacle();
    }

    private void HandleBuildingChanged(PlacedBuilding building)
    {
        if (building == placedBuilding)
            SynchronizeObstacle();
    }

    private void SynchronizeObstacle()
    {
        if (gridSurface == null ||
            placedBuilding == null ||
            !placedBuilding.isActiveAndEnabled ||
            placedBuilding.Definition == null ||
            !placedBuilding.Definition.blocksPathfinding)
        {
            ReleaseObstacle();
            return;
        }

        Vector2Int origin = placedBuilding.Origin;
        int width = placedBuilding.Width;
        int depth = placedBuilding.Depth;

        if (isRegistered &&
            registeredOrigin == origin &&
            registeredWidth == width &&
            registeredDepth == depth)
        {
            return;
        }

        gridSurface.ReleaseObstacleOwner(gameObject);
        gridSurface.SetObstacleArea(origin, width, depth, gameObject);

        registeredOrigin = origin;
        registeredWidth = width;
        registeredDepth = depth;
        isRegistered = true;
    }

    private void ReleaseObstacle()
    {
        if (gridSurface != null && isRegistered)
            gridSurface.ReleaseObstacleOwner(gameObject);

        isRegistered = false;
    }

    private void OnDestroy()
    {
        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
        ReleaseObstacle();
    }
}