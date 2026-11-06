using UnityEngine;

/// <summary>
/// Stores fuel for hobby aircraft and automatically refuels a parked
/// hobby aircraft when it is close to this station.
/// </summary>
[DisallowMultipleComponent]
public class AircraftFuelStation : MonoBehaviour
{
    [Header("Fuel Storage")]
    [Min(1f)]
    [SerializeField] private float maximumFuelLitres = 5000f;

    [Min(0f)]
    [SerializeField] private float currentFuelLitres = 5000f;

    [Header("Automatic Refuelling")]
    [Tooltip("Horizontal distance from this station within which a parked hobby aircraft can refuel.")]
    [Min(0.1f)]
    [SerializeField] private float refuellingRadius = 12f;

    [Tooltip("Fuel transferred per in-game minute.")]
    [Min(0.1f)]
    [SerializeField] private float refuellingRateLitresPerMinute = 50f;

    [Header("Automatic Fuel Delivery")]
    [Range(0f, 1f)]
    [SerializeField] private float deliveryRequestThreshold = 0.25f;

    [Header("Runtime Status")]
    [SerializeField] private bool deliveryRequested;
    [SerializeField] private bool deliveryInProgress;
    [SerializeField] private string debugStatus = "Fuel station ready";

    private AircraftFuelTank currentlyServicedAircraft;

    public float MaximumFuelLitres => maximumFuelLitres;
    public float CurrentFuelLitres => currentFuelLitres;
    public float RemainingFuelCapacity =>
        Mathf.Max(0f, maximumFuelLitres - currentFuelLitres);

    public bool NeedsFuelDelivery => deliveryRequested && !deliveryInProgress;
    public bool IsFuelDeliveryInProgress => deliveryInProgress;
    public string DebugStatus => debugStatus;

    private void Awake()
    {
        maximumFuelLitres = Mathf.Max(1f, maximumFuelLitres);
        currentFuelLitres = Mathf.Clamp(
            currentFuelLitres,
            0f,
            maximumFuelLitres);
    }

    private void Update()
    {
        UpdateDeliveryRequest();

        if (currentFuelLitres <= 0f)
        {
            currentlyServicedAircraft = null;
            debugStatus = "Out of fuel; waiting for fuel delivery";
            return;
        }

        AircraftFuelTank aircraft = FindParkedAircraftAtStation();

        if (aircraft == null)
        {
            currentlyServicedAircraft = null;

            if (!deliveryInProgress && !deliveryRequested)
                debugStatus = "Waiting for a parked hobby aircraft";

            return;
        }

        currentlyServicedAircraft = aircraft;

        if (aircraft.CurrentFuelLitres >= aircraft.MaximumFuelLitres)
        {
            debugStatus = $"{aircraft.name}: fuel tank is full";
            return;
        }

        float amountToTransfer =
            refuellingRateLitresPerMinute * Time.deltaTime / 60f;

        if (amountToTransfer <= 0f)
            return;

        float transferred = aircraft.AddFuel(
            Mathf.Min(amountToTransfer, currentFuelLitres));

        currentFuelLitres = Mathf.Max(
            0f,
            currentFuelLitres - transferred);

        debugStatus =
            $"Refuelling {aircraft.name}: " +
            $"{aircraft.CurrentFuelLitres:F1}/" +
            $"{aircraft.MaximumFuelLitres:F1} L";

        UpdateDeliveryRequest();
    }

    private AircraftFuelTank FindParkedAircraftAtStation()
    {
        AircraftStatus[] aircraftStatuses =
            FindObjectsByType<AircraftStatus>(FindObjectsSortMode.None);

        AircraftFuelTank nearestAircraft = null;
        float nearestDistanceSquared = refuellingRadius * refuellingRadius;

        Vector3 stationPosition = transform.position;

        foreach (AircraftStatus status in aircraftStatuses)
        {
            if (status == null || !status.IsParked)
                continue;

            AircraftFlightIdentity identity =
                status.GetComponent<AircraftFlightIdentity>();

            if (identity == null ||
                !identity.IsInitialized ||
                !identity.IsHobbyFlight)
            {
                continue;
            }

            AircraftFuelTank fuelTank =
                status.GetComponent<AircraftFuelTank>();

            if (fuelTank == null || fuelTank.IsFull)
                continue;

            Vector3 offset =
                status.transform.position - stationPosition;

            offset.y = 0f;

            float distanceSquared = offset.sqrMagnitude;

            if (distanceSquared > nearestDistanceSquared)
                continue;

            nearestDistanceSquared = distanceSquared;
            nearestAircraft = fuelTank;
        }

        return nearestAircraft;
    }

    private void UpdateDeliveryRequest()
    {
        float requestLevel =
            maximumFuelLitres * deliveryRequestThreshold;

        if (currentFuelLitres <= requestLevel)
            deliveryRequested = true;
    }

    /// <summary>
    /// Called by the fuel truck when it begins servicing this station.
    /// </summary>
    public bool TryBeginFuelDelivery()
    {
        UpdateDeliveryRequest();

        if (!NeedsFuelDelivery || RemainingFuelCapacity <= 0f)
            return false;

        deliveryInProgress = true;
        debugStatus = "Fuel delivery truck servicing station";
        return true;
    }

    /// <summary>
    /// Adds delivered fuel and returns the amount accepted by the station.
    /// </summary>
    public float ReceiveFuelDelivery(float amountLitres)
    {
        if (!deliveryInProgress || amountLitres <= 0f)
            return 0f;

        float accepted = Mathf.Min(
            amountLitres,
            RemainingFuelCapacity);

        currentFuelLitres += accepted;

        return accepted;
    }

    /// <summary>
    /// Called by the fuel truck after completing its delivery.
    /// </summary>
    public void CompleteFuelDelivery()
    {
        deliveryInProgress = false;
        deliveryRequested =
            currentFuelLitres <= maximumFuelLitres * deliveryRequestThreshold;

        debugStatus = deliveryRequested
            ? "Fuel delivery completed; more fuel is required"
            : "Fuel delivery completed";
    }

    /// <summary>
    /// Restores the station's saved fuel stock after its building is loaded.
    /// </summary>
    public void RestoreFuelStock(float savedFuelLitres)
    {
        currentFuelLitres = Mathf.Clamp(
            savedFuelLitres,
            0f,
            maximumFuelLitres);

        deliveryInProgress = false;
        deliveryRequested =
            currentFuelLitres <= maximumFuelLitres * deliveryRequestThreshold;

        UpdateDeliveryRequest();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, refuellingRadius);
    }
#endif
}
