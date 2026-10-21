
using System.Collections.Generic;
using UnityEngine;

public class RoadNetwork : MonoBehaviour
{
    public const int RoadModuleSize = 2;

    private class RoadCell
    {
        public Vector2Int Position;
        public RoadDirection Direction;

        public bool ConnectNorth;
        public bool ConnectEast;
        public bool ConnectSouth;
        public bool ConnectWest;

        public RoadCell(Vector2Int position, RoadDirection direction)
        {
            Position = position;
            Direction = direction;
        }
    }

    private readonly Dictionary<Vector2Int, RoadCell> roadCells =
        new Dictionary<Vector2Int, RoadCell>();

    private static readonly Vector2Int North = new Vector2Int(0, 2);
    private static readonly Vector2Int East = new Vector2Int(2, 0);
    private static readonly Vector2Int South = new Vector2Int(0, -2);
    private static readonly Vector2Int West = new Vector2Int(-2, 0);

    public Vector2Int GetRoadAnchor(Vector2Int cell)
    {
        return new Vector2Int(
            Mathf.FloorToInt(cell.x / (float)RoadModuleSize) * RoadModuleSize,
            Mathf.FloorToInt(cell.y / (float)RoadModuleSize) * RoadModuleSize
        );
    }

    public Vector3 GetRoadCenterWorldPosition(
        Vector2Int cell,
        GridManager gridManager)
    {
        Vector2Int anchor = GetRoadAnchor(cell);

        Vector3 position = gridManager.GridToWorld(anchor);
        position.x += gridManager.CellSize;
        position.z += gridManager.CellSize;

        return position;
    }

    public void AddRoad(Vector2Int position, RoadDirection direction)
    {
        position = GetRoadAnchor(position);

        if (roadCells.TryGetValue(position, out RoadCell existing))
        {
            existing.Direction = direction;
            UpdateConnectionsAround(position);
            return;
        }

        roadCells.Add(position, new RoadCell(position, direction));
        UpdateConnectionsAround(position);
    }

    public void RemoveRoad(Vector2Int position)
    {
        position = GetRoadAnchor(position);

        if (!roadCells.Remove(position))
            return;

        UpdateConnectionsAround(position);
    }

    public bool IsRoad(Vector2Int position)
    {
        return roadCells.ContainsKey(GetRoadAnchor(position));
    }

    public int GetConnectionMask(Vector2Int position)
    {
        position = GetRoadAnchor(position);

        if (!roadCells.TryGetValue(position, out RoadCell cell))
            return 0;

        int mask = 0;

        if (cell.ConnectNorth) mask |= 1;
        if (cell.ConnectEast) mask |= 2;
        if (cell.ConnectSouth) mask |= 4;
        if (cell.ConnectWest) mask |= 8;

        return mask;
    }

    public List<Vector2Int> GetRoadPositions()
    {
        return new List<Vector2Int>(roadCells.Keys);
    }

    private void UpdateConnectionsAround(Vector2Int position)
    {
        UpdateConnectionsForCell(position);
        UpdateConnectionsForCell(position + North);
        UpdateConnectionsForCell(position + East);
        UpdateConnectionsForCell(position + South);
        UpdateConnectionsForCell(position + West);
    }

    private void UpdateConnectionsForCell(Vector2Int position)
    {
        if (!roadCells.TryGetValue(position, out RoadCell cell))
            return;

        cell.ConnectNorth = false;
        cell.ConnectEast = false;
        cell.ConnectSouth = false;
        cell.ConnectWest = false;

        UpdateConnection(cell, RoadDirection.North, North);
        UpdateConnection(cell, RoadDirection.East, East);
        UpdateConnection(cell, RoadDirection.South, South);
        UpdateConnection(cell, RoadDirection.West, West);
    }

    private void UpdateConnection(
        RoadCell cell,
        RoadDirection direction,
        Vector2Int offset)
    {
        Vector2Int neighborPosition = cell.Position + offset;

        if (!roadCells.TryGetValue(neighborPosition, out RoadCell neighbor))
            return;

        SetConnection(cell, direction, true);
        SetConnection(neighbor, GetOpposite(direction), true);
    }

    private void SetConnection(
        RoadCell cell,
        RoadDirection direction,
        bool connected)
    {
        switch (direction)
        {
            case RoadDirection.North: cell.ConnectNorth = connected; break;
            case RoadDirection.East: cell.ConnectEast = connected; break;
            case RoadDirection.South: cell.ConnectSouth = connected; break;
            case RoadDirection.West: cell.ConnectWest = connected; break;
        }
    }

    private RoadDirection GetOpposite(RoadDirection direction)
    {
        switch (direction)
        {
            case RoadDirection.North: return RoadDirection.South;
            case RoadDirection.East: return RoadDirection.West;
            case RoadDirection.South: return RoadDirection.North;
            case RoadDirection.West: return RoadDirection.East;
            default: return RoadDirection.North;
        }
    }

    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int target)
    {
        start = GetRoadAnchor(start);
        target = GetRoadAnchor(target);

        List<Vector2Int> empty = new List<Vector2Int>();

        if (!roadCells.ContainsKey(start) || !roadCells.ContainsKey(target))
            return empty;

        if (start == target)
            return new List<Vector2Int> { start };

        var openSet = new PriorityQueue();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, int>();
        var closedSet = new HashSet<Vector2Int>();

        gScore[start] = 0;
        openSet.Enqueue(start, GetHeuristic(start, target));

        while (openSet.Count > 0)
        {
            Vector2Int current = openSet.Dequeue();

            if (closedSet.Contains(current))
                continue;

            if (current == target)
                return ReconstructPath(cameFrom, start, target);

            closedSet.Add(current);

            foreach (Vector2Int next in GetValidNextRoadCells(current))
            {
                if (closedSet.Contains(next))
                    continue;

                int tentative = gScore[current] + 1;

                if (gScore.TryGetValue(next, out int oldScore) &&
                    tentative >= oldScore)
                    continue;

                cameFrom[next] = current;
                gScore[next] = tentative;

                openSet.Enqueue(next, tentative + GetHeuristic(next, target));
            }
        }

        return empty;
    }

    private int GetHeuristic(Vector2Int current, Vector2Int target)
    {
        return (Mathf.Abs(current.x - target.x) +
                Mathf.Abs(current.y - target.y)) / RoadModuleSize;
    }

    private List<Vector2Int> GetValidNextRoadCells(Vector2Int current)
    {
        var result = new List<Vector2Int>();

        if (!roadCells.TryGetValue(current, out RoadCell cell))
            return result;

        AddIfValid(result, cell, RoadDirection.North, North);
        AddIfValid(result, cell, RoadDirection.East, East);
        AddIfValid(result, cell, RoadDirection.South, South);
        AddIfValid(result, cell, RoadDirection.West, West);

        return result;
    }

    private void AddIfValid(
        List<Vector2Int> result,
        RoadCell cell,
        RoadDirection direction,
        Vector2Int offset)
    {
        if (!IsConnected(cell, direction))
            return;

        Vector2Int next = cell.Position + offset;

        if (!roadCells.TryGetValue(next, out RoadCell nextCell))
            return;

        // Preserve the existing one-way rule:
        // either the current road or the next road must support this direction.
        bool currentSupportsMovement = cell.Direction == direction;
        bool nextSupportsMovement = nextCell.Direction == direction;

        if (currentSupportsMovement || nextSupportsMovement)
            result.Add(next);
    }

    private bool IsConnected(RoadCell cell, RoadDirection direction)
    {
        switch (direction)
        {
            case RoadDirection.North: return cell.ConnectNorth;
            case RoadDirection.East: return cell.ConnectEast;
            case RoadDirection.South: return cell.ConnectSouth;
            case RoadDirection.West: return cell.ConnectWest;
            default: return false;
        }
    }

    private List<Vector2Int> ReconstructPath(
        Dictionary<Vector2Int, Vector2Int> cameFrom,
        Vector2Int start,
        Vector2Int target)
    {
        var path = new List<Vector2Int>();
        Vector2Int current = target;
        path.Add(current);

        while (current != start)
        {
            if (!cameFrom.TryGetValue(current, out Vector2Int previous))
                return new List<Vector2Int>();

            current = previous;
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    [ContextMenu("Debug All Road Cells")]
    private void DebugAllRoadCells()
    {
        Debug.Log($"========== ROAD NETWORK: {roadCells.Count} MODULES ==========");

        foreach (var entry in roadCells)
        {
            RoadCell cell = entry.Value;

            Debug.Log(
                $"ROAD MODULE {cell.Position} | Direction: {cell.Direction} | " +
                $"Connections: {GetConnectionMask(cell.Position)}");
        }
    }

    public void Clear()
    {
        roadCells.Clear();
    }

    public List<RoadCellSaveData> GetSaveData()
    {
        var result = new List<RoadCellSaveData>();

        foreach (var entry in roadCells)
        {
            RoadCell cell = entry.Value;

            int directionX = 0;
            int directionZ = 0;

            switch (cell.Direction)
            {
                case RoadDirection.North: directionZ = 1; break;
                case RoadDirection.East: directionX = 1; break;
                case RoadDirection.South: directionZ = -1; break;
                case RoadDirection.West: directionX = -1; break;
            }

            result.Add(new RoadCellSaveData
            {
                x = cell.Position.x,
                z = cell.Position.y,
                directionX = directionX,
                directionZ = directionZ,
                connectNorth = cell.ConnectNorth,
                connectEast = cell.ConnectEast,
                connectSouth = cell.ConnectSouth,
                connectWest = cell.ConnectWest
            });
        }

        return result;
    }

    private class PriorityQueue
    {
        private class Node
        {
            public Vector2Int Position;
            public int Priority;
            public int Order;

            public Node(Vector2Int position, int priority, int order)
            {
                Position = position;
                Priority = priority;
                Order = order;
            }
        }

        private readonly List<Node> nodes = new List<Node>();
        private int orderCounter;

        public int Count => nodes.Count;

        public void Enqueue(Vector2Int position, int priority)
        {
            nodes.Add(new Node(position, priority, orderCounter++));
        }

        public Vector2Int Dequeue()
        {
            int bestIndex = 0;

            for (int i = 1; i < nodes.Count; i++)
            {
                if (nodes[i].Priority < nodes[bestIndex].Priority ||
                    (nodes[i].Priority == nodes[bestIndex].Priority &&
                     nodes[i].Order < nodes[bestIndex].Order))
                {
                    bestIndex = i;
                }
            }

            Vector2Int result = nodes[bestIndex].Position;
            nodes.RemoveAt(bestIndex);
            return result;
        }
    }
}
