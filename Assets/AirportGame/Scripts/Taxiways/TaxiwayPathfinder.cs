
using System.Collections.Generic;
using UnityEngine;

public class TaxiwayPathfinder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;

    [Header("Path Cost")]
    [Min(1f)]
    [SerializeField] private float offCenterlineCost = 3f;

    [Header("Pathfinding Test")]
    public Vector2Int debugStart;
    public Vector2Int debugTarget;

    [Min(1)]
    public int debugMinimumTaxiwayWidth = 1;

    [TextArea(2, 5)]
    public string debugLastResult = "No pathfinding test run.";

    private void Awake()
    {
        if (taxiwayNetwork == null)
        {
            taxiwayNetwork =
                GetComponent<TaxiwayNetwork>();
        }

        if (taxiwayNetwork == null)
        {
            taxiwayNetwork =
                FindFirstObjectByType<TaxiwayNetwork>();
        }
    }

    public List<Vector2Int> FindPath(
        Vector2Int start,
        Vector2Int target,
        int minimumTaxiwayWidth = 1)
    {
        List<Vector2Int> emptyPath =
            new List<Vector2Int>();

        if (taxiwayNetwork == null)
        {
            Debug.LogError(
                "TaxiwayPathfinder: TaxiwayNetwork is missing."
            );

            return emptyPath;
        }

        minimumTaxiwayWidth =
            Mathf.Max(1, minimumTaxiwayWidth);

        if (!IsSuitableTaxiwayCell(
                start,
                minimumTaxiwayWidth) ||
            !IsSuitableTaxiwayCell(
                target,
                minimumTaxiwayWidth))
        {
            return emptyPath;
        }

        if (start == target)
        {
            return new List<Vector2Int>
            {
                start
            };
        }

        MinPriorityQueue openSet =
            new MinPriorityQueue();

        Dictionary<Vector2Int, Vector2Int> cameFrom =
            new Dictionary<Vector2Int, Vector2Int>();

        Dictionary<Vector2Int, float> gScore =
            new Dictionary<Vector2Int, float>();

        HashSet<Vector2Int> closedSet =
            new HashSet<Vector2Int>();

        gScore[start] = 0f;

        openSet.Enqueue(
            start,
            GetHeuristic(start, target)
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

            List<Vector2Int> neighbors =
                taxiwayNetwork.GetConnectedTaxiwayCells(
                    current
                );

            foreach (Vector2Int next in neighbors)
            {
                if (closedSet.Contains(next))
                    continue;

                if (!IsSuitableTaxiwayCell(
                        next,
                        minimumTaxiwayWidth))
                {
                    continue;
                }

                float movementCost =
                    taxiwayNetwork.IsCenterlineCell(next)
                        ? 1f
                        : offCenterlineCost;

                float tentativeGScore =
                    gScore[current] + movementCost;

                if (!gScore.TryGetValue(
                        next,
                        out float currentGScore) ||
                    tentativeGScore < currentGScore)
                {
                    cameFrom[next] = current;
                    gScore[next] = tentativeGScore;

                    float fScore =
                        tentativeGScore +
                        GetHeuristic(next, target);

                    openSet.Enqueue(
                        next,
                        fScore
                    );
                }
            }
        }

        return emptyPath;
    }

    private bool IsSuitableTaxiwayCell(
        Vector2Int cell,
        int minimumTaxiwayWidth)
    {
        if (!taxiwayNetwork.IsTaxiway(cell))
            return false;

        return taxiwayNetwork.GetTaxiwayWidth(cell) >=
               minimumTaxiwayWidth;
    }

    private float GetHeuristic(
        Vector2Int current,
        Vector2Int target)
    {
        return Mathf.Abs(current.x - target.x) +
               Mathf.Abs(current.y - target.y);
    }

    private List<Vector2Int> ReconstructPath(
        Dictionary<Vector2Int, Vector2Int> cameFrom,
        Vector2Int start,
        Vector2Int target)
    {
        List<Vector2Int> path =
            new List<Vector2Int>();

        Vector2Int current = target;
        path.Add(current);

        while (current != start)
        {
            if (!cameFrom.TryGetValue(
                    current,
                    out Vector2Int previous))
            {
                return new List<Vector2Int>();
            }

            current = previous;
            path.Add(current);
        }

        path.Reverse();

        return path;
    }

    [ContextMenu("Test Taxiway Path")]
    private void TestTaxiwayPath()
    {
        List<Vector2Int> path =
            FindPath(
                debugStart,
                debugTarget,
                debugMinimumTaxiwayWidth
            );

        if (path.Count == 0)
        {
            debugLastResult =
                $"NO PATH | {debugStart} -> {debugTarget}";

            Debug.Log(debugLastResult, this);
            return;
        }

        string pathText = "";

        foreach (Vector2Int cell in path)
        {
            pathText += cell + " ";
        }

        debugLastResult =
            $"PATH FOUND | Cells: {path.Count}\n" +
            pathText;

        Debug.Log(debugLastResult, this);
    }

    private class MinPriorityQueue
    {
        private struct Node
        {
            public Vector2Int Position;
            public float Priority;
            public int Order;

            public Node(
                Vector2Int position,
                float priority,
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

        public int Count => nodes.Count;

        public void Enqueue(
            Vector2Int position,
            float priority)
        {
            Node node = new Node(
                position,
                priority,
                orderCounter++
            );

            nodes.Add(node);

            int index = nodes.Count - 1;

            while (index > 0)
            {
                int parentIndex =
                    (index - 1) / 2;

                if (!ComesBefore(
                        nodes[index],
                        nodes[parentIndex]))
                {
                    break;
                }

                Swap(index, parentIndex);
                index = parentIndex;
            }
        }

        public Vector2Int Dequeue()
        {
            Node result = nodes[0];

            int lastIndex = nodes.Count - 1;
            nodes[0] = nodes[lastIndex];
            nodes.RemoveAt(lastIndex);

            int index = 0;

            while (true)
            {
                int leftIndex = index * 2 + 1;
                int rightIndex = index * 2 + 2;
                int bestIndex = index;

                if (leftIndex < nodes.Count &&
                    ComesBefore(
                        nodes[leftIndex],
                        nodes[bestIndex]))
                {
                    bestIndex = leftIndex;
                }

                if (rightIndex < nodes.Count &&
                    ComesBefore(
                        nodes[rightIndex],
                        nodes[bestIndex]))
                {
                    bestIndex = rightIndex;
                }

                if (bestIndex == index)
                    break;

                Swap(index, bestIndex);
                index = bestIndex;
            }

            return result.Position;
        }

        private bool ComesBefore(
            Node first,
            Node second)
        {
            if (!Mathf.Approximately(
                    first.Priority,
                    second.Priority))
            {
                return first.Priority < second.Priority;
            }

            return first.Order < second.Order;
        }

        private void Swap(
            int firstIndex,
            int secondIndex)
        {
            Node temporary = nodes[firstIndex];

            nodes[firstIndex] = nodes[secondIndex];
            nodes[secondIndex] = temporary;
        }
    }
}