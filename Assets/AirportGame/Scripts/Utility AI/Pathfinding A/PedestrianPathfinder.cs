using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grid-based A* pathfinding for pedestrians.
/// Walkable surfaces: Grass, Foundation and Tarmac.
/// Runways, taxiways, registered building obstacles and occupied wall edges block movement.
/// Door edges remain traversable.
/// </summary>
public class PedestrianPathfinder : MonoBehaviour
{
    [Header("Grid References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private GridOccupancy gridOccupancy;
    [SerializeField] private EdgeOccupancy edgeOccupancy;

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

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = GetComponent<GridManager>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (gridManager != null)
        {
            if (gridSurface == null)
                gridSurface = gridManager.GetComponent<GridSurface>();

            if (gridOccupancy == null)
                gridOccupancy = gridManager.GetComponent<GridOccupancy>();

            if (edgeOccupancy == null)
                edgeOccupancy = gridManager.GetComponent<EdgeOccupancy>();
        }

        if (gridSurface == null)
            gridSurface = FindFirstObjectByType<GridSurface>();

        if (gridOccupancy == null)
            gridOccupancy = FindFirstObjectByType<GridOccupancy>();

        if (edgeOccupancy == null)
            edgeOccupancy = FindFirstObjectByType<EdgeOccupancy>();
    }

    /// <summary>
    /// Finds a path between two grid cells. The returned path includes both endpoints.
    /// Returns an empty list when no valid path exists.
    /// </summary>
    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int target)
    {
        if (!HasValidReferences() ||
            !IsWalkable(start) ||
            !IsWalkable(target))
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
                    !CanMoveBetween(current, next))
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

                int tentativeG = currentBestG + moveCost + randomPenalty;

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

    /// <summary>
    /// Finds a path using world positions. The returned list contains world-space cell centres.
    /// </summary>
    public List<Vector3> FindWorldPath(Vector3 startWorld, Vector3 targetWorld)
    {
        List<Vector3> worldPath = new List<Vector3>();

        if (!HasValidReferences())
            return worldPath;

        List<Vector2Int> gridPath = FindPath(
            gridManager.WorldToGrid(startWorld),
            gridManager.WorldToGrid(targetWorld));

        return ConvertGridPathToWorld(SmoothPath(gridPath));
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
        out List<Vector3> worldPath)
    {
        selectedDestination = preferredWorld;
        worldPath = FindWorldPath(startWorld, preferredWorld);

        // Preserve existing behaviour whenever the preferred point is reachable.
        if (worldPath.Count > 0)
            return true;

        if (!HasValidReferences())
            return false;

        List<Vector2Int> approachCells =
            GetApproachCells(targetBuilding, preferredWorld, providerWorld);

        Vector2Int startCell = gridManager.WorldToGrid(startWorld);
        List<Vector2Int> bestPath = null;
        Vector2Int bestTarget = default;
        int bestCost = int.MaxValue;

        int reachableCandidateCount = 0;
        for (int i = 0; i < approachCells.Count; i++)
        {
            Vector2Int candidate = approachCells[i];
            List<Vector2Int> candidatePath = FindPath(startCell, candidate);

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

            Debug.LogWarning(
                $"PedestrianPathfinder: No interaction approach route. " +
                $"Start={startWorld} cell={startCell} startWalkable={IsWalkable(startCell)}; " +
                $"Preferred={preferredWorld} cell={gridManager.WorldToGrid(preferredWorld)}; " +
                $"Provider={providerWorld} cell={gridManager.WorldToGrid(providerWorld)}; " +
                $"PlacedBuilding={buildingInfo}; candidates={approachCells.Count}, " +
                $"reachable={reachableCandidateCount}. " +
                $"Candidate cells: {string.Join(", ", approachCells)}.");
            return false;
        }

        worldPath = ConvertGridPathToWorld(SmoothPath(bestPath));
        selectedDestination = gridManager.GridToWorldCenter(bestTarget);
        selectedDestination.y += pathHeightOffset;
        return worldPath.Count > 0;
    }

    private List<Vector2Int> GetApproachCells(
        PlacedBuilding targetBuilding,
        Vector3 preferredWorld,
        Vector3 providerWorld)
    {
        List<Vector2Int> result = new List<Vector2Int>(48);

        // A surface-placed item may not have a supporting surface reference
        // (for example, when the object is restored or spawned dynamically).
        // In that case use its own world position as the local search anchor.
        if (targetBuilding != null && targetBuilding.IsSurfacePlaced)
            targetBuilding = targetBuilding.SupportingSurface;

        if (targetBuilding == null)
        {
            AddApproachRings(
                result,
                gridManager.WorldToGrid(providerWorld),
                3);
            AddApproachRings(
                result,
                gridManager.WorldToGrid(preferredWorld),
                3);
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

            AddRectangleApproachRings(result, minX, minY, maxX, maxY, 3);
            return result;
        }

        int footprintMinX = targetBuilding.Origin.x;
        int footprintMinY = targetBuilding.Origin.y;
        int footprintMaxX =
            footprintMinX + Mathf.Max(1, targetBuilding.Width) - 1;
        int footprintMaxY =
            footprintMinY + Mathf.Max(1, targetBuilding.Depth) - 1;

        AddRectangleApproachRings(
            result,
            footprintMinX,
            footprintMinY,
            footprintMaxX,
            footprintMaxY,
            3);

        // Include cells around the provider's actual transform as a safety net
        // for prefab pivots that do not sit at the footprint centre.
        AddApproachRings(result, gridManager.WorldToGrid(providerWorld), 2);
        return result;
    }

    private void AddRectangleApproachRings(
        List<Vector2Int> cells,
        int minX,
        int minY,
        int maxX,
        int maxY,
        int maximumRadius)
    {
        for (int radius = 1; radius <= maximumRadius; radius++)
        {
            for (int x = minX - radius; x <= maxX + radius; x++)
            {
                AddApproachCell(cells, new Vector2Int(x, minY - radius));
                AddApproachCell(cells, new Vector2Int(x, maxY + radius));
            }

            for (int y = minY - radius + 1; y <= maxY + radius - 1; y++)
            {
                AddApproachCell(cells, new Vector2Int(minX - radius, y));
                AddApproachCell(cells, new Vector2Int(maxX + radius, y));
            }
        }
    }

    private void AddApproachRings(
        List<Vector2Int> cells,
        Vector2Int centre,
        int maximumRadius)
    {
        for (int radius = 1; radius <= maximumRadius; radius++)
        {
            for (int x = centre.x - radius; x <= centre.x + radius; x++)
            {
                AddApproachCell(cells, new Vector2Int(x, centre.y - radius));
                AddApproachCell(cells, new Vector2Int(x, centre.y + radius));
            }

            for (int y = centre.y - radius + 1; y <= centre.y + radius - 1; y++)
            {
                AddApproachCell(cells, new Vector2Int(centre.x - radius, y));
                AddApproachCell(cells, new Vector2Int(centre.x + radius, y));
            }
        }
    }

    private void AddApproachCell(
        List<Vector2Int> cells,
        Vector2Int candidate)
    {
        if (cells.Contains(candidate) ||
            !IsWalkable(candidate))
        {
            return;
        }

        cells.Add(candidate);
    }

    private static int CalculatePathCost(List<Vector2Int> path)
    {
        int cost = 0;

        for (int i = 1; i < path.Count; i++)
        {
            Vector2Int difference = path[i] - path[i - 1];
            cost += difference.x != 0 && difference.y != 0
                ? DiagonalMoveCost
                : StraightMoveCost;
        }

        return cost;
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
    private List<Vector2Int> SmoothPath(List<Vector2Int> path)
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
                    path[candidateIndex]))
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
    private bool HasClearLineOfSight(Vector2Int from, Vector2Int to)
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

            if (!CanMoveBetween(current, next))
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
    public bool CanMoveBetweenWorldPositions(Vector3 fromWorld, Vector3 toWorld)
    {
        if (!HasValidReferences())
            return false;

        Vector2Int from = gridManager.WorldToGrid(fromWorld);
        Vector2Int to = gridManager.WorldToGrid(toWorld);

        if (from == to)
            return IsWalkable(from);

        return HasClearLineOfSight(from, to);
    }

    public bool TryFindPath(
        Vector2Int start,
        Vector2Int target,
        out List<Vector2Int> path)
    {
        path = FindPath(start, target);
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
            surface != BuildSurface.Tarmac)
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

    private bool CanMoveBetween(Vector2Int from, Vector2Int to)
    {
        if (!IsWalkable(to))
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

            if (!IsWalkable(horizontalCell) ||
                !IsWalkable(verticalCell))
            {
                return false;
            }

            return CanMoveCardinal(from, horizontalCell) &&
                   CanMoveCardinal(from, verticalCell) &&
                   CanMoveCardinal(horizontalCell, to) &&
                   CanMoveCardinal(verticalCell, to);
        }

        return CanMoveCardinal(from, to);
    }

    private bool CanMoveCardinal(Vector2Int from, Vector2Int to)
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

        // Door objects occupy the same grid edges as walls but allow passage.
        return edgeBuilding != null &&
               edgeBuilding.Definition != null &&
               edgeBuilding.Definition.category == BuildCategory.Door;
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
}
