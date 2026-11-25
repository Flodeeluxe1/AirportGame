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
    [SerializeField] private float busFrequencyMinutes = 15f;
    [Min(0)]
    [SerializeField] private int ownedBuses;
    [Min(0)]
    [SerializeField] private int activeBuses;

    private readonly HashSet<PlacedBuilding> registeredStops =
        new HashSet<PlacedBuilding>();

    public event Action StopsChanged;
    public event Action BusFrequencyChanged;

    public RoadNetwork RoadNetwork => roadNetwork;
    public GridManager GridManager => gridManager;
    public float BusFrequencyMinutes => busFrequencyMinutes;
    public int OwnedBuses => ownedBuses;
    public int ActiveBuses => activeBuses;
    public int InactiveBuses => Mathf.Max(0, ownedBuses - activeBuses);
    public int RegisteredStopCount => registeredStops.Count;

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
    /// Updates the number of owned and assigned buses. Purchasing and operating
    /// costs are applied by the economy/UI layer when fleet management is added.
    /// </summary>
    public bool TrySetFleet(int newOwnedBuses, int newActiveBuses)
    {
        if (newOwnedBuses < 0 || newActiveBuses < 0 ||
            newActiveBuses > newOwnedBuses)
            return false;

        ownedBuses = newOwnedBuses;
        activeBuses = newActiveBuses;
        return true;
    }
}
