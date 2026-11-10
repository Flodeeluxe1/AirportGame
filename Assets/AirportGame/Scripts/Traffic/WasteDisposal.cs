using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aggregates the dumpsters belonging to one WasteDisposal prefab.
/// Child dumpsters are discovered dynamically, so runtime additions and removals
/// are reflected immediately in the reported capacity and waste totals.
/// </summary>
[DisallowMultipleComponent]
public class WasteDisposal : MonoBehaviour
{
    private static readonly HashSet<WasteDisposal> activeDisposals =
        new HashSet<WasteDisposal>();

    public static IReadOnlyCollection<WasteDisposal> ActiveDisposals => activeDisposals;

    public static event Action<WasteDisposal> DisposalRegistered;
    public static event Action<WasteDisposal> DisposalUnregistered;
    public static event Action<WasteDisposal, float> WasteAmountChanged;

    private void OnEnable()
    {
        activeDisposals.Add(this);
        DisposalRegistered?.Invoke(this);
    }

    private void OnDisable()
    {
        activeDisposals.Remove(this);
        DisposalUnregistered?.Invoke(this);
    }

    [Header("Dumpster Configuration")]
    [Range(1, 6)]
    [SerializeField] private int maximumDumpsterCount = 6;

    [Header("Stop Point")]
    [SerializeField] private Transform garbageCollectorStopPoint;
    [Min(0.1f)]
    [SerializeField] private float maximumRoadApproachDistance = 8f;

    public float MaximumRoadApproachDistance => Mathf.Max(0.1f, maximumRoadApproachDistance);

    private readonly List<WasteDumpster> dumpsters = new List<WasteDumpster>();

    public int MaximumDumpsterCount => Mathf.Clamp(maximumDumpsterCount, 1, 6);

    public Transform GarbageCollectorStopPoint
    {
        get
        {
            if (garbageCollectorStopPoint == null)
            {
                Transform[] children = GetComponentsInChildren<Transform>(true);
                foreach (Transform child in children)
                {
                    if (child != transform && child.name == "GarbageCollectorStopPoint")
                    {
                        garbageCollectorStopPoint = child;
                        break;
                    }
                }
            }

            return garbageCollectorStopPoint;
        }
    }

    public IReadOnlyList<WasteDumpster> Dumpsters
    {
        get
        {
            RefreshDumpsters();
            return dumpsters;
        }
    }

    public int DumpsterCount
    {
        get
        {
            RefreshDumpsters();
            return dumpsters.Count;
        }
    }

    public float MaximumWasteCapacity
    {
        get
        {
            RefreshDumpsters();
            float total = 0f;
            foreach (WasteDumpster dumpster in dumpsters)
                total += dumpster.MaximumCapacity;
            return total;
        }
    }

    public float CurrentWaste
    {
        get
        {
            RefreshDumpsters();
            float total = 0f;
            foreach (WasteDumpster dumpster in dumpsters)
                total += dumpster.CurrentWaste;
            return total;
        }
    }

    public float RemainingWasteCapacity =>
        Mathf.Max(0f, MaximumWasteCapacity - CurrentWaste);

    public bool HasWaste => CurrentWaste > 0.001f;

    public bool IsAtCapacity =>
        MaximumWasteCapacity > 0f &&
        CurrentWaste >= MaximumWasteCapacity - 0.001f;

    /// <summary>
    /// Adds waste across this disposal's dumpsters in hierarchy order.
    /// Returns the amount accepted; any excess remains with the caller.
    /// </summary>
    public float AddWaste(float amount)
    {
        if (amount <= 0f)
            return 0f;

        RefreshDumpsters();
        float remaining = amount;
        float acceptedTotal = 0f;

        foreach (WasteDumpster dumpster in dumpsters)
        {
            if (remaining <= 0.001f)
                break;

            float accepted = dumpster.AddWaste(remaining);
            acceptedTotal += accepted;
            remaining -= accepted;
        }

        if (acceptedTotal > 0.001f)
            WasteAmountChanged?.Invoke(this, acceptedTotal);

        return acceptedTotal;
    }

    /// <summary>
    /// Collects waste across all child dumpsters up to the requested amount.
    /// Returns the amount actually removed.
    /// </summary>
    public float CollectWaste(float requestedAmount)
    {
        if (requestedAmount <= 0f)
            return 0f;

        RefreshDumpsters();
        float remaining = requestedAmount;
        float collectedTotal = 0f;

        foreach (WasteDumpster dumpster in dumpsters)
        {
            if (remaining <= 0.001f)
                break;

            float collected = dumpster.CollectWaste(remaining);
            collectedTotal += collected;
            remaining -= collected;
        }

        if (collectedTotal > 0.001f)
            WasteAmountChanged?.Invoke(this, -collectedTotal);

        return collectedTotal;
    }

    private void RefreshDumpsters()
    {
        dumpsters.Clear();

        WasteDumpster[] found = GetComponentsInChildren<WasteDumpster>(true);
        int limit = MaximumDumpsterCount;

        foreach (WasteDumpster dumpster in found)
        {
            if (dumpster == null || dumpster.transform == transform)
                continue;

            if (dumpsters.Count >= limit)
                break;

            dumpsters.Add(dumpster);
        }
    }

    private void OnValidate()
    {
        maximumDumpsterCount = Mathf.Clamp(maximumDumpsterCount, 1, 6);
    }
}
