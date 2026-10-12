
using System.Collections.Generic;
using UnityEngine;

public class AircraftRunwayTrafficManager : MonoBehaviour
{
    private sealed class RunwayReservation
    {
        public AircraftRunway Runway;
        public MonoBehaviour Owner;
    }

    [Header("Runway Occupancy")]
    [Tooltip("Additional clearance around an aircraft when checking if it still occupies a runway.")]
    [Min(0f)]
    [SerializeField] private float runwayOccupancyBuffer = 0.5f;

    [SerializeField] private GridManager gridManager;

    private readonly Dictionary<EntityId, RunwayReservation>
        departureReservations = new();

    private readonly Dictionary<EntityId, RunwayReservation>
        lineupReservations = new();

    public int GetLandingTrafficCount(
        AircraftRunway runway,
        AircraftLandingController requestingAircraft = null)
    {
        if (runway == null)
            return 0;

        AircraftLandingController[] aircraft =
            FindObjectsByType<AircraftLandingController>(
                FindObjectsSortMode.None
            );

        int count = 0;

        foreach (AircraftLandingController controller in aircraft)
        {
            if (controller == null ||
                controller == requestingAircraft)
            {
                continue;
            }

            if (!controller.IsLandingTrafficActive)
                continue;

            if (controller.SelectedRunway == runway)
                count++;
        }

        return count;
    }

    public int GetDepartureTrafficCount(
        AircraftRunway runway,
        MonoBehaviour requestingAircraft = null)
    {
        if (runway == null)
            return 0;

        RemoveInvalidReservations();

        int count = 0;

        foreach (RunwayReservation reservation
                 in departureReservations.Values)
        {
            if (reservation.Runway != runway ||
                reservation.Owner == requestingAircraft)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public int GetRunwayTrafficCount(
        AircraftRunway runway,
        AircraftLandingController requestingLandingAircraft = null,
        MonoBehaviour requestingDepartureAircraft = null)
    {
        if (runway == null)
            return 0;

        return GetLandingTrafficCount(
                   runway,
                   requestingLandingAircraft
               )
               +
               GetDepartureTrafficCount(
                   runway,
                   requestingDepartureAircraft
               );
    }

    public bool RegisterDepartureReservation(
        AircraftRunway runway,
        MonoBehaviour departureAircraft)
    {
        if (runway == null || departureAircraft == null)
        {
            Debug.LogWarning(
                "Cannot register a departure reservation without " +
                "a runway and an aircraft.",
                this
            );

            return false;
        }

        RemoveInvalidReservations();

        EntityId id = departureAircraft.GetEntityId();

        if (departureReservations.TryGetValue(
                id,
                out RunwayReservation existing))
        {
            existing.Runway = runway;
            existing.Owner = departureAircraft;
            return true;
        }

        departureReservations.Add(
            id,
            new RunwayReservation
            {
                Runway = runway,
                Owner = departureAircraft
            }
        );

        Debug.Log(
            $"RUNWAY DEPARTURE RESERVED | Aircraft: " +
            $"{departureAircraft.name} | Runway: {runway.name}",
            this
        );

        return true;
    }

    public bool TryGetReservedDepartureRunway(
        MonoBehaviour departureAircraft,
        out AircraftRunway runway)
    {
        runway = null;

        if (departureAircraft == null)
            return false;

        RemoveInvalidReservations();

        EntityId id = departureAircraft.GetEntityId();

        if (!departureReservations.TryGetValue(
                id,
                out RunwayReservation reservation))
        {
            return false;
        }

        if (reservation.Owner != departureAircraft ||
            reservation.Runway == null)
        {
            return false;
        }

        runway = reservation.Runway;
        return true;
    }

    public void ReleaseDepartureReservation(
        MonoBehaviour departureAircraft)
    {
        if (departureAircraft == null)
            return;

        EntityId id = departureAircraft.GetEntityId();

        if (departureReservations.TryGetValue(
                id,
                out RunwayReservation reservation) &&
            reservation.Owner == departureAircraft)
        {
            departureReservations.Remove(id);

            Debug.Log(
                $"RUNWAY DEPARTURE RESERVATION RELEASED | " +
                $"Aircraft: {departureAircraft.name} | " +
                $"Runway: {reservation.Runway.name}",
                this
            );
        }

        ReleaseRunwayLineUpReservation(departureAircraft);
    }

    /// <summary>
    /// Checks whether there is an active arrival, an aircraft still
    /// physically occupying the runway, or another departure holding
    /// the line-up reservation.
    /// </summary>
    public bool IsRunwayClearForLineUp(
        AircraftRunway runway,
        MonoBehaviour requestingDeparture,
        out string reason)
    {
        reason = string.Empty;

        if (runway == null)
        {
            reason = "No runway is selected.";
            return false;
        }

        RemoveInvalidReservations();

        AircraftLandingController[] arrivals =
            FindObjectsByType<AircraftLandingController>(
                FindObjectsSortMode.None
            );

        foreach (AircraftLandingController arrival in arrivals)
        {
            if (arrival == null ||
                arrival.SelectedRunway != runway)
            {
                continue;
            }

            // Includes alignment, approach, flare, rollout and go-around.
            if (arrival.IsLandingTrafficActive)
            {
                reason =
                    $"Waiting for arrival {arrival.name} to finish " +
                    "landing and clear the runway.";

                return false;
            }

            // LandingComplete becomes true before the aircraft necessarily
            // finishes taxiing off the runway. Check its actual footprint.
            if (IsAircraftOccupyingRunway(arrival, runway))
            {
                reason =
                    $"Waiting for landed aircraft {arrival.name} " +
                    "to vacate the runway.";

                return false;
            }
        }

        foreach (RunwayReservation reservation
                 in lineupReservations.Values)
        {
            if (reservation.Runway != runway ||
                reservation.Owner == requestingDeparture)
            {
                continue;
            }

            reason =
                $"Waiting for {reservation.Owner.name}, which already " +
                "owns the runway line-up reservation.";

            return false;
        }

        return true;
    }

    /// <summary>
    /// Atomically reserves runway line-up for one departing aircraft.
    /// Departure traffic reservations alone do not grant runway clearance.
    /// </summary>
    public bool TryReserveRunwayForLineUp(
        AircraftRunway runway,
        MonoBehaviour departureAircraft,
        out string reason)
    {
        reason = string.Empty;

        if (runway == null || departureAircraft == null)
        {
            reason = "Runway or departure aircraft is missing.";
            return false;
        }

        RemoveInvalidReservations();

        if (!IsRunwayClearForLineUp(
                runway,
                departureAircraft,
                out reason))
        {
            return false;
        }

        EntityId id = departureAircraft.GetEntityId();

        if (lineupReservations.TryGetValue(
                id,
                out RunwayReservation existing))
        {
            if (existing.Owner == departureAircraft)
            {
                existing.Runway = runway;
                existing.Owner = departureAircraft;
                return true;
            }

            // Defensive handling in case an entity ID was reused.
            lineupReservations.Remove(id);
        }

        lineupReservations.Add(
            id,
            new RunwayReservation
            {
                Runway = runway,
                Owner = departureAircraft
            }
        );

        Debug.Log(
            $"RUNWAY LINE-UP RESERVED | Aircraft: " +
            $"{departureAircraft.name} | Runway: {runway.name}",
            this
        );

        return true;
    }

    public void ReleaseRunwayLineUpReservation(
        MonoBehaviour departureAircraft)
    {
        if (departureAircraft == null)
            return;

        EntityId id = departureAircraft.GetEntityId();

        if (!lineupReservations.TryGetValue(
                id,
                out RunwayReservation reservation))
        {
            return;
        }

        if (reservation.Owner != departureAircraft)
            return;

        lineupReservations.Remove(id);

        Debug.Log(
            $"RUNWAY LINE-UP RESERVATION RELEASED | Aircraft: " +
            $"{departureAircraft.name} | Runway: " +
            $"{(reservation.Runway != null ? reservation.Runway.name : "Unknown")}",
            this
        );
    }

    private bool IsAircraftOccupyingRunway(
        AircraftLandingController aircraft,
        AircraftRunway runway)
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        // Fail safe: if the runway footprint cannot be determined,
        // don't permit a departure based on an uncertain clearance check.
        if (gridManager == null ||
            !runway.TryGetGeometry(
                gridManager,
                out Vector3 center,
                out Vector3 axis,
                out float length,
                out float surfaceY))
        {
            return true;
        }

        PlacedBuilding placed =
            runway.GetComponent<PlacedBuilding>();

        if (placed == null)
            placed = runway.GetComponentInParent<PlacedBuilding>();

        if (placed == null)
            placed = runway.GetComponentInChildren<PlacedBuilding>(true);

        if (placed == null)
            return true;

        axis.y = 0f;

        if (axis.sqrMagnitude < 0.0001f)
            return true;

        axis.Normalize();

        Vector3 lateralAxis =
            new Vector3(-axis.z, 0f, axis.x);

        // Runway geometry determines the longitudinal axis. The other
        // placed-building dimension determines the width.
        bool axisIsX = Mathf.Abs(axis.x) >= Mathf.Abs(axis.z);

        float lateralCellCount =
            axisIsX ? placed.Depth : placed.Width;

        float halfLength = length * 0.5f;
        float halfWidth =
            lateralCellCount * gridManager.CellSize * 0.5f;

        Collider[] colliders =
            aircraft.GetComponentsInChildren<Collider>();

        foreach (Collider aircraftCollider in colliders)
        {
            if (aircraftCollider == null ||
                !aircraftCollider.enabled ||
                !aircraftCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            Bounds bounds = aircraftCollider.bounds;

            // Ignore a landed aircraft if it is clearly at a different
            // altitude from the runway surface.
            if (Mathf.Abs(bounds.center.y - surfaceY) >
                bounds.extents.y + 3f)
            {
                continue;
            }

            Vector3 offset = bounds.center - center;
            offset.y = 0f;

            float alongDistance =
                Mathf.Abs(Vector3.Dot(offset, axis));

            float lateralDistance =
                Mathf.Abs(Vector3.Dot(offset, lateralAxis));

            // Project the collider's world AABB extents onto the
            // runway's longitudinal and lateral axes.
            float alongExtent =
                Mathf.Abs(axis.x) * bounds.extents.x +
                Mathf.Abs(axis.z) * bounds.extents.z;

            float lateralExtent =
                Mathf.Abs(lateralAxis.x) * bounds.extents.x +
                Mathf.Abs(lateralAxis.z) * bounds.extents.z;

            bool overlapsRunway =
                alongDistance <=
                    halfLength + alongExtent + runwayOccupancyBuffer
                &&
                lateralDistance <=
                    halfWidth + lateralExtent + runwayOccupancyBuffer;

            if (overlapsRunway)
                return true;
        }

        // If the aircraft has no collider, use its root position with
        // a small conservative margin.
        if (colliders.Length == 0)
        {
            Vector3 offset = aircraft.transform.position - center;
            offset.y = 0f;

            return
                Mathf.Abs(Vector3.Dot(offset, axis)) <=
                    halfLength + runwayOccupancyBuffer
                &&
                Mathf.Abs(Vector3.Dot(offset, lateralAxis)) <=
                    halfWidth + runwayOccupancyBuffer;
        }

        return false;
    }

    private void RemoveInvalidReservations()
    {
        RemoveInvalidReservationsFrom(departureReservations);
        RemoveInvalidReservationsFrom(lineupReservations);
    }

    private void RemoveInvalidReservationsFrom(
        Dictionary<EntityId, RunwayReservation> reservations)
    {
        List<EntityId> invalidIds = null;

        foreach (KeyValuePair<EntityId, RunwayReservation> pair
                 in reservations)
        {
            RunwayReservation reservation = pair.Value;

            if (reservation.Owner != null &&
                reservation.Runway != null)
            {
                continue;
            }

            if (invalidIds == null)
                invalidIds = new List<EntityId>();

            invalidIds.Add(pair.Key);
        }

        if (invalidIds == null)
            return;

        foreach (EntityId id in invalidIds)
            reservations.Remove(id);
    }

    [ContextMenu("Debug/Log Departure Reservations")]
    private void DebugLogDepartureReservations()
    {
        RemoveInvalidReservations();

        Debug.Log(
            $"Departure reservations: {departureReservations.Count} | " +
            $"Line-up reservations: {lineupReservations.Count}",
            this
        );

        foreach (RunwayReservation reservation
                 in departureReservations.Values)
        {
            Debug.Log(
                $"Departure queued: {reservation.Owner.name} | " +
                $"Runway: {reservation.Runway.name}",
                reservation.Owner
            );
        }

        foreach (RunwayReservation reservation
                 in lineupReservations.Values)
        {
            Debug.Log(
                $"Line-up reserved: {reservation.Owner.name} | " +
                $"Runway: {reservation.Runway.name}",
                reservation.Owner
            );
        }
    }
}