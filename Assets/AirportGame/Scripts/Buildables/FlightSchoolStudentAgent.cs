using System;
using UnityEngine;

/// <summary>
/// Movement and seat reservation for one Flight School student.
/// The student uses the shared pedestrian movement and interaction-point systems.
/// </summary>
[DisallowMultipleComponent]
public class FlightSchoolStudentAgent : MonoBehaviour
{
    private PedestrianMovementController movement;
    private TrafficTestVehicle vehicle;

    private InteractionProvider reservedSeat;
    private InteractionDefinition seatInteraction;

    private bool walkingToSeat;
    private bool walkingToAircraft;
    private bool walkingToCar;

    public event Action<FlightSchoolStudentAgent> SeatReached;
    public event Action<FlightSchoolStudentAgent> AircraftReached;
    public event Action<FlightSchoolStudentAgent> CarReached;
    public event Action<FlightSchoolStudentAgent> PathFailed;

    public TrafficTestVehicle Vehicle => vehicle;
    public InteractionProvider ReservedSeat => reservedSeat;
    public bool IsSeated =>
        reservedSeat != null &&
        !walkingToSeat &&
        !walkingToAircraft &&
        !walkingToCar;

    public void Initialize(TrafficTestVehicle assignedVehicle)
    {
        vehicle = assignedVehicle;

        // Students follow the school timetable, not the hobby-pilot Utility AI.
        DisablePilotAutonomy<PilotInteractionController>();
        DisablePilotAutonomy<PilotWanderingController>();
        DisablePilotAutonomy<PilotSocialInteractionController>();
        DisablePilotAutonomy<PilotSocialSeatingController>();
        DisablePilotAutonomy<PilotGroupConversationController>();
        DisablePilotAutonomy<PilotDepartureController>();

        movement = GetComponent<PedestrianMovementController>();
        if (movement == null)
            movement = gameObject.AddComponent<PedestrianMovementController>();

        movement.Arrived -= HandleMovementArrived;
        movement.PathFailed -= HandlePathFailed;
        movement.Arrived += HandleMovementArrived;
        movement.PathFailed += HandlePathFailed;
    }

    private void DisablePilotAutonomy<T>() where T : Behaviour
    {
        T behaviour = GetComponent<T>();

        if (behaviour != null)
            behaviour.enabled = false;
    }

    public bool AssignSeat(
        InteractionProvider provider,
        InteractionDefinition interaction)
    {
        if (provider == null || interaction == null || movement == null)
            return false;

        if (!provider.TryReserve(this, interaction))
            return false;

        reservedSeat = provider;
        seatInteraction = interaction;
        walkingToSeat = true;
        walkingToAircraft = false;
        walkingToCar = false;

        if (!movement.MoveTo(provider.InteractionPosition))
        {
            ReleaseSeat();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Walks from the reserved chair to the training aircraft.
    /// The chair reservation is retained so the student can return to it.
    /// </summary>
    public bool BeginFlightLesson(GameObject aircraft)
    {
        if (aircraft == null || reservedSeat == null || movement == null)
            return false;

        walkingToSeat = false;
        walkingToAircraft = true;
        walkingToCar = false;

        Vector3 aircraftApproach =
            aircraft.transform.position +
            aircraft.transform.right * 2f;

        return movement.MoveTo(aircraftApproach);
    }

    /// <summary>
    /// Returns the student to their original reserved chair after landing.
    /// </summary>
    public bool ReturnToSeat()
    {
        if (reservedSeat == null || movement == null)
            return false;

        walkingToSeat = true;
        walkingToAircraft = false;
        walkingToCar = false;

        return movement.MoveTo(reservedSeat.InteractionPosition);
    }

    /// <summary>
    /// Leaves the classroom seat and walks back to this student's own parked car.
    /// </summary>
    public void BeginDeparture()
    {
        if (vehicle == null || movement == null)
        {
            PathFailed?.Invoke(this);
            return;
        }

        ReleaseSeat();

        walkingToSeat = false;
        walkingToAircraft = false;
        walkingToCar = true;

        Vector3 carSidePosition =
            vehicle.ParkingPosition +
            vehicle.transform.right * 1.5f;

        movement.MoveTo(carSidePosition);
    }

    private void HandleMovementArrived()
    {
        if (walkingToSeat)
        {
            walkingToSeat = false;

            if (reservedSeat != null)
                movement.AlignToAnimationPoint(reservedSeat);

            SeatReached?.Invoke(this);
            return;
        }

        if (walkingToAircraft)
        {
            walkingToAircraft = false;
            AircraftReached?.Invoke(this);
            return;
        }

        if (walkingToCar)
        {
            walkingToCar = false;
            CarReached?.Invoke(this);

            if (vehicle != null)
                vehicle.RequestDeparture();

            Destroy(gameObject);
        }
    }

    private void HandlePathFailed()
    {
        if (walkingToSeat && !walkingToAircraft)
            ReleaseSeat();

        walkingToSeat = false;
        walkingToAircraft = false;
        walkingToCar = false;
        PathFailed?.Invoke(this);
    }

    public void ReleaseSeat()
    {
        if (reservedSeat != null)
            reservedSeat.ReleaseReservation(this, seatInteraction);

        reservedSeat = null;
        seatInteraction = null;
    }

    private void OnDestroy()
    {
        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            movement.PathFailed -= HandlePathFailed;
        }

        ReleaseSeat();
    }
}
