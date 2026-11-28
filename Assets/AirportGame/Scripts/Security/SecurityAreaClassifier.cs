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
    [SerializeField] private GridSurface gridSurface;

    [Header("Scanner Boundary Warnings")]
    [Min(1), SerializeField, Tooltip("Maximum allowed distance from a body scanner to either wall endpoint, measured in grid cells.")]
    private int maxScannerToWallDistance = 8;

    private SecurityArea[] areas;
    private bool[] enclosedRoomReachable;
    private bool[] secureReachable;
    private bool[] physicallyOutsideReachable;
    private bool[] criticalReachable;
    private int[] floodQueue;

    // Physical perimeter edges (walls, doors and fences) must form a
    // continuous loop before any enclosed cell can be considered secure.
    private readonly HashSet<GridEdge> perimeterEdges = new HashSet<GridEdge>();
    // Includes physical edges plus the virtual body-scanner divider.
    private readonly HashSet<GridEdge> blockingEdges = new HashSet<GridEdge>();
    private struct ScannerDivider
    {
        public int sideA;
        public int sideB;

        public ScannerDivider(int sideA, int sideB)
        {
            this.sideA = sideA;
            this.sideB = sideB;
        }
    }

    private readonly List<ScannerDivider> scannerDividers =
        new List<ScannerDivider>();
    private readonly List<int> secureSeeds = new List<int>();
    private bool[] secureForbidden;

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

        if (gridSurface == null)
            gridSurface = GetComponent<GridSurface>();

        if (gridSurface == null)
            gridSurface = FindAnyObjectByType<GridSurface>();
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
        perimeterEdges.Clear();
        blockingEdges.Clear();
        scannerDividers.Clear();
        secureSeeds.Clear();

        if (gridManager == null || edgeOccupancy == null ||
            gridSurface == null ||
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
                if (category == BuildCategory.Fence ||
                    category == BuildCategory.Wall ||
                    category == BuildCategory.Door)
                {
                    AddBuildingEdges(building, perimeterEdges);
                    AddBuildingEdges(building, blockingEdges);
                }
            }

            if (building.Definition.passengerJourneyStationRole ==
                PassengerJourneyStationRole.BodySecurity)
            {
                AddScannerBoundary(building);
            }
        }

        // First identify cells connected to the outside of the map.
        // Physical walls/fences and scanner dividers both block this flood:
        // a complete scanner line separates the public side from the
        // runway-side region even when both sides are inside the map bounds.
        FloodFromGridPerimeter(enclosedRoomReachable, blockingEdges);

        // Physical enclosure is evaluated independently of the scanner.
        // A cell unreachable from the map perimeter using only physical
        // walls, doors and fences is Secure, even on the runway side.
        FloodFromGridPerimeter(physicallyOutsideReachable, perimeterEdges);

        // The runway is the authoritative seed for the airport-side region.
        // Flood it regardless of outside connectivity; outside reachability
        // takes precedence later, so a runway connected to public space
        // remains Unsecure while still identifying the correct scanner side.
        for (int z = 0; z < gridHeight; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (gridSurface.GetSurface(new Vector2Int(x, z)) != BuildSurface.Runway)
                    continue;

                int runwayIndex = ToIndex(x, z);
                FloodFromSeed(
                    runwayIndex,
                    criticalReachable,
                    blockingEdges,
                    null);
            }
        }

        // Scanner rotation is deliberately ignored. Each scanner is only
        // a divider: the runway flood determines which half is airport-side,
        // and the opposite half is eligible to become Secure.
        for (int i = 0; i < scannerDividers.Count; i++)
        {
            ScannerDivider divider = scannerDividers[i];

            if (criticalReachable[divider.sideA] &&
                !criticalReachable[divider.sideB])
            {
                AddSecureSeed(divider.sideB);
            }
            else if (criticalReachable[divider.sideB] &&
                     !criticalReachable[divider.sideA])
            {
                AddSecureSeed(divider.sideA);
            }
        }

        // Secure floods may not pass through public cells or the runway-
        // connected Critical Area, even if an incomplete perimeter gives
        // those regions a route around the scanner divider.
        for (int i = 0; i < cellCount; i++)
        {
            secureForbidden[i] =
                enclosedRoomReachable[i] || criticalReachable[i];
        }

        for (int i = 0; i < secureSeeds.Count; i++)
        {
            int seed = secureSeeds[i];
            if (!secureForbidden[seed])
            {
                FloodFromSeed(
                    seed,
                    secureReachable,
                    blockingEdges,
                    secureForbidden);
            }
        }

        for (int i = 0; i < cellCount; i++)
        {
            if (!physicallyOutsideReachable[i])
                areas[i] = SecurityArea.Secure;
            else if (enclosedRoomReachable[i])
                areas[i] = SecurityArea.Unsecure;
            else if (criticalReachable[i])
                areas[i] = SecurityArea.Critical;
            else
                areas[i] = SecurityArea.Unclassified;
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

        if (enclosedRoomReachable == null || enclosedRoomReachable.Length != cellCount)
            enclosedRoomReachable = new bool[cellCount];

        if (secureReachable == null || secureReachable.Length != cellCount)
            secureReachable = new bool[cellCount];

        if (criticalReachable == null || criticalReachable.Length != cellCount)
            criticalReachable = new bool[cellCount];

        if (secureForbidden == null || secureForbidden.Length != cellCount)
            secureForbidden = new bool[cellCount];

        if (physicallyOutsideReachable == null || physicallyOutsideReachable.Length != cellCount)
            physicallyOutsideReachable = new bool[cellCount];

        if (floodQueue == null || floodQueue.Length != cellCount)
            floodQueue = new int[cellCount];

        System.Array.Clear(enclosedRoomReachable, 0, enclosedRoomReachable.Length);
        System.Array.Clear(secureReachable, 0, secureReachable.Length);
        System.Array.Clear(criticalReachable, 0, criticalReachable.Length);
        System.Array.Clear(secureForbidden, 0, secureForbidden.Length);
        System.Array.Clear(physicallyOutsideReachable, 0, physicallyOutsideReachable.Length);
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

        if (alongGridX)
            positiveStep = new Vector2Int(right.x >= 0f ? 1 : -1, 0);
        else
            positiveStep = new Vector2Int(0, right.z >= 0f ? 1 : -1);

        Vector2Int negativeStep = -positiveStep;

        bool foundPositiveWall = TryFindWall(
            scanner.Origin, positiveStep, out GridEdge positiveWall, out int positiveDistance);
        bool foundNegativeWall = TryFindWall(
            scanner.Origin, negativeStep, out GridEdge negativeWall, out int negativeDistance);

        if (!foundPositiveWall || !foundNegativeWall)
        {
            incompleteScannerBoundaryCount++;
            return;
        }

        if (positiveDistance > maxScannerToWallDistance ||
            negativeDistance > maxScannerToWallDistance)
        {
            Debug.LogWarning(
                $"Body scanner '{scanner.name}' is too far from its wall endpoint(s). " +
                $"Distances: {positiveDistance} and {negativeDistance} grid cells; " +
                $"configured maximum: {maxScannerToWallDistance}.",
                scanner);
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

            AddScannerDivider(
                scanner.Origin + Vector2Int.up,
                scanner.Origin + Vector2Int.down);
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

            AddScannerDivider(
                scanner.Origin + Vector2Int.right,
                scanner.Origin + Vector2Int.left);
        }
    }

    private void AddScannerDivider(Vector2Int sideA, Vector2Int sideB)
    {
        if (!gridManager.IsInsideGrid(sideA) ||
            !gridManager.IsInsideGrid(sideB))
        {
            return;
        }

        scannerDividers.Add(new ScannerDivider(
            ToIndex(sideA),
            ToIndex(sideB)));
    }

    private void AddSecureSeed(int index)
    {
        secureSeeds.Add(index);
    }

    private bool TryFindWall(
        Vector2Int startCell,
        Vector2Int step,
        out GridEdge wallEdge,
        out int distanceInCells)
    {
        Vector2Int currentCell = startCell;
        int maximumSteps = gridWidth + gridHeight;

        for (int i = 0; i < maximumSteps; i++)
        {
            GridEdge candidate = GetSharedEdge(currentCell, step);

            if (IsWallOrDoor(candidate))
            {
                wallEdge = candidate;
                distanceInCells = i + 1;
                return true;
            }

            Vector2Int nextCell = currentCell + step;
            if (!gridManager.IsInsideGrid(nextCell))
                break;

            currentCell = nextCell;
        }

        wallEdge = default;
        distanceInCells = 0;
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
        return !physicallyOutsideReachable[ToIndex(x, z)];
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
