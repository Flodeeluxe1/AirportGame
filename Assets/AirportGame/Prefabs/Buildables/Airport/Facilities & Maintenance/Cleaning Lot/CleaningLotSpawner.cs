using UnityEngine;

[DisallowMultipleComponent]
public class CleaningLotSpawner : MonoBehaviour
{
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
            Debug.LogError(
                $"{name}: Assign the CleaningVehicle prefab.",
                this);
            return;
        }

        if (cleaningVehicleParkingPoint == null)
        {
            Debug.LogError(
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
            Debug.LogError(
                $"{name}: CleaningVehicle prefab must have an " +
                "AircraftCleaningVehicleController on its root.",
                spawnedCleaningVehicle);
            Destroy(spawnedCleaningVehicle);
            return;
        }

        controller.SetHomeParkingPoint(cleaningVehicleParkingPoint);

        if (logSpawn)
        {
            Debug.Log(
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
}
