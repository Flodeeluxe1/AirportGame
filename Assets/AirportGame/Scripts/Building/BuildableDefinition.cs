
using UnityEngine;

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
    Pipelines
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

    public BuildSurface requiredSurface = BuildSurface.Any;

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
