using System;
using UnityEngine;

/// <summary>
/// A point-in-time view of airport conditions used to evaluate one airline.
/// Values are collected from live airport systems by the AMS integration layer.
/// </summary>
[Serializable]
public class AirlineAirportSnapshot
{
    [Header("Airport Fees")]
    [Min(0)]
    public int representativeLandingAndParkingFee;

    [Header("Gates")]
    [Min(0)]
    public int suitableGateCount;

    [Header("Runways")]
    [Min(0f)]
    public float longestSuitableRunwayLengthMeters;

    [Header("Cargo Capacity")]
    [Min(0f)]
    public float cargoStorageCapacity;

    [Min(0f)]
    public float cargoHandlingThroughputPerHour;

    [Header("Airport Reputation")]
    [Range(0f, 100f)]
    public float airportReputation = 50f;

    [Header("Previous Contract Performance")]
    [Range(0f, 100f)]
    public float previousContractPerformance = 50f;

    /// <summary>
    /// Curfew and night-operation restrictions are not implemented yet.
    /// Keep this at 100 until the airport operating-hours system exists.
    /// </summary>
    [Range(0f, 100f)]
    public float nightOperationsScore = 100f;
}

[Serializable]
public struct AirlineInterestResult
{
    [Range(0f, 100f)] public float airportFees;
    [Range(0f, 100f)] public float suitableGates;
    [Range(0f, 100f)] public float runwaySuitability;
    [Range(0f, 100f)] public float cargoHandlingCapacity;
    [Range(0f, 100f)] public float nightOperations;
    [Range(0f, 100f)] public float airportReputation;
    [Range(0f, 100f)] public float previousContractPerformance;
    [Range(0f, 100f)] public float totalInterest;

    public AirlineInterestResult(
        float fees,
        float gates,
        float runway,
        float cargo,
        float night,
        float reputation,
        float performance,
        float total)
    {
        airportFees = fees;
        suitableGates = gates;
        runwaySuitability = runway;
        cargoHandlingCapacity = cargo;
        nightOperations = night;
        airportReputation = reputation;
        previousContractPerformance = performance;
        totalInterest = total;
    }
}
