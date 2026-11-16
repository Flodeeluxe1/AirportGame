using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Calculates a local 0-100 environment score from nearby placed buildables.
/// Uses cached PlacedBuilding registrations and linear distance falloff.
/// </summary>
public static class EnvironmentScoreCalculator
{
    public const float NeutralScore = 50f;

    private static readonly List<PlacedBuilding> activeBuildings =
        new List<PlacedBuilding>();

    private static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
        activeBuildings.Clear();
        initialized = false;
    }

    public static float CalculateScore(Vector3 worldPosition)
    {
        EnsureInitialized();

        float score = NeutralScore;

        for (int i = 0; i < activeBuildings.Count; i++)
        {
            PlacedBuilding building = activeBuildings[i];
            if (building == null || !building.isActiveAndEnabled)
                continue;

            BuildableDefinition definition = building.Definition;
            if (definition == null ||
                Mathf.Approximately(definition.environmentValue, 0f) ||
                definition.environmentRadius <= 0f)
            {
                continue;
            }

            Vector3 offset = building.transform.position - worldPosition;
            offset.y = 0f;

            float distance = offset.magnitude;
            float radius = definition.environmentRadius;
            if (distance >= radius)
                continue;

            float falloff = 1f - distance / radius;
            score += definition.environmentValue * falloff;
        }

        return Mathf.Clamp(score, 0f, 100f);
    }

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        PlacedBuilding.BuildingChanged += HandleBuildingChanged;
        RebuildCache();
        initialized = true;
    }

    private static void HandleBuildingChanged(PlacedBuilding building)
    {
        RebuildCache();
    }

    private static void RebuildCache()
    {
        activeBuildings.Clear();

        IReadOnlyList<PlacedBuilding> buildings =
            PlacedBuilding.ActiveBuildings;

        for (int i = 0; i < buildings.Count; i++)
        {
            PlacedBuilding building = buildings[i];
            if (building != null && building.isActiveAndEnabled)
                activeBuildings.Add(building);
        }
    }
}
