using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sightseeing aircraft taxi controller.
/// Finds a connected taxiway route from the runway exit to the
/// reserved SightseeingAircraftParkingSpot, then slowly rotates
/// the aircraft to its configured parking heading.
/// </summary>
[DisallowMultipleComponent]
public class AircraftSSLandingTaxiController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AircraftSSLandingController landingController;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;
    [SerializeField] private TaxiwayPathfinder taxiwayPathfinder;

    [Header("Route Search")]
    [Min(1)]
    [SerializeField] private int connectionSearchRadiusCells = 12;
    [Min(1)]
    [SerializeField] private int maximumRouteAttempts = 32;
    [Min(1)]
    [SerializeField] private int minimumTaxiwayWidth = 3;
    [Min(0.1f)]
    [SerializeField] private float maximumRunwayExitLateralDistance = 12f;
    [Min(0f)]
    [SerializeField] private float minimumExitForwardDistance = 0.5f;
    [Min(0f)]
    [SerializeField] private float runwayEndTolerance = 1f;
    [Min(0.1f)]
    [SerializeField] private float maximumParkingConnectorDistance = 12f;

    [Header("Taxi Movement")]
    [Min(1f)]
    [SerializeField] private float taxiSpeedKnots = 15f;

    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 2f;
    [Min(1f)]
    [SerializeField] private float parkingRotationSpeed = 10f;
    [Min(0.05f)]
    [SerializeField] private float waypointArrivalDistance = 0.2f;
    [Min(0.1f)]
    [SerializeField] private float parkingArrivalDistance = 0.5f;
    [Min(0f)]
    [SerializeField] private float taxiHeightOffset = 0.05f;
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Smooth Taxi Turns")]
    [Range(0.1f, 3f)]
    [SerializeField] private float turningRadiusInCells = 2f;
    [Range(4, 16)]
    [SerializeField] private int turnCurveResolution = 16;
    [Min(0.005f)]
    [SerializeField] private float taxiPathPointTolerance = 0.02f;

    [Header("Debug")]
    [SerializeField] private bool logTaxiProgress = true;
    [TextArea(2, 5)]
    public string debugStatus = "Waiting for sightseeing landing completion.";

    private enum TaxiPhase
    {
        WaitingForLanding,
        RollingToRunwayExit,
        JoiningTaxiway,
        FollowingTaxiway,
        ApproachingParking,
        RotatingToParking,
        Parked,
        Failed
    }

    private struct ExitCandidate
    {
        public Vector2Int Cell;
        public float ForwardDistance;
        public float LateralDistance;

        public ExitCandidate(
            Vector2Int cell,
            float forwardDistance,
            float lateralDistance)
        {
            Cell = cell;
            ForwardDistance = forwardDistance;
            LateralDistance = lateralDistance;
        }
    }

    private struct TargetCandidate
    {
        public Vector2Int Cell;
        public float DistanceSquared;

        public TargetCandidate(Vector2Int cell, float distanceSquared)
        {
            Cell = cell;
            DistanceSquared = distanceSquared;
        }
    }

    private readonly List<Vector2Int> route = new List<Vector2Int>();
    private readonly List<Vector3> taxiMovementPath = new List<Vector3>();

    private bool routeAttempted;
    private int currentRouteIndex;
    private TaxiPhase phase = TaxiPhase.WaitingForLanding;

    private AircraftRunway selectedRunway;
    private SightseeingAircraftParkingSpot reservedSpot;
    private Vector3 landingDirection;
    private Vector3 runwayExitPoint;
    private Vector3 taxiwayJoinPoint;
    private Vector3 parkingTarget;
    private float taxiWorldY;
    private bool hasPublishedParkedStatus;

    private const float BezierCircleFactor = 0.55228475f;

    public bool IsTaxiing =>
        phase == TaxiPhase.RollingToRunwayExit ||
        phase == TaxiPhase.JoiningTaxiway ||
        phase == TaxiPhase.FollowingTaxiway ||
        phase == TaxiPhase.ApproachingParking;

    public bool IsControllingAircraft =>
        IsTaxiing || phase == TaxiPhase.RotatingToParking;

    // Becomes true only after the aircraft finishes its parking turn.
    public bool HasArrivedAtParking => phase == TaxiPhase.Parked;

    private void Awake()
    {
        if (landingController == null)
            landingController = GetComponent<AircraftSSLandingController>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindFirstObjectByType<TaxiwayNetwork>();

        if (taxiwayPathfinder == null)
            taxiwayPathfinder = FindFirstObjectByType<TaxiwayPathfinder>();
    }

    private void Update()
    {
        AircraftStatus status = GetComponent<AircraftStatus>();

        if (status != null)
        {
            if (IsTaxiing)
            {
                // Landing taxi owns the aircraft while it is moving.
                status.SetTaxiing(true);
            }
            else if (phase == TaxiPhase.RotatingToParking)
            {
                // The aircraft is turning in place at its parking spot.
                status.SetTaxiing(false);
                status.SetCurrentSpeedKnots(0f);
            }
            else if (HasArrivedAtParking && !hasPublishedParkedStatus)
            {
                // Publish parked status once. Do not keep calling
                // SetParked(true), because it resets CurrentSpeedKnots
                // and would overwrite the later departure taxi speed.
                status.SetParked(true);
                hasPublishedParkedStatus = true;
            }
        }

        if (landingController == null)
            return;

        if (!routeAttempted && landingController.IsLandingComplete)
        {
            routeAttempted = true;
            TryBuildTaxiRoute();
        }

        switch (phase)
        {
            case TaxiPhase.RollingToRunwayExit:
                MoveToRunwayExit();
                break;

            case TaxiPhase.JoiningTaxiway:
                MoveToTaxiwayJoin();
                break;

            case TaxiPhase.FollowingTaxiway:
                FollowTaxiwayRoute();
                break;

            case TaxiPhase.ApproachingParking:
                MoveToParkingSpot();
                break;

            case TaxiPhase.RotatingToParking:
                RotateToParkingHeading();
                break;
        }
    }


    private void TryBuildTaxiRoute()
    {
        Debug.Log(
            $"[SS TAXI DIAG] START | Aircraft={name} | " +
            $"Position={transform.position}",
            this);

        if (gridManager == null ||
            taxiwayNetwork == null ||
            taxiwayPathfinder == null ||
            landingController == null)
        {
            Fail(
                $"Missing references | GridManager={gridManager != null} | " +
                $"TaxiwayNetwork={taxiwayNetwork != null} | " +
                $"TaxiwayPathfinder={taxiwayPathfinder != null} | " +
                $"LandingController={landingController != null}");
            return;
        }

        reservedSpot = FindReservedParkingSpot();

        if (reservedSpot == null)
        {
            Fail("No sightseeing parking spot is reserved for this aircraft.");
            return;
        }

        PlacedBuilding parkingBuilding =
            reservedSpot.GetComponent<PlacedBuilding>();

        if (parkingBuilding == null)
        {
            Fail("Sightseeing parking spot has no PlacedBuilding component.");
            return;
        }

        Debug.Log(
            $"[SS TAXI DIAG] PARKING | Spot={reservedSpot.name} | " +
            $"Position={reservedSpot.ParkingWorldPosition} | " +
            $"GridCell={gridManager.WorldToGrid(reservedSpot.ParkingWorldPosition)} | " +
            $"Origin={parkingBuilding.Origin} | " +
            $"Width={parkingBuilding.Width} | Depth={parkingBuilding.Depth} | " +
            $"Rotation={parkingBuilding.Rotation}",
            this);

        selectedRunway = landingController.SelectedRunway;
        landingDirection = landingController.LandingDirection;
        landingDirection.y = 0f;

        if (selectedRunway == null ||
            landingDirection.sqrMagnitude < 0.0001f)
        {
            Fail("Selected runway or landing direction is invalid.");
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
            Fail("Could not retrieve selected runway geometry.");
            return;
        }

        PlacedBuilding runwayBuilding =
            selectedRunway.GetComponent<PlacedBuilding>();

        if (runwayBuilding == null)
        {
            Fail("Selected runway has no PlacedBuilding component.");
            return;
        }

        Debug.Log(
            $"[SS TAXI DIAG] RUNWAY | Name={selectedRunway.name} | " +
            $"Center={runwayCenter} | Axis={runwayAxis} | " +
            $"Length={runwayLength} | SurfaceY={runwaySurfaceY} | " +
            $"Direction={landingDirection} | " +
            $"Origin={runwayBuilding.Origin} | " +
            $"Width={runwayBuilding.Width} | Depth={runwayBuilding.Depth}",
            this);

        taxiWorldY = runwaySurfaceY + taxiHeightOffset;

        Vector3 aircraftPosition = transform.position;
        aircraftPosition.y = runwaySurfaceY;

        Vector3 runwayEnd =
            runwayCenter + landingDirection * (runwayLength * 0.5f);

        float remainingRunwayDistance =
            Vector3.Dot(
                runwayEnd - aircraftPosition,
                landingDirection);

        Debug.Log(
            $"[SS TAXI DIAG] RUNWAY POSITION | AircraftAt={aircraftPosition} | " +
            $"RunwayEnd={runwayEnd} | " +
            $"RemainingDistance={remainingRunwayDistance}",
            this);

        if (remainingRunwayDistance <= 0f)
        {
            Fail("Aircraft is at or beyond the runway end.");
            return;
        }

        // Only consider runway exits ahead of the landed aircraft.
        List<ExitCandidate> exits = FindForwardRunwayExits(
            aircraftPosition,
            remainingRunwayDistance,
            runwayBuilding);

        Debug.Log(
            $"[SS TAXI DIAG] EXITS FOUND | Count={exits.Count} | " +
            $"MinimumWidth={minimumTaxiwayWidth}",
            this);

        for (int i = 0; i < exits.Count; i++)
        {
            ExitCandidate e = exits[i];

            Debug.Log(
                $"[SS TAXI DIAG] EXIT {i} | Cell={e.Cell} | " +
                $"Forward={e.ForwardDistance:F2} | " +
                $"Lateral={e.LateralDistance:F2}",
                this);
        }

        if (exits.Count == 0)
        {
            Fail("No compatible taxiway exit was found ahead of the landed aircraft.");
            return;
        }

        parkingTarget = reservedSpot.ParkingWorldPosition;
        parkingTarget.y = taxiWorldY;

        List<TargetCandidate> targets =
            FindNearbyTaxiwayTargets(parkingTarget);

        float connectorDistance =
            maximumParkingConnectorDistance * gridManager.CellSize;

        Debug.Log(
            $"[SS TAXI DIAG] TARGET SEARCH | ParkingTarget={parkingTarget} | " +
            $"SearchRadius={connectionSearchRadiusCells} cells | " +
            $"CellSize={gridManager.CellSize} | " +
            $"MaxConnectorDistance={connectorDistance:F2} | " +
            $"TargetsFound={targets.Count}",
            this);

        for (int i = 0; i < targets.Count; i++)
        {
            TargetCandidate t = targets[i];
            Vector3 world = gridManager.GridToWorldCenter(t.Cell);

            float edgeDistance =
                GetDistanceToParkingFootprintEdge(t.Cell);

            Debug.Log(
                $"[SS TAXI DIAG] TARGET {i} | Cell={t.Cell} | " +
                $"World={world} | " +
                $"DistanceToFootprintEdge={edgeDistance:F2} | " +
                $"DistanceToParkingRoot={HorizontalDistance(world, parkingTarget):F2} | " +
                $"TaxiwayWidth={taxiwayNetwork.GetTaxiwayWidth(t.Cell)}",
                this);
        }

        if (targets.Count == 0)
        {
            Fail("No suitable taxiway centerline was found near sightseeing parking.");
            return;
        }

        bool foundRoute = false;

        ExitCandidate chosenExit = default;
        TargetCandidate chosenTarget = default;
        List<Vector2Int> chosenPath = null;

        int attempts = 0;
        int skippedByDistance = 0;
        int failedPaths = 0;

        int bestPathLength = int.MaxValue;
        float bestExitDistance = float.MaxValue;

        // Exits are ordered from nearest to farthest ahead.
        // Give each exit a share of the remaining attempt budget so that
        // one disconnected exit cannot consume the entire search.
        for (int exitIndex = 0;
             exitIndex < exits.Count;
             exitIndex++)
        {
            if (attempts >= maximumRouteAttempts)
                break;

            ExitCandidate exit = exits[exitIndex];

            int remainingExits = exits.Count - exitIndex;
            int remainingAttempts = maximumRouteAttempts - attempts;

            int attemptsForThisExit = Mathf.Max(
                1,
                remainingAttempts / remainingExits);

            int exitAttempts = 0;

            // Targets are sorted by distance to the parking position.
            foreach (TargetCandidate target in targets)
            {
                float distanceToFootprintEdge =
                    GetDistanceToParkingFootprintEdge(target.Cell);

                if (distanceToFootprintEdge > connectorDistance)
                {
                    skippedByDistance++;
                    continue;
                }

                if (attempts >= maximumRouteAttempts ||
                    exitAttempts >= attemptsForThisExit)
                {
                    break;
                }

                attempts++;
                exitAttempts++;

                List<Vector2Int> candidatePath =
                    taxiwayPathfinder.FindPath(
                        exit.Cell,
                        target.Cell,
                        minimumTaxiwayWidth);

                if (candidatePath == null || candidatePath.Count == 0)
                {
                    failedPaths++;
                    continue;
                }

                // Keep the best route found across the forward exits.
                // Prefer fewer taxiway cells, then the nearer forward exit.
                if (candidatePath.Count < bestPathLength ||
                    (candidatePath.Count == bestPathLength &&
                     exit.ForwardDistance < bestExitDistance))
                {
                    foundRoute = true;
                    chosenExit = exit;
                    chosenTarget = target;
                    chosenPath = candidatePath;

                    bestPathLength = candidatePath.Count;
                    bestExitDistance = exit.ForwardDistance;
                }

                // Targets are sorted by parking distance, so the first
                // successful target is the nearest viable target for this exit.
                break;
            }
        }

        Debug.Log(
            $"[SS TAXI DIAG] SEARCH SUMMARY | " +
            $"Exits={exits.Count} | Targets={targets.Count} | " +
            $"PairsTested={attempts} | SkippedByDistance={skippedByDistance} | " +
            $"FailedPaths={failedPaths} | RouteFound={foundRoute} | " +
            $"ChosenExit={(foundRoute ? chosenExit.Cell.ToString() : "none")} | " +
            $"BestPathLength={(foundRoute ? bestPathLength : 0)}",
            this);

        if (!foundRoute)
        {
            Fail(
                "No connected taxiway route was found. " +
                $"Diagnostic: exits={exits.Count}, targets={targets.Count}, " +
                $"pairs tested={attempts}, distance skips={skippedByDistance}, " +
                $"pathfinder failures={failedPaths}.");
            return;
        }

        route.Clear();
        route.AddRange(chosenPath);

        runwayExitPoint =
            aircraftPosition +
            landingDirection * chosenExit.ForwardDistance;

        runwayExitPoint.y = taxiWorldY;

        taxiwayJoinPoint =
            gridManager.GridToWorldCenter(chosenExit.Cell);

        taxiwayJoinPoint.y = taxiWorldY;

        BuildSmoothedTaxiMovementPath();

        if (taxiMovementPath.Count == 0)
        {
            Fail("The taxiway movement path could not be created.");
            return;
        }

        currentRouteIndex = 1;
        phase = TaxiPhase.RollingToRunwayExit;

        Vector3 adjustedPosition = transform.position;
        adjustedPosition.y = taxiWorldY;
        transform.position = adjustedPosition;

        debugStatus =
            $"Sightseeing taxi route found. Forward exit {chosenExit.Cell}, " +
            $"target {chosenTarget.Cell}, {route.Count} route cells.";

        Debug.Log(
            $"[SS TAXI DIAG] ROUTE FOUND | Exit={chosenExit.Cell} | " +
            $"ForwardDistance={chosenExit.ForwardDistance:F2} | " +
            $"Target={chosenTarget.Cell} | Cells={route.Count} | " +
            $"RunwayExitPoint={runwayExitPoint} | " +
            $"TaxiwayJoinPoint={taxiwayJoinPoint}",
            this);
    }



    private void BuildSmoothedTaxiMovementPath()
    {
        taxiMovementPath.Clear();

        if (route == null || route.Count == 0)
            return;

        List<Vector3> points = new List<Vector3>(route.Count);

        foreach (Vector2Int cell in route)
        {
            Vector3 point = gridManager.GridToWorldCenter(cell);
            point.y = taxiWorldY;
            points.Add(point);
        }

        if (points.Count < 3)
        {
            foreach (Vector3 point in points)
                AddMovementPoint(point);

            return;
        }

        // Detect turns using the same logic as the departure taxi controller.
        List<int> turns = new List<int>();

        for (int i = 1; i < points.Count - 1; i++)
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

            float dot = Vector3.Dot(incoming, outgoing);

            // Ignore straight sections and near-180-degree reversals.
            if (dot > 0.99f || dot < -0.99f)
                continue;

            turns.Add(i);
        }

        if (turns.Count == 0)
        {
            AddStraightMovementPoints(points[0], points[points.Count - 1]);
            return;
        }

        // Calculate each turn radius using the distance between corners,
        // matching the departure taxi controller's approach.
        float[] radii = new float[points.Count];

        for (int t = 0; t < turns.Count; t++)
        {
            int index = turns[t];

            int previousTurn = t > 0
                ? turns[t - 1]
                : 0;

            int nextTurn = t < turns.Count - 1
                ? turns[t + 1]
                : points.Count - 1;

            float previousDistance = Vector3.Distance(
                points[previousTurn],
                points[index]);

            float nextDistance = Vector3.Distance(
                points[index],
                points[nextTurn]);

            float requestedRadius =
                Mathf.Max(0.01f, turningRadiusInCells) *
                gridManager.CellSize;

            radii[index] = Mathf.Min(
                requestedRadius,
                previousDistance * 0.5f,
                nextDistance * 0.5f);
        }

        const float bezierFactor = 0.55228475f;

        Vector3 current = points[0];
        AddMovementPoint(current);

        foreach (int index in turns)
        {
            float radius = radii[index];

            if (radius <= 0.01f)
                continue;

            Vector3 incoming = points[index] - points[index - 1];
            Vector3 outgoing = points[index + 1] - points[index];

            incoming.y = 0f;
            outgoing.y = 0f;

            if (incoming.sqrMagnitude < 0.0001f ||
                outgoing.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            incoming.Normalize();
            outgoing.Normalize();

            // Start the curve before the corner and finish it beyond it.
            Vector3 entry = points[index] - incoming * radius;
            Vector3 exit = points[index] + outgoing * radius;

            entry.y = taxiWorldY;
            exit.y = taxiWorldY;

            // Straight taxiway section leading into this turn.
            AddStraightMovementPoints(current, entry);

            Vector3 control1 =
                entry + incoming * radius * bezierFactor;

            Vector3 control2 =
                exit - outgoing * radius * bezierFactor;

            int resolution = Mathf.Clamp(turnCurveResolution, 4, 16);

            for (int sample = 1; sample <= resolution; sample++)
            {
                float t = sample / (float)resolution;
                float u = 1f - t;

                Vector3 point =
                    u * u * u * entry +
                    3f * u * u * t * control1 +
                    3f * u * t * t * control2 +
                    t * t * t * exit;

                point.y = taxiWorldY;
                AddMovementPoint(point);
            }

            current = exit;
        }

        // Finish the final straight section after the last corner.
        AddStraightMovementPoints(
            current,
            points[points.Count - 1]);
    }

    private void AddStraightMovementPoints(Vector3 start, Vector3 end)
    {
        start.y = taxiWorldY;
        end.y = taxiWorldY;

        float distance = Vector3.Distance(start, end);

        float maximumStep = Mathf.Max(
            0.1f,
            gridManager.CellSize * 0.5f);

        int steps = Mathf.Max(
            1,
            Mathf.CeilToInt(distance / maximumStep));

        for (int i = 1; i <= steps; i++)
        {
            Vector3 point = Vector3.Lerp(
                start,
                end,
                i / (float)steps);

            point.y = taxiWorldY;
            AddMovementPoint(point);
        }
    }


    private void AddMovementPoint(Vector3 point)
    {
        point.y = taxiWorldY;

        if (taxiMovementPath.Count > 0)
        {
            Vector3 previous =
                taxiMovementPath[taxiMovementPath.Count - 1];

            if (HorizontalDistance(previous, point) < 0.005f)
                return;
        }

        taxiMovementPath.Add(point);
    }

    private Vector3 EvaluateCubicBezier(
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float oneMinusT = 1f - t;

        return
            oneMinusT * oneMinusT * oneMinusT * p0 +
            3f * oneMinusT * oneMinusT * t * p1 +
            3f * oneMinusT * t * t * p2 +
            t * t * t * p3;
    }

    private SightseeingAircraftParkingSpot FindReservedParkingSpot()
    {
        SightseeingAircraftParkingSpot[] spots =
            FindObjectsByType<SightseeingAircraftParkingSpot>(
                FindObjectsSortMode.None);

        foreach (SightseeingAircraftParkingSpot spot in spots)
        {
            if (spot != null &&
                spot.IsPlacedBuilding &&
                spot.ReservedAircraft == gameObject)
                return spot;
        }

        return null;
    }

    private float GetDistanceToParkingFootprintEdge(Vector2Int taxiwayCell)
    {
        if (reservedSpot == null || gridManager == null)
            return float.PositiveInfinity;

        PlacedBuilding building =
            reservedSpot.GetComponent<PlacedBuilding>();

        if (building == null || building.Width <= 0 || building.Depth <= 0)
            return float.PositiveInfinity;

        float footprintMinX = building.Origin.x;
        float footprintMaxX = building.Origin.x + building.Width;
        float footprintMinZ = building.Origin.y;
        float footprintMaxZ = building.Origin.y + building.Depth;

        float cellCenterX = taxiwayCell.x + 0.5f;
        float cellCenterZ = taxiwayCell.y + 0.5f;

        float dx = Mathf.Max(
            footprintMinX - cellCenterX,
            0f,
            cellCenterX - footprintMaxX);

        float dz = Mathf.Max(
            footprintMinZ - cellCenterZ,
            0f,
            cellCenterZ - footprintMaxZ);

        return Mathf.Sqrt(dx * dx + dz * dz) * gridManager.CellSize;
    }

    private List<ExitCandidate> FindForwardRunwayExits(
        Vector3 aircraftPosition,
        float remainingRunwayDistance,
        PlacedBuilding runwayBuilding)
    {
        List<ExitCandidate> candidates = new List<ExitCandidate>();

        int minX = runwayBuilding.Origin.x;
        int maxX = runwayBuilding.Origin.x + runwayBuilding.Width - 1;
        int minZ = runwayBuilding.Origin.y;
        int maxZ = runwayBuilding.Origin.y + runwayBuilding.Depth - 1;

        bool runwayAlongX =
            runwayBuilding.Width >= runwayBuilding.Depth;

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);

                if (!taxiwayNetwork.IsTaxiway(cell) ||
                    !taxiwayNetwork.IsCenterlineCell(cell) ||
                    taxiwayNetwork.GetTaxiwayWidth(cell) < minimumTaxiwayWidth)
                    continue;

                bool connectedExit;

                if (runwayAlongX)
                {
                    if (cell.x < minX || cell.x > maxX)
                        continue;

                    connectedExit =
                        (cell.y == maxZ + 1 &&
                         taxiwayNetwork.IsCenterlineCell(
                             new Vector2Int(cell.x, cell.y + 1))) ||
                        (cell.y == minZ - 1 &&
                         taxiwayNetwork.IsCenterlineCell(
                             new Vector2Int(cell.x, cell.y - 1)));
                }
                else
                {
                    if (cell.y < minZ || cell.y > maxZ)
                        continue;

                    connectedExit =
                        (cell.x == maxX + 1 &&
                         taxiwayNetwork.IsCenterlineCell(
                             new Vector2Int(cell.x + 1, cell.y))) ||
                        (cell.x == minX - 1 &&
                         taxiwayNetwork.IsCenterlineCell(
                             new Vector2Int(cell.x - 1, cell.y)));
                }

                if (!connectedExit)
                    continue;

                Vector3 cellWorld = gridManager.GridToWorldCenter(cell);
                Vector3 delta = cellWorld - aircraftPosition;
                delta.y = 0f;

                float forwardDistance =
                    Vector3.Dot(delta, landingDirection);

                if (forwardDistance < minimumExitForwardDistance ||
                    forwardDistance > remainingRunwayDistance + runwayEndTolerance)
                    continue;

                Vector3 lateralOffset =
                    delta - landingDirection * forwardDistance;
                lateralOffset.y = 0f;

                float lateralDistance = lateralOffset.magnitude;

                if (lateralDistance > maximumRunwayExitLateralDistance)
                    continue;

                candidates.Add(new ExitCandidate(
                    cell, forwardDistance, lateralDistance));
            }
        }

        candidates.Sort((a, b) =>
        {
            int comparison =
                a.ForwardDistance.CompareTo(b.ForwardDistance);

            return comparison != 0
                ? comparison
                : a.LateralDistance.CompareTo(b.LateralDistance);
        });

        return candidates;
    }

    private List<TargetCandidate> FindNearbyTaxiwayTargets(
        Vector3 worldPosition)
    {
        List<TargetCandidate> candidates =
            new List<TargetCandidate>();

        Vector2Int center = gridManager.WorldToGrid(worldPosition);

        for (int dx = -connectionSearchRadiusCells;
             dx <= connectionSearchRadiusCells;
             dx++)
        {
            for (int dz = -connectionSearchRadiusCells;
                 dz <= connectionSearchRadiusCells;
                 dz++)
            {
                Vector2Int cell =
                    new Vector2Int(center.x + dx, center.y + dz);

                if (!gridManager.IsInsideGrid(cell) ||
                    !taxiwayNetwork.IsTaxiway(cell) ||
                    !taxiwayNetwork.IsCenterlineCell(cell) ||
                    taxiwayNetwork.GetTaxiwayWidth(cell) < minimumTaxiwayWidth)
                    continue;

                Vector3 cellWorld =
                    gridManager.GridToWorldCenter(cell);

                candidates.Add(new TargetCandidate(
                    cell,
                    HorizontalDistanceSquared(worldPosition, cellWorld)));
            }
        }

        candidates.Sort(
            (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));

        return candidates;
    }

    private void MoveToRunwayExit()
    {
        if (MoveToward(runwayExitPoint, waypointArrivalDistance))
        {
            SetPositionAt(runwayExitPoint);
            phase = TaxiPhase.JoiningTaxiway;
            debugStatus = "Reached runway exit. Joining taxiway.";
        }
    }

    private void MoveToTaxiwayJoin()
    {
        if (MoveToward(taxiwayJoinPoint, waypointArrivalDistance))
        {
            SetPositionAt(taxiwayJoinPoint);
            phase = TaxiPhase.FollowingTaxiway;
            debugStatus = "Joined taxiway. Following route to sightseeing parking.";
        }
    }

    private void FollowTaxiwayRoute()
    {
        while (currentRouteIndex < taxiMovementPath.Count)
        {
            Vector3 waypoint = taxiMovementPath[currentRouteIndex];
            waypoint.y = taxiWorldY;

            if (HorizontalDistance(transform.position, waypoint) <=
                taxiPathPointTolerance)
            {
                SetPositionAt(waypoint);
                currentRouteIndex++;
                continue;
            }

            MoveToward(waypoint, taxiPathPointTolerance);
            return;
        }

        phase = TaxiPhase.ApproachingParking;
        debugStatus =
            $"Taxiway route complete. Approaching {reservedSpot.name}.";
    }

    private void MoveToParkingSpot()
    {
        if (reservedSpot == null || !reservedSpot.IsPlacedBuilding)
        {
            Fail("The reserved sightseeing parking spot was removed during taxiing.");
            return;
        }

        parkingTarget = reservedSpot.ParkingWorldPosition;
        parkingTarget.y = taxiWorldY;

        if (MoveToward(parkingTarget, parkingArrivalDistance))
        {
            SetPositionAt(parkingTarget);

            // Do not snap to the parking heading.
            // Rotate in place at a controlled speed instead.
            phase = TaxiPhase.RotatingToParking;
            debugStatus =
                $"At {reservedSpot.name}. Slowly turning to parking heading.";

            if (logTaxiProgress)
            {
                Debug.Log(
                    $"SIGHTSEEING AIRCRAFT STARTING PARKING TURN | " +
                    $"Aircraft: {name} | Parking: {reservedSpot.name} | " +
                    $"Rotation speed: {parkingRotationSpeed:F1} degrees/second",
                    this);
            }
        }
    }

    private void RotateToParkingHeading()
    {
        if (reservedSpot == null || !reservedSpot.IsPlacedBuilding)
        {
            Fail("The reserved sightseeing parking spot was removed during the parking turn.");
            return;
        }

        Quaternion targetRotation = reservedSpot.ParkingWorldRotation;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            Mathf.Max(1f, parkingRotationSpeed) * Time.deltaTime);

        if (Quaternion.Angle(transform.rotation, targetRotation) <= 0.5f)
        {
            transform.rotation = targetRotation;
            phase = TaxiPhase.Parked;
            debugStatus =
                $"Sightseeing aircraft parked at {reservedSpot.name}.";

            if (logTaxiProgress)
            {
                Debug.Log(
                    $"SIGHTSEEING AIRCRAFT PARKED | Aircraft: {name} | " +
                    $"Parking: {reservedSpot.name}",
                    this);
            }
        }
    }

    private bool MoveToward(Vector3 target, float arrivalDistance)
    {
        if (HorizontalDistance(transform.position, target) <= arrivalDistance)
            return true;

        Vector3 movement = target - transform.position;
        movement.y = 0f;

        if (movement.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(
                movement.normalized, Vector3.up);

            targetRotation *= Quaternion.Euler(modelRotationOffset);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }

        float speedKnots = taxiSpeedKnots;

        AircraftTaxiTrafficFollowing trafficFollowing =
            GetComponent<AircraftTaxiTrafficFollowing>();

        if (trafficFollowing != null)
        {
            speedKnots =
                trafficFollowing.GetTrafficFollowingSpeed(taxiSpeedKnots);
        }
        else
        {
            AircraftStatus status = GetComponent<AircraftStatus>();

            if (status != null)
                status.SetCurrentSpeedKnots(speedKnots);
        }

        float speed = AircraftStatus.KnotsToWorldSpeed(speedKnots);

        transform.position = Vector3.MoveTowards(
            transform.position,
            target,
            speed * Time.deltaTime);

        return HorizontalDistance(transform.position, target) <= arrivalDistance;
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

    private float HorizontalDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private void Fail(string message)
    {
        phase = TaxiPhase.Failed;
        debugStatus = message;

        Debug.LogWarning(
            $"SIGHTSEEING TAXI FAILED | Aircraft: {name} | {message}",
            this);
    }

    private void OnDestroy()
    {
        if (reservedSpot != null)
            reservedSpot.Release(gameObject);
    }
}