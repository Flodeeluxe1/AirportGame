
using System.Collections.Generic;
using UnityEngine;

public class FoundationSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;

    [Header("Visual")]
    [SerializeField] private GameObject foundationTilePrefab;

    private readonly Dictionary<Vector2Int, GameObject> foundationTiles =
        new Dictionary<Vector2Int, GameObject>();

    private void Awake()
    {
        if (gridManager == null)
        {
            gridManager =
                GetComponent<GridManager>();
        }

        if (gridSurface == null &&
            gridManager != null)
        {
            gridSurface =
                gridManager.GetComponent<GridSurface>();
        }
    }

    public bool HasTile(Vector2Int position)
    {
        return foundationTiles.ContainsKey(position);
    }

    public void CreateTile(Vector2Int position)
    {
        if (foundationTilePrefab == null)
        {
            Debug.LogError(
                "FoundationSystem: " +
                "Foundation tile prefab is not assigned."
            );

            return;
        }

        if (gridManager == null)
            return;

        if (!gridManager.IsInsideGrid(position))
            return;

        if (HasTile(position))
            return;

        GameObject tile =
            Instantiate(
                foundationTilePrefab,
                gridManager.GridToWorldCenter(position),
                Quaternion.identity,
                transform
            );

        foundationTiles[position] =
            tile;
    }

    public void RemoveTile(Vector2Int position)
    {
        if (!foundationTiles.TryGetValue(
                position,
                out GameObject tile))
        {
            return;
        }

        if (tile != null)
        {
            Destroy(tile);
        }

        foundationTiles.Remove(position);
    }

    public void CreateTiles(
        Vector2Int origin,
        int width,
        int depth)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell =
                    origin +
                    new Vector2Int(x, z);

                CreateTile(cell);
            }
        }
    }

    public void RemoveTiles(
        Vector2Int origin,
        int width,
        int depth)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell =
                    origin +
                    new Vector2Int(x, z);

                RemoveTile(cell);
            }
        }
    }

    public void Clear()
    {
        foreach (
            KeyValuePair<Vector2Int, GameObject> entry
            in foundationTiles)
        {
            if (entry.Value != null)
            {
                Destroy(entry.Value);
            }
        }

        foundationTiles.Clear();
    }
}
