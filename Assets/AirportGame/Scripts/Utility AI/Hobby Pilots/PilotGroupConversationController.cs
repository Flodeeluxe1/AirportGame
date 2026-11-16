using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates group conversations between three to five hobby pilots.
/// Groups use nearby ordinary seat providers when enough seats are available;
/// otherwise they gather around a shared standing point.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(PedestrianMovementController))]
[RequireComponent(typeof(PilotInteractionController))]
public class PilotGroupConversationController : MonoBehaviour
{
    private static readonly List<PedestrianMovementController> NearbyMovements =
        new List<PedestrianMovementController>(24);
    private static readonly List<InteractionProvider> NearbyProviders =
        new List<InteractionProvider>(64);

    private enum GroupState { Idle, Travelling, Talking }

    private sealed class Member
    {
        public PilotGroupConversationController Controller;
        public Vector3 Target;
        public bool Arrived;
        public InteractionProvider SeatProvider;
        public InteractionDefinition SeatDefinition;
    }

    private struct SeatOption
    {
        public InteractionProvider Provider;
        public InteractionDefinition Definition;
        public Vector3 Position;
    }

    [Header("Group Search")]
    [Min(0.1f)]
    [SerializeField] private float searchInterval = 4f;
    [Min(0.1f)]
    [SerializeField] private float searchRadius = 12f;
    [Range(3, 5)]
    [SerializeField] private int minimumGroupSize = 3;
    [Range(3, 5)]
    [SerializeField] private int maximumGroupSize = 5;
    [Range(0f, 100f)]
    [SerializeField] private float maximumSocialNeedValue = 80f;

    [Header("Group Conversation")]
    [Min(1)]
    [SerializeField] private int conversationDurationGameSeconds = 900;
    [Min(0f)]
    [SerializeField] private float socialSatisfactionAmount = 35f;
    [Min(0f)]
    [SerializeField] private float entertainmentSatisfactionAmount = 20f;
    [Min(0f)]
    [SerializeField] private float boredomSatisfactionAmount = 20f;
    [Min(0.2f)]
    [SerializeField] private float standingCircleRadius = 1.5f;
    [Min(0.5f)]
    [SerializeField] private float seatClusterRadius = 4.5f;
    [Min(1f)]
    [SerializeField] private float travelTimeout = 60f;
    [Min(1f)]
    [SerializeField] private float facingRotationSpeed = 540f;

    [Header("Runtime")]
    [SerializeField] private string currentStatus = "Idle";
    [SerializeField] private int currentGroupSize;

    private UtilityNeeds needs;
    private PedestrianMovementController movement;
    private PilotInteractionController interactionController;
    private PilotSocialInteractionController casualController;
    private PilotSocialSeatingController seatingController;
    private GameTimeManager gameTimeManager;
    private bool subscribedToGameTime;
    private GroupState state;
    private PilotGroupConversationController activeLeader;
    private readonly List<Member> members = new List<Member>();
    private float nextSearchTime;
    private float travelElapsed;
    private long conversationStartGameSecond;
    private bool isLeader;

    public bool IsBusy => state != GroupState.Idle || activeLeader != null;
    public string CurrentStatus => currentStatus;

    private void Awake()
    {
        needs = GetComponent<UtilityNeeds>();
        movement = GetComponent<PedestrianMovementController>();
        interactionController = GetComponent<PilotInteractionController>();
        casualController = GetComponent<PilotSocialInteractionController>();
        seatingController = GetComponent<PilotSocialSeatingController>();
        ResolveGameTimeManager();
    }

    private void OnEnable()
    {
        if (movement != null)
        {
            movement.Arrived += HandleMovementArrived;
            movement.PathFailed += HandlePathFailed;
        }
        ResolveGameTimeManager();
        nextSearchTime = Time.time + UnityEngine.Random.Range(0f, searchInterval);
    }

    private void OnDisable()
    {
        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            movement.PathFailed -= HandlePathFailed;
        }
        if (gameTimeManager != null && subscribedToGameTime)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
        subscribedToGameTime = false;

        if (isLeader)
            CancelGroup("Group conversation interrupted");
        else if (activeLeader != null)
            activeLeader.CancelGroup("A group member became unavailable");
    }

    private void Update()
    {
        if (gameTimeManager == null)
            ResolveGameTimeManager();

        if (activeLeader != null && activeLeader != this)
            return;

        if (state == GroupState.Travelling)
        {
            travelElapsed += Time.deltaTime;
            if (travelElapsed >= travelTimeout)
                CancelGroup("Could not reach group meeting point");
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

    private bool CanInitiate()
    {
        return isActiveAndEnabled && state == GroupState.Idle &&
               activeLeader == null && needs != null && movement != null &&
               !movement.IsMoving && interactionController != null &&
               interactionController.CanPauseForSocial &&
               gameTimeManager != null &&
               needs.GetValue(UtilityNeeds.NeedType.Social) <= maximumSocialNeedValue;
    }

    private bool CanJoin()
    {
        return isActiveAndEnabled && state == GroupState.Idle &&
               activeLeader == null && needs != null && movement != null &&
               !movement.IsMoving && interactionController != null &&
               interactionController.CanPauseForSocial && gameTimeManager != null &&
               needs.GetValue(UtilityNeeds.NeedType.Social) <= maximumSocialNeedValue;
    }

    /// <summary>Prevents two-person systems taking pilots when a group can form.</summary>
    public static bool ShouldPreferGroup(PilotSocialInteractionController requester)
    {
        if (requester == null)
            return false;

        PilotGroupConversationController own =
            requester.GetComponent<PilotGroupConversationController>();
        return own != null && own.HasNearbyGroupCandidates();
    }

    public static bool ShouldPreferGroup(PilotSocialSeatingController requester)
    {
        if (requester == null)
            return false;

        PilotGroupConversationController own =
            requester.GetComponent<PilotGroupConversationController>();
        return own != null && own.HasNearbyGroupCandidates();
    }

    private bool HasNearbyGroupCandidates()
    {
        if (!CanInitiate())
            return false;

        UtilityAISpatialRegistry<PedestrianMovementController>.GetNearby(
            transform.position, searchRadius, NearbyMovements);
        int count = 1;
        for (int i = 0; i < NearbyMovements.Count; i++)
        {
            PilotGroupConversationController candidate =
                NearbyMovements[i].GetComponent<PilotGroupConversationController>();
            if (candidate == null || candidate == this || !candidate.CanJoin())
                continue;
            if (Vector3.Distance(transform.position, candidate.transform.position) <= searchRadius)
                count++;
            if (count >= minimumGroupSize)
                return true;
        }
        return false;
    }

    private void TryStartGroup()
    {
        if (!CanInitiate())
            return;

        List<PilotGroupConversationController> candidates =
            new List<PilotGroupConversationController>(maximumGroupSize) { this };
        UtilityAISpatialRegistry<PedestrianMovementController>.GetNearby(
            transform.position, searchRadius, NearbyMovements);

        for (int i = 0; i < NearbyMovements.Count; i++)
        {
            PilotGroupConversationController candidate =
                NearbyMovements[i].GetComponent<PilotGroupConversationController>();
            if (candidate == null || candidate == this || !candidate.CanJoin())
                continue;
            if (Vector3.Distance(transform.position, candidate.transform.position) > searchRadius)
                continue;
            candidates.Add(candidate);
        }

        candidates.Sort((a, b) =>
            Vector3.Distance(transform.position, a.transform.position)
                .CompareTo(Vector3.Distance(transform.position, b.transform.position)));

        int groupCount = Mathf.Min(maximumGroupSize, candidates.Count);
        if (groupCount < minimumGroupSize)
            return;
        candidates.RemoveRange(groupCount, candidates.Count - groupCount);

        Vector3 center = Vector3.zero;
        foreach (PilotGroupConversationController candidate in candidates)
            center += candidate.transform.position;
        center /= candidates.Count;

        List<SeatOption> seats = FindSeatOptions(center);

        seats.Sort((a, b) =>
            Vector3.Distance(center, a.Position).CompareTo(Vector3.Distance(center, b.Position)));

        bool useSeats = seats.Count >= candidates.Count;
        if (useSeats)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (Vector3.Distance(center, seats[i].Position) > seatClusterRadius)
                {
                    useSeats = false;
                    break;
                }
            }
        }

        members.Clear();
        for (int i = 0; i < candidates.Count; i++)
        {
            Vector3 target;
            InteractionProvider seatProvider = null;
            InteractionDefinition seatDefinition = null;

            if (useSeats)
            {
                SeatOption seat = seats[i];
                if (!seat.Provider.TryReserve(candidates[i], seat.Definition))
                {
                    ReleaseReservations();
                    members.Clear();
                    return;
                }
                target = seat.Position;
                seatProvider = seat.Provider;
                seatDefinition = seat.Definition;
            }
            else
            {
                float angle = (Mathf.PI * 2f * i) / candidates.Count;
                target = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * standingCircleRadius;
            }

            members.Add(new Member
            {
                Controller = candidates[i],
                Target = target,
                SeatProvider = seatProvider,
                SeatDefinition = seatDefinition
            });
        }

        isLeader = true;
        state = GroupState.Travelling;
        activeLeader = this;
        travelElapsed = 0f;
        currentGroupSize = members.Count;
        currentStatus = useSeats ? "Walking to group seats" : "Walking to group conversation";

        foreach (Member member in members)
        {
            PilotGroupConversationController controller = member.Controller;
            controller.activeLeader = this;
            controller.state = GroupState.Travelling;
            controller.currentGroupSize = members.Count;
            controller.currentStatus = currentStatus;

            if (!controller.movement.MoveTo(member.Target))
            {
                CancelGroup("A group member could not reach the meeting point");
                return;
            }
        }
    }

    private List<SeatOption> FindSeatOptions(Vector3 center)
    {
        List<SeatOption> result = new List<SeatOption>(maximumGroupSize);
        UtilityAISpatialRegistry<InteractionProvider>.GetNearby(
            center, seatClusterRadius, NearbyProviders);

        for (int providerIndex = 0; providerIndex < NearbyProviders.Count; providerIndex++)
        {
            InteractionProvider provider = NearbyProviders[providerIndex];
            if (provider == null || !provider.InteractionsEnabled)
                continue;

            IReadOnlyList<InteractionDefinition> definitions = provider.Interactions;
            for (int i = 0; i < definitions.Count; i++)
            {
                InteractionDefinition definition = definitions[i];
                if (definition == null || string.IsNullOrEmpty(definition.InteractionId))
                    continue;

                string id = definition.InteractionId;
                bool isSeat = id.IndexOf("sit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              id.IndexOf("seat", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isSeat || !provider.CanOffer(definition))
                    continue;

                result.Add(new SeatOption
                {
                    Provider = provider,
                    Definition = definition,
                    Position = provider.InteractionPosition
                });
                break;
            }
        }
        return result;
    }

    private void HandleMovementArrived()
    {
        if (activeLeader == null)
            return;
        activeLeader.MarkMemberArrived(this);
    }

    private void HandlePathFailed()
    {
        if (activeLeader != null)
            activeLeader.CancelGroup("A group member's path failed");
    }

    private void MarkMemberArrived(PilotGroupConversationController controller)
    {
        if (!isLeader || state != GroupState.Travelling)
            return;

        Member member = members.Find(item => item.Controller == controller);
        if (member == null)
            return;

        member.Arrived = true;
        foreach (Member item in members)
        {
            if (!item.Arrived)
                return;
        }

        // Seated members first reach their walkable approach points, then
        // move to the seat's dedicated animation positions.
        foreach (Member item in members)
        {
            if (item.SeatProvider != null)
            {
                item.Controller.transform.SetPositionAndRotation(
                    item.SeatProvider.AnimationPosition,
                    item.SeatProvider.AnimationRotation);
            }
        }

        state = GroupState.Talking;
        conversationStartGameSecond = GetCurrentAbsoluteGameSecond();
        foreach (Member item in members)
        {
            item.Controller.state = GroupState.Talking;
            item.Controller.currentStatus = "Talking in a group";
            item.Controller.movement.Stop();
        }
        FaceGroup();
    }

    private void FaceGroup()
    {
        if (!isLeader || members.Count == 0)
            return;

        Vector3 center = Vector3.zero;
        foreach (Member member in members)
            center += member.Controller.transform.position;
        center /= members.Count;

        foreach (Member member in members)
        {
            Vector3 direction = center - member.Controller.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                continue;

            Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up);
            member.Controller.transform.rotation = Quaternion.RotateTowards(
                member.Controller.transform.rotation,
                target,
                facingRotationSpeed * Time.deltaTime);
        }
    }

    private void ResolveGameTimeManager()
    {
        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();
        if (gameTimeManager == null || subscribedToGameTime)
            return;
        gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
        subscribedToGameTime = true;
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
        if (gameTimeManager == null)
            return 0;
        return (gameTimeManager.CurrentDay - 1) * 86400L +
               gameTimeManager.GetGameTimeSeconds();
    }

    private void CompleteGroup()
    {
        foreach (Member member in members)
        {
            if (member.Controller != null && member.Controller.needs != null)
            {
                member.Controller.needs.SatisfyNeed(
                    UtilityNeeds.NeedType.Social, socialSatisfactionAmount);
                member.Controller.needs.SatisfyNeed(
                    UtilityNeeds.NeedType.Entertainment, entertainmentSatisfactionAmount);
                member.Controller.needs.SatisfyNeed(
                    UtilityNeeds.NeedType.Boredom, boredomSatisfactionAmount);
            }
        }
        ReleaseReservations();
        ResetGroup("Group conversation complete");
    }

    private void CancelGroup(string reason)
    {
        if (!isLeader)
        {
            if (activeLeader != null)
                activeLeader.CancelGroup(reason);
            return;
        }

        ReleaseReservations();
        ResetGroup(reason);
    }

    private void ReleaseReservations()
    {
        foreach (Member member in members)
        {
            if (member.SeatProvider != null && member.SeatDefinition != null &&
                member.Controller != null)
                member.SeatProvider.ReleaseReservation(
                    member.Controller, member.SeatDefinition);
        }
    }

    private void ResetGroup(string status)
    {
        foreach (Member member in members)
        {
            PilotGroupConversationController controller = member.Controller;
            if (controller == null)
                continue;
            if (controller.movement != null && controller.movement.IsMoving)
                controller.movement.Stop();
            controller.activeLeader = null;
            controller.state = GroupState.Idle;
            controller.isLeader = false;
            controller.currentGroupSize = 0;
            controller.currentStatus = status;
            controller.nextSearchTime = Time.time + controller.searchInterval;
        }
        members.Clear();
        activeLeader = null;
        state = GroupState.Idle;
        isLeader = false;
        currentGroupSize = 0;
        currentStatus = status;
        travelElapsed = 0f;
        conversationStartGameSecond = 0;
        nextSearchTime = Time.time + searchInterval;
    }

    private void OnValidate()
    {
        searchInterval = Mathf.Max(0.1f, searchInterval);
        searchRadius = Mathf.Max(0.1f, searchRadius);
        minimumGroupSize = Mathf.Clamp(minimumGroupSize, 3, 5);
        maximumGroupSize = Mathf.Clamp(maximumGroupSize, minimumGroupSize, 5);
        conversationDurationGameSeconds = Mathf.Max(1, conversationDurationGameSeconds);
        socialSatisfactionAmount = Mathf.Max(0f, socialSatisfactionAmount);
        entertainmentSatisfactionAmount = Mathf.Max(0f, entertainmentSatisfactionAmount);
        boredomSatisfactionAmount = Mathf.Max(0f, boredomSatisfactionAmount);
        standingCircleRadius = Mathf.Max(0.2f, standingCircleRadius);
        seatClusterRadius = Mathf.Max(0.5f, seatClusterRadius);
        travelTimeout = Mathf.Max(1f, travelTimeout);
        facingRotationSpeed = Mathf.Max(1f, facingRotationSpeed);
    }
}
