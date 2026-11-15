using System.Collections;
using UnityEngine;

/// <summary>
/// Selects available interactions according to the pilot's current needs,
/// walks to the provider's interaction point, performs the interaction,
/// applies its need effects and releases the reservation.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(PedestrianMovementController))]
public class PilotInteractionController : MonoBehaviour
{
    [Header("Decision")]
    [Min(0.1f)]
    [SerializeField] private float decisionInterval = 2f;

    [Header("Runtime")]
    [SerializeField] private string currentStatus = "Idle";
    [SerializeField] private string currentInteractionName;

    private UtilityNeeds needs;
    private PedestrianMovementController movement;
    private InteractionProvider reservedProvider;
    private InteractionDefinition reservedInteraction;
    private Coroutine interactionRoutine;
    private float nextDecisionTime;

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
        TryStartBestInteraction();
    }

    private void TryStartBestInteraction()
    {
        InteractionProvider[] providers =
            FindObjectsByType<InteractionProvider>(FindObjectsSortMode.None);

        InteractionProvider bestProvider = null;
        InteractionDefinition bestInteraction = null;
        float bestScore = 0f;

        foreach (InteractionProvider provider in providers)
        {
            if (provider == null || !provider.InteractionsEnabled)
                continue;

            foreach (InteractionDefinition definition in provider.Interactions)
            {
                if (definition == null || !provider.CanOffer(definition))
                    continue;

                float score = CalculateUtility(definition);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestProvider = provider;
                    bestInteraction = definition;
                }
            }
        }

        if (bestProvider == null || bestInteraction == null)
        {
            currentStatus = "No suitable available interactions";
            return;
        }

        if (!bestProvider.TryReserve(this, bestInteraction))
        {
            currentStatus = "Interaction reservation unavailable";
            return;
        }

        reservedProvider = bestProvider;
        reservedInteraction = bestInteraction;
        currentInteractionName = bestInteraction.DisplayName;

        Vector3 destination = bestProvider.InteractionPosition;

        if (!movement.MoveTo(destination))
        {
            CancelCurrentInteraction("Could not find a path to interaction");
            return;
        }

        currentStatus = "Walking to " + currentInteractionName;
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

    private void HandleMovementArrived()
    {
        if (reservedProvider == null || reservedInteraction == null)
            return;

        Vector3 point = reservedProvider.InteractionPosition;
        float distance = Vector3.Distance(transform.position, point);

        if (distance > reservedInteraction.InteractionRange)
        {
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
        ReleaseCurrentReservation();
        nextDecisionTime = Time.time + decisionInterval;
    }

    private void HandlePathFailed()
    {
        if (reservedInteraction != null)
            CancelCurrentInteraction("Path to interaction failed");
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
        if (reservedProvider != null && reservedInteraction != null)
            reservedProvider.ReleaseReservation(this, reservedInteraction);

        reservedProvider = null;
        reservedInteraction = null;
        currentInteractionName = string.Empty;
    }
}