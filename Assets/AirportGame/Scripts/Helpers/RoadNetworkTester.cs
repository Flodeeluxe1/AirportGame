using System.Collections.Generic;
using UnityEngine;

public class RoadNetworkTester : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    [SerializeField] private RoadNetwork roadNetwork;

    [Header("Test Cells")]
    [SerializeField] private Vector2Int startCell;
    [SerializeField] private Vector2Int targetCell;

    [ContextMenu("Test Path")]
    private void TestPath()
    {
        if (roadNetwork == null)
        {
            LogError(
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
            Log(
                $"NO PATH: {startCell} -> {targetCell}"
            );

            return;
        }

        Log(
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

        Log(
            "Path: " +
            pathText
        );
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}