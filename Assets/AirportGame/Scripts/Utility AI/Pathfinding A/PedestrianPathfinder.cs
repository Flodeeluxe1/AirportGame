using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grid-based A* pathfinding for pedestrians.
/// Walkable surfaces: Grass, Foundation, Tarmac and Pedestrian Tarmac.
/// Runways, taxiways, registered building obstacles and occupied wall edges block movement.
/// Door edges remain traversable.
/// </summary>
public class PedestrianPathfinder : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;

    [Header("Grid References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private GridOccupancy gridOccupancy;
    [SerializeField] private EdgeOccupancy edgeOccupancy;
    [SerializeField] private RoadNetwork roadNetwork;

    [Header("Path Settings")]
    [SerializeField, Min(0f)] private float pathHeightOffset = 0.05f;
    [SerializeField, Range(0, 5)] private int randomStepPenalty = 3;

    [Header("Path Smoothing")]
    [SerializeField, Range(1, 6)] private int smoothingLookAheadCells = 3;

    private static int pathSearchCounter;
    private int randomState;

    // Reused A* workspace. Path searches run on Unity's main thread and are
    // sequential, so one workspace avoids per-search collection allocations.
    private readonly List<OpenNode> openSet = new List<OpenNode>(256);
    private readonly Dictionary<Vector2Int, Vector2Int> cameFrom =
        new Dictionary<Vector2Int, Vector2Int>(256);
    private readonly Dictionary<Vector2Int, int> gScore =
        new Dictionary<Vector2Int, int>(256);
    private readonly HashSet<Vector2Int> closedSet =
        new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, int> randomCellPenalties =
        new Dictionary<Vector2Int, int>(256);
    private readonly int[] neighborOrder = new int[8];

    private static readonly Vector2Int[] NeighborOffsets =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
        new Vector2Int(1, 1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1, -1)
    };

    private const int StraightMoveCost = 10;
    private const int DiagonalMoveCost = 14;
    private const int RoadTraversalPenalty = 32;
    private const int GrassTraversalPenalty = 16;
    private const int FoundationTraversalPenalty = 2;
    private const int TarmacTraversalPenalty = 8;
    private const int PedestrianTarmacTraversalPenalty = 0;

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = GetComponent<GridManager>();

        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (gridManager != null)
        {
            if (gridSurface == null)
                gridSurface = gridManager.GetComponent<GridSurface>();

            if (gridOccupancy == null)
                gridOccupancy = gridManager.GetComponent<GridOccupancy>();

            if (edgeOccupancy == null)
                edgeOccupancy = gridManager.GetComponent<EdgeOccupancy>();

            if (roadNetwork == null)
                roadNetwork = gridManager.GetComponent<RoadNetwork>();
        }

        if (gridSurface == null)
            gridSurface = FindAnyObjectByType<GridSurface>();

        if (gridOccupancy == null)
            gridOccupancy = FindAnyObjectByType<GridOccupancy>();

        if (edgeOccupancy == null)
            edgeOccupancy = FindAnyObjectByType<EdgeOccupancy>();

        if (roadNetwork == null)
            roadNetwork = FindAnyObjectByType<RoadNetwork>();
    }

    /// <summary>
    /// Finds a path between two grid cells. The returned path includes both endpoints.
    /// Returns an empty list when no valid path exists.
    /// </summary>
    public List<Vector2Int> FindPath(
        Vector2Int start,
        Vector2Int target,
        PedestrianAccessProfile accessProfile = null,
        bool allowOccupiedEndpoints = false,
        PlacedBuilding allowedStartFootprint = null,
        PlacedBuilding allowedTargetFootprint = null)
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        if (!HasValidReferences() ||
            !(IsWalkableForRoute(start, start, target, allowOccupiedEndpoints, allowedStartFootprint, allowedTargetFootprint)) ||
            !(IsWalkableForRoute(target, start, target, allowOccupiedEndpoints, allowedStartFootprint, allowedTargetFootprint)))
        {
            return new List<Vector2Int>();
        }

        if (start == target)
            return new List<Vector2Int> { start };

        openSet.Clear();
        cameFrom.Clear();
        gScore.Clear();
        closedSet.Clear();
        randomCellPenalties.Clear();

        // Use a private lightweight random generator so route variation does
        // not allocate a System.Random instance or consume Unity's global RNG.
        randomState = unchecked(Environment.TickCount ^ (++pathSearchCounter * 397));
        if (randomState == 0)
            randomState = 1;

        // Reset and shuffle neighbour evaluation order for this search.
        for (int i = 0; i < neighborOrder.Length; i++)
            neighborOrder[i] = i;

        for (int i = neighborOrder.Length - 1; i > 0; i--)
        {
            int swapIndex = NextRandom(i + 1);
            int temp = neighborOrder[i];
            neighborOrder[i] = neighborOrder[swapIndex];
            neighborOrder[swapIndex] = temp;
        }

        int order = 0;
        gScore[start] = 0;
        Enqueue(openSet, new OpenNode(
            start,
            0,
            GetHeuristic(start, target),
            order++));

        while (openSet.Count > 0)
        {
            OpenNode currentNode = Dequeue(openSet);
            Vector2Int current = currentNode.Position;

            if (closedSet.Contains(current))
                continue;

            if (!gScore.TryGetValue(current, out int currentBestG) ||
                currentNode.G != currentBestG)
            {
                continue;
            }

            if (current == target)
                return ReconstructPath(cameFrom, start, target);

            closedSet.Add(current);

            for (int i = 0; i < neighborOrder.Length; i++)
            {
                Vector2Int next = current + NeighborOffsets[neighborOrder[i]];

                if (closedSet.Contains(next) ||
                    !CanMoveBetween(current, next, accessProfile, allowOccupiedEndpoints && next == target, start, target, allowedStartFootprint, allowedTargetFootprint))
                {
                    continue;
                }

                Vector2Int difference = next - current;
                int moveCost = difference.x != 0 && difference.y != 0
                    ? DiagonalMoveCost
                    : StraightMoveCost;
                int randomPenalty = 0;
                if (randomStepPenalty > 0)
                {
                    if (!randomCellPenalties.TryGetValue(next, out randomPenalty))
                    {
                        randomPenalty = NextRandom(randomStepPenalty + 1);
                        randomCellPenalties[next] = randomPenalty;
                    }
                }

                // Roads remain walkable, but pedestrians strongly prefer
                // pedestrian tarmac, foundations, grass and ordinary tarmac when alternatives exist.
                int roadPenalty = IsRoadCell(next) ? RoadTraversalPenalty : 0;
                int surfacePenalty = GetSurfaceTraversalPenalty(next);
                int tentativeG = currentBestG + moveCost + randomPenalty + roadPenalty + surfacePenalty;

                if (gScore.TryGetValue(next, out int previousG) &&
                    tentativeG >= previousG)
                {
                    continue;
                }

                cameFrom[next] = current;
                gScore[next] = tentativeG;

                Enqueue(openSet, new OpenNode(
                    next,
                    tentativeG,
                    GetHeuristic(next, target),
                    order++));
            }
        }

        return new List<Vector2Int>();
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("PedestrianPathfinder.FindPath", performanceTimingStart);
        }
    }

    /// <summary>
    /// Finds the nearest walkable pedestrian grid cell around a desired
    /// spawn point and returns its world-space centre.
    /// </summary>
    public bool TryFindNearestWalkablePosition(
        Vector3 desiredWorldPosition,
        int maximumSearchRadius,
        out Vector3 walkableWorldPosition)
    {
        walkableWorldPosition = desiredWorldPosition;

        if (!HasValidReferences())
            return false;

        Vector2Int originCell = gridManager.WorldToGrid(desiredWorldPosition);
        Vector2Int bestCell = default;
        float bestDistance = float.MaxValue;
        bool found = false;

        maximumSearchRadius = Mathf.Max(0, maximumSearchRadius);

        for (int radius = 0; radius <= maximumSearchRadius; radius++)
        {
            int minX = originCell.x - radius;
            int maxX = originCell.x + radius;
            int minY = originCell.y - radius;
            int maxY = originCell.y + radius;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (radius > 0 &&
                        x != minX && x != maxX &&
                        y != minY && y != maxY)
                    {
                        continue;
                    }

                    Vector2Int candidate = new Vector2Int(x, y);
                    if (!IsWalkable(candidate))
                        continue;

                    Vector3 candidateWorld =
                        gridManager.GridToWorldCenter(candidate);

                    float distance = HorizontalDistanceSquared(
                        desiredWorldPosition,
                        candidateWorld);

                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    bestCell = candidate;
                    found = true;
                }
            }

            // Once a candidate exists, a farther ring cannot contain a
            // closer grid cell than the ring just searched.
            if (found)
                break;
        }

        if (!found)
            return false;

        walkableWorldPosition =
            gridManager.GridToWorldCenter(bestCell);
        walkableWorldPosition.y += pathHeightOffset;
        return true;
    }

    /// <summary>
    /// Selects a varied pedestrian waiting position on the side of a bus stop
    /// sign opposite the nearest road cell. Road cells are never valid results.
    /// The lateral offset spreads passengers along the pavement parallel to the road.
    /// </summary>
    public bool TryFindBusStopWaitingPosition(
        Vector3 stopSignPosition,
        float lateralOffset,
        float distanceFromSign,
        int maximumSearchRadius,
        out Vector3 waitingWorldPosition)
    {
        waitingWorldPosition = stopSignPosition;
        if (!HasValidReferences() || roadNetwork == null)
            return false;

        Vector2Int signCell = gridManager.WorldToGrid(stopSignPosition);
        Vector2Int nearestRoadCell = default;
        float nearestRoadDistance = float.MaxValue;
        bool foundRoad = false;
        const int roadSearchRadius = 6;

        for (int x = -roadSearchRadius; x <= roadSearchRadius; x++)
        {
            for (int y = -roadSearchRadius; y <= roadSearchRadius; y++)
            {
                Vector2Int candidate = signCell + new Vector2Int(x, y);
                if (!gridManager.IsInsideGrid(candidate) || !roadNetwork.IsRoad(candidate))
                    continue;

                Vector3 roadWorld = gridManager.GridToWorldCenter(candidate);
                float distance = HorizontalDistanceSquared(stopSignPosition, roadWorld);
                if (distance >= nearestRoadDistance)
                    continue;

                nearestRoadDistance = distance;
                nearestRoadCell = candidate;
                foundRoad = true;
            }
        }

        if (!foundRoad)
            return false;

        Vector3 roadPosition = gridManager.GridToWorldCenter(nearestRoadCell);
        Vector3 awayFromRoad = stopSignPosition - roadPosition;
        awayFromRoad.y = 0f;
        if (awayFromRoad.sqrMagnitude < 0.0001f)
            return false;
        awayFromRoad.Normalize();

        Vector3 alongRoad = Vector3.Cross(Vector3.up, awayFromRoad).normalized;
        Vector3 desiredPosition = stopSignPosition +
                                  alongRoad * lateralOffset +
                                  awayFromRoad * Mathf.Max(0.25f, distanceFromSign);
        Vector2Int originCell = gridManager.WorldToGrid(desiredPosition);
        Vector2Int bestCell = default;
        float bestDistance = float.MaxValue;
        bool found = false;

        maximumSearchRadius = Mathf.Max(0, maximumSearchRadius);
        for (int radius = 0; radius <= maximumSearchRadius; radius++)
        {
            int minX = originCell.x - radius;
            int maxX = originCell.x + radius;
            int minY = originCell.y - radius;
            int maxY = originCell.y + radius;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (radius > 0 && x != minX && x != maxX &&
                        y != minY && y != maxY)
                        continue;

                    Vector2Int candidate = new Vector2Int(x, y);
                    if (!IsWalkable(candidate) || IsRoadCell(candidate))
                        continue;

                    Vector3 candidateWorld = gridManager.GridToWorldCenter(candidate);
                    Vector3 fromSign = candidateWorld - stopSignPosition;
                    fromSign.y = 0f;
                    if (Vector3.Dot(fromSign, awayFromRoad) <= 0.1f)
                        continue;

                    float distance = HorizontalDistanceSquared(desiredPosition, candidateWorld);
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    bestCell = candidate;
                    found = true;
                }
            }

            if (found)
                break;
        }

        if (!found)
            return false;

        waitingWorldPosition = gridManager.GridToWorldCenter(bestCell);
        waitingWorldPosition.y += pathHeightOffset;
        return true;
    }

    private static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    /// <summary>
    /// Finds a path using world positions. The returned list contains world-space cell centres.
    /// </summary>
    public List<Vector3> FindWorldPath(
        Vector3 startWorld,
        Vector3 targetWorld,
        PedestrianAccessProfile accessProfile = null,
        bool allowOccupiedEndpoints = false,
        PlacedBuilding allowedStartFootprint = null,
        PlacedBuilding allowedTargetFootprint = null)
    {
        List<Vector3> worldPath = new List<Vector3>();

        if (!HasValidReferences())
            return worldPath;

        List<Vector2Int> gridPath = FindPath(
            gridManager.WorldToGrid(startWorld),
            gridManager.WorldToGrid(targetWorld),
            accessProfile,
            allowOccupiedEndpoints,
            allowedStartFootprint,
            allowedTargetFootprint);

        return ConvertGridPathToWorld(SmoothPath(
            gridPath, accessProfile, allowOccupiedEndpoints,
            allowedStartFootprint, allowedTargetFootprint));
    }

    /// <summary>
    /// Tries the provider's preferred InteractionPoint first. If that cell is
    /// blocked or unreachable, searches the cells immediately surrounding the
    /// provider's occupied footprint and returns the shortest reachable route.
    /// </summary>
    public bool TryFindBestApproachPath(
        Vector3 startWorld,
        Vector3 preferredWorld,
        Vector3 providerWorld,
        PlacedBuilding targetBuilding,
        out Vector3 selectedDestination,
        out List<Vector3> worldPath,
        bool logFailure = true,
        PedestrianAccessProfile accessProfile = null)
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        selectedDestination = preferredWorld;
        worldPath = FindWorldPath(startWorld, preferredWorld, accessProfile);

        // Preserve existing behaviour whenever the preferred point is reachable.
        if (worldPath.Count > 0)
            return true;

        if (!HasValidReferences())
            return false;

        List<Vector2Int> approachCells =
            GetApproachCells(targetBuilding, preferredWorld, providerWorld, accessProfile);

        Vector2Int startCell = gridManager.WorldToGrid(startWorld);
        List<Vector2Int> bestPath = null;
        Vector2Int bestTarget = default;
        int bestCost = int.MaxValue;

        int reachableCandidateCount = 0;
        for (int i = 0; i < approachCells.Count; i++)
        {
            Vector2Int candidate = approachCells[i];
            List<Vector2Int> candidatePath = FindPath(startCell, candidate, accessProfile);

            if (candidatePath.Count == 0)
                continue;

            reachableCandidateCount++;
            int candidateCost = CalculatePathCost(candidatePath);
            if (candidateCost >= bestCost)
                continue;

            bestCost = candidateCost;
            bestTarget = candidate;
            bestPath = candidatePath;
        }

        if (bestPath == null)
        {
            string buildingInfo = targetBuilding == null
                ? "none"
                : $"origin={targetBuilding.Origin}, size={targetBuilding.Width}x{targetBuilding.Depth}, " +
                  $"edge={targetBuilding.IsEdgeBuilding}, surfacePlaced={targetBuilding.IsSurfacePlaced}";

            if (logFailure)
            {
                LogWarning(
                    $"PedestrianPathfinder: No interaction approach route. " +
                    $"Start={startWorld} cell={startCell} startWalkable={IsWalkable(startCell)}; " +
                    $"Preferred={preferredWorld} cell={gridManager.WorldToGrid(preferredWorld)}; " +
                    $"Provider={providerWorld} cell={gridManager.WorldToGrid(providerWorld)}; " +
                    $"PlacedBuilding={buildingInfo}; candidates={approachCells.Count}, " +
                    $"reachable={reachableCandidateCount}. " +
                    $"Candidate cells: {string.Join(", ", approachCells)}.");
            }
            return false;
        }

        worldPath = ConvertGridPathToWorld(SmoothPath(bestPath, accessProfile));
        selectedDestination = gridManager.GridToWorldCenter(bestTarget);
        selectedDestination.y += pathHeightOffset;
        return worldPath.Count > 0;
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("PedestrianPathfinder.TryFindBestApproachPath", performanceTimingStart);
        }
    }

    private List<Vector2Int> GetApproachCells(
        PlacedBuilding targetBuilding,
        Vector3 preferredWorld,
        Vector3 providerWorld,
        PedestrianAccessProfile accessProfile)
    {
        List<Vector2Int> result = new List<Vector2Int>(16);

        if (targetBuilding != null && targetBuilding.IsSurfacePlaced)
            targetBuilding = targetBuilding.SupportingSurface;

        // A provider without a building footprint (for example an aircraft)
        // can only be approached from a cardinally adjacent cell. Validate
        // the edge between the provider cell and candidate so walls are not
        // mistaken for valid approach positions.
        if (targetBuilding == null)
        {
            Vector2Int anchor = gridManager.WorldToGrid(providerWorld);
            AddAdjacentApproachCell(result, anchor + Vector2Int.right, anchor, accessProfile);
            AddAdjacentApproachCell(result, anchor + Vector2Int.left, anchor, accessProfile);
            AddAdjacentApproachCell(result, anchor + Vector2Int.up, anchor, accessProfile);
            AddAdjacentApproachCell(result, anchor + Vector2Int.down, anchor, accessProfile);

            // The preferred interaction point is the intended boarding side
            // of the vehicle. Include its own cell when walkable, not only the
            // cells around it.
            Vector2Int preferred = gridManager.WorldToGrid(preferredWorld);
            AddWalkableApproachCell(result, preferred);

            // Vehicle pivots and parking positions can sit on blocked cells,
            // while the actual boarding side may be offset from that pivot.
            // Collect nearby walkable cells around both reference points
            // without requiring an edge crossing from the vehicle's cell:
            // that cell may be occupied by the vehicle or bordered by a curb.
            // The subsequent A* search still validates the pedestrian's
            // complete route, including every wall and door edge.
            AddNearbyApproachCells(result, preferred, 2);
            AddNearbyApproachCells(result, anchor, 2);

            return result;
        }

        if (targetBuilding.IsEdgeBuilding)
        {
            int minX = targetBuilding.Origin.x;
            int minY = targetBuilding.Origin.y;
            int maxX = minX;
            int maxY = minY;

            if (targetBuilding.EdgeDirection == EdgeDirection.Horizontal)
                maxX += Mathf.Max(1, targetBuilding.EdgeLength) - 1;
            else
                maxY += Mathf.Max(1, targetBuilding.EdgeLength) - 1;

            AddFootprintBoundaryApproachCells(result, minX, minY, maxX, maxY, accessProfile);
            return result;
        }

        int footprintMinX = targetBuilding.Origin.x;
        int footprintMinY = targetBuilding.Origin.y;
        int footprintMaxX =
            footprintMinX + Mathf.Max(1, targetBuilding.Width) - 1;
        int footprintMaxY =
            footprintMinY + Mathf.Max(1, targetBuilding.Depth) - 1;

        AddFootprintBoundaryApproachCells(
            result,
            footprintMinX,
            footprintMinY,
            footprintMaxX,
            footprintMaxY,
            accessProfile);

        return result;
    }

    private void AddFootprintBoundaryApproachCells(
        List<Vector2Int> cells,
        int minX,
        int minY,
        int maxX,
        int maxY,
        PedestrianAccessProfile accessProfile)
    {
        // Approach cells must touch the footprint directly. This avoids
        // treating cells several tiles away, or on the far side of a wall,
        // as valid interaction positions.
        for (int x = minX; x <= maxX; x++)
        {
            AddAdjacentApproachCell(cells, new Vector2Int(x, minY - 1), new Vector2Int(x, minY), accessProfile);
            AddAdjacentApproachCell(cells, new Vector2Int(x, maxY + 1), new Vector2Int(x, maxY), accessProfile);
        }

        for (int y = minY; y <= maxY; y++)
        {
            AddAdjacentApproachCell(cells, new Vector2Int(minX - 1, y), new Vector2Int(minX, y), accessProfile);
            AddAdjacentApproachCell(cells, new Vector2Int(maxX + 1, y), new Vector2Int(maxX, y), accessProfile);
        }
    }

    private void AddAdjacentApproachCell(
        List<Vector2Int> cells,
        Vector2Int candidate,
        Vector2Int footprintCell,
        PedestrianAccessProfile accessProfile)
    {
        if (cells.Contains(candidate) ||
            !IsWalkable(candidate) ||
            !CanMoveCardinal(footprintCell, candidate, accessProfile))
        {
            return;
        }

        cells.Add(candidate);
    }

    private void AddWalkableApproachCell(
        List<Vector2Int> cells,
        Vector2Int candidate)
    {
        if (!cells.Contains(candidate) && IsWalkable(candidate))
            cells.Add(candidate);
    }

    private void AddNearbyApproachCells(
        List<Vector2Int> cells,
        Vector2Int anchor,
        int maximumRadius)
    {
        // Scan a bounded square instead of flood-filling from the vehicle
        // pivot. The pivot can be on a road/occupied cell or separated from
        // the pedestrian area by a curb, so it is not a reliable flood origin.
        // Only candidate cells need to be walkable; FindPath validates the
        // pedestrian's route to each candidate with normal edge checks.
        for (int x = -maximumRadius; x <= maximumRadius; x++)
        {
            for (int y = -maximumRadius; y <= maximumRadius; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                AddWalkableApproachCell(
                    cells,
                    anchor + new Vector2Int(x, y));
            }
        }
    }

    private int CalculatePathCost(List<Vector2Int> path)
    {
        int cost = 0;

        for (int i = 1; i < path.Count; i++)
        {
            Vector2Int difference = path[i] - path[i - 1];
            cost += difference.x != 0 && difference.y != 0
                ? DiagonalMoveCost
                : StraightMoveCost;

            if (IsRoadCell(path[i]))
                cost += RoadTraversalPenalty;
            cost += GetSurfaceTraversalPenalty(path[i]);
        }

        return cost;
    }

    private bool IsRoadCell(Vector2Int cell)
    {
        return roadNetwork != null && roadNetwork.IsRoad(cell);
    }

    private int GetSurfaceTraversalPenalty(Vector2Int cell)
    {
        switch (gridSurface.GetSurface(cell))
        {
            case BuildSurface.PedestrianTarmac: return PedestrianTarmacTraversalPenalty;
            case BuildSurface.Tarmac: return TarmacTraversalPenalty;
            case BuildSurface.Foundation: return FoundationTraversalPenalty;
            case BuildSurface.Grass:
            default: return GrassTraversalPenalty;
        }
    }

    private List<Vector3> ConvertGridPathToWorld(List<Vector2Int> gridPath)
    {
        List<Vector3> worldPath = new List<Vector3>(gridPath.Count);

        foreach (Vector2Int cell in gridPath)
        {
            Vector3 position = gridManager.GridToWorldCenter(cell);
            position.y += pathHeightOffset;
            worldPath.Add(position);
        }

        return worldPath;
    }

    /// <summary>
    /// Removes unnecessary intermediate grid waypoints when a short,
    /// collision-safe straight segment can connect the remaining cells.
    /// A limited look-ahead preserves the overall shape of randomized routes.
    /// </summary>
    private List<Vector2Int> SmoothPath(
        List<Vector2Int> path,
        PedestrianAccessProfile accessProfile,
        bool allowOccupiedEndpoints = false,
        PlacedBuilding allowedStartFootprint = null,
        PlacedBuilding allowedTargetFootprint = null)
    {
        if (path == null || path.Count <= 2 || smoothingLookAheadCells <= 1)
            return path;

        List<Vector2Int> smoothedPath = new List<Vector2Int>();
        int anchorIndex = 0;
        smoothedPath.Add(path[0]);

        while (anchorIndex < path.Count - 1)
        {
            int furthestCandidate = Mathf.Min(
                path.Count - 1,
                anchorIndex + smoothingLookAheadCells);

            int chosenIndex = anchorIndex + 1;

            for (int candidateIndex = furthestCandidate;
                 candidateIndex > anchorIndex + 1;
                 candidateIndex--)
            {
                if (HasClearLineOfSight(
                    path[anchorIndex],
                    path[candidateIndex],
                    accessProfile,
                    allowOccupiedEndpoints && candidateIndex == path.Count - 1,
                    path[0], path[path.Count - 1],
                    allowedStartFootprint, allowedTargetFootprint))
                {
                    chosenIndex = candidateIndex;
                    break;
                }
            }

            smoothedPath.Add(path[chosenIndex]);
            anchorIndex = chosenIndex;
        }

        return smoothedPath;
    }

    /// <summary>
    /// Uses a grid traversal between cell centres and validates every
    /// crossed step with the same walkability, wall and corner rules as A*.
    /// </summary>
    private bool HasClearLineOfSight(
        Vector2Int from, Vector2Int to,
        PedestrianAccessProfile accessProfile = null,
        bool allowOccupiedDestination = false,
        Vector2Int routeStart = default,
        Vector2Int routeTarget = default,
        PlacedBuilding allowedStartFootprint = null,
        PlacedBuilding allowedTargetFootprint = null)
    {
        int deltaX = Mathf.Abs(to.x - from.x);
        int deltaY = Mathf.Abs(to.y - from.y);
        int stepX = from.x < to.x ? 1 : -1;
        int stepY = from.y < to.y ? 1 : -1;
        int error = deltaX - deltaY;

        Vector2Int current = from;

        while (current != to)
        {
            int doubledError = error * 2;
            Vector2Int next = current;

            if (doubledError > -deltaY)
            {
                error -= deltaY;
                next.x += stepX;
            }

            if (doubledError < deltaX)
            {
                error += deltaX;
                next.y += stepY;
            }

            if (!CanMoveBetween(
                current, next, accessProfile,
                allowOccupiedDestination && next == to,
                routeStart, routeTarget,
                allowedStartFootprint, allowedTargetFootprint))
                return false;

            current = next;
        }

        return true;
    }

    /// <summary>
    /// Checks whether a short world-space movement segment stays on walkable
    /// cells and crosses only permitted grid edges. Used to keep local
    /// pedestrian avoidance from steering through walls or blocked cells.
    /// </summary>
    public bool CanMoveBetweenWorldPositions(
        Vector3 fromWorld,
        Vector3 toWorld,
        PedestrianAccessProfile accessProfile = null,
        PlacedBuilding allowedStartFootprint = null,
        PlacedBuilding allowedTargetFootprint = null)
    {
        if (!HasValidReferences())
            return false;

        Vector2Int from = gridManager.WorldToGrid(fromWorld);
        Vector2Int to = gridManager.WorldToGrid(toWorld);

        if (from == to)
        {
            return IsWalkableForRoute(
                to, from, to, false,
                allowedStartFootprint, allowedTargetFootprint);
        }

        return HasClearLineOfSight(
            from, to, accessProfile, false,
            from, to,
            allowedStartFootprint, allowedTargetFootprint);
    }

    public bool TryFindPath(
        Vector2Int start,
        Vector2Int target,
        out List<Vector2Int> path,
        PedestrianAccessProfile accessProfile = null)
    {
        path = FindPath(start, target, accessProfile);
        return path.Count > 0;
    }

    public bool IsWalkable(Vector2Int cell)
    {
        if (!HasValidReferences() ||
            !gridManager.IsInsideGrid(cell))
        {
            return false;
        }

        BuildSurface surface = gridSurface.GetSurface(cell);

        if (surface != BuildSurface.Grass &&
            surface != BuildSurface.Foundation &&
            surface != BuildSurface.Tarmac &&
            surface != BuildSurface.PedestrianTarmac &&
            surface != BuildSurface.Road)
        {
            return false;
        }

        if (gridSurface.IsObstacle(cell))
            return false;

        // Check the occupancy registry directly as well as the obstacle map.
        // This also covers buildings restored from a save before their obstacle
        // registration component has had an update to synchronize.
        if (gridOccupancy != null &&
            gridOccupancy.TryGetOccupant(cell, out GameObject occupant) &&
            occupant != null)
        {
            PlacedBuilding building = occupant.GetComponent<PlacedBuilding>();

            if (building != null &&
                building.Definition != null &&
                building.Definition.blocksPathfinding)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsWalkableForRoute(
        Vector2Int cell,
        Vector2Int routeStart,
        Vector2Int routeTarget,
        bool allowOccupiedDestination,
        PlacedBuilding allowedStartFootprint,
        PlacedBuilding allowedTargetFootprint)
    {
        if (IsInsideFootprint(cell, allowedStartFootprint) ||
            IsInsideFootprint(cell, allowedTargetFootprint))
        {
            if (!HasValidReferences() || !gridManager.IsInsideGrid(cell))
                return false;

            BuildSurface surface = gridSurface.GetSurface(cell);
            return surface == BuildSurface.Grass ||
                   surface == BuildSurface.Foundation ||
                   surface == BuildSurface.Tarmac ||
                   surface == BuildSurface.PedestrianTarmac ||
                   surface == BuildSurface.Road;
        }

        if (allowOccupiedDestination && cell == routeTarget)
            return IsWalkableEndpoint(cell);

        if (allowOccupiedDestination && cell == routeStart)
            return IsWalkableEndpoint(cell);

        return IsWalkable(cell);
    }

    private static bool IsInsideFootprint(Vector2Int cell, PlacedBuilding building)
    {
        if (building == null || building.IsEdgeBuilding || building.IsSurfacePlaced)
            return false;

        Vector2Int origin = building.Origin;
        return cell.x >= origin.x &&
               cell.y >= origin.y &&
               cell.x < origin.x + Mathf.Max(1, building.Width) &&
               cell.y < origin.y + Mathf.Max(1, building.Depth);
    }

    // The first and last cells represent the pedestrian's departure and
    // destination points. They may be inside an occupied gate/stop footprint,
    // so ignore cell occupancy and obstacle flags for these two cells only.
    // The surface and grid bounds must still be valid, and all crossed edges
    // remain subject to the normal door/wall access checks.
    private bool IsWalkableEndpoint(Vector2Int cell)
    {
        if (!HasValidReferences() || !gridManager.IsInsideGrid(cell))
            return false;

        BuildSurface surface = gridSurface.GetSurface(cell);
        return surface == BuildSurface.Grass ||
               surface == BuildSurface.Foundation ||
               surface == BuildSurface.Tarmac ||
               surface == BuildSurface.PedestrianTarmac ||
               surface == BuildSurface.Road;
    }

    private bool CanMoveBetween(
        Vector2Int from,
        Vector2Int to,
        PedestrianAccessProfile accessProfile,
        bool allowOccupiedDestination = false,
        Vector2Int routeStart = default,
        Vector2Int routeTarget = default,
        PlacedBuilding allowedStartFootprint = null,
        PlacedBuilding allowedTargetFootprint = null)
    {
        if (!IsWalkableForRoute(
                to, routeStart, routeTarget, allowOccupiedDestination,
                allowedStartFootprint, allowedTargetFootprint))
            return false;

        Vector2Int difference = to - from;

        // Diagonal movement is allowed only when both adjacent cells are
        // walkable and all four edges around the shared corner are clear.
        // This prevents cutting through wall corners or squeezing between
        // blocked cells.
        if (difference.x != 0 && difference.y != 0)
        {
            Vector2Int horizontalCell =
                from + new Vector2Int(difference.x, 0);
            Vector2Int verticalCell =
                from + new Vector2Int(0, difference.y);

            if (!IsWalkableForRoute(horizontalCell, routeStart, routeTarget, false, allowedStartFootprint, allowedTargetFootprint) ||
                !IsWalkableForRoute(verticalCell, routeStart, routeTarget, false, allowedStartFootprint, allowedTargetFootprint))
            {
                return false;
            }

            return CanMoveCardinal(from, horizontalCell, accessProfile) &&
                   CanMoveCardinal(from, verticalCell, accessProfile) &&
                   CanMoveCardinal(horizontalCell, to, accessProfile) &&
                   CanMoveCardinal(verticalCell, to, accessProfile);
        }

        return CanMoveCardinal(from, to, accessProfile);
    }

    private bool CanMoveCardinal(
        Vector2Int from,
        Vector2Int to,
        PedestrianAccessProfile accessProfile)
    {
        Vector2Int difference = to - from;
        GridEdge crossedEdge;

        if (difference == Vector2Int.right)
        {
            crossedEdge = new GridEdge(
                new Vector2Int(from.x + 1, from.y),
                EdgeDirection.Vertical);
        }
        else if (difference == Vector2Int.left)
        {
            crossedEdge = new GridEdge(
                new Vector2Int(from.x, from.y),
                EdgeDirection.Vertical);
        }
        else if (difference == Vector2Int.up)
        {
            crossedEdge = new GridEdge(
                new Vector2Int(from.x, from.y + 1),
                EdgeDirection.Horizontal);
        }
        else if (difference == Vector2Int.down)
        {
            crossedEdge = new GridEdge(
                new Vector2Int(from.x, from.y),
                EdgeDirection.Horizontal);
        }
        else
        {
            return false;
        }

        if (!edgeOccupancy.TryGetOccupant(
                crossedEdge,
                out GameObject edgeOccupant))
        {
            return true;
        }

        if (edgeOccupant == null)
            return false;

        PlacedBuilding edgeBuilding =
            edgeOccupant.GetComponent<PlacedBuilding>();

        // Door objects occupy the same grid edges as walls. Their optional
        // access controller decides whether this pedestrian may cross.
        if (edgeBuilding == null ||
            edgeBuilding.Definition == null ||
            edgeBuilding.Definition.category != BuildCategory.Door)
        {
            return false;
        }

        DoorAccessController doorAccess =
            edgeOccupant.GetComponent<DoorAccessController>();

        // Existing doors without a controller remain unrestricted.
        return doorAccess == null || doorAccess.CanPass(accessProfile);
    }

    private bool HasValidReferences()
    {
        if (gridManager != null &&
            gridSurface != null &&
            gridOccupancy != null &&
            edgeOccupancy != null)
        {
            return true;
        }

        ResolveReferences();

        return gridManager != null &&
               gridSurface != null &&
               gridOccupancy != null &&
               edgeOccupancy != null;
    }

    private int NextRandom(int exclusiveMaximum)
    {
        uint value = (uint)randomState;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        randomState = unchecked((int)value);
        return (int)(value % (uint)exclusiveMaximum);
    }

    private static int GetHeuristic(Vector2Int from, Vector2Int to)
    {
        int deltaX = Mathf.Abs(from.x - to.x);
        int deltaY = Mathf.Abs(from.y - to.y);
        int diagonalSteps = Mathf.Min(deltaX, deltaY);
        int straightSteps = Mathf.Max(deltaX, deltaY) - diagonalSteps;

        return diagonalSteps * DiagonalMoveCost +
               straightSteps * StraightMoveCost;
    }

    private static List<Vector2Int> ReconstructPath(
        Dictionary<Vector2Int, Vector2Int> cameFrom,
        Vector2Int start,
        Vector2Int target)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int current = target;
        path.Add(current);

        while (current != start)
        {
            if (!cameFrom.TryGetValue(current, out Vector2Int previous))
                return new List<Vector2Int>();

            current = previous;
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private struct OpenNode
    {
        public Vector2Int Position;
        public int G;
        public int H;
        public int Order;
        public int F => G + H;

        public OpenNode(Vector2Int position, int g, int h, int order)
        {
            Position = position;
            G = g;
            H = h;
            Order = order;
        }
    }

    private static void Enqueue(List<OpenNode> heap, OpenNode node)
    {
        heap.Add(node);
        int index = heap.Count - 1;

        while (index > 0)
        {
            int parent = (index - 1) / 2;

            if (!ComesBefore(node, heap[parent]))
                break;

            heap[index] = heap[parent];
            index = parent;
        }

        heap[index] = node;
    }

    private static OpenNode Dequeue(List<OpenNode> heap)
    {
        OpenNode result = heap[0];
        int lastIndex = heap.Count - 1;
        OpenNode last = heap[lastIndex];
        heap.RemoveAt(lastIndex);

        if (heap.Count == 0)
            return result;

        int index = 0;

        while (true)
        {
            int left = index * 2 + 1;

            if (left >= heap.Count)
                break;

            int right = left + 1;
            int bestChild =
                right < heap.Count && ComesBefore(heap[right], heap[left])
                    ? right
                    : left;

            if (!ComesBefore(heap[bestChild], last))
                break;

            heap[index] = heap[bestChild];
            index = bestChild;
        }

        heap[index] = last;
        return result;
    }

    private static bool ComesBefore(OpenNode a, OpenNode b)
    {
        if (a.F != b.F)
            return a.F < b.F;

        if (a.H != b.H)
            return a.H < b.H;

        return a.Order < b.Order;
    }

    private void Log(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.Log(message, context); else Debug.Log(message);
    }
    private void LogWarning(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message);
    }
    private void LogError(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogError(message, context); else Debug.LogError(message);
    }

}
