using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-level coordinator for all player-assigned Flight School rooms.
/// Add one instance to a scene GameObject; it does not belong on a building
/// prefab because rooms are created dynamically from foundations and walls.
/// </summary>
public class FlightSchoolManager : MonoBehaviour
{
    [Header("Room Detection")]
    [SerializeField] private RoomPurposeController roomPurposeController;
    [SerializeField, Min(0.1f)] private float roomRefreshInterval = 0.5f;

    [Header("Training Aircraft")]
    [SerializeField] private GameObject trainingAircraftPrefab;

    [Header("Lesson Reward")]
    [SerializeField, Min(1)] private int lessonReward = 300;

    [Header("Runtime Status")]
    [SerializeField]
    private string debugStatus =
        "Waiting for an assigned Flight School room.";

    private sealed class SchoolOperation
    {
        public Vector2Int anchor;
        public RoomDetectionController.DetectedRoom room;
        public bool assigned;
        public bool valid;
        public string validationMessage;
        public GameObject aircraft;
        public HobbyAircraftParkingSpot parkingSpot;
        public bool rewardPaid;
    }

    private readonly Dictionary<Vector2Int, SchoolOperation> operations =
        new Dictionary<Vector2Int, SchoolOperation>();

    private AirportEconomyManager economyManager;
    private float nextRoomRefreshTime;
    private static FlightSchoolManager activeManager;

    private void Awake()
    {
        if (activeManager != null && activeManager != this)
        {
            Debug.LogError(
                "Only one scene-level FlightSchoolManager should be active. " +
                "The duplicate manager has been disabled.",
                this);
            enabled = false;
            return;
        }

        activeManager = this;
        ResolveReferences();
    }

    private void Update()
    {
        if (!enabled)
            return;

        ResolveReferences();

        if (Time.unscaledTime >= nextRoomRefreshTime)
        {
            nextRoomRefreshTime =
                Time.unscaledTime + Mathf.Max(0.1f, roomRefreshInterval);

            RefreshSchoolRooms();
        }

        foreach (SchoolOperation operation in operations.Values)
        {
            // An active lesson is deliberately allowed to finish even if
            // the room becomes invalid or stops being detected.
            if (operation.aircraft != null)
            {
                CheckLessonCompletion(operation);
                continue;
            }

            if (!operation.assigned || !operation.valid || operation.rewardPaid)
                continue;

            TrySpawnTrainingAircraft(operation);
        }
    }

    private void ResolveReferences()
    {
        if (roomPurposeController == null)
            roomPurposeController = FindFirstObjectByType<RoomPurposeController>();

        if (economyManager == null)
            economyManager = FindFirstObjectByType<AirportEconomyManager>();
    }

    private void RefreshSchoolRooms()
    {
        if (roomPurposeController == null)
        {
            debugStatus = "RoomPurposeController was not found in the scene.";
            return;
        }

        foreach (SchoolOperation operation in operations.Values)
        {
            operation.assigned = false;
            operation.room = null;
            operation.valid = false;
            operation.validationMessage =
                "Room is no longer detected as an assigned Flight School.";
        }

        List<RoomDetectionController.DetectedRoom> rooms =
            roomPurposeController.GetAssignedFlightSchoolRooms();

        foreach (RoomDetectionController.DetectedRoom room in rooms)
        {
            if (room == null || room.cells == null || room.cells.Count == 0)
                continue;

            Vector2Int anchor = GetAnchor(room);

            if (!operations.TryGetValue(anchor, out SchoolOperation operation))
            {
                operation = new SchoolOperation
                {
                    anchor = anchor,
                    rewardPaid = false
                };

                operations.Add(anchor, operation);
            }

            operation.room = room;
            operation.assigned = true;
            operation.valid = roomPurposeController.ValidateFlightSchool(
                room, out string reason);

            operation.validationMessage = operation.valid
                ? "Operational."
                : reason;
        }

        int assignedCount = 0;
        int validCount = 0;

        foreach (SchoolOperation operation in operations.Values)
        {
            if (!operation.assigned)
                continue;

            assignedCount++;

            if (operation.valid)
                validCount++;
        }

        debugStatus = assignedCount == 0
            ? "Waiting for an assigned Flight School room."
            : $"Flight Schools: {validCount}/{assignedCount} operational.";
    }

    private Vector2Int GetAnchor(
        RoomDetectionController.DetectedRoom room)
    {
        Vector2Int anchor = room.cells[0];

        foreach (Vector2Int cell in room.cells)
        {
            if (cell.x < anchor.x ||
                (cell.x == anchor.x && cell.y < anchor.y))
            {
                anchor = cell;
            }
        }

        return anchor;
    }

    private void TrySpawnTrainingAircraft(SchoolOperation operation)
    {
        if (trainingAircraftPrefab == null)
        {
            operation.validationMessage =
                "Assign the training aircraft prefab in the Inspector.";
            return;
        }

        HobbyAircraftParkingSpot[] parkingSpots =
            FindObjectsByType<HobbyAircraftParkingSpot>(
                FindObjectsSortMode.None);

        foreach (HobbyAircraftParkingSpot spot in parkingSpots)
        {
            if (spot == null ||
                !spot.IsPlacedBuilding ||
                !spot.ReservedForFlightSchool ||
                spot.IsOccupied)
            {
                continue;
            }

            GameObject aircraft = Instantiate(
                trainingAircraftPrefab,
                spot.ParkingWorldPosition,
                spot.ParkingWorldRotation);

            if (aircraft == null)
                continue;

            if (!spot.TryReserve(aircraft, true))
            {
                Destroy(aircraft);
                continue;
            }

            AircraftFlightIdentity identity =
                aircraft.GetComponent<AircraftFlightIdentity>();

            TrainingFlightController trainingController =
                aircraft.GetComponent<TrainingFlightController>();

            if (identity == null || trainingController == null)
            {
                Debug.LogError(
                    $"{name}: The training aircraft prefab needs " +
                    "AircraftFlightIdentity and TrainingFlightController.",
                    aircraft);

                spot.Release(aircraft);
                Destroy(aircraft);
                return;
            }

            string trainingNumber =
                $"TRAIN-{System.Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()}";

            identity.InitializeTrainingFlight(trainingNumber, 0, 0);

            if (!trainingController.StartTrainingLesson())
            {
                Debug.LogError(
                    $"{name}: The training lesson could not start for " +
                    $"Flight School room at {operation.anchor}.",
                    this);

                spot.Release(aircraft);
                Destroy(aircraft);
                return;
            }

            operation.aircraft = aircraft;
            operation.parkingSpot = spot;
            operation.validationMessage =
                $"Training aircraft active at parking spot '{spot.name}'.";

            Debug.Log(
                $"{name}: Started a training lesson for Flight School room " +
                $"at {operation.anchor}; aircraft spawned at {spot.name}.",
                this);

            return;
        }

        operation.validationMessage =
            "Waiting for an unoccupied parking spot designated for flight school.";
    }

    private void CheckLessonCompletion(SchoolOperation operation)
    {
        if (operation.aircraft == null)
            return;

        TrainingFlightController trainingController =
            operation.aircraft.GetComponent<TrainingFlightController>();

        if (trainingController == null)
        {
            operation.validationMessage =
                "Training controller missing from the active aircraft.";
            return;
        }

        if (trainingController.HasFailed)
        {
            operation.validationMessage =
                "Training lesson stopped. Check the aircraft's Console errors.";
            return;
        }

        if (!trainingController.IsLessonComplete || operation.rewardPaid)
            return;

        if (economyManager == null)
            economyManager = FindFirstObjectByType<AirportEconomyManager>();

        if (economyManager == null)
        {
            operation.validationMessage =
                "Lesson complete, but AirportEconomyManager was not found.";
            return;
        }

        if (!economyManager.AddRevenue(
                lessonReward,
                "Flight school lesson completed"))
        {
            operation.validationMessage =
                "Lesson complete, but reward payment failed. Will retry.";
            return;
        }

        operation.rewardPaid = true;
        operation.validationMessage =
            "Lesson complete. Paid $" + lessonReward + ". Aircraft remains parked.";

        Debug.Log(
            $"{name}: Flight School room at {operation.anchor} paid " +
            lessonReward + ". The training aircraft remains parked.",
            this);
    }

    private void OnDestroy()
    {
        if (activeManager == this)
            activeManager = null;

        // Preserve the previous manager's cleanup behavior if the scene
        // manager itself is intentionally removed.
        foreach (SchoolOperation operation in operations.Values)
        {
            if (operation.aircraft == null)
                continue;

            if (operation.parkingSpot != null)
                operation.parkingSpot.Release(operation.aircraft);

            Destroy(operation.aircraft);
            operation.aircraft = null;
        }
    }
}