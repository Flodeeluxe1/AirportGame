
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
                FindAnyObjectByType<TaxiwayNetwork>();
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

        if (!IsSuitableGroundMovementCell(
                start,
                minimumTaxiwayWidth) ||
            !IsSuitableGroundMovementCell(
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

            TryVisitSingleTargetNeighbor(current, current + Vector2Int.up, target,
                minimumTaxiwayWidth, gScore[current], openSet, cameFrom, gScore, closedSet);
            TryVisitSingleTargetNeighbor(current, current + Vector2Int.right, target,
                minimumTaxiwayWidth, gScore[current], openSet, cameFrom, gScore, closedSet);
            TryVisitSingleTargetNeighbor(current, current + Vector2Int.down, target,
                minimumTaxiwayWidth, gScore[current], openSet, cameFrom, gScore, closedSet);
            TryVisitSingleTargetNeighbor(current, current + Vector2Int.left, target,
                minimumTaxiwayWidth, gScore[current], openSet, cameFrom, gScore, closedSet);
        }

        return emptyPath;
    }

    /// <summary>
    /// Finds the lowest-cost route from one start cell to any supplied target.
    /// This performs one A* search for the complete target set instead of one
    /// separate search for every target.
    /// </summary>
    public List<Vector2Int> FindPathToBestTarget(
        Vector2Int start,
        IReadOnlyDictionary<Vector2Int, float> targetApproachCosts,
        out Vector2Int bestTarget,
        out float bestTotalCost,
        int minimumTaxiwayWidth = 1)
    {
        bestTarget = default;
        bestTotalCost = float.MaxValue;

        if (taxiwayNetwork == null ||
            targetApproachCosts == null ||
            targetApproachCosts.Count == 0)
        {
            return new List<Vector2Int>();
        }

        minimumTaxiwayWidth = Mathf.Max(1, minimumTaxiwayWidth);

        if (!IsSuitableGroundMovementCell(start, minimumTaxiwayWidth))
            return new List<Vector2Int>();

        int targetCount = 0;
        int minimumX = int.MaxValue;
        int maximumX = int.MinValue;
        int minimumY = int.MaxValue;
        int maximumY = int.MinValue;

        foreach (KeyValuePair<Vector2Int, float> entry in targetApproachCosts)
        {
            if (entry.Key == start ||
                !IsSuitableGroundMovementCell(entry.Key, minimumTaxiwayWidth))
                continue;

            targetCount++;
            minimumX = Mathf.Min(minimumX, entry.Key.x);
            maximumX = Mathf.Max(maximumX, entry.Key.x);
            minimumY = Mathf.Min(minimumY, entry.Key.y);
            maximumY = Mathf.Max(maximumY, entry.Key.y);
        }

        if (targetCount == 0)
            return new List<Vector2Int>();

        MinPriorityQueue openSet = new MinPriorityQueue();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        Dictionary<Vector2Int, float> gScore = new Dictionary<Vector2Int, float>();
        HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();

        gScore[start] = 0f;
        openSet.Enqueue(
            start,
            GetTargetBoundsHeuristic(start, minimumX, maximumX, minimumY, maximumY));

        bool foundTarget = false;

        while (openSet.Count > 0)
        {
            // The heuristic is a lower bound (at least one cost unit per
            // grid step), so no remaining node can improve this target.
            if (foundTarget && openSet.PeekPriority >= bestTotalCost)
                break;

            Vector2Int current = openSet.Dequeue();

            if (!closedSet.Add(current))
                continue;

            float currentGScore = gScore[current];

            if (targetApproachCosts.TryGetValue(current, out float approachCost) &&
                current != start)
            {
                float totalCost = currentGScore + approachCost;

                if (totalCost < bestTotalCost)
                {
                    bestTotalCost = totalCost;
                    bestTarget = current;
                    foundTarget = true;
                }
            }

            TryVisitGroundNeighbor(
                current, current + Vector2Int.up, currentGScore,
                minimumTaxiwayWidth,
                minimumX, maximumX, minimumY, maximumY,
                openSet, cameFrom, gScore, closedSet);

            TryVisitGroundNeighbor(
                current, current + Vector2Int.right, currentGScore,
                minimumTaxiwayWidth,
                minimumX, maximumX, minimumY, maximumY,
                openSet, cameFrom, gScore, closedSet);

            TryVisitGroundNeighbor(
                current, current + Vector2Int.down, currentGScore,
                minimumTaxiwayWidth,
                minimumX, maximumX, minimumY, maximumY,
                openSet, cameFrom, gScore, closedSet);

            TryVisitGroundNeighbor(
                current, current + Vector2Int.left, currentGScore,
                minimumTaxiwayWidth,
                minimumX, maximumX, minimumY, maximumY,
                openSet, cameFrom, gScore, closedSet);
        }

        if (!foundTarget)
            return new List<Vector2Int>();

        return ReconstructPath(cameFrom, start, bestTarget);
    }

    private void TryVisitSingleTargetNeighbor(
        Vector2Int current,
        Vector2Int next,
        Vector2Int target,
        int minimumTaxiwayWidth,
        float currentGScore,
        MinPriorityQueue openSet,
        Dictionary<Vector2Int, Vector2Int> cameFrom,
        Dictionary<Vector2Int, float> gScore,
        HashSet<Vector2Int> closedSet)
    {
        if (closedSet.Contains(next) ||
            !IsSuitableGroundMovementCell(next, minimumTaxiwayWidth))
            return;

        float movementCost = taxiwayNetwork.IsRunway(next)
            ? 100f
            : taxiwayNetwork.IsCenterlineCell(next) ? 1f : offCenterlineCost;

        float tentative = currentGScore + movementCost;
        if (gScore.TryGetValue(next, out float oldScore) && tentative >= oldScore)
            return;

        cameFrom[next] = current;
        gScore[next] = tentative;
        openSet.Enqueue(next, tentative + GetHeuristic(next, target));
    }

    /// <summary>
    /// Finds the best route from any supplied start cell to any supplied
    /// approach cell in one shared search. This replaces one full A* search
    /// per candidate aircraft start position.
    /// </summary>
    public List<Vector2Int> FindPathFromBestStartToBestTarget(
        IReadOnlyCollection<Vector2Int> starts,
        IReadOnlyDictionary<Vector2Int, float> targetApproachCosts,
        out Vector2Int bestStart,
        out Vector2Int bestTarget,
        out float bestTotalCost,
        int minimumTaxiwayWidth = 1)
    {
        bestStart = default;
        bestTarget = default;
        bestTotalCost = float.MaxValue;

        if (taxiwayNetwork == null || starts == null || starts.Count == 0 ||
            targetApproachCosts == null || targetApproachCosts.Count == 0)
            return new List<Vector2Int>();

        minimumTaxiwayWidth = Mathf.Max(1, minimumTaxiwayWidth);

        int minimumX = int.MaxValue;
        int maximumX = int.MinValue;
        int minimumY = int.MaxValue;
        int maximumY = int.MinValue;
        int targetCount = 0;

        foreach (KeyValuePair<Vector2Int, float> entry in targetApproachCosts)
        {
            if (!IsSuitableGroundMovementCell(entry.Key, minimumTaxiwayWidth))
                continue;

            targetCount++;
            minimumX = Mathf.Min(minimumX, entry.Key.x);
            maximumX = Mathf.Max(maximumX, entry.Key.x);
            minimumY = Mathf.Min(minimumY, entry.Key.y);
            maximumY = Mathf.Max(maximumY, entry.Key.y);
        }

        if (targetCount == 0)
            return new List<Vector2Int>();

        HashSet<Vector2Int> startSet = new HashSet<Vector2Int>();
        MinPriorityQueue openSet = new MinPriorityQueue();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        Dictionary<Vector2Int, float> gScore = new Dictionary<Vector2Int, float>();
        HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();

        foreach (Vector2Int start in starts)
        {
            if (!IsSuitableGroundMovementCell(start, minimumTaxiwayWidth) ||
                !startSet.Add(start))
                continue;

            gScore[start] = 0f;
            openSet.Enqueue(start,
                GetTargetBoundsHeuristic(start, minimumX, maximumX, minimumY, maximumY));
        }

        if (startSet.Count == 0)
            return new List<Vector2Int>();

        bool foundTarget = false;

        while (openSet.Count > 0)
        {
            if (foundTarget && openSet.PeekPriority >= bestTotalCost)
                break;

            Vector2Int current = openSet.Dequeue();
            if (!closedSet.Add(current))
                continue;

            float currentScore = gScore[current];
            if (!startSet.Contains(current) &&
                targetApproachCosts.TryGetValue(current, out float approachCost))
            {
                float totalCost = currentScore + approachCost;
                if (totalCost < bestTotalCost)
                {
                    bestTotalCost = totalCost;
                    bestTarget = current;
                    foundTarget = true;
                }
            }

            TryVisitGroundNeighbor(current, current + Vector2Int.up, currentScore,
                minimumTaxiwayWidth, minimumX, maximumX, minimumY, maximumY,
                openSet, cameFrom, gScore, closedSet);
            TryVisitGroundNeighbor(current, current + Vector2Int.right, currentScore,
                minimumTaxiwayWidth, minimumX, maximumX, minimumY, maximumY,
                openSet, cameFrom, gScore, closedSet);
            TryVisitGroundNeighbor(current, current + Vector2Int.down, currentScore,
                minimumTaxiwayWidth, minimumX, maximumX, minimumY, maximumY,
                openSet, cameFrom, gScore, closedSet);
            TryVisitGroundNeighbor(current, current + Vector2Int.left, currentScore,
                minimumTaxiwayWidth, minimumX, maximumX, minimumY, maximumY,
                openSet, cameFrom, gScore, closedSet);
        }

        if (!foundTarget)
            return new List<Vector2Int>();

        List<Vector2Int> path = ReconstructPathToAnyStart(cameFrom, startSet, bestTarget);
        if (path.Count > 0)
            bestStart = path[0];

        return path;
    }

    private List<Vector2Int> ReconstructPathToAnyStart(
        Dictionary<Vector2Int, Vector2Int> cameFrom,
        HashSet<Vector2Int> starts,
        Vector2Int target)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int current = target;
        path.Add(current);

        while (!starts.Contains(current))
        {
            if (!cameFrom.TryGetValue(current, out Vector2Int previous))
                return new List<Vector2Int>();

            current = previous;
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private void TryVisitGroundNeighbor(
        Vector2Int current,
        Vector2Int next,
        float currentGScore,
        int minimumTaxiwayWidth,
        int minimumX,
        int maximumX,
        int minimumY,
        int maximumY,
        MinPriorityQueue openSet,
        Dictionary<Vector2Int, Vector2Int> cameFrom,
        Dictionary<Vector2Int, float> gScore,
        HashSet<Vector2Int> closedSet)
    {
        if (closedSet.Contains(next) ||
            !IsSuitableGroundMovementCell(next, minimumTaxiwayWidth))
        {
            return;
        }

        float movementCost;

        if (taxiwayNetwork.IsRunway(next))
            movementCost = 100f;
        else
            movementCost = taxiwayNetwork.IsCenterlineCell(next)
                ? 1f
                : offCenterlineCost;

        float tentativeGScore = currentGScore + movementCost;

        if (gScore.TryGetValue(next, out float existingScore) &&
            tentativeGScore >= existingScore)
        {
            return;
        }

        cameFrom[next] = current;
        gScore[next] = tentativeGScore;

        float priority =
            tentativeGScore +
            GetTargetBoundsHeuristic(next, minimumX, maximumX, minimumY, maximumY);

        openSet.Enqueue(next, priority);
    }

    private float GetTargetBoundsHeuristic(
        Vector2Int current,
        int minimumX,
        int maximumX,
        int minimumY,
        int maximumY)
    {
        int distanceX = current.x < minimumX
            ? minimumX - current.x
            : current.x > maximumX
                ? current.x - maximumX
                : 0;

        int distanceY = current.y < minimumY
            ? minimumY - current.y
            : current.y > maximumY
                ? current.y - maximumY
                : 0;

        // Distance to the target bounding rectangle is a cheap lower bound
        // for the distance to any target cell.
        return distanceX + distanceY;
    }

    private bool IsSuitableGroundMovementCell(
        Vector2Int cell,
        int minimumTaxiwayWidth)
    {
        if (!taxiwayNetwork.IsGroundMovementCell(cell))
            return false;

        // Runway cells are traversable regardless of taxiway-width rules.
        if (taxiwayNetwork.IsRunway(cell))
            return true;

        // Taxiway width restrictions remain unchanged.
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
        public float PeekPriority => nodes.Count > 0 ? nodes[0].Priority : float.MaxValue;

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