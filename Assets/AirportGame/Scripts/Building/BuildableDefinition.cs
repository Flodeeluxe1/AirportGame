using UnityEngine;

public enum BuildCategory
{
    Floor,
    Wall,
    Door,
    Airport,
    Roads,
    Commercial,
    Taxiways
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

    [Header("Building")]
    public bool canRotate = true;
    public int buildCost = 0;

    [Header("Pathfinding")]
    public bool blocksPathfinding = false;

    [Header("UI")]
    public BuildCategory category = BuildCategory.Airport;
    public Texture2D icon;
}