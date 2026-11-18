using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Creates the daily Flight School student cohort. Each student arrives in
/// their own SUV, parks through the ordinary ParkingSpace traffic system,
/// and walks to a reserved classroom chair.
/// </summary>
[DisallowMultipleComponent]
public class FlightSchoolStudentManager : MonoBehaviour
{
    [Header("Prefab References")]
    [SerializeField] private GameObject studentCarPrefab;
    [SerializeField] private GameObject studentPrefab;

    [Header("Scene References")]
    [SerializeField] private TrafficTestSpawner trafficSpawner;
    [SerializeField] private FlightSchoolManager flightSchoolManager;
    [SerializeField] private RoomPurposeController roomPurposeController;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Operating Hours")]
    [SerializeField, Range(0, 23)] private int openingHour = 8;
    [SerializeField, Range(0, 23)] private int closingHour = 18;

    [Header("Arrival Traffic")]
    [Tooltip("Small real-time gap between cars prevents the entire cohort spawning on the same road position.")]
    [SerializeField, Min(0f)] private float carSpawnInterval = 0.35f;

    [Header("Student Placement")]
    [SerializeField, Min(0.1f)] private float carExitOffset = 1.5f;

    [Header("Runtime Status")]
    [SerializeField] private string debugStatus = "Waiting for Flight School opening.";

    private sealed class SeatSlot
    {
        public InteractionProvider provider;
        public InteractionDefinition interaction;
    }

    private sealed class StudentRecord
    {
        public Vector2Int roomAnchor;
        public TrafficTestVehicle vehicle;
        public FlightSchoolStudentAgent student;
    }

    private readonly Dictionary<Vector2Int, long> spawnedDayByRoom =
        new Dictionary<Vector2Int, long>();

    private readonly Dictionary<Vector2Int, List<SeatSlot>> seatsByRoom =
        new Dictionary<Vector2Int, List<SeatSlot>>();

    private readonly List<StudentRecord> students =
        new List<StudentRecord>();

    private float nextRoomCheckTime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();

        if (flightSchoolManager == null ||
            roomPurposeController == null ||
            gameTimeManager == null ||
            trafficSpawner == null)
        {
            debugStatus = "Waiting for FlightSchoolManager, RoomPurposeController, GameTimeManager and TrafficTestSpawner.";
            return;
        }

        int currentSeconds = gameTimeManager.GetGameTimeSeconds();
        int openingSeconds = openingHour * 60 * 60;
        int closingSeconds = closingHour * 60 * 60;

        if (currentSeconds < openingSeconds ||
            currentSeconds >= closingSeconds)
        {
            if (currentSeconds >= closingSeconds)
                debugStatus = "Flight School is closed for today.";

            return;
        }

        if (Time.unscaledTime < nextRoomCheckTime)
            return;

        nextRoomCheckTime =
            Time.unscaledTime + 1f;

        SpawnDueSchoolCohorts();
    }

    private void ResolveReferences()
    {
        if (trafficSpawner == null)
            trafficSpawner = FindFirstObjectByType<TrafficTestSpawner>();

        if (flightSchoolManager == null)
            flightSchoolManager = FindFirstObjectByType<FlightSchoolManager>();

        if (roomPurposeController == null)
            roomPurposeController = FindFirstObjectByType<RoomPurposeController>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();
    }

    private void SpawnDueSchoolCohorts()
    {
        if (studentCarPrefab == null || studentPrefab == null)
        {
            debugStatus = "Assign the student car and Flightstudent prefabs in the Inspector.";
            return;
        }

        long currentDay = gameTimeManager.CurrentDay;

        List<RoomDetectionController.DetectedRoom> rooms =
            roomPurposeController.GetAssignedFlightSchoolRooms();

        foreach (RoomDetectionController.DetectedRoom room in rooms)
        {
            if (room == null || room.cells == null || room.cells.Count == 0)
                continue;

            Vector2Int anchor = GetRoomAnchor(room);

            if (spawnedDayByRoom.TryGetValue(anchor, out long spawnedDay) &&
                spawnedDay == currentDay)
            {
                continue;
            }

            if (!flightSchoolManager.TryGetDailyStudentDemand(
                    anchor,
                    out int dailyDemand))
            {
                continue;
            }

            // A zero-demand school has still been evaluated for this day.
            spawnedDayByRoom[anchor] = currentDay;

            if (dailyDemand <= 0)
            {
                debugStatus = $"Flight School at {anchor} has no student demand today.";
                continue;
            }

            List<SeatSlot> seats = FindRoomSeats(room);

            if (seats.Count < dailyDemand)
            {
                Debug.LogWarning(
                    $"FlightSchoolStudentManager: Room at {anchor} has demand for " +
                    $"{dailyDemand} students but only {seats.Count} usable chair " +
                    "InteractionProviders were found. Spawning only the available seats.",
                    this);

                dailyDemand = seats.Count;
            }

            if (dailyDemand <= 0)
            {
                debugStatus = $"No usable chair interaction points found at {anchor}.";
                continue;
            }

            seatsByRoom[anchor] = seats;
            StartCoroutine(SpawnRoomCohort(anchor, dailyDemand, currentDay));

            debugStatus = $"Spawning {dailyDemand} student(s) for Flight School at {anchor}.";
        }
    }

    private IEnumerator SpawnRoomCohort(
        Vector2Int roomAnchor,
        int studentCount,
        long cohortDay)
    {
        for (int i = 0; i < studentCount; i++)
        {
            if (gameTimeManager == null ||
                gameTimeManager.CurrentDay != cohortDay)
            {
                yield break;
            }

            TrafficTestVehicle vehicle =
                trafficSpawner.SpawnExternallyControlledVehicle(studentCarPrefab);

            if (vehicle != null)
            {
                StudentRecord record = new StudentRecord
                {
                    roomAnchor = roomAnchor,
                    vehicle = vehicle
                };

                students.Add(record);

                vehicle.Parked += HandleVehicleParked;
                vehicle.Exited += HandleVehicleExited;
            }
            else
            {
                Debug.LogWarning(
                    $"FlightSchoolStudentManager: Could not spawn student car " +
                    $"{i + 1}/{studentCount} for room {roomAnchor}.",
                    this);
            }

            if (carSpawnInterval > 0f && i < studentCount - 1)
                yield return new WaitForSecondsRealtime(carSpawnInterval);
        }
    }

    private List<SeatSlot> FindRoomSeats(
        RoomDetectionController.DetectedRoom room)
    {
        List<SeatSlot> result = new List<SeatSlot>();
        HashSet<Vector2Int> roomCells =
            new HashSet<Vector2Int>(room.cells);

        PlacedBuilding[] placedBuildings =
            FindObjectsByType<PlacedBuilding>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        foreach (PlacedBuilding placed in placedBuildings)
        {
            if (placed == null || placed.Definition == null)
                continue;

            if ((placed.Definition.roomFurnitureRole &
                 RoomFurnitureRole.Chair) == 0)
            {
                continue;
            }

            PlacedBuilding roomBuilding = placed;

            if (roomBuilding.IsSurfacePlaced)
                roomBuilding = roomBuilding.SupportingSurface;

            if (roomBuilding == null || roomBuilding.IsEdgeBuilding ||
                !BuildingTouchesRoom(roomBuilding, roomCells))
            {
                continue;
            }

            InteractionProvider provider =
                placed.GetComponent<InteractionProvider>();

            if (provider == null)
                provider = placed.GetComponentInChildren<InteractionProvider>(true);

            if (provider == null ||
                provider.Interactions == null ||
                provider.Interactions.Count == 0)
            {
                continue;
            }

            result.Add(new SeatSlot
            {
                provider = provider,
                interaction = provider.Interactions[0]
            });
        }

        return result;
    }

    private bool BuildingTouchesRoom(
        PlacedBuilding building,
        HashSet<Vector2Int> roomCells)
    {
        for (int x = 0; x < Mathf.Max(1, building.Width); x++)
        {
            for (int z = 0; z < Mathf.Max(1, building.Depth); z++)
            {
                Vector2Int cell =
                    building.Origin + new Vector2Int(x, z);

                if (roomCells.Contains(cell))
                    return true;
            }
        }

        return false;
    }

    private void HandleVehicleParked(TrafficTestVehicle vehicle)
    {
        StudentRecord record = FindRecord(vehicle);

        if (record == null || vehicle == null)
            return;

        if (studentPrefab == null)
        {
            vehicle.RequestDeparture();
            return;
        }

        Vector3 spawnPosition =
            vehicle.ParkingPosition +
            vehicle.transform.right * carExitOffset;

        GameObject studentObject = Instantiate(
            studentPrefab,
            spawnPosition,
            vehicle.transform.rotation);

        FlightSchoolStudentAgent student =
            studentObject.GetComponent<FlightSchoolStudentAgent>();

        if (student == null)
            student = studentObject.AddComponent<FlightSchoolStudentAgent>();

        student.Initialize(vehicle);
        student.SeatReached += HandleStudentSeated;
        student.PathFailed += HandleStudentPathFailed;
        record.student = student;

        if (!TryAssignRoomSeat(record, student))
        {
            Debug.LogWarning(
                $"FlightSchoolStudentManager: No available classroom chair for " +
                $"student arriving at room {record.roomAnchor}.",
                student);

            student.BeginDeparture();
        }
    }

    private bool TryAssignRoomSeat(
        StudentRecord record,
        FlightSchoolStudentAgent student)
    {
        if (!seatsByRoom.TryGetValue(
                record.roomAnchor,
                out List<SeatSlot> seats))
        {
            return false;
        }

        foreach (SeatSlot seat in seats)
        {
            if (seat.provider == null || seat.interaction == null)
                continue;

            if (student.AssignSeat(seat.provider, seat.interaction))
                return true;
        }

        return false;
    }

    private void HandleStudentSeated(FlightSchoolStudentAgent student)
    {
        debugStatus = $"Student seated in Flight School at {FindStudentRoom(student)}.";
    }

    private void HandleStudentPathFailed(FlightSchoolStudentAgent student)
    {
        StudentRecord record = FindRecord(student);

        if (record != null && record.vehicle != null)
            record.vehicle.RequestDeparture();

        RemoveStudentSubscriptions(student);
    }

    private void HandleVehicleExited(TrafficTestVehicle vehicle)
    {
        StudentRecord record = FindRecord(vehicle);

        if (record == null)
            return;

        if (record.student != null)
        {
            RemoveStudentSubscriptions(record.student);
            Destroy(record.student.gameObject);
        }

        vehicle.Parked -= HandleVehicleParked;
        vehicle.Exited -= HandleVehicleExited;
        students.Remove(record);
    }

    private StudentRecord FindRecord(TrafficTestVehicle vehicle)
    {
        for (int i = 0; i < students.Count; i++)
        {
            if (students[i].vehicle == vehicle)
                return students[i];
        }

        return null;
    }

    private StudentRecord FindRecord(FlightSchoolStudentAgent student)
    {
        for (int i = 0; i < students.Count; i++)
        {
            if (students[i].student == student)
                return students[i];
        }

        return null;
    }

    private Vector2Int FindStudentRoom(FlightSchoolStudentAgent student)
    {
        StudentRecord record = FindRecord(student);
        return record != null ? record.roomAnchor : Vector2Int.zero;
    }

    private void RemoveStudentSubscriptions(FlightSchoolStudentAgent student)
    {
        if (student == null)
            return;

        student.SeatReached -= HandleStudentSeated;
        student.PathFailed -= HandleStudentPathFailed;
    }

    private Vector2Int GetRoomAnchor(
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

    private void OnDestroy()
    {
        foreach (StudentRecord record in students)
        {
            if (record.vehicle != null)
            {
                record.vehicle.Parked -= HandleVehicleParked;
                record.vehicle.Exited -= HandleVehicleExited;
            }

            if (record.student != null)
                RemoveStudentSubscriptions(record.student);
        }
    }
}
