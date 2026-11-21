
using UnityEngine;

public class AircraftTestSpawner : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
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
                FindAnyObjectByType<GridManager>();
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

        Log(
            $"AIRCRAFT SPAWNED | " +
            $"Name: {aircraft.name} | " +
            $"Cell: {spawnCell}"
        );
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}