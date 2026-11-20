using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Renders the dedicated pedestrian-tarmac surface on its own Tilemap.
/// GridSurface remains the source of truth for the logical surface.
/// </summary>
public class TarmacPedestrianTilemapController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private Tilemap pedestrianTarmacTilemap;

    [Header("Pedestrian Tarmac Tile")]
    [SerializeField] private TileBase pedestrianTarmacTile;

    private void Awake()
    {
        if (pedestrianTarmacTilemap == null)
            pedestrianTarmacTilemap = GetComponent<Tilemap>();
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
        if (gridSurface == null && gridManager != null)
            gridSurface = gridManager.GetComponent<GridSurface>();
        if (gridSurface == null)
            gridSurface = FindAnyObjectByType<GridSurface>();
    }

    public void SetPedestrianTarmacTile(Vector2Int gridPosition)
    {
        if (pedestrianTarmacTilemap == null || pedestrianTarmacTile == null)
            return;
        pedestrianTarmacTilemap.SetTile(
            new Vector3Int(gridPosition.x, gridPosition.y, 0), pedestrianTarmacTile);
    }

    public void RemovePedestrianTarmacTile(Vector2Int gridPosition)
    {
        if (pedestrianTarmacTilemap == null)
            return;
        pedestrianTarmacTilemap.SetTile(
            new Vector3Int(gridPosition.x, gridPosition.y, 0), null);
    }

    public void ClearAllPedestrianTarmacTiles()
    {
        if (pedestrianTarmacTilemap != null)
            pedestrianTarmacTilemap.ClearAllTiles();
    }

    public void RefreshFromGridSurface()
    {
        if (pedestrianTarmacTilemap == null || gridSurface == null || gridManager == null)
            return;
        pedestrianTarmacTilemap.ClearAllTiles();
        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (gridSurface.GetSurface(cell) == BuildSurface.PedestrianTarmac)
                    SetPedestrianTarmacTile(cell);
            }
        }
    }
}
