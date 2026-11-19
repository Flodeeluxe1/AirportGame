using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates standing-only social conversations between Flight School students
/// assigned to the same classroom. Meeting points are reserved walkable cells
/// inside that classroom; conversation timing uses in-game time.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FlightSchoolStudentAgent))]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(PedestrianMovementController))]
public class FlightSchoolStudentGroupConversationController : MonoBehaviour
{
    private static readonly List<FlightSchoolStudentGroupConversationController> ActiveControllers =
        new List<FlightSchoolStudentGroupConversationController>(32);
    private static readonly List<PedestrianMovementController> NearbyMovements =
        new List<PedestrianMovementController>(24);
    private static readonly HashSet<Vector2Int> ReservedMeetingCells =
        new HashSet<Vector2Int>();

    private enum GroupState { Idle, Travelling, Talking }

    private sealed class Member
    {
        public FlightSchoolStudentGroupConversationController Controller;
        public Vector3 Target;
        public Vector2Int Cell;
        public bool Arrived;
    }

    [Header("Group Search")]
    [SerializeField, Min(0.1f)] private float searchInterval = 4f;
    [SerializeField, Min(1f)] private float searchRadius = 12f;
    [SerializeField, Range(2, 5)] private int maximumGroupSize = 5;
    [SerializeField, Range(0f, 100f)] private float socialNeedThreshold = 80f;
    [SerializeField, Min(0.5f)] private float meetingAreaRadius = 4f;
    [SerializeField, Min(0.5f)] private float minimumMemberSpacing = 0.9f;

    [Header("Conversation")]
    [SerializeField, Range(1, 30)] private int minimumDurationMinutes = 4;
    [SerializeField, Range(1, 30)] private int maximumDurationMinutes = 12;
    [SerializeField, Min(0f)] private float socialSatisfactionAmount = 40f;
    [SerializeField, Min(1f)] private float travelTimeout = 90f;
    [SerializeField, Min(1f)] private float facingRotationSpeed = 540f;

    [Header("Runtime")]
    [SerializeField] private string currentStatus = "Idle";
    [SerializeField] private int currentGroupSize;

    private FlightSchoolStudentAgent student;
    private UtilityNeeds needs;
    private PedestrianMovementController movement;
    private GridManager gridManager;
    private PedestrianPathfinder pathfinder;
    private RoomDetectionController roomDetection;
    private GameTimeManager gameTimeManager;
    private Vector2Int classroomAnchor;
    private bool initialized;
    private bool subscribedToGameTime;
    private GroupState state;
    private FlightSchoolStudentGroupConversationController activeLeader;
    private readonly List<Member> members = new List<Member>(5);
    private readonly List<Vector3> selectedTargets = new List<Vector3>(5);
    private readonly List<Vector2Int> selectedCells = new List<Vector2Int>(5);
    private float nextSearchTime;
    private float travelElapsed;
    private long conversationStartGameSecond;
    private int conversationDurationGameSeconds;
    private bool isLeader;

    public bool IsBusy => state != GroupState.Idle || activeLeader != null;
    public string CurrentStatus => currentStatus;

    private void Awake()
    {
        student = GetComponent<FlightSchoolStudentAgent>();
        needs = GetComponent<UtilityNeeds>();
        movement = GetComponent<PedestrianMovementController>();
        ResolveSceneReferences();
    }

    private void OnEnable()
    {
        if (!ActiveControllers.Contains(this))
            ActiveControllers.Add(this);

        nextSearchTime = Time.time + Random.Range(0f, searchInterval);
        ResolveGameTimeManager();
    }

    private void OnDisable()
    {
        ActiveControllers.Remove(this);

        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            movement.PathFailed -= HandlePathFailed;
        }

        if (gameTimeManager != null && subscribedToGameTime)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
        subscribedToGameTime = false;

        if (isLeader)
            CancelGroup("Conversation controller disabled");
        else if (activeLeader != null)
            activeLeader.CancelGroup("A group member became unavailable");
    }

    public void Initialize(Vector2Int roomAnchor)
    {
        classroomAnchor = roomAnchor;
        student = GetComponent<FlightSchoolStudentAgent>();
        needs = GetComponent<UtilityNeeds>();
        movement = GetComponent<PedestrianMovementController>();
        ResolveSceneReferences();

        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            movement.PathFailed -= HandlePathFailed;
            movement.Arrived += HandleMovementArrived;
            movement.PathFailed += HandlePathFailed;
        }

        ResolveGameTimeManager();
        initialized = true;
        nextSearchTime = Time.time + Random.Range(0f, searchInterval);
    }

    private void ResolveSceneReferences()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (pathfinder == null)
            pathfinder = FindAnyObjectByType<PedestrianPathfinder>();
        if (roomDetection == null)
            roomDetection = FindAnyObjectByType<RoomDetectionController>();
        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();
    }

    private void ResolveGameTimeManager()
    {
        ResolveSceneReferences();
        if (gameTimeManager == null || subscribedToGameTime)
            return;

        gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
        subscribedToGameTime = true;
    }

    private void Update()
    {
        if (!initialized)
            return;

        if (gameTimeManager == null)
            ResolveGameTimeManager();

        if (activeLeader != null && activeLeader != this)
            return;

        if (state == GroupState.Travelling)
        {
            travelElapsed += Time.deltaTime;
            if (travelElapsed >= travelTimeout)
                CancelGroup("A student could not reach the meeting point");
            return;
        }

        if (state == GroupState.Talking)
        {
            FaceGroup();
            return;
        }

        if (Time.time < nextSearchTime)
            return;

        nextSearchTime = Time.time + searchInterval;
        TryStartGroup();
    }

    private bool CanJoin()
    {
        return initialized && isActiveAndEnabled &&
               state == GroupState.Idle && activeLeader == null &&
               student != null && student.IsSeated &&
               needs != null && movement != null && !movement.IsMoving &&
               needs.GetValue(UtilityNeeds.NeedType.Social) <= socialNeedThreshold &&
               gameTimeManager != null;
    }

    private void TryStartGroup()
    {
        if (!CanJoin() || roomDetection == null || gridManager == null || pathfinder == null)
            return;

        List<FlightSchoolStudentGroupConversationController> candidates =
            new List<FlightSchoolStudentGroupConversationController>(maximumGroupSize) { this };

        for (int i = 0; i < ActiveControllers.Count; i++)
        {
            FlightSchoolStudentGroupConversationController candidate = ActiveControllers[i];
            if (candidate == null || candidate == this ||
                candidate.classroomAnchor != classroomAnchor || !candidate.CanJoin())
                continue;

            if (Vector3.Distance(transform.position, candidate.transform.position) <= searchRadius)
                candidates.Add(candidate);
        }

        if (candidates.Count < 2)
            return;

        candidates.Sort((a, b) =>
            Vector3.Distance(transform.position, a.transform.position)
                .CompareTo(Vector3.Distance(transform.position, b.transform.position)));

        int groupSize = Random.Range(2, Mathf.Min(maximumGroupSize, candidates.Count) + 1);
        candidates.RemoveRange(groupSize, candidates.Count - groupSize);

        Vector3 center = Vector3.zero;
        for (int i = 0; i < candidates.Count; i++)
            center += candidates[i].transform.position;
        center /= candidates.Count;

        if (!TryFindMeetingPositions(center, groupSize, candidates))
            return;

        members.Clear();
        for (int i = 0; i < groupSize; i++)
        {
            FlightSchoolStudentGroupConversationController controller = candidates[i];
            if (!controller.student.PrepareForGroupConversation())
            {
                CancelUnstartedGroup(candidates);
                return;
            }

            members.Add(new Member
            {
                Controller = controller,
                Target = selectedTargets[i],
                Cell = selectedCells[i]
            });
        }

        isLeader = true;
        state = GroupState.Travelling;
        activeLeader = this;
        travelElapsed = 0f;
        currentGroupSize = members.Count;
        currentStatus = "Walking to standing conversation";

        foreach (Member member in members)
        {
            FlightSchoolStudentGroupConversationController controller = member.Controller;
            controller.activeLeader = this;
            controller.state = GroupState.Travelling;
            controller.currentGroupSize = members.Count;
            controller.currentStatus = currentStatus;

            if (!controller.movement.TryMoveTo(member.Target))
            {
                CancelGroup("A student could not path to the conversation point");
                return;
            }
        }
    }

    private bool TryFindMeetingPositions(
        Vector3 center,
        int requiredCount,
        List<FlightSchoolStudentGroupConversationController> groupCandidates)
    {
        if (!roomDetection.TryGetRoomAt(classroomAnchor, out RoomDetectionController.DetectedRoom room) ||
            room == null || room.cells == null)
            return false;

        List<Vector2Int> freeCells = new List<Vector2Int>(room.cells.Count);
        for (int i = 0; i < room.cells.Count; i++)
        {
            Vector2Int cell = room.cells[i];
            if (ReservedMeetingCells.Contains(cell))
                continue;

            Vector3 cellCenter = gridManager.GridToWorldCenter(cell);
            if (Vector3.Distance(center, cellCenter) > meetingAreaRadius)
                continue;

            if (!pathfinder.TryFindNearestWalkablePosition(cellCenter, 0, out Vector3 walkablePosition) ||
                gridManager.WorldToGrid(walkablePosition) != cell ||
                IsOccupiedByOtherPedestrian(walkablePosition, groupCandidates))
                continue;

            freeCells.Add(cell);
        }

        // Shuffle so groups do not repeatedly gather in the same part of the room.
        for (int i = freeCells.Count - 1; i > 0; i--)
        {
            int swap = Random.Range(0, i + 1);
            Vector2Int temp = freeCells[i];
            freeCells[i] = freeCells[swap];
            freeCells[swap] = temp;
        }

        selectedTargets.Clear();
        selectedCells.Clear();

        for (int i = 0; i < freeCells.Count && selectedCells.Count < requiredCount; i++)
        {
            Vector2Int cell = freeCells[i];
            Vector3 cellCenter = gridManager.GridToWorldCenter(cell);
            if (!pathfinder.TryFindNearestWalkablePosition(cellCenter, 0, out Vector3 target))
                continue;

            bool tooClose = false;
            for (int j = 0; j < selectedTargets.Count; j++)
            {
                if (Vector3.Distance(selectedTargets[j], target) < minimumMemberSpacing)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            selectedCells.Add(cell);
            selectedTargets.Add(target);
        }

        if (selectedCells.Count < requiredCount)
        {
            selectedCells.Clear();
            selectedTargets.Clear();
            return false;
        }

        foreach (Vector2Int cell in selectedCells)
            ReservedMeetingCells.Add(cell);

        return true;
    }

    private bool IsOccupiedByOtherPedestrian(
        Vector3 position,
        List<FlightSchoolStudentGroupConversationController> groupCandidates)
    {
        UtilityAISpatialRegistry<PedestrianMovementController>.GetNearby(
            position, 0.8f, NearbyMovements);

        for (int i = 0; i < NearbyMovements.Count; i++)
        {
            PedestrianMovementController nearby = NearbyMovements[i];
            if (nearby == null)
                continue;

            bool isGroupMember = false;
            for (int j = 0; j < groupCandidates.Count; j++)
            {
                if (groupCandidates[j].movement == nearby)
                {
                    isGroupMember = true;
                    break;
                }
            }

            if (!isGroupMember &&
                Vector3.Distance(nearby.transform.position, position) < 0.65f)
                return true;
        }

        return false;
    }

    private void CancelUnstartedGroup(
        List<FlightSchoolStudentGroupConversationController> candidates)
    {
        foreach (FlightSchoolStudentGroupConversationController candidate in candidates)
        {
            if (candidate != null && candidate.student != null)
                candidate.student.EndGroupConversationAndReturnToSeat();
        }

        foreach (Vector2Int cell in selectedCells)
            ReservedMeetingCells.Remove(cell);
        selectedCells.Clear();
        selectedTargets.Clear();
        members.Clear();
    }

    private void HandleMovementArrived()
    {
        if (activeLeader != null)
            activeLeader.MarkMemberArrived(this);
    }

    private void HandlePathFailed()
    {
        if (activeLeader != null)
            activeLeader.CancelGroup("A student's conversation path failed");
    }

    private void MarkMemberArrived(FlightSchoolStudentGroupConversationController controller)
    {
        if (!isLeader || state != GroupState.Travelling)
            return;

        Member member = members.Find(item => item.Controller == controller);
        if (member == null)
            return;

        member.Arrived = true;
        for (int i = 0; i < members.Count; i++)
        {
            if (!members[i].Arrived)
                return;
        }

        state = GroupState.Talking;
        conversationStartGameSecond = GetCurrentAbsoluteGameSecond();
        int minimum = Mathf.Min(minimumDurationMinutes, maximumDurationMinutes);
        int maximum = Mathf.Max(minimumDurationMinutes, maximumDurationMinutes);
        conversationDurationGameSeconds = Random.Range(minimum, maximum + 1) * 60;

        foreach (Member item in members)
        {
            item.Controller.state = GroupState.Talking;
            item.Controller.currentStatus = "Standing and talking";
            item.Controller.movement.Stop();
        }

        FaceGroup();
    }

    private void FaceGroup()
    {
        if (!isLeader || members.Count == 0)
            return;

        Vector3 center = Vector3.zero;
        for (int i = 0; i < members.Count; i++)
            center += members[i].Controller.transform.position;
        center /= members.Count;

        for (int i = 0; i < members.Count; i++)
        {
            FlightSchoolStudentGroupConversationController controller = members[i].Controller;
            Vector3 direction = center - controller.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                continue;

            Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up);
            controller.transform.rotation = Quaternion.RotateTowards(
                controller.transform.rotation, target, facingRotationSpeed * Time.deltaTime);
        }
    }

    private void HandleGameTimeAdvanced(long previousSecond, long currentSecond)
    {
        if (!isLeader || state != GroupState.Talking)
            return;

        if (currentSecond - conversationStartGameSecond >= conversationDurationGameSeconds)
            CompleteGroup();
    }

    private long GetCurrentAbsoluteGameSecond()
    {
        return (gameTimeManager.CurrentDay - 1) * 86400L +
               gameTimeManager.GetGameTimeSeconds();
    }

    private void CompleteGroup()
    {
        foreach (Member member in members)
        {
            if (member.Controller != null && member.Controller.needs != null)
                member.Controller.needs.SatisfyNeed(
                    UtilityNeeds.NeedType.Social, socialSatisfactionAmount);
        }

        FinishGroupAndReturnMembers(null, "Conversation complete");
    }

    public void InterruptForDeparture()
    {
        if (isLeader)
            CancelGroup("A student is departing", student);
        else if (activeLeader != null)
            activeLeader.CancelGroup("A student is departing", student);
    }

    private void CancelGroup(string reason, FlightSchoolStudentAgent departingStudent = null)
    {
        if (!isLeader)
        {
            if (activeLeader != null)
                activeLeader.CancelGroup(reason, departingStudent);
            return;
        }

        FinishGroupAndReturnMembers(departingStudent, reason);
    }

    private void FinishGroupAndReturnMembers(
        FlightSchoolStudentAgent departingStudent,
        string status)
    {
        ReleaseReservedCells();

        foreach (Member member in members)
        {
            FlightSchoolStudentGroupConversationController controller = member.Controller;
            if (controller == null)
                continue;

            controller.activeLeader = null;
            controller.state = GroupState.Idle;
            controller.isLeader = false;
            controller.currentGroupSize = 0;
            controller.currentStatus = status;
            controller.nextSearchTime = Time.time + controller.searchInterval;

            if (controller.movement != null && controller.movement.IsMoving)
                controller.movement.Stop();

            if (controller.student != null &&
                controller.student != departingStudent)
                controller.student.EndGroupConversationAndReturnToSeat();
        }

        members.Clear();
        activeLeader = null;
        state = GroupState.Idle;
        isLeader = false;
        currentGroupSize = 0;
        currentStatus = status;
        travelElapsed = 0f;
        conversationStartGameSecond = 0;
        conversationDurationGameSeconds = 0;
        nextSearchTime = Time.time + searchInterval;
    }

    private void ReleaseReservedCells()
    {
        foreach (Member member in members)
            ReservedMeetingCells.Remove(member.Cell);

        selectedCells.Clear();
        selectedTargets.Clear();
    }

    private void OnValidate()
    {
        searchInterval = Mathf.Max(0.1f, searchInterval);
        searchRadius = Mathf.Max(1f, searchRadius);
        maximumGroupSize = Mathf.Clamp(maximumGroupSize, 2, 5);
        meetingAreaRadius = Mathf.Max(0.5f, meetingAreaRadius);
        minimumMemberSpacing = Mathf.Max(0.5f, minimumMemberSpacing);
        minimumDurationMinutes = Mathf.Clamp(minimumDurationMinutes, 1, 30);
        maximumDurationMinutes = Mathf.Clamp(maximumDurationMinutes, minimumDurationMinutes, 30);
        travelTimeout = Mathf.Max(1f, travelTimeout);
        facingRotationSpeed = Mathf.Max(1f, facingRotationSpeed);
    }
}
