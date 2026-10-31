using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sightseeing aircraft departure taxi controller.
///
/// Waits for boarding to complete, selects a suitable runway,
/// taxis to a safe holding point, waits for runway clearance,
/// taxis onto the runway, and reports when lined up for takeoff.
///
/// Takeoff itself is handled by a separate sightseeing controller.
/// </summary>
[DisallowMultipleComponent]
public class AircraftSSDepartureTaxiController : MonoBehaviour
{
    public enum DepartureState
    {
        WaitingForBoarding,
        WaitingForRunway,
        TaxiingToHoldingPoint,
        AtHoldingPoint,
        TaxiingToRunwayEntry,
        MovingOntoRunway,
        AligningOnRunway,
        LinedUpReadyForTakeoff
    }

    [Header("References")]
    [SerializeField] private AircraftSSTurnaroundController turnaroundController;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private Weather weather;
    [SerializeField] private AircraftRunwayTrafficManager trafficManager;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;
    [SerializeField] private TaxiwayPathfinder taxiwayPathfinder;
    [SerializeField] private BoxCollider aircraftCollider;

    [Header("Runway Selection")]
    [Min(0f)]
    [SerializeField] private float maximumSidewindComponentKnots = 15f;

    [Min(0.5f)]
    [SerializeField] private float runwaySelectionRetryInterval = 3f;

    [Min(1)]
    [SerializeField] private int startSearchRadiusCells = 12;

    [Range(1, 20)]
    [SerializeField] private int maximumStartCandidates = 10;

    [Header("Holding Position")]
    [Min(0f)]
    [SerializeField] private float runwaySafetyMarginInCells = 1f;

    [Header("Taxi Movement")]
    [Min(1f)]
    [SerializeField] private float taxiSpeedKnots = 15f;

    [Min(1f)]
    [SerializeField] private float rotationSpeedDegreesPerSecond = 45f;

    [Min(0.01f)]
    [SerializeField] private float waypointArrivalDistance = 0.15f;

    [Tooltip("Adjust if the aircraft model's nose is not local +Z.")]
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Smooth Taxi Turns")]
    [Min(0.1f)]
    [SerializeField] private float turningRadiusInCells = 2f;

    [Range(4, 16)]
    [SerializeField] private int turnCurveResolution = 16;

    [Header("Runway Line-Up")]
    [Min(0f)]
    [SerializeField] private float lineUpSafetyMarginInCells = 0.25f;

    [Header("Debug")]
    [SerializeField]
    private DepartureState currentState =
        DepartureState.WaitingForBoarding;

    [SerializeField] private AircraftRunway selectedRunway;
    [SerializeField] private Vector3 takeoffDirection;
    [SerializeField] private Vector3 departureThreshold;
    [SerializeField] private Vector2Int holdingCell;
    [SerializeField] private bool hasHoldingCell;
    [SerializeField] private string debugSelectedRunway = "None";
    [SerializeField] private string debugTakeoffHeading = "None";
    [SerializeField] private string debugStatus = "Waiting for boarding";
    [SerializeField] private float debugTaxiRouteLength;
    [SerializeField] private int debugRunwayTrafficCount;
    [SerializeField] private float debugHeadwindKnots;
    [SerializeField] private float debugSidewindKnots;

    private readonly List<Vector2Int> taxiRoute = new();
    private readonly List<Vector2Int> runwayJoinRoute = new();
    private readonly List<Vector3> movementWaypoints = new();

    private int currentWaypointIndex;
    private int holdingRouteIndex;
    private float retryTimer;
    private bool hasLoggedWaitingMessage;
    private string lastRunwayWaitReason = string.Empty;

    private Vector2Int runwayJoinCell;
    private Vector3 runwayCenter;
    private float runwayLength;
    private float runwaySurfaceY;
    private Vector3 runwayLineUpTarget;
    private Quaternion runwayLineUpRotation;

    public DepartureState CurrentState => currentState;
    public AircraftRunway SelectedRunway => selectedRunway;
    public Vector3 TakeoffDirection => takeoffDirection;
    public Vector3 DepartureThreshold => departureThreshold;
    public Vector2Int HoldingCell => holdingCell;
    public bool HasHoldingCell => hasHoldingCell;

    public bool AtHoldingPoint =>
        currentState == DepartureState.AtHoldingPoint;

    public bool IsLinedUp =>
        currentState == DepartureState.LinedUpReadyForTakeoff;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        AircraftStatus status = GetComponent<AircraftStatus>();

        if (status != null)
        {
            bool isTaxiing =
                currentState == DepartureState.TaxiingToHoldingPoint ||
                currentState == DepartureState.TaxiingToRunwayEntry ||
                currentState == DepartureState.MovingOntoRunway;

            status.SetTaxiing(isTaxiing);

            if (!isTaxiing)
                status.SetCurrentSpeedKnots(0f);
        }

        switch (currentState)
        {
            case DepartureState.AtHoldingPoint:
                UpdateHoldingPoint();
                return;

            case DepartureState.TaxiingToHoldingPoint:
            case DepartureState.TaxiingToRunwayEntry:
            case DepartureState.MovingOntoRunway:
                UpdateTaxiMovement();
                return;

            case DepartureState.AligningOnRunway:
                UpdateRunwayAlignment();
                return;

            case DepartureState.LinedUpReadyForTakeoff:
                return;
        }

        ResolveReferences();

        if (turnaroundController == null ||
            !turnaroundController.IsBoardingComplete)
        {
            currentState = DepartureState.WaitingForBoarding;
            debugStatus = "Parked; waiting for boarding to complete.";
            return;
        }

        currentState = DepartureState.WaitingForRunway;

        retryTimer -= Time.deltaTime;

        if (retryTimer > 0f)
            return;

        retryTimer = runwaySelectionRetryInterval;
        TrySelectDepartureRunway();
    }

    private void ResolveReferences()
    {
        if (turnaroundController == null)
            turnaroundController =
                GetComponent<AircraftSSTurnaroundController>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (weather == null)
            weather = FindFirstObjectByType<Weather>();

        if (trafficManager == null)
            trafficManager =
                FindFirstObjectByType<AircraftRunwayTrafficManager>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindFirstObjectByType<TaxiwayNetwork>();

        if (taxiwayPathfinder == null)
            taxiwayPathfinder = FindFirstObjectByType<TaxiwayPathfinder>();

        if (aircraftCollider == null)
            aircraftCollider = GetComponentInChildren<BoxCollider>(true);
    }

    public bool TrySelectDepartureRunway()
    {
        ResolveReferences();

        if (selectedRunway != null)
            return true;

        if (turnaroundController == null ||
            !turnaroundController.IsBoardingComplete)
        {
            debugStatus = "Waiting for sightseeing boarding to finish.";
            return false;
        }

        if (gridManager == null ||
            weather == null ||
            trafficManager == null ||
            taxiwayNetwork == null ||
            taxiwayPathfinder == null)
        {
            LogWaitingMessage(
                "Missing GridManager, Weather, runway traffic manager, " +
                "TaxiwayNetwork or TaxiwayPathfinder.");
            return false;
        }

        if (aircraftCollider == null)
        {
            LogWaitingMessage("Aircraft BoxCollider is missing.");
            return false;
        }

        if (gridManager.CellSize <= 0f)
        {
            LogWaitingMessage("GridManager CellSize is invalid.");
            return false;
        }

        List<Vector2Int> startCandidates = GetStartCandidates();

        if (startCandidates.Count == 0)
        {
            LogWaitingMessage(
                "No taxiway cells found near the sightseeing aircraft.");
            return false;
        }

        AircraftRunway[] runways =
            FindObjectsByType<AircraftRunway>(
                FindObjectsSortMode.None);

        if (runways.Length == 0)
        {
            LogWaitingMessage("No placed runways were found.");
            return false;
        }

        Vector3 windFrom = weather.WindFromDirection;
        windFrom.y = 0f;

        if (windFrom.sqrMagnitude < 0.0001f)
            windFrom = Vector3.forward;

        windFrom.Normalize();

        AircraftRunway bestRunway = null;
        Vector3 bestDirection = Vector3.zero;
        Vector3 bestThreshold = Vector3.zero;
        Vector3 bestCenter = Vector3.zero;
        float bestLength = 0f;
        float bestSurfaceY = 0f;

        List<Vector2Int> bestSafeRoute = null;
        List<Vector2Int> bestJoinRoute = null;
        Vector2Int bestHoldingCell = default;

        bool bestCrosswindWithinLimit = false;
        int bestTrafficCount = int.MaxValue;
        float bestHeadwind = -1f;
        float bestRouteLength = float.MaxValue;

        foreach (AircraftRunway candidate in runways)
        {
            if (candidate == null)
                continue;

            if (!candidate.TryGetGeometry(
                    gridManager,
                    out Vector3 center,
                    out Vector3 axis,
                    out float length,
                    out float surfaceY))
            {
                continue;
            }

            axis.y = 0f;

            if (axis.sqrMagnitude < 0.0001f)
                continue;

            axis.Normalize();

            Vector3 direction =
                Vector3.Dot(axis, windFrom) >= 0f ? axis : -axis;

            float headwind =
                weather.windSpeedKnots *
                Mathf.Max(0f, Vector3.Dot(direction, windFrom));

            float sidewind = CalculateSidewindComponentKnots(direction);

            bool crosswindWithinLimit =
                sidewind <= maximumSidewindComponentKnots + 0.001f;

            int trafficCount = trafficManager.GetRunwayTrafficCount(
                candidate,
                null,
                this);

            if (!TryFindRouteToRunwayThreshold(
                    candidate,
                    direction,
                    startCandidates,
                    out List<Vector2Int> approachRoute))
            {
                continue;
            }

            if (!TryCreateSafeHoldingRoute(
                    approachRoute,
                    out List<Vector2Int> safeRoute,
                    out Vector2Int candidateHoldingCell))
            {
                continue;
            }

            float routeLength = CalculateRouteLength(safeRoute);

            if (safeRoute.Count > 0)
            {
                Vector3 firstPoint =
                    gridManager.GridToWorldCenter(safeRoute[0]);

                firstPoint.y = transform.position.y;

                routeLength += Vector3.Distance(
                    transform.position,
                    firstPoint);
            }

            bool choose = bestRunway == null;

            if (!choose)
            {
                if (crosswindWithinLimit != bestCrosswindWithinLimit)
                    choose = crosswindWithinLimit;
                else if (trafficCount != bestTrafficCount)
                    choose = trafficCount < bestTrafficCount;
                else if (headwind > bestHeadwind + 0.01f)
                    choose = true;
                else if (
                    Mathf.Abs(headwind - bestHeadwind) <= 0.01f &&
                    routeLength < bestRouteLength - 0.01f)
                    choose = true;
            }

            if (!choose)
                continue;

            bestRunway = candidate;
            bestDirection = direction;
            bestCenter = center;
            bestLength = length;
            bestSurfaceY = surfaceY;
            bestThreshold = center - direction * (length * 0.5f);
            bestThreshold.y = surfaceY;

            bestSafeRoute = safeRoute;
            bestJoinRoute = approachRoute;
            bestHoldingCell = candidateHoldingCell;

            bestCrosswindWithinLimit = crosswindWithinLimit;
            bestTrafficCount = trafficCount;
            bestHeadwind = headwind;
            bestRouteLength = routeLength;
        }

        if (bestRunway == null ||
            bestSafeRoute == null ||
            bestJoinRoute == null)
        {
            LogWaitingMessage(
                "No reachable runway with a suitable safe holding point was found.");
            return false;
        }

        if (!trafficManager.RegisterDepartureReservation(bestRunway, this))
        {
            LogWaitingMessage("Could not reserve the selected runway.");
            return false;
        }

        selectedRunway = bestRunway;
        takeoffDirection = bestDirection.normalized;
        departureThreshold = bestThreshold;
        runwayCenter = bestCenter;
        runwayLength = bestLength;
        runwaySurfaceY = bestSurfaceY;

        debugSelectedRunway = bestRunway.gameObject.name;
        debugTakeoffHeading = HeadingName(takeoffDirection);
        debugRunwayTrafficCount = bestTrafficCount;
        debugHeadwindKnots = bestHeadwind;
        debugSidewindKnots =
            CalculateSidewindComponentKnots(takeoffDirection);
        debugTaxiRouteLength = bestRouteLength;

        taxiRoute.Clear();
        taxiRoute.AddRange(bestSafeRoute);

        runwayJoinRoute.Clear();
        runwayJoinRoute.AddRange(bestJoinRoute);

        holdingCell = bestHoldingCell;
        runwayJoinCell = bestJoinRoute[bestJoinRoute.Count - 1];
        hasHoldingCell = true;
        holdingRouteIndex = bestSafeRoute.Count - 1;

        BuildSmoothedMovementWaypoints(taxiRoute);
        currentWaypointIndex = 0;
        currentState = DepartureState.TaxiingToHoldingPoint;

        debugStatus = $"Taxiing to the holding point for {debugSelectedRunway}.";
        retryTimer = runwaySelectionRetryInterval;
        hasLoggedWaitingMessage = false;
        lastRunwayWaitReason = string.Empty;

        Debug.Log(
            $"SIGHTSEEING DEPARTURE ROUTE SELECTED | Aircraft: {name} | " +
            $"Runway: {debugSelectedRunway} | Heading: {debugTakeoffHeading} | " +
            $"Holding cell: {holdingCell} | Route cells: {taxiRoute.Count}",
            this);

        return true;
    }

    private List<Vector2Int> GetStartCandidates()
    {
        List<Vector2Int> candidates = new();

        Vector2Int aircraftCell =
            gridManager.WorldToGrid(transform.position);

        for (int x = -startSearchRadiusCells; x <= startSearchRadiusCells; x++)
        {
            for (int z = -startSearchRadiusCells; z <= startSearchRadiusCells; z++)
            {
                Vector2Int cell = aircraftCell + new Vector2Int(x, z);

                if (!gridManager.IsInsideGrid(cell) ||
                    !taxiwayNetwork.IsTaxiway(cell))
                    continue;

                if (!candidates.Contains(cell))
                    candidates.Add(cell);
            }
        }

        candidates.Sort((a, b) =>
        {
            bool aCenter = taxiwayNetwork.IsCenterlineCell(a);
            bool bCenter = taxiwayNetwork.IsCenterlineCell(b);

            if (aCenter != bCenter)
                return aCenter ? -1 : 1;

            return Vector2Int.Distance(a, aircraftCell).CompareTo(
                Vector2Int.Distance(b, aircraftCell));
        });

        if (candidates.Count > maximumStartCandidates)
        {
            candidates.RemoveRange(
                maximumStartCandidates,
                candidates.Count - maximumStartCandidates);
        }

        return candidates;
    }

    private bool TryFindRouteToRunwayThreshold(
        AircraftRunway runway,
        Vector3 direction,
        List<Vector2Int> startCandidates,
        out List<Vector2Int> bestRoute)
    {
        bestRoute = null;

        PlacedBuilding placed = runway.GetComponent<PlacedBuilding>();

        if (placed == null)
            placed = runway.GetComponentInParent<PlacedBuilding>();

        if (placed == null)
            placed = runway.GetComponentInChildren<PlacedBuilding>(true);

        if (placed == null)
            return false;

        bool axisIsX = Mathf.Abs(direction.x) >= Mathf.Abs(direction.z);
        int lengthInCells = axisIsX ? placed.Width : placed.Depth;

        int bestOffset = int.MaxValue;
        float bestDistance = float.MaxValue;

        for (int offset = 0; offset < lengthInCells; offset++)
        {
            Vector2Int sideA;
            Vector2Int sideB;

            if (axisIsX)
            {
                int x = direction.x > 0f
                    ? placed.Origin.x + offset
                    : placed.Origin.x + placed.Width - 1 - offset;

                sideA = new Vector2Int(x, placed.Origin.y - 1);
                sideB = new Vector2Int(x, placed.Origin.y + placed.Depth);
            }
            else
            {
                int z = direction.z > 0f
                    ? placed.Origin.y + offset
                    : placed.Origin.y + placed.Depth - 1 - offset;

                sideA = new Vector2Int(placed.Origin.x - 1, z);
                sideB = new Vector2Int(placed.Origin.x + placed.Width, z);
            }

            foreach (Vector2Int side in new[] { sideA, sideB })
            {
                if (!gridManager.IsInsideGrid(side) ||
                    !taxiwayNetwork.IsTaxiway(side))
                    continue;

                List<Vector2Int> centerlines = FindNearbyCenterlines(side);

                foreach (Vector2Int target in centerlines)
                {
                    int offsetFromThreshold = axisIsX
                        ? (direction.x > 0f
                            ? target.x - placed.Origin.x
                            : placed.Origin.x + placed.Width - 1 - target.x)
                        : (direction.z > 0f
                            ? target.y - placed.Origin.y
                            : placed.Origin.y + placed.Depth - 1 - target.y);

                    if (offsetFromThreshold < 0)
                        continue;

                    foreach (Vector2Int start in startCandidates)
                    {
                        List<Vector2Int> path = taxiwayPathfinder.FindPath(start, target);

                        if (path == null || path.Count == 0)
                            continue;

                        float distance = CalculateRouteLength(path);

                        Vector3 first = gridManager.GridToWorldCenter(path[0]);
                        first.y = transform.position.y;
                        distance += Vector3.Distance(transform.position, first);

                        if (offsetFromThreshold > bestOffset ||
                            (offsetFromThreshold == bestOffset &&
                             distance >= bestDistance))
                            continue;

                        bestOffset = offsetFromThreshold;
                        bestDistance = distance;
                        bestRoute = path;
                    }
                }
            }
        }

        return bestRoute != null;
    }

    private List<Vector2Int> FindNearbyCenterlines(Vector2Int start)
    {
        List<Vector2Int> found = new();
        Queue<Vector2Int> queue = new();
        HashSet<Vector2Int> visited = new();

        queue.Enqueue(start);
        visited.Add(start);

        int searchRadius = Mathf.Max(1, taxiwayNetwork.GetTaxiwayWidth(start));

        for (int distance = 0; queue.Count > 0 && distance <= searchRadius; distance++)
        {
            int layerCount = queue.Count;

            for (int i = 0; i < layerCount; i++)
            {
                Vector2Int current = queue.Dequeue();

                if (taxiwayNetwork.IsCenterlineCell(current))
                {
                    found.Add(current);
                    continue;
                }

                foreach (Vector2Int neighbour in
                         taxiwayNetwork.GetConnectedTaxiwayCells(current))
                {
                    if (visited.Add(neighbour))
                        queue.Enqueue(neighbour);
                }
            }

            if (found.Count > 0)
                break;
        }

        return found;
    }

    private bool TryCreateSafeHoldingRoute(
        List<Vector2Int> approachRoute,
        out List<Vector2Int> safeRoute,
        out Vector2Int safeCell)
    {
        safeRoute = null;
        safeCell = default;

        if (approachRoute == null || approachRoute.Count < 2)
            return false;

        float accumulatedDistance = 0f;

        for (int i = approachRoute.Count - 2; i >= 0; i--)
        {
            Vector2Int current = approachRoute[i];
            Vector2Int next = approachRoute[i + 1];

            Vector3 currentWorld = gridManager.GridToWorldCenter(current);
            Vector3 nextWorld = gridManager.GridToWorldCenter(next);

            currentWorld.y = 0f;
            nextWorld.y = 0f;

            accumulatedDistance += Vector3.Distance(currentWorld, nextWorld);

            if (!taxiwayNetwork.IsCenterlineCell(current))
                continue;

            Vector3 direction = nextWorld - currentWorld;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
                continue;

            float extent = GetAircraftForwardExtent(direction.normalized);

            if (extent < 0f)
                return false;

            float requiredDistance =
                extent + runwaySafetyMarginInCells * gridManager.CellSize;

            if (accumulatedDistance + 0.001f < requiredDistance)
                continue;

            safeRoute = approachRoute.GetRange(0, i + 1);
            safeCell = current;
            return true;
        }

        return false;
    }

    private float GetAircraftForwardExtent(Vector3 direction)
    {
        if (aircraftCollider == null)
            return -1f;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return -1f;

        direction.Normalize();

        Quaternion intendedRotation =
            Quaternion.LookRotation(direction, Vector3.up) *
            Quaternion.Euler(modelRotationOffset);

        Transform colliderTransform = aircraftCollider.transform;

        Vector3 localCenter = transform.InverseTransformPoint(
            colliderTransform.TransformPoint(aircraftCollider.center));

        Vector3 localHalfX = transform.InverseTransformVector(
            colliderTransform.TransformVector(
                Vector3.right * (aircraftCollider.size.x * 0.5f)));

        Vector3 localHalfY = transform.InverseTransformVector(
            colliderTransform.TransformVector(
                Vector3.up * (aircraftCollider.size.y * 0.5f)));

        Vector3 localHalfZ = transform.InverseTransformVector(
            colliderTransform.TransformVector(
                Vector3.forward * (aircraftCollider.size.z * 0.5f)));

        Vector3 scale = transform.lossyScale;

        Vector3 centerOffset = intendedRotation * Vector3.Scale(localCenter, scale);
        Vector3 halfX = intendedRotation * Vector3.Scale(localHalfX, scale);
        Vector3 halfY = intendedRotation * Vector3.Scale(localHalfY, scale);
        Vector3 halfZ = intendedRotation * Vector3.Scale(localHalfZ, scale);

        return Mathf.Max(
            0f,
            Vector3.Dot(centerOffset, direction) +
            Mathf.Abs(Vector3.Dot(halfX, direction)) +
            Mathf.Abs(Vector3.Dot(halfY, direction)) +
            Mathf.Abs(Vector3.Dot(halfZ, direction)));
    }

    private void UpdateHoldingPoint()
    {
        if (selectedRunway == null || trafficManager == null)
            return;

        retryTimer -= Time.deltaTime;

        if (retryTimer > 0f)
            return;

        retryTimer = runwaySelectionRetryInterval;

        if (!trafficManager.TryReserveRunwayForLineUp(
                selectedRunway,
                this,
                out string reason))
        {
            debugStatus = $"Holding for runway clearance: {reason}";

            if (reason != lastRunwayWaitReason)
            {
                Debug.Log($"{name}: {debugStatus}", this);
                lastRunwayWaitReason = reason;
            }

            return;
        }

        lastRunwayWaitReason = string.Empty;

        Debug.Log(
            $"{name}: Runway clearance acquired for {selectedRunway.name}.",
            this);

        StartTaxiingToRunwayEntry();
    }

    private void StartTaxiingToRunwayEntry()
    {
        if (runwayJoinRoute.Count == 0 ||
            holdingRouteIndex < 0 ||
            holdingRouteIndex >= runwayJoinRoute.Count)
        {
            trafficManager.ReleaseRunwayLineUpReservation(this);
            currentState = DepartureState.AtHoldingPoint;
            debugStatus = "Cannot continue: runway join route is invalid.";
            Debug.LogWarning($"{name}: {debugStatus}", this);
            return;
        }

        List<Vector2Int> remainingRoute = new();

        for (int i = holdingRouteIndex; i < runwayJoinRoute.Count; i++)
            remainingRoute.Add(runwayJoinRoute[i]);

        BuildSmoothedMovementWaypoints(remainingRoute);
        currentWaypointIndex = movementWaypoints.Count > 1 ? 1 : 0;
        currentState = DepartureState.TaxiingToRunwayEntry;
        debugStatus = $"Taxiing to runway entry for {debugSelectedRunway}.";
    }

    private void StartMovementOntoRunway()
    {
        if (selectedRunway == null || runwayJoinRoute.Count == 0)
        {
            AbortLineUpAndReturnToHold("Runway join data is missing.");
            return;
        }

        Vector3 joinWorld = gridManager.GridToWorldCenter(runwayJoinCell);
        joinWorld.y = transform.position.y;

        float extent = GetAircraftForwardExtent(takeoffDirection);

        if (extent < 0f)
        {
            AbortLineUpAndReturnToHold("Could not calculate aircraft collider dimensions.");
            return;
        }

        float halfLength = runwayLength * 0.5f;
        float margin = lineUpSafetyMarginInCells * gridManager.CellSize;
        float minimum = -halfLength + extent + margin;
        float maximum = halfLength - extent - margin;

        if (minimum > maximum)
        {
            AbortLineUpAndReturnToHold("Runway is too short for this aircraft.");
            return;
        }

        float joinPosition = Vector3.Dot(joinWorld - runwayCenter, takeoffDirection);
        float lineUpPosition = Mathf.Clamp(
            Mathf.Max(joinPosition, minimum),
            minimum,
            maximum);

        runwayLineUpTarget = runwayCenter + takeoffDirection * lineUpPosition;
        runwayLineUpTarget.y = transform.position.y;

        Vector3 entryDirection = takeoffDirection;

        if (runwayJoinRoute.Count >= 2)
        {
            Vector3 previous = gridManager.GridToWorldCenter(
                runwayJoinRoute[runwayJoinRoute.Count - 2]);

            entryDirection = joinWorld - previous;
            entryDirection.y = 0f;

            if (entryDirection.sqrMagnitude > 0.0001f)
                entryDirection.Normalize();
            else
                entryDirection = takeoffDirection;
        }

        BuildRunwayEntryCurve(
            transform.position,
            runwayLineUpTarget,
            entryDirection,
            takeoffDirection);

        currentWaypointIndex = 0;
        currentState = DepartureState.MovingOntoRunway;
        debugStatus = $"Moving onto {debugSelectedRunway} to line up.";
    }

    private void BuildRunwayEntryCurve(
        Vector3 start,
        Vector3 end,
        Vector3 entryDirection,
        Vector3 exitDirection)
    {
        movementWaypoints.Clear();

        start.y = transform.position.y;
        end.y = transform.position.y;

        float distance = Vector3.Distance(start, end);

        if (distance < 0.05f)
        {
            movementWaypoints.Add(end);
            return;
        }

        float radius = Mathf.Min(
            turningRadiusInCells * gridManager.CellSize,
            distance * 0.5f);

        if (radius < 0.05f)
        {
            movementWaypoints.Add(end);
            return;
        }

        const float factor = 0.55228475f;

        Vector3 control1 = start + entryDirection * radius * factor;
        Vector3 control2 = end - exitDirection * radius * factor;

        for (int sample = 1; sample <= turnCurveResolution; sample++)
        {
            float t = sample / (float)turnCurveResolution;
            float u = 1f - t;

            Vector3 point =
                u * u * u * start +
                3f * u * u * t * control1 +
                3f * u * t * t * control2 +
                t * t * t * end;

            point.y = transform.position.y;
            movementWaypoints.Add(point);
        }
    }

    private void BeginFinalRunwayAlignment()
    {
        transform.position = runwayLineUpTarget;

        runwayLineUpRotation =
            Quaternion.LookRotation(takeoffDirection, Vector3.up) *
            Quaternion.Euler(modelRotationOffset);

        currentState = DepartureState.AligningOnRunway;
        debugStatus = $"Aligning on {debugSelectedRunway}.";
    }

    private void UpdateRunwayAlignment()
    {
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            runwayLineUpRotation,
            rotationSpeedDegreesPerSecond * Time.deltaTime);

        if (Quaternion.Angle(transform.rotation, runwayLineUpRotation) > 0.5f)
            return;

        transform.rotation = runwayLineUpRotation;
        currentState = DepartureState.LinedUpReadyForTakeoff;
        debugStatus = $"Lined up on {debugSelectedRunway}; waiting for takeoff.";

        Debug.Log(
            $"SIGHTSEEING AIRCRAFT LINED UP READY FOR TAKEOFF | " +
            $"Aircraft: {name} | Runway: {debugSelectedRunway} | " +
            $"Heading: {debugTakeoffHeading}",
            this);
    }

    private void AbortLineUpAndReturnToHold(string reason)
    {
        if (trafficManager != null)
            trafficManager.ReleaseRunwayLineUpReservation(this);

        currentState = DepartureState.AtHoldingPoint;
        debugStatus = $"Line-up aborted; holding. {reason}";
        retryTimer = runwaySelectionRetryInterval;

        Debug.LogWarning($"{name}: {debugStatus}", this);
    }

    private void UpdateTaxiMovement()
    {
        if (movementWaypoints.Count == 0 ||
            currentWaypointIndex >= movementWaypoints.Count)
        {
            HandleMovementRouteCompleted();
            return;
        }

        float speedKnots = taxiSpeedKnots;

        AircraftTaxiTrafficFollowing following =
            GetComponent<AircraftTaxiTrafficFollowing>();

        if (following != null)
        {
            speedKnots =
                following.GetTrafficFollowingSpeed(taxiSpeedKnots);
        }
        else
        {
            AircraftStatus status = GetComponent<AircraftStatus>();

            if (status != null)
                status.SetCurrentSpeedKnots(speedKnots);
        }

        float speed = AircraftStatus.KnotsToWorldSpeed(speedKnots);

        Vector3 target = movementWaypoints[currentWaypointIndex];
        target.y = transform.position.y;

        Vector3 toTarget = target - transform.position;

        if (toTarget.magnitude <= waypointArrivalDistance)
        {
            currentWaypointIndex++;

            if (currentWaypointIndex >= movementWaypoints.Count)
                HandleMovementRouteCompleted();

            return;
        }

        Vector3 direction = toTarget;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion rotation =
                Quaternion.LookRotation(direction.normalized, Vector3.up) *
                Quaternion.Euler(modelRotationOffset);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                rotation,
                rotationSpeedDegreesPerSecond * Time.deltaTime);
        }

        if (speed <= 0.001f)
            return;

        transform.position = Vector3.MoveTowards(
            transform.position,
            target,
            speed * Time.deltaTime);
    }

    private void HandleMovementRouteCompleted()
    {
        switch (currentState)
        {
            case DepartureState.TaxiingToHoldingPoint:
                ArriveAtHoldingPoint();
                break;

            case DepartureState.TaxiingToRunwayEntry:
                StartMovementOntoRunway();
                break;

            case DepartureState.MovingOntoRunway:
                BeginFinalRunwayAlignment();
                break;
        }
    }

    private void BuildSmoothedMovementWaypoints(List<Vector2Int> cells)
    {
        movementWaypoints.Clear();

        if (cells == null || cells.Count == 0)
            return;

        List<Vector3> points = new();

        foreach (Vector2Int cell in cells)
        {
            Vector3 point = gridManager.GridToWorldCenter(cell);
            point.y = transform.position.y;
            points.Add(point);
        }

        if (points.Count < 3)
        {
            movementWaypoints.AddRange(points);
            return;
        }

        List<int> turns = new();

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 incoming = (points[i] - points[i - 1]).normalized;
            Vector3 outgoing = (points[i + 1] - points[i]).normalized;
            float dot = Vector3.Dot(incoming, outgoing);

            if (dot > 0.99f || dot < -0.99f)
                continue;

            turns.Add(i);
        }

        float[] radii = new float[points.Count];

        for (int t = 0; t < turns.Count; t++)
        {
            int index = turns[t];
            int previousTurn = t > 0 ? turns[t - 1] : 0;
            int nextTurn = t < turns.Count - 1
                ? turns[t + 1]
                : points.Count - 1;

            float previousDistance =
                Vector3.Distance(points[previousTurn], points[index]);

            float nextDistance =
                Vector3.Distance(points[index], points[nextTurn]);

            float radius = turningRadiusInCells * gridManager.CellSize;

            radii[index] = Mathf.Min(
                radius,
                previousDistance * 0.5f,
                nextDistance * 0.5f);
        }

        const float factor = 0.55228475f;

        Vector3 current = points[0];
        AddMovementPoint(current);

        foreach (int index in turns)
        {
            float radius = radii[index];

            if (radius <= 0.01f)
                continue;

            Vector3 incoming = (points[index] - points[index - 1]).normalized;
            Vector3 outgoing = (points[index + 1] - points[index]).normalized;

            Vector3 entry = points[index] - incoming * radius;
            Vector3 exit = points[index] + outgoing * radius;

            AddStraightMovementPoints(current, entry);

            Vector3 control1 = entry + incoming * radius * factor;
            Vector3 control2 = exit - outgoing * radius * factor;

            for (int sample = 1; sample <= turnCurveResolution; sample++)
            {
                float t = sample / (float)turnCurveResolution;
                float u = 1f - t;

                Vector3 point =
                    u * u * u * entry +
                    3f * u * u * t * control1 +
                    3f * u * t * t * control2 +
                    t * t * t * exit;

                AddMovementPoint(point);
            }

            current = exit;
        }

        AddStraightMovementPoints(current, points[points.Count - 1]);
    }

    private void AddStraightMovementPoints(Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);
        float maximumStep = Mathf.Max(0.1f, gridManager.CellSize * 0.5f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / maximumStep));

        for (int i = 1; i <= steps; i++)
            AddMovementPoint(Vector3.Lerp(start, end, i / (float)steps));
    }

    private void AddMovementPoint(Vector3 point)
    {
        if (movementWaypoints.Count > 0 &&
            Vector3.Distance(movementWaypoints[movementWaypoints.Count - 1], point) < 0.01f)
            return;

        movementWaypoints.Add(point);
    }

    private void ArriveAtHoldingPoint()
    {
        currentState = DepartureState.AtHoldingPoint;
        retryTimer = 0f;
        debugStatus = $"At safe holding point for {debugSelectedRunway}; waiting for clearance.";

        Debug.Log(
            $"SIGHTSEEING AIRCRAFT AT DEPARTURE HOLDING POINT | " +
            $"Aircraft: {name} | Runway: {debugSelectedRunway} | " +
            $"Holding cell: {holdingCell}",
            this);
    }

    private float CalculateRouteLength(List<Vector2Int> path)
    {
        if (path == null || path.Count < 2)
            return 0f;

        float length = 0f;

        for (int i = 1; i < path.Count; i++)
        {
            length += Vector2Int.Distance(path[i - 1], path[i]) *
                      gridManager.CellSize;
        }

        return length;
    }

    private float CalculateSidewindComponentKnots(Vector3 direction)
    {
        Vector3 windFrom = weather.WindFromDirection;
        windFrom.y = 0f;
        direction.y = 0f;

        if (windFrom.sqrMagnitude < 0.0001f ||
            direction.sqrMagnitude < 0.0001f)
            return 0f;

        windFrom.Normalize();
        direction.Normalize();

        float alignment = Mathf.Clamp01(
            Mathf.Abs(Vector3.Dot(direction, windFrom)));

        return weather.windSpeedKnots *
               Mathf.Sqrt(Mathf.Max(0f, 1f - alignment * alignment));
    }

    private string HeadingName(Vector3 direction)
    {
        float heading = Mathf.Repeat(
            Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
            360f);

        string cardinal;

        if (heading >= 315f || heading < 45f)
            cardinal = "North";
        else if (heading < 135f)
            cardinal = "East";
        else if (heading < 225f)
            cardinal = "South";
        else
            cardinal = "West";

        return $"{cardinal} ({heading:F0}°)";
    }

    private void LogWaitingMessage(string message)
    {
        debugStatus = message;

        if (hasLoggedWaitingMessage)
            return;

        hasLoggedWaitingMessage = true;

        Debug.LogWarning(
            $"{name}: Sightseeing departure taxi waiting. {message}",
            this);
    }

    private void OnDestroy()
    {
        if (trafficManager == null)
            return;

        trafficManager.ReleaseRunwayLineUpReservation(this);
        trafficManager.ReleaseDepartureReservation(this);
    }

    [ContextMenu("Debug/Select Runway And Start Taxi")]
    private void DebugSelectDepartureRunway()
    {
        TrySelectDepartureRunway();
    }
}