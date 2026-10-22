
using UnityEngine;

public class TrafficTestSpawner : MonoBehaviour
{
    private enum TrafficSpawnMode
    {
        NormalTrafficOnly,
        CargoTrucksOnly,
        Both
    }

    [Header("References")]
    [SerializeField] private GameObject trafficVehiclePrefab;
    [SerializeField] private GameObject cargoTruckPrefab;
    [SerializeField] private GridManager gridManager;

    [Header("Traffic Type")]
    [SerializeField]
    private TrafficSpawnMode spawnMode =
        TrafficSpawnMode.NormalTrafficOnly;

    [Tooltip("When Both is selected, choose randomly between normal traffic and cargo trucks.")]
    [SerializeField] private bool randomizeWhenBoth = true;

    [Header("Spawning")]
    [Min(0.1f)]
    [SerializeField] private float spawnInterval = 10f;

    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Exit")]
    [SerializeField] private Transform exitPoint;

    private float spawnTimer;
    private bool nextSpawnIsCargoTruck;

    private void Awake()
    {
        if (gridManager == null)
        {
            gridManager = FindFirstObjectByType<GridManager>();
        }
    }

    private void Start()
    {
        spawnTimer = spawnInterval;
    }

    private void Update()
    {
        spawnTimer -= Time.deltaTime;

        if (spawnTimer > 0f)
        {
            return;
        }

        spawnTimer = spawnInterval;
        SpawnVehicle();
    }

    private void SpawnVehicle()
    {
        if (gridManager == null)
        {
            Debug.LogError(
                "TrafficTestSpawner: GridManager is missing.",
                this
            );
            return;
        }

        if (exitPoint == null)
        {
            Debug.LogError(
                "TrafficTestSpawner: Exit Point is missing.",
                this
            );
            return;
        }

        bool spawnCargoTruck = ShouldSpawnCargoTruck();

        GameObject selectedPrefab = spawnCargoTruck
            ? cargoTruckPrefab
            : trafficVehiclePrefab;

        if (selectedPrefab == null)
        {
            Debug.LogError(
                spawnCargoTruck
                    ? "TrafficTestSpawner: Cargo Truck Prefab is missing."
                    : "TrafficTestSpawner: Traffic Vehicle Prefab is missing.",
                this
            );
            return;
        }

        Vector2Int startCell =
            gridManager.WorldToGrid(transform.position);

        Vector2Int exitCell =
            gridManager.WorldToGrid(exitPoint.position);

        Vector3 spawnPosition =
            gridManager.GridToWorldCenter(startCell);

        spawnPosition.y += spawnHeightOffset;


        GameObject vehicle = Instantiate(
            selectedPrefab,
            spawnPosition,
            transform.rotation
        );

        // Assign a unique ID shared by both vehicle types.
        VehicleSpawnID spawnID =
            vehicle.GetComponent<VehicleSpawnID>();

        if (spawnID == null)
        {
            Debug.LogError(
                "TrafficTestSpawner: Vehicle prefab must have " +
                "VehicleSpawnID attached.",
                vehicle
            );

            Destroy(vehicle);
            return;
        }

        spawnID.InitializeID();

        Debug.Log(
            $"VEHICLE SPAWNED | ID: {spawnID.SpawnID} | " +
            $"Type: {(spawnCargoTruck ? "Cargo Truck" : "Normal Traffic")}",
            vehicle
        );


        if (spawnCargoTruck)
        {
            CargoDeliveryTruck cargoTruck =
                vehicle.GetComponent<CargoDeliveryTruck>();

            if (cargoTruck == null)
            {
                Debug.LogError(
                    "TrafficTestSpawner: Cargo Truck Prefab must have " +
                    "CargoDeliveryTruck attached.",
                    vehicle
                );

                Destroy(vehicle);
                return;
            }

            // Pass the spawner's route before the truck's Start() runs.
            cargoTruck.InitializeRoute(startCell, exitCell);

            Debug.Log(
                $"CARGO TRUCK SPAWNED | Start: {startCell} | " +
                $"Exit: {exitCell}",
                vehicle
            );
        }
        else
        {
            TrafficTestVehicle trafficVehicle =
                vehicle.GetComponent<TrafficTestVehicle>();

            if (trafficVehicle == null)
            {
                Debug.LogError(
                    "TrafficTestSpawner: Traffic Vehicle Prefab must have " +
                    "TrafficTestVehicle attached.",
                    vehicle
                );

                Destroy(vehicle);
                return;
            }

            trafficVehicle.InitializeRoute(startCell, exitCell);

            Debug.Log(
                $"TRAFFIC SPAWNED | Start: {startCell} | Exit: {exitCell}",
                vehicle
            );
        }
    }

    private bool ShouldSpawnCargoTruck()
    {
        switch (spawnMode)
        {
            case TrafficSpawnMode.NormalTrafficOnly:
                return false;

            case TrafficSpawnMode.CargoTrucksOnly:
                return true;

            case TrafficSpawnMode.Both:
                if (randomizeWhenBoth)
                {
                    return Random.value < 0.5f;
                }

                bool cargo = nextSpawnIsCargoTruck;
                nextSpawnIsCargoTruck = !nextSpawnIsCargoTruck;
                return cargo;

            default:
                return false;
        }
    }
}
