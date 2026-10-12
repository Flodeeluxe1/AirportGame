using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class GridCoordinateDebugger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;

    [Header("Display")]
    [SerializeField] private bool showCoordinates = true;
    [SerializeField] private bool showAxes = true;
    [SerializeField] private bool showDirectionLabels = true;

    [Header("Coordinate Text")]
    [SerializeField] private Color coordinateColor = Color.white;
    [SerializeField] private int fontSize = 12;
    [SerializeField] private float textHeight = 0.02f;

    [Header("Direction Text")]
    [SerializeField] private Color directionColor = Color.yellow;
    [SerializeField] private float directionOffset = 0.25f;

    [Header("Grid")]
    [SerializeField] private Color axisColor = Color.cyan;

    private void Awake()
    {
        if (gridManager == null)
        {
            gridManager =
                GetComponent<GridManager>();
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (gridManager == null)
        {
            gridManager =
                GetComponent<GridManager>();
        }

        if (gridManager == null)
            return;

        DrawCoordinates();
        DrawAxes();
        DrawDirections();
    }

    private void DrawCoordinates()
    {
        if (!showCoordinates)
            return;

        Handles.color = coordinateColor;

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                Vector2Int gridPosition =
                    new Vector2Int(
                        x,
                        z
                    );

                Vector3 worldPosition =
                    gridManager.GridToWorldCenter(
                        gridPosition
                    );

                worldPosition.y +=
                    textHeight;

                Handles.Label(
                    worldPosition,
                    $"{x},{z}",
                    GetTextStyle()
                );
            }
        }
    }

    private void DrawAxes()
    {
        if (!showAxes)
            return;

        Vector3 origin =
            gridManager.transform.position;

        Vector3 xEnd =
            origin +
            new Vector3(
                gridManager.Width *
                gridManager.CellSize,
                0f,
                0f
            );

        Vector3 zEnd =
            origin +
            new Vector3(
                0f,
                0f,
                gridManager.Height *
                gridManager.CellSize
            );

        Gizmos.color = axisColor;

        Gizmos.DrawLine(
            origin,
            xEnd
        );

        Gizmos.DrawLine(
            origin,
            zEnd
        );

        Handles.color = axisColor;

        Handles.Label(
            xEnd +
            new Vector3(
                0.2f,
                0.05f,
                0f
            ),
            "X →",
            GetTextStyle()
        );

        Handles.Label(
            zEnd +
            new Vector3(
                0f,
                0.05f,
                0.2f
            ),
            "Z / South ↓",
            GetTextStyle()
        );
    }

    private void DrawDirections()
    {
        if (!showDirectionLabels)
            return;

        Vector3 origin =
            gridManager.GridToWorldCenter(
                new Vector2Int(
                    gridManager.Width / 2,
                    gridManager.Height / 2
                )
            );

        Handles.color = directionColor;

        Handles.Label(
            origin +
            new Vector3(
                0f,
                0.05f,
                directionOffset
            ),
            "SOUTH (+Y)",
            GetDirectionTextStyle()
        );

        Handles.Label(
            origin +
            new Vector3(
                0f,
                0.05f,
                -directionOffset
            ),
            "NORTH (-Y)",
            GetDirectionTextStyle()
        );

        Handles.Label(
            origin +
            new Vector3(
                directionOffset,
                0.05f,
                0f
            ),
            "EAST (+X)",
            GetDirectionTextStyle()
        );

        Handles.Label(
            origin +
            new Vector3(
                -directionOffset,
                0.05f,
                0f
            ),
            "WEST (-X)",
            GetDirectionTextStyle()
        );
    }

    private GUIStyle GetTextStyle()
    {
        GUIStyle style =
            new GUIStyle(
                EditorStyles.boldLabel
            );

        style.fontSize =
            fontSize;

        style.normal.textColor =
            coordinateColor;

        style.alignment =
            TextAnchor.MiddleCenter;

        return style;
    }

    private GUIStyle GetDirectionTextStyle()
    {
        GUIStyle style =
            new GUIStyle(
                EditorStyles.boldLabel
            );

        style.fontSize =
            fontSize;

        style.normal.textColor =
            directionColor;

        style.alignment =
            TextAnchor.MiddleCenter;

        return style;
    }
#endif
}