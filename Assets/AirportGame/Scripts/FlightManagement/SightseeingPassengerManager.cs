using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Creates sightseeing passenger cohorts one in-game hour before each
/// scheduled flight and coordinates check-in, boarding and return journeys.
/// </summary>
[DisallowMultipleComponent]
public class SightseeingPassengerManager : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    private const string DeskDefinitionId = "airport.sightseeing.ticketdesk";

    [Header("Prefabs")]
    [SerializeField] private GameObject passengerPrefab;
    [SerializeField] private GameObject passengerCarPrefab;

    [Header("Ticket Desk")]
    [SerializeField] private InteractionDefinition checkInInteraction;
    [Min(0.1f)] [SerializeField] private float checkInRetrySeconds = 5f;

    [Header("Public Terminal Waiting Area")]
    [SerializeField] private Transform publicTerminalWaitingArea;
    [Min(1)] [SerializeField] private int waitingAreaRadiusCells = 8;

    [Header("Road Arrival")]
    [Min(0f)] [SerializeField] private float carSpawnIntervalGameSeconds = 1.5f;
    [Min(0.1f)] [SerializeField] private float carExitOffset = 1.5f;

    [Header("Scene References")]
    [SerializeField] private TrafficTestSpawner trafficSpawner;
    [SerializeField] private SightseeingAircraftSpawner aircraftSpawner;
    [SerializeField] private SightseeingFlightEconomy sightseeingEconomy;
    [SerializeField] private GameTimeManager gameTimeManager;
    [SerializeField] private PedestrianPathfinder pedestrianPathfinder;
    [SerializeField] private RoomPurposeController roomPurposeController;

    [Header("Runtime")]
    [SerializeField] private string debugStatus = "Waiting for sightseeing schedule.";
    [SerializeField] private int activePassengers;
    [SerializeField] private int pendingFlights;

    private sealed class PassengerRecord
    {
        public TrafficTestVehicle vehicle;
        public SightseeingPassengerAgent agent;
        public bool checkedIn;
        public bool onBoard;
        public bool completed;
        public bool gatheringResolved;
    }

    private sealed class FlightGroup
    {
        public long scheduledHourSlot;
        public long scheduledBoardingGameSecond;
        public AircraftFlightIdentity identity;
        public AircraftSSTurnaroundController turnaround;
        public SightseeingAircraftParkingSpot parkingSpot;
        public readonly List<PassengerRecord> passengers = new List<PassengerRecord>();
        public int nextBoardingIndex;
        public readonly Queue<PassengerRecord> waitingToBoard = new Queue<PassengerRecord>();
        public bool boardingPassengerActive;
        public bool boardingStarted;
        public bool returned;
        public bool cancelled;
        public int carSpawnCount;
        public int nextCarSpawnIndex;
        public long carSpawnEventId;
    }

    private readonly List<FlightGroup> activeGroups = new List<FlightGroup>();
    private bool subscribedToSpawner;
    private bool subscribedToGameTime;
    private bool initialSchedulePrepared;
    private double nextCheckInRetryTime;

    private void Awake() => ResolveReferences();

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToSpawner();
        SubscribeToGameTime();
        ResumePendingCarSpawns();
    }

    private void OnDisable()
    {
        if (aircraftSpawner != null && subscribedToSpawner)
        {
            aircraftSpawner.FlightScheduled -= HandleFlightScheduled;
            aircraftSpawner.AircraftSpawned -= HandleAircraftSpawned;
        }
        subscribedToSpawner = false;
        CancelPendingCarSpawnEvents();
        if (gameTimeManager != null && subscribedToGameTime)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
        subscribedToGameTime = false;
    }

    private void Update()
    {
        ResolveReferences();
        SubscribeToSpawner();
        SubscribeToGameTime();

        if (!SightseeingAircraftSpawner.HasTicketDeskPlaced())
            CancelUnspawnedGroupsWithoutDesk();

        activePassengers = 0;
        for (int i = 0; i < activeGroups.Count; i++)
        for (int j = 0; j < activeGroups[i].passengers.Count; j++)
        {
            PassengerRecord record = activeGroups[i].passengers[j];
            if (record != null && !record.completed && record.agent != null)
                activePassengers++;
        }
        pendingFlights = 0;
        for (int i = 0; i < activeGroups.Count; i++)
            if (activeGroups[i].turnaround == null) pendingFlights++;

        if (!initialSchedulePrepared && aircraftSpawner != null &&
            aircraftSpawner.isActiveAndEnabled && gameTimeManager != null &&
            passengerPrefab != null && passengerCarPrefab != null &&
            trafficSpawner != null &&
            SightseeingAircraftSpawner.HasTicketDeskPlaced())
        {
            initialSchedulePrepared = true;
            long currentSlot = gameTimeManager.CurrentDay * 24L + gameTimeManager.Hour;
            HandleFlightScheduled(currentSlot + 1L);
        }

        if (passengerPrefab == null || passengerCarPrefab == null ||
            trafficSpawner == null || aircraftSpawner == null ||
            gameTimeManager == null || pedestrianPathfinder == null)
        {
            debugStatus = "Assign passenger/car prefabs and ensure traffic, sightseeing, time and pathfinding systems exist.";
            return;
        }

        if (GameTimeManager.SimulationTime >= nextCheckInRetryTime)
        {
            nextCheckInRetryTime = GameTimeManager.SimulationTime + Mathf.Max(0.1f, checkInRetrySeconds);
            RetryCheckIn();
        }
    }

    private void ResolveReferences()
    {
        if (trafficSpawner == null) trafficSpawner = FindAnyObjectByType<TrafficTestSpawner>();
        if (aircraftSpawner == null) aircraftSpawner = FindAnyObjectByType<SightseeingAircraftSpawner>();
        if (sightseeingEconomy == null) sightseeingEconomy = SightseeingFlightEconomy.Instance;
        if (gameTimeManager == null) gameTimeManager = FindAnyObjectByType<GameTimeManager>();
        if (pedestrianPathfinder == null) pedestrianPathfinder = FindAnyObjectByType<PedestrianPathfinder>();
        if (roomPurposeController == null) roomPurposeController = FindAnyObjectByType<RoomPurposeController>();
    }

    private void SubscribeToGameTime()
    {
        if (gameTimeManager == null || subscribedToGameTime)
            return;

        gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
        subscribedToGameTime = true;
    }

    private void HandleGameTimeAdvanced(long previousSecond, long currentSecond)
    {
        if (currentSecond <= previousSecond)
            return;
        UpdateDepartureUrgencies(currentSecond);
    }

    private void UpdateDepartureUrgencies(long currentSecond)
    {
        for (int i = 0; i < activeGroups.Count; i++)
        {
            FlightGroup group = activeGroups[i];
            if (group == null || group.cancelled || group.boardingStarted)
                continue;

            float urgency = CalculateDepartureUrgency(
                group.scheduledBoardingGameSecond - currentSecond);
            for (int j = 0; j < group.passengers.Count; j++)
            {
                PassengerRecord record = group.passengers[j];
                if (record != null && record.agent != null && !record.completed)
                    record.agent.SetDepartureUrgency(urgency);
            }
        }
    }

    private static float CalculateDepartureUrgency(long secondsRemaining)
    {
        if (secondsRemaining >= 3600L) return 0f;
        if (secondsRemaining <= 0L) return 100f;

        if (secondsRemaining >= 1800L)
            return Mathf.SmoothStep(0f, 20f, (3600f - secondsRemaining) / 1800f);
        if (secondsRemaining >= 1200L)
            return Mathf.SmoothStep(20f, 40f, (1800f - secondsRemaining) / 600f);
        if (secondsRemaining >= 600L)
            return Mathf.SmoothStep(40f, 70f, (1200f - secondsRemaining) / 600f);
        if (secondsRemaining >= 300L)
            return Mathf.SmoothStep(70f, 90f, (600f - secondsRemaining) / 300f);
        return Mathf.SmoothStep(90f, 100f, (300f - secondsRemaining) / 300f);
    }

    private long GetAbsoluteGameSecondForHourSlot(long hourSlot)
    {
        long dayIndex = hourSlot / 24L;
        int hour = (int)(hourSlot % 24L);
        return (dayIndex - 1L) * 86400L + hour * 3600L;
    }

    private void SubscribeToSpawner()
    {
        if (aircraftSpawner == null || subscribedToSpawner) return;
        aircraftSpawner.FlightScheduled += HandleFlightScheduled;
        aircraftSpawner.AircraftSpawned += HandleAircraftSpawned;
        subscribedToSpawner = true;
    }

    private void HandleFlightScheduled(long hourSlot)
    {
        if (!SightseeingAircraftSpawner.HasTicketDeskPlaced())
            return;

        if (passengerPrefab == null || passengerCarPrefab == null || trafficSpawner == null)
        {
            LogWarning("SightseeingPassengerManager: Assign passenger and passenger-car prefabs before the next scheduled flight.", this);
            return;
        }

        int capacity = 0;
        GameObject aircraftPrefab = aircraftSpawner != null ? aircraftSpawner.SightseeingAircraftPrefab : null;
        AircraftSSTurnaroundController prefabTurnaround =
            aircraftPrefab != null ? aircraftPrefab.GetComponent<AircraftSSTurnaroundController>() : null;
        if (prefabTurnaround != null) capacity = prefabTurnaround.PassengerCapacity;

        int demand = sightseeingEconomy != null
            ? sightseeingEconomy.CalculatePassengerDemand(capacity)
            : 0;

        // Discard cohorts for hourly slots the aircraft scheduler skipped,
        // while retaining the original slot of an aircraft still awaiting parking.
        long pendingAircraftSlot = aircraftSpawner != null
            ? aircraftSpawner.PendingFlightHourSlot
            : 0L;
        for (int i = activeGroups.Count - 1; i >= 0; i--)
        {
            FlightGroup stale = activeGroups[i];
            if (stale.turnaround == null && !stale.cancelled &&
                stale.scheduledHourSlot < hourSlot &&
                stale.scheduledHourSlot != pendingAircraftSlot)
                CancelGroup(stale);
        }

        for (int i = 0; i < activeGroups.Count; i++)
            if (!activeGroups[i].cancelled && activeGroups[i].scheduledHourSlot == hourSlot)
                return;

        FlightGroup group = new FlightGroup
        {
            scheduledHourSlot = hourSlot,
            scheduledBoardingGameSecond = GetAbsoluteGameSecondForHourSlot(hourSlot)
        };
        activeGroups.Add(group);

        debugStatus = "Preparing " + demand + " sightseeing passenger(s) for the next hourly flight.";
        if (demand > 0)
        {
            group.carSpawnCount = demand;
            SpawnNextPassengerCar(group);
        }
    }

    private void SpawnNextPassengerCar(FlightGroup group)
    {
        if (!isActiveAndEnabled || group == null || group.cancelled ||
            !activeGroups.Contains(group) || trafficSpawner == null ||
            passengerCarPrefab == null || group.nextCarSpawnIndex >= group.carSpawnCount)
            return;

        int passengerIndex = group.nextCarSpawnIndex + 1;
        TrafficTestVehicle car = trafficSpawner.SpawnExternallyControlledVehicle(passengerCarPrefab);
        if (car == null)
        {
            LogWarning("SightseeingPassengerManager: Could not spawn passenger car " +
                passengerIndex + "/" + group.carSpawnCount + ".", this);
        }
        else
        {
            PassengerRecord record = new PassengerRecord { vehicle = car };
            group.passengers.Add(record);
            car.Parked += HandleCarParked;
            car.Exited += HandleCarExited;
        }

        group.nextCarSpawnIndex++;
        if (group.nextCarSpawnIndex < group.carSpawnCount &&
            carSpawnIntervalGameSeconds > 0f)
        {
            ScheduleNextPassengerCar(group, carSpawnIntervalGameSeconds);
        }
        else if (group.nextCarSpawnIndex < group.carSpawnCount)
        {
            SpawnNextPassengerCar(group);
        }
    }

    private void ScheduleNextPassengerCar(FlightGroup group, double delaySeconds)
    {
        if (gameTimeManager == null || group == null || group.cancelled ||
            group.carSpawnEventId != 0)
            return;

        group.carSpawnEventId = gameTimeManager.EventScheduler.ScheduleAfter(
            System.Math.Max(0.0, delaySeconds),
            () =>
            {
                group.carSpawnEventId = 0;
                SpawnNextPassengerCar(group);
            });
    }

    private void ResumePendingCarSpawns()
    {
        for (int i = 0; i < activeGroups.Count; i++)
        {
            FlightGroup group = activeGroups[i];
            if (group != null && !group.cancelled &&
                group.nextCarSpawnIndex < group.carSpawnCount &&
                group.carSpawnEventId == 0)
                ScheduleNextPassengerCar(group, carSpawnIntervalGameSeconds);
        }
    }

    private void CancelPendingCarSpawnEvents()
    {
        if (gameTimeManager == null)
            return;

        for (int i = 0; i < activeGroups.Count; i++)
        {
            FlightGroup group = activeGroups[i];
            if (group == null || group.carSpawnEventId == 0)
                continue;

            gameTimeManager.EventScheduler.Cancel(group.carSpawnEventId);
            group.carSpawnEventId = 0;
        }
    }

    private Transform GetWaitingAreaAnchor()
    {
        if (publicTerminalWaitingArea != null)
            return publicTerminalWaitingArea;

        IReadOnlyList<PlacedBuilding> buildings = PlacedBuilding.ActiveBuildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            PlacedBuilding building = buildings[i];
            if (building != null && building.Definition != null &&
                building.Definition.id == DeskDefinitionId)
                return building.transform;
        }

        return transform;
    }

    private void HandleAircraftSpawned(AircraftFlightIdentity identity, AircraftSSTurnaroundController turnaround)
    {
        long flightSlot = identity != null
            ? identity.ScheduledDayIndex * 24L + identity.PlannedStartMinute / 60
            : 0L;
        FlightGroup group = null;
        for (int i = 0; i < activeGroups.Count; i++)
        {
            if (!activeGroups[i].cancelled && activeGroups[i].scheduledHourSlot == flightSlot)
            {
                group = activeGroups[i];
                break;
            }
        }
        if (group == null)
        {
            group = new FlightGroup
            {
                scheduledHourSlot = flightSlot,
                scheduledBoardingGameSecond = GetAbsoluteGameSecondForHourSlot(flightSlot)
            };
            activeGroups.Add(group);
        }
        group.identity = identity;
        group.turnaround = turnaround;

        if (turnaround == null)
        {
            LogError("SightseeingPassengerManager: Sightseeing aircraft has no AircraftSSTurnaroundController.", this);
            return;
        }

        turnaround.BoardingStarted += HandleBoardingStarted;
        turnaround.ReturnedToParking += HandleAircraftReturned;
        debugStatus = "Passenger group linked to sightseeing flight " + (identity != null ? identity.FlightNumber : "unknown") + ".";
    }

    private void HandleCarParked(TrafficTestVehicle car)
    {
        PassengerRecord record = FindRecord(car, out FlightGroup group);
        if (record == null || car == null) return;
        if (group != null && (group.cancelled || group.boardingStarted))
        {
            record.completed = true;
            car.RequestDeparture();
            return;
        }

        Vector3 desired = car.ParkingPosition + car.transform.right * carExitOffset;
        if (passengerPrefab == null || pedestrianPathfinder == null ||
            !pedestrianPathfinder.TryFindNearestWalkablePosition(desired, 8, out Vector3 spawnPosition))
        {
            LogWarning("SightseeingPassengerManager: Could not find a pedestrian spawn cell beside " + car.name + ".", car);
            car.RequestDeparture();
            record.completed = true;
            return;
        }

        GameObject passengerObject = Instantiate(passengerPrefab, spawnPosition, car.transform.rotation);

        // The prefab pivot is not guaranteed to sit at its feet. Align the
        // combined renderer bounds with the walkable ground surface so the
        // passenger is not spawned half below the floor.
        Renderer[] passengerRenderers = passengerObject.GetComponentsInChildren<Renderer>(true);
        if (passengerRenderers.Length > 0)
        {
            Bounds passengerBounds = passengerRenderers[0].bounds;
            for (int i = 1; i < passengerRenderers.Length; i++)
                passengerBounds.Encapsulate(passengerRenderers[i].bounds);

            float groundOffset = spawnPosition.y - passengerBounds.min.y;
            passengerObject.transform.position += Vector3.up * groundOffset;
        }

        UtilityNeeds needs = passengerObject.GetComponent<UtilityNeeds>();
        if (needs == null) needs = passengerObject.AddComponent<UtilityNeeds>();
        needs.ResetForNewVisit();

        SightseeingPassengerAgent agent = passengerObject.GetComponent<SightseeingPassengerAgent>();
        if (agent == null) agent = passengerObject.AddComponent<SightseeingPassengerAgent>();
        record.agent = agent;

        agent.Initialize(this, car, GetWaitingAreaAnchor(), waitingAreaRadiusCells);
        agent.SetDepartureUrgency(CalculateDepartureUrgency(
            group != null ? group.scheduledBoardingGameSecond - GetAbsoluteGameSecond() : 0L));

        TryBeginCheckIn(agent);
    }

    public bool TryBeginCheckIn(SightseeingPassengerAgent agent)
    {
        if (agent == null || !agent.isActiveAndEnabled || agent.HasTicket ||
            agent.IsOnBoard || agent.IsWalkingToCar ||
            agent.State == SightseeingPassengerAgent.PassengerState.Completed)
            return false;

        InteractionProvider provider = FindBestDesk(agent, out InteractionDefinition definition);
        if (provider == null || definition == null) return false;

        return agent.BeginCheckIn(provider, definition,
            sightseeingEconomy != null ? sightseeingEconomy.TicketPrice : 0);
    }

    private InteractionProvider FindBestDesk(SightseeingPassengerAgent agent, out InteractionDefinition selected)
    {
        selected = null;
        InteractionProvider best = null;
        float bestTime = float.PositiveInfinity;
        IReadOnlyList<PlacedBuilding> buildings = PlacedBuilding.ActiveBuildings;

        for (int i = 0; i < buildings.Count; i++)
        {
            PlacedBuilding building = buildings[i];
            if (building == null || building.Definition == null || building.Definition.id != DeskDefinitionId)
                continue;

            InteractionProvider provider = building.GetComponent<InteractionProvider>();
            if (provider == null) provider = building.GetComponentInChildren<InteractionProvider>(true);
            if (provider == null || !provider.InteractionsEnabled) continue;

            PedestrianAccessProfile profile = agent.GetComponent<PedestrianAccessProfile>();
            if (roomPurposeController != null &&
                !roomPurposeController.IsPositionAccessibleToPedestrian(provider.InteractionPosition, profile))
                continue;

            InteractionDefinition definition = FindCheckInDefinition(provider);
            if (definition == null || !provider.CanOffer(definition)) continue;

            float time = agent.Movement.EstimateTravelTime(provider);
            if (float.IsInfinity(time) || float.IsNaN(time) || time >= bestTime) continue;

            bestTime = time;
            best = provider;
            selected = definition;
        }

        return best;
    }

    private InteractionDefinition FindCheckInDefinition(InteractionProvider provider)
    {
        if (checkInInteraction != null && provider.Interactions.Contains(checkInInteraction))
            return checkInInteraction;

        for (int i = 0; i < provider.Interactions.Count; i++)
        {
            InteractionDefinition definition = provider.Interactions[i];
            if (definition == null) continue;
            string id = definition.InteractionId ?? "";
            string label = definition.DisplayName ?? "";
            if (id.IndexOf("ticket", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("checkin", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("ticket", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("check-in", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return definition;
        }
        return null;
    }

    public void HandlePassengerCheckedIn(SightseeingPassengerAgent agent, int price)
    {
        PassengerRecord record = FindRecord(agent, out _);
        if (record == null || record.checkedIn) return;
        record.checkedIn = true;
        if (sightseeingEconomy != null) sightseeingEconomy.RegisterTicketSale(price);
        debugStatus = "Passenger purchased a sightseeing ticket (" + price + ") and checked in.";
    }

    public void HandlePassengerRefund(SightseeingPassengerAgent agent, int ticketPrice)
    {
        PassengerRecord record = FindRecord(agent, out _);
        if (record == null || !record.checkedIn || record.onBoard)
            return;

        record.checkedIn = false;
        if (sightseeingEconomy != null)
            sightseeingEconomy.RegisterTicketRefund(ticketPrice);
        debugStatus = "Sightseeing ticket refunded because the passenger could not board.";
    }

    private void HandleBoardingStarted(AircraftSSTurnaroundController turnaround)
    {
        FlightGroup group = FindGroup(turnaround);
        if (group == null || group.boardingStarted) return;

        group.boardingStarted = true;
        group.parkingSpot = turnaround.ReservedSpot;

        Transform marker = group.parkingSpot != null
            ? group.parkingSpot.transform.Find("BoardingPoint")
            : null;

        int eligible = 0;
        for (int i = 0; i < group.passengers.Count; i++)
        {
            PassengerRecord record = group.passengers[i];
            if (record != null && record.checkedIn && record.agent != null &&
                record.agent.isActiveAndEnabled)
                eligible++;
            else if (record != null && record.agent != null)
                record.agent.BeginReturnToCar();
        }

        group.waitingToBoard.Clear();
        group.boardingPassengerActive = false;
        turnaround.ConfigurePassengerManifest(eligible);

        if (marker == null)
        {
            LogError("SightseeingPassengerManager: Add a child named BoardingPoint to the reserved sightseeing parking spot.", this);
            turnaround.ConfigurePassengerManifest(0);
            for (int i = 0; i < group.passengers.Count; i++)
                if (group.passengers[i]?.agent != null) group.passengers[i].agent.BeginReturnToCar();
            return;
        }

        // Everyone walks to a separate position around the BoardingPoint at once.
        // Each passenger joins the boarding queue on arrival, so the first to
        // reach the gathering area can board without waiting for the others.
        int slot = 0;
        float gatheringRadius = Mathf.Max(1.8f, eligible * 0.32f);
        for (int i = 0; i < group.passengers.Count; i++)
        {
            PassengerRecord record = group.passengers[i];
            if (record == null || !record.checkedIn || record.agent == null ||
                !record.agent.isActiveAndEnabled)
                continue;

            float angle = eligible > 0 ? (Mathf.PI * 2f * slot / eligible) : 0f;
            Vector3 offset = marker.right * Mathf.Cos(angle) + marker.forward * Mathf.Sin(angle);
            offset.y = 0f;
            Vector3 gatheringPosition = marker.position + offset.normalized * gatheringRadius;
            slot++;

            if (!record.agent.BeginGatheringForBoarding(gatheringPosition, group.parkingSpot))
            {
                ResolveGatheringFailure(group, record);
            }
        }

    }

    private void ResolveGatheringFailure(FlightGroup group, PassengerRecord record)
    {
        if (group == null || record == null || record.gatheringResolved)
            return;

        record.gatheringResolved = true;
        if (group.turnaround != null)
            group.turnaround.NotifyPassengerBoardingFailed();
        record.checkedIn = false;
        if (record.agent != null)
            record.agent.BeginReturnToCar();
    }

    private void StartNextBoarding(FlightGroup group, Transform marker)
    {
        if (group == null || group.turnaround == null || marker == null ||
            group.boardingPassengerActive)
            return;

        while (group.waitingToBoard.Count > 0)
        {
            PassengerRecord record = group.waitingToBoard.Dequeue();
            if (record == null || !record.checkedIn || record.agent == null ||
                !record.agent.isActiveAndEnabled)
                continue;

            if (record.agent.BeginBoarding(marker, group.parkingSpot))
            {
                group.boardingPassengerActive = true;
                return;
            }

            group.turnaround.NotifyPassengerBoardingFailed();
            record.checkedIn = false;
            record.agent.BeginReturnToCar();
        }
    }

    public void HandlePassengerReachedGatheringSpot(SightseeingPassengerAgent agent)
    {
        PassengerRecord record = FindRecord(agent, out FlightGroup group);
        if (record == null || group == null || !group.boardingStarted ||
            record.gatheringResolved || !agent.IsGatheringForBoarding)
            return;

        record.gatheringResolved = true;
        group.waitingToBoard.Enqueue(record);

        Transform marker = group.parkingSpot != null
            ? group.parkingSpot.transform.Find("BoardingPoint")
            : null;
        StartNextBoarding(group, marker);
    }

    public void HandlePassengerReachedAircraft(SightseeingPassengerAgent agent)
    {
        PassengerRecord record = FindRecord(agent, out FlightGroup group);
        if (record == null || group == null || group.turnaround == null ||
            record.onBoard || !group.boardingStarted) return;

        record.onBoard = true;
        group.boardingPassengerActive = false;
        agent.MarkOnBoard();
        group.turnaround.NotifyPassengerBoarded();

        Transform marker = group.parkingSpot != null
            ? group.parkingSpot.transform.Find("BoardingPoint")
            : null;
        StartNextBoarding(group, marker);
    }

    public void HandlePassengerRouteFailed(SightseeingPassengerAgent agent)
    {
        PassengerRecord record = FindRecord(agent, out FlightGroup group);
        if (record == null || agent == null) return;

        if (agent.IsGatheringForBoarding && group != null && group.turnaround != null)
        {
            ResolveGatheringFailure(group, record);
        }
        else if (agent.IsWalkingToAircraft && group != null && group.turnaround != null)
        {
            group.boardingPassengerActive = false;
            group.turnaround.NotifyPassengerBoardingFailed();
            record.checkedIn = false;
            agent.BeginReturnToCar();
            Transform marker = group.parkingSpot != null ? group.parkingSpot.transform.Find("BoardingPoint") : null;
            StartNextBoarding(group, marker);
        }
        else if (agent.IsWalkingToCheckIn)
        {
            agent.CancelCheckIn();
        }
    }

    private void HandleAircraftReturned(AircraftSSTurnaroundController turnaround)
    {
        FlightGroup group = FindGroup(turnaround);
        if (group == null || group.returned) return;

        group.returned = true;
        for (int i = 0; i < group.passengers.Count; i++)
        {
            PassengerRecord record = group.passengers[i];
            if (record == null || !record.onBoard || record.agent == null) continue;
            record.onBoard = false;
            record.agent.DisembarkToCar();
        }

        debugStatus = "Sightseeing flight returned; passengers are disembarking.";
        TryRemoveFinishedGroup(group);
        StartCoroutine(CompleteReturnAfterDisembarkation(group, turnaround));
    }

    private IEnumerator CompleteReturnAfterDisembarkation(
        FlightGroup group,
        AircraftSSTurnaroundController turnaround)
    {
        long startSecond = GetAbsoluteGameSecond();
        const long maximumWaitSeconds = 20L;
        const float clearanceDistance = 3f;

        while (turnaround != null &&
               GetAbsoluteGameSecond() - startSecond < maximumWaitSeconds)
        {
            bool allPassengersClear = true;
            Vector3 aircraftPosition = group.parkingSpot != null
                ? group.parkingSpot.transform.position
                : turnaround.transform.position;

            for (int i = 0; i < group.passengers.Count; i++)
            {
                PassengerRecord record = group.passengers[i];
                if (record == null || record.completed || record.agent == null)
                    continue;

                if (!record.agent.IsWalkingToCar ||
                    Vector3.Distance(record.agent.transform.position, aircraftPosition) < clearanceDistance)
                {
                    allPassengersClear = false;
                    break;
                }
            }

            if (allPassengersClear)
                break;

            yield return null;
        }

        if (turnaround != null)
            turnaround.CompleteReturnAndPrepareForReuse();
    }

    private void HandleCarExited(TrafficTestVehicle car)
    {
        PassengerRecord record = FindRecord(car, out FlightGroup group);
        if (record == null) return;

        car.Parked -= HandleCarParked;
        car.Exited -= HandleCarExited;
        record.completed = true;
        record.vehicle = null;
        record.agent = null;
        TryRemoveFinishedGroup(group);
    }

    private void CancelUnspawnedGroupsWithoutDesk()
    {
        for (int i = activeGroups.Count - 1; i >= 0; i--)
        {
            FlightGroup group = activeGroups[i];
            if (group.turnaround == null && !group.cancelled)
                CancelGroup(group);
        }
    }

    private void CancelGroup(FlightGroup group)
    {
        if (group == null || group.cancelled) return;
        group.cancelled = true;
        if (group.carSpawnEventId != 0 && gameTimeManager != null)
        {
            gameTimeManager.EventScheduler.Cancel(group.carSpawnEventId);
            group.carSpawnEventId = 0;
        }

        for (int i = 0; i < group.passengers.Count; i++)
        {
            PassengerRecord record = group.passengers[i];
            if (record == null) continue;

            if (record.agent != null)
                record.agent.BeginReturnToCar();
            else if (record.vehicle != null && record.vehicle.IsParked)
                record.vehicle.RequestDeparture();
            else if (record.vehicle == null)
                record.completed = true;
        }

        TryRemoveFinishedGroup(group);
    }

    private void RetryCheckIn()
    {
        for (int i = 0; i < activeGroups.Count; i++)
        {
            FlightGroup group = activeGroups[i];
            if (group.boardingStarted || group.cancelled) continue;
            for (int j = 0; j < group.passengers.Count; j++)
            {
                PassengerRecord record = group.passengers[j];
                if (record == null || record.completed || record.checkedIn ||
                    record.agent == null || !record.agent.isActiveAndEnabled) continue;
                TryBeginCheckIn(record.agent);
            }
        }
    }

    private PassengerRecord FindRecord(TrafficTestVehicle car, out FlightGroup owner)
    {
        for (int i = 0; i < activeGroups.Count; i++)
        for (int j = 0; j < activeGroups[i].passengers.Count; j++)
        {
            PassengerRecord record = activeGroups[i].passengers[j];
            if (record != null && record.vehicle == car) { owner = activeGroups[i]; return record; }
        }
        owner = null;
        return null;
    }

    private PassengerRecord FindRecord(SightseeingPassengerAgent agent, out FlightGroup owner)
    {
        for (int i = 0; i < activeGroups.Count; i++)
        for (int j = 0; j < activeGroups[i].passengers.Count; j++)
        {
            PassengerRecord record = activeGroups[i].passengers[j];
            if (record != null && record.agent == agent) { owner = activeGroups[i]; return record; }
        }
        owner = null;
        return null;
    }

    private FlightGroup FindGroup(AircraftSSTurnaroundController turnaround)
    {
        for (int i = 0; i < activeGroups.Count; i++)
            if (activeGroups[i].turnaround == turnaround) return activeGroups[i];
        return null;
    }

    private void TryRemoveFinishedGroup(FlightGroup group)
    {
        // Keep the manifest attached until the aircraft has returned. This
        // prevents a zero-passenger cohort from falling back to random demand.
        if (group == null || (!group.returned && !group.cancelled)) return;
        for (int i = 0; i < group.passengers.Count; i++)
            if (group.passengers[i] != null && !group.passengers[i].completed) return;

        if (group.turnaround != null)
        {
            group.turnaround.BoardingStarted -= HandleBoardingStarted;
            group.turnaround.ReturnedToParking -= HandleAircraftReturned;
        }
        activeGroups.Remove(group);
    }

    private void OnDestroy()
    {
        OnDisable();
        for (int i = 0; i < activeGroups.Count; i++)
        {
            FlightGroup group = activeGroups[i];
            if (group.turnaround != null)
            {
                group.turnaround.BoardingStarted -= HandleBoardingStarted;
                group.turnaround.ReturnedToParking -= HandleAircraftReturned;
            }
            for (int j = 0; j < group.passengers.Count; j++)
            {
                TrafficTestVehicle car = group.passengers[j]?.vehicle;
                if (car != null)
                {
                    car.Parked -= HandleCarParked;
                    car.Exited -= HandleCarExited;
                }
            }
        }
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
