using System.Collections.Generic;
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

        // Arrival stages.
        BaggageClaim,
        AwaitingTransport,
        TravellingToTransport,
        AtTransport,

        // Departure stages.
        CheckIn,
        BaggageDrop,
        BodySecurity,
        LuggageSecurity,
        DeparturesArea,
        BoardingGate,
        TravellingToBoardingGate,
        AtBoardingGate,

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
    /// Finds the nearest active building providing the requested passenger
    /// journey service. The buildable role identifies the service while the
    /// existing InteractionProvider supplies availability and interaction data.
    /// </summary>
    public static bool TryFindNearestStation(
        PassengerJourneyStationRole role,
        Vector3 worldPosition,
        out InteractionProvider provider,
        out PlacedBuilding building)
    {
        provider = null;
        building = null;

        if (role == PassengerJourneyStationRole.None)
            return false;

        float nearestDistanceSquared = float.PositiveInfinity;
        IReadOnlyList<PlacedBuilding> buildings = PlacedBuilding.ActiveBuildings;

        for (int i = 0; i < buildings.Count; i++)
        {
            PlacedBuilding candidateBuilding = buildings[i];
            if (candidateBuilding == null ||
                candidateBuilding.Definition == null ||
                candidateBuilding.Definition.passengerJourneyStationRole != role)
                continue;

            InteractionProvider candidateProvider =
                candidateBuilding.GetComponent<InteractionProvider>();
            if (candidateProvider == null || !candidateProvider.InteractionsEnabled)
                continue;

            bool hasAvailableInteraction = false;
            var definitions = candidateProvider.Interactions;
            for (int interactionIndex = 0;
                 interactionIndex < definitions.Count;
                 interactionIndex++)
            {
                if (candidateProvider.CanOffer(definitions[interactionIndex]))
                {
                    hasAvailableInteraction = true;
                    break;
                }
            }

            if (!hasAvailableInteraction)
                continue;

            float distanceSquared =
                (candidateProvider.InteractionPosition - worldPosition).sqrMagnitude;
            if (distanceSquared >= nearestDistanceSquared)
                continue;

            nearestDistanceSquared = distanceSquared;
            provider = candidateProvider;
            building = candidateBuilding;
        }

        return provider != null;
    }

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

    /// <summary>Starts a departing passenger at the check-in stage.</summary>
    public void InitializeDeparture(bool passengerHasLuggage)
    {
        direction = JourneyDirection.Departure;
        hasLuggage = passengerHasLuggage;
        transportDestination = TransportDestination.None;
        currentStage = JourneyStage.CheckIn;
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
        if (direction == JourneyDirection.Arrival &&
            (currentStage == JourneyStage.AtTransport ||
             currentStage == JourneyStage.TravellingToTransport))
            currentStage = JourneyStage.Completed;
    }

    public bool CompleteCheckIn()
    {
        if (direction != JourneyDirection.Departure ||
            currentStage != JourneyStage.CheckIn)
            return false;

        currentStage = hasLuggage
            ? JourneyStage.BaggageDrop
            : JourneyStage.BodySecurity;
        return true;
    }

    public bool CompleteBaggageDrop()
    {
        if (direction != JourneyDirection.Departure ||
            !hasLuggage ||
            currentStage != JourneyStage.BaggageDrop)
            return false;

        currentStage = JourneyStage.BodySecurity;
        return true;
    }

    public bool CompleteBodySecurity()
    {
        if (direction != JourneyDirection.Departure ||
            currentStage != JourneyStage.BodySecurity)
            return false;

        currentStage = hasLuggage
            ? JourneyStage.LuggageSecurity
            : JourneyStage.DeparturesArea;
        return true;
    }

    public bool CompleteLuggageSecurity()
    {
        if (direction != JourneyDirection.Departure ||
            !hasLuggage ||
            currentStage != JourneyStage.LuggageSecurity)
            return false;

        currentStage = JourneyStage.DeparturesArea;
        return true;
    }

    public bool BeginBoardingGateJourney()
    {
        if (direction != JourneyDirection.Departure ||
            currentStage != JourneyStage.DeparturesArea)
            return false;

        currentStage = JourneyStage.TravellingToBoardingGate;
        return true;
    }

    public bool ReachBoardingGate()
    {
        if (direction != JourneyDirection.Departure ||
            currentStage != JourneyStage.TravellingToBoardingGate)
            return false;

        currentStage = JourneyStage.AtBoardingGate;
        return true;
    }

    public bool CompleteDepartureJourney()
    {
        if (direction != JourneyDirection.Departure ||
            currentStage != JourneyStage.AtBoardingGate)
            return false;

        currentStage = JourneyStage.Completed;
        return true;
    }
}
