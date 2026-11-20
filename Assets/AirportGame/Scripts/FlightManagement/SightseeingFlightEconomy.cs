
using UnityEngine;

/// <summary>
/// Airport-wide ticket pricing and passenger-demand settings
/// for sightseeing flights.
/// </summary>
[DisallowMultipleComponent]
public class SightseeingFlightEconomy : MonoBehaviour
{
    public static SightseeingFlightEconomy Instance { get; private set; }

    [Header("Ticket Pricing")]
    [Min(1)]
    [SerializeField] private int ticketPrice = 100;

    [Header("Passenger Demand")]
    [Tooltip("Expected passengers at the reference ticket price.")]
    [Min(0)]
    [SerializeField] private int referenceDemand = 4;

    [Tooltip("Random variation around expected demand.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float demandVariation = 0.25f;

    [Header("Airport Revenue")]
    [Tooltip("Fraction of ticket revenue earned by the airport.")]
    [Range(0f, 1f)]
    [SerializeField] private float airportCommissionRate = 0.20f;

    [Header("Ticket Sales Runtime Totals")]
    [SerializeField] private int ticketsSold;
    [SerializeField] private long totalTicketSalesValue;

    public int TicketsSold => ticketsSold;
    public long TotalTicketSalesValue => totalTicketSalesValue;

    public int TicketPrice => Mathf.Max(1, ticketPrice);
    public float AirportCommissionRate => airportCommissionRate;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError(
                "Only one SightseeingFlightEconomy should exist in the scene.",
                this);

            enabled = false;
            return;
        }

        Instance = this;
    }

    /// <summary>
    /// Records a completed sightseeing ticket purchase. Ticket sales are
    /// tracked separately from the airport's commission to avoid counting
    /// the airline's full fare as airport income.
    /// </summary>
    public void RegisterTicketSale()
    {
        ticketsSold++;
        totalTicketSalesValue += TicketPrice;
    }

    public void RegisterTicketRefund(int refundedTicketPrice)
    {
        if (ticketsSold > 0)
            ticketsSold--;

        totalTicketSalesValue = System.Math.Max(
            0L,
            totalTicketSalesValue - Mathf.Max(0, refundedTicketPrice));
    }

    /// <summary>
    /// Generates passenger demand when boarding completes.
    /// The final count cannot exceed the aircraft's capacity.
    /// </summary>

    public int CalculatePassengerDemand(int aircraftCapacity)
    {
        aircraftCapacity = Mathf.Max(0, aircraftCapacity);

        if (aircraftCapacity == 0)
            return 0;

        // Sightseeing tickets cannot be sold without a placed ticket desk.
        if (!SightseeingAircraftSpawner.HasTicketDeskPlaced())
        {
            Debug.Log(
                "SIGHTSEEING NO TICKET DESK | Passenger demand set to zero.",
                this);

            return 0;
        }

        float expectedDemand =
            referenceDemand * (100f / TicketPrice);

        float variation = Random.Range(
            1f - demandVariation,
            1f + demandVariation);

        int demand = Mathf.Max(
            0,
            Mathf.RoundToInt(expectedDemand * variation));

        return Mathf.Min(demand, aircraftCapacity);
    }


    /// <summary>
    /// Calculates the airport's share of ticket revenue.
    /// Rounds the total commission down to a whole dollar.
    /// </summary>
    public int CalculateAirportCommission(int passengers)
    {
        if (passengers <= 0)
            return 0;

        long ticketRevenue = (long)TicketPrice * passengers;

        double commission =
            ticketRevenue * airportCommissionRate;

        return (int)Mathf.Clamp(
            (float)System.Math.Floor(commission),
            0f,
            int.MaxValue);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
