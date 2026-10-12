
using UnityEngine;

public class AircraftTestSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject aircraftPrefab;
    [SerializeField] private GridManager gridManager;

    [Header("Spawning")]
    [Min(0.1f)]
    [SerializeField] private float spawnInterval = 10f;

    [SerializeField] private float spawnHeightOffset = 0f;

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
        // Spawn the first aircraft after one interval,
        // matching the existing traffic spawner.
        spawnTimer = spawnInterval;
    }

    private void Update()
    {
        if (aircraftPrefab == null ||
            gridManager == null)
        {
            return;
        }

        spawnTimer -= Time.deltaTime;

        if (spawnTimer > 0f)
            return;

        spawnTimer = spawnInterval;

        SpawnAircraft();
    }

    private void SpawnAircraft()
    {
        Vector2Int spawnCell =
            gridManager.WorldToGrid(
                transform.position
            );

        Vector3 spawnPosition =
            gridManager.GridToWorldCenter(
                spawnCell
            );

        spawnPosition.y += spawnHeightOffset;

        GameObject aircraft =
            Instantiate(
                aircraftPrefab,
                spawnPosition,
                transform.rotation
            );

        Debug.Log(
            $"AIRCRAFT SPAWNED | " +
            $"Name: {aircraft.name} | " +
            $"Cell: {spawnCell}"
        );
    }
}