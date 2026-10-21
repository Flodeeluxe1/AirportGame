
using System.Collections.Generic;
using UnityEngine;

public class RoadModuleRuleManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private Transform moduleParent;

    [Header("Road Module Prefabs")]
    [SerializeField] private GameObject deadEndPrefab;
    [SerializeField] private GameObject straightPrefab;
    [SerializeField] private GameObject cornerPrefab;
    [SerializeField] private GameObject tJunctionPrefab;
    [SerializeField] private GameObject crossroadsPrefab;

    [Header("Prefab Rotation Offsets")]
    [SerializeField] private Vector3 deadEndRotationOffset;
    [SerializeField] private Vector3 straightRotationOffset;
    [SerializeField] private Vector3 cornerRotationOffset;
    [SerializeField] private Vector3 tJunctionRotationOffset;
    [SerializeField] private Vector3 crossroadsRotationOffset;

    private const int North = 1;
    private const int East = 2;
    private const int South = 4;
    private const int West = 8;

    private class RoadModule
    {
        public GameObject Instance;
        public GameObject Prefab;
    }

    private readonly Dictionary<Vector2Int, RoadModule> modules =
        new Dictionary<Vector2Int, RoadModule>();

    private void Awake()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (moduleParent == null)
            moduleParent = transform;
    }

    public void RefreshCell(Vector2Int cell)
    {
        if (gridManager == null || roadNetwork == null)
        {
            Debug.LogError(
                "RoadModuleRuleManager: GridManager or RoadNetwork is missing.");
            return;
        }

        cell = roadNetwork.GetRoadAnchor(cell);

        if (!roadNetwork.IsRoad(cell))
        {
            RemoveModule(cell);
            return;
        }

        int mask = roadNetwork.GetConnectionMask(cell);

        GameObject prefab = GetPrefab(
            mask,
            out int rotationSteps,
            out Vector3 rotationOffset);

        if (prefab == null)
        {
            Debug.LogError(
                $"RoadModuleRuleManager: Missing prefab for module {cell}, " +
                $"connection mask {mask}.");

            RemoveModule(cell);
            return;
        }

        Quaternion rotation =
            Quaternion.Euler(0f, rotationSteps * 90f, 0f) *
            Quaternion.Euler(rotationOffset);

        Vector3 position =
            roadNetwork.GetRoadCenterWorldPosition(cell, gridManager);

        if (modules.TryGetValue(cell, out RoadModule existing) &&
            existing.Instance != null &&
            existing.Prefab == prefab)
        {
            existing.Instance.transform.SetPositionAndRotation(position, rotation);
            return;
        }

        RemoveModule(cell);

        GameObject instance = Instantiate(
            prefab, position, rotation, moduleParent);

        instance.name = $"{prefab.name}_Road_{cell.x}_{cell.y}";

        modules[cell] = new RoadModule
        {
            Instance = instance,
            Prefab = prefab
        };
    }

    public void RefreshCellAndNeighbors(Vector2Int cell)
    {
        if (roadNetwork == null)
            return;

        cell = roadNetwork.GetRoadAnchor(cell);

        RefreshCell(cell);
        RefreshCell(cell + Vector2Int.up * RoadNetwork.RoadModuleSize);
        RefreshCell(cell + Vector2Int.right * RoadNetwork.RoadModuleSize);
        RefreshCell(cell + Vector2Int.down * RoadNetwork.RoadModuleSize);
        RefreshCell(cell + Vector2Int.left * RoadNetwork.RoadModuleSize);
    }

    public void RefreshAllRoadModules()
    {
        if (roadNetwork == null)
        {
            Debug.LogError("RoadModuleRuleManager: RoadNetwork is missing.");
            return;
        }

        List<Vector2Int> roadCells = roadNetwork.GetRoadPositions();
        HashSet<Vector2Int> currentRoads = new HashSet<Vector2Int>(roadCells);
        List<Vector2Int> staleCells = new List<Vector2Int>();

        foreach (Vector2Int cell in modules.Keys)
        {
            if (!currentRoads.Contains(cell))
                staleCells.Add(cell);
        }

        foreach (Vector2Int cell in staleCells)
            RemoveModule(cell);

        foreach (Vector2Int cell in roadCells)
            RefreshCell(cell);
    }

    public void ClearAllRoadModules()
    {
        foreach (RoadModule module in modules.Values)
        {
            if (module.Instance != null)
                Destroy(module.Instance);
        }

        modules.Clear();
    }

    private void RemoveModule(Vector2Int cell)
    {
        cell = roadNetwork != null
            ? roadNetwork.GetRoadAnchor(cell)
            : cell;

        if (!modules.TryGetValue(cell, out RoadModule module))
            return;

        if (module.Instance != null)
            Destroy(module.Instance);

        modules.Remove(cell);
    }

    private GameObject GetPrefab(
        int mask,
        out int rotationSteps,
        out Vector3 rotationOffset)
    {
        rotationSteps = 0;
        rotationOffset = deadEndRotationOffset;

        int count = CountConnections(mask);

        if (count <= 1)
        {
            if (count == 1)
                rotationSteps = FindRotation(mask, North);

            return deadEndPrefab;
        }

        if (count == 2)
        {
            bool straight =
                mask == (North | South) ||
                mask == (East | West);

            if (straight)
            {
                rotationSteps = FindRotation(mask, North | South);
                rotationOffset = straightRotationOffset;
                return straightPrefab;
            }

            rotationSteps = FindRotation(mask, North | East);
            rotationOffset = cornerRotationOffset;
            return cornerPrefab;
        }

        if (count == 3)
        {
            rotationSteps = FindRotation(mask, North | East | South);
            rotationOffset = tJunctionRotationOffset;
            return tJunctionPrefab;
        }

        rotationOffset = crossroadsRotationOffset;
        return crossroadsPrefab;
    }

    private static int CountConnections(int mask)
    {
        int count = 0;
        if ((mask & North) != 0) count++;
        if ((mask & East) != 0) count++;
        if ((mask & South) != 0) count++;
        if ((mask & West) != 0) count++;
        return count;
    }

    private static int FindRotation(int targetMask, int baseMask)
    {
        for (int steps = 0; steps < 4; steps++)
        {
            if (RotateMask(baseMask, steps) == targetMask)
                return steps;
        }

        return 0;
    }

    private static int RotateMask(int mask, int clockwiseSteps)
    {
        for (int i = 0; i < clockwiseSteps; i++)
        {
            int rotated = 0;

            if ((mask & North) != 0) rotated |= East;
            if ((mask & East) != 0) rotated |= South;
            if ((mask & South) != 0) rotated |= West;
            if ((mask & West) != 0) rotated |= North;

            mask = rotated;
        }

        return mask;
    }
}
