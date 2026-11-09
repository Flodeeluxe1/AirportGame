using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dispatches fuel tankers to the best available Fuel Delivery Station.
/// </summary>
public class FuelTruckSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject fuelTruckPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private RoadNetwork roadNetwork;

    [Header("Fuel Delivery")]
    [Min(1f)]
    [SerializeField] private float tankerCapacityLitres = 20000f;
    [Min(0.01f)]
    [SerializeField] private float fuelPricePerLitre = 1f;

    [Header("Spawner")]
    [SerializeField] private float spawnHeightOffset = 0.2f;

    [Header("Diagnostics")]
    [SerializeField] private bool logDispatches = true;

    private readonly HashSet<PipelineNetworkManager.PipelineNetwork> activeNetworks =
        new HashSet<PipelineNetworkManager.PipelineNetwork>();

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
            gridManager = FindFirstObjectByType<GridManager>();

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

    }

    private void Start()
    {
        if (fuelTruckPrefab == null || spawnPoint == null || exitPoint == null ||
            gridManager == null || roadNetwork == null)
        {
            Debug.LogError(
                "FuelTruckSpawner: Assign the tanker prefab, spawn point, exit point, GridManager and RoadNetwork.",
                this);
            enabled = false;
            return;
        }

    }

    public bool TryDispatchDelivery(
        PipelineNetworkManager.PipelineNetwork network)
    {
        if (network == null || network.CapacityLitres <= 0f ||
            network.RemainingCapacityLitres <= 0f ||
            activeNetworks.Contains(network))
            return false;

        StationRoute selectedRoute = SelectDeliveryStation(network);
        if (selectedRoute == null)
            return false;

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
            Debug.LogError(
                "FuelTruckSpawner: FuelTruckController must be on the tanker prefab root.",
                truckObject);
            Destroy(truckObject);
            return false;
        }

        selectedRoute.Station.RegisterTruck(truck);
        activeNetworks.Add(network);

        truck.InitializeDelivery(
            selectedRoute.Outbound,
            selectedRoute.Returning,
            selectedRoute.Station,
            network,
            amount,
            fuelPricePerLitre,
            this);

        if (logDispatches)
        {
            Debug.Log(
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

        List<StationRoute> candidates = new List<StationRoute>();

        FuelDeliveryStation[] stations =
            FindObjectsByType<FuelDeliveryStation>(FindObjectsSortMode.None);

        foreach (FuelDeliveryStation station in stations)
        {
            if (station == null || station.FuelTruckStopPoint == null ||
                station.FuelNetwork != targetNetwork)
                continue;

            if (!TryBuildStationRoute(station, startCell, exitCell, out StationRoute route))
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
        Vector2Int startCell,
        Vector2Int exitCell,
        out StationRoute result)
    {
        result = null;

        Transform stopPoint = station.FuelTruckStopPoint;
        if (stopPoint == null)
            return false;

        List<Vector2Int> roadPositions = roadNetwork.GetRoadPositions();
        Vector2Int bestRoadCell = Vector2Int.zero;
        List<Vector2Int> bestOutbound = null;
        List<Vector2Int> bestReturn = null;
        float bestDistance = float.MaxValue;

        foreach (Vector2Int candidate in roadPositions)
        {
            Vector3 roadPosition =
                roadNetwork.GetRoadCenterWorldPosition(candidate, gridManager);

            float distance = Vector3.Distance(roadPosition, stopPoint.position);
            if (distance > station.MaximumRoadApproachDistance ||
                distance >= bestDistance)
                continue;

            List<Vector2Int> outbound = roadNetwork.FindPath(startCell, candidate);
            if (outbound == null || outbound.Count < 2)
                continue;

            // The final road segment must run perpendicular to the stop point's
            // forward axis, with the stop point directly ahead of the truck.
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

            Vector3 previousRoadPosition =
                roadNetwork.GetRoadCenterWorldPosition(
                    outbound[outbound.Count - 2], gridManager);
            Vector3 roadTravelDirection = roadPosition - previousRoadPosition;
            roadTravelDirection.y = 0f;

            if (roadTravelDirection.sqrMagnitude < 0.0001f)
                continue;

            roadTravelDirection.Normalize();

            if (Mathf.Abs(Vector3.Dot(roadTravelDirection, stopForward)) > 0.15f)
                continue;

            List<Vector2Int> returning = roadNetwork.FindPath(candidate, exitCell);
            if (returning == null || returning.Count == 0)
                continue;

            bestDistance = distance;
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

    public void NotifyTruckFinished(
        PipelineNetworkManager.PipelineNetwork network)
    {
        if (network != null)
            activeNetworks.Remove(network);
    }
}
