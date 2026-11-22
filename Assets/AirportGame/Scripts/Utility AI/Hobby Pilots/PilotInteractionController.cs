using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Selects available interactions according to the pilot's current needs,
/// joins provider queues when enabled, walks to the assigned position,
/// performs the interaction and applies its need effects.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(PedestrianMovementController))]
public class PilotInteractionController : MonoBehaviour
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
    private double nextDecisionTime;
    private double nextQueuePositionRefresh;
    private Vector3 lastQueueDestination;
    private readonly List<UtilityNeeds.NeedType> replenishingNeeds =
        new List<UtilityNeeds.NeedType>();


    public string CurrentStatus => currentStatus;
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
                return "Pilot Interaction Controller";
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
                return "Paused: pilot is in a group conversation.";
            if (seatingController != null && seatingController.IsBusy)
                return "Paused: pilot is participating in a seated activity.";
            if (socialController != null && socialController.IsBusy)
                return "Paused: pilot is socializing.";
            if (queuedProvider != null)
                return "Paused: pilot is handling an interaction queue.";
            if (reservedProvider != null || interactionRoutine != null)
                return "Paused: pilot has an active interaction.";
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
        ReleaseCurrentReservation();
        currentStatus = "Disabled";
    }

    private void Update()
    {
        if (needs == null || movement == null)
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

        if (reservedInteraction != null || movement.IsMoving)
            return;

        if (GameTimeManager.SimulationTime < nextDecisionTime)
            return;

        nextDecisionTime = GameTimeManager.SimulationTime + decisionInterval;
        TryStartSelectedInteraction();
    }

    /// <summary>
    /// Returns true when at least one available interaction can improve a current need
    /// and the pilot has a valid path to its provider.
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
    /// True while no utility interaction or social activity currently owns this pilot.
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

                float timeAdjustedScore =
                    utility / (1f + totalTime * timeCostPerSecond);

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

        float roll = Random.value * totalWeight;

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
        if (queuedProvider != null)
        {
            if (!queuedProvider.Queue.IsActive(this))
                return;

            reservedProvider = queuedProvider;
            reservedInteraction = queuedInteraction;
        }

        if (reservedProvider == null || reservedInteraction == null)
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
                if (effect == null || effect.satisfactionAmount <= 0f)
                    continue;

                needs.SatisfyNeed(effect.need, effect.satisfactionAmount);
            }
        }

        currentStatus = "Completed " + completedInteraction.DisplayName;
        interactionRoutine = null;

        // The animation point may be inside the object's blocked footprint.
        // Return to the reachable approach point before releasing the slot.
        InteractionProvider completedProvider = reservedProvider;
        if (completedProvider != null && movement != null)
            movement.AlignToInteractionPoint(completedProvider);

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
    }

    private void CancelCurrentInteraction(string reason)
    {
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