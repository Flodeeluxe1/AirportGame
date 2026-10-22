using System.Collections.Generic;
using UnityEngine;

public class TrafficTestVehicle : MonoBehaviour
{
    public enum VehicleState
    {
        Idle,
        DrivingToParking,
        WaitingForParking,
        AligningParkingApproach,
        ApproachingParkingEntry,
        EnteringParking,
        MovingIntoParking,
        Parked,
        LeavingParking,
        DrivingToExit,
        Arrived
    }

    [Header("References")]
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private IntersectionManager intersectionManager;

    [Header("Intersection Control")]
    [Tooltip("How many road modules before an intersection a vehicle should begin waiting.")]
    [SerializeField, Min(0)] private int intersectionLookAheadModules = 1;

    [Tooltip("How many road modules beyond an intersection the vehicle must travel before releasing it.")]
    [SerializeField, Min(1)] private int intersectionClearModules = 2;

    [Header("Movement")]
    [SerializeField] private float speed = 3f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float heightOffset = 0.2f;

    [Header("Road Turn Smoothing")]
    [Tooltip("Requested turn radius in grid-cell units.")]
    [SerializeField, Min(0f)]
    private float turningRadiusInCells = 2f;

    [Tooltip("Number of segments used to approximate each Bezier curve.")]
    [SerializeField, Min(1)]
    private int turnCurveResolution = 8;

    private List<Vector3> smoothedMovementWaypoints =
        new List<Vector3>();

    private int currentSmoothedWaypointIndex;

    [Header("Parking")]
    [SerializeField] private float parkingDuration = 5f;

    [Tooltip("Distance outside the parking edge for the straight entry approach.")]
    [SerializeField] private float parkingEntryClearance = 0.75f;

    [Tooltip("Distance tolerance for reaching a parking waypoint.")]
    [SerializeField] private float parkingWaypointTolerance = 0.03f;

    [Header("Traffic Following")]
    [SerializeField] private LayerMask trafficLayer;
    [SerializeField] private float trafficDetectionDistance = 3f;
    [SerializeField] private float safeFollowingDistance = 0.7f;
    [SerializeField] private float rayOriginHeight = 0.15f;
    [SerializeField] private float rayOriginForwardOffset = 0.3f;
    [SerializeField] private float acceleration = 4f;
    [SerializeField] private float braking = 8f;

    [Header("Debug - Runtime Information")]
    public string debugState = "Idle";
    public float debugCurrentSpeed;

    [Tooltip("Most recent collider hit by any traffic sensor ray.")]
    public string debugLastRayHitCollider = "None";

    public string debugLastRayDirection = "None";
    public float debugLastRayDistance;

    [Tooltip("Closest vehicle currently blocking this car.")]
    public string debugBlockingCollider = "None";

    public string debugBlockingVehicleState = "None";
    public float debugBlockingDistance;

    [Tooltip("Results from the five traffic sensor rays.")]
    [TextArea(3, 8)]
    public string debugRaycastSummary = "Not tested";

    public string debugTrafficDecision = "Not tested";
    public float debugLastSensorUpdateTime;

    private Vector2Int startCell;
    private Vector2Int exitCell;

    private List<Vector2Int> path = new List<Vector2Int>();

    private ParkingSpace parkingSpace;
    private Vector2Int parkingApproachCell;

    private Vector3 parkingAlignmentPosition;
    private Vector3 parkingPreEntryPosition;
    private Vector3 parkingEntryPosition;
    private Vector3 parkingCenterPosition;

    private int leavingParkingStep;

    private Vector3 parkingOutwardDirection;
    private Vector2Int parkingExitStartCell;

    private float parkingTimer;
    private float currentSpeed;

    private VehicleState state = VehicleState.Idle;
    public VehicleState CurrentState => state;

    // Intersection reservation currently held by this vehicle.
    private bool hasIntersectionReservation;
    private Vector2Int reservedIntersection;
    private int reservedIntersectionPathIndex = -1;

    // Intersection this vehicle is currently waiting to enter.
    private bool waitingForIntersection;
    private Vector2Int requestedIntersection;
    private int requestedIntersectionPathIndex = -1;

    // ---------------------------------------------------------
    // INITIALIZATION
    // ---------------------------------------------------------

    public void InitializeRoute(
        Vector2Int startCell,
        Vector2Int exitCell)
    {
        this.startCell = startCell;
        this.exitCell = exitCell;

        currentSpeed = speed;

        StartRoute();
    }

    private void Awake()
    {
        if (roadNetwork == null)
        {
            roadNetwork = FindFirstObjectByType<RoadNetwork>();
        }

        if (gridManager == null)
        {
            gridManager = FindFirstObjectByType<GridManager>();
        }

        if (intersectionManager == null)
        {
            intersectionManager = FindFirstObjectByType<IntersectionManager>();
        }

        currentSpeed = speed;
        RefreshDebugInformation();
    }

    private void StartRoute()
    {
        if (roadNetwork == null)
        {
            Debug.LogError(
                "TrafficTestVehicle: RoadNetwork is missing.");
            return;
        }

        if (gridManager == null)
        {
            Debug.LogError(
                "TrafficTestVehicle: GridManager is missing.");
            return;
        }

        if (!TryFindParkingSpace())
        {
            Debug.Log(
                $"TRAFFIC NO PARKING CANDIDATE | Start: {startCell}");

            state = VehicleState.Idle;
            return;
        }

        if (!parkingSpace.Reserve(gameObject))
        {
            Debug.Log("TRAFFIC FAILED TO RESERVE PARKING.");

            state = VehicleState.Idle;
            return;
        }

        path = roadNetwork.FindPath(
            startCell,
            parkingApproachCell);

        if (path == null || path.Count == 0)
        {
            parkingSpace.CancelReservation(gameObject);
            parkingSpace = null;

            Debug.Log(
                $"TRAFFIC NO PATH TO PARKING | " +
                $"Start: {startCell} | " +
                $"Approach: {parkingApproachCell}");

            state = VehicleState.Idle;
            return;
        }

        Debug.Log(
            $"TRAFFIC PARKING ROUTE | " +
            $"Start: {startCell} | " +
            $"Approach: {parkingApproachCell} | " +
            $"Queue Position: {parkingSpace.GetQueuePosition(gameObject)}");

        LogPath("PARKING PATH");

        InitializeSmoothedMovementPath();

        if (path.Count == 1)
        {
            BeginParkingEntry();
            return;
        }

        state = VehicleState.DrivingToParking;
    }

    private void Update()
    {
        switch (state)
        {
            case VehicleState.DrivingToParking:
            case VehicleState.DrivingToExit:
                MoveAlongPath();
                break;

            case VehicleState.WaitingForParking:
                UpdateParkingWaiting();
                break;

            case VehicleState.AligningParkingApproach:
                MoveToParkingAlignment();
                break;

            case VehicleState.ApproachingParkingEntry:
                MoveToParkingPreEntry();
                break;

            case VehicleState.EnteringParking:
                MoveToParkingEdge();
                break;

            case VehicleState.MovingIntoParking:
                MoveIntoParking();
                break;

            case VehicleState.Parked:
                UpdateParking();
                break;

            case VehicleState.LeavingParking:
                LeaveParking();
                break;
        }

        RefreshDebugInformation();
    }

    private void RefreshDebugInformation()
    {
        debugState = state.ToString();
        debugCurrentSpeed = currentSpeed;
    }

    // ---------------------------------------------------------
    // PARKING SPACE SELECTION
    // ---------------------------------------------------------

    private bool TryFindParkingSpace()
    {
        ParkingSpace[] parkingSpaces =
            FindObjectsByType<ParkingSpace>(
                FindObjectsSortMode.None);

        if (parkingSpaces.Length == 0)
            return false;

        ParkingSpace bestFreeSpace = null;
        Vector2Int bestFreeApproach = Vector2Int.zero;
        int bestFreePathLength = int.MaxValue;

        foreach (ParkingSpace candidate in parkingSpaces)
        {
            if (candidate == null ||
                candidate.IsOccupied ||
                candidate.IsReserved)
            {
                continue;
            }

            if (!TryGetApproachCells(
                    candidate,
                    out List<Vector2Int> approachCells))
            {
                continue;
            }

            foreach (Vector2Int approachCell in approachCells)
            {
                List<Vector2Int> candidatePath =
                    roadNetwork.FindPath(startCell, approachCell);

                if (candidatePath == null ||
                    candidatePath.Count == 0)
                {
                    continue;
                }

                if (candidatePath.Count < bestFreePathLength)
                {
                    bestFreePathLength = candidatePath.Count;
                    bestFreeSpace = candidate;
                    bestFreeApproach = approachCell;
                }
            }
        }

        if (bestFreeSpace != null)
        {
            parkingSpace = bestFreeSpace;
            parkingApproachCell = bestFreeApproach;
            return true;
        }

        ParkingSpace bestFutureSpace = null;
        Vector2Int bestFutureApproach = Vector2Int.zero;
        float bestAvailableTime = float.MaxValue;
        int bestFuturePathLength = int.MaxValue;

        foreach (ParkingSpace candidate in parkingSpaces)
        {
            if (candidate == null)
                continue;

            if (!TryGetApproachCells(
                    candidate,
                    out List<Vector2Int> approachCells))
            {
                continue;
            }

            float availableTime =
                candidate.GetEstimatedAvailableTime(parkingDuration);

            foreach (Vector2Int approachCell in approachCells)
            {
                List<Vector2Int> candidatePath =
                    roadNetwork.FindPath(startCell, approachCell);

                if (candidatePath == null ||
                    candidatePath.Count == 0)
                {
                    continue;
                }

                if (availableTime < bestAvailableTime ||
                    (Mathf.Approximately(
                         availableTime,
                         bestAvailableTime) &&
                     candidatePath.Count < bestFuturePathLength))
                {
                    bestAvailableTime = availableTime;
                    bestFuturePathLength = candidatePath.Count;
                    bestFutureSpace = candidate;
                    bestFutureApproach = approachCell;
                }
            }
        }

        if (bestFutureSpace == null)
            return false;

        parkingSpace = bestFutureSpace;
        parkingApproachCell = bestFutureApproach;

        float waitTime = Mathf.Max(
            0f,
            bestAvailableTime - Time.time);

        Debug.Log(
            $"TRAFFIC WAITING PARKING SELECTED | " +
            $"Estimated Available In: {waitTime:F1}s");

        return true;
    }

    private bool TryGetApproachCells(
        ParkingSpace space,
        out List<Vector2Int> approachCells)
    {
        approachCells = new List<Vector2Int>();

        if (space == null || roadNetwork == null)
            return false;

        PlacedBuilding building =
            space.GetComponent<PlacedBuilding>();

        if (building == null)
            return false;

        Vector2Int origin = building.Origin;
        int width = building.Width;
        int depth = building.Depth;

        if (width <= 0 || depth <= 0)
            return false;

        for (int z = 0; z < depth; z++)
        {
            AddApproachIfRoad(
                approachCells,
                origin + new Vector2Int(-1, z),
                origin,
                width,
                depth);

            AddApproachIfRoad(
                approachCells,
                origin + new Vector2Int(width, z),
                origin,
                width,
                depth);
        }

        for (int x = 0; x < width; x++)
        {
            AddApproachIfRoad(
                approachCells,
                origin + new Vector2Int(x, -1),
                origin,
                width,
                depth);

            AddApproachIfRoad(
                approachCells,
                origin + new Vector2Int(x, depth),
                origin,
                width,
                depth);
        }

        return approachCells.Count > 0;
    }

    private void AddApproachIfRoad(
        List<Vector2Int> approachCells,
        Vector2Int cell,
        Vector2Int buildingOrigin,
        int buildingWidth,
        int buildingDepth)
    {
        if (roadNetwork == null)
            return;

        Vector2Int anchor = roadNetwork.GetRoadAnchor(cell);
        int moduleSize = RoadNetwork.RoadModuleSize;

        int buildingMaxX =
            buildingOrigin.x + buildingWidth - 1;

        int buildingMaxZ =
            buildingOrigin.y + buildingDepth - 1;

        int roadMaxX = anchor.x + moduleSize - 1;
        int roadMaxZ = anchor.y + moduleSize - 1;

        bool overlapsBuilding =
            anchor.x <= buildingMaxX &&
            roadMaxX >= buildingOrigin.x &&
            anchor.y <= buildingMaxZ &&
            roadMaxZ >= buildingOrigin.y;

        if (overlapsBuilding)
            return;

        if (!roadNetwork.IsRoad(anchor))
            return;

        if (!approachCells.Contains(anchor))
            approachCells.Add(anchor);
    }

    // ---------------------------------------------------------
    // PARKING MANEUVER GEOMETRY
    // ---------------------------------------------------------

    private bool ConfigureParkingManeuver()
    {
        if (parkingSpace == null ||
            gridManager == null ||
            roadNetwork == null)
        {
            return false;
        }

        PlacedBuilding building =
            parkingSpace.GetComponent<PlacedBuilding>();

        if (building == null ||
            building.Width <= 0 ||
            building.Depth <= 0)
        {
            Debug.LogError(
                "TrafficTestVehicle: Parking space has no valid PlacedBuilding.",
                this);

            return false;
        }

        parkingCenterPosition =
            GetParkingWorldPosition(parkingSpace);

        Vector3 roadPosition =
            GetRoadWorldPosition(parkingApproachCell);

        Vector3 directionToRoad =
            roadPosition - parkingCenterPosition;

        directionToRoad.y = 0f;

        if (directionToRoad.sqrMagnitude < 0.001f)
        {
            Debug.LogError(
                "TrafficTestVehicle: Road approach is too close " +
                "to the parking center to determine an exit direction.",
                this);

            return false;
        }

        Vector3 outwardDirection;

        if (Mathf.Abs(directionToRoad.x) >
            Mathf.Abs(directionToRoad.z))
        {
            outwardDirection =
                directionToRoad.x >= 0f
                    ? Vector3.right
                    : Vector3.left;

            float halfWidth =
                building.Width * gridManager.CellSize * 0.5f;

            parkingEntryPosition =
                parkingCenterPosition +
                outwardDirection * halfWidth;
        }
        else
        {
            outwardDirection =
                directionToRoad.z >= 0f
                    ? Vector3.forward
                    : Vector3.back;

            float halfDepth =
                building.Depth * gridManager.CellSize * 0.5f;

            parkingEntryPosition =
                parkingCenterPosition +
                outwardDirection * halfDepth;
        }

        parkingEntryPosition.y =
            parkingCenterPosition.y;

        float clearance = Mathf.Max(
            0.01f,
            parkingEntryClearance);

        parkingPreEntryPosition =
            parkingEntryPosition +
            outwardDirection * clearance;

        parkingPreEntryPosition.y =
            parkingCenterPosition.y;

        parkingAlignmentPosition = roadPosition;

        if (Mathf.Abs(outwardDirection.x) > 0.5f)
        {
            parkingAlignmentPosition.z =
                parkingPreEntryPosition.z;
        }
        else
        {
            parkingAlignmentPosition.x =
                parkingPreEntryPosition.x;
        }

        parkingAlignmentPosition.y =
            roadPosition.y;

        Vector3 exitDirection =
            parkingEntryPosition - parkingCenterPosition;

        exitDirection.y = 0f;

        if (exitDirection.sqrMagnitude < 0.001f)
        {
            Debug.LogError(
                "TrafficTestVehicle: Invalid parking exit direction.",
                this);

            return false;
        }

        parkingOutwardDirection =
            exitDirection.normalized;

        Debug.Log(
            $"PARKING MANEUVER CONFIGURED | " +
            $"Approach: {parkingApproachCell} | " +
            $"Outward: {parkingOutwardDirection} | " +
            $"Alignment: {parkingAlignmentPosition} | " +
            $"Pre-entry: {parkingPreEntryPosition} | " +
            $"Entry: {parkingEntryPosition} | " +
            $"Center: {parkingCenterPosition}");

        return true;
    }

    private void BeginParkingEntry()
    {
        if (parkingSpace == null)
        {
            state = VehicleState.Idle;
            return;
        }

        if (!parkingSpace.CanVehicleEnter(gameObject))
        {
            state = VehicleState.WaitingForParking;

            Debug.Log(
                $"TRAFFIC WAITING AT PARKING APPROACH | " +
                $"Cell: {parkingApproachCell} | " +
                $"Queue Position: {parkingSpace.GetQueuePosition(gameObject)}");

            return;
        }

        if (!ConfigureParkingManeuver())
        {
            state = VehicleState.Idle;
            return;
        }

        Debug.Log(
            $"TRAFFIC ALIGNING FOR PARKING | " +
            $"Cell: {parkingApproachCell}");

        state = VehicleState.AligningParkingApproach;
    }

    private void UpdateParkingWaiting()
    {
        if (parkingSpace == null)
        {
            state = VehicleState.Idle;
            return;
        }

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            0f,
            braking * Time.deltaTime);

        if (!parkingSpace.CanVehicleEnter(gameObject))
            return;

        Debug.Log("TRAFFIC PARKING SPACE NOW AVAILABLE");

        BeginParkingEntry();
    }

    private void MoveToParkingAlignment()
    {
        MoveTowards(parkingAlignmentPosition, speed);

        if (!ReachedPosition(parkingAlignmentPosition))
            return;

        transform.position = parkingAlignmentPosition;

        Debug.Log("TRAFFIC PARKING APPROACH ALIGNED");

        state = VehicleState.ApproachingParkingEntry;
    }

    private void MoveToParkingPreEntry()
    {
        MoveTowards(parkingPreEntryPosition, speed);

        if (!ReachedPosition(parkingPreEntryPosition))
            return;

        transform.position = parkingPreEntryPosition;

        Debug.Log("TRAFFIC REACHED PARKING PRE-ENTRY");

        state = VehicleState.EnteringParking;
    }

    private void MoveToParkingEdge()
    {
        MoveTowards(parkingEntryPosition, speed);

        if (!ReachedPosition(parkingEntryPosition))
            return;

        transform.position = parkingEntryPosition;

        Debug.Log("TRAFFIC REACHED PARKING EDGE");

        state = VehicleState.MovingIntoParking;
    }

    private void MoveIntoParking()
    {
        MoveTowards(parkingCenterPosition, speed);

        if (!ReachedPosition(parkingCenterPosition))
            return;

        transform.position = parkingCenterPosition;

        if (!parkingSpace.ParkVehicle(
                gameObject,
                parkingDuration))
        {
            Debug.LogError(
                "TrafficTestVehicle: Failed to occupy parking space.");

            state = VehicleState.Idle;
            return;
        }

        parkingTimer = parkingDuration;
        currentSpeed = speed;
        state = VehicleState.Parked;

        Debug.Log(
            $"TRAFFIC PARKED | Duration: {parkingDuration} seconds");
    }

    private bool ReachedPosition(Vector3 targetPosition)
    {
        return Vector3.Distance(
            transform.position,
            targetPosition) <= parkingWaypointTolerance;
    }

    // ---------------------------------------------------------
    // PARKING WAIT, STAY AND DEPARTURE
    // ---------------------------------------------------------

    private void UpdateParking()
    {
        parkingTimer -= Time.deltaTime;

        if (parkingTimer > 0f)
            return;

        Debug.Log("TRAFFIC PARKING COMPLETE");

        leavingParkingStep = 0;
        state = VehicleState.LeavingParking;
    }

    private void LeaveParking()
    {
        // Rotate outward before beginning the departure maneuver.
        if (leavingParkingStep == 0)
        {
            if (!RotateTowardsDirection(parkingOutwardDirection))
                return;

            leavingParkingStep = 1;
        }

        Vector3 targetPosition;

        switch (leavingParkingStep)
        {
            case 1:
                targetPosition = parkingEntryPosition;
                break;

            case 2:
                targetPosition = parkingPreEntryPosition;
                break;

            case 3:
                targetPosition = parkingAlignmentPosition;
                break;

            default:
                FinishLeavingParking();
                return;
        }

        MoveTowards(
            targetPosition,
            GetTrafficFollowingSpeed());

        if (!ReachedPosition(targetPosition))
            return;

        transform.position = targetPosition;

        leavingParkingStep++;

        if (leavingParkingStep > 3)
        {
            FinishLeavingParking();
        }
    }

    private bool RotateTowardsDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return true;

        Quaternion targetRotation =
            Quaternion.LookRotation(
                direction.normalized,
                Vector3.up);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * 100f * Time.deltaTime);

        return Quaternion.Angle(
            transform.rotation,
            targetRotation) < 2f;
    }

    private void FinishLeavingParking()
    {
        if (parkingSpace != null)
        {
            parkingSpace.ReleaseVehicle(gameObject);
        }

        parkingExitStartCell =
            gridManager.WorldToGrid(parkingAlignmentPosition);

        Debug.Log(
            $"TRAFFIC LEFT PARKING | " +
            $"A* Start Cell: {parkingExitStartCell} | " +
            $"Exit: {exitCell}");

        if (intersectionManager != null)
            intersectionManager.CancelRequest(this);

        hasIntersectionReservation = false;
        waitingForIntersection = false;
        reservedIntersectionPathIndex = -1;
        requestedIntersectionPathIndex = -1;

        path = roadNetwork.FindPath(
            parkingExitStartCell,
            exitCell);

        if (path == null || path.Count == 0)
        {
            Debug.Log(
                $"TRAFFIC NO PATH TO EXIT | " +
                $"From: {parkingExitStartCell} | " +
                $"Exit: {exitCell}");

            smoothedMovementWaypoints.Clear();
            currentSmoothedWaypointIndex = 0;

            state = VehicleState.Idle;
            return;
        }

        LogPath("EXIT PATH");

        if (path.Count == 1)
        {
            Debug.Log(
                $"TRAFFIC ARRIVED AT EXIT | Cell: {exitCell}");

            state = VehicleState.Arrived;
            Destroy(gameObject);
            return;
        }

        // Build a fresh smoothed path for the departure route.
        InitializeSmoothedMovementPath();

        state = VehicleState.DrivingToExit;
    }

    // ---------------------------------------------------------
    // ROAD PATH SMOOTHING
    // ---------------------------------------------------------

    private void InitializeSmoothedMovementPath()
    {
        smoothedMovementWaypoints =
            BuildSmoothedMovementWaypoints(path);

        // Waypoint zero is the route's starting point.
        // The vehicle proceeds toward waypoint one.
        currentSmoothedWaypointIndex =
            smoothedMovementWaypoints.Count > 1 ? 1 : 0;
    }

    private List<Vector3> BuildSmoothedMovementWaypoints(
        List<Vector2Int> cells)
    {
        List<Vector3> originalWaypoints =
            new List<Vector3>();

        List<Vector3> smoothedWaypoints =
            new List<Vector3>();

        if (cells == null || cells.Count == 0)
            return smoothedWaypoints;

        foreach (Vector2Int cell in cells)
        {
            originalWaypoints.Add(
                GetRoadWorldPosition(cell));
        }

        if (originalWaypoints.Count <= 2)
            return originalWaypoints;

        int waypointCount = originalWaypoints.Count;

        float[] turnRadii = new float[waypointCount];

        float requestedRadius =
            turningRadiusInCells * gridManager.CellSize;

        // Identify each corner and calculate its curve radius.
        for (int i = 1; i < waypointCount - 1; i++)
        {
            Vector3 incoming =
                originalWaypoints[i] -
                originalWaypoints[i - 1];

            Vector3 outgoing =
                originalWaypoints[i + 1] -
                originalWaypoints[i];

            incoming.y = 0f;
            outgoing.y = 0f;

            if (incoming.sqrMagnitude < 0.001f ||
                outgoing.sqrMagnitude < 0.001f)
            {
                continue;
            }

            incoming.Normalize();
            outgoing.Normalize();

            float turnAngle =
                Vector3.Angle(incoming, outgoing);

            // Ignore straight segments and near-reversals.
            if (turnAngle < 1f || turnAngle > 179f)
                continue;

            float incomingLength = Vector3.Distance(
                originalWaypoints[i - 1],
                originalWaypoints[i]);

            float outgoingLength = Vector3.Distance(
                originalWaypoints[i],
                originalWaypoints[i + 1]);

            // Limit the radius so adjacent turns do not overlap.
            turnRadii[i] = Mathf.Min(
                requestedRadius,
                incomingLength * 0.5f,
                outgoingLength * 0.5f);
        }

        smoothedWaypoints.Add(originalWaypoints[0]);

        const float bezierCircleFactor = 0.55228475f;

        int resolution = Mathf.Max(1, turnCurveResolution);

        // Process corners in route order.
        for (int i = 1; i < waypointCount - 1; i++)
        {
            Vector3 corner = originalWaypoints[i];
            float radius = turnRadii[i];

            if (radius <= 0.001f)
            {
                AddWaypointIfDifferent(
                    smoothedWaypoints,
                    corner);

                continue;
            }

            Vector3 incoming =
                (corner - originalWaypoints[i - 1]).normalized;

            Vector3 outgoing =
                (originalWaypoints[i + 1] - corner).normalized;

            incoming.y = 0f;
            outgoing.y = 0f;

            incoming.Normalize();
            outgoing.Normalize();

            // The curve starts before the original corner.
            Vector3 entry =
                corner - incoming * radius;

            // It finishes beyond the original corner.
            Vector3 exit =
                corner + outgoing * radius;

            AddWaypointIfDifferent(
                smoothedWaypoints,
                entry);

            Vector3 control1 =
                entry +
                incoming * radius * bezierCircleFactor;

            Vector3 control2 =
                exit -
                outgoing * radius * bezierCircleFactor;

            // Sample the cubic Bezier into movement waypoints.
            for (int step = 1; step <= resolution; step++)
            {
                float t = step / (float)resolution;

                Vector3 point = EvaluateCubicBezier(
                    entry,
                    control1,
                    control2,
                    exit,
                    t);

                AddWaypointIfDifferent(
                    smoothedWaypoints,
                    point);
            }
        }

        // Retain the original route's final destination.
        AddWaypointIfDifferent(
            smoothedWaypoints,
            originalWaypoints[waypointCount - 1]);

        return smoothedWaypoints;
    }

    private Vector3 EvaluateCubicBezier(
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        float t)
    {
        float u = 1f - t;

        return
            u * u * u * p0 +
            3f * u * u * t * p1 +
            3f * u * t * t * p2 +
            t * t * t * p3;
    }

    private void AddWaypointIfDifferent(
        List<Vector3> waypoints,
        Vector3 position)
    {
        if (waypoints.Count == 0 ||
            Vector3.Distance(
                waypoints[waypoints.Count - 1],
                position) > 0.001f)
        {
            waypoints.Add(position);
        }
    }

    // ---------------------------------------------------------
    // ROAD PATH FOLLOWING
    // ---------------------------------------------------------

    private void MoveAlongPath()
    {
        if (path == null || path.Count == 0)
        {
            HandleRoadPathFinished();
            return;
        }

        // Do not let road traffic enter an intersection until it owns the
        // reservation. Parking maneuvers are intentionally unaffected.
        if (HandleIntersectionControl())
            return;

        if (smoothedMovementWaypoints == null ||
            smoothedMovementWaypoints.Count == 0)
        {
            InitializeSmoothedMovementPath();
        }

        if (currentSmoothedWaypointIndex >=
            smoothedMovementWaypoints.Count)
        {
            HandleRoadPathFinished();
            return;
        }

        Vector3 targetPosition =
            smoothedMovementWaypoints[
                currentSmoothedWaypointIndex];

        float movementSpeed = GetTrafficFollowingSpeed();

        MoveTowards(targetPosition, movementSpeed);

        if (ReachedPosition(targetPosition))
        {
            transform.position = targetPosition;
            currentSmoothedWaypointIndex++;
        }
    }

    /// <summary>
    /// Compares this vehicle's existing entity-ID priority with another
    /// vehicle. A lower entity ID wins, matching ShouldDetectVehicle().
    /// </summary>
    public int CompareIntersectionPriority(TrafficTestVehicle other)
    {
        if (other == null)
            return -1;

        return gameObject.GetEntityId().CompareTo(
            other.gameObject.GetEntityId());
    }

    private bool HandleIntersectionControl()
    {
        if (intersectionManager == null ||
            roadNetwork == null ||
            gridManager == null ||
            path == null ||
            path.Count == 0)
        {
            return false;
        }

        int nearestPathIndex = GetNearestPathIndex();
        bool releasedReservationThisFrame = false;

        // Keep the reservation until the vehicle has moved completely past
        // the junction along its route. Releasing it earlier can allow a
        // crossing vehicle to visually overlap this vehicle.
        if (hasIntersectionReservation)
        {
            int releaseIndex = reservedIntersectionPathIndex +
                               Mathf.Max(1, intersectionClearModules);

            bool routeCleared = nearestPathIndex >= releaseIndex;
            bool pathEnded = nearestPathIndex >= path.Count - 1;

            if (routeCleared || pathEnded)
            {
                intersectionManager.Release(
                    reservedIntersection,
                    this);

                Debug.Log(
                    $"INTERSECTION RELEASED | Vehicle: {name} | " +
                    $"Intersection: {reservedIntersection}");

                hasIntersectionReservation = false;
                reservedIntersectionPathIndex = -1;
                releasedReservationThisFrame = true;
            }
            else
            {
                // This vehicle may continue through the intersection it owns.
                return false;
            }
        }

        // Find the next intersection on the remaining route.
        int nextIntersectionIndex = -1;
        Vector2Int nextIntersection = Vector2Int.zero;

        int scanStart = Mathf.Max(
            0,
            nearestPathIndex + (releasedReservationThisFrame ? 1 : 0));

        for (int i = scanStart; i < path.Count; i++)
        {
            Vector2Int anchor = roadNetwork.GetRoadAnchor(path[i]);

            if (!roadNetwork.IsIntersection(anchor))
                continue;

            nextIntersectionIndex = i;
            nextIntersection = anchor;
            break;
        }

        if (nextIntersectionIndex < 0)
        {
            if (waitingForIntersection)
            {
                intersectionManager.CancelRequest(this);
                waitingForIntersection = false;
                requestedIntersectionPathIndex = -1;
            }

            return false;
        }

        int holdingPathIndex = Mathf.Max(
            0,
            nextIntersectionIndex - Mathf.Max(0, intersectionLookAheadModules));

        // Before the holding area, let normal smoothed movement continue.
        if (nearestPathIndex < holdingPathIndex)
            return false;

        // If the next intersection changed because the route changed, cancel
        // the old request before submitting the new one.
        if (waitingForIntersection &&
            requestedIntersection != nextIntersection)
        {
            intersectionManager.CancelRequest(this);
            waitingForIntersection = false;
        }

        if (!waitingForIntersection)
        {
            requestedIntersection = nextIntersection;
            requestedIntersectionPathIndex = nextIntersectionIndex;
            waitingForIntersection = true;

            intersectionManager.RequestEntry(
                requestedIntersection,
                this);

            Debug.Log(
                $"INTERSECTION REQUESTED | Vehicle: {name} | " +
                $"Intersection: {requestedIntersection} | " +
                $"Holding Path Index: {holdingPathIndex}");
        }

        if (intersectionManager.HasReservation(
                requestedIntersection,
                this))
        {
            reservedIntersection = requestedIntersection;
            reservedIntersectionPathIndex = requestedIntersectionPathIndex;
            hasIntersectionReservation = true;
            waitingForIntersection = false;
            requestedIntersectionPathIndex = -1;

            Debug.Log(
                $"INTERSECTION GRANTED | Vehicle: {name} | " +
                $"Intersection: {reservedIntersection}");

            // Permission has been granted, so movement may continue this frame.
            return false;
        }

        // Brake while waiting. Do not advance the movement waypoint index.
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            0f,
            braking * Time.deltaTime);

        debugTrafficDecision =
            $"Waiting for intersection {requestedIntersection}";

        return true;
    }

    private int GetNearestPathIndex()
    {
        if (path == null || path.Count == 0 || roadNetwork == null || gridManager == null)
            return 0;

        int nearestIndex = 0;
        float nearestDistance = float.MaxValue;
        Vector3 currentPosition = transform.position;
        currentPosition.y = 0f;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 roadPosition = GetRoadWorldPosition(path[i]);
            roadPosition.y = 0f;

            float distance = (roadPosition - currentPosition).sqrMagnitude;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    private void OnDisable()
    {
        // Clean up parking reservations when this vehicle
        // is disabled or destroyed.
        if (parkingSpace != null)
        {
            parkingSpace.CancelReservation(gameObject);
            parkingSpace.ReleaseVehicle(gameObject);
        }


        if (intersectionManager != null)
        {
            intersectionManager.CancelRequest(this);

            if (hasIntersectionReservation)
            {
                intersectionManager.Release(reservedIntersection, this);
            }
        }

        hasIntersectionReservation = false;
        waitingForIntersection = false;
        reservedIntersectionPathIndex = -1;
        requestedIntersectionPathIndex = -1;
    }

    private void HandleRoadPathFinished()
    {
        if (state == VehicleState.DrivingToParking)
        {
            BeginParkingEntry();
            return;
        }

        if (state == VehicleState.DrivingToExit)
        {
            Debug.Log(
                $"TRAFFIC ARRIVED AT EXIT | Cell: {exitCell}");

            state = VehicleState.Arrived;
            Destroy(gameObject);
        }
    }

    // ---------------------------------------------------------
    // TRAFFIC FOLLOWING
    // ---------------------------------------------------------

    private float GetTrafficFollowingSpeed()
    {
        float targetSpeed = speed;

        if (TryGetVehicleAhead(out RaycastHit hit))
        {
            float safeDistance =
                Mathf.Max(0.01f, safeFollowingDistance);

            float detectionDistance =
                Mathf.Max(
                    trafficDetectionDistance,
                    safeDistance + 0.01f);

            float normalizedDistance =
                Mathf.InverseLerp(
                    safeDistance,
                    detectionDistance,
                    hit.distance);

            targetSpeed = Mathf.Lerp(
                0f,
                speed,
                normalizedDistance);

            debugTrafficDecision =
                $"Braking for {debugBlockingCollider}";
        }
        else
        {
            debugTrafficDecision =
                "No blocking vehicle detected";
        }

        float rate =
            targetSpeed < currentSpeed
                ? braking
                : acceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            rate * Time.deltaTime);

        return currentSpeed;
    }

    private bool TryGetVehicleAhead(out RaycastHit hit)
    {
        debugLastRayHitCollider = "None";
        debugLastRayDirection = "None";
        debugLastRayDistance = 0f;

        debugBlockingCollider = "None";
        debugBlockingVehicleState = "None";
        debugBlockingDistance = 0f;

        debugRaycastSummary = "";
        debugTrafficDecision = "Checking traffic";
        debugLastSensorUpdateTime = Time.time;

        // Ignore traffic-following raycasts while holding a valid
        // intersection reservation. Intersection control still runs normally.
        if (hasIntersectionReservation &&
            intersectionManager != null &&
            intersectionManager.HasReservation(reservedIntersection, this))
        {
            hit = default;

            debugRaycastSummary =
                "Traffic raycasts skipped: holding intersection reservation.";

            debugTrafficDecision =
                "Continuing through reserved intersection";

            return false;
        }

        Vector3 origin =
            transform.position +
            Vector3.up * rayOriginHeight +
            transform.forward * rayOriginForwardOffset;

        Vector3 forward = transform.forward;

        Vector3 left60 =
            Quaternion.Euler(0f, -60f, 0f) * forward;

        Vector3 left30 =
            Quaternion.Euler(0f, -30f, 0f) * forward;

        Vector3 right30 =
            Quaternion.Euler(0f, 30f, 0f) * forward;

        Vector3 right60 =
            Quaternion.Euler(0f, 60f, 0f) * forward;

        bool foundVehicle = false;
        float closestDistance = float.MaxValue;
        RaycastHit closestHit = new RaycastHit();

        CheckTrafficRay(
            origin, left60, "Left 60",
            ref foundVehicle, ref closestDistance, ref closestHit);

        CheckTrafficRay(
            origin, left30, "Left 30",
            ref foundVehicle, ref closestDistance, ref closestHit);

        CheckTrafficRay(
            origin, forward, "Forward",
            ref foundVehicle, ref closestDistance, ref closestHit);

        CheckTrafficRay(
            origin, right30, "Right 30",
            ref foundVehicle, ref closestDistance, ref closestHit);

        CheckTrafficRay(
            origin, right60, "Right 60",
            ref foundVehicle, ref closestDistance, ref closestHit);

        hit = closestHit;

        if (foundVehicle)
        {
            debugBlockingCollider = closestHit.collider.name;

            TrafficTestVehicle blockingVehicle =
                closestHit.collider
                    .GetComponentInParent<TrafficTestVehicle>();

            if (blockingVehicle != null)
            {
                debugBlockingVehicleState =
                    blockingVehicle.state.ToString();
            }

            debugBlockingDistance = closestHit.distance;

            debugTrafficDecision =
                $"Blocking vehicle at {closestHit.distance:F2}m";
        }
        else
        {
            debugTrafficDecision =
                "No blocking vehicle detected";
        }

        return foundVehicle;
    }

    private void CheckTrafficRay(
        Vector3 origin,
        Vector3 direction,
        string rayName,
        ref bool foundVehicle,
        ref float closestDistance,
        ref RaycastHit closestHit)
    {
        if (!Physics.Raycast(
                origin,
                direction,
                out RaycastHit rayHit,
                trafficDetectionDistance,
                trafficLayer,
                QueryTriggerInteraction.Ignore))
        {
            debugRaycastSummary +=
                $"{rayName}: no hit\n";

            return;
        }

        debugLastRayHitCollider = rayHit.collider.name;
        debugLastRayDirection = rayName;
        debugLastRayDistance = rayHit.distance;

        debugRaycastSummary +=
            $"{rayName}: {rayHit.collider.name} " +
            $"({rayHit.distance:F2}m)";

        TrafficTestVehicle vehicle =
            rayHit.collider
                .GetComponentInParent<TrafficTestVehicle>();

        if (vehicle == null)
        {
            debugRaycastSummary +=
                " — no TrafficTestVehicle component\n";

            return;
        }

        if (vehicle == this)
        {
            debugRaycastSummary += " — self, ignored\n";
            return;
        }

        // A car waiting for this same intersection must not block
        // the vehicle that currently owns its reservation.
        if (ShouldIgnoreIntersectionWaitingVehicle(vehicle))
        {
            debugRaycastSummary +=
                $" — intersection reservation holder, ignored " +
                $"({vehicle.state})\n";

            return;
        }

        if (!ShouldDetectVehicle(vehicle))
        {
            debugRaycastSummary +=
                $" — ignored by traffic rules ({vehicle.state})\n";

            return;
        }

        debugRaycastSummary +=
            $" — blocking vehicle ({vehicle.state})\n";

        if (rayHit.distance < closestDistance)
        {
            closestDistance = rayHit.distance;
            closestHit = rayHit;
            foundVehicle = true;
        }
    }

    private bool ShouldDetectVehicle(TrafficTestVehicle vehicle)
    {
        if (vehicle == null || vehicle == this)
            return false;

        // Ignore vehicles that are not actively participating in traffic.
        if (vehicle.state == VehicleState.Parked ||
            vehicle.state == VehicleState.Idle ||
            vehicle.state == VehicleState.Arrived)
        {
            return false;
        }

        // Preserve existing parking behavior:
        // A vehicle leaving parking does not wait for road traffic
        // or for vehicles performing parking maneuvers.
        if (state == VehicleState.LeavingParking)
        {
            if (vehicle.state == VehicleState.WaitingForParking ||
                vehicle.state == VehicleState.AligningParkingApproach ||
                vehicle.state == VehicleState.ApproachingParkingEntry ||
                vehicle.state == VehicleState.EnteringParking ||
                vehicle.state == VehicleState.MovingIntoParking)
            {
                return false;
            }

            if (vehicle.state == VehicleState.DrivingToParking ||
                vehicle.state == VehicleState.DrivingToExit)
            {
                return false;
            }
        }

        // Road traffic must still detect a vehicle leaving parking.
        if (vehicle.state == VehicleState.LeavingParking)
        {
            if (state == VehicleState.DrivingToParking ||
                state == VehicleState.DrivingToExit)
            {
                return true;
            }
        }

        // No entity-ID crossing priority here.
        // IntersectionManager controls intersection entry.
        // Keep detecting moving vehicles for normal traffic following.
        return true;
    }

    private bool ShouldIgnoreIntersectionWaitingVehicle(
    TrafficTestVehicle other)
    {
        if (other == null)
            return false;

        // This vehicle must own an intersection reservation.
        if (!hasIntersectionReservation)
            return false;

        if (intersectionManager == null)
            return false;

        // The other vehicle must actually be waiting for an intersection.
        if (!other.waitingForIntersection)
            return false;

        // Both vehicles must be approaching the same intersection.
        if (reservedIntersection != other.requestedIntersection)
            return false;

        // Verify that this vehicle still owns the reservation.
        return intersectionManager.HasReservation(
            reservedIntersection,
            this);
    }

    // ---------------------------------------------------------
    // WORLD POSITIONS AND MOVEMENT
    // ---------------------------------------------------------

    private Vector3 GetRoadWorldPosition(Vector2Int cell)
    {
        Vector3 position =
            roadNetwork.GetRoadCenterWorldPosition(
                cell,
                gridManager);

        position.y += heightOffset;

        return position;
    }

    private Vector3 GetParkingWorldPosition(ParkingSpace space)
    {
        PlacedBuilding building =
            space.GetComponent<PlacedBuilding>();

        Vector3 position =
            gridManager.GridToWorld(building.Origin);

        position += new Vector3(
            building.Width * gridManager.CellSize * 0.5f,
            0f,
            building.Depth * gridManager.CellSize * 0.5f);

        position.y += heightOffset;

        return position;
    }

    private void MoveTowards(
        Vector3 targetPosition,
        float movementSpeed)
    {
        Vector3 movement =
            targetPosition - transform.position;

        movement.y = 0f;

        if (movement.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(
                    movement.normalized,
                    Vector3.up);

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime);
        }

        transform.position =
            Vector3.MoveTowards(
                transform.position,
                targetPosition,
                Mathf.Max(0f, movementSpeed) * Time.deltaTime);
    }

    private void LogPath(string title)
    {
        string pathText = "";

        foreach (Vector2Int cell in path)
        {
            pathText += cell + " ";
        }

        Debug.Log($"{title}: {pathText}");
    }

    // ---------------------------------------------------------
    // TRAFFIC SENSOR GIZMOS
    // ---------------------------------------------------------

    private void OnDrawGizmos()
    {
        Vector3 origin =
            transform.position +
            Vector3.up * rayOriginHeight +
            transform.forward * rayOriginForwardOffset;

        Vector3 forward = transform.forward;

        Vector3 left60 =
            Quaternion.Euler(0f, -60f, 0f) * forward;

        Vector3 left30 =
            Quaternion.Euler(0f, -30f, 0f) * forward;

        Vector3 right30 =
            Quaternion.Euler(0f, 30f, 0f) * forward;

        Vector3 right60 =
            Quaternion.Euler(0f, 60f, 0f) * forward;

        DrawTrafficRayGizmo(
            origin, left60, new Color(1f, 0.5f, 0f));

        DrawTrafficRayGizmo(
            origin, left30, Color.yellow);

        DrawTrafficRayGizmo(
            origin, forward, Color.green);

        DrawTrafficRayGizmo(
            origin, right30, Color.cyan);

        DrawTrafficRayGizmo(
            origin, right60, new Color(0.5f, 0.3f, 1f));
    }

    private void DrawTrafficRayGizmo(
        Vector3 origin,
        Vector3 direction,
        Color color)
    {
        float rayLength = trafficDetectionDistance;

        if (Physics.Raycast(
                origin,
                direction,
                out RaycastHit hit,
                trafficDetectionDistance,
                trafficLayer,
                QueryTriggerInteraction.Ignore))
        {
            TrafficTestVehicle vehicle =
                hit.collider
                    .GetComponentInParent<TrafficTestVehicle>();

            if (vehicle != null &&
                vehicle != this &&
                ShouldDetectVehicle(vehicle))
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(origin, hit.point);
                Gizmos.DrawWireSphere(hit.point, 0.12f);
                return;
            }

            rayLength = hit.distance;
        }

        Gizmos.color = color;

        Gizmos.DrawLine(
            origin,
            origin + direction.normalized * rayLength);

        Gizmos.DrawWireSphere(
            origin + direction.normalized * rayLength,
            0.06f);
    }
}