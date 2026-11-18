using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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

    [Header("One-Way Direction Overlay")]
    [SerializeField, Min(0f)] private float arrowHeightOffset = 0.012f;

    private const int North = 1;
    private const int East = 2;
    private const int South = 4;
    private const int West = 8;

    private class RoadModule
    {
        public GameObject Instance;
        public GameObject Arrow;
        public GameObject Prefab;
    }

    private readonly Dictionary<Vector2Int, RoadModule> modules =
        new Dictionary<Vector2Int, RoadModule>();

    private Mesh arrowMesh;
    private Material arrowMaterial;

    private void Awake()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (roadNetwork == null)
            roadNetwork = FindFirstObjectByType<RoadNetwork>();

        if (moduleParent == null)
            moduleParent = transform;

        CreateArrowAssets();
    }

    private void OnDestroy()
    {
        if (arrowMesh != null)
            Destroy(arrowMesh);

        if (arrowMaterial != null)
            Destroy(arrowMaterial);
    }

    private void CreateArrowAssets()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
        {
            Debug.LogError(
                "RoadModuleRuleManager: Could not find an unlit shader for road direction arrows.");
            return;
        }

        arrowMaterial = new Material(shader)
        {
            name = "Runtime_RoadDirectionArrow_Material",
            enableInstancing = true
        };

        if (arrowMaterial.HasProperty("_BaseColor"))
            arrowMaterial.SetColor("_BaseColor", Color.white);
        else if (arrowMaterial.HasProperty("_Color"))
            arrowMaterial.SetColor("_Color", Color.white);

        if (arrowMaterial.HasProperty("_Cull"))
            arrowMaterial.SetFloat("_Cull", 0f);

        arrowMesh = CreateArrowMesh();
    }

    private Mesh CreateArrowMesh()
    {
        // The arrow points along local +Z. Its flat shape sits just above the road.
        Vector3[] vertices =
        {
            Vector3.zero,
            new Vector3(-0.11f, 0f, -0.58f),
            new Vector3( 0.11f, 0f, -0.58f),
            new Vector3( 0.11f, 0f,  0.02f),
            new Vector3( 0.32f, 0f,  0.02f),
            new Vector3( 0f,    0f,  0.62f),
            new Vector3(-0.32f, 0f,  0.02f),
            new Vector3(-0.11f, 0f,  0.02f)
        };

        int[] triangles =
        {
            0, 2, 1,
            0, 3, 2,
            0, 4, 3,
            0, 5, 4,
            0, 6, 5,
            0, 7, 6,
            0, 1, 7
        };

        Mesh mesh = new Mesh
        {
            name = "RoadDirectionArrow"
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
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
            UpdateArrowOverlay(existing, cell, position);
            return;
        }

        RemoveModule(cell);

        GameObject instance = Instantiate(
            prefab, position, rotation, moduleParent);

        instance.name = $"{prefab.name}_Road_{cell.x}_{cell.y}";

        RoadModule module = new RoadModule
        {
            Instance = instance,
            Prefab = prefab
        };

        modules[cell] = module;
        UpdateArrowOverlay(module, cell, position);
    }

    private void UpdateArrowOverlay(
        RoadModule module,
        Vector2Int cell,
        Vector3 roadPosition)
    {
        if (arrowMesh == null || arrowMaterial == null)
            return;

        if (!roadNetwork.TryGetRoadDirection(cell, out RoadDirection direction))
        {
            if (module.Arrow != null)
                Destroy(module.Arrow);

            module.Arrow = null;
            return;
        }

        if (module.Arrow == null)
        {
            module.Arrow = new GameObject(
                $"RoadDirectionArrow_{cell.x}_{cell.y}");

            module.Arrow.transform.SetParent(moduleParent, true);

            MeshFilter meshFilter = module.Arrow.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = arrowMesh;

            MeshRenderer meshRenderer = module.Arrow.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = arrowMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        module.Arrow.transform.SetPositionAndRotation(
            roadPosition + Vector3.up * arrowHeightOffset,
            GetArrowRotation(direction));
    }

    private static Quaternion GetArrowRotation(RoadDirection direction)
    {
        switch (direction)
        {
            case RoadDirection.North:
                return Quaternion.identity;

            case RoadDirection.East:
                return Quaternion.Euler(0f, 90f, 0f);

            case RoadDirection.South:
                return Quaternion.Euler(0f, 180f, 0f);

            case RoadDirection.West:
                return Quaternion.Euler(0f, 270f, 0f);

            default:
                return Quaternion.identity;
        }
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

            if (module.Arrow != null)
                Destroy(module.Arrow);
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

        if (module.Arrow != null)
            Destroy(module.Arrow);

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
