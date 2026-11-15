using UnityEngine;

/// <summary>
/// Coordinates mutual conversations between available hobby pilots.
/// Both pilots walk to separate meeting positions, face one another,
/// wait for the conversation, and receive Social satisfaction together.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(PedestrianMovementController))]
[RequireComponent(typeof(PilotInteractionController))]
public class PilotSocialInteractionController : MonoBehaviour
{
    private enum SocialState
    {
        Idle,
        Travelling,
        Conversing
    }

    [Header("Talk To Pilot")]
    [Min(0.1f)]
    [SerializeField] private float searchInterval = 2f;
    [Min(0.1f)]
    [SerializeField] private float partnerSearchRadius = 8f;
    [Range(0f, 100f)]
    [SerializeField] private float maximumSocialNeedValue = 80f;
    [Min(1)]
    [SerializeField] private int conversationDurationGameSeconds = 30;
    [Min(0f)]
    [SerializeField] private float socialSatisfactionAmount = 30f;
    [Min(0.2f)]
    [SerializeField] private float conversationDistance = 1.2f;
    [Min(1f)]
    [SerializeField] private float travelTimeout = 30f;
    [Min(1f)]
    [SerializeField] private float facingRotationSpeed = 540f;

    [Header("Runtime")]
    [SerializeField] private string currentStatus = "Idle";
    [SerializeField] private string currentPartnerName;

    private UtilityNeeds needs;
    private PedestrianMovementController movement;
    private PilotInteractionController interactionController;
    private GameTimeManager gameTimeManager;
    private PilotSocialInteractionController partner;
    private SocialState state;
    private bool isConversationLeader;
    private bool hasArrived;
    private float nextSearchTime;
    private float travelElapsed;
    private long conversationStartGameSecond;

    public bool IsBusy => state != SocialState.Idle;
    public string CurrentStatus => currentStatus;

    private void Awake()
    {
        needs = GetComponent<UtilityNeeds>();
        movement = GetComponent<PedestrianMovementController>();
        interactionController = GetComponent<PilotInteractionController>();
        gameTimeManager = FindFirstObjectByType<GameTimeManager>();
    }

    private void OnEnable()
    {
        if (movement == null)
            movement = GetComponent<PedestrianMovementController>();

        if (movement != null)
        {
            movement.Arrived += HandleMovementArrived;
            movement.PathFailed += HandlePathFailed;
        }

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;

        nextSearchTime = Time.time + Random.Range(0f, searchInterval);
    }

    private void OnDisable()
    {
        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            movement.PathFailed -= HandlePathFailed;
        }

        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;

        CancelConversation("Social interaction interrupted");
    }

    private void Update()
    {
        if (state != SocialState.Idle && partner == null)
        {
            ResetOwnState("Conversation partner unavailable");
            return;
        }

        switch (state)
        {
            case SocialState.Travelling:
                travelElapsed += Time.deltaTime;

                if (travelElapsed >= travelTimeout)
                    CancelConversation("Could not reach conversation position");

                return;

            case SocialState.Conversing:
                FacePartner();

                return;
        }

        if (Time.time < nextSearchTime)
            return;

        nextSearchTime = Time.time + searchInterval;
        TryFindConversationPartner();
    }

    private bool CanStartConversation()
    {
        return isActiveAndEnabled &&
               state == SocialState.Idle &&
               partner == null &&
               needs != null &&
               movement != null &&
               !movement.IsMoving &&
               interactionController != null &&
               interactionController.CanPauseForSocial &&
               needs.GetValue(UtilityNeeds.NeedType.Social) <= maximumSocialNeedValue;
    }

    private void TryFindConversationPartner()
    {
        if (!CanStartConversation())
            return;

        PilotSocialInteractionController[] pilots =
            FindObjectsByType<PilotSocialInteractionController>(
                FindObjectsSortMode.None);

        PilotSocialInteractionController selectedPartner = null;
        float closestDistance = partnerSearchRadius;

        foreach (PilotSocialInteractionController candidate in pilots)
        {
            if (candidate == null || candidate == this ||
                !candidate.CanStartConversation())
            {
                continue;
            }

            float distance = Vector3.Distance(
                transform.position,
                candidate.transform.position);

            if (distance >= closestDistance)
                continue;

            Vector3 direction = candidate.transform.position - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude <= 0.0001f)
                direction = transform.forward;

            direction.Normalize();

            Vector3 meetingCenter =
                (transform.position + candidate.transform.position) * 0.5f;

            float halfDistance = conversationDistance * 0.5f;
            Vector3 ownTarget = meetingCenter - direction * halfDistance;
            Vector3 partnerTarget = meetingCenter + direction * halfDistance;

            if (float.IsInfinity(movement.EstimateTravelTime(ownTarget)) ||
                float.IsInfinity(candidate.movement.EstimateTravelTime(partnerTarget)))
            {
                continue;
            }

            closestDistance = distance;
            selectedPartner = candidate;
        }

        if (selectedPartner != null)
            BeginConversationTravel(selectedPartner);
    }

    private void BeginConversationTravel(
        PilotSocialInteractionController other)
    {
        if (other == null ||
            !CanStartConversation() ||
            !other.CanStartConversation())
        {
            return;
        }

        Vector3 direction = other.transform.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            direction = transform.forward;

        direction.Normalize();

        Vector3 meetingCenter =
            (transform.position + other.transform.position) * 0.5f;

        float halfDistance = conversationDistance * 0.5f;
        Vector3 ownTarget = meetingCenter - direction * halfDistance;
        Vector3 partnerTarget = meetingCenter + direction * halfDistance;

        partner = other;
        other.partner = this;

        isConversationLeader = true;
        other.isConversationLeader = false;

        state = SocialState.Travelling;
        other.state = SocialState.Travelling;

        hasArrived = false;
        other.hasArrived = false;

        travelElapsed = 0f;
        other.travelElapsed = 0f;

        conversationStartGameSecond = GetCurrentAbsoluteGameSecond();
        other.conversationStartGameSecond = conversationStartGameSecond;

        currentPartnerName = other.gameObject.name;
        other.currentPartnerName = gameObject.name;

        currentStatus = "Walking to conversation";
        other.currentStatus = "Walking to conversation";

        if (!movement.MoveTo(ownTarget))
        {
            CancelConversation("Could not find a path to conversation");
            return;
        }

        if (partner != other || other.partner != this)
            return;

        if (!other.movement.MoveTo(partnerTarget))
            CancelConversation("Could not find a path to conversation");
    }

    private void HandleMovementArrived()
    {
        if (state != SocialState.Travelling)
            return;

        hasArrived = true;
        TryBeginConversation();
    }

    private void HandlePathFailed()
    {
        if (state == SocialState.Travelling)
            CancelConversation("Path to conversation failed");
    }

    private void TryBeginConversation()
    {
        if (partner == null ||
            state != SocialState.Travelling ||
            !hasArrived ||
            !partner.hasArrived)
        {
            return;
        }

        state = SocialState.Conversing;
        partner.state = SocialState.Conversing;

        currentStatus = "Talking to " + partner.gameObject.name;
        partner.currentStatus = "Talking to " + gameObject.name;

        conversationStartGameSecond = GetCurrentAbsoluteGameSecond();
        partner.conversationStartGameSecond = conversationStartGameSecond;

        movement.Stop();
        partner.movement.Stop();

        FacePartner();
        partner.FacePartner();
    }

    private void HandleGameTimeAdvanced(long previousSecond, long currentSecond)
    {
        if (state != SocialState.Conversing || !isConversationLeader)
            return;

        if (currentSecond - conversationStartGameSecond >= conversationDurationGameSeconds)
            CompleteConversation();
    }

    private long GetCurrentAbsoluteGameSecond()
    {
        if (gameTimeManager == null)
            return 0;

        return (gameTimeManager.CurrentDay - 1) * 86400L +
               gameTimeManager.GetGameTimeSeconds();
    }

    private void FacePartner()
    {
        if (partner == null)
            return;

        Vector3 direction = partner.transform.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(direction.normalized, Vector3.up);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            facingRotationSpeed * Time.deltaTime);
    }

    private void CompleteConversation()
    {
        PilotSocialInteractionController other = partner;

        if (other == null)
        {
            ResetOwnState("Conversation partner unavailable");
            return;
        }

        needs.SatisfyNeed(
            UtilityNeeds.NeedType.Social,
            socialSatisfactionAmount);

        other.needs.SatisfyNeed(
            UtilityNeeds.NeedType.Social,
            other.socialSatisfactionAmount);

        partner = null;
        other.partner = null;

        ResetOwnState("Conversation complete");
        other.ResetOwnState("Conversation complete");
    }

    private void CancelConversation(string reason)
    {
        PilotSocialInteractionController other = partner;
        partner = null;

        ResetOwnState(reason);

        if (other != null && other.partner == this)
        {
            other.partner = null;
            other.ResetOwnState(reason);
        }
    }

    private void ResetOwnState(string status)
    {
        if (movement != null && movement.IsMoving)
            movement.Stop();

        state = SocialState.Idle;
        isConversationLeader = false;
        hasArrived = false;
        travelElapsed = 0f;
        conversationStartGameSecond = 0;
        currentPartnerName = string.Empty;
        currentStatus = status;
        nextSearchTime = Time.time + searchInterval;
    }

    private void OnValidate()
    {
        searchInterval = Mathf.Max(0.1f, searchInterval);
        partnerSearchRadius = Mathf.Max(0.1f, partnerSearchRadius);
        maximumSocialNeedValue = Mathf.Clamp(maximumSocialNeedValue, 0f, 100f);
        conversationDurationGameSeconds = Mathf.Max(1, conversationDurationGameSeconds);
        socialSatisfactionAmount = Mathf.Max(0f, socialSatisfactionAmount);
        conversationDistance = Mathf.Max(0.2f, conversationDistance);
        travelTimeout = Mathf.Max(1f, travelTimeout);
        facingRotationSpeed = Mathf.Max(1f, facingRotationSpeed);
    }
}
