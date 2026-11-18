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
    private bool walkingToCar;

    public event Action<FlightSchoolStudentAgent> SeatReached;
    public event Action<FlightSchoolStudentAgent> CarReached;
    public event Action<FlightSchoolStudentAgent> PathFailed;

    public TrafficTestVehicle Vehicle => vehicle;
    public InteractionProvider ReservedSeat => reservedSeat;
    public bool IsSeated => reservedSeat != null && !walkingToSeat && !walkingToCar;

    public void Initialize(TrafficTestVehicle assignedVehicle)
    {
        vehicle = assignedVehicle;

        movement = GetComponent<PedestrianMovementController>();
        if (movement == null)
            movement = gameObject.AddComponent<PedestrianMovementController>();

        movement.Arrived -= HandleMovementArrived;
        movement.PathFailed -= HandlePathFailed;
        movement.Arrived += HandleMovementArrived;
        movement.PathFailed += HandlePathFailed;
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
        walkingToCar = false;

        if (!movement.MoveTo(provider.InteractionPosition))
        {
            ReleaseSeat();
            return false;
        }

        return true;
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
        walkingToCar = true;

        Vector3 carSidePosition =
            vehicle.ParkingPosition +
            vehicle.transform.right * 1.5f;

        if (!movement.MoveTo(carSidePosition))
            HandlePathFailed();
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
        if (walkingToSeat)
            ReleaseSeat();

        walkingToSeat = false;
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
