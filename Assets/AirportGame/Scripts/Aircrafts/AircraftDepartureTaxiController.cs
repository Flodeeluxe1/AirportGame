
using System.Collections.Generic;
using UnityEngine;

public class AircraftDepartureTaxiController : MonoBehaviour
{
    public enum DepartureState
    {
        WaitingForGateExit,
        WaitingForRunway,
        TaxiingToHoldingPoint,
        AtHoldingPoint,
        TaxiingToRunwayEntry,
        MovingOntoRunway,
        AligningOnRunway,
        LinedUpReadyForTakeoff
    }

    [Header("References")]
    [SerializeField] private AircraftGateExitController gateExitController;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private Weather weather;
    [SerializeField] private AircraftRunwayTrafficManager trafficManager;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;
    [SerializeField] private TaxiwayPathfinder taxiwayPathfinder;
    [SerializeField] private BoxCollider aircraftCollider;

    [Header("Runway Selection")]
    [Tooltip("Maximum permitted crosswind component, in knots.")]
    [Min(0f)]
    [SerializeField] private float maximumSidewindComponentKnots = 15f;

    [Tooltip("Seconds between retries when no reachable runway is found.")]
    [Min(0.5f)]
    [SerializeField] private float runwaySelectionRetryInterval = 3f;

    [Tooltip("Search radius for nearby taxiway starting cells.")]
    [Min(1)]
    [SerializeField] private int startSearchRadiusCells = 4;

    [Tooltip("Maximum taxiway starting cells considered by A*.")]
    [Range(1, 20)]
    [SerializeField] private int maximumStartCandidates = 10;

    [Header("Holding Position")]
    [Tooltip("Extra space beyond the aircraft's collider, in grid cells.")]
    [Min(0f)]
    [SerializeField] private float runwaySafetyMarginInCells = 1f;

    [Header("Taxi Movement")]
    [Min(1f)]
    [SerializeField] private float taxiSpeedKnots = 15f;

    [Tooltip("Converts knots to Unity world units per second.")]
    [Min(0.001f)]
    [SerializeField] private float worldUnitsPerSecondPerKnot = 0.05f;

    [Min(1f)]
    [SerializeField] private float rotationSpeedDegreesPerSecond = 45f;

    [Min(0.01f)]
    [SerializeField] private float waypointArrivalDistance = 0.15f;

    [Tooltip("Use this if the aircraft model's nose isn't its local +Z axis.")]
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Smooth Taxi Turns")]
    [Min(0.1f)]
    [SerializeField] private float turningRadiusInCells = 1f;

    [Range(4, 16)]
    [SerializeField] private int turnCurveResolution = 8;

    [Header("Runway Line-Up")]
    [Tooltip("The aircraft is positioned fully on the runway before it reports lined up.")]
    [Min(0f)]
    [SerializeField] private float lineUpSafetyMarginInCells = 0.25f;

    [Header("Runtime Status")]
    [SerializeField]
    private DepartureState currentState =
        DepartureState.WaitingForGateExit;

    [SerializeField] private AircraftRunway selectedRunway;
    [SerializeField] private Vector3 takeoffDirection;
    [SerializeField] private Vector3 departureThreshold;
    [SerializeField] private Vector2Int holdingCell;
    [SerializeField] private bool hasHoldingCell;

    [SerializeField] private float debugAircraftForwardExtent;
    [SerializeField] private float debugRequiredHoldingDistance;
    [SerializeField] private float debugActualHoldingDistance;
    [SerializeField] private int debugRunwayTrafficCount;
    [SerializeField] private float debugHeadwindComponentKnots;
    [SerializeField] private float debugSidewindComponentKnots;
    [SerializeField] private float debugTaxiRouteLength;
    [SerializeField] private string debugSelectedRunway = "None";
    [SerializeField] private string debugTakeoffHeading = "None";
    [SerializeField] private string debugStatus = "Waiting for gate exit";

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
    public bool AtHoldingPoint => currentState == DepartureState.AtHoldingPoint;
    public bool IsLinedUp => currentState == DepartureState.LinedUpReadyForTakeoff;

    private void Awake()
    {
        ResolveReferences();
    }



    private void Update()
    {
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

        bool isInitializedTrainingFlight =
            identity != null &&
            identity.IsInitialized &&
            identity.IsTrainingFlight;

        bool isHobbyFlight =
            identity != null &&
            identity.IsInitialized &&
            identity.IsHobbyFlight;

        if (isInitializedTrainingFlight)
        {
            TrainingFlightController trainingController =
                GetComponent<TrainingFlightController>();

            if (trainingController == null ||
                !trainingController.DepartureRequested)
            {
                currentState = DepartureState.WaitingForGateExit;
                debugStatus =
                    "Training aircraft parked; waiting for circuit departure request.";
                return;
            }
        }
        else if (isHobbyFlight)
        {
            HobbyAircraftVisitController visitController =
                GetComponent<HobbyAircraftVisitController>();

            // Hobby aircraft wait until their visit has finished.
            if (visitController == null ||
                !visitController.DepartureRequested)
            {
                currentState = DepartureState.WaitingForGateExit;
                debugStatus =
                    "Hobby aircraft parked; waiting for visit to finish";
                return;
            }
        }
        else if (gateExitController == null ||
                 !gateExitController.ReadyForTaxi)
        {
            currentState = DepartureState.WaitingForGateExit;
            debugStatus = "Waiting for gate exit to finish";
            return;
        }

        currentState = DepartureState.WaitingForRunway;

        retryTimer -= Time.deltaTime;

        if (retryTimer > 0f)
            return;

        retryTimer = runwaySelectionRetryInterval;

        TrySelectDepartureRunway();
    }


    public bool ResetForTrainingCircuit()
    {
        AircraftFlightIdentity identity =
            GetComponent<AircraftFlightIdentity>();

        if (identity == null || !identity.IsTrainingFlight)
        {
            Debug.LogWarning(
                $"{name}: Departure reset is only for training flights.",
                this
            );

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
        debugTakeoffHeading = "None";
        debugStatus = "Training circuit reset; waiting for departure request.";

        currentState = DepartureState.WaitingForGateExit;

        return true;
    }


    private void ResolveReferences()
    {
        if (gateExitController == null)
            gateExitController = GetComponent<AircraftGateExitController>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (weather == null)
            weather = FindFirstObjectByType<Weather>();

        if (trafficManager == null)
            trafficManager = FindFirstObjectByType<AircraftRunwayTrafficManager>();

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

        if (gridManager == null ||
            weather == null ||
            trafficManager == null ||
            taxiwayNetwork == null ||
            taxiwayPathfinder == null)
        {
            LogWaitingMessage(
                "Missing GridManager, Weather, traffic manager, " +
                "TaxiwayNetwork or TaxiwayPathfinder."
            );

            return false;
        }

        if (aircraftCollider == null)
        {
            LogWaitingMessage(
                "Aircraft-sized BoxCollider is missing. Assign it in the Inspector."
            );

            return false;
        }

        if (gridManager.CellSize <= 0f)
        {
            LogWaitingMessage("GridManager has an invalid CellSize.");
            return false;
        }

        List<Vector2Int> startCandidates = GetStartCandidates();

        if (startCandidates.Count == 0)
        {
            LogWaitingMessage("No taxiway cells found near the aircraft.");
            return false;
        }

        AircraftRunway[] runways =
            FindObjectsByType<AircraftRunway>(
                FindObjectsSortMode.None
            );

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
        Vector2Int bestJoinCell = default;

        bool bestCrosswindWithinLimit = false;
        int bestTrafficCount = int.MaxValue;
        float bestHeadwindKnots = -1f;
        float bestRouteLength = float.MaxValue;
        float bestAircraftExtent = 0f;
        float bestRequiredDistance = 0f;
        float bestActualDistance = 0f;

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

            // Same wind-direction convention as AircraftLandingController.
            Vector3 candidateDirection =
                Vector3.Dot(axis, windFrom) >= 0f
                    ? axis
                    : -axis;

            float headwindKnots =
                weather.windSpeedKnots *
                Mathf.Max(
                    0f,
                    Vector3.Dot(candidateDirection, windFrom)
                );

            float sidewindKnots =
                CalculateSidewindComponentKnots(candidateDirection);

            bool crosswindWithinLimit =
                sidewindKnots <= maximumSidewindComponentKnots + 0.001f;

            int trafficCount =
                trafficManager.GetRunwayTrafficCount(
                    candidate,
                    null,
                    this
                );

            if (!TryFindRouteToRunwayThreshold(
                    candidate,
                    candidateDirection,
                    startCandidates,
                    out List<Vector2Int> approachRoute))
            {
                continue;
            }

            if (!TryCreateSafeHoldingRoute(
                    approachRoute,
                    out List<Vector2Int> safeRoute,
                    out Vector2Int candidateHoldingCell,
                    out float aircraftExtent,
                    out float requiredDistance,
                    out float actualDistance))
            {
                continue;
            }

            float routeLength = CalculateRouteLength(safeRoute);

            Vector3 firstPoint =
                gridManager.GridToWorldCenter(safeRoute[0]);

            firstPoint.y = transform.position.y;

            routeLength += Vector3.Distance(
                transform.position,
                firstPoint
            );

            bool chooseCandidate = bestRunway == null;

            if (!chooseCandidate)
            {
                if (crosswindWithinLimit != bestCrosswindWithinLimit)
                {
                    chooseCandidate = crosswindWithinLimit;
                }
                else if (trafficCount != bestTrafficCount)
                {
                    chooseCandidate = trafficCount < bestTrafficCount;
                }
                else if (headwindKnots > bestHeadwindKnots + 0.01f)
                {
                    chooseCandidate = true;
                }
                else if (Mathf.Abs(headwindKnots - bestHeadwindKnots) <= 0.01f &&
                         routeLength < bestRouteLength - 0.01f)
                {
                    chooseCandidate = true;
                }
            }

            if (!chooseCandidate)
                continue;

            bestRunway = candidate;
            bestDirection = candidateDirection;
            bestCenter = center;
            bestLength = length;
            bestSurfaceY = surfaceY;

            bestThreshold =
                center - candidateDirection * (length * 0.5f);

            bestThreshold.y = surfaceY;

            bestSafeRoute = safeRoute;
            bestJoinRoute = approachRoute;
            bestHoldingCell = candidateHoldingCell;
            bestJoinCell = approachRoute[approachRoute.Count - 1];

            bestCrosswindWithinLimit = crosswindWithinLimit;
            bestTrafficCount = trafficCount;
            bestHeadwindKnots = headwindKnots;
            bestRouteLength = routeLength;

            bestAircraftExtent = aircraftExtent;
            bestRequiredDistance = requiredDistance;
            bestActualDistance = actualDistance;
        }

        if (bestRunway == null ||
            bestSafeRoute == null ||
            bestJoinRoute == null)
        {
            LogWaitingMessage(
                "No reachable runway has a taxiway centerline with " +
                "enough room for this aircraft to hold safely."
            );

            return false;
        }

        if (!trafficManager.RegisterDepartureReservation(
                bestRunway,
                this))
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
        debugHeadwindComponentKnots = bestHeadwindKnots;
        debugSidewindComponentKnots =
            CalculateSidewindComponentKnots(takeoffDirection);
        debugTaxiRouteLength = bestRouteLength;
        debugAircraftForwardExtent = bestAircraftExtent;
        debugRequiredHoldingDistance = bestRequiredDistance;
        debugActualHoldingDistance = bestActualDistance;

        taxiRoute.Clear();
        taxiRoute.AddRange(bestSafeRoute);

        runwayJoinRoute.Clear();
        runwayJoinRoute.AddRange(bestJoinRoute);

        holdingCell = bestHoldingCell;
        runwayJoinCell = bestJoinCell;
        hasHoldingCell = true;
        holdingRouteIndex = bestSafeRoute.Count - 1;

        BuildSmoothedMovementWaypoints(taxiRoute);
        currentWaypointIndex = 0;

        currentState = DepartureState.TaxiingToHoldingPoint;

        debugStatus =
            $"Taxiing to safe holding point for {debugSelectedRunway}.";

        retryTimer = runwaySelectionRetryInterval;
        hasLoggedWaitingMessage = false;
        lastRunwayWaitReason = string.Empty;

        Debug.Log(
            $"AIRCRAFT DEPARTURE ROUTE SELECTED | " +
            $"Aircraft: {name} | Runway: {debugSelectedRunway} | " +
            $"Takeoff heading: {debugTakeoffHeading} | " +
            $"Traffic: {debugRunwayTrafficCount} | " +
            $"Headwind: {debugHeadwindComponentKnots:F1} knots | " +
            $"Sidewind: {debugSidewindComponentKnots:F1} knots | " +
            $"Holding cell: {holdingCell} | " +
            $"Runway join cell: {runwayJoinCell} | " +
            $"Route cells: {taxiRoute.Count} | " +
            $"Required holding distance: {debugRequiredHoldingDistance:F2} | " +
            $"Actual holding distance: {debugActualHoldingDistance:F2}",
            this
        );

        return true;
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
            debugStatus =
                $"Holding for runway clearance: {reason}";

            if (reason != lastRunwayWaitReason)
            {
                Debug.Log(
                    $"{name}: {debugStatus}",
                    this
                );

                lastRunwayWaitReason = reason;
            }

            return;
        }

        lastRunwayWaitReason = string.Empty;

        Debug.Log(
            $"{name}: Runway clear. Line-up reservation acquired " +
            $"for {selectedRunway.name}.",
            this
        );

        StartTaxiingToRunwayEntry();
    }

    private void StartTaxiingToRunwayEntry()
    {
        if (runwayJoinRoute.Count == 0 ||
            holdingRouteIndex < 0 ||
            holdingRouteIndex >= runwayJoinRoute.Count)
        {
            debugStatus =
                "Cannot continue to runway entry: join route is invalid.";

            Debug.LogWarning($"{name}: {debugStatus}", this);

            trafficManager.ReleaseRunwayLineUpReservation(this);
            currentState = DepartureState.AtHoldingPoint;
            return;
        }

        List<Vector2Int> remainingRoute = new();

        for (int i = holdingRouteIndex;
             i < runwayJoinRoute.Count;
             i++)
        {
            remainingRoute.Add(runwayJoinRoute[i]);
        }

        BuildSmoothedMovementWaypoints(remainingRoute);

        // The aircraft is already at the holding cell, so don't
        // travel backward to the first point of the remaining route.
        currentWaypointIndex =
            movementWaypoints.Count > 1 ? 1 : 0;

        currentState = DepartureState.TaxiingToRunwayEntry;

        debugStatus =
            $"Runway clear. Taxiing from holding point to runway entry " +
            $"for {debugSelectedRunway}.";
    }

    private void StartMovementOntoRunway()
    {
        if (selectedRunway == null ||
            runwayJoinRoute.Count == 0)
        {
            AbortLineUpAndReturnToHold(
                "Runway join data is missing."
            );

            return;
        }

        Vector3 runwayJoinWorld =
            gridManager.GridToWorldCenter(runwayJoinCell);

        runwayJoinWorld.y = transform.position.y;

        float aircraftExtent =
            GetAircraftForwardExtent(takeoffDirection);

        if (aircraftExtent < 0f)
        {
            AbortLineUpAndReturnToHold(
                "Could not calculate aircraft collider dimensions."
            );

            return;
        }

        float halfRunwayLength = runwayLength * 0.5f;

        float safetyMargin =
            lineUpSafetyMarginInCells * gridManager.CellSize;

        // The aircraft must fit on the runway after it lines up.
        float minimumAlongRunway =
            -halfRunwayLength + aircraftExtent + safetyMargin;

        float maximumAlongRunway =
            halfRunwayLength - aircraftExtent - safetyMargin;

        if (minimumAlongRunway > maximumAlongRunway)
        {
            AbortLineUpAndReturnToHold(
                "Runway is too short to accommodate this aircraft safely."
            );

            return;
        }

        float joinLongitudinalPosition =
            Vector3.Dot(
                runwayJoinWorld - runwayCenter,
                takeoffDirection
            );

        // Position the aircraft as close to the threshold as practical,
        // while keeping the complete collider on the runway.
        float lineUpLongitudinalPosition =
            Mathf.Clamp(
                Mathf.Max(
                    joinLongitudinalPosition,
                    minimumAlongRunway
                ),
                minimumAlongRunway,
                maximumAlongRunway
            );

        runwayLineUpTarget =
            runwayCenter +
            takeoffDirection * lineUpLongitudinalPosition;

        runwayLineUpTarget.y = transform.position.y;

        Vector3 entryDirection = takeoffDirection;

        if (runwayJoinRoute.Count >= 2)
        {
            Vector3 previous =
                gridManager.GridToWorldCenter(
                    runwayJoinRoute[runwayJoinRoute.Count - 2]
                );

            entryDirection =
                runwayJoinWorld - previous;

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
            takeoffDirection
        );

        currentWaypointIndex = 0;
        currentState = DepartureState.MovingOntoRunway;

        debugStatus =
            $"Moving onto {debugSelectedRunway} and lining up " +
            $"{debugTakeoffHeading}.";
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
            distance * 0.5f
        );

        if (radius < 0.05f)
        {
            movementWaypoints.Add(end);
            return;
        }

        const float bezierControlFactor = 0.55228475f;

        Vector3 control1 =
            start +
            entryDirection * radius * bezierControlFactor;

        Vector3 control2 =
            end -
            exitDirection * radius * bezierControlFactor;

        for (int sample = 1;
             sample <= turnCurveResolution;
             sample++)
        {
            float t = sample / (float)turnCurveResolution;
            float oneMinusT = 1f - t;

            Vector3 point =
                oneMinusT * oneMinusT * oneMinusT * start +
                3f * oneMinusT * oneMinusT * t * control1 +
                3f * oneMinusT * t * t * control2 +
                t * t * t * end;

            point.y = transform.position.y;

            movementWaypoints.Add(point);
        }
    }

    private void BeginFinalRunwayAlignment()
    {
        transform.position = runwayLineUpTarget;

        runwayLineUpRotation =
            Quaternion.LookRotation(
                takeoffDirection,
                Vector3.up
            ) *
            Quaternion.Euler(modelRotationOffset);

        currentState = DepartureState.AligningOnRunway;

        debugStatus =
            $"On runway. Aligning with takeoff heading " +
            $"{debugTakeoffHeading}.";
    }

    private void UpdateRunwayAlignment()
    {
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            runwayLineUpRotation,
            rotationSpeedDegreesPerSecond * Time.deltaTime
        );

        if (Quaternion.Angle(
                transform.rotation,
                runwayLineUpRotation) > 0.5f)
        {
            return;
        }

        transform.rotation = runwayLineUpRotation;

        currentState = DepartureState.LinedUpReadyForTakeoff;

        debugStatus =
            $"Lined up on {debugSelectedRunway}. " +
            "Waiting for takeoff implementation.";

        Debug.Log(
            $"AIRCRAFT LINED UP READY FOR TAKEOFF | " +
            $"Aircraft: {name} | Runway: {debugSelectedRunway} | " +
            $"Heading: {debugTakeoffHeading} | " +
            $"Position: {transform.position}",
            this
        );

        // Keep both reservations until the takeoff controller
        // takes over and the aircraft clears the runway.
    }

    private void AbortLineUpAndReturnToHold(string reason)
    {
        if (trafficManager != null)
            trafficManager.ReleaseRunwayLineUpReservation(this);

        currentState = DepartureState.AtHoldingPoint;

        debugStatus =
            $"Line-up aborted; holding for a safe solution. {reason}";

        Debug.LogWarning(
            $"{name}: {debugStatus}",
            this
        );

        retryTimer = runwaySelectionRetryInterval;
    }

    private List<Vector2Int> GetStartCandidates()
    {
        List<Vector2Int> candidates = new();

        if (gateExitController != null &&
            gateExitController.UsedForwardExit &&
            gateExitController.HasForwardExitCell &&
            taxiwayNetwork.IsTaxiway(
                gateExitController.ForwardExitCell))
        {
            candidates.Add(gateExitController.ForwardExitCell);
        }

        Vector2Int aircraftCell =
            gridManager.WorldToGrid(transform.position);

        for (int x = -startSearchRadiusCells;
             x <= startSearchRadiusCells;
             x++)
        {
            for (int z = -startSearchRadiusCells;
                 z <= startSearchRadiusCells;
                 z++)
            {
                Vector2Int candidate =
                    aircraftCell + new Vector2Int(x, z);

                if (!gridManager.IsInsideGrid(candidate))
                    continue;

                if (!taxiwayNetwork.IsTaxiway(candidate))
                    continue;

                if (!candidates.Contains(candidate))
                    candidates.Add(candidate);
            }
        }

        Vector2Int preferredCell =
            gateExitController != null &&
            gateExitController.HasForwardExitCell
                ? gateExitController.ForwardExitCell
                : aircraftCell;

        candidates.Sort((a, b) =>
        {
            bool aPreferred = a == preferredCell;
            bool bPreferred = b == preferredCell;

            if (aPreferred != bPreferred)
                return aPreferred ? -1 : 1;

            bool aCenterline = taxiwayNetwork.IsCenterlineCell(a);
            bool bCenterline = taxiwayNetwork.IsCenterlineCell(b);

            if (aCenterline != bCenterline)
                return aCenterline ? -1 : 1;

            return Vector2Int.Distance(a, aircraftCell).CompareTo(
                Vector2Int.Distance(b, aircraftCell)
            );
        });

        if (candidates.Count > maximumStartCandidates)
        {
            candidates.RemoveRange(
                maximumStartCandidates,
                candidates.Count - maximumStartCandidates
            );
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

        PlacedBuilding placed =
            runway.GetComponent<PlacedBuilding>();

        if (placed == null)
            placed = runway.GetComponentInParent<PlacedBuilding>();

        if (placed == null)
            placed = runway.GetComponentInChildren<PlacedBuilding>(true);

        if (placed == null)
            return false;

        bool runwayAxisIsX =
            Mathf.Abs(direction.x) >= Mathf.Abs(direction.z);

        int longitudinalCellCount =
            runwayAxisIsX ? placed.Width : placed.Depth;

        int bestThresholdOffset = int.MaxValue;
        float bestRouteLength = float.MaxValue;

        for (int offset = 0;
             offset < longitudinalCellCount;
             offset++)
        {
            Vector2Int sideCellA;
            Vector2Int sideCellB;

            if (runwayAxisIsX)
            {
                int x = direction.x > 0f
                    ? placed.Origin.x + offset
                    : placed.Origin.x + placed.Width - 1 - offset;

                sideCellA = new Vector2Int(x, placed.Origin.y - 1);
                sideCellB = new Vector2Int(x, placed.Origin.y + placed.Depth);
            }
            else
            {
                int z = direction.z > 0f
                    ? placed.Origin.y + offset
                    : placed.Origin.y + placed.Depth - 1 - offset;

                sideCellA = new Vector2Int(placed.Origin.x - 1, z);
                sideCellB = new Vector2Int(placed.Origin.x + placed.Width, z);
            }

            Vector2Int[] sideCells = { sideCellA, sideCellB };

            foreach (Vector2Int sideCell in sideCells)
            {
                if (!gridManager.IsInsideGrid(sideCell) ||
                    !taxiwayNetwork.IsTaxiway(sideCell))
                {
                    continue;
                }

                int searchRadius = Mathf.Max(
                    1,
                    taxiwayNetwork.GetTaxiwayWidth(sideCell)
                );

                Queue<Vector2Int> queue = new();
                HashSet<Vector2Int> visited = new();
                List<Vector2Int> nearestCenterlines = new();

                queue.Enqueue(sideCell);
                visited.Add(sideCell);

                int distance = 0;

                while (queue.Count > 0 && distance <= searchRadius)
                {
                    int cellsAtThisDistance = queue.Count;

                    for (int i = 0; i < cellsAtThisDistance; i++)
                    {
                        Vector2Int current = queue.Dequeue();

                        if (taxiwayNetwork.IsCenterlineCell(current))
                        {
                            nearestCenterlines.Add(current);
                            continue;
                        }

                        foreach (Vector2Int neighbour in
                                 taxiwayNetwork.GetConnectedTaxiwayCells(current))
                        {
                            if (visited.Add(neighbour))
                                queue.Enqueue(neighbour);
                        }
                    }

                    if (nearestCenterlines.Count > 0)
                        break;

                    distance++;
                }

                foreach (Vector2Int target in nearestCenterlines)
                {
                    int thresholdOffset = runwayAxisIsX
                        ? direction.x > 0f
                            ? target.x - placed.Origin.x
                            : placed.Origin.x + placed.Width - 1 - target.x
                        : direction.z > 0f
                            ? target.y - placed.Origin.y
                            : placed.Origin.y + placed.Depth - 1 - target.y;

                    if (thresholdOffset < 0)
                        continue;

                    foreach (Vector2Int start in startCandidates)
                    {
                        List<Vector2Int> path =
                            taxiwayPathfinder.FindPath(start, target);

                        if (path == null || path.Count == 0)
                            continue;

                        float routeLength = CalculateRouteLength(path);

                        Vector3 firstPoint =
                            gridManager.GridToWorldCenter(path[0]);

                        firstPoint.y = transform.position.y;

                        routeLength += Vector3.Distance(
                            transform.position,
                            firstPoint
                        );

                        bool isBetterCandidate =
                            thresholdOffset < bestThresholdOffset ||
                            (
                                thresholdOffset == bestThresholdOffset &&
                                routeLength < bestRouteLength
                            );

                        if (!isBetterCandidate)
                            continue;

                        bestThresholdOffset = thresholdOffset;
                        bestRouteLength = routeLength;
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
        out Vector2Int safeCell,
        out float aircraftExtent,
        out float requiredDistance,
        out float actualDistance)
    {
        safeRoute = null;
        safeCell = default;
        aircraftExtent = 0f;
        requiredDistance = 0f;
        actualDistance = 0f;

        if (approachRoute == null || approachRoute.Count < 2)
            return false;

        float accumulatedDistance = 0f;

        for (int i = approachRoute.Count - 2; i >= 0; i--)
        {
            Vector2Int currentCell = approachRoute[i];
            Vector2Int nextCell = approachRoute[i + 1];

            Vector3 currentWorld =
                gridManager.GridToWorldCenter(currentCell);

            Vector3 nextWorld =
                gridManager.GridToWorldCenter(nextCell);

            currentWorld.y = 0f;
            nextWorld.y = 0f;

            accumulatedDistance +=
                Vector3.Distance(currentWorld, nextWorld);

            if (!taxiwayNetwork.IsCenterlineCell(currentCell))
                continue;

            Vector3 towardRunway = nextWorld - currentWorld;
            towardRunway.y = 0f;

            if (towardRunway.sqrMagnitude < 0.0001f)
                continue;

            towardRunway.Normalize();

            float extent = GetAircraftForwardExtent(towardRunway);

            if (extent < 0f)
                return false;

            float required =
                extent +
                runwaySafetyMarginInCells * gridManager.CellSize;

            if (accumulatedDistance + 0.001f < required)
                continue;

            safeRoute = approachRoute.GetRange(0, i + 1);
            safeCell = currentCell;
            aircraftExtent = extent;
            requiredDistance = required;
            actualDistance = accumulatedDistance;

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

        Vector3 localCenter =
            transform.InverseTransformPoint(
                colliderTransform.TransformPoint(
                    aircraftCollider.center
                )
            );

        Vector3 localHalfX =
            transform.InverseTransformVector(
                colliderTransform.TransformVector(
                    Vector3.right * (aircraftCollider.size.x * 0.5f)
                )
            );

        Vector3 localHalfY =
            transform.InverseTransformVector(
                colliderTransform.TransformVector(
                    Vector3.up * (aircraftCollider.size.y * 0.5f)
                )
            );

        Vector3 localHalfZ =
            transform.InverseTransformVector(
                colliderTransform.TransformVector(
                    Vector3.forward * (aircraftCollider.size.z * 0.5f)
                )
            );

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
            Mathf.Abs(Vector3.Dot(halfZ, direction))
        );
    }

    private float CalculateRouteLength(List<Vector2Int> path)
    {
        if (path == null || path.Count < 2)
            return 0f;

        float length = 0f;

        for (int i = 1; i < path.Count; i++)
        {
            length +=
                Vector2Int.Distance(path[i - 1], path[i]) *
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

        float cruisingSpeed =
            taxiSpeedKnots * worldUnitsPerSecondPerKnot;

        AircraftTaxiTrafficFollowing trafficFollowing =
            GetComponent<AircraftTaxiTrafficFollowing>();

        float speed = trafficFollowing != null
            ? trafficFollowing.GetTrafficFollowingSpeed(cruisingSpeed)
            : cruisingSpeed;

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

        Vector3 planarDirection = toTarget;
        planarDirection.y = 0f;

        if (planarDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion rotation =
                Quaternion.LookRotation(
                    planarDirection.normalized,
                    Vector3.up
                ) * Quaternion.Euler(modelRotationOffset);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                rotation,
                rotationSpeedDegreesPerSecond * Time.deltaTime
            );
        }

        if (speed <= 0.001f)
            return;

        transform.position = Vector3.MoveTowards(
            transform.position,
            target,
            speed * Time.deltaTime
        );
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

    private void BuildSmoothedMovementWaypoints(
        List<Vector2Int> cells)
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
            Vector3 incoming =
                (points[i] - points[i - 1]).normalized;

            Vector3 outgoing =
                (points[i + 1] - points[i]).normalized;

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
            int nextTurn =
                t < turns.Count - 1 ? turns[t + 1] : points.Count - 1;

            float previousDistance =
                Vector3.Distance(points[previousTurn], points[index]);

            float nextDistance =
                Vector3.Distance(points[index], points[nextTurn]);

            float radius =
                turningRadiusInCells * gridManager.CellSize;

            radii[index] = Mathf.Min(
                radius,
                previousDistance * 0.5f,
                nextDistance * 0.5f
            );
        }

        const float bezierControlFactor = 0.55228475f;

        Vector3 current = points[0];
        AddMovementPoint(current);

        foreach (int index in turns)
        {
            float radius = radii[index];

            if (radius <= 0.01f)
                continue;

            Vector3 incoming =
                (points[index] - points[index - 1]).normalized;

            Vector3 outgoing =
                (points[index + 1] - points[index]).normalized;

            Vector3 entry =
                points[index] - incoming * radius;

            Vector3 exit =
                points[index] + outgoing * radius;

            AddStraightMovementPoints(current, entry);

            Vector3 control1 =
                entry + incoming * radius * bezierControlFactor;

            Vector3 control2 =
                exit - outgoing * radius * bezierControlFactor;

            for (int sample = 1;
                 sample <= turnCurveResolution;
                 sample++)
            {
                float t = sample / (float)turnCurveResolution;
                float oneMinusT = 1f - t;

                Vector3 point =
                    oneMinusT * oneMinusT * oneMinusT * entry +
                    3f * oneMinusT * oneMinusT * t * control1 +
                    3f * oneMinusT * t * t * control2 +
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

        float maximumStep =
            Mathf.Max(0.1f, gridManager.CellSize * 0.5f);

        int steps =
            Mathf.Max(1, Mathf.CeilToInt(distance / maximumStep));

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            AddMovementPoint(Vector3.Lerp(start, end, t));
        }
    }

    private void AddMovementPoint(Vector3 point)
    {
        if (movementWaypoints.Count > 0 &&
            Vector3.Distance(
                movementWaypoints[movementWaypoints.Count - 1],
                point
            ) < 0.01f)
        {
            return;
        }

        movementWaypoints.Add(point);
    }

    private void ArriveAtHoldingPoint()
    {
        currentState = DepartureState.AtHoldingPoint;
        retryTimer = 0f;

        debugStatus =
            $"At safe holding point for {debugSelectedRunway}. " +
            "Waiting for runway clearance.";

        Debug.Log(
            $"AIRCRAFT AT SAFE DEPARTURE HOLDING POINT | " +
            $"Aircraft: {name} | Runway: {debugSelectedRunway} | " +
            $"Holding cell: {holdingCell}",
            this
        );
    }

    private float CalculateSidewindComponentKnots(Vector3 direction)
    {
        Vector3 windFrom = weather.WindFromDirection;
        windFrom.y = 0f;
        direction.y = 0f;

        if (windFrom.sqrMagnitude < 0.0001f ||
            direction.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        windFrom.Normalize();
        direction.Normalize();

        float alignment =
            Mathf.Clamp01(
                Mathf.Abs(Vector3.Dot(direction, windFrom))
            );

        return weather.windSpeedKnots *
               Mathf.Sqrt(Mathf.Max(0f, 1f - alignment * alignment));
    }

    private string HeadingName(Vector3 direction)
    {
        float heading = Mathf.Repeat(
            Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
            360f
        );

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
            $"{name}: Departure taxi waiting. {message}",
            this
        );
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