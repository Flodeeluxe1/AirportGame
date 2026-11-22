using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AirlineContractTerms
{
    public int durationDays;
    public List<AirlineContractFlightWindowRequirement> flightWindows =
        new List<AirlineContractFlightWindowRequirement>();
    public int turnaroundMinutes;
    public AircraftGateSize minimumGateSize;
    public float minimumRunwayLengthMeters;
    public float minimumCargoStorageCapacity;
    public float minimumCargoHandlingThroughputPerHour;
    public int paymentPerCompletedFlight;
    [Range(0f, 1f)] public float missedFlightPaymentReduction;
    public int completionBonus;

    public static AirlineContractTerms FromTemplate(
        AirlineContractTemplate template,
        float interest)
    {
        if (template == null)
            return null;

        float paymentMultiplier = Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp01(interest / 100f));
        AirlineContractTerms terms = new AirlineContractTerms
        {
            durationDays = template.DurationDays,
            turnaroundMinutes = template.TurnaroundMinutes,
            minimumGateSize = template.MinimumGateSize,
            minimumRunwayLengthMeters = template.MinimumRunwayLengthMeters,
            minimumCargoStorageCapacity = template.MinimumCargoStorageCapacity,
            minimumCargoHandlingThroughputPerHour =
                template.MinimumCargoHandlingThroughputPerHour,
            paymentPerCompletedFlight = Mathf.RoundToInt(
                template.PaymentPerCompletedFlight * paymentMultiplier),
            missedFlightPaymentReduction = template.MissedFlightPaymentReduction,
            completionBonus = Mathf.RoundToInt(template.CompletionBonus * paymentMultiplier)
        };

        foreach (AirlineContractFlightWindowRequirement requirement in template.FlightWindows)
        {
            if (requirement == null)
                continue;

            terms.flightWindows.Add(new AirlineContractFlightWindowRequirement
            {
                window = requirement.window,
                flightsPerDay = requirement.flightsPerDay
            });
        }

        return terms;
    }
}

[Serializable]
public class AirlineContractOffer
{
    public string offerId;
    public AirlineDefinition airline;
    public AirlineContractTemplate template;
    public float airlineInterest;
    public long offeredOnDay;
    public long expiresOnDay;
    public AirlineContractTerms terms;
}

public enum AirlineContractStatus
{
    Active,
    Completed,
    Terminated,
    Failed
}

[Serializable]
public class AirlineContractFlightObligation
{
    public string obligationId;
    public string flightNumber;
    public AirlineOperatingWindow window;
    public int slotIndex;
}

[Serializable]
public class ActiveAirlineContract
{
    public string contractId;
    public string sourceOfferId;
    public AirlineDefinition airline;
    public AirlineContractTemplate sourceTemplate;
    public long startDay;
    public long endDay;
    public AirlineContractStatus status;
    public AirlineContractTerms agreedTerms;
    public List<AirlineContractFlightObligation> flightObligations =
        new List<AirlineContractFlightObligation>();
}
