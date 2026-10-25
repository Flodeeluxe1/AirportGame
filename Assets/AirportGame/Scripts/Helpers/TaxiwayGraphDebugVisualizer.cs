
using System.Collections.Generic;
using UnityEngine;

public class TaxiwayGraphDebugVisualizer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;

    [Header("Visualization")]
    [SerializeField] private bool showGraph = true;
    [SerializeField] private bool showConnectionLines = true;
    [SerializeField] private bool showCellMarkers = true;
    [SerializeField] private bool showCenterlineMarkers = true;

    [Tooltip("Vertical offset above the taxiway surface.")]
    [SerializeField] private float verticalOffset = 0.15f;

    [Min(0.02f)]
    [SerializeField] private float cellMarkerSize = 0.18f;

    [Min(0.02f)]
    [SerializeField] private float centerlineMarkerSize = 0.28f;

    [Header("Reachability Test")]
    [Tooltip("Starting taxiway cell used for the flood-fill test.")]
    [SerializeField] private Vector2Int startCell = Vector2Int.zero;

    [Min(1)]
    [SerializeField] private int minimumTaxiwayWidth = 1;

    [Tooltip("When enabled, cells that A* cannot reach from Start Cell are highlighted.")]
    [SerializeField] private bool colorByReachability = true;

    [Header("Colors")]
    [SerializeField] private Color normalCellColor = Color.cyan;
    [SerializeField] private Color reachableColor = Color.green;
    [SerializeField] private Color unreachableColor = Color.red;
    [SerializeField] private Color isolatedColor = new Color(1f, 0.5f, 0f);
    [SerializeField] private Color insufficientWidthColor = Color.gray;
    [SerializeField] private Color centerlineColor = Color.yellow;
    [SerializeField] private Color startCellColor = Color.blue;
    [SerializeField] private Color connectionColor = Color.white;

    private readonly HashSet<Vector2Int> reachableCells =
        new HashSet<Vector2Int>();

    private readonly HashSet<Vector2Int> allTaxiwayCells =
        new HashSet<Vector2Int>();

    private readonly Queue<Vector2Int> floodQueue =
        new Queue<Vector2Int>();

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (taxiwayNetwork == null)
            taxiwayNetwork = FindFirstObjectByType<TaxiwayNetwork>();
    }

    private void OnDrawGizmos()
    {
        if (!showGraph)
            return;

        ResolveReferences();

        if (gridManager == null || taxiwayNetwork == null)
            return;

        CollectTaxiwayCells();
        CalculateReachableCells();
        DrawConnections();
        DrawCells();
    }

    private void CollectTaxiwayCells()
    {
        allTaxiwayCells.Clear();

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);

                if (taxiwayNetwork.IsTaxiway(cell))
                    allTaxiwayCells.Add(cell);
            }
        }
    }

    private void CalculateReachableCells()
    {
        reachableCells.Clear();
        floodQueue.Clear();

        if (!colorByReachability)
            return;

        if (!IsSuitableCell(startCell))
            return;

        reachableCells.Add(startCell);
        floodQueue.Enqueue(startCell);

        while (floodQueue.Count > 0)
        {
            Vector2Int current = floodQueue.Dequeue();

            List<Vector2Int> neighbors =
                taxiwayNetwork.GetConnectedTaxiwayCells(current);

            foreach (Vector2Int neighbor in neighbors)
            {
                if (!IsSuitableCell(neighbor))
                    continue;

                if (reachableCells.Add(neighbor))
                    floodQueue.Enqueue(neighbor);
            }
        }
    }

    private bool IsSuitableCell(Vector2Int cell)
    {
        return taxiwayNetwork.IsTaxiway(cell) &&
               taxiwayNetwork.GetTaxiwayWidth(cell) >=
               Mathf.Max(1, minimumTaxiwayWidth);
    }

    private Vector3 GetCellWorldPosition(Vector2Int cell)
    {
        Vector3 position = gridManager.GridToWorldCenter(cell);
        position.y += verticalOffset;
        return position;
    }

    private void DrawConnections()
    {
        if (!showConnectionLines)
            return;

        foreach (Vector2Int cell in allTaxiwayCells)
        {
            List<Vector2Int> neighbors =
                taxiwayNetwork.GetConnectedTaxiwayCells(cell);

            Vector3 startPosition = GetCellWorldPosition(cell);

            foreach (Vector2Int neighbor in neighbors)
            {
                // Draw each undirected connection only once.
                if (neighbor.x < cell.x ||
                    (neighbor.x == cell.x && neighbor.y < cell.y))
                {
                    continue;
                }

                bool bothSuitable =
                    IsSuitableCell(cell) &&
                    IsSuitableCell(neighbor);

                if (colorByReachability && bothSuitable)
                {
                    bool bothReachable =
                        reachableCells.Contains(cell) &&
                        reachableCells.Contains(neighbor);

                    Gizmos.color = bothReachable
                        ? reachableColor
                        : connectionColor;
                }
                else
                {
                    Gizmos.color = connectionColor;
                }

                Gizmos.DrawLine(
                    startPosition,
                    GetCellWorldPosition(neighbor)
                );
            }
        }
    }

    private void DrawCells()
    {
        if (!showCellMarkers)
            return;

        foreach (Vector2Int cell in allTaxiwayCells)
        {
            bool suitable = IsSuitableCell(cell);

            Color markerColor = normalCellColor;

            if (!suitable)
            {
                markerColor = insufficientWidthColor;
            }
            else if (colorByReachability)
            {
                markerColor = reachableCells.Contains(cell)
                    ? reachableColor
                    : unreachableColor;
            }

            List<Vector2Int> neighbors =
                taxiwayNetwork.GetConnectedTaxiwayCells(cell);

            if (neighbors.Count == 0)
                markerColor = isolatedColor;

            if (cell == startCell && colorByReachability)
                markerColor = startCellColor;

            Vector3 position = GetCellWorldPosition(cell);

            Gizmos.color = markerColor;
            Gizmos.DrawSphere(position, cellMarkerSize);

            if (showCenterlineMarkers &&
                taxiwayNetwork.IsCenterlineCell(cell))
            {
                Gizmos.color = centerlineColor;

                Gizmos.DrawWireSphere(
                    position,
                    centerlineMarkerSize
                );
            }
        }
    }

    [ContextMenu("Log Reachability Summary")]
    private void LogReachabilitySummary()
    {
        ResolveReferences();

        if (gridManager == null || taxiwayNetwork == null)
        {
            Debug.LogError(
                "TaxiwayGraphDebugVisualizer: GridManager or " +
                "TaxiwayNetwork is missing.",
                this
            );
            return;
        }

        CollectTaxiwayCells();
        CalculateReachableCells();

        int suitableCount = 0;
        int unreachableCount = 0;
        int isolatedCount = 0;
        int unsuitableWidthCount = 0;

        List<Vector2Int> unreachableCells =
            new List<Vector2Int>();

        foreach (Vector2Int cell in allTaxiwayCells)
        {
            if (!IsSuitableCell(cell))
            {
                unsuitableWidthCount++;
                continue;
            }

            suitableCount++;

            List<Vector2Int> neighbors =
                taxiwayNetwork.GetConnectedTaxiwayCells(cell);

            bool hasSuitableNeighbor = false;

            foreach (Vector2Int neighbor in neighbors)
            {
                if (IsSuitableCell(neighbor))
                {
                    hasSuitableNeighbor = true;
                    break;
                }
            }

            if (!hasSuitableNeighbor)
                isolatedCount++;

            if (colorByReachability &&
                !reachableCells.Contains(cell))
            {
                unreachableCount++;
                unreachableCells.Add(cell);
            }
        }

        string report =
            "========== TAXIWAY GRAPH SUMMARY ==========\n" +
            $"Total taxiway cells: {allTaxiwayCells.Count}\n" +
            $"Minimum required width: {minimumTaxiwayWidth}\n" +
            $"Suitable cells: {suitableCount}\n" +
            $"Start cell: {startCell}\n" +
            $"Start cell suitable: {IsSuitableCell(startCell)}\n" +
            $"Reachable cells: {reachableCells.Count}\n" +
            $"Unreachable suitable cells: {unreachableCount}\n" +
            $"Isolated suitable cells: {isolatedCount}\n" +
            $"Cells below required width: {unsuitableWidthCount}\n";

        if (unreachableCells.Count > 0)
        {
            report += "Unreachable cells: ";

            foreach (Vector2Int cell in unreachableCells)
                report += cell + " ";

            report += "\n";
        }

        report += "============================================";

        Debug.Log(report, this);
    }
}
