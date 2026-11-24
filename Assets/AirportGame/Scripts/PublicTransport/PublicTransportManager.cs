using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the airport public-transport system and coordinates its single
/// airport route. Vehicle movement and passenger simulation are handled by
/// their own components and are intentionally not part of this manager.
/// </summary>
[DisallowMultipleComponent]
public class PublicTransportManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private PublicTransportRoute route;

    [Header("Bus Service")]
    [Min(1f)]
    [SerializeField] private float busFrequencyMinutes = 15f;
    [Min(0)]
    [SerializeField] private int ownedBuses;
    [Min(0)]
    [SerializeField] private int activeBuses;

    private readonly HashSet<PublicTransportStop> registeredStops =
        new HashSet<PublicTransportStop>();

    public event Action StopsChanged;

    public RoadNetwork RoadNetwork => roadNetwork;
    public GridManager GridManager => gridManager;
    public PublicTransportRoute Route => route;
    public float BusFrequencyMinutes => busFrequencyMinutes;
    public int OwnedBuses => ownedBuses;
    public int ActiveBuses => activeBuses;
    public int InactiveBuses => Mathf.Max(0, ownedBuses - activeBuses);
    public int RegisteredStopCount => registeredStops.Count;

    public IReadOnlyCollection<PublicTransportStop> RegisteredStops =>
        registeredStops;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        PublicTransportStop.StopRegistered += RegisterStop;
        PublicTransportStop.StopUnregistered += UnregisterStop;

        RefreshRegisteredStops();
    }

    private void OnDisable()
    {
        PublicTransportStop.StopRegistered -= RegisterStop;
        PublicTransportStop.StopUnregistered -= UnregisterStop;
    }

    private void CacheReferences()
    {
        if (roadNetwork == null)
            roadNetwork = FindAnyObjectByType<RoadNetwork>();

        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (route == null)
            route = GetComponent<PublicTransportRoute>();
    }

    private void RefreshRegisteredStops()
    {
        registeredStops.Clear();

        IReadOnlyList<PublicTransportStop> activeStops =
            PublicTransportStop.ActiveStops;

        for (int i = 0; i < activeStops.Count; i++)
        {
            PublicTransportStop stop = activeStops[i];
            if (stop != null && stop.IsRegistered)
                registeredStops.Add(stop);
        }

        StopsChanged?.Invoke();
    }

    private void RegisterStop(PublicTransportStop stop)
    {
        if (stop == null || !stop.IsRegistered)
            return;

        if (registeredStops.Add(stop))
            StopsChanged?.Invoke();
    }

    private void UnregisterStop(PublicTransportStop stop)
    {
        if (registeredStops.Remove(stop))
            StopsChanged?.Invoke();
    }

    public void SetBusFrequencyMinutes(float minutes)
    {
        busFrequencyMinutes = Mathf.Max(1f, minutes);
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
