using UnityEngine;

/// <summary>
/// Calculates an airline-specific attractiveness score from an airport snapshot.
/// This class has no scene dependencies; airport data collection remains separate.
/// </summary>
public static class AirlineInterestCalculator
{
    public static AirlineInterestResult Calculate(
        AirlineDefinition airline,
        AirlineAirportSnapshot airport)
    {
        if (airline == null || airport == null)
            return default;

        float feeScore = CalculateFeeScore(
            airport.representativeLandingAndParkingFee,
            airline.IdealLandingAndParkingFee,
            airline.MaximumAcceptableLandingAndParkingFee,
            airline.PriceSensitivity);

        float gateScore = CalculateCapacityScore(
            airport.suitableGateCount,
            airline.DesiredSuitableGateCount);

        float runwayScore = CalculateCapacityScore(
            airport.longestRunwayLengthMeters,
            airline.MinimumRunwayLengthMeters);

        float storageScore = CalculateCapacityScore(
            airport.cargoStorageCapacity,
            airline.DesiredCargoStorageCapacity);

        float throughputScore = CalculateCapacityScore(
            airport.cargoHandlingThroughputPerHour,
            airline.DesiredCargoHandlingThroughputPerHour);

        // Passenger airport size is represented by developed foundation cells.
        // Cargo airlines continue to use storage and handling throughput.
        float cargoScore = airline.OperationType == AirlineOperationType.Passenger
            ? CalculateCapacityScore(
                airport.foundationCellCount,
                airline.DesiredFoundationCellCount)
            : (storageScore + throughputScore) * 0.5f;

        float nightScore = Mathf.Clamp(
            airport.nightOperationsScore,
            0f,
            100f);

        float reputationScore = CalculateReputationScore(
            airport.airportReputation,
            airline.MinimumPreferredAirportReputation);

        float performanceScore = Mathf.Clamp(
            airport.previousContractPerformance,
            0f,
            100f);

        AirlineInterestWeights weights = airline.InterestWeights;
        if (weights == null || weights.Total <= 0f)
            return new AirlineInterestResult(
                feeScore,
                gateScore,
                runwayScore,
                cargoScore,
                nightScore,
                reputationScore,
                performanceScore,
                0f);

        float weightedTotal =
            feeScore * weights.airportFees +
            gateScore * weights.suitableGates +
            runwayScore * weights.runwaySuitability +
            cargoScore * weights.cargoHandlingCapacity +
            nightScore * weights.nightOperations +
            reputationScore * weights.airportReputation +
            performanceScore * weights.previousContractPerformance;

        float totalInterest = Mathf.Clamp(
            weightedTotal / weights.Total,
            0f,
            100f);

        return new AirlineInterestResult(
            feeScore,
            gateScore,
            runwayScore,
            cargoScore,
            nightScore,
            reputationScore,
            performanceScore,
            totalInterest);
    }

    private static float CalculateFeeScore(
        float currentFee,
        float idealFee,
        float maximumAcceptableFee,
        float priceSensitivity)
    {
        if (currentFee <= idealFee)
            return 100f;

        if (maximumAcceptableFee <= idealFee)
            return currentFee <= maximumAcceptableFee ? 100f : 0f;

        float normalized =
            (currentFee - idealFee) /
            (maximumAcceptableFee - idealFee);

        // Price sensitivity changes how quickly interest falls as fees rise.
        float sensitivity = Mathf.Max(0.01f, priceSensitivity);
        float score = 100f * (1f - normalized * sensitivity);

        return Mathf.Clamp(score, 0f, 100f);
    }

    private static float CalculateCapacityScore(
        float available,
        float desired)
    {
        if (desired <= 0f)
            return 100f;

        return Mathf.Clamp01(available / desired) * 100f;
    }

    private static float CalculateReputationScore(
        float reputation,
        float preferredMinimum)
    {
        reputation = Mathf.Clamp(reputation, 0f, 100f);
        preferredMinimum = Mathf.Clamp(preferredMinimum, 0f, 100f);

        if (preferredMinimum >= 100f)
            return reputation >= 100f ? 100f : reputation;

        if (reputation >= preferredMinimum)
            return 100f;

        return reputation / Mathf.Max(1f, preferredMinimum) * 100f;
    }
}
