
using System.Collections.Generic;
using UnityEngine;

public class TaxiwayNetwork : MonoBehaviour
{
    private class TaxiwayCell
    {
        public Vector2Int Position;
        public BuildableDefinition Definition;

        public bool ConnectNorth;
        public bool ConnectEast;
        public bool ConnectSouth;
        public bool ConnectWest;

        public TaxiwayCell(
            Vector2Int position,
            BuildableDefinition definition)
        {
            Position = position;
            Definition = definition;
        }
    }

    private readonly Dictionary<Vector2Int, TaxiwayCell> taxiwayCells =
        new Dictionary<Vector2Int, TaxiwayCell>();

    private readonly HashSet<Vector2Int> centerlineCells =
        new HashSet<Vector2Int>();

    private static readonly Vector2Int North =
        new Vector2Int(0, 1);

    private static readonly Vector2Int East =
        new Vector2Int(1, 0);

    private static readonly Vector2Int South =
        new Vector2Int(0, -1);

    private static readonly Vector2Int West =
        new Vector2Int(-1, 0);

    public bool IsTaxiway(Vector2Int position)
    {
        return taxiwayCells.ContainsKey(position);
    }

    public bool IsCenterlineCell(Vector2Int position)
    {
        return centerlineCells.Contains(position);
    }

    public void AddTaxiwayCenterlineCell(
        Vector2Int position)
    {
        if (taxiwayCells.ContainsKey(position))
        {
            centerlineCells.Add(position);
        }
    }

    public int GetTaxiwayWidth(Vector2Int position)
    {
        if (!taxiwayCells.TryGetValue(
                position,
                out TaxiwayCell cell))
        {
            return 0;
        }

        return cell.Definition != null
            ? Mathf.Max(1, cell.Definition.width)
            : 1;
    }

    public BuildableDefinition GetDefinition(
        Vector2Int position)
    {
        if (!taxiwayCells.TryGetValue(
                position,
                out TaxiwayCell cell))
        {
            return null;
        }

        return cell.Definition;
    }

    public void AddTaxiwayCell(
        Vector2Int position,
        BuildableDefinition definition)
    {
        if (definition == null)
        {
            Debug.LogWarning(
                "TaxiwayNetwork: Cannot add a cell " +
                "without a BuildableDefinition."
            );

            return;
        }

        if (taxiwayCells.TryGetValue(
                position,
                out TaxiwayCell existingCell))
        {
            if (existingCell.Definition != definition)
            {
                Debug.LogWarning(
                    $"TaxiwayNetwork: Cell {position} " +
                    "already belongs to another taxiway definition."
                );

                return;
            }

            UpdateConnectionsAround(position);
            return;
        }

        taxiwayCells.Add(
            position,
            new TaxiwayCell(
                position,
                definition
            )
        );

        UpdateConnectionsAround(position);
    }

    public void RemoveTaxiwayCell(
        Vector2Int position)
    {
        if (!taxiwayCells.Remove(position))
            return;

        centerlineCells.Remove(position);

        UpdateConnectionsAround(position);
    }

    public List<Vector2Int> GetConnectedTaxiwayCells(
        Vector2Int position)
    {
        List<Vector2Int> connectedCells =
            new List<Vector2Int>();

        if (!taxiwayCells.TryGetValue(
                position,
                out TaxiwayCell cell))
        {
            return connectedCells;
        }

        if (cell.ConnectNorth)
            connectedCells.Add(position + North);

        if (cell.ConnectEast)
            connectedCells.Add(position + East);

        if (cell.ConnectSouth)
            connectedCells.Add(position + South);

        if (cell.ConnectWest)
            connectedCells.Add(position + West);

        return connectedCells;
    }

    private void UpdateConnectionsAround(
        Vector2Int position)
    {
        UpdateConnectionsForCell(position);
        UpdateConnectionsForCell(position + North);
        UpdateConnectionsForCell(position + East);
        UpdateConnectionsForCell(position + South);
        UpdateConnectionsForCell(position + West);
    }

    private void UpdateConnectionsForCell(
        Vector2Int position)
    {
        if (!taxiwayCells.TryGetValue(
                position,
                out TaxiwayCell cell))
        {
            return;
        }

        cell.ConnectNorth =
            taxiwayCells.ContainsKey(position + North);

        cell.ConnectEast =
            taxiwayCells.ContainsKey(position + East);

        cell.ConnectSouth =
            taxiwayCells.ContainsKey(position + South);

        cell.ConnectWest =
            taxiwayCells.ContainsKey(position + West);
    }

    public void Clear()
    {
        taxiwayCells.Clear();
        centerlineCells.Clear();
    }

    [ContextMenu("Debug All Taxiway Cells")]
    private void DebugAllTaxiwayCells()
    {
        Debug.Log(
            "========== TAXIWAY NETWORK DEBUG =========="
        );

        Debug.Log(
            $"Total Taxiway Footprint Cells: {taxiwayCells.Count}"
        );

        Debug.Log(
            $"Total Centerline Cells: {centerlineCells.Count}"
        );

        foreach (
            KeyValuePair<Vector2Int, TaxiwayCell> entry
            in taxiwayCells)
        {
            TaxiwayCell cell = entry.Value;

            Debug.Log(
                BuildDebugText(cell)
            );
        }

        Debug.Log(
            "========== END TAXIWAY NETWORK DEBUG =========="
        );
    }

    private string BuildDebugText(
        TaxiwayCell cell)
    {
        string connections = "";

        if (cell.ConnectNorth)
            connections += "N ";

        if (cell.ConnectEast)
            connections += "E ";

        if (cell.ConnectSouth)
            connections += "S ";

        if (cell.ConnectWest)
            connections += "W ";

        if (connections == "")
            connections = "NONE";

        string definitionName =
            cell.Definition != null
                ? cell.Definition.displayName
                : "Unknown";

        string centerline =
            centerlineCells.Contains(cell.Position)
                ? "YES"
                : "NO";

        return
            $"TAXIWAY CELL {cell.Position}\n" +
            $"Definition: {definitionName}\n" +
            $"Width: {GetTaxiwayWidth(cell.Position)} cells\n" +
            $"Centerline: {centerline}\n" +
            $"Connections: {connections}";
    }

    public System.Collections.Generic.List<TaxiwayCellSaveData> GetSaveData()
    {
        var result = new System.Collections.Generic.List<TaxiwayCellSaveData>();

        foreach (var entry in taxiwayCells)
        {
            Vector2Int position = entry.Key;
            TaxiwayCell cell = entry.Value;

            if (cell.Definition == null)
            {
                Debug.LogWarning(
                    $"TaxiwayNetwork: Skipping cell {position}; " +
                    "its BuildableDefinition is missing."
                );

                continue;
            }

            if (string.IsNullOrWhiteSpace(cell.Definition.id))
            {
                Debug.LogWarning(
                    $"TaxiwayNetwork: Skipping cell {position}; " +
                    "its definition has no stable ID."
                );

                continue;
            }

            result.Add(new TaxiwayCellSaveData
            {
                x = position.x,
                z = position.y,
                definitionId = cell.Definition.id,
                isCenterline = centerlineCells.Contains(position)
            });
        }

        return result;
    }
}