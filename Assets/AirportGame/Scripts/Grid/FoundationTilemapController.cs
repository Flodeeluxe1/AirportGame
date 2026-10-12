
using UnityEngine;
using UnityEngine.Tilemaps;

public class FoundationTilemapController : MonoBehaviour
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
                FindFirstObjectByType<GridManager>();
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
            Debug.LogWarning(
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
}

