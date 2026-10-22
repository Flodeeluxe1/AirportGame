
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serializes access to each intersection for both normal traffic
/// and cargo delivery trucks.
///
/// Add this component to the same RoadManager GameObject as RoadNetwork.
/// Lower entity IDs have priority. Equal IDs are resolved FIFO.
/// </summary>
public class IntersectionManager : MonoBehaviour
{
    private class EntryRequest
    {
        public Component Vehicle;
        public long RequestOrder;

        public EntryRequest(Component vehicle, long requestOrder)
        {
            Vehicle = vehicle;
            RequestOrder = requestOrder;
        }
    }

    private class IntersectionState
    {
        public Component ReservedBy;

        public readonly List<EntryRequest> Waiting =
            new List<EntryRequest>();
    }

    private readonly Dictionary<Vector2Int, IntersectionState> intersections =
        new Dictionary<Vector2Int, IntersectionState>();

    private long nextRequestOrder;

    public void RequestEntry(
        Vector2Int intersection,
        Component vehicle)
    {
        if (!IsValidVehicle(vehicle))
            return;

        IntersectionState state = GetState(intersection);

        if (state.ReservedBy == vehicle)
            return;

        foreach (EntryRequest request in state.Waiting)
        {
            if (request.Vehicle == vehicle)
                return;
        }

        state.Waiting.Add(
            new EntryRequest(vehicle, nextRequestOrder++));
    }

    public bool HasReservation(
        Vector2Int intersection,
        Component vehicle)
    {
        return vehicle != null &&
               intersections.TryGetValue(
                   intersection,
                   out IntersectionState state) &&
               state.ReservedBy == vehicle;
    }

    public bool IsReserved(Vector2Int intersection)
    {
        return intersections.TryGetValue(
                   intersection,
                   out IntersectionState state) &&
               state.ReservedBy != null;
    }

    public void Release(
        Vector2Int intersection,
        Component vehicle)
    {
        if (vehicle == null)
            return;

        if (!intersections.TryGetValue(
                intersection,
                out IntersectionState state))
        {
            return;
        }

        if (state.ReservedBy == vehicle)
            state.ReservedBy = null;

        RemoveRequest(state, vehicle);
    }

    public void CancelRequest(Component vehicle)
    {
        if (vehicle == null)
            return;

        foreach (IntersectionState state in intersections.Values)
        {
            RemoveRequest(state, vehicle);

            // Clean up reservations if a vehicle is disabled or destroyed.
            if (state.ReservedBy == vehicle)
                state.ReservedBy = null;
        }
    }

    private void LateUpdate()
    {
        foreach (KeyValuePair<Vector2Int, IntersectionState> pair
                 in intersections)
        {
            IntersectionState state = pair.Value;

            if (state.ReservedBy != null)
                continue;

            state.Waiting.RemoveAll(
                request => !IsValidVehicle(request.Vehicle));

            if (state.Waiting.Count == 0)
                continue;

            state.Waiting.Sort((a, b) =>
            {
                if (a.Vehicle == null)
                    return b.Vehicle == null ? 0 : 1;

                if (b.Vehicle == null)
                    return -1;

                int priorityComparison =
                    CompareVehiclePriority(a.Vehicle, b.Vehicle);

                if (priorityComparison != 0)
                    return priorityComparison;

                return a.RequestOrder.CompareTo(b.RequestOrder);
            });

            EntryRequest winner = state.Waiting[0];
            state.Waiting.RemoveAt(0);

            if (IsValidVehicle(winner.Vehicle))
                state.ReservedBy = winner.Vehicle;
        }
    }


    private int CompareVehiclePriority(
        Component a,
        Component b)
    {
        VehicleSpawnID aSpawnID =
            a.GetComponent<VehicleSpawnID>();

        VehicleSpawnID bSpawnID =
            b.GetComponent<VehicleSpawnID>();

        if (aSpawnID == null || bSpawnID == null)
        {
            Debug.LogError(
                "IntersectionManager: A vehicle is missing VehicleSpawnID."
            );

            return 0;
        }

        return aSpawnID.SpawnID.CompareTo(bSpawnID.SpawnID);
    }


    private bool IsValidVehicle(Component vehicle)
    {
        if (vehicle == null || !vehicle.gameObject.activeInHierarchy)
            return false;

        return vehicle is TrafficTestVehicle ||
               vehicle is CargoDeliveryTruck;
    }

    private IntersectionState GetState(Vector2Int intersection)
    {
        if (!intersections.TryGetValue(
                intersection,
                out IntersectionState state))
        {
            state = new IntersectionState();
            intersections.Add(intersection, state);
        }

        return state;
    }

    private void RemoveRequest(
        IntersectionState state,
        Component vehicle)
    {
        state.Waiting.RemoveAll(
            request => request.Vehicle == null ||
                       request.Vehicle == vehicle);
    }
}
