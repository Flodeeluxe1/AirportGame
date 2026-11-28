using System.Collections.Generic;
using UnityEngine;

public enum SecurityArea
{
    Unclassified,
    Unsecure,
    Secure,
    Critical
}

/// <summary>
/// Classifies grid cells using the airport fence perimeter and virtual
/// body-scanner boundaries. Rebuilds only when placed buildings or edge
/// occupancy change; it does not poll every frame.
/// Attach this component to the GridManager GameObject.
/// </summary>
[DisallowMultipleComponent]
public sealed class SecurityAreaClassifier : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private EdgeOccupancy edgeOccupancy;

    private SecurityArea[] areas;
    private bool[] criticalReachable;
    private bool[] enclosedRoomReachable;
    private bool[] secureReachable;
    private int[] floodQueue;

    private readonly HashSet<GridEdge> fenceEdges = new HashSet<GridEdge>();
    private readonly HashSet<GridEdge> blockingEdges = new HashSet<GridEdge>();
    private readonly List<int> secureSeeds = new List<int>();

    [SerializeField, Tooltip("Number of body scanners missing a wall endpoint.")]
    private int incompleteScannerBoundaryCount;
    [SerializeField, Tooltip("True when a secure flood reaches the outer grid boundary.")]
    private bool hasSecurityBoundaryLeak;

    public int IncompleteScannerBoundaryCount => incompleteScannerBoundaryCount;
    public bool HasSecurityBoundaryLeak => hasSecurityBoundaryLeak;
    public bool IsReady { get; private set; }

    private int gridWidth;
    private int gridHeight;

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        Rebuild();
    }

    private void Start()
    {
        // Other scene objects may finish their Awake/OnEnable after this one.
        ResolveReferences();
        Subscribe();
        Rebuild();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = GetComponent<GridManager>();

        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (edgeOccupancy == null)
            edgeOccupancy = GetComponent<EdgeOccupancy>();

        if (edgeOccupancy == null)
            edgeOccupancy = FindAnyObjectByType<EdgeOccupancy>();
    }

    private void Subscribe()
    {
        PlacedBuilding.BuildingChanged -= OnBuildingChanged;
        PlacedBuilding.BuildingChanged += OnBuildingChanged;

        if (edgeOccupancy != null)
        {
            edgeOccupancy.OccupancyChanged -= OnOccupancyChanged;
            edgeOccupancy.OccupancyChanged += OnOccupancyChanged;
        }
    }

    private void Unsubscribe()
    {
        PlacedBuilding.BuildingChanged -= OnBuildingChanged;

        if (edgeOccupancy != null)
            edgeOccupancy.OccupancyChanged -= OnOccupancyChanged;
    }

    private void OnBuildingChanged(PlacedBuilding building)
    {
        Rebuild();
    }

    private void OnOccupancyChanged()
    {
        Rebuild();
    }

    public SecurityArea GetArea(Vector2Int cell)
    {
        if (!TryGetArea(cell, out SecurityArea area))
            return SecurityArea.Unclassified;

        return area;
    }

    public bool TryGetArea(Vector2Int cell, out SecurityArea area)
    {
        area = SecurityArea.Unclassified;

        if (!IsReady ||
            cell.x < 0 || cell.x >= gridWidth ||
            cell.y < 0 || cell.y >= gridHeight)
        {
            return false;
        }

        area = areas[ToIndex(cell)];
        return true;
    }

    [ContextMenu("Rebuild Security Areas")]
    public void Rebuild()
    {
        ResolveReferences();

        IsReady = false;
        incompleteScannerBoundaryCount = 0;
        hasSecurityBoundaryLeak = false;
        fenceEdges.Clear();
        blockingEdges.Clear();
        secureSeeds.Clear();

        if (gridManager == null || edgeOccupancy == null ||
            gridManager.Width <= 0 || gridManager.Height <= 0)
        {
            return;
        }

        gridWidth = gridManager.Width;
        gridHeight = gridManager.Height;
        int cellCount = gridWidth * gridHeight;

        EnsureBuffers(cellCount);

        IReadOnlyList<PlacedBuilding> buildings = PlacedBuilding.ActiveBuildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            PlacedBuilding building = buildings[i];
            if (building == null || building.Definition == null)
                continue;

            BuildCategory category = building.Definition.category;

            if (building.IsEdgeBuilding)
            {
                if (category == BuildCategory.Fence)
                    AddBuildingEdges(building, fenceEdges);

                if (category == BuildCategory.Fence ||
                    category == BuildCategory.Wall ||
                    category == BuildCategory.Door)
                {
                    AddBuildingEdges(building, blockingEdges);
                }
            }

            if (building.Definition.passengerJourneyStationRole ==
                PassengerJourneyStationRole.BodySecurity)
            {
                AddScannerBoundary(building);
            }
        }

        // Preserve fence-enclosed critical zones, and also detect rooms
        // enclosed by walls/doors. This allows a scanner to divide a
        // fully enclosed room without requiring a fence perimeter.
        FloodFromGridPerimeter(criticalReachable, fenceEdges);
        FloodFromGridPerimeter(enclosedRoomReachable, blockingEdges);

        // The scanner's local forward direction is the post-scanner side.
        // Flood only within the wall-enclosed room and stop at the scanner's
        // virtual boundary, so only that side becomes Critical.
        for (int i = 0; i < secureSeeds.Count; i++)
        {
            int seed = secureSeeds[i];
            if (!enclosedRoomReachable[seed])
                FloodFromSeed(seed, secureReachable, blockingEdges, enclosedRoomReachable);
        }

        for (int i = 0; i < cellCount; i++)
        {
            if (!criticalReachable[i] || secureReachable[i])
                areas[i] = SecurityArea.Critical;
            else
                areas[i] = SecurityArea.Unsecure;
        }

        // If a secure flood reaches the outer grid row/column, there is an
        // open route to the map boundary (often a missing scanner endpoint
        // or an incomplete wall connection).
        for (int x = 0; x < gridWidth && !hasSecurityBoundaryLeak; x++)
        {
            hasSecurityBoundaryLeak =
                IsSecure(x, 0) || IsSecure(x, gridHeight - 1);
        }

        for (int z = 0; z < gridHeight && !hasSecurityBoundaryLeak; z++)
        {
            hasSecurityBoundaryLeak =
                IsSecure(0, z) || IsSecure(gridWidth - 1, z);
        }

        IsReady = true;
    }

    private void EnsureBuffers(int cellCount)
    {
        if (areas == null || areas.Length != cellCount)
            areas = new SecurityArea[cellCount];

        if (criticalReachable == null || criticalReachable.Length != cellCount)
            criticalReachable = new bool[cellCount];

        if (enclosedRoomReachable == null || enclosedRoomReachable.Length != cellCount)
            enclosedRoomReachable = new bool[cellCount];

        if (secureReachable == null || secureReachable.Length != cellCount)
            secureReachable = new bool[cellCount];

        if (floodQueue == null || floodQueue.Length != cellCount)
            floodQueue = new int[cellCount];

        System.Array.Clear(criticalReachable, 0, criticalReachable.Length);
        System.Array.Clear(enclosedRoomReachable, 0, enclosedRoomReachable.Length);
        System.Array.Clear(secureReachable, 0, secureReachable.Length);
    }

    private void AddBuildingEdges(
        PlacedBuilding building,
        HashSet<GridEdge> destination)
    {
        int length = Mathf.Max(1, building.EdgeLength);

        for (int i = 0; i < length; i++)
        {
            Vector2Int cell = building.Origin;
            if (building.EdgeDirection == EdgeDirection.Horizontal)
                cell.x += i;
            else
                cell.y += i;

            destination.Add(new GridEdge(cell, building.EdgeDirection));
        }
    }

    private void AddScannerBoundary(PlacedBuilding scanner)
    {
        Vector3 right = scanner.transform.right;
        bool alongGridX = Mathf.Abs(right.x) >= Mathf.Abs(right.z);

        Vector2Int positiveStep;
        int normalSign;

        if (alongGridX)
        {
            positiveStep = new Vector2Int(right.x >= 0f ? 1 : -1, 0);
            normalSign = scanner.transform.forward.z >= 0f ? 1 : -1;
        }
        else
        {
            positiveStep = new Vector2Int(0, right.z >= 0f ? 1 : -1);
            normalSign = scanner.transform.forward.x >= 0f ? 1 : -1;
        }

        Vector2Int negativeStep = -positiveStep;

        if (!TryFindWall(scanner.Origin, positiveStep, out GridEdge positiveWall) ||
            !TryFindWall(scanner.Origin, negativeStep, out GridEdge negativeWall))
        {
            incompleteScannerBoundaryCount++;
            return;
        }

        if (alongGridX)
        {
            int minX = Mathf.Min(positiveWall.cell.x, negativeWall.cell.x);
            int maxX = Mathf.Max(positiveWall.cell.x, negativeWall.cell.x);

            for (int x = minX; x < maxX; x++)
            {
                blockingEdges.Add(new GridEdge(
                    new Vector2Int(x, scanner.Origin.y),
                    EdgeDirection.Horizontal));
            }

            Vector2Int secureCell = scanner.Origin + new Vector2Int(0, normalSign);
            AddSecureSeed(secureCell);
        }
        else
        {
            int minZ = Mathf.Min(positiveWall.cell.y, negativeWall.cell.y);
            int maxZ = Mathf.Max(positiveWall.cell.y, negativeWall.cell.y);

            for (int z = minZ; z < maxZ; z++)
            {
                blockingEdges.Add(new GridEdge(
                    new Vector2Int(scanner.Origin.x, z),
                    EdgeDirection.Vertical));
            }

            Vector2Int secureCell = scanner.Origin + new Vector2Int(normalSign, 0);
            AddSecureSeed(secureCell);
        }
    }

    private void AddSecureSeed(Vector2Int cell)
    {
        if (gridManager.IsInsideGrid(cell))
            secureSeeds.Add(ToIndex(cell));
    }

    private bool TryFindWall(
        Vector2Int startCell,
        Vector2Int step,
        out GridEdge wallEdge)
    {
        Vector2Int currentCell = startCell;
        int maximumSteps = gridWidth + gridHeight;

        for (int i = 0; i < maximumSteps; i++)
        {
            GridEdge candidate = GetSharedEdge(currentCell, step);

            if (IsWallOrDoor(candidate))
            {
                wallEdge = candidate;
                return true;
            }

            Vector2Int nextCell = currentCell + step;
            if (!gridManager.IsInsideGrid(nextCell))
                break;

            currentCell = nextCell;
        }

        wallEdge = default;
        return false;
    }

    private static GridEdge GetSharedEdge(Vector2Int cell, Vector2Int step)
    {
        if (step.x > 0)
            return new GridEdge(cell + Vector2Int.right, EdgeDirection.Vertical);

        if (step.x < 0)
            return new GridEdge(cell, EdgeDirection.Vertical);

        if (step.y > 0)
            return new GridEdge(cell + Vector2Int.up, EdgeDirection.Horizontal);

        return new GridEdge(cell, EdgeDirection.Horizontal);
    }

    private bool IsWallOrDoor(GridEdge edge)
    {
        if (!edgeOccupancy.TryGetOccupant(edge, out GameObject occupant) ||
            occupant == null)
        {
            return false;
        }

        PlacedBuilding building = occupant.GetComponent<PlacedBuilding>();
        if (building == null || building.Definition == null)
            return false;

        return building.Definition.category == BuildCategory.Wall ||
               building.Definition.category == BuildCategory.Door;
    }

    private void FloodFromGridPerimeter(
        bool[] reached,
        HashSet<GridEdge> barriers)
    {
        int tail = 0;

        for (int x = 0; x < gridWidth; x++)
        {
            EnqueueIfNew(ToIndex(x, 0), reached, ref tail);
            if (gridHeight > 1)
                EnqueueIfNew(ToIndex(x, gridHeight - 1), reached, ref tail);
        }

        for (int z = 1; z < gridHeight - 1; z++)
        {
            EnqueueIfNew(ToIndex(0, z), reached, ref tail);
            if (gridWidth > 1)
                EnqueueIfNew(ToIndex(gridWidth - 1, z), reached, ref tail);
        }

        FloodQueueFromExisting(tail, reached, barriers, null);
    }

    private void FloodFromSeed(
        int seed,
        bool[] reached,
        HashSet<GridEdge> barriers,
        bool[] forbidden)
    {
        if (reached[seed] || (forbidden != null && forbidden[seed]))
            return;

        reached[seed] = true;
        floodQueue[0] = seed;
        FloodQueueFromExisting(1, reached, barriers, forbidden);
    }

    private void EnqueueIfNew(int index, bool[] reached, ref int tail)
    {
        if (reached[index])
            return;

        reached[index] = true;
        floodQueue[tail++] = index;
    }

    private void FloodQueueFromExisting(
        int tail,
        bool[] reached,
        HashSet<GridEdge> barriers,
        bool[] forbidden)
    {
        int head = 0;

        while (head < tail)
        {
            int index = floodQueue[head++];
            int x = index % gridWidth;
            int z = index / gridWidth;

            TryVisit(x + 1, z, new GridEdge(
                new Vector2Int(x + 1, z), EdgeDirection.Vertical),
                reached, barriers, forbidden, ref tail);

            TryVisit(x - 1, z, new GridEdge(
                new Vector2Int(x, z), EdgeDirection.Vertical),
                reached, barriers, forbidden, ref tail);

            TryVisit(x, z + 1, new GridEdge(
                new Vector2Int(x, z + 1), EdgeDirection.Horizontal),
                reached, barriers, forbidden, ref tail);

            TryVisit(x, z - 1, new GridEdge(
                new Vector2Int(x, z), EdgeDirection.Horizontal),
                reached, barriers, forbidden, ref tail);
        }
    }

    private void TryVisit(
        int x,
        int z,
        GridEdge crossingEdge,
        bool[] reached,
        HashSet<GridEdge> barriers,
        bool[] forbidden,
        ref int tail)
    {
        if (x < 0 || x >= gridWidth || z < 0 || z >= gridHeight ||
            barriers.Contains(crossingEdge))
        {
            return;
        }

        int index = ToIndex(x, z);
        if (reached[index] || (forbidden != null && forbidden[index]))
            return;

        reached[index] = true;
        floodQueue[tail++] = index;
    }

    private bool IsSecure(int x, int z)
    {
        return secureReachable[ToIndex(x, z)];
    }

    private int ToIndex(Vector2Int cell)
    {
        return ToIndex(cell.x, cell.y);
    }

    private int ToIndex(int x, int z)
    {
        return z * gridWidth + x;
    }
}
