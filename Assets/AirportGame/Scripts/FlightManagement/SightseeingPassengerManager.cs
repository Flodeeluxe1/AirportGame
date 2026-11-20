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
    }

    private sealed class FlightGroup
    {
        public long scheduledHourSlot;
        public AircraftFlightIdentity identity;
        public AircraftSSTurnaroundController turnaround;
        public SightseeingAircraftParkingSpot parkingSpot;
        public readonly List<PassengerRecord> passengers = new List<PassengerRecord>();
        public int nextBoardingIndex;
        public bool boardingStarted;
        public bool returned;
        public bool cancelled;
    }

    private readonly List<FlightGroup> activeGroups = new List<FlightGroup>();
    private bool subscribedToSpawner;
    private bool initialSchedulePrepared;
    private float nextCheckInRetryTime;

    private void Awake() => ResolveReferences();

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToSpawner();
    }

    private void OnDisable()
    {
        if (aircraftSpawner != null && subscribedToSpawner)
        {
            aircraftSpawner.FlightScheduled -= HandleFlightScheduled;
            aircraftSpawner.AircraftSpawned -= HandleAircraftSpawned;
        }
        subscribedToSpawner = false;
    }

    private void Update()
    {
        ResolveReferences();
        SubscribeToSpawner();

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

        if (Time.time >= nextCheckInRetryTime)
        {
            nextCheckInRetryTime = Time.time + Mathf.Max(0.1f, checkInRetrySeconds);
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
            Debug.LogWarning("SightseeingPassengerManager: Assign passenger and passenger-car prefabs before the next scheduled flight.", this);
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

        FlightGroup group = new FlightGroup { scheduledHourSlot = hourSlot };
        activeGroups.Add(group);

        debugStatus = "Preparing " + demand + " sightseeing passenger(s) for the next hourly flight.";
        if (demand > 0) StartCoroutine(SpawnPassengerCars(group, demand));
    }

    private IEnumerator SpawnPassengerCars(FlightGroup group, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (group == null || !activeGroups.Contains(group)) yield break;

            TrafficTestVehicle car = trafficSpawner.SpawnExternallyControlledVehicle(passengerCarPrefab);
            if (car == null)
            {
                Debug.LogWarning("SightseeingPassengerManager: Could not spawn passenger car " + (i + 1) + "/" + count + ".", this);
            }
            else
            {
                PassengerRecord record = new PassengerRecord { vehicle = car };
                group.passengers.Add(record);
                car.Parked += HandleCarParked;
                car.Exited += HandleCarExited;
            }

            if (carSpawnIntervalGameSeconds > 0f && i < count - 1)
            {
                long target = GetAbsoluteGameSecond() + Mathf.Max(1, Mathf.CeilToInt(carSpawnIntervalGameSeconds));
                while (gameTimeManager != null && GetAbsoluteGameSecond() < target)
                    yield return null;
            }
        }
    }

    private long GetAbsoluteGameSecond()
    {
        return gameTimeManager != null
            ? (gameTimeManager.CurrentDay - 1) * 86400L + gameTimeManager.GetGameTimeSeconds()
            : 0L;
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
            group = new FlightGroup { scheduledHourSlot = flightSlot };
            activeGroups.Add(group);
        }
        group.identity = identity;
        group.turnaround = turnaround;

        if (turnaround == null)
        {
            Debug.LogError("SightseeingPassengerManager: Sightseeing aircraft has no AircraftSSTurnaroundController.", this);
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
            Debug.LogWarning("SightseeingPassengerManager: Could not find a pedestrian spawn cell beside " + car.name + ".", car);
            car.RequestDeparture();
            record.completed = true;
            return;
        }

        GameObject passengerObject = Instantiate(passengerPrefab, spawnPosition, car.transform.rotation);
        UtilityNeeds needs = passengerObject.GetComponent<UtilityNeeds>();
        if (needs == null) needs = passengerObject.AddComponent<UtilityNeeds>();
        needs.ResetForNewVisit();

        Collider body = passengerObject.GetComponent<Collider>();
        if (body == null) body = passengerObject.GetComponentInChildren<Collider>();
        if (body != null) passengerObject.transform.position += Vector3.up * body.bounds.extents.y;

        SightseeingPassengerAgent agent = passengerObject.GetComponent<SightseeingPassengerAgent>();
        if (agent == null) agent = passengerObject.AddComponent<SightseeingPassengerAgent>();
        record.agent = agent;

        agent.Initialize(this, car, GetWaitingAreaAnchor(), waitingAreaRadiusCells);

        // A car may finish parking after boarding has already started.
        // Such a late arrival must leave again rather than miss the flight.
        if (group != null && group.boardingStarted)
            agent.BeginReturnToCar();
        else
            TryBeginCheckIn(agent);
    }

    public bool TryBeginCheckIn(SightseeingPassengerAgent agent)
    {
        if (agent == null || !agent.isActiveAndEnabled || agent.HasTicket || agent.IsOnBoard)
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
        if (sightseeingEconomy != null) sightseeingEconomy.RegisterTicketSale();
        debugStatus = "Passenger purchased a sightseeing ticket (" + price + ") and checked in.";
    }

    private void HandleBoardingStarted(AircraftSSTurnaroundController turnaround)
    {
        FlightGroup group = FindGroup(turnaround);
        if (group == null || group.boardingStarted) return;

        group.boardingStarted = true;
        group.parkingSpot = turnaround.ReservedSpot;

        int eligible = 0;
        for (int i = 0; i < group.passengers.Count; i++)
        {
            PassengerRecord record = group.passengers[i];
            if (record != null && record.checkedIn && record.agent != null && record.agent.isActiveAndEnabled)
                eligible++;
            else if (record != null && record.agent != null)
                record.agent.BeginReturnToCar();
        }
        turnaround.ConfigurePassengerManifest(eligible);

        Transform marker = group.parkingSpot != null
            ? group.parkingSpot.transform.Find("BoardingPoint")
            : null;

        if (marker == null)
        {
            Debug.LogError("SightseeingPassengerManager: Add a child named BoardingPoint to the reserved sightseeing parking spot.", this);
            turnaround.ConfigurePassengerManifest(0);
            for (int i = 0; i < group.passengers.Count; i++)
                if (group.passengers[i]?.agent != null) group.passengers[i].agent.BeginReturnToCar();
            return;
        }

        group.nextBoardingIndex = 0;
        StartNextBoarding(group, marker);
    }

    private void StartNextBoarding(FlightGroup group, Transform marker)
    {
        if (group == null || group.turnaround == null || marker == null) return;

        while (group.nextBoardingIndex < group.passengers.Count)
        {
            PassengerRecord record = group.passengers[group.nextBoardingIndex++];
            if (record == null || !record.checkedIn || record.agent == null || !record.agent.isActiveAndEnabled)
                continue;

            if (record.agent.BeginBoarding(marker, group.parkingSpot)) return;

            group.turnaround.NotifyPassengerBoardingFailed();
            record.checkedIn = false;
            record.agent.BeginReturnToCar();
        }
    }

    public void HandlePassengerReachedAircraft(SightseeingPassengerAgent agent)
    {
        PassengerRecord record = FindRecord(agent, out FlightGroup group);
        if (record == null || group == null || group.turnaround == null ||
            record.onBoard || !group.boardingStarted) return;

        record.onBoard = true;
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

        if (agent.IsWalkingToAircraft && group != null && group.turnaround != null)
        {
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
        turnaround.CompleteReturnAndDespawn();
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
        for (int i = 0; i < activeGroups.Count; i++)
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
            if (group.boardingStarted) continue;
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
}
