using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dispatches garbage collectors from the shared road traffic spawn point.
/// It reserves each WasteDisposal while a collector is assigned to it.
/// </summary>
[DisallowMultipleComponent]
public class GarbageCollectorSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject garbageCollectorPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private RoadNetwork roadNetwork;

    [Header("Dispatch")]
    [Range(0f, 100f)]
    [SerializeField] private float dispatchThresholdPercent = 75f;
    [Min(0.1f)]
    [SerializeField] private float scanInterval = 2f;
    [Min(1)]
    [SerializeField] private int maximumActiveCollectors = 3;
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Diagnostics")]
    [SerializeField] private bool logDispatches = true;

    private readonly HashSet<WasteDisposal> reservedDisposals =
        new HashSet<WasteDisposal>();

    private readonly HashSet<GarbageCollectorController> activeCollectors =
        new HashSet<GarbageCollectorController>();

    private float scanTimer;

    public int ActiveCollectorCount
    {
        get
        {
            activeCollectors.RemoveWhere(collector => collector == null);
            return activeCollectors.Count;
        }
    }

    private void Awake()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();
    }

    private void Start()
    {
        if (garbageCollectorPrefab == null || spawnPoint == null ||
            exitPoint == null || gridManager == null || roadNetwork == null)
        {
            Debug.LogError(
                "GarbageCollectorSpawner: Assign the collector prefab, shared road spawn point, exit point, GridManager and RoadNetwork.",
                this);
            enabled = false;
            return;
        }

        Vector2Int startCell =
            roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(spawnPoint.position));

        Vector2Int exitCell =
            roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(exitPoint.position));

        if (!roadNetwork.IsRoad(startCell) || !roadNetwork.IsRoad(exitCell))
        {
            Debug.LogError(
                "GarbageCollectorSpawner: The shared spawn point and exit point must both be on the road network.",
                this);
            enabled = false;
        }
    }

    private void Update()
    {
        scanTimer += Time.deltaTime;
        if (scanTimer < scanInterval)
            return;

        scanTimer = 0f;

        while (ActiveCollectorCount < Mathf.Max(1, maximumActiveCollectors))
        {
            if (!TrySpawnCollector())
                break;
        }
    }

    private bool TrySpawnCollector()
    {
        Vector2Int startCell =
            roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(spawnPoint.position));

        if (!TryReserveNextDisposal(
                startCell,
                true,
                out WasteDisposal disposal,
                out Vector2Int disposalRoadCell,
                out List<Vector2Int> outboundRoute))
        {
            return false;
        }

        Vector3 spawnPosition =
            roadNetwork.GetRoadCenterWorldPosition(startCell, gridManager);
        spawnPosition.y += spawnHeightOffset;

        GameObject collectorObject = Instantiate(
            garbageCollectorPrefab,
            spawnPosition,
            spawnPoint.rotation);

        GarbageCollectorController collector =
            collectorObject.GetComponent<GarbageCollectorController>();

        if (collector == null)
        {
            Debug.LogError(
                "GarbageCollectorSpawner: GarbageCollectorController must be on the collector prefab root.",
                collectorObject);
            ReleaseDisposal(disposal);
            Destroy(collectorObject);
            return false;
        }

        activeCollectors.Add(collector);

        if (!collector.Initialize(
                this,
                startCell,
                disposal,
                disposalRoadCell,
                outboundRoute))
        {
            activeCollectors.Remove(collector);
            ReleaseDisposal(disposal);
            Destroy(collectorObject);
            return false;
        }

        if (logDispatches)
        {
            Debug.Log(
                $"GARBAGE COLLECTOR DISPATCHED | Target: {disposal.name} | " +
                $"Waste: {disposal.CurrentWaste:F1}/{disposal.MaximumWasteCapacity:F1} | " +
                $"Active collectors: {ActiveCollectorCount}",
                collectorObject);
        }

        return true;
    }

    /// <summary>
    /// Reserves the closest reachable disposal and builds a road route to it.
    /// The initial dispatch can require the configured waste threshold;
    /// collectors already on a route may visit any disposal containing waste.
    /// </summary>
    public bool TryReserveNextDisposal(
        Vector2Int startCell,
        bool requireDispatchThreshold,
        out WasteDisposal selectedDisposal,
        out Vector2Int selectedRoadCell,
        out List<Vector2Int> selectedRoute)
    {
        selectedDisposal = null;
        selectedRoadCell = Vector2Int.zero;
        selectedRoute = null;

        WasteDisposal[] disposals =
            FindObjectsByType<WasteDisposal>(FindObjectsSortMode.None);

        int bestRouteLength = int.MaxValue;
        float bestDistance = float.MaxValue;

        foreach (WasteDisposal disposal in disposals)
        {
            if (disposal == null ||
                reservedDisposals.Contains(disposal) ||
                !disposal.HasWaste ||
                disposal.GarbageCollectorStopPoint == null)
            {
                continue;
            }

            float capacity = disposal.MaximumWasteCapacity;
            float waste = disposal.CurrentWaste;

            if (requireDispatchThreshold)
            {
                if (capacity <= 0f ||
                    waste / capacity * 100f < dispatchThresholdPercent)
                    continue;
            }

            if (!TryBuildDisposalRoute(
                    disposal,
                    startCell,
                    out Vector2Int roadCell,
                    out List<Vector2Int> route))
            {
                continue;
            }

            float distance = Vector3.Distance(
                roadNetwork.GetRoadCenterWorldPosition(roadCell, gridManager),
                disposal.GarbageCollectorStopPoint.position);

            if (route.Count > bestRouteLength ||
                (route.Count == bestRouteLength && distance >= bestDistance))
                continue;

            bestRouteLength = route.Count;
            bestDistance = distance;
            selectedDisposal = disposal;
            selectedRoadCell = roadCell;
            selectedRoute = route;
        }

        if (selectedDisposal == null)
            return false;

        reservedDisposals.Add(selectedDisposal);
        return true;
    }

    private bool TryBuildDisposalRoute(
        WasteDisposal disposal,
        Vector2Int startCell,
        out Vector2Int bestRoadCell,
        out List<Vector2Int> bestRoute)
    {
        bestRoadCell = Vector2Int.zero;
        bestRoute = null;

        Transform stopPoint = disposal.GarbageCollectorStopPoint;
        if (stopPoint == null)
            return false;

        Vector2Int exitCell =
            roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(exitPoint.position));

        List<Vector2Int> roadPositions = roadNetwork.GetRoadPositions();
        float bestDistance = float.MaxValue;
        int bestLength = int.MaxValue;

        foreach (Vector2Int candidate in roadPositions)
        {
            Vector3 roadPosition =
                roadNetwork.GetRoadCenterWorldPosition(candidate, gridManager);

            float distance = Vector3.Distance(roadPosition, stopPoint.position);
            if (distance > disposal.MaximumRoadApproachDistance ||
                distance >= bestDistance)
                continue;

            List<Vector2Int> outbound = roadNetwork.FindPath(startCell, candidate);
            if (outbound == null || outbound.Count == 0)
                continue;

            Vector3 stopForward = stopPoint.forward;
            stopForward.y = 0f;
            Vector3 approachDirection = stopPoint.position - roadPosition;
            approachDirection.y = 0f;

            if (stopForward.sqrMagnitude < 0.0001f ||
                approachDirection.sqrMagnitude < 0.0001f)
                continue;

            stopForward.Normalize();
            approachDirection.Normalize();

            if (Vector3.Dot(approachDirection, stopForward) < 0.95f)
                continue;

            if (outbound.Count >= 2)
            {
                Vector3 previousPosition =
                    roadNetwork.GetRoadCenterWorldPosition(
                        outbound[outbound.Count - 2], gridManager);
                Vector3 travelDirection = roadPosition - previousPosition;
                travelDirection.y = 0f;

                if (travelDirection.sqrMagnitude < 0.0001f)
                    continue;

                travelDirection.Normalize();

                if (Mathf.Abs(Vector3.Dot(travelDirection, stopForward)) > 0.15f)
                    continue;
            }

            List<Vector2Int> returnRoute =
                roadNetwork.FindPath(candidate, exitCell);

            if (returnRoute == null || returnRoute.Count == 0)
                continue;

            if (outbound.Count > bestLength)
                continue;

            bestDistance = distance;
            bestLength = outbound.Count;
            bestRoadCell = candidate;
            bestRoute = outbound;
        }

        return bestRoute != null;
    }

    public bool TryGetExitRoute(
        Vector2Int startCell,
        out List<Vector2Int> exitRoute)
    {
        Vector2Int exitCell =
            roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(exitPoint.position));

        exitRoute = roadNetwork.FindPath(startCell, exitCell);
        return exitRoute != null && exitRoute.Count > 0;
    }

    public void ReleaseDisposal(WasteDisposal disposal)
    {
        if (disposal != null)
            reservedDisposals.Remove(disposal);
    }

    public void NotifyCollectorFinished(GarbageCollectorController collector)
    {
        if (collector == null)
            return;

        activeCollectors.Remove(collector);
        ReleaseDisposal(collector.CurrentDisposal);
    }
}
