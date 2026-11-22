using System;
using System.Collections.Generic;
using UnityEngine;

public enum AirlineOperatingWindow
{
    Morning,
    Afternoon,
    Evening,
    Night
}

[Serializable]
public class AirlineContractFlightWindowRequirement
{
    public AirlineOperatingWindow window = AirlineOperatingWindow.Morning;

    [Min(0)]
    public int flightsPerDay = 1;
}

[CreateAssetMenu(
    fileName = "AirlineContractTemplate",
    menuName = "Airport/Airline Management/Contract Template"
)]
public class AirlineContractTemplate : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string templateId;
    [SerializeField] private string displayName = "Standard Operations Contract";

    [Header("Contract Duration")]
    [Min(1)]
    [SerializeField] private int durationDays = 7;

    [Header("Daily Flight Requirements")]
    [SerializeField] private List<AirlineContractFlightWindowRequirement> flightWindows =
        new List<AirlineContractFlightWindowRequirement>
        {
            new AirlineContractFlightWindowRequirement
            {
                window = AirlineOperatingWindow.Morning,
                flightsPerDay = 1
            },
            new AirlineContractFlightWindowRequirement
            {
                window = AirlineOperatingWindow.Afternoon,
                flightsPerDay = 1
            }
        };

    [Min(0)]
    [SerializeField] private int turnaroundMinutes = 60;

    [Header("Airport Requirements")]
    [SerializeField] private AircraftGateSize minimumGateSize = AircraftGateSize.Grass;
    [Min(0f)]
    [SerializeField] private float minimumRunwayLengthMeters = 100f;
    [Min(0f)]
    [SerializeField] private float minimumCargoStorageCapacity;
    [Min(0f)]
    [SerializeField] private float minimumCargoHandlingThroughputPerHour;

    [Header("Payment")]
    [Min(0)]
    [SerializeField] private int paymentPerCompletedFlight = 500;
    [Range(0f, 1f)]
    [SerializeField] private float missedFlightPaymentReduction = 0.5f;
    [Min(0)]
    [SerializeField] private int completionBonus;

    [Header("Offer Eligibility")]
    [Range(0f, 100f)]
    [SerializeField] private float minimumAirlineInterest = 20f;

    public string TemplateId => templateId;
    public string DisplayName => displayName;
    public int DurationDays => durationDays;
    public IReadOnlyList<AirlineContractFlightWindowRequirement> FlightWindows => flightWindows;
    public int TotalFlightsPerDay
    {
        get
        {
            int total = 0;
            if (flightWindows == null)
                return total;

            foreach (AirlineContractFlightWindowRequirement requirement in flightWindows)
            {
                if (requirement != null)
                    total += Mathf.Max(0, requirement.flightsPerDay);
            }

            return total;
        }
    }
    public int TurnaroundMinutes => turnaroundMinutes;
    public AircraftGateSize MinimumGateSize => minimumGateSize;
    public float MinimumRunwayLengthMeters => minimumRunwayLengthMeters;
    public float MinimumCargoStorageCapacity => minimumCargoStorageCapacity;
    public float MinimumCargoHandlingThroughputPerHour => minimumCargoHandlingThroughputPerHour;
    public int PaymentPerCompletedFlight => paymentPerCompletedFlight;
    public float MissedFlightPaymentReduction => missedFlightPaymentReduction;
    public int CompletionBonus => completionBonus;
    public float MinimumAirlineInterest => minimumAirlineInterest;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(templateId))
            templateId = name;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = name;

        durationDays = Mathf.Max(1, durationDays);
        turnaroundMinutes = Mathf.Max(0, turnaroundMinutes);
        minimumRunwayLengthMeters = Mathf.Max(0f, minimumRunwayLengthMeters);
        minimumCargoStorageCapacity = Mathf.Max(0f, minimumCargoStorageCapacity);
        minimumCargoHandlingThroughputPerHour =
            Mathf.Max(0f, minimumCargoHandlingThroughputPerHour);
        paymentPerCompletedFlight = Mathf.Max(0, paymentPerCompletedFlight);
        completionBonus = Mathf.Max(0, completionBonus);

        if (flightWindows == null)
            flightWindows = new List<AirlineContractFlightWindowRequirement>();

        foreach (AirlineContractFlightWindowRequirement requirement in flightWindows)
        {
            if (requirement != null)
                requirement.flightsPerDay = Mathf.Max(0, requirement.flightsPerDay);
        }
    }
}
