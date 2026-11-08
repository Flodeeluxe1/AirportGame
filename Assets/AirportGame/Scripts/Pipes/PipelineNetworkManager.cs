using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Groups pipeline cells and building connectors into connected networks.
/// A connector joins every adjacent pipe cell, and unconnected connectors
/// are retained as individual networks for standalone fuel storage.
/// </summary>
[DisallowMultipleComponent]
public sealed class PipelineNetworkManager : MonoBehaviour
{
    public sealed class PipelineNetwork
    {
        public IReadOnlyCollection<Vector2Int> PipeCells => pipeCells;
        public IReadOnlyCollection<FuelConnector> Connectors => connectors;

        private readonly HashSet<Vector2Int> pipeCells = new();
        private readonly List<FuelConnector> connectors = new();

        internal void AddPipe(Vector2Int cell) => pipeCells.Add(cell);

        internal void AddConnector(FuelConnector connector)
        {
            if (connector != null && !connectors.Contains(connector))
                connectors.Add(connector);
        }
    }

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    [Header("References")]
    [SerializeField] private PipelinePrefabRuleManager pipelineRuleManager;

    [Header("Diagnostics")]
    [SerializeField] private bool logNetworkRebuilds;

    private readonly List<PipelineNetwork> networks = new();
    private readonly Dictionary<FuelConnector, PipelineNetwork> connectorNetworks = new();
    private int lastTopologyHash;
    private bool hasBuiltTopology;

    public IReadOnlyList<PipelineNetwork> Networks => networks;
    public int NetworkCount => networks.Count;
    public int TopologyVersion { get; private set; }

    private void Start()
    {
        ResolveReferences();
        RebuildNetworks();
    }

    private void Update()
    {
        ResolveReferences();

        if (pipelineRuleManager == null)
            return;

        int currentHash = CalculateTopologyHash();

        if (!hasBuiltTopology || currentHash != lastTopologyHash)
            RebuildNetworks();
    }

    private int CalculateTopologyHash()
    {
        unchecked
        {
            int sum = 17;
            int xor = 0;

            if (pipelineRuleManager != null)
            {
                foreach (Vector2Int cell in pipelineRuleManager.PipeCells)
                {
                    int value = cell.GetHashCode();
                    sum += value;
                    xor ^= value;
                }
            }

            FuelConnector[] connectors =
                FindObjectsByType<FuelConnector>(FindObjectsSortMode.None);

            foreach (FuelConnector connector in connectors)
            {
                if (connector == null)
                    continue;

                int value = connector.GetHashCode() * 397 ^
                            connector.GridCell.GetHashCode();
                sum += value;
                xor ^= value;
            }

            return sum * 397 ^ xor;
        }
    }

    private void ResolveReferences()
    {
        if (pipelineRuleManager == null)
            pipelineRuleManager = FindFirstObjectByType<PipelinePrefabRuleManager>();
    }

    public PipelineNetwork GetNetworkFor(FuelConnector connector)
    {
        if (connector == null)
            return null;

        connectorNetworks.TryGetValue(connector, out PipelineNetwork network);
        return network;
    }

    public void RebuildNetworks()
    {
        ResolveReferences();

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

        FuelConnector[] allConnectors =
            FindObjectsByType<FuelConnector>(FindObjectsSortMode.None);

        Dictionary<Vector2Int, List<FuelConnector>> connectorsByPipeCell =
            new Dictionary<Vector2Int, List<FuelConnector>>();

        Dictionary<FuelConnector, List<Vector2Int>> adjacentPipesByConnector =
            new Dictionary<FuelConnector, List<Vector2Int>>();

        foreach (FuelConnector connector in allConnectors)
        {
            if (connector == null)
                continue;

            List<Vector2Int> adjacentPipes = new List<Vector2Int>();

            foreach (Vector2Int direction in Directions)
            {
                Vector2Int adjacentCell = connector.GridCell + direction;

                if (!pipelineRuleManager.IsPipe(adjacentCell))
                    continue;

                adjacentPipes.Add(adjacentCell);

                if (!connectorsByPipeCell.TryGetValue(
                        adjacentCell, out List<FuelConnector> list))
                {
                    list = new List<FuelConnector>();
                    connectorsByPipeCell.Add(adjacentCell, list);
                }

                list.Add(connector);
            }

            adjacentPipesByConnector.Add(connector, adjacentPipes);
        }

        HashSet<FuelConnector> assignedConnectors = new HashSet<FuelConnector>();

        // Flood-fill pipe cells. A connector is a bridge: all pipe cells
        // adjacent to the same connector belong to the same network.
        while (remainingPipes.Count > 0)
        {
            Vector2Int seed = default;
            foreach (Vector2Int cell in remainingPipes)
            {
                seed = cell;
                break;
            }

            PipelineNetwork network = new PipelineNetwork();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
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

                if (!connectorsByPipeCell.TryGetValue(
                        cell, out List<FuelConnector> adjacentConnectors))
                    continue;

                foreach (FuelConnector connector in adjacentConnectors)
                {
                    if (connector == null)
                        continue;

                    network.AddConnector(connector);

                    if (!assignedConnectors.Add(connector))
                        continue;

                    if (!adjacentPipesByConnector.TryGetValue(
                            connector, out List<Vector2Int> connectorPipes))
                        continue;

                    // This connector can join otherwise separate pipe groups.
                    foreach (Vector2Int connectorPipe in connectorPipes)
                    {
                        if (remainingPipes.Remove(connectorPipe))
                            queue.Enqueue(connectorPipe);
                    }
                }
            }

            AddNetwork(network);
        }

        // Keep disconnected building connectors as standalone networks.
        foreach (FuelConnector connector in allConnectors)
        {
            if (connector == null || !assignedConnectors.Add(connector))
                continue;

            PipelineNetwork network = new PipelineNetwork();
            network.AddConnector(connector);
            AddNetwork(network);
        }

        TopologyVersion++;
        lastTopologyHash = CalculateTopologyHash();
        hasBuiltTopology = true;

        if (logNetworkRebuilds)
        {
            Debug.Log(
                $"PIPELINE NETWORKS REBUILT | Networks: {networks.Count} | " +
                $"Connected building connectors: {connectorNetworks.Count}",
                this);
        }
    }

    private void AddNetwork(PipelineNetwork network)
    {
        networks.Add(network);

        foreach (FuelConnector connector in network.Connectors)
            connectorNetworks[connector] = network;
    }
}
