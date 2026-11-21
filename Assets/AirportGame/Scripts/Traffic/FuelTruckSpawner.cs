using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dispatches fuel tankers to the best available Fuel Delivery Station.
/// </summary>
public class FuelTruckSpawner : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    [Header("References")]
    [SerializeField] private GameObject fuelTruckPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Fuel Delivery")]
    [Min(1f)]
    [SerializeField] private float tankerCapacityLitres = 20000f;
    [Min(0.01f)]
    [SerializeField] private float fuelPricePerLitre = 1f;

    [Header("Spawner")]
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Delivery Recovery")]
    [Min(0f)]
    [SerializeField] private float retryCooldownAfterFailureSeconds = 30f;

    [Header("Diagnostics")]
    [SerializeField] private bool logDispatches = true;

    private readonly HashSet<PipelineNetworkManager.PipelineNetwork> activeNetworks =
        new HashSet<PipelineNetworkManager.PipelineNetwork>();

    private readonly HashSet<FuelTruckController> activeTankers =
        new HashSet<FuelTruckController>();

    public int ActiveTankerCount
    {
        get
        {
            int count = 0;
            foreach (FuelTruckController truck in activeTankers)
            {
                if (truck != null)
                    count++;
            }
            return count;
        }
    }

    public IReadOnlyCollection<FuelTruckController> ActiveTankers => activeTankers;

    public bool HasActiveDelivery(PipelineNetworkManager.PipelineNetwork network)
    {
        return network != null && activeNetworks.Contains(network);
    }

    private readonly Dictionary<PipelineNetworkManager.PipelineNetwork, long>
        networkRetryTimes =
            new Dictionary<PipelineNetworkManager.PipelineNetwork, long>();

    private RoadNetwork cachedRouteRoadNetwork;
    private int cachedRoadTopologyVersion = -1;
    private Vector2Int cachedRouteStartCell;
    private Vector2Int cachedRouteExitCell;
    private List<Vector2Int> cachedRoadPositions;
    private RoadNetwork.RoadPathTree cachedOutboundTree;
    private RoadNetwork.RoadPathTree cachedReturnTree;

    private class StationRoute
    {
        public FuelDeliveryStation Station;
        public Vector2Int RoadCell;
        public List<Vector2Int> Outbound;
        public List<Vector2Int> Returning;
    }

    private void Awake()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (roadNetwork == null)
            roadNetwork = FindAnyObjectByType<RoadNetwork>();

        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();
    }

    private void Start()
    {
        if (fuelTruckPrefab == null || spawnPoint == null || exitPoint == null ||
            gridManager == null || roadNetwork == null || gameTimeManager == null)
        {
            LogError(
                "FuelTruckSpawner: Assign the tanker prefab, spawn point, exit point, GridManager and RoadNetwork.",
                this);
            enabled = false;
            return;
        }

    }

    public bool TryDispatchDelivery(
        PipelineNetworkManager.PipelineNetwork network,
        PipelineNetworkManager networkManager,
        float refillThresholdPercent)
    {
        if (network == null || networkManager == null ||
            !networkManager.IsNetworkCurrent(network) ||
            network.CapacityLitres <= 0f ||
            network.RemainingCapacityLitres <= 0f ||
            activeNetworks.Contains(network))
            return false;

        if (networkRetryTimes.TryGetValue(network, out long retryGameSecond))
        {
            if (GetAbsoluteGameTimeSeconds() < retryGameSecond)
                return false;

            networkRetryTimes.Remove(network);
        }

        float currentFuelPercentage =
            network.CurrentFuelLitres / network.CapacityLitres * 100f;

        // Another source may have replenished this network since the last check.
        if (currentFuelPercentage > refillThresholdPercent)
            return false;

        StationRoute selectedRoute = SelectDeliveryStation(network);
        if (selectedRoute == null)
        {
            RegisterFailedDispatch(network);
            return false;
        }

        float amount = Mathf.Min(tankerCapacityLitres, network.RemainingCapacityLitres);
        if (amount <= 0f)
            return false;

        Vector3 spawnPosition =
            roadNetwork.GetRoadCenterWorldPosition(selectedRoute.Outbound[0], gridManager);
        spawnPosition.y += spawnHeightOffset;

        GameObject truckObject = Instantiate(
            fuelTruckPrefab, spawnPosition, spawnPoint.rotation);
        FuelTruckController truck = truckObject.GetComponent<FuelTruckController>();

        if (truck == null)
        {
            LogError(
                "FuelTruckSpawner: FuelTruckController must be on the tanker prefab root.",
                truckObject);
            Destroy(truckObject);
            RegisterFailedDispatch(network);
            return false;
        }

        if (!truck.AlignColliderBottomToGround(
                spawnPosition.y - spawnHeightOffset))
        {
            LogWarning(
                "FuelTruckSpawner: Fuel truck has no enabled BoxCollider; " +
                "leaving its spawn height unchanged.",
                truckObject);
        }

        selectedRoute.Station.RegisterTruck(truck);
        activeNetworks.Add(network);
        activeTankers.Add(truck);

        truck.InitializeDelivery(
            selectedRoute.Outbound,
            selectedRoute.Returning,
            selectedRoute.Station,
            network,
            amount,
            fuelPricePerLitre,
            this,
            networkManager,
            refillThresholdPercent,
            networkManager.TopologyVersion);

        if (logDispatches)
        {
            Log(
                $"FUEL TANKER DISPATCHED | Pipeline reserve: " +
                $"{network.CurrentFuelLitres:F0}/{network.CapacityLitres:F0} L | " +
                $"Delivery station: {selectedRoute.Station.name} | " +
                $"Queued there: {selectedRoute.Station.QueuedVehicleCount}",
                truckObject);
        }

        return true;
    }

    private StationRoute SelectDeliveryStation(
        PipelineNetworkManager.PipelineNetwork targetNetwork)
    {
        Vector2Int startCell = roadNetwork.GetRoadAnchor(
            gridManager.WorldToGrid(spawnPoint.position));
        Vector2Int exitCell = roadNetwork.GetRoadAnchor(
            gridManager.WorldToGrid(exitPoint.position));

        if (!roadNetwork.IsRoad(startCell) || !roadNetwork.IsRoad(exitCell))
            return null;

        FuelDeliveryStation[] stations = FindObjectsByType<FuelDeliveryStation>();
        List<FuelDeliveryStation> matchingStations = new List<FuelDeliveryStation>();

        foreach (FuelDeliveryStation station in stations)
        {
            if (station == null || station.FuelTruckStopPoint == null ||
                station.FuelNetwork != targetNetwork)
                continue;

            // Prune destroyed queue entries once before candidate selection.
            station.RefreshQueueState();
            matchingStations.Add(station);
        }

        if (matchingStations.Count == 0)
            return null;

        // The spawn and exit stay fixed between deliveries. Reuse the
        // route trees until the road layout or either endpoint changes.
        if (cachedRouteRoadNetwork != roadNetwork ||
            cachedRoadTopologyVersion != roadNetwork.TopologyVersion ||
            cachedRouteStartCell != startCell ||
            cachedRouteExitCell != exitCell ||
            cachedRoadPositions == null)
        {
            cachedRoadPositions = roadNetwork.GetRoadPositions();
            cachedOutboundTree = roadNetwork.BuildForwardPathTree(startCell);
            cachedReturnTree = roadNetwork.BuildReversePathTree(exitCell);
            cachedRouteRoadNetwork = roadNetwork;
            cachedRoadTopologyVersion = roadNetwork.TopologyVersion;
            cachedRouteStartCell = startCell;
            cachedRouteExitCell = exitCell;
        }

        List<StationRoute> candidates = new List<StationRoute>();
        foreach (FuelDeliveryStation station in matchingStations)
        {
            if (!TryBuildStationRoute(
                    station, cachedRoadPositions, cachedOutboundTree, cachedReturnTree,
                    out StationRoute route))
                continue;

            candidates.Add(route);
        }

        if (candidates.Count == 0)
            return null;

        // Any unoccupied station wins over every occupied station.
        // Within the same occupancy group, choose the shortest queue.
        candidates.Sort((a, b) =>
        {
            int occupiedComparison =
                a.Station.IsOccupied.CompareTo(b.Station.IsOccupied);

            if (occupiedComparison != 0)
                return occupiedComparison;

            int queueComparison =
                a.Station.QueuedVehicleCount.CompareTo(b.Station.QueuedVehicleCount);

            if (queueComparison != 0)
                return queueComparison;

            return Vector3.Distance(
                a.Station.FuelTruckStopPoint.position,
                spawnPoint.position).CompareTo(
                Vector3.Distance(
                    b.Station.FuelTruckStopPoint.position,
                    spawnPoint.position));
        });

        return candidates[0];
    }

    private bool TryBuildStationRoute(
        FuelDeliveryStation station,
        List<Vector2Int> roadPositions,
        RoadNetwork.RoadPathTree outboundTree,
        RoadNetwork.RoadPathTree returnTree,
        out StationRoute result)
    {
        result = null;

        Transform stopPoint = station.FuelTruckStopPoint;
        if (stopPoint == null)
            return false;

        Vector2Int bestRoadCell = Vector2Int.zero;
        List<Vector2Int> bestOutbound = null;
        List<Vector2Int> bestReturn = null;
        float bestDistance = float.MaxValue;

        Vector3 stopForward = stopPoint.forward;
        stopForward.y = 0f;
        if (stopForward.sqrMagnitude < 0.0001f)
            return false;
        stopForward.Normalize();

        foreach (Vector2Int candidate in roadPositions)
        {
            Vector3 roadPosition = roadNetwork.GetRoadCenterWorldPosition(candidate, gridManager);
            Vector3 approachOffset = stopPoint.position - roadPosition;
            float distanceSquared = approachOffset.sqrMagnitude;
            Vector3 approachDirection = approachOffset;
            approachDirection.y = 0f;

            float maximumDistance = station.MaximumRoadApproachDistance;
            if (distanceSquared < 0.0001f ||
                distanceSquared > maximumDistance * maximumDistance ||
                distanceSquared >= bestDistance * bestDistance ||
                approachDirection.sqrMagnitude < 0.0001f)
                continue;

            approachDirection.Normalize();
            if (Vector3.Dot(approachDirection, stopForward) < 0.95f)
                continue;

            // The outbound route is reconstructed from the shared tree only
            // for geometrically valid candidates.
            List<Vector2Int> outbound = outboundTree.GetPath(candidate);
            if (outbound == null || outbound.Count < 2)
                continue;

            Vector3 previousRoadPosition = roadNetwork.GetRoadCenterWorldPosition(
                outbound[outbound.Count - 2], gridManager);
            Vector3 roadTravelDirection = roadPosition - previousRoadPosition;
            roadTravelDirection.y = 0f;

            if (roadTravelDirection.sqrMagnitude < 0.0001f)
                continue;

            roadTravelDirection.Normalize();
            if (Mathf.Abs(Vector3.Dot(roadTravelDirection, stopForward)) > 0.15f)
                continue;

            // Return routes use a reverse tree rooted at the exit. This
            // preserves the road network's directed-edge rules.
            List<Vector2Int> returning = returnTree.GetPath(candidate);
            if (returning == null || returning.Count == 0)
                continue;

            bestDistance = Mathf.Sqrt(distanceSquared);
            bestRoadCell = candidate;
            bestOutbound = outbound;
            bestReturn = returning;
        }

        if (bestOutbound == null || bestReturn == null)
            return false;

        result = new StationRoute
        {
            Station = station,
            RoadCell = bestRoadCell,
            Outbound = bestOutbound,
            Returning = bestReturn
        };

        return true;
    }

    private long GetAbsoluteGameTimeSeconds()
    {
        return
            (gameTimeManager.CurrentDay - 1) * 86400L +
            gameTimeManager.GetGameTimeSeconds();
    }

    private void RegisterFailedDispatch(
        PipelineNetworkManager.PipelineNetwork network)
    {
        if (network == null)
            return;

        activeNetworks.Remove(network);
        networkRetryTimes[network] =
            GetAbsoluteGameTimeSeconds() + Mathf.CeilToInt(Mathf.Max(0f, retryCooldownAfterFailureSeconds));
    }

    public void NotifyTruckFinished(
        PipelineNetworkManager.PipelineNetwork network,
        FuelTruckController truck,
        bool successful)
    {
        if (truck != null)
            activeTankers.Remove(truck);

        if (network == null)
            return;

        activeNetworks.Remove(network);

        if (successful)
            networkRetryTimes.Remove(network);
        else
            RegisterFailedDispatch(network);
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
