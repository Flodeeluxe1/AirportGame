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

    [Header("Operation")]
    [SerializeField] private AirlineOperationType operationType = AirlineOperationType.Cargo;

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
    [Tooltip("Minimum developed foundation cells required. This value is also the passenger-capacity target for this contract's interest score.")]
    [Min(0)]
    [SerializeField] private int minimumFoundationCells;
    [Tooltip("Minimum cargo storage required. This value is also the storage-capacity target for this contract's interest score.")]
    [Min(0f)]
    [SerializeField] private float minimumCargoStorageCapacity;
    [Tooltip("Minimum cargo handling throughput required per hour. This value is also the handling-capacity target for this contract's interest score.")]
    [Min(0f)]
    [SerializeField] private float minimumCargoHandlingThroughputPerHour;

    [Header("Payment")]
    [Min(0)]
    [SerializeField] private int paymentPerCompletedFlight = 500;
    [Tooltip("Additional cash penalty charged for each missed, cancelled, or operationally failed flight. The flight payment is also not earned.")]
    [Min(0)]
    [SerializeField] private int missedFlightPenalty = 250;
    [Min(0)]
    [SerializeField] private int completionBonus;

    [Header("Offer Eligibility")]
    [Range(0f, 100f)]
    [SerializeField] private float minimumAirlineInterest = 20f;

    public string TemplateId => templateId;
    public string DisplayName => displayName;
    public AirlineOperationType OperationType => operationType;
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
    public int MinimumFoundationCells => minimumFoundationCells;
    public float MinimumCargoStorageCapacity => minimumCargoStorageCapacity;
    public float MinimumCargoHandlingThroughputPerHour => minimumCargoHandlingThroughputPerHour;
    public int PaymentPerCompletedFlight => paymentPerCompletedFlight;
    public int MissedFlightPenalty => missedFlightPenalty;
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
        minimumFoundationCells = Mathf.Max(0, minimumFoundationCells);
        minimumCargoStorageCapacity = Mathf.Max(0f, minimumCargoStorageCapacity);
        minimumCargoHandlingThroughputPerHour =
            Mathf.Max(0f, minimumCargoHandlingThroughputPerHour);
        paymentPerCompletedFlight = Mathf.Max(0, paymentPerCompletedFlight);
        missedFlightPenalty = Mathf.Max(0, missedFlightPenalty);
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
