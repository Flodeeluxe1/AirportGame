using UnityEngine;

/// <summary>
/// Scene-view overlay for SecurityAreaClassifier.
/// Attach to the same GridManager GameObject as the classifier.
/// </summary>
[DisallowMultipleComponent]
public sealed class SecurityAreaDebugVisualizer : MonoBehaviour
{
    [SerializeField] private SecurityAreaClassifier classifier;
    [SerializeField] private bool showAreaCells = true;
    [SerializeField, Range(0.1f, 1f)] private float cellFill = 0.82f;
    [SerializeField, Min(0f)] private float verticalOffset = 0.08f;

    private GridManager gridManager;

    private void OnDrawGizmos()
    {
        if (!showAreaCells)
            return;

        ResolveReferences();
        if (classifier == null || gridManager == null || !classifier.IsReady)
            return;

        float size = gridManager.CellSize * cellFill;
        Vector3 cubeSize = new Vector3(size, 0.035f, size);

        for (int z = 0; z < gridManager.Height; z++)
        {
            for (int x = 0; x < gridManager.Width; x++)
            {
                SecurityArea area = classifier.GetArea(new Vector2Int(x, z));
                if (area == SecurityArea.Unclassified)
                    continue;

                Gizmos.color = GetAreaColor(area);
                Vector3 center = gridManager.GridToWorldCenter(new Vector2Int(x, z));
                center.y += verticalOffset;
                Gizmos.DrawCube(center, cubeSize);
            }
        }

        DrawDiagnostics();
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = GetComponent<GridManager>();

        if (classifier == null)
            classifier = GetComponent<SecurityAreaClassifier>();

        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (classifier == null)
            classifier = FindAnyObjectByType<SecurityAreaClassifier>();
    }

    private void DrawDiagnostics()
    {
        if (classifier.IncompleteScannerBoundaryCount == 0 &&
            !classifier.HasSecurityBoundaryLeak)
        {
            return;
        }

        Vector3 labelPosition = gridManager.transform.position +
            new Vector3(0f, verticalOffset + 0.2f, 0f);

        // Keep diagnostics visible without requiring an editor-only Handles
        // dependency; the missing boundaries are also reflected by the
        // orange partial lines from SecurityBoundaryVisualizer.
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 1f);
        Gizmos.DrawWireSphere(labelPosition, gridManager.CellSize * 0.18f);
    }

    private static Color GetAreaColor(SecurityArea area)
    {
        switch (area)
        {
            case SecurityArea.Unsecure:
                return new Color(0.2f, 0.55f, 1f, 0.22f);
            case SecurityArea.Secure:
                return new Color(0.2f, 0.9f, 0.4f, 0.3f);
            case SecurityArea.Critical:
                return new Color(1f, 0.25f, 0.2f, 0.28f);
            default:
                return Color.clear;
        }
    }
}
