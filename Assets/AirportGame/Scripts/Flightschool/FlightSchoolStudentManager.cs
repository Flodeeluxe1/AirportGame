using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates Flight School student arrivals, classroom seats, lesson hand-offs
/// and departures. Each student keeps their own SUV in an ordinary ParkingSpace.
/// </summary>
[DisallowMultipleComponent]
public class FlightSchoolStudentManager : MonoBehaviour
{
    private const long StudentReturnWaitSeconds = 30L * 60L;
    private const int SecondsPerDay = 86400;

    [Header("Prefab References")]
    [SerializeField] private GameObject studentCarPrefab;
    [SerializeField] private GameObject studentPrefab;
    [SerializeField] private GameObject instructorPrefab;

    [Header("Instructor")]
    [SerializeField, Min(1)] private int instructorSpawnLeadMinutes = 30;
    [SerializeField, Min(0.25f)] private float instructorSpawnCheckInterval = 1f;

    [Header("Classroom Seating")]
    [Tooltip("A sit interaction such as Sit_Rest. Used to make chair InteractionProviders reservable.")]
    [SerializeField] private InteractionDefinition seatInteractionDefinition;

    [Header("Scene References")]
    [SerializeField] private TrafficTestSpawner trafficSpawner;
    [SerializeField] private FlightSchoolManager flightSchoolManager;
    [SerializeField] private RoomPurposeController roomPurposeController;
    [SerializeField] private GameTimeManager gameTimeManager;
    [SerializeField] private PedestrianPathfinder pedestrianPathfinder;

    [Header("Operating Hours")]
    [SerializeField, Range(0, 23)] private int openingHour = 8;
    [SerializeField, Range(0, 23)] private int closingHour = 18;

    [Header("Arrival Traffic")]
    [Tooltip("In-game seconds between student car spawns, allowing each car to clear the road entry.")]
    [SerializeField, Min(0f)] private float carSpawnInterval = 1.5f;

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

        public bool activeLesson;
        public bool hasStartedLesson;
        public bool returningFromLesson;
        public bool waitingAfterLesson;
        public bool departing;
        public long departureReadyAtGameSeconds;
        public GameObject pendingAircraft;
    }

    private readonly Dictionary<Vector2Int, long> spawnedDayByRoom =
        new Dictionary<Vector2Int, long>();

    private readonly Dictionary<Vector2Int, List<SeatSlot>> seatsByRoom =
        new Dictionary<Vector2Int, List<SeatSlot>>();

    private readonly Dictionary<Vector2Int, GameObject> readyAircraftByRoom =
        new Dictionary<Vector2Int, GameObject>();

    private readonly List<StudentRecord> students =
        new List<StudentRecord>();

    private readonly Dictionary<Vector2Int, FlightSchoolInstructorController> instructors =
        new Dictionary<Vector2Int, FlightSchoolInstructorController>();
    private readonly HashSet<Vector2Int> cohortSpawningRooms =
        new HashSet<Vector2Int>();
    private float nextInstructorSpawnCheckTime;

    private long nextRoomCheckGameSecond;
    private bool subscribedToLessons;
    private bool subscribedToGameTime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();
        SubscribeToLessons();
        SubscribeToGameTime();

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

        UpdateInstructorSpawns(currentSeconds, openingSeconds);

        if (currentSeconds >= closingSeconds)
        {
            HandleSchoolClosing();
            debugStatus = "Flight School is closed. Remaining active lessons are being completed.";
            return;
        }

        if (currentSeconds < openingSeconds)
            return;

        long currentGameSecond = GetAbsoluteGameTimeSeconds();
        if (currentGameSecond < nextRoomCheckGameSecond)
            return;

        nextRoomCheckGameSecond = currentGameSecond + 1L;
        SpawnDueSchoolCohorts();
    }

    private void ResolveReferences()
    {
        if (trafficSpawner == null)
            trafficSpawner = FindAnyObjectByType<TrafficTestSpawner>();

        if (flightSchoolManager == null)
            flightSchoolManager = GetComponent<FlightSchoolManager>();

        if (flightSchoolManager == null)
            flightSchoolManager = FindAnyObjectByType<FlightSchoolManager>();

        if (roomPurposeController == null)
            roomPurposeController = FindAnyObjectByType<RoomPurposeController>();

        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();

        if (pedestrianPathfinder == null)
            pedestrianPathfinder = FindAnyObjectByType<PedestrianPathfinder>();
    }

    private void SubscribeToGameTime()
    {
        if (subscribedToGameTime || gameTimeManager == null)
            return;

        gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
        subscribedToGameTime = true;
    }

    private void HandleGameTimeAdvanced(long previousSecond, long currentSecond)
    {
        UpdateStudentDepartureTimers(currentSecond);
    }

    private void SubscribeToLessons()
    {
        if (subscribedToLessons || flightSchoolManager == null)
            return;

        flightSchoolManager.LessonReady += HandleLessonReady;
        flightSchoolManager.LessonStarted += HandleLessonStarted;
        flightSchoolManager.LessonCompleted += HandleLessonCompleted;
        subscribedToLessons = true;
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

            if (dailyDemand <= 0)
            {
                spawnedDayByRoom[anchor] = currentDay;
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

            spawnedDayByRoom[anchor] = currentDay;
            seatsByRoom[anchor] = seats;
            cohortSpawningRooms.Add(anchor);
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
                gameTimeManager.CurrentDay != cohortDay ||
                gameTimeManager.GetGameTimeSeconds() >= closingHour * 60 * 60)
            {
                FinishCohortSpawn(roomAnchor);
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
            {
                long nextCarSpawnGameSecond =
                    GetAbsoluteGameTimeSeconds() +
                    Mathf.CeilToInt(carSpawnInterval);

                while (gameTimeManager != null &&
                       GetAbsoluteGameTimeSeconds() < nextCarSpawnGameSecond)
                {
                    yield return null;
                }
            }
        }

        FinishCohortSpawn(roomAnchor);
    }

    private void FinishCohortSpawn(Vector2Int roomAnchor)
    {
        cohortSpawningRooms.Remove(roomAnchor);
        TryDespawnInstructorIfFinished(roomAnchor);
    }

    private List<SeatSlot> FindRoomSeats(
        RoomDetectionController.DetectedRoom room)
    {
        List<SeatSlot> result = new List<SeatSlot>();
        HashSet<Vector2Int> roomCells = new HashSet<Vector2Int>(room.cells);

        PlacedBuilding[] placedBuildings =
            FindObjectsByType<PlacedBuilding>(
                FindObjectsInactive.Exclude);

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

            // The chair's named child points are enough to configure a
            // provider at runtime when the prefab does not already have one.
            if (provider == null && seatInteractionDefinition != null)
                provider = placed.gameObject.AddComponent<InteractionProvider>();

            if (provider == null)
                continue;

            if (seatInteractionDefinition != null)
                provider.AddInteractionDefinition(seatInteractionDefinition);

            if (!provider.InteractionsEnabled)
                continue;

            InteractionDefinition seatInteraction = null;

            for (int i = 0; i < provider.Interactions.Count; i++)
            {
                InteractionDefinition definition = provider.Interactions[i];

                if (definition == null ||
                    string.IsNullOrEmpty(definition.InteractionId))
                {
                    continue;
                }

                string interactionId = definition.InteractionId;

                bool isSeatInteraction =
                    interactionId.IndexOf(
                        "sit",
                        System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    interactionId.IndexOf(
                        "seat",
                        System.StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isSeatInteraction || !provider.CanOffer(definition))
                    continue;

                seatInteraction = definition;
                break;
            }

            if (seatInteraction == null)
                continue;

            result.Add(new SeatSlot
            {
                provider = provider,
                interaction = seatInteraction
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
                Vector2Int cell = building.Origin + new Vector2Int(x, z);

                if (roomCells.Contains(cell))
                    return true;
            }
        }

        return false;
    }

    private void HandleVehicleParked(TrafficTestVehicle vehicle)
    {
        StudentRecord record = FindRecord(vehicle);

        if (vehicle == null)
            return;

        if (record == null)
        {
            Debug.LogError(
                $"FlightSchoolStudentManager: Parked event received from {vehicle.name} " +
                $"(instance {vehicle.GetEntityId()}), but no student record matches it.",
                vehicle);
            return;
        }

        if (record.student != null)
        {
            Debug.LogWarning(
                $"FlightSchoolStudentManager: {vehicle.name} parked more than once; " +
                "a student is already assigned to this car.",
                vehicle);
            return;
        }

        Debug.Log(
            $"FlightSchoolStudentManager: Handling parked student car {vehicle.name} " +
            $"for room {record.roomAnchor}. Departing={record.departing}, " +
            $"studentPrefabAssigned={studentPrefab != null}.",
            vehicle);

        if (record.departing)
        {
            Debug.LogWarning(
                $"FlightSchoolStudentManager: {vehicle.name} was already marked for " +
                "departure before its parked event. Requesting car departure.",
                vehicle);
            vehicle.RequestDeparture();
            return;
        }

        if (studentPrefab == null)
        {
            Debug.LogError(
                $"FlightSchoolStudentManager: Student prefab is not assigned; " +
                $"{vehicle.name} cannot spawn a student.",
                this);
            vehicle.RequestDeparture();
            return;
        }

        Vector3 desiredSpawnPosition =
            vehicle.ParkingPosition +
            vehicle.transform.right * carExitOffset;

        if (pedestrianPathfinder == null ||
            !pedestrianPathfinder.TryFindNearestWalkablePosition(
                desiredSpawnPosition,
                8,
                out Vector3 spawnPosition))
        {
            Debug.LogError(
                $"FlightSchoolStudentManager: Could not find a walkable pedestrian " +
                $"spawn cell near {desiredSpawnPosition} for {vehicle.name}. " +
                "The student was not spawned and the car will leave.",
                vehicle);
            vehicle.RequestDeparture();
            return;
        }

        GameObject studentObject = Instantiate(
            studentPrefab,
            spawnPosition,
            vehicle.transform.rotation);

        // Match the hobby-pilot visit lifecycle: prefab values are defaults,
        // but every newly spawned student starts with independently randomized needs.
        UtilityNeeds studentNeeds = studentObject.GetComponent<UtilityNeeds>();
        if (studentNeeds != null)
            studentNeeds.ResetForNewVisit();

        Collider studentCollider = studentObject.GetComponent<Collider>();
        if (studentCollider == null)
            studentCollider = studentObject.GetComponentInChildren<Collider>();

        if (studentCollider != null)
        {
            studentObject.transform.position +=
                Vector3.up * studentCollider.bounds.extents.y;
        }
        else
        {
            Debug.LogWarning(
                $"FlightSchoolStudentManager: {studentObject.name} has no Collider; " +
                "its spawn height cannot be adjusted.",
                studentObject);
        }

        FlightSchoolStudentAgent student =
            studentObject.GetComponent<FlightSchoolStudentAgent>();

        if (student == null)
            student = studentObject.AddComponent<FlightSchoolStudentAgent>();

        student.Initialize(vehicle, record.roomAnchor);
        student.SeatReached += HandleStudentSeated;
        student.AircraftReached += HandleStudentReachedAircraft;
        student.PathFailed += HandleStudentPathFailed;
        record.student = student;

        if (instructors.TryGetValue(record.roomAnchor, out FlightSchoolInstructorController instructor) &&
            instructor != null)
        {
            instructor.NotifyStudentArrived(student);
        }

        Debug.Log(
            $"FlightSchoolStudentManager: Spawned {studentObject.name} for {vehicle.name}; " +
            $"trying to reserve one of {GetSeatCount(record.roomAnchor)} classroom seats.",
            studentObject);

        if (!TryAssignRoomSeat(record, student))
        {
            Debug.LogError(
                $"FlightSchoolStudentManager: {studentObject.name} could not reserve " +
                $"or path to a classroom chair in room {record.roomAnchor}. " +
                "See the FlightSchoolStudentAgent seat diagnostics immediately above.",
                student);

            BeginStudentDeparture(record);
            return;
        }

        TryDispatchWaitingStudent(record.roomAnchor);
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

            // The room's seat list is a snapshot taken before the cohort arrives.
            // Another student or pilot may have reserved a chair since then.
            // Treat that as normal contention and try the next available chair
            // instead of asking AssignSeat to reserve a known-unavailable slot.
            if (!seat.provider.CanOffer(seat.interaction))
                continue;

            if (student.AssignSeat(seat.provider, seat.interaction))
                return true;

            if (record.departing)
                return false;
        }

        return false;
    }

    private void HandleStudentSeated(FlightSchoolStudentAgent student)
    {
        StudentRecord record = FindRecord(student);

        if (record == null)
            return;

        if (record.returningFromLesson)
        {
            record.returningFromLesson = false;
            record.waitingAfterLesson = true;
            record.departureReadyAtGameSeconds =
                GetAbsoluteGameTimeSeconds() + StudentReturnWaitSeconds;

            debugStatus =
                $"Student returned to Flight School at {record.roomAnchor}; " +
                "30-minute classroom wait started.";

            return;
        }

        if (readyAircraftByRoom.ContainsKey(record.roomAnchor))
            TryDispatchWaitingStudent(record.roomAnchor);
    }

    private void HandleLessonReady(
        Vector2Int roomAnchor,
        GameObject aircraft)
    {
        if (aircraft == null)
            return;

        readyAircraftByRoom[roomAnchor] = aircraft;
        TryDispatchWaitingStudent(roomAnchor);
    }

    private void TryDispatchWaitingStudent(Vector2Int roomAnchor)
    {
        if (!readyAircraftByRoom.TryGetValue(
                roomAnchor,
                out GameObject aircraft) ||
            aircraft == null)
        {
            return;
        }

        StudentRecord selected = null;

        foreach (StudentRecord record in students)
        {
            if (record.roomAnchor != roomAnchor ||
                record.student == null ||
                record.hasStartedLesson ||
                record.activeLesson ||
                record.returningFromLesson ||
                record.waitingAfterLesson ||
                record.departing ||
                !record.student.IsSeated)
            {
                continue;
            }

            selected = record;
            break;
        }

        if (selected == null)
            return;

        selected.activeLesson = true;
        selected.pendingAircraft = aircraft;

        if (!selected.student.BeginFlightLesson(aircraft))
        {
            selected.activeLesson = false;
            selected.pendingAircraft = null;
            flightSchoolManager.CancelStudentReadyForLesson(roomAnchor);
            BeginStudentDeparture(selected);
        }
    }

    private void HandleStudentReachedAircraft(
        FlightSchoolStudentAgent student)
    {
        StudentRecord record = FindRecord(student);

        if (record == null ||
            !record.activeLesson ||
            record.pendingAircraft == null)
        {
            return;
        }

        if (!flightSchoolManager.ConfirmStudentReadyForLesson(
                record.roomAnchor))
        {
            record.activeLesson = false;
            record.pendingAircraft = null;
            readyAircraftByRoom.Remove(record.roomAnchor);
            flightSchoolManager.CancelStudentReadyForLesson(record.roomAnchor);

            if (!student.ReturnToSeat())
                BeginStudentDeparture(record);

            return;
        }

        readyAircraftByRoom.Remove(record.roomAnchor);
    }

    private void HandleLessonStarted(
        Vector2Int roomAnchor,
        GameObject aircraft)
    {
        StudentRecord record = FindActiveStudent(roomAnchor, aircraft);

        if (record == null)
            return;

        record.hasStartedLesson = true;
        record.activeLesson = true;
        if (record.student != null)
            record.student.SetStudentVisible(false);

        debugStatus = $"Student started a flight lesson at {roomAnchor}.";
    }

    private void HandleLessonCompleted(
        Vector2Int roomAnchor,
        GameObject aircraft)
    {
        StudentRecord record = FindActiveStudent(roomAnchor, aircraft);

        if (record == null || record.student == null)
            return;

        record.activeLesson = false;
        record.returningFromLesson = true;
        record.pendingAircraft = null;
        record.student.SetStudentVisible(true);

        if (!record.student.ReturnToSeat())
            BeginStudentDeparture(record);

        debugStatus = $"Student returning to the classroom at {roomAnchor}.";
    }

    private StudentRecord FindActiveStudent(
        Vector2Int roomAnchor,
        GameObject aircraft)
    {
        foreach (StudentRecord record in students)
        {
            if (record.roomAnchor == roomAnchor &&
                record.activeLesson &&
                record.pendingAircraft == aircraft)
            {
                return record;
            }
        }

        return null;
    }

    private int GetSeatCount(Vector2Int roomAnchor)
    {
        return seatsByRoom.TryGetValue(roomAnchor, out List<SeatSlot> seats)
            ? seats.Count
            : 0;
    }

    private void HandleStudentPathFailed(FlightSchoolStudentAgent student)
    {
        StudentRecord record = FindRecord(student);

        if (record == null)
            return;

        if (record.departing)
        {
            Debug.LogError(
                $"FlightSchoolStudentManager: {student.name} failed its departure walk " +
                $"to car {record.vehicle?.name}; requesting the car to leave anyway.",
                student);

            if (record.vehicle != null)
                record.vehicle.RequestDeparture();

            Destroy(student.gameObject);
            return;
        }

        Debug.LogError(
            $"FlightSchoolStudentManager: {student.name} path failed. " +
            $"ActiveLesson={record.activeLesson}, HasStartedLesson={record.hasStartedLesson}, " +
            $"ReturningFromLesson={record.returningFromLesson}, WaitingAfterLesson={record.waitingAfterLesson}. " +
            "The student will be sent back to its car.",
            student);

        if (record.activeLesson && !record.hasStartedLesson)
        {
            flightSchoolManager.CancelStudentReadyForLesson(record.roomAnchor);
            readyAircraftByRoom.Remove(record.roomAnchor);
        }

        BeginStudentDeparture(record);
    }

    private void HandleSchoolClosing()
    {
        foreach (Vector2Int roomAnchor in readyAircraftByRoom.Keys)
            flightSchoolManager.CancelStudentReadyForLesson(roomAnchor);

        readyAircraftByRoom.Clear();

        for (int i = students.Count - 1; i >= 0; i--)
        {
            StudentRecord record = students[i];

            if (record == null ||
                record.departing ||
                record.hasStartedLesson ||
                record.returningFromLesson ||
                record.waitingAfterLesson)
            {
                continue;
            }

            BeginStudentDeparture(record);
        }
    }

    private void UpdateStudentDepartureTimers(long currentGameSeconds)
    {

        for (int i = 0; i < students.Count; i++)
        {
            StudentRecord record = students[i];

            if (record == null ||
                !record.waitingAfterLesson ||
                record.departing ||
                currentGameSeconds < record.departureReadyAtGameSeconds)
            {
                continue;
            }

            BeginStudentDeparture(record);
        }
    }

    private void BeginStudentDeparture(StudentRecord record)
    {
        if (record == null || record.departing)
            return;

        record.departing = true;
        record.waitingAfterLesson = false;
        record.returningFromLesson = false;

        if (record.student != null)
        {
            if (record.activeLesson && !record.hasStartedLesson)
                flightSchoolManager.CancelStudentReadyForLesson(record.roomAnchor);

            if (!record.student.BeginDeparture())
            {
                Debug.LogWarning(
                    $"FlightSchoolStudentManager: {record.student.name} cannot walk " +
                    "to its car. Requesting vehicle departure without the student.",
                    record.student);

                FlightSchoolStudentAgent departingStudent = record.student;
                record.student = null;
                RemoveStudentSubscriptions(departingStudent);
                Destroy(departingStudent.gameObject);

                if (record.vehicle != null)
                    record.vehicle.RequestDeparture();
            }
        }
        else if (record.vehicle != null)
        {
            record.vehicle.RequestDeparture();
        }
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
        TryDespawnInstructorIfFinished(record.roomAnchor);
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

    private void UpdateInstructorSpawns(int currentSeconds, int openingSeconds)
    {
        if (instructorPrefab == null ||
            currentSeconds < openingSeconds - instructorSpawnLeadMinutes * 60 ||
            currentSeconds >= openingSeconds ||
            Time.unscaledTime < nextInstructorSpawnCheckTime)
        {
            return;
        }

        nextInstructorSpawnCheckTime =
            Time.unscaledTime + Mathf.Max(0.25f, instructorSpawnCheckInterval);

        GridManager gridManager = FindAnyObjectByType<GridManager>();
        if (gridManager == null || pedestrianPathfinder == null)
            return;

        List<RoomDetectionController.DetectedRoom> rooms =
            roomPurposeController.GetAssignedFlightSchoolRooms();

        foreach (RoomDetectionController.DetectedRoom room in rooms)
        {
            if (room == null || room.cells == null || room.cells.Count == 0)
                continue;

            Vector2Int roomAnchor = GetRoomAnchor(room);
            if (instructors.TryGetValue(roomAnchor, out FlightSchoolInstructorController existing) &&
                existing != null)
            {
                continue;
            }

            if (!flightSchoolManager.TryGetDailyStudentDemand(roomAnchor, out int demand) ||
                demand <= 0)
            {
                continue;
            }

            if (!TryGetRoomWallBoard(
                    room,
                    gridManager,
                    out PlacedBuilding wallBoard,
                    out Vector3 boardCenter,
                    out Vector3 inwardDirection,
                    out Vector3 boardAxis))
            {
                continue;
            }

            float halfPatrolLength = Mathf.Max(
                0.25f,
                wallBoard.EdgeLength * gridManager.CellSize * 0.5f - 0.4f);

            Vector3 patrolCenter = boardCenter + inwardDirection * 1.25f;
            Vector3 patrolStart = patrolCenter - boardAxis * halfPatrolLength;
            Vector3 patrolEnd = patrolCenter + boardAxis * halfPatrolLength;

            if (!pedestrianPathfinder.TryFindNearestWalkablePosition(
                    patrolCenter,
                    3,
                    out Vector3 spawnPosition))
            {
                continue;
            }

            GameObject instructorObject = Instantiate(
                instructorPrefab,
                spawnPosition,
                Quaternion.LookRotation(-inwardDirection, Vector3.up));

            if (instructorObject == null)
                continue;

            Collider instructorCollider = instructorObject.GetComponent<Collider>();
            if (instructorCollider == null)
                instructorCollider = instructorObject.GetComponentInChildren<Collider>();

            if (instructorCollider != null)
                instructorObject.transform.position +=
                    Vector3.up * instructorCollider.bounds.extents.y;

            FlightSchoolInstructorController controller =
                instructorObject.GetComponent<FlightSchoolInstructorController>();

            if (controller == null)
                controller = instructorObject.AddComponent<FlightSchoolInstructorController>();

            controller.Initialize(
                roomAnchor,
                boardCenter,
                patrolStart,
                patrolEnd,
                this,
                gameTimeManager);

            instructors[roomAnchor] = controller;
        }
    }

    private bool TryGetRoomWallBoard(
        RoomDetectionController.DetectedRoom room,
        GridManager gridManager,
        out PlacedBuilding selectedBoard,
        out Vector3 boardCenter,
        out Vector3 inwardDirection,
        out Vector3 boardAxis)
    {
        selectedBoard = null;
        boardCenter = Vector3.zero;
        inwardDirection = Vector3.zero;
        boardAxis = Vector3.right;

        HashSet<Vector2Int> roomCells = new HashSet<Vector2Int>(room.cells);

        foreach (PlacedBuilding placed in PlacedBuilding.ActiveBuildings)
        {
            if (placed == null ||
                placed.Definition == null ||
                !placed.Definition.isWallMounted ||
                (placed.Definition.roomFurnitureRole & RoomFurnitureRole.WallBoard) == 0)
            {
                continue;
            }

            bool horizontal = placed.EdgeDirection == EdgeDirection.Horizontal;
            int edgeLength = Mathf.Max(1, placed.EdgeLength);

            for (int i = 0; i < edgeLength; i++)
            {
                Vector2Int edgeCell = placed.Origin +
                    (horizontal ? Vector2Int.right : Vector2Int.up) * i;

                bool touchesRoom;
                if (horizontal)
                {
                    if (roomCells.Contains(edgeCell))
                    {
                        inwardDirection = Vector3.forward;
                        touchesRoom = true;
                    }
                    else if (roomCells.Contains(edgeCell + Vector2Int.down))
                    {
                        inwardDirection = Vector3.back;
                        touchesRoom = true;
                    }
                    else
                    {
                        touchesRoom = false;
                    }
                }
                else
                {
                    if (roomCells.Contains(edgeCell))
                    {
                        inwardDirection = Vector3.right;
                        touchesRoom = true;
                    }
                    else if (roomCells.Contains(edgeCell + Vector2Int.left))
                    {
                        inwardDirection = Vector3.left;
                        touchesRoom = true;
                    }
                    else
                    {
                        touchesRoom = false;
                    }
                }

                if (!touchesRoom)
                    continue;

                boardAxis = horizontal ? Vector3.right : Vector3.forward;
                boardCenter = gridManager.GridToWorld(placed.Origin) +
                    boardAxis * (edgeLength * gridManager.CellSize * 0.5f);
                boardCenter.y = placed.transform.position.y;
                selectedBoard = placed;
                return true;
            }
        }

        return false;
    }

    private void TryDespawnInstructorIfFinished(Vector2Int roomAnchor)
    {
        if (cohortSpawningRooms.Contains(roomAnchor))
            return;

        for (int i = 0; i < students.Count; i++)
        {
            if (students[i] != null && students[i].roomAnchor == roomAnchor)
                return;
        }

        if (instructors.TryGetValue(roomAnchor, out FlightSchoolInstructorController instructor))
        {
            instructors.Remove(roomAnchor);
            if (instructor != null)
                Destroy(instructor.gameObject);
        }
    }

    public void CollectSeatedStudents(
        Vector2Int roomAnchor,
        List<FlightSchoolStudentAgent> results)
    {
        if (results == null)
            return;

        results.Clear();
        for (int i = 0; i < students.Count; i++)
        {
            StudentRecord record = students[i];
            if (record != null &&
                record.roomAnchor == roomAnchor &&
                record.student != null &&
                record.student.IsSeated)
            {
                results.Add(record.student);
            }
        }
    }

    private long GetAbsoluteGameTimeSeconds()
    {
        if (gameTimeManager == null)
            return 0;

        return
            (gameTimeManager.CurrentDay - 1) * (long)SecondsPerDay +
            gameTimeManager.GetGameTimeSeconds();
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

    private void RemoveStudentSubscriptions(FlightSchoolStudentAgent student)
    {
        if (student == null)
            return;

        student.SeatReached -= HandleStudentSeated;
        student.AircraftReached -= HandleStudentReachedAircraft;
        student.PathFailed -= HandleStudentPathFailed;
    }

    private void OnDestroy()
    {
        if (subscribedToGameTime && gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
        subscribedToGameTime = false;

        if (subscribedToLessons && flightSchoolManager != null)
        {
            flightSchoolManager.LessonReady -= HandleLessonReady;
            flightSchoolManager.LessonStarted -= HandleLessonStarted;
            flightSchoolManager.LessonCompleted -= HandleLessonCompleted;
        }

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
