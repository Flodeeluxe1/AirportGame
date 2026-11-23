
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-level coordinator for all player-assigned Flight School rooms.
/// Each room has its own daily student demand, based on facility quality
/// and capped by the number of student seats in that room.
///
/// Training operates from 08:00 up to (but not including) 18:00.
/// Lessons already underway are allowed to finish after closing.
///
/// When an aircraft first spawns, it waits for the configured preparation
/// period before its first lesson starts. After each completed lesson,
/// it waits for the same period before the next lesson starts.
/// </summary>
public class FlightSchoolManager : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;

    private const int SecondsPerDay = 86400;
    private const int OpeningTimeSeconds = 8 * 60 * 60;
    private const int ClosingTimeSeconds = 18 * 60 * 60;

    [Header("Room Detection")]
    [SerializeField] private RoomPurposeController roomPurposeController;
    [SerializeField, Min(0.1f)] private float roomRefreshInterval = 0.5f;
    [SerializeField, Min(0.5f)] private float parkingSpotRefreshInterval = 2f;

    [Header("Training Aircraft")]
    [SerializeField] private GameObject trainingAircraftPrefab;
    [SerializeField, Min(0.1f)] private float spawnRetryInterval = 1f;

    [Header("Lesson Reward")]
    [SerializeField, Min(1)] private int lessonReward = 300;

    [Header("Lesson Preparation and Turnaround")]
    [Tooltip("In-game minutes the aircraft waits after spawning and between lessons.")]
    [SerializeField, Min(0)] private int turnaroundMinutes = 30;

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
        public TrainingFlightController trainingController;
        public double nextSpawnAttemptTime;

        public bool rewardPaid;
        public bool lessonRestartPending;

        // Used for both the initial preparation period and later turnarounds.
        public bool turnaroundPending;
        public long turnaroundReadyAtGameSeconds;

        public long demandDay = -1;
        public int studentsStartedToday;
        public bool lessonReadyNotified;
        public bool studentReadyToStart;
        public bool lessonCompletionNotified;
    }

    private readonly Dictionary<Vector2Int, SchoolOperation> operations =
        new Dictionary<Vector2Int, SchoolOperation>();

    private AirportEconomyManager economyManager;
    private FlightSchoolAssessmentController assessmentController;
    private GameTimeManager gameTimeManager;

    private long roomRefreshEventId;
    private long parkingSpotRefreshEventId;
    private HobbyAircraftParkingSpot[] cachedParkingSpots =
        Array.Empty<HobbyAircraftParkingSpot>();
    private static FlightSchoolManager activeManager;

    public GameObject TrainingAircraftPrefab => trainingAircraftPrefab;

    public event Action<Vector2Int, GameObject> LessonReady;
    public event Action<Vector2Int, GameObject> LessonStarted;
    public event Action<Vector2Int, GameObject> LessonCompleted;

    private void Awake()
    {
        if (activeManager != null && activeManager != this)
        {
            LogError(
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

        EnsurePeriodicRefreshEventsScheduled();

        foreach (SchoolOperation operation in operations.Values)
        {
            EnsureDailyDemandIsCurrent(operation);

            // An active aircraft is managed here even during its initial
            // preparation period, when its lesson has not started yet.
            if (operation.aircraft != null)
            {
                CheckLessonCompletion(operation);
                continue;
            }

            if (!operation.assigned ||
                !operation.valid ||
                operation.rewardPaid)
            {
                continue;
            }

            TrySpawnTrainingAircraft(operation);
        }
    }

    private void OnDisable()
    {
        CancelPeriodicRefreshEvents();

        if (activeManager == this)
            activeManager = null;
    }

    private void EnsurePeriodicRefreshEventsScheduled()
    {
        if (gameTimeManager == null ||
            gameTimeManager.EventScheduler == null)
            return;

        if (roomRefreshEventId == 0)
            roomRefreshEventId = gameTimeManager.EventScheduler.ScheduleAfter(
                0.0, HandleRoomRefreshScheduled);

        if (parkingSpotRefreshEventId == 0)
            parkingSpotRefreshEventId = gameTimeManager.EventScheduler.ScheduleAfter(
                0.0, HandleParkingSpotRefreshScheduled);
    }

    private void HandleRoomRefreshScheduled()
    {
        roomRefreshEventId = 0;
        if (!isActiveAndEnabled)
            return;

        RefreshSchoolRooms();
        if (gameTimeManager != null && gameTimeManager.EventScheduler != null)
            roomRefreshEventId = gameTimeManager.EventScheduler.ScheduleAfter(
                Mathf.Max(0.1f, roomRefreshInterval), HandleRoomRefreshScheduled);
    }

    private void HandleParkingSpotRefreshScheduled()
    {
        parkingSpotRefreshEventId = 0;
        if (!isActiveAndEnabled)
            return;

        RefreshParkingSpotCache();
        if (gameTimeManager != null && gameTimeManager.EventScheduler != null)
            parkingSpotRefreshEventId = gameTimeManager.EventScheduler.ScheduleAfter(
                Mathf.Max(0.5f, parkingSpotRefreshInterval), HandleParkingSpotRefreshScheduled);
    }

    private void CancelPeriodicRefreshEvents()
    {
        if (gameTimeManager != null && gameTimeManager.EventScheduler != null)
        {
            if (roomRefreshEventId != 0)
                gameTimeManager.EventScheduler.Cancel(roomRefreshEventId);
            if (parkingSpotRefreshEventId != 0)
                gameTimeManager.EventScheduler.Cancel(parkingSpotRefreshEventId);
        }

        roomRefreshEventId = 0;
        parkingSpotRefreshEventId = 0;
    }

    private void ResolveReferences()
    {
        if (roomPurposeController == null)
            roomPurposeController =
                FindAnyObjectByType<RoomPurposeController>();

        if (economyManager == null)
            economyManager =
                FindAnyObjectByType<AirportEconomyManager>();

        if (assessmentController == null)
            assessmentController =
                FindAnyObjectByType<FlightSchoolAssessmentController>();

        if (gameTimeManager == null)
            gameTimeManager =
                FindAnyObjectByType<GameTimeManager>();
    }

    private long GetAbsoluteGameTimeSeconds()
    {
        if (gameTimeManager == null)
            return 0;

        return
            (gameTimeManager.CurrentDay - 1) * (long)SecondsPerDay +
            gameTimeManager.GetGameTimeSeconds();
    }

    private void EnsureDailyDemandIsCurrent(SchoolOperation operation)
    {
        if (gameTimeManager == null)
            return;

        long currentDay = gameTimeManager.CurrentDay;

        if (operation.demandDay == currentDay)
            return;

        operation.demandDay = currentDay;
        operation.studentsStartedToday = 0;
    }

    private bool IsWithinOperatingHours()
    {
        if (gameTimeManager == null)
            return false;

        int currentSeconds = gameTimeManager.GetGameTimeSeconds();

        return currentSeconds >= OpeningTimeSeconds &&
               currentSeconds < ClosingTimeSeconds;
    }

    private bool TryGetDailyDemand(
        SchoolOperation operation,
        out int dailyDemand,
        out string reason)
    {
        dailyDemand = 0;
        reason = string.Empty;

        if (assessmentController == null)
        {
            reason =
                "FlightSchoolAssessmentController was not found in the scene.";
            return false;
        }

        if (!assessmentController.TryGetAssessment(
                operation.anchor,
                out FlightSchoolRoomAssessment assessment))
        {
            reason =
                "Waiting for the Flight School facility assessment.";
            return false;
        }

        // Room quality contributes up to 14 students (70 quality points).
        // Each designated Flight School parking spot adds 3 more students.
        // The final demand is always limited by the room's student seating.
        int qualityDemand =
            Mathf.RoundToInt(assessment.facilityQualityScore / 5f);

        int parkingDemand =
            Mathf.Max(0, assessment.designatedTrainingParkingSpotCount) * 3;

        int seatCount = Mathf.Max(0, assessment.studentSeatCount);
        dailyDemand = Mathf.Min(qualityDemand + parkingDemand, seatCount);

        return true;
    }

    private bool CanStartNewLesson(
        SchoolOperation operation,
        out string reason)
    {
        reason = string.Empty;

        if (gameTimeManager == null)
        {
            reason = "GameTimeManager was not found in the scene.";
            return false;
        }

        if (!IsWithinOperatingHours())
        {
            reason =
                "Flight School is closed. Operating hours are 08:00–18:00.";
            return false;
        }

        if (!TryGetDailyDemand(
                operation,
                out int dailyDemand,
                out string demandReason))
        {
            reason = demandReason;
            return false;
        }

        if (dailyDemand <= 0)
        {
            reason =
                "Daily student demand is 0. Improve facility quality and check student seats.";
            return false;
        }

        if (operation.studentsStartedToday >= dailyDemand)
        {
            reason =
                $"Daily student demand reached ({operation.studentsStartedToday}/{dailyDemand}). Waiting for the next day.";
            return false;
        }

        return true;
    }

    private void RefreshSchoolRooms()
    {
        if (roomPurposeController == null)
        {
            debugStatus =
                "RoomPurposeController was not found in the scene.";
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
            if (room == null ||
                room.cells == null ||
                room.cells.Count == 0)
            {
                continue;
            }

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

            operation.valid =
                roomPurposeController.ValidateFlightSchool(
                    room,
                    out string reason);

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

    private void RefreshParkingSpotCache()
    {
        cachedParkingSpots = FindObjectsByType<HobbyAircraftParkingSpot>();
    }

    private void TrySpawnTrainingAircraft(SchoolOperation operation)
    {
        if (!CanStartNewLesson(operation, out string reason))
        {
            operation.validationMessage = reason;
            return;
        }

        if (trainingAircraftPrefab == null)
        {
            operation.validationMessage =
                "Assign the training aircraft prefab in the Inspector.";
            return;
        }

        if (GameTimeManager.SimulationTime < operation.nextSpawnAttemptTime)
            return;

        operation.nextSpawnAttemptTime =
            GameTimeManager.SimulationTime + Mathf.Max(0.1f, spawnRetryInterval);

        foreach (HobbyAircraftParkingSpot spot in cachedParkingSpots)
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

            AircraftAirportFees airportFees =
                aircraft.GetComponent<AircraftAirportFees>();
            if (airportFees != null)
                airportFees.SetFeeCategory(AirportFeeCategory.FlightSchool);

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
                LogError(
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

            // Do not start the lesson yet. First allow time for the student
            // to board and for the aircraft to be prepared.
            operation.aircraft = aircraft;
            operation.parkingSpot = spot;
            operation.trainingController = trainingController;
            operation.rewardPaid = false;
            operation.lessonRestartPending = false;

            BeginTurnaround(operation);

            operation.validationMessage =
                $"Aircraft spawned at '{spot.name}'. Initial preparation: " +
                $"{turnaroundMinutes} in-game minute(s) remaining.";

            Log(
                $"{name}: Training aircraft spawned for Flight School room " +
                $"at {operation.anchor}, parking at '{spot.name}'. " +
                $"Waiting {turnaroundMinutes} in-game minutes before the first lesson.",
                this);

            return;
        }

        operation.validationMessage =
            "Waiting for an unoccupied parking spot designated for flight school.";
    }

    private void BeginTurnaround(SchoolOperation operation)
    {
        if (operation.turnaroundPending || gameTimeManager == null)
            return;

        long turnaroundSeconds =
            Mathf.Max(0, turnaroundMinutes) * 60L;

        operation.turnaroundPending = true;
        operation.turnaroundReadyAtGameSeconds =
            GetAbsoluteGameTimeSeconds() + turnaroundSeconds;

        Log(
            $"{name}: Flight School room at {operation.anchor} began its " +
            $"{turnaroundMinutes}-minute preparation/turnaround period.",
            this);
    }

    private bool IsTurnaroundComplete(SchoolOperation operation)
    {
        if (!operation.turnaroundPending || gameTimeManager == null)
            return false;

        return GetAbsoluteGameTimeSeconds() >=
               operation.turnaroundReadyAtGameSeconds;
    }

    private int GetTurnaroundMinutesRemaining(SchoolOperation operation)
    {
        if (!operation.turnaroundPending || gameTimeManager == null)
            return turnaroundMinutes;

        long remainingSeconds =
            operation.turnaroundReadyAtGameSeconds -
            GetAbsoluteGameTimeSeconds();

        if (remainingSeconds <= 0)
            return 0;

        return (int)((remainingSeconds + 59L) / 60L);
    }

    private void CheckLessonCompletion(SchoolOperation operation)
    {
        if (operation.aircraft == null)
            return;

        TrainingFlightController trainingController = operation.trainingController;

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

        // Initial preparation: the aircraft exists, but its first lesson
        // has not started yet. Wait for the game-clock timer to expire.
        if (!trainingController.IsLessonComplete &&
            !operation.lessonRestartPending &&
            !operation.rewardPaid)
        {
            if (!IsTurnaroundComplete(operation))
            {
                operation.validationMessage =
                    $"Aircraft parked for initial student boarding and preparation. " +
                    $"Remaining: {GetTurnaroundMinutesRemaining(operation)} in-game minute(s).";
                return;
            }

            if (!operation.assigned || !operation.valid)
            {
                operation.validationMessage =
                    "Initial preparation complete. Waiting for the Flight School room to be valid again.";
                return;
            }

            if (!CanStartNewLesson(operation, out string initialReason))
            {
                operation.validationMessage =
                    "Initial preparation complete. " + initialReason;
                return;
            }

            if (!operation.lessonReadyNotified)
            {
                operation.lessonReadyNotified = true;
                LessonReady?.Invoke(operation.anchor, operation.aircraft);
                operation.validationMessage =
                    "Aircraft ready. Waiting for a student to reach the training aircraft.";
                return;
            }

            if (!operation.studentReadyToStart)
                return;

            if (!trainingController.StartTrainingLesson())
            {
                operation.validationMessage =
                    "Student is ready. Retrying the first lesson start.";
                return;
            }

            operation.studentsStartedToday++;
            operation.turnaroundPending = false;
            operation.turnaroundReadyAtGameSeconds = 0;
            operation.lessonReadyNotified = false;
            operation.studentReadyToStart = false;
            operation.lessonCompletionNotified = false;
            LessonStarted?.Invoke(operation.anchor, operation.aircraft);

            operation.validationMessage =
                $"First lesson started after preparation. Daily students: " +
                $"{operation.studentsStartedToday}. Same aircraft and parking retained.";

            Log(
                $"{name}: Started the first training lesson for Flight School " +
                $"room at {operation.anchor} after its {turnaroundMinutes}-minute " +
                $"initial preparation. Daily students started: " +
                $"{operation.studentsStartedToday}.",
                this);

            return;
        }

        if (!trainingController.IsLessonComplete &&
            !operation.lessonRestartPending)
        {
            return;
        }

        if (trainingController.IsLessonComplete &&
            !operation.lessonCompletionNotified)
        {
            operation.lessonCompletionNotified = true;
            LessonCompleted?.Invoke(operation.anchor, operation.aircraft);
        }

        // Start the turnaround clock when a completed lesson is detected.
        if (trainingController.IsLessonComplete &&
            !operation.turnaroundPending)
        {
            BeginTurnaround(operation);
        }

        // Pay once for each completed lesson. Payment failures are retried.
        if (trainingController.IsLessonComplete && !operation.rewardPaid)
        {
            if (economyManager == null)
                economyManager =
                    FindAnyObjectByType<AirportEconomyManager>();

            if (economyManager == null)
            {
                operation.validationMessage =
                    "Lesson complete, but AirportEconomyManager was not found.";
                return;
            }

            if (!economyManager.AddRevenue(
                    lessonReward,
                    "Flight school lesson completed",
                    AirportFinancialCategory.FlightSchool))
            {
                operation.validationMessage =
                    "Lesson complete, but reward payment failed. Will retry.";
                return;
            }

            operation.rewardPaid = true;

            Log(
                $"{name}: Flight School room at {operation.anchor} paid " +
                lessonReward + " for the completed lesson.",
                this);
        }

        if (!operation.rewardPaid && !operation.lessonRestartPending)
            return;

        if (!operation.assigned || !operation.valid)
        {
            operation.validationMessage =
                "Lesson reward paid. Aircraft remains parked until the Flight School room is valid again.";
            return;
        }

        if (!operation.turnaroundPending)
        {
            operation.validationMessage =
                "Lesson reward paid. Waiting for the game clock before starting turnaround.";
            return;
        }

        if (!IsTurnaroundComplete(operation))
        {
            int remainingMinutes =
                GetTurnaroundMinutesRemaining(operation);

            operation.validationMessage =
                $"Lesson reward paid. Aircraft is parked for student transfer and preparation. " +
                $"Turnaround remaining: {remainingMinutes} in-game minute(s).";

            return;
        }

        if (!CanStartNewLesson(operation, out string reason))
        {
            operation.validationMessage =
                "Lesson reward paid. Turnaround complete. " + reason;
            return;
        }

        if (!operation.lessonRestartPending)
        {
            if (!trainingController.ResetForNextLesson())
            {
                operation.validationMessage =
                    "Turnaround complete. Waiting to reset the parked aircraft for the next lesson.";
                return;
            }

            operation.lessonRestartPending = true;
        }

        if (!operation.lessonReadyNotified)
        {
            operation.lessonReadyNotified = true;
            LessonReady?.Invoke(operation.anchor, operation.aircraft);
            operation.validationMessage =
                "Aircraft ready. Waiting for the next student to reach the training aircraft.";
            return;
        }

        if (!operation.studentReadyToStart)
            return;

        // Retry a failed lesson start without resetting again or paying twice.
        if (!trainingController.StartTrainingLesson())
        {
            operation.validationMessage =
                "Student is ready. Retrying the next lesson start.";
            return;
        }

        // Consume demand only when the next lesson successfully starts.
        operation.studentsStartedToday++;

        operation.rewardPaid = false;
        operation.lessonRestartPending = false;
        operation.turnaroundPending = false;
        operation.turnaroundReadyAtGameSeconds = 0;
        operation.lessonReadyNotified = false;
        operation.studentReadyToStart = false;
        operation.lessonCompletionNotified = false;
        LessonStarted?.Invoke(operation.anchor, operation.aircraft);

        operation.validationMessage =
            $"Next lesson started after turnaround. Daily students: " +
            $"{operation.studentsStartedToday}. Same aircraft and parking retained.";

        Log(
            $"{name}: Started the next lesson for Flight School room " +
            $"{operation.anchor} after its {turnaroundMinutes}-minute turnaround. " +
            $"Daily students started: {operation.studentsStartedToday}.",
            this);
    }

    /// <summary>
    /// Returns the current daily student demand for a detected Flight School room.
    /// The demand calculation remains owned by this manager so student spawning
    /// uses exactly the same quota as lesson scheduling.
    /// </summary>
    public bool TryGetDailyStudentDemand(
        Vector2Int roomAnchor,
        out int dailyDemand)
    {
        dailyDemand = 0;

        RefreshSchoolRooms();

        if (!operations.TryGetValue(
                roomAnchor,
                out SchoolOperation operation) ||
            !operation.assigned ||
            !operation.valid)
        {
            return false;
        }

        EnsureDailyDemandIsCurrent(operation);

        return TryGetDailyDemand(
            operation,
            out dailyDemand,
            out _);
    }

    /// <summary>
    /// Called by the student coordinator after the assigned student reaches
    /// the training aircraft and is ready for the lesson to begin.
    /// </summary>
    public bool ConfirmStudentReadyForLesson(Vector2Int roomAnchor)
    {
        if (!operations.TryGetValue(
                roomAnchor,
                out SchoolOperation operation) ||
            !operation.lessonReadyNotified ||
            operation.aircraft == null ||
            !CanStartNewLesson(operation, out _))
        {
            return false;
        }

        operation.studentReadyToStart = true;
        return true;
    }

    /// <summary>
    /// Clears a pending student hand-off when the assigned pedestrian cannot
    /// reach the aircraft or the school closes before the lesson begins.
    /// </summary>
    public void CancelStudentReadyForLesson(Vector2Int roomAnchor)
    {
        if (!operations.TryGetValue(
                roomAnchor,
                out SchoolOperation operation))
        {
            return;
        }

        operation.studentReadyToStart = false;
        operation.lessonReadyNotified = false;
    }

    private void OnDestroy()
    {
        if (activeManager == this)
            activeManager = null;

        foreach (SchoolOperation operation in operations.Values)
        {
            if (operation.aircraft == null)
                continue;

            if (operation.parkingSpot != null)
                operation.parkingSpot.Release(operation.aircraft);

            Destroy(operation.aircraft);
            operation.aircraft = null;
            operation.trainingController = null;
        }
    }

    private void Log(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.Log(message, context); else Debug.Log(message);
    }
    private void LogWarning(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message);
    }
    private void LogError(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogError(message, context); else Debug.LogError(message);
    }

}
