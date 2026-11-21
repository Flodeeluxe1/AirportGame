using UnityEngine;

[DisallowMultipleComponent]
public class CleaningLotSpawner : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    [Header("Prefab")]
    [SerializeField] private GameObject cleaningVehiclePrefab;

    [Header("Parking")]
    [Tooltip("Spawn and home parking transform for the cleaning vehicle.")]
    [SerializeField] private Transform cleaningVehicleParkingPoint;

    [Header("Spawn")]
    [SerializeField] private float spawnHeightOffset;

    [Header("Diagnostics")]
    [SerializeField] private bool logSpawn = true;

    private GameObject spawnedCleaningVehicle;

    private void Start()
    {
        if (cleaningVehicleParkingPoint == null)
            cleaningVehicleParkingPoint = transform.Find("CleaningVehicleParking");

        if (cleaningVehiclePrefab == null)
        {
            LogError(
                $"{name}: Assign the CleaningVehicle prefab.",
                this);
            return;
        }

        if (cleaningVehicleParkingPoint == null)
        {
            LogError(
                $"{name}: Assign a CleaningVehicleParking transform.",
                this);
            return;
        }

        Vector3 spawnPosition = cleaningVehicleParkingPoint.position;
        spawnPosition.y += spawnHeightOffset;

        spawnedCleaningVehicle = Instantiate(
            cleaningVehiclePrefab,
            spawnPosition,
            cleaningVehicleParkingPoint.rotation);

        AircraftCleaningVehicleController controller =
            spawnedCleaningVehicle.GetComponent<AircraftCleaningVehicleController>();

        if (controller == null)
        {
            LogError(
                $"{name}: CleaningVehicle prefab must have an " +
                "AircraftCleaningVehicleController on its root.",
                spawnedCleaningVehicle);
            Destroy(spawnedCleaningVehicle);
            return;
        }

        controller.SetHomeParkingPoint(cleaningVehicleParkingPoint);

        if (logSpawn)
        {
            Log(
                $"{name}: Spawned cleaning vehicle '{spawnedCleaningVehicle.name}' " +
                $"at {cleaningVehicleParkingPoint.name}.",
                this);
        }
    }

    private void OnDestroy()
    {
        if (spawnedCleaningVehicle != null)
            Destroy(spawnedCleaningVehicle);
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
