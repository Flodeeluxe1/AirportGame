
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TaxiwayTilemapController : MonoBehaviour
{
    [System.Serializable]
    private class TaxiwayTileMapping
    {
        public BuildableDefinition definition;
        public TileBase tile;
    }

    [Header("References")]
    [SerializeField] private Tilemap taxiwayTilemap;

    [Header("Taxiway Visuals")]
    [SerializeField]
    private List<TaxiwayTileMapping> tileMappings =
        new List<TaxiwayTileMapping>();

    private void Awake()
    {
        if (taxiwayTilemap == null)
        {
            taxiwayTilemap = GetComponent<Tilemap>();
        }
    }

    public TileBase GetTileForDefinition(
        BuildableDefinition definition)
    {
        if (definition == null)
            return null;

        foreach (TaxiwayTileMapping mapping in tileMappings)
        {
            if (mapping == null)
                continue;

            if (mapping.definition == definition)
                return mapping.tile;
        }

        return null;
    }

    public TileBase GetTaxiwayTile(
        Vector2Int gridPosition)
    {
        if (taxiwayTilemap == null)
            return null;

        Vector3Int tilePosition = new Vector3Int(
            gridPosition.x,
            gridPosition.y,
            0
        );

        return taxiwayTilemap.GetTile(tilePosition);
    }

    public bool CanSetTaxiwayTile(
        Vector2Int gridPosition,
        TileBase tile)
    {
        if (taxiwayTilemap == null || tile == null)
            return false;

        TileBase existingTile =
            GetTaxiwayTile(gridPosition);

        return existingTile == null ||
               existingTile == tile;
    }

    public void SetTaxiwayTile(
        Vector2Int gridPosition,
        TileBase tile)
    {
        if (taxiwayTilemap == null || tile == null)
            return;

        Vector3Int tilePosition = new Vector3Int(
            gridPosition.x,
            gridPosition.y,
            0
        );

        taxiwayTilemap.SetTile(
            tilePosition,
            tile
        );
    }

    public void RemoveTaxiwayTile(
        Vector2Int gridPosition)
    {
        if (taxiwayTilemap == null)
            return;

        Vector3Int tilePosition = new Vector3Int(
            gridPosition.x,
            gridPosition.y,
            0
        );

        taxiwayTilemap.SetTile(
            tilePosition,
            null
        );
    }

    public void ClearAllTaxiwayTiles()
    {
        if (taxiwayTilemap == null)
            return;

        taxiwayTilemap.ClearAllTiles();
    }
}
