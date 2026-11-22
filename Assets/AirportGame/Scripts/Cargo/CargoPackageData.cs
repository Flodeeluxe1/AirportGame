using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime cargo identity and lifecycle data.
/// This class is deliberately independent of a visual GameObject so cargo
/// can remain represented in the simulation while its box visual is released.
/// </summary>
[Serializable]
public sealed class CargoPackageData
{
    [SerializeField] private string packageId;
    [SerializeField] private CargoDirection direction;
    [SerializeField] private CargoPackageState state = CargoPackageState.Staged;
    [SerializeField] private string flightOperationId;
    [SerializeField] private CargoZone currentZone;

    [NonSerialized] private CargoPackage visual;
    [NonSerialized] private GameObject visualPrefab;

    public CargoPackage Visual => visual;

    public string PackageId => packageId;
    public CargoDirection Direction => direction;
    public CargoPackageState State => state;
    public string FlightOperationId => flightOperationId;
    public CargoZone CurrentZone => currentZone;

    public bool IsAvailableForCollection =>
        state == CargoPackageState.Staged && currentZone != null;

    public CargoPackageData() { }

    public CargoPackageData(
        string id,
        CargoDirection cargoDirection,
        CargoPackageState packageState,
        string assignedFlightOperationId,
        CargoZone startingZone)
    {
        Initialize(
            id,
            cargoDirection,
            packageState,
            assignedFlightOperationId,
            startingZone);
    }

    public void SetVisualPrefab(GameObject prefab)
    {
        visualPrefab = prefab;
    }

    public void AttachVisual(CargoPackage packageVisual)
    {
        visual = packageVisual;
    }

    public void DetachVisual(CargoPackage packageVisual)
    {
        if (visual == packageVisual)
            visual = null;
    }

    public void ReleaseVisual()
    {
        if (visual == null)
            return;

        CargoPackage oldVisual = visual;
        visual = null;
        oldVisual.UnbindDataForPool();
        CargoPackageVisualPool.Release(visualPrefab, oldVisual.gameObject);
    }

    public bool CreateVisualAt(Vector3 position, Quaternion rotation)
    {
        if (visual != null)
        {
            visual.transform.SetPositionAndRotation(position, rotation);
            return true;
        }

        if (visualPrefab == null)
            return false;

        GameObject instance = CargoPackageVisualPool.Acquire(
            visualPrefab,
            position,
            rotation);

        CargoPackage packageVisual = instance.GetComponent<CargoPackage>();
        if (packageVisual == null)
        {
            CargoPackageVisualPool.Discard(instance);
            return false;
        }

        packageVisual.BindData(this);
        instance.SetActive(true);
        return true;
    }

    public void Initialize(
        string id,
        CargoDirection cargoDirection,
        CargoPackageState packageState,
        string assignedFlightOperationId,
        CargoZone startingZone)
    {
        packageId = id;
        direction = cargoDirection;
        state = packageState;
        flightOperationId = assignedFlightOperationId ?? string.Empty;
        currentZone = startingZone;
    }

    public bool IsForFlight(string operationId)
    {
        return direction == CargoDirection.Outgoing &&
               !string.IsNullOrWhiteSpace(operationId) &&
               flightOperationId == operationId;
    }

    public bool TryReserve()
    {
        if (state != CargoPackageState.Staged || currentZone == null)
            return false;

        state = CargoPackageState.Reserved;
        return true;
    }

    public bool SetInTransit()
    {
        if (state != CargoPackageState.Reserved)
            return false;

        state = CargoPackageState.InTransit;
        currentZone = null;
        return true;
    }

    public void SetDelivered()
    {
        state = CargoPackageState.Delivered;
        currentZone = null;
    }

    public bool ReturnToZone(CargoZone zone)
    {
        if (zone == null)
            return false;

        currentZone = zone;
        state = CargoPackageState.Staged;
        return true;
    }
}

/// <summary>
/// Reuses cargo box visuals while keeping the number of inactive GameObjects
/// bounded. Pool entries are keyed by their source prefab.
/// </summary>
internal static class CargoPackageVisualPool
{
    private const int MaxInactivePerPrefab = 256;

    private static readonly Dictionary<GameObject, Stack<GameObject>> pools =
        new Dictionary<GameObject, Stack<GameObject>>();

    private static Transform poolRoot;

    public static GameObject Acquire(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation)
    {
        if (prefab != null &&
            pools.TryGetValue(prefab, out Stack<GameObject> pool))
        {
            while (pool.Count > 0)
            {
                GameObject pooled = pool.Pop();
                if (pooled == null)
                    continue;

                pooled.transform.SetParent(null, false);
                pooled.transform.SetPositionAndRotation(position, rotation);
                return pooled;
            }
        }

        return UnityEngine.Object.Instantiate(prefab, position, rotation);
    }

    public static void Release(GameObject prefab, GameObject instance)
    {
        if (instance == null)
            return;

        if (prefab == null)
        {
            UnityEngine.Object.Destroy(instance);
            return;
        }

        if (!pools.TryGetValue(prefab, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>();
            pools.Add(prefab, pool);
        }

        if (pool.Count >= MaxInactivePerPrefab)
        {
            UnityEngine.Object.Destroy(instance);
            return;
        }

        EnsurePoolRoot();
        instance.SetActive(false);
        instance.transform.SetParent(poolRoot, false);
        pool.Push(instance);
    }

    public static void Discard(GameObject instance)
    {
        if (instance != null)
            UnityEngine.Object.Destroy(instance);
    }

    private static void EnsurePoolRoot()
    {
        if (poolRoot != null)
            return;

        GameObject root = new GameObject("Cargo Package Visual Pool");
        UnityEngine.Object.DontDestroyOnLoad(root);
        poolRoot = root.transform;
    }
}
