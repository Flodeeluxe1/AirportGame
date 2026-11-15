using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks temporary claims on provider/interaction pairs so agents cannot
/// overbook a single interaction slot.
/// Agents are responsible for releasing their reservations when finished,
/// interrupted, disabled or destroyed.
/// </summary>
public static class InteractionReservation
{
    private sealed class Reservation
    {
        public InteractionProvider Provider;
        public InteractionDefinition Definition;
        public MonoBehaviour Agent;
    }

    private static readonly List<Reservation> Reservations =
        new List<Reservation>();

    public static bool HasAvailableSlot(
        InteractionProvider provider,
        InteractionDefinition definition)
    {
        if (provider == null || definition == null)
            return false;

        CleanupInvalidReservations();

        int reservedCount = 0;

        for (int i = 0; i < Reservations.Count; i++)
        {
            Reservation reservation = Reservations[i];

            if (reservation.Provider == provider &&
                reservation.Definition == definition)
            {
                reservedCount++;
            }
        }

        return reservedCount < definition.MaxConcurrentUsers;
    }

    public static bool TryReserve(
        InteractionProvider provider,
        InteractionDefinition definition,
        MonoBehaviour agent)
    {
        if (provider == null || definition == null || agent == null)
            return false;

        CleanupInvalidReservations();

        // Repeated requests from the current owner are treated as success.
        for (int i = 0; i < Reservations.Count; i++)
        {
            Reservation existing = Reservations[i];

            if (existing.Provider == provider &&
                existing.Definition == definition &&
                existing.Agent == agent)
            {
                return true;
            }
        }

        if (!HasAvailableSlot(provider, definition))
            return false;

        Reservations.Add(new Reservation
        {
            Provider = provider,
            Definition = definition,
            Agent = agent
        });

        return true;
    }

    public static void Release(
        InteractionProvider provider,
        InteractionDefinition definition,
        MonoBehaviour agent)
    {
        if (provider == null || definition == null || agent == null)
            return;

        for (int i = Reservations.Count - 1; i >= 0; i--)
        {
            Reservation reservation = Reservations[i];

            if (reservation.Provider == provider &&
                reservation.Definition == definition &&
                reservation.Agent == agent)
            {
                Reservations.RemoveAt(i);
            }
        }
    }

    public static void ReleaseAll(
        MonoBehaviour agent,
        InteractionProvider provider = null)
    {
        if (agent == null)
            return;

        for (int i = Reservations.Count - 1; i >= 0; i--)
        {
            Reservation reservation = Reservations[i];

            if (reservation.Agent == agent &&
                (provider == null || reservation.Provider == provider))
            {
                Reservations.RemoveAt(i);
            }
        }
    }

    public static void ReleaseAllForProvider(InteractionProvider provider)
    {
        if (provider == null)
            return;

        for (int i = Reservations.Count - 1; i >= 0; i--)
        {
            if (Reservations[i].Provider == provider)
                Reservations.RemoveAt(i);
        }
    }

    private static void CleanupInvalidReservations()
    {
        for (int i = Reservations.Count - 1; i >= 0; i--)
        {
            Reservation reservation = Reservations[i];

            if (reservation.Provider == null ||
                reservation.Definition == null ||
                reservation.Agent == null ||
                !reservation.Agent.isActiveAndEnabled)
            {
                Reservations.RemoveAt(i);
            }
        }
    }
}