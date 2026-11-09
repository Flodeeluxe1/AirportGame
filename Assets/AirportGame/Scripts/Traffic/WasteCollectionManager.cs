using UnityEngine;

/// <summary>
/// Tracks airport waste levels and requests collectors when a disposal
/// reaches the configured fill percentage. Place this component on the
/// scene object named WasteCollectionManager and reference the spawner on
/// the separate RoadTrafficSpawner object.
/// </summary>
[DisallowMultipleComponent]
public class WasteCollectionManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GarbageCollectorSpawner garbageCollectorSpawner;

    [Header("Disposal Costs")]
    [Min(0f)]
    [SerializeField] private float fixedCollectionFeePerTrip = 25f;
    [Min(0f)]
    [SerializeField] private float disposalFeePerUnit = 0.2f;

    [Header("Automatic Collection")]
    [Range(0f, 100f)]
    [SerializeField] private float dispatchThresholdPercent = 75f;
    [Min(0.1f)]
    [SerializeField] private float scanInterval = 2f;

    [Header("Diagnostics")]
    [SerializeField] private bool logCollectionChecks = true;

    [Header("Runtime")]
    [SerializeField] private int trackedDisposalCount;
    [SerializeField] private float totalCurrentWaste;
    [SerializeField] private float totalWasteCapacity;

    private float scanTimer;
    private AirportEconomyManager economyManager;

    public int TrackedDisposalCount => trackedDisposalCount;
    public float TotalCurrentWaste => totalCurrentWaste;
    public float TotalWasteCapacity => totalWasteCapacity;
    public float TotalRemainingCapacity =>
        Mathf.Max(0f, totalWasteCapacity - totalCurrentWaste);

    private void Awake()
    {
        economyManager = FindFirstObjectByType<AirportEconomyManager>();

        if (garbageCollectorSpawner == null)
            garbageCollectorSpawner = FindFirstObjectByType<GarbageCollectorSpawner>();

        if (garbageCollectorSpawner == null)
        {
            Debug.LogError(
                "WasteCollectionManager: Assign the GarbageCollectorSpawner from the RoadTrafficSpawner GameObject.",
                this);
            enabled = false;
        }
    }

    private void Update()
    {
        scanTimer += Time.deltaTime;
        if (scanTimer < scanInterval)
            return;

        scanTimer = 0f;
        RefreshWasteTotals();

        if (logCollectionChecks)
        {
            float totalPercent = totalWasteCapacity > 0f
                ? totalCurrentWaste / totalWasteCapacity * 100f
                : 0f;

            Debug.Log(
                $"WASTE COLLECTION CHECK | Airport total: {totalCurrentWaste:F1}/{totalWasteCapacity:F1} " +
                $"({totalPercent:F1}%) | Per-disposal dispatch threshold: {dispatchThresholdPercent:F1}% | " +
                $"Disposals: {trackedDisposalCount} | Active collectors: " +
                $"{(garbageCollectorSpawner != null ? garbageCollectorSpawner.ActiveCollectorCount : 0)}/" +
                $"{(garbageCollectorSpawner != null ? garbageCollectorSpawner.MaximumActiveCollectors : 0)}",
                this);
        }

        if (garbageCollectorSpawner == null || !garbageCollectorSpawner.isActiveAndEnabled)
        {
            if (logCollectionChecks)
                Debug.LogWarning("WASTE COLLECTION CHECK BLOCKED | GarbageCollectorSpawner is missing or disabled.", this);
            return;
        }

        if (garbageCollectorSpawner.ActiveCollectorCount >=
            garbageCollectorSpawner.MaximumActiveCollectors)
        {
            if (logCollectionChecks)
                Debug.Log("WASTE COLLECTION CHECK BLOCKED | Maximum active collector count reached.", this);
            return;
        }

        while (garbageCollectorSpawner.ActiveCollectorCount <
               garbageCollectorSpawner.MaximumActiveCollectors)
        {
            if (!garbageCollectorSpawner.TryDispatchCollector(
                    dispatchThresholdPercent))
                break;
        }
    }

    /// <summary>
    /// Adds generated waste to available dumpster capacity across all active
    /// airport waste disposals. Returns the amount successfully stored.
    /// </summary>
    public float AddWasteToAirport(float amount)
    {
        if (amount <= 0f)
            return 0f;

        WasteDisposal[] disposals =
            FindObjectsByType<WasteDisposal>(FindObjectsSortMode.None);

        float remaining = amount;
        float acceptedTotal = 0f;

        foreach (WasteDisposal disposal in disposals)
        {
            if (disposal == null || !disposal.gameObject.activeInHierarchy ||
                disposal.RemainingWasteCapacity <= 0.001f)
                continue;

            float accepted = disposal.AddWaste(remaining);
            acceptedTotal += accepted;
            remaining -= accepted;

            if (remaining <= 0.001f)
                break;
        }

        RefreshWasteTotals();
        return acceptedTotal;
    }

    /// <summary>
    /// Charges the airport once for a completed garbage collector trip.
    /// The charge combines a fixed trip fee and a fee per collected waste unit.
    /// Currency is rounded up to the next whole unit because the economy balance
    /// stores whole currency units.
    /// </summary>
    public bool ChargeCompletedCollectionTrip(float collectedUnits)
    {
        if (collectedUnits <= 0.001f)
            return false;

        if (economyManager == null)
            economyManager = FindFirstObjectByType<AirportEconomyManager>();

        if (economyManager == null)
        {
            Debug.LogError(
                "WasteCollectionManager: Cannot charge waste collection because AirportEconomyManager was not found.",
                this);
            return false;
        }

        float calculatedCost =
            fixedCollectionFeePerTrip + collectedUnits * disposalFeePerUnit;
        int charge = Mathf.CeilToInt(calculatedCost);

        if (charge <= 0)
            return true;

        string reason = $"Waste collection ({collectedUnits:F1} units)";

        if (!economyManager.TrySpend(charge, reason))
        {
            Debug.LogWarning(
                $"WASTE COLLECTION CHARGE FAILED | {reason} | Cost: ${charge:N0}.",
                this);
            return false;
        }

        Debug.Log(
            $"WASTE COLLECTION CHARGED | {reason} | Fixed fee: ${fixedCollectionFeePerTrip:F2} | " +
            $"Variable fee: ${(collectedUnits * disposalFeePerUnit):F2} | Total charged: ${charge:N0}.",
            this);

        return true;
    }

    private void RefreshWasteTotals()
    {
        trackedDisposalCount = 0;
        totalCurrentWaste = 0f;
        totalWasteCapacity = 0f;

        WasteDisposal[] disposals =
            FindObjectsByType<WasteDisposal>(FindObjectsSortMode.None);

        foreach (WasteDisposal disposal in disposals)
        {
            if (disposal == null || !disposal.gameObject.activeInHierarchy)
                continue;

            trackedDisposalCount++;
            totalCurrentWaste += disposal.CurrentWaste;
            totalWasteCapacity += disposal.MaximumWasteCapacity;
        }
    }
}