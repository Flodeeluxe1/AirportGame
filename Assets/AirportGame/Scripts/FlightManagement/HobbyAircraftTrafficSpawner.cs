
using UnityEngine;

/// <summary>
/// Spawns visiting hobby aircraft according to runway availability,
/// grass parking capacity, fees, time of day and airport demand.
/// Includes an optional real-time test mode for ground movement testing.
/// This system is separate from scheduled airline traffic.
/// </summary>
[DisallowMultipleComponent]
public class HobbyAircraftTrafficSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject hobbyAircraftPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private GameTimeManager gameTimeManager;
    [SerializeField] private GridManager gridManager;

    [Header("Traffic Limits")]
    [Min(1)]
    [SerializeField] private int maximumActiveHobbyAircraft = 3;

    [Tooltip("How often the airport checks for a potential visitor, in game minutes.")]
    [Min(1)]
    [SerializeField] private int demandCheckIntervalMinutes = 15;

    [Header("Test Flight Mode")]
    [Tooltip("When enabled, attempts to spawn a test aircraft at a fixed real-time interval.")]
    [SerializeField] private bool testFlightMode = false;

    [Tooltip("Seconds between test aircraft spawn attempts. Uses real time, not game time.")]
    [Min(1f)]
    [SerializeField] private float testSpawnIntervalSeconds = 10f;

    private float nextTestSpawnTime;

    [Header("Daily Demand")]
    [Tooltip("Expected daily visitors before parking capacity is added.")]
    [Min(0f)]
    [SerializeField] private float baseDailyVisits = 1.5f;

    [Tooltip("Additional expected daily visitors per placed grass parking spot.")]
    [Min(0f)]
    [SerializeField] private float dailyVisitsPerParkingSpot = 0.85f;

    [Tooltip("Additional daily demand per usable runway beyond the first.")]
    [Min(0f)]
    [SerializeField] private float dailyVisitsPerExtraRunway = 0.5f;

    [Tooltip("Can later be connected to an airport reputation system.")]
    [Min(0f)]
    [SerializeField] private float reputationDemandMultiplier = 1f;

    [Min(0f)]
    [SerializeField] private float maximumExpectedDailyVisits = 15f;

    [Header("Time Of Day")]
    [Tooltip("Demand multiplier from 07:00 to 18:59.")]
    [Min(0f)]
    [SerializeField] private float daytimeMultiplier = 1f;

    [Tooltip("Demand multiplier from 05:00-06:59 and 19:00-20:59.")]
    [Min(0f)]
    [SerializeField] private float dawnDuskMultiplier = 0.5f;

    [Tooltip("Demand multiplier outside daytime and dawn/dusk.")]
    [Min(0f)]
    [SerializeField] private float nighttimeMultiplier = 0.1f;

    [Header("Weekly Demand")]
    [Tooltip("Days 6 and 7 are treated as the weekend; day 1 is Monday.")]
    [Min(0f)]
    [SerializeField] private float weekendDemandMultiplier = 1.25f;

    [Header("Fee Sensitivity")]
    [Tooltip("Higher configured fees reduce demand. 0.001 means a $100 fee adds 0.1 to the fee penalty.")]
    [Min(0f)]
    [SerializeField] private float landingFeeSensitivity = 0.001f;

    [Min(0f)]
    [SerializeField] private float parkingFeeSensitivity = 0.001f;

    [Header("Flight Numbers")]
    [Min(1)]
    [SerializeField] private int nextFlightNumber = 101;

    [Header("Debug")]
    [SerializeField] private bool logDemandChecks = false;
    [SerializeField] private string debugStatus = "Initializing";

    private const long SecondsPerDay = 86400L;

    private long nextDemandCheckGameSeconds;
    private long lastObservedGameSeconds = -1;
    private bool initialized;
    private bool previousTestFlightMode;

    private void Start()
    {
        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (hobbyAircraftPrefab == null)
        {
            Debug.LogError(
                "HobbyAircraftTrafficSpawner: Assign a hobby aircraft prefab.",
                this
            );

            enabled = false;
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogError(
                "HobbyAircraftTrafficSpawner: Assign an approach spawn point.",
                this
            );

            enabled = false;
            return;
        }

        if (gameTimeManager == null)
        {
            Debug.LogError(
                "HobbyAircraftTrafficSpawner: GameTimeManager is missing.",
                this
            );

            enabled = false;
            return;
        }

        if (!ValidateHobbyAircraftPrefab())
        {
            enabled = false;
            return;
        }

        long now = GetCurrentGameSeconds();

        nextDemandCheckGameSeconds =
            now + GetCheckIntervalSeconds();

        lastObservedGameSeconds = now;
        initialized = true;

        previousTestFlightMode = testFlightMode;
        nextTestSpawnTime = Time.unscaledTime + Mathf.Max(1f, testSpawnIntervalSeconds);

        debugStatus = testFlightMode
            ? "Test flight mode enabled; waiting for first test spawn"
            : "Waiting for first demand check";

        Debug.Log(
            "HobbyAircraftTrafficSpawner ready. " +
            $"Demand checks every {demandCheckIntervalMinutes} " +
            "in-game minutes.",
            this
        );
    }

    private void Update()
    {
        if (!initialized || gameTimeManager == null)
            return;

        // Detect the checkbox being toggled in the Inspector at runtime.
        if (testFlightMode != previousTestFlightMode)
        {
            previousTestFlightMode = testFlightMode;

            if (testFlightMode)
            {
                nextTestSpawnTime =
                    Time.unscaledTime + Mathf.Max(1f, testSpawnIntervalSeconds);

                debugStatus = "Test flight mode enabled";
                Debug.Log("Hobby test flight mode enabled.", this);
            }
            else
            {
                debugStatus = "Test flight mode disabled; normal demand resumed";
                Debug.Log("Hobby test flight mode disabled.", this);
            }
        }

        if (testFlightMode)
        {
            UpdateTestFlightSpawning();
            return;
        }

        UpdateNormalDemandSpawning();
    }

    private void UpdateTestFlightSpawning()
    {
        if (Time.unscaledTime < nextTestSpawnTime)
            return;

        // Schedule the next attempt from now. This avoids a burst of
        // aircraft if the game temporarily pauses or a frame takes too long.
        nextTestSpawnTime =
            Time.unscaledTime + Mathf.Max(1f, testSpawnIntervalSeconds);

        bool spawned = TrySpawnVisitor(ignoreDemandRoll: true);

        if (!spawned && logDemandChecks)
        {
            Debug.Log(
                $"Hobby test spawn skipped: {debugStatus}",
                this
            );
        }
    }

    private void UpdateNormalDemandSpawning()
    {
        long now = GetCurrentGameSeconds();

        // Handle loading a save or resetting the game clock.
        if (lastObservedGameSeconds >= 0 &&
            now < lastObservedGameSeconds)
        {
            nextDemandCheckGameSeconds =
                now + GetCheckIntervalSeconds();

            lastObservedGameSeconds = now;
            debugStatus = "Game clock reset; demand timer restarted";
            return;
        }

        lastObservedGameSeconds = now;

        if (now < nextDemandCheckGameSeconds)
            return;

        bool spawnedThisUpdate = false;
        int checksProcessed = 0;

        // Process elapsed intervals safely if game time advances quickly.
        while (nextDemandCheckGameSeconds <= now &&
               checksProcessed < 512)
        {
            nextDemandCheckGameSeconds +=
                GetCheckIntervalSeconds();

            checksProcessed++;

            if (!spawnedThisUpdate &&
                TrySpawnVisitor(ignoreDemandRoll: false))
            {
                spawnedThisUpdate = true;
            }
        }

        // Avoid an excessive catch-up loop after a very large time jump.
        if (nextDemandCheckGameSeconds <= now)
        {
            nextDemandCheckGameSeconds =
                now + GetCheckIntervalSeconds();
        }
    }

    private bool TrySpawnVisitor(bool ignoreDemandRoll)
    {
        if (!HasRequiredAirportInfrastructure(out int runwayCount))
        {
            debugStatus = "Waiting for a usable runway and airport systems";
            return false;
        }

        GetParkingSpotCounts(
            out int parkingSpotCount,
            out int freeSpotCount
        );

        if (parkingSpotCount == 0)
        {
            debugStatus = "No placed grass parking spots";
            return false;
        }

        if (freeSpotCount == 0)
        {
            debugStatus = "All grass parking spots are occupied";
            return false;
        }

        int activeAircraftCount = CountActiveHobbyAircraft();

        int effectiveAircraftLimit = Mathf.Min(
            maximumActiveHobbyAircraft,
            parkingSpotCount
        );

        // Count inbound, parked and departing hobby aircraft.
        // This prevents multiple inbound aircraft from competing
        // for the same limited parking capacity.
        if (activeAircraftCount >= effectiveAircraftLimit)
        {
            debugStatus = "Hobby aircraft traffic limit reached";
            return false;
        }

        if (!ignoreDemandRoll)
        {
            float probability = CalculateArrivalProbability(
                parkingSpotCount,
                runwayCount
            );

            bool arrivalRequested = Random.value < probability;

            if (logDemandChecks)
            {
                Debug.Log(
                    $"Hobby demand check | Parking: {parkingSpotCount} | " +
                    $"Free: {freeSpotCount} | Active: {activeAircraftCount} | " +
                    $"Arrival probability: {probability:P1} | " +
                    $"Arrival selected: {arrivalRequested}",
                    this
                );
            }

            if (!arrivalRequested)
            {
                debugStatus = "No visitor attracted on this demand check";
                return false;
            }
        }
        else if (logDemandChecks)
        {
            Debug.Log(
                $"Hobby TEST spawn | Parking: {parkingSpotCount} | " +
                $"Free: {freeSpotCount} | Active: {activeAircraftCount}",
                this
            );
        }

        return SpawnHobbyAircraft();
    }

    private bool HasRequiredAirportInfrastructure(
        out int usableRunwayCount)
    {
        usableRunwayCount = 0;

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (gridManager == null ||
            FindFirstObjectByType<Weather>() == null ||
            FindFirstObjectByType<AircraftRunwayTrafficManager>() == null ||
            FindFirstObjectByType<TaxiwayNetwork>() == null ||
            FindFirstObjectByType<TaxiwayPathfinder>() == null)
        {
            return false;
        }

        AircraftRunway[] runways =
            FindObjectsByType<AircraftRunway>(
                FindObjectsSortMode.None
            );

        foreach (AircraftRunway runway in runways)
        {
            if (runway == null ||
                runway.GetComponent<PlacedBuilding>() == null)
            {
                continue;
            }

            if (runway.TryGetGeometry(
                    gridManager,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                usableRunwayCount++;
            }
        }

        return usableRunwayCount > 0;
    }


    private void GetParkingSpotCounts(
        out int total,
        out int available)
    {
        total = 0;
        available = 0;

        HobbyAircraftParkingSpot[] spots =
            FindObjectsByType<HobbyAircraftParkingSpot>(
                FindObjectsSortMode.None
            );

        foreach (HobbyAircraftParkingSpot spot in spots)
        {
            if (spot == null ||
                !spot.IsPlacedBuilding ||
                spot.ReservedForFlightSchool)
            {
                continue;
            }

            // Only count spots available to ordinary hobby traffic.
            total++;

            if (!spot.IsOccupied)
                available++;
        }
    }


    private int CountActiveHobbyAircraft()
    {
        AircraftFlightIdentity[] identities =
            FindObjectsByType<AircraftFlightIdentity>(
                FindObjectsSortMode.None
            );

        int count = 0;

        foreach (AircraftFlightIdentity identity in identities)
        {
            if (identity != null &&
                identity.IsInitialized &&
                identity.IsHobbyFlight)
            {
                count++;
            }
        }

        return count;
    }

    private float CalculateArrivalProbability(
        int parkingSpotCount,
        int runwayCount)
    {
        float expectedDailyVisits =
            baseDailyVisits +
            dailyVisitsPerParkingSpot * parkingSpotCount +
            dailyVisitsPerExtraRunway * Mathf.Max(0, runwayCount - 1);

        expectedDailyVisits = Mathf.Min(
            expectedDailyVisits,
            maximumExpectedDailyVisits
        );

        expectedDailyVisits *= reputationDemandMultiplier;
        expectedDailyVisits *= GetTimeOfDayMultiplier();
        expectedDailyVisits *= GetDayOfWeekMultiplier();
        expectedDailyVisits *= GetFeeDemandMultiplier();

        float checksPerDay =
            1440f / Mathf.Max(1, demandCheckIntervalMinutes);

        return Mathf.Clamp01(
            expectedDailyVisits / checksPerDay
        );
    }

    private float GetTimeOfDayMultiplier()
    {
        int hour = gameTimeManager.Hour;

        if (hour >= 7 && hour < 19)
            return daytimeMultiplier;

        if ((hour >= 5 && hour < 7) ||
            (hour >= 19 && hour < 21))
        {
            return dawnDuskMultiplier;
        }

        return nighttimeMultiplier;
    }

    private float GetDayOfWeekMultiplier()
    {
        // The game's first day is treated as Monday.
        int dayOfWeek =
            (int)((gameTimeManager.CurrentDay - 1) % 7) + 1;

        return dayOfWeek >= 6
            ? weekendDemandMultiplier
            : 1f;
    }

    private float GetFeeDemandMultiplier()
    {
        HobbyAircraftVisitController visitController =
            hobbyAircraftPrefab.GetComponent<HobbyAircraftVisitController>();

        if (visitController == null)
            return 1f;

        float feePenalty =
            visitController.LandingFee * landingFeeSensitivity +
            visitController.ParkingFee * parkingFeeSensitivity;

        return 1f / (1f + feePenalty);
    }

    private bool SpawnHobbyAircraft()
    {
        GameObject aircraft = Instantiate(
            hobbyAircraftPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        AircraftFlightIdentity identity =
            aircraft.GetComponent<AircraftFlightIdentity>();

        if (identity == null)
        {
            Debug.LogError(
                "Spawned hobby aircraft has no AircraftFlightIdentity.",
                aircraft
            );

            Destroy(aircraft);
            debugStatus = "Spawn failed: missing flight identity";
            return false;
        }

        string flightNumber = GetNextFlightNumber();

        int startMinute =
            gameTimeManager.Hour * 60 +
            gameTimeManager.Minute;

        identity.InitializeHobbyFlight(
            flightNumber,
            startMinute,
            gameTimeManager.CurrentDay
        );

        debugStatus = $"Spawned visiting aircraft {flightNumber}";

        Debug.Log(
            $"HOBBY VISITOR ARRIVAL | Flight: {flightNumber} | " +
            $"Day: {gameTimeManager.CurrentDay} | " +
            $"Time: {gameTimeManager.FormattedTime}",
            aircraft
        );

        return true;
    }

    private string GetNextFlightNumber()
    {
        // Avoid reusing a flight number that is currently active.
        for (int attempt = 0; attempt < 10000; attempt++)
        {
            string candidate = $"VFR{nextFlightNumber}";
            nextFlightNumber++;

            bool inUse = false;

            AircraftFlightIdentity[] identities =
                FindObjectsByType<AircraftFlightIdentity>(
                    FindObjectsSortMode.None
                );

            foreach (AircraftFlightIdentity identity in identities)
            {
                if (identity != null &&
                    identity.IsInitialized &&
                    identity.FlightNumber == candidate)
                {
                    inUse = true;
                    break;
                }
            }

            if (!inUse)
                return candidate;
        }

        return $"VFR{System.Guid.NewGuid().ToString("N").Substring(0, 8)}";
    }

    private bool ValidateHobbyAircraftPrefab()
    {
        bool valid = true;

        if (hobbyAircraftPrefab.GetComponent<AircraftFlightIdentity>() == null ||
            hobbyAircraftPrefab.GetComponent<AircraftLandingController>() == null ||
            hobbyAircraftPrefab.GetComponent<HobbyAircraftTaxiController>() == null ||
            hobbyAircraftPrefab.GetComponent<HobbyAircraftVisitController>() == null ||
            hobbyAircraftPrefab.GetComponent<AircraftDepartureTaxiController>() == null ||
            hobbyAircraftPrefab.GetComponent<AircraftTakeoffController>() == null)
        {
            Debug.LogError(
                "Hobby aircraft prefab is missing one or more required " +
                "root components: AircraftFlightIdentity, " +
                "AircraftLandingController, HobbyAircraftTaxiController, " +
                "HobbyAircraftVisitController, " +
                "AircraftDepartureTaxiController or AircraftTakeoffController.",
                this
            );

            valid = false;
        }

        if (hobbyAircraftPrefab.GetComponent<AircraftAirportFees>() != null)
        {
            Debug.LogWarning(
                "The hobby prefab also has AircraftAirportFees. " +
                "HobbyAircraftVisitController already charges hobby fees, " +
                "so remove AircraftAirportFees from this hobby prefab " +
                "to avoid duplicate landing charges.",
                hobbyAircraftPrefab
            );
        }

        return valid;
    }

    private long GetCurrentGameSeconds()
    {
        return (gameTimeManager.CurrentDay - 1L) * SecondsPerDay
               + gameTimeManager.GetGameTimeSeconds();
    }

    private long GetCheckIntervalSeconds()
    {
        return Mathf.Max(1, demandCheckIntervalMinutes) * 60L;
    }
}
