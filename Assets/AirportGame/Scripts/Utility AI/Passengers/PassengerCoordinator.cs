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
    [SerializeField] private PedestrianPathfinder pedestrianPathfinder;

    private readonly List<ScheduledPassengerFlight> dispatchFlights =
        new List<ScheduledPassengerFlight>();
    private readonly Dictionary<string, int> bookedPassengerCounts =
        new Dictionary<string, int>();
    private readonly Dictionary<string, int> registeredPassengerCounts =
        new Dictionary<string, int>();
    private readonly Dictionary<string, int> registeredIncomingPassengerCounts =
        new Dictionary<string, int>();
    private readonly List<ScheduledPassengerFlight> registrationFlights =
        new List<ScheduledPassengerFlight>();
    private readonly HashSet<string> currentRegistrationIds =
        new HashSet<string>();
    private GameTimeManager subscribedGameTimeManager;
    private bool subscribedToScheduleReload;
    private readonly Dictionary<string, List<AirlinePassengerAgent>> terminalPassengersByOperationId =
        new Dictionary<string, List<AirlinePassengerAgent>>();
    private readonly Dictionary<BusController, List<BusPassengerRecord>> busManifests =
        new Dictionary<BusController, List<BusPassengerRecord>>();
    private readonly Queue<AirlinePassengerAgent> waitingReturnPassengers =
        new Queue<AirlinePassengerAgent>();
    private readonly HashSet<AirlinePassengerAgent> waitingReturnPassengerSet =
        new HashSet<AirlinePassengerAgent>();
    private readonly Dictionary<BusController, List<AirlinePassengerAgent>> returnPassengersByBus =
        new Dictionary<BusController, List<AirlinePassengerAgent>>();
    private readonly HashSet<AirlinePassengerAgent> queuedForBusDisembarking =
        new HashSet<AirlinePassengerAgent>();
    private readonly HashSet<BusController> subscribedBuses =
        new HashSet<BusController>();
    private readonly HashSet<AirlinePassengerAgent> walkingToBusStopPassengers =
        new HashSet<AirlinePassengerAgent>();
    private bool busSpawnerSubscribed;

    private readonly Queue<IncomingPassengerSpawnJob> incomingPassengerSpawnJobs =
        new Queue<IncomingPassengerSpawnJob>();
    private IncomingPassengerSpawnJob activeIncomingPassengerSpawnJob;
    private float incomingPassengerSpawnIntervalRemaining;

    private readonly Queue<OutgoingPassengerBoardingJob> outgoingPassengerBoardingJobs =
        new Queue<OutgoingPassengerBoardingJob>();
    private OutgoingPassengerBoardingJob activeOutgoingPassengerBoardingJob;
    private float outgoingPassengerBoardingIntervalRemaining;

    private sealed class OutgoingPassengerBoardingJob
    {
        public AircraftTurnaroundController Turnaround;
        public Transform AircraftTransform;
        public readonly List<AirlinePassengerAgent> Passengers =
            new List<AirlinePassengerAgent>();
        public int NextPassengerIndex;
        public int BoardedCount;
    }

    private sealed class IncomingPassengerSpawnJob
    {
        public AircraftTurnaroundController Turnaround;
        public string OperationId;
        public string FlightNumber;
        public int PassengerCount;
        public int NextPassengerIndex;
        public int SpawnedCount;
        public int RoutedCount;
        public int FailedCount;
        public AircraftGate Gate;
        public PlacedBuilding GateFootprint;
        public PlacedBuilding FirstStop;
    }


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

    private void Update()
    {
        float deltaGameSeconds = GameTimeManager.SimulationDeltaTime;

        if (activeIncomingPassengerSpawnJob == null &&
            incomingPassengerSpawnJobs.Count > 0)
        {
            activeIncomingPassengerSpawnJob = incomingPassengerSpawnJobs.Dequeue();
            incomingPassengerSpawnIntervalRemaining = 1f;
        }

        if (activeIncomingPassengerSpawnJob != null)
        {
            incomingPassengerSpawnIntervalRemaining -= deltaGameSeconds;
            const int maxSpawnsPerUpdate = 32;
            for (int i = 0; i < maxSpawnsPerUpdate &&
                            activeIncomingPassengerSpawnJob != null &&
                            incomingPassengerSpawnIntervalRemaining <= 0f; i++)
            {
                SpawnNextIncomingPassenger(activeIncomingPassengerSpawnJob);
                if (activeIncomingPassengerSpawnJob != null &&
                    activeIncomingPassengerSpawnJob.NextPassengerIndex >=
                    activeIncomingPassengerSpawnJob.PassengerCount)
                {
                    CompleteIncomingPassengerSpawnJob();
                }

                if (activeIncomingPassengerSpawnJob != null)
                    incomingPassengerSpawnIntervalRemaining += 1f;
                else if (incomingPassengerSpawnJobs.Count > 0)
                {
                    activeIncomingPassengerSpawnJob = incomingPassengerSpawnJobs.Dequeue();
                    incomingPassengerSpawnIntervalRemaining += 1f;
                }
            }
        }

        ProcessOutgoingPassengerBoarding(deltaGameSeconds);
    }

    private void ProcessOutgoingPassengerBoarding(float deltaGameSeconds)
    {
        if (activeOutgoingPassengerBoardingJob == null)
        {
            if (outgoingPassengerBoardingJobs.Count == 0)
                return;

            activeOutgoingPassengerBoardingJob =
                outgoingPassengerBoardingJobs.Dequeue();
            outgoingPassengerBoardingIntervalRemaining = 1f;
        }

        outgoingPassengerBoardingIntervalRemaining -= deltaGameSeconds;
        const int maxBoardingsPerUpdate = 32;
        for (int i = 0; i < maxBoardingsPerUpdate &&
                        activeOutgoingPassengerBoardingJob != null &&
                        outgoingPassengerBoardingIntervalRemaining <= 0f; i++)
        {
            OutgoingPassengerBoardingJob job =
                activeOutgoingPassengerBoardingJob;
            AirlinePassengerAgent passenger =
                job.Passengers[job.NextPassengerIndex];

            if (passenger != null &&
                passenger.State == AirlinePassengerAgent.JourneyState.WalkingToTerminal)
            {
                // Keep the current passenger in the queue until they physically
                // reach the gate. Do not count a walking passenger as boarded.
                outgoingPassengerBoardingIntervalRemaining = 0f;
                return;
            }

            job.NextPassengerIndex++;
            if (passenger != null &&
                passenger.State == AirlinePassengerAgent.JourneyState.AtTerminal &&
                passenger.BoardAircraft(job.AircraftTransform))
            {
                job.BoardedCount++;
            }

            if (job.NextPassengerIndex >= job.Passengers.Count)
            {
                if (job.Turnaround != null)
                    job.Turnaround.CompleteExternalBoarding();

                activeOutgoingPassengerBoardingJob = null;
                if (outgoingPassengerBoardingJobs.Count > 0)
                {
                    activeOutgoingPassengerBoardingJob =
                        outgoingPassengerBoardingJobs.Dequeue();
                    outgoingPassengerBoardingIntervalRemaining += 1f;
                }
            }
            else
            {
                outgoingPassengerBoardingIntervalRemaining += 1f;
            }
        }
    }

    private void HandlePassengersBoardingStarted(
        AircraftTurnaroundController turnaround)
    {
        if (turnaround == null)
            return;

        AircraftFlightIdentity identity =
            turnaround.GetComponent<AircraftFlightIdentity>();
        AircraftStats stats = turnaround.GetComponent<AircraftStats>();

        if (identity == null || !identity.IsInitialized ||
            stats == null || stats.OutgoingPassengers <= 0 ||
            !terminalPassengersByOperationId.TryGetValue(
                identity.OperationId,
                out List<AirlinePassengerAgent> terminalPassengers))
        {
            turnaround.CompleteExternalBoarding();
            return;
        }

        OutgoingPassengerBoardingJob job =
            new OutgoingPassengerBoardingJob
            {
                Turnaround = turnaround,
                AircraftTransform = turnaround.transform
            };

        int requestedPassengers = stats.OutgoingPassengers;
        for (int i = 0; i < terminalPassengers.Count &&
                        job.Passengers.Count < requestedPassengers; i++)
        {
            AirlinePassengerAgent passenger = terminalPassengers[i];
            if (passenger != null &&
                (passenger.State == AirlinePassengerAgent.JourneyState.AtTerminal ||
                 passenger.State == AirlinePassengerAgent.JourneyState.WalkingToTerminal))
            {
                job.Passengers.Add(passenger);
            }
        }

        terminalPassengersByOperationId.Remove(identity.OperationId);

        if (job.Passengers.Count == 0)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: No terminal passengers were ready to board flight {identity.FlightNumber}; completing its boarding task without passenger objects.",
                turnaround);
            turnaround.CompleteExternalBoarding();
            return;
        }

        if (job.Passengers.Count < requestedPassengers)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Flight {identity.FlightNumber} has {requestedPassengers} booked passenger(s), but only {job.Passengers.Count} passenger object(s) reached the terminal. Boarding the available passengers.",
                turnaround);
        }

        outgoingPassengerBoardingJobs.Enqueue(job);
    }

    private void SpawnNextIncomingPassenger(IncomingPassengerSpawnJob job)
    {
        int passengerIndex = job.NextPassengerIndex++;
        if (job.Gate == null)
        {
            job.FailedCount++;
            return;
        }

        Vector3 spawnPosition = job.Gate.transform.position;
        GameObject passengerObject = Instantiate(
            airlinePassengerPrefab, spawnPosition, Quaternion.identity);
        AirlinePassengerAgent passenger = passengerObject.GetComponent<AirlinePassengerAgent>();

        if (passenger == null || !passenger.Initialize(
            job.OperationId, PassengerIdentity.TransportMode.Bus))
        {
            job.FailedCount++;
            Debug.LogWarning(
                $"PassengerCoordinator: Failed to initialize arriving passenger {passengerIndex + 1}/{job.PassengerCount} for flight {job.FlightNumber}. " +
                $"Prefab: '{airlinePassengerPrefab.name}'. Check that it has a working AirlinePassengerAgent.",
                passengerObject);
            Destroy(passengerObject);
            return;
        }

        job.SpawnedCount++;
        if (passenger.Movement != null)
            passenger.Movement.AlignFeetToGround(spawnPosition);

        if (job.FirstStop == null)
            return;

        Vector3 waitingPosition;
        if (pedestrianPathfinder == null ||
            !pedestrianPathfinder.TryFindBusStopWaitingPosition(
                job.FirstStop.transform.position,
                Random.Range(-2.5f, 2.5f),
                Random.Range(1.1f, 2.2f),
                8,
                out waitingPosition))
        {
            job.FailedCount++;
            Destroy(passengerObject);
            return;
        }

        walkingToBusStopPassengers.Add(passenger);
        passenger.ReachedBusStop += HandlePassengerReachedBusStop;
        if (!passenger.WalkToBusStop(
                waitingPosition,
                job.GateFootprint,
                job.FirstStop))
        {
            passenger.ReachedBusStop -= HandlePassengerReachedBusStop;
            walkingToBusStopPassengers.Remove(passenger);
            job.FailedCount++;
            Debug.LogWarning(
                $"PassengerCoordinator: Passenger for flight {job.FlightNumber} spawned at gate '{job.Gate.name}' but could not start walking to bus stop '{job.FirstStop.name}'.",
                passengerObject);
            Destroy(passengerObject);
            return;
        }

        job.RoutedCount++;
    }

    private void CompleteIncomingPassengerSpawnJob()
    {
        IncomingPassengerSpawnJob job = activeIncomingPassengerSpawnJob;
        if (job == null)
            return;

        Debug.Log(
            $"PassengerCoordinator: Arriving passenger result for flight {job.FlightNumber}: " +
            $"requested {job.PassengerCount}, spawned {job.SpawnedCount}, walking to bus stop {job.RoutedCount}, failed {job.FailedCount}.",
            job.Turnaround);

        activeIncomingPassengerSpawnJob = null;
        if (job.Turnaround != null)
            job.Turnaround.CompleteExternalDeboarding();
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
        if (pedestrianPathfinder == null)
            pedestrianPathfinder = FindAnyObjectByType<PedestrianPathfinder>();

        SubscribeToBusSpawner();
        SubscribeToScheduleAndTime();
        RefreshScheduledPassengerRegistrations();
        if (busSpawner == null)
            Debug.LogError("PassengerCoordinator: BusSpawner is missing; airline bus manifests cannot be loaded.", this);

        if (gateRegistry == null)
            Debug.LogError("PassengerCoordinator: AircraftGateRegistry is missing; arriving passengers cannot be routed from their gate.", this);

        if (airlinePassengerPrefab == null)
            Debug.LogError("PassengerCoordinator: Assign the airline Passenger prefab. SS_Passenger is reserved for sightseeing.", this);
        else if (airlinePassengerPrefab.GetComponent<AirlinePassengerAgent>() == null)
            Debug.LogError("PassengerCoordinator: The assigned Passenger prefab must contain AirlinePassengerAgent.", airlinePassengerPrefab);
    }

    private void SubscribeToScheduleAndTime()
    {
        if (scheduledAircraftSpawner != null && !subscribedToScheduleReload)
        {
            scheduledAircraftSpawner.PassengerScheduleReloaded +=
                HandlePassengerScheduleReloaded;
            subscribedToScheduleReload = true;
        }

        if (subscribedGameTimeManager == null)
            subscribedGameTimeManager = GameTimeManager.Instance;
        if (subscribedGameTimeManager != null)
            subscribedGameTimeManager.GameTimeAdvanced -= HandlePassengerRegistrationTimeAdvanced;
        if (subscribedGameTimeManager != null)
            subscribedGameTimeManager.GameTimeAdvanced += HandlePassengerRegistrationTimeAdvanced;
    }

    private void HandlePassengerScheduleReloaded()
    {
        registeredPassengerCounts.Clear();
        RefreshScheduledPassengerRegistrations();
    }

    private void HandlePassengerRegistrationTimeAdvanced(long previousAbsoluteSecond, long currentAbsoluteSecond)
    {
        if (currentAbsoluteSecond / 86400L != previousAbsoluteSecond / 86400L)
            RefreshScheduledPassengerRegistrations();
    }

    private void RefreshScheduledPassengerRegistrations()
    {
        if (scheduledAircraftSpawner == null || publicTransportManager == null)
            return;

        long currentDayIndex = GameTimeManager.Instance != null
            ? GameTimeManager.Instance.CurrentDay - 1
            : (long)System.Math.Floor(GameTimeManager.SimulationTime / 86400.0);
        double now = GameTimeManager.SimulationTime;
        double intervalSeconds = System.Math.Max(60.0, publicTransportManager.BusFrequencyMinutes * 60.0);
        float airportRanking = AirportRatingManager.Instance != null
            ? AirportRatingManager.Instance.AirportRating
            : 50f;

        scheduledAircraftSpawner.GetUpcomingPassengerFlights(
            currentDayIndex, currentDayIndex + 2, registrationFlights);

        currentRegistrationIds.Clear();
        for (int i = 0; i < registrationFlights.Count; i++)
        {
            ScheduledPassengerFlight flight = registrationFlights[i];
            if (string.IsNullOrWhiteSpace(flight.OperationId))
                continue;

            long flightAbsoluteSecond =
                flight.ScheduledDayIndex * 86400L + flight.StartMinute * 60L;
            double assignedBusDeparture =
                System.Math.Floor((flightAbsoluteSecond - 3600L) / intervalSeconds) * intervalSeconds;
            if (assignedBusDeparture + 0.01 < now)
                continue;

            currentRegistrationIds.Add(flight.OperationId);
            if (registeredPassengerCounts.ContainsKey(flight.OperationId))
                continue;

            GameObject aircraftPrefab =
                flight.Definition.OperationType == FlightOperationType.PassengerOnly
                    ? scheduledAircraftSpawner.PassengerAircraftPrefab
                    : scheduledAircraftSpawner.CargoAndPassengersAircraftPrefab;
            AircraftStats aircraftStats = aircraftPrefab != null
                ? aircraftPrefab.GetComponent<AircraftStats>()
                : null;
            int capacity = aircraftStats != null ? aircraftStats.maxPassengers : 0;
            registeredPassengerCounts[flight.OperationId] =
                CalculateBookedPassengers(capacity, airportRanking, flight.StartMinute);
            registeredIncomingPassengerCounts[flight.OperationId] =
                CalculateBookedPassengers(capacity, airportRanking, flight.StartMinute);
        }

        RemoveStalePassengerRegistrations(currentRegistrationIds);
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
        AircraftTurnaroundController.PassengersDeboardingStarted -= HandlePassengersDeboardingStarted;
        AircraftTurnaroundController.PassengersBoardingStarted -= HandlePassengersBoardingStarted;
        if (subscribedToScheduleReload && scheduledAircraftSpawner != null)
            scheduledAircraftSpawner.PassengerScheduleReloaded -= HandlePassengerScheduleReloaded;
        subscribedToScheduleReload = false;
        if (subscribedGameTimeManager != null)
            subscribedGameTimeManager.GameTimeAdvanced -= HandlePassengerRegistrationTimeAdvanced;
        subscribedGameTimeManager = null;

        foreach (BusController bus in subscribedBuses)
        {
            if (bus == null)
                continue;
            bus.StopReached -= HandleBusStopReached;
            bus.JourneyCompleted -= HandleBusJourneyCompleted;
            bus.StopServiceCompleted -= HandleBusStopServiceCompleted;
            bus.PassengerBoardingFailed -= HandlePassengerBoardingFailed;
            bus.PassengerDisembarked -= HandleBusPassengerDisembarked;
            bus.DisembarkingCompleted -= HandleBusDisembarkingCompleted;
        }

        foreach (AirlinePassengerAgent passenger in walkingToBusStopPassengers)
            if (passenger != null)
                passenger.ReachedBusStop -= HandlePassengerReachedBusStop;
    }

    private void OnEnable()
    {
        if (instance == this)
        {
            AircraftTurnaroundController.PassengersDeboardingStarted += HandlePassengersDeboardingStarted;
            AircraftTurnaroundController.PassengersBoardingStarted += HandlePassengersBoardingStarted;
            if (logToConsole)
                Debug.Log("PassengerCoordinator: Subscribed to AircraftTurnaroundController.PassengersDeboarded.", this);
            SubscribeToBusSpawner();
            SubscribeToScheduleAndTime();

            foreach (BusController bus in subscribedBuses)
            {
                if (bus == null)
                    continue;
                bus.StopReached += HandleBusStopReached;
                bus.JourneyCompleted += HandleBusJourneyCompleted;
                bus.StopServiceCompleted += HandleBusStopServiceCompleted;
                bus.PassengerBoardingFailed += HandlePassengerBoardingFailed;
                bus.PassengerDisembarked += HandleBusPassengerDisembarked;
                bus.DisembarkingCompleted += HandleBusDisembarkingCompleted;
            }

            List<AirlinePassengerAgent> alreadyWaiting = new List<AirlinePassengerAgent>();
            foreach (AirlinePassengerAgent passenger in walkingToBusStopPassengers)
            {
                if (passenger == null)
                    continue;

                passenger.ReachedBusStop += HandlePassengerReachedBusStop;
                if (passenger.State == AirlinePassengerAgent.JourneyState.WaitingForBus)
                    alreadyWaiting.Add(passenger);
            }

            for (int i = 0; i < alreadyWaiting.Count; i++)
                HandlePassengerReachedBusStop(alreadyWaiting[i]);
        }
    }

    private void SubscribeToBus(BusController bus)
    {
        if (bus == null || !subscribedBuses.Add(bus))
            return;

        bus.StopReached += HandleBusStopReached;
        bus.JourneyCompleted += HandleBusJourneyCompleted;
        bus.StopServiceCompleted += HandleBusStopServiceCompleted;
        bus.PassengerBoardingFailed += HandlePassengerBoardingFailed;
        bus.PassengerDisembarked += HandleBusPassengerDisembarked;
        bus.DisembarkingCompleted += HandleBusDisembarkingCompleted;
    }

    private void HandleBusDispatched(BusController bus)
    {
        if (bus == null)
            return;

        SubscribeToBus(bus);
        if (scheduledAircraftSpawner == null || publicTransportManager == null ||
            airlinePassengerPrefab == null)
            return;

        double intervalSeconds = System.Math.Max(60.0, publicTransportManager.BusFrequencyMinutes * 60.0);
        double now = GameTimeManager.SimulationTime;
        double departureSecond = System.Math.Floor(now / intervalSeconds + 1e-7) * intervalSeconds;
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
            int requestedPassengers =
                registeredPassengerCounts.TryGetValue(flight.OperationId, out int registeredCount)
                    ? registeredCount
                    : 0;
            int passengerCount = Mathf.Min(requestedPassengers, bus.AvailablePassengerCapacity);
            int createdPassengerCount = 0;

            for (int passengerIndex = 0; passengerIndex < passengerCount; passengerIndex++)
            {
                Vector3 spawnPosition = bus.transform.position + Vector3.up * 0.1f;
                GameObject passengerObject = Instantiate(
                    airlinePassengerPrefab, spawnPosition, Quaternion.identity);
                AirlinePassengerAgent agent = passengerObject.GetComponent<AirlinePassengerAgent>();

                if (agent == null || !agent.Initialize(
                    flight.OperationId, PassengerIdentity.TransportMode.Bus))
                {
                    Destroy(passengerObject);
                    continue;
                }

                // Passenger prefab pivots and collider bottoms vary. Align the
                // feet to the bus spawn height before parenting the passenger.
                if (agent.Movement != null)
                    agent.Movement.AlignFeetToGround(spawnPosition);

                if (!bus.TryBoardPassenger(agent))
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

        if (!busManifests.TryGetValue(bus, out List<BusPassengerRecord> manifest))
            return;

        for (int i = manifest.Count - 1; i >= 0; i--)
        {
            BusPassengerRecord record = manifest[i];
            if (record == null || record.Agent == null)
            {
                manifest.RemoveAt(i);
                continue;
            }

            AirlinePassengerAgent passenger = record.Agent;
            if (queuedForBusDisembarking.Contains(passenger))
                continue;

            if (gateRegistry == null ||
                !gateRegistry.TryGetGate(record.GateStableId, out AircraftGate gate) ||
                gate == null)
            {
                Debug.LogWarning(
                    $"PassengerCoordinator: Could not find gate '{record.GateStableId}' for flight {passenger.FlightOperationId}; passenger remains on the bus.",
                    this);
                continue;
            }

            if (pedestrianPathfinder == null ||
                !pedestrianPathfinder.TryFindBusStopWaitingPosition(
                    stop.transform.position,
                    Random.Range(-2.5f, 2.5f),
                    Random.Range(1.1f, 2.2f),
                    8,
                    out Vector3 disembarkPosition))
            {
                Debug.LogWarning(
                    $"PassengerCoordinator: Could not find a walkable disembark position beside stop {stop.name} for flight {passenger.FlightOperationId}.",
                    this);
                continue;
            }

            PlacedBuilding gateFootprint = gate.GetComponent<PlacedBuilding>();
            if (gateFootprint == null)
                gateFootprint = gate.GetComponentInParent<PlacedBuilding>();

            if (bus.QueuePassengerForDisembarking(
                passenger, disembarkPosition, gate.transform.position,
                stop, gateFootprint))
                queuedForBusDisembarking.Add(passenger);
        }

        if (manifest.Count == 0)
            busManifests.Remove(bus);
    }

    private void HandleBusPassengerDisembarked(
        BusController bus, AirlinePassengerAgent passenger, bool succeeded)
    {
        if (passenger == null)
            return;

        queuedForBusDisembarking.Remove(passenger);
        if (!succeeded)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Passenger for flight {passenger.FlightOperationId} could not disembark and remains assigned to the bus.",
                passenger);
            return;
        }

        if (busManifests.TryGetValue(bus, out List<BusPassengerRecord> manifest))
        {
            for (int i = manifest.Count - 1; i >= 0; i--)
            {
                if (manifest[i] != null && manifest[i].Agent == passenger)
                    manifest.RemoveAt(i);
            }
            if (manifest.Count == 0)
                busManifests.Remove(bus);
        }

        string operationId = passenger.FlightOperationId;
        if (!terminalPassengersByOperationId.TryGetValue(
            operationId, out List<AirlinePassengerAgent> flightPassengers))
        {
            flightPassengers = new List<AirlinePassengerAgent>();
            terminalPassengersByOperationId.Add(operationId, flightPassengers);
        }
        flightPassengers.Add(passenger);
    }

    private void HandleBusDisembarkingCompleted(BusController bus)
    {
        // Arriving passengers are released to board only after every queued
        // onboard passenger has finished leaving this bus.
        BoardWaitingReturnPassengers(bus);
    }

    private void BoardWaitingReturnPassengers(BusController bus)
    {
        if (bus == null || waitingReturnPassengers.Count == 0 ||
            !bus.IsBoardingOpen)
            return;

        int passengersToCheck = waitingReturnPassengers.Count;
        while (passengersToCheck-- > 0 && bus.IsBoardingOpen &&
               waitingReturnPassengers.Count > 0)
        {
            AirlinePassengerAgent passenger = waitingReturnPassengers.Dequeue();
            if (passenger == null)
                continue;

            if (passenger.State != AirlinePassengerAgent.JourneyState.WaitingForBus)
            {
                waitingReturnPassengerSet.Remove(passenger);
                continue;
            }

            if (!QueueReturnPassengerForBus(bus, passenger))
            {
                waitingReturnPassengers.Enqueue(passenger);
                break;
            }
        }
    }

    private void HandleBusJourneyCompleted(BusController bus)
    {
        if (bus == null)
            return;

        // Homebound passengers are delivered when the bus reaches its exit.
        // Do not return early: an unfinished departing manifest must also be
        // cleaned up, and the bus event subscriptions must always be released.
        if (returnPassengersByBus.TryGetValue(bus, out List<AirlinePassengerAgent> passengers))
        {
            for (int i = 0; i < passengers.Count; i++)
                if (passengers[i] != null)
                {
                    waitingReturnPassengerSet.Remove(passengers[i]);
                    passengers[i].CompleteReturnBusJourney();
                }
            returnPassengersByBus.Remove(bus);
        }

        if (busManifests.TryGetValue(bus, out List<BusPassengerRecord> unfinishedManifest))
        {
            for (int i = 0; i < unfinishedManifest.Count; i++)
            {
                AirlinePassengerAgent passenger = unfinishedManifest[i] != null
                    ? unfinishedManifest[i].Agent
                    : null;
                if (passenger == null)
                    continue;

                Debug.LogWarning(
                    $"PassengerCoordinator: Flight passenger {passenger.FlightOperationId} could not reach its assigned gate before the bus exited.",
                    passenger);
                passenger.AbandonJourney();
                Destroy(passenger.gameObject);
            }
            busManifests.Remove(bus);
        }

        bus.StopReached -= HandleBusStopReached;
        bus.JourneyCompleted -= HandleBusJourneyCompleted;
        bus.StopServiceCompleted -= HandleBusStopServiceCompleted;
        bus.PassengerBoardingFailed -= HandlePassengerBoardingFailed;
        bus.PassengerDisembarked -= HandleBusPassengerDisembarked;
        bus.DisembarkingCompleted -= HandleBusDisembarkingCompleted;
        subscribedBuses.Remove(bus);
    }

    private void HandlePassengersDeboardingStarted(AircraftTurnaroundController turnaround)
    {
        if (turnaround == null)
        {
            Debug.LogError("PassengerCoordinator: Deboarding event received with a null turnaround controller.", this);
            return;
        }

        if (logToConsole)
            Debug.Log($"PassengerCoordinator: Deboarding started for '{turnaround.name}'. Preparing real arriving passenger agents.", turnaround);

        if (airlinePassengerPrefab == null || gateRegistry == null)
        {
            Debug.LogError(
                $"PassengerCoordinator: Cannot spawn arriving passengers for {turnaround.name}. " +
                $"Missing: {(airlinePassengerPrefab == null ? "airline passenger prefab; " : string.Empty)}" +
                $"{(gateRegistry == null ? "AircraftGateRegistry." : string.Empty)}",
                turnaround);
            turnaround.CompleteExternalDeboarding();
            return;
        }

        if (busSpawner == null)
            Debug.LogWarning(
                $"PassengerCoordinator: BusSpawner is missing; arriving passengers for {turnaround.name} will spawn at the gate but cannot travel to a bus stop.",
                turnaround);

        AircraftFlightIdentity identity = turnaround.GetComponent<AircraftFlightIdentity>();
        AircraftStats stats = turnaround.GetComponent<AircraftStats>();
        if (identity == null || !identity.IsInitialized || identity.Definition == null ||
            stats == null ||
            (identity.Definition.OperationType != FlightOperationType.PassengerOnly &&
             identity.Definition.OperationType != FlightOperationType.CargoAndPassengers))
        {
            string operationType = identity != null && identity.Definition != null
                ? identity.Definition.OperationType.ToString()
                : "unavailable";
            Debug.LogWarning(
                $"PassengerCoordinator: Ignoring deboarding event for '{turnaround.name}'. " +
                $"AircraftFlightIdentity: {(identity == null ? "MISSING" : identity.IsInitialized ? "initialized" : "NOT initialized")}; " +
                $"FlightDefinition: {(identity != null && identity.Definition != null ? "present" : "MISSING")}; " +
                $"AircraftStats: {(stats == null ? "MISSING" : "present")}; OperationType: {operationType}. " +
                "Expected an initialized PassengerOnly or CargoAndPassengers flight.",
                turnaround);
            return;
        }

        if (logToConsole)
            Debug.Log($"PassengerCoordinator: Flight {identity.FlightNumber} accepted. Incoming passengers: {stats.IncomingPassengers}; operation ID: '{identity.OperationId}'.", turnaround);

        if (stats.IncomingPassengers <= 0)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Flight {identity.FlightNumber} completed deboarding with 0 incoming passengers; no passengers will spawn. Check its scheduled passenger demand configuration.",
                turnaround);
            turnaround.CompleteExternalDeboarding();
            return;
        }

        string gateId = identity.HasActualGate
            ? identity.ActualGateStableId
            : identity.PlannedGateStableId;
        if (!gateRegistry.TryGetGate(gateId, out AircraftGate gate) || gate == null)
        {
            Debug.LogError(
                $"PassengerCoordinator: Cannot spawn arriving passengers for flight {identity.FlightNumber}; gate '{gateId}' could not be resolved.",
                turnaround);
            turnaround.CompleteExternalDeboarding();
            return;
        }

        PlacedBuilding firstStop = busSpawner != null
            ? busSpawner.GetNextFirstStop()
            : null;
        if (firstStop == null)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Flight {identity.FlightNumber} passengers will spawn at gate '{gateId}', but cannot be routed because no valid first bus stop is available.",
                turnaround);
        }

        if (logToConsole)
            Debug.Log($"PassengerCoordinator: Queued {stats.IncomingPassengers} arriving passenger(s) for flight {identity.FlightNumber} at gate '{gateId}'. They will spawn one per second.", turnaround);

        PlacedBuilding gateFootprint = gate.GetComponent<PlacedBuilding>();
        if (gateFootprint == null)
            gateFootprint = gate.GetComponentInParent<PlacedBuilding>();

        if (gateFootprint == null)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Gate '{gateId}' has no PlacedBuilding footprint; passenger routing will use ordinary cell walkability.",
                gate);
        }

        incomingPassengerSpawnJobs.Enqueue(new IncomingPassengerSpawnJob
        {
            Turnaround = turnaround,
            OperationId = identity.OperationId,
            FlightNumber = identity.FlightNumber,
            PassengerCount = stats.IncomingPassengers,
            Gate = gate,
            GateFootprint = gateFootprint,
            FirstStop = firstStop
        });
    }

    private void HandlePassengerReachedBusStop(AirlinePassengerAgent passenger)
    {
        if (passenger == null)
            return;

        passenger.ReachedBusStop -= HandlePassengerReachedBusStop;
        walkingToBusStopPassengers.Remove(passenger);
        if (passenger.State != AirlinePassengerAgent.JourneyState.WaitingForBus ||
            waitingReturnPassengerSet.Contains(passenger))
            return;

        // If a bus is still boarding at the stop, late arrivals join that
        // bus immediately. Otherwise they wait for the next bus.
        foreach (BusController bus in subscribedBuses)
        {
            if (bus == null || !bus.IsBoardingOpen)
                continue;

            if (QueueReturnPassengerForBus(bus, passenger))
                return;
        }

        if (waitingReturnPassengerSet.Add(passenger))
            waitingReturnPassengers.Enqueue(passenger);
    }

    private bool QueueReturnPassengerForBus(
        BusController bus,
        AirlinePassengerAgent passenger)
    {
        if (bus == null || passenger == null ||
            !bus.QueuePassengerForBoarding(passenger))
            return false;

        waitingReturnPassengerSet.Add(passenger);
        if (!returnPassengersByBus.TryGetValue(
            bus, out List<AirlinePassengerAgent> onboardReturnPassengers))
        {
            onboardReturnPassengers = new List<AirlinePassengerAgent>();
            returnPassengersByBus.Add(bus, onboardReturnPassengers);
        }

        onboardReturnPassengers.Add(passenger);
        return true;
    }

    private void HandleBusStopServiceCompleted(BusController bus)
    {
        // The bus has closed boarding. Passengers who arrive from this point
        // remain in the shared waiting queue for the next bus.
    }

    private void HandlePassengerBoardingFailed(
        BusController bus,
        AirlinePassengerAgent passenger)
    {
        if (bus == null || passenger == null)
            return;

        if (returnPassengersByBus.TryGetValue(
            bus, out List<AirlinePassengerAgent> assignedPassengers))
        {
            assignedPassengers.Remove(passenger);
            if (assignedPassengers.Count == 0)
                returnPassengersByBus.Remove(bus);
        }

        // Keep a passenger in the shared queue only if they are still in a
        // state where another bus can accept them.
        if (passenger.State == AirlinePassengerAgent.JourneyState.WaitingForBus)
        {
            if (waitingReturnPassengerSet.Add(passenger))
                waitingReturnPassengers.Enqueue(passenger);
        }
        else
        {
            waitingReturnPassengerSet.Remove(passenger);
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

        if (!bookedPassengerCounts.TryGetValue(flightIdentity.OperationId, out int bookedPassengers))
        {
            bookedPassengers = 0;
            Debug.LogWarning(
                $"PassengerCoordinator: No bus manifest was registered for flight {flightIdentity.FlightNumber}; outgoing passenger count is set to zero.",
                aircraftStats);
        }

        aircraftStats.SetOutgoingPassengers(bookedPassengers);
        if (registeredIncomingPassengerCounts.TryGetValue(
            flightIdentity.OperationId, out int incomingPassengers))
        {
            aircraftStats.SetIncomingPassengers(incomingPassengers);
        }
        else
        {
            Debug.LogWarning(
                $"PassengerCoordinator: No incoming passenger demand was registered for flight {flightIdentity.FlightNumber}; incoming passengers set to zero.",
                aircraftStats);
            aircraftStats.SetIncomingPassengers(0);
        }
        return true;
    }

    public void UpdateScheduledFlightGate(AircraftFlightIdentity flightIdentity)
    {
        if (flightIdentity == null || !flightIdentity.IsInitialized ||
            !flightIdentity.HasActualGate || gateRegistry == null ||
            !terminalPassengersByOperationId.TryGetValue(
                flightIdentity.OperationId, out List<AirlinePassengerAgent> passengers))
            return;

        if (!gateRegistry.TryGetGate(
            flightIdentity.ActualGateStableId, out AircraftGate actualGate) || actualGate == null)
            return;

        for (int i = 0; i < passengers.Count; i++)
        {
            AirlinePassengerAgent passenger = passengers[i];
            if (passenger != null)
                passenger.RedirectToTerminal(actualGate.transform.position);
        }

        // Keep the terminal passenger list until the aircraft's boarding task
        // consumes it. These visible agents are the outbound flight manifest.
    }

    public void ForgetScheduledFlightPassengerCount(string operationId)
    {
        if (!string.IsNullOrWhiteSpace(operationId))
            bookedPassengerCounts.Remove(operationId);
    }

    private void RemoveStalePassengerRegistrations(HashSet<string> activeOperationIds)
    {
        List<string> staleOperationIds = null;
        foreach (string operationId in registeredPassengerCounts.Keys)
        {
            if (activeOperationIds.Contains(operationId))
                continue;
            if (staleOperationIds == null)
                staleOperationIds = new List<string>();
            staleOperationIds.Add(operationId);
        }

        if (staleOperationIds == null)
            return;
        for (int i = 0; i < staleOperationIds.Count; i++)
        {
            registeredPassengerCounts.Remove(staleOperationIds[i]);
            registeredIncomingPassengerCounts.Remove(staleOperationIds[i]);
        }
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