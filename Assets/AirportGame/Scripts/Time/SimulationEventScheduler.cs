using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Executes callbacks against absolute simulation time rather than rendered-frame time.
/// Owned and advanced by GameTimeManager.
/// </summary>
public sealed class SimulationEventScheduler
{
    private const int MaximumEventsPerUpdate = 1000;

    private sealed class ScheduledEvent
    {
        public long Id;
        public long Sequence;
        public double DueTime;
        public double RepeatInterval;
        public Action Callback;

        public bool IsRecurring => RepeatInterval > 0.0;
    }

    private sealed class ScheduledEventComparer : IComparer<ScheduledEvent>
    {
        public int Compare(ScheduledEvent a, ScheduledEvent b)
        {
            if (ReferenceEquals(a, b))
                return 0;

            int timeComparison = a.DueTime.CompareTo(b.DueTime);
            if (timeComparison != 0)
                return timeComparison;

            return a.Sequence.CompareTo(b.Sequence);
        }
    }

    private readonly SortedSet<ScheduledEvent> eventQueue =
        new SortedSet<ScheduledEvent>(new ScheduledEventComparer());

    private readonly Dictionary<long, ScheduledEvent> eventsById =
        new Dictionary<long, ScheduledEvent>();

    private long nextEventId = 1;
    private long nextSequence;

    /// <summary>
    /// Number of currently scheduled one-shot and recurring events.
    /// </summary>
    public int PendingEventCount => eventsById.Count;

    /// <summary>
    /// Schedules a one-shot callback at an absolute simulation time in seconds.
    /// Events with equal due times execute in the order they were scheduled.
    /// </summary>
    public long ScheduleAt(double dueTime, Action callback)
    {
        ValidateCallback(callback);
        ValidateTime(dueTime, nameof(dueTime));

        long id = nextEventId++;
        ScheduledEvent scheduledEvent = new ScheduledEvent
        {
            Id = id,
            Sequence = nextSequence++,
            DueTime = dueTime,
            RepeatInterval = 0.0,
            Callback = callback
        };

        eventsById.Add(id, scheduledEvent);
        eventQueue.Add(scheduledEvent);
        return id;
    }

    /// <summary>
    /// Schedules a one-shot callback after a delay measured in simulation seconds.
    /// A zero delay makes it eligible on the next scheduler processing pass.
    /// </summary>
    public long ScheduleAfter(double delaySeconds, Action callback)
    {
        if (double.IsNaN(delaySeconds) ||
            double.IsInfinity(delaySeconds) ||
            delaySeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delaySeconds),
                "Simulation delay must be a finite, non-negative value."
            );
        }

        return ScheduleAt(
            GameTimeManager.SimulationTime + delaySeconds,
            callback
        );
    }

    /// <summary>
    /// Schedules a fixed-interval callback. The first execution occurs at
    /// firstDueTime; later executions retain that cadence and catch up if needed.
    /// </summary>
    public long ScheduleRecurring(
        double firstDueTime,
        double intervalSeconds,
        Action callback)
    {
        ValidateCallback(callback);
        ValidateTime(firstDueTime, nameof(firstDueTime));

        if (double.IsNaN(intervalSeconds) ||
            double.IsInfinity(intervalSeconds) ||
            intervalSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalSeconds),
                "Recurring interval must be a finite, positive value."
            );
        }

        long id = nextEventId++;
        ScheduledEvent scheduledEvent = new ScheduledEvent
        {
            Id = id,
            Sequence = nextSequence++,
            DueTime = firstDueTime,
            RepeatInterval = intervalSeconds,
            Callback = callback
        };

        eventsById.Add(id, scheduledEvent);
        eventQueue.Add(scheduledEvent);
        return id;
    }

    /// <summary>
    /// Cancels a scheduled one-shot or recurring event.
    /// Returns false if the event has already executed or was already cancelled.
    /// </summary>
    public bool Cancel(long eventId)
    {
        ScheduledEvent scheduledEvent;
        if (!eventsById.TryGetValue(eventId, out scheduledEvent))
            return false;

        eventsById.Remove(eventId);
        eventQueue.Remove(scheduledEvent);
        return true;
    }

    /// <summary>
    /// Executes due events in due-time and insertion order.
    /// A per-update cap prevents a large backlog or immediate self-scheduling
    /// callback from monopolising a rendered frame.
    /// </summary>
    public int ProcessDueEvents(double currentSimulationTime)
    {
        ValidateTime(currentSimulationTime, nameof(currentSimulationTime));

        int processedCount = 0;

        while (processedCount < MaximumEventsPerUpdate &&
               eventQueue.Count > 0)
        {
            ScheduledEvent scheduledEvent = eventQueue.Min;
            if (scheduledEvent.DueTime > currentSimulationTime)
                break;

            eventQueue.Remove(scheduledEvent);

            if (!scheduledEvent.IsRecurring)
                eventsById.Remove(scheduledEvent.Id);

            try
            {
                scheduledEvent.Callback.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            processedCount++;

            if (scheduledEvent.IsRecurring &&
                eventsById.ContainsKey(scheduledEvent.Id))
            {
                ScheduledEvent nextOccurrence = new ScheduledEvent
                {
                    Id = scheduledEvent.Id,
                    Sequence = nextSequence++,
                    DueTime = scheduledEvent.DueTime + scheduledEvent.RepeatInterval,
                    RepeatInterval = scheduledEvent.RepeatInterval,
                    Callback = scheduledEvent.Callback
                };

                eventsById[scheduledEvent.Id] = nextOccurrence;
                eventQueue.Add(nextOccurrence);
            }
        }

        return processedCount;
    }

    /// <summary>
    /// Removes all pending callbacks. Used when the owning game-time manager is destroyed.
    /// </summary>
    public void Clear()
    {
        eventQueue.Clear();
        eventsById.Clear();
    }

    private static void ValidateCallback(Action callback)
    {
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));
    }

    private static void ValidateTime(double time, string parameterName)
    {
        if (double.IsNaN(time) || double.IsInfinity(time))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Simulation time must be finite."
            );
        }
    }
}
