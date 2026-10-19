
using UnityEngine;

/// <summary>
/// Spawns sightseeing aircraft and reserves a dedicated parking spot
/// before allowing the aircraft to begin its flight sequence.
/// </summary>
[DisallowMultipleComponent]
public class SightseeingAircraftSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject sightseeingAircraftPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Test Settings")]
    [SerializeField] private bool spawnOnStart;
    [SerializeField] private bool logSpawnDetails = true;

    [Header("Automatic Spawning")]
    [SerializeField] private bool spawnAutomatically;
    [Min(0.1f)]
    [SerializeField] private float spawnIntervalSeconds = 5f;

    [Header("Flight Number")]
    [Min(1)]
    [SerializeField] private int nextFlightNumber = 1;

    private float spawnTimer;

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
                this
            );

            enabled = false;
            return;
        }

        if (spawnOnStart)
            SpawnSightseeingAircraft();
    }

    private void Update()
    {
        if (!spawnAutomatically)
        {
            spawnTimer = 0f;
            return;
        }

        spawnTimer += Time.deltaTime;

        if (spawnTimer >= Mathf.Max(0.1f, spawnIntervalSeconds))
        {
            spawnTimer = 0f;
            SpawnSightseeingAircraft();
        }
    }

    [ContextMenu("Spawn Sightseeing Aircraft")]
    public void SpawnSightseeingAircraft()
    {
        if (sightseeingAircraftPrefab == null ||
            spawnPoint == null ||
            gameTimeManager == null)
        {
            Debug.LogError(
                "SightseeingAircraftSpawner: Check the Inspector references.",
                this
            );
            return;
        }

        SightseeingAircraftParkingSpot spot =
            FindAvailableParkingSpot();

        if (spot == null)
        {
            Debug.LogWarning(
                "SightseeingAircraftSpawner: No available sightseeing " +
                "parking spot. Aircraft was not spawned.",
                this
            );
            return;
        }

        GameObject aircraft = Instantiate(
            sightseeingAircraftPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        if (!spot.TryReserve(aircraft))
        {
            Debug.LogWarning(
                "SightseeingAircraftSpawner: Could not reserve the selected " +
                "parking spot. Aircraft was not spawned.",
                spot
            );

            Destroy(aircraft);
            return;
        }

        AircraftFlightIdentity identity =
            aircraft.GetComponent<AircraftFlightIdentity>();

        if (identity == null)
        {
            Debug.LogError(
                "Sightseeing aircraft prefab is missing AircraftFlightIdentity.",
                aircraft
            );

            spot.Release(aircraft);
            Destroy(aircraft);
            return;
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

        if (logSpawnDetails)
        {
            Debug.Log(
                $"SIGHTSEEING AIRCRAFT SPAWNED | Flight: {flightNumber} | " +
                $"Reserved Spot: {spot.name} | " +
                $"Day: {gameTimeManager.CurrentDay} | " +
                $"Time: {gameTimeManager.FormattedTime}",
                aircraft
            );
        }
    }

    private SightseeingAircraftParkingSpot FindAvailableParkingSpot()
    {
        SightseeingAircraftParkingSpot[] spots =
            FindObjectsByType<SightseeingAircraftParkingSpot>(
                FindObjectsSortMode.None
            );

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
                FindObjectsSortMode.None
            );

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
}
