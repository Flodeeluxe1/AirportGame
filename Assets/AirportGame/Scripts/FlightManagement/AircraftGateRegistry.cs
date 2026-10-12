
using System.Collections.Generic;
using UnityEngine;

public class AircraftGateRegistry : MonoBehaviour
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
            FindObjectsByType<AircraftGate>(
                FindObjectsSortMode.None
            );

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

        Debug.Log(
            $"========== AIRCRAFT GATES ({gates.Count}) ==========",
            this
        );

        foreach (AircraftGate gate in gates)
        {
            Debug.Log(
                $"{gate.DisplayName} | " +
                $"Size: {gate.Size} | " +
                $"ID: {gate.StableId}",
                gate
            );
        }

        Debug.Log(
            "========== END AIRCRAFT GATES ==========",
            this
        );
    }
}