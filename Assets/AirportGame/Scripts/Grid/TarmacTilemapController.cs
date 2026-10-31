
using UnityEngine;
using UnityEngine.Tilemaps;

public class TarmacTilemapController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private Tilemap tarmacTilemap;

    [Header("Tarmac Tile")]
    [SerializeField] private TileBase tarmacTile;

    private void Awake()
    {
        if (tarmacTilemap == null)
            tarmacTilemap = GetComponent<Tilemap>();

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (gridSurface == null && gridManager != null)
            gridSurface = gridManager.GetComponent<GridSurface>();
    }

    public void SetTarmacTile(Vector2Int gridPosition)
    {
        if (tarmacTilemap == null || tarmacTile == null)
            return;

        Vector3Int cell = new Vector3Int(
            gridPosition.x,
            gridPosition.y,
            0
        );

        tarmacTilemap.SetTile(cell, tarmacTile);
    }

    public void RemoveTarmacTile(Vector2Int gridPosition)
    {
        if (tarmacTilemap == null)
            return;

        Vector3Int cell = new Vector3Int(
            gridPosition.x,
            gridPosition.y,
            0
        );

        tarmacTilemap.SetTile(cell, null);
    }

    public void ClearAllTarmacTiles()
    {
        if (tarmacTilemap == null)
            return;

        tarmacTilemap.ClearAllTiles();
    }

    public void RefreshFromGridSurface()
    {
        if (tarmacTilemap == null ||
            gridSurface == null ||
            gridManager == null)
        {
            return;
        }

        tarmacTilemap.ClearAllTiles();

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                Vector2Int gridPosition = new Vector2Int(x, z);

                if (gridSurface.GetSurface(gridPosition) ==
                    BuildSurface.Tarmac)
                {
                    SetTarmacTile(gridPosition);
                }
            }
        }
    }
}
