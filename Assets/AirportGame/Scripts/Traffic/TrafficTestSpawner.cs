using UnityEngine;

public class TrafficTestSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject trafficVehiclePrefab;
    [SerializeField] private GridManager gridManager;

    [Header("Spawning")]
    [SerializeField] private float spawnInterval = 10f;
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Exit")]
    [SerializeField] private Transform exitPoint;

    private float spawnTimer;

    private void Awake()
    {
        if (gridManager == null)
        {
            gridManager =
                FindFirstObjectByType<GridManager>();
        }
    }

    private void Start()
    {
        spawnTimer =
            spawnInterval;
    }

    private void Update()
    {
        spawnTimer -=
            Time.deltaTime;

        if (spawnTimer > 0f)
            return;

        spawnTimer =
            spawnInterval;

        SpawnVehicle();
    }

    private void SpawnVehicle()
    {
        if (trafficVehiclePrefab == null)
        {
            Debug.LogError(
                "TrafficTestSpawner: " +
                "Traffic Vehicle Prefab is missing."
            );

            return;
        }

        if (gridManager == null)
        {
            Debug.LogError(
                "TrafficTestSpawner: " +
                "GridManager is missing."
            );

            return;
        }

        if (exitPoint == null)
        {
            Debug.LogError(
                "TrafficTestSpawner: " +
                "Exit Point is missing."
            );

            return;
        }

        Vector2Int startCell =
            gridManager.WorldToGrid(
                transform.position
            );

        Vector2Int exitCell =
            gridManager.WorldToGrid(
                exitPoint.position
            );

        Vector3 spawnPosition =
            gridManager.GridToWorldCenter(
                startCell
            );

        spawnPosition.y +=
            spawnHeightOffset;

        GameObject vehicle =
            Instantiate(
                trafficVehiclePrefab,
                spawnPosition,
                transform.rotation
            );

        TrafficTestVehicle trafficVehicle =
            vehicle.GetComponent<TrafficTestVehicle>();

        if (trafficVehicle == null)
        {
            Debug.LogError(
                "TrafficTestSpawner: " +
                "Spawned prefab does not have " +
                "TrafficTestVehicle attached."
            );

            Destroy(vehicle);

            return;
        }

        trafficVehicle.InitializeRoute(
            startCell,
            exitCell
        );

        Debug.Log(
            $"TRAFFIC SPAWNED | " +
            $"Start: {startCell} | " +
            $"Exit: {exitCell}"
        );
    }
}