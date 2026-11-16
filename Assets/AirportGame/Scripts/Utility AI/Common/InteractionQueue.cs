using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a single-file waiting queue for the interactions offered by one provider.
/// The first agent reserves the interaction while walking to it and using it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InteractionProvider))]
public class InteractionQueue : MonoBehaviour
{
    private sealed class QueueEntry
    {
        public MonoBehaviour Agent;
        public InteractionDefinition Definition;
        public float EstimatedTravelTime;
    }

    [Header("Queue Settings")]
    [SerializeField] private bool queueEnabled = true;
    [Min(0)]
    [SerializeField] private int maximumWaitingAgents = 5;
    [Min(0.25f)]
    [SerializeField] private float queueSpacing = 1f;
    [Tooltip("Optional first waiting position. Further positions extend along Queue Direction.")]
    [SerializeField] private Transform firstQueuePosition;
    [Tooltip("Direction in which the queue extends. Defaults to this object's forward direction.")]
    [SerializeField] private Transform queueDirection;

    private readonly List<QueueEntry> entries = new List<QueueEntry>();
    private InteractionProvider provider;
    private QueueEntry activeEntry;
    private float activeStartTime;
    private float activeEstimatedTotalTime;
    private bool queuePositionResolved;

    public bool QueueEnabled => queueEnabled && isActiveAndEnabled;
    public int WaitingCount => Mathf.Max(0, entries.Count - (activeEntry != null ? 1 : 0));
    public int MaximumWaitingAgents => maximumWaitingAgents;

    private void Awake()
    {
        provider = GetComponent<InteractionProvider>();
        ResolveQueuePosition();
    }

    private void ResolveQueuePosition()
    {
        if (queuePositionResolved || firstQueuePosition != null)
        {
            queuePositionResolved = true;
            return;
        }

        queuePositionResolved = true;

        Transform[] childTransforms = GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < childTransforms.Length; i++)
        {
            if (childTransforms[i] != transform &&
                childTransforms[i].name == "First Queue Position")
            {
                firstQueuePosition = childTransforms[i];
                return;
            }
        }
    }

    private void Update()
    {
        CleanupInvalidEntries();

        if (activeEntry == null && entries.Count > 0)
            TryBeginNextEntry();
    }

    public bool CanAccept(InteractionDefinition definition)
    {
        if (!queueEnabled || !isActiveAndEnabled ||
            definition == null || provider == null)
        {
            return false;
        }

        bool providerOffersDefinition = false;
        IReadOnlyList<InteractionDefinition> offered = provider.Interactions;

        for (int i = 0; i < offered.Count; i++)
        {
            if (offered[i] == definition)
            {
                providerOffersDefinition = true;
                break;
            }
        }

        if (!providerOffersDefinition)
            return false;

        CleanupInvalidEntries();

        if (activeEntry == null && entries.Count == 0)
            return true;

        return WaitingCount < maximumWaitingAgents;
    }

    /// <summary>
    /// Adds an agent to the end of the queue. If the provider is free,
    /// the agent immediately becomes the active user and reserves the slot.
    /// </summary>
    public bool TryJoin(
        MonoBehaviour agent,
        InteractionDefinition definition,
        float estimatedTravelTime)
    {
        if (agent == null || definition == null || !CanAccept(definition))
            return false;

        if (FindEntry(agent) != null)
            return true;

        entries.Add(new QueueEntry
        {
            Agent = agent,
            Definition = definition,
            EstimatedTravelTime = Mathf.Max(0f, estimatedTravelTime)
        });

        if (activeEntry == null)
            TryBeginNextEntry();

        return true;
    }

    public bool IsQueued(MonoBehaviour agent)
    {
        return FindEntry(agent) != null;
    }

    public bool IsActive(MonoBehaviour agent)
    {
        return activeEntry != null && activeEntry.Agent == agent;
    }

    /// <summary>
    /// Returns the assigned world position for an active user or waiting agent.
    /// </summary>
    public Vector3 GetQueuePosition(MonoBehaviour agent)
    {
        int index = GetEntryIndex(agent);
        if (index < 0 || provider == null)
            return transform.position;

        if (IsActive(agent))
            return provider.InteractionPosition;

        bool hasActiveUser = activeEntry != null;
        int positionBehindFront = hasActiveUser ? index - 1 : index;

        Vector3 direction = queueDirection != null
            ? queueDirection.forward
            : transform.forward;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            direction = Vector3.forward;

        direction.Normalize();

        ResolveQueuePosition();

        if (firstQueuePosition != null)
        {
            return firstQueuePosition.position +
                   direction * queueSpacing * Mathf.Max(0, positionBehindFront);
        }

        // Without a dedicated queue marker, place the first waiting agent
        // one queue spacing beyond the provider's interaction point.
        return provider.InteractionPosition +
               direction * queueSpacing * (positionBehindFront + 1);
    }

    /// <summary>
    /// Estimates the wait before a new arrival can begin its interaction.
    /// Includes the active user's remaining walk/use time and all waiting users ahead.
    /// </summary>
    public float GetEstimatedWaitTime()
    {
        CleanupInvalidEntries();

        float waitTime = 0f;

        if (activeEntry != null)
        {
            float elapsed = Mathf.Max(0f, Time.time - activeStartTime);
            waitTime += Mathf.Max(0f, activeEstimatedTotalTime - elapsed);
        }

        foreach (QueueEntry entry in entries)
        {
            if (entry == activeEntry)
                continue;

            waitTime += entry.EstimatedTravelTime;
            waitTime += entry.Definition != null
                ? entry.Definition.DurationSeconds
                : 0f;
        }

        return waitTime;
    }

    /// <summary>
    /// Removes an agent from the queue. If it was the active user,
    /// its reservation is released and the next agent is promoted.
    /// </summary>
    public void Leave(MonoBehaviour agent)
    {
        if (agent == null)
            return;

        QueueEntry entry = FindEntry(agent);
        if (entry == null)
            return;

        bool wasActive = entry == activeEntry;

        if (wasActive && provider != null && entry.Definition != null)
            provider.ReleaseReservation(agent, entry.Definition);

        entries.Remove(entry);

        if (wasActive)
        {
            activeEntry = null;
            activeEstimatedTotalTime = 0f;
            TryBeginNextEntry();
        }
    }

    /// <summary>
    /// Completes the active interaction, releases its reservation and promotes
    /// the next waiting agent.
    /// </summary>
    public void Complete(MonoBehaviour agent)
    {
        if (!IsActive(agent))
            return;

        Leave(agent);
    }

    private void TryBeginNextEntry()
    {
        if (activeEntry != null || entries.Count == 0 || provider == null)
            return;

        QueueEntry next = entries[0];

        if (next.Agent == null || next.Definition == null ||
            !provider.TryReserve(next.Agent, next.Definition))
        {
            return;
        }

        activeEntry = next;
        activeStartTime = Time.time;

        PedestrianMovementController movement =
            next.Agent.GetComponent<PedestrianMovementController>();

        float travelTime = movement != null
            ? movement.EstimateTravelTime(provider.InteractionPosition)
            : next.EstimatedTravelTime;

        activeEstimatedTotalTime =
            Mathf.Max(0f, travelTime) + next.Definition.DurationSeconds;
    }

    private QueueEntry FindEntry(MonoBehaviour agent)
    {
        if (agent == null)
            return null;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Agent == agent)
                return entries[i];
        }

        return null;
    }

    private int GetEntryIndex(MonoBehaviour agent)
    {
        if (agent == null)
            return -1;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Agent == agent)
                return i;
        }

        return -1;
    }

    private void CleanupInvalidEntries()
    {
        if (entries.Count == 0)
            return;

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            QueueEntry entry = entries[i];

            if (entry.Agent == null || !entry.Agent.isActiveAndEnabled ||
                entry.Definition == null)
            {
                if (entry == activeEntry)
                {
                    if (provider != null && entry.Agent != null &&
                        entry.Definition != null)
                    {
                        provider.ReleaseReservation(entry.Agent, entry.Definition);
                    }

                    activeEntry = null;
                    activeEstimatedTotalTime = 0f;
                }

                entries.RemoveAt(i);
            }
        }
    }

    private void OnDisable()
    {
        if (provider != null)
        {
            foreach (QueueEntry entry in entries)
            {
                if (entry.Agent != null && entry.Definition != null)
                    provider.ReleaseReservation(entry.Agent, entry.Definition);
            }
        }

        entries.Clear();
        activeEntry = null;
        activeEstimatedTotalTime = 0f;
    }

    private void OnValidate()
    {
        maximumWaitingAgents = Mathf.Max(0, maximumWaitingAgents);
        queueSpacing = Mathf.Max(0.25f, queueSpacing);
    }
}