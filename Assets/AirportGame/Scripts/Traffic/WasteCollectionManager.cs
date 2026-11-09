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

    [Header("Automatic Collection")]
    [Range(0f, 100f)]
    [SerializeField] private float dispatchThresholdPercent = 75f;
    [Min(0.1f)]
    [SerializeField] private float scanInterval = 2f;

    [Header("Runtime")]
    [SerializeField] private int trackedDisposalCount;
    [SerializeField] private float totalCurrentWaste;
    [SerializeField] private float totalWasteCapacity;

    private float scanTimer;

    public int TrackedDisposalCount => trackedDisposalCount;
    public float TotalCurrentWaste => totalCurrentWaste;
    public float TotalWasteCapacity => totalWasteCapacity;
    public float TotalRemainingCapacity =>
        Mathf.Max(0f, totalWasteCapacity - totalCurrentWaste);

    private void Awake()
    {
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

        while (garbageCollectorSpawner != null &&
               garbageCollectorSpawner.isActiveAndEnabled &&
               garbageCollectorSpawner.ActiveCollectorCount <
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