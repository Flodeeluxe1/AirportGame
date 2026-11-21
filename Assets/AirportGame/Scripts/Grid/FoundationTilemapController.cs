
using UnityEngine;
using UnityEngine.Tilemaps;

public class FoundationTilemapController : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private Tilemap foundationTilemap;

    [Header("Foundation Tile")]
    [SerializeField] private TileBase foundationTile;

    private void Awake()
    {
        if (foundationTilemap == null)
        {
            foundationTilemap =
                GetComponent<Tilemap>();
        }

        if (gridManager == null)
        {
            gridManager =
                FindAnyObjectByType<GridManager>();
        }

        if (gridSurface == null &&
            gridManager != null)
        {
            gridSurface =
                gridManager.GetComponent<GridSurface>();
        }
    }

    public void SetFoundationTile(
        Vector2Int gridPosition)
    {
        if (foundationTilemap == null)
            return;

        if (foundationTile == null)
        {
            LogWarning(
                "FoundationTilemapController: " +
                "Foundation Tile is not assigned."
            );

            return;
        }

        Vector3Int tilePosition =
            new Vector3Int(
                gridPosition.x,
                gridPosition.y,
                0
            );

        foundationTilemap.SetTile(
            tilePosition,
            foundationTile
        );
    }

    public void RemoveFoundationTile(
        Vector2Int gridPosition)
    {
        if (foundationTilemap == null)
            return;

        Vector3Int tilePosition =
            new Vector3Int(
                gridPosition.x,
                gridPosition.y,
                0
            );

        foundationTilemap.SetTile(
            tilePosition,
            null
        );
    }

    public void RefreshFromGridSurface()
    {
        if (foundationTilemap == null ||
            gridSurface == null ||
            gridManager == null)
        {
            return;
        }

        foundationTilemap.ClearAllTiles();

        for (int x = 0;
             x < gridManager.Width;
             x++)
        {
            for (int z = 0;
                 z < gridManager.Height;
                 z++)
            {
                Vector2Int gridPosition =
                    new Vector2Int(
                        x,
                        z
                    );

                if (gridSurface.GetSurface(
                        gridPosition
                    ) == BuildSurface.Foundation)
                {
                    SetFoundationTile(
                        gridPosition
                    );
                }
            }
        }
    }

    public void RemoveFoundationArea(
    Vector2Int origin,
    int width,
    int depth)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                RemoveFoundationTile(
                    origin + new Vector2Int(x, z)
                );
            }
        }
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}

