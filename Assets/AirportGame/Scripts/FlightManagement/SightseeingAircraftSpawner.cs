
using UnityEngine;

/// <summary>
/// Airport-wide sightseeing flight scheduler and aircraft spawner.
///
/// Schedules one flight at each new in-game hour. If no parking spot
/// is available, keeps one pending flight and retries until it can spawn.
/// Missed hourly slots never accumulate into a queue.
/// </summary>
[DisallowMultipleComponent]
public class SightseeingAircraftSpawner : MonoBehaviour
{
    private enum SpawnAttemptResult
    {
        Spawned,
        WaitingForParking,
        RetryableFailure,
        FatalFailure
    }

    private static SightseeingAircraftSpawner activeSpawner;

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

    public bool FlightPending => flightPending;
    public bool HourlyScheduleEnabled => hourlyScheduleEnabled;
    public string ScheduleStatus => scheduleStatus;

    private void Awake()
    {
        // This component represents the airport-wide schedule.
        // Prevent accidental duplicate schedulers in the scene.
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

        scheduleStatus = hourlyScheduleEnabled
            ? "Waiting for the next hourly departure"
            : "Hourly schedule disabled";

        if (logSpawnDetails)
        {
            Debug.Log(
                $"SIGHTSEEING SCHEDULE READY | Day: " +
                $"{gameTimeManager.CurrentDay} | " +
                $"Time: {gameTimeManager.FormattedTime} | " +
                $"Next flight at the next in-game hour.",
                this);
        }

        // Optional manual testing convenience.
        // This test spawn does not create an additional scheduled flight.
        if (spawnOnStart)
            SpawnSightseeingAircraft();
    }

    private void Update()
    {
        if (!hourlyScheduleEnabled || gameTimeManager == null)
            return;

        // Do not schedule or retry departures while game time is paused.
        if (gameTimeManager.CurrentSpeedMultiplier <= 0)
            return;

        CheckForNewHourlySlot();

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

    private void CheckForNewHourlySlot()
    {
        long currentSlot = GetCurrentHourSlot();

        if (currentSlot <= lastObservedHourSlot)
            return;

        // Record the newest slot even if several hours were skipped
        // between updates. Never queue one flight per missed hour.
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

            case SpawnAttemptResult.WaitingForParking:
            case SpawnAttemptResult.RetryableFailure:
                retryTimer = Mathf.Max(
                    0.1f,
                    parkingRetryIntervalSeconds);
                break;

            case SpawnAttemptResult.FatalFailure:
                // Stop automatic attempts when the prefab or configuration
                // is invalid, rather than repeatedly creating broken aircraft.
                hourlyScheduleEnabled = false;
                flightPending = false;
                scheduleStatus =
                    "Schedule disabled because of a configuration error";
                break;
        }
    }

    /// <summary>
    /// Manual test spawn. Does not alter the hourly schedule.
    /// </summary>
    [ContextMenu("Spawn Sightseeing Aircraft")]
    public void SpawnSightseeingAircraft()
    {
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

        if (result == SpawnAttemptResult.WaitingForParking)
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
