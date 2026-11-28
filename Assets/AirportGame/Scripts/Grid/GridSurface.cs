using System.Collections.Generic;
using UnityEngine;

public class GridSurface : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    public event System.Action FoundationSurfaceChanged;
    public event System.Action RunwaySurfaceChanged;

    [SerializeField]
    private GridManager gridManager;

    private BuildSurface[,] surfaces;

    // Shared, precomputed proximity map used by ground-vehicle pathfinding.
    // It is rebuilt lazily after relevant surface changes.
    private readonly Dictionary<int, bool[,]> groundVehicleClearanceMaps =
        new Dictionary<int, bool[,]>();

    private readonly Dictionary<Vector2Int, GameObject> surfaceOwners =
        new Dictionary<Vector2Int, GameObject>();

    private readonly Dictionary<Vector2Int, GameObject> obstacleOwners =
        new Dictionary<Vector2Int, GameObject>();


    private readonly Dictionary<Vector2Int, BuildSurface>
        taxiwayUnderlyingSurfaces =
            new Dictionary<Vector2Int, BuildSurface>();

    private readonly Dictionary<Vector2Int, BuildSurface>
        roadUnderlyingSurfaces =
            new Dictionary<Vector2Int, BuildSurface>();

    private readonly Dictionary<Vector2Int, BuildSurface>
    runwayUnderlyingSurfaces =
        new Dictionary<Vector2Int, BuildSurface>();


    private void Awake()
    {
        if (gridManager == null)
        {
            gridManager = GetComponent<GridManager>();
        }

        Initialize();
    }

    private void Initialize()
    {
        if (gridManager == null)
        {
            LogError(
                "GridSurface: GridManager is missing."
            );
            return;
        }

        surfaces = new BuildSurface[
            gridManager.Width,
            gridManager.Height
        ];

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
                surfaces[x, z] = BuildSurface.Grass;
        }

        surfaceOwners.Clear();
        obstacleOwners.Clear();
        taxiwayUnderlyingSurfaces.Clear();
        roadUnderlyingSurfaces.Clear();
        runwayUnderlyingSurfaces.Clear();
        InvalidateGroundVehicleClearanceMap();
    }

    public BuildSurface GetSurface(Vector2Int position)
    {
        if (gridManager == null || surfaces == null)
            return BuildSurface.Grass;

        if (!gridManager.IsInsideGrid(position))
            return BuildSurface.Grass;

        return surfaces[position.x, position.y];
    }

    /// <summary>
    /// Returns the shared precomputed proximity result for foundations and
    /// runways. Ownership exceptions are handled by the requesting vehicle.
    /// </summary>
    public bool IsWithinGroundVehicleClearance(
        Vector2Int position,
        int foundationClearance,
        int runwayClearance)
    {
        if (gridManager == null ||
            surfaces == null ||
            !gridManager.IsInsideGrid(position))
        {
            return false;
        }

        foundationClearance = Mathf.Clamp(foundationClearance, 0, 5);
        runwayClearance = Mathf.Clamp(runwayClearance, 0, 5);

        // Cache maps by both radii so vehicles with different settings can
        // safely share GridSurface without rebuilding each other's maps.
        int mapKey = foundationClearance * 6 + runwayClearance;

        if (!groundVehicleClearanceMaps.TryGetValue(
                mapKey,
                out bool[,] clearanceMap) ||
            clearanceMap.GetLength(0) != gridManager.Width ||
            clearanceMap.GetLength(1) != gridManager.Height)
        {
            clearanceMap = BuildGroundVehicleClearanceMap(
                foundationClearance,
                runwayClearance);
            groundVehicleClearanceMaps[mapKey] = clearanceMap;
        }

        return clearanceMap[position.x, position.y];
    }

    private bool[,] BuildGroundVehicleClearanceMap(
        int foundationClearance,
        int runwayClearance)
    {
        bool[,] clearanceMap = new bool[
            gridManager.Width,
            gridManager.Height];

        if (foundationClearance == 0 && runwayClearance == 0)
            return clearanceMap;

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                BuildSurface surface = surfaces[x, z];

                if (surface == BuildSurface.Foundation &&
                    foundationClearance > 0)
                {
                    MarkGroundVehicleClearanceArea(
                        clearanceMap,
                        x,
                        z,
                        foundationClearance);
                }

                if (surface == BuildSurface.Runway &&
                    runwayClearance > 0)
                {
                    MarkGroundVehicleClearanceArea(
                        clearanceMap,
                        x,
                        z,
                        runwayClearance);
                }
            }
        }

        return clearanceMap;
    }

    private void MarkGroundVehicleClearanceArea(
        bool[,] clearanceMap,
        int sourceX,
        int sourceZ,
        int radius)
    {
        int minX = Mathf.Max(0, sourceX - radius);
        int maxX = Mathf.Min(gridManager.Width - 1, sourceX + radius);
        int minZ = Mathf.Max(0, sourceZ - radius);
        int maxZ = Mathf.Min(gridManager.Height - 1, sourceZ + radius);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                // The original clearance rule checks neighbouring cells,
                // not the source cell itself.
                if (x == sourceX && z == sourceZ)
                    continue;

                clearanceMap[x, z] = true;
            }
        }
    }

    private void InvalidateGroundVehicleClearanceMap()
    {
        if (groundVehicleClearanceMaps.Count > 0)
            groundVehicleClearanceMaps.Clear();
    }

    public void SetSurface(Vector2Int position, BuildSurface surface)
    {
        if (gridManager == null)
        {
            LogError(
                "GridSurface: Cannot set a surface because GridManager is missing."
            );
            return;
        }

        if (surfaces == null)
            Initialize();

        if (surfaces == null ||
            !gridManager.IsInsideGrid(position))
            return;

        BuildSurface previousSurface = surfaces[position.x, position.y];

        // Roads are an overlay: keep them visible to placement restrictions
        // while allowing the underlying ground surface to be updated.
        if (roadUnderlyingSurfaces.TryGetValue(
                position,
                out BuildSurface underlyingRoadSurface))
        {
            roadUnderlyingSurfaces[position] = surface;

            if (surface == BuildSurface.Grass)
                surfaceOwners.Remove(position);

            if (underlyingRoadSurface == BuildSurface.Foundation ||
                surface == BuildSurface.Foundation)
            {
                FoundationSurfaceChanged?.Invoke();
            }

            return;
        }

        if (previousSurface == surface)
        {
            if (surface == BuildSurface.Grass)
                surfaceOwners.Remove(position);
            return;
        }

        surfaces[position.x, position.y] = surface;
        InvalidateGroundVehicleClearanceMap();

        if (surface == BuildSurface.Grass)
            surfaceOwners.Remove(position);

        if (previousSurface == BuildSurface.Foundation ||
            surface == BuildSurface.Foundation)
        {
            FoundationSurfaceChanged?.Invoke();
        }
    }

    public void SetSurfaceArea(
        Vector2Int origin,
        int width,
        int depth,
        BuildSurface surface)
    {
        SetSurfaceArea(origin, width, depth, surface, null);
    }

    public void SetSurfaceArea(
        Vector2Int origin,
        int width,
        int depth,
        BuildSurface surface,
        GameObject owner)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                SetSurface(cell, surface);

                if (surface != BuildSurface.Grass &&
                    owner != null &&
                    gridManager != null &&
                    gridManager.IsInsideGrid(cell))
                {
                    surfaceOwners[cell] = owner;
                }
            }
        }
    }

    public bool IsSurface(
        Vector2Int position,
        BuildSurface requiredSurface)
    {
        if (requiredSurface == BuildSurface.Any)
            return true;

        return GetSurface(position) == requiredSurface;
    }

    public bool CanSetSurfaceArea(
        Vector2Int origin,
        int width,
        int depth,
        BuildSurface requiredCurrentSurface)
    {
        if (gridManager == null)
            return false;

        if (surfaces == null)
            Initialize();

        if (surfaces == null)
            return false;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                if (!gridManager.IsInsideGrid(cell))
                    return false;

                if (GetSurface(cell) != requiredCurrentSurface)
                    return false;
            }
        }

        return true;
    }

    public bool CanSetSurfaceAreaFromSurfaces(
        Vector2Int origin,
        int width,
        int depth,
        BuildSurface firstAllowed,
        BuildSurface secondAllowed,
        BuildSurface thirdAllowed)
    {
        if (gridManager == null)
            return false;

        if (surfaces == null)
            Initialize();

        if (surfaces == null)
            return false;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);
                if (!gridManager.IsInsideGrid(cell))
                    return false;

                BuildSurface current = GetSurface(cell);
                if (current != firstAllowed &&
                    current != secondAllowed &&
                    current != thirdAllowed)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool IsSurfaceOwner(
        Vector2Int position,
        GameObject owner)
    {
        if (owner == null)
            return false;

        return surfaceOwners.TryGetValue(
                   position,
                   out GameObject currentOwner
               ) &&
               currentOwner == owner;
    }

    public void ReleaseSurfaceOwner(GameObject owner)
    {
        if (owner == null)
            return;

        List<Vector2Int> cellsToRelease =
            new List<Vector2Int>();

        foreach (KeyValuePair<Vector2Int, GameObject> entry
                 in surfaceOwners)
        {
            if (entry.Value == owner)
                cellsToRelease.Add(entry.Key);
        }

        foreach (Vector2Int cell in cellsToRelease)
        {
            surfaceOwners.Remove(cell);

            if (gridManager != null &&
                surfaces != null &&
                gridManager.IsInsideGrid(cell))
            {
                if (roadUnderlyingSurfaces.TryGetValue(
                        cell,
                        out BuildSurface underlyingRoadSurface))
                {
                    roadUnderlyingSurfaces[cell] = BuildSurface.Grass;

                    if (underlyingRoadSurface == BuildSurface.Foundation)
                        FoundationSurfaceChanged?.Invoke();

                    continue;
                }

                BuildSurface previousSurface = surfaces[cell.x, cell.y];
                surfaces[cell.x, cell.y] = BuildSurface.Grass;
                InvalidateGroundVehicleClearanceMap();

                if (previousSurface == BuildSurface.Foundation)
                    FoundationSurfaceChanged?.Invoke();
            }
        }
    }

    // Pathfinding obstacle API

    public bool IsObstacle(Vector2Int position)
    {
        if (gridManager == null ||
            !gridManager.IsInsideGrid(position))
        {
            return true;
        }

        return obstacleOwners.ContainsKey(position);
    }

    public bool IsObstacleOwnedBy(
    Vector2Int position,
    GameObject owner)
    {
        if (owner == null)
            return false;

        return obstacleOwners.TryGetValue(
                   position,
                   out GameObject currentOwner) &&
               currentOwner == owner;
    }

    public void SetObstacleArea(
        Vector2Int origin,
        int width,
        int depth,
        GameObject owner)
    {
        if (gridManager == null || owner == null)
            return;

        if (width <= 0 || depth <= 0)
            return;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                if (gridManager.IsInsideGrid(cell))
                {
                    obstacleOwners[cell] = owner;
                }
            }
        }
    }

    public void ReleaseObstacleOwner(GameObject owner)
    {
        if (owner == null)
            return;

        List<Vector2Int> cellsToRelease =
            new List<Vector2Int>();

        foreach (KeyValuePair<Vector2Int, GameObject> entry
                 in obstacleOwners)
        {
            if (entry.Value == owner)
                cellsToRelease.Add(entry.Key);
        }

        foreach (Vector2Int cell in cellsToRelease)
        {
            if (obstacleOwners.TryGetValue(
                    cell,
                    out GameObject currentOwner) &&
                currentOwner == owner)
            {
                obstacleOwners.Remove(cell);
            }
        }
    }

    public void ResetAllSurfaces()
    {
        if (gridManager == null || surfaces == null)
        {
            LogWarning(
                "GridSurface: Cannot reset because the grid is not initialized."
            );
            return;
        }

        bool foundationChanged = false;
        bool runwayChanged = false;

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                if (surfaces[x, z] == BuildSurface.Foundation)
                    foundationChanged = true;
                if (surfaces[x, z] == BuildSurface.Runway)
                    runwayChanged = true;

                surfaces[x, z] = BuildSurface.Grass;
                InvalidateGroundVehicleClearanceMap();
            }
        }

        if (foundationChanged)
            FoundationSurfaceChanged?.Invoke();
        if (runwayChanged)
            RunwaySurfaceChanged?.Invoke();

        surfaceOwners.Clear();
        obstacleOwners.Clear();
        taxiwayUnderlyingSurfaces.Clear();
        roadUnderlyingSurfaces.Clear();
        runwayUnderlyingSurfaces.Clear();
    }


    public GameObject GetSurfaceOwner(Vector2Int position)
    {
        if (surfaceOwners.TryGetValue(position, out GameObject owner))
            return owner;

        return null;
    }

    public bool RemoveSurfaceCell(Vector2Int position, GameObject owner)
    {
        if (gridManager == null ||
            surfaces == null ||
            owner == null ||
            !gridManager.IsInsideGrid(position))
        {
            return false;
        }

        if (!surfaceOwners.TryGetValue(position, out GameObject currentOwner) ||
            currentOwner != owner)
        {
            return false;
        }

        surfaceOwners.Remove(position);

        if (roadUnderlyingSurfaces.TryGetValue(
                position,
                out BuildSurface underlyingRoadSurface))
        {
            roadUnderlyingSurfaces[position] = BuildSurface.Grass;

            if (underlyingRoadSurface == BuildSurface.Foundation)
                FoundationSurfaceChanged?.Invoke();
        }
        else
        {
            BuildSurface previousSurface = surfaces[position.x, position.y];
            surfaces[position.x, position.y] = BuildSurface.Grass;
            InvalidateGroundVehicleClearanceMap();

            if (previousSurface == BuildSurface.Foundation)
                FoundationSurfaceChanged?.Invoke();
        }

        // Remove an obstacle only if this same object owns the cell.
        if (obstacleOwners.TryGetValue(position, out GameObject obstacleOwner) &&
            obstacleOwner == owner)
        {
            obstacleOwners.Remove(position);
        }

        return true;
    }

    public bool HasSurfaceCellsOwnedBy(GameObject owner)
    {
        if (owner == null)
            return false;

        foreach (KeyValuePair<Vector2Int, GameObject> entry in surfaceOwners)
        {
            if (entry.Value == owner)
                return true;
        }

        return false;
    }


    public void RegisterRoadArea(
        Vector2Int origin,
        int width,
        int depth)
    {
        if (gridManager == null)
            return;

        if (surfaces == null)
            Initialize();

        if (surfaces == null)
            return;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                if (gridManager == null ||
                    !gridManager.IsInsideGrid(cell))
                    continue;

                if (!roadUnderlyingSurfaces.ContainsKey(cell))
                {
                    BuildSurface underlying = surfaces[cell.x, cell.y];
                    roadUnderlyingSurfaces[cell] =
                        underlying == BuildSurface.Road
                            ? BuildSurface.Grass
                            : underlying;
                }

                BuildSurface previousSurface = surfaces[cell.x, cell.y];
                surfaces[cell.x, cell.y] = BuildSurface.Road;
                InvalidateGroundVehicleClearanceMap();

                if (previousSurface == BuildSurface.Foundation)
                    FoundationSurfaceChanged?.Invoke();
            }
        }
    }

    public void UnregisterRoadArea(
        Vector2Int origin,
        int width,
        int depth)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                if (gridManager == null ||
                    surfaces == null ||
                    !gridManager.IsInsideGrid(cell))
                    continue;

                BuildSurface previousSurface = surfaces[cell.x, cell.y];

                if (roadUnderlyingSurfaces.TryGetValue(
                        cell,
                        out BuildSurface underlyingSurface))
                {
                    surfaces[cell.x, cell.y] = underlyingSurface;
                    InvalidateGroundVehicleClearanceMap();
                    roadUnderlyingSurfaces.Remove(cell);
                }
                else if (previousSurface == BuildSurface.Road)
                {
                    surfaces[cell.x, cell.y] = BuildSurface.Grass;
                    InvalidateGroundVehicleClearanceMap();
                }

                BuildSurface currentSurface = surfaces[cell.x, cell.y];
                if (previousSurface == BuildSurface.Foundation ||
                    currentSurface == BuildSurface.Foundation)
                {
                    FoundationSurfaceChanged?.Invoke();
                }
            }
        }
    }

    public void RegisterTaxiwayCell(Vector2Int position)
    {
        if (gridManager == null ||
            surfaces == null ||
            !gridManager.IsInsideGrid(position))
        {
            return;
        }

        // Preserve the original surface only on the first registration.
        BuildSurface previousSurface = surfaces[position.x, position.y];
        if (previousSurface != BuildSurface.Taxiway)
        {
            taxiwayUnderlyingSurfaces[position] = previousSurface;
        }

        surfaces[position.x, position.y] = BuildSurface.Taxiway;
        InvalidateGroundVehicleClearanceMap();

        if (previousSurface == BuildSurface.Foundation)
            FoundationSurfaceChanged?.Invoke();
    }

    public void UnregisterTaxiwayCell(Vector2Int position)
    {
        if (gridManager == null ||
            surfaces == null ||
            !gridManager.IsInsideGrid(position))
        {
            return;
        }

        BuildSurface previousSurface = surfaces[position.x, position.y];

        if (taxiwayUnderlyingSurfaces.TryGetValue(
                position,
                out BuildSurface underlyingSurface))
        {
            surfaces[position.x, position.y] = underlyingSurface;
            InvalidateGroundVehicleClearanceMap();
            taxiwayUnderlyingSurfaces.Remove(position);
        }
        else if (previousSurface == BuildSurface.Taxiway)
        {
            // Fallback for taxiway cells without saved underlying data.
            surfaces[position.x, position.y] = BuildSurface.Grass;
            InvalidateGroundVehicleClearanceMap();
        }

        BuildSurface currentSurface = surfaces[position.x, position.y];
        if (previousSurface == BuildSurface.Foundation ||
            currentSurface == BuildSurface.Foundation)
        {
            FoundationSurfaceChanged?.Invoke();
        }
    }

    public void RegisterRunwayCell(Vector2Int position)
    {
        if (gridManager == null ||
            !gridManager.IsInsideGrid(position))
        {
            return;
        }

        if (surfaces == null)
            Initialize();

        if (surfaces == null)
            return;

        // Preserve the original surface only on first registration.
        BuildSurface previousSurface = surfaces[position.x, position.y];
        if (previousSurface != BuildSurface.Runway &&
            !runwayUnderlyingSurfaces.ContainsKey(position))
        {
            runwayUnderlyingSurfaces[position] = previousSurface;
        }

        if (previousSurface != BuildSurface.Runway)
        {
            surfaces[position.x, position.y] = BuildSurface.Runway;
            InvalidateGroundVehicleClearanceMap();
            RunwaySurfaceChanged?.Invoke();
        }

        if (previousSurface == BuildSurface.Foundation)
            FoundationSurfaceChanged?.Invoke();
    }

    public void UnregisterRunwayCell(Vector2Int position)
    {
        if (gridManager == null ||
            surfaces == null ||
            !gridManager.IsInsideGrid(position))
        {
            return;
        }

        BuildSurface previousSurface = surfaces[position.x, position.y];

        if (runwayUnderlyingSurfaces.TryGetValue(
                position,
                out BuildSurface underlyingSurface))
        {
            surfaces[position.x, position.y] = underlyingSurface;
            InvalidateGroundVehicleClearanceMap();
            runwayUnderlyingSurfaces.Remove(position);
        }
        else if (previousSurface == BuildSurface.Runway)
        {
            surfaces[position.x, position.y] = BuildSurface.Grass;
            InvalidateGroundVehicleClearanceMap();
        }

        BuildSurface currentSurface = surfaces[position.x, position.y];
        if (previousSurface == BuildSurface.Runway &&
            currentSurface != BuildSurface.Runway)
        {
            RunwaySurfaceChanged?.Invoke();
        }
        if (previousSurface == BuildSurface.Foundation ||
            currentSurface == BuildSurface.Foundation)
        {
            FoundationSurfaceChanged?.Invoke();
        }
    }



    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}