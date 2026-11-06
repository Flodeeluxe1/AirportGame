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

    [Tooltip("Selling price charged to hobby aircraft per litre.")]
    [Min(0f)]
    [SerializeField] private float sellingPricePerLitre = 1.5f;

    [SerializeField] private Transform aircraftFuelStopPoint;

    [Header("Automatic Fuel Delivery")]
    [Range(0f, 1f)]
    [SerializeField] private float deliveryRequestThreshold = 0.25f;

    [Header("Runtime Status")]
    [SerializeField] private bool deliveryRequested;
    [SerializeField] private bool deliveryInProgress;
    [SerializeField] private string debugStatus = "Fuel station ready";

    private AircraftFuelTank currentlyServicedAircraft;
    private float fuelTransferredThisSession;
    private int fuelRevenueEarnedThisSession;
    private AirportEconomyManager economyManager;

    public float SellingPricePerLitre => sellingPricePerLitre;
    public Transform AircraftFuelStopPoint
    {
        get
        {
            if (aircraftFuelStopPoint == null)
            {
                Transform[] children = GetComponentsInChildren<Transform>(true);
                foreach (Transform child in children)
                {
                    if (child != transform && child.name == "AircraftFuelStopPoint")
                    {
                        aircraftFuelStopPoint = child;
                        break;
                    }
                }
            }
            return aircraftFuelStopPoint;
        }
    }

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
        sellingPricePerLitre = Mathf.Max(0f, sellingPricePerLitre);
        economyManager = FindFirstObjectByType<AirportEconomyManager>();
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
            fuelTransferredThisSession = 0f;
            fuelRevenueEarnedThisSession = 0;

            if (!deliveryInProgress && !deliveryRequested)
                debugStatus = "Waiting for a parked hobby aircraft";

            return;
        }

        if (currentlyServicedAircraft != aircraft)
        {
            currentlyServicedAircraft = aircraft;
            fuelTransferredThisSession = 0f;
            fuelRevenueEarnedThisSession = 0;
        }

        if (aircraft.CurrentFuelLitres >= aircraft.MaximumFuelLitres)
        {
            debugStatus = $"{aircraft.name}: fuel tank is full";
            return;
        }

        float amountToTransfer =
            refuellingRateLitresPerMinute * Time.deltaTime / 60f;

        if (amountToTransfer <= 0f)
            return;

        float proposedAmount = Mathf.Min(
            amountToTransfer,
            Mathf.Min(
                currentFuelLitres,
                aircraft.MaximumFuelLitres - aircraft.CurrentFuelLitres));
        float proposedTotal = fuelTransferredThisSession + proposedAmount;
        int totalCost = Mathf.CeilToInt(proposedTotal * sellingPricePerLitre);
        int additionalCost = Mathf.Max(0, totalCost - fuelRevenueEarnedThisSession);

        if (additionalCost > 0)
        {
            if (economyManager == null)
                economyManager = FindFirstObjectByType<AirportEconomyManager>();

            if (economyManager == null ||
                !economyManager.AddRevenue(
                    additionalCost,
                    $"Hobby aircraft fuel sales ({aircraft.name})"))
            {
                debugStatus = $"{aircraft.name}: could not record fuel sale";
                return;
            }

            fuelRevenueEarnedThisSession += additionalCost;
        }

        float transferred = aircraft.AddFuel(proposedAmount);
        fuelTransferredThisSession += transferred;
        currentFuelLitres = Mathf.Max(0f, currentFuelLitres - transferred);

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

            AircraftHobbyFuelDepartureController departure =
                status.GetComponent<AircraftHobbyFuelDepartureController>();

            if (departure == null ||
                !departure.IsAtFuelStation ||
                departure.SelectedStation != this)
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

        deliveryRequested = currentFuelLitres <= requestLevel;
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
