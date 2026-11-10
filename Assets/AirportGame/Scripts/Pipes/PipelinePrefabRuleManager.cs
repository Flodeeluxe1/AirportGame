using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Owns the logical 1x1 pipeline cells and rebuilds their visuals from
/// four-direction neighbour masks.
/// </summary>
[DisallowMultipleComponent]
public sealed class PipelinePrefabRuleManager : MonoBehaviour
{
    private const int North = 1;
    private const int East = 2;
    private const int South = 4;
    private const int West = 8;

    [Header("Grid")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridOccupancy gridOccupancy;
    [SerializeField] private GridSurface gridSurface;

    [Header("Visual Parent")]
    [SerializeField] private Transform pipeParent;
    [SerializeField] private float heightOffset = 0.02f;

    [Header("Pipe Prefabs")]
    [SerializeField] private GameObject straightPrefab;
    [SerializeField] private GameObject cornerPrefab;
    [SerializeField] private GameObject threeWayPrefab;
    [SerializeField] private GameObject fourWayPrefab;
    [SerializeField] private GameObject endPrefab;

    [Header("Prefab Rotation Offsets")]
    [SerializeField] private int straightRotationOffset;
    [SerializeField] private int cornerRotationOffset;
    [SerializeField] private int threeWayRotationOffset;
    [SerializeField] private int fourWayRotationOffset;
    [SerializeField] private int endRotationOffset;

    [Header("Diagnostics")]
    [SerializeField] private bool logPlacement;

    private readonly HashSet<Vector2Int> pipeCells = new();
    private readonly Dictionary<Vector2Int, GameObject> visuals = new();
    private readonly Dictionary<Vector2Int, int> connectorCellCounts = new();

    public IReadOnlyCollection<Vector2Int> PipeCells => pipeCells;

    public event System.Action PipeTopologyChanged;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private static readonly int[] DirectionBits =
    {
        North, East, South, West
    };

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        FuelConnector.ConnectorRegistered += HandleConnectorRegistered;
        FuelConnector.ConnectorUnregistered += HandleConnectorUnregistered;
        RebuildConnectorCellCache();
    }

    private void OnDisable()
    {
        FuelConnector.ConnectorRegistered -= HandleConnectorRegistered;
        FuelConnector.ConnectorUnregistered -= HandleConnectorUnregistered;
        connectorCellCounts.Clear();
    }

    private void HandleConnectorRegistered(FuelConnector connector)
    {
        if (connector == null)
            return;

        Vector2Int cell = connector.GridCell;
        connectorCellCounts.TryGetValue(cell, out int count);
        connectorCellCounts[cell] = count + 1;
    }

    private void HandleConnectorUnregistered(FuelConnector connector)
    {
        if (connector == null)
            return;

        Vector2Int cell = connector.GridCell;
        if (!connectorCellCounts.TryGetValue(cell, out int count))
            return;

        if (count <= 1)
            connectorCellCounts.Remove(cell);
        else
            connectorCellCounts[cell] = count - 1;
    }

    private void RebuildConnectorCellCache()
    {
        connectorCellCounts.Clear();

        foreach (FuelConnector connector in FuelConnector.ActiveConnectors)
        {
            if (connector == null)
                continue;

            Vector2Int cell = connector.GridCell;
            connectorCellCounts.TryGetValue(cell, out int count);
            connectorCellCounts[cell] = count + 1;
        }
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (gridOccupancy == null)
            gridOccupancy = FindFirstObjectByType<GridOccupancy>();

        if (gridSurface == null && gridManager != null)
            gridSurface = gridManager.GetComponent<GridSurface>();
    }

    public bool CanPlacePipe(Vector2Int cell)
    {
        ResolveReferences();

        if (gridManager == null || !gridManager.IsInsideGrid(cell))
            return false;

        if (pipeCells.Contains(cell))
            return true;

        if (gridOccupancy != null && gridOccupancy.IsOccupied(cell))
            return false;

        return gridSurface != null &&
               gridSurface.IsSurface(cell, BuildSurface.Grass);
    }

    public bool TryAddPipe(Vector2Int cell)
    {
        if (!CanPlacePipe(cell))
            return false;

        if (!pipeCells.Add(cell))
            return true;

        RebuildVisuals();
        PipeTopologyChanged?.Invoke();

        if (logPlacement)
            Debug.Log($"PIPELINE CELL PLACED | {cell}", this);

        return true;
    }

    public bool TryAddPipes(IEnumerable<Vector2Int> cells)
    {
        if (cells == null)
            return false;

        HashSet<Vector2Int> requested = new(cells);

        foreach (Vector2Int cell in requested)
        {
            if (!CanPlacePipe(cell))
                return false;
        }

        bool changed = false;

        foreach (Vector2Int cell in requested)
            changed |= pipeCells.Add(cell);

        if (changed)
        {
            RebuildVisuals();
            PipeTopologyChanged?.Invoke();
        }

        if (logPlacement && changed)
            Debug.Log($"PIPELINE BUILT | Added {requested.Count} grid cells", this);

        return true;
    }

    public bool RemovePipe(Vector2Int cell)
    {
        if (!pipeCells.Remove(cell))
            return false;

        RebuildVisuals();
        PipeTopologyChanged?.Invoke();
        return true;
    }

    public bool IsPipe(Vector2Int cell) => pipeCells.Contains(cell);

    public bool HasPipeOrConnectorNeighbour(
        Vector2Int cell,
        IReadOnlyCollection<Vector2Int> connectorCells)
    {
        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int neighbour = cell + Directions[i];

            if (pipeCells.Contains(neighbour) ||
                (connectorCells != null && connectorCells.Contains(neighbour)))
                return true;
        }

        return false;
    }

    public GameObject CreatePreviewVisual(
        Vector2Int cell,
        IReadOnlyCollection<Vector2Int> routeCells,
        Transform previewParent,
        bool valid)
    {
        ResolveReferences();

        if (gridManager == null || previewParent == null)
            return null;

        int mask = GetConnectionMask(cell, routeCells);
        GameObject prefab = GetPrefab(mask);

        if (prefab == null)
            return null;

        Vector3 position = gridManager.GridToWorldCenter(cell);

        Quaternion rotation =
            Quaternion.Euler(0f, GetRotation(mask), 0f) *
            prefab.transform.rotation;

        GameObject instance = Instantiate(
            prefab,
            position,
            rotation,
            previewParent);

        AlignVisualToGround(instance, position);

        PlacementPreview preview =
            instance.AddComponent<PlacementPreview>();

        preview.Initialize();
        preview.SetValid(valid);

        instance.name = $"PipelinePreview_{cell.x}_{cell.y}";
        return instance;
    }

    public void RebuildVisuals()
    {
        ResolveReferences();
        ClearVisuals();

        if (gridManager == null)
            return;

        foreach (Vector2Int cell in pipeCells)
        {
            int mask = GetConnectionMask(cell);
            GameObject prefab = GetPrefab(mask);

            if (prefab == null)
                continue;

            Vector3 position = gridManager.GridToWorldCenter(cell);
            float groundY = position.y;

            Quaternion rotation =
                Quaternion.Euler(0f, GetRotation(mask), 0f) *
                prefab.transform.rotation;

            GameObject instance = Instantiate(
                prefab,
                position,
                rotation,
                pipeParent != null ? pipeParent : transform);

            AlignVisualToGround(instance, position);

            instance.name = $"Pipeline_{cell.x}_{cell.y}";
            instance.AddComponent<PipelinePiece>().Initialize(cell);
            visuals[cell] = instance;
        }
    }

    public void RestorePipes(IEnumerable<Vector2Int> savedCells)
    {
        ResolveReferences();
        pipeCells.Clear();

        if (savedCells != null)
        {
            foreach (Vector2Int cell in savedCells)
            {
                if (gridManager != null && gridManager.IsInsideGrid(cell))
                    pipeCells.Add(cell);
                else
                    Debug.LogWarning(
                        $"PipelinePrefabRuleManager: Skipped saved pipe cell " +
                        $"{cell} because it is outside the current grid.",
                        this);
            }
        }

        RebuildVisuals();
        PipeTopologyChanged?.Invoke();
    }

    public void ClearAllPipes()
    {
        bool changed = pipeCells.Count > 0;
        pipeCells.Clear();
        ClearVisuals();
        if (changed)
            PipeTopologyChanged?.Invoke();
    }

    private int GetConnectionMask(
        Vector2Int cell,
        IReadOnlyCollection<Vector2Int> additionalCells = null)
    {
        int mask = 0;

        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int neighbour = cell + Directions[i];

            if (pipeCells.Contains(neighbour) ||
                connectorCellCounts.ContainsKey(neighbour) ||
                (additionalCells != null && additionalCells.Contains(neighbour)))
            {
                mask |= DirectionBits[i];
            }
        }

        return mask;
    }

    private GameObject GetPrefab(int mask)
    {
        int count = CountBits(mask);

        if (count <= 1)
            return endPrefab;

        if (count == 2)
        {
            if (mask == (North | South) || mask == (East | West))
                return straightPrefab;

            return cornerPrefab;
        }

        if (count == 3)
            return threeWayPrefab;

        return fourWayPrefab;
    }

    private int GetRotation(int mask)
    {
        int count = CountBits(mask);

        if (count <= 1)
        {
            if ((mask & East) != 0) return 90 + endRotationOffset;
            if ((mask & South) != 0) return 180 + endRotationOffset;
            if ((mask & West) != 0) return 270 + endRotationOffset;
            return endRotationOffset;
        }

        if (count == 2)
        {
            if (mask == (East | West))
                return 90 + straightRotationOffset;

            if (mask == (North | East))
                return cornerRotationOffset;

            if (mask == (East | South))
                return 90 + cornerRotationOffset;

            if (mask == (South | West))
                return 180 + cornerRotationOffset;

            if (mask == (West | North))
                return 270 + cornerRotationOffset;

            return straightRotationOffset;
        }

        if (count == 3)
        {
            // The default T prefab is assumed to omit the south connection.
            if (mask == (North | East | West))
                return threeWayRotationOffset;

            if (mask == (North | East | South))
                return 90 + threeWayRotationOffset;

            if (mask == (East | South | West))
                return 180 + threeWayRotationOffset;

            return 270 + threeWayRotationOffset;
        }

        return fourWayRotationOffset;
    }

    private static int CountBits(int value)
    {
        int count = 0;

        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }

    private void AlignVisualToGround(GameObject instance, Vector3 gridPosition)
    {
        if (instance == null)
            return;

        Renderer[] renderers =
            instance.GetComponentsInChildren<Renderer>(true);

        float lowestY = float.PositiveInfinity;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
                continue;

            lowestY = Mathf.Min(lowestY, renderer.bounds.min.y);
        }

        if (!float.IsPositiveInfinity(lowestY))
        {
            Vector3 alignedPosition = instance.transform.position;
            alignedPosition.y += gridPosition.y - lowestY + heightOffset;
            instance.transform.position = alignedPosition;
        }
        else
        {
            instance.transform.position += Vector3.up * heightOffset;
        }
    }

    private void ClearVisuals()
    {
        foreach (GameObject visual in visuals.Values)
        {
            if (visual != null)
                Destroy(visual);
        }

        visuals.Clear();
    }
}