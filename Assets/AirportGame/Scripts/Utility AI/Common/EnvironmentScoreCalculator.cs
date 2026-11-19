using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Calculates a local 0-100 environment score using a spatial index of
/// placed buildables and linear distance falloff.
/// </summary>
public static class EnvironmentScoreCalculator
{
    public const float NeutralScore = 50f;
    private const float CellSize = 12f;
    private static readonly Dictionary<Vector2Int, List<PlacedBuilding>> Buckets = new Dictionary<Vector2Int, List<PlacedBuilding>>();
    private static readonly Dictionary<PlacedBuilding, List<Vector2Int>> BuildingCells = new Dictionary<PlacedBuilding, List<Vector2Int>>();
    private static bool initialized;
    private static int version;
    private static RoomPurposeController roomPurposeController;

    public static int Version { get { EnsureInitialized(); return version; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
        Buckets.Clear();
        BuildingCells.Clear();
        initialized = false;
        version = 0;
        roomPurposeController = null;
    }

    public static float CalculateScore(Vector3 worldPosition)
    {
        EnsureInitialized();
        Vector2Int cell = GetCell(worldPosition);
        float score = NeutralScore;

        if (Buckets.TryGetValue(cell, out List<PlacedBuilding> nearbyBuildings))
        {
            for (int i = 0; i < nearbyBuildings.Count; i++)
            {
                PlacedBuilding building = nearbyBuildings[i];
                if (building == null || !building.isActiveAndEnabled)
                    continue;

                BuildableDefinition definition = building.Definition;
                if (definition == null)
                    continue;

                float radius = definition.environmentRadius;
                float value = definition.environmentValue;
                if (radius <= 0f || Mathf.Approximately(value, 0f))
                    continue;

                Vector3 offset = building.transform.position - worldPosition;
                offset.y = 0f;
                float distanceSquared = offset.sqrMagnitude;
                if (distanceSquared >= radius * radius)
                    continue;

                score += value * (1f - Mathf.Sqrt(distanceSquared) / radius);
            }
        }

        score = Mathf.Clamp(score, 0f, 100f);

        if (roomPurposeController == null)
            roomPurposeController = Object.FindAnyObjectByType<RoomPurposeController>();

        if (roomPurposeController != null)
            score = roomPurposeController.ApplyRoomEnvironmentEffect(worldPosition, score);

        return Mathf.Clamp(score, 0f, 100f);
    }

    public static void NotifyRoomEnvironmentChanged()
    {
        version++;
    }

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        PlacedBuilding.BuildingChanged += HandleBuildingChanged;
        IReadOnlyList<PlacedBuilding> buildings = PlacedBuilding.ActiveBuildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            PlacedBuilding building = buildings[i];
            if (building != null && building.isActiveAndEnabled)
                RegisterBuilding(building);
        }
        initialized = true;
    }

    private static void HandleBuildingChanged(PlacedBuilding building)
    {
        if (ReferenceEquals(building, null))
            return;
        if (building.isActiveAndEnabled)
            RegisterBuilding(building);
        else
            UnregisterBuilding(building);
    }

    private static void RegisterBuilding(PlacedBuilding building)
    {
        UnregisterBuilding(building);
        BuildableDefinition definition = building.Definition;
        if (definition == null || Mathf.Approximately(definition.environmentValue, 0f) || definition.environmentRadius <= 0f)
            return;

        Vector3 position = building.transform.position;
        float radius = definition.environmentRadius;
        Vector2Int minimum = GetCell(new Vector3(position.x - radius, 0f, position.z - radius));
        Vector2Int maximum = GetCell(new Vector3(position.x + radius, 0f, position.z + radius));
        List<Vector2Int> occupiedCells = new List<Vector2Int>();

        for (int x = minimum.x; x <= maximum.x; x++)
        {
            for (int y = minimum.y; y <= maximum.y; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!Buckets.TryGetValue(cell, out List<PlacedBuilding> bucket))
                {
                    bucket = new List<PlacedBuilding>(4);
                    Buckets.Add(cell, bucket);
                }
                bucket.Add(building);
                occupiedCells.Add(cell);
            }
        }

        BuildingCells[building] = occupiedCells;
        version++;
    }

    private static void UnregisterBuilding(PlacedBuilding building)
    {
        if (!BuildingCells.TryGetValue(building, out List<Vector2Int> occupiedCells))
            return;

        for (int i = 0; i < occupiedCells.Count; i++)
        {
            Vector2Int cell = occupiedCells[i];
            if (!Buckets.TryGetValue(cell, out List<PlacedBuilding> bucket))
                continue;
            bucket.Remove(building);
            if (bucket.Count == 0)
                Buckets.Remove(cell);
        }
        BuildingCells.Remove(building);
        version++;
    }

    private static Vector2Int GetCell(Vector3 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.z / CellSize));
    }
}
