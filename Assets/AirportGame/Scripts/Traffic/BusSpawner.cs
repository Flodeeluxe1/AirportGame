using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dispatches buses from the shared RoadTrafficSpawner entry, visits the
/// currently placed bus stops in shortest-road-distance order, then exits.
/// </summary>
[DisallowMultipleComponent]
public class BusSpawner : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;

    [Header("References")]
    [SerializeField] private GameObject busPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private PublicTransportManager publicTransportManager;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Spawner")]
    [Min(1)] [SerializeField] private int maximumActiveBuses = 3;
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Diagnostics")]
    [SerializeField] private bool logDispatches = true;
    [SerializeField] private bool logDispatchDiagnostics = true;

    private readonly HashSet<BusController> activeBuses =
        new HashSet<BusController>();
    private long nextDispatchGameSecond;

    public int ActiveBusCount
    {
        get
        {
            activeBuses.RemoveWhere(bus => bus == null);
            return activeBuses.Count;
        }
    }

    private void Awake()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (roadNetwork == null)
            roadNetwork = FindAnyObjectByType<RoadNetwork>();
        if (publicTransportManager == null)
            publicTransportManager = FindAnyObjectByType<PublicTransportManager>();
        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();
    }

    private void Start()
    {
        if (busPrefab == null || spawnPoint == null || exitPoint == null ||
            gridManager == null || roadNetwork == null ||
            publicTransportManager == null || gameTimeManager == null)
        {
            LogError(
                "BusSpawner: Assign the bus prefab, shared road spawn point, exit point, GridManager, RoadNetwork, PublicTransportManager and GameTimeManager.",
                this);
            enabled = false;
            return;
        }

        nextDispatchGameSecond = GetAbsoluteGameTimeSeconds();
    }

    private void Update()
    {
        if (gameTimeManager == null || publicTransportManager == null)
            return;

        int fleetLimit = Mathf.Min(
            maximumActiveBuses, publicTransportManager.ActiveBuses);

        if (fleetLimit <= 0 || ActiveBusCount >= fleetLimit)
            return;

        long now = GetAbsoluteGameTimeSeconds();
        if (now < nextDispatchGameSecond)
            return;

        if (TryDispatchBus())
        {
            long interval = Mathf.Max(
                1L,
                Mathf.CeilToInt(publicTransportManager.BusFrequencyMinutes * 60f));
            nextDispatchGameSecond = now + interval;
        }
        else
        {
            // Retry soon if there are temporarily no valid stops or roads.
            nextDispatchGameSecond = now + 10L;
        }
    }

    public bool TryDispatchBus()
    {
        if (!isActiveAndEnabled || busPrefab == null || spawnPoint == null ||
            exitPoint == null || gridManager == null || roadNetwork == null ||
            publicTransportManager == null)
        {
            LogDispatchFailure("Missing reference or spawner is disabled.");
            return false;
        }

        Vector2Int startCell = roadNetwork.GetRoadAnchor(
            gridManager.WorldToGrid(spawnPoint.position));
        Vector2Int exitCell = roadNetwork.GetRoadAnchor(
            gridManager.WorldToGrid(exitPoint.position));

        if (!roadNetwork.IsRoad(startCell) || !roadNetwork.IsRoad(exitCell))
        {
            LogDispatchFailure(
                $"Spawn or exit point is not on a registered road. Start: {startCell}; Exit: {exitCell}.");
            return false;
        }

        if (!TryBuildJourney(startCell, exitCell,
                out List<Vector2Int> journey,
                out List<int> stopIndices))
        {
            LogDispatchFailure("No reachable bus stops and exit were found.");
            return false;
        }

        Vector3 spawnPosition =
            roadNetwork.GetRoadCenterWorldPosition(startCell, gridManager);
        spawnPosition.y += spawnHeightOffset;

        GameObject busObject = Instantiate(
            busPrefab, spawnPosition, spawnPoint.rotation);

        BusController bus = busObject.GetComponent<BusController>();
        VehicleSpawnID spawnID = busObject.GetComponent<VehicleSpawnID>();

        if (bus == null || spawnID == null)
        {
            LogError(
                "BusSpawner: Bus prefab root must contain BusController and VehicleSpawnID.",
                busObject);
            Destroy(busObject);
            return false;
        }

        spawnID.InitializeID();

        if (!bus.AlignColliderBottomToGround(
                spawnPosition.y - spawnHeightOffset))
        {
            LogWarning(
                "BusSpawner: Bus has no enabled BoxCollider; leaving its spawn height unchanged.",
                busObject);
        }

        activeBuses.Add(bus);
        if (!bus.InitializeJourney(journey, stopIndices, this))
        {
            activeBuses.Remove(bus);
            LogDispatchFailure("BusController rejected the generated journey.");
            Destroy(busObject);
            return false;
        }

        if (logDispatches)
            Log($"BUS DISPATCHED | ID: {spawnID.SpawnID} | Stops: {stopIndices.Count} | Road cells: {journey.Count}", busObject);

        return true;
    }

    private bool TryBuildJourney(
        Vector2Int startCell,
        Vector2Int exitCell,
        out List<Vector2Int> journey,
        out List<int> stopIndices)
    {
        journey = new List<Vector2Int>();
        stopIndices = new List<int>();

        List<Vector2Int> roadPositions = roadNetwork.GetRoadPositions();
        List<PlacedBuilding> remainingStops =
            new List<PlacedBuilding>();

        foreach (PlacedBuilding stop in publicTransportManager.RegisteredStops)
        {
            if (stop != null && stop.isActiveAndEnabled)
                remainingStops.Add(stop);
        }

        if (remainingStops.Count == 0)
            return false;

        Vector2Int currentCell = startCell;
        journey.Add(currentCell);

        while (remainingStops.Count > 0)
        {
            PlacedBuilding bestStop = null;
            List<Vector2Int> bestPath = null;
            int bestLength = int.MaxValue;

            for (int i = 0; i < remainingStops.Count; i++)
            {
                Vector2Int stopRoadCell = FindNearestRoadCell(
                    remainingStops[i].transform.position, roadPositions);

                List<Vector2Int> candidate =
                    roadNetwork.FindPath(currentCell, stopRoadCell);

                if (candidate == null || candidate.Count == 0 ||
                    candidate.Count >= bestLength)
                    continue;

                bestLength = candidate.Count;
                bestStop = remainingStops[i];
                bestPath = candidate;
            }

            if (bestStop == null || bestPath == null)
                break;

            AppendPath(journey, bestPath);
            stopIndices.Add(journey.Count - 1);
            currentCell = bestPath[bestPath.Count - 1];
            remainingStops.Remove(bestStop);
        }

        if (stopIndices.Count == 0)
            return false;

        List<Vector2Int> exitPath =
            roadNetwork.FindPath(currentCell, exitCell);

        if (exitPath == null || exitPath.Count == 0)
            return false;

        AppendPath(journey, exitPath);
        return journey.Count >= 2;
    }

    private Vector2Int FindNearestRoadCell(
        Vector3 worldPosition,
        List<Vector2Int> roadPositions)
    {
        Vector2Int bestCell = Vector2Int.zero;
        float bestDistanceSqr = float.PositiveInfinity;

        for (int i = 0; i < roadPositions.Count; i++)
        {
            Vector3 roadPosition = roadNetwork.GetRoadCenterWorldPosition(
                roadPositions[i], gridManager);
            float distanceSqr =
                (roadPosition - worldPosition).sqrMagnitude;

            if (distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            bestCell = roadPositions[i];
        }

        return bestCell;
    }

    private static void AppendPath(
        List<Vector2Int> destination,
        List<Vector2Int> segment)
    {
        int startIndex = destination.Count > 0 &&
                         segment.Count > 0 &&
                         destination[destination.Count - 1] == segment[0]
            ? 1 : 0;

        for (int i = startIndex; i < segment.Count; i++)
            destination.Add(segment[i]);
    }

    public void NotifyBusExited(BusController bus)
    {
        if (bus != null)
            activeBuses.Remove(bus);
    }

    private long GetAbsoluteGameTimeSeconds()
    {
        return (gameTimeManager.CurrentDay - 1) * 86400L +
               gameTimeManager.GetGameTimeSeconds();
    }

    private void LogDispatchFailure(string reason)
    {
        if (logDispatchDiagnostics)
            LogWarning($"BUS NOT DISPATCHED | {reason}", this);
    }

    private void Log(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.Log(message, context);
        else Debug.Log(message);
    }

    private void LogWarning(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogWarning(message, context);
        else Debug.LogWarning(message);
    }

    private void LogError(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogError(message, context);
        else Debug.LogError(message);
    }
}
