
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
    InstructorDesk = 1,
    StudentSeat = 2,
    OfficeDesk = 4,
    OfficeChair = 8,
    OfficeComputer = 16,
    OfficePrinter = 32
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

    [Min(0f)]
    public float mountHeight = 2f;

    [Header("Surface Placement")]
    [Tooltip("Allows this object to be placed freely on a supporting placed object instead of occupying floor grid cells.")]
    public bool canBePlacedOnSurface = false;

    [Tooltip("Allows other surface-placeable objects to be placed on this object.")]
    public bool canSupportSurfaceObjects = false;

    [Header("Building")]
    public bool canRotate = true;
    public int buildCost = 0;

    [Header("Pathfinding")]
    public bool blocksPathfinding = false;

    [Header("UI")]
    [Tooltip("Menu category is generated from the buildable folder by the Editor catalogue builder.")]
    public string menuCategory;
    public BuildCategory category = BuildCategory.Airport;
    public Texture2D icon;

    [Header("Room Furniture")]
    public RoomFurnitureRole roomFurnitureRole = RoomFurnitureRole.None;
}
