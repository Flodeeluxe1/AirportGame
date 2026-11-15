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
        public float Score;
        public float TravelTime;
    }

    [Header("Decision")]
    [Min(0.1f)]
    [SerializeField] private float decisionInterval = 2f;
    [Min(1)]
    [SerializeField] private int shortlistSize = 4;
    [Min(0f)]
    [SerializeField] private float timeCostPerSecond = 0.02f;

    [Header("Queue")]
    [Min(0.05f)]
    [SerializeField] private float queuePositionRefreshInterval = 0.25f;

    [Header("Runtime")]
    [SerializeField] private string currentStatus = "Idle";
    [SerializeField] private string currentInteractionName;

    private readonly List<InteractionCandidate> shortlist =
        new List<InteractionCandidate>();

    private UtilityNeeds needs;
    private PedestrianMovementController movement;
    private InteractionProvider reservedProvider;
    private InteractionDefinition reservedInteraction;
    private InteractionProvider queuedProvider;
    private InteractionDefinition queuedInteraction;
    private Coroutine interactionRoutine;
    private float nextDecisionTime;
    private float nextQueuePositionRefresh;
    private Vector3 lastQueueDestination;

    public string CurrentStatus => currentStatus;

    private void Awake()
    {
        needs = GetComponent<UtilityNeeds>();
        movement = GetComponent<PedestrianMovementController>();
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

        ReleaseCurrentReservation();
        currentStatus = "Disabled";
    }

    private void Update()
    {
        if (needs == null || movement == null)
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

        if (Time.time < nextDecisionTime)
            return;

        nextDecisionTime = Time.time + decisionInterval;
        TryStartSelectedInteraction();
    }

    private void TryStartSelectedInteraction()
    {
        shortlist.Clear();

        InteractionProvider[] providers =
            FindObjectsByType<InteractionProvider>(FindObjectsSortMode.None);

        foreach (InteractionProvider provider in providers)
        {
            if (provider == null || !provider.InteractionsEnabled)
                continue;

            foreach (InteractionDefinition definition in provider.Interactions)
            {
                if (definition == null || !provider.CanOffer(definition))
                    continue;

                float utility = CalculateUtility(definition);
                if (utility <= 0f)
                    continue;

                float travelTime =
                    movement.EstimateTravelTime(provider.InteractionPosition);

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
                    Score = timeAdjustedScore,
                    TravelTime = travelTime
                });
            }
        }

        if (shortlist.Count == 0)
        {
            currentStatus = "No suitable available interactions";
            return;
        }

        InteractionCandidate selected = SelectWeightedCandidate();

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

        if (!movement.MoveTo(selected.Provider.InteractionPosition))
        {
            CancelCurrentInteraction("Could not find a path to interaction");
            return;
        }

        currentStatus = "Walking to " + currentInteractionName;
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

            Vector3 interactionPosition = queuedProvider.InteractionPosition;
            if (!movement.IsMoving &&
                Vector3.Distance(transform.position, interactionPosition) > 0.12f &&
                Time.time >= nextQueuePositionRefresh)
            {
                nextQueuePositionRefresh = Time.time + queuePositionRefreshInterval;
                lastQueueDestination = interactionPosition;

                if (!movement.MoveTo(interactionPosition))
                    CancelQueuedInteraction("Could not reach interaction");
                else
                    currentStatus = "Walking to " + currentInteractionName;
            }

            return;
        }

        reservedProvider = null;
        reservedInteraction = null;

        if (Time.time < nextQueuePositionRefresh)
            return;

        nextQueuePositionRefresh = Time.time + queuePositionRefreshInterval;
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

        Vector3 point = reservedProvider.InteractionPosition;
        float distance = Vector3.Distance(transform.position, point);

        if (distance > reservedInteraction.InteractionRange)
        {
            if (queuedProvider != null)
                CancelQueuedInteraction("Arrived outside interaction range");
            else
                CancelCurrentInteraction("Arrived outside interaction range");

            return;
        }

        currentStatus = "Performing " + reservedInteraction.DisplayName;
        interactionRoutine = StartCoroutine(PerformInteraction());
    }

    private IEnumerator PerformInteraction()
    {
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

            elapsed += Time.deltaTime;
            yield return null;
        }

        InteractionDefinition completedInteraction = reservedInteraction;

        foreach (InteractionDefinition.NeedEffect effect in completedInteraction.NeedEffects)
        {
            if (effect == null || effect.satisfactionAmount <= 0f)
                continue;

            needs.SatisfyNeed(effect.need, effect.satisfactionAmount);
        }

        currentStatus = "Completed " + completedInteraction.DisplayName;
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

        nextDecisionTime = Time.time + decisionInterval;
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
        nextDecisionTime = Time.time + decisionInterval;
    }

    private void CancelCurrentInteraction(string reason)
    {
        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }

        if (movement != null && movement.IsMoving)
            movement.Stop();

        ReleaseCurrentReservation();
        currentStatus = reason;
        nextDecisionTime = Time.time + decisionInterval;
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
        queuePositionRefreshInterval = Mathf.Max(0.05f, queuePositionRefreshInterval);
    }
}