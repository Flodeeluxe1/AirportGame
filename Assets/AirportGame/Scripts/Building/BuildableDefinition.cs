
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
    Road = 1 << 6,
    All = Grass | Foundation | Tarmac | Taxiway | Runway | PedestrianTarmac | Road
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
    PedestrianTarmac,
    Fence
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

public enum PassengerJourneyStationRole
{
    None,
    AirlineCheckIn,
    BaggageDrop,
    BodySecurity,
    LuggageSecurity,
    BoardingGate,
    BaggageClaim,
    BoardingDesk
}

[System.Flags]
public enum PublicTransportMode
{
    None = 0,
    [InspectorName("Bus Stop")]
    BusStop = 1 << 0,
    [InspectorName("Train Stop")]
    TrainStop = 1 << 1,
    [InspectorName("Taxi Stop")]
    TaxiStop = 1 << 2,
    [InspectorName("Underground Stop")]
    UndergroundStop = 1 << 3
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

    [Tooltip("Select every ground surface this buildable may be placed on. Unrestricted (Any) placement excludes roads unless Road or All is selected. Leave empty to use the legacy Required Surface setting below.")]
    public BuildSurfaceOptions allowedSurfaces = BuildSurfaceOptions.None;

    [HideInInspector]
    public BuildSurface requiredSurface = BuildSurface.Any;

    public bool AllowsAnySurface =>
        allowedSurfaces == BuildSurfaceOptions.All ||
        (allowedSurfaces == BuildSurfaceOptions.None &&
         requiredSurface == BuildSurface.Any);

    public bool AllowsSurface(BuildSurface surface)
    {
        // Unrestricted legacy definitions may use every ground surface,
        // but roads remain forbidden unless Road is explicitly selected
        // (or the definition explicitly selects All).
        if (surface == BuildSurface.Road &&
            allowedSurfaces != BuildSurfaceOptions.All &&
            (allowedSurfaces & BuildSurfaceOptions.Road) == 0 &&
            !(allowedSurfaces == BuildSurfaceOptions.None &&
              requiredSurface == BuildSurface.Road))
        {
            return false;
        }

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
            BuildSurface.Road => BuildSurfaceOptions.Road,
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

    [Header("Passenger Journey Station")]
    [Tooltip("Defines which passenger-journey service this buildable provides.")]
    public PassengerJourneyStationRole passengerJourneyStationRole =
        PassengerJourneyStationRole.None;

    [Header("Public Transport")]
    [Tooltip("Select the public-transport stop types this buildable provides.")]
    public PublicTransportMode publicTransportModes = PublicTransportMode.None;
}
