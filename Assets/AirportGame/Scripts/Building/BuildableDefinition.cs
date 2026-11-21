
using UnityEngine;

[System.Flags]
public enum BuildSurfaceOptions
{
    None = 0,
    Grass = 1 << 0,
    Foundation = 1 << 1,
    Tarmac = 1 << 2,
    Taxiway = 1 << 3,
    Runway = 1 << 4,
    PedestrianTarmac = 1 << 5,
    All = Grass | Foundation | Tarmac | Taxiway | Runway | PedestrianTarmac
}

public enum BuildCategory
{
    Floor,
    Wall,
    Door,
    Airport,
    Roads,
    Commercial,
    Taxiways,
    Tarmac,
    Pipelines,
    PedestrianTarmac
}

[System.Flags]
public enum RoomFurnitureRole
{
    None = 0,
    Chair = 2,
    Desk = 4,
    WallBoard = 8,
    Computer = 16,
    Printer = 32,
    Projector = 64,
    Book = 128
}

[CreateAssetMenu(
    fileName = "BuildableDefinition",
    menuName = "Airport/Building/Buildable Definition"
)]
public class BuildableDefinition : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName;

    [Header("Prefab")]
    public GameObject prefab;

    [Header("Placement")]
    public PlacementType placementType = PlacementType.Cell;

    [Tooltip("Select every ground surface this buildable may be placed on. Leave empty to use the legacy Required Surface setting below.")]
    public BuildSurfaceOptions allowedSurfaces = BuildSurfaceOptions.None;

    [HideInInspector]
    public BuildSurface requiredSurface = BuildSurface.Any;

    public bool AllowsAnySurface =>
        allowedSurfaces == BuildSurfaceOptions.All ||
        (allowedSurfaces == BuildSurfaceOptions.None &&
         requiredSurface == BuildSurface.Any);

    public bool AllowsSurface(BuildSurface surface)
    {
        if (AllowsAnySurface)
            return true;

        if (allowedSurfaces == BuildSurfaceOptions.None)
            return requiredSurface == surface;

        BuildSurfaceOptions option = surface switch
        {
            BuildSurface.Grass => BuildSurfaceOptions.Grass,
            BuildSurface.Foundation => BuildSurfaceOptions.Foundation,
            BuildSurface.Tarmac => BuildSurfaceOptions.Tarmac,
            BuildSurface.Taxiway => BuildSurfaceOptions.Taxiway,
            BuildSurface.Runway => BuildSurfaceOptions.Runway,
            BuildSurface.PedestrianTarmac => BuildSurfaceOptions.PedestrianTarmac,
            _ => BuildSurfaceOptions.None
        };

        return option != BuildSurfaceOptions.None &&
               (allowedSurfaces & option) != 0;
    }

    [Header("Grid Footprint")]
    public int width = 1;
    public int depth = 1;

    [Header("Edge Placement")]
    [Min(1)]
    public int edgeLength = 1;

    public bool requiresExistingWall = false;

    [Header("Wall Mounted")]
    public bool isWallMounted = false;


    [Header("Surface Placement")]
    [Tooltip("Allows this object to be placed freely on a supporting placed object instead of occupying floor grid cells.")]
    public bool canBePlacedOnSurface = false;

    [Tooltip("Allows other surface-placeable objects to be placed on this object.")]
    public bool canSupportSurfaceObjects = false;

    [Header("Building")]
    public bool canRotate = true;
    public int buildCost = 0;

    [Header("Pathfinding")]
    public bool blocksPathfinding = true;

    [Header("Environment")]
    [Tooltip("Positive values improve the local environment; negative values reduce it.")]
    public float environmentValue = 0f;

    [Min(0f)]
    [Tooltip("Maximum distance in world units at which this object influences the environment.")]
    public float environmentRadius = 0f;

    [Header("UI")]
    [Tooltip("Menu category is generated from the buildable folder by the Editor catalogue builder.")]
    public string menuCategory;
    public BuildCategory category = BuildCategory.Airport;
    public Texture2D icon;

    [Header("Room Furniture")]
    public RoomFurnitureRole roomFurnitureRole = RoomFurnitureRole.None;
}
