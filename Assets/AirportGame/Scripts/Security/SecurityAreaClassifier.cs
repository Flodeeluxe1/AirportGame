using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
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
    // Reusable visitation buffers; cleared once per rebuild before each flood.
    private bool[] enclosedRoomReachable;
    private bool[] criticalReachable;
    private int[] floodQueue;

    // Edge barriers are stored as flat arrays for fast flood-fill lookups.
    // Horizontal edges: z * gridWidth + x (z ranges 0..gridHeight).
    // Vertical edges: z * (gridWidth + 1) + x (x ranges 0..gridWidth).
    private bool[] horizontalBlockingEdges;
    private bool[] verticalBlockingEdges;
    [SerializeField, Tooltip("Number of body scanners missing a wall endpoint.")]
    private int incompleteScannerBoundaryCount;
    [SerializeField, Tooltip("True when a secure flood reaches the outer grid boundary.")]
    private bool hasSecurityBoundaryLeak;

    public int IncompleteScannerBoundaryCount => incompleteScannerBoundaryCount;
    public bool HasSecurityBoundaryLeak => hasSecurityBoundaryLeak;
    public bool IsReady { get; private set; }

    private int gridWidth;
    private int gridHeight;
    private int floodVisitedCells;
    private bool rebuildRequested;
    private bool edgeDataDirty = true;
    private int cachedGridWidth;
    private int cachedGridHeight;

    private struct ScannerTransformSnapshot
    {
        public PlacedBuilding Building;
        public Transform Transform;
        public Vector3 Position;
        public Vector3 Right;
        public Vector2Int Origin;
    }

    private readonly List<ScannerTransformSnapshot> scannerSnapshots =
        new List<ScannerTransformSnapshot>();

    private void OnEnable()
    {
        edgeDataDirty = true;
        ResolveReferences();
        Subscribe();
        RequestRebuild();
    }

    private void Start()
    {
        // Other scene objects may finish their Awake/OnEnable after this one.
        ResolveReferences();
        Subscribe();
        RequestRebuild();
    }

    private void OnDisable()
    {
        rebuildRequested = false;
        Unsubscribe();
    }

    private void LateUpdate()
    {
        if (HasScannerTransformChanged())
        {
            edgeDataDirty = true;
            RequestRebuild();
        }

        if (!rebuildRequested)
            return;

        // Clear before rebuilding so any change raised during the rebuild
        // schedules one follow-up on the next frame.
        rebuildRequested = false;
        Rebuild();
    }

    private void RequestRebuild()
    {
        if (isActiveAndEnabled)
            rebuildRequested = true;
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

        if (gridSurface != null)
        {
            gridSurface.FoundationSurfaceChanged -= OnFoundationSurfaceChanged;
            gridSurface.FoundationSurfaceChanged += OnFoundationSurfaceChanged;
        }
    }

    private void Unsubscribe()
    {
        PlacedBuilding.BuildingChanged -= OnBuildingChanged;

        if (edgeOccupancy != null)
            edgeOccupancy.OccupancyChanged -= OnOccupancyChanged;

        if (gridSurface != null)
            gridSurface.FoundationSurfaceChanged -= OnFoundationSurfaceChanged;

    }

    private void OnBuildingChanged(PlacedBuilding building)
    {
        edgeDataDirty = true;
        RequestRebuild();
    }

    private void OnOccupancyChanged()
    {
        edgeDataDirty = true;
        RequestRebuild();
    }

    private void OnFoundationSurfaceChanged()
    {
        RequestRebuild();
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
        long rebuildStartTicks = Stopwatch.GetTimestamp();
        ResolveReferences();

        IsReady = false;
        incompleteScannerBoundaryCount = 0;
        hasSecurityBoundaryLeak = false;

        if (gridManager == null || edgeOccupancy == null ||
            gridSurface == null ||
            gridManager.Width <= 0 || gridManager.Height <= 0)
        {
            PerformanceMonitoringManager.RecordSecurityRebuild(
                TicksToMilliseconds(Stopwatch.GetTimestamp() - rebuildStartTicks),
                0.0, 0.0, 0.0, 0.0, 0.0, 0);
            return;
        }

        gridWidth = gridManager.Width;
        gridHeight = gridManager.Height;
        int cellCount = gridWidth * gridHeight;

        EnsureBuffers(cellCount);
        if (cachedGridWidth != gridWidth || cachedGridHeight != gridHeight)
        {
            edgeDataDirty = true;
            cachedGridWidth = gridWidth;
            cachedGridHeight = gridHeight;
        }

        double edgeCollectionMs = 0.0;
        double scannerBoundaryMs = 0.0;
        if (edgeDataDirty)
        {
            BuildEdgeData(out edgeCollectionMs, out scannerBoundaryMs);
            edgeDataDirty = false;
        }

        // First identify cells connected to the outside of the map.
        // Physical walls/fences and scanner dividers both block this flood:
        // a complete scanner line separates the public side from the
        // runway-side region even when both sides are inside the map bounds.
        floodVisitedCells = 0;
        long publicFloodStartTicks = Stopwatch.GetTimestamp();
        System.Array.Clear(enclosedRoomReachable, 0, enclosedRoomReachable.Length);
        FloodFromGridPerimeter(enclosedRoomReachable);
        double publicFloodMs = TicksToMilliseconds(
            Stopwatch.GetTimestamp() - publicFloodStartTicks);
        int publicCellsVisited = floodVisitedCells;

        // The runway is the authoritative seed for the airport-side region.
        // Flood it regardless of outside connectivity; outside reachability
        // takes precedence later, so a runway connected to public space
        // remains Unsecure while still identifying the correct scanner side.
        System.Array.Clear(criticalReachable, 0, criticalReachable.Length);
        floodVisitedCells = 0;
        long runwayFloodStartTicks = Stopwatch.GetTimestamp();
        FloodFromRunwaySurfaces(criticalReachable);
        double runwayFloodMs = TicksToMilliseconds(
            Stopwatch.GetTimestamp() - runwayFloodStartTicks);
        int runwayCellsVisited = floodVisitedCells;

        long classificationStartTicks = Stopwatch.GetTimestamp();
        for (int z = 0; z < gridHeight; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int index = ToIndex(x, z);

                // Public-side connectivity wins. The scanner divider is
                // included in blockingEdges, separating this from the
                // runway-side foundation area.
                if (enclosedRoomReachable[index])
                    areas[index] = SecurityArea.Unsecure;
                else if (gridSurface.GetSurface(new Vector2Int(x, z)) ==
                         BuildSurface.Foundation)
                    areas[index] = SecurityArea.Secure;
                else if (criticalReachable[index])
                    areas[index] = SecurityArea.Critical;
                else
                    areas[index] = SecurityArea.Unclassified;
            }
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

        double classificationMs = TicksToMilliseconds(
            Stopwatch.GetTimestamp() - classificationStartTicks);
        double totalMs = TicksToMilliseconds(
            Stopwatch.GetTimestamp() - rebuildStartTicks);
        PerformanceMonitoringManager.RecordSecurityRebuild(
            totalMs,
            edgeCollectionMs,
            scannerBoundaryMs,
            publicFloodMs,
            runwayFloodMs,
            classificationMs,
            publicCellsVisited + runwayCellsVisited);
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private void EnsureBuffers(int cellCount)
    {
        if (areas == null || areas.Length != cellCount)
            areas = new SecurityArea[cellCount];

        int horizontalEdgeCount = gridWidth * (gridHeight + 1);
        int verticalEdgeCount = (gridWidth + 1) * gridHeight;
        if (horizontalBlockingEdges == null || horizontalBlockingEdges.Length != horizontalEdgeCount)
            horizontalBlockingEdges = new bool[horizontalEdgeCount];
        if (verticalBlockingEdges == null || verticalBlockingEdges.Length != verticalEdgeCount)
            verticalBlockingEdges = new bool[verticalEdgeCount];

        if (enclosedRoomReachable == null || enclosedRoomReachable.Length != cellCount)
            enclosedRoomReachable = new bool[cellCount];

        if (criticalReachable == null || criticalReachable.Length != cellCount)
            criticalReachable = new bool[cellCount];

        if (floodQueue == null || floodQueue.Length != cellCount)
            floodQueue = new int[cellCount];

    }

    private void BuildEdgeData(out double edgeCollectionMs, out double scannerBoundaryMs)
    {
        long edgeCollectionStartTicks = Stopwatch.GetTimestamp();
        long scannerBoundaryTicks = 0;

        System.Array.Clear(horizontalBlockingEdges, 0, horizontalBlockingEdges.Length);
        System.Array.Clear(verticalBlockingEdges, 0, verticalBlockingEdges.Length);
        scannerSnapshots.Clear();

        IReadOnlyList<PlacedBuilding> buildings = PlacedBuilding.ActiveBuildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            PlacedBuilding building = buildings[i];
            if (building == null || building.Definition == null)
                continue;

            BuildCategory category = building.Definition.category;
            if (building.IsEdgeBuilding &&
                (category == BuildCategory.Fence ||
                 category == BuildCategory.Wall ||
                 category == BuildCategory.Door))
            {
                AddBuildingEdges(building);
            }

            if (building.Definition.passengerJourneyStationRole ==
                PassengerJourneyStationRole.BodySecurity)
            {
                Transform scannerTransform = building.transform;
                scannerSnapshots.Add(new ScannerTransformSnapshot
                {
                    Building = building,
                    Transform = scannerTransform,
                    Position = scannerTransform.position,
                    Right = scannerTransform.right,
                    Origin = building.Origin
                });

                long scannerStartTicks = Stopwatch.GetTimestamp();
                AddScannerBoundary(building);
                scannerBoundaryTicks += Stopwatch.GetTimestamp() - scannerStartTicks;
            }
        }

        edgeCollectionMs = TicksToMilliseconds(
            Stopwatch.GetTimestamp() - edgeCollectionStartTicks - scannerBoundaryTicks);
        scannerBoundaryMs = TicksToMilliseconds(scannerBoundaryTicks);
    }

    private bool HasScannerTransformChanged()
    {
        int snapshotIndex = 0;
        IReadOnlyList<PlacedBuilding> buildings = PlacedBuilding.ActiveBuildings;

        for (int i = 0; i < buildings.Count; i++)
        {
            PlacedBuilding building = buildings[i];
            if (building == null || building.Definition == null ||
                building.Definition.passengerJourneyStationRole !=
                    PassengerJourneyStationRole.BodySecurity)
            {
                continue;
            }

            if (snapshotIndex >= scannerSnapshots.Count)
                return true;

            ScannerTransformSnapshot snapshot = scannerSnapshots[snapshotIndex++];
            Transform scannerTransform = building.transform;

            if (snapshot.Building != building ||
                snapshot.Transform != scannerTransform ||
                snapshot.Origin != building.Origin ||
                snapshot.Position != scannerTransform.position ||
                snapshot.Right != scannerTransform.right)
            {
                return true;
            }
        }

        return snapshotIndex != scannerSnapshots.Count;
    }

    private void AddBuildingEdges(PlacedBuilding building)
    {
        int length = Mathf.Max(1, building.EdgeLength);

        for (int i = 0; i < length; i++)
        {
            Vector2Int cell = building.Origin;
            if (building.EdgeDirection == EdgeDirection.Horizontal)
                cell.x += i;
            else
                cell.y += i;

            AddBlockingEdge(new GridEdge(cell, building.EdgeDirection));
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
                AddBlockingEdge(new GridEdge(
                    new Vector2Int(x, scanner.Origin.y),
                    EdgeDirection.Horizontal));
            }

        }
        else
        {
            int minZ = Mathf.Min(positiveWall.cell.y, negativeWall.cell.y);
            int maxZ = Mathf.Max(positiveWall.cell.y, negativeWall.cell.y);

            for (int z = minZ; z < maxZ; z++)
            {
                AddBlockingEdge(new GridEdge(
                    new Vector2Int(scanner.Origin.x, z),
                    EdgeDirection.Vertical));
            }

        }
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
               building.Definition.category == BuildCategory.Door ||
               building.Definition.category == BuildCategory.Fence;
    }

    private void AddBlockingEdge(GridEdge edge)
    {
        int x = edge.cell.x;
        int z = edge.cell.y;

        if (edge.direction == EdgeDirection.Horizontal)
        {
            if (x >= 0 && x < gridWidth && z >= 0 && z <= gridHeight)
                horizontalBlockingEdges[z * gridWidth + x] = true;
        }
        else
        {
            if (x >= 0 && x <= gridWidth && z >= 0 && z < gridHeight)
                verticalBlockingEdges[z * (gridWidth + 1) + x] = true;
        }
    }

    private bool IsHorizontalBlocked(int x, int edgeZ)
    {
        return horizontalBlockingEdges[edgeZ * gridWidth + x];
    }

    private bool IsVerticalBlocked(int edgeX, int z)
    {
        return verticalBlockingEdges[z * (gridWidth + 1) + edgeX];
    }

    private void FloodFromGridPerimeter(bool[] reached)
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

        FloodQueueFromExisting(tail, reached);
    }

    // Queue every runway cell as a seed before traversing. This is equivalent
    // to flooding each unvisited runway component separately, but processes
    // all runway-connected regions in one breadth-first traversal.
    private void FloodFromRunwaySurfaces(bool[] reached)
    {
        int tail = 0;

        for (int z = 0; z < gridHeight; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (gridSurface.GetSurface(new Vector2Int(x, z)) != BuildSurface.Runway)
                    continue;

                EnqueueIfNew(ToIndex(x, z), reached, ref tail);
            }
        }

        FloodQueueFromExisting(tail, reached);
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
        bool[] reached)
    {
        int head = 0;
        int width = gridWidth;
        int height = gridHeight;
        int verticalStride = width + 1;

        while (head < tail)
        {
            int index = floodQueue[head++];
            floodVisitedCells++;

            int x = index % width;
            int z = index / width;

            // Inline the four neighbor checks. This avoids four helper calls,
            // repeated coordinate validation and repeated index calculations
            // for every visited cell.
            if (x + 1 < width &&
                !verticalBlockingEdges[z * verticalStride + x + 1])
            {
                int neighbor = index + 1;
                if (!reached[neighbor])
                {
                    reached[neighbor] = true;
                    floodQueue[tail++] = neighbor;
                }
            }

            if (x > 0 &&
                !verticalBlockingEdges[z * verticalStride + x])
            {
                int neighbor = index - 1;
                if (!reached[neighbor])
                {
                    reached[neighbor] = true;
                    floodQueue[tail++] = neighbor;
                }
            }

            if (z + 1 < height &&
                !horizontalBlockingEdges[(z + 1) * width + x])
            {
                int neighbor = index + width;
                if (!reached[neighbor])
                {
                    reached[neighbor] = true;
                    floodQueue[tail++] = neighbor;
                }
            }

            if (z > 0 &&
                !horizontalBlockingEdges[z * width + x])
            {
                int neighbor = index - width;
                if (!reached[neighbor])
                {
                    reached[neighbor] = true;
                    floodQueue[tail++] = neighbor;
                }
            }
        }
    }

    private bool IsSecure(int x, int z)
    {
        return areas[ToIndex(x, z)] == SecurityArea.Secure;
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
