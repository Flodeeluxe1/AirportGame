
using System.Collections;
using UnityEngine;

public class GateReadyForTaxiTestSpawner : MonoBehaviour
{
    [Header("Aircraft")]
    [SerializeField]
    private GameObject aircraftPrefab;

    [Tooltip("Create an empty child at the desired aircraft spawn position.")]
    [SerializeField]
    private Transform spawnPoint;

    [Header("Spawning")]
    [SerializeField]
    private bool spawnOnPlay = true;

    [SerializeField]
    private bool spawnImmediately = true;

    [Min(1f)]
    [SerializeField]
    private float spawnIntervalSeconds = 5f;

    [Min(1)]
    [SerializeField]
    private int maximumAircraftToSpawn = 5;

    [Header("Testing")]
    [SerializeField]
    private bool disableLandingController = true;

    private int spawnedCount;

    private void Start()
    {
        if (!spawnOnPlay)
            return;

        StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        if (spawnImmediately)
            SpawnAircraft();

        while (spawnedCount < maximumAircraftToSpawn)
        {
            yield return new WaitForSecondsRealtime(
                spawnIntervalSeconds
            );

            SpawnAircraft();
        }
    }

    private void SpawnAircraft()
    {
        if (spawnedCount >= maximumAircraftToSpawn)
            return;

        if (aircraftPrefab == null)
        {
            Debug.LogError(
                $"{name}: Aircraft Prefab is not assigned.",
                this
            );

            return;
        }

        Transform point =
            spawnPoint != null ? spawnPoint : transform;

        GameObject aircraft = Instantiate(
            aircraftPrefab,
            point.position,
            point.rotation
        );

        spawnedCount++;

        aircraft.name = $"DepartureTestAircraft_{spawnedCount:00}";

        // Prevent the test aircraft from starting its landing sequence.
        if (disableLandingController)
        {
            AircraftLandingController landingController =
                aircraft.GetComponentInChildren
                    <AircraftLandingController>(true);

            if (landingController != null)
                landingController.enabled = false;
        }

        AircraftGateExitController gateExitController =
            aircraft.GetComponent<AircraftGateExitController>();

        if (gateExitController == null)
        {
            Debug.LogError(
                $"{aircraft.name}: AircraftGateExitController is missing.",
                aircraft
            );

            return;
        }

        AircraftDepartureTaxiController departureController =
            aircraft.GetComponent<AircraftDepartureTaxiController>();

        if (departureController == null)
        {
            Debug.LogError(
                $"{aircraft.name}: AircraftDepartureTaxiController is missing.",
                aircraft
            );

            return;
        }

        gateExitController.SetReadyForTaxiForTesting();

        Debug.Log(
            $"DEPARTURE TEST AIRCRAFT SPAWNED | " +
            $"Aircraft: {aircraft.name} | " +
            $"Spawned: {spawnedCount}/{maximumAircraftToSpawn}",
            aircraft
        );
    }

    [ContextMenu("Debug/Spawn One Test Aircraft")]
    private void DebugSpawnOneAircraft()
    {
        SpawnAircraft();
    }

    [ContextMenu("Debug/Reset Spawn Counter")]
    private void DebugResetSpawnCounter()
    {
        spawnedCount = 0;

        Debug.Log(
            $"{name}: Test spawner counter reset. " +
            "Existing aircraft have not been destroyed.",
            this
        );
    }
}