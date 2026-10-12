using UnityEngine;

public class GridManager : MonoBehaviour
{
    [Header("Grid Settings")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private int width = 50;
    [SerializeField] private int height = 50;

    [Header("Visualization")]
    [SerializeField] private bool showGrid = true;
    [SerializeField] private Color gridColor = Color.gray;

    public float CellSize => cellSize;
    public int Width => width;
    public int Height => height;

    private void OnDrawGizmos()
    {
        if (!showGrid)
            return;

        Gizmos.color = gridColor;

        for (int x = 0; x <= width; x++)
        {
            Vector3 start = transform.position +
                            new Vector3(x * cellSize, 0f, 0f);

            Vector3 end = transform.position +
                          new Vector3(x * cellSize, 0f, height * cellSize);

            Gizmos.DrawLine(start, end);
        }

        for (int z = 0; z <= height; z++)
        {
            Vector3 start = transform.position +
                            new Vector3(0f, 0f, z * cellSize);

            Vector3 end = transform.position +
                          new Vector3(width * cellSize, 0f, z * cellSize);

            Gizmos.DrawLine(start, end);
        }
    }

    public Vector2Int WorldToGrid(Vector3 worldPosition)
    {
        Vector3 localPosition = worldPosition - transform.position;

        int x = Mathf.FloorToInt(localPosition.x / cellSize);
        int z = Mathf.FloorToInt(localPosition.z / cellSize);

        return new Vector2Int(x, z);
    }

    public Vector3 GridToWorld(Vector2Int gridPosition)
    {
        return transform.position +
               new Vector3(
                   gridPosition.x * cellSize,
                   0f,
                   gridPosition.y * cellSize
               );
    }

    public Vector3 GridToWorldCenter(Vector2Int gridPosition)
    {
        return transform.position +
               new Vector3(
                   gridPosition.x * cellSize + cellSize * 0.5f,
                   0f,
                   gridPosition.y * cellSize + cellSize * 0.5f
               );
    }

    public bool IsInsideGrid(Vector2Int position)
    {
        return position.x >= 0 &&
               position.x < width &&
               position.y >= 0 &&
               position.y < height;
    }
}