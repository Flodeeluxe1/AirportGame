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

    [Header("Passenger Transport Distribution")]
    [Min(0f)] [SerializeField] private float trainTransportWeight = 1f;
    [Min(0f)] [SerializeField] private float busTransportWeight = 1f;
    [Min(0f)] [SerializeField] private float undergroundTransportWeight = 1f;
    [Min(0f)] [SerializeField] private float carTransportWeight = 1f;
    [Min(1)] [SerializeField] private int busCapacityPerDeparture = 50;
    [Min(1)] [SerializeField] private int targetHourlyPassengerDemand = 150;
    [Min(1)] [SerializeField] private int targetParkingSpaceCount = 20;
    [Min(0f)] [SerializeField] private float passengerCarParkingFeePerMinute;
    [Min(0.01f)] [SerializeField] private float referenceParkingCost = 5f;
    [SerializeField] private bool enableTrainPassengerAssignment;
    [SerializeField] private bool enableUndergroundPassengerAssignment;

    private ParkingSpace[] cachedParkingSpaces;
    private bool parkingSpaceCacheDirty = true;
    private int cachedParkingSpaceCount;
    private int cachedOccupiedParkingSpaceCount;
    private int busPassengersTransported;
    private int carPassengersTransported;
    private double cumulativeBusWaitSeconds;
    private int completedBusWaitMeasurements;
    private readonly Dictionary<AirlinePassengerAgent, double> busWaitStartTimes =
        new Dictionary<AirlinePassengerAgent, double>();
    private readonly Dictionary<string, int> pendingOutgoingBusPassengerCounts =
        new Dictionary<string, int>();
    private readonly Dictionary<string, string> pendingOutgoingBusGateIds =
        new Dictionary<string, string>();
    private readonly Queue<string> pendingOutgoingBusOperationIds =
        new Queue<string>();
    private readonly HashSet<string> pendingOutgoingBusQueuedOperationIds =
        new HashSet<string>();

    public event System.Action TransportStatisticsChanged;
    public int BusPassengersTransported => busPassengersTransported;
    public int CarPassengersTransported => carPassengersTransported;
    public int WaitingForBusPassengers
    {
        get
        {
            int count = waitingReturnPassengerSet.Count;
            foreach (KeyValuePair<string, int> pair in pendingOutgoingBusPassengerCounts)
                count += Mathf.Max(0, pair.Value);
            return count;
        }
    }
    public int TotalParkingSpaces => cachedParkingSpaceCount;
    public int OccupiedParkingSpaces => cachedOccupiedParkingSpaceCount;
    public float AverageBusWaitMinutes =>
        completedBusWaitMeasurements > 0
            ? (float)(cumulativeBusWaitSeconds / completedBusWaitMeasurements / 60.0)
            : 0f;

    [Header("Airline Bus Transport")]
    [SerializeField] private GameObject airlinePassengerPrefab;
    [SerializeField] private ScheduledAircraftSpawner scheduledAircraftSpawner;
    [SerializeField] private BusSpawner busSpawner;
    [SerializeField] private PublicTransportManager publicTransportManager;
    [SerializeField] private TrafficTestSpawner trafficSpawner;
    [SerializeField] private GameObject airlinePassengerCarPrefab;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private RoadNetwork roadNetwork;

    private Vector3 cachedPassengerRoadsideDropoffPosition;
    private bool hasCachedPassengerRoadsideDropoffPosition;
    private int cachedRoadTopologyVersion = -1;
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
    private readonly Dictionary<string, int> registeredIncomingCarPassengerCounts =
        new Dictionary<string, int>();
    private readonly Dictionary<string, int> registeredOutgoingCarPassengerCounts =
        new Dictionary<string, int>();
    private readonly Dictionary<string, int> spawnedOutgoingCarPassengerCounts =
        new Dictionary<string, int>();
    private readonly HashSet<string> outgoingCarDispatchProcessedOperationIds =
        new HashSet<string>();
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
    private readonly Queue<IncomingCarPickupGroup> pendingIncomingCarPickups =
        new Queue<IncomingCarPickupGroup>();
    private readonly Dictionary<TrafficTestVehicle, IncomingCarPickupGroup> incomingCarGroupsByVehicle =
        new Dictionary<TrafficTestVehicle, IncomingCarPickupGroup>();
    private readonly Dictionary<TrafficTestVehicle, OutgoingCarDropoffGroup> outgoingCarGroupsByVehicle =
        new Dictionary<TrafficTestVehicle, OutgoingCarDropoffGroup>();
    private readonly Dictionary<AirlinePassengerAgent, IncomingCarPickupGroup> incomingCarGroupsByPassenger =
        new Dictionary<AirlinePassengerAgent, IncomingCarPickupGroup>();
    private bool spawningIncomingCar;
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
        public string OperationId;
        public int RequestedPassengerCount;
        public readonly List<AirlinePassengerAgent> Passengers =
            new List<AirlinePassengerAgent>();
        public readonly HashSet<AirlinePassengerAgent> AddedPassengers =
            new HashSet<AirlinePassengerAgent>();
        public int NextPassengerIndex;
        public int BoardedCount;
    }

    private sealed class IncomingPassengerSpawnJob
    {
        public AircraftTurnaroundController Turnaround;
        public string OperationId;
        public string FlightNumber;
        public int PassengerCount;
        public int CarPassengerCount;
        public readonly List<AirlinePassengerAgent> CarPassengers =
            new List<AirlinePassengerAgent>();
        public int NextPassengerIndex;
        public int SpawnedCount;
        public int RoutedCount;
        public int FailedCount;
        public AircraftGate Gate;
        public PlacedBuilding GateFootprint;
        public PlacedBuilding FirstStop;
    }


    private sealed class IncomingCarPickupGroup
    {
        public readonly List<AirlinePassengerAgent> Passengers =
            new List<AirlinePassengerAgent>();
        public AircraftGate Gate;
        public PlacedBuilding GateFootprint;
        public TrafficTestVehicle Vehicle;
        public int BoardedCount;
    }

    private sealed class OutgoingCarDropoffGroup
    {
        public string OperationId;
        public AircraftGate Gate;
        public PlacedBuilding GateFootprint;
        public int PassengerCount;
        public TrafficTestVehicle Vehicle;
        public bool PassengersSpawned;
    }

    private sealed class BusPassengerRecord
    {
        public AirlinePassengerAgent Agent;
        public string GateStableId;
    }

    public float PassengerCarParkingFeePerMinute => passengerCarParkingFeePerMinute;

    public void SetPassengerCarParkingFeePerMinute(float feePerMinute)
    {
        float updatedFee = Mathf.Max(0f, feePerMinute);
        if (Mathf.Approximately(passengerCarParkingFeePerMinute, updatedFee))
            return;

        passengerCarParkingFeePerMinute = updatedFee;
        NotifyTransportStatisticsChanged();
    }

    private void NotifyTransportStatisticsChanged()
    {
        TransportStatisticsChanged?.Invoke();
    }

    private void RefreshParkingSpaceCache()
    {
        if (parkingSpaceCacheDirty || cachedParkingSpaces == null)
        {
            cachedParkingSpaces = FindObjectsByType<ParkingSpace>();
            cachedParkingSpaceCount = cachedParkingSpaces.Length;
            parkingSpaceCacheDirty = false;
        }

        int occupied = 0;
        for (int i = 0; i < cachedParkingSpaces.Length; i++)
        {
            ParkingSpace space = cachedParkingSpaces[i];
            if (space != null && (space.IsOccupied || space.IsReserved))
                occupied++;
        }

        cachedOccupiedParkingSpaceCount = occupied;
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

        OutgoingPassengerBoardingJob job =
            activeOutgoingPassengerBoardingJob;

        CollectPassengersForBoarding(job);
        if (job.Passengers.Count < job.RequestedPassengerCount)
            return;

        outgoingPassengerBoardingIntervalRemaining -= deltaGameSeconds;
        const int maxBoardingsPerUpdate = 32;
        for (int i = 0; i < maxBoardingsPerUpdate &&
                        activeOutgoingPassengerBoardingJob != null &&
                        outgoingPassengerBoardingIntervalRemaining <= 0f; i++)
        {
            AirlinePassengerAgent passenger =
                job.Passengers[job.NextPassengerIndex];

            if (passenger == null)
            {
                Debug.LogError(
                    $"PassengerCoordinator: An outbound passenger for flight {job.OperationId} was destroyed before boarding. The boarding task remains incomplete.",
                    job.Turnaround);
                outgoingPassengerBoardingIntervalRemaining = 0f;
                return;
            }

            if (passenger.State == AirlinePassengerAgent.JourneyState.AtTerminal)
            {
                if (!passenger.BoardAircraft(job.AircraftTransform))
                {
                    outgoingPassengerBoardingIntervalRemaining = 0f;
                    return;
                }

                job.BoardedCount++;
                job.NextPassengerIndex++;
            }
            else if (passenger.State == AirlinePassengerAgent.JourneyState.OnAircraft)
            {
                job.BoardedCount++;
                job.NextPassengerIndex++;
            }
            else
            {
                // Wait for this specific passenger to reach the gate.
                // Do not skip, or complete the turnaround before they board.
                outgoingPassengerBoardingIntervalRemaining = 0f;
                return;
            }

            if (job.NextPassengerIndex >= job.RequestedPassengerCount)
            {
                if (job.Turnaround != null)
                    job.Turnaround.CompleteExternalBoarding();

                terminalPassengersByOperationId.Remove(job.OperationId);
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

    private void CollectPassengersForBoarding(OutgoingPassengerBoardingJob job)
    {
        if (job == null ||
            !terminalPassengersByOperationId.TryGetValue(
                job.OperationId,
                out List<AirlinePassengerAgent> terminalPassengers))
            return;

        for (int i = 0; i < terminalPassengers.Count &&
                        job.Passengers.Count < job.RequestedPassengerCount; i++)
        {
            AirlinePassengerAgent passenger = terminalPassengers[i];
            if (passenger != null && job.AddedPassengers.Add(passenger))
                job.Passengers.Add(passenger);
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
            stats == null)
        {
            turnaround.CompleteExternalBoarding();
            return;
        }

        if (stats.OutgoingPassengers <= 0)
        {
            turnaround.CompleteExternalBoarding();
            return;
        }

        OutgoingPassengerBoardingJob job =
            new OutgoingPassengerBoardingJob
            {
                Turnaround = turnaround,
                AircraftTransform = turnaround.transform,
                OperationId = identity.OperationId,
                RequestedPassengerCount = stats.OutgoingPassengers
            };

        CollectPassengersForBoarding(job);
        outgoingPassengerBoardingJobs.Enqueue(job);
    }

    private void SpawnNextIncomingPassenger(IncomingPassengerSpawnJob job)
    {
        int passengerIndex = job.NextPassengerIndex;
        if (job.Turnaround == null)
        {
            job.FailedCount++;
            job.NextPassengerIndex++;
            return;
        }

        Vector3 spawnPosition = job.Gate != null
            ? job.Gate.transform.position
            : job.Turnaround.transform.position;
        GameObject passengerObject = Instantiate(
            airlinePassengerPrefab, spawnPosition, Quaternion.identity);
        AirlinePassengerAgent passenger =
            passengerObject.GetComponent<AirlinePassengerAgent>();

        bool isCarPassenger = passengerIndex < job.CarPassengerCount;
        PassengerIdentity.TransportMode transportMode = isCarPassenger
            ? PassengerIdentity.TransportMode.Car
            : PassengerIdentity.TransportMode.Bus;
        if (passenger == null || !passenger.Initialize(
            job.OperationId, transportMode))
        {
            job.FailedCount++;
            job.NextPassengerIndex++;
            Debug.LogWarning(
                $"PassengerCoordinator: Failed to initialize arriving passenger {passengerIndex + 1}/{job.PassengerCount} for flight {job.FlightNumber}. " +
                $"Prefab: '{airlinePassengerPrefab.name}'. Check that it has a working AirlinePassengerAgent.",
                passengerObject);
            Destroy(passengerObject);
            return;
        }

        job.NextPassengerIndex++;
        job.SpawnedCount++;
        if (passenger.Movement != null)
            passenger.Movement.AlignFeetToGround(spawnPosition);

        if (isCarPassenger)
        {
            job.CarPassengers.Add(passenger);
            return;
        }

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
            Debug.LogWarning(
                $"PassengerCoordinator: Arriving passenger for flight {job.FlightNumber} was created but no walkable waiting position could be found at bus stop '{job.FirstStop.name}'.",
                passengerObject);
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
                $"PassengerCoordinator: Passenger for flight {job.FlightNumber} spawned at '{(job.Gate != null ? job.Gate.name : job.Turnaround.name)}' but could not start walking to bus stop '{job.FirstStop.name}'.",
                passengerObject);
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

        QueueIncomingCarPickupGroups(job);
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
        SubscribeToTransportManager();
        RefreshParkingSpaceCache();
        if (gateRegistry == null)
            gateRegistry = FindAnyObjectByType<AircraftGateRegistry>();
        if (pedestrianPathfinder == null)
            pedestrianPathfinder = FindAnyObjectByType<PedestrianPathfinder>();
        ResolvePassengerRoadsideDependencies();
        if (trafficSpawner == null)
            trafficSpawner = FindAnyObjectByType<TrafficTestSpawner>();
        if (airlinePassengerCarPrefab == null)
        {
            SightseeingPassengerManager sightseeingManager =
                FindAnyObjectByType<SightseeingPassengerManager>();
            if (sightseeingManager != null)
                airlinePassengerCarPrefab = sightseeingManager.PassengerCarPrefab;
            if (trafficSpawner == null && sightseeingManager != null)
                trafficSpawner = sightseeingManager.TrafficSpawner;
        }

        SubscribeToBusSpawner();
        SubscribeToScheduleAndTime();
        RefreshScheduledPassengerRegistrations();
        DispatchOutgoingCarsForCurrentDeparture();
        if (busSpawner == null)
            Debug.LogError("PassengerCoordinator: BusSpawner is missing; airline bus manifests cannot be loaded.", this);

        if (gateRegistry == null)
            Debug.LogError("PassengerCoordinator: AircraftGateRegistry is missing; arriving passengers cannot be routed from their gate.", this);

        if (airlinePassengerPrefab == null)
            Debug.LogError("PassengerCoordinator: Assign the airline Passenger prefab. SS_Passenger is reserved for sightseeing.", this);

        if (trafficSpawner == null || airlinePassengerCarPrefab == null)
            Debug.LogWarning(
                "PassengerCoordinator: Passenger car traffic requires the shared TrafficTestSpawner and passenger car prefab. The car prefab is normally read from SightseeingPassengerManager.",
                this);

        if (airlinePassengerPrefab != null &&
            airlinePassengerPrefab.GetComponent<AirlinePassengerAgent>() == null)
        {
            Debug.LogError(
                "PassengerCoordinator: The assigned Passenger prefab must contain AirlinePassengerAgent.",
                airlinePassengerPrefab);
        }

        TrySpawnPendingIncomingCarPickups();
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
        bookedPassengerCounts.Clear();
        registeredIncomingPassengerCounts.Clear();
        registeredIncomingCarPassengerCounts.Clear();
        registeredOutgoingCarPassengerCounts.Clear();
        spawnedOutgoingCarPassengerCounts.Clear();
        outgoingCarDispatchProcessedOperationIds.Clear();
        pendingOutgoingBusPassengerCounts.Clear();
        pendingOutgoingBusGateIds.Clear();
        pendingOutgoingBusOperationIds.Clear();
        pendingOutgoingBusQueuedOperationIds.Clear();
        RefreshScheduledPassengerRegistrations();
    }

    private void HandlePassengerRegistrationTimeAdvanced(long previousAbsoluteSecond, long currentAbsoluteSecond)
    {
        if (currentAbsoluteSecond / 86400L != previousAbsoluteSecond / 86400L)
            RefreshScheduledPassengerRegistrations();

        ProcessScheduledOutgoingCarDispatches(previousAbsoluteSecond, currentAbsoluteSecond);
    }

    private void ProcessScheduledOutgoingCarDispatches(
        long previousAbsoluteSecond,
        long currentAbsoluteSecond)
    {
        if (scheduledAircraftSpawner == null || publicTransportManager == null)
            return;

        double intervalSeconds = System.Math.Max(
            60.0, publicTransportManager.BusFrequencyMinutes * 60.0);
        double nextDeparture =
            (System.Math.Floor(previousAbsoluteSecond / intervalSeconds) + 1.0) *
            intervalSeconds;

        while (nextDeparture <= currentAbsoluteSecond)
        {
            scheduledAircraftSpawner.GetPassengerFlightsForBusDeparture(
                nextDeparture,
                publicTransportManager.BusFrequencyMinutes,
                dispatchFlights);

            for (int i = 0; i < dispatchFlights.Count; i++)
            {
                ScheduledPassengerFlight flight = dispatchFlights[i];
                int totalDemand = registeredPassengerCounts.TryGetValue(
                    flight.OperationId, out int demand) ? demand : 0;
                int carPassengers = EnsureOutgoingCarDropoffGroupsSpawned(flight);
                QueueOutgoingBusPassengers(
                    flight, Mathf.Max(0, totalDemand - carPassengers));
            }

            nextDeparture += intervalSeconds;
        }
    }

    private void DispatchOutgoingCarsForCurrentDeparture()
    {
        if (scheduledAircraftSpawner == null || publicTransportManager == null)
            return;

        double intervalSeconds = System.Math.Max(
            60.0, publicTransportManager.BusFrequencyMinutes * 60.0);
        double departureSecond =
            System.Math.Floor(GameTimeManager.SimulationTime / intervalSeconds) *
            intervalSeconds;

        scheduledAircraftSpawner.GetPassengerFlightsForBusDeparture(
            departureSecond,
            publicTransportManager.BusFrequencyMinutes,
            dispatchFlights);

        for (int i = 0; i < dispatchFlights.Count; i++)
        {
            ScheduledPassengerFlight flight = dispatchFlights[i];
            int totalDemand = registeredPassengerCounts.TryGetValue(
                flight.OperationId, out int demand) ? demand : 0;
            int carPassengers = EnsureOutgoingCarDropoffGroupsSpawned(flight);
            QueueOutgoingBusPassengers(
                flight, Mathf.Max(0, totalDemand - carPassengers));
        }
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
            int outgoingDemand =
                CalculateBookedPassengers(capacity, airportRanking, flight.StartMinute);
            registeredPassengerCounts[flight.OperationId] = outgoingDemand;
            bookedPassengerCounts[flight.OperationId] = outgoingDemand;
            // The outgoing split is recalculated at dispatch time so recent
            // changes to bus service, parking supply and fees are respected.
            registeredOutgoingCarPassengerCounts[flight.OperationId] = 0;

            int incomingDemand =
                CalculateBookedPassengers(capacity, airportRanking, flight.StartMinute);
            registeredIncomingPassengerCounts[flight.OperationId] = incomingDemand;
            registeredIncomingCarPassengerCounts[flight.OperationId] =
                CalculateCarPassengerCount(incomingDemand);
        }

        RemoveStalePassengerRegistrations(currentRegistrationIds);
    }

    private bool transportManagerSubscribed;

    private void SubscribeToTransportManager()
    {
        if (transportManagerSubscribed || publicTransportManager == null)
            return;

        publicTransportManager.BusFrequencyChanged += HandleTransportCapacityChanged;
        publicTransportManager.TrainServiceChanged += HandleTransportCapacityChanged;
        publicTransportManager.UndergroundServiceChanged += HandleTransportCapacityChanged;
        publicTransportManager.FleetChanged += HandleTransportCapacityChanged;
        publicTransportManager.StopsChanged += HandleTransportCapacityChanged;
        transportManagerSubscribed = true;
    }

    private void HandleTransportCapacityChanged()
    {
        NotifyTransportStatisticsChanged();
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
        ParkingSpace.AvailabilityChanged -= HandleParkingAvailabilityChanged;
        if (transportManagerSubscribed && publicTransportManager != null)
        {
            publicTransportManager.BusFrequencyChanged -= HandleTransportCapacityChanged;
            publicTransportManager.TrainServiceChanged -= HandleTransportCapacityChanged;
            publicTransportManager.UndergroundServiceChanged -= HandleTransportCapacityChanged;
            publicTransportManager.FleetChanged -= HandleTransportCapacityChanged;
            publicTransportManager.StopsChanged -= HandleTransportCapacityChanged;
        }
        transportManagerSubscribed = false;
        PlacedBuilding.BuildingChanged -= HandleParkingBuildingChanged;
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
            bus.PassengerBoarded -= HandleBusPassengerBoarded;
            bus.PassengerDisembarked -= HandleBusPassengerDisembarked;
            bus.DisembarkingCompleted -= HandleBusDisembarkingCompleted;
        }

        foreach (AirlinePassengerAgent passenger in walkingToBusStopPassengers)
            if (passenger != null)
                passenger.ReachedBusStop -= HandlePassengerReachedBusStop;

        foreach (KeyValuePair<TrafficTestVehicle, OutgoingCarDropoffGroup> pair in outgoingCarGroupsByVehicle)
        {
            if (pair.Key == null)
                continue;
            pair.Key.Parked -= HandleOutgoingPassengerCarParked;
            pair.Key.RoadsideDropoffStarted -= HandleOutgoingPassengerCarRoadsideDropoff;
            pair.Key.Exited -= HandleOutgoingPassengerCarExited;
        }

        foreach (KeyValuePair<TrafficTestVehicle, IncomingCarPickupGroup> pair in incomingCarGroupsByVehicle)
        {
            if (pair.Key == null)
                continue;
            pair.Key.Parked -= HandleIncomingPickupCarParked;
            pair.Key.Exited -= HandleIncomingPickupCarExited;
        }

        foreach (KeyValuePair<AirlinePassengerAgent, IncomingCarPickupGroup> pair in incomingCarGroupsByPassenger)
            if (pair.Key != null)
                pair.Key.ReachedCarPickup -= HandlePassengerReachedIncomingCar;
    }

    private void OnEnable()
    {
        ParkingSpace.AvailabilityChanged += HandleParkingAvailabilityChanged;
        parkingSpaceCacheDirty = true;
        ResolvePassengerRoadsideDependencies();
        PlacedBuilding.BuildingChanged += HandleParkingBuildingChanged;
        if (instance == this)
        {
            AircraftTurnaroundController.PassengersDeboardingStarted += HandlePassengersDeboardingStarted;
            AircraftTurnaroundController.PassengersBoardingStarted += HandlePassengersBoardingStarted;
            if (logToConsole)
                Debug.Log("PassengerCoordinator: Subscribed to aircraft passenger deboarding and boarding events.", this);
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
                bus.PassengerBoarded += HandleBusPassengerBoarded;
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

            foreach (KeyValuePair<TrafficTestVehicle, OutgoingCarDropoffGroup> pair in outgoingCarGroupsByVehicle)
            {
                if (pair.Key == null)
                    continue;
                pair.Key.Parked -= HandleOutgoingPassengerCarParked;
                pair.Key.RoadsideDropoffStarted -= HandleOutgoingPassengerCarRoadsideDropoff;
                pair.Key.Exited -= HandleOutgoingPassengerCarExited;
                pair.Key.Parked += HandleOutgoingPassengerCarParked;
                pair.Key.RoadsideDropoffStarted += HandleOutgoingPassengerCarRoadsideDropoff;
                pair.Key.Exited += HandleOutgoingPassengerCarExited;

                if (!pair.Value.PassengersSpawned)
                {
                    if (pair.Key.IsParked)
                        HandleOutgoingPassengerCarParked(pair.Key);
                    else if (pair.Key.CurrentState ==
                             TrafficTestVehicle.VehicleState.WaitingAtRoadsideDropoff)
                        HandleOutgoingPassengerCarRoadsideDropoff(pair.Key);
                }
            }

            foreach (KeyValuePair<TrafficTestVehicle, IncomingCarPickupGroup> pair in incomingCarGroupsByVehicle)
            {
                if (pair.Key == null)
                    continue;
                pair.Key.Parked -= HandleIncomingPickupCarParked;
                pair.Key.Exited -= HandleIncomingPickupCarExited;
                pair.Key.Parked += HandleIncomingPickupCarParked;
                pair.Key.Exited += HandleIncomingPickupCarExited;
                if (pair.Key.IsParked)
                    HandleIncomingPickupCarParked(pair.Key);
            }

            List<AirlinePassengerAgent> alreadyAtCarPickup =
                new List<AirlinePassengerAgent>();
            foreach (KeyValuePair<AirlinePassengerAgent, IncomingCarPickupGroup> pair in incomingCarGroupsByPassenger)
            {
                if (pair.Key == null)
                    continue;
                pair.Key.ReachedCarPickup -= HandlePassengerReachedIncomingCar;
                pair.Key.ReachedCarPickup += HandlePassengerReachedIncomingCar;
                if (pair.Key.State == AirlinePassengerAgent.JourneyState.AtCarPickup)
                    alreadyAtCarPickup.Add(pair.Key);
            }

            for (int i = 0; i < alreadyAtCarPickup.Count; i++)
                HandlePassengerReachedIncomingCar(alreadyAtCarPickup[i]);

            TrySpawnPendingIncomingCarPickups();
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
        bus.PassengerBoarded += HandleBusPassengerBoarded;
        bus.PassengerDisembarked += HandleBusPassengerDisembarked;
        bus.DisembarkingCompleted += HandleBusDisembarkingCompleted;
    }

    private void HandleBusDispatched(BusController bus)
    {
        if (bus == null)
            return;

        SubscribeToBus(bus);
        if (airlinePassengerPrefab == null)
            return;

        if (scheduledAircraftSpawner != null && publicTransportManager != null)
        {
            double intervalSeconds = System.Math.Max(
                60.0, publicTransportManager.BusFrequencyMinutes * 60.0);
            double now = GameTimeManager.SimulationTime;
            double departureSecond =
                System.Math.Floor(now / intervalSeconds + 1e-7) * intervalSeconds;
            scheduledAircraftSpawner.GetPassengerFlightsForBusDeparture(
                departureSecond,
                publicTransportManager.BusFrequencyMinutes,
                dispatchFlights);

            for (int i = 0; i < dispatchFlights.Count; i++)
            {
                ScheduledPassengerFlight flight = dispatchFlights[i];
                int totalDemand = registeredPassengerCounts.TryGetValue(
                    flight.OperationId, out int demand) ? demand : 0;
                int carPassengers = EnsureOutgoingCarDropoffGroupsSpawned(flight);
                QueueOutgoingBusPassengers(
                    flight, Mathf.Max(0, totalDemand - carPassengers));
            }
        }

        // Older flight manifests are served first. Overflow remains queued
        // until a later timetable departure has capacity.
        List<BusPassengerRecord> manifest = new List<BusPassengerRecord>();
        LoadPendingOutgoingBusPassengers(bus, manifest);
        if (manifest.Count > 0)
            busManifests[bus] = manifest;

        NotifyTransportStatisticsChanged();
    }

    private void QueueOutgoingBusPassengers(
        ScheduledPassengerFlight flight,
        int passengerCount)
    {
        if (passengerCount <= 0 ||
            string.IsNullOrWhiteSpace(flight.OperationId) ||
            !pendingOutgoingBusQueuedOperationIds.Add(flight.OperationId))
            return;

        pendingOutgoingBusPassengerCounts[flight.OperationId] = passengerCount;
        pendingOutgoingBusGateIds[flight.OperationId] = flight.GateStableId;
        pendingOutgoingBusOperationIds.Enqueue(flight.OperationId);
        NotifyTransportStatisticsChanged();
    }

    private void LoadPendingOutgoingBusPassengers(
        BusController bus,
        List<BusPassengerRecord> manifest)
    {
        while (bus != null && bus.AvailablePassengerCapacity > 0 &&
               pendingOutgoingBusOperationIds.Count > 0)
        {
            string operationId = pendingOutgoingBusOperationIds.Peek();
            if (!pendingOutgoingBusPassengerCounts.TryGetValue(
                operationId, out int remaining) || remaining <= 0)
            {
                pendingOutgoingBusOperationIds.Dequeue();
                pendingOutgoingBusQueuedOperationIds.Remove(operationId);
                pendingOutgoingBusPassengerCounts.Remove(operationId);
                pendingOutgoingBusGateIds.Remove(operationId);
                continue;
            }

            pendingOutgoingBusGateIds.TryGetValue(
                operationId, out string gateStableId);
            Vector3 spawnPosition = bus.transform.position + Vector3.up * 0.1f;
            GameObject passengerObject = Instantiate(
                airlinePassengerPrefab, spawnPosition, Quaternion.identity);
            AirlinePassengerAgent agent =
                passengerObject.GetComponent<AirlinePassengerAgent>();

            if (agent == null || !agent.Initialize(
                operationId, PassengerIdentity.TransportMode.Bus))
            {
                Destroy(passengerObject);
                Debug.LogWarning(
                    $"PassengerCoordinator: Could not create a queued bus passenger for flight {operationId}; they will remain queued for the next bus.",
                    this);
                break;
            }

            if (agent.Movement != null)
                agent.Movement.AlignFeetToGround(spawnPosition);

            if (!bus.TryBoardPassenger(agent))
            {
                Destroy(passengerObject);
                break;
            }

            manifest.Add(new BusPassengerRecord
            {
                Agent = agent,
                GateStableId = gateStableId
            });
            remaining--;
            if (remaining > 0)
            {
                pendingOutgoingBusPassengerCounts[operationId] = remaining;
            }
            else
            {
                pendingOutgoingBusPassengerCounts.Remove(operationId);
                pendingOutgoingBusGateIds.Remove(operationId);
                pendingOutgoingBusQueuedOperationIds.Remove(operationId);
                pendingOutgoingBusOperationIds.Dequeue();
            }
        }

        NotifyTransportStatisticsChanged();
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

        busPassengersTransported++;
        NotifyTransportStatisticsChanged();

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
                    busPassengersTransported++;
                    passengers[i].CompleteReturnBusJourney();
                }
            returnPassengersByBus.Remove(bus);
            NotifyTransportStatisticsChanged();
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
        bus.PassengerBoarded -= HandleBusPassengerBoarded;
        bus.PassengerDisembarked -= HandleBusPassengerDisembarked;
        bus.DisembarkingCompleted -= HandleBusDisembarkingCompleted;
        subscribedBuses.Remove(bus);
    }

    private void HandlePassengersDeboardingStarted(AircraftTurnaroundController turnaround)
    {
        if (turnaround == null)
            return;

        AircraftFlightIdentity identity =
            turnaround.GetComponent<AircraftFlightIdentity>();
        AircraftStats stats = turnaround.GetComponent<AircraftStats>();

        if (identity == null || !identity.IsInitialized ||
            identity.Definition == null || stats == null ||
            (identity.Definition.OperationType != FlightOperationType.PassengerOnly &&
             identity.Definition.OperationType != FlightOperationType.CargoAndPassengers))
        {
            Debug.LogError(
                $"PassengerCoordinator: Cannot resolve the passenger flight manifest for '{turnaround.name}'. Deboarding remains in progress.",
                turnaround);
            return;
        }

        if (stats.IncomingPassengers <= 0)
        {
            if (logToConsole)
                Debug.Log($"PassengerCoordinator: Flight {identity.FlightNumber} has no incoming passengers to deboard.", turnaround);
            turnaround.CompleteExternalDeboarding();
            return;
        }

        if (airlinePassengerPrefab == null)
        {
            Debug.LogError(
                $"PassengerCoordinator: Airline passenger prefab is missing. Deboarding for flight {identity.FlightNumber} remains in progress.",
                turnaround);
            return;
        }

        string gateId = identity.HasActualGate
            ? identity.ActualGateStableId
            : identity.PlannedGateStableId;

        AircraftGate gate = null;
        if (gateRegistry != null &&
            !string.IsNullOrEmpty(gateId) &&
            gateRegistry.TryGetGate(gateId, out AircraftGate resolvedGate))
        {
            gate = resolvedGate;
        }

        if (gate == null)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Cannot resolve gate '{gateId}' for arriving flight {identity.FlightNumber}. Passengers will spawn at the parked aircraft position.",
                turnaround);
        }

        int incomingCarCount =
            CalculateCarPassengerCount(stats.IncomingPassengers);
        registeredIncomingCarPassengerCounts[identity.OperationId] =
            incomingCarCount;
        int incomingBusCount =
            Mathf.Max(0, stats.IncomingPassengers - incomingCarCount);

        PlacedBuilding firstStop = incomingBusCount > 0 && busSpawner != null
            ? busSpawner.GetNextFirstStop()
            : null;

        if (incomingBusCount > 0 && firstStop == null)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: No first bus stop is available for flight {identity.FlightNumber}. Bus-assigned passengers will still deboard visibly at the gate.",
                turnaround);
        }

        PlacedBuilding gateFootprint = null;
        if (gate != null)
        {
            gateFootprint = gate.GetComponent<PlacedBuilding>();
            if (gateFootprint == null)
                gateFootprint = gate.GetComponentInParent<PlacedBuilding>();
        }

        IncomingPassengerSpawnJob job = new IncomingPassengerSpawnJob
        {
            Turnaround = turnaround,
            OperationId = identity.OperationId,
            FlightNumber = identity.FlightNumber,
            PassengerCount = stats.IncomingPassengers,
            CarPassengerCount = Mathf.Clamp(incomingCarCount, 0, stats.IncomingPassengers),
            Gate = gate,
            GateFootprint = gateFootprint,
            FirstStop = firstStop
        };

        if (logToConsole)
            Debug.Log($"PassengerCoordinator: Starting deboarding for flight {identity.FlightNumber}: {job.PassengerCount} real passenger agent(s), one per simulated second.", turnaround);

        incomingPassengerSpawnJobs.Enqueue(job);
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
        {
            busWaitStartTimes[passenger] = GameTimeManager.SimulationTime;
            waitingReturnPassengers.Enqueue(passenger);
            NotifyTransportStatisticsChanged();
        }
    }

    private bool QueueReturnPassengerForBus(
        BusController bus,
        AirlinePassengerAgent passenger)
    {
        if (bus == null || passenger == null ||
            !bus.QueuePassengerForBoarding(passenger))
            return false;

        if (!busWaitStartTimes.ContainsKey(passenger))
            busWaitStartTimes[passenger] = GameTimeManager.SimulationTime;

        waitingReturnPassengerSet.Add(passenger);
        if (!returnPassengersByBus.TryGetValue(
            bus, out List<AirlinePassengerAgent> onboardReturnPassengers))
        {
            onboardReturnPassengers = new List<AirlinePassengerAgent>();
            returnPassengersByBus.Add(bus, onboardReturnPassengers);
        }

        onboardReturnPassengers.Add(passenger);
        NotifyTransportStatisticsChanged();
        return true;
    }

    private void HandleBusPassengerBoarded(
        BusController bus,
        AirlinePassengerAgent passenger)
    {
        if (passenger == null)
            return;

        waitingReturnPassengerSet.Remove(passenger);
        if (busWaitStartTimes.TryGetValue(passenger, out double waitStartedAt))
        {
            cumulativeBusWaitSeconds += System.Math.Max(
                0.0, GameTimeManager.SimulationTime - waitStartedAt);
            completedBusWaitMeasurements++;
            busWaitStartTimes.Remove(passenger);
        }

        NotifyTransportStatisticsChanged();
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
            registeredIncomingCarPassengerCounts.Remove(staleOperationIds[i]);
            registeredOutgoingCarPassengerCounts.Remove(staleOperationIds[i]);
            spawnedOutgoingCarPassengerCounts.Remove(staleOperationIds[i]);
            outgoingCarDispatchProcessedOperationIds.Remove(staleOperationIds[i]);
            pendingOutgoingBusPassengerCounts.Remove(staleOperationIds[i]);
            pendingOutgoingBusGateIds.Remove(staleOperationIds[i]);
            pendingOutgoingBusQueuedOperationIds.Remove(staleOperationIds[i]);
        }
    }

    private int CalculateCarPassengerCount(int totalPassengers)
    {
        if (totalPassengers <= 0)
            return 0;

        float busCapacityPerHour = publicTransportManager != null
            ? publicTransportManager.GetServiceCapacityPerHour(
                PassengerIdentity.TransportMode.Bus, busCapacityPerDeparture)
            : 0f;
        float trainCapacityPerHour = publicTransportManager != null
            ? publicTransportManager.GetServiceCapacityPerHour(
                PassengerIdentity.TransportMode.Train, busCapacityPerDeparture)
            : 0f;
        float undergroundCapacityPerHour = publicTransportManager != null
            ? publicTransportManager.GetServiceCapacityPerHour(
                PassengerIdentity.TransportMode.Underground, busCapacityPerDeparture)
            : 0f;

        float demandTarget = Mathf.Max(1, targetHourlyPassengerDemand);
        float busScore = busTransportWeight *
            Mathf.Clamp01(busCapacityPerHour / demandTarget);
        // Capacity inputs are ready for future service implementations, but
        // these modes stay out of live allocation until their journeys exist.
        float trainScore = enableTrainPassengerAssignment
            ? trainTransportWeight * Mathf.Clamp01(trainCapacityPerHour / demandTarget)
            : 0f;
        float undergroundScore = enableUndergroundPassengerAssignment
            ? undergroundTransportWeight * Mathf.Clamp01(undergroundCapacityPerHour / demandTarget)
            : 0f;

        RefreshParkingSpaceCache();
        int totalParkingSpaces = cachedParkingSpaceCount;
        float parkingCapacityFactor = Mathf.Clamp01(
            (float)totalParkingSpaces / Mathf.Max(1, targetParkingSpaceCount));

        // A passenger car is expected to occupy the space for the mean of the
        // configured 2–15 minute dwell range.
        const float expectedParkingMinutes = 8.5f;
        float expectedParkingCost =
            Mathf.Max(0f, passengerCarParkingFeePerMinute) * expectedParkingMinutes;
        float parkingPriceFactor = 1f /
            (1f + expectedParkingCost / Mathf.Max(0.01f, referenceParkingCost));

        float carScore =
            carTransportWeight * parkingCapacityFactor * parkingPriceFactor;

        float totalScore = busScore + trainScore + undergroundScore + carScore;
        if (totalScore <= 0.0001f)
        {
            // If no public-transport service can carry passengers, use cars
            // as the fallback; their vehicles can use the roadside drop-off.
            return totalPassengers;
        }

        int carPassengers = Mathf.RoundToInt(
            totalPassengers * carScore / totalScore);

        if (logToConsole)
        {
            Debug.Log(
                $"Passenger transport split | Demand: {totalPassengers} | " +
                $"Bus capacity/h: {busCapacityPerHour:F1} | Train capacity/h: {trainCapacityPerHour:F1} | " +
                $"Underground capacity/h: {undergroundCapacityPerHour:F1} | " +
                $"Scores B/T/U/C: {busScore:F2}/{trainScore:F2}/{undergroundScore:F2}/{carScore:F2} | " +
                $"Parking spaces: {totalParkingSpaces} | Fee/min: {passengerCarParkingFeePerMinute:F2} | " +
                $"Cars: {carPassengers} | Bus: {totalPassengers - carPassengers}",
                this);
        }

        return Mathf.Clamp(carPassengers, 0, totalPassengers);
    }

    private void QueueIncomingCarPickupGroups(IncomingPassengerSpawnJob job)
    {
        if (job == null || job.CarPassengers.Count == 0)
            return;

        int nextPassenger = 0;
        while (nextPassenger < job.CarPassengers.Count)
        {
            int groupSize = Random.Range(1, 5);
            int remaining = job.CarPassengers.Count - nextPassenger;
            groupSize = Mathf.Min(groupSize, remaining);

            IncomingCarPickupGroup group = new IncomingCarPickupGroup
            {
                Gate = job.Gate,
                GateFootprint = job.GateFootprint
            };

            for (int i = 0; i < groupSize; i++)
            {
                AirlinePassengerAgent passenger =
                    job.CarPassengers[nextPassenger++];
                if (passenger != null)
                    group.Passengers.Add(passenger);
            }

            if (group.Passengers.Count > 0)
                pendingIncomingCarPickups.Enqueue(group);
        }

        TrySpawnPendingIncomingCarPickups();
    }

    private int EnsureOutgoingCarDropoffGroupsSpawned(
        ScheduledPassengerFlight flight)
    {
        if (string.IsNullOrWhiteSpace(flight.OperationId))
            return 0;

        if (outgoingCarDispatchProcessedOperationIds.Contains(flight.OperationId))
            return spawnedOutgoingCarPassengerCounts.TryGetValue(
                flight.OperationId, out int alreadySpawned)
                ? alreadySpawned
                : 0;

        int totalPassengers =
            registeredPassengerCounts.TryGetValue(
                flight.OperationId, out int registeredCount)
                ? registeredCount
                : 0;
        int requestedPassengers = CalculateCarPassengerCount(totalPassengers);
        registeredOutgoingCarPassengerCounts[flight.OperationId] =
            requestedPassengers;
        int spawnedPassengers =
            SpawnOutgoingCarDropoffGroups(flight, requestedPassengers);

        outgoingCarDispatchProcessedOperationIds.Add(flight.OperationId);
        spawnedOutgoingCarPassengerCounts[flight.OperationId] = spawnedPassengers;
        // Keep the full flight demand. Car failures fall back into the bus queue.
        bookedPassengerCounts[flight.OperationId] = totalPassengers;
        return spawnedPassengers;
    }

    private void ResolvePassengerRoadsideDependencies()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (roadNetwork == null)
            roadNetwork = FindAnyObjectByType<RoadNetwork>();
    }

    private void InvalidatePassengerRoadsideDropoff()
    {
        hasCachedPassengerRoadsideDropoffPosition = false;
    }

    private bool TryGetPassengerRoadsideDropoffPosition(out Vector3 position)
    {
        ResolvePassengerRoadsideDependencies();
        position = Vector3.zero;

        if (gridManager == null || roadNetwork == null || trafficSpawner == null)
        {
            Debug.LogWarning(
                "PassengerCoordinator: Cannot resolve passenger roadside drop-off because a dependency is missing. " +
                $"GridManager={(gridManager != null ? gridManager.name : "NULL")}, " +
                $"RoadNetwork={(roadNetwork != null ? roadNetwork.name : "NULL")}, " +
                $"TrafficSpawner={(trafficSpawner != null ? trafficSpawner.name : "NULL")}.",
                this);
            return false;
        }

        if (hasCachedPassengerRoadsideDropoffPosition &&
            cachedRoadTopologyVersion == roadNetwork.TopologyVersion)
        {
            position = cachedPassengerRoadsideDropoffPosition;
            return true;
        }

        Vector2Int spawnerGridCell =
            gridManager.WorldToGrid(trafficSpawner.transform.position);
        Vector2Int entryCell = roadNetwork.GetRoadAnchor(spawnerGridCell);
        RoadNetwork.RoadPathTree reachableRoads =
            roadNetwork.BuildForwardPathTree(entryCell);
        List<Vector2Int> roadPositions = roadNetwork.GetRoadPositions();
        List<Vector2Int> terminalFoundationCells = new List<Vector2Int>();
        IReadOnlyList<PlacedBuilding> placedBuildings = PlacedBuilding.ActiveBuildings;
        for (int i = 0; i < placedBuildings.Count; i++)
        {
            PlacedBuilding building = placedBuildings[i];
            if (building == null || building.Definition == null ||
                building.Definition.id != "floor.foundation.raw")
                continue;

            for (int x = 0; x < Mathf.Max(1, building.Width); x++)
            {
                for (int z = 0; z < Mathf.Max(1, building.Depth); z++)
                    terminalFoundationCells.Add(building.Origin + new Vector2Int(x, z));
            }
        }

        int foundationCount = terminalFoundationCells.Count;
        if (foundationCount == 0)
        {
            Debug.LogWarning(
                "PassengerCoordinator: No placed terminal foundation buildings were found. " +
                "Expected BuildableDefinition id 'floor.foundation.raw'. " +
                $"Active placed buildings={placedBuildings.Count}.",
                this);
            return false;
        }

        float bestDistance = float.MaxValue;
        bool found = false;
        int reachableRoadCount = 0;

        foreach (Vector2Int roadCell in roadPositions)
        {
            // Only choose road modules the passenger spawner can actually reach.
            if (reachableRoads.GetPath(roadCell).Count == 0)
                continue;

            reachableRoadCount++;
            Vector2Int roadCenterCell = roadCell + Vector2Int.one;
            float nearestFoundationDistance = float.MaxValue;
            for (int foundationIndex = 0; foundationIndex < terminalFoundationCells.Count; foundationIndex++)
            {
                Vector2Int foundationCell = terminalFoundationCells[foundationIndex];
                float dx = roadCenterCell.x - (foundationCell.x + 0.5f);
                float dz = roadCenterCell.y - (foundationCell.y + 0.5f);
                float distance = dx * dx + dz * dz;
                if (distance < nearestFoundationDistance)
                    nearestFoundationDistance = distance;
            }

            if (nearestFoundationDistance >= bestDistance)
                continue;

            bestDistance = nearestFoundationDistance;
            cachedPassengerRoadsideDropoffPosition =
                roadNetwork.GetRoadCenterWorldPosition(roadCell, gridManager);
            found = true;
        }

        if (!found)
        {
            Debug.LogWarning(
                "PassengerCoordinator: Could not find a reachable road module for passenger drop-off. " +
                $"Spawner world position={trafficSpawner.transform.position}, spawner grid cell={spawnerGridCell}, " +
                $"resolved road entry cell={entryCell}, road modules={roadPositions.Count}, " +
                $"reachable road modules={reachableRoadCount}, terminal foundation tiles={foundationCount}. " +
                "Check that the TrafficTestSpawner is positioned on/next to the connected road network and that the road network reaches the terminal.",
                this);
            return false;
        }

        Debug.Log(
            "PassengerCoordinator: Resolved passenger roadside drop-off. " +
            $"Entry cell={entryCell}, reachable road modules={reachableRoadCount}/{roadPositions.Count}, " +
            $"terminal foundation tiles={foundationCount}, drop-off world position={cachedPassengerRoadsideDropoffPosition}.",
            this);

        cachedRoadTopologyVersion = roadNetwork.TopologyVersion;
        hasCachedPassengerRoadsideDropoffPosition = true;
        position = cachedPassengerRoadsideDropoffPosition;
        return true;
    }

    private int SpawnOutgoingCarDropoffGroups(
        ScheduledPassengerFlight flight,
        int requestedPassengers)
    {
        if (requestedPassengers <= 0)
            return 0;

        if (trafficSpawner == null)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Outbound car dispatch failed for flight {flight.OperationId}: TrafficTestSpawner reference is NULL.",
                this);
            return 0;
        }

        if (airlinePassengerPrefab == null)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Outbound car dispatch failed for flight {flight.OperationId}: Airline Passenger Prefab is NULL.",
                this);
            return 0;
        }

        if (airlinePassengerCarPrefab == null)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Outbound car dispatch failed for flight {flight.OperationId}: Passenger Car Prefab is NULL.",
                this);
            return 0;
        }

        if (!TryGetPassengerRoadsideDropoffPosition(out Vector3 roadsideDropoffPosition))
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Outbound car dispatch failed for flight {flight.OperationId}: no valid roadside drop-off position could be resolved. See the preceding roadside diagnostic for the exact reason.",
                this);
            return 0;
        }

        if (gateRegistry == null)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Outbound car dispatch failed for flight {flight.OperationId}: AircraftGateRegistry reference is NULL.",
                this);
            return 0;
        }

        if (!gateRegistry.TryGetGate(flight.GateStableId, out AircraftGate gate) ||
            gate == null)
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Outbound car dispatch failed for flight {flight.OperationId}: gate lookup failed. " +
                $"GateStableId='{flight.GateStableId}', registry='{gateRegistry.name}'. " +
                "Check that this flight's gate stable ID matches a currently registered gate.",
                this);
            return 0;
        }

        PlacedBuilding gateFootprint = gate.GetComponent<PlacedBuilding>();
        if (gateFootprint == null)
            gateFootprint = gate.GetComponentInParent<PlacedBuilding>();

        int assignedPassengers = 0;
        while (assignedPassengers < requestedPassengers)
        {
            int groupSize = Mathf.Min(
                Random.Range(1, 5), requestedPassengers - assignedPassengers);

            TrafficTestVehicle car = trafficSpawner.SpawnPassengerArrivalVehicle(
                airlinePassengerCarPrefab,
                roadsideDropoffPosition,
                Random.Range(120f, 900f),
                120f);

            if (car == null)
            {
                Debug.LogWarning(
                    $"PassengerCoordinator: Could not spawn an outbound passenger car for flight {flight.OperationId}. Remaining car passengers will use the bus manifest.",
                    this);
                break;
            }

            OutgoingCarDropoffGroup group = new OutgoingCarDropoffGroup
            {
                OperationId = flight.OperationId,
                Gate = gate,
                GateFootprint = gateFootprint,
                PassengerCount = groupSize,
                Vehicle = car
            };

            outgoingCarGroupsByVehicle.Add(car, group);
            car.Parked += HandleOutgoingPassengerCarParked;
            car.RoadsideDropoffStarted += HandleOutgoingPassengerCarRoadsideDropoff;
            car.Exited += HandleOutgoingPassengerCarExited;
            assignedPassengers += groupSize;
        }

        return assignedPassengers;
    }

    private void HandleOutgoingPassengerCarParked(TrafficTestVehicle car)
    {
        HandleOutgoingPassengerCarArrived(car, true);
    }

    private void HandleOutgoingPassengerCarRoadsideDropoff(TrafficTestVehicle car)
    {
        HandleOutgoingPassengerCarArrived(car, false);
    }

    private void HandleOutgoingPassengerCarArrived(
        TrafficTestVehicle car,
        bool isParked)
    {
        if (car == null ||
            !outgoingCarGroupsByVehicle.TryGetValue(
                car, out OutgoingCarDropoffGroup group) ||
            group.PassengersSpawned)
            return;

        group.PassengersSpawned = true;
        if (group.Gate == null)
        {
            ReduceBookedOutgoingPassengers(group.OperationId, group.PassengerCount);
            Debug.LogWarning(
                $"PassengerCoordinator: Gate was removed before outbound passengers for flight {group.OperationId} could leave their car.",
                car);
            return;
        }

        PlacedBuilding carFootprint = isParked && car.CurrentParkingSpace != null
            ? car.CurrentParkingSpace.GetComponent<PlacedBuilding>()
            : null;

        for (int i = 0; i < group.PassengerCount; i++)
        {
            Vector3 desiredPosition =
                car.transform.position + car.transform.right * 1.5f;
            Vector3 passengerGroundPosition = desiredPosition;
            if (pedestrianPathfinder != null &&
                !pedestrianPathfinder.TryFindNearestWalkablePosition(
                    desiredPosition, 8, out passengerGroundPosition))
            {
                ReduceBookedOutgoingPassengers(group.OperationId, 1);
                continue;
            }

            Vector3 spawnPosition =
                passengerGroundPosition + Vector3.up * 0.1f;
            GameObject passengerObject = Instantiate(
                airlinePassengerPrefab, spawnPosition, Quaternion.identity);
            AirlinePassengerAgent passenger =
                passengerObject.GetComponent<AirlinePassengerAgent>();

            if (passenger == null ||
                !passenger.Initialize(
                    group.OperationId, PassengerIdentity.TransportMode.Car))
            {
                Destroy(passengerObject);
                ReduceBookedOutgoingPassengers(group.OperationId, 1);
                continue;
            }

            if (passenger.Movement != null)
                passenger.Movement.AlignFeetToGround(spawnPosition);

            if (!passenger.WalkToTerminalFromCar(
                group.Gate.transform.position,
                carFootprint,
                group.GateFootprint))
            {
                Debug.LogWarning(
                    $"PassengerCoordinator: Outbound car passenger for flight {group.OperationId} could not find a walking route to gate {group.Gate.name}.",
                    passenger);
                passenger.AbandonJourney();
                Destroy(passengerObject);
                ReduceBookedOutgoingPassengers(group.OperationId, 1);
                continue;
            }

            if (!terminalPassengersByOperationId.TryGetValue(
                group.OperationId, out List<AirlinePassengerAgent> passengers))
            {
                passengers = new List<AirlinePassengerAgent>();
                terminalPassengersByOperationId.Add(group.OperationId, passengers);
            }
            passengers.Add(passenger);
            carPassengersTransported++;
        }

        NotifyTransportStatisticsChanged();
    }

    private void ReduceBookedOutgoingPassengers(string operationId, int amount)
    {
        if (amount <= 0 ||
            !bookedPassengerCounts.TryGetValue(operationId, out int booked))
            return;

        bookedPassengerCounts[operationId] = Mathf.Max(0, booked - amount);
    }

    private void HandleOutgoingPassengerCarExited(TrafficTestVehicle car)
    {
        if (car == null ||
            !outgoingCarGroupsByVehicle.TryGetValue(
                car, out OutgoingCarDropoffGroup group))
            return;

        outgoingCarGroupsByVehicle.Remove(car);
        car.Parked -= HandleOutgoingPassengerCarParked;
        car.RoadsideDropoffStarted -= HandleOutgoingPassengerCarRoadsideDropoff;
        car.Exited -= HandleOutgoingPassengerCarExited;
    }

    private void HandleParkingAvailabilityChanged()
    {
        RefreshParkingSpaceCache();
        NotifyTransportStatisticsChanged();
        TrySpawnPendingIncomingCarPickups();
    }

    private void HandleParkingBuildingChanged(PlacedBuilding building)
    {
        // Placed foundation tiles are the authoritative terminal footprint.
        // Any placement/removal may change the nearest valid roadside stop.
        InvalidatePassengerRoadsideDropoff();
        parkingSpaceCacheDirty = true;
        RefreshParkingSpaceCache();
        NotifyTransportStatisticsChanged();

        if (building != null && building.GetComponent<ParkingSpace>() != null)
            TrySpawnPendingIncomingCarPickups();
    }

    private bool HasFreePassengerParkingSpace()
    {
        RefreshParkingSpaceCache();
        for (int i = 0; i < cachedParkingSpaces.Length; i++)
        {
            ParkingSpace space = cachedParkingSpaces[i];
            if (space != null && !space.IsOccupied && !space.IsReserved)
                return true;
        }
        return false;
    }

    private void TrySpawnPendingIncomingCarPickups()
    {
        if (spawningIncomingCar || !isActiveAndEnabled ||
            pendingIncomingCarPickups.Count == 0 ||
            trafficSpawner == null || airlinePassengerCarPrefab == null)
            return;

        spawningIncomingCar = true;
        try
        {
            while (pendingIncomingCarPickups.Count > 0 &&
                   HasFreePassengerParkingSpace())
            {
                IncomingCarPickupGroup group =
                    pendingIncomingCarPickups.Dequeue();

                TrafficTestVehicle car =
                    trafficSpawner.SpawnExternallyControlledVehicle(
                        airlinePassengerCarPrefab);

                // SpawnExternallyControlledVehicle reserves a free space
                // synchronously. Reject a vehicle that could not reserve one.
                if (car == null || car.CurrentParkingSpace == null)
                {
                    if (car != null)
                        Destroy(car.gameObject);
                    pendingIncomingCarPickups.Enqueue(group);
                    break;
                }

                group.Vehicle = car;
                incomingCarGroupsByVehicle.Add(car, group);
                car.Parked += HandleIncomingPickupCarParked;
                car.Exited += HandleIncomingPickupCarExited;
            }
        }
        finally
        {
            spawningIncomingCar = false;
        }
    }

    private void HandleIncomingPickupCarParked(TrafficTestVehicle car)
    {
        if (car == null ||
            !incomingCarGroupsByVehicle.TryGetValue(
                car, out IncomingCarPickupGroup group))
            return;

        ParkingSpace parking = car.CurrentParkingSpace;
        PlacedBuilding parkingFootprint = parking != null
            ? parking.GetComponent<PlacedBuilding>()
            : null;
        Vector3 desiredPosition =
            car.ParkingPosition + car.transform.right * 1.5f;

        if (pedestrianPathfinder == null ||
            !pedestrianPathfinder.TryFindNearestWalkablePosition(
                desiredPosition, 8, out Vector3 pickupPosition))
        {
            Debug.LogWarning(
                $"PassengerCoordinator: Could not find a pedestrian pickup position beside car {car.name}.",
                car);
            AbandonIncomingPickupGroup(group);
            car.RequestDeparture();
            return;
        }

        int routedPassengers = 0;
        for (int i = 0; i < group.Passengers.Count; i++)
        {
            AirlinePassengerAgent passenger = group.Passengers[i];
            if (passenger == null)
                continue;

            passenger.ReachedCarPickup += HandlePassengerReachedIncomingCar;
            incomingCarGroupsByPassenger[passenger] = group;

            if (passenger.WalkToCarPickup(
                pickupPosition, group.GateFootprint, parkingFootprint))
            {
                routedPassengers++;
            }
            else
            {
                passenger.ReachedCarPickup -= HandlePassengerReachedIncomingCar;
                incomingCarGroupsByPassenger.Remove(passenger);
                passenger.AbandonJourney();
                Destroy(passenger.gameObject);
                group.Passengers[i] = null;
            }
        }

        if (routedPassengers == 0)
            car.RequestDeparture();
    }

    private void HandlePassengerReachedIncomingCar(
        AirlinePassengerAgent passenger)
    {
        if (passenger == null ||
            !incomingCarGroupsByPassenger.TryGetValue(
                passenger, out IncomingCarPickupGroup group))
            return;

        passenger.ReachedCarPickup -= HandlePassengerReachedIncomingCar;
        incomingCarGroupsByPassenger.Remove(passenger);

        if (group.Vehicle == null ||
            !passenger.BoardCar(group.Vehicle.transform))
        {
            passenger.AbandonJourney();
            Destroy(passenger.gameObject);
            return;
        }

        group.BoardedCount++;
        int remainingPassengers = 0;
        for (int i = 0; i < group.Passengers.Count; i++)
            if (group.Passengers[i] != null)
                remainingPassengers++;

        carPassengersTransported++;
        NotifyTransportStatisticsChanged();

        if (group.BoardedCount >= remainingPassengers &&
            group.Vehicle.IsParked)
            group.Vehicle.RequestDeparture();
    }

    private void HandleIncomingPickupCarExited(TrafficTestVehicle car)
    {
        if (car == null ||
            !incomingCarGroupsByVehicle.TryGetValue(
                car, out IncomingCarPickupGroup group))
            return;

        car.Parked -= HandleIncomingPickupCarParked;
        car.Exited -= HandleIncomingPickupCarExited;
        incomingCarGroupsByVehicle.Remove(car);

        for (int i = 0; i < group.Passengers.Count; i++)
        {
            AirlinePassengerAgent passenger = group.Passengers[i];
            if (passenger == null)
                continue;

            passenger.ReachedCarPickup -= HandlePassengerReachedIncomingCar;
            incomingCarGroupsByPassenger.Remove(passenger);
            if (passenger.State == AirlinePassengerAgent.JourneyState.OnCar)
                passenger.CompleteReturnCarJourney();
            else
            {
                passenger.AbandonJourney();
                Destroy(passenger.gameObject);
            }
        }

        HandleParkingAvailabilityChanged();
    }

    private void AbandonIncomingPickupGroup(IncomingCarPickupGroup group)
    {
        if (group == null)
            return;

        for (int i = 0; i < group.Passengers.Count; i++)
        {
            AirlinePassengerAgent passenger = group.Passengers[i];
            if (passenger == null)
                continue;

            passenger.ReachedCarPickup -= HandlePassengerReachedIncomingCar;
            incomingCarGroupsByPassenger.Remove(passenger);
            passenger.AbandonJourney();
            Destroy(passenger.gameObject);
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