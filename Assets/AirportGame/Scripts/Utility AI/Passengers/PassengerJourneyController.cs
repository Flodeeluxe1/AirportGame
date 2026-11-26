using UnityEngine;

/// <summary>
/// Tracks the mandatory stages of an airline passenger's airport journey.
/// Optional needs and interactions remain owned by the existing Utility AI.
/// </summary>
[DisallowMultipleComponent]
public sealed class PassengerJourneyController : MonoBehaviour
{
    public enum JourneyDirection
    {
        Arrival,
        Departure
    }

    public enum JourneyStage
    {
        Uninitialized,
        Aircraft,
        BaggageClaim,
        AwaitingTransport,
        TravellingToTransport,
        AtTransport,
        Completed
    }

    public enum TransportDestination
    {
        None,
        BusStop,
        PickupCar,
        TrainStation,
        UndergroundStation,
        TaxiPickup
    }

    [SerializeField] private JourneyDirection direction;
    [SerializeField] private JourneyStage currentStage;
    [SerializeField] private TransportDestination transportDestination;
    [SerializeField] private bool hasLuggage;

    public JourneyDirection Direction => direction;
    public JourneyStage CurrentStage => currentStage;
    public TransportDestination CurrentTransportDestination => transportDestination;
    public bool HasLuggage => hasLuggage;

    /// <summary>
    /// Starts an arriving passenger's journey after they have been created
    /// from aircraft deboarding. Baggage claim is only mandatory when luggage
    /// is present; passengers without luggage proceed directly to transport.
    /// </summary>
    public void InitializeArrival(bool passengerHasLuggage)
    {
        direction = JourneyDirection.Arrival;
        hasLuggage = passengerHasLuggage;
        transportDestination = TransportDestination.None;
        currentStage = JourneyStage.Aircraft;
        AdvancePastAircraft();
    }

    /// <summary>
    /// Advances from the aircraft stage. The baggage stage is skipped when
    /// the passenger has no luggage.
    /// </summary>
    public void AdvancePastAircraft()
    {
        if (direction != JourneyDirection.Arrival ||
            currentStage != JourneyStage.Aircraft)
            return;

        currentStage = hasLuggage
            ? JourneyStage.BaggageClaim
            : JourneyStage.AwaitingTransport;
    }

    /// <summary>
    /// Completes the future baggage-claim stage and makes transport the next
    /// mandatory destination. No current passenger is sent here unless their
    /// luggage flag is true.
    /// </summary>
    public bool CompleteBaggageClaim()
    {
        if (direction != JourneyDirection.Arrival ||
            currentStage != JourneyStage.BaggageClaim)
            return false;

        currentStage = JourneyStage.AwaitingTransport;
        return true;
    }

    public bool BeginTransport(TransportDestination destination)
    {
        if (direction != JourneyDirection.Arrival ||
            currentStage != JourneyStage.AwaitingTransport ||
            destination == TransportDestination.None)
            return false;

        transportDestination = destination;
        currentStage = JourneyStage.TravellingToTransport;
        return true;
    }

    public void ReachTransport()
    {
        if (currentStage == JourneyStage.TravellingToTransport)
            currentStage = JourneyStage.AtTransport;
    }

    public void CancelTransport()
    {
        if (currentStage != JourneyStage.TravellingToTransport)
            return;

        transportDestination = TransportDestination.None;
        currentStage = JourneyStage.AwaitingTransport;
    }

    public void CompleteTransportJourney()
    {
        if (currentStage == JourneyStage.AtTransport ||
            currentStage == JourneyStage.TravellingToTransport)
            currentStage = JourneyStage.Completed;
    }
}
