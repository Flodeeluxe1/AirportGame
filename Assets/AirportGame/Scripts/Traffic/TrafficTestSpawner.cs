
using UnityEngine;

public class TrafficTestSpawner : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;

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
    [SerializeField] private GameTimeManager gameTimeManager;

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

    private long nextSpawnGameSecond;
    private bool nextSpawnIsCargoTruck;

    private void Awake()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();
    }

    private void Start()
    {
        if (gameTimeManager == null)
        {
            LogError(
                "TrafficTestSpawner: GameTimeManager is missing; spawning requires in-game time.",
                this);
            enabled = false;
            return;
        }

        nextSpawnGameSecond =
            GetAbsoluteGameTimeSeconds() + Mathf.CeilToInt(spawnInterval);
    }

    private void Update()
    {
        if (gameTimeManager == null)
            return;

        long currentGameSecond = GetAbsoluteGameTimeSeconds();
        if (currentGameSecond < nextSpawnGameSecond)
            return;

        nextSpawnGameSecond =
            currentGameSecond + Mathf.CeilToInt(spawnInterval);

        SpawnVehicle();
    }

    private long GetAbsoluteGameTimeSeconds()
    {
        return
            (gameTimeManager.CurrentDay - 1) * 86400L +
            gameTimeManager.GetGameTimeSeconds();
    }

    private void SpawnVehicle()
    {
        if (gridManager == null)
        {
            LogError(
                "TrafficTestSpawner: GridManager is missing.",
                this
            );
            return;
        }

        if (exitPoint == null)
        {
            LogError(
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
            LogError(
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
            LogError(
                "TrafficTestSpawner: Vehicle prefab must have " +
                "VehicleSpawnID attached.",
                vehicle
            );

            Destroy(vehicle);
            return;
        }

        spawnID.InitializeID();

        Log(
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
                LogError(
                    "TrafficTestSpawner: Cargo Truck Prefab must have " +
                    "CargoDeliveryTruck attached.",
                    vehicle
                );

                Destroy(vehicle);
                return;
            }

            if (!cargoTruck.AlignColliderBottomToGround(
                    spawnPosition.y - spawnHeightOffset))
            {
                LogWarning(
                    "TrafficTestSpawner: Cargo truck has no enabled " +
                    "BoxCollider; leaving its spawn height unchanged.",
                    vehicle);
            }

            // Pass the spawner's route before the truck's Start() runs.
            cargoTruck.InitializeRoute(startCell, exitCell);

            Log(
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
                LogError(
                    "TrafficTestSpawner: Traffic Vehicle Prefab must have " +
                    "TrafficTestVehicle attached.",
                    vehicle
                );

                Destroy(vehicle);
                return;
            }

            if (!trafficVehicle.AlignColliderBottomToGround(
                    spawnPosition.y - spawnHeightOffset))
            {
                LogWarning(
                    "TrafficTestSpawner: Traffic vehicle has no enabled " +
                    "BoxCollider; leaving its spawn height unchanged.",
                    vehicle);
            }

            trafficVehicle.InitializeRoute(startCell, exitCell);

            Log(
                $"TRAFFIC SPAWNED | Start: {startCell} | Exit: {exitCell}",
                vehicle
            );
        }
    }

    /// <summary>
    /// Spawns an outbound passenger car. It parks for the requested duration
    /// when a space is immediately free; otherwise it stops at the supplied
    /// roadside point for the short fallback duration.
    /// </summary>
    public TrafficTestVehicle SpawnPassengerArrivalVehicle(
        GameObject vehiclePrefab,
        Vector3 roadsideStopPosition,
        float parkingDurationSeconds,
        float roadsideDurationSeconds)
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (gridManager == null || exitPoint == null || vehiclePrefab == null)
        {
            LogError(
                "TrafficTestSpawner: Passenger arrival vehicle requires GridManager, Exit Point and vehicle prefab.",
                this);
            return null;
        }

        Vector2Int startCell = gridManager.WorldToGrid(transform.position);
        Vector2Int exitCell = gridManager.WorldToGrid(exitPoint.position);
        Vector2Int roadsideCell = gridManager.WorldToGrid(roadsideStopPosition);
        Vector3 spawnPosition = gridManager.GridToWorldCenter(startCell);
        spawnPosition.y += spawnHeightOffset;

        GameObject vehicleObject = Instantiate(
            vehiclePrefab, spawnPosition, transform.rotation);
        VehicleSpawnID spawnID = vehicleObject.GetComponent<VehicleSpawnID>();
        TrafficTestVehicle trafficVehicle =
            vehicleObject.GetComponent<TrafficTestVehicle>();

        if (spawnID == null || trafficVehicle == null)
        {
            LogError(
                "TrafficTestSpawner: Passenger car prefab must contain VehicleSpawnID and TrafficTestVehicle.",
                vehicleObject);
            Destroy(vehicleObject);
            return null;
        }

        spawnID.InitializeID();
        trafficVehicle.AlignColliderBottomToGround(
            spawnPosition.y - spawnHeightOffset);
        trafficVehicle.InitializePassengerArrivalRoute(
            startCell, exitCell, roadsideCell,
            parkingDurationSeconds, roadsideDurationSeconds);

        if (trafficVehicle.CurrentState == TrafficTestVehicle.VehicleState.Idle)
        {
            LogWarning(
                "TrafficTestSpawner: Passenger car could not find a route to parking or roadside drop-off.",
                vehicleObject);
            Destroy(vehicleObject);
            return null;
        }

        Log(
            $"PASSENGER ARRIVAL CAR SPAWNED | ID: {spawnID.SpawnID} | Start: {startCell} | Roadside fallback: {roadsideCell}",
            vehicleObject);
        return trafficVehicle;
    }

    /// <summary>
    /// Spawns a traffic vehicle using this spawner's road entry and exit.
    /// Used by systems that need to control when a parked vehicle departs.
    /// </summary>
    public TrafficTestVehicle SpawnExternallyControlledVehicle(
        GameObject vehiclePrefab)
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (gridManager == null || exitPoint == null || vehiclePrefab == null)
        {
            LogError(
                "TrafficTestSpawner: Cannot spawn externally controlled vehicle. " +
                "GridManager, Exit Point and vehicle prefab are required.",
                this);
            return null;
        }

        Vector2Int startCell =
            gridManager.WorldToGrid(transform.position);

        Vector2Int exitCell =
            gridManager.WorldToGrid(exitPoint.position);

        Vector3 spawnPosition =
            gridManager.GridToWorldCenter(startCell);

        spawnPosition.y += spawnHeightOffset;

        GameObject vehicleObject = Instantiate(
            vehiclePrefab,
            spawnPosition,
            transform.rotation);

        VehicleSpawnID spawnID =
            vehicleObject.GetComponent<VehicleSpawnID>();

        TrafficTestVehicle trafficVehicle =
            vehicleObject.GetComponent<TrafficTestVehicle>();

        if (spawnID == null || trafficVehicle == null)
        {
            LogError(
                "TrafficTestSpawner: Externally controlled vehicle prefab " +
                "must contain VehicleSpawnID and TrafficTestVehicle.",
                vehicleObject);

            Destroy(vehicleObject);
            return null;
        }

        spawnID.InitializeID();

        if (!trafficVehicle.AlignColliderBottomToGround(
                spawnPosition.y - spawnHeightOffset))
        {
            LogWarning(
                "TrafficTestSpawner: Externally controlled vehicle has no " +
                "enabled BoxCollider; leaving its spawn height unchanged.",
                vehicleObject);
        }

        trafficVehicle.InitializeRoute(startCell, exitCell, true);

        Log(
            $"EXTERNALLY CONTROLLED TRAFFIC SPAWNED | " +
            $"ID: {spawnID.SpawnID} | Start: {startCell} | Exit: {exitCell}",
            vehicleObject);

        return trafficVehicle;
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

}
