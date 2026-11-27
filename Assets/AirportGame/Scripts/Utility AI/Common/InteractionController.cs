using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Selects available interactions according to the pedestrian's current needs,
/// joins provider queues when enabled, walks to the assigned position,
/// performs the interaction and applies its need effects.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(PedestrianMovementController))]
public class InteractionController : MonoBehaviour
{
    private struct InteractionCandidate
    {
        public InteractionProvider Provider;
        public InteractionDefinition Definition;
        public float Utility;
        public float Score;
        public float TravelTime;
        public float WaitingTime;
    }

    public sealed class DebugCandidateSnapshot
    {
        public InteractionProvider Provider { get; }
        public InteractionDefinition Definition { get; }
        public float Utility { get; }
        public float Score { get; }
        public float TravelTime { get; }
        public float WaitingTime { get; }
        public bool Selected { get; internal set; }

        public DebugCandidateSnapshot(
            InteractionProvider provider,
            InteractionDefinition definition,
            float utility,
            float score,
            float travelTime,
            float waitingTime)
        {
            Provider = provider;
            Definition = definition;
            Utility = utility;
            Score = score;
            TravelTime = travelTime;
            WaitingTime = waitingTime;
        }
    }

    [Header("Decision")]
    [Min(0.1f)]
    [SerializeField] private float decisionInterval = 2f;
    [Min(1)]
    [SerializeField] private int shortlistSize = 4;
    [Min(0f)]
    [SerializeField] private float timeCostPerSecond = 0.02f;
    [Min(1f)]
    [SerializeField] private float interactionSearchRadius = 100f;


    [Header("Room Preferences")]
    [Min(1f)]
    [SerializeField] private float pilotsLoungeUtilityMultiplier = 1.15f;

    [Header("Queue")]
    [Min(0.05f)]
    [SerializeField] private float queuePositionRefreshInterval = 0.25f;

    [Header("Runtime")]
    [SerializeField] private string currentStatus = "Idle";
    [SerializeField] private string currentInteractionName;

    private readonly List<InteractionCandidate> shortlist =
        new List<InteractionCandidate>();
    private readonly List<DebugCandidateSnapshot> debugCandidates =
        new List<DebugCandidateSnapshot>();
    private readonly List<InteractionProvider> nearbyProviders =
        new List<InteractionProvider>(64);

    private UtilityNeeds needs;
    private PedestrianMovementController movement;
    private PilotSocialInteractionController socialController;
    private PilotSocialSeatingController seatingController;
    private PilotGroupConversationController groupController;
    private RoomPurposeController roomPurposeController;
    private PedestrianAccessProfile accessProfile;
    private InteractionProvider reservedProvider;
    private InteractionDefinition reservedInteraction;
    private InteractionProvider queuedProvider;
    private InteractionDefinition queuedInteraction;
    private Coroutine interactionRoutine;
    private bool externallyControlled;
    private bool journeyInteractionsEnabled;
    private bool mandatoryInteractionActive;
    private bool exitingInteraction;
    private Vector3 journeyDestination;
    private PlacedBuilding journeyAllowedStartFootprint;
    private PlacedBuilding journeyAllowedTargetFootprint;
    private double nextDecisionTime;
    private double nextQueuePositionRefresh;
    private Vector3 lastQueueDestination;
    private readonly List<UtilityNeeds.NeedType> replenishingNeeds =
        new List<UtilityNeeds.NeedType>();


    public string CurrentStatus => currentStatus;
    public bool IsExternallyControlled => externallyControlled;
    public event Action<InteractionProvider, InteractionDefinition> MandatoryInteractionCompleted;
    public event Action<InteractionProvider, InteractionDefinition, string> MandatoryInteractionFailed;

    public bool IsHandlingInteraction =>
        (journeyInteractionsEnabled || mandatoryInteractionActive) &&
        !HasReachedJourneyDestination() &&
        (reservedProvider != null || queuedProvider != null || interactionRoutine != null);

    private bool HasReachedJourneyDestination()
    {
        if (!journeyInteractionsEnabled || movement == null || movement.IsMoving)
            return false;

        Vector3 current = transform.position;
        Vector3 destination = journeyDestination;
        current.y = 0f;
        destination.y = 0f;
        return (current - destination).sqrMagnitude <= 0.0625f;
    }

    /// <summary>Temporarily hands movement and decisions to a transport system without disabling visitor systems.</summary>
    public void SetExternallyControlled(bool controlled)
    {
        if (externallyControlled == controlled)
            return;

        externallyControlled = controlled;
        if (controlled)
        {
            journeyInteractionsEnabled = false;
            mandatoryInteractionActive = false;
            exitingInteraction = false;
            if (interactionRoutine != null)
            {
                StopCoroutine(interactionRoutine);
                interactionRoutine = null;
            }

            ResumeInteractionNeedDecay();
            ReleaseCurrentReservation();
            if (movement != null && movement.IsMoving)
                movement.Stop();
            currentStatus = "Externally controlled";
        }
        else
        {
            nextDecisionTime = GameTimeManager.SimulationTime;
            currentStatus = "Idle";
        }
    }
    /// <summary>Allows need-driven interactions to temporarily interrupt and then resume a mandatory passenger route.</summary>
    public void SetJourneyInteractions(
        bool enabled,
        Vector3 mandatoryDestination,
        PlacedBuilding allowedStartFootprint = null,
        PlacedBuilding allowedTargetFootprint = null)
    {
        journeyInteractionsEnabled = enabled;
        if (!enabled)
            return;

        externallyControlled = false;
        journeyDestination = mandatoryDestination;
        journeyAllowedStartFootprint = allowedStartFootprint;
        journeyAllowedTargetFootprint = allowedTargetFootprint;
        nextDecisionTime = GameTimeManager.SimulationTime;
    }

    public void StopJourneyInteractions()
    {
        journeyInteractionsEnabled = false;
    }

    /// <summary>
    /// Starts a specific journey-stage interaction, bypassing utility scoring
    /// while retaining the provider's normal reservation and queue behaviour.
    /// </summary>
    public bool BeginMandatoryInteraction(
        InteractionProvider provider,
        InteractionDefinition definition,
        PlacedBuilding allowedStartFootprint = null,
        PlacedBuilding allowedTargetFootprint = null)
    {
        if (externallyControlled || needs == null || movement == null ||
            provider == null || definition == null ||
            reservedProvider != null || queuedProvider != null ||
            interactionRoutine != null || !provider.CanOffer(definition))
        {
            return false;
        }

        journeyInteractionsEnabled = false;
        journeyAllowedStartFootprint = allowedStartFootprint;
        journeyAllowedTargetFootprint = allowedTargetFootprint;
        mandatoryInteractionActive = true;

        if (provider.Queue != null && provider.Queue.QueueEnabled)
        {
            float travelTime = movement.EstimateTravelTime(provider);
            if (float.IsInfinity(travelTime) || float.IsNaN(travelTime) ||
                !provider.Queue.TryJoin(this, definition, travelTime))
            {
                FailMandatoryInteraction(provider, definition,
                    "Could not join mandatory interaction queue");
                return false;
            }

            queuedProvider = provider;
            queuedInteraction = definition;
            currentInteractionName = definition.DisplayName;
            nextQueuePositionRefresh = 0f;
            UpdateQueueState();
            return true;
        }

        if (!provider.TryReserve(this, definition))
        {
            FailMandatoryInteraction(provider, definition,
                "Mandatory interaction reservation unavailable");
            return false;
        }

        reservedProvider = provider;
        reservedInteraction = definition;
        currentInteractionName = definition.DisplayName;

        if (!movement.TryMoveTo(provider))
        {
            FailMandatoryInteraction(provider, definition,
                "No path to mandatory interaction");
            return false;
        }

        currentStatus = "Walking to " + currentInteractionName;
        return true;
    }

    private void FailMandatoryInteraction(
        InteractionProvider provider,
        InteractionDefinition definition,
        string reason)
    {
        bool wasMandatory = mandatoryInteractionActive;
        mandatoryInteractionActive = false;
        if (queuedProvider != null && queuedProvider.Queue != null)
            queuedProvider.Queue.Leave(this);
        else if (reservedProvider != null && reservedInteraction != null)
            reservedProvider.ReleaseReservation(this, reservedInteraction);

        queuedProvider = null;
        queuedInteraction = null;
        reservedProvider = null;
        reservedInteraction = null;
        currentInteractionName = string.Empty;
        currentStatus = reason;

        if (wasMandatory)
            MandatoryInteractionFailed?.Invoke(provider, definition, reason);
    }

    private void ResumeJourneyRoute()
    {
        if (!journeyInteractionsEnabled || movement == null ||
            reservedProvider != null || queuedProvider != null ||
            interactionRoutine != null)
            return;

        if (!movement.MoveToAllowingOccupiedEndpoints(
                journeyDestination,
                journeyAllowedStartFootprint,
                journeyAllowedTargetFootprint))
        {
            journeyInteractionsEnabled = false;
            currentStatus = "Mandatory journey route could not be resumed";
        }
        else
        {
            currentStatus = "Resuming passenger journey";
        }
    }

    public string CurrentInteractionName => currentInteractionName;
    public IReadOnlyList<DebugCandidateSnapshot> DebugCandidates => debugCandidates;

    public string DebugActivity
    {
        get
        {
            if (groupController != null && groupController.IsBusy)
                return groupController.CurrentStatus;
            if (seatingController != null && seatingController.IsBusy)
                return seatingController.CurrentStatus;
            if (socialController != null && socialController.IsBusy)
                return socialController.CurrentStatus;
            if (reservedProvider != null || queuedProvider != null ||
                interactionRoutine != null)
                return string.IsNullOrEmpty(currentInteractionName)
                    ? currentStatus
                    : currentInteractionName;
            if (movement != null && movement.IsMoving)
                return movement.MovementStatus;
            return "None";
        }
    }

    public string DebugActivitySource
    {
        get
        {
            if (groupController != null && groupController.IsBusy)
                return "Group Conversation Controller";
            if (seatingController != null && seatingController.IsBusy)
                return "Seating Controller";
            if (socialController != null && socialController.IsBusy)
                return "Social Interaction Controller";
            if (reservedProvider != null || queuedProvider != null ||
                interactionRoutine != null)
                return "Interaction Controller";
            if (movement != null && movement.IsMoving)
                return "Pedestrian Movement Controller";
            return "None";
        }
    }

    public string DebugActivityStatus
    {
        get
        {
            if (groupController != null && groupController.IsBusy)
                return groupController.CurrentStatus;
            if (seatingController != null && seatingController.IsBusy)
                return seatingController.CurrentStatus;
            if (socialController != null && socialController.IsBusy)
                return socialController.CurrentStatus;
            if (reservedProvider != null || queuedProvider != null ||
                interactionRoutine != null)
                return currentStatus;
            if (movement != null && movement.IsMoving)
                return movement.MovementStatus;
            return currentStatus;
        }
    }

    public string DebugDecisionStatus
    {
        get
        {
            if (groupController != null && groupController.IsBusy)
                return "Paused: pedestrian is in a group conversation.";
            if (seatingController != null && seatingController.IsBusy)
                return "Paused: pilot is participating in a seated activity.";
            if (socialController != null && socialController.IsBusy)
                return "Paused: pilot is socializing.";
            if (queuedProvider != null)
                return "Paused: pilot is handling an interaction queue.";
            if (reservedProvider != null || interactionRoutine != null)
                return "Paused: pedestrian has an active interaction.";
            if (movement != null && movement.IsMoving)
                return "Paused: pilot is moving.";
            if (currentStatus == "No suitable available interactions")
                return "Latest decision found no suitable available interactions.";
            return "No decision shortlist recorded yet.";
        }
    }

    public bool CanPauseForSocial =>
        reservedProvider == null &&
        queuedProvider == null &&
        interactionRoutine == null &&
        (socialController == null || !socialController.IsBusy) &&
        (seatingController == null || !seatingController.IsBusy) &&
        (groupController == null || !groupController.IsBusy) &&
        movement != null &&
        !movement.IsMoving;

    private void Awake()
    {
        needs = GetComponent<UtilityNeeds>();
        movement = GetComponent<PedestrianMovementController>();
        socialController = GetComponent<PilotSocialInteractionController>();
        seatingController = GetComponent<PilotSocialSeatingController>();
        groupController = GetComponent<PilotGroupConversationController>();
        accessProfile = GetComponent<PedestrianAccessProfile>();
        roomPurposeController = FindAnyObjectByType<RoomPurposeController>();
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
    }

    private void OnDisable()
    {
        exitingInteraction = false;
        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            movement.PathFailed -= HandlePathFailed;
            movement.Stop();
        }

        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }

        ResumeInteractionNeedDecay();
        mandatoryInteractionActive = false;
        ReleaseCurrentReservation();
        currentStatus = "Disabled";
    }

    private void Update()
    {
        if (externallyControlled || needs == null || movement == null)
            return;

        if ((socialController != null && socialController.IsBusy) ||
            (seatingController != null && seatingController.IsBusy) ||
            (groupController != null && groupController.IsBusy))
            return;

        if (queuedProvider != null)
        {
            UpdateQueueState();
            return;
        }

        if (reservedProvider != null && !reservedProvider.isActiveAndEnabled)
        {
            CancelCurrentInteraction("Interaction provider became unavailable");
            return;
        }

        if (reservedInteraction != null ||
            (movement.IsMoving && !journeyInteractionsEnabled))
            return;

        if (GameTimeManager.SimulationTime < nextDecisionTime)
            return;

        nextDecisionTime = GameTimeManager.SimulationTime + decisionInterval;
        TryStartSelectedInteraction();
    }

    /// <summary>
    /// Returns true when at least one available interaction can improve a current need
    /// and the pedestrian has a valid path to its provider.
    /// </summary>
    public bool HasSuitableAvailableInteraction()
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        if (needs == null || movement == null)
            return false;

        UtilityAISpatialRegistry<InteractionProvider>.GetNearby(
            transform.position, interactionSearchRadius, nearbyProviders);

        for (int providerIndex = 0; providerIndex < nearbyProviders.Count; providerIndex++)
        {
            InteractionProvider provider = nearbyProviders[providerIndex];
            if (provider == null || !provider.InteractionsEnabled ||
                (roomPurposeController != null &&
                 !roomPurposeController.IsPositionAccessibleToPedestrian(
                     provider.InteractionPosition, accessProfile)))
                continue;

            bool travelTimeCalculated = false;
            float providerTravelTime = float.PositiveInfinity;

            foreach (InteractionDefinition definition in provider.Interactions)
            {
                if (definition == null || !provider.CanOffer(definition) ||
                    CalculateUtility(definition) <= 0f)
                    continue;

                if (!travelTimeCalculated)
                {
                    travelTimeCalculated = true;
                    providerTravelTime = movement.EstimateTravelTime(provider);
                }

                if (!float.IsInfinity(providerTravelTime) &&
                    !float.IsNaN(providerTravelTime))
                {
                    return true;
                }
            }
        }

        return false;
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("HasSuitableAvailableInteraction", performanceTimingStart);
        }
}

    /// <summary>
    /// True while no utility interaction or social activity currently owns this pedestrian.
    /// </summary>
    public bool CanWander =>
        reservedProvider == null &&
        queuedProvider == null &&
        interactionRoutine == null &&
        (socialController == null || !socialController.IsBusy) &&
        (seatingController == null || !seatingController.IsBusy) &&
        (groupController == null || !groupController.IsBusy);

    private void TryStartSelectedInteraction()
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        shortlist.Clear();
        debugCandidates.Clear();

        UtilityAISpatialRegistry<InteractionProvider>.GetNearby(
            transform.position, interactionSearchRadius, nearbyProviders);

        for (int providerIndex = 0; providerIndex < nearbyProviders.Count; providerIndex++)
        {
            InteractionProvider provider = nearbyProviders[providerIndex];
            if (provider == null || !provider.InteractionsEnabled ||
                (roomPurposeController != null &&
                 !roomPurposeController.IsPositionAccessibleToPedestrian(
                     provider.InteractionPosition, accessProfile)))
                continue;

            bool travelTimeCalculated = false;
            float providerTravelTime = float.PositiveInfinity;

            foreach (InteractionDefinition definition in provider.Interactions)
            {
                if (definition == null || !provider.CanOffer(definition))
                    continue;

                float utility = CalculateUtility(definition);
                if (utility <= 0f)
                    continue;

                Vector3 interactionPosition = provider.InteractionPosition;

                if (roomPurposeController != null &&
                    roomPurposeController.IsPositionInPilotsLounge(interactionPosition))
                {
                    utility *= pilotsLoungeUtilityMultiplier;
                }

                if (!travelTimeCalculated)
                {
                    travelTimeCalculated = true;
                    providerTravelTime = movement.EstimateTravelTime(provider);
                }

                float travelTime = providerTravelTime;
                if (float.IsInfinity(travelTime) || float.IsNaN(travelTime))
                    continue;

                float waitingTime = provider.Queue != null &&
                                    provider.Queue.QueueEnabled
                    ? provider.Queue.GetEstimatedWaitTime()
                    : 0f;

                float totalTime =
                    travelTime + waitingTime + definition.DurationSeconds;

                float departureUrgency = needs.GetUrgency(UtilityNeeds.NeedType.DepartureUrgency) / 100f;
                float departureInfluence = Mathf.Clamp01(
                    departureUrgency * needs.GetUtilityWeight(UtilityNeeds.NeedType.DepartureUrgency));
                float urgencyAdjustedTimeCost = timeCostPerSecond *
                    (1f + departureInfluence * 4f);

                float timeAdjustedScore =
                    utility / (1f + totalTime * urgencyAdjustedTimeCost);

                if (timeAdjustedScore <= 0f)
                    continue;

                AddToShortlist(new InteractionCandidate
                {
                    Provider = provider,
                    Definition = definition,
                    Utility = utility,
                    Score = timeAdjustedScore,
                    TravelTime = travelTime,
                    WaitingTime = waitingTime
                });
            }
        }

        if (shortlist.Count == 0)
        {
            currentStatus = "No suitable available interactions";
            return;
        }

        foreach (InteractionCandidate candidate in shortlist)
        {
            debugCandidates.Add(new DebugCandidateSnapshot(
                candidate.Provider,
                candidate.Definition,
                candidate.Utility,
                candidate.Score,
                candidate.TravelTime,
                candidate.WaitingTime));
        }

        InteractionCandidate selected = SelectWeightedCandidate();
        for (int i = 0; i < debugCandidates.Count; i++)
        {
            DebugCandidateSnapshot snapshot = debugCandidates[i];
            if (snapshot.Provider == selected.Provider &&
                snapshot.Definition == selected.Definition)
            {
                snapshot.Selected = true;
                break;
            }
        }

        if (selected.Provider == null || selected.Definition == null)
        {
            currentStatus = "Selected interaction became unavailable";
            return;
        }

        if (selected.Provider.Queue != null &&
            selected.Provider.Queue.QueueEnabled)
        {
            if (!selected.Provider.Queue.TryJoin(
                    this,
                    selected.Definition,
                    selected.TravelTime))
            {
                currentStatus = "Could not join interaction queue";
                return;
            }

            queuedProvider = selected.Provider;
            queuedInteraction = selected.Definition;
            currentInteractionName = selected.Definition.DisplayName;
            nextQueuePositionRefresh = 0f;
            UpdateQueueState();
            return;
        }

        if (!selected.Provider.TryReserve(this, selected.Definition))
        {
            currentStatus = "Interaction reservation unavailable";
            return;
        }

        reservedProvider = selected.Provider;
        reservedInteraction = selected.Definition;
        currentInteractionName = selected.Definition.DisplayName;

        if (!movement.MoveTo(selected.Provider))
        {
            CancelCurrentInteraction("Could not find a path to interaction");
            return;
        }

        currentStatus = "Walking to " + currentInteractionName;
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("TryStartSelectedInteraction", performanceTimingStart);
        }
}

    private void AddToShortlist(InteractionCandidate candidate)
    {
        int insertIndex = shortlist.Count;

        for (int i = 0; i < shortlist.Count; i++)
        {
            if (candidate.Score > shortlist[i].Score)
            {
                insertIndex = i;
                break;
            }
        }

        shortlist.Insert(insertIndex, candidate);

        int maximumCandidates = Mathf.Max(1, shortlistSize);
        if (shortlist.Count > maximumCandidates)
            shortlist.RemoveAt(shortlist.Count - 1);
    }

    private InteractionCandidate SelectWeightedCandidate()
    {
        float totalWeight = 0f;

        foreach (InteractionCandidate candidate in shortlist)
            totalWeight += candidate.Score;

        if (totalWeight <= 0f)
            return shortlist[0];

        float roll = UnityEngine.Random.value * totalWeight;

        foreach (InteractionCandidate candidate in shortlist)
        {
            roll -= candidate.Score;

            if (roll <= 0f)
                return candidate;
        }

        return shortlist[shortlist.Count - 1];
    }

    private float CalculateUtility(InteractionDefinition definition)
    {
        float score = 0f;

        foreach (InteractionDefinition.NeedEffect effect in definition.NeedEffects)
        {
            if (effect == null || effect.satisfactionAmount <= 0f)
                continue;

            float currentNeedValue = needs.GetValue(effect.need);

            if (currentNeedValue >= effect.maximumNeedValueToConsider)
                continue;

            float urgency = needs.GetUrgency(effect.need);
            float utilityWeight = needs.GetUtilityWeight(effect.need);
            float potentialSatisfaction = Mathf.Min(100f, effect.satisfactionAmount);
            score += urgency * utilityWeight * potentialSatisfaction / 100f;
        }

        if (score <= 0f)
            return 0f;


        foreach (InteractionDefinition.NeedConflict conflict in definition.NeedConflicts)
        {
            if (conflict == null || conflict.penaltyAtMaximumUrgency <= 0f)
                continue;

            float conflictingUrgency = needs.GetUrgency(conflict.need) / 100f;
            float penalty = conflictingUrgency * conflict.penaltyAtMaximumUrgency;
            score *= 1f - Mathf.Clamp01(penalty);
        }

        return score;
    }

    private void UpdateQueueState()
    {
        if (queuedProvider == null || queuedInteraction == null ||
            !queuedProvider.isActiveAndEnabled ||
            queuedProvider.Queue == null ||
            !queuedProvider.Queue.QueueEnabled ||
            !queuedProvider.Queue.IsQueued(this))
        {
            CancelQueuedInteraction("Interaction queue became unavailable");
            return;
        }

        InteractionQueue queue = queuedProvider.Queue;

        if (queue.IsActive(this))
        {
            reservedProvider = queuedProvider;
            reservedInteraction = queuedInteraction;

            // The pilot is already using the provider at its AnimationPoint.
            // Do not send it back to the InteractionPoint until the interaction
            // routine has completed and explicitly returns it there.
            if (interactionRoutine != null)
                return;

            if (!movement.IsMoving &&
                !movement.IsAtInteractionApproach(queuedProvider) &&
                GameTimeManager.SimulationTime >= nextQueuePositionRefresh)
            {
                nextQueuePositionRefresh = GameTimeManager.SimulationTime + queuePositionRefreshInterval;
                lastQueueDestination = queuedProvider.InteractionPosition;

                if (!movement.MoveTo(queuedProvider))
                    CancelQueuedInteraction("Could not reach interaction");
                else
                    currentStatus = "Walking to " + currentInteractionName;
            }

            return;
        }

        reservedProvider = null;
        reservedInteraction = null;

        if (GameTimeManager.SimulationTime < nextQueuePositionRefresh)
            return;

        nextQueuePositionRefresh = GameTimeManager.SimulationTime + queuePositionRefreshInterval;
        Vector3 queuePosition = queue.GetQueuePosition(this);

        bool destinationChanged =
            Vector3.Distance(lastQueueDestination, queuePosition) > 0.15f;

        if (!movement.IsMoving && destinationChanged)
        {
            lastQueueDestination = queuePosition;

            if (!movement.MoveTo(queuePosition))
            {
                CancelQueuedInteraction("Could not reach queue position");
                return;
            }
        }
        else if (movement.IsMoving && destinationChanged &&
                 Vector3.Distance(movement.Destination, queuePosition) > 0.15f)
        {
            lastQueueDestination = queuePosition;

            if (!movement.MoveTo(queuePosition))
            {
                CancelQueuedInteraction("Could not reach updated queue position");
                return;
            }
        }

        currentStatus = "Waiting in queue for " + currentInteractionName;
    }

    private void HandleMovementArrived()
    {
        // The arrival at an optional post-interaction exit point completes the
        // interaction route; it must not start the interaction a second time.
        if (exitingInteraction)
            return;

        if (queuedProvider != null)
        {
            if (!queuedProvider.Queue.IsActive(this))
                return;

            reservedProvider = queuedProvider;
            reservedInteraction = queuedInteraction;
        }

        if (reservedProvider == null || reservedInteraction == null)
            return;

        // A passenger can arrive at its mandatory transport destination while
        // an optional interaction is reserved. Only treat an Arrived event as
        // interaction arrival when the movement controller confirms that the
        // passenger reached this provider's approach point.
        if (journeyInteractionsEnabled &&
            !movement.IsAtInteractionApproach(reservedProvider))
            return;

        float distance =
            Vector3.Distance(transform.position, movement.Destination);

        if (distance > reservedInteraction.InteractionRange)
        {
            if (queuedProvider != null)
                CancelQueuedInteraction("Arrived outside interaction range");
            else
                CancelCurrentInteraction("Arrived outside interaction range");

            return;
        }

        // Move from the reachable approach point to the object's animation position.
        // This point may intentionally be inside the object's occupied footprint.
        movement.AlignToAnimationPoint(reservedProvider);

        currentStatus = "Performing " + reservedInteraction.DisplayName;
        interactionRoutine = StartCoroutine(PerformInteraction());
    }

    private IEnumerator PerformInteraction()
    {
        PauseInteractionNeedDecay();
        float duration = reservedInteraction.DurationSeconds;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (reservedProvider == null ||
                !reservedProvider.isActiveAndEnabled ||
                reservedInteraction == null)
            {
                CancelCurrentInteraction("Interaction interrupted");
                yield break;
            }

            elapsed += GameTimeManager.SimulationDeltaTime;
            yield return null;
        }

        InteractionDefinition completedInteraction = reservedInteraction;
        InteractionProvider completedProvider = reservedProvider;
        ResumeInteractionNeedDecay();

        if (completedInteraction == null || needs == null)
        {
            CancelCurrentInteraction("Interaction data became unavailable");
            yield break;
        }

        IReadOnlyList<InteractionDefinition.NeedEffect> needEffects =
            completedInteraction.NeedEffects;

        if (needEffects != null)
        {
            foreach (InteractionDefinition.NeedEffect effect in needEffects)
            {
                if (effect == null || Mathf.Approximately(effect.satisfactionAmount, 0f))
                    continue;

                needs.ModifyNeed(effect.need, effect.satisfactionAmount);
            }
        }


        currentStatus = "Completed " + completedInteraction.DisplayName;

        // Some interactions (for example, body security) are pass-through
        // stations. Walk to their optional exit point before releasing the
        // reservation or reporting the mandatory interaction as complete.
        if (completedProvider != null &&
            completedProvider.HasAnimationExitPoint &&
            movement != null)
        {
            exitingInteraction = true;
            if (!movement.TryMoveTo(completedProvider.AnimationExitPosition))
            {
                exitingInteraction = false;
                CancelCurrentInteraction("Could not find a path through interaction exit");
                yield break;
            }

            currentStatus = "Walking through " + completedInteraction.DisplayName;
            while (movement != null && movement.IsMoving)
                yield return null;

            if (movement == null || !movement.HasArrived)
            {
                exitingInteraction = false;
                CancelCurrentInteraction("Could not reach interaction exit point");
                yield break;
            }

            exitingInteraction = false;
        }
        else if (completedProvider != null && movement != null)
        {
            // Preserve the existing return-to-approach behaviour by default.
            movement.AlignToInteractionPoint(completedProvider);
        }

        interactionRoutine = null;

        if (queuedProvider != null && queuedProvider.Queue != null)
        {
            queuedProvider.Queue.Complete(this);
            queuedProvider = null;
            queuedInteraction = null;
            reservedProvider = null;
            reservedInteraction = null;
            currentInteractionName = string.Empty;
        }
        else
        {
            ReleaseCurrentReservation();
        }

        nextDecisionTime = GameTimeManager.SimulationTime + decisionInterval;

        if (mandatoryInteractionActive)
        {
            mandatoryInteractionActive = false;
            MandatoryInteractionCompleted?.Invoke(
                completedProvider, completedInteraction);
            yield break;
        }

        ResumeJourneyRoute();
    }

    private void PauseInteractionNeedDecay()
    {
        ResumeInteractionNeedDecay();

        if (needs == null || reservedInteraction == null)
            return;

        IReadOnlyList<InteractionDefinition.NeedEffect> effects =
            reservedInteraction.NeedEffects;

        if (effects == null)
            return;

        foreach (InteractionDefinition.NeedEffect effect in effects)
        {
            if (effect == null || effect.satisfactionAmount <= 0f ||
                replenishingNeeds.Contains(effect.need))
                continue;

            needs.PauseNeedDecay(effect.need);
            replenishingNeeds.Add(effect.need);
        }

    }

    private void ResumeInteractionNeedDecay()
    {
        if (needs != null)
        {
            foreach (UtilityNeeds.NeedType need in replenishingNeeds)
                needs.ResumeNeedDecay(need);
        }

        replenishingNeeds.Clear();
    }

    private void HandlePathFailed()
    {
        if (queuedProvider != null)
            CancelQueuedInteraction("Path to queue position failed");
        else if (reservedInteraction != null)
            CancelCurrentInteraction("Path to interaction failed");
    }

    private void CancelQueuedInteraction(string reason)
    {
        bool wasMandatory = mandatoryInteractionActive;
        InteractionProvider failedProvider = queuedProvider;
        InteractionDefinition failedDefinition = queuedInteraction;
        mandatoryInteractionActive = false;
        ResumeInteractionNeedDecay();

        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }

        if (movement != null && movement.IsMoving)
            movement.Stop();

        if (queuedProvider != null && queuedProvider.Queue != null)
            queuedProvider.Queue.Leave(this);

        queuedProvider = null;
        queuedInteraction = null;
        reservedProvider = null;
        reservedInteraction = null;
        currentInteractionName = string.Empty;
        currentStatus = reason;
        nextDecisionTime = GameTimeManager.SimulationTime + decisionInterval;
        if (wasMandatory)
            MandatoryInteractionFailed?.Invoke(failedProvider, failedDefinition, reason);
        else
            ResumeJourneyRoute();
    }

    private void CancelCurrentInteraction(string reason)
    {
        exitingInteraction = false;
        bool wasMandatory = mandatoryInteractionActive;
        InteractionProvider failedProvider = reservedProvider;
        InteractionDefinition failedDefinition = reservedInteraction;
        mandatoryInteractionActive = false;
        ResumeInteractionNeedDecay();

        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }

        if (movement != null && movement.IsMoving)
            movement.Stop();

        ReleaseCurrentReservation();
        currentStatus = reason;
        nextDecisionTime = GameTimeManager.SimulationTime + decisionInterval;
        if (wasMandatory)
            MandatoryInteractionFailed?.Invoke(failedProvider, failedDefinition, reason);
        else
            ResumeJourneyRoute();
    }

    private void ReleaseCurrentReservation()
    {
        if (queuedProvider != null && queuedProvider.Queue != null)
            queuedProvider.Queue.Leave(this);
        else if (reservedProvider != null && reservedInteraction != null)
            reservedProvider.ReleaseReservation(this, reservedInteraction);

        queuedProvider = null;
        queuedInteraction = null;
        reservedProvider = null;
        reservedInteraction = null;
        currentInteractionName = string.Empty;
    }

    private void OnValidate()
    {
        decisionInterval = Mathf.Max(0.1f, decisionInterval);
        shortlistSize = Mathf.Max(1, shortlistSize);
        timeCostPerSecond = Mathf.Max(0f, timeCostPerSecond);
        interactionSearchRadius = Mathf.Max(1f, interactionSearchRadius);
        queuePositionRefreshInterval = Mathf.Max(0.05f, queuePositionRefreshInterval);
    }
}