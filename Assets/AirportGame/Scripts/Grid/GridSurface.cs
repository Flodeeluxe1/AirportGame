using System.Collections.Generic;
using UnityEngine;

public class GridSurface : MonoBehaviour
{
    [SerializeField]
    private GridManager gridManager;

    private BuildSurface[,] surfaces;

    private readonly Dictionary<Vector2Int, GameObject> surfaceOwners =
        new Dictionary<Vector2Int, GameObject>();

    private void Awake()
    {
        if (gridManager == null)
        {
            gridManager =
                GetComponent<GridManager>();
        }

        Initialize();
    }

    private void Initialize()
    {
        surfaces =
            new BuildSurface[
                gridManager.Width,
                gridManager.Height
            ];

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                surfaces[x, z] =
                    BuildSurface.Grass;
            }
        }

        surfaceOwners.Clear();
    }

    public BuildSurface GetSurface(
        Vector2Int position)
    {
        if (!gridManager.IsInsideGrid(position))
            return BuildSurface.Grass;

        return surfaces[
            position.x,
            position.y
        ];
    }

    public void SetSurface(
        Vector2Int position,
        BuildSurface surface)
    {
        if (!gridManager.IsInsideGrid(position))
            return;

        surfaces[
            position.x,
            position.y
        ] = surface;

        if (surface == BuildSurface.Grass)
        {
            surfaceOwners.Remove(position);
        }
    }

    public void SetSurfaceArea(
        Vector2Int origin,
        int width,
        int depth,
        BuildSurface surface)
    {
        SetSurfaceArea(
            origin,
            width,
            depth,
            surface,
            null
        );
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
                Vector2Int cell =
                    origin +
                    new Vector2Int(x, z);

                SetSurface(
                    cell,
                    surface
                );

                if (surface != BuildSurface.Grass &&
                    owner != null)
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

        return GetSurface(position) ==
               requiredSurface;
    }

    public bool CanSetSurfaceArea(
        Vector2Int origin,
        int width,
        int depth,
        BuildSurface requiredCurrentSurface)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell =
                    origin +
                    new Vector2Int(x, z);

                if (!gridManager.IsInsideGrid(cell))
                    return false;

                if (GetSurface(cell) !=
                    requiredCurrentSurface)
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

        if (!surfaceOwners.TryGetValue(
                position,
                out GameObject currentOwner))
        {
            return false;
        }

        return currentOwner == owner;
    }

    public void ReleaseSurfaceOwner(
        GameObject owner)
    {
        if (owner == null)
            return;

        List<Vector2Int> cellsToRelease =
            new List<Vector2Int>();

        foreach (
            KeyValuePair<Vector2Int, GameObject> entry
            in surfaceOwners)
        {
            if (entry.Value == owner)
            {
                cellsToRelease.Add(
                    entry.Key
                );
            }
        }

        foreach (Vector2Int cell in cellsToRelease)
        {
            surfaceOwners.Remove(cell);

            if (gridManager.IsInsideGrid(cell))
            {
                surfaces[
                    cell.x,
                    cell.y
                ] = BuildSurface.Grass;
            }
        }
    }
}