
using System.Collections.Generic;
using UnityEngine;

public class CargoDeliveryTruck : MonoBehaviour
{
    private enum TruckState
    {
        Idle,
        WaitingForCargoZone,
        DrivingToCargoZone,
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

    [Header("Route")]
    [SerializeField] private Vector2Int startCell;
    [SerializeField] private Vector2Int exitCell;
    [SerializeField] private bool snapToStartCell = true;

    [Header("Movement")]
    [Min(0.1f)]
    [SerializeField] private float movementSpeed = 5f;

    [Min(0.1f)]
    [SerializeField] private float rotationSpeed = 8f;

    [SerializeField] private float heightOffset = 0.2f;

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
    [SerializeField] private string debugLastRayHitCollider = "None";
    [SerializeField] private string debugLastRayDirection = "None";
    [SerializeField] private float debugLastRayDistance;
    [SerializeField] private string debugBlockingCollider = "None";
    [SerializeField] private float debugBlockingDistance;

    [TextArea(3, 8)]
    [SerializeField] private string debugRaycastSummary = "Not tested";

    [SerializeField] private string debugTrafficDecision = "Not tested";
    [SerializeField] private string debugDestructionReason = "Not destroyed";

    private TruckState state = TruckState.Idle;

    private List<Vector2Int> currentPath = new List<Vector2Int>();
    private int pathIndex;
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

    public void InitializeRoute(
        Vector2Int newStartCell,
        Vector2Int newExitCell)
    {
        InitializeRoute(
            newStartCell,
            newExitCell,
            string.Empty,
            string.Empty
        );
    }

    public void InitializeRoute(
        Vector2Int newStartCell,
        Vector2Int newExitCell,
        string operationId,
        string flightNumber)
    {
        startCell = newStartCell;
        exitCell = newExitCell;
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
        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

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

            case TruckState.DrivingToVanStop:
                if (MoveToPoint(
                        GetVanStopWorldPosition(),
                        movementSpeed))
                {
                    if (!cargoUnloaded)
                        UnloadCargoBoxes();

                    unloadingTimer = unloadingDuration;
                    SetState(TruckState.Unloading);

                    debugMessage =
                        $"Arrived at VanStopPoint. " +
                        $"{debugBoxesUnloaded} boxes unloaded.";
                }
                break;

            case TruckState.Unloading:
                unloadingTimer -= Time.deltaTime;

                if (unloadingTimer <= 0f)
                {
                    SetState(TruckState.ReturningToRoad);
                    debugMessage = "Unloading finished.";
                }
                break;

            case TruckState.ReturningToRoad:
                if (MoveToPoint(
                        approachWorldPosition,
                        movementSpeed))
                {
                    ReleaseCargoZone();

                    if (!TryCreatePath(
                            approachWorldPosition,
                            exitCell))
                    {
                        FinishRoute(
                            "Could not find a road path to the exit."
                        );
                        break;
                    }

                    SetState(TruckState.DrivingToExit);
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
                "RoadNetwork or GridManager reference is missing."
            );
            return;
        }

        if (!roadNetwork.IsRoad(startCell))
        {
            FinishRoute($"Start cell {startCell} is not a road.");
            return;
        }

        if (!roadNetwork.IsRoad(exitCell))
        {
            FinishRoute($"Exit cell {exitCell} is not a road.");
            return;
        }

        if (snapToStartCell)
        {
            Vector3 startPosition =
                gridManager.GridToWorldCenter(startCell);

            startPosition.y += heightOffset;
            transform.position = startPosition;
        }

        if (!TryReserveCargoZone())
        {
            EnterCargoZoneWaitingState();
            return;
        }

        StartDrivingWithReservedCargoZone();
    }

    private void EnterCargoZoneWaitingState()
    {
        state = TruckState.WaitingForCargoZone;
        debugState = state.ToString();
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
                this
            );

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

        Vector2Int approachCell =
            gridManager.WorldToGrid(approachWorldPosition);

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

        pathIndex = 1;
        debugPathLength = currentPath.Count;
        currentSpeed = movementSpeed;

        SetTruckVisible(true);
        SetState(TruckState.DrivingToCargoZone);

        debugMessage = routeInitializedBySpawner
            ? "Route initialized using cells from spawner."
            : "Route initialized using Inspector route cells.";

        Debug.Log(
            "CARGO TRUCK READY | " +
            $"Flight: {GetFlightLabel()} | " +
            $"Operation: {GetOperationLabel()} | " +
            $"Cargo zone: {debugCargoZone} | " +
            "Truck is now visible and driving.",
            this
        );
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

    private bool TryReserveCargoZone()
    {
        PlacedBuilding[] buildings =
            FindObjectsByType<PlacedBuilding>(
                FindObjectsSortMode.None
            );

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
                FindChildByName(
                    building.transform,
                    "VanStopPoint"
                );

            Transform candidateCargoArea =
                FindChildByName(
                    building.transform,
                    "CargoAreaPoint"
                );

            if (candidateVanStop == null ||
                candidateCargoArea == null)
            {
                continue;
            }

            List<Vector2Int> approachCells =
                GetApproachCells(building);

            foreach (Vector2Int approachCell in approachCells)
            {
                if (!roadNetwork.IsRoad(approachCell))
                    continue;

                List<Vector2Int> path =
                    roadNetwork.FindPath(
                        startCell,
                        approachCell
                    );

                if (path == null ||
                    path.Count == 0 ||
                    path.Count >= shortestPathLength)
                {
                    continue;
                }

                shortestPathLength = path.Count;
                bestBuilding = building;
                bestCargoZone = candidateCargoZone;
                bestVanStopPoint = candidateVanStop;
                bestCargoAreaPoint = candidateCargoArea;
                bestApproachCell = approachCell;
                bestPath = path;
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

        approachWorldPosition =
            gridManager.GridToWorldCenter(bestApproachCell);

        approachWorldPosition.y += heightOffset;

        currentPath = bestPath;
        debugPathLength = bestPath.Count;
        debugCargoZone = bestBuilding.name;

        return true;
    }

    private List<Vector2Int> GetApproachCells(
        PlacedBuilding building)
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
                this
            );
            return;
        }

        if (cargoAreaPoint == null ||
            reservedCargoZoneComponent == null)
        {
            debugMessage =
                "Cannot unload cargo: cargo zone or cargo area is missing.";

            Debug.LogError(
                "CargoDeliveryTruck: CargoZone or CargoAreaPoint is missing.",
                this
            );
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
                this
            );
        }

        int columns = Mathf.Max(1, boxesPerRow);
        float boxHeight = cargoBoxPrefab.transform.localScale.y;

        Vector3 basePosition =
            cargoAreaPoint.position +
            Vector3.up * (boxHeight * 0.5f);

        Vector3 right = cargoAreaPoint.right;
        Vector3 forward = cargoAreaPoint.forward;

        int rows = Mathf.CeilToInt(
            (float)CargoBoxCount / columns
        );

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
                cargoAreaPoint.rotation
            );

            cargoBox.name = $"CargoBox_{i + 1:00}";

            if (hasFlightAssignment)
            {
                CargoPackage package =
                    cargoBox.GetComponent<CargoPackage>();

                if (package == null)
                {
                    Debug.LogError(
                        $"CargoDeliveryTruck: CargoBox prefab is missing " +
                        $"CargoPackage. Box {i + 1} is not registered.",
                        cargoBox
                    );
                }
                else
                {
                    string packageId =
                        $"{deliveryOperationId}:OUT:{i + 1:00}";

                    package.Initialize(
                        packageId,
                        CargoDirection.Outgoing,
                        deliveryOperationId,
                        reservedCargoZoneComponent
                    );

                    bool registered =
                        reservedCargoZoneComponent.RegisterPackage(package);

                    if (!registered)
                    {
                        Debug.LogError(
                            $"CargoDeliveryTruck: Could not register " +
                            $"package {packageId} with cargo zone " +
                            $"{reservedCargoZoneComponent.name}.",
                            cargoBox
                        );
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

    private void MoveAlongPath(TruckState stateAfterPath)
    {
        if (currentPath == null || currentPath.Count == 0)
        {
            FinishRoute("The current road path is empty.");
            return;
        }

        if (pathIndex >= currentPath.Count)
        {
            SetState(stateAfterPath);
            return;
        }

        Vector3 targetPosition =
            GetRoadWorldPosition(currentPath[pathIndex]);

        float trafficAwareSpeed = GetTrafficFollowingSpeed();

        MoveToPoint(targetPosition, trafficAwareSpeed);

        if (Vector3.Distance(
                transform.position,
                targetPosition) < 0.01f)
        {
            transform.position = targetPosition;
            pathIndex++;
        }
    }

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
                    safeDistance + 0.01f
                );

            float normalizedDistance =
                Mathf.InverseLerp(
                    safeDistance,
                    detectionDistance,
                    hit.distance
                );

            targetSpeed = Mathf.Lerp(
                0f,
                movementSpeed,
                normalizedDistance
            );

            debugTrafficDecision =
                $"Braking for {debugBlockingCollider}";
        }
        else
        {
            debugTrafficDecision =
                "No blocking vehicle detected";
        }

        float rate = targetSpeed < currentSpeed
            ? braking
            : acceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            rate * Time.deltaTime
        );

        return currentSpeed;
    }

    private bool TryGetVehicleAhead(out RaycastHit hit)
    {
        debugLastRayHitCollider = "None";
        debugLastRayDirection = "None";
        debugLastRayDistance = 0f;
        debugBlockingCollider = "None";
        debugBlockingDistance = 0f;
        debugRaycastSummary = "";

        Vector3 origin =
            transform.position +
            Vector3.up * rayOriginHeight +
            transform.forward * rayOriginForwardOffset;

        Vector3 forward = transform.forward;
        Vector3 left45 =
            Quaternion.Euler(0f, -45f, 0f) * forward;
        Vector3 right45 =
            Quaternion.Euler(0f, 45f, 0f) * forward;

        bool foundVehicle = false;
        float closestDistance = float.MaxValue;
        RaycastHit closestHit = new RaycastHit();

        CheckTrafficRay(
            origin,
            forward,
            "Forward",
            ref foundVehicle,
            ref closestDistance,
            ref closestHit
        );

        CheckTrafficRay(
            origin,
            left45,
            "Left 45",
            ref foundVehicle,
            ref closestDistance,
            ref closestHit
        );

        CheckTrafficRay(
            origin,
            right45,
            "Right 45",
            ref foundVehicle,
            ref closestDistance,
            ref closestHit
        );

        hit = closestHit;

        if (foundVehicle)
        {
            debugBlockingCollider = closestHit.collider.name;
            debugBlockingDistance = closestHit.distance;

            debugTrafficDecision =
                $"Blocking vehicle at {closestHit.distance:F2}m";
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

        Component detectedVehicle =
            GetVehicleComponent(rayHit.collider);

        if (detectedVehicle == null)
        {
            debugRaycastSummary +=
                $"{rayName}: {rayHit.collider.name} " +
                $"({rayHit.distance:F2}m) - not a recognized vehicle\n";
            return;
        }

        if (detectedVehicle.gameObject == gameObject)
        {
            debugRaycastSummary +=
                $"{rayName}: self, ignored\n";
            return;
        }

        debugRaycastSummary +=
            $"{rayName}: {rayHit.collider.name} " +
            $"({rayHit.distance:F2}m) - blocking vehicle\n";

        if (rayHit.distance < closestDistance)
        {
            closestDistance = rayHit.distance;
            closestHit = rayHit;
            foundVehicle = true;
        }
    }

    private Component GetVehicleComponent(Collider hitCollider)
    {
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

    private bool TryCreatePath(
        Vector3 fromWorldPosition,
        Vector2Int destinationCell)
    {
        Vector2Int fromCell =
            gridManager.WorldToGrid(fromWorldPosition);

        if (!roadNetwork.IsRoad(fromCell))
        {
            debugMessage =
                $"Current approach cell {fromCell} is not a road.";
            return false;
        }

        currentPath = roadNetwork.FindPath(
            fromCell,
            destinationCell
        );

        if (currentPath == null || currentPath.Count == 0)
            return false;

        pathIndex = 1;
        debugPathLength = currentPath.Count;

        return true;
    }

    private Vector3 GetRoadWorldPosition(Vector2Int cell)
    {
        Vector3 position = gridManager.GridToWorldCenter(cell);
        position.y += heightOffset;
        return position;
    }

    private Vector3 GetVanStopWorldPosition()
    {
        Vector3 target = vanStopPoint.position;
        target.y += heightOffset;
        return target;
    }

    private bool MoveToPoint(
        Vector3 targetPosition,
        float speed)
    {
        Vector3 direction = targetPosition - transform.position;

        if (direction.sqrMagnitude <= 0.01f)
        {
            transform.position = targetPosition;
            return true;
        }

        Vector3 flatDirection = direction;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(flatDirection.normalized);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            speed * Time.deltaTime
        );

        return Vector3.Distance(
            transform.position,
            targetPosition
        ) <= 0.01f;
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
            this
        );

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

    private void OnDestroy()
    {
        string flight = string.IsNullOrWhiteSpace(deliveryFlightNumber)
            ? "Unknown"
            : deliveryFlightNumber;

        string operation = string.IsNullOrWhiteSpace(deliveryOperationId)
            ? "Unknown"
            : deliveryOperationId;

        string reason = string.IsNullOrWhiteSpace(debugDestructionReason)
            ? "Unknown; inspect other scripts"
            : debugDestructionReason;

        Debug.LogWarning(
            "CARGO TRUCK DESTROYED | " +
            $"Flight: {flight} | " +
            $"Operation: {operation} | " +
            $"Last State: {state} | " +
            $"Reason: {reason} | " +
            $"Last Message: {debugMessage}",
            this
        );
    }

    private void OnDisable()
    {
        ReleaseCargoZone();
    }
}
