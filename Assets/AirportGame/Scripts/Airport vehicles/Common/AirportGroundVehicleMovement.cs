using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AirportGroundVehicleMovement : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    [Header("Grid References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridOccupancy gridOccupancy;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;

    [Header("Movement Costs")]
    [Min(1)]
    [SerializeField] private int grassMovementCost = 10;
    [Min(1)]
    [SerializeField] private int foundationMovementCost = 1;
    [Min(1)]
    [SerializeField] private int tarmacMovementCost = 1;
    [Min(1)]
    [SerializeField] private int taxiwayMovementCost = 5;
    [Min(1)]
    [SerializeField] private int runwayMovementCost = 5;
    [Min(1)]
    [SerializeField] private int pedestrianTarmacMovementCost = 1;
    [Min(1)]
    [SerializeField] private int roadMovementCost = 5;

    [Header("Clearance")]
    [Range(0, 5)]
    [SerializeField] private int foundationClearance = 1;
    [Range(0, 5)]
    [SerializeField] private int runwayClearance = 1;

    [Header("Pathfinding")]
    [Range(1, 50)]
    [SerializeField] private int maxPathNodesPerFrame = 50;

    [Header("Pathfinding Diagnostics")]
    [SerializeField] private int lastPathSearchFrames;
    [SerializeField] private int lastPathSearchExpandedCells;
    [SerializeField] private int lastPathSearchDequeuedNodes;

    [Header("Movement")]
    [Min(0.1f)]
    [SerializeField] private float movementSpeed = 3f;
    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 5f;
    [Min(0.01f)]
    [SerializeField] private float waypointArrivalDistance = 0.08f;
    [Min(0f)]
    [SerializeField] private float groundHeightOffset = 0.05f;

    [Header("Smooth Turns")]
    [Min(0.5f)]
    [SerializeField] private float turningRadiusInCells = 2f;
    [Range(4, 20)]
    [SerializeField] private int turnCurveResolution = 10;

    [Header("Final Parking Curve")]
    [Min(0.5f)]
    [SerializeField] private float parkingTurnDistanceInCells = 2f;
    [Range(8, 30)]
    [SerializeField] private int parkingCurveResolution = 16;
    [SerializeField] private bool parkFacingWorldNegativeZ = true;

    [Header("Runtime Status")]
    [SerializeField] private bool isMoving;
    [SerializeField] private string movementStatus = "Idle";
    [SerializeField] private int routeCellCount;
    [SerializeField] private int currentWaypointIndex;

    private readonly List<Vector3> movementPath =
        new List<Vector3>();

    // Reused A* workspace, following the pedestrian pathfinder's allocation-
    // conscious implementation. Searches run sequentially on Unity's main thread.
    private readonly List<VehicleOpenNode> pathOpenSet =
        new List<VehicleOpenNode>(256);
    private readonly Dictionary<Vector2Int, Vector2Int> pathCameFrom =
        new Dictionary<Vector2Int, Vector2Int>(256);
    private readonly Dictionary<Vector2Int, int> pathGScore =
        new Dictionary<Vector2Int, int>(256);
    private readonly HashSet<Vector2Int> pathClosedSet =
        new HashSet<Vector2Int>();

    private static readonly Vector2Int[] PathNeighborOffsets =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left,
        new Vector2Int(1, 1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, -1),
        new Vector2Int(-1, 1)
    };

    private Vector3 destination;
    private Vector3 lastRequestedDestination;

    private Transform destinationFacingTransform;
    private Transform lastRequestedFacingTransform;

    private GameObject currentDestinationGate;
    private GameObject currentSourceGate;
    private GameObject lastRequestedDestinationGate;
    private GameObject lastRequestedSourceGate;

    private Transform homeSpawnPoint;

    private bool hasLastRequestedDestination;
    private bool isCalculatingPath;
    private Vector2Int pathStart;
    private Vector2Int pathTarget;
    private int pathInsertionOrder;
    private int pathMinimumMovementCost;
    private int pathSearchFrames;
    private int pathSearchDequeuedNodes;
    private int rejectedOutOfBounds;
    private int rejectedFoundation;
    private int rejectedClearance;
    private int rejectedOccupied;
    private int rejectedObstacle;
    private float movementHeight;

    public bool IsMoving => isMoving || isCalculatingPath;
    public bool IsCalculatingPath => isCalculatingPath;
    public string MovementStatus => movementStatus;
    public int RouteCellCount => routeCellCount;
    public int CurrentWaypointIndex => currentWaypointIndex;
    public IReadOnlyList<Vector3> MovementPath => movementPath;

    public float MovementSpeed
    {
        get => movementSpeed;
        set => movementSpeed = Mathf.Max(0.1f, value);
    }

    public float RotationSpeed
    {
        get => rotationSpeed;
        set => rotationSpeed = Mathf.Max(0.1f, value);
    }

    private void Awake()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (gridOccupancy == null)
            gridOccupancy = FindAnyObjectByType<GridOccupancy>();

        if (gridSurface == null)
            gridSurface = FindAnyObjectByType<GridSurface>();

        if (roadNetwork == null)
            roadNetwork = FindAnyObjectByType<RoadNetwork>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindAnyObjectByType<TaxiwayNetwork>();

        if (gridManager == null)
        {
            Fail("GridManager was not found.");
            return;
        }

        if (gridOccupancy == null)
        {
            Fail("GridOccupancy was not found.");
            return;
        }

        if (gridSurface == null)
        {
            Fail("GridSurface was not found.");
            return;
        }

        if (taxiwayNetwork == null)
        {
            Fail("TaxiwayNetwork was not found.");
            return;
        }

        if (roadNetwork == null)
        {
            Fail("RoadNetwork was not found.");
            return;
        }

        grassMovementCost = Mathf.Max(1, grassMovementCost);
        foundationMovementCost = Mathf.Max(1, foundationMovementCost);
        tarmacMovementCost = Mathf.Max(1, tarmacMovementCost);
        taxiwayMovementCost = Mathf.Max(1, taxiwayMovementCost);
        runwayMovementCost = Mathf.Max(1, runwayMovementCost);
        pedestrianTarmacMovementCost = Mathf.Max(1, pedestrianTarmacMovementCost);
        roadMovementCost = Mathf.Max(1, roadMovementCost);
        foundationClearance = Mathf.Clamp(foundationClearance, 0, 5);
        runwayClearance = Mathf.Clamp(runwayClearance, 0, 5);

        movementHeight = transform.position.y + groundHeightOffset;
    }

    private void Update()
    {
        if (isCalculatingPath)
            ProcessPathSearchFrame();
        else if (isMoving)
            UpdateMovement();
    }

    /// <summary>
    /// Identifies the vehicle's own parking lot or home area.
    /// Cells belonging to this area are allowed for this vehicle only.
    /// </summary>
    public void SetHomeArea(Transform spawnPoint)
    {
        homeSpawnPoint = spawnPoint;
    }

    /// <summary>
    /// Starts route calculation with an optional final facing direction.\n    /// Returns true when the request is accepted; completion is asynchronous.
    /// </summary>
    public bool MoveTo(
        Vector3 worldDestination,
        Transform facingTransform = null)
    {
        return MoveToInternal(
            worldDestination,
            facingTransform,
            null,
            currentDestinationGate);
    }

    /// <summary>
    /// Movement to a specific gate. Only this gate's footprint is
    /// permitted as an obstacle along this route.
    /// </summary>
    public bool MoveTo(
        Vector3 worldDestination,
        GameObject destinationGate)
    {
        return MoveToInternal(
            worldDestination,
            null,
            destinationGate,
            currentDestinationGate);
    }

    /// <summary>
    /// Movement with both a final facing direction and a permitted
    /// destination gate.
    /// </summary>
    public bool MoveTo(
        Vector3 worldDestination,
        Transform facingTransform,
        GameObject destinationGate)
    {
        return MoveToInternal(
            worldDestination,
            facingTransform,
            destinationGate,
            currentDestinationGate);
    }

    private bool MoveToInternal(
        Vector3 worldDestination,
        Transform facingTransform,
        GameObject destinationGate,
        GameObject sourceGate)
    {
        destinationFacingTransform = facingTransform;

        currentDestinationGate = destinationGate;
        currentSourceGate = null;

        lastRequestedFacingTransform = facingTransform;
        lastRequestedDestinationGate = destinationGate;
        lastRequestedSourceGate = sourceGate;
        lastRequestedDestination = worldDestination;
        hasLastRequestedDestination = true;

        isMoving = false;
        isCalculatingPath = false;
        movementPath.Clear();
        currentWaypointIndex = 0;
        routeCellCount = 0;
        movementStatus = "Calculating route";

        if (!HasRequiredReferences())
        {
            Fail("Cannot move: one or more grid references are missing.");
            return false;
        }

        Vector2Int startCell =
            gridManager.WorldToGrid(transform.position);

        Vector2Int targetCell =
            gridManager.WorldToGrid(worldDestination);

        if (!gridManager.IsInsideGrid(startCell) ||
            !gridManager.IsInsideGrid(targetCell))
        {
            Fail(
                $"Start or target is outside the grid. " +
                $"Start: {startCell}, Target: {targetCell}");
            return false;
        }

        if (IsCellOwnedBy(startCell, sourceGate))
        {
            currentSourceGate = sourceGate;
        }

        BeginPathSearch(startCell, targetCell);
        return !movementStatus.StartsWith("Failed:");
    }

    private bool HasRequiredReferences()
    {
        return gridManager != null &&
               gridOccupancy != null &&
               gridSurface != null &&
               roadNetwork != null &&
               taxiwayNetwork != null;
    }

    [ContextMenu("Recalculate Last Route")]
    public bool RecalculateLastRoute()
    {
        if (!hasLastRequestedDestination)
        {
            LogWarning(
                $"{name}: There is no previous destination to recalculate.",
                this);
            return false;
        }

        if (isMoving || isCalculatingPath)
        {
            LogWarning(
                $"{name}: Cannot recalculate while the vehicle is moving or calculating a route. " +
                "Stop the vehicle first.",
                this);
            return false;
        }

        bool routeStarted = MoveToInternal(
            lastRequestedDestination,
            lastRequestedFacingTransform,
            lastRequestedDestinationGate,
            lastRequestedSourceGate);

        Log(
            $"{name}: Manual route recalculation " +
            (routeStarted ? "started." : "failed.") +
            $" Destination: {lastRequestedDestination}.",
            this);

        return routeStarted;
    }

    // ---------------------------------------------------------------------
    // Home area and destination obstacle ownership
    // ---------------------------------------------------------------------

    private bool IsHomeAreaCell(Vector2Int cell)
    {
        if (homeSpawnPoint == null || gridOccupancy == null)
            return false;

        if (gridOccupancy.IsOccupiedBy(
                cell,
                homeSpawnPoint.gameObject))
        {
            return true;
        }

        Transform homeAreaParent = homeSpawnPoint.parent;

        return homeAreaParent != null &&
               gridOccupancy.IsOccupiedBy(
                   cell,
                   homeAreaParent.gameObject);
    }

    private bool IsCurrentDestinationGateCell(Vector2Int cell)
    {
        return IsCellOwnedBy(cell, currentDestinationGate) ||
               IsCellOwnedBy(cell, currentSourceGate);
    }

    private bool IsCellOwnedBy(Vector2Int cell, GameObject owner)
    {
        if (owner == null)
            return false;

        return gridOccupancy.IsOccupiedBy(cell, owner) ||
               gridSurface.IsObstacleOwnedBy(cell, owner) ||
               gridSurface.IsSurfaceOwner(cell, owner);
    }

    // ---------------------------------------------------------------------
    // A* pathfinding
    // ---------------------------------------------------------------------

    private void BeginPathSearch(Vector2Int start, Vector2Int target)
    {
        pathStart = start;
        pathTarget = target;
        pathSearchFrames = 0;
        pathSearchDequeuedNodes = 0;
        lastPathSearchFrames = 0;
        lastPathSearchExpandedCells = 0;
        lastPathSearchDequeuedNodes = 0;

        if (start == target)
        {
            CompletePathSearch(new List<Vector2Int> { start });
            return;
        }

        bool targetObstacle = gridSurface.IsObstacle(target);
        bool targetHomeCell = IsHomeAreaCell(target);
        bool targetDestinationCell = IsCurrentDestinationGateCell(target);

        if (logToConsole)
        {
            bool targetOccupied = gridOccupancy.IsOccupied(target);
            bool targetOccupiedByDestination =
                currentDestinationGate != null &&
                gridOccupancy.IsOccupiedBy(target, currentDestinationGate);
            bool targetObstacleOwnedByDestination =
                currentDestinationGate != null &&
                gridSurface.IsObstacleOwnedBy(target, currentDestinationGate);
            bool targetSurfaceOwnedByDestination =
                currentDestinationGate != null &&
                gridSurface.IsSurfaceOwner(target, currentDestinationGate);

            Log(
                $"{name}: PATHFINDING TARGET DIAGNOSTICS\n" +
                $"Start: {start}, Target: {target}\n" +
                $"Destination object: {(currentDestinationGate != null ? currentDestinationGate.name : "None")}\n" +
                $"Destination object ID: {(currentDestinationGate != null ? currentDestinationGate.GetEntityId().ToString() : "None")}\n" +
                $"Target surface: {gridSurface.GetSurface(target)}\n" +
                $"Target occupied: {targetOccupied}\n" +
                $"Target occupied by destination: {targetOccupiedByDestination}\n" +
                $"Target obstacle: {targetObstacle}\n" +
                $"Target obstacle owned by destination: {targetObstacleOwnedByDestination}\n" +
                $"Target surface owned by destination: {targetSurfaceOwnedByDestination}\n" +
                $"Target is home cell: {targetHomeCell}\n" +
                $"Target is permitted destination cell: {targetDestinationCell}",
                this);
        }

        if (targetObstacle && !targetHomeCell && !targetDestinationCell)
        {
            Fail("No route: target is an obstacle not owned by the permitted destination.");
            return;
        }

        pathOpenSet.Clear();
        pathCameFrom.Clear();
        pathGScore.Clear();
        pathClosedSet.Clear();

        rejectedOutOfBounds = 0;
        rejectedFoundation = 0;
        rejectedClearance = 0;
        rejectedOccupied = 0;
        rejectedObstacle = 0;
        pathInsertionOrder = 0;

        pathMinimumMovementCost = GetMinimumMovementCost();
        pathGScore[start] = 0;
        EnqueuePathNode(new VehicleOpenNode(
            start,
            0,
            OctileDistance(start, target) * pathMinimumMovementCost,
            pathInsertionOrder++));

        isMoving = false;
        isCalculatingPath = true;
        movementStatus = "Calculating route";
    }

    private void ProcessPathSearchFrame()
    {
        int nodesProcessed = 0;
        // Clamp existing serialized component values too, so older prefabs
        // configured to 200 also receive the safer per-frame cap.
        int frameNodeLimit = Mathf.Clamp(maxPathNodesPerFrame, 1, 50);
        pathSearchFrames++;

        while (pathOpenSet.Count > 0 && nodesProcessed < frameNodeLimit)
        {
            VehicleOpenNode currentNode = DequeuePathNode();
            nodesProcessed++;
            pathSearchDequeuedNodes++;

            Vector2Int current = currentNode.Position;

            // Improved routes add a new heap entry. Ignore older entries whose
            // cost is no longer the best known cost for this cell.
            if (!pathGScore.TryGetValue(current, out int currentBestG) ||
                currentNode.G != currentBestG ||
                !pathClosedSet.Add(current))
            {
                continue;
            }

            if (current == pathTarget)
            {
                CompletePathSearch(
                    ReconstructPath(pathCameFrom, pathStart, pathTarget));
                return;
            }

            for (int i = 0; i < PathNeighborOffsets.Length; i++)
            {
                Vector2Int neighbor = current + PathNeighborOffsets[i];

                if (!gridManager.IsInsideGrid(neighbor))
                {
                    rejectedOutOfBounds++;
                    continue;
                }

                if (pathClosedSet.Contains(neighbor))
                    continue;

                bool isHomeCell = IsHomeAreaCell(neighbor);
                bool isDestinationGateCell =
                    IsCurrentDestinationGateCell(neighbor);

                int deltaX = Mathf.Abs(neighbor.x - current.x);
                int deltaY = Mathf.Abs(neighbor.y - current.y);
                bool isDiagonal = deltaX == 1 && deltaY == 1;

                if (isDiagonal)
                {
                    Vector2Int horizontalSideCell =
                        current + new Vector2Int(neighbor.x - current.x, 0);
                    Vector2Int verticalSideCell =
                        current + new Vector2Int(0, neighbor.y - current.y);

                    if (IsBlockedForDiagonal(horizontalSideCell, pathStart, pathTarget) ||
                        IsBlockedForDiagonal(verticalSideCell, pathStart, pathTarget))
                    {
                        rejectedObstacle++;
                        continue;
                    }
                }

                if (neighbor != pathStart &&
                    neighbor != pathTarget &&
                    !isHomeCell &&
                    !isDestinationGateCell)
                {
                    if (gridSurface.GetSurface(neighbor) == BuildSurface.Foundation)
                    {
                        rejectedFoundation++;
                        continue;
                    }

                    if (IsWithinFoundationOrRunwayClearance(neighbor))
                    {
                        rejectedClearance++;
                        continue;
                    }

                    if (gridOccupancy.IsOccupied(neighbor))
                    {
                        rejectedOccupied++;
                        continue;
                    }

                    if (gridSurface.IsObstacle(neighbor))
                    {
                        rejectedObstacle++;
                        continue;
                    }
                }

                int stepCost = GetMovementCost(neighbor);
                if (isDiagonal)
                    stepCost = Mathf.CeilToInt(stepCost * 1.41421356f);

                int tentativeScore = currentBestG + stepCost;

                if (pathGScore.TryGetValue(neighbor, out int existingScore) &&
                    tentativeScore >= existingScore)
                {
                    continue;
                }

                pathCameFrom[neighbor] = current;
                pathGScore[neighbor] = tentativeScore;

                EnqueuePathNode(new VehicleOpenNode(
                    neighbor,
                    tentativeScore,
                    OctileDistance(neighbor, pathTarget) * pathMinimumMovementCost,
                    pathInsertionOrder++));
            }
        }

        if (pathOpenSet.Count == 0)
        {
            string diagnostic =
                $"No route found from {pathStart} to {pathTarget}. " +
                $"Expanded cells: {pathClosedSet.Count}; " +
                $"rejected out of bounds: {rejectedOutOfBounds}; " +
                $"foundation: {rejectedFoundation}; " +
                $"clearance: {rejectedClearance}; " +
                $"occupied: {rejectedOccupied}; obstacles: {rejectedObstacle}.";

            if (logToConsole)
                LogWarning($"{name}: PATHFINDING FAILED\n{diagnostic}", this);

            Fail(diagnostic);
        }
    }

    private void CompletePathSearch(List<Vector2Int> cells)
    {
        isCalculatingPath = false;
        lastPathSearchFrames = pathSearchFrames;
        lastPathSearchExpandedCells = pathClosedSet.Count;
        lastPathSearchDequeuedNodes = pathSearchDequeuedNodes;

        if (cells == null || cells.Count == 0)
        {
            Fail(
                $"No route found from {pathStart} to {pathTarget}. " +
                "Check foundation clearance, occupied cells and grid bounds.");
            return;
        }

        routeCellCount = cells.Count;
        List<Vector3> cellPositions = new List<Vector3>(cells.Count);

        foreach (Vector2Int cell in cells)
        {
            Vector3 point = gridManager.GridToWorldCenter(cell);
            point.y = movementHeight;
            cellPositions.Add(point);
        }

        movementPath.Clear();
        BuildSmoothedMovementPath(cellPositions);

        destination = lastRequestedDestination;
        destination.y = movementHeight;

        if (movementPath.Count == 0 ||
            HorizontalDistance(
                movementPath[movementPath.Count - 1],
                destination) > 0.02f)
        {
            movementPath.Add(destination);
        }

        BuildFinalParkingCurve();

        currentWaypointIndex = 0;
        while (currentWaypointIndex < movementPath.Count &&
               HorizontalDistance(
                   transform.position,
                   movementPath[currentWaypointIndex]) <=
               waypointArrivalDistance)
        {
            currentWaypointIndex++;
        }

        isMoving = currentWaypointIndex < movementPath.Count;
        movementStatus = isMoving
            ? "Following smoothed grid route"
            : "Arrived";

        Log(
            $"{name}: Weighted route calculated. " +
            $"Grid cells: {routeCellCount}, " +
            $"waypoints: {movementPath.Count}, " +
            $"grass cost: {grassMovementCost}, " +
            $"foundation cost: {foundationMovementCost}, " +
            $"tarmac cost: {tarmacMovementCost}, " +
            $"taxiway cost: {taxiwayMovementCost}, " +
            $"runway cost: {runwayMovementCost}, " +
            $"pedestrian tarmac cost: {pedestrianTarmacMovementCost}, " +
            $"road cost: {roadMovementCost}, " +
            $"foundation clearance: {foundationClearance}, " +
            $"parking turn distance: {parkingTurnDistanceInCells} cells.",
            this);
    }

    private struct VehicleOpenNode
    {
        public Vector2Int Position;
        public int G;
        public float H;
        public float F => G + H;
        public int Order;

        public VehicleOpenNode(
            Vector2Int position,
            int g,
            float h,
            int order)
        {
            Position = position;
            G = g;
            H = h;
            Order = order;
        }
    }

    private void EnqueuePathNode(VehicleOpenNode node)
    {
        pathOpenSet.Add(node);
        int index = pathOpenSet.Count - 1;

        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (!PathNodeComesBefore(node, pathOpenSet[parent]))
                break;

            pathOpenSet[index] = pathOpenSet[parent];
            index = parent;
        }

        pathOpenSet[index] = node;
    }

    private VehicleOpenNode DequeuePathNode()
    {
        VehicleOpenNode result = pathOpenSet[0];
        int lastIndex = pathOpenSet.Count - 1;
        VehicleOpenNode last = pathOpenSet[lastIndex];
        pathOpenSet.RemoveAt(lastIndex);

        if (pathOpenSet.Count == 0)
            return result;

        int index = 0;
        while (true)
        {
            int left = index * 2 + 1;
            if (left >= pathOpenSet.Count)
                break;

            int right = left + 1;
            int bestChild =
                right < pathOpenSet.Count &&
                PathNodeComesBefore(pathOpenSet[right], pathOpenSet[left])
                    ? right
                    : left;

            if (!PathNodeComesBefore(pathOpenSet[bestChild], last))
                break;

            pathOpenSet[index] = pathOpenSet[bestChild];
            index = bestChild;
        }

        pathOpenSet[index] = last;
        return result;
    }

    private static bool PathNodeComesBefore(
        VehicleOpenNode a,
        VehicleOpenNode b)
    {
        if (a.F != b.F)
            return a.F < b.F;

        // Match the old list-based A* tie behaviour: retain insertion order
        // for equal estimated total costs.
        return a.Order < b.Order;
    }

    private bool IsWithinFoundationOrRunwayClearance(Vector2Int cell)
    {
        int searchRadius = Mathf.Max(
            foundationClearance,
            runwayClearance);

        if (searchRadius <= 0)
            return false;

        for (int x = -searchRadius; x <= searchRadius; x++)
        {
            for (int y = -searchRadius; y <= searchRadius; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                bool checkFoundation =
                    foundationClearance > 0 &&
                    Mathf.Abs(x) <= foundationClearance &&
                    Mathf.Abs(y) <= foundationClearance;

                bool checkRunway =
                    runwayClearance > 0 &&
                    Mathf.Abs(x) <= runwayClearance &&
                    Mathf.Abs(y) <= runwayClearance;

                if (!checkFoundation && !checkRunway)
                    continue;

                Vector2Int checkedCell = new Vector2Int(
                    cell.x + x,
                    cell.y + y);

                if (!gridManager.IsInsideGrid(checkedCell))
                    continue;

                if (IsHomeAreaCell(checkedCell) ||
                    IsCurrentDestinationGateCell(checkedCell))
                {
                    continue;
                }

                BuildSurface surface =
                    gridSurface.GetSurface(checkedCell);

                if ((checkFoundation &&
                     surface == BuildSurface.Foundation) ||
                    (checkRunway &&
                     surface == BuildSurface.Runway))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private int GetMovementCost(Vector2Int cell)
    {
        // Prefer the live network data where available, then use the
        // surface recorded on the grid for all other surface types.
        if (taxiwayNetwork.IsTaxiway(cell))
            return taxiwayMovementCost;

        if (roadNetwork.IsRoad(cell))
            return roadMovementCost;

        switch (gridSurface.GetSurface(cell))
        {
            case BuildSurface.Grass:
                return grassMovementCost;

            case BuildSurface.Foundation:
                return foundationMovementCost;

            case BuildSurface.Tarmac:
                return tarmacMovementCost;

            case BuildSurface.Taxiway:
                return taxiwayMovementCost;

            case BuildSurface.Runway:
                return runwayMovementCost;

            case BuildSurface.PedestrianTarmac:
                return pedestrianTarmacMovementCost;

            case BuildSurface.Road:
                return roadMovementCost;

            case BuildSurface.Any:
            default:
                return grassMovementCost;
        }
    }

    private List<Vector2Int> ReconstructPath(
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

    private float OctileDistance(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        int diagonalSteps = Mathf.Min(dx, dy);
        int straightSteps = Mathf.Max(dx, dy) - diagonalSteps;

        return diagonalSteps * 1.41421356f + straightSteps;
    }

    private int GetMinimumMovementCost()
    {
        // Foundation cells are rejected as intermediate route cells, so their
        // movement cost cannot contribute to a valid path. Including a lower
        // foundation cost weakens the A* heuristic and can cause many extra
        // cells to be expanded without improving route correctness.
        return Mathf.Min(
            grassMovementCost,
            tarmacMovementCost,
            taxiwayMovementCost,
            runwayMovementCost,
            pedestrianTarmacMovementCost,
            roadMovementCost);
    }

    private bool IsBlockedForDiagonal(
        Vector2Int cell,
        Vector2Int start,
        Vector2Int target)
    {
        if (!gridManager.IsInsideGrid(cell) ||
            cell == start ||
            cell == target ||
            IsHomeAreaCell(cell) ||
            IsCurrentDestinationGateCell(cell))
        {
            return false;
        }

        return gridSurface.GetSurface(cell) == BuildSurface.Foundation ||
               IsWithinFoundationOrRunwayClearance(cell) ||
               gridOccupancy.IsOccupied(cell) ||
               gridSurface.IsObstacle(cell);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;

        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ---------------------------------------------------------------------
    // Path construction and final parking curve
    // ---------------------------------------------------------------------

    private void BuildFinalParkingCurve()
    {
        if (movementPath.Count < 2)
            return;

        float cellSize = GetGridCellSize();

        if (cellSize <= 0.001f)
            return;

        float totalDistance = GetPathLength(movementPath);

        if (totalDistance <= 0.05f)
            return;

        float turnDistance =
            cellSize * parkingTurnDistanceInCells;

        turnDistance = Mathf.Min(turnDistance, totalDistance * 0.8f);

        float entryDistance = totalDistance - turnDistance;

        Vector3 entryPoint = GetPointAlongMovementPath(entryDistance);
        Vector3 pointAhead = GetPointAlongMovementPath(
            Mathf.Min(totalDistance, entryDistance + 0.1f));

        Vector3 approachDirection = pointAhead - entryPoint;
        approachDirection.y = 0f;

        if (approachDirection.sqrMagnitude < 0.0001f)
        {
            approachDirection = destination - entryPoint;
            approachDirection.y = 0f;
        }

        if (approachDirection.sqrMagnitude < 0.0001f)
            return;

        approachDirection.Normalize();

        Vector3 parkingDirection =
            destinationFacingTransform != null
                ? destinationFacingTransform.forward
                : (parkFacingWorldNegativeZ
                    ? Vector3.back
                    : Vector3.forward);

        parkingDirection.y = 0f;
        parkingDirection.Normalize();

        float curveLength = HorizontalDistance(entryPoint, destination);

        if (curveLength <= 0.05f)
            return;

        float handleLength = Mathf.Min(
            turnDistance * 0.45f,
            curveLength * 0.45f);

        Vector3 controlPoint1 =
            entryPoint + approachDirection * handleLength;

        Vector3 controlPoint2 =
            destination - parkingDirection * handleLength;

        List<Vector3> originalPath =
            new List<Vector3>(movementPath);

        float distanceAlongPath = 0f;
        movementPath.Clear();
        movementPath.Add(originalPath[0]);

        for (int i = 1; i < originalPath.Count; i++)
        {
            Vector3 previous = originalPath[i - 1];
            Vector3 current = originalPath[i];

            float segmentLength = HorizontalDistance(previous, current);

            if (segmentLength <= 0.0001f)
                continue;

            if (distanceAlongPath + segmentLength < entryDistance)
            {
                movementPath.Add(current);
                distanceAlongPath += segmentLength;
                continue;
            }

            float distanceIntoSegment =
                Mathf.Clamp(
                    entryDistance - distanceAlongPath,
                    0f,
                    segmentLength);

            float t = distanceIntoSegment / segmentLength;

            Vector3 exactEntryPoint = Vector3.Lerp(previous, current, t);
            exactEntryPoint.y = movementHeight;

            if (HorizontalDistance(
                    movementPath[movementPath.Count - 1],
                    exactEntryPoint) > 0.01f)
            {
                movementPath.Add(exactEntryPoint);
            }

            break;
        }

        if (movementPath.Count == 0)
            movementPath.Add(entryPoint);

        if (HorizontalDistance(
                movementPath[movementPath.Count - 1],
                entryPoint) > 0.02f)
        {
            movementPath.Add(entryPoint);
        }

        for (int i = 1; i <= parkingCurveResolution; i++)
        {
            float t = i / (float)parkingCurveResolution;
            float oneMinusT = 1f - t;

            Vector3 curvePoint =
                oneMinusT * oneMinusT * oneMinusT * entryPoint +
                3f * oneMinusT * oneMinusT * t * controlPoint1 +
                3f * oneMinusT * t * t * controlPoint2 +
                t * t * t * destination;

            curvePoint.y = movementHeight;

            if (HorizontalDistance(
                    movementPath[movementPath.Count - 1],
                    curvePoint) > 0.005f)
            {
                movementPath.Add(curvePoint);
            }
        }

        if (movementPath.Count == 0)
            movementPath.Add(destination);
        else
            movementPath[movementPath.Count - 1] = destination;

        Log(
            $"{name}: Built final parking curve. " +
            $"Approach distance: {turnDistance:F2} world units, " +
            $"direction: {parkingDirection}.",
            this);
    }

    private float GetGridCellSize()
    {
        if (gridManager == null)
            return 0f;

        Vector3 cellA =
            gridManager.GridToWorldCenter(Vector2Int.zero);

        Vector3 cellB =
            gridManager.GridToWorldCenter(Vector2Int.right);

        return HorizontalDistance(cellA, cellB);
    }

    private float GetPathLength(List<Vector3> path)
    {
        float length = 0f;

        for (int i = 1; i < path.Count; i++)
            length += HorizontalDistance(path[i - 1], path[i]);

        return length;
    }

    private Vector3 GetPointAlongMovementPath(float targetDistance)
    {
        if (movementPath.Count == 0)
            return destination;

        if (targetDistance <= 0f)
            return movementPath[0];

        float distanceAlongPath = 0f;

        for (int i = 1; i < movementPath.Count; i++)
        {
            Vector3 previous = movementPath[i - 1];
            Vector3 current = movementPath[i];

            float segmentLength = HorizontalDistance(previous, current);

            if (segmentLength <= 0.0001f)
                continue;

            if (distanceAlongPath + segmentLength >= targetDistance)
            {
                float t =
                    (targetDistance - distanceAlongPath) / segmentLength;

                return Vector3.Lerp(previous, current, t);
            }

            distanceAlongPath += segmentLength;
        }

        return movementPath[movementPath.Count - 1];
    }

    private void BuildSmoothedMovementPath(List<Vector3> points)
    {
        if (points.Count == 0)
            return;

        if (points.Count <= 2)
        {
            movementPath.AddRange(points);
            return;
        }

        int count = points.Count;
        float[] cumulativeDistance = new float[count];
        float cellSize = 0f;

        for (int i = 1; i < count; i++)
        {
            float segmentLength =
                HorizontalDistance(points[i - 1], points[i]);

            cumulativeDistance[i] =
                cumulativeDistance[i - 1] + segmentLength;

            if (cellSize <= 0.001f && segmentLength > 0.001f)
                cellSize = segmentLength;
        }

        if (cellSize <= 0.001f)
        {
            movementPath.AddRange(points);
            return;
        }

        List<int> corners = new List<int>();

        for (int i = 1; i < count - 1; i++)
        {
            Vector3 incoming = points[i] - points[i - 1];
            Vector3 outgoing = points[i + 1] - points[i];

            incoming.y = 0f;
            outgoing.y = 0f;

            if (incoming.sqrMagnitude < 0.0001f ||
                outgoing.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            incoming.Normalize();
            outgoing.Normalize();

            // Smooth every meaningful change in travel direction, including
            // diagonal-to-straight and diagonal-to-diagonal transitions.
            // A dot product near 1 indicates an almost-straight segment.
            if (Vector3.Dot(incoming, outgoing) < 0.995f)
                corners.Add(i);
        }

        if (corners.Count == 0)
        {
            movementPath.AddRange(points);
            return;
        }

        float requestedRadius = turningRadiusInCells * cellSize;
        List<float> entryDistances = new List<float>();
        List<float> exitDistances = new List<float>();

        for (int i = 0; i < corners.Count; i++)
        {
            int cornerIndex = corners[i];
            float radius = requestedRadius;

            if (i > 0)
            {
                float previousDistance =
                    cumulativeDistance[cornerIndex] -
                    cumulativeDistance[corners[i - 1]];

                radius = Mathf.Min(radius, previousDistance * 0.45f);
            }

            if (i < corners.Count - 1)
            {
                float nextDistance =
                    cumulativeDistance[corners[i + 1]] -
                    cumulativeDistance[cornerIndex];

                radius = Mathf.Min(radius, nextDistance * 0.45f);
            }

            radius = Mathf.Min(
                radius,
                cumulativeDistance[cornerIndex] * 0.45f);

            radius = Mathf.Min(
                radius,
                (cumulativeDistance[count - 1] -
                 cumulativeDistance[cornerIndex]) * 0.45f);

            radius = Mathf.Max(0.01f, radius);

            entryDistances.Add(cumulativeDistance[cornerIndex] - radius);
            exitDistances.Add(cumulativeDistance[cornerIndex] + radius);
        }

        movementPath.Add(points[0]);
        float lastDistance = 0f;

        for (int i = 0; i < corners.Count; i++)
        {
            float entryDistance = entryDistances[i];
            float exitDistance = exitDistances[i];

            for (int p = 1; p < count - 1; p++)
            {
                float pointDistance = cumulativeDistance[p];

                if (pointDistance > lastDistance + 0.001f &&
                    pointDistance < entryDistance - 0.001f)
                {
                    movementPath.Add(points[p]);
                }
            }

            Vector3 entryPoint = GetPointAlongPath(
                points, cumulativeDistance, entryDistance);

            Vector3 cornerPoint = points[corners[i]];

            Vector3 exitPoint = GetPointAlongPath(
                points, cumulativeDistance, exitDistance);

            AddWaypointIfDifferent(entryPoint);

            for (int segment = 1; segment <= turnCurveResolution; segment++)
            {
                float t = segment / (float)turnCurveResolution;
                float oneMinusT = 1f - t;

                Vector3 curvePoint =
                    oneMinusT * oneMinusT * entryPoint +
                    2f * oneMinusT * t * cornerPoint +
                    t * t * exitPoint;

                curvePoint.y = movementHeight;
                AddWaypointIfDifferent(curvePoint);
            }

            lastDistance = exitDistance;
        }

        for (int i = 1; i < count; i++)
        {
            if (cumulativeDistance[i] > lastDistance + 0.001f)
                AddWaypointIfDifferent(points[i]);
        }
    }

    private Vector3 GetPointAlongPath(
        List<Vector3> points,
        float[] cumulativeDistance,
        float targetDistance)
    {
        if (targetDistance <= 0f)
            return points[0];

        int lastIndex = points.Count - 1;

        if (targetDistance >= cumulativeDistance[lastIndex])
            return points[lastIndex];

        for (int i = 1; i < points.Count; i++)
        {
            if (cumulativeDistance[i] < targetDistance)
                continue;

            float segmentStart = cumulativeDistance[i - 1];
            float segmentEnd = cumulativeDistance[i];
            float segmentLength = segmentEnd - segmentStart;

            if (segmentLength <= 0.0001f)
                return points[i];

            float t = (targetDistance - segmentStart) / segmentLength;
            return Vector3.Lerp(points[i - 1], points[i], t);
        }

        return points[lastIndex];
    }

    private void AddWaypointIfDifferent(Vector3 point)
    {
        point.y = movementHeight;

        if (movementPath.Count > 0 &&
            HorizontalDistance(
                movementPath[movementPath.Count - 1],
                point) < 0.01f)
        {
            return;
        }

        movementPath.Add(point);
    }

    // ---------------------------------------------------------------------
    // Movement and rotation
    // ---------------------------------------------------------------------

    public void StopMovement()
    {
        isMoving = false;
        isCalculatingPath = false;
        pathOpenSet.Clear();
        pathCameFrom.Clear();
        pathGScore.Clear();
        pathClosedSet.Clear();
        movementPath.Clear();
        currentWaypointIndex = 0;
        movementStatus = "Stopped";
    }

    private void UpdateMovement()
    {
        if (currentWaypointIndex >= movementPath.Count)
        {
            FinishMovement();
            return;
        }

        float remainingTime = GameTimeManager.SimulationDeltaTime;
        const int maxWaypointsPerUpdate = 64;

        for (int step = 0;
             step < maxWaypointsPerUpdate &&
             remainingTime > 0.0001f &&
             currentWaypointIndex < movementPath.Count;
             step++)
        {
            Vector3 waypoint = movementPath[currentWaypointIndex];
            Vector3 direction = waypoint - transform.position;
            direction.y = 0f;
            float distance = direction.magnitude;

            if (distance <= waypointArrivalDistance)
            {
                transform.position = new Vector3(
                    waypoint.x,
                    movementHeight,
                    waypoint.z);
                currentWaypointIndex++;
                continue;
            }

            if (movementSpeed <= 0.001f)
                return;

            float timeToWaypoint = distance / movementSpeed;
            float segmentTime = Mathf.Min(remainingTime, timeToWaypoint);
            float travelDistance = movementSpeed * segmentTime;

            Quaternion targetRotation = Quaternion.LookRotation(
                direction.normalized,
                Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Mathf.Clamp01(rotationSpeed * segmentTime));

            if (segmentTime >= timeToWaypoint)
            {
                transform.position = new Vector3(
                    waypoint.x,
                    movementHeight,
                    waypoint.z);
                currentWaypointIndex++;
            }
            else
            {
                Vector3 nextPosition =
                    transform.position + direction.normalized * travelDistance;
                nextPosition.y = movementHeight;
                transform.position = nextPosition;
            }

            remainingTime -= segmentTime;
        }

        if (currentWaypointIndex >= movementPath.Count)
            FinishMovement();
    }

    private void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(
            direction.normalized,
            Vector3.up);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * 100f * GameTimeManager.SimulationDeltaTime);
    }

    private void FinishMovement()
    {
        transform.position = destination;

        Vector3 finalDirection =
            destinationFacingTransform != null
                ? destinationFacingTransform.forward
                : (parkFacingWorldNegativeZ
                    ? Vector3.back
                    : Vector3.forward);

        finalDirection.y = 0f;

        if (finalDirection.sqrMagnitude < 0.0001f)
            finalDirection = Vector3.back;

        transform.rotation = Quaternion.LookRotation(
            finalDirection.normalized,
            Vector3.up);

        isMoving = false;
        currentWaypointIndex = movementPath.Count;
        movementStatus = "Arrived";

        Log(
            $"{name}: Reached destination and completed the parking approach.",
            this);
    }

    public void ReportFailure(string reason)
    {
        Fail(reason);
    }

    private void Fail(string reason)
    {
        isMoving = false;
        isCalculatingPath = false;
        movementStatus = "Failed: " + reason;

        LogWarning(
            $"{nameof(AirportGroundVehicleMovement)} on {name}: " +
            reason,
            this);
    }

    private void OnDrawGizmosSelected()
    {
        if (movementPath == null || movementPath.Count == 0)
            return;

        Gizmos.color = Color.cyan;

        for (int i = 0; i < movementPath.Count; i++)
        {
            Gizmos.DrawSphere(movementPath[i], 0.08f);

            if (i > 0)
                Gizmos.DrawLine(movementPath[i - 1], movementPath[i]);
        }
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}