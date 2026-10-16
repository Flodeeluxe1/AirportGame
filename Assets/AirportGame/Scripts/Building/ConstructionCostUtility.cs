
using UnityEngine;

/// <summary>
/// Shared payment logic for all construction placement handlers.
/// BuildableDefinition.buildCost is the cost of one unit.
/// </summary>
public static class ConstructionCostUtility
{
    public static bool TryPay(
        BuildableDefinition definition,
        int quantity,
        string constructionType)
    {
        if (definition == null)
        {
            Debug.LogError(
                "ConstructionCostUtility: BuildableDefinition is missing."
            );

            return false;
        }

        if (quantity <= 0)
        {
            Debug.LogError(
                "ConstructionCostUtility: Quantity must be positive."
            );

            return false;
        }

        if (definition.buildCost < 0)
        {
            Debug.LogError(
                $"ConstructionCostUtility: '{definition.displayName}' " +
                "has a negative build cost. Construction was cancelled."
            );

            return false;
        }

        // A cost of zero means this buildable is free.
        if (definition.buildCost == 0)
            return true;

        long totalCost = (long)definition.buildCost * quantity;

        if (totalCost > int.MaxValue)
        {
            Debug.LogError(
                $"ConstructionCostUtility: Cost for " +
                $"'{definition.displayName}' exceeds the supported limit."
            );

            return false;
        }

        AirportEconomyManager economyManager =
            Object.FindFirstObjectByType<AirportEconomyManager>();

        if (economyManager == null)
        {
            Debug.LogError(
                "ConstructionCostUtility: AirportEconomyManager was not " +
                "found. Construction cannot proceed."
            );

            return false;
        }

        return economyManager.TrySpend(
            (int)totalCost,
            $"{constructionType}: {definition.displayName} " +
            (quantity > 1 ? $"x{quantity}" : "")
        );
    }
}
