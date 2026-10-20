
using System.Collections.Generic;
using UnityEngine;

public class AirportPassengerRevenueManager : MonoBehaviour
{
    public static AirportPassengerRevenueManager Instance { get; private set; }

    [Header("Passenger Pricing")]
    [SerializeField, Min(0f)]
    private float baseTicketPrice = 10f;

    [Tooltip("Airport fee as a percentage of the base ticket price.")]
    [SerializeField, Range(0f, 100f)]
    private float airportFeePercent = 0f;

    [Header("Recorded Totals")]
    [SerializeField] private int departedPassengerFlights;
    [SerializeField] private int totalPassengers;
    [SerializeField] private float totalBaseFareValue;
    [SerializeField] private float totalAirportFeeRevenue;
    [SerializeField] private float totalTicketSales;

    private readonly HashSet<string> recordedOperations =
        new HashSet<string>();

    public float BaseTicketPrice => Mathf.Max(0f, baseTicketPrice);

    public float AirportFeePercent =>
        Mathf.Clamp(airportFeePercent, 0f, 100f);

    public int DepartedPassengerFlights => departedPassengerFlights;
    public int TotalPassengers => totalPassengers;
    public float TotalBaseFareValue => totalBaseFareValue;
    public float TotalAirportFeeRevenue => totalAirportFeeRevenue;
    public float TotalTicketSales => totalTicketSales;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError(
                "More than one AirportPassengerRevenueManager exists.",
                this
            );

            enabled = false;
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }


    public bool RecordPassengerDeparture(AircraftFlightIdentity identity, int passengerCount)
    {
        if (identity == null || !identity.IsInitialized || identity.Definition == null)
        {
            Debug.LogError("Cannot record passenger revenue: flight identity is missing.", this);
            return false;
        }

        if (identity.Definition.OperationType != FlightOperationType.PassengerOnly &&
            identity.Definition.OperationType != FlightOperationType.CargoAndPassengers)
        {
            Debug.LogWarning($"{identity.FlightNumber} is not a passenger flight.", this);
            return false;
        }

        if (passengerCount < 0)
        {
            Debug.LogError($"Cannot record negative passenger count for {identity.FlightNumber}.", this);
            return false;
        }

        string operationId = identity.OperationId;

        if (recordedOperations.Contains(operationId))
        {
            Debug.LogWarning($"Revenue for {operationId} has already been recorded.", this);
            return true;
        }

        float baseFare = BaseTicketPrice * passengerCount;
        float airportFee = baseFare * (AirportFeePercent / 100f);
        float ticketSales = baseFare + airportFee;

        // Find the existing airport economy manager.
        AirportEconomyManager economy = GetComponent<AirportEconomyManager>();

        if (economy == null)
        {
            Debug.LogError(
                "AirportPassengerRevenueManager needs an AirportEconomyManager " +
                "on the same GameObject to add passenger revenue to the balance.",
                this);
            return false;
        }

        // Add the combined ticket sales and airport fee to the airport account.
        int revenueAmount = Mathf.RoundToInt(ticketSales);

        if (revenueAmount > 0 &&
            !economy.AddRevenue(
                revenueAmount,
                $"Passenger flight {identity.FlightNumber} ticket sales and airport fee"))
        {
            Debug.LogError(
                $"Could not add ${revenueAmount} to the airport balance. " +
                "Passenger revenue statistics were not updated.",
                this);
            return false;
        }

        // Update passenger revenue statistics only after the account update succeeds.
        recordedOperations.Add(operationId);
        departedPassengerFlights++;
        totalPassengers += passengerCount;
        totalBaseFareValue += baseFare;
        totalAirportFeeRevenue += airportFee;
        totalTicketSales += ticketSales;

        Debug.Log(
            $"PASSENGER FLIGHT REVENUE | Flight: {identity.FlightNumber} | " +
            $"Passengers: {passengerCount} | Base fare: ${baseFare:F2} | " +
            $"Airport fee: ${airportFee:F2} | " +
            $"Added to airport account: ${revenueAmount:F2} | " +
            $"New airport balance: ${economy.Balance:N0} | " +
            $"Total airport fee revenue: ${totalAirportFeeRevenue:F2}",
            this);

        return true;
    }

}
