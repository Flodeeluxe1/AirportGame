using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Groups pipeline cells and building connectors into connected networks.
/// Each network owns one shared fuel reserve whose capacity is supplied by
/// the storage tanks connected to that network.
/// </summary>
[DisallowMultipleComponent]
public sealed class PipelineNetworkManager : MonoBehaviour
{
    public sealed class PipelineNetwork
    {
        public IReadOnlyCollection<Vector2Int> PipeCells => pipeCells;
        public IReadOnlyCollection<FuelConnector> Connectors => connectors;
        public float CapacityLitres { get; internal set; }
        public float CurrentFuelLitres { get; internal set; }
        public float RemainingCapacityLitres =>
            Mathf.Max(0f, CapacityLitres - CurrentFuelLitres);

        private readonly HashSet<Vector2Int> pipeCells = new();
        private readonly List<FuelConnector> connectors = new();

        internal void AddPipe(Vector2Int cell) => pipeCells.Add(cell);

        internal void AddConnector(FuelConnector connector)
        {
            if (connector != null && !connectors.Contains(connector))
                connectors.Add(connector);
        }

        public float ConsumeFuel(float amountLitres)
        {
            if (amountLitres <= 0f)
                return 0f;

            float consumed = Mathf.Min(amountLitres, CurrentFuelLitres);
            CurrentFuelLitres -= consumed;
            return consumed;
        }

        public float AddFuel(float amountLitres)
        {
            if (amountLitres <= 0f)
                return 0f;

            float accepted = Mathf.Min(amountLitres, RemainingCapacityLitres);
            CurrentFuelLitres += accepted;
            return accepted;
        }

        internal void RestoreFuel(float amountLitres)
        {
            CurrentFuelLitres = Mathf.Clamp(
                amountLitres,
                0f,
                CapacityLitres);
        }
    }

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
    };

    [Header("References")]
    [SerializeField] private PipelinePrefabRuleManager pipelineRuleManager;
    [SerializeField] private FuelTruckSpawner fuelTruckSpawner;

    [Header("Automatic Fuel Refill")]
    [Range(0f, 100f)]
    [SerializeField] private float automaticRefillThresholdPercent = 25f;
    [Min(0.1f)]
    [SerializeField] private float automaticRefillCheckInterval = 2f;

    [Header("Diagnostics")]
    [SerializeField] private bool logNetworkRebuilds;

    private readonly List<PipelineNetwork> networks = new();
    private readonly Dictionary<FuelConnector, PipelineNetwork> connectorNetworks = new();
    private readonly HashSet<FuelStorageTank> initializedTanks = new();
    private PipelinePrefabRuleManager subscribedPipelineRuleManager;
    private bool hasStarted;
    private Coroutine topologyRebuildRoutine;
    private float automaticRefillCheckTimer;

    private List<PipelineNetworkSaveData> pendingSavedInventories;
    private bool hasPendingSavedInventories;

    public IReadOnlyList<PipelineNetwork> Networks => networks;
    public int NetworkCount => networks.Count;
    public float AutomaticRefillThresholdPercent =>
        automaticRefillThresholdPercent;
    public int TopologyVersion { get; private set; }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToPipelineRuleManager();
        FuelConnector.ConnectorRegistered += HandleConnectorChanged;
        FuelConnector.ConnectorUnregistered += HandleConnectorChanged;

        if (hasStarted)
            QueueTopologyRebuild();
    }

    private void Start()
    {
        ResolveReferences();
        SubscribeToPipelineRuleManager();
        hasStarted = true;
        RebuildNetworks();
    }

    private void OnDisable()
    {
        if (subscribedPipelineRuleManager != null)
            subscribedPipelineRuleManager.PipeTopologyChanged -= HandlePipeTopologyChanged;

        subscribedPipelineRuleManager = null;
        FuelConnector.ConnectorRegistered -= HandleConnectorChanged;
        FuelConnector.ConnectorUnregistered -= HandleConnectorChanged;

        if (topologyRebuildRoutine != null)
        {
            StopCoroutine(topologyRebuildRoutine);
            topologyRebuildRoutine = null;
        }
    }

    private void Update()
    {
        automaticRefillCheckTimer -= Time.deltaTime;
        if (automaticRefillCheckTimer <= 0f)
        {
            automaticRefillCheckTimer =
                Mathf.Max(0.1f, automaticRefillCheckInterval);
            CheckAutomaticRefillRequests();
        }
    }

    private void HandlePipeTopologyChanged()
    {
        QueueTopologyRebuild();
    }

    private void HandleConnectorChanged(FuelConnector connector)
    {
        QueueTopologyRebuild();
    }

    private void SubscribeToPipelineRuleManager()
    {
        if (subscribedPipelineRuleManager == pipelineRuleManager)
            return;

        if (subscribedPipelineRuleManager != null)
            subscribedPipelineRuleManager.PipeTopologyChanged -= HandlePipeTopologyChanged;

        subscribedPipelineRuleManager = pipelineRuleManager;

        if (subscribedPipelineRuleManager != null)
            subscribedPipelineRuleManager.PipeTopologyChanged += HandlePipeTopologyChanged;
    }

    private void QueueTopologyRebuild()
    {
        if (!hasStarted || topologyRebuildRoutine != null || !isActiveAndEnabled)
            return;

        topologyRebuildRoutine = StartCoroutine(RebuildTopologyNextFrame());
    }

    private IEnumerator RebuildTopologyNextFrame()
    {
        // Coalesce multiple pipe/connector changes made during one operation.
        yield return null;
        topologyRebuildRoutine = null;
        RebuildNetworks();
    }

    private void CheckAutomaticRefillRequests()
    {
        if (fuelTruckSpawner == null)
            return;

        foreach (PipelineNetwork network in networks)
        {
            if (network == null || network.CapacityLitres <= 0f)
                continue;

            float fuelPercentage =
                network.CurrentFuelLitres / network.CapacityLitres * 100f;

            if (fuelPercentage <= automaticRefillThresholdPercent)
                fuelTruckSpawner.TryDispatchDelivery(
                    network,
                    this,
                    automaticRefillThresholdPercent);
        }
    }

    private void ResolveReferences()
    {
        if (pipelineRuleManager == null)
            pipelineRuleManager = FindFirstObjectByType<PipelinePrefabRuleManager>();

        if (fuelTruckSpawner == null)
            fuelTruckSpawner = FindFirstObjectByType<FuelTruckSpawner>();
    }

    public PipelineNetwork GetNetworkFor(FuelConnector connector)
    {
        if (connector == null)
            return null;

        connectorNetworks.TryGetValue(connector, out PipelineNetwork network);
        return network;
    }

    public bool IsNetworkCurrent(PipelineNetwork network)
    {
        return network != null && networks.Contains(network);
    }

    public List<PipelineNetworkSaveData> GetSaveData()
    {
        List<PipelineNetworkSaveData> savedNetworks = new();

        foreach (PipelineNetwork network in networks)
        {
            PipelineNetworkSaveData saved = new()
            {
                currentFuelLitres = network.CurrentFuelLitres
            };

            saved.pipeCells.AddRange(network.PipeCells);

            foreach (FuelConnector connector in network.Connectors)
            {
                if (connector != null)
                    saved.connectorCells.Add(connector.GridCell);
            }

            savedNetworks.Add(saved);
        }

        return savedNetworks;
    }

    public void RestoreSavedFuelInventories(
        List<PipelineNetworkSaveData> savedNetworks)
    {
        pendingSavedInventories = savedNetworks != null
            ? new List<PipelineNetworkSaveData>(savedNetworks)
            : new List<PipelineNetworkSaveData>();

        hasPendingSavedInventories = true;
        QueueTopologyRebuild();
    }

    public void ClearNetworksForWorldReset()
    {
        networks.Clear();
        connectorNetworks.Clear();
        initializedTanks.Clear();
        pendingSavedInventories = null;
        hasPendingSavedInventories = false;
        TopologyVersion++;
    }

    public void RebuildNetworks()
    {
        ResolveReferences();

        List<PipelineNetwork> previousNetworks = new List<PipelineNetwork>(networks);
        networks.Clear();
        connectorNetworks.Clear();

        if (pipelineRuleManager == null)
        {
            Debug.LogWarning(
                "PipelineNetworkManager: PipelinePrefabRuleManager was not found.",
                this);
            return;
        }

        HashSet<Vector2Int> remainingPipes =
            new HashSet<Vector2Int>(pipelineRuleManager.PipeCells);

        List<FuelConnector> allConnectors =
            new List<FuelConnector>(FuelConnector.ActiveConnectors);

        Dictionary<Vector2Int, List<FuelConnector>> connectorsByPipeCell = new();
        Dictionary<FuelConnector, List<Vector2Int>> adjacentPipesByConnector = new();

        foreach (FuelConnector connector in allConnectors)
        {
            if (connector == null)
                continue;

            List<Vector2Int> adjacentPipes = new();
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int adjacentCell = connector.GridCell + direction;
                if (!pipelineRuleManager.IsPipe(adjacentCell))
                    continue;

                adjacentPipes.Add(adjacentCell);
                if (!connectorsByPipeCell.TryGetValue(adjacentCell, out List<FuelConnector> list))
                {
                    list = new List<FuelConnector>();
                    connectorsByPipeCell.Add(adjacentCell, list);
                }
                list.Add(connector);
            }
            adjacentPipesByConnector.Add(connector, adjacentPipes);
        }

        HashSet<FuelConnector> assignedConnectors = new();

        while (remainingPipes.Count > 0)
        {
            Vector2Int seed = default;
            foreach (Vector2Int cell in remainingPipes)
            {
                seed = cell;
                break;
            }

            PipelineNetwork network = new();
            Queue<Vector2Int> queue = new();
            queue.Enqueue(seed);
            remainingPipes.Remove(seed);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                network.AddPipe(cell);

                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int neighbour = cell + direction;
                    if (remainingPipes.Remove(neighbour))
                        queue.Enqueue(neighbour);
                }

                if (!connectorsByPipeCell.TryGetValue(cell, out List<FuelConnector> adjacentConnectors))
                    continue;

                foreach (FuelConnector connector in adjacentConnectors)
                {
                    if (connector == null)
                        continue;

                    network.AddConnector(connector);
                    if (!assignedConnectors.Add(connector))
                        continue;

                    if (!adjacentPipesByConnector.TryGetValue(connector, out List<Vector2Int> connectorPipes))
                        continue;

                    foreach (Vector2Int connectorPipe in connectorPipes)
                    {
                        if (remainingPipes.Remove(connectorPipe))
                            queue.Enqueue(connectorPipe);
                    }
                }
            }

            AddNetwork(network);
        }

        foreach (FuelConnector connector in allConnectors)
        {
            if (connector == null || !assignedConnectors.Add(connector))
                continue;

            PipelineNetwork network = new();
            network.AddConnector(connector);
            AddNetwork(network);
        }

        ReconcileFuelInventories(previousNetworks);

        if (hasPendingSavedInventories)
            ApplySavedFuelInventories();

        TopologyVersion++;

        if (logNetworkRebuilds)
        {
            Debug.Log(
                $"PIPELINE NETWORKS REBUILT | Networks: {networks.Count} | " +
                $"Connected connectors: {connectorNetworks.Count} | " +
                $"Total stored fuel: {GetTotalStoredFuel():F1} L",
                this);
        }
    }

    private void ApplySavedFuelInventories()
    {
        if (pendingSavedInventories == null)
            pendingSavedInventories = new List<PipelineNetworkSaveData>();

        HashSet<int> usedSavedNetworks = new();

        foreach (PipelineNetwork network in networks)
        {
            int matchedIndex = -1;

            for (int i = 0; i < pendingSavedInventories.Count; i++)
            {
                if (usedSavedNetworks.Contains(i))
                    continue;

                PipelineNetworkSaveData saved = pendingSavedInventories[i];
                if (saved == null)
                    continue;

                bool overlaps = false;

                foreach (Vector2Int cell in network.PipeCells)
                {
                    if (saved.pipeCells != null && saved.pipeCells.Contains(cell))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps && saved.connectorCells != null)
                {
                    foreach (FuelConnector connector in network.Connectors)
                    {
                        if (connector != null &&
                            saved.connectorCells.Contains(connector.GridCell))
                        {
                            overlaps = true;
                            break;
                        }
                    }
                }

                if (overlaps)
                {
                    matchedIndex = i;
                    break;
                }
            }

            if (matchedIndex < 0)
                continue;

            PipelineNetworkSaveData matched = pendingSavedInventories[matchedIndex];
            network.RestoreFuel(matched.currentFuelLitres);
            usedSavedNetworks.Add(matchedIndex);

            foreach (FuelStorageTank tank in GetStorageTanks(network))
                initializedTanks.Add(tank);
        }

        int unmatched = pendingSavedInventories.Count - usedSavedNetworks.Count;
        if (unmatched > 0)
        {
            Debug.LogWarning(
                $"PipelineNetworkManager: {unmatched} saved fuel network(s) " +
                "could not be matched to the restored pipeline layout.",
                this);
        }

        pendingSavedInventories = null;
        hasPendingSavedInventories = false;
    }

    private void ReconcileFuelInventories(List<PipelineNetwork> previousNetworks)
    {
        foreach (PipelineNetwork network in networks)
        {
            HashSet<FuelStorageTank> tanks = GetStorageTanks(network);
            foreach (FuelStorageTank tank in tanks)
                network.CapacityLitres += tank.CapacityLitres;
        }

        // Carry each previous reserve into every resulting network it overlaps.
        // When one old network splits, its reserve is divided by the new
        // networks' storage capacities. When networks merge, contributions add.
        foreach (PipelineNetwork previous in previousNetworks)
        {
            if (previous.CurrentFuelLitres <= 0f)
                continue;

            List<PipelineNetwork> destinations = new();
            float totalDestinationCapacity = 0f;

            foreach (PipelineNetwork current in networks)
            {
                if (!NetworksOverlap(previous, current) || current.CapacityLitres <= 0f)
                    continue;

                destinations.Add(current);
                totalDestinationCapacity += current.CapacityLitres;
            }

            if (destinations.Count == 0 || totalDestinationCapacity <= 0f)
                continue;

            float remainingFuel = previous.CurrentFuelLitres;
            for (int i = 0; i < destinations.Count; i++)
            {
                PipelineNetwork destination = destinations[i];
                float share = i == destinations.Count - 1
                    ? remainingFuel
                    : previous.CurrentFuelLitres *
                      destination.CapacityLitres / totalDestinationCapacity;

                float accepted = destination.AddFuel(share);
                remainingFuel -= share;

                if (accepted < share)
                {
                    Debug.LogWarning(
                        "PipelineNetworkManager: Fuel exceeded the capacity of a " +
                        "resulting network during a topology change. Excess fuel was discarded.",
                        this);
                }
            }
        }

        // A tank contributes its initial stock once, the first time it joins
        // any pipeline network. It never reinitializes on later rebuilds.
        foreach (PipelineNetwork network in networks)
        {
            foreach (FuelStorageTank tank in GetStorageTanks(network))
            {
                if (initializedTanks.Add(tank))
                    network.AddFuel(tank.InitialFuelLitres);
            }
        }
    }

    private HashSet<FuelStorageTank> GetStorageTanks(PipelineNetwork network)
    {
        HashSet<FuelStorageTank> tanks = new();
        foreach (FuelConnector connector in network.Connectors)
        {
            if (connector != null && connector.StorageTank != null)
                tanks.Add(connector.StorageTank);
        }
        return tanks;
    }

    private bool NetworksOverlap(PipelineNetwork a, PipelineNetwork b)
    {
        foreach (Vector2Int cell in a.PipeCells)
        {
            if (((HashSet<Vector2Int>)b.PipeCells).Contains(cell))
                return true;
        }

        foreach (FuelConnector connector in a.Connectors)
        {
            if (((List<FuelConnector>)b.Connectors).Contains(connector))
                return true;
        }

        return false;
    }

    private float GetTotalStoredFuel()
    {
        float total = 0f;
        foreach (PipelineNetwork network in networks)
            total += network.CurrentFuelLitres;
        return total;
    }

    private void AddNetwork(PipelineNetwork network)
    {
        networks.Add(network);
        foreach (FuelConnector connector in network.Connectors)
            connectorNetworks[connector] = network;
    }
}
