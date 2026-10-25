
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Builds taxiway visuals from registered placement areas.
/// Junctions are identified before straight modules are generated.
/// This component does not modify TaxiwayNetwork, pathfinding,
/// centerline data, Tilemaps, or save data.
/// </summary>
public sealed class TaxiwayPrefabRuleManager : MonoBehaviour
{
    private const int North = 1;
    private const int East = 2;
    private const int South = 4;
    private const int West = 8;
    private const int ModuleSize = 5;

    [Header("Grid")]
    [SerializeField] private GridManager gridManager;

    [Header("Spawn Parent")]
    [SerializeField] private Transform moduleParent;

    [Header("Taxiway Prefabs")]
    [SerializeField] private GameObject straight5x5Prefab;
    [SerializeField] private GameObject straight5x1Prefab;
    [SerializeField] private GameObject curvedPrefab;
    [SerializeField] private GameObject tSectionPrefab;
    [SerializeField] private GameObject intersectionPrefab;

    [Header("Prefab Rotation Offsets")]
    [SerializeField] private Vector3 straight5x5RotationOffset;
    [SerializeField] private Vector3 straight5x1RotationOffset;
    [SerializeField] private int curvedRotationOffset;
    [SerializeField] private int tSectionRotationOffset;
    [SerializeField] private int intersectionRotationOffset;

    [Header("Diagnostics")]
    [SerializeField] private bool logJunctionDiagnostics = true;

    private readonly List<PlacementArea> placementAreas =
        new List<PlacementArea>();

    private readonly List<VisualModule> modules =
        new List<VisualModule>();

    private readonly List<GameObject> spawnedVisuals =
        new List<GameObject>();

    private sealed class PlacementArea
    {
        public Vector2Int Origin;
        public int Width;
        public int Depth;

        public int MaxX => Origin.x + Width;
        public int MaxZ => Origin.y + Depth;

        public PlacementArea(Vector2Int origin, int width, int depth)
        {
            Origin = origin;
            Width = width;
            Depth = depth;
        }

        public override string ToString()
        {
            return $"Origin={Origin}, Size={Width}x{Depth}, " +
                   $"MaxX={MaxX}, MaxZ={MaxZ}";
        }
    }

    private sealed class JunctionCandidate
    {
        public Vector2Int Origin;
        public int ConnectionMask;
        public string DetectionReason;

        public JunctionCandidate(
            Vector2Int origin,
            int connectionMask,
            string detectionReason)
        {
            Origin = origin;
            ConnectionMask = connectionMask;
            DetectionReason = detectionReason;
        }
    }

    private sealed class VisualModule
    {
        public Vector2Int Origin;
        public int Width;
        public int Depth;
        public bool IsSmallStraight;
        public bool IsVerticalRun;
        public bool IsJunctionFootprint;
        public int ConnectionMask;
        public int ForcedConnectionMask;

        public int MaxX => Origin.x + Width;
        public int MaxZ => Origin.y + Depth;

        public VisualModule(
            Vector2Int origin,
            int width,
            int depth,
            bool isSmallStraight,
            bool isVerticalRun = false,
            bool isJunctionFootprint = false,
            int forcedConnectionMask = 0)
        {
            Origin = origin;
            Width = width;
            Depth = depth;
            IsSmallStraight = isSmallStraight;
            IsVerticalRun = isVerticalRun;
            IsJunctionFootprint = isJunctionFootprint;
            ForcedConnectionMask = forcedConnectionMask;
            ConnectionMask = 0;
        }
    }

    public void RegisterTaxiwayArea(
        Vector2Int origin,
        int areaWidth,
        int areaDepth)
    {
        if (gridManager == null)
        {
            Debug.LogError(
                "TaxiwayPrefabRuleManager: GridManager is missing.", this);
            return;
        }

        if (areaWidth <= 0 || areaDepth <= 0)
        {
            Debug.LogWarning(
                "TaxiwayPrefabRuleManager: Dimensions must be positive.",
                this);
            return;
        }

        if (areaWidth != ModuleSize && areaDepth != ModuleSize)
        {
            Debug.LogWarning(
                "TaxiwayPrefabRuleManager: Expected a 5-cell-wide " +
                "or 5-cell-deep area. Received " +
                areaWidth + "x" + areaDepth + ".", this);
            return;
        }

        placementAreas.Add(
            new PlacementArea(origin, areaWidth, areaDepth));

        RebuildVisuals();
    }

    public void RebuildVisuals()
    {
        ClearSpawnedVisuals();
        modules.Clear();

        List<JunctionCandidate> junctions = FindJunctionCandidates();

        foreach (PlacementArea area in placementAreas)
            BuildModulesForArea(area);

        RemoveExactDuplicateModules();
        ApplyJunctionFootprints(junctions);
        MergeCompleteStraightBlocks();
        CalculateConnectionMasks();

        if (logJunctionDiagnostics)
            LogFinalJunctionDecisions();

        SpawnAllModules();
    }

    public void ClearAllTaxiwayAreas()
    {
        placementAreas.Clear();
        modules.Clear();
        ClearSpawnedVisuals();
    }

    private void BuildModulesForArea(PlacementArea area)
    {
        bool vertical = area.Width == ModuleSize;
        int length = vertical ? area.Depth : area.Width;
        int fullModules = length / ModuleSize;
        int remainder = length % ModuleSize;

        for (int i = 0; i < fullModules; i++)
        {
            Vector2Int origin = area.Origin;

            if (vertical)
                origin.y += i * ModuleSize;
            else
                origin.x += i * ModuleSize;

            AddModule(origin, ModuleSize, ModuleSize, false, vertical);
        }

        int remainderStart = fullModules * ModuleSize;

        for (int i = 0; i < remainder; i++)
        {
            Vector2Int origin = area.Origin;

            if (vertical)
            {
                origin.y += remainderStart + i;
                AddModule(origin, ModuleSize, 1, true, true);
            }
            else
            {
                origin.x += remainderStart + i;
                AddModule(origin, 1, ModuleSize, true, false);
            }
        }
    }

    private void AddModule(
        Vector2Int origin,
        int width,
        int depth,
        bool isSmallStraight,
        bool isVerticalRun = false)
    {
        modules.Add(new VisualModule(
            origin, width, depth, isSmallStraight, isVerticalRun));
    }

    private List<JunctionCandidate> FindJunctionCandidates()
    {
        List<JunctionCandidate> candidates =
            new List<JunctionCandidate>();

        for (int i = 0; i < placementAreas.Count; i++)
        {
            for (int j = i + 1; j < placementAreas.Count; j++)
            {
                PlacementArea a = placementAreas[i];
                PlacementArea b = placementAreas[j];

                bool aSquare = IsSquareArea(a);
                bool bSquare = IsSquareArea(b);

                bool aCanBeVertical = a.Width == ModuleSize;
                bool aCanBeHorizontal = a.Depth == ModuleSize;
                bool bCanBeVertical = b.Width == ModuleSize;
                bool bCanBeHorizontal = b.Depth == ModuleSize;

                if (aCanBeVertical && bCanBeHorizontal &&
                    !(aSquare && bSquare))
                {
                    TryAddJunctionCandidate(
                        a, b, candidates,
                        $"Area A treated as vertical and area B as " +
                        $"horizontal. Pair indices {i} and {j}.");
                }

                if (bCanBeVertical && aCanBeHorizontal &&
                    !(aSquare && bSquare))
                {
                    TryAddJunctionCandidate(
                        b, a, candidates,
                        $"Area B treated as vertical and area A as " +
                        $"horizontal. Pair indices {j} and {i}.");
                }
            }
        }

        return candidates;
    }

    private bool IsSquareArea(PlacementArea area)
    {
        return area.Width == ModuleSize &&
               area.Depth == ModuleSize;
    }

    private void TryAddJunctionCandidate(
        PlacementArea vertical,
        PlacementArea horizontal,
        List<JunctionCandidate> candidates,
        string pairReason)
    {
        int overlapMinX = Mathf.Max(
            vertical.Origin.x, horizontal.Origin.x);
        int overlapMaxX = Mathf.Min(
            vertical.MaxX, horizontal.MaxX);
        int overlapMinZ = Mathf.Max(
            vertical.Origin.y, horizontal.Origin.y);
        int overlapMaxZ = Mathf.Min(
            vertical.MaxZ, horizontal.MaxZ);

        int overlapWidth = overlapMaxX - overlapMinX;
        int overlapDepth = overlapMaxZ - overlapMinZ;

        if (overlapWidth >= ModuleSize &&
            overlapDepth >= ModuleSize)
        {
            Vector2Int candidateOrigin =
                new Vector2Int(overlapMinX, overlapMinZ);

            TryAddCandidateAtOrigin(
                candidateOrigin, candidates,
                "Crossing/overlap: intersection is at least 5x5. " +
                pairReason, vertical, horizontal);
            return;
        }

        bool touchesVerticalSide =
            horizontal.MaxX == vertical.Origin.x ||
            horizontal.Origin.x == vertical.MaxX;

        bool horizontalSpanInsideVertical =
            horizontal.Origin.y >= vertical.Origin.y &&
            horizontal.MaxZ <= vertical.MaxZ &&
            horizontal.Depth == ModuleSize;

        if (touchesVerticalSide && horizontalSpanInsideVertical)
        {
            Vector2Int candidateOrigin =
                new Vector2Int(
                    vertical.Origin.x, horizontal.Origin.y);

            TryAddCandidateAtOrigin(
                candidateOrigin, candidates,
                "Horizontal branch touches the side of a vertical " +
                "corridor and its 5-cell depth is within that corridor. " +
                pairReason, vertical, horizontal);
            return;
        }

        bool touchesHorizontalEnd =
            vertical.MaxZ == horizontal.Origin.y ||
            vertical.Origin.y == horizontal.MaxZ;

        bool verticalSpanInsideHorizontal =
            vertical.Origin.x >= horizontal.Origin.x &&
            vertical.MaxX <= horizontal.MaxX &&
            vertical.Width == ModuleSize;

        if (touchesHorizontalEnd && verticalSpanInsideHorizontal)
        {
            Vector2Int candidateOrigin =
                new Vector2Int(
                    vertical.Origin.x, horizontal.Origin.y);

            TryAddCandidateAtOrigin(
                candidateOrigin, candidates,
                "Vertical branch touches the end of a horizontal " +
                "corridor and its 5-cell width is within that corridor. " +
                pairReason, vertical, horizontal);
        }
    }

    private void TryAddCandidateAtOrigin(
        Vector2Int origin,
        List<JunctionCandidate> candidates,
        string reason,
        PlacementArea vertical,
        PlacementArea horizontal)
    {
        foreach (JunctionCandidate existing in candidates)
        {
            if (existing.Origin == origin)
            {
                if (logJunctionDiagnostics)
                {
                    Debug.Log(
                        "[Taxiway Junction Diagnostics] Candidate " +
                        $"at {origin} was not added again. " +
                        "A candidate at this origin already exists.\n" +
                        $"Existing reason: {existing.DetectionReason}\n" +
                        $"Additional reason: {reason}", this);
                }

                return;
            }
        }

        int mask = CalculateAreaConnectionMask(origin);

        if (!IsJunctionMask(mask))
        {
            if (logJunctionDiagnostics)
            {
                Debug.Log(
                    "[Taxiway Junction Diagnostics] Candidate REJECTED\n" +
                    $"Origin: {origin}, footprint: 5x5\n" +
                    $"Detection rule: {reason}\n" +
                    $"Area-derived mask: {DescribeMask(mask)}\n" +
                    $"Connection count: {CountConnections(mask)}\n" +
                    "Reason rejected: mask is neither a T/intersection " +
                    "with at least 3 connections nor a corner.", this);
            }

            return;
        }

        JunctionCandidate candidate = new JunctionCandidate(
            origin, mask, reason);

        candidates.Add(candidate);

        if (logJunctionDiagnostics)
        {
            Debug.Log(
                "[Taxiway Junction Diagnostics] Candidate ACCEPTED\n" +
                $"Origin: {origin}, footprint: 5x5\n" +
                $"Detection rule: {reason}\n" +
                $"Vertical area: {vertical}\n" +
                $"Horizontal area: {horizontal}\n" +
                $"Area-derived mask: {DescribeMask(mask)}\n" +
                $"Connection count: {CountConnections(mask)}\n" +
                $"Mask directions: {GetConnectionDirections(mask)}\n" +
                $"Adjacent grid coordinates:\n" +
                GetAdjacentGridCoordinates(origin) +
                "Relevant registered areas:\n" +
                GetRelevantPlacementAreas(origin), this);
        }
    }


    private int CalculateAreaConnectionMask(Vector2Int origin)
    {
        int mask = 0;

        int maxX = origin.x + ModuleSize;
        int maxZ = origin.y + ModuleSize;

        foreach (PlacementArea area in placementAreas)
        {
            bool overlapsZ =
                area.Origin.y < maxZ &&
                area.MaxZ > origin.y;

            bool overlapsX =
                area.Origin.x < maxX &&
                area.MaxX > origin.x;

            // West: the area must reach or cross the module's west edge.
            if (overlapsZ &&
                area.Origin.x < origin.x &&
                area.MaxX >= origin.x)
            {
                mask |= West;
            }

            // East: the area must reach or cross the module's east edge.
            if (overlapsZ &&
                area.Origin.x <= maxX &&
                area.MaxX > maxX)
            {
                mask |= East;
            }

            // South: the area must reach or cross the module's south edge.
            if (overlapsX &&
                area.Origin.y < origin.y &&
                area.MaxZ >= origin.y)
            {
                mask |= South;
            }

            // North: the area must reach or cross the module's north edge.
            if (overlapsX &&
                area.Origin.y <= maxZ &&
                area.MaxZ > maxZ)
            {
                mask |= North;
            }
        }

        return mask;
    }


    private void ApplyJunctionFootprints(
        List<JunctionCandidate> junctions)
    {
        List<VisualModule> rebuilt = new List<VisualModule>();

        foreach (VisualModule module in modules)
        {
            bool overlapsJunction = false;

            foreach (JunctionCandidate junction in junctions)
            {
                if (RectanglesOverlap(
                    module.Origin, module.Width, module.Depth,
                    junction.Origin, ModuleSize, ModuleSize))
                {
                    overlapsJunction = true;
                    break;
                }
            }

            if (!overlapsJunction)
            {
                rebuilt.Add(module);
                continue;
            }

            if (module.IsSmallStraight)
                continue;

            if (module.IsVerticalRun)
            {
                for (int z = module.Origin.y; z < module.MaxZ; z++)
                {
                    Vector2Int stripOrigin =
                        new Vector2Int(module.Origin.x, z);

                    if (OverlapsAnyJunction(
                        stripOrigin, ModuleSize, 1, junctions))
                        continue;

                    rebuilt.Add(new VisualModule(
                        stripOrigin, ModuleSize, 1, true, true));
                }
            }
            else
            {
                for (int x = module.Origin.x; x < module.MaxX; x++)
                {
                    Vector2Int stripOrigin =
                        new Vector2Int(x, module.Origin.y);

                    if (OverlapsAnyJunction(
                        stripOrigin, 1, ModuleSize, junctions))
                        continue;

                    rebuilt.Add(new VisualModule(
                        stripOrigin, 1, ModuleSize, true, false));
                }
            }
        }

        foreach (JunctionCandidate junction in junctions)
        {
            rebuilt.Add(new VisualModule(
                junction.Origin,
                ModuleSize,
                ModuleSize,
                false,
                false,
                true,
                junction.ConnectionMask));
        }

        modules.Clear();
        modules.AddRange(rebuilt);
    }

    private bool OverlapsAnyJunction(
        Vector2Int origin,
        int width,
        int depth,
        List<JunctionCandidate> junctions)
    {
        foreach (JunctionCandidate junction in junctions)
        {
            if (RectanglesOverlap(
                origin, width, depth,
                junction.Origin, ModuleSize, ModuleSize))
                return true;
        }

        return false;
    }

    private bool RectanglesOverlap(
        Vector2Int aOrigin,
        int aWidth,
        int aDepth,
        Vector2Int bOrigin,
        int bWidth,
        int bDepth)
    {
        return aOrigin.x < bOrigin.x + bWidth &&
               aOrigin.x + aWidth > bOrigin.x &&
               aOrigin.y < bOrigin.y + bDepth &&
               aOrigin.y + aDepth > bOrigin.y;
    }

    private void RemoveExactDuplicateModules()
    {
        for (int i = modules.Count - 1; i >= 0; i--)
        {
            VisualModule candidate = modules[i];
            bool duplicateFound = false;

            for (int j = 0; j < i; j++)
            {
                VisualModule existing = modules[j];

                if (candidate.Origin == existing.Origin &&
                    candidate.Width == existing.Width &&
                    candidate.Depth == existing.Depth)
                {
                    duplicateFound = true;
                    break;
                }
            }

            if (duplicateFound)
                modules.RemoveAt(i);
        }
    }

    private void MergeCompleteStraightBlocks()
    {
        HashSet<Vector2Int> candidateOrigins =
            new HashSet<Vector2Int>();

        foreach (VisualModule module in modules)
        {
            if (!module.IsSmallStraight)
                continue;

            if (module.Width == ModuleSize && module.Depth == 1)
            {
                for (int offset = 0; offset < ModuleSize; offset++)
                {
                    candidateOrigins.Add(new Vector2Int(
                        module.Origin.x, module.Origin.y - offset));
                }
            }

            if (module.Width == 1 && module.Depth == ModuleSize)
            {
                for (int offset = 0; offset < ModuleSize; offset++)
                {
                    candidateOrigins.Add(new Vector2Int(
                        module.Origin.x - offset, module.Origin.y));
                }
            }
        }

        foreach (Vector2Int origin in candidateOrigins)
        {
            if (!CanMergeIntoStraightBlock(origin))
                continue;

            for (int i = modules.Count - 1; i >= 0; i--)
            {
                VisualModule module = modules[i];

                if (!module.IsSmallStraight)
                    continue;

                bool fullyInside =
                    module.Origin.x >= origin.x &&
                    module.Origin.y >= origin.y &&
                    module.MaxX <= origin.x + ModuleSize &&
                    module.MaxZ <= origin.y + ModuleSize;

                if (fullyInside)
                    modules.RemoveAt(i);
            }

            modules.Add(new VisualModule(
                origin, ModuleSize, ModuleSize, false));
        }
    }

    private bool CanMergeIntoStraightBlock(Vector2Int origin)
    {
        int maxX = origin.x + ModuleSize;
        int maxZ = origin.y + ModuleSize;

        foreach (VisualModule module in modules)
        {
            bool overlaps = RectanglesOverlap(
                origin, ModuleSize, ModuleSize,
                module.Origin, module.Width, module.Depth);

            if (!overlaps)
                continue;

            if (module.IsJunctionFootprint || !module.IsSmallStraight)
                return false;

            bool fullyInside =
                module.Origin.x >= origin.x &&
                module.Origin.y >= origin.y &&
                module.MaxX <= maxX &&
                module.MaxZ <= maxZ;

            if (!fullyInside)
                return false;
        }

        for (int z = origin.y; z < maxZ; z++)
        {
            for (int x = origin.x; x < maxX; x++)
            {
                int coverageCount = 0;

                foreach (VisualModule module in modules)
                {
                    if (!module.IsSmallStraight)
                        continue;

                    bool coversCell =
                        x >= module.Origin.x &&
                        x < module.MaxX &&
                        z >= module.Origin.y &&
                        z < module.MaxZ;

                    if (coversCell)
                        coverageCount++;
                }

                if (coverageCount != 1)
                    return false;
            }
        }

        return true;
    }

    private void CalculateConnectionMasks()
    {
        foreach (VisualModule module in modules)
        {
            int mask = 0;

            foreach (VisualModule neighbor in modules)
            {
                if (module == neighbor)
                    continue;

                if (TouchesEast(module, neighbor)) mask |= East;
                if (TouchesWest(module, neighbor)) mask |= West;
                if (TouchesNorth(module, neighbor)) mask |= North;
                if (TouchesSouth(module, neighbor)) mask |= South;
            }

            if (module.IsJunctionFootprint)
                mask |= module.ForcedConnectionMask;

            module.ConnectionMask = mask;
        }
    }

    private bool TouchesEast(VisualModule a, VisualModule b)
    {
        return a.MaxX == b.Origin.x &&
               IntervalsOverlap(a.Origin.y, a.MaxZ, b.Origin.y, b.MaxZ);
    }

    private bool TouchesWest(VisualModule a, VisualModule b)
    {
        return a.Origin.x == b.MaxX &&
               IntervalsOverlap(a.Origin.y, a.MaxZ, b.Origin.y, b.MaxZ);
    }

    private bool TouchesNorth(VisualModule a, VisualModule b)
    {
        return a.MaxZ == b.Origin.y &&
               IntervalsOverlap(a.Origin.x, a.MaxX, b.Origin.x, b.MaxX);
    }

    private bool TouchesSouth(VisualModule a, VisualModule b)
    {
        return a.Origin.y == b.MaxZ &&
               IntervalsOverlap(a.Origin.x, a.MaxX, b.Origin.x, b.MaxX);
    }

    private bool IntervalsOverlap(int aMin, int aMax, int bMin, int bMax)
    {
        return aMin < bMax && bMin < aMax;
    }

    private void SpawnAllModules()
    {
        foreach (VisualModule module in modules)
        {
            if (module.IsJunctionFootprint)
                SpawnModule(module);
        }

        foreach (VisualModule module in modules)
        {
            if (!module.IsJunctionFootprint)
                SpawnModule(module);
        }
    }

    private void SpawnModule(VisualModule module)
    {
        GameObject prefab;
        int rotationY = 0;
        int rotationOffset = 0;

        if (module.IsJunctionFootprint)
        {
            prefab = SelectPrefab(
                module.ConnectionMask,
                out rotationY,
                out rotationOffset);

            rotationY += rotationOffset;

            if (logJunctionDiagnostics)
            {
                Debug.Log(
                    "[Taxiway Junction Diagnostics] FINAL PREFAB\n" +
                    $"Origin: {module.Origin}, size: " +
                    $"{module.Width}x{module.Depth}\n" +
                    $"Final mask: {DescribeMask(module.ConnectionMask)}\n" +
                    $"Final connection count: " +
                    $"{CountConnections(module.ConnectionMask)}\n" +
                    $"Final directions: " +
                    $"{GetConnectionDirections(module.ConnectionMask)}\n" +
                    $"Prefab selected: " +
                    $"{(prefab != null ? prefab.name : "NULL")}\n" +
                    $"Base rotation: {rotationY - rotationOffset} degrees\n" +
                    $"Rotation offset: {rotationOffset} degrees\n" +
                    $"Final rotation: {rotationY} degrees\n" +
                    $"Adjacent grid coordinates:\n" +
                    GetAdjacentGridCoordinates(module.Origin), this);
            }
        }
        else if (module.IsSmallStraight)
        {
            prefab = straight5x1Prefab;

            if (module.Width == 1 && module.Depth == ModuleSize)
                rotationY = 90;
        }
        else
        {
            prefab = straight5x5Prefab;

            if (module.ConnectionMask == (East | West))
                rotationY = 90;
        }

        if (prefab == null)
        {
            Debug.LogError(
                "TaxiwayPrefabRuleManager: Required prefab is missing. " +
                "Module origin: " + module.Origin, this);
            return;
        }

        Vector3 worldPosition = GetModuleCenter(module);

        Quaternion rotation =
            Quaternion.Euler(0f, rotationY, 0f) *
            Quaternion.Euler(GetPrefabRotationOffset(prefab));

        GameObject instance = Instantiate(
            prefab, worldPosition, rotation, moduleParent);

        Vector3 prefabScale = prefab.transform.localScale;

        instance.transform.localScale = new Vector3(
            prefabScale.x * gridManager.CellSize,
            prefabScale.y,
            prefabScale.z * gridManager.CellSize);

        spawnedVisuals.Add(instance);
    }

    private GameObject SelectPrefab(
        int mask,
        out int rotationY,
        out int rotationOffset)
    {
        rotationY = 0;
        rotationOffset = 0;

        int count = CountConnections(mask);

        if (count >= 4)
        {
            rotationOffset = intersectionRotationOffset;
            return intersectionPrefab;
        }

        if (count == 3)
        {
            rotationY = FindRotation(mask, North | East | South);
            rotationOffset = tSectionRotationOffset;
            return tSectionPrefab;
        }

        if (IsCorner(mask))
        {
            rotationY = FindRotation(mask, North | East);
            rotationOffset = curvedRotationOffset;
            return curvedPrefab;
        }

        Debug.LogWarning(
            "TaxiwayPrefabRuleManager: Junction footprint at " +
            "mask " + mask + " does not describe a T, corner, or " +
            "intersection. Check the placement areas.", this);

        return null;
    }

    private bool IsJunctionMask(int mask)
    {
        return CountConnections(mask) >= 3 || IsCorner(mask);
    }

    private int CountConnections(int mask)
    {
        int count = 0;
        if ((mask & North) != 0) count++;
        if ((mask & East) != 0) count++;
        if ((mask & South) != 0) count++;
        if ((mask & West) != 0) count++;
        return count;
    }

    private bool IsCorner(int mask)
    {
        return mask == (North | East) ||
               mask == (East | South) ||
               mask == (South | West) ||
               mask == (West | North);
    }

    private int FindRotation(int targetMask, int baseMask)
    {
        for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
        {
            if (RotateMask(baseMask, quarterTurns) == targetMask)
                return quarterTurns * 90;
        }

        return 0;
    }

    private int RotateMask(int mask, int quarterTurns)
    {
        for (int i = 0; i < quarterTurns; i++)
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

    private Vector3 GetModuleCenter(VisualModule module)
    {
        Vector3 origin = gridManager.GridToWorld(module.Origin);

        return origin + new Vector3(
            module.Width * gridManager.CellSize * 0.5f,
            0f,
            module.Depth * gridManager.CellSize * 0.5f);
    }

    private Vector3 GetPrefabRotationOffset(GameObject prefab)
    {
        if (prefab == straight5x1Prefab)
            return straight5x1RotationOffset;

        if (prefab == straight5x5Prefab)
            return straight5x5RotationOffset;

        return Vector3.zero;
    }

    private string DescribeMask(int mask)
    {
        return $"{mask} ({GetConnectionDirections(mask)})";
    }

    private string GetConnectionDirections(int mask)
    {
        List<string> directions = new List<string>();

        if ((mask & North) != 0) directions.Add("North");
        if ((mask & East) != 0) directions.Add("East");
        if ((mask & South) != 0) directions.Add("South");
        if ((mask & West) != 0) directions.Add("West");

        return directions.Count == 0
            ? "None"
            : string.Join(", ", directions);
    }

    /// <summary>
    /// Lists the five grid cells immediately outside each side of the
    /// candidate footprint. These are coordinates, not occupancy claims.
    /// </summary>
    private string GetAdjacentGridCoordinates(Vector2Int origin)
    {
        StringBuilder result = new StringBuilder();

        result.Append("North: ");
        for (int x = origin.x; x < origin.x + ModuleSize; x++)
            result.Append($"({x},{origin.y + ModuleSize}) ");
        result.AppendLine();

        result.Append("East: ");
        for (int z = origin.y; z < origin.y + ModuleSize; z++)
            result.Append($"({origin.x + ModuleSize},{z}) ");
        result.AppendLine();

        result.Append("South: ");
        for (int x = origin.x; x < origin.x + ModuleSize; x++)
            result.Append($"({x},{origin.y - 1}) ");
        result.AppendLine();

        result.Append("West: ");
        for (int z = origin.y; z < origin.y + ModuleSize; z++)
            result.Append($"({origin.x - 1},{z}) ");
        result.AppendLine();

        return result.ToString();
    }

    /// <summary>
    /// Lists registered areas that overlap or touch the candidate
    /// footprint. The mask logic uses these placement-area boundaries.
    /// </summary>
    private string GetRelevantPlacementAreas(Vector2Int origin)
    {
        StringBuilder result = new StringBuilder();
        int maxX = origin.x + ModuleSize;
        int maxZ = origin.y + ModuleSize;
        int found = 0;

        for (int i = 0; i < placementAreas.Count; i++)
        {
            PlacementArea area = placementAreas[i];

            bool overlapsOrTouches =
                area.Origin.x <= maxX &&
                area.MaxX >= origin.x &&
                area.Origin.y <= maxZ &&
                area.MaxZ >= origin.y;

            if (!overlapsOrTouches)
                continue;

            result.AppendLine($"  Area #{i}: {area}");
            found++;
        }

        if (found == 0)
            result.AppendLine("  No relevant registered areas found.");

        return result.ToString();
    }

    private void LogFinalJunctionDecisions()
    {
        foreach (VisualModule module in modules)
        {
            if (!module.IsJunctionFootprint)
                continue;

            Debug.Log(
                "[Taxiway Junction Diagnostics] FINAL JUNCTION MODULE\n" +
                $"Origin: {module.Origin}, size: " +
                $"{module.Width}x{module.Depth}\n" +
                $"Forced mask from placement areas: " +
                $"{DescribeMask(module.ForcedConnectionMask)}\n" +
                $"Calculated final mask: " +
                $"{DescribeMask(module.ConnectionMask)}\n" +
                $"Final connection count: " +
                $"{CountConnections(module.ConnectionMask)}\n" +
                $"Adjacent grid coordinates:\n" +
                GetAdjacentGridCoordinates(module.Origin), this);
        }
    }

    public bool TryGetVisualModuleAtCell(
        Vector2Int cell,
        out Vector2Int origin,
        out int width,
        out int depth)
    {
        foreach (VisualModule module in modules)
        {
            if (cell.x >= module.Origin.x &&
                cell.x < module.MaxX &&
                cell.y >= module.Origin.y &&
                cell.y < module.MaxZ)
            {
                origin = module.Origin;
                width = module.Width;
                depth = module.Depth;
                return true;
            }
        }

        origin = Vector2Int.zero;
        width = 0;
        depth = 0;
        return false;
    }

    public bool RemoveVisualModulesAtCells(IEnumerable<Vector2Int> cells)
    {
        if (cells == null)
            return true;

        List<VisualModule> modulesToRemove =
            new List<VisualModule>();

        foreach (Vector2Int cell in cells)
        {
            foreach (VisualModule module in modules)
            {
                bool containsCell =
                    cell.x >= module.Origin.x &&
                    cell.x < module.MaxX &&
                    cell.y >= module.Origin.y &&
                    cell.y < module.MaxZ;

                if (!containsCell)
                    continue;

                bool alreadyAdded = false;

                foreach (VisualModule existing in modulesToRemove)
                {
                    if (existing.Origin == module.Origin &&
                        existing.Width == module.Width &&
                        existing.Depth == module.Depth)
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                    modulesToRemove.Add(module);

                break;
            }
        }

        if (modulesToRemove.Count == 0)
            return true;

        List<PlacementArea> updatedAreas =
            new List<PlacementArea>(placementAreas);

        foreach (VisualModule module in modulesToRemove)
        {
            List<PlacementArea> nextAreas =
                new List<PlacementArea>();

            foreach (PlacementArea area in updatedAreas)
            {
                if (!TrySubtractModuleFromArea(
                    area, module, out List<PlacementArea> remainingAreas))
                {
                    Debug.LogWarning(
                        "TaxiwayPrefabRuleManager: Cannot safely remove " +
                        "module at " + module.Origin +
                        ". No taxiway visuals were removed.", this);
                    return false;
                }

                nextAreas.AddRange(remainingAreas);
            }

            updatedAreas = nextAreas;
        }

        placementAreas.Clear();
        placementAreas.AddRange(updatedAreas);

        RebuildVisuals();

        Debug.Log(
            "TaxiwayPrefabRuleManager: Removed " +
            modulesToRemove.Count + " visual taxiway module(s).", this);

        return true;
    }

    private bool TrySubtractModuleFromArea(
        PlacementArea area,
        VisualModule module,
        out List<PlacementArea> remainingAreas)
    {
        remainingAreas = new List<PlacementArea>();

        int overlapMinX = Mathf.Max(area.Origin.x, module.Origin.x);
        int overlapMaxX = Mathf.Min(area.MaxX, module.MaxX);
        int overlapMinZ = Mathf.Max(area.Origin.y, module.Origin.y);
        int overlapMaxZ = Mathf.Min(area.MaxZ, module.MaxZ);

        if (overlapMinX >= overlapMaxX ||
            overlapMinZ >= overlapMaxZ)
        {
            remainingAreas.Add(area);
            return true;
        }

        bool coversFullWidth =
            overlapMinX == area.Origin.x &&
            overlapMaxX == area.MaxX;

        bool coversFullDepth =
            overlapMinZ == area.Origin.y &&
            overlapMaxZ == area.MaxZ;

        if (coversFullWidth)
        {
            int beforeDepth = overlapMinZ - area.Origin.y;

            if (beforeDepth > 0)
                remainingAreas.Add(new PlacementArea(
                    area.Origin, area.Width, beforeDepth));

            int afterDepth = area.MaxZ - overlapMaxZ;

            if (afterDepth > 0)
                remainingAreas.Add(new PlacementArea(
                    new Vector2Int(area.Origin.x, overlapMaxZ),
                    area.Width, afterDepth));

            return true;
        }

        if (coversFullDepth)
        {
            int beforeWidth = overlapMinX - area.Origin.x;

            if (beforeWidth > 0)
                remainingAreas.Add(new PlacementArea(
                    area.Origin, beforeWidth, area.Depth));

            int afterWidth = area.MaxX - overlapMaxX;

            if (afterWidth > 0)
                remainingAreas.Add(new PlacementArea(
                    new Vector2Int(overlapMaxX, area.Origin.y),
                    afterWidth, area.Depth));

            return true;
        }

        return false;
    }

    private void ClearSpawnedVisuals()
    {
        foreach (GameObject visual in spawnedVisuals)
        {
            if (visual == null)
                continue;

            if (Application.isPlaying)
                Destroy(visual);
            else
                DestroyImmediate(visual);
        }

        spawnedVisuals.Clear();
    }

    public List<TaxiwayAreaSaveData> GetPlacementAreasForSave()
    {
        List<TaxiwayAreaSaveData> result =
            new List<TaxiwayAreaSaveData>();

        foreach (PlacementArea area in placementAreas)
        {
            result.Add(new TaxiwayAreaSaveData
            {
                originX = area.Origin.x,
                originZ = area.Origin.y,
                width = area.Width,
                depth = area.Depth
            });
        }

        return result;
    }

    public void RestorePlacementAreas(
        IEnumerable<TaxiwayAreaSaveData> savedAreas)
    {
        ClearAllTaxiwayAreas();

        if (savedAreas == null)
        {
            Debug.LogWarning(
                "TaxiwayPrefabRuleManager: No saved taxiway areas were provided.",
                this);
            return;
        }

        foreach (TaxiwayAreaSaveData saved in savedAreas)
        {
            if (saved == null ||
                saved.width <= 0 ||
                saved.depth <= 0 ||
                (saved.width != 5 && saved.depth != 5))
            {
                Debug.LogWarning(
                    "TaxiwayPrefabRuleManager: Skipping invalid saved taxiway area.",
                    this);
                continue;
            }

            placementAreas.Add(
                new PlacementArea(
                    new Vector2Int(saved.originX, saved.originZ),
                    saved.width,
                    saved.depth));
        }

        RebuildVisuals();
    }
}
