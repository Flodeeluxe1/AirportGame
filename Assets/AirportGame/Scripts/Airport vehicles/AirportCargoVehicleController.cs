
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AirportCargoVehicleController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridOccupancy gridOccupancy;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;
    [SerializeField] private GuidedCargoTrailerFollower trailerFollower;

    [Header("Home Return")]
    [Tooltip("The live CargoTugLot transform that spawned this vehicle.")]
    [SerializeField] private Transform homeSpawnPoint;
    [Tooltip("The tug's original spawn offset relative to the lot.")]
    [SerializeField] private Vector3 homeSpawnOffset;
    [Tooltip("World-space height used for the tug's home position.")]
    [SerializeField] private float homeSpawnHeight = 0.23f;
    [SerializeField] private bool isReturningHome;

    [Header("Pathfinding Costs")]
    [Min(1)]
    [SerializeField] private int grassMovementCost = 1;
    [Min(1)]
    [SerializeField] private int taxiwayMovementCost = 5;

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

    [Header("Cargo Zone Navigation Test")]
    [SerializeField] private CargoZone cargoZoneTestTarget;
    [SerializeField] private bool testCargoZoneOnPlay = true;

    [Header("Cargo Collection Test")]
    [SerializeField] private bool testCollectionOnArrival = true;
    [SerializeField] private string testFlightOperationId = "0:AB109";

    [Header("Cargo Capacity")]
    [Min(1)]
    [SerializeField] private int maximumCargoCapacity = 10;

    [Header("Runtime Status")]
    [SerializeField] private bool isMoving;
    [SerializeField] private string movementStatus = "Idle";
    [SerializeField] private int routeCellCount;
    [SerializeField] private int currentWaypointIndex;
    [SerializeField] private int carriedPackageCount;

    private readonly List<Vector3> movementPath = new List<Vector3>();
    private readonly List<CargoPackage> carriedPackages =
        new List<CargoPackage>();

    private Vector3 destination;
    private float movementHeight;
    private bool testCollectionAttempted;
    private bool updatingReturnRoute;
    private Vector3 lastHomeTarget;

    public bool IsMoving => isMoving;
    public string MovementStatus => movementStatus;
    public bool IsReturningHome => isReturningHome;
    public int CarriedPackageCount => carriedPackages.Count;
    public int MaximumCargoCapacity => maximumCargoCapacity;
    public int AvailableCargoCapacity =>
        Mathf.Max(0, maximumCargoCapacity - carriedPackages.Count);

    public IReadOnlyList<CargoPackage> CarriedPackages => carriedPackages;

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

        if (trailerFollower == null)
            trailerFollower = GetComponent<GuidedCargoTrailerFollower>();

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
        taxiwayMovementCost = Mathf.Max(1, taxiwayMovementCost);
        foundationClearance = Mathf.Clamp(foundationClearance, 0, 3);

        movementHeight = transform.position.y + groundHeightOffset;
        RefreshCarriedPackageCount();
    }

    private void Start()
    {
        if (!testCargoZoneOnPlay)
            return;

        if (cargoZoneTestTarget == null)
        {
            Debug.LogWarning(
                $"{name}: Assign a Cargo Zone Test Target in the Inspector.",
                this
            );
            return;
        }

        MoveToCargoZone(cargoZoneTestTarget);
    }

    private void Update()
    {
        if (isMoving)
            UpdateMovement();

        UpdateHomeReturnTarget();

        if (!isMoving &&
            testCollectionOnArrival &&
            !testCollectionAttempted &&
            movementStatus == "Arrived")
        {
            testCollectionAttempted = true;

            int collected = CollectOutgoingPackages(
                cargoZoneTestTarget,
                testFlightOperationId
            );

            Debug.Log(
                $"{name}: Collection test finished. " +
                $"Collected: {collected}, " +
                $"carried: {CarriedPackageCount}/{MaximumCargoCapacity}, " +
                $"remaining capacity: {AvailableCargoCapacity}.",
                this
            );
        }
    }

    /// <summary>
    /// Called by CargoTugLotSpawner. Stores the live scene Transform,
    /// not a snapshot of its world position.
    /// </summary>
    public void SetHomeSpawnPoint(
        Transform spawnPoint,
        Vector3 localOffset,
        float worldHeight)
    {
        homeSpawnPoint = spawnPoint;
        homeSpawnOffset = localOffset;
        homeSpawnHeight = worldHeight;

        Debug.Log(
            $"{name}: Home spawn point set to " +
            $"{(spawnPoint != null ? spawnPoint.name : "None")}.",
            this
        );
    }

    /// <summary>
    /// Starts the return journey only if the dispatcher did not assign
    /// another job and neither baggage job is still active.
    /// </summary>
    public bool BeginReturnHomeIfIdle()
    {
        if (homeSpawnPoint == null)
        {
            Debug.LogWarning(
                $"{name}: Cannot return home because no home spawn point " +
                "has been assigned.",
                this
            );
            return false;
        }

        AirportCargoVehicleDispatcherRegistration registration =
            GetComponent<AirportCargoVehicleDispatcherRegistration>();

        if (registration != null && registration.HasActiveAssignment)
            return false;

        AirportCargoIncomingBaggageJob incomingJob =
            GetComponent<AirportCargoIncomingBaggageJob>();

        AirportCargoOutgoingBaggageJob outgoingJob =
            GetComponent<AirportCargoOutgoingBaggageJob>();

        if ((incomingJob != null && incomingJob.IsBusy) ||
            (outgoingJob != null && outgoingJob.IsBusy))
        {
            return false;
        }

        if (isMoving || carriedPackages.Count > 0)
            return false;

        Vector3 target = GetCurrentHomeTarget();
        lastHomeTarget = target;

        isReturningHome = true;
        updatingReturnRoute = true;

        bool routeStarted = MoveTo(target);

        updatingReturnRoute = false;

        if (!routeStarted)
        {
            isReturningHome = false;
            return false;
        }

        Debug.Log(
            $"{name}: Returning to cargo tug lot '{homeSpawnPoint.name}' " +
            $"at {target}.",
            this
        );

        return true;
    }

    /// <summary>
    /// Cancels only the return-home state. The next MoveTo call replaces
    /// the existing route with the new assignment's route.
    /// </summary>
    public void CancelReturnHome()
    {
        if (!isReturningHome)
            return;

        isReturningHome = false;

        Debug.Log(
            $"{name}: Return-home journey cancelled for a new assignment.",
            this
        );
    }

    private Vector3 GetCurrentHomeTarget()
    {
        Vector3 target =
            homeSpawnPoint.position +
            homeSpawnPoint.rotation * homeSpawnOffset;

        target.y = homeSpawnHeight;
        return target;
    }

    private void UpdateHomeReturnTarget()
    {
        if (!isReturningHome)
            return;

        if (homeSpawnPoint == null)
        {
            isReturningHome = false;
            return;
        }

        AirportCargoVehicleDispatcherRegistration registration =
            GetComponent<AirportCargoVehicleDispatcherRegistration>();

        if (registration != null && registration.HasActiveAssignment)
        {
            CancelReturnHome();
            return;
        }

        if (!isMoving)
        {
            if (movementStatus == "Arrived")
            {
                isReturningHome = false;

                Debug.Log(
                    $"{name}: Returned to the cargo tug lot.",
                    this
                );
            }

            return;
        }

        Vector3 currentTarget = GetCurrentHomeTarget();

        // Recalculate the route if the lot moved while the tug was away.
        if (Vector3.Distance(currentTarget, lastHomeTarget) > 0.1f)
        {
            lastHomeTarget = currentTarget;
            updatingReturnRoute = true;

            bool routeStarted = MoveTo(currentTarget);

            updatingReturnRoute = false;

            if (!routeStarted)
            {
                isReturningHome = false;

                Debug.LogWarning(
                    $"{name}: Could not recalculate the return route " +
                    "after the cargo tug lot moved.",
                    this
                );
            }
        }
    }

    public bool MoveToCargoZone(CargoZone zone)
    {
        if (zone == null)
        {
            Fail("Cannot move to cargo zone: the zone reference is missing.");
            return false;
        }

        if (zone.AirportVehicleStopPoint == null)
        {
            Fail(
                $"Cargo zone '{zone.name}' has no AirportVehicleStopPoint."
            );
            return false;
        }

        Debug.Log(
            $"{name}: Navigating to cargo zone '{zone.name}'.",
            this
        );

        return MoveTo(zone.AirportVehicleStopPoint.position);
    }

    public int CollectOutgoingPackages(
        CargoZone zone,
        string flightOperationId)
    {
        if (zone == null)
        {
            Debug.LogWarning(
                $"{name}: Cannot collect cargo because the zone is missing.",
                this
            );
            return 0;
        }

        if (string.IsNullOrWhiteSpace(flightOperationId))
        {
            Debug.LogWarning(
                $"{name}: Cannot collect cargo without a flight operation ID.",
                this
            );
            return 0;
        }

        int freeCapacity = AvailableCargoCapacity;

        if (freeCapacity <= 0)
        {
            Debug.Log(
                $"{name}: Cargo capacity is full " +
                $"({CarriedPackageCount}/{MaximumCargoCapacity}).",
                this
            );
            return 0;
        }

        List<CargoPackage> availablePackages =
            zone.GetAvailablePackages(
                CargoDirection.Outgoing,
                flightOperationId
            );

        int collected = 0;

        foreach (CargoPackage package in availablePackages)
        {
            if (collected >= freeCapacity)
                break;

            if (package == null || !package.TryReserve())
                continue;

            zone.UnregisterPackage(package);
            package.SetInTransit();
            HidePackageVisual(package);
            carriedPackages.Add(package);
            collected++;
        }

        RefreshCarriedPackageCount();

        Debug.Log(
            $"{name}: Collected {collected} package(s) for flight " +
            $"{flightOperationId} from zone '{zone.name}'. " +
            $"Vehicle load: {CarriedPackageCount}/{MaximumCargoCapacity}.",
            this
        );

        return collected;
    }

    private void HidePackageVisual(CargoPackage package)
    {
        Renderer[] renderers =
            package.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer packageRenderer in renderers)
            packageRenderer.enabled = false;

        Collider[] colliders =
            package.GetComponentsInChildren<Collider>(true);

        foreach (Collider packageCollider in colliders)
            packageCollider.enabled = false;
    }

    private void RefreshCarriedPackageCount()
    {
        carriedPackages.RemoveAll(package => package == null);
        carriedPackageCount = carriedPackages.Count;
    }

    public bool MoveTo(Vector3 worldDestination)
    {
        // Any normal movement command replaces the return route.
        // Internal target updates preserve the return-home state.
        if (!updatingReturnRoute)
            isReturningHome = false;

        isMoving = false;
        movementPath.Clear();
        currentWaypointIndex = 0;
        routeCellCount = 0;
        movementStatus = "Calculating route";

        if (gridManager == null ||
            gridOccupancy == null ||
            gridSurface == null ||
            roadNetwork == null ||
            taxiwayNetwork == null)
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
                $"Start: {startCell}, Target: {targetCell}"
            );
            return false;
        }

        List<Vector2Int> cells =
            FindShortestGridPath(startCell, targetCell);

        if (cells.Count == 0)
        {
            Fail(
                $"No route found from {startCell} to {targetCell}. " +
                "Check foundation clearance, occupied cells and grid bounds."
            );
            return false;
        }

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
                   movementPath[currentWaypointIndex]
               ) <= waypointArrivalDistance)
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
            $"grass cost: {grassMovementCost}, " +
            $"taxiway/road cost: {taxiwayMovementCost}, " +
            $"foundation clearance: {foundationClearance}, " +
            $"parking turn distance: {parkingTurnDistanceInCells} cells.",
            this
        );

        return true;
    }

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
            Mathf.Min(totalDistance, entryDistance + 0.1f)
        );

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

        Vector3 parkingDirection = parkFacingWorldNegativeZ
            ? Vector3.back
            : Vector3.forward;

        float curveLength = HorizontalDistance(entryPoint, destination);

        if (curveLength <= 0.05f)
            return;

        float handleLength = Mathf.Min(
            turnDistance * 0.45f,
            curveLength * 0.45f
        );

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
                Mathf.Clamp(entryDistance - distanceAlongPath, 0f, segmentLength);

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
            this
        );
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
                continue;

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
                cumulativeDistance[cornerIndex] * 0.45f
            );

            radius = Mathf.Min(
                radius,
                (cumulativeDistance[count - 1] -
                 cumulativeDistance[cornerIndex]) * 0.45f
            );

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

    public void StopMovement()
    {
        isMoving = false;
        movementPath.Clear();
        currentWaypointIndex = 0;
        movementStatus = "Stopped";
        isReturningHome = false;
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
            movementSpeed * Time.deltaTime
        );

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
            Vector3.up
        );

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * 100f * Time.deltaTime
        );
    }

    private void FinishMovement()
    {
        transform.position = destination;

        Vector3 finalDirection = parkFacingWorldNegativeZ
            ? Vector3.back
            : Vector3.forward;

        transform.rotation = Quaternion.LookRotation(
            finalDirection,
            Vector3.up
        );

        isMoving = false;
        currentWaypointIndex = movementPath.Count;
        movementStatus = "Arrived";

        Debug.Log(
            $"{name}: Reached destination and completed the parking approach.",
            this
        );
    }

    
    private List<Vector2Int> FindShortestGridPath(
        Vector2Int start,
        Vector2Int target)
        {
            List<Vector2Int> empty = new List<Vector2Int>();

            if (start == target)
            {
                if (start != target && gridSurface.IsObstacle(target))
                    return empty;

                return new List<Vector2Int> { start };
            }

            // Do not allow a destination on a runway or another blocked cell.
            if (gridSurface.IsObstacle(target))
                return empty;

            List<Vector2Int> openSet = new List<Vector2Int> { start };
            HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();
            Dictionary<Vector2Int, Vector2Int> cameFrom =
                new Dictionary<Vector2Int, Vector2Int>();
            Dictionary<Vector2Int, int> gScore =
                new Dictionary<Vector2Int, int>();

            gScore[start] = 0;

            while (openSet.Count > 0)
            {
                int bestIndex = 0;

                for (int i = 1; i < openSet.Count; i++)
                {
                    int currentScore =
                        gScore[openSet[i]] +
                        ManhattanDistance(openSet[i], target) *
                        grassMovementCost;

                    int bestScore =
                        gScore[openSet[bestIndex]] +
                        ManhattanDistance(openSet[bestIndex], target) *
                        grassMovementCost;

                    if (currentScore < bestScore)
                        bestIndex = i;
                }

                Vector2Int current = openSet[bestIndex];
                openSet.RemoveAt(bestIndex);

                if (current == target)
                    return ReconstructPath(cameFrom, start, target);

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
                    if (!gridManager.IsInsideGrid(neighbor) ||
                        closedSet.Contains(neighbor))
                    {
                        continue;
                    }

                // Runway and other pathfinding-obstacle cells are forbidden.
                // Allow the tug's current cell so it can escape if necessary.
                if (neighbor != start &&
                    neighbor != target &&
                    gridSurface.GetSurface(neighbor) == BuildSurface.Foundation)
                {
                    Debug.Log(
                        $"[CargoPathfinding] BLOCKED {neighbor}: Foundation surface");

                    continue;
                }

                if (neighbor != start &&
                    neighbor != target &&
                    IsWithinFoundationClearance(neighbor))
                {
                    Debug.Log(
                        $"[CargoPathfinding] BLOCKED {neighbor}: Within foundation clearance " +
                        $"(clearance = {foundationClearance})");

                    continue;
                }

                if (neighbor != target &&
                    neighbor != start &&
                    gridOccupancy.IsOccupied(neighbor))
                {
                    Debug.Log(
                        $"[CargoPathfinding] BLOCKED {neighbor}: Occupied cell");

                    continue;
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
                    cell.y + y
                );

                if (!gridManager.IsInsideGrid(checkedCell))
                    continue;

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

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;

        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private void Fail(string reason)
    {
        isMoving = false;
        movementStatus = "Failed: " + reason;

        Debug.LogWarning(
            $"{nameof(AirportCargoVehicleController)} on {name}: " +
            reason,
            this
        );
    }

    public bool ResolveCarriedPackage(
        CargoPackage package,
        bool delivered,
        CargoZone returnZone = null)
    {
        if (package == null || !carriedPackages.Contains(package))
            return false;

        if (delivered)
        {
            package.SetDelivered();
            carriedPackages.Remove(package);
            Destroy(package.gameObject);
        }
        else
        {
            if (returnZone == null)
                return false;

            package.ReturnToZone(returnZone);

            if (!returnZone.RegisterPackage(package))
                return false;

            Renderer[] renderers =
                package.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer packageRenderer in renderers)
                packageRenderer.enabled = true;

            Collider[] colliders =
                package.GetComponentsInChildren<Collider>(true);

            foreach (Collider packageCollider in colliders)
                packageCollider.enabled = true;

            carriedPackages.Remove(package);
        }

        RefreshCarriedPackageCount();
        return true;
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
