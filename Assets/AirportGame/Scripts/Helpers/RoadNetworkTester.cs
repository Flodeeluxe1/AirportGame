using System.Collections.Generic;
using UnityEngine;

public class RoadNetworkTester : MonoBehaviour
{
    [SerializeField] private RoadNetwork roadNetwork;

    [Header("Test Cells")]
    [SerializeField] private Vector2Int startCell;
    [SerializeField] private Vector2Int targetCell;

    [ContextMenu("Test Path")]
    private void TestPath()
    {
        if (roadNetwork == null)
        {
            Debug.LogError(
                "RoadNetworkTester: RoadNetwork is not assigned."
            );

            return;
        }

        List<Vector2Int> path =
            roadNetwork.FindPath(
                startCell,
                targetCell
            );

        if (path.Count == 0)
        {
            Debug.Log(
                $"NO PATH: {startCell} -> {targetCell}"
            );

            return;
        }

        Debug.Log(
            $"PATH FOUND: {startCell} -> " +
            $"{targetCell} | " +
            $"Cells: {path.Count}"
        );

        string pathText = "";

        foreach (Vector2Int cell in path)
        {
            pathText +=
                cell +
                " ";
        }

        Debug.Log(
            "Path: " +
            pathText
        );
    }
}