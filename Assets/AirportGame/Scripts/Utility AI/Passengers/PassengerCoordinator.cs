using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Calculates passenger demand for scheduled passenger flights.
/// </summary>
[DisallowMultipleComponent]
public class PassengerCoordinator : MonoBehaviour
{
    [Header("Demand Settings")]
    [SerializeField, Range(0f, 1f)] private float baseOccupancy = 0.30f;
    [SerializeField] private float minimumRandomVariation = 0.85f;
    [SerializeField] private float maximumRandomVariation = 1.15f;
    [SerializeField] private bool logToConsole;

    [Header("Airline Bus Transport")]
    [SerializeField] private GameObject airlinePassengerPrefab;
    [SerializeField] private ScheduledAircraftSpawner scheduledAircraftSpawner;
    [SerializeField] private BusSpawner busSpawner;
    [SerializeField] private PublicTransportManager publicTransportManager;
    [SerializeField] private AircraftGateRegistry gateRegistry;

    private readonly List<ScheduledPassengerFlight> dispatchFlights =
        new List<ScheduledPassengerFlight>();
    private readonly Dictionary<string, int> bookedPassengerCounts =
        new Dictionary<string, int>();
    private readonly Dictionary<BusController, List<BusPassengerRecord>> busManifests =
        new Dictionary<BusController, List<BusPassengerRecord>>();
    private readonly Queue<AirlinePassengerAgent> waitingReturnPassengers =
        new Queue<AirlinePassengerAgent>();
    private readonly HashSet<AirlinePassengerAgent> waitingReturnPassengerSet =
        new HashSet<AirlinePassengerAgent>();
    private readonly Dictionary<BusController, List<AirlinePassengerAgent>> returnPassengersByBus =
        new Dictionary<BusController, List<AirlinePassengerAgent>>();
    private readonly HashSet<BusController> subscribedBuses =
        new HashSet<BusController>();
    private readonly HashSet<AirlinePassengerAgent> walkingToBusStopPassengers =
        new HashSet<AirlinePassengerAgent>();
    private bool busSpawnerSubscribed;

    private sealed class BusPassengerRecord
    {
        public AirlinePassengerAgent Agent;
        public string GateStableId;
    }

    private static PassengerCoordinator instance;

    public static PassengerCoordinator Instance
    {
        get
        {
            if (instance == null)
                instance = FindAnyObjectByType<PassengerCoordinator>();
            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogError("Only one PassengerCoordinator may be active.", this);
            enabled = false;
            return;
        }

        instance = this;
    }

    private void Start()
    {
        if (scheduledAircraftSpawner == null)
            scheduledAircraftSpawner = FindAnyObjectByType<ScheduledAircraftSpawner>();
        if (busSpawner == null)
            busSpawner = FindAnyObjectByType<BusSpawner>();
        if (publicTransportManager == null)
            publicTransportManager = FindAnyObjectByType<PublicTransportManager>();
        if (gateRegistry == null)
            gateRegistry = FindAnyObjectByType<AircraftGateRegistry>();

        SubscribeToBusSpawner();
        if (busSpawner == null)
            Debug.LogError("PassengerCoordinator: BusSpawner is missing; airline bus manifests cannot be loaded.", this);

        if (airlinePassengerPrefab == null)
            Debug.LogError("PassengerCoordinator: Assign the airline Passenger prefab. SS_Passenger is reserved for sightseeing.", this);
        else if (airlinePassengerPrefab.GetComponent<AirlinePassengerAgent>() == null)
            Debug.LogError("PassengerCoordinator: The assigned Passenger prefab must contain AirlinePassengerAgent.", airlinePassengerPrefab);
    }

    private void SubscribeToBusSpawner()
    {
        if (busSpawnerSubscribed || busSpawner == null)
            return;

        busSpawner.BusDispatched += HandleBusDispatched;
        busSpawnerSubscribed = true;
        foreach (KeyValuePair<BusController, List<BusPassengerRecord>> pair in busManifests)
            if (pair.Key != null)
                SubscribeToBus(pair.Key);
        foreach (KeyValuePair<BusController, List<AirlinePassengerAgent>> pair in returnPassengersByBus)
            if (pair.Key != null)
                SubscribeToBus(pair.Key);
    }

    private void OnDisable()
    {
        if (busSpawnerSubscribed && busSpawner != null)
            busSpawner.BusDispatched -= HandleBusDispatched;
        busSpawnerSubscribed = false;
        AircraftTurnaroundController.PassengersDeboarded -= HandlePassengersDeboarded;

        foreach (BusController bus in subscribedBuses)
        {
            if (bus == null)
                continue;
            bus.StopReached -= HandleBusStopReached;
            bus.JourneyCompleted -= HandleBusJourneyCompleted;
        }

        foreach (AirlinePassengerAgent passenger in walkingToBusStopPassengers)
            if (passenger != null)
                passenger.ReachedBusStop -= HandlePassengerReachedBusStop;
    }

    private void OnEnable()
    {
        if (instance == this)
        {
            AircraftTurnaroundController.PassengersDeboarded += HandlePassengersDeboarded;
            SubscribeToBusSpawner();

            foreach (BusController bus in subscribedBuses)
            {
                if (bus == null)
                    continue;
                bus.StopReached += HandleBusStopReached;
                bus.JourneyCompleted += HandleBusJourneyCompleted;
            }

            foreach (AirlinePassengerAgent passenger in walkingToBusStopPassengers)
                if (passenger != null)
                    passenger.ReachedBusStop += HandlePassengerReachedBusStop;
        }
    }

    private void SubscribeToBus(BusController bus)
    {
        if (bus == null || !subscribedBuses.Add(bus))
            return;

        bus.StopReached += HandleBusStopReached;
        bus.JourneyCompleted += HandleBusJourneyCompleted;
    }

    private void HandleBusDispatched(BusController bus)
    {
        if (bus == null)
            return;

        SubscribeToBus(bus);
        if (scheduledAircraftSpawner == null || publicTransportManager == null ||
            airlinePassengerPrefab == null)
            return;

        double departureSecond = GameTimeManager.SimulationTime;
        scheduledAircraftSpawner.GetPassengerFlightsForBusDeparture(
            departureSecond,
            publicTransportManager.BusFrequencyMinutes,
            dispatchFlights);

        if (dispatchFlights.Count == 0)
            return;

        List<BusPassengerRecord> manifest = new List<BusPassengerRecord>();
        busManifests[bus] = manifest;

        for (int flightIndex = 0; flightIndex < dispatchFlights.Count; flightIndex++)
        {
            ScheduledPassengerFlight flight = dispatchFlights[flightIndex];
            GameObject aircraftPrefab = flight.Definition.OperationType == FlightOperationType.PassengerOnly
                ? scheduledAircraftSpawner.PassengerAircraftPrefab
                : scheduledAircraftSpawner.CargoAndPassengersAircraftPrefab;
            AircraftStats aircraftStats = aircraftPrefab != null
                ? aircraftPrefab.GetComponent<AircraftStats>()
                : null;

            int aircraftCapacity = aircraftStats != null ? aircraftStats.maxPassengers : 0;
            float airportRanking = AirportRatingManager.Instance != null
                ? AirportRatingManager.Instance.AirportRating
                : 50f;
            int requestedPassengers = CalculateBookedPassengers(
                aircraftCapacity, airportRanking, flight.StartMinute);
            int passengerCount = Mathf.Min(requestedPassengers, bus.AvailablePassengerCapacity);
            int createdPassengerCount = 0;

            for (int passengerIndex = 0; passengerIndex < passengerCount; passengerIndex++)
            {
                Vector3 spawnPosition = bus.transform.position + Vector3.up * 0.1f;
                GameObject passengerObject = Instantiate(
                    airlinePassengerPrefab, spawnPosition, Quaternion.identity);
                AirlinePassengerAgent agent = passengerObject.GetComponent<AirlinePassengerAgent>();

                if (agent == null || !agent.Initialize(
                    flight.OperationId, PassengerIdentity.TransportMode.Bus) ||
                    !bus.TryBoardPassenger(agent))
                {
                    if (passengerObject != null)
                        Destroy(passengerObject);
                    continue;
                }

                manifest.Add(new BusPassengerRecord
                {
                    Agent = agent,
                    GateStableId = flight.GateStableId
                });
                createdPassengerCount++;
            }

            bookedPassengerCounts[flight.OperationId] = createdPassengerCount;
        }

        if (manifest.Count == 0)
            busManifests.Remove(bus);
    }

    private void HandleBusStopReached(BusController bus, PlacedBuilding stop)
    {
        if (bus == null || stop == null || bus.FirstStop != stop)
            return;

        if (busManifests.TryGetValue(bus, out List<BusPassengerRecord> manifest))
        {
            for (int i = manifest.Count - 1; i >= 0; i--)
        {
            BusPassengerRecord record = manifest[i];
            if (record == null || record.Agent == null)
            {
                manifest.RemoveAt(i);
                continue;
            }

            if (gateRegistry == null ||
                !gateRegistry.TryGetGate(record.GateStableId, out AircraftGate gate) ||
                gate == null)
            {
                Debug.LogWarning(
                    $"PassengerCoordinator: Could not find gate '{record.GateStableId}' for flight {record.Agent.FlightOperationId}; passenger remains on the bus.",
                    this);
                continue;
            }

            if (bus.DisembarkPassengerToTerminal(record.Agent, gate.transform.position))
                manifest.RemoveAt(i);
        }

            if (manifest.Count == 0)
                busManifests.Remove(bus);
        }

        BoardWaitingReturnPassengers(bus);
    }

    private void BoardWaitingReturnPassengers(BusController bus)
    {
        if (bus == null || waitingReturnPassengers.Count == 0 ||
            bus.AvailablePassengerCapacity <= 0)
            return;

        List<AirlinePassengerAgent> onboardReturnPassengers;
        if (!returnPassengersByBus.TryGetValue(bus, out onboardReturnPassengers))
        {
            onboardReturnPassengers = new List<AirlinePassengerAgent>();
            returnPassengersByBus.Add(bus, onboardReturnPassengers);
        }

        while (bus.AvailablePassengerCapacity > 0 && waitingReturnPassengers.Count > 0)
        {
            AirlinePassengerAgent passenger = waitingReturnPassengers.Dequeue();
            if (passenger == null)
                continue;

            waitingReturnPassengerSet.Remove(passenger);
            if (passenger.State != AirlinePassengerAgent.JourneyState.WaitingForBus)
                continue;

            if (!bus.TryBoardPassenger(passenger))
            {
                waitingReturnPassengers.Enqueue(passenger);
                waitingReturnPassengerSet.Add(passenger);
                break;
            }

            onboardReturnPassengers.Add(passenger);
        }

        if (onboardReturnPassengers.Count == 0)
            returnPassengersByBus.Remove(bus);
    }

    private void HandleBusJourneyCompleted(BusController bus)
    {
        if (bus == null)
            return;

        if (returnPassengersByBus.TryGetValue(bus, out List<AirlinePassengerAgent> passengers))
        {
            for (int i = 0; i < passengers.Count; i++)
                if (passengers[i] != null)
                    passengers[i].CompleteReturnBusJourney();
            returnPassengersByBus.Remove(bus);
        }

        bus.StopReached -= HandleBusStopReached;
        bus.JourneyCompleted -= HandleBusJourneyCompleted;
        subscribedBuses.Remove(bus);
    }

    private void HandlePassengersDeboarded(AircraftTurnaroundController turnaround)
    {
        if (turnaround == null || airlinePassengerPrefab == null || busSpawner == null ||
            gateRegistry == null)
            return;

        AircraftFlightIdentity identity = turnaround.GetComponent<AircraftFlightIdentity>();
        AircraftStats stats = turnaround.GetComponent<AircraftStats>();
        if (identity == null || !identity.IsInitialized || identity.Definition == null ||
            stats == null || stats.IncomingPassengers <= 0 ||
            (identity.Definition.OperationType != FlightOperationType.PassengerOnly &&
             identity.Definition.OperationType != FlightOperationType.CargoAndPassengers))
            return;

        string gateId = identity.HasActualGate
            ? identity.ActualGateStableId
            : identity.PlannedGateStableId;
        if (!gateRegistry.TryGetGate(gateId, out AircraftGate gate) || gate == null)
            return;

        PlacedBuilding firstStop = busSpawner.GetNextFirstStop();
        if (firstStop == null)
            return;

        int passengerCount = stats.IncomingPassengers;
        for (int i = 0; i < passengerCount; i++)
        {
            float angle = i * 2.39996323f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.65f;
            GameObject passengerObject = Instantiate(
                airlinePassengerPrefab, gate.transform.position + offset, Quaternion.identity);
            AirlinePassengerAgent passenger = passengerObject.GetComponent<AirlinePassengerAgent>();

            if (passenger == null || !passenger.Initialize(
                identity.OperationId, PassengerIdentity.TransportMode.Bus))
            {
                Destroy(passengerObject);
                continue;
            }

            walkingToBusStopPassengers.Add(passenger);
            passenger.ReachedBusStop += HandlePassengerReachedBusStop;
            Vector3 stopOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.5f;
            if (!passenger.WalkToBusStop(firstStop.transform.position + stopOffset))
            {
                passenger.ReachedBusStop -= HandlePassengerReachedBusStop;
                walkingToBusStopPassengers.Remove(passenger);
                Destroy(passengerObject);
            }
        }
    }

    private void HandlePassengerReachedBusStop(AirlinePassengerAgent passenger)
    {
        if (passenger == null)
            return;

        passenger.ReachedBusStop -= HandlePassengerReachedBusStop;
        walkingToBusStopPassengers.Remove(passenger);
        if (passenger.State == AirlinePassengerAgent.JourneyState.WaitingForBus &&
            waitingReturnPassengerSet.Add(passenger))
            waitingReturnPassengers.Enqueue(passenger);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>
    /// Calculates and assigns the booked outgoing passengers for a scheduled
    /// PassengerOnly or CargoAndPassengers flight. Flight-level passenger
    /// setup is owned by this coordinator rather than the aircraft spawner.
    /// </summary>
    public bool ConfigureScheduledFlightPassengers(
        AircraftStats aircraftStats,
        AircraftFlightIdentity flightIdentity)
    {
        if (aircraftStats == null || flightIdentity == null ||
            !flightIdentity.IsInitialized ||
            flightIdentity.Definition == null ||
            (flightIdentity.Definition.OperationType != FlightOperationType.PassengerOnly &&
             flightIdentity.Definition.OperationType != FlightOperationType.CargoAndPassengers))
        {
            return false;
        }

        if (!bookedPassengerCounts.TryGetValue(flightIdentity.OperationId, out int bookedPassengers))
        {
            bookedPassengers = 0;
            Debug.LogWarning(
                $"PassengerCoordinator: No bus manifest was registered for flight {flightIdentity.FlightNumber}; outgoing passenger count is set to zero.",
                aircraftStats);
        }

        aircraftStats.SetOutgoingPassengers(bookedPassengers);
        return true;
    }

    public void ForgetScheduledFlightPassengerCount(string operationId)
    {
        if (!string.IsNullOrWhiteSpace(operationId))
            bookedPassengerCounts.Remove(operationId);
    }

    public int CalculateBookedPassengers(int capacity, float airportRanking, int scheduledStartMinute)
    {
        if (capacity <= 0)
            return 0;

        float ranking = Mathf.Clamp(airportRanking, 0f, 100f);
        float rankingOccupancy = baseOccupancy + 0.005625f * ranking;
        float timeMultiplier = GetTimeOfDayMultiplier(scheduledStartMinute);
        float variation = Random.Range(
            minimumRandomVariation,
            maximumRandomVariation);

        float demand = capacity * rankingOccupancy * timeMultiplier * variation;
        int bookedPassengers = Mathf.Clamp(Mathf.RoundToInt(demand), 0, capacity);

        if (logToConsole)
        {
            Debug.Log(
                $"Passenger demand | Capacity: {capacity} | Ranking: {ranking:0.0} | " +
                $"Base occupancy: {rankingOccupancy:P1} | Time multiplier: {timeMultiplier:0.00} | " +
                $"Variation: {variation:0.00} | Booked: {bookedPassengers}",
                this);
        }

        return bookedPassengers;
    }

    private float GetTimeOfDayMultiplier(int minuteOfDay)
    {
        int minute = ((minuteOfDay % 1440) + 1440) % 1440;
        int hour = minute / 60;

        if (hour >= 5 && hour < 8) return 1.15f;   // Early morning
        if (hour >= 8 && hour < 11) return 1.25f;  // Morning
        if (hour >= 11 && hour < 14) return 1.50f; // Midday
        if (hour >= 14 && hour < 17) return 1.00f; // Afternoon
        if (hour >= 17 && hour < 22) return 1.10f; // Evening
        return 0.50f;                               // Late night
    }
}