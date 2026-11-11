
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

    private static SightseeingAircraftSpawner activeSpawner;
    private static readonly HashSet<PlacedBuilding> ticketDeskBuildings = new HashSet<PlacedBuilding>();

    [Header("References")]
    [SerializeField] private GameObject sightseeingAircraftPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Hourly Schedule")]
    [SerializeField] private bool hourlyScheduleEnabled = true;

    [Tooltip("Real-world seconds between retries when parking is unavailable.")]
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
    [SerializeField] private long lastObservedHourSlot;
    [SerializeField] private float retryTimer;
    [SerializeField] private string scheduleStatus = "Initializing";

    private bool waitingMessageLogged;
    private bool subscribedToGameTime;

    public GameObject SightseeingAircraftPrefab => sightseeingAircraftPrefab;
    public bool FlightPending => flightPending;
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
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();
        SubscribeToGameTime();
    }

    private void OnDisable()
    {
        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
        if (gameTimeManager != null && subscribedToGameTime)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
        subscribedToGameTime = false;
    }

    private static void RebuildTicketDeskCache()
    {
        ticketDeskBuildings.Clear();
        PlacedBuilding[] buildings = FindObjectsByType<PlacedBuilding>(FindObjectsSortMode.None);
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
            Debug.LogError(
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
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (sightseeingAircraftPrefab == null ||
            spawnPoint == null ||
            gameTimeManager == null)
        {
            Debug.LogError(
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
            Debug.Log(
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
        if (gameTimeManager == null)
            return;

        if (!hourlyScheduleEnabled)
            return;

        // Do not schedule or retry departures while game time is paused.
        if (gameTimeManager.CurrentSpeedMultiplier <= 0)
            return;

        if (!flightPending)
            return;

        if (retryTimer > 0f)
        {
            retryTimer -= Time.unscaledDeltaTime;

            if (retryTimer > 0f)
                return;
        }

        ProcessPendingFlight();
    }

    private void HandleGameTimeAdvanced(long previousSecond, long currentSecond)
    {
        if (!HasTicketDeskPlaced())
        {
            flightPending = false;
            retryTimer = 0f;
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

        // Record the newest slot even if several hours were skipped.
        // Never queue one flight per missed hour.
        lastObservedHourSlot = currentSlot;

        if (flightPending)
        {
            scheduleStatus =
                "One flight is pending; additional hourly slots are skipped.";

            if (logSpawnDetails)
            {
                Debug.Log(
                    "SIGHTSEEING SCHEDULE | A new hour has started, " +
                    "but one flight is already pending. No additional " +
                    "flight was queued.",
                    this);
            }

            return;
        }

        flightPending = true;
        retryTimer = 0f;
        waitingMessageLogged = false;

        scheduleStatus = "Hourly flight due";

        if (logSpawnDetails)
        {
            Debug.Log(
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
                retryTimer = 0f;
                waitingMessageLogged = false;
                scheduleStatus = "Flight spawned; waiting for next hour";
                break;

            case SpawnAttemptResult.TicketDeskUnavailable:
                flightPending = false;
                retryTimer = 0f;
                waitingMessageLogged = false;
                scheduleStatus = "Waiting for a sightseeing ticket desk";
                break;

            case SpawnAttemptResult.WaitingForParking:
            case SpawnAttemptResult.RetryableFailure:
                retryTimer = Mathf.Max(
                    0.1f,
                    parkingRetryIntervalSeconds);
                break;

            case SpawnAttemptResult.FatalFailure:
                // Stop automatic attempts when configuration is invalid.
                hourlyScheduleEnabled = false;
                flightPending = false;
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
            Debug.LogWarning(
                "Manual sightseeing spawn skipped: no sightseeing ticket desk is placed.",
                this);
            return;
        }

        if (gameTimeManager == null ||
            sightseeingAircraftPrefab == null ||
            spawnPoint == null)
        {
            Debug.LogError(
                "SightseeingAircraftSpawner: Check the Inspector references.",
                this);
            return;
        }

        SpawnAttemptResult result = TrySpawnSightseeingAircraft();

        if (result == SpawnAttemptResult.TicketDeskUnavailable)
        {
            Debug.LogWarning(
                "Manual sightseeing spawn skipped: no sightseeing ticket desk is placed.",
                this);
        }
        else if (result == SpawnAttemptResult.WaitingForParking)
        {
            Debug.LogWarning(
                "Manual sightseeing spawn skipped because no parking " +
                "spot is available. The hourly schedule was not changed.",
                this);
        }
        else if (result == SpawnAttemptResult.FatalFailure)
        {
            Debug.LogError(
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
            Debug.LogError(
                "SightseeingAircraftSpawner: Required references are missing.",
                this);

            return SpawnAttemptResult.FatalFailure;
        }

        SightseeingAircraftParkingSpot spot =
            FindAvailableParkingSpot();

        if (spot == null)
        {
            if (!waitingMessageLogged)
            {
                Debug.LogWarning(
                    "SIGHTSEEING FLIGHT WAITING | No available sightseeing " +
                    "parking spot. The flight remains pending.",
                    this);

                waitingMessageLogged = true;
            }

            scheduleStatus = "Waiting for an available parking spot";

            return SpawnAttemptResult.WaitingForParking;
        }

        GameObject aircraft = Instantiate(
            sightseeingAircraftPrefab,
            spawnPoint.position,
            spawnPoint.rotation);

        if (aircraft == null)
        {
            Debug.LogError(
                "SightseeingAircraftSpawner: Aircraft instantiation failed.",
                this);

            return SpawnAttemptResult.RetryableFailure;
        }

        AircraftAirportFees airportFees =
            aircraft.GetComponent<AircraftAirportFees>();
        if (airportFees != null)
            airportFees.SetFeeCategory(AirportFeeCategory.Sightseeing);

        // Reserve parking before allowing the aircraft to operate.
        if (!spot.TryReserve(aircraft))
        {
            Debug.LogWarning(
                "SightseeingAircraftSpawner: The selected parking spot " +
                "could not be reserved. The aircraft will be removed " +
                "and the flight will be retried.",
                spot);

            Destroy(aircraft);

            return SpawnAttemptResult.RetryableFailure;
        }

        AircraftFlightIdentity identity =
            aircraft.GetComponent<AircraftFlightIdentity>();

        if (identity == null)
        {
            Debug.LogError(
                "Sightseeing aircraft prefab is missing " +
                "AircraftFlightIdentity. Check the prefab.",
                aircraft);

            spot.Release(aircraft);
            Destroy(aircraft);

            return SpawnAttemptResult.FatalFailure;
        }

        string flightNumber = GetNextFlightNumber();

        int startMinute =
            gameTimeManager.Hour * 60 +
            gameTimeManager.Minute;

        identity.InitializeHobbyFlight(
            flightNumber,
            startMinute,
            gameTimeManager.CurrentDay);

        if (logSpawnDetails)
        {
            Debug.Log(
                $"SIGHTSEEING AIRCRAFT SPAWNED | " +
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
            FindObjectsByType<SightseeingAircraftParkingSpot>(
                FindObjectsSortMode.None);

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
            FindObjectsByType<AircraftFlightIdentity>(
                FindObjectsSortMode.None);

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
}
