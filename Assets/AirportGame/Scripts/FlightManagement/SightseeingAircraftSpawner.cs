
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Airport-wide sightseeing flight scheduler and aircraft spawner.
///
/// Schedules one flight at each new in-game hour. Flights are only
/// scheduled and spawned while at least one sightseeing ticket desk
/// is placed. If no parking spot is available, one pending flight
/// retries until it can spawn. Missed hourly slots never accumulate.
/// </summary>
[DisallowMultipleComponent]
public class SightseeingAircraftSpawner : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    private enum SpawnAttemptResult
    {
        Spawned,
        TicketDeskUnavailable,
        WaitingForParking,
        RetryableFailure,
        FatalFailure
    }

    private const string TicketDeskDefinitionId =
        "airport.sightseeing.ticketdesk";

    

    public event Action<long> FlightScheduled;
    public event Action<AircraftFlightIdentity, AircraftSSTurnaroundController> AircraftSpawned;

    private static SightseeingAircraftSpawner activeSpawner;
    private static readonly HashSet<PlacedBuilding> ticketDeskBuildings = new HashSet<PlacedBuilding>();

    [Header("References")]
    [SerializeField] private GameObject sightseeingAircraftPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Hourly Schedule")]
    [SerializeField] private bool hourlyScheduleEnabled = true;

    [Tooltip("Game-time seconds between retries when parking is unavailable.")]
    [Min(0.1f)]
    [SerializeField] private float parkingRetryIntervalSeconds = 2f;

    [Header("Testing")]
    [Tooltip("Spawn one test aircraft when this component starts.")]
    [SerializeField] private bool spawnOnStart;

    [SerializeField] private bool logSpawnDetails = true;

    [Header("Flight Number")]
    [Min(1)]
    [SerializeField] private int nextFlightNumber = 1;

    [Header("Runtime Status")]
    [SerializeField] private bool flightPending;
    [SerializeField] private long pendingFlightHourSlot;
    [SerializeField] private long lastObservedHourSlot;
    [SerializeField] private float retryTimer;
    [SerializeField] private string scheduleStatus = "Initializing";

    private bool waitingMessageLogged;
    private bool subscribedToGameTime;
    private long retryEventId;
    private AircraftSSTurnaroundController reusableAircraft;

    public GameObject SightseeingAircraftPrefab => sightseeingAircraftPrefab;
    public bool FlightPending => flightPending;
    public long PendingFlightHourSlot => pendingFlightHourSlot;
    public bool HourlyScheduleEnabled => hourlyScheduleEnabled;
    public string ScheduleStatus => scheduleStatus;

    /// <summary>
    /// True if at least one placed building uses the sightseeing ticket
    /// desk definition. Uses the runtime PlacedBuilding component, so
    /// no additional prefab component is required.
    /// </summary>
    public static bool HasTicketDeskPlaced()
    {
        return ticketDeskBuildings.Count > 0;
    }

    private void OnEnable()
    {
        PlacedBuilding.BuildingChanged += HandleBuildingChanged;
        RebuildTicketDeskCache();
        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();
        SubscribeToGameTime();
    }

    private void OnDisable()
    {
        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
        if (gameTimeManager != null && subscribedToGameTime)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
        subscribedToGameTime = false;
        CancelScheduledRetry();
    }

    private static void RebuildTicketDeskCache()
    {
        ticketDeskBuildings.Clear();
        PlacedBuilding[] buildings = FindObjectsByType<PlacedBuilding>();
        foreach (PlacedBuilding building in buildings)
            AddTicketDeskIfRelevant(building);
    }

    private static void HandleBuildingChanged(PlacedBuilding building)
    {
        if (building == null || !building.isActiveAndEnabled ||
            building.Definition == null ||
            building.Definition.id != TicketDeskDefinitionId)
        {
            if (building != null)
                ticketDeskBuildings.Remove(building);
            return;
        }

        ticketDeskBuildings.Add(building);
    }

    private static void AddTicketDeskIfRelevant(PlacedBuilding building)
    {
        if (building != null && building.isActiveAndEnabled &&
            building.Definition != null &&
            building.Definition.id == TicketDeskDefinitionId)
            ticketDeskBuildings.Add(building);
    }

    private void SubscribeToGameTime()
    {
        if (gameTimeManager == null || subscribedToGameTime)
            return;

        gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
        subscribedToGameTime = true;
    }

    private void Awake()
    {
        // Keep one airport-wide scheduler.
        if (activeSpawner != null && activeSpawner != this)
        {
            LogError(
                "Only one SightseeingAircraftSpawner should exist in the scene. " +
                "The duplicate scheduler has been disabled.",
                this);

            enabled = false;
            return;
        }

        activeSpawner = this;
    }

    private void Start()
    {
        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();

        if (sightseeingAircraftPrefab == null ||
            spawnPoint == null ||
            gameTimeManager == null)
        {
            LogError(
                "SightseeingAircraftSpawner: Assign the aircraft prefab, " +
                "spawn point, and GameTimeManager.",
                this);

            enabled = false;
            return;
        }

        // Establish the current hour as the baseline.
        // The first scheduled flight becomes due at the next hour boundary.
        lastObservedHourSlot = GetCurrentHourSlot();
        SubscribeToGameTime();

        scheduleStatus = HasTicketDeskPlaced()
            ? (hourlyScheduleEnabled
                ? "Waiting for the next hourly departure"
                : "Hourly schedule disabled")
            : "Waiting for a sightseeing ticket desk";

        if (logSpawnDetails)
        {
            Log(
                $"SIGHTSEEING SCHEDULE READY | Day: " +
                $"{gameTimeManager.CurrentDay} | " +
                $"Time: {gameTimeManager.FormattedTime} | " +
                $"Ticket desk present: {HasTicketDeskPlaced()}",
                this);
        }

        // Optional manual testing convenience.
        // The spawn method also checks for a ticket desk.
        if (spawnOnStart)
            SpawnSightseeingAircraft();
    }

    private void Update()
    {
        // Keep Inspector-driven schedule toggles responsive without polling
        // the retry timer itself.
        if (hourlyScheduleEnabled && flightPending && retryEventId == 0 &&
            gameTimeManager != null && gameTimeManager.CurrentSpeedMultiplier > 0)
            SchedulePendingFlightRetry(0.0);
    }

    private void CancelScheduledRetry()
    {
        if (retryEventId != 0 && gameTimeManager != null)
            gameTimeManager.EventScheduler.Cancel(retryEventId);

        retryEventId = 0;
        retryTimer = 0f;
    }

    private void SchedulePendingFlightRetry(double delaySeconds)
    {
        if (!isActiveAndEnabled || gameTimeManager == null ||
            !hourlyScheduleEnabled || !flightPending || retryEventId != 0)
            return;

        retryTimer = (float)delaySeconds;
        retryEventId = gameTimeManager.EventScheduler.ScheduleAfter(
            delaySeconds,
            () =>
            {
                retryEventId = 0;
                retryTimer = 0f;

                if (!isActiveAndEnabled || !hourlyScheduleEnabled ||
                    !flightPending || gameTimeManager == null ||
                    gameTimeManager.CurrentSpeedMultiplier <= 0)
                    return;

                ProcessPendingFlight();
            });
    }

    private void HandleGameTimeAdvanced(long previousSecond, long currentSecond)
    {
        if (!HasTicketDeskPlaced())
        {
            flightPending = false;
            pendingFlightHourSlot = 0L;
            CancelScheduledRetry();
            waitingMessageLogged = false;
            lastObservedHourSlot = GetCurrentHourSlot();
            scheduleStatus = "Waiting for a sightseeing ticket desk";
            return;
        }

        if (hourlyScheduleEnabled && gameTimeManager != null &&
            gameTimeManager.CurrentSpeedMultiplier > 0)
            CheckForNewHourlySlot();
    }

    private void CheckForNewHourlySlot()
    {
        long currentSlot = GetCurrentHourSlot();

        if (currentSlot <= lastObservedHourSlot)
            return;

        bool wasAlreadyPending = flightPending;
        lastObservedHourSlot = currentSlot;

        if (!wasAlreadyPending)
        {
            flightPending = true;
            pendingFlightHourSlot = currentSlot;
            CancelScheduledRetry();
            waitingMessageLogged = false;
            scheduleStatus = "Hourly flight due";
        }

        // At this boundary, passengers for the following hourly slot are
        // spawned. The current slot's cohort was prepared one hour earlier.
        FlightScheduled?.Invoke(currentSlot + 1L);

        if (wasAlreadyPending)
        {
            scheduleStatus =
                "One flight is pending; additional hourly slots are skipped.";

            if (logSpawnDetails)
            {
                Log(
                    "SIGHTSEEING SCHEDULE | A new hour has started, " +
                    "but one flight is already pending. No additional " +
                    "flight was queued.",
                    this);
            }

            return;
        }

        if (logSpawnDetails)
        {
            Log(
                $"SIGHTSEEING FLIGHT DUE | Day: " +
                $"{gameTimeManager.CurrentDay} | " +
                $"Time: {gameTimeManager.FormattedTime}",
                this);
        }
    }

    private long GetCurrentHourSlot()
    {
        return gameTimeManager.CurrentDay * 24L +
               gameTimeManager.Hour;
    }

    private void ProcessPendingFlight()
    {
        SpawnAttemptResult result = TrySpawnSightseeingAircraft();

        switch (result)
        {
            case SpawnAttemptResult.Spawned:
                flightPending = false;
                pendingFlightHourSlot = 0L;
                CancelScheduledRetry();
                waitingMessageLogged = false;
                scheduleStatus = "Flight spawned; waiting for next hour";
                break;

            case SpawnAttemptResult.TicketDeskUnavailable:
                flightPending = false;
                CancelScheduledRetry();
                waitingMessageLogged = false;
                scheduleStatus = "Waiting for a sightseeing ticket desk";
                break;

            case SpawnAttemptResult.WaitingForParking:
            case SpawnAttemptResult.RetryableFailure:
                SchedulePendingFlightRetry(
                    Mathf.Max(0.1f, parkingRetryIntervalSeconds));
                break;

            case SpawnAttemptResult.FatalFailure:
                // Stop automatic attempts when configuration is invalid.
                hourlyScheduleEnabled = false;
                flightPending = false;
                CancelScheduledRetry();
                scheduleStatus =
                    "Schedule disabled because of a configuration error";
                break;
        }
    }

    /// <summary>
    /// Manual test spawn. Does not alter the hourly schedule.
    /// Still requires a placed ticket desk.
    /// </summary>
    [ContextMenu("Spawn Sightseeing Aircraft")]
    public void SpawnSightseeingAircraft()
    {
        if (!HasTicketDeskPlaced())
        {
            LogWarning(
                "Manual sightseeing spawn skipped: no sightseeing ticket desk is placed.",
                this);
            return;
        }

        if (gameTimeManager == null ||
            sightseeingAircraftPrefab == null ||
            spawnPoint == null)
        {
            LogError(
                "SightseeingAircraftSpawner: Check the Inspector references.",
                this);
            return;
        }

        SpawnAttemptResult result = TrySpawnSightseeingAircraft();

        if (result == SpawnAttemptResult.TicketDeskUnavailable)
        {
            LogWarning(
                "Manual sightseeing spawn skipped: no sightseeing ticket desk is placed.",
                this);
        }
        else if (result == SpawnAttemptResult.WaitingForParking)
        {
            LogWarning(
                "Manual sightseeing spawn skipped because no parking " +
                "spot is available. The hourly schedule was not changed.",
                this);
        }
        else if (result == SpawnAttemptResult.FatalFailure)
        {
            LogError(
                "Manual sightseeing spawn failed because the aircraft " +
                "prefab or its configuration is invalid.",
                this);
        }
    }

    private SpawnAttemptResult TrySpawnSightseeingAircraft()
    {
        // Check this immediately before spawning, including manual spawns
        // and retries of previously pending flights.
        if (!HasTicketDeskPlaced())
            return SpawnAttemptResult.TicketDeskUnavailable;

        if (sightseeingAircraftPrefab == null ||
            spawnPoint == null ||
            gameTimeManager == null)
        {
            LogError(
                "SightseeingAircraftSpawner: Required references are missing.",
                this);

            return SpawnAttemptResult.FatalFailure;
        }

        bool reusingAircraft = reusableAircraft != null;
        SightseeingAircraftParkingSpot spot = reusingAircraft
            ? reusableAircraft.ReservedSpot
            : FindAvailableParkingSpot();

        if (reusingAircraft && !reusableAircraft.PrepareForNextFlight())
            return SpawnAttemptResult.RetryableFailure;

        if (spot == null)
        {
            if (!waitingMessageLogged)
            {
                LogWarning(
                    "SIGHTSEEING FLIGHT WAITING | No available sightseeing " +
                    "parking spot. The flight remains pending.",
                    this);

                waitingMessageLogged = true;
            }

            scheduleStatus = "Waiting for an available parking spot";

            return SpawnAttemptResult.WaitingForParking;
        }

        GameObject aircraft = reusingAircraft
            ? reusableAircraft.gameObject
            : Instantiate(
                sightseeingAircraftPrefab,
                spawnPoint.position,
                spawnPoint.rotation);

        if (aircraft == null)
        {
            LogError(
                "SightseeingAircraftSpawner: Aircraft instantiation failed.",
                this);

            return SpawnAttemptResult.RetryableFailure;
        }

        AircraftAirportFees airportFees =
            aircraft.GetComponent<AircraftAirportFees>();
        if (airportFees != null)
            airportFees.SetFeeCategory(AirportFeeCategory.Sightseeing);

        // A reused aircraft keeps its original parking reservation.
        // New aircraft reserve a free sightseeing stand before operating.
        if (!reusingAircraft && !spot.TryReserve(aircraft))
        {
            LogWarning(
                "SightseeingAircraftSpawner: The selected parking spot " +
                "could not be reserved. The aircraft will be removed " +
                "and the flight will be retried.",
                spot);

            Destroy(aircraft);

            return SpawnAttemptResult.RetryableFailure;
        }

        AircraftSSTurnaroundController turnaround =
            aircraft.GetComponent<AircraftSSTurnaroundController>();

        if (turnaround == null)
        {
            LogError("Sightseeing aircraft prefab is missing AircraftSSTurnaroundController.", aircraft);
            if (!reusingAircraft) spot.Release(aircraft);
            Destroy(aircraft);
            return SpawnAttemptResult.FatalFailure;
        }

        if (!reusingAircraft)
            reusableAircraft = turnaround;

        AircraftFlightIdentity identity =
            aircraft.GetComponent<AircraftFlightIdentity>();

        if (identity == null)
        {
            LogError(
                "Sightseeing aircraft prefab is missing " +
                "AircraftFlightIdentity. Check the prefab.",
                aircraft);

            spot.Release(aircraft);
            Destroy(aircraft);

            return SpawnAttemptResult.FatalFailure;
        }

        string flightNumber = GetNextFlightNumber();

        long scheduledSlot = pendingFlightHourSlot > 0L
            ? pendingFlightHourSlot
            : GetCurrentHourSlot();
        long scheduledDay = scheduledSlot / 24L;
        int startMinute = (int)(scheduledSlot % 24L) * 60;

        identity.InitializeHobbyFlight(
            flightNumber,
            startMinute,
            scheduledDay);

        AircraftSpawned?.Invoke(identity, turnaround);

        if (logSpawnDetails)
        {
            Log(
                $"SIGHTSEEING AIRCRAFT {(reusingAircraft ? "REUSED" : "SPAWNED")} | " +
                $"Flight: {flightNumber} | " +
                $"Reserved Spot: {spot.name} | " +
                $"Day: {gameTimeManager.CurrentDay} | " +
                $"Time: {gameTimeManager.FormattedTime}",
                aircraft);
        }

        return SpawnAttemptResult.Spawned;
    }

    private SightseeingAircraftParkingSpot FindAvailableParkingSpot()
    {
        SightseeingAircraftParkingSpot[] spots =
            FindObjectsByType<SightseeingAircraftParkingSpot>();

        foreach (SightseeingAircraftParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            if (!spot.IsPlacedBuilding)
                continue;

            if (spot.IsOccupied)
                continue;

            return spot;
        }

        return null;
    }

    private string GetNextFlightNumber()
    {
        string candidate;

        do
        {
            candidate = $"SS{nextFlightNumber}";
            nextFlightNumber++;
        }
        while (IsFlightNumberInUse(candidate));

        return candidate;
    }

    private bool IsFlightNumberInUse(string candidate)
    {
        AircraftFlightIdentity[] identities =
            FindObjectsByType<AircraftFlightIdentity>();

        foreach (AircraftFlightIdentity identity in identities)
        {
            if (identity != null &&
                identity.IsInitialized &&
                identity.FlightNumber == candidate)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDestroy()
    {
        if (activeSpawner == this)
            activeSpawner = null;
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
