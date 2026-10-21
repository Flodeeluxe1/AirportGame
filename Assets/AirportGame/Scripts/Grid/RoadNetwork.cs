using System;
using System.Collections.Generic;
using UnityEngine;

public class RoadNetwork : MonoBehaviour
{
    private class RoadCell
    {
        public Vector2Int Position;
        public RoadDirection Direction;

        public bool ConnectNorth;
        public bool ConnectEast;
        public bool ConnectSouth;
        public bool ConnectWest;

        public RoadCell(
            Vector2Int position,
            RoadDirection direction)
        {
            Position = position;
            Direction = direction;
        }
    }

    private readonly Dictionary<Vector2Int, RoadCell> roadCells =
        new Dictionary<Vector2Int, RoadCell>();

    private static readonly Vector2Int North =
        new Vector2Int(0, 1);

    private static readonly Vector2Int East =
        new Vector2Int(1, 0);

    private static readonly Vector2Int South =
        new Vector2Int(0, -1);

    private static readonly Vector2Int West =
        new Vector2Int(-1, 0);

    public void AddRoad(
        Vector2Int position,
        RoadDirection direction)
    {
        if (roadCells.ContainsKey(position))
        {
            roadCells[position].Direction =
                direction;

            UpdateConnectionsAround(position);

            return;
        }

        roadCells.Add(
            position,
            new RoadCell(
                position,
                direction
            )
        );

        UpdateConnectionsAround(position);
    }



    public int GetConnectionMask(Vector2Int position)
    {
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



    public void RemoveRoad(
        Vector2Int position)
    {
        if (!roadCells.ContainsKey(position))
            return;

        roadCells.Remove(position);

        UpdateConnectionsAround(position);
    }

    public bool IsRoad(
        Vector2Int position)
    {
        return roadCells.ContainsKey(position);
    }

    private void UpdateConnectionsAround(
        Vector2Int position)
    {
        UpdateConnectionsForCell(position);

        UpdateConnectionsForCell(
            position + North
        );

        UpdateConnectionsForCell(
            position + East
        );

        UpdateConnectionsForCell(
            position + South
        );

        UpdateConnectionsForCell(
            position + West
        );
    }

    private void UpdateConnectionsForCell(
        Vector2Int position)
    {
        if (!roadCells.TryGetValue(
                position,
                out RoadCell cell))
        {
            return;
        }

        ResetConnections(cell);

        UpdateConnection(
            cell,
            RoadDirection.North,
            North
        );

        UpdateConnection(
            cell,
            RoadDirection.East,
            East
        );

        UpdateConnection(
            cell,
            RoadDirection.South,
            South
        );

        UpdateConnection(
            cell,
            RoadDirection.West,
            West
        );
    }

    private void UpdateConnection(
        RoadCell cell,
        RoadDirection direction,
        Vector2Int offset)
    {
        Vector2Int neighborPosition =
            cell.Position + offset;

        if (!roadCells.TryGetValue(
                neighborPosition,
                out RoadCell neighbor))
        {
            return;
        }

        /*
         * Physical connection.
         *
         * This says only:
         *
         * "These two road cells touch."
         *
         * It does NOT decide traffic direction.
         */
        SetConnection(
            cell,
            direction,
            true
        );

        SetConnection(
            neighbor,
            GetOpposite(direction),
            true
        );
    }

    private void ResetConnections(
        RoadCell cell)
    {
        cell.ConnectNorth = false;
        cell.ConnectEast = false;
        cell.ConnectSouth = false;
        cell.ConnectWest = false;
    }

    private void SetConnection(
        RoadCell cell,
        RoadDirection direction,
        bool connected)
    {
        switch (direction)
        {
            case RoadDirection.North:
                cell.ConnectNorth = connected;
                break;

            case RoadDirection.East:
                cell.ConnectEast = connected;
                break;

            case RoadDirection.South:
                cell.ConnectSouth = connected;
                break;

            case RoadDirection.West:
                cell.ConnectWest = connected;
                break;
        }
    }

    private RoadDirection GetOpposite(
        RoadDirection direction)
    {
        switch (direction)
        {
            case RoadDirection.North:
                return RoadDirection.South;

            case RoadDirection.East:
                return RoadDirection.West;

            case RoadDirection.South:
                return RoadDirection.North;

            case RoadDirection.West:
                return RoadDirection.East;
        }

        return RoadDirection.North;
    }

    public List<Vector2Int> FindPath(
        Vector2Int start,
        Vector2Int target)
    {
        List<Vector2Int> empty =
            new List<Vector2Int>();

        if (!IsRoad(start) ||
            !IsRoad(target))
        {
            return empty;
        }

        if (start == target)
        {
            return new List<Vector2Int>
            {
                start
            };
        }

        PriorityQueue openSet =
            new PriorityQueue();

        Dictionary<Vector2Int, Vector2Int> cameFrom =
            new Dictionary<Vector2Int, Vector2Int>();

        Dictionary<Vector2Int, int> gScore =
            new Dictionary<Vector2Int, int>();

        HashSet<Vector2Int> closedSet =
            new HashSet<Vector2Int>();

        gScore[start] = 0;

        openSet.Enqueue(
            start,
            GetHeuristic(
                start,
                target
            )
        );

        while (openSet.Count > 0)
        {
            Vector2Int current =
                openSet.Dequeue();

            if (closedSet.Contains(current))
                continue;

            if (current == target)
            {
                return ReconstructPath(
                    cameFrom,
                    start,
                    target
                );
            }

            closedSet.Add(current);

            /*
             * A* itself knows nothing about roads.
             *
             * It simply asks:
             *
             * "What are the valid outgoing
             * successors from this node?"
             */
            foreach (
                Vector2Int next
                in GetValidNextRoadCells(current))
            {
                if (closedSet.Contains(next))
                    continue;

                int tentativeGScore =
                    gScore[current] + 1;

                if (!gScore.TryGetValue(
                        next,
                        out int currentGScore) ||
                    tentativeGScore < currentGScore)
                {
                    cameFrom[next] =
                        current;

                    gScore[next] =
                        tentativeGScore;

                    int fScore =
                        tentativeGScore +
                        GetHeuristic(
                            next,
                            target
                        );

                    openSet.Enqueue(
                        next,
                        fScore
                    );
                }
            }
        }

        return empty;
    }

    private int GetHeuristic(
        Vector2Int current,
        Vector2Int target)
    {
        return Mathf.Abs(
                   current.x -
                   target.x
               ) +
               Mathf.Abs(
                   current.y -
                   target.y
               );
    }

    private List<Vector2Int> GetValidNextRoadCells(
    Vector2Int current)
    {
        List<Vector2Int> result =
            new List<Vector2Int>();

        if (!roadCells.TryGetValue(
                current,
                out RoadCell cell))
        {
            return result;
        }

        /*
         * A* asks the road network for every physically
         * connected neighbor.
         *
         * AddIfValid() decides whether a directed edge
         * exists between the two cells.
         */
        AddIfValid(
            result,
            cell,
            RoadDirection.North,
            North
        );

        AddIfValid(
            result,
            cell,
            RoadDirection.East,
            East
        );

        AddIfValid(
            result,
            cell,
            RoadDirection.South,
            South
        );

        AddIfValid(
            result,
            cell,
            RoadDirection.West,
            West
        );

        return result;
    }

    private void AddIfValid(
    List<Vector2Int> result,
    RoadCell cell,
    RoadDirection direction,
    Vector2Int offset)
    {
        /*
         * There must be a physical connection first.
         */
        if (!IsConnected(
                cell,
                direction))
        {
            return;
        }

        Vector2Int next =
            cell.Position + offset;

        if (!roadCells.TryGetValue(
                next,
                out RoadCell nextCell))
        {
            return;
        }

        /*
         * Directed edge rule:
         *
         * The transition is valid when either:
         *
         * 1. The current road points toward the neighbor.
         *
         * OR
         *
         * 2. The neighbor road points away from the current
         *    cell in the same direction.
         *
         * This allows natural turns without hard-coded
         * corner or junction rules.
         */
        bool currentSupportsMovement =
            cell.Direction == direction;

        bool nextSupportsMovement =
            nextCell.Direction == direction;

        if (!currentSupportsMovement &&
            !nextSupportsMovement)
        {
            return;
        }

        result.Add(next);
    }

    private bool IsConnected(
        RoadCell cell,
        RoadDirection direction)
    {
        switch (direction)
        {
            case RoadDirection.North:
                return cell.ConnectNorth;

            case RoadDirection.East:
                return cell.ConnectEast;

            case RoadDirection.South:
                return cell.ConnectSouth;

            case RoadDirection.West:
                return cell.ConnectWest;
        }

        return false;
    }

    private List<Vector2Int> ReconstructPath(
        Dictionary<Vector2Int, Vector2Int> cameFrom,
        Vector2Int start,
        Vector2Int target)
    {
        List<Vector2Int> path =
            new List<Vector2Int>();

        Vector2Int current =
            target;

        path.Add(current);

        while (current != start)
        {
            if (!cameFrom.TryGetValue(
                    current,
                    out Vector2Int previous))
            {
                return new List<Vector2Int>();
            }

            current =
                previous;

            path.Add(current);
        }

        path.Reverse();

        return path;
    }

    [ContextMenu("Debug All Road Cells")]
    private void DebugAllRoadCells()
    {
        Debug.Log(
            "========== ROAD NETWORK DEBUG =========="
        );

        Debug.Log(
            $"Total Road Cells: {roadCells.Count}"
        );

        foreach (
            KeyValuePair<Vector2Int, RoadCell> entry
            in roadCells)
        {
            RoadCell cell =
                entry.Value;

            Debug.Log(
                BuildDebugText(cell)
            );
        }

        Debug.Log(
            "========== END ROAD NETWORK DEBUG =========="
        );
    }

    private string BuildDebugText(
        RoadCell cell)
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

        return
            $"ROAD CELL {cell.Position}\n" +
            $"Direction: {cell.Direction}\n" +
            $"Connections: {connections}";
    }

    private class PriorityQueue
    {
        private class Node
        {
            public Vector2Int Position;
            public int Priority;
            public int Order;

            public Node(
                Vector2Int position,
                int priority,
                int order)
            {
                Position = position;
                Priority = priority;
                Order = order;
            }
        }

        private readonly List<Node> nodes =
            new List<Node>();

        private int orderCounter;

        public int Count =>
            nodes.Count;

        public void Enqueue(
            Vector2Int position,
            int priority)
        {
            nodes.Add(
                new Node(
                    position,
                    priority,
                    orderCounter++
                )
            );
        }

        public Vector2Int Dequeue()
        {
            int bestIndex = 0;

            for (int i = 1; i < nodes.Count; i++)
            {
                if (nodes[i].Priority <
                    nodes[bestIndex].Priority)
                {
                    bestIndex = i;
                    continue;
                }

                if (nodes[i].Priority ==
                    nodes[bestIndex].Priority &&
                    nodes[i].Order <
                    nodes[bestIndex].Order)
                {
                    bestIndex = i;
                }
            }

            Vector2Int result =
                nodes[bestIndex].Position;

            nodes.RemoveAt(bestIndex);

            return result;
        }
    }

    public void Clear()
    {
        roadCells.Clear();
    }

    public System.Collections.Generic.List<RoadCellSaveData> GetSaveData()
    {
        var result = new System.Collections.Generic.List<RoadCellSaveData>();

        foreach (var entry in roadCells)
        {
            RoadCell cell = entry.Value;

            int directionX = 0;
            int directionZ = 0;

            switch (cell.Direction)
            {
                case RoadDirection.North:
                    directionZ = 1;
                    break;
                case RoadDirection.East:
                    directionX = 1;
                    break;
                case RoadDirection.South:
                    directionZ = -1;
                    break;
                case RoadDirection.West:
                    directionX = -1;
                    break;
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
}
