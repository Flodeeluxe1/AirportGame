using UnityEngine;

/// <summary>
/// Spawns one airport fuel truck at its parking lot.
/// </summary>
[DisallowMultipleComponent]
public class FuelTruckLotSpawner : MonoBehaviour
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
            Debug.LogError(
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

        if (showDebugMessages)
        {
            Debug.Log(
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
}
