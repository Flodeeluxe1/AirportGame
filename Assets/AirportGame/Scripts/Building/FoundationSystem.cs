
using System.Collections.Generic;
using UnityEngine;

public class FoundationSystem : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;

    [Header("Visual")]
    [SerializeField] private GameObject foundationTilePrefab;

    private readonly Dictionary<Vector2Int, GameObject> foundationTiles =
        new Dictionary<Vector2Int, GameObject>();

    public event System.Action FoundationTilesChanged;

    public IEnumerable<Vector2Int> FoundationPositions => foundationTiles.Keys;

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
            LogError(
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
        FoundationTilesChanged?.Invoke();
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
        FoundationTilesChanged?.Invoke();
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
        bool hadTiles = foundationTiles.Count > 0;
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
        if (hadTiles)
            FoundationTilesChanged?.Invoke();
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
