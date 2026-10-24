
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds taxiway visuals from registered placement areas.
///
/// Junction footprints are identified before straight modules are
/// generated. Junctions may cross the original 5x5 module boundaries.
/// Straight areas displaced by a junction are filled with 5x1 prefabs.
///
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
    }

    private sealed class JunctionCandidate
    {
        public Vector2Int Origin;
        public int ConnectionMask;

        public JunctionCandidate(Vector2Int origin, int connectionMask)
        {
            Origin = origin;
            ConnectionMask = connectionMask;
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

    /// <summary>
    /// Registers a successfully placed taxiway rectangle.
    /// One dimension must be five cells.
    /// </summary>
    public void RegisterTaxiwayArea(
        Vector2Int origin,
        int areaWidth,
        int areaDepth)
    {
        if (gridManager == null)
        {
            Debug.LogError(
                "TaxiwayPrefabRuleManager: GridManager is missing.",
                this);
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
                areaWidth + "x" + areaDepth + ".",
                this);
            return;
        }

        placementAreas.Add(
            new PlacementArea(origin, areaWidth, areaDepth));

        RebuildVisuals();
    }

    /// <summary>
    /// Rebuilds all taxiway visuals from the registered areas.
    /// </summary>
    public void RebuildVisuals()
    {
        ClearSpawnedVisuals();
        modules.Clear();

        // First find junctions independently of the old 5x5 boundaries.
        List<JunctionCandidate> junctions = FindJunctionCandidates();

        // Build the normal 5x5 modules and 5x1 remainders.
        foreach (PlacementArea area in placementAreas)
        {
            BuildModulesForArea(area);
        }

        RemoveExactDuplicateModules();

        // Remove straight modules that overlap junction footprints.
        // The uncovered portions of affected 5x5 modules become 5x1s.
        ApplyJunctionFootprints(junctions);

        // Merge complete groups of five 5x1 strips into 5x5 modules.
        // Junction footprints and incomplete areas are left untouched.
        MergeCompleteStraightBlocks();

        // Determine final connections after the layout is rebuilt.
        CalculateConnectionMasks();

        // Spawn junctions first so their visuals take priority.
        SpawnAllModules();
    }

    /// <summary>
    /// Clears this manager's placement records and spawned visuals.
    /// Does not clear other taxiway or gameplay data.
    /// </summary>
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

            AddModule(
                origin,
                ModuleSize,
                ModuleSize,
                false,
                vertical);
        }

        int remainderStart = fullModules * ModuleSize;

        for (int i = 0; i < remainder; i++)
        {
            Vector2Int origin = area.Origin;

            if (vertical)
            {
                origin.y += remainderStart + i;

                AddModule(
                    origin,
                    ModuleSize,
                    1,
                    true,
                    true);
            }
            else
            {
                origin.x += remainderStart + i;

                AddModule(
                    origin,
                    1,
                    ModuleSize,
                    true,
                    false);
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
        modules.Add(
            new VisualModule(
                origin,
                width,
                depth,
                isSmallStraight,
                isVerticalRun));
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

                // Treat one square area as the vertical run when the
                // other area is horizontal, and vice versa.
                bool aCanBeVertical = a.Width == ModuleSize;
                bool aCanBeHorizontal = a.Depth == ModuleSize;
                bool bCanBeVertical = b.Width == ModuleSize;
                bool bCanBeHorizontal = b.Depth == ModuleSize;

                if (aCanBeVertical && bCanBeHorizontal &&
                    !(aSquare && bSquare))
                {
                    TryAddJunctionCandidate(a, b, candidates);
                }

                if (bCanBeVertical && aCanBeHorizontal &&
                    !(aSquare && bSquare))
                {
                    TryAddJunctionCandidate(b, a, candidates);
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

    /// <summary>
    /// The first area is treated as a vertical run (width 5).
    /// The second is treated as a horizontal run (depth 5).
    ///
    /// A junction can be inside their overlap, or inside the main
    /// corridor where the perpendicular branch meets its side.
    /// </summary>
    private void TryAddJunctionCandidate(
        PlacementArea vertical,
        PlacementArea horizontal,
        List<JunctionCandidate> candidates)
    {
        Vector2Int candidateOrigin;

        int overlapMinX = Mathf.Max(
            vertical.Origin.x,
            horizontal.Origin.x);

        int overlapMaxX = Mathf.Min(
            vertical.MaxX,
            horizontal.MaxX);

        int overlapMinZ = Mathf.Max(
            vertical.Origin.y,
            horizontal.Origin.y);

        int overlapMaxZ = Mathf.Min(
            vertical.MaxZ,
            horizontal.MaxZ);

        int overlapWidth = overlapMaxX - overlapMinX;
        int overlapDepth = overlapMaxZ - overlapMinZ;

        // Crossing or overlapping placement areas.
        if (overlapWidth >= ModuleSize &&
            overlapDepth >= ModuleSize)
        {
            candidateOrigin = new Vector2Int(
                overlapMinX,
                overlapMinZ);

            TryAddCandidateAtOrigin(candidateOrigin, candidates);
            return;
        }

        // Horizontal branch touches the side of a vertical corridor.
        bool touchesVerticalSide =
            horizontal.MaxX == vertical.Origin.x ||
            horizontal.Origin.x == vertical.MaxX;

        bool horizontalSpanInsideVertical =
            horizontal.Origin.y >= vertical.Origin.y &&
            horizontal.MaxZ <= vertical.MaxZ &&
            horizontal.Depth == ModuleSize;

        if (touchesVerticalSide && horizontalSpanInsideVertical)
        {
            candidateOrigin = new Vector2Int(
                vertical.Origin.x,
                horizontal.Origin.y);

            TryAddCandidateAtOrigin(candidateOrigin, candidates);
            return;
        }

        // Vertical branch touches the end of a horizontal corridor.
        bool touchesHorizontalEnd =
            vertical.MaxZ == horizontal.Origin.y ||
            vertical.Origin.y == horizontal.MaxZ;

        bool verticalSpanInsideHorizontal =
            vertical.Origin.x >= horizontal.Origin.x &&
            vertical.MaxX <= horizontal.MaxX &&
            vertical.Width == ModuleSize;

        if (touchesHorizontalEnd && verticalSpanInsideHorizontal)
        {
            candidateOrigin = new Vector2Int(
                vertical.Origin.x,
                horizontal.Origin.y);

            TryAddCandidateAtOrigin(candidateOrigin, candidates);
        }
    }

    private void TryAddCandidateAtOrigin(
        Vector2Int origin,
        List<JunctionCandidate> candidates)
    {
        foreach (JunctionCandidate existing in candidates)
        {
            if (existing.Origin == origin)
                return;
        }

        int mask = CalculateAreaConnectionMask(origin);

        // Do not reserve a junction footprint unless the registered
        // areas actually describe a junction or corner here.
        if (!IsJunctionMask(mask))
            return;

        candidates.Add(
            new JunctionCandidate(origin, mask));
    }

    /// <summary>
    /// Calculates the connections implied by the original placement
    /// areas, including areas that touch the candidate at an edge.
    /// </summary>
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

            if (overlapsZ)
            {
                if (area.MaxX == origin.x ||
                    area.Origin.x < origin.x)
                {
                    mask |= West;
                }

                if (area.Origin.x == maxX ||
                    area.MaxX > maxX)
                {
                    mask |= East;
                }
            }

            if (overlapsX)
            {
                if (area.MaxZ == origin.y ||
                    area.Origin.y < origin.y)
                {
                    mask |= South;
                }

                if (area.Origin.y == maxZ ||
                    area.MaxZ > maxZ)
                {
                    mask |= North;
                }
            }
        }

        return mask;
    }

    /// <summary>
    /// Removes straight modules overlapping junction footprints.
    /// If a 5x5 module is cut by a junction, its remaining cells
    /// are rebuilt as 5x1 strips along the original run direction.
    /// </summary>
    private void ApplyJunctionFootprints(
        List<JunctionCandidate> junctions)
    {
        List<VisualModule> rebuilt =
            new List<VisualModule>();

        foreach (VisualModule module in modules)
        {
            bool overlapsJunction = false;

            foreach (JunctionCandidate junction in junctions)
            {
                if (RectanglesOverlap(
                    module.Origin,
                    module.Width,
                    module.Depth,
                    junction.Origin,
                    ModuleSize,
                    ModuleSize))
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

            // A small strip intersecting a junction is covered by it.
            if (module.IsSmallStraight)
                continue;

            // Split a vertical-run 5x5 module into 5x1 rows.
            if (module.IsVerticalRun)
            {
                for (int z = module.Origin.y;
                     z < module.MaxZ;
                     z++)
                {
                    Vector2Int stripOrigin =
                        new Vector2Int(module.Origin.x, z);

                    if (OverlapsAnyJunction(
                        stripOrigin,
                        ModuleSize,
                        1,
                        junctions))
                    {
                        continue;
                    }

                    rebuilt.Add(
                        new VisualModule(
                            stripOrigin,
                            ModuleSize,
                            1,
                            true,
                            true));
                }
            }
            else
            {
                // Split a horizontal-run 5x5 module into 1x5 columns.
                for (int x = module.Origin.x;
                     x < module.MaxX;
                     x++)
                {
                    Vector2Int stripOrigin =
                        new Vector2Int(x, module.Origin.y);

                    if (OverlapsAnyJunction(
                        stripOrigin,
                        1,
                        ModuleSize,
                        junctions))
                    {
                        continue;
                    }

                    rebuilt.Add(
                        new VisualModule(
                            stripOrigin,
                            1,
                            ModuleSize,
                            true,
                            false));
                }
            }
        }

        // Add each junction exactly once.
        foreach (JunctionCandidate junction in junctions)
        {
            rebuilt.Add(
                new VisualModule(
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
                origin,
                width,
                depth,
                junction.Origin,
                ModuleSize,
                ModuleSize))
            {
                return true;
            }
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

        // Generate possible 5x5 origins from every small straight strip.
        foreach (VisualModule module in modules)
        {
            if (!module.IsSmallStraight)
                continue;

            // Five horizontal 5x1 strips can form one 5x5 block.
            if (module.Width == ModuleSize && module.Depth == 1)
            {
                for (int offset = 0; offset < ModuleSize; offset++)
                {
                    candidateOrigins.Add(
                        new Vector2Int(
                            module.Origin.x,
                            module.Origin.y - offset));
                }
            }

            // Five vertical 1x5 strips can form one 5x5 block.
            if (module.Width == 1 && module.Depth == ModuleSize)
            {
                for (int offset = 0; offset < ModuleSize; offset++)
                {
                    candidateOrigins.Add(
                        new Vector2Int(
                            module.Origin.x - offset,
                            module.Origin.y));
                }
            }
        }

        foreach (Vector2Int origin in candidateOrigins)
        {
            if (!CanMergeIntoStraightBlock(origin))
                continue;

            // Remove the five strips that completely fill this block.
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

            // Add one full-size straight module in their place.
            modules.Add(
                new VisualModule(
                    origin,
                    ModuleSize,
                    ModuleSize,
                    false));
        }
    }

    private bool CanMergeIntoStraightBlock(Vector2Int origin)
    {
        int maxX = origin.x + ModuleSize;
        int maxZ = origin.y + ModuleSize;

        // First reject junctions, full-size modules, and strips that
        // partially overlap the proposed block.
        foreach (VisualModule module in modules)
        {
            bool overlaps = RectanglesOverlap(
                origin,
                ModuleSize,
                ModuleSize,
                module.Origin,
                module.Width,
                module.Depth);

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

        // Verify that every cell in the 5x5 block is covered by
        // exactly one small straight strip.
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

                if (TouchesEast(module, neighbor))
                    mask |= East;

                if (TouchesWest(module, neighbor))
                    mask |= West;

                if (TouchesNorth(module, neighbor))
                    mask |= North;

                if (TouchesSouth(module, neighbor))
                    mask |= South;
            }

            // The original placement areas provide the intended
            // junction connections even if module boundaries changed.
            if (module.IsJunctionFootprint)
                mask |= module.ForcedConnectionMask;

            module.ConnectionMask = mask;
        }
    }

    private bool TouchesEast(
        VisualModule a,
        VisualModule b)
    {
        return a.MaxX == b.Origin.x &&
               IntervalsOverlap(
                   a.Origin.y, a.MaxZ,
                   b.Origin.y, b.MaxZ);
    }

    private bool TouchesWest(
        VisualModule a,
        VisualModule b)
    {
        return a.Origin.x == b.MaxX &&
               IntervalsOverlap(
                   a.Origin.y, a.MaxZ,
                   b.Origin.y, b.MaxZ);
    }

    private bool TouchesNorth(
        VisualModule a,
        VisualModule b)
    {
        return a.MaxZ == b.Origin.y &&
               IntervalsOverlap(
                   a.Origin.x, a.MaxX,
                   b.Origin.x, b.MaxX);
    }

    private bool TouchesSouth(
        VisualModule a,
        VisualModule b)
    {
        return a.Origin.y == b.MaxZ &&
               IntervalsOverlap(
                   a.Origin.x, a.MaxX,
                   b.Origin.x, b.MaxX);
    }

    private bool IntervalsOverlap(
        int aMin,
        int aMax,
        int bMin,
        int bMax)
    {
        return aMin < bMax && bMin < aMax;
    }

    private void SpawnAllModules()
    {
        // Junction footprints always spawn before the straight modules.
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
        }
        else if (module.IsSmallStraight)
        {
            prefab = straight5x1Prefab;

            if (module.Width == 1 &&
                module.Depth == ModuleSize)
            {
                rotationY = 90;
            }
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
                "Module origin: " + module.Origin,
                this);
            return;
        }

        Vector3 worldPosition = GetModuleCenter(module);

        Quaternion rotation =
            Quaternion.Euler(0f, rotationY, 0f) *
            Quaternion.Euler(GetPrefabRotationOffset(prefab));

        GameObject instance = Instantiate(
            prefab,
            worldPosition,
            rotation,
            moduleParent);

        Vector3 prefabScale = prefab.transform.localScale;

        instance.transform.localScale = new Vector3(
            prefabScale.x * gridManager.CellSize,
            prefabScale.y,
            prefabScale.z * gridManager.CellSize);

        spawnedVisuals.Add(instance);
    }

    private Vector3 GetPrefabRotationOffset(GameObject prefab)
    {
        if (prefab == straight5x1Prefab)
            return straight5x1RotationOffset;

        if (prefab == straight5x5Prefab)
            return straight5x5RotationOffset;

        return Vector3.zero;
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
            rotationY = FindRotation(
                mask,
                North | East | South);

            rotationOffset = tSectionRotationOffset;
            return tSectionPrefab;
        }

        if (IsCorner(mask))
        {
            rotationY = FindRotation(
                mask,
                North | East);

            rotationOffset = curvedRotationOffset;
            return curvedPrefab;
        }

        // A junction footprint with an unexpected mask should not
        // silently become a straight prefab.
        Debug.LogWarning(
            "TaxiwayPrefabRuleManager: Junction footprint at " +
            "mask " + mask + " does not describe a T, corner, or " +
            "intersection. Check the placement areas.",
            this);

        return null;
    }

    private bool IsJunctionMask(int mask)
    {
        int count = CountConnections(mask);
        return count >= 3 || IsCorner(mask);
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
}
