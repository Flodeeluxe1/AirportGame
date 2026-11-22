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
    public int missedFlightPenalty;
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
            missedFlightPenalty = template.MissedFlightPenalty,
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
    public long lastFinalizedDay;
    public List<AirlineContractFlightPerformanceRecord> performanceRecords =
        new List<AirlineContractFlightPerformanceRecord>();
    public int onTimeFlights;
    public int lateFlights;
    public int missedFlights;
    public int cancelledFlights;
    public int operationalFailures;
    public int totalPaymentsReceived;
    public int totalPenaltiesPaid;
    public int unpaidPenaltyAmount;
    public bool completionBonusPaid;
    public int renewalCount;
    public bool renewalDeclined;
    public int renewalBasePaymentPerFlight;
    public int renewalBaseCompletionBonus;
}

public enum AirlineContractFlightResult
{
    OnTime,
    Late,
    Missed,
    Cancelled,
    OperationalFailure
}

[Serializable]
public class AirlineContractFlightPerformanceRecord
{
    public string obligationId;
    public string flightNumber;
    public long scheduledDay;
    public AirlineContractFlightResult result;
    public long completedAbsoluteGameSecond;
    public int paymentReceived;
    public int penaltyCharged;
    public int penaltyPaid;
}

[Serializable]
public class AirlineRelationshipRecord
{
    public string airlineId;
    public string airlineName;
    [Range(0f, 100f)] public float relationship = 50f;
}
