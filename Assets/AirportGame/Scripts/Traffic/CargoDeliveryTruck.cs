
using System.Collections.Generic;
using UnityEngine;

public class CargoDeliveryTruck : MonoBehaviour
{
    public enum TruckState
    {
        Idle,
        WaitingForCargoZone,
        DrivingToCargoZone,
        AligningVanStopApproach,
        ApproachingVanStopEntry,
        DrivingToVanStop,
        Unloading,
        ReturningToRoad,
        DrivingToExit,
        Arrived
    }

    private static readonly HashSet<PlacedBuilding> ReservedCargoZones =
        new HashSet<PlacedBuilding>();

    private const int CargoBoxCount = 10;

    [Header("References")]
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private IntersectionManager intersectionManager;

    [Header("Intersection Control")]
    [Tooltip("How many road modules before an intersection the truck should begin waiting.")]
    [SerializeField, Min(0)] private int intersectionLookAheadModules = 1;

    [Tooltip("How many road modules beyond an intersection the truck must travel before releasing it.")]
    [SerializeField, Min(1)] private int intersectionClearModules = 2;

    [Header("Route")]
    [SerializeField] private Vector2Int startCell;
    [SerializeField] private Vector2Int exitCell;
    [SerializeField] private bool snapToStartCell = true;

    [Header("Road Module Settings")]
    [Tooltip("Road modules cover this many grid cells in each direction.")]
    [Min(1)]
    [SerializeField] private int roadModuleSize = 2;

    [Header("Movement")]
    [Min(0.1f)]
    [SerializeField] private float movementSpeed = 5f;

    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 10f;

    [SerializeField] private float heightOffset = 0.2f;

    [Header("Road Turn Smoothing")]
    [Tooltip("Requested turn radius in grid-cell units.")]
    [Min(0f)]
    [SerializeField] private float turningRadiusInCells = 2f;

    [Tooltip("Number of segments used to approximate each Bezier curve.")]
    [Min(1)]
    [SerializeField] private int turnCurveResolution = 8;

    private List<Vector3> smoothedMovementWaypoints =
        new List<Vector3>();

    private int currentSmoothedWaypointIndex;

    [Header("Cargo Zone Parking Maneuver")]
    [Tooltip("Distance outside VanStopPoint for the straight entry approach.")]
    [Min(0.01f)]
    [SerializeField] private float vanStopEntryClearance = 0.75f;

    [Tooltip("Tolerance for reaching cargo parking waypoints.")]
    [Min(0.01f)]
    [SerializeField] private float vanStopWaypointTolerance = 0.03f;

    private Vector3 vanStopAlignmentPosition;
    private Vector3 vanStopPreEntryPosition;
    private Vector3 vanStopOutwardDirection;
    private int returningFromCargoZoneStep;

    [Header("Traffic Following")]
    [SerializeField] private LayerMask trafficLayer;

    [Min(0.1f)]
    [SerializeField] private float trafficDetectionDistance = 3f;

    [Min(0.01f)]
    [SerializeField] private float safeFollowingDistance = 0.7f;

    [SerializeField] private float rayOriginHeight = 0.15f;
    [SerializeField] private float rayOriginForwardOffset = 0.3f;

    [Min(0.1f)]
    [SerializeField] private float acceleration = 4f;

    [Min(0.1f)]
    [SerializeField] private float braking = 8f;

    [Header("Cargo Unloading")]
    [SerializeField] private GameObject cargoBoxPrefab;

    [Min(0f)]
    [SerializeField] private float unloadingDuration = 5f;

    [Min(0.1f)]
    [SerializeField] private float cargoBoxSpacing = 0.4f;

    [Min(1)]
    [SerializeField] private int boxesPerRow = 5;

    [Header("Cleanup")]
    [SerializeField] private bool destroyOnExit = true;

    [Header("Debug - Runtime Information")]
    [SerializeField] private string debugState = "Idle";
    [SerializeField] private string debugCargoZone = "";
    [SerializeField] private string debugMessage = "";
    [SerializeField] private int debugPathLength;
    [SerializeField] private float debugCurrentSpeed;
    [SerializeField] private int debugBoxesUnloaded;

    [Tooltip("Most recent collider hit by a traffic sensor.")]
    [SerializeField] private string debugLastRayHitCollider = "None";

    [SerializeField] private string debugLastRayDirection = "None";
    [SerializeField] private float debugLastRayDistance;
    [SerializeField] private string debugBlockingCollider = "None";
    [SerializeField] private string debugBlockingVehicleState = "None";
    [SerializeField] private float debugBlockingDistance;

    [Tooltip("Results from the five traffic sensor rays.")]
    [TextArea(3, 8)]
    [SerializeField] private string debugRaycastSummary = "Not tested";

    [SerializeField] private string debugTrafficDecision = "Not tested";
    [SerializeField] private float debugLastSensorUpdateTime;
    [SerializeField] private string debugDestructionReason = "Not destroyed";

    private TruckState state = TruckState.Idle;
    public TruckState CurrentState => state;

    // Intersection reservation currently held by this truck.
    private bool hasIntersectionReservation;
    private Vector2Int reservedIntersection;
    private int reservedIntersectionPathIndex = -1;

    // Intersection this truck is currently waiting to enter.
    private bool waitingForIntersection;
    private Vector2Int requestedIntersection;
    private int requestedIntersectionPathIndex = -1;
    public bool IsWaitingForIntersection => waitingForIntersection;
    public Vector2Int RequestedIntersection => requestedIntersection;


    private List<Vector2Int> currentPath = new List<Vector2Int>();

    private float unloadingTimer;
    private float currentSpeed;

    private PlacedBuilding reservedCargoZone;
    private CargoZone reservedCargoZoneComponent;
    private Transform vanStopPoint;
    private Transform cargoAreaPoint;
    private Vector3 approachWorldPosition;

    private bool routeInitializedBySpawner;
    private bool routeStarted;
    private bool cargoUnloaded;
    private bool waitingLogShown;

    private string deliveryOperationId = string.Empty;
    private string deliveryFlightNumber = string.Empty;

    private Renderer[] truckRenderers;
    private Collider[] truckColliders;
    private bool[] originalRendererEnabled;
    private bool[] originalColliderEnabled;

    // ---------------------------------------------------------
    // ROUTE INITIALIZATION
    // ---------------------------------------------------------

    public void InitializeRoute(
        Vector2Int newStartCell,
        Vector2Int newExitCell)
    {
        InitializeRoute(
            newStartCell,
            newExitCell,
            string.Empty,
            string.Empty);
    }

    public void InitializeRoute(
        Vector2Int newStartCell,
        Vector2Int newExitCell,
        string operationId,
        string flightNumber)
    {
        roadModuleSize = Mathf.Max(1, roadModuleSize);

        startCell = GetRoadModuleAnchor(newStartCell);
        exitCell = GetRoadModuleAnchor(newExitCell);

        deliveryOperationId = operationId ?? string.Empty;
        deliveryFlightNumber = flightNumber ?? string.Empty;

        routeInitializedBySpawner = true;

        debugMessage =
            $"Route received from spawner: {startCell} -> {exitCell}. " +
            $"Flight: {deliveryFlightNumber}, " +
            $"Operation: {deliveryOperationId}";
    }

    private void Awake()
    {
        roadModuleSize = Mathf.Max(1, roadModuleSize);

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (intersectionManager == null)
            intersectionManager = FindFirstObjectByType<IntersectionManager>();

        truckRenderers = GetComponentsInChildren<Renderer>(true);
        truckColliders = GetComponentsInChildren<Collider>(true);

        originalRendererEnabled = new bool[truckRenderers.Length];

        for (int i = 0; i < truckRenderers.Length; i++)
            originalRendererEnabled[i] = truckRenderers[i].enabled;

        originalColliderEnabled = new bool[truckColliders.Length];

        for (int i = 0; i < truckColliders.Length; i++)
            originalColliderEnabled[i] = truckColliders[i].enabled;

        currentSpeed = movementSpeed;
        RefreshDebugInformation();
    }

    private void Start()
    {
        StartDeliveryRoute();
    }

    private void Update()
    {
        switch (state)
        {
            case TruckState.WaitingForCargoZone:
                TryStartWaitingTruck();
                break;

            case TruckState.DrivingToCargoZone:
                MoveAlongPath(TruckState.DrivingToVanStop);
                break;

            case TruckState.AligningVanStopApproach:
                MoveToVanStopAlignment();
                break;

            case TruckState.ApproachingVanStopEntry:
                MoveToVanStopPreEntry();
                break;

            case TruckState.DrivingToVanStop:
                MoveIntoVanStop();
                break;

            case TruckState.Unloading:
                unloadingTimer -= Time.deltaTime;
                currentSpeed = 0f;

                if (unloadingTimer <= 0f)
                {
                    returningFromCargoZoneStep = 0;
                    SetState(TruckState.ReturningToRoad);
                    debugMessage = "Unloading finished. Leaving cargo zone.";
                }
                break;

            case TruckState.ReturningToRoad:
                if (MoveOutOfCargoZone())
                {
                    ReleaseCargoZone();

                    if (!TryCreatePath(
                            approachWorldPosition,
                            exitCell))
                    {
                        FinishRoute(
                            "Could not find a road path to the exit.");
                        break;
                    }

                    InitializeSmoothedMovementPath();
                    currentSpeed = movementSpeed;

                    SetState(TruckState.DrivingToExit);
                    debugMessage = "Returning to road and continuing to exit.";
                }
                break;

            case TruckState.DrivingToExit:
                MoveAlongPath(TruckState.Arrived);
                break;
        }

        RefreshDebugInformation();
    }

    private void RefreshDebugInformation()
    {
        debugState = state.ToString();
        debugCurrentSpeed = currentSpeed;
    }

    private void StartDeliveryRoute()
    {
        if (routeStarted)
            return;

        routeStarted = true;

        if (roadNetwork == null || gridManager == null)
        {
            FinishRoute(
                "RoadNetwork or GridManager reference is missing.");
            return;
        }

        startCell = GetRoadModuleAnchor(startCell);
        exitCell = GetRoadModuleAnchor(exitCell);

        if (!roadNetwork.IsRoad(startCell))
        {
            FinishRoute($"Start road module {startCell} is not a road.");
            return;
        }

        if (!roadNetwork.IsRoad(exitCell))
        {
            FinishRoute($"Exit road module {exitCell} is not a road.");
            return;
        }

        if (snapToStartCell)
            transform.position = GetRoadWorldPosition(startCell);

        if (!TryReserveCargoZone())
        {
            EnterCargoZoneWaitingState();
            return;
        }

        StartDrivingWithReservedCargoZone();
    }

    // ---------------------------------------------------------
    // CARGO ZONE RESERVATION AND WAITING
    // ---------------------------------------------------------

    private void EnterCargoZoneWaitingState()
    {
        SetState(TruckState.WaitingForCargoZone);
        debugMessage = "Waiting for an available cargo zone.";
        currentSpeed = 0f;

        SetTruckVisible(false);

        if (!waitingLogShown)
        {
            Debug.Log(
                "CARGO TRUCK WAITING | " +
                $"Flight: {GetFlightLabel()} | " +
                $"Operation: {GetOperationLabel()} | " +
                "Reason: No available cargo zone. " +
                "Truck is hidden until a zone becomes available.",
                this);

            waitingLogShown = true;
        }
    }

    private void TryStartWaitingTruck()
    {
        if (!TryReserveCargoZone())
            return;

        StartDrivingWithReservedCargoZone();
    }

    private void StartDrivingWithReservedCargoZone()
    {
        if (reservedCargoZone == null ||
            reservedCargoZoneComponent == null)
        {
            ReleaseCargoZone();
            EnterCargoZoneWaitingState();
            return;
        }

        Vector2Int approachCell = GetRoadModuleAnchor(
            gridManager.WorldToGrid(approachWorldPosition));

        currentPath = roadNetwork.FindPath(startCell, approachCell);

        if (currentPath == null || currentPath.Count == 0)
        {
            ReleaseCargoZone();
            EnterCargoZoneWaitingState();

            debugMessage =
                "Could not create a path to the reserved cargo zone. " +
                "Waiting to retry.";

            return;
        }

        debugPathLength = currentPath.Count;
        InitializeSmoothedMovementPath();

        currentSpeed = movementSpeed;

        SetTruckVisible(true);
        SetState(TruckState.DrivingToCargoZone);

        debugMessage = routeInitializedBySpawner
            ? "Route initialized using road module anchors from spawner."
            : "Route initialized using Inspector route cells.";

        Debug.Log(
            "CARGO TRUCK READY | " +
            $"Flight: {GetFlightLabel()} | " +
            $"Operation: {GetOperationLabel()} | " +
            $"Cargo zone: {debugCargoZone} | " +
            "Truck is now visible and driving.",
            this);
    }

    private bool TryReserveCargoZone()
    {
        if (roadNetwork == null || gridManager == null)
            return false;

        PlacedBuilding[] buildings =
            FindObjectsByType<PlacedBuilding>(
                FindObjectsSortMode.None);

        int shortestPathLength = int.MaxValue;

        PlacedBuilding bestBuilding = null;
        CargoZone bestCargoZone = null;
        Transform bestVanStopPoint = null;
        Transform bestCargoAreaPoint = null;

        Vector2Int bestApproachCell = default;
        List<Vector2Int> bestPath = null;

        foreach (PlacedBuilding building in buildings)
        {
            if (building == null ||
                ReservedCargoZones.Contains(building))
            {
                continue;
            }

            CargoZone candidateCargoZone =
                building.GetComponent<CargoZone>();

            if (candidateCargoZone == null)
                continue;

            Transform candidateVanStop =
                FindChildByName(building.transform, "VanStopPoint");

            Transform candidateCargoArea =
                FindChildByName(building.transform, "CargoAreaPoint");

            if (candidateVanStop == null ||
                candidateCargoArea == null)
            {
                continue;
            }

            List<Vector2Int> approachCells = GetApproachCells(building);

            foreach (Vector2Int rawApproachCell in approachCells)
            {
                Vector2Int approachCell =
                    GetRoadModuleAnchor(rawApproachCell);

                if (!roadNetwork.IsRoad(approachCell))
                    continue;

                List<Vector2Int> candidatePath =
                    roadNetwork.FindPath(startCell, approachCell);

                if (candidatePath == null ||
                    candidatePath.Count == 0 ||
                    candidatePath.Count >= shortestPathLength)
                {
                    continue;
                }

                shortestPathLength = candidatePath.Count;
                bestBuilding = building;
                bestCargoZone = candidateCargoZone;
                bestVanStopPoint = candidateVanStop;
                bestCargoAreaPoint = candidateCargoArea;
                bestApproachCell = approachCell;
                bestPath = candidatePath;
            }
        }

        if (bestBuilding == null ||
            bestCargoZone == null ||
            bestVanStopPoint == null ||
            bestCargoAreaPoint == null)
        {
            return false;
        }

        reservedCargoZone = bestBuilding;
        reservedCargoZoneComponent = bestCargoZone;
        vanStopPoint = bestVanStopPoint;
        cargoAreaPoint = bestCargoAreaPoint;

        ReservedCargoZones.Add(reservedCargoZone);

        approachWorldPosition = GetRoadWorldPosition(bestApproachCell);

        currentPath = bestPath;
        debugPathLength = bestPath.Count;
        debugCargoZone = bestBuilding.name;

        return true;
    }

    private List<Vector2Int> GetApproachCells(PlacedBuilding building)
    {
        List<Vector2Int> cells = new List<Vector2Int>();

        Vector2Int origin = building.Origin;
        int width = building.Width;
        int depth = building.Depth;

        if (width >= depth)
        {
            cells.Add(origin + new Vector2Int(-1, 0));
            cells.Add(origin + new Vector2Int(width, 0));
        }
        else
        {
            cells.Add(origin + new Vector2Int(0, -1));
            cells.Add(origin + new Vector2Int(0, depth));
        }

        return cells;
    }

    private void SetTruckVisible(bool visible)
    {
        if (truckRenderers != null)
        {
            for (int i = 0; i < truckRenderers.Length; i++)
            {
                if (truckRenderers[i] != null)
                {
                    truckRenderers[i].enabled =
                        visible && originalRendererEnabled[i];
                }
            }
        }

        if (truckColliders != null)
        {
            for (int i = 0; i < truckColliders.Length; i++)
            {
                if (truckColliders[i] != null)
                {
                    truckColliders[i].enabled =
                        visible && originalColliderEnabled[i];
                }
            }
        }
    }

    private string GetFlightLabel()
    {
        return string.IsNullOrWhiteSpace(deliveryFlightNumber)
            ? "Unassigned"
            : deliveryFlightNumber;
    }

    private string GetOperationLabel()
    {
        return string.IsNullOrWhiteSpace(deliveryOperationId)
            ? "Unassigned"
            : deliveryOperationId;
    }

    // ---------------------------------------------------------
    // CARGO ZONE PARKING MANEUVER
    // ---------------------------------------------------------

    private bool ConfigureVanStopManeuver()
    {
        if (reservedCargoZone == null ||
            vanStopPoint == null ||
            gridManager == null)
        {
            return false;
        }

        Vector3 stopPosition = GetVanStopWorldPosition();

        Vector3 directionToRoad =
            approachWorldPosition - stopPosition;

        directionToRoad.y = 0f;

        if (directionToRoad.sqrMagnitude < 0.001f)
        {
            Debug.LogError(
                "CargoDeliveryTruck: Cannot determine the cargo-zone " +
                "approach direction. Check VanStopPoint and road placement.",
                this);

            return false;
        }

        // Choose the cardinal direction facing the road, as the
        // TrafficTestVehicle parking maneuver does.
        if (Mathf.Abs(directionToRoad.x) >
            Mathf.Abs(directionToRoad.z))
        {
            vanStopOutwardDirection =
                directionToRoad.x >= 0f
                    ? Vector3.right
                    : Vector3.left;
        }
        else
        {
            vanStopOutwardDirection =
                directionToRoad.z >= 0f
                    ? Vector3.forward
                    : Vector3.back;
        }

        float clearance = Mathf.Max(
            0.01f,
            vanStopEntryClearance);

        // Position outside the designated stop point. The final
        // leg from this position to VanStopPoint is straight.
        vanStopPreEntryPosition =
            stopPosition +
            vanStopOutwardDirection * clearance;

        vanStopPreEntryPosition.y = stopPosition.y;

        // Align laterally with the pre-entry point while staying
        // on the road approach position's other axis.
        vanStopAlignmentPosition = approachWorldPosition;

        if (Mathf.Abs(vanStopOutwardDirection.x) > 0.5f)
        {
            vanStopAlignmentPosition.z =
                vanStopPreEntryPosition.z;
        }
        else
        {
            vanStopAlignmentPosition.x =
                vanStopPreEntryPosition.x;
        }

        vanStopAlignmentPosition.y = approachWorldPosition.y;

        debugMessage =
            $"Parking maneuver configured. Outward direction: " +
            $"{vanStopOutwardDirection}";

        return true;
    }

    private void BeginVanStopEntry()
    {
        if (!ConfigureVanStopManeuver())
        {
            FinishRoute(
                "Could not configure the cargo-zone parking maneuver.");
            return;
        }

        returningFromCargoZoneStep = 0;
        currentSpeed = movementSpeed;

        SetState(TruckState.AligningVanStopApproach);

        debugMessage =
            "Aligning for perpendicular cargo-zone parking.";
    }

    private void MoveToVanStopAlignment()
    {
        MoveToPoint(
            vanStopAlignmentPosition,
            movementSpeed);

        if (!ReachedVanStopPosition(vanStopAlignmentPosition))
            return;

        transform.position = vanStopAlignmentPosition;

        SetState(TruckState.ApproachingVanStopEntry);
        debugMessage = "Cargo-zone approach aligned.";
    }

    private void MoveToVanStopPreEntry()
    {
        MoveToPoint(
            vanStopPreEntryPosition,
            movementSpeed);

        if (!ReachedVanStopPosition(vanStopPreEntryPosition))
            return;

        transform.position = vanStopPreEntryPosition;

        SetState(TruckState.DrivingToVanStop);
        debugMessage = "Reached cargo-zone pre-entry position.";
    }

    private void MoveIntoVanStop()
    {
        Vector3 stopPosition = GetVanStopWorldPosition();

        MoveToPoint(stopPosition, movementSpeed);

        if (!ReachedVanStopPosition(stopPosition))
            return;

        transform.position = stopPosition;
        currentSpeed = 0f;

        // Finish facing into the cargo zone before unloading.
        Vector3 inwardDirection = -vanStopOutwardDirection;

        if (inwardDirection.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(
                inwardDirection,
                Vector3.up);
        }

        if (!cargoUnloaded)
            UnloadCargoBoxes();

        unloadingTimer = unloadingDuration;

        SetState(TruckState.Unloading);

        debugMessage =
            $"Parked at VanStopPoint. " +
            $"{debugBoxesUnloaded} boxes unloaded.";
    }

    private bool MoveOutOfCargoZone()
    {
        // Step 0: rotate outward, matching TrafficTestVehicle.
        if (returningFromCargoZoneStep == 0)
        {
            if (!RotateTowardsDirection(vanStopOutwardDirection))
                return false;

            returningFromCargoZoneStep = 1;
        }

        Vector3 targetPosition;

        switch (returningFromCargoZoneStep)
        {
            case 1:
                targetPosition = vanStopPreEntryPosition;
                break;

            case 2:
                targetPosition = vanStopAlignmentPosition;
                break;

            case 3:
                targetPosition = approachWorldPosition;
                break;

            default:
                returningFromCargoZoneStep = 0;
                return true;
        }

        MoveToPoint(targetPosition, movementSpeed);

        if (!ReachedVanStopPosition(targetPosition))
            return false;

        transform.position = targetPosition;
        returningFromCargoZoneStep++;

        if (returningFromCargoZoneStep > 3)
        {
            returningFromCargoZoneStep = 0;
            currentSpeed = movementSpeed;
            return true;
        }

        return false;
    }

    private bool ReachedVanStopPosition(Vector3 targetPosition)
    {
        return Vector3.Distance(
            transform.position,
            targetPosition) <= vanStopWaypointTolerance;
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

    // ---------------------------------------------------------
    // CARGO UNLOADING
    // ---------------------------------------------------------

    private void UnloadCargoBoxes()
    {
        cargoUnloaded = true;
        debugBoxesUnloaded = 0;

        if (cargoBoxPrefab == null)
        {
            debugMessage =
                "Cannot unload cargo: Cargo Box Prefab is not assigned.";

            Debug.LogError(
                "CargoDeliveryTruck: Assign the Cargo Box Prefab " +
                "in the Inspector.",
                this);

            return;
        }

        if (cargoAreaPoint == null ||
            reservedCargoZoneComponent == null)
        {
            debugMessage =
                "Cannot unload cargo: cargo zone or cargo area is missing.";

            Debug.LogError(
                "CargoDeliveryTruck: CargoZone or CargoAreaPoint is missing.",
                this);

            return;
        }

        bool hasFlightAssignment =
            !string.IsNullOrWhiteSpace(deliveryOperationId) &&
            !string.IsNullOrWhiteSpace(deliveryFlightNumber);

        if (!hasFlightAssignment)
        {
            Debug.LogWarning(
                "CargoDeliveryTruck: This truck has no scheduled flight " +
                "assignment. Boxes will be spawned visually but will not " +
                "be registered as outgoing flight cargo.",
                this);
        }

        int columns = Mathf.Max(1, boxesPerRow);
        float boxHeight = cargoBoxPrefab.transform.localScale.y;

        Vector3 basePosition =
            cargoAreaPoint.position +
            Vector3.up * (boxHeight * 0.5f);

        Vector3 right = cargoAreaPoint.right;
        Vector3 forward = cargoAreaPoint.forward;

        int rows = Mathf.CeilToInt(
            (float)CargoBoxCount / columns);

        for (int i = 0; i < CargoBoxCount; i++)
        {
            int column = i % columns;
            int row = i / columns;

            float horizontalOffset =
                (column -
                 (Mathf.Min(columns, CargoBoxCount) - 1) * 0.5f)
                * cargoBoxSpacing;

            float forwardOffset =
                (row - (rows - 1) * 0.5f) * cargoBoxSpacing;

            Vector3 spawnPosition =
                basePosition +
                right * horizontalOffset +
                forward * forwardOffset;

            GameObject cargoBox = Instantiate(
                cargoBoxPrefab,
                spawnPosition,
                cargoAreaPoint.rotation);

            cargoBox.name = $"CargoBox_{i + 1:00}";

            if (hasFlightAssignment)
            {
                CargoPackage package =
                    cargoBox.GetComponent<CargoPackage>();

                if (package == null)
                {
                    Debug.LogError(
                        "CargoDeliveryTruck: CargoBox prefab is missing " +
                        $"CargoPackage. Box {i + 1} is not registered.",
                        cargoBox);
                }
                else
                {
                    string packageId =
                        $"{deliveryOperationId}:OUT:{i + 1:00}";

                    package.Initialize(
                        packageId,
                        CargoDirection.Outgoing,
                        deliveryOperationId,
                        reservedCargoZoneComponent);

                    bool registered =
                        reservedCargoZoneComponent.RegisterPackage(package);

                    if (!registered)
                    {
                        Debug.LogError(
                            $"CargoDeliveryTruck: Could not register " +
                            $"package {packageId} with cargo zone " +
                            $"{reservedCargoZoneComponent.name}.",
                            cargoBox);
                    }
                }
            }

            debugBoxesUnloaded++;
        }

        debugMessage = hasFlightAssignment
            ? $"Unloaded {debugBoxesUnloaded} outgoing packages for " +
              $"{deliveryFlightNumber} ({deliveryOperationId})."
            : $"Unloaded {debugBoxesUnloaded} visual-only cargo boxes.";
    }

    // ---------------------------------------------------------
    // ROAD PATH SMOOTHING
    // Mirrors TrafficTestVehicle's Bezier waypoint generation.
    // ---------------------------------------------------------

    private void InitializeSmoothedMovementPath()
    {
        smoothedMovementWaypoints =
            BuildSmoothedMovementWaypoints(currentPath);

        currentSmoothedWaypointIndex =
            smoothedMovementWaypoints.Count > 1 ? 1 : 0;
    }

    private List<Vector3> BuildSmoothedMovementWaypoints(
        List<Vector2Int> cells)
    {
        List<Vector3> originalWaypoints = new List<Vector3>();
        List<Vector3> smoothedWaypoints = new List<Vector3>();

        if (cells == null || cells.Count == 0)
            return smoothedWaypoints;

        foreach (Vector2Int cell in cells)
            originalWaypoints.Add(GetRoadWorldPosition(cell));

        if (originalWaypoints.Count <= 2)
            return originalWaypoints;

        int waypointCount = originalWaypoints.Count;
        float[] turnRadii = new float[waypointCount];

        float requestedRadius =
            turningRadiusInCells * gridManager.CellSize;

        for (int i = 1; i < waypointCount - 1; i++)
        {
            Vector3 incoming =
                originalWaypoints[i] - originalWaypoints[i - 1];

            Vector3 outgoing =
                originalWaypoints[i + 1] - originalWaypoints[i];

            incoming.y = 0f;
            outgoing.y = 0f;

            if (incoming.sqrMagnitude < 0.001f ||
                outgoing.sqrMagnitude < 0.001f)
            {
                continue;
            }

            incoming.Normalize();
            outgoing.Normalize();

            float turnAngle = Vector3.Angle(incoming, outgoing);

            if (turnAngle < 1f || turnAngle > 179f)
                continue;

            float incomingLength = Vector3.Distance(
                originalWaypoints[i - 1],
                originalWaypoints[i]);

            float outgoingLength = Vector3.Distance(
                originalWaypoints[i],
                originalWaypoints[i + 1]);

            turnRadii[i] = Mathf.Min(
                requestedRadius,
                incomingLength * 0.5f,
                outgoingLength * 0.5f);
        }

        smoothedWaypoints.Add(originalWaypoints[0]);

        const float bezierCircleFactor = 0.55228475f;
        int resolution = Mathf.Max(1, turnCurveResolution);

        for (int i = 1; i < waypointCount - 1; i++)
        {
            Vector3 corner = originalWaypoints[i];
            float radius = turnRadii[i];

            if (radius <= 0.001f)
            {
                AddWaypointIfDifferent(smoothedWaypoints, corner);
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

            Vector3 entry = corner - incoming * radius;
            Vector3 exit = corner + outgoing * radius;

            AddWaypointIfDifferent(smoothedWaypoints, entry);

            Vector3 control1 =
                entry + incoming * radius * bezierCircleFactor;

            Vector3 control2 =
                exit - outgoing * radius * bezierCircleFactor;

            for (int step = 1; step <= resolution; step++)
            {
                float t = step / (float)resolution;

                Vector3 point = EvaluateCubicBezier(
                    entry,
                    control1,
                    control2,
                    exit,
                    t);

                AddWaypointIfDifferent(smoothedWaypoints, point);
            }
        }

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

    private void MoveAlongPath(TruckState stateAfterPath)
    {
        if (currentPath == null || currentPath.Count == 0)
        {
            FinishRoute("The current road path is empty.");
            return;
        }

        // Only road driving uses intersection reservations.
        // Cargo-zone entry and unloading are handled by separate states.
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
            currentSpeed = 0f;

            if (stateAfterPath == TruckState.DrivingToVanStop)
            {
                BeginVanStopEntry();
            }
            else
            {
                SetState(stateAfterPath);
            }

            return;
        }

        Vector3 targetPosition =
            smoothedMovementWaypoints[currentSmoothedWaypointIndex];

        float trafficAwareSpeed = GetTrafficFollowingSpeed();

        MoveToPoint(targetPosition, trafficAwareSpeed);

        if (Vector3.Distance(
                transform.position,
                targetPosition) <= 0.03f)
        {
            transform.position = targetPosition;
            currentSmoothedWaypointIndex++;
        }
    }

    // ---------------------------------------------------------
    // TRAFFIC FOLLOWING
    // Uses five sensor directions and the closest-hit rule.
    // ---------------------------------------------------------

    private float GetTrafficFollowingSpeed()
    {
        float targetSpeed = movementSpeed;

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
                movementSpeed,
                normalizedDistance);

            debugTrafficDecision =
                $"Braking for {debugBlockingCollider}";
        }
        else
        {
            debugTrafficDecision = "No blocking vehicle detected";
        }

        float rate = targetSpeed < currentSpeed
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

        Vector3 origin =
            transform.position +
            Vector3.up * rayOriginHeight +
            transform.forward * rayOriginForwardOffset;

        Vector3 forward = transform.forward;

        Vector3 left60 = Quaternion.Euler(0f, -60f, 0f) * forward;
        Vector3 left30 = Quaternion.Euler(0f, -30f, 0f) * forward;
        Vector3 right30 = Quaternion.Euler(0f, 30f, 0f) * forward;
        Vector3 right60 = Quaternion.Euler(0f, 60f, 0f) * forward;

        bool foundVehicle = false;
        float closestDistance = float.MaxValue;
        RaycastHit closestHit = default;

        CheckTrafficRay(origin, left60, "Left 60",
            ref foundVehicle, ref closestDistance, ref closestHit);

        CheckTrafficRay(origin, left30, "Left 30",
            ref foundVehicle, ref closestDistance, ref closestHit);

        CheckTrafficRay(origin, forward, "Forward",
            ref foundVehicle, ref closestDistance, ref closestHit);

        CheckTrafficRay(origin, right30, "Right 30",
            ref foundVehicle, ref closestDistance, ref closestHit);

        CheckTrafficRay(origin, right60, "Right 60",
            ref foundVehicle, ref closestDistance, ref closestHit);

        hit = closestHit;

        if (foundVehicle)
        {
            debugBlockingCollider = closestHit.collider.name;
            debugBlockingDistance = closestHit.distance;

            Component blocker = GetVehicleComponent(closestHit.collider);

            if (blocker != null)
                debugBlockingVehicleState = GetVehicleStateLabel(blocker);

            debugTrafficDecision =
                $"Blocking vehicle at {closestHit.distance:F2}m";
        }
        else
        {
            debugTrafficDecision = "No blocking vehicle detected";
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
            debugRaycastSummary += $"{rayName}: no hit\n";
            return;
        }

        debugLastRayHitCollider = rayHit.collider.name;
        debugLastRayDirection = rayName;
        debugLastRayDistance = rayHit.distance;

        debugRaycastSummary +=
            $"{rayName}: {rayHit.collider.name} " +
            $"({rayHit.distance:F2}m)";

        Component detectedVehicle = GetVehicleComponent(rayHit.collider);

        if (detectedVehicle == null)
        {
            debugRaycastSummary +=
                " — no recognized vehicle component\n";
            return;
        }

        if (detectedVehicle.gameObject == gameObject)
        {
            debugRaycastSummary += " — self, ignored\n";
            return;
        }

        // Apply the existing traffic rules to normal vehicles.
        if (detectedVehicle is TrafficTestVehicle normalVehicle)
        {
            if (!ShouldDetectVehicle(normalVehicle))
            {
                debugRaycastSummary +=
                    $" — ignored by traffic rules " +
                    $"({normalVehicle.CurrentState})\n";
                return;
            }
        }
        // Cargo trucks use their own state machine.
        else if (detectedVehicle is CargoDeliveryTruck cargoTruck)
        {
            if (!ShouldDetectVehicle(cargoTruck))
            {
                debugRaycastSummary +=
                    $" — ignored cargo truck " +
                    $"({cargoTruck.CurrentState})\n";
                return;
            }
        }
        else
        {
            debugRaycastSummary += " — unsupported vehicle type\n";
            return;
        }

        debugRaycastSummary +=
            $" — blocking vehicle ({GetVehicleStateLabel(detectedVehicle)})\n";

        if (rayHit.distance < closestDistance)
        {
            closestDistance = rayHit.distance;
            closestHit = rayHit;
            foundVehicle = true;
        }
    }



    private Component GetVehicleComponent(Collider hitCollider)
    {
        if (hitCollider == null)
            return null;

        TrafficTestVehicle normalVehicle =
            hitCollider.GetComponentInParent<TrafficTestVehicle>();

        if (normalVehicle != null)
            return normalVehicle;

        CargoDeliveryTruck cargoTruck =
            hitCollider.GetComponentInParent<CargoDeliveryTruck>();

        if (cargoTruck != null)
            return cargoTruck;

        return null;
    }




    private bool ShouldDetectVehicle(Component vehicle)
    {
        if (vehicle == null || vehicle.gameObject == gameObject)
            return false;

        bool thisTruckIsLeavingCargoZone =
            state == TruckState.ReturningToRoad;

        bool thisTruckIsOnRoad =
            state == TruckState.DrivingToCargoZone ||
            state == TruckState.DrivingToExit;

        TrafficTestVehicle normalVehicle =
            vehicle as TrafficTestVehicle;

        if (normalVehicle != null)
        {
            TrafficTestVehicle.VehicleState otherState =
                normalVehicle.CurrentState;

            // Ignore normal vehicles that are parked, idle, or finished.
            if (otherState == TrafficTestVehicle.VehicleState.Parked ||
                otherState == TrafficTestVehicle.VehicleState.Idle ||
                otherState == TrafficTestVehicle.VehicleState.Arrived)
            {
                return false;
            }

            // Ignore a normal vehicle waiting for the same intersection
            // when this cargo truck already owns the reservation.
            if (ShouldIgnoreIntersectionWaitingVehicle(normalVehicle))
            {
                return false;
            }

            // Preserve the normal-traffic parking priority:
            // a cargo truck leaving its cargo zone must not wait for
            // normal vehicles that are themselves parking.
            if (thisTruckIsLeavingCargoZone)
            {
                if (IsNormalVehicleParking(otherState))
                    return false;

                if (otherState ==
                        TrafficTestVehicle.VehicleState.DrivingToParking ||
                    otherState ==
                        TrafficTestVehicle.VehicleState.DrivingToExit)
                {
                    return false;
                }
            }

            // Road traffic must detect normal vehicles leaving parking.
            if (thisTruckIsOnRoad &&
                otherState ==
                    TrafficTestVehicle.VehicleState.LeavingParking)
            {
                return true;
            }

            return true;
        }

        CargoDeliveryTruck otherTruck =
            vehicle as CargoDeliveryTruck;

        if (otherTruck != null)
        {
            TruckState otherState = otherTruck.state;

            // Ignore a cargo truck waiting for the same intersection
            // when this cargo truck already owns the reservation.
            if (ShouldIgnoreIntersectionWaitingVehicle(otherTruck))
            {
                return false;
            }

            // A parked or unloading cargo truck must not block traffic.
            if (otherState == TruckState.Idle ||
                otherState == TruckState.WaitingForCargoZone ||
                otherState == TruckState.Unloading ||
                otherState == TruckState.Arrived)
            {
                return false;
            }

            // When this truck is leaving its cargo zone, do not let
            // vehicles entering or leaving other cargo zones block it.
            if (thisTruckIsLeavingCargoZone)
            {
                if (IsCargoTruckParking(otherState))
                    return false;

                if (otherState == TruckState.DrivingToCargoZone ||
                    otherState == TruckState.DrivingToExit)
                {
                    return false;
                }
            }

            // Road traffic must still detect another cargo truck
            // actively pulling out of its cargo zone.
            if (thisTruckIsOnRoad &&
                otherState == TruckState.ReturningToRoad)
            {
                return true;
            }

            return true;
        }

        return false;
    }


    private bool IsNormalVehicleParking(
        TrafficTestVehicle.VehicleState vehicleState)
    {
        return
            vehicleState ==
                TrafficTestVehicle.VehicleState.WaitingForParking ||
            vehicleState ==
                TrafficTestVehicle.VehicleState.AligningParkingApproach ||
            vehicleState ==
                TrafficTestVehicle.VehicleState.ApproachingParkingEntry ||
            vehicleState ==
                TrafficTestVehicle.VehicleState.EnteringParking ||
            vehicleState ==
                TrafficTestVehicle.VehicleState.MovingIntoParking;
    }

    private bool IsCargoTruckParking(TruckState truckState)
    {
        return
            truckState == TruckState.AligningVanStopApproach ||
            truckState == TruckState.ApproachingVanStopEntry ||
            truckState == TruckState.DrivingToVanStop;
    }


    private string GetVehicleStateLabel(Component vehicle)
    {
        if (vehicle == null)
            return "Unknown";

        TrafficTestVehicle normalVehicle =
            vehicle as TrafficTestVehicle;

        if (normalVehicle != null)
            return normalVehicle.CurrentState.ToString();

        CargoDeliveryTruck cargoTruck =
            vehicle as CargoDeliveryTruck;

        if (cargoTruck != null)
            return cargoTruck.state.ToString();

        return "Unknown";
    }

    private bool ShouldIgnoreIntersectionWaitingVehicle(
    Component other)
    {
        if (other == null)
            return false;

        if (!hasIntersectionReservation)
            return false;

        if (intersectionManager == null)
            return false;

        bool otherIsWaiting;
        Vector2Int otherIntersection;

        TrafficTestVehicle normalVehicle =
            other.GetComponent<TrafficTestVehicle>();

        CargoDeliveryTruck cargoTruck =
            other.GetComponent<CargoDeliveryTruck>();

        if (normalVehicle != null)
        {
            otherIsWaiting = normalVehicle.IsWaitingForIntersection;
            otherIntersection = normalVehicle.RequestedIntersection;
        }
        else if (cargoTruck != null)
        {
            otherIsWaiting = cargoTruck.IsWaitingForIntersection;
            otherIntersection = cargoTruck.RequestedIntersection;
        }
        else
        {
            return false;
        }

        if (!otherIsWaiting)
            return false;

        if (reservedIntersection != otherIntersection)
            return false;

        return intersectionManager.HasReservation(
            reservedIntersection,
            this);
    }

    // ---------------------------------------------------------
    // INTERSECTION CONTROL
    // ---------------------------------------------------------
    private bool HandleIntersectionControl()
    {
        if (intersectionManager == null ||
            roadNetwork == null ||
            gridManager == null ||
            currentPath == null ||
            currentPath.Count == 0)
        {
            return false;
        }

        int nearestPathIndex = GetNearestPathIndex();
        bool releasedReservationThisFrame = false;

        // Keep the reservation until the truck has cleared the junction.
        if (hasIntersectionReservation)
        {
            int releaseIndex =
                reservedIntersectionPathIndex +
                Mathf.Max(1, intersectionClearModules);

            bool routeCleared = nearestPathIndex >= releaseIndex;
            bool pathEnded = nearestPathIndex >= currentPath.Count - 1;

            if (routeCleared || pathEnded)
            {
                intersectionManager.Release(
                    reservedIntersection,
                    this);

                hasIntersectionReservation = false;
                reservedIntersectionPathIndex = -1;
                releasedReservationThisFrame = true;

                debugMessage =
                    $"Released intersection {reservedIntersection}.";
            }
            else
            {
                // Continue through the intersection this truck owns.
                return false;
            }
        }

        int nextIntersectionIndex = -1;
        Vector2Int nextIntersection = Vector2Int.zero;

        int scanStart = Mathf.Max(
            0,
            nearestPathIndex + (releasedReservationThisFrame ? 1 : 0));

        for (int i = scanStart; i < currentPath.Count; i++)
        {
            Vector2Int anchor =
                roadNetwork.GetRoadAnchor(currentPath[i]);

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
            nextIntersectionIndex -
            Mathf.Max(0, intersectionLookAheadModules));

        // Continue driving until reaching the holding point.
        if (nearestPathIndex < holdingPathIndex)
            return false;

        // Cancel an outdated request if the next intersection changed.
        if (waitingForIntersection &&
            requestedIntersection != nextIntersection)
        {
            intersectionManager.CancelRequest(this);
            waitingForIntersection = false;
            requestedIntersectionPathIndex = -1;
        }

        if (!waitingForIntersection)
        {
            requestedIntersection = nextIntersection;
            requestedIntersectionPathIndex = nextIntersectionIndex;
            waitingForIntersection = true;

            intersectionManager.RequestEntry(
                requestedIntersection,
                this);

            debugMessage =
                $"Waiting for intersection {requestedIntersection}.";
        }

        if (intersectionManager.HasReservation(
                requestedIntersection,
                this))
        {
            reservedIntersection = requestedIntersection;
            reservedIntersectionPathIndex =
                requestedIntersectionPathIndex;

            hasIntersectionReservation = true;
            waitingForIntersection = false;
            requestedIntersectionPathIndex = -1;

            debugMessage =
                $"Granted intersection {reservedIntersection}.";

            // Permission granted; normal movement may continue this frame.
            return false;
        }

        // Hold position while waiting for permission.
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
        if (currentPath == null ||
            currentPath.Count == 0 ||
            roadNetwork == null ||
            gridManager == null)
        {
            return 0;
        }

        int nearestIndex = 0;
        float nearestDistance = float.MaxValue;

        Vector3 currentPosition = transform.position;
        currentPosition.y = 0f;

        for (int i = 0; i < currentPath.Count; i++)
        {
            Vector3 roadPosition =
                GetRoadWorldPosition(currentPath[i]);

            roadPosition.y = 0f;

            float distance =
                (roadPosition - currentPosition).sqrMagnitude;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }


    // ---------------------------------------------------------
    // RETURN PATH TO EXIT
    // ---------------------------------------------------------

    private bool TryCreatePath(
        Vector3 fromWorldPosition,
        Vector2Int destinationCell)
    {
        Vector2Int fromCell = GetRoadModuleAnchor(
            gridManager.WorldToGrid(fromWorldPosition));

        destinationCell = GetRoadModuleAnchor(destinationCell);

        if (!roadNetwork.IsRoad(fromCell))
        {
            debugMessage =
                $"Current approach module {fromCell} is not a road.";
            return false;
        }

        currentPath = roadNetwork.FindPath(fromCell, destinationCell);

        if (currentPath == null || currentPath.Count == 0)
            return false;

        debugPathLength = currentPath.Count;
        return true;
    }

    // ---------------------------------------------------------
    // WORLD POSITIONS AND MOVEMENT
    // ---------------------------------------------------------

    private Vector3 GetRoadWorldPosition(Vector2Int cell)
    {
        Vector3 position = GetRoadModuleWorldPosition(cell);
        position.y += heightOffset;
        return position;
    }

    private Vector3 GetRoadModuleWorldPosition(Vector2Int anchor)
    {
        anchor = GetRoadModuleAnchor(anchor);

        Vector3 position = gridManager.GridToWorld(anchor);

        float moduleWorldSize =
            gridManager.CellSize * roadModuleSize;

        position.x += moduleWorldSize * 0.5f;
        position.z += moduleWorldSize * 0.5f;

        return position;
    }

    private Vector2Int GetRoadModuleAnchor(Vector2Int cell)
    {
        int size = Mathf.Max(1, roadModuleSize);

        int anchorX =
            Mathf.FloorToInt((float)cell.x / size) * size;

        int anchorY =
            Mathf.FloorToInt((float)cell.y / size) * size;

        return new Vector2Int(anchorX, anchorY);
    }

    private Vector3 GetVanStopWorldPosition()
    {
        if (vanStopPoint == null)
            return transform.position;

        Vector3 target = vanStopPoint.position;
        target.y += heightOffset;
        return target;
    }

    private bool MoveToPoint(
        Vector3 targetPosition,
        float speed)
    {
        Vector3 direction = targetPosition - transform.position;

        Vector3 flatDirection = direction;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(
                    flatDirection.normalized,
                    Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            Mathf.Max(0f, speed) * Time.deltaTime);

        return Vector3.Distance(
            transform.position,
            targetPosition) <= 0.01f;
    }

    private Transform FindChildByName(
        Transform parent,
        string childName)
    {
        foreach (Transform child in
                 parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
                return child;
        }

        return null;
    }

    // ---------------------------------------------------------
    // STATE AND CLEANUP
    // ---------------------------------------------------------

    private void SetState(TruckState newState)
    {
        state = newState;
        debugState = state.ToString();

        if (state == TruckState.Arrived)
        {
            ReleaseCargoZone();
            debugMessage = "Truck reached its exit.";

            if (destroyOnExit)
            {
                debugDestructionReason = "Reached exit.";
                Destroy(gameObject);
            }
        }
    }

    private void FinishRoute(string message)
    {
        debugMessage = message;

        Debug.LogWarning(
            $"CargoDeliveryTruck: {message} " +
            $"Flight: {GetFlightLabel()} | " +
            $"Operation: {GetOperationLabel()}",
            this);

        ReleaseCargoZone();

        state = TruckState.Arrived;
        debugState = state.ToString();

        if (destroyOnExit)
        {
            debugDestructionReason = message;
            Destroy(gameObject);
        }
    }

    private void ReleaseCargoZone()
    {
        if (reservedCargoZone != null)
        {
            ReservedCargoZones.Remove(reservedCargoZone);
            reservedCargoZone = null;
        }

        reservedCargoZoneComponent = null;
        vanStopPoint = null;
        cargoAreaPoint = null;
    }

    private void OnDisable()
    {
        if (intersectionManager != null)
        {
            intersectionManager.CancelRequest(this);

            if (hasIntersectionReservation)
            {
                intersectionManager.Release(
                    reservedIntersection,
                    this);
            }
        }

        hasIntersectionReservation = false;
        waitingForIntersection = false;
        reservedIntersectionPathIndex = -1;
        requestedIntersectionPathIndex = -1;

        ReleaseCargoZone();
    }

    private void OnDestroy()
    {
        string flight =
            string.IsNullOrWhiteSpace(deliveryFlightNumber)
                ? "Unknown"
                : deliveryFlightNumber;

        string operation =
            string.IsNullOrWhiteSpace(deliveryOperationId)
                ? "Unknown"
                : deliveryOperationId;

        string reason =
            string.IsNullOrWhiteSpace(debugDestructionReason)
                ? "Unknown; inspect other scripts"
                : debugDestructionReason;

        Debug.LogWarning(
            "CARGO TRUCK DESTROYED | " +
            $"Flight: {flight} | " +
            $"Operation: {operation} | " +
            $"Last State: {state} | " +
            $"Reason: {reason} | " +
            $"Last Message: {debugMessage}",
            this);
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

        DrawTrafficRayGizmo(origin, left60, new Color(1f, 0.5f, 0f));
        DrawTrafficRayGizmo(origin, left30, Color.yellow);
        DrawTrafficRayGizmo(origin, forward, Color.green);
        DrawTrafficRayGizmo(origin, right30, Color.cyan);
        DrawTrafficRayGizmo(origin, right60, new Color(0.5f, 0.3f, 1f));
    }

    private void DrawTrafficRayGizmo(
        Vector3 origin,
        Vector3 direction,
        Color color)
    {
        if (!Application.isPlaying)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(
                origin,
                origin + direction.normalized * trafficDetectionDistance);
            return;
        }

        float rayLength = trafficDetectionDistance;

        if (Physics.Raycast(
                origin,
                direction,
                out RaycastHit hit,
                trafficDetectionDistance,
                trafficLayer,
                QueryTriggerInteraction.Ignore))
        {
            Component vehicle = GetVehicleComponent(hit.collider);

            if (vehicle != null && ShouldDetectVehicle(vehicle))
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
