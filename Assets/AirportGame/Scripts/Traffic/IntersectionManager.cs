using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serializes access to each intersection. Add this component to the same
/// RoadManager GameObject as RoadNetwork.
/// </summary>
public class IntersectionManager : MonoBehaviour
{
    private class EntryRequest
    {
        public TrafficTestVehicle Vehicle;
        public long RequestOrder;

        public EntryRequest(TrafficTestVehicle vehicle, long requestOrder)
        {
            Vehicle = vehicle;
            RequestOrder = requestOrder;
        }
    }

    private class IntersectionState
    {
        public TrafficTestVehicle ReservedBy;
        public readonly List<EntryRequest> Waiting = new List<EntryRequest>();
    }

    private readonly Dictionary<Vector2Int, IntersectionState> intersections =
        new Dictionary<Vector2Int, IntersectionState>();

    private long nextRequestOrder;

    public void RequestEntry(Vector2Int intersection, TrafficTestVehicle vehicle)
    {
        if (vehicle == null)
            return;

        IntersectionState state = GetState(intersection);

        if (state.ReservedBy == vehicle)
            return;

        foreach (EntryRequest request in state.Waiting)
        {
            if (request.Vehicle == vehicle)
                return;
        }

        state.Waiting.Add(new EntryRequest(vehicle, nextRequestOrder++));
    }

    public bool HasReservation(Vector2Int intersection, TrafficTestVehicle vehicle)
    {
        return intersections.TryGetValue(intersection, out IntersectionState state)
            && state.ReservedBy == vehicle;
    }

    public bool IsReserved(Vector2Int intersection)
    {
        return intersections.TryGetValue(intersection, out IntersectionState state)
            && state.ReservedBy != null;
    }

    public void Release(Vector2Int intersection, TrafficTestVehicle vehicle)
    {
        if (!intersections.TryGetValue(intersection, out IntersectionState state))
            return;

        if (state.ReservedBy == vehicle)
            state.ReservedBy = null;

        RemoveRequest(state, vehicle);
    }

    public void CancelRequest(TrafficTestVehicle vehicle)
    {
        if (vehicle == null)
            return;

        foreach (IntersectionState state in intersections.Values)
        {
            RemoveRequest(state, vehicle);

            // Also clean up reservations if a vehicle is disabled/destroyed.
            if (state.ReservedBy == vehicle)
                state.ReservedBy = null;
        }
    }

    private void LateUpdate()
    {
        foreach (KeyValuePair<Vector2Int, IntersectionState> pair in intersections)
        {
            IntersectionState state = pair.Value;

            if (state.ReservedBy != null)
                continue;

            state.Waiting.RemoveAll(request => request.Vehicle == null);

            if (state.Waiting.Count == 0)
                continue;

            // Match the vehicle's existing entity-ID priority rule:
            // lower entity ID wins. Equal IDs are resolved FIFO.
            state.Waiting.Sort((a, b) =>
            {
                if (a.Vehicle == null)
                    return b.Vehicle == null ? 0 : 1;

                if (b.Vehicle == null)
                    return -1;

                int priorityComparison =
                    a.Vehicle.CompareIntersectionPriority(b.Vehicle);

                if (priorityComparison != 0)
                    return priorityComparison;

                return a.RequestOrder.CompareTo(b.RequestOrder);
            });

            EntryRequest winner = state.Waiting[0];
            state.Waiting.RemoveAt(0);
            state.ReservedBy = winner.Vehicle;
        }
    }

    private IntersectionState GetState(Vector2Int intersection)
    {
        if (!intersections.TryGetValue(intersection, out IntersectionState state))
        {
            state = new IntersectionState();
            intersections.Add(intersection, state);
        }

        return state;
    }

    private void RemoveRequest(IntersectionState state, TrafficTestVehicle vehicle)
    {
        state.Waiting.RemoveAll(request =>
            request.Vehicle == null || request.Vehicle == vehicle);
    }
}
