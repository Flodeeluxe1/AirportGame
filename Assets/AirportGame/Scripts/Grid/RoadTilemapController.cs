using UnityEngine;
using UnityEngine.Tilemaps;

public class RoadTilemapController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Tilemap roadTilemap;

    [Header("Road Tile")]
    [SerializeField] private TileBase roadTile;

    private void Awake()
    {
        if (roadTilemap == null)
        {
            roadTilemap =
                GetComponent<Tilemap>();
        }
    }

    public void SetRoadTile(
        Vector2Int gridPosition,
        RoadDirection direction)
    {
        if (roadTilemap == null)
            return;

        if (roadTile == null)
        {
            Debug.LogWarning(
                "RoadTilemapController: " +
                "Road Tile is not assigned."
            );

            return;
        }
        /*
        Vector3Int tilePosition =
            new Vector3Int(
                gridPosition.x,
                0,
                gridPosition.y
            );
        */
        //Test
        Vector3Int tilePosition =
            new Vector3Int(
                gridPosition.x,
                gridPosition.y,
                0
            );

        roadTilemap.SetTile(
            tilePosition,
            roadTile
        );

        float rotation =
            GetRotation(direction);

        Matrix4x4 matrix =
            Matrix4x4.TRS(
                new Vector3(
                    0.5f,
                    0f,
                    0.5f
                ),
                Quaternion.Euler(
                    0f,
                    rotation,
                    0f
                ),
                Vector3.one
            );

        roadTilemap.SetTransformMatrix(
            tilePosition,
            matrix
        );
    }

    public void RemoveRoadTile(
        Vector2Int gridPosition)
    {
        if (roadTilemap == null)
            return;

        Vector3Int tilePosition =
            new Vector3Int(
                gridPosition.x,
                gridPosition.y,
                0
            );

        roadTilemap.SetTile(
            tilePosition,
            null
        );
    }

    private float GetRotation(
        RoadDirection direction)
    {
        switch (direction)
        {
            case RoadDirection.North:
                return 90f;

            case RoadDirection.East:
                return 0f;

            case RoadDirection.South:
                return 270f;

            case RoadDirection.West:
                return 180f;
        }

        return 0f;
    }
}