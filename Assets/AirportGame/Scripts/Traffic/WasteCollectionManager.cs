using UnityEngine;

/// <summary>
/// Tracks airport waste levels and requests collectors when a disposal
/// reaches the configured fill percentage. Add alongside
/// GarbageCollectorSpawner on the shared RoadTrafficSpawner object.
/// </summary>
[DisallowMultipleComponent]
public class WasteCollectionManager : MonoBehaviour
{
    [Header("Automatic Collection")]
    [Range(0f, 100f)]
    [SerializeField] private float dispatchThresholdPercent = 75f;
    [Min(0.1f)]
    [SerializeField] private float scanInterval = 2f;

    [Header("Runtime")]
    [SerializeField] private int trackedDisposalCount;
    [SerializeField] private float totalCurrentWaste;
    [SerializeField] private float totalWasteCapacity;

    private GarbageCollectorSpawner garbageCollectorSpawner;
    private float scanTimer;

    public int TrackedDisposalCount => trackedDisposalCount;
    public float TotalCurrentWaste => totalCurrentWaste;
    public float TotalWasteCapacity => totalWasteCapacity;
    public float TotalRemainingCapacity =>
        Mathf.Max(0f, totalWasteCapacity - totalCurrentWaste);

    private void Awake()
    {
        garbageCollectorSpawner = GetComponent<GarbageCollectorSpawner>();

        if (garbageCollectorSpawner == null)
        {
            Debug.LogError(
                "WasteCollectionManager requires GarbageCollectorSpawner on the same GameObject.",
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
