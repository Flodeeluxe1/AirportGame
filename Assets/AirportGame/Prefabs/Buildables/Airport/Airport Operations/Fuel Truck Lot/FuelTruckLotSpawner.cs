using UnityEngine;

/// <summary>
/// Spawns one airport fuel truck at its parking lot.
/// </summary>
[DisallowMultipleComponent]
public class FuelTruckLotSpawner : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
{
    [Header("Prefab")]
    [SerializeField] private GameObject fuelTruckPrefab;

    [Header("Parking Position")]
    [Tooltip("Optional child transform used as the truck's spawn position.")]
    [SerializeField] private Transform parkingPosition;

    [Header("Spawn Settings")]
    [Tooltip("World-space height at which the fuel truck spawns.")]
    [SerializeField] private float spawnHeight = 0.23f;

    [Header("Debug")]
    [SerializeField] private bool showDebugMessages = true;

    private GameObject spawnedFuelTruck;

    private void Start()
    {
        if (fuelTruckPrefab == null)
        {
            LogError(
                "FuelTruckLotSpawner: Airport fuel truck prefab is not assigned.",
                this);
            return;
        }

        if (parkingPosition == null)
            parkingPosition = transform.Find("FuelTruckParking");

        Vector3 spawnPosition;
        Quaternion spawnRotation;

        if (parkingPosition != null)
        {
            spawnPosition = parkingPosition.position;
            spawnRotation = parkingPosition.rotation;
        }
        else
        {
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
        }

        spawnPosition.y = spawnHeight;

        spawnedFuelTruck = Instantiate(
            fuelTruckPrefab,
            spawnPosition,
            spawnRotation);

        spawnedFuelTruck.name = fuelTruckPrefab.name;

        AirportFuelTruckController truckController =
            spawnedFuelTruck.GetComponent<AirportFuelTruckController>();

        if (truckController == null)
        {
            LogError(
                "FuelTruckLotSpawner: Airport fuel truck prefab must have " +
                "an AirportFuelTruckController component on its root.",
                spawnedFuelTruck);
            Destroy(spawnedFuelTruck);
            spawnedFuelTruck = null;
            return;
        }

        if (parkingPosition != null)
        {
            truckController.SetHomeSpawnPoint(
                parkingPosition,
                Vector3.zero,
                spawnHeight);
        }
        else
        {
            truckController.SetHomeSpawnPoint(
                transform,
                Vector3.zero,
                spawnHeight);
        }

        if (showDebugMessages)
        {
            Log(
                "FuelTruckLotSpawner: Spawned airport fuel truck at " +
                gameObject.name + ".",
                this);
        }
    }

    private void OnDestroy()
    {
        if (spawnedFuelTruck != null)
            Destroy(spawnedFuelTruck);
    }

 
    private void Log(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.Log(message, context); else Debug.Log(message);
    }

    private void LogWarning(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message);
    }

    private void LogError(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogError(message, context); else Debug.LogError(message);
    }

    private void LogException(System.Exception exception, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception);
    }
}
