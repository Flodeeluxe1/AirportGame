
using System.Collections.Generic;
using UnityEngine;

public class AircraftGateRegistry : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
{
    private readonly List<AircraftGate> gates =
        new List<AircraftGate>();

    public IReadOnlyList<AircraftGate> Gates => gates;

    private void Awake()
    {
        RefreshGates();
    }

    public void RefreshGates()
    {
        gates.Clear();

        AircraftGate[] foundGates =
            FindObjectsByType<AircraftGate>();

        foreach (AircraftGate gate in foundGates)
        {
            if (gate == null)
                continue;

            // Only count placed gates that have a valid grid origin.
            if (!gate.TryGetGridOrigin(out _))
                continue;

            if (string.IsNullOrEmpty(gate.StableId))
                continue;

            gates.Add(gate);
        }

        gates.Sort(
            (a, b) => string.CompareOrdinal(
                a.StableId,
                b.StableId
            )
        );
    }

    public List<AircraftGate> GetCompatibleGates(
        AircraftGateSize requiredSize)
    {
        RefreshGates();

        List<AircraftGate> compatibleGates =
            new List<AircraftGate>();

        foreach (AircraftGate gate in gates)
        {
            if (gate.CanAccommodate(requiredSize))
                compatibleGates.Add(gate);
        }

        return compatibleGates;
    }

    public bool TryGetGate(
        string stableId,
        out AircraftGate result)
    {
        RefreshGates();

        foreach (AircraftGate gate in gates)
        {
            if (gate.StableId == stableId)
            {
                result = gate;
                return true;
            }
        }

        result = null;
        return false;
    }

    [ContextMenu("Refresh And Debug Built Gates")]
    private void DebugBuiltGates()
    {
        RefreshGates();

        Log(
            $"========== AIRCRAFT GATES ({gates.Count}) ==========",
            this
        );

        foreach (AircraftGate gate in gates)
        {
            Log(
                $"{gate.DisplayName} | " +
                $"Size: {gate.Size} | " +
                $"ID: {gate.StableId}",
                gate
            );
        }

        Log(
            "========== END AIRCRAFT GATES ==========",
            this
        );
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}