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
    }

    private void SubscribeToBusSpawner()
    {
        if (busSpawnerSubscribed || busSpawner == null)
            return;

        busSpawner.BusDispatched += HandleBusDispatched;
        busSpawnerSubscribed = true;
        foreach (KeyValuePair<BusController, List<BusPassengerRecord>> pair in busManifests)
            if (pair.Key != null)
                pair.Key.StopReached += HandleBusStopReached;
    }

    private void OnDisable()
    {
        if (busSpawnerSubscribed && busSpawner != null)
            busSpawner.BusDispatched -= HandleBusDispatched;
        busSpawnerSubscribed = false;

        foreach (KeyValuePair<BusController, List<BusPassengerRecord>> pair in busManifests)
            if (pair.Key != null)
                pair.Key.StopReached -= HandleBusStopReached;
    }

    private void OnEnable()
    {
        if (instance == this)
            SubscribeToBusSpawner();
    }

    private void HandleBusDispatched(BusController bus)
    {
        if (bus == null || scheduledAircraftSpawner == null ||
            publicTransportManager == null || airlinePassengerPrefab == null)
            return;

        long departureSecond = (long)System.Math.Round(GameTimeManager.SimulationTime);
        scheduledAircraftSpawner.GetPassengerFlightsForBusDeparture(
            departureSecond,
            publicTransportManager.BusFrequencyMinutes,
            dispatchFlights);

        if (dispatchFlights.Count == 0)
            return;

        List<BusPassengerRecord> manifest = new List<BusPassengerRecord>();
        busManifests[bus] = manifest;
        bus.StopReached += HandleBusStopReached;

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
            bookedPassengerCounts[flight.OperationId] = passengerCount;

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
            }
        }

        if (manifest.Count == 0)
        {
            bus.StopReached -= HandleBusStopReached;
            busManifests.Remove(bus);
        }
    }

    private void HandleBusStopReached(BusController bus, PlacedBuilding stop)
    {
        if (bus == null || stop == null || bus.FirstStop != stop ||
            !busManifests.TryGetValue(bus, out List<BusPassengerRecord> manifest))
            return;

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
        {
            bus.StopReached -= HandleBusStopReached;
            busManifests.Remove(bus);
        }
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

        AirportRatingManager ratingManager = AirportRatingManager.Instance;
        float airportRanking = ratingManager != null
            ? ratingManager.AirportRating
            : 50f;

        int bookedPassengers;
        if (!bookedPassengerCounts.TryGetValue(flightIdentity.OperationId, out bookedPassengers))
        {
            bookedPassengers = CalculateBookedPassengers(
                aircraftStats.maxPassengers,
                airportRanking,
                flightIdentity.PlannedStartMinute);
        }

        aircraftStats.SetOutgoingPassengers(bookedPassengers);
        return true;
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