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
    private bool departureStarted;

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
        {
            Debug.LogError(
                $"FlightSchoolStudentAgent {name}: Cannot assign chair. " +
                $"ProviderMissing={provider == null}, InteractionMissing={interaction == null}, " +
                $"MovementControllerMissing={movement == null}.",
                this);
            return false;
        }

        if (!provider.TryReserve(this, interaction))
        {
            Debug.LogWarning(
                $"FlightSchoolStudentAgent {name}: Chair reservation failed. " +
                $"Provider={provider.name}, Interaction={interaction.InteractionId}, " +
                $"Enabled={provider.InteractionsEnabled}, CanOffer={provider.CanOffer(interaction)}.",
                provider);
            return false;
        }

        reservedSeat = provider;
        seatInteraction = interaction;
        walkingToSeat = true;
        walkingToAircraft = false;
        walkingToCar = false;

        if (!movement.TryMoveTo(provider))
        {
            Debug.LogError(
                $"FlightSchoolStudentAgent {name}: Reserved chair {provider.name}, " +
                $"but no reachable approach cell was found. Preferred target={provider.InteractionPosition}.",
                this);
            HandlePathFailed();
            return false;
        }

        Debug.Log(
            $"FlightSchoolStudentAgent {name}: Reserved chair {provider.name}; " +
            $"walking to selected approach point at {movement.Destination}.",
            this);

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

        // Sitting places the student on the chair's animation anchor, which
        // may be inside a blocked building footprint. Restore the reserved
        // chair's reachable approach position before calculating a route.
        if (walkingToSeat == false && IsSeated)
            movement.AlignToInteractionPoint(reservedSeat);

        walkingToSeat = false;
        walkingToAircraft = true;
        walkingToCar = false;

        Vector3 aircraftApproach =
            aircraft.transform.position +
            aircraft.transform.right * 2f;

        PlacedBuilding aircraftBuilding =
            aircraft.GetComponentInParent<PlacedBuilding>();

        if (movement.TryMoveToNearby(
                aircraftApproach,
                aircraft.transform.position,
                aircraftBuilding))
        {
            Debug.Log(
                $"FlightSchoolStudentAgent {name}: Walking to aircraft {aircraft.name}. " +
                $"Start={transform.position}, preferred approach={aircraftApproach}, " +
                $"selected approach={movement.Destination}.",
                this);
            return true;
        }

        Debug.LogError(
            $"FlightSchoolStudentAgent {name}: No reachable aircraft approach found. " +
            $"Aircraft={aircraft.name}, Start={transform.position}, " +
            $"StartCell={GridCellForDiagnostics(transform.position)}, " +
            $"Preferred={aircraftApproach}, " +
            $"PreferredCell={GridCellForDiagnostics(aircraftApproach)}, " +
            $"MovementStatus={movement.MovementStatus}.",
            this);

        HandlePathFailed();
        return false;
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

        if (movement.TryMoveTo(reservedSeat))
            return true;

        HandlePathFailed();
        return false;
    }

    /// <summary>
    /// Leaves the classroom seat and walks back to this student's own parked car.
    /// </summary>
    public bool BeginDeparture()
    {
        if (departureStarted)
            return walkingToCar;

        if (vehicle == null || movement == null)
            return false;

        // A seated student is aligned to the chair animation point, which
        // can be on a non-walkable cell. Move back to the saved approach point
        // before releasing the reservation and walking to the car.
        if (IsSeated && reservedSeat != null)
            movement.AlignToInteractionPoint(reservedSeat);

        ReleaseSeat();

        walkingToSeat = false;
        walkingToAircraft = false;
        walkingToCar = true;

        Vector3 carSidePosition =
            vehicle.ParkingPosition +
            vehicle.transform.right * 1.5f;

        if (!movement.TryMoveTo(carSidePosition))
        {
            walkingToCar = false;
            return false;
        }

        departureStarted = true;
        return true;
    }

    private string GridCellForDiagnostics(Vector3 worldPosition)
    {
        GridManager grid = FindFirstObjectByType<GridManager>();
        return grid != null
            ? grid.WorldToGrid(worldPosition).ToString()
            : "unknown";
    }

    private void HandleMovementArrived()
    {
        Debug.Log(
            $"FlightSchoolStudentAgent {name}: Movement arrived. " +
            $"WalkingToSeat={walkingToSeat}, WalkingToAircraft={walkingToAircraft}, " +
            $"WalkingToCar={walkingToCar}.",
            this);

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
        Debug.LogError(
            $"FlightSchoolStudentAgent {name}: Pedestrian path failed. " +
            $"WalkingToSeat={walkingToSeat}, WalkingToAircraft={walkingToAircraft}, " +
            $"WalkingToCar={walkingToCar}, ReservedSeat={(reservedSeat != null ? reservedSeat.name : "none")}.",
            this);

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
