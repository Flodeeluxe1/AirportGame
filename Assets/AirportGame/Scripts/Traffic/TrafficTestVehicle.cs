using System.Collections.Generic;
using UnityEngine;

public class TrafficTestVehicle : MonoBehaviour
{
    private enum VehicleState
    {
        Idle,
        DrivingToParking,
        WaitingForParking,
        EnteringParking,
        Parked,
        LeavingParking,
        DrivingToExit,
        Arrived
    }

    [Header("References")]
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private GridManager gridManager;

    [Header("Movement")]
    [SerializeField] private float speed = 3f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float heightOffset = 0.2f;

    [Header("Parking")]
    [SerializeField] private float parkingDuration = 5f;

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

    [Tooltip("Results from the forward, left, and right sensor rays.")]
    [TextArea(3, 8)]
    public string debugRaycastSummary = "Not tested";

    public string debugTrafficDecision = "Not tested";
    public float debugLastSensorUpdateTime;

    private Vector2Int startCell;
    private Vector2Int exitCell;

    private List<Vector2Int> path =
        new List<Vector2Int>();

    private int currentPathIndex;

    private ParkingSpace parkingSpace;
    private Vector2Int parkingApproachCell;

    private float parkingTimer;
    private float currentSpeed;

    private VehicleState state =
        VehicleState.Idle;

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
            roadNetwork =
                FindFirstObjectByType<RoadNetwork>();
        }

        if (gridManager == null)
        {
            gridManager =
                FindFirstObjectByType<GridManager>();
        }

        currentSpeed = speed;
        RefreshDebugInformation();
    }

    private void StartRoute()
    {
        if (roadNetwork == null)
        {
            Debug.LogError(
                "TrafficTestVehicle: RoadNetwork is missing."
            );
            return;
        }

        if (gridManager == null)
        {
            Debug.LogError(
                "TrafficTestVehicle: GridManager is missing."
            );
            return;
        }

        if (!TryFindParkingSpace())
        {
            Debug.Log(
                $"TRAFFIC NO PARKING CANDIDATE | Start: {startCell}"
            );

            state = VehicleState.Idle;
            return;
        }

        if (!parkingSpace.Reserve(gameObject))
        {
            Debug.Log(
                "TRAFFIC FAILED TO RESERVE PARKING."
            );

            state = VehicleState.Idle;
            return;
        }

        path = roadNetwork.FindPath(
            startCell,
            parkingApproachCell
        );

        if (path.Count == 0)
        {
            parkingSpace.CancelReservation(gameObject);
            parkingSpace = null;

            Debug.Log(
                $"TRAFFIC NO PATH TO PARKING | " +
                $"Start: {startCell} | " +
                $"Approach: {parkingApproachCell}"
            );

            state = VehicleState.Idle;
            return;
        }

        Debug.Log(
            $"TRAFFIC PARKING ROUTE | " +
            $"Start: {startCell} | " +
            $"Approach: {parkingApproachCell} | " +
            $"Queue Position: {parkingSpace.GetQueuePosition(gameObject)}"
        );

        LogPath("PARKING PATH");

        currentPathIndex = 1;

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
                MoveAlongPath();
                break;

            case VehicleState.WaitingForParking:
                UpdateParkingWaiting();
                break;

            case VehicleState.EnteringParking:
                MoveIntoParking();
                break;

            case VehicleState.Parked:
                UpdateParking();
                break;

            case VehicleState.LeavingParking:
                LeaveParking();
                break;

            case VehicleState.DrivingToExit:
                MoveAlongPath();
                break;
        }

        RefreshDebugInformation();
    }

    private void RefreshDebugInformation()
    {
        debugState = state.ToString();
        debugCurrentSpeed = currentSpeed;
    }

    private bool TryFindParkingSpace()
    {
        ParkingSpace[] parkingSpaces =
            FindObjectsByType<ParkingSpace>(
                FindObjectsSortMode.None
            );

        if (parkingSpaces.Length == 0)
            return false;

        ParkingSpace bestFreeSpace = null;
        Vector2Int bestFreeApproach = Vector2Int.zero;
        List<Vector2Int> bestFreePath = null;
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
                    roadNetwork.FindPath(
                        startCell,
                        approachCell
                    );

                if (candidatePath.Count == 0)
                    continue;

                if (candidatePath.Count < bestFreePathLength)
                {
                    bestFreePathLength = candidatePath.Count;
                    bestFreeSpace = candidate;
                    bestFreeApproach = approachCell;
                    bestFreePath = candidatePath;
                }
            }
        }

        if (bestFreeSpace != null)
        {
            parkingSpace = bestFreeSpace;
            parkingApproachCell = bestFreeApproach;
            path = bestFreePath;
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
                candidate.GetEstimatedAvailableTime(
                    parkingDuration
                );

            foreach (Vector2Int approachCell in approachCells)
            {
                List<Vector2Int> candidatePath =
                    roadNetwork.FindPath(
                        startCell,
                        approachCell
                    );

                if (candidatePath.Count == 0)
                    continue;

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
                    path = candidatePath;
                }
            }
        }

        if (bestFutureSpace == null)
            return false;

        parkingSpace = bestFutureSpace;
        parkingApproachCell = bestFutureApproach;

        float waitTime =
            Mathf.Max(0f, bestAvailableTime - Time.time);

        Debug.Log(
            $"TRAFFIC WAITING PARKING SELECTED | " +
            $"Estimated Available In: {waitTime:F1}s"
        );

        return true;
    }

    private bool TryGetApproachCells(
        ParkingSpace parkingSpace,
        out List<Vector2Int> approachCells)
    {
        approachCells = new List<Vector2Int>();

        PlacedBuilding placedBuilding =
            parkingSpace.GetComponent<PlacedBuilding>();

        if (placedBuilding == null)
            return false;

        Vector2Int origin = placedBuilding.Origin;
        int width = placedBuilding.Width;
        int depth = placedBuilding.Depth;

        if (width >= depth)
        {
            AddApproachIfRoad(
                approachCells,
                origin + new Vector2Int(-1, 0)
            );

            AddApproachIfRoad(
                approachCells,
                origin + new Vector2Int(width, 0)
            );
        }
        else
        {
            AddApproachIfRoad(
                approachCells,
                origin + new Vector2Int(0, -1)
            );

            AddApproachIfRoad(
                approachCells,
                origin + new Vector2Int(0, depth)
            );
        }

        return approachCells.Count > 0;
    }

    private void AddApproachIfRoad(
        List<Vector2Int> approachCells,
        Vector2Int cell)
    {
        if (roadNetwork.IsRoad(cell))
            approachCells.Add(cell);
    }

    private void MoveAlongPath()
    {
        if (currentPathIndex >= path.Count)
        {
            if (state == VehicleState.DrivingToParking)
            {
                BeginParkingEntry();
                return;
            }

            if (state == VehicleState.DrivingToExit)
            {
                Debug.Log(
                    $"TRAFFIC ARRIVED AT EXIT | Cell: {exitCell}"
                );

                Destroy(gameObject);
                return;
            }

            return;
        }

        Vector3 targetPosition =
            GetRoadWorldPosition(
                path[currentPathIndex]
            );

        float movementSpeed =
            GetTrafficFollowingSpeed();

        MoveTowards(targetPosition, movementSpeed);

        if (Vector3.Distance(
                transform.position,
                targetPosition) < 0.01f)
        {
            transform.position = targetPosition;

            Debug.Log(
                $"TRAFFIC REACHED ROAD CELL | {path[currentPathIndex]}"
            );

            currentPathIndex++;
        }
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
                $"Queue Position: {parkingSpace.GetQueuePosition(gameObject)}"
            );

            return;
        }

        Debug.Log(
            $"TRAFFIC ENTERING PARKING | Cell: {parkingApproachCell}"
        );

        state = VehicleState.EnteringParking;
    }

    private void UpdateParkingWaiting()
    {
        if (parkingSpace == null)
        {
            state = VehicleState.Idle;
            return;
        }

        if (!parkingSpace.CanVehicleEnter(gameObject))
        {
            currentSpeed = Mathf.MoveTowards(
                currentSpeed,
                0f,
                braking * Time.deltaTime
            );

            return;
        }

        Debug.Log("TRAFFIC PARKING SPACE NOW AVAILABLE");

        state = VehicleState.EnteringParking;
    }

    private void MoveIntoParking()
    {
        Vector3 parkingPosition =
            GetParkingWorldPosition(parkingSpace);

        MoveTowards(parkingPosition, speed);

        if (Vector3.Distance(
                transform.position,
                parkingPosition) < 0.01f)
        {
            transform.position = parkingPosition;

            if (!parkingSpace.ParkVehicle(
                    gameObject,
                    parkingDuration))
            {
                Debug.LogError(
                    "TrafficTestVehicle: Failed to occupy parking space."
                );
                return;
            }

            parkingTimer = parkingDuration;
            currentSpeed = speed;
            state = VehicleState.Parked;

            Debug.Log(
                $"TRAFFIC PARKED | Duration: {parkingDuration} seconds"
            );
        }
    }

    private void UpdateParking()
    {
        parkingTimer -= Time.deltaTime;

        if (parkingTimer > 0f)
            return;

        Debug.Log("TRAFFIC PARKING COMPLETE");

        parkingSpace.ReleaseVehicle(gameObject);
        state = VehicleState.LeavingParking;
    }

    private void LeaveParking()
    {
        Vector3 approachPosition =
            GetRoadWorldPosition(parkingApproachCell);

        float movementSpeed =
            GetTrafficFollowingSpeed();

        MoveTowards(approachPosition, movementSpeed);

        if (Vector3.Distance(
                transform.position,
                approachPosition) < 0.01f)
        {
            transform.position = approachPosition;

            Debug.Log(
                $"TRAFFIC LEFT PARKING | Road Cell: {parkingApproachCell}"
            );

            path = roadNetwork.FindPath(
                parkingApproachCell,
                exitCell
            );

            if (path.Count == 0)
            {
                Debug.Log(
                    $"TRAFFIC NO PATH TO EXIT | " +
                    $"From: {parkingApproachCell} | " +
                    $"Exit: {exitCell}"
                );

                state = VehicleState.Idle;
                return;
            }

            LogPath("EXIT PATH");
            currentPathIndex = 1;

            if (path.Count == 1)
            {
                Debug.Log(
                    $"TRAFFIC ARRIVED AT EXIT | Cell: {exitCell}"
                );

                Destroy(gameObject);
                return;
            }

            state = VehicleState.DrivingToExit;
        }
    }

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
                speed,
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

        float rate =
            targetSpeed < currentSpeed
                ? braking
                : acceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            rate * Time.deltaTime
        );

        return currentSpeed;
    }

    private bool TryGetVehicleAhead(
        out RaycastHit hit)
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

        Vector3 left45 =
            Quaternion.Euler(0f, -45f, 0f) * forward;

        Vector3 right45 =
            Quaternion.Euler(0f, 45f, 0f) * forward;

        bool foundVehicle = false;
        float closestDistance = float.MaxValue;
        RaycastHit closestHit = new RaycastHit();

        CheckTrafficRay(
            origin, forward, "Forward",
            ref foundVehicle, ref closestDistance, ref closestHit
        );

        CheckTrafficRay(
            origin, left45, "Left 45",
            ref foundVehicle, ref closestDistance, ref closestHit
        );

        CheckTrafficRay(
            origin, right45, "Right 45",
            ref foundVehicle, ref closestDistance, ref closestHit
        );

        hit = closestHit;

        if (foundVehicle)
        {
            debugBlockingCollider =
                closestHit.collider.name;

            TrafficTestVehicle blockingVehicle =
                closestHit.collider.GetComponentInParent<TrafficTestVehicle>();

            if (blockingVehicle != null)
                debugBlockingVehicleState =
                    blockingVehicle.state.ToString();

            debugBlockingDistance =
                closestHit.distance;

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

        debugLastRayHitCollider =
            rayHit.collider.name;

        debugLastRayDirection =
            rayName;

        debugLastRayDistance =
            rayHit.distance;

        debugRaycastSummary +=
            $"{rayName}: {rayHit.collider.name} " +
            $"({rayHit.distance:F2}m)";

        TrafficTestVehicle vehicle =
            rayHit.collider.GetComponentInParent<TrafficTestVehicle>();

        if (vehicle == null)
        {
            debugRaycastSummary +=
                " — no TrafficTestVehicle component\n";

            return;
        }

        if (vehicle == this)
        {
            debugRaycastSummary +=
                " — self, ignored\n";

            return;
        }

        if (!ShouldDetectVehicle(vehicle))
        {
            debugRaycastSummary +=
                $" — ignored by priority rule ({vehicle.state})\n";

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

    private bool ShouldDetectVehicle(
    TrafficTestVehicle vehicle)
    {
        if (vehicle == null)
            return false;

        // Parked vehicles are not moving traffic obstacles.
        if (vehicle.state == VehicleState.Parked)
            return false;

        if (vehicle.state == VehicleState.Idle ||
            vehicle.state == VehicleState.Arrived)
        {
            return false;
        }

        // A car leaving parking has priority over cars
        // approaching or entering parking.
        if (state == VehicleState.LeavingParking)
        {
            if (vehicle.state == VehicleState.DrivingToParking ||
                vehicle.state == VehicleState.WaitingForParking ||
                vehicle.state == VehicleState.EnteringParking)
            {
                return false;
            }
        }

        if (vehicle.state == VehicleState.LeavingParking)
        {
            if (state == VehicleState.DrivingToParking ||
                state == VehicleState.WaitingForParking ||
                state == VehicleState.EnteringParking)
            {
                return true;
            }
        }

        // Intersection right-of-way:
        // apply a deterministic priority to perpendicular
        // moving vehicles to prevent mutual braking.
        bool thisCarMovingOnRoad =
            state == VehicleState.DrivingToParking ||
            state == VehicleState.DrivingToExit;

        bool otherCarMovingOnRoad =
            vehicle.state == VehicleState.DrivingToParking ||
            vehicle.state == VehicleState.DrivingToExit;

        if (thisCarMovingOnRoad && otherCarMovingOnRoad)
        {
            float alignment = Vector3.Dot(
                transform.forward.normalized,
                vehicle.transform.forward.normalized
            );

            // Perpendicular or near-perpendicular approaches.
            if (alignment < 0.5f)
            {
                bool thisCarHasPriority =
                    gameObject.GetEntityId()
                        .CompareTo(vehicle.gameObject.GetEntityId()) < 0;

                // The priority car ignores the yielding car.
                // The yielding car detects the priority car.
                return !thisCarHasPriority;
            }
        }

        // Normal traffic-following behavior.
        return true;
    }

    private Vector3 GetRoadWorldPosition(Vector2Int cell)
    {
        Vector3 position =
            roadNetwork.GetRoadCenterWorldPosition(cell, gridManager);

        position.y += heightOffset;
        return position;
    }

    private Vector3 GetParkingWorldPosition(
        ParkingSpace parkingSpace)
    {
        PlacedBuilding placedBuilding =
            parkingSpace.GetComponent<PlacedBuilding>();

        Vector3 position =
            gridManager.GridToWorld(
                placedBuilding.Origin
            );

        position += new Vector3(
            placedBuilding.Width *
            gridManager.CellSize * 0.5f,
            0f,
            placedBuilding.Depth *
            gridManager.CellSize * 0.5f
        );

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
                    Vector3.up
                );

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime
                );
        }

        transform.position =
            Vector3.MoveTowards(
                transform.position,
                targetPosition,
                movementSpeed * Time.deltaTime
            );
    }

    private void LogPath(string title)
    {
        string pathText = "";

        foreach (Vector2Int cell in path)
            pathText += cell + " ";

        Debug.Log($"{title}: {pathText}");
    }
}