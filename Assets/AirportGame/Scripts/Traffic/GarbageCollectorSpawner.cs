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

    [Header("Spawner")]
    [Min(1)]
    [SerializeField] private int maximumActiveCollectors = 3;
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Diagnostics")]
    [SerializeField] private bool logDispatches = true;
    [SerializeField] private bool logDispatchDiagnostics = true;

    private readonly HashSet<WasteDisposal> reservedDisposals =
        new HashSet<WasteDisposal>();

    private readonly HashSet<GarbageCollectorController> activeCollectors =
        new HashSet<GarbageCollectorController>();

    public event System.Action CollectorAvailabilityChanged;

    public int MaximumActiveCollectors => Mathf.Max(1, maximumActiveCollectors);

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
        }
    }

    public bool TryDispatchCollector(float thresholdPercent)
    {
        if (!isActiveAndEnabled || garbageCollectorPrefab == null ||
            spawnPoint == null || exitPoint == null ||
            gridManager == null || roadNetwork == null)
        {
            LogDispatchFailure("Missing reference or spawner is disabled. Check prefab, spawn/exit points, GridManager and RoadNetwork.");
            return false;
        }

        if (ActiveCollectorCount >= MaximumActiveCollectors)
        {
            LogDispatchFailure($"Active collector limit reached ({ActiveCollectorCount}/{MaximumActiveCollectors}).");
            return false;
        }

        Vector2Int startCell =
            roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(spawnPoint.position));
        Vector2Int exitCell =
            roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(exitPoint.position));

        if (!roadNetwork.IsRoad(startCell))
        {
            LogDispatchFailure($"Spawn point is not on a registered road cell. Cell: {startCell}.");
            return false;
        }

        if (!roadNetwork.IsRoad(exitCell))
        {
            LogDispatchFailure($"Exit point is not on a registered road cell. Cell: {exitCell}.");
            return false;
        }

        if (!TryReserveNextDisposal(
                startCell,
                true,
                thresholdPercent,
                out WasteDisposal disposal,
                out Vector2Int disposalRoadCell,
                out List<Vector2Int> outboundRoute))
        {
            LogDispatchFailure("No disposal passed the waste, reservation and route checks. See per-disposal diagnostics above.");
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
            LogDispatchFailure($"Collector prefab instantiated, but Initialize() rejected the assignment for '{disposal.name}'.");
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

    private void LogDispatchFailure(string reason)
    {
        if (logDispatchDiagnostics)
            Debug.LogWarning($"GARBAGE COLLECTOR NOT DISPATCHED | {reason}", this);
    }

    public bool TryReserveNextDisposal(
        Vector2Int startCell,
        bool requireDispatchThreshold,
        float thresholdPercent,
        out WasteDisposal selectedDisposal,
        out Vector2Int selectedRoadCell,
        out List<Vector2Int> selectedRoute)
    {
        selectedDisposal = null;
        selectedRoadCell = Vector2Int.zero;
        selectedRoute = null;
        reservedDisposals.RemoveWhere(disposal => disposal == null);

        int bestRouteLength = int.MaxValue;
        float bestDistance = float.MaxValue;
        int inactiveCount = 0;
        int reservedCount = 0;
        int emptyCount = 0;
        int missingStopCount = 0;
        int belowThresholdCount = 0;
        int routeFailureCount = 0;

        foreach (WasteDisposal disposal in WasteDisposal.ActiveDisposals)
        {
            if (disposal == null || !disposal.gameObject.activeInHierarchy)
            {
                inactiveCount++;
                continue;
            }

            if (reservedDisposals.Contains(disposal))
            {
                reservedCount++;
                LogDisposalSkip(disposal, "already reserved by another collector");
                continue;
            }

            if (!disposal.HasWaste)
            {
                emptyCount++;
                LogDisposalSkip(disposal, "contains no waste");
                continue;
            }

            if (disposal.GarbageCollectorStopPoint == null)
            {
                missingStopCount++;
                LogDisposalSkip(disposal, "GarbageCollectorStopPoint was not found/assigned");
                continue;
            }

            float capacity = disposal.MaximumWasteCapacity;
            float waste = disposal.CurrentWaste;
            float fillPercent = capacity > 0f ? waste / capacity * 100f : 0f;

            if (requireDispatchThreshold &&
                (capacity <= 0f || fillPercent < thresholdPercent))
            {
                belowThresholdCount++;
                LogDisposalSkip(
                    disposal,
                    $"below dispatch threshold ({waste:F1}/{capacity:F1} = {fillPercent:F1}%, needs {thresholdPercent:F1}%)");
                continue;
            }

            if (!TryBuildDisposalRoute(
                    disposal,
                    startCell,
                    out Vector2Int roadCell,
                    out List<Vector2Int> route,
                    out string routeFailure))
            {
                routeFailureCount++;
                LogDisposalSkip(disposal, routeFailure);
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

        if (selectedDisposal == null && logDispatchDiagnostics)
        {
            Debug.LogWarning(
                $"GARBAGE DISPATCH SEARCH SUMMARY | Found disposals: {disposals.Length} | " +
                $"Inactive: {inactiveCount} | Reserved: {reservedCount} | Empty: {emptyCount} | " +
                $"Missing stop point: {missingStopCount} | Below threshold: {belowThresholdCount} | " +
                $"Route rejected: {routeFailureCount}.",
                this);
            return false;
        }

        if (selectedDisposal == null)
            return false;

        reservedDisposals.Add(selectedDisposal);
        if (logDispatchDiagnostics)
        {
            Debug.Log(
                $"GARBAGE DISPOSAL SELECTED | {selectedDisposal.name} | " +
                $"Waste: {selectedDisposal.CurrentWaste:F1}/{selectedDisposal.MaximumWasteCapacity:F1} | " +
                $"Road approach cell: {selectedRoadCell} | Route cells: {selectedRoute.Count}",
                selectedDisposal);
        }

        return true;
    }

    private void LogDisposalSkip(WasteDisposal disposal, string reason)
    {
        if (logDispatchDiagnostics)
            Debug.Log($"GARBAGE DISPOSAL SKIPPED | {disposal.name} | {reason}", disposal);
    }

    private bool TryBuildDisposalRoute(
        WasteDisposal disposal,
        Vector2Int startCell,
        out Vector2Int bestRoadCell,
        out List<Vector2Int> bestRoute,
        out string failureReason)
    {
        bestRoadCell = Vector2Int.zero;
        bestRoute = null;
        failureReason = "No suitable road approach found.";

        Transform stopPoint = disposal.GarbageCollectorStopPoint;
        if (stopPoint == null)
        {
            failureReason = "GarbageCollectorStopPoint is missing.";
            return false;
        }

        Vector2Int exitCell =
            roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(exitPoint.position));

        if (!roadNetwork.IsRoad(startCell))
        {
            failureReason = $"Spawn cell {startCell} is not a road.";
            return false;
        }

        if (!roadNetwork.IsRoad(exitCell))
        {
            failureReason = $"Exit cell {exitCell} is not a road.";
            return false;
        }

        List<Vector2Int> roadPositions = roadNetwork.GetRoadPositions();
        float bestDistance = float.MaxValue;
        int bestLength = int.MaxValue;

        int withinDistanceCount = 0;
        int outboundRouteCount = 0;
        int validReturnRouteCount = 0;

        foreach (Vector2Int candidate in roadPositions)
        {
            Vector3 roadPosition =
                roadNetwork.GetRoadCenterWorldPosition(candidate, gridManager);

            float distance = Vector3.Distance(roadPosition, stopPoint.position);
            if (distance > disposal.MaximumRoadApproachDistance)
                continue;
            withinDistanceCount++;

            // Select a reachable road cell near the stop point. Do not require
            // the road's final travel direction to align with the parking point:
            // the collector drives to the stop point and faces its forward axis
            // once it arrives.
            List<Vector2Int> outbound = roadNetwork.FindPath(startCell, candidate);
            if (outbound == null || outbound.Count == 0)
                continue;
            outboundRouteCount++;

            List<Vector2Int> returnRoute =
                roadNetwork.FindPath(candidate, exitCell);

            if (returnRoute == null || returnRoute.Count == 0)
                continue;
            validReturnRouteCount++;

            if (outbound.Count > bestLength ||
                (outbound.Count == bestLength && distance >= bestDistance))
                continue;

            bestDistance = distance;
            bestLength = outbound.Count;
            bestRoadCell = candidate;
            bestRoute = outbound;
        }

        if (bestRoute != null)
            return true;

        if (withinDistanceCount == 0)
            failureReason = $"No road cell within {disposal.MaximumRoadApproachDistance:F1}m of the stop point.";
        else if (outboundRouteCount == 0)
            failureReason = $"Road cells are nearby ({withinDistanceCount}), but none has a route from spawn cell {startCell}.";
        else if (validReturnRouteCount == 0)
            failureReason = $"Reachable approach cells ({outboundRouteCount}) exist, but none has a return route to exit cell {exitCell}.";
        else
            failureReason = "A candidate route passed checks but no route was selected.";

        failureReason +=
            $" Candidate counts: nearby={withinDistanceCount}, outbound={outboundRouteCount}, " +
            $"return={validReturnRouteCount}.";

        return false;
    }

    public bool TryGetExitRoute(
        Vector2Int startCell,
        out List<Vector2Int> exitRoute)
    {
        Vector2Int exitCell =
            roadNetwork.GetRoadAnchor(gridManager.WorldToGrid(exitPoint.position));

        if (!roadNetwork.IsRoad(startCell) || !roadNetwork.IsRoad(exitCell))
        {
            exitRoute = null;
            return false;
        }

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
        CollectorAvailabilityChanged?.Invoke();
    }
}