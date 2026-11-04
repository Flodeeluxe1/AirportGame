using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AirportGroundVehicleMovement : MonoBehaviour
{
    [Header("Grid References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridOccupancy gridOccupancy;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;

    [Header("Movement Costs")]
    [Min(1)]
    [SerializeField] private int tarmacMovementCost = 1;
    [Min(1)]
    [SerializeField] private int taxiwayMovementCost = 5;
    [Min(1)]
    [SerializeField] private int grassMovementCost = 10;

    [Header("Foundation Clearance")]
    [Range(0, 3)]
    [SerializeField] private int foundationClearance = 1;

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
    private float movementHeight;

    public bool IsMoving => isMoving;
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
            gridManager = FindFirstObjectByType<GridManager>();

        if (gridOccupancy == null)
            gridOccupancy = FindFirstObjectByType<GridOccupancy>();

        if (gridSurface == null)
            gridSurface = FindFirstObjectByType<GridSurface>();

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindFirstObjectByType<TaxiwayNetwork>();

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

        tarmacMovementCost = Mathf.Max(1, tarmacMovementCost);
        taxiwayMovementCost = Mathf.Max(1, taxiwayMovementCost);
        grassMovementCost = Mathf.Max(1, grassMovementCost);

        movementHeight = transform.position.y + groundHeightOffset;
    }

    private void Update()
    {
        if (isMoving)
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
    /// Normal movement with an optional final facing direction.
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

        List<Vector2Int> cells =
            FindShortestGridPath(startCell, targetCell);

        if (cells.Count == 0)
        {
            Fail(
                $"No route found from {startCell} to {targetCell}. " +
                "Check foundation clearance, occupied cells and grid bounds.");
            return false;
        }

        routeCellCount = cells.Count;

        List<Vector3> cellPositions = new List<Vector3>();

        foreach (Vector2Int cell in cells)
        {
            Vector3 point = gridManager.GridToWorldCenter(cell);
            point.y = movementHeight;
            cellPositions.Add(point);
        }

        BuildSmoothedMovementPath(cellPositions);

        destination = worldDestination;
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

        Debug.Log(
            $"{name}: Weighted route calculated. " +
            $"Grid cells: {routeCellCount}, " +
            $"waypoints: {movementPath.Count}, " +
            $"tarmac cost: {tarmacMovementCost}, " +
            $"taxiway/road cost: {taxiwayMovementCost}, " +
            $"grass cost: {grassMovementCost}, " +
            $"foundation clearance: {foundationClearance}, " +
            $"parking turn distance: {parkingTurnDistanceInCells} cells.",
            this);

        return true;
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
            Debug.LogWarning(
                $"{name}: There is no previous destination to recalculate.",
                this);
            return false;
        }

        if (isMoving)
        {
            Debug.LogWarning(
                $"{name}: Cannot recalculate while the vehicle is moving. " +
                "Stop the vehicle first.",
                this);
            return false;
        }

        bool routeStarted = MoveToInternal(
            lastRequestedDestination,
            lastRequestedFacingTransform,
            lastRequestedDestinationGate,
            lastRequestedSourceGate);

        Debug.Log(
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

    private List<Vector2Int> FindShortestGridPath(
        Vector2Int start,
        Vector2Int target)
    {
    List<Vector2Int> empty = new List<Vector2Int>();

    if (start == target)
        return new List<Vector2Int> { start };

    bool targetObstacle = gridSurface.IsObstacle(target);
    bool targetHomeCell = IsHomeAreaCell(target);
    bool targetDestinationCell = IsCurrentDestinationGateCell(target);

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

    Debug.Log(
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

    if (targetObstacle &&
        !targetHomeCell &&
        !targetDestinationCell)
    {
        Debug.LogWarning(
            $"{name}: TARGET REJECTED because it is an obstacle " +
            "and is not owned by the permitted destination object.",
            this);

        return empty;
    }

    List<Vector2Int> openSet = new List<Vector2Int> { start };
    HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();

    Dictionary<Vector2Int, Vector2Int> cameFrom =
        new Dictionary<Vector2Int, Vector2Int>();

    Dictionary<Vector2Int, int> gScore =
        new Dictionary<Vector2Int, int>();

    gScore[start] = 0;

    int rejectedOutOfBounds = 0;
    int rejectedFoundation = 0;
    int rejectedClearance = 0;
    int rejectedOccupied = 0;
    int rejectedObstacle = 0;

    while (openSet.Count > 0)
    {
        int bestIndex = 0;

        for (int i = 1; i < openSet.Count; i++)
        {
            int currentScore =
                gScore[openSet[i]] +
                ManhattanDistance(openSet[i], target) *
                tarmacMovementCost;

            int bestScore =
                gScore[openSet[bestIndex]] +
                ManhattanDistance(openSet[bestIndex], target) *
                tarmacMovementCost;

            if (currentScore < bestScore)
                bestIndex = i;
        }

        Vector2Int current = openSet[bestIndex];
        openSet.RemoveAt(bestIndex);

            if (current == target)
            {
                List<Vector2Int> foundPath =
                    ReconstructPath(cameFrom, start, target);

                Debug.Log(
                    $"{name}: PATHFINDING SUCCEEDED. " +
                    $"Path cells: {foundPath.Count}, " +
                    $"Expanded cells: {closedSet.Count}.",
                    this);

                return foundPath;
            }

            if (!closedSet.Add(current))
            continue;

        Vector2Int[] neighbors =
        {
            current + Vector2Int.up,
            current + Vector2Int.right,
            current + Vector2Int.down,
            current + Vector2Int.left
        };

        foreach (Vector2Int neighbor in neighbors)
        {
            if (!gridManager.IsInsideGrid(neighbor))
            {
                rejectedOutOfBounds++;
                continue;
            }

            if (closedSet.Contains(neighbor))
                continue;

            bool isHomeCell = IsHomeAreaCell(neighbor);
            bool isDestinationGateCell =
                IsCurrentDestinationGateCell(neighbor);

            if (neighbor != start &&
                neighbor != target &&
                !isHomeCell &&
                !isDestinationGateCell)
            {
                if (gridSurface.GetSurface(neighbor) ==
                    BuildSurface.Foundation)
                {
                    rejectedFoundation++;
                    continue;
                }

                if (IsWithinFoundationClearance(neighbor))
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
            int tentativeScore = gScore[current] + stepCost;

            if (gScore.TryGetValue(
                    neighbor,
                    out int existingScore) &&
                tentativeScore >= existingScore)
            {
                continue;
            }

            cameFrom[neighbor] = current;
            gScore[neighbor] = tentativeScore;

            if (!openSet.Contains(neighbor))
                openSet.Add(neighbor);
        }
    }

    Debug.LogWarning(
        $"{name}: PATHFINDING FAILED\n" +
        $"Start: {start}, Target: {target}\n" +
        $"Expanded cells: {closedSet.Count}\n" +
        $"Rejected out of bounds: {rejectedOutOfBounds}\n" +
        $"Rejected foundation: {rejectedFoundation}\n" +
        $"Rejected foundation clearance: {rejectedClearance}\n" +
        $"Rejected occupied: {rejectedOccupied}\n" +
        $"Rejected obstacles: {rejectedObstacle}\n" +
        $"Remaining open cells: {openSet.Count}",
        this);

    Vector2Int[] targetNeighbors =
    {
        target + Vector2Int.up,
        target + Vector2Int.right,
        target + Vector2Int.down,
        target + Vector2Int.left
    };

    foreach (Vector2Int neighbor in targetNeighbors)
    {
        if (!gridManager.IsInsideGrid(neighbor))
        {
            Debug.Log(
                $"{name}: Target neighbor {neighbor}: OUTSIDE GRID.",
                this);
            continue;
        }

        Debug.Log(
            $"{name}: Target neighbor {neighbor}: " +
            $"Surface={gridSurface.GetSurface(neighbor)}, " +
            $"Occupied={gridOccupancy.IsOccupied(neighbor)}, " +
            $"FoundationClearance={IsWithinFoundationClearance(neighbor)}, " +
            $"Obstacle={gridSurface.IsObstacle(neighbor)}, " +
            $"Home={IsHomeAreaCell(neighbor)}, " +
            $"Destination={IsCurrentDestinationGateCell(neighbor)}",
            this);
    }

    return empty;
}

    private bool IsWithinFoundationClearance(Vector2Int cell)
    {
        if (foundationClearance <= 0)
            return false;

        for (int x = -foundationClearance;
             x <= foundationClearance;
             x++)
        {
            for (int y = -foundationClearance;
                 y <= foundationClearance;
                 y++)
            {
                if (x == 0 && y == 0)
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

                if (gridSurface.GetSurface(checkedCell) ==
                    BuildSurface.Foundation)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private int GetMovementCost(Vector2Int cell)
    {
        if (gridSurface.GetSurface(cell) == BuildSurface.Tarmac)
            return tarmacMovementCost;

        if (taxiwayNetwork.IsTaxiway(cell) ||
            roadNetwork.IsRoad(cell))
        {
            return taxiwayMovementCost;
        }

        return grassMovementCost;
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

    private int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
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

        Debug.Log(
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

            if (Mathf.Abs(Vector3.Dot(incoming, outgoing)) < 0.1f)
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

        Vector3 waypoint = movementPath[currentWaypointIndex];
        Vector3 direction = waypoint - transform.position;
        direction.y = 0f;

        if (direction.magnitude <= waypointArrivalDistance)
        {
            currentWaypointIndex++;
            return;
        }

        FaceDirection(direction);

        Vector3 nextPosition = Vector3.MoveTowards(
            transform.position,
            waypoint,
            movementSpeed * Time.deltaTime);

        nextPosition.y = movementHeight;
        transform.position = nextPosition;
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
            rotationSpeed * 100f * Time.deltaTime);
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

        Debug.Log(
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
        movementStatus = "Failed: " + reason;

        Debug.LogWarning(
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
}