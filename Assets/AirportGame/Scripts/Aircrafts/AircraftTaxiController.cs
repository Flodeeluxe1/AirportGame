
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class AircraftTaxiController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AircraftLandingController landingController;
    [SerializeField] private AircraftFlightIdentity flightIdentity;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;
    [SerializeField] private TaxiwayPathfinder taxiwayPathfinder;
    [SerializeField] private AircraftGateRegistry gateRegistry;

    [Header("Route Search")]
    [Min(1)]
    [SerializeField] private int connectionSearchRadiusCells = 12;

    [Range(1, 10)]
    [SerializeField] private int maximumConnectionCandidates = 4;

    [Min(1)]
    [SerializeField] private int maximumRouteAttempts = 16;

    [Header("Runway Exit")]
    [Tooltip("Maximum lateral distance from the runway centerline to consider an exit.")]
    [Min(1f)]
    [SerializeField] private float maximumRunwayExitLateralDistance = 12f;

    [Tooltip("Ignore exits too close behind or alongside the aircraft.")]
    [Min(0f)]
    [SerializeField] private float minimumExitForwardDistance = 0.5f;

    [Tooltip("Tolerance for a candidate exit near the runway end.")]
    [Min(0f)]
    [SerializeField] private float runwayEndTolerance = 1f;

    [Header("Taxi Movement")]
    [Tooltip("Target taxi speed, in knots.")]
    [Min(1f)]
    [SerializeField] private float taxiSpeedKnots = 15f;

    [Tooltip("Converts knots to Unity world units per second.")]
    [Min(0.001f)]
    [SerializeField] private float worldUnitsPerSecondPerKnot = 0.05f;

    [Tooltip("How quickly the aircraft rotates toward its travel direction.")]
    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 3f;

    [Tooltip("Arrival threshold for the runway exit and taxiway join.")]
    [Min(0.05f)]
    [SerializeField] private float waypointArrivalDistance = 0.2f;

    [Tooltip("Arrival threshold for the final gate approach.")]
    [Min(0.1f)]
    [SerializeField] private float gateArrivalDistance = 0.5f;

    [Tooltip("Height above the runway/grid surface while taxiing.")]
    [Min(0f)]
    [SerializeField] private float taxiHeightOffset = 0.05f;

    [Tooltip("Use this if the aircraft model's nose is not its local +Z axis.")]
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Smooth Taxi Turns")]
    [Tooltip("Desired curve radius as a fraction of one grid cell.")]
    [Range(0.1f, 3f)]
    [SerializeField] private float turningRadiusInCells = 2f;

    [Tooltip("Number of curve segments used to approximate each rounded corner.")]
    [Range(4, 16)]
    [SerializeField] private int turnCurveResolution = 8;

    [Tooltip("Distance tolerance for advancing through smoothed taxi path points.")]
    [Min(0.005f)]
    [SerializeField] private float taxiPathPointTolerance = 0.02f;

    [Header("Debug")]
    [SerializeField] private bool logRouteDetails = true;
    [SerializeField] private bool logTaxiProgress = true;
    [SerializeField] private bool drawDebugRoute = true;

    [TextArea(2, 5)]
    public string debugStatus = "Waiting for landing completion.";

    public string debugAssignedGate = "None";
    public Vector2Int debugStartCell;
    public Vector2Int debugTargetCell;
    public Vector2Int debugRunwayExitCell;
    public float debugRunwayExitDistance;
    public int debugRouteCells;
    public int debugCurrentWaypoint;
    public string debugTaxiPhase = "WaitingForLanding";

    [TextArea(3, 12)]
    public string debugLastPath = "No taxi route calculated.";

    private enum TaxiPhase
    {
        WaitingForLanding,
        RollingForwardOnRunway,
        JoiningTaxiway,
        FollowingTaxiway,
        ApproachingGate,
        Arrived,
        Failed
    }

    private readonly List<Vector2Int> taxiRoute =
        new List<Vector2Int>();

    private readonly List<Vector3> taxiMovementPath =
        new List<Vector3>();

    private TaxiPhase phase = TaxiPhase.WaitingForLanding;

    private bool routeAttempted;
    private bool isTaxiing;
    private bool hasArrivedAtGate;

    private int currentWaypointIndex;
    private float taxiWorldY;

    private AircraftGate assignedGate;
    private AircraftRunway selectedRunway;
    private AircraftTaxiTrafficFollowing trafficFollowing;

    private Vector3 landingDirection;
    private Vector3 runwayExitPoint;
    private Vector3 taxiwayJoinPoint;
    private Vector3 taxiStartPosition;

    public bool HasRoute => taxiRoute.Count > 0;
    public bool IsTaxiing => isTaxiing;
    public bool HasArrivedAtGate => hasArrivedAtGate;

    public IReadOnlyList<Vector2Int> Route => taxiRoute;

    private struct TaxiwayCandidate
    {
        public Vector2Int Cell;
        public float DistanceSquared;

        public TaxiwayCandidate(
            Vector2Int cell,
            float distanceSquared)
        {
            Cell = cell;
            DistanceSquared = distanceSquared;
        }
    }

    private struct RunwayExitCandidate
    {
        public Vector2Int Cell;
        public float ForwardDistance;
        public float LateralDistance;
        public int Side;

        public RunwayExitCandidate(
            Vector2Int cell,
            float forwardDistance,
            float lateralDistance,
            int side)
        {
            Cell = cell;
            ForwardDistance = forwardDistance;
            LateralDistance = lateralDistance;
            Side = side;
        }
    }

    private struct RouteCandidatePair
    {
        public TaxiwayCandidate Start;
        public TaxiwayCandidate Target;
        public float CombinedDistanceSquared;

        public RouteCandidatePair(
            TaxiwayCandidate start,
            TaxiwayCandidate target)
        {
            Start = start;
            Target = target;

            CombinedDistanceSquared =
                start.DistanceSquared + target.DistanceSquared;
        }
    }

    private void Awake()
    {
        if (landingController == null)
            landingController = GetComponent<AircraftLandingController>();

        if (flightIdentity == null)
            flightIdentity = GetComponent<AircraftFlightIdentity>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (taxiwayPathfinder == null)
            taxiwayPathfinder = FindFirstObjectByType<TaxiwayPathfinder>();

        if (taxiwayNetwork == null && taxiwayPathfinder != null)
            taxiwayNetwork = taxiwayPathfinder.GetComponent<TaxiwayNetwork>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindFirstObjectByType<TaxiwayNetwork>();

        if (gateRegistry == null)
            gateRegistry = FindFirstObjectByType<AircraftGateRegistry>();

        // Shared component handles all aircraft traffic detection and
        // speed adjustment for arriving aircraft.
        trafficFollowing =
            GetComponent<AircraftTaxiTrafficFollowing>();
    }


    private void Update()
    {
        // Hobby aircraft use their own taxi controller.
        // Keep the existing gate-based taxi system exclusive
        // to scheduled airline flights.
        if (flightIdentity != null &&
            flightIdentity.IsHobbyFlight)
        {
            debugTaxiPhase = "HobbyTaxiController";
            return;
        }

        if (!routeAttempted &&
            landingController != null &&
            landingController.IsLandingComplete)
        {
            routeAttempted = true;
            CalculateTaxiRoute();
        }

        if (isTaxiing)
            UpdateTaxiMovement();

        debugTaxiPhase = phase.ToString();
    }


    private void CalculateTaxiRoute()
    {
        taxiRoute.Clear();
        taxiMovementPath.Clear();

        if (flightIdentity == null || !flightIdentity.IsInitialized)
        {
            SetFailure(
                "Taxi routing requires an initialized AircraftFlightIdentity."
            );
            return;
        }

        if (landingController == null ||
            gridManager == null ||
            taxiwayNetwork == null ||
            taxiwayPathfinder == null ||
            gateRegistry == null)
        {
            SetFailure(
                "Taxi routing references are incomplete. " +
                "Check AircraftLandingController, GridManager, " +
                "TaxiwayNetwork, TaxiwayPathfinder, and AircraftGateRegistry."
            );
            return;
        }

        GateAssignmentController assignmentController =
            GateAssignmentController.Instance;

        if (assignmentController == null)
        {
            SetFailure("GateAssignmentController was not found.");
            return;
        }

        selectedRunway = landingController.SelectedRunway;
        landingDirection = landingController.LandingDirection;
        landingDirection.y = 0f;

        if (selectedRunway == null ||
            landingDirection.sqrMagnitude < 0.0001f)
        {
            SetFailure(
                "The selected runway or landing direction is invalid."
            );
            return;
        }

        landingDirection.Normalize();

        if (!selectedRunway.TryGetGeometry(
                gridManager,
                out Vector3 runwayCenter,
                out Vector3 runwayAxis,
                out float runwayLength,
                out float runwaySurfaceY))
        {
            SetFailure(
                "Could not retrieve the selected runway's geometry."
            );
            return;
        }

        if (!assignmentController.TryAssignGate(
                flightIdentity,
                out AircraftGate gate,
                out string assignmentMessage))
        {
            SetFailure(
                $"Could not obtain a gate assignment: {assignmentMessage}"
            );
            return;
        }

        if (gate == null ||
            string.IsNullOrEmpty(flightIdentity.ActualGateStableId))
        {
            SetFailure(
                "Gate assignment succeeded without a valid actual gate."
            );
            return;
        }

        assignedGate = gate;
        debugAssignedGate = assignedGate.DisplayName;

        int minimumTaxiwayWidth =
            GetMinimumTaxiwayWidth(
                flightIdentity.Definition.RequiredGateSize
            );

        taxiWorldY = runwaySurfaceY + taxiHeightOffset;

        Vector3 aircraftPosition = transform.position;
        aircraftPosition.y = runwaySurfaceY;

        taxiStartPosition = aircraftPosition;

        Vector3 runwayEndInLandingDirection =
            runwayCenter +
            landingDirection * (runwayLength * 0.5f);

        float remainingRunwayDistance =
            Vector3.Dot(
                runwayEndInLandingDirection - aircraftPosition,
                landingDirection
            );

        if (remainingRunwayDistance <= 0f)
        {
            SetFailure(
                "The aircraft is at or beyond the runway end; " +
                "no forward runway exit can be selected."
            );
            return;
        }

        List<RunwayExitCandidate> exitCandidates =
            FindForwardRunwayExitCandidates(
                aircraftPosition,
                landingDirection,
                remainingRunwayDistance,
                minimumTaxiwayWidth
            );

        if (exitCandidates.Count == 0)
        {
            SetFailure(
                $"No suitable taxiway exit was found ahead of the aircraft " +
                $"on runway {selectedRunway.name}."
            );
            return;
        }

        List<TaxiwayCandidate> targetCandidates =
            FindNearbyCenterlineCells(
                assignedGate.transform.position,
                minimumTaxiwayWidth
            );

        if (targetCandidates.Count == 0)
        {
            SetFailure(
                $"No suitable taxiway centerline was found near " +
                $"{assignedGate.DisplayName}."
            );
            return;
        }

        List<RouteCandidatePair> pairs =
            BuildCandidatePairsFromExits(
                exitCandidates,
                targetCandidates
            );

        int attempts = 0;
        bool routeFound = false;

        RunwayExitCandidate selectedExit = default;
        Vector2Int selectedTarget = default;

        foreach (RouteCandidatePair pair in pairs)
        {
            if (attempts >= maximumRouteAttempts)
                break;

            attempts++;

            List<Vector2Int> path =
                taxiwayPathfinder.FindPath(
                    pair.Start.Cell,
                    pair.Target.Cell,
                    minimumTaxiwayWidth
                );

            if (path == null || path.Count == 0)
                continue;

            selectedExit = FindExitCandidate(
                exitCandidates,
                pair.Start.Cell
            );

            selectedTarget = pair.Target.Cell;

            taxiRoute.AddRange(path);
            routeFound = true;
            break;
        }

        if (!routeFound)
        {
            SetFailure(
                $"No connected A* route was found from the candidate " +
                $"runway exits to {assignedGate.DisplayName}. " +
                $"Tried {attempts} routes."
            );
            return;
        }

        Vector3 exitCellWorldPosition =
            gridManager.GridToWorldCenter(selectedExit.Cell);

        exitCellWorldPosition.y = taxiWorldY;

        runwayExitPoint =
            aircraftPosition +
            landingDirection * selectedExit.ForwardDistance;

        runwayExitPoint.y = taxiWorldY;

        taxiwayJoinPoint = exitCellWorldPosition;

        debugRunwayExitCell = selectedExit.Cell;
        debugRunwayExitDistance = selectedExit.ForwardDistance;
        debugStartCell = selectedExit.Cell;
        debugTargetCell = selectedTarget;
        debugRouteCells = taxiRoute.Count;
        debugCurrentWaypoint = 0;
        debugLastPath = FormatPath(taxiRoute);

        BuildSmoothedTaxiMovementPath();

        if (taxiMovementPath.Count == 0)
        {
            SetFailure(
                "The A* path was found, but the taxi movement path is empty."
            );
            return;
        }

        currentWaypointIndex = 0;
        hasArrivedAtGate = false;
        isTaxiing = true;
        phase = TaxiPhase.RollingForwardOnRunway;

        Vector3 adjustedAircraftPosition = transform.position;
        adjustedAircraftPosition.y = taxiWorldY;
        transform.position = adjustedAircraftPosition;

        debugStatus =
            $"Continuing forward on the runway for " +
            $"{debugRunwayExitDistance:F1} units toward exit " +
            $"{debugRunwayExitCell}, then taxiing to " +
            $"{assignedGate.DisplayName}.";

        if (logRouteDetails)
        {
            Debug.Log(
                $"AIRCRAFT TAXI ROUTE FOUND | " +
                $"Flight: {flightIdentity.FlightNumber} | " +
                $"Planned Gate: {flightIdentity.PlannedGateStableId} | " +
                $"Actual Gate: {flightIdentity.ActualGateStableId} | " +
                $"Runway: {selectedRunway.name} | " +
                $"Landing Direction: {landingDirection} | " +
                $"Exit Cell: {debugRunwayExitCell} | " +
                $"Forward Distance: {debugRunwayExitDistance:F1} | " +
                $"Path Cells: {debugRouteCells} | " +
                $"Smoothed Points: {taxiMovementPath.Count}\n" +
                debugLastPath,
                this
            );
        }

        if (logTaxiProgress)
        {
            Debug.Log(
                $"AIRCRAFT TAXI STARTED | " +
                $"Flight: {flightIdentity.FlightNumber} | " +
                $"Runway: {selectedRunway.name} | " +
                $"Next Exit: {debugRunwayExitCell} | " +
                $"Gate: {assignedGate.DisplayName} | " +
                $"Speed: {taxiSpeedKnots:F1} knots",
                this
            );
        }
    }

    private void BuildSmoothedTaxiMovementPath()
    {
        taxiMovementPath.Clear();

        if (taxiRoute.Count == 0)
            return;

        List<Vector3> gridPoints = new List<Vector3>();

        foreach (Vector2Int cell in taxiRoute)
        {
            Vector3 point = gridManager.GridToWorldCenter(cell);
            point.y = taxiWorldY;
            gridPoints.Add(point);
        }

        if (gridPoints.Count == 1)
        {
            taxiMovementPath.Add(gridPoints[0]);
            return;
        }

        float cellSize = Mathf.Max(0.001f, gridManager.CellSize);
        float desiredRadius = turningRadiusInCells * cellSize;
        int pointCount = gridPoints.Count;

        Vector3[] incomingDirections = new Vector3[pointCount];
        Vector3[] outgoingDirections = new Vector3[pointCount];
        float[] cornerRadii = new float[pointCount];

        List<int> turnIndices = new List<int>();

        for (int i = 1; i < pointCount - 1; i++)
        {
            Vector3 incoming = gridPoints[i] - gridPoints[i - 1];
            Vector3 outgoing = gridPoints[i + 1] - gridPoints[i];

            incoming.y = 0f;
            outgoing.y = 0f;

            if (incoming.sqrMagnitude < 0.0001f ||
                outgoing.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            incoming.Normalize();
            outgoing.Normalize();

            float directionDot = Vector3.Dot(incoming, outgoing);

            if (directionDot > 0.99f)
                continue;

            turnIndices.Add(i);

            incomingDirections[i] = incoming;
            outgoingDirections[i] = outgoing;

            if (Mathf.Abs(directionDot) <= 0.01f)
                cornerRadii[i] = desiredRadius;
            else
                cornerRadii[i] = 0f;
        }

        for (int turnIndex = 0;
             turnIndex < turnIndices.Count;
             turnIndex++)
        {
            int cornerIndex = turnIndices[turnIndex];

            if (cornerRadii[cornerIndex] <= 0f)
                continue;

            float incomingAvailable;

            if (turnIndex > 0)
            {
                int previousCornerIndex =
                    turnIndices[turnIndex - 1];

                incomingAvailable =
                    Vector3.Distance(
                        gridPoints[previousCornerIndex],
                        gridPoints[cornerIndex]
                    ) * 0.5f;
            }
            else
            {
                incomingAvailable =
                    Vector3.Distance(
                        gridPoints[0],
                        gridPoints[cornerIndex]
                    );
            }

            float outgoingAvailable;

            if (turnIndex < turnIndices.Count - 1)
            {
                int nextCornerIndex =
                    turnIndices[turnIndex + 1];

                outgoingAvailable =
                    Vector3.Distance(
                        gridPoints[cornerIndex],
                        gridPoints[nextCornerIndex]
                    ) * 0.5f;
            }
            else
            {
                outgoingAvailable =
                    Vector3.Distance(
                        gridPoints[cornerIndex],
                        gridPoints[pointCount - 1]
                    );
            }

            cornerRadii[cornerIndex] = Mathf.Min(
                desiredRadius,
                incomingAvailable,
                outgoingAvailable
            );
        }

        taxiMovementPath.Add(gridPoints[0]);

        const float BezierCircleFactor = 0.55228475f;

        for (int turnIndex = 0;
             turnIndex < turnIndices.Count;
             turnIndex++)
        {
            int cornerIndex = turnIndices[turnIndex];

            Vector3 corner = gridPoints[cornerIndex];
            float radius = cornerRadii[cornerIndex];

            if (radius < 0.01f)
            {
                AddMovementPoint(corner);
                continue;
            }

            Vector3 incomingDirection =
                incomingDirections[cornerIndex];

            Vector3 outgoingDirection =
                outgoingDirections[cornerIndex];

            Vector3 entryPoint =
                corner - incomingDirection * radius;

            Vector3 exitPoint =
                corner + outgoingDirection * radius;

            entryPoint.y = taxiWorldY;
            exitPoint.y = taxiWorldY;

            AddMovementPoint(entryPoint);

            Vector3 controlPoint1 =
                entryPoint +
                incomingDirection *
                (radius * BezierCircleFactor);

            Vector3 controlPoint2 =
                exitPoint -
                outgoingDirection *
                (radius * BezierCircleFactor);

            for (int sample = 1;
                 sample <= turnCurveResolution;
                 sample++)
            {
                float t = sample / (float)turnCurveResolution;

                Vector3 curvePoint = EvaluateCubicBezier(
                    entryPoint,
                    controlPoint1,
                    controlPoint2,
                    exitPoint,
                    t
                );

                curvePoint.y = taxiWorldY;
                AddMovementPoint(curvePoint);
            }
        }

        AddMovementPoint(gridPoints[pointCount - 1]);
    }

    private void AddMovementPoint(Vector3 point)
    {
        point.y = taxiWorldY;

        if (taxiMovementPath.Count > 0)
        {
            Vector3 previous =
                taxiMovementPath[taxiMovementPath.Count - 1];

            if ((previous - point).sqrMagnitude < 0.000001f)
                return;
        }

        taxiMovementPath.Add(point);
    }

    private Vector3 EvaluateCubicBezier(
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        float t)
    {
        float inverseT = 1f - t;

        return
            inverseT * inverseT * inverseT * p0 +
            3f * inverseT * inverseT * t * p1 +
            3f * inverseT * t * t * p2 +
            t * t * t * p3;
    }

    private List<RunwayExitCandidate> FindForwardRunwayExitCandidates(
        Vector3 aircraftPosition,
        Vector3 runwayDirection,
        float remainingRunwayDistance,
        int minimumTaxiwayWidth)
    {
        List<RunwayExitCandidate> candidates =
            new List<RunwayExitCandidate>();

        if (selectedRunway == null)
            return candidates;

        PlacedBuilding runwayBuilding =
            selectedRunway.GetComponent<PlacedBuilding>();

        if (runwayBuilding == null)
        {
            Debug.LogWarning(
                $"Cannot validate taxiway exits: runway " +
                $"{selectedRunway.name} has no PlacedBuilding component.",
                selectedRunway
            );

            return candidates;
        }

        Vector2Int origin = runwayBuilding.Origin;

        int minX = origin.x;
        int maxX = origin.x + runwayBuilding.Width - 1;
        int minZ = origin.y;
        int maxZ = origin.y + runwayBuilding.Depth - 1;

        bool runwayAlongX =
            runwayBuilding.Width >= runwayBuilding.Depth;

        Vector3 perpendicularDirection =
            new Vector3(
                -runwayDirection.z,
                0f,
                runwayDirection.x
            );

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);

                if (!taxiwayNetwork.IsTaxiway(cell) ||
                    !taxiwayNetwork.IsCenterlineCell(cell))
                {
                    continue;
                }

                if (taxiwayNetwork.GetTaxiwayWidth(cell) <
                    minimumTaxiwayWidth)
                {
                    continue;
                }

                bool connectedExit = false;

                if (runwayAlongX)
                {
                    if (cell.x < minX || cell.x > maxX)
                        continue;

                    if (cell.y == maxZ + 1 &&
                        taxiwayNetwork.IsCenterlineCell(
                            new Vector2Int(cell.x, cell.y + 1)))
                    {
                        connectedExit = true;
                    }

                    if (cell.y == minZ - 1 &&
                        taxiwayNetwork.IsCenterlineCell(
                            new Vector2Int(cell.x, cell.y - 1)))
                    {
                        connectedExit = true;
                    }
                }
                else
                {
                    if (cell.y < minZ || cell.y > maxZ)
                        continue;

                    if (cell.x == maxX + 1 &&
                        taxiwayNetwork.IsCenterlineCell(
                            new Vector2Int(cell.x + 1, cell.y)))
                    {
                        connectedExit = true;
                    }

                    if (cell.x == minX - 1 &&
                        taxiwayNetwork.IsCenterlineCell(
                            new Vector2Int(cell.x - 1, cell.y)))
                    {
                        connectedExit = true;
                    }
                }

                if (!connectedExit)
                    continue;

                Vector3 cellWorldPosition =
                    gridManager.GridToWorldCenter(cell);

                Vector3 delta = cellWorldPosition - aircraftPosition;
                delta.y = 0f;

                float forwardDistance =
                    Vector3.Dot(delta, runwayDirection);

                if (forwardDistance < minimumExitForwardDistance)
                    continue;

                if (forwardDistance >
                    remainingRunwayDistance + runwayEndTolerance)
                {
                    continue;
                }

                Vector3 lateralOffset =
                    delta - runwayDirection * forwardDistance;

                lateralOffset.y = 0f;

                float lateralDistance = lateralOffset.magnitude;

                if (lateralDistance >
                    maximumRunwayExitLateralDistance)
                {
                    continue;
                }

                float sideValue =
                    Vector3.Dot(
                        lateralOffset,
                        perpendicularDirection
                    );

                int side = sideValue >= 0f ? 1 : -1;

                candidates.Add(
                    new RunwayExitCandidate(
                        cell,
                        forwardDistance,
                        lateralDistance,
                        side
                    )
                );
            }
        }

        candidates.Sort((a, b) =>
        {
            if (!Mathf.Approximately(
                    a.ForwardDistance,
                    b.ForwardDistance))
            {
                return a.ForwardDistance.CompareTo(
                    b.ForwardDistance
                );
            }

            return a.LateralDistance.CompareTo(
                b.LateralDistance
            );
        });

        List<RunwayExitCandidate> uniqueCandidates =
            new List<RunwayExitCandidate>();

        HashSet<long> usedExitStations =
            new HashSet<long>();

        float cellSize = Mathf.Max(0.001f, gridManager.CellSize);

        foreach (RunwayExitCandidate candidate in candidates)
        {
            int station = Mathf.RoundToInt(
                candidate.ForwardDistance / cellSize
            );

            long stationKey =
                (long)station * 2L +
                (candidate.Side > 0 ? 1L : 0L);

            if (!usedExitStations.Add(stationKey))
                continue;

            uniqueCandidates.Add(candidate);
        }

        return uniqueCandidates;
    }

    private RunwayExitCandidate FindExitCandidate(
        List<RunwayExitCandidate> candidates,
        Vector2Int cell)
    {
        foreach (RunwayExitCandidate candidate in candidates)
        {
            if (candidate.Cell == cell)
                return candidate;
        }

        return default;
    }

    private List<RouteCandidatePair> BuildCandidatePairsFromExits(
        List<RunwayExitCandidate> exitCandidates,
        List<TaxiwayCandidate> targetCandidates)
    {
        List<RouteCandidatePair> pairs =
            new List<RouteCandidatePair>();

        foreach (RunwayExitCandidate exit in exitCandidates)
        {
            TaxiwayCandidate start = new TaxiwayCandidate(
                exit.Cell,
                exit.ForwardDistance
            );

            foreach (TaxiwayCandidate target in targetCandidates)
                pairs.Add(new RouteCandidatePair(start, target));
        }

        pairs.Sort((a, b) =>
        {
            int comparison =
                a.Start.DistanceSquared.CompareTo(
                    b.Start.DistanceSquared
                );

            if (comparison != 0)
                return comparison;

            return a.Target.DistanceSquared.CompareTo(
                b.Target.DistanceSquared
            );
        });

        return pairs;
    }

    private void UpdateTaxiMovement()
    {
        if (assignedGate == null)
        {
            StopTaxiing(
                "Taxiing stopped because the assigned gate is missing."
            );
            return;
        }

        if (taxiRoute.Count == 0 ||
            taxiMovementPath.Count == 0)
        {
            StopTaxiing(
                "Taxiing stopped because the route is missing."
            );
            return;
        }

        switch (phase)
        {
            case TaxiPhase.RollingForwardOnRunway:
                UpdateRunwayExitApproach();
                break;

            case TaxiPhase.JoiningTaxiway:
                UpdateTaxiwayJoin();
                break;

            case TaxiPhase.FollowingTaxiway:
                UpdateTaxiwayRoute();
                break;

            case TaxiPhase.ApproachingGate:
                UpdateFinalGateApproach();
                break;
        }
    }

    private void UpdateRunwayExitApproach()
    {
        if (HorizontalDistance(
                transform.position,
                runwayExitPoint) <= waypointArrivalDistance)
        {
            SetPositionAt(runwayExitPoint);
            phase = TaxiPhase.JoiningTaxiway;

            debugStatus =
                $"Reached the runway exit. Turning onto taxiway " +
                $"{debugRunwayExitCell}.";

            if (logTaxiProgress)
            {
                Debug.Log(
                    $"AIRCRAFT REACHED RUNWAY EXIT | " +
                    $"Flight: {flightIdentity.FlightNumber} | " +
                    $"Exit Cell: {debugRunwayExitCell}",
                    this
                );
            }

            return;
        }

        MoveTowardWaypoint(runwayExitPoint);
    }

    private void UpdateTaxiwayJoin()
    {
        if (HorizontalDistance(
                transform.position,
                taxiwayJoinPoint) <= waypointArrivalDistance)
        {
            SetPositionAt(taxiwayJoinPoint);

            currentWaypointIndex = 1;
            debugCurrentWaypoint = currentWaypointIndex;

            phase = TaxiPhase.FollowingTaxiway;

            debugStatus =
                $"Joined taxiway. Following smoothed A* route to " +
                $"{assignedGate.DisplayName}.";

            return;
        }

        MoveTowardWaypoint(taxiwayJoinPoint);
    }

    private void UpdateTaxiwayRoute()
    {
        while (currentWaypointIndex < taxiMovementPath.Count)
        {
            Vector3 waypoint =
                taxiMovementPath[currentWaypointIndex];

            if (HorizontalDistance(
                    transform.position,
                    waypoint) <= taxiPathPointTolerance)
            {
                currentWaypointIndex++;
                debugCurrentWaypoint = currentWaypointIndex;
                continue;
            }

            MoveTowardWaypoint(waypoint);
            return;
        }

        phase = TaxiPhase.ApproachingGate;

        debugStatus =
            $"Taxiway route complete. Approaching " +
            $"{assignedGate.DisplayName}.";
    }

    private void UpdateFinalGateApproach()
    {
        Vector3 gateTarget = assignedGate.transform.position;
        gateTarget.y = taxiWorldY;

        if (HorizontalDistance(
                transform.position,
                gateTarget) <= gateArrivalDistance)
        {
            FinishTaxiing(gateTarget);
            return;
        }

        MoveTowardWaypoint(gateTarget);
    }

    private void MoveTowardWaypoint(Vector3 target)
    {
        Vector3 movement = target - transform.position;
        movement.y = 0f;

        if (movement.sqrMagnitude > 0.0001f)
            FaceMovementDirection(movement);

        float taxiSpeed =
            taxiSpeedKnots * worldUnitsPerSecondPerKnot;

        // Delegate aircraft detection and speed adjustment to the shared
        // traffic-following component used by departing aircraft.
        if (trafficFollowing != null)
        {
            taxiSpeed =
                trafficFollowing.GetTrafficFollowingSpeed(taxiSpeed);
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            target,
            taxiSpeed * Time.deltaTime
        );
    }

    private void FaceMovementDirection(Vector3 movement)
    {
        movement.y = 0f;

        if (movement.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(
                movement.normalized,
                Vector3.up
            );

        targetRotation *= Quaternion.Euler(modelRotationOffset);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    private void SetPositionAt(Vector3 target)
    {
        Vector3 position = target;
        position.y = taxiWorldY;
        transform.position = position;
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;

        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private void FinishTaxiing(Vector3 gateTarget)
    {
        SetPositionAt(gateTarget);

        isTaxiing = false;
        hasArrivedAtGate = true;

        phase = TaxiPhase.Arrived;
        debugCurrentWaypoint = taxiMovementPath.Count;
        debugTaxiPhase = phase.ToString();

        debugStatus =
            $"Arrived at {assignedGate.DisplayName}. " +
            "Aircraft is at its assigned gate.";

        if (logTaxiProgress)
        {
            Debug.Log(
                $"AIRCRAFT ARRIVED AT GATE | " +
                $"Flight: {flightIdentity.FlightNumber} | " +
                $"Planned Gate: {flightIdentity.PlannedGateStableId} | " +
                $"Actual Gate: {flightIdentity.ActualGateStableId} | " +
                $"Gate: {assignedGate.DisplayName}",
                this
            );
        }

        GetComponent<AircraftTurnaroundController>()?.BeginTurnaround();
    }

    private void StopTaxiing(string reason)
    {
        isTaxiing = false;
        hasArrivedAtGate = false;

        phase = TaxiPhase.Failed;
        debugTaxiPhase = phase.ToString();
        debugStatus = reason;

        Debug.LogWarning(
            $"AIRCRAFT TAXI STOPPED | " +
            $"{(flightIdentity != null ? flightIdentity.FlightNumber : "Unknown flight")} | " +
            reason,
            this
        );
    }

    private int GetMinimumTaxiwayWidth(
        AircraftGateSize requiredGateSize)
    {
        switch (requiredGateSize)
        {
            case AircraftGateSize.Grass:
                return 3;

            case AircraftGateSize.Small:
                return 3;

            case AircraftGateSize.Middle:
                return 5;

            case AircraftGateSize.Big:
                return 7;

            default:
                return 3;
        }
    }

    private List<TaxiwayCandidate> FindNearbyCenterlineCells(
        Vector3 worldPosition,
        int minimumTaxiwayWidth)
    {
        List<TaxiwayCandidate> candidates =
            new List<TaxiwayCandidate>();

        Vector2Int center =
            gridManager.WorldToGrid(worldPosition);

        int radius = connectionSearchRadiusCells;

        for (int xOffset = -radius; xOffset <= radius; xOffset++)
        {
            for (int zOffset = -radius; zOffset <= radius; zOffset++)
            {
                Vector2Int cell = new Vector2Int(
                    center.x + xOffset,
                    center.y + zOffset
                );

                if (!gridManager.IsInsideGrid(cell))
                    continue;

                if (!taxiwayNetwork.IsTaxiway(cell))
                    continue;

                if (!taxiwayNetwork.IsCenterlineCell(cell))
                    continue;

                if (taxiwayNetwork.GetTaxiwayWidth(cell) <
                    minimumTaxiwayWidth)
                {
                    continue;
                }

                Vector3 cellWorldPosition =
                    gridManager.GridToWorldCenter(cell);

                float distanceSquared =
                    HorizontalDistanceSquared(
                        worldPosition,
                        cellWorldPosition
                    );

                candidates.Add(
                    new TaxiwayCandidate(
                        cell,
                        distanceSquared
                    )
                );
            }
        }

        candidates.Sort(
            (a, b) =>
                a.DistanceSquared.CompareTo(b.DistanceSquared)
        );

        if (candidates.Count > maximumConnectionCandidates)
        {
            candidates.RemoveRange(
                maximumConnectionCandidates,
                candidates.Count - maximumConnectionCandidates
            );
        }

        return candidates;
    }

    private List<RouteCandidatePair> BuildCandidatePairs(
        List<TaxiwayCandidate> startCandidates,
        List<TaxiwayCandidate> targetCandidates)
    {
        List<RouteCandidatePair> pairs =
            new List<RouteCandidatePair>();

        foreach (TaxiwayCandidate start in startCandidates)
        {
            foreach (TaxiwayCandidate target in targetCandidates)
                pairs.Add(new RouteCandidatePair(start, target));
        }

        pairs.Sort(
            (a, b) =>
                a.CombinedDistanceSquared.CompareTo(
                    b.CombinedDistanceSquared
                )
        );

        return pairs;
    }

    private float HorizontalDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;

        return dx * dx + dz * dz;
    }

    private string FormatPath(List<Vector2Int> path)
    {
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0)
                builder.Append(" -> ");

            builder.Append(path[i]);
        }

        return builder.ToString();
    }

    private void SetFailure(string message)
    {
        debugStatus = message;
        debugLastPath = "NO ROUTE FOUND";
        debugRouteCells = 0;
        debugCurrentWaypoint = 0;

        taxiRoute.Clear();
        taxiMovementPath.Clear();

        isTaxiing = false;
        hasArrivedAtGate = false;

        phase = TaxiPhase.Failed;
        debugTaxiPhase = phase.ToString();

        Debug.LogWarning(
            $"AIRCRAFT TAXI ROUTE FAILED | " +
            $"{(flightIdentity != null ? flightIdentity.FlightNumber : "Unknown flight")} | " +
            message,
            this
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugRoute || gridManager == null)
            return;

        if (routeAttempted &&
            phase != TaxiPhase.Failed &&
            phase != TaxiPhase.WaitingForLanding)
        {
            Gizmos.color = Color.yellow;

            Gizmos.DrawSphere(runwayExitPoint, 0.25f);
            Gizmos.DrawLine(taxiStartPosition, runwayExitPoint);
            Gizmos.DrawLine(runwayExitPoint, taxiwayJoinPoint);
        }

        if (taxiRoute == null || taxiRoute.Count == 0)
            return;

        Gizmos.color = new Color(0.2f, 0.55f, 1f, 0.75f);

        foreach (Vector2Int cell in taxiRoute)
        {
            Vector3 point = gridManager.GridToWorldCenter(cell);
            point.y = taxiWorldY;
            Gizmos.DrawSphere(point, 0.08f);
        }

        Gizmos.color = Color.cyan;

        for (int i = 0; i < taxiMovementPath.Count; i++)
        {
            Vector3 point = taxiMovementPath[i];

            Gizmos.DrawSphere(point, 0.035f);

            if (i > 0)
                Gizmos.DrawLine(taxiMovementPath[i - 1], point);
        }

        if (assignedGate != null && taxiMovementPath.Count > 0)
        {
            Vector3 gatePosition = assignedGate.transform.position;
            gatePosition.y = taxiWorldY;

            Gizmos.DrawLine(
                taxiMovementPath[taxiMovementPath.Count - 1],
                gatePosition
            );

            Gizmos.DrawWireSphere(gatePosition, 0.35f);
        }
    }
}
