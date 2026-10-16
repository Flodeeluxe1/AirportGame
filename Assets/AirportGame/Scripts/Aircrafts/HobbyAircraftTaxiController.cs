
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class HobbyAircraftTaxiController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AircraftLandingController landingController;
    [SerializeField] private AircraftFlightIdentity flightIdentity;
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
    [SerializeField] private float maximumParkingConnectorDistance = 4f;

    [Header("Taxi Movement")]
    [Min(1f)]
    [SerializeField] private float taxiSpeedKnots = 10f;
    [Min(0.001f)]
    [SerializeField] private float worldUnitsPerSecondPerKnot = 0.05f;
    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 3f;
    [Min(0.05f)]
    [SerializeField] private float waypointArrivalDistance = 0.2f;
    [Min(0.1f)]
    [SerializeField] private float parkingArrivalDistance = 0.5f;
    [Min(0f)]
    [SerializeField] private float taxiHeightOffset = 0.05f;
    [SerializeField] private Vector3 modelRotationOffset;

    [Header("Debug")]
    [SerializeField] private bool logTaxiProgress = true;
    [TextArea(2, 5)]
    public string debugStatus = "Waiting for VFR landing completion.";

    private enum TaxiPhase
    {
        WaitingForLanding,
        RollingToRunwayExit,
        JoiningTaxiway,
        FollowingTaxiway,
        ApproachingParking,
        Parked,
        Failed
    }

    private struct ExitCandidate
    {
        public Vector2Int Cell;
        public float ForwardDistance;
        public float LateralDistance;
        public int Side;

        public ExitCandidate(
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

    private struct TargetCandidate
    {
        public Vector2Int Cell;
        public float DistanceSquared;

        public TargetCandidate(
            Vector2Int cell,
            float distanceSquared)
        {
            Cell = cell;
            DistanceSquared = distanceSquared;
        }
    }

    private readonly List<Vector2Int> route =
        new List<Vector2Int>();

    private bool routeAttempted;
    private int currentRouteIndex;
    private TaxiPhase phase = TaxiPhase.WaitingForLanding;

    private AircraftRunway selectedRunway;
    private HobbyAircraftParkingSpot reservedSpot;
    private Vector3 landingDirection;
    private Vector3 runwayExitPoint;
    private Vector3 taxiwayJoinPoint;
    private Vector3 parkingTarget;
    private float taxiWorldY;

    public bool IsTaxiing =>
        phase == TaxiPhase.RollingToRunwayExit ||
        phase == TaxiPhase.JoiningTaxiway ||
        phase == TaxiPhase.FollowingTaxiway ||
        phase == TaxiPhase.ApproachingParking;

    public bool HasArrivedAtParking =>
        phase == TaxiPhase.Parked;

    private void Awake()
    {
        if (landingController == null)
            landingController =
                GetComponent<AircraftLandingController>();

        if (flightIdentity == null)
            flightIdentity =
                GetComponent<AircraftFlightIdentity>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (taxiwayNetwork == null)
            taxiwayNetwork =
                FindFirstObjectByType<TaxiwayNetwork>();

        if (taxiwayPathfinder == null)
            taxiwayPathfinder =
                FindFirstObjectByType<TaxiwayPathfinder>();
    }

    private void Update()
    {
        if (flightIdentity == null ||
            !flightIdentity.IsHobbyFlight)
        {
            return;
        }

        if (!routeAttempted &&
            landingController != null &&
            landingController.IsLandingComplete)
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
        }
    }

    private void TryBuildTaxiRoute()
    {
        if (gridManager == null ||
            taxiwayNetwork == null ||
            taxiwayPathfinder == null ||
            landingController == null)
        {
            Fail(
                "Hobby taxi references are incomplete. Check " +
                "GridManager, TaxiwayNetwork, TaxiwayPathfinder " +
                "and AircraftLandingController."
            );
            return;
        }

        if (flightIdentity == null ||
            !flightIdentity.IsInitialized)
        {
            Fail(
                "VFR taxiing requires an initialized " +
                "AircraftFlightIdentity."
            );
            return;
        }

        reservedSpot = FindReservedParkingSpot();

        if (reservedSpot == null)
        {
            Fail(
                "No grass parking spot is reserved for this " +
                "VFR aircraft."
            );
            return;
        }

        selectedRunway = landingController.SelectedRunway;
        landingDirection = landingController.LandingDirection;
        landingDirection.y = 0f;

        if (selectedRunway == null ||
            landingDirection.sqrMagnitude < 0.0001f)
        {
            Fail(
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
            Fail("Could not retrieve the selected runway geometry.");
            return;
        }

        PlacedBuilding runwayBuilding =
            selectedRunway.GetComponent<PlacedBuilding>();

        if (runwayBuilding == null)
        {
            Fail(
                "The selected runway has no PlacedBuilding component."
            );
            return;
        }

        taxiWorldY = runwaySurfaceY + taxiHeightOffset;

        Vector3 aircraftPosition = transform.position;
        aircraftPosition.y = runwaySurfaceY;

        Vector3 runwayEnd =
            runwayCenter +
            landingDirection * (runwayLength * 0.5f);

        float remainingRunwayDistance =
            Vector3.Dot(
                runwayEnd - aircraftPosition,
                landingDirection
            );

        if (remainingRunwayDistance <= 0f)
        {
            Fail(
                "The aircraft is at or beyond the runway end; " +
                "no forward runway exit can be selected."
            );
            return;
        }

        List<ExitCandidate> exits =
            FindForwardRunwayExits(
                aircraftPosition,
                remainingRunwayDistance,
                runwayBuilding
            );

        if (exits.Count == 0)
        {
            Fail(
                "No compatible taxiway exit was found ahead " +
                "of the landed aircraft."
            );
            return;
        }

        parkingTarget = reservedSpot.ParkingWorldPosition;
        parkingTarget.y = taxiWorldY;

        List<TargetCandidate> targets =
            FindNearbyTaxiwayTargets(parkingTarget);

        if (targets.Count == 0)
        {
            Fail(
                "No suitable taxiway centerline was found near " +
                "the reserved grass parking spot."
            );
            return;
        }

        bool foundRoute = false;
        ExitCandidate chosenExit = default;
        TargetCandidate chosenTarget = default;
        List<Vector2Int> chosenPath = null;
        int attempts = 0;

        foreach (ExitCandidate exit in exits)
        {
            foreach (TargetCandidate target in targets)
            {
                Vector3 targetWorld =
                    gridManager.GridToWorldCenter(target.Cell);

                targetWorld.y = taxiWorldY;

                float connectorDistance =
                    maximumParkingConnectorDistance *
                    gridManager.CellSize;

                if (HorizontalDistance(
                        targetWorld,
                        parkingTarget) > connectorDistance)
                {
                    continue;
                }

                if (attempts >= maximumRouteAttempts)
                    break;

                attempts++;

                List<Vector2Int> candidatePath =
                    taxiwayPathfinder.FindPath(
                        exit.Cell,
                        target.Cell,
                        minimumTaxiwayWidth
                    );

                if (candidatePath == null ||
                    candidatePath.Count == 0)
                {
                    continue;
                }

                chosenExit = exit;
                chosenTarget = target;
                chosenPath = candidatePath;
                foundRoute = true;
                break;
            }

            if (foundRoute ||
                attempts >= maximumRouteAttempts)
            {
                break;
            }
        }

        if (!foundRoute)
        {
            Fail(
                "No connected taxiway route was found between " +
                "a runway exit and the reserved grass parking spot."
            );
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

        currentRouteIndex = 1;
        phase = TaxiPhase.RollingToRunwayExit;

        Vector3 adjustedPosition = transform.position;
        adjustedPosition.y = taxiWorldY;
        transform.position = adjustedPosition;

        debugStatus =
            $"VFR taxi route found. Runway exit {chosenExit.Cell}, " +
            $"taxiway target {chosenTarget.Cell}, " +
            $"{route.Count} route cells.";

        if (logTaxiProgress)
        {
            Debug.Log(
                $"VFR TAXI ROUTE FOUND | " +
                $"Flight: {flightIdentity.FlightNumber} | " +
                $"Parking: {reservedSpot.name} | " +
                $"Exit: {chosenExit.Cell} | " +
                $"Target: {chosenTarget.Cell} | " +
                $"Route cells: {route.Count}",
                this
            );
        }
    }

    private HobbyAircraftParkingSpot FindReservedParkingSpot()
    {
        HobbyAircraftParkingSpot[] spots =
            FindObjectsByType<HobbyAircraftParkingSpot>(
                FindObjectsSortMode.None
            );

        foreach (HobbyAircraftParkingSpot spot in spots)
        {
            if (spot != null &&
                spot.IsPlacedBuilding &&
                spot.ReservedAircraft == gameObject)
            {
                return spot;
            }
        }

        return null;
    }

    private List<ExitCandidate> FindForwardRunwayExits(
        Vector3 aircraftPosition,
        float remainingRunwayDistance,
        PlacedBuilding runwayBuilding)
    {
        List<ExitCandidate> candidates =
            new List<ExitCandidate>();

        int minX = runwayBuilding.Origin.x;
        int maxX =
            runwayBuilding.Origin.x +
            runwayBuilding.Width - 1;

        int minZ = runwayBuilding.Origin.y;
        int maxZ =
            runwayBuilding.Origin.y +
            runwayBuilding.Depth - 1;

        bool runwayAlongX =
            runwayBuilding.Width >= runwayBuilding.Depth;

        Vector3 perpendicularDirection =
            new Vector3(
                -landingDirection.z,
                0f,
                landingDirection.x
            );

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);

                if (!taxiwayNetwork.IsTaxiway(cell) ||
                    !taxiwayNetwork.IsCenterlineCell(cell) ||
                    taxiwayNetwork.GetTaxiwayWidth(cell) <
                    minimumTaxiwayWidth)
                {
                    continue;
                }

                bool connectedExit = false;

                if (runwayAlongX)
                {
                    if (cell.x < minX || cell.x > maxX)
                        continue;

                    connectedExit =
                        (cell.y == maxZ + 1 &&
                         taxiwayNetwork.IsCenterlineCell(
                             new Vector2Int(
                                 cell.x,
                                 cell.y + 1
                             ))) ||
                        (cell.y == minZ - 1 &&
                         taxiwayNetwork.IsCenterlineCell(
                             new Vector2Int(
                                 cell.x,
                                 cell.y - 1
                             )));
                }
                else
                {
                    if (cell.y < minZ || cell.y > maxZ)
                        continue;

                    connectedExit =
                        (cell.x == maxX + 1 &&
                         taxiwayNetwork.IsCenterlineCell(
                             new Vector2Int(
                                 cell.x + 1,
                                 cell.y
                             ))) ||
                        (cell.x == minX - 1 &&
                         taxiwayNetwork.IsCenterlineCell(
                             new Vector2Int(
                                 cell.x - 1,
                                 cell.y
                             )));
                }

                if (!connectedExit)
                    continue;

                Vector3 cellWorld =
                    gridManager.GridToWorldCenter(cell);

                Vector3 delta =
                    cellWorld - aircraftPosition;

                delta.y = 0f;

                float forwardDistance =
                    Vector3.Dot(
                        delta,
                        landingDirection
                    );

                if (forwardDistance <
                    minimumExitForwardDistance ||
                    forwardDistance >
                    remainingRunwayDistance + runwayEndTolerance)
                {
                    continue;
                }

                Vector3 lateralOffset =
                    delta -
                    landingDirection * forwardDistance;

                lateralOffset.y = 0f;

                float lateralDistance =
                    lateralOffset.magnitude;

                if (lateralDistance >
                    maximumRunwayExitLateralDistance)
                {
                    continue;
                }

                int side =
                    Vector3.Dot(
                        lateralOffset,
                        perpendicularDirection
                    ) >= 0f ? 1 : -1;

                candidates.Add(
                    new ExitCandidate(
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
            int comparison =
                a.ForwardDistance.CompareTo(
                    b.ForwardDistance
                );

            return comparison != 0
                ? comparison
                : a.LateralDistance.CompareTo(
                    b.LateralDistance
                );
        });

        return candidates;
    }

    private List<TargetCandidate> FindNearbyTaxiwayTargets(
        Vector3 worldPosition)
    {
        List<TargetCandidate> candidates =
            new List<TargetCandidate>();

        Vector2Int center =
            gridManager.WorldToGrid(worldPosition);

        for (int dx = -connectionSearchRadiusCells;
             dx <= connectionSearchRadiusCells;
             dx++)
        {
            for (int dz = -connectionSearchRadiusCells;
                 dz <= connectionSearchRadiusCells;
                 dz++)
            {
                Vector2Int cell =
                    new Vector2Int(
                        center.x + dx,
                        center.y + dz
                    );

                if (!gridManager.IsInsideGrid(cell) ||
                    !taxiwayNetwork.IsTaxiway(cell) ||
                    !taxiwayNetwork.IsCenterlineCell(cell) ||
                    taxiwayNetwork.GetTaxiwayWidth(cell) <
                    minimumTaxiwayWidth)
                {
                    continue;
                }

                Vector3 cellWorld =
                    gridManager.GridToWorldCenter(cell);

                float distanceSquared =
                    HorizontalDistanceSquared(
                        worldPosition,
                        cellWorld
                    );

                candidates.Add(
                    new TargetCandidate(
                        cell,
                        distanceSquared
                    )
                );
            }
        }

        candidates.Sort(
            (a, b) =>
                a.DistanceSquared.CompareTo(
                    b.DistanceSquared
                )
        );

        return candidates;
    }

    private void MoveToRunwayExit()
    {
        if (MoveToward(
                runwayExitPoint,
                waypointArrivalDistance))
        {
            SetPositionAt(runwayExitPoint);
            phase = TaxiPhase.JoiningTaxiway;

            debugStatus =
                $"Reached runway exit {route[0]}. Joining taxiway.";
        }
    }

    private void MoveToTaxiwayJoin()
    {
        if (MoveToward(
                taxiwayJoinPoint,
                waypointArrivalDistance))
        {
            SetPositionAt(taxiwayJoinPoint);
            phase = TaxiPhase.FollowingTaxiway;

            debugStatus =
                "Joined taxiway. Following route toward grass parking.";
        }
    }

    private void FollowTaxiwayRoute()
    {
        while (currentRouteIndex < route.Count)
        {
            Vector3 waypoint =
                gridManager.GridToWorldCenter(
                    route[currentRouteIndex]
                );

            waypoint.y = taxiWorldY;

            if (HorizontalDistance(
                    transform.position,
                    waypoint) <= waypointArrivalDistance)
            {
                currentRouteIndex++;
                continue;
            }

            MoveToward(
                waypoint,
                waypointArrivalDistance
            );

            return;
        }

        phase = TaxiPhase.ApproachingParking;

        debugStatus =
            $"Taxiway route complete. Approaching {reservedSpot.name}.";
    }

    private void MoveToParkingSpot()
    {
        if (reservedSpot == null ||
            !reservedSpot.IsPlacedBuilding)
        {
            Fail(
                "The reserved grass parking spot was removed during taxiing."
            );
            return;
        }

        parkingTarget =
            reservedSpot.ParkingWorldPosition;

        parkingTarget.y = taxiWorldY;

        if (MoveToward(
                parkingTarget,
                parkingArrivalDistance))
        {
            SetPositionAt(parkingTarget);
            phase = TaxiPhase.Parked;

            debugStatus =
                $"VFR aircraft parked at {reservedSpot.name}.";

            if (logTaxiProgress)
            {
                Debug.Log(
                    $"VFR AIRCRAFT PARKED | " +
                    $"Flight: {flightIdentity.FlightNumber} | " +
                    $"Parking spot: {reservedSpot.name}",
                    this
                );
            }
        }
    }

    private bool MoveToward(
        Vector3 target,
        float arrivalDistance)
    {
        if (HorizontalDistance(
                transform.position,
                target) <= arrivalDistance)
        {
            return true;
        }

        Vector3 movement =
            target - transform.position;

        movement.y = 0f;

        if (movement.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(
                    movement.normalized,
                    Vector3.up
                );

            targetRotation *=
                Quaternion.Euler(modelRotationOffset);

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime
                );
        }

        float speed =
            taxiSpeedKnots *
            worldUnitsPerSecondPerKnot;

        transform.position =
            Vector3.MoveTowards(
                transform.position,
                target,
                speed * Time.deltaTime
            );

        return HorizontalDistance(
            transform.position,
            target) <= arrivalDistance;
    }

    private void SetPositionAt(Vector3 target)
    {
        Vector3 position = target;
        position.y = taxiWorldY;
        transform.position = position;
    }

    private float HorizontalDistance(
        Vector3 a,
        Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;

        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private float HorizontalDistanceSquared(
        Vector3 a,
        Vector3 b)
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
            $"VFR TAXI ROUTE FAILED | " +
            $"{(flightIdentity != null ? flightIdentity.FlightNumber : "Unknown")}: " +
            message,
            this
        );
    }

    private void OnDestroy()
    {
        if (reservedSpot != null)
            reservedSpot.Release(gameObject);
    }
}
