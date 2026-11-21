
using System.Collections;
using UnityEngine;

public class GateReadyForTaxiTestSpawner : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
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
            LogError(
                $"{name}: Aircraft Prefab is not assigned.",
                this
            );
            return;
        }

        Transform point = spawnPoint != null
            ? spawnPoint
            : transform;

        GameObject aircraft = Instantiate(
            aircraftPrefab,
            point.position,
            point.rotation
        );

        aircraft.name = $"DepartureTestAircraft_{spawnedCount + 1:00}";

        // Prevent the test aircraft from starting a landing sequence.
        if (disableLandingController)
        {
            AircraftCargoPaxLandingController landingController =
                aircraft.GetComponentInChildren
                    <AircraftCargoPaxLandingController>(true);

            if (landingController != null)
                landingController.enabled = false;
        }

        // Skip the normal turnaround process.
        AircraftTurnaroundController turnaroundController =
            aircraft.GetComponent<AircraftTurnaroundController>();

        if (turnaroundController != null)
            turnaroundController.enabled = false;

        AircraftCargoPaxGateExitController gateExitController =
            aircraft.GetComponent<AircraftCargoPaxGateExitController>();

        AircraftCargoPaxDepartureTaxiController departureController =
            aircraft.GetComponent<AircraftCargoPaxDepartureTaxiController>();

        if (gateExitController == null || departureController == null)
        {
            LogError(
                $"{aircraft.name}: Required gate-exit or departure controller is missing.",
                aircraft
            );

            Destroy(aircraft);
            return;
        }

        // Bypass turnaround and initialize the departure taxi sequence.
        gateExitController.SetReadyForTaxiForTesting();

        spawnedCount++;

        Log(
            $"DEPARTURE TEST AIRCRAFT SPAWNED | " +
            $"Aircraft: {aircraft.name} | " +
            $"Spawned: {spawnedCount}/{maximumAircraftToSpawn} | " +
            "Turnaround skipped; ready for departure taxi.",
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

        Log(
            $"{name}: Test spawner counter reset. " +
            "Existing aircraft have not been destroyed.",
            this
        );
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}