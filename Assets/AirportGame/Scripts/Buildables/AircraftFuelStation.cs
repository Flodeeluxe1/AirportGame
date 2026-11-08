using UnityEngine;

/// <summary>
/// Stores fuel for hobby aircraft and automatically refuels a parked
/// hobby aircraft when it is close to this station.
/// </summary>
[DisallowMultipleComponent]
public class AircraftFuelStation : MonoBehaviour
{
    [Header("Pipeline Network")]
    [SerializeField] private PipelineNetworkManager pipelineNetworkManager;
    [SerializeField] private FuelConnector fuelConnector;

    [Header("Legacy Fuel Storage (unused when connected)") ]
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
    private AircraftHobbyFuelDepartureController reservedAircraft;
    private readonly System.Collections.Generic.List<AircraftHobbyFuelDepartureController> reservationQueue = new();
    private float fuelTransferredThisSession;
    private float fuelSessionPricePerLitre;
    private AirportEconomyManager economyManager;

    public float SellingPricePerLitre => sellingPricePerLitre;

    /// <summary>
    /// Adds an aircraft to the FIFO queue. The first aircraft reserves the
    /// station immediately when it is free; later aircraft wait at parking.
    /// </summary>
    public bool RequestAircraftReservation(AircraftHobbyFuelDepartureController aircraft)
    {
        if (aircraft == null)
            return false;

        RemoveInvalidQueueEntries();

        if (reservedAircraft == aircraft)
            return true;

        if (!reservationQueue.Contains(aircraft))
            reservationQueue.Add(aircraft);

        return TryGrantNextReservation();
    }

    public bool IsReservedForAircraft(AircraftHobbyFuelDepartureController aircraft)
    {
        return aircraft != null && reservedAircraft == aircraft;
    }

    public bool IsQueuedForAircraft(AircraftHobbyFuelDepartureController aircraft)
    {
        return aircraft != null && reservationQueue.Contains(aircraft);
    }

    public bool TryAcquireQueuedReservation(AircraftHobbyFuelDepartureController aircraft)
    {
        if (aircraft == null)
            return false;

        RemoveInvalidQueueEntries();

        if (reservedAircraft == aircraft)
            return true;

        TryGrantNextReservation();
        return reservedAircraft == aircraft;
    }

    public void CancelAircraftReservationRequest(AircraftHobbyFuelDepartureController aircraft)
    {
        if (aircraft == null)
            return;

        reservationQueue.Remove(aircraft);

        if (reservedAircraft == aircraft)
            reservedAircraft = null;

        TryGrantNextReservation();
    }

    public void ReleaseAircraftReservation(AircraftHobbyFuelDepartureController aircraft)
    {
        if (aircraft == null || reservedAircraft != aircraft)
            return;

        reservedAircraft = null;
        TryGrantNextReservation();
    }

    private bool TryGrantNextReservation()
    {
        RemoveInvalidQueueEntries();

        if (reservedAircraft != null)
            return false;

        if (reservationQueue.Count == 0)
            return false;

        reservedAircraft = reservationQueue[0];
        reservationQueue.RemoveAt(0);
        return true;
    }

    private void RemoveInvalidQueueEntries()
    {
        reservationQueue.RemoveAll(aircraft => aircraft == null);
        if (reservedAircraft == null)
            reservedAircraft = null;
    }
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

    private PipelineNetworkManager.PipelineNetwork FuelNetwork
    {
        get
        {
            ResolvePipelineReferences();
            return pipelineNetworkManager != null && fuelConnector != null
                ? pipelineNetworkManager.GetNetworkFor(fuelConnector)
                : null;
        }
    }

    public float MaximumFuelLitres => FuelNetwork?.CapacityLitres ?? 0f;
    public float CurrentFuelLitres => FuelNetwork?.CurrentFuelLitres ?? 0f;
    public float RemainingFuelCapacity => FuelNetwork?.RemainingCapacityLitres ?? 0f;

    public bool NeedsFuelDelivery => deliveryRequested && !deliveryInProgress;
    public bool IsFuelDeliveryInProgress => deliveryInProgress;
    public string DebugStatus => debugStatus;

    private void ResolvePipelineReferences()
    {
        if (pipelineNetworkManager == null)
            pipelineNetworkManager = FindFirstObjectByType<PipelineNetworkManager>();

        if (fuelConnector == null)
            fuelConnector = GetComponentInChildren<FuelConnector>(true);
    }

    private void Awake()
    {
        maximumFuelLitres = Mathf.Max(1f, maximumFuelLitres);
        sellingPricePerLitre = Mathf.Max(0f, sellingPricePerLitre);
        economyManager = FindFirstObjectByType<AirportEconomyManager>();
        ResolvePipelineReferences();
    }

    private void Update()
    {
        UpdateDeliveryRequest();

        if (CurrentFuelLitres <= 0f)
        {
            CompleteFuelSaleSession();
            currentlyServicedAircraft = null;
            debugStatus = "Out of fuel; waiting for fuel delivery";
            return;
        }

        AircraftFuelTank aircraft = FindParkedAircraftAtStation();

        if (aircraft == null)
        {
            CompleteFuelSaleSession();
            currentlyServicedAircraft = null;

            if (!deliveryInProgress && !deliveryRequested)
                debugStatus = "Waiting for a parked hobby aircraft";

            return;
        }

        if (currentlyServicedAircraft != aircraft)
        {
            CompleteFuelSaleSession();
            currentlyServicedAircraft = aircraft;
            fuelTransferredThisSession = 0f;
            fuelSessionPricePerLitre = sellingPricePerLitre;
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
                CurrentFuelLitres,
                aircraft.MaximumFuelLitres - aircraft.CurrentFuelLitres));
        float transferred = aircraft.AddFuel(proposedAmount);
        fuelTransferredThisSession += transferred;
        FuelNetwork?.ConsumeFuel(transferred);

        debugStatus =
            $"Refuelling {aircraft.name}: " +
            $"{aircraft.CurrentFuelLitres:F1}/" +
            $"{aircraft.MaximumFuelLitres:F1} L";

        UpdateDeliveryRequest();
    }

    private void CompleteFuelSaleSession()
    {
        if (fuelTransferredThisSession <= 0f)
            return;

        int totalCost = Mathf.CeilToInt(
            fuelTransferredThisSession * fuelSessionPricePerLitre);

        if (totalCost > 0)
        {
            if (economyManager == null)
                economyManager = FindFirstObjectByType<AirportEconomyManager>();

            if (economyManager == null ||
                !economyManager.AddRevenue(
                    totalCost,
                    $"Hobby aircraft fuel sales ({currentlyServicedAircraft?.name ?? "aircraft"})"))
            {
                debugStatus = "Could not record completed aircraft fuel sale";
            }
        }

        fuelTransferredThisSession = 0f;
        fuelSessionPricePerLitre = 0f;
    }

    private AircraftFuelTank FindParkedAircraftAtStation()
    {
        // Only the aircraft holding this station's reservation can refuel.
        // Checking it directly avoids scanning every AircraftStatus in the scene.
        AircraftHobbyFuelDepartureController departure = reservedAircraft;

        if (departure == null ||
            !departure.IsAtFuelStation ||
            departure.SelectedStation != this)
        {
            return null;
        }

        AircraftStatus status = departure.GetComponent<AircraftStatus>();

        if (status == null || !status.IsParked)
            return null;

        AircraftFlightIdentity identity =
            departure.GetComponent<AircraftFlightIdentity>();

        if (identity == null ||
            !identity.IsInitialized ||
            !identity.IsHobbyFlight)
        {
            return null;
        }

        AircraftFuelTank fuelTank =
            departure.GetComponent<AircraftFuelTank>();

        if (fuelTank == null || fuelTank.IsFull)
            return null;

        Vector3 offset = departure.transform.position - transform.position;
        offset.y = 0f;

        float radiusSquared = refuellingRadius * refuellingRadius;

        if (offset.sqrMagnitude > radiusSquared)
            return null;

        return fuelTank;
    }

    private void UpdateDeliveryRequest()
    {
        float requestLevel =
            MaximumFuelLitres * deliveryRequestThreshold;

        deliveryRequested = CurrentFuelLitres <= requestLevel;
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

        PipelineNetworkManager.PipelineNetwork network = FuelNetwork;
        return network != null ? network.AddFuel(amountLitres) : 0f;
    }

    /// <summary>
    /// Called by the fuel truck after completing its delivery.
    /// </summary>
    public void CompleteFuelDelivery()
    {
        deliveryInProgress = false;
        deliveryRequested =
            CurrentFuelLitres <= MaximumFuelLitres * deliveryRequestThreshold;

        debugStatus = deliveryRequested
            ? "Fuel delivery completed; more fuel is required"
            : "Fuel delivery completed";
    }

    /// <summary>
    /// Restores the station's saved fuel stock after its building is loaded.
    /// </summary>
    public void RestoreFuelStock(float savedFuelLitres)
    {
        // Shared pipeline fuel is restored by PipelineNetworkManager.
        // This legacy method remains for compatibility with older save data.

        deliveryInProgress = false;
        deliveryRequested =
            CurrentFuelLitres <= MaximumFuelLitres * deliveryRequestThreshold;

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
