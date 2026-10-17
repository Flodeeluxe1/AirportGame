
using System.Collections.Generic;
using UnityEngine;

public class AircraftTrainingDepartureTaxiController : MonoBehaviour
{
    public enum DepartureState
    {
        WaitingForDepartureRequest,
        WaitingForRunway,
        TaxiingToHoldingPoint,
        AtHoldingPoint,
        TaxiingToRunwayEntry,
        MovingOntoRunway,
        AligningOnRunway,
        LinedUpReadyForTakeoff
    }

    [Header("References")]
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
    [SerializeField] private int startSearchRadiusCells = 4;
    [Range(1, 20)]
    [SerializeField] private int maximumStartCandidates = 10;

    [Header("Holding Position")]
    [Min(0f)]
    [SerializeField] private float runwaySafetyMarginInCells = 1f;

    [Header("Taxi Movement")]
    [Min(1f)]
    [SerializeField] private float taxiSpeedKnots = 15f;
    [Min(0.001f)]
    [SerializeField] private float worldUnitsPerSecondPerKnot = 0.05f;
    [Min(1f)]
    [SerializeField] private float rotationSpeedDegreesPerSecond = 45f;
    [Min(0.01f)]
    [SerializeField] private float waypointArrivalDistance = 0.15f;
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Smooth Taxi Turns")]
    [Min(0.1f)]
    [SerializeField] private float turningRadiusInCells = 1f;
    [Range(4, 16)]
    [SerializeField] private int turnCurveResolution = 8;

    [Header("Runway Line-Up")]
    [Min(0f)]
    [SerializeField] private float lineUpSafetyMarginInCells = 0.25f;

    [Header("Runtime Status")]
    [SerializeField]
    private DepartureState currentState =
        DepartureState.WaitingForDepartureRequest;
    [SerializeField] private AircraftRunway selectedRunway;
    [SerializeField] private Vector3 takeoffDirection;
    [SerializeField] private Vector3 departureThreshold;
    [SerializeField] private Vector2Int holdingCell;
    [SerializeField] private bool hasHoldingCell;
    [SerializeField]
    private string debugStatus =
        "Waiting for training departure request";
    [SerializeField] private string debugSelectedRunway = "None";
    [SerializeField] private float debugHeadwindComponentKnots;
    [SerializeField] private float debugSidewindComponentKnots;
    [SerializeField] private float debugTaxiRouteLength;

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

        AircraftFlightIdentity identity =
            GetComponent<AircraftFlightIdentity>();

        TrainingFlightController trainingController =
            GetComponent<TrainingFlightController>();

        if (identity == null ||
            !identity.IsInitialized ||
            !identity.IsTrainingFlight ||
            trainingController == null ||
            !trainingController.DepartureRequested)
        {
            currentState = DepartureState.WaitingForDepartureRequest;
            debugStatus =
                "Training aircraft parked; waiting for circuit departure request.";
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


    public bool PrepareForTouchAndGo(
        AircraftRunway runway,
        Vector3 takeoffDirection)
    {
        if (runway == null)
        {
            debugStatus =
                "Touch-and-go failed: landing runway is missing.";
            return false;
        }

        ResolveReferences();

        if (trafficManager == null || gridManager == null)
        {
            debugStatus =
                "Touch-and-go waiting: runway traffic manager or grid is missing.";
            return false;
        }

        takeoffDirection.y = 0f;

        if (takeoffDirection.sqrMagnitude < 0.0001f)
        {
            debugStatus =
                "Touch-and-go failed: takeoff direction is invalid.";
            return false;
        }

        takeoffDirection.Normalize();

        if (!runway.TryGetGeometry(
                gridManager,
                out Vector3 center,
                out Vector3 axis,
                out float length,
                out float surfaceY))
        {
            debugStatus =
                "Touch-and-go failed: runway geometry is unavailable.";
            return false;
        }

        if (!trafficManager.TryReserveRunwayForLineUp(
                runway,
                this,
                out string reason))
        {
            debugStatus =
                $"Touch-and-go waiting for runway clearance: {reason}";
            return false;
        }

        if (!trafficManager.RegisterDepartureReservation(runway, this))
        {
            trafficManager.ReleaseRunwayLineUpReservation(this);

            debugStatus =
                "Touch-and-go failed: departure reservation could not be registered.";
            return false;
        }

        selectedRunway = runway;
        this.takeoffDirection = takeoffDirection;

        runwayCenter = center;
        runwayLength = length;
        runwaySurfaceY = surfaceY;

        hasHoldingCell = false;
        holdingCell = default;

        taxiRoute.Clear();
        runwayJoinRoute.Clear();
        movementWaypoints.Clear();

        currentWaypointIndex = 0;
        holdingRouteIndex = 0;

        departureThreshold =
            center + takeoffDirection * (length * 0.5f);

        debugSelectedRunway = runway.name;
        debugStatus =
            "Touch-and-go cleared. Ready for immediate takeoff.";

        currentState = DepartureState.LinedUpReadyForTakeoff;

        Debug.Log(
            $"TRAINING TOUCH-AND-GO CLEARED | Aircraft: {name} | " +
            $"Runway: {runway.name}",
            this
        );

        return true;
    }


    public bool ResetForTrainingCircuit()
    {
        AircraftFlightIdentity identity =
            GetComponent<AircraftFlightIdentity>();

        if (identity == null || !identity.IsTrainingFlight)
        {
            Debug.LogWarning(
                $"{name}: Departure reset is only for training flights.",
                this);
            return false;
        }

        if (trafficManager != null)
        {
            trafficManager.ReleaseRunwayLineUpReservation(this);
            trafficManager.ReleaseDepartureReservation(this);
        }

        selectedRunway = null;
        takeoffDirection = Vector3.zero;
        departureThreshold = Vector3.zero;
        holdingCell = default;
        hasHoldingCell = false;

        taxiRoute.Clear();
        runwayJoinRoute.Clear();
        movementWaypoints.Clear();

        currentWaypointIndex = 0;
        holdingRouteIndex = 0;
        retryTimer = 0f;
        hasLoggedWaitingMessage = false;
        lastRunwayWaitReason = string.Empty;

        runwayJoinCell = default;
        runwayCenter = Vector3.zero;
        runwayLength = 0f;
        runwaySurfaceY = 0f;
        runwayLineUpTarget = Vector3.zero;
        runwayLineUpRotation = Quaternion.identity;

        debugSelectedRunway = "None";
        debugStatus =
            "Training circuit reset; waiting for departure request.";

        currentState = DepartureState.WaitingForDepartureRequest;
        return true;
    }

    public bool TrySelectDepartureRunway()
    {
        ResolveReferences();

        if (selectedRunway != null)
            return true;

        if (gridManager == null ||
            weather == null ||
            trafficManager == null ||
            taxiwayNetwork == null ||
            taxiwayPathfinder == null)
        {
            LogWaitingMessage(
                "Missing grid, weather, runway traffic, taxiway network or pathfinder.");
            return false;
        }

        if (aircraftCollider == null || gridManager.CellSize <= 0f)
        {
            LogWaitingMessage(
                "Aircraft BoxCollider or valid grid cell size is missing.");
            return false;
        }

        List<Vector2Int> startCandidates = GetStartCandidates();
        if (startCandidates.Count == 0)
        {
            LogWaitingMessage("No taxiway cells found near the aircraft.");
            return false;
        }

        AircraftRunway[] runways =
            FindObjectsByType<AircraftRunway>(FindObjectsSortMode.None);

        if (runways.Length == 0)
        {
            LogWaitingMessage("No runways were found.");
            return false;
        }

        Vector3 windFrom = weather.WindFromDirection;
        windFrom.y = 0f;

        if (windFrom.sqrMagnitude < 0.0001f)
            windFrom = Vector3.forward;

        windFrom.Normalize();

        AircraftRunway bestRunway = null;
        Vector3 bestDirection = Vector3.zero;
        Vector3 bestCenter = Vector3.zero;
        float bestLength = 0f;
        float bestSurfaceY = 0f;
        float bestHeadwind = -1f;
        float bestSidewind = float.MaxValue;
        float bestRouteLength = float.MaxValue;
        int bestTrafficCount = int.MaxValue;

        List<Vector2Int> bestHoldingRoute = null;
        List<Vector2Int> bestJoinRoute = null;
        Vector2Int bestHoldingCell = default;

        foreach (AircraftRunway runway in runways)
        {
            if (runway == null ||
                !runway.TryGetGeometry(
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

            float sidewind = CalculateSidewind(direction);

            if (!TryFindRouteToRunway(
                runway,
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

            Vector3 firstPoint =
                gridManager.GridToWorldCenter(safeRoute[0]);
            firstPoint.y = transform.position.y;

            routeLength += Vector3.Distance(transform.position, firstPoint);

            int trafficCount =
                trafficManager.GetRunwayTrafficCount(runway, null, this);

            bool crosswindAllowed =
                sidewind <= maximumSidewindComponentKnots + 0.001f;
            bool bestCrosswindAllowed =
                bestRunway != null &&
                bestSidewind <= maximumSidewindComponentKnots + 0.001f;

            bool better =
                bestRunway == null ||
                (crosswindAllowed && !bestCrosswindAllowed) ||
                (crosswindAllowed == bestCrosswindAllowed &&
                 trafficCount < bestTrafficCount) ||
                (crosswindAllowed == bestCrosswindAllowed &&
                 trafficCount == bestTrafficCount &&
                 headwind > bestHeadwind + 0.01f) ||
                (crosswindAllowed == bestCrosswindAllowed &&
                 trafficCount == bestTrafficCount &&
                 Mathf.Abs(headwind - bestHeadwind) <= 0.01f &&
                 routeLength < bestRouteLength);

            if (!better)
                continue;

            bestRunway = runway;
            bestDirection = direction;
            bestCenter = center;
            bestLength = length;
            bestSurfaceY = surfaceY;
            bestHeadwind = headwind;
            bestSidewind = sidewind;
            bestRouteLength = routeLength;
            bestTrafficCount = trafficCount;
            bestHoldingRoute = safeRoute;
            bestJoinRoute = approachRoute;
            bestHoldingCell = candidateHoldingCell;
        }

        if (bestRunway == null ||
            bestHoldingRoute == null ||
            bestJoinRoute == null)
        {
            LogWaitingMessage(
                "No reachable runway with a safe taxiway holding position was found.");
            return false;
        }

        if (!trafficManager.RegisterDepartureReservation(bestRunway, this))
        {
            LogWaitingMessage("Could not reserve the selected runway.");
            return false;
        }

        selectedRunway = bestRunway;
        takeoffDirection = bestDirection.normalized;
        runwayCenter = bestCenter;
        runwayLength = bestLength;
        runwaySurfaceY = bestSurfaceY;
        departureThreshold =
            runwayCenter - takeoffDirection * (runwayLength * 0.5f);
        departureThreshold.y = runwaySurfaceY;

        debugSelectedRunway = bestRunway.name;
        debugHeadwindComponentKnots = bestHeadwind;
        debugSidewindComponentKnots = bestSidewind;
        debugTaxiRouteLength = bestRouteLength;

        taxiRoute.Clear();
        taxiRoute.AddRange(bestHoldingRoute);

        runwayJoinRoute.Clear();
        runwayJoinRoute.AddRange(bestJoinRoute);

        holdingCell = bestHoldingCell;
        runwayJoinCell = bestJoinRoute[bestJoinRoute.Count - 1];
        hasHoldingCell = true;
        holdingRouteIndex = bestHoldingRoute.Count - 1;

        BuildSmoothedMovementWaypoints(taxiRoute);
        currentWaypointIndex = 0;
        currentState = DepartureState.TaxiingToHoldingPoint;

        debugStatus = $"Taxiing to holding point for {debugSelectedRunway}.";
        hasLoggedWaitingMessage = false;
        lastRunwayWaitReason = string.Empty;

        return true;
    }

    private List<Vector2Int> GetStartCandidates()
    {
        List<Vector2Int> candidates = new();
        Vector2Int aircraftCell =
            gridManager.WorldToGrid(transform.position);

        for (int x = -startSearchRadiusCells;
             x <= startSearchRadiusCells; x++)
        {
            for (int z = -startSearchRadiusCells;
                 z <= startSearchRadiusCells; z++)
            {
                Vector2Int cell = aircraftCell + new Vector2Int(x, z);

                if (gridManager.IsInsideGrid(cell) &&
                    taxiwayNetwork.IsTaxiway(cell) &&
                    !candidates.Contains(cell))
                {
                    candidates.Add(cell);
                }
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

    private bool TryFindRouteToRunway(
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
        int cellCount = axisIsX ? placed.Width : placed.Depth;

        int bestOffset = int.MaxValue;
        float bestLength = float.MaxValue;

        for (int offset = 0; offset < cellCount; offset++)
        {
            Vector2Int a;
            Vector2Int b;

            if (axisIsX)
            {
                int x = direction.x > 0f
                    ? placed.Origin.x + offset
                    : placed.Origin.x + placed.Width - 1 - offset;

                a = new Vector2Int(x, placed.Origin.y - 1);
                b = new Vector2Int(x, placed.Origin.y + placed.Depth);
            }
            else
            {
                int z = direction.z > 0f
                    ? placed.Origin.y + offset
                    : placed.Origin.y + placed.Depth - 1 - offset;

                a = new Vector2Int(placed.Origin.x - 1, z);
                b = new Vector2Int(placed.Origin.x + placed.Width, z);
            }

            foreach (Vector2Int sideCell in new[] { a, b })
            {
                if (!gridManager.IsInsideGrid(sideCell) ||
                    !taxiwayNetwork.IsTaxiway(sideCell))
                    continue;

                int radius = Mathf.Max(
                    1, taxiwayNetwork.GetTaxiwayWidth(sideCell));

                Queue<Vector2Int> queue = new();
                HashSet<Vector2Int> visited = new();
                List<Vector2Int> targets = new();

                queue.Enqueue(sideCell);
                visited.Add(sideCell);

                int distance = 0;
                while (queue.Count > 0 && distance <= radius)
                {
                    int count = queue.Count;

                    for (int i = 0; i < count; i++)
                    {
                        Vector2Int current = queue.Dequeue();

                        if (taxiwayNetwork.IsCenterlineCell(current))
                        {
                            targets.Add(current);
                            continue;
                        }

                        foreach (Vector2Int neighbour in
                                 taxiwayNetwork.GetConnectedTaxiwayCells(current))
                        {
                            if (visited.Add(neighbour))
                                queue.Enqueue(neighbour);
                        }
                    }

                    if (targets.Count > 0)
                        break;

                    distance++;
                }

                foreach (Vector2Int target in targets)
                {
                    int offsetFromThreshold = axisIsX
                        ? direction.x > 0f
                            ? target.x - placed.Origin.x
                            : placed.Origin.x + placed.Width - 1 - target.x
                        : direction.z > 0f
                            ? target.y - placed.Origin.y
                            : placed.Origin.y + placed.Depth - 1 - target.y;

                    if (offsetFromThreshold < 0)
                        continue;

                    foreach (Vector2Int start in startCandidates)
                    {
                        List<Vector2Int> path =
                            taxiwayPathfinder.FindPath(start, target);

                        if (path == null || path.Count == 0)
                            continue;

                        float pathLength = CalculateRouteLength(path);
                        Vector3 first =
                            gridManager.GridToWorldCenter(path[0]);
                        first.y = transform.position.y;
                        pathLength += Vector3.Distance(transform.position, first);

                        if (offsetFromThreshold > bestOffset ||
                            (offsetFromThreshold == bestOffset &&
                             pathLength >= bestLength))
                            continue;

                        bestOffset = offsetFromThreshold;
                        bestLength = pathLength;
                        bestRoute = path;
                    }
                }
            }
        }

        return bestRoute != null;
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

        float distance = 0f;

        for (int i = approachRoute.Count - 2; i >= 0; i--)
        {
            Vector3 current =
                gridManager.GridToWorldCenter(approachRoute[i]);
            Vector3 next =
                gridManager.GridToWorldCenter(approachRoute[i + 1]);

            current.y = 0f;
            next.y = 0f;
            distance += Vector3.Distance(current, next);

            if (!taxiwayNetwork.IsCenterlineCell(approachRoute[i]))
                continue;

            Vector3 direction = next - current;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
                continue;

            float extent = GetAircraftForwardExtent(direction.normalized);
            if (extent < 0f)
                return false;

            float required =
                extent + runwaySafetyMarginInCells * gridManager.CellSize;

            if (distance + 0.001f < required)
                continue;

            safeRoute = approachRoute.GetRange(0, i + 1);
            safeCell = approachRoute[i];
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
                Vector3.right * aircraftCollider.size.x * 0.5f));

        Vector3 localHalfY = transform.InverseTransformVector(
            colliderTransform.TransformVector(
                Vector3.up * aircraftCollider.size.y * 0.5f));

        Vector3 localHalfZ = transform.InverseTransformVector(
            colliderTransform.TransformVector(
                Vector3.forward * aircraftCollider.size.z * 0.5f));

        Vector3 scale = transform.lossyScale;

        Vector3 centerOffset =
            intendedRotation * Vector3.Scale(localCenter, scale);
        Vector3 halfX =
            intendedRotation * Vector3.Scale(localHalfX, scale);
        Vector3 halfY =
            intendedRotation * Vector3.Scale(localHalfY, scale);
        Vector3 halfZ =
            intendedRotation * Vector3.Scale(localHalfZ, scale);

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
            selectedRunway, this, out string reason))
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
        StartTaxiingToRunwayEntry();
    }

    private void StartTaxiingToRunwayEntry()
    {
        if (runwayJoinRoute.Count == 0 ||
            holdingRouteIndex < 0 ||
            holdingRouteIndex >= runwayJoinRoute.Count)
        {
            AbortLineUpAndReturnToHold("Runway join route is invalid.");
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

        Vector3 joinWorld =
            gridManager.GridToWorldCenter(runwayJoinCell);
        joinWorld.y = transform.position.y;

        float extent = GetAircraftForwardExtent(takeoffDirection);
        if (extent < 0f)
        {
            AbortLineUpAndReturnToHold(
                "Could not calculate aircraft collider dimensions.");
            return;
        }

        float halfLength = runwayLength * 0.5f;
        float margin = lineUpSafetyMarginInCells * gridManager.CellSize;
        float minPosition = -halfLength + extent + margin;
        float maxPosition = halfLength - extent - margin;

        if (minPosition > maxPosition)
        {
            AbortLineUpAndReturnToHold(
                "Runway is too short for this aircraft.");
            return;
        }

        float joinPosition =
            Vector3.Dot(joinWorld - runwayCenter, takeoffDirection);

        float lineUpPosition = Mathf.Clamp(
            Mathf.Max(joinPosition, minPosition),
            minPosition,
            maxPosition);

        runwayLineUpTarget =
            runwayCenter + takeoffDirection * lineUpPosition;
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
        debugStatus = $"Moving onto runway {debugSelectedRunway}.";
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

        const float controlFactor = 0.55228475f;

        Vector3 control1 =
            start + entryDirection * radius * controlFactor;
        Vector3 control2 =
            end - exitDirection * radius * controlFactor;

        for (int i = 1; i <= turnCurveResolution; i++)
        {
            float t = i / (float)turnCurveResolution;
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
        debugStatus = $"Aligning with runway heading for {debugSelectedRunway}.";
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
            $"TRAINING AIRCRAFT LINED UP | Aircraft: {name} | " +
            $"Runway: {debugSelectedRunway} | Position: {transform.position}",
            this);
    }

    private void AbortLineUpAndReturnToHold(string reason)
    {
        if (trafficManager != null)
            trafficManager.ReleaseRunwayLineUpReservation(this);

        currentState = DepartureState.AtHoldingPoint;
        debugStatus = $"Line-up aborted; holding. {reason}";
        Debug.LogWarning($"{name}: {debugStatus}", this);
        retryTimer = runwaySelectionRetryInterval;
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

    private void UpdateTaxiMovement()
    {
        if (movementWaypoints.Count == 0 ||
            currentWaypointIndex >= movementWaypoints.Count)
        {
            HandleMovementRouteCompleted();
            return;
        }

        float baseSpeed = taxiSpeedKnots * worldUnitsPerSecondPerKnot;

        AircraftTaxiTrafficFollowing following =
            GetComponent<AircraftTaxiTrafficFollowing>();

        float speed = following != null
            ? following.GetTrafficFollowingSpeed(baseSpeed)
            : baseSpeed;

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
            Quaternion targetRotation =
                Quaternion.LookRotation(direction.normalized, Vector3.up) *
                Quaternion.Euler(modelRotationOffset);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
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
                currentState = DepartureState.AtHoldingPoint;
                retryTimer = 0f;
                debugStatus = $"At safe holding point for {debugSelectedRunway}.";
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
                radius, previousDistance * 0.5f, nextDistance * 0.5f);
        }

        const float controlFactor = 0.55228475f;

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

            Vector3 control1 = entry + incoming * radius * controlFactor;
            Vector3 control2 = exit - outgoing * radius * controlFactor;

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
        float maxStep = Mathf.Max(0.1f, gridManager.CellSize * 0.5f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / maxStep));

        for (int i = 1; i <= steps; i++)
            AddMovementPoint(Vector3.Lerp(start, end, i / (float)steps));
    }

    private void AddMovementPoint(Vector3 point)
    {
        if (movementWaypoints.Count > 0 &&
            Vector3.Distance(
                movementWaypoints[movementWaypoints.Count - 1],
                point) < 0.01f)
            return;

        movementWaypoints.Add(point);
    }

    private float CalculateSidewind(Vector3 direction)
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

    private void LogWaitingMessage(string message)
    {
        debugStatus = message;

        if (hasLoggedWaitingMessage)
            return;

        hasLoggedWaitingMessage = true;
        Debug.LogWarning($"{name}: Training departure waiting. {message}", this);
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
