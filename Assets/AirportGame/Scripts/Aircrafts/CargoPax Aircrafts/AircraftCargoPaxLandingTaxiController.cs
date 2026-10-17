
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class AircraftCargoPaxLandingTaxiController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AircraftCargoPaxLandingController landingController;
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
    [Min(1f)]
    [SerializeField] private float maximumRunwayExitLateralDistance = 12f;

    [Min(0f)]
    [SerializeField] private float minimumExitForwardDistance = 0.5f;

    [Min(0f)]
    [SerializeField] private float runwayEndTolerance = 1f;

    [Header("Taxi Movement")]
    [Min(1f)]
    [SerializeField] private float taxiSpeedKnots = 15f;

    [Min(0.001f)]
    [SerializeField] private float worldUnitsPerSecondPerKnot = 0.05f;

    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 3f;

    [Min(0.05f)]
    [SerializeField] private float waypointArrivalDistance = 0.2f;

    [Min(0.1f)]
    [SerializeField] private float gateArrivalDistance = 0.5f;

    [Min(0f)]
    [SerializeField] private float taxiHeightOffset = 0.05f;

    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Smooth Taxi Turns")]
    [Range(0.1f, 3f)]
    [SerializeField] private float turningRadiusInCells = 2f;

    [Range(4, 16)]
    [SerializeField] private int turnCurveResolution = 8;

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

        public TaxiwayCandidate(Vector2Int cell, float distanceSquared)
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
        public RunwayExitCandidate Exit;
        public TaxiwayCandidate Target;

        public RouteCandidatePair(
            RunwayExitCandidate exit,
            TaxiwayCandidate target)
        {
            Exit = exit;
            Target = target;
        }
    }

    private void Awake()
    {
        if (landingController == null)
            landingController =
                GetComponent<AircraftCargoPaxLandingController>();

        if (flightIdentity == null)
            flightIdentity = GetComponent<AircraftFlightIdentity>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (taxiwayPathfinder == null)
            taxiwayPathfinder = FindFirstObjectByType<TaxiwayPathfinder>();

        if (taxiwayNetwork == null && taxiwayPathfinder != null)
            taxiwayNetwork =
                taxiwayPathfinder.GetComponent<TaxiwayNetwork>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindFirstObjectByType<TaxiwayNetwork>();

        if (gateRegistry == null)
            gateRegistry = FindFirstObjectByType<AircraftGateRegistry>();

        trafficFollowing = GetComponent<AircraftTaxiTrafficFollowing>();
    }


    private void Update()
    {
        AircraftStatus status = GetComponent<AircraftStatus>();

        if (status != null)
        {
            status.SetTaxiing(isTaxiing);

            if (hasArrivedAtGate)
                status.SetParked(true);
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
                "Taxi routing requires an initialized AircraftFlightIdentity.");
            return;
        }

        if (landingController == null ||
            gridManager == null ||
            taxiwayNetwork == null ||
            taxiwayPathfinder == null ||
            gateRegistry == null)
        {
            SetFailure(
                "Taxi routing references are incomplete. Check " +
                "AircraftCargoPaxLandingController, GridManager, " +
                "TaxiwayNetwork, TaxiwayPathfinder and AircraftGateRegistry.");
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
            SetFailure("The selected runway or landing direction is invalid.");
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
            SetFailure("Could not retrieve the selected runway geometry.");
            return;
        }

        if (!assignmentController.TryAssignGate(
                flightIdentity,
                out AircraftGate gate,
                out string assignmentMessage))
        {
            SetFailure(
                $"Could not obtain a gate assignment: {assignmentMessage}");
            return;
        }

        if (gate == null ||
            string.IsNullOrEmpty(flightIdentity.ActualGateStableId))
        {
            SetFailure(
                "Gate assignment succeeded without a valid actual gate.");
            return;
        }

        assignedGate = gate;
        debugAssignedGate = assignedGate.DisplayName;

        int minimumTaxiwayWidth =
            GetMinimumTaxiwayWidth(
                flightIdentity.Definition.RequiredGateSize);

        taxiWorldY = runwaySurfaceY + taxiHeightOffset;

        Vector3 aircraftPosition = transform.position;
        aircraftPosition.y = runwaySurfaceY;
        taxiStartPosition = aircraftPosition;

        Vector3 runwayEnd =
            runwayCenter + landingDirection * (runwayLength * 0.5f);

        float remainingRunwayDistance =
            Vector3.Dot(runwayEnd - aircraftPosition, landingDirection);

        if (remainingRunwayDistance <= 0f)
        {
            SetFailure(
                "The aircraft is at or beyond the runway end. " +
                "No forward runway exit can be selected.");
            return;
        }

        List<RunwayExitCandidate> exits =
            FindForwardRunwayExitCandidates(
                aircraftPosition,
                landingDirection,
                remainingRunwayDistance,
                minimumTaxiwayWidth);

        if (exits.Count == 0)
        {
            SetFailure(
                $"No suitable taxiway exit was found ahead of " +
                $"the aircraft on runway {selectedRunway.name}.");
            return;
        }

        List<TaxiwayCandidate> targets =
            FindNearbyCenterlineCells(
                assignedGate.transform.position,
                minimumTaxiwayWidth);

        if (targets.Count == 0)
        {
            SetFailure(
                $"No suitable taxiway centerline was found near " +
                $"{assignedGate.DisplayName}.");
            return;
        }

        List<RouteCandidatePair> pairs =
            BuildCandidatePairs(exits, targets);

        bool routeFound = false;
        int attempts = 0;
        RunwayExitCandidate selectedExit = default;
        Vector2Int selectedTarget = default;

        foreach (RouteCandidatePair pair in pairs)
        {
            if (attempts >= maximumRouteAttempts)
                break;

            attempts++;

            List<Vector2Int> path = taxiwayPathfinder.FindPath(
                pair.Exit.Cell,
                pair.Target.Cell,
                minimumTaxiwayWidth);

            if (path == null || path.Count == 0)
                continue;

            selectedExit = pair.Exit;
            selectedTarget = pair.Target.Cell;

            taxiRoute.AddRange(path);
            routeFound = true;
            break;
        }

        if (!routeFound)
        {
            SetFailure(
                $"No connected A* route was found from the runway exits " +
                $"to {assignedGate.DisplayName}. Tried {attempts} routes.");
            return;
        }

        Vector3 exitCellPosition =
            gridManager.GridToWorldCenter(selectedExit.Cell);

        exitCellPosition.y = taxiWorldY;

        runwayExitPoint =
            aircraftPosition +
            landingDirection * selectedExit.ForwardDistance;

        runwayExitPoint.y = taxiWorldY;
        taxiwayJoinPoint = exitCellPosition;

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
            SetFailure("The calculated taxi movement path is empty.");
            return;
        }

        currentWaypointIndex = 0;
        hasArrivedAtGate = false;
        isTaxiing = true;
        phase = TaxiPhase.RollingForwardOnRunway;

        Vector3 adjustedPosition = transform.position;
        adjustedPosition.y = taxiWorldY;
        transform.position = adjustedPosition;

        debugStatus =
            $"Continuing forward on the runway for " +
            $"{debugRunwayExitDistance:F1} units toward exit " +
            $"{debugRunwayExitCell}, then taxiing to " +
            $"{assignedGate.DisplayName}.";

        if (logRouteDetails)
        {
            Debug.Log(
                $"CARGOPAX TAXI ROUTE FOUND | " +
                $"Flight: {flightIdentity.FlightNumber} | " +
                $"Gate: {assignedGate.DisplayName} | " +
                $"Runway: {selectedRunway.name} | " +
                $"Exit: {debugRunwayExitCell} | " +
                $"Path cells: {debugRouteCells} | " +
                $"Smoothed points: {taxiMovementPath.Count}\n" +
                debugLastPath,
                this);
        }

        if (logTaxiProgress)
        {
            Debug.Log(
                $"CARGOPAX TAXI STARTED | " +
                $"Flight: {flightIdentity.FlightNumber} | " +
                $"Gate: {assignedGate.DisplayName} | " +
                $"Speed: {taxiSpeedKnots:F1} knots",
                this);
        }
    }

    private void BuildSmoothedTaxiMovementPath()
    {
        taxiMovementPath.Clear();

        if (taxiRoute.Count == 0)
            return;

        List<Vector3> points = new List<Vector3>();

        foreach (Vector2Int cell in taxiRoute)
        {
            Vector3 point = gridManager.GridToWorldCenter(cell);
            point.y = taxiWorldY;
            points.Add(point);
        }

        if (points.Count == 1)
        {
            taxiMovementPath.Add(points[0]);
            return;
        }

        float cellSize = Mathf.Max(0.001f, gridManager.CellSize);
        float desiredRadius = turningRadiusInCells * cellSize;

        taxiMovementPath.Add(points[0]);

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 previous = points[i - 1];
            Vector3 corner = points[i];
            Vector3 next = points[i + 1];

            Vector3 incoming = corner - previous;
            Vector3 outgoing = next - corner;

            incoming.y = 0f;
            outgoing.y = 0f;

            if (incoming.sqrMagnitude < 0.0001f ||
                outgoing.sqrMagnitude < 0.0001f)
            {
                AddMovementPoint(corner);
                continue;
            }

            incoming.Normalize();
            outgoing.Normalize();

            float dot = Vector3.Dot(incoming, outgoing);

            if (dot > 0.99f || dot < -0.99f)
            {
                AddMovementPoint(corner);
                continue;
            }

            if (Mathf.Abs(dot) > 0.01f)
            {
                AddMovementPoint(corner);
                continue;
            }

            float radius = Mathf.Min(
                desiredRadius,
                Vector3.Distance(previous, corner) * 0.5f,
                Vector3.Distance(corner, next) * 0.5f);

            if (radius < 0.01f)
            {
                AddMovementPoint(corner);
                continue;
            }

            Vector3 entry = corner - incoming * radius;
            Vector3 exit = corner + outgoing * radius;

            AddMovementPoint(entry);

            for (int segment = 1;
                 segment <= turnCurveResolution;
                 segment++)
            {
                float t = segment / (float)turnCurveResolution;
                float inverseT = 1f - t;

                Vector3 curvePoint =
                    inverseT * inverseT * entry +
                    2f * inverseT * t * corner +
                    t * t * exit;

                curvePoint.y = taxiWorldY;
                AddMovementPoint(curvePoint);
            }
        }

        AddMovementPoint(points[points.Count - 1]);
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
                $"Runway {selectedRunway.name} has no PlacedBuilding component.",
                selectedRunway);
            return candidates;
        }

        Vector2Int origin = runwayBuilding.Origin;

        int minX = origin.x;
        int maxX = origin.x + runwayBuilding.Width - 1;
        int minZ = origin.y;
        int maxZ = origin.y + runwayBuilding.Depth - 1;

        bool runwayAlongX =
            runwayBuilding.Width >= runwayBuilding.Depth;

        Vector3 perpendicular =
            new Vector3(-runwayDirection.z, 0f, runwayDirection.x);

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);

                if (!taxiwayNetwork.IsTaxiway(cell) ||
                    !taxiwayNetwork.IsCenterlineCell(cell))
                    continue;

                if (taxiwayNetwork.GetTaxiwayWidth(cell) <
                    minimumTaxiwayWidth)
                    continue;

                bool connectedExit = false;

                if (runwayAlongX)
                {
                    if (cell.x < minX || cell.x > maxX)
                        continue;

                    if (cell.y == maxZ + 1 &&
                        taxiwayNetwork.IsCenterlineCell(
                            new Vector2Int(cell.x, cell.y + 1)))
                        connectedExit = true;

                    if (cell.y == minZ - 1 &&
                        taxiwayNetwork.IsCenterlineCell(
                            new Vector2Int(cell.x, cell.y - 1)))
                        connectedExit = true;
                }
                else
                {
                    if (cell.y < minZ || cell.y > maxZ)
                        continue;

                    if (cell.x == maxX + 1 &&
                        taxiwayNetwork.IsCenterlineCell(
                            new Vector2Int(cell.x + 1, cell.y)))
                        connectedExit = true;

                    if (cell.x == minX - 1 &&
                        taxiwayNetwork.IsCenterlineCell(
                            new Vector2Int(cell.x - 1, cell.y)))
                        connectedExit = true;
                }

                if (!connectedExit)
                    continue;

                Vector3 worldPosition =
                    gridManager.GridToWorldCenter(cell);

                Vector3 delta = worldPosition - aircraftPosition;
                delta.y = 0f;

                float forwardDistance =
                    Vector3.Dot(delta, runwayDirection);

                if (forwardDistance < minimumExitForwardDistance ||
                    forwardDistance >
                    remainingRunwayDistance + runwayEndTolerance)
                    continue;

                Vector3 lateralOffset =
                    delta - runwayDirection * forwardDistance;

                lateralOffset.y = 0f;

                float lateralDistance = lateralOffset.magnitude;

                if (lateralDistance > maximumRunwayExitLateralDistance)
                    continue;

                float sideValue =
                    Vector3.Dot(lateralOffset, perpendicular);

                int side = sideValue >= 0f ? 1 : -1;

                candidates.Add(new RunwayExitCandidate(
                    cell,
                    forwardDistance,
                    lateralDistance,
                    side));
            }
        }

        candidates.Sort((a, b) =>
        {
            int forwardComparison =
                a.ForwardDistance.CompareTo(b.ForwardDistance);

            if (forwardComparison != 0)
                return forwardComparison;

            return a.LateralDistance.CompareTo(b.LateralDistance);
        });

        List<RunwayExitCandidate> unique =
            new List<RunwayExitCandidate>();

        HashSet<long> usedStations = new HashSet<long>();
        float cellSize = Mathf.Max(0.001f, gridManager.CellSize);

        foreach (RunwayExitCandidate candidate in candidates)
        {
            int station = Mathf.RoundToInt(
                candidate.ForwardDistance / cellSize);

            long key = (long)station * 2L +
                       (candidate.Side > 0 ? 1L : 0L);

            if (usedStations.Add(key))
                unique.Add(candidate);
        }

        return unique;
    }

    private List<RouteCandidatePair> BuildCandidatePairs(
        List<RunwayExitCandidate> exits,
        List<TaxiwayCandidate> targets)
    {
        List<RouteCandidatePair> pairs =
            new List<RouteCandidatePair>();

        foreach (RunwayExitCandidate exit in exits)
        {
            foreach (TaxiwayCandidate target in targets)
                pairs.Add(new RouteCandidatePair(exit, target));
        }

        pairs.Sort((a, b) =>
        {
            int exitComparison =
                a.Exit.ForwardDistance.CompareTo(b.Exit.ForwardDistance);

            if (exitComparison != 0)
                return exitComparison;

            return a.Target.DistanceSquared.CompareTo(
                b.Target.DistanceSquared);
        });

        return pairs;
    }

    private List<TaxiwayCandidate> FindNearbyCenterlineCells(
        Vector3 worldPosition,
        int minimumTaxiwayWidth)
    {
        List<TaxiwayCandidate> candidates =
            new List<TaxiwayCandidate>();

        Vector2Int center = gridManager.WorldToGrid(worldPosition);
        int radius = connectionSearchRadiusCells;

        for (int xOffset = -radius; xOffset <= radius; xOffset++)
        {
            for (int zOffset = -radius; zOffset <= radius; zOffset++)
            {
                Vector2Int cell = new Vector2Int(
                    center.x + xOffset,
                    center.y + zOffset);

                if (!gridManager.IsInsideGrid(cell))
                    continue;

                if (!taxiwayNetwork.IsTaxiway(cell) ||
                    !taxiwayNetwork.IsCenterlineCell(cell))
                    continue;

                if (taxiwayNetwork.GetTaxiwayWidth(cell) <
                    minimumTaxiwayWidth)
                    continue;

                Vector3 cellPosition =
                    gridManager.GridToWorldCenter(cell);

                candidates.Add(new TaxiwayCandidate(
                    cell,
                    HorizontalDistanceSquared(worldPosition, cellPosition)));
            }
        }

        candidates.Sort((a, b) =>
            a.DistanceSquared.CompareTo(b.DistanceSquared));

        if (candidates.Count > maximumConnectionCandidates)
        {
            candidates.RemoveRange(
                maximumConnectionCandidates,
                candidates.Count - maximumConnectionCandidates);
        }

        return candidates;
    }

    private void UpdateTaxiMovement()
    {
        if (assignedGate == null)
        {
            StopTaxiing("Taxiing stopped because the assigned gate is missing.");
            return;
        }

        if (taxiRoute.Count == 0 || taxiMovementPath.Count == 0)
        {
            StopTaxiing("Taxiing stopped because the route is missing.");
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
        if (HorizontalDistance(transform.position, runwayExitPoint) <=
            waypointArrivalDistance)
        {
            SetPositionAt(runwayExitPoint);
            phase = TaxiPhase.JoiningTaxiway;

            debugStatus =
                $"Reached runway exit {debugRunwayExitCell}. Joining taxiway.";

            if (logTaxiProgress)
            {
                Debug.Log(
                    $"CARGOPAX REACHED RUNWAY EXIT | " +
                    $"Flight: {flightIdentity.FlightNumber} | " +
                    $"Exit: {debugRunwayExitCell}",
                    this);
            }

            return;
        }

        MoveTowardWaypoint(runwayExitPoint);
    }

    private void UpdateTaxiwayJoin()
    {
        if (HorizontalDistance(transform.position, taxiwayJoinPoint) <=
            waypointArrivalDistance)
        {
            SetPositionAt(taxiwayJoinPoint);

            currentWaypointIndex = 1;
            debugCurrentWaypoint = currentWaypointIndex;
            phase = TaxiPhase.FollowingTaxiway;

            debugStatus =
                $"Joined taxiway. Following route to {assignedGate.DisplayName}.";

            return;
        }

        MoveTowardWaypoint(taxiwayJoinPoint);
    }

    private void UpdateTaxiwayRoute()
    {
        while (currentWaypointIndex < taxiMovementPath.Count)
        {
            Vector3 waypoint = taxiMovementPath[currentWaypointIndex];

            if (HorizontalDistance(transform.position, waypoint) <=
                taxiPathPointTolerance)
            {
                currentWaypointIndex++;
                debugCurrentWaypoint = currentWaypointIndex;
                continue;
            }

            MoveTowardWaypoint(waypoint);
            return;
        }

        phase = TaxiPhase.ApproachingGate;
        debugStatus = $"Taxiway route complete. Approaching {assignedGate.DisplayName}.";
    }

    private void UpdateFinalGateApproach()
    {
        Vector3 gateTarget = assignedGate.transform.position;
        gateTarget.y = taxiWorldY;

        if (HorizontalDistance(transform.position, gateTarget) <=
            gateArrivalDistance)
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

        float taxiSpeed = taxiSpeedKnots * worldUnitsPerSecondPerKnot;

        if (trafficFollowing != null)
            taxiSpeed = trafficFollowing.GetTrafficFollowingSpeed(taxiSpeed);

        transform.position = Vector3.MoveTowards(
            transform.position,
            target,
            taxiSpeed * Time.deltaTime);
    }

    private void FaceMovementDirection(Vector3 movement)
    {
        movement.y = 0f;

        if (movement.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(movement.normalized, Vector3.up);

        targetRotation *= Quaternion.Euler(modelRotationOffset);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime);
    }

    private void SetPositionAt(Vector3 target)
    {
        target.y = taxiWorldY;
        transform.position = target;
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private float HorizontalDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
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
            $"Arrived at {assignedGate.DisplayName}. Aircraft is at its assigned gate.";

        if (logTaxiProgress)
        {
            Debug.Log(
                $"CARGOPAX ARRIVED AT GATE | " +
                $"Flight: {flightIdentity.FlightNumber} | " +
                $"Gate: {assignedGate.DisplayName}",
                this);
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
            $"CARGOPAX TAXI STOPPED | " +
            $"{(flightIdentity != null ? flightIdentity.FlightNumber : "Unknown flight")} | " +
            reason,
            this);
    }

    private int GetMinimumTaxiwayWidth(AircraftGateSize requiredGateSize)
    {
        switch (requiredGateSize)
        {
            case AircraftGateSize.Grass:
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
            $"CARGOPAX TAXI ROUTE FAILED | " +
            $"{(flightIdentity != null ? flightIdentity.FlightNumber : "Unknown flight")} | " +
            message,
            this);
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
                gatePosition);

            Gizmos.DrawWireSphere(gatePosition, 0.35f);
        }
    }
}
