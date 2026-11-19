using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight visual-only behaviour for one Flight School instructor.
/// Uses shared pedestrian movement and game time; it never changes student needs.
/// </summary>
[DisallowMultipleComponent]
public class FlightSchoolInstructorController : MonoBehaviour
{
    private enum InstructorState
    {
        BoardPause,
        MovingAlongBoard,
        MovingToStudent,
        TalkingToStudent,
        Demonstrating,
        DemonstrationPause
    }

    [Header("Timing")]
    [SerializeField, Min(1f)] private float boardPauseMinimum = 3f;
    [SerializeField, Min(1f)] private float boardPauseMaximum = 7f;
    [SerializeField, Min(10f)] private float studentVisitIntervalMinimum = 75f;
    [SerializeField, Min(10f)] private float studentVisitIntervalMaximum = 150f;
    [SerializeField, Min(30f)] private float demonstrationIntervalMinimum = 180f;
    [SerializeField, Min(30f)] private float demonstrationIntervalMaximum = 300f;

    [Header("Runtime")]
    [SerializeField] private string currentActivity = "Preparing lesson";

    private PedestrianMovementController movement;
    private GameTimeManager gameTimeManager;
    private FlightSchoolStudentManager studentManager;
    private Vector2Int roomAnchor;
    private Vector3 boardCenter;
    private readonly Vector3[] boardPatrolPoints = new Vector3[2];
    private readonly List<FlightSchoolStudentAgent> seatedStudents =
        new List<FlightSchoolStudentAgent>(12);

    private InstructorState state;
    private FlightSchoolStudentAgent pendingWelcomeStudent;
    private FlightSchoolStudentAgent currentStudent;
    private FlightSchoolStudentAgent lastVisitedStudent;
    private bool hasWelcomedStudent;
    private bool currentVisitIsWelcome;
    private bool movingToRight;
    private float nextActionTime;
    private float nextStudentVisitTime;
    private float nextDemonstrationTime;
    private long conversationEndGameSecond;
    private bool initialized;
    private bool subscribedToGameTime;

    public void Initialize(
        Vector2Int classroomAnchor,
        Vector3 wallBoardCenter,
        Vector3 patrolStart,
        Vector3 patrolEnd,
        FlightSchoolStudentManager manager,
        GameTimeManager timeManager)
    {
        roomAnchor = classroomAnchor;
        boardCenter = wallBoardCenter;
        boardPatrolPoints[0] = patrolStart;
        boardPatrolPoints[1] = patrolEnd;
        studentManager = manager;
        gameTimeManager = timeManager;

        movement = GetComponent<PedestrianMovementController>();
        if (movement == null)
            movement = gameObject.AddComponent<PedestrianMovementController>();

        movement.Arrived -= HandleMovementArrived;
        movement.PathFailed -= HandleMovementFailed;
        movement.Arrived += HandleMovementArrived;
        movement.PathFailed += HandleMovementFailed;

        SubscribeToGameTime();
        initialized = true;
        state = InstructorState.BoardPause;
        nextActionTime = Time.time + 1f;
        ScheduleNextStudentVisit();
        ScheduleNextDemonstration();
        MoveToBoardPoint(0);
    }

    private void OnEnable()
    {
        SubscribeToGameTime();
    }

    private void OnDisable()
    {
        if (currentStudent != null)
            currentStudent.SetInstructorConversationActive(false);

        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            movement.PathFailed -= HandleMovementFailed;
        }

        if (gameTimeManager != null && subscribedToGameTime)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;

        subscribedToGameTime = false;
    }

    private void SubscribeToGameTime()
    {
        if (gameTimeManager == null || subscribedToGameTime)
            return;

        gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
        subscribedToGameTime = true;
    }

    private void Update()
    {
        if ((state == InstructorState.MovingToStudent ||
             state == InstructorState.TalkingToStudent) &&
            (currentStudent == null || !currentStudent.IsInstructorConversationActive))
        {
            ReturnToBoard();
        }

        if (!initialized || movement == null || movement.IsMoving ||
            Time.time < nextActionTime)
        {
            return;
        }

        if (state == InstructorState.BoardPause)
        {
            if (pendingWelcomeStudent != null)
            {
                FlightSchoolStudentAgent welcome = pendingWelcomeStudent;
                pendingWelcomeStudent = null;
                BeginStudentVisit(welcome, true);
                return;
            }

            if (Time.time >= nextStudentVisitTime &&
                TryChooseStudentToVisit())
            {
                return;
            }

            if (Time.time >= nextDemonstrationTime)
            {
                BeginDemonstration();
                return;
            }

            MoveToBoardPoint(movingToRight ? 1 : 0);
        }
        else if (state == InstructorState.DemonstrationPause)
        {
            ReturnToBoard();
        }
    }

    public void NotifyStudentArrived(FlightSchoolStudentAgent student)
    {
        if (student == null || hasWelcomedStudent)
            return;

        hasWelcomedStudent = true;
        pendingWelcomeStudent = student;
    }

    private bool TryChooseStudentToVisit()
    {
        if (studentManager == null)
            return false;

        studentManager.CollectSeatedStudents(roomAnchor, seatedStudents);
        if (seatedStudents.Count == 0)
        {
            ScheduleNextStudentVisit();
            return false;
        }

        FlightSchoolStudentAgent selected = null;
        if (seatedStudents.Count == 1)
        {
            selected = seatedStudents[0];
        }
        else
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                FlightSchoolStudentAgent candidate =
                    seatedStudents[Random.Range(0, seatedStudents.Count)];

                if (candidate != lastVisitedStudent)
                {
                    selected = candidate;
                    break;
                }
            }
        }

        if (selected == null)
            selected = seatedStudents[Random.Range(0, seatedStudents.Count)];

        return BeginStudentVisit(selected, false);
    }

    private bool BeginStudentVisit(
        FlightSchoolStudentAgent student,
        bool isWelcome)
    {
        if (student == null || !student.IsSeated ||
            student.ReservedSeat == null || movement == null)
        {
            ScheduleNextStudentVisit();
            return false;
        }

        Vector3 awayFromStudent = transform.position - student.transform.position;
        awayFromStudent.y = 0f;
        if (awayFromStudent.sqrMagnitude < 0.01f)
            awayFromStudent = Vector3.right;
        awayFromStudent.Normalize();

        Vector3 preferredPosition =
            student.transform.position + awayFromStudent * 1.1f;

        PlacedBuilding chair =
            student.ReservedSeat.GetComponentInParent<PlacedBuilding>();

        if (!movement.TryMoveToNearby(
                preferredPosition,
                student.transform.position,
                chair))
        {
            ScheduleNextStudentVisit();
            ReturnToBoard();
            return false;
        }

        currentStudent = student;
        currentStudent.SetInstructorConversationActive(true);
        currentVisitIsWelcome = isWelcome;
        lastVisitedStudent = student;
        state = InstructorState.MovingToStudent;
        currentActivity = isWelcome
            ? "Welcoming a student"
            : "Answering a student's question";
        return true;
    }

    private void BeginDemonstration()
    {
        ScheduleNextDemonstration();
        state = InstructorState.Demonstrating;
        currentActivity = "Demonstrating at the wallboard";

        if (!movement.TryMoveToNearby(boardCenter, boardCenter))
            ReturnToBoard();
    }

    private void MoveToBoardPoint(int pointIndex)
    {
        state = InstructorState.MovingAlongBoard;
        currentActivity = "Explaining at the wallboard";

        Vector3 target = boardPatrolPoints[Mathf.Clamp(pointIndex, 0, 1)];
        if (!movement.TryMoveToNearby(target, target))
        {
            movingToRight = !movingToRight;
            nextActionTime = Time.time + 2f;
            state = InstructorState.BoardPause;
        }
    }

    private void ReturnToBoard()
    {
        if (currentStudent != null)
            currentStudent.SetInstructorConversationActive(false);

        currentStudent = null;
        currentVisitIsWelcome = false;
        currentActivity = "Returning to the wallboard";
        MoveToBoardPoint(movingToRight ? 1 : 0);
    }

    private void HandleMovementArrived()
    {
        switch (state)
        {
            case InstructorState.MovingAlongBoard:
                FaceWallBoard();
                state = InstructorState.BoardPause;
                currentActivity = "Explaining at the wallboard";
                nextActionTime = Time.time +
                    Random.Range(boardPauseMinimum, boardPauseMaximum);
                movingToRight = !movingToRight;
                break;

            case InstructorState.MovingToStudent:
                FaceCurrentStudent();
                state = InstructorState.TalkingToStudent;
                currentActivity = currentVisitIsWelcome
                    ? "Welcoming a student"
                    : "Talking with a student";
                int duration = currentVisitIsWelcome
                    ? Random.Range(30, 61)
                    : Random.Range(2 * 60, 7 * 60 + 1);
                conversationEndGameSecond =
                    GetAbsoluteGameTimeSeconds() + duration;
                break;

            case InstructorState.Demonstrating:
                FaceWallBoard();
                state = InstructorState.DemonstrationPause;
                currentActivity = "Demonstrating at the wallboard";
                nextActionTime = Time.time + Random.Range(15f, 26f);
                break;
        }
    }

    private void HandleMovementFailed()
    {
        if (!initialized)
            return;

        if (state == InstructorState.MovingToStudent ||
            state == InstructorState.TalkingToStudent)
        {
            if (currentStudent != null)
                currentStudent.SetInstructorConversationActive(false);

            currentStudent = null;
            currentVisitIsWelcome = false;
            ScheduleNextStudentVisit();
        }

        state = InstructorState.BoardPause;
        currentActivity = "Returning to the wallboard";
        nextActionTime = Time.time + 1f;
    }

    private void HandleGameTimeAdvanced(long previousSecond, long currentSecond)
    {
        if (state != InstructorState.TalkingToStudent ||
            currentSecond < conversationEndGameSecond)
        {
            return;
        }

        ScheduleNextStudentVisit();
        ReturnToBoard();
    }

    private void FaceWallBoard()
    {
        Vector3 direction = boardCenter - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void FaceCurrentStudent()
    {
        if (currentStudent == null)
            return;

        Vector3 direction = currentStudent.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private long GetAbsoluteGameTimeSeconds()
    {
        if (gameTimeManager == null)
            return 0;

        return
            (gameTimeManager.CurrentDay - 1) * 86400L +
            gameTimeManager.GetGameTimeSeconds();
    }

    private void ScheduleNextStudentVisit()
    {
        nextStudentVisitTime = Time.time +
            Random.Range(studentVisitIntervalMinimum, studentVisitIntervalMaximum);
    }

    private void ScheduleNextDemonstration()
    {
        nextDemonstrationTime = Time.time +
            Random.Range(demonstrationIntervalMinimum, demonstrationIntervalMaximum);
    }
}
