using System.Collections.Generic;
using UnityEngine;

public class GridSurface : MonoBehaviour
{
    [SerializeField]
    private GridManager gridManager;

    private BuildSurface[,] surfaces;

    private readonly Dictionary<Vector2Int, GameObject> surfaceOwners =
        new Dictionary<Vector2Int, GameObject>();

    private readonly Dictionary<Vector2Int, GameObject> obstacleOwners =
        new Dictionary<Vector2Int, GameObject>();

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
            Debug.LogError(
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
            {
                surfaces[x, z] = BuildSurface.Grass;
            }
        }

        surfaceOwners.Clear();
        obstacleOwners.Clear();
    }

    public BuildSurface GetSurface(Vector2Int position)
    {
        if (gridManager == null || surfaces == null)
            return BuildSurface.Grass;

        if (!gridManager.IsInsideGrid(position))
            return BuildSurface.Grass;

        return surfaces[position.x, position.y];
    }

    public void SetSurface(Vector2Int position, BuildSurface surface)
    {
        if (gridManager == null)
        {
            Debug.LogError(
                "GridSurface: Cannot set a surface because GridManager is missing."
            );
            return;
        }

        if (surfaces == null)
            Initialize();

        if (surfaces == null ||
            !gridManager.IsInsideGrid(position))
            return;

        surfaces[position.x, position.y] = surface;

        if (surface == BuildSurface.Grass)
            surfaceOwners.Remove(position);
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
                surfaces[cell.x, cell.y] = BuildSurface.Grass;
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
            Debug.LogWarning(
                "GridSurface: Cannot reset because the grid is not initialized."
            );
            return;
        }

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                surfaces[x, z] = BuildSurface.Grass;
            }
        }

        surfaceOwners.Clear();
        obstacleOwners.Clear();
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
        surfaces[position.x, position.y] = BuildSurface.Grass;

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

}