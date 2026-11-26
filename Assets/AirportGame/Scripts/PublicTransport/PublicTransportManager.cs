using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns airport public-transport fleet settings and the collection of
/// currently placed public-transport stops. Bus journeys and passenger
/// simulation are handled by their own systems.
/// </summary>
[DisallowMultipleComponent]
public class PublicTransportManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private GridManager gridManager;

    [Header("Bus Service")]
    [Min(1f)]
    [SerializeField] private float busFrequencyMinutes = 30f;
    [Min(0)]
    [SerializeField] private int ownedBuses;
    [Min(0)]
    [SerializeField] private int activeBuses;

    // Populated by their service managers when train and underground
    // transport are implemented. Zero means that the service is unavailable.
    private float trainCapacityPerHour;
    private float undergroundCapacityPerHour;

    private readonly HashSet<PlacedBuilding> registeredStops =
        new HashSet<PlacedBuilding>();

    public event Action StopsChanged;
    public event Action BusFrequencyChanged;
    public event Action TrainServiceChanged;
    public event Action UndergroundServiceChanged;
    public event Action FleetChanged;

    public RoadNetwork RoadNetwork => roadNetwork;
    public GridManager GridManager => gridManager;
    public float BusFrequencyMinutes => busFrequencyMinutes;
    public float BusCapacityPerHour(int capacityPerDeparture)
    {
        if (activeBuses <= 0 || registeredStops.Count == 0)
            return 0f;

        return (60f / Mathf.Max(1f, busFrequencyMinutes)) * Mathf.Max(1, capacityPerDeparture);
    }

    public float GetServiceCapacityPerHour(
        PassengerIdentity.TransportMode mode,
        int busCapacityPerDeparture)
    {
        switch (mode)
        {
            case PassengerIdentity.TransportMode.Bus:
                return BusCapacityPerHour(busCapacityPerDeparture);
            case PassengerIdentity.TransportMode.Train:
                return trainCapacityPerHour;
            case PassengerIdentity.TransportMode.Underground:
                return undergroundCapacityPerHour;
            default:
                return 0f;
        }
    }
    public int OwnedBuses => ownedBuses;
    public int ActiveBuses => activeBuses;
    public int InactiveBuses => Mathf.Max(0, ownedBuses - activeBuses);
    public int RegisteredStopCount => registeredStops.Count;
    public float TrainCapacityPerHour => trainCapacityPerHour;
    public float UndergroundCapacityPerHour => undergroundCapacityPerHour;

    public IReadOnlyCollection<PlacedBuilding> RegisteredStops =>
        registeredStops;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        PlacedBuilding.BuildingChanged += HandleBuildingChanged;
        RefreshRegisteredStops();
    }

    private void OnDisable()
    {
        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
    }

    private void CacheReferences()
    {
        if (roadNetwork == null)
            roadNetwork = FindAnyObjectByType<RoadNetwork>();

        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();
    }

    private void RefreshRegisteredStops()
    {
        registeredStops.Clear();

        IReadOnlyList<PlacedBuilding> activeBuildings =
            PlacedBuilding.ActiveBuildings;

        for (int i = 0; i < activeBuildings.Count; i++)
        {
            PlacedBuilding building = activeBuildings[i];
            if (IsPublicTransportStop(building))
                registeredStops.Add(building);
        }

        StopsChanged?.Invoke();
    }

    private void HandleBuildingChanged(PlacedBuilding building)
    {
        if (building == null)
            return;

        bool isActive = IsActiveBuilding(building);
        bool isStop = isActive && IsPublicTransportStop(building);
        bool changed;

        if (isStop)
            changed = registeredStops.Add(building);
        else
            changed = registeredStops.Remove(building);

        if (changed)
            StopsChanged?.Invoke();
    }

    private static bool IsPublicTransportStop(PlacedBuilding building)
    {
        return building != null &&
               building.isActiveAndEnabled &&
               building.Definition != null &&
               (building.Definition.publicTransportModes &
                PublicTransportMode.BusStop) != 0;
    }

    private static bool IsActiveBuilding(PlacedBuilding building)
    {
        IReadOnlyList<PlacedBuilding> activeBuildings =
            PlacedBuilding.ActiveBuildings;

        for (int i = 0; i < activeBuildings.Count; i++)
        {
            if (activeBuildings[i] == building)
                return true;
        }

        return false;
    }

    public void SetBusFrequencyMinutes(float minutes)
    {
        float updatedFrequency = Mathf.Max(1f, minutes);
        if (Mathf.Approximately(busFrequencyMinutes, updatedFrequency))
            return;

        busFrequencyMinutes = updatedFrequency;
        BusFrequencyChanged?.Invoke();
    }

    /// <summary>
    /// Updates the estimated passenger capacity provided by train service.
    /// The train system can call this whenever its timetable or train capacity changes.
    /// </summary>
    public void SetTrainService(float frequencyMinutes, int maxTrainCapacity)
    {
        float capacity = frequencyMinutes > 0f && maxTrainCapacity > 0
            ? (60f / frequencyMinutes) * maxTrainCapacity
            : 0f;
        if (Mathf.Approximately(trainCapacityPerHour, capacity))
            return;

        trainCapacityPerHour = capacity;
        TrainServiceChanged?.Invoke();
    }

    /// <summary>
    /// Updates the estimated passenger capacity provided by underground service.
    /// The underground system can call this whenever its timetable or train capacity changes.
    /// </summary>
    public void SetUndergroundService(float frequencyMinutes, int maxTrainCapacity)
    {
        float capacity = frequencyMinutes > 0f && maxTrainCapacity > 0
            ? (60f / frequencyMinutes) * maxTrainCapacity
            : 0f;
        if (Mathf.Approximately(undergroundCapacityPerHour, capacity))
            return;

        undergroundCapacityPerHour = capacity;
        UndergroundServiceChanged?.Invoke();
    }

    /// <summary>
    /// Updates the number of owned and assigned buses. Purchasing and operating
    /// costs are applied by the economy/UI layer when fleet management is added.
    /// </summary>
    public bool TrySetFleet(int newOwnedBuses, int newActiveBuses)
    {
        if (newOwnedBuses < 0 || newActiveBuses < 0 ||
            newActiveBuses > newOwnedBuses)
            return false;

        if (ownedBuses == newOwnedBuses && activeBuses == newActiveBuses)
            return true;

        ownedBuses = newOwnedBuses;
        activeBuses = newActiveBuses;
        FleetChanged?.Invoke();
        return true;
    }
}
