using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Groups pipeline cells and building connectors into connected networks.
/// This class only resolves connectivity; fuel storage and transfer are
/// handled separately.
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

        HashSet<Vector2Int> remaining =
            new HashSet<Vector2Int>(pipelineRuleManager.PipeCells);

        FuelConnector[] connectors =
            FindObjectsByType<FuelConnector>(FindObjectsSortMode.None);

        Dictionary<Vector2Int, List<FuelConnector>> connectorsByAdjacentCell =
            new Dictionary<Vector2Int, List<FuelConnector>>();

        foreach (FuelConnector connector in connectors)
        {
            if (connector == null)
                continue;

            Vector2Int connectorCell = connector.GridCell;

            foreach (Vector2Int direction in Directions)
            {
                Vector2Int adjacentPipeCell = connectorCell + direction;

                if (!pipelineRuleManager.IsPipe(adjacentPipeCell))
                    continue;

                if (!connectorsByAdjacentCell.TryGetValue(
                        adjacentPipeCell, out List<FuelConnector> list))
                {
                    list = new List<FuelConnector>();
                    connectorsByAdjacentCell.Add(adjacentPipeCell, list);
                }

                if (!list.Contains(connector))
                    list.Add(connector);
            }
        }

        while (remaining.Count > 0)
        {
            Vector2Int seed = default;
            foreach (Vector2Int cell in remaining)
            {
                seed = cell;
                break;
            }

            PipelineNetwork network = new PipelineNetwork();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(seed);
            remaining.Remove(seed);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                network.AddPipe(cell);

                for (int i = 0; i < Directions.Length; i++)
                {
                    Vector2Int neighbour = cell + Directions[i];

                    if (remaining.Remove(neighbour))
                        queue.Enqueue(neighbour);
                }

                if (connectorsByAdjacentCell.TryGetValue(
                        cell, out List<FuelConnector> connectedHere))
                {
                    foreach (FuelConnector connector in connectedHere)
                        network.AddConnector(connector);
                }
            }

            networks.Add(network);

            foreach (FuelConnector connector in network.Connectors)
                connectorNetworks[connector] = network;
        }

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
}
