using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collects live airport conditions and recalculates configured airline
/// interest only when one of the contributing airport systems changes.
/// </summary>
[DisallowMultipleComponent]
public class AirlineAirportSnapshotCollector : MonoBehaviour
{
    [Header("Airline Definitions")]
    [SerializeField] private List<AirlineDefinition> airlines =
        new List<AirlineDefinition>();

    [Header("Airport References")]
    [SerializeField] private AirportManagementController airportManagementController;
    [SerializeField] private AircraftGateRegistry gateRegistry;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;

    [Header("Runtime Results")]
    [SerializeField] private List<AirlineInterestRecord> interestResults =
        new List<AirlineInterestRecord>();

    private readonly Dictionary<AirlineDefinition, Dictionary<AirlineOperationType, AirlineInterestResult>> results =
        new Dictionary<AirlineDefinition, Dictionary<AirlineOperationType, AirlineInterestResult>>();

    private bool foundationCountDirty = true;
    private int cachedFoundationCellCount;
    private Coroutine pendingFoundationRefresh;

    public event Action<AirlineDefinition, AirlineInterestResult> InterestUpdated;

    public IReadOnlyList<AirlineInterestRecord> InterestResults => interestResults;
    public IReadOnlyList<AirlineDefinition> ConfiguredAirlines => airlines;

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToFoundationChanges();

        PlacedBuilding.BuildingChanged += HandleBuildingChanged;
        CargoZone.CapacityChanged += HandleCargoCapacityChanged;

        if (airportManagementController != null)
            airportManagementController.StateChanged += HandleAirportStateChanged;

        if (AirportRatingManager.Instance != null)
            AirportRatingManager.Instance.AirportRatingChanged += HandleAirportRatingChanged;

        StartCoroutine(RefreshAfterInitialization());
    }

    private void OnDisable()
    {
        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
        if (gridSurface != null)
            gridSurface.FoundationSurfaceChanged -= HandleFoundationSurfaceChanged;
        pendingFoundationRefresh = null;
        CargoZone.CapacityChanged -= HandleCargoCapacityChanged;

        if (airportManagementController != null)
            airportManagementController.StateChanged -= HandleAirportStateChanged;

        if (AirportRatingManager.Instance != null)
            AirportRatingManager.Instance.AirportRatingChanged -= HandleAirportRatingChanged;
    }

    private IEnumerator RefreshAfterInitialization()
    {
        // AirportManagementController initializes default fee profiles in Start.
        // Wait one frame so scene services have completed their Start methods.
        yield return null;
        ResolveReferences();
        SubscribeToResolvedServices();
        RefreshAllAirlines();
    }

    private void ResolveReferences()
    {
        if (airportManagementController == null)
            airportManagementController = AirportManagementController.Instance;

        if (gateRegistry == null)
            gateRegistry = FindAnyObjectByType<AircraftGateRegistry>();

        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (gridSurface == null)
            gridSurface = FindAnyObjectByType<GridSurface>();
    }

    private void SubscribeToResolvedServices()
    {
        SubscribeToFoundationChanges();
        if (airportManagementController != null)
            airportManagementController.StateChanged -= HandleAirportStateChanged;

        if (airportManagementController != null)
            airportManagementController.StateChanged += HandleAirportStateChanged;

        AirportRatingManager ratingManager = AirportRatingManager.Instance;
        if (ratingManager != null)
        {
            ratingManager.AirportRatingChanged -= HandleAirportRatingChanged;
            ratingManager.AirportRatingChanged += HandleAirportRatingChanged;
        }
    }


    private void SubscribeToFoundationChanges()
    {
        if (gridSurface == null)
            return;

        gridSurface.FoundationSurfaceChanged -= HandleFoundationSurfaceChanged;
        gridSurface.FoundationSurfaceChanged += HandleFoundationSurfaceChanged;
    }

    private void HandleFoundationSurfaceChanged()
    {
        foundationCountDirty = true;

        // GridSurface can raise this event once per changed cell. Coalesce a
        // burst into one refresh on the next frame instead of rescanning and
        // recalculating every airline for every individual cell.
        if (pendingFoundationRefresh == null && isActiveAndEnabled)
            pendingFoundationRefresh = StartCoroutine(RefreshAfterFoundationChanges());
    }

    private IEnumerator RefreshAfterFoundationChanges()
    {
        yield return null;
        pendingFoundationRefresh = null;
        if (isActiveAndEnabled)
            RefreshAllAirlines();
    }

    private int GetFoundationCellCount()
    {
        if (!foundationCountDirty)
            return cachedFoundationCellCount;

        cachedFoundationCellCount = 0;
        if (gridManager != null && gridSurface != null)
        {
            for (int x = 0; x < gridManager.Width; x++)
            {
                for (int z = 0; z < gridManager.Height; z++)
                {
                    if (gridSurface.GetSurface(new Vector2Int(x, z)) ==
                        BuildSurface.Foundation)
                    {
                        cachedFoundationCellCount++;
                    }
                }
            }
        }

        foundationCountDirty = false;
        return cachedFoundationCellCount;
    }

    private void HandleBuildingChanged(PlacedBuilding building)
    {
        // Invalidate explicitly before collecting. Event subscription order is
        // not guaranteed, so the registry may not have handled this event yet.
        if (gateRegistry != null)
            gateRegistry.InvalidateCache();

        // Building placement/removal can affect gates, runways, and cargo zones.
        RefreshAllAirlines();
    }

    private void HandleCargoCapacityChanged()
    {
        RefreshAllAirlines();
    }

    private void HandleAirportStateChanged()
    {
        ResolveReferences();
        SubscribeToResolvedServices();
        RefreshAllAirlines();
    }

    private void HandleAirportRatingChanged(float rating)
    {
        RefreshAllAirlines();
    }

    [ContextMenu("Refresh Airline Interest")]
    public void RefreshAllAirlines()
    {
        ResolveReferences();

        // A loaded airport may have replaced the gates since the last snapshot.
        if (gateRegistry != null)
            gateRegistry.InvalidateCache();

        results.Clear();
        interestResults.Clear();

        foreach (AirlineDefinition airline in airlines)
        {
            if (airline == null)
                continue;

            HashSet<AirlineOperationType> operationTypes =
                new HashSet<AirlineOperationType>();
            foreach (AirlineContractTemplate template in airline.ContractTemplates)
            {
                if (template != null && operationTypes.Add(template.OperationType))
                    RefreshInterest(airline, template.OperationType);
            }
        }
    }

    public AirlineInterestResult RefreshInterest(
        AirlineDefinition airline,
        AirlineOperationType operationType)
    {
        if (airline == null)
            return default;

        AirlineAirportSnapshot snapshot = BuildSnapshot(airline, operationType);
        AirlineInterestResult result =
            AirlineInterestCalculator.Calculate(airline, snapshot, operationType);

        if (!results.TryGetValue(airline, out Dictionary<AirlineOperationType, AirlineInterestResult> airlineResults))
        {
            airlineResults = new Dictionary<AirlineOperationType, AirlineInterestResult>();
            results.Add(airline, airlineResults);
        }
        airlineResults[operationType] = result;

        AirlineInterestRecord record = FindRecord(airline, operationType);
        if (record == null)
        {
            record = new AirlineInterestRecord();
            interestResults.Add(record);
        }

        record.airline = airline;
        record.operationType = operationType;
        record.snapshot = snapshot;
        record.result = result;

        InterestUpdated?.Invoke(airline, result);
        return result;
    }

    public bool TryGetInterest(
        AirlineDefinition airline,
        AirlineOperationType operationType,
        out AirlineInterestResult result)
    {
        if (airline != null &&
            results.TryGetValue(airline, out Dictionary<AirlineOperationType, AirlineInterestResult> airlineResults) &&
            airlineResults.TryGetValue(operationType, out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    public AirlineInterestResult CalculateInterest(
        AirlineDefinition airline,
        AirlineOperationType operationType)
    {
        if (airline == null)
            return default;

        return AirlineInterestCalculator.Calculate(
            airline,
            BuildSnapshot(airline, operationType),
            operationType);
    }

    public AirlineAirportSnapshot BuildSnapshot(
        AirlineDefinition airline,
        AirlineOperationType operationType)
    {
        if (airline == null)
            return null;

        ResolveReferences();

        AirlineAirportSnapshot snapshot = new AirlineAirportSnapshot
        {
            representativeLandingAndParkingFee = GetRepresentativeFee(airline, operationType),
            suitableGateCount = GetSuitableGateCount(airline),
            longestRunwayLengthMeters = GetLongestRunwayLength(),
            nightOperationsScore = 100f,
            airportReputation = AirportRatingManager.Instance != null
                ? AirportRatingManager.Instance.AirportRating
                : 50f,
            previousContractPerformance = 50f,
            foundationCellCount = GetFoundationCellCount()
        };

        GetCargoCapacity(
            out snapshot.cargoStorageCapacity,
            out snapshot.cargoHandlingThroughputPerHour);

        return snapshot;
    }

    private int GetRepresentativeFee(
        AirlineDefinition airline,
        AirlineOperationType operationType)
    {
        if (airportManagementController == null)
            return airline.MaximumAcceptableLandingAndParkingFee;

        AirportManagementController.FeeSettings settings =
            airportManagementController.GetFeeSettingsForDisplay();

        if (settings == null)
            return airline.MaximumAcceptableLandingAndParkingFee;

        int cargoFee = GetProfileTotal(settings.cargo);
        int passengerFee = GetProfileTotal(settings.passenger);

        switch (operationType)
        {
            case AirlineOperationType.Passenger:
                return passengerFee;

            case AirlineOperationType.CargoAndPassengers:
                return Mathf.RoundToInt((cargoFee + passengerFee) * 0.5f);

            default:
                return cargoFee;
        }
    }

    private static int GetProfileTotal(
        AirportManagementController.AircraftFeeProfile profile)
    {
        return profile == null
            ? 0
            : Mathf.Max(0, profile.landingFee) +
              Mathf.Max(0, profile.parkingFee);
    }

    private int GetSuitableGateCount(AirlineDefinition airline)
    {
        if (gateRegistry != null)
            return gateRegistry.GetCompatibleGates(airline.PreferredGateSize).Count;

        AircraftGate[] gates = FindObjectsByType<AircraftGate>();
        int count = 0;

        foreach (AircraftGate gate in gates)
        {
            if (gate != null &&
                gate.TryGetGridOrigin(out _) &&
                gate.CanAccommodate(airline.PreferredGateSize))
            {
                count++;
            }
        }

        return count;
    }

    private float GetLongestRunwayLength()
    {
        if (gridManager == null)
            return 0f;

        AircraftRunway[] runways = FindObjectsByType<AircraftRunway>();
        float longestLength = 0f;

        foreach (AircraftRunway runway in runways)
        {
            if (runway == null ||
                !runway.TryGetGeometry(
                    gridManager,
                    out _,
                    out _,
                    out float runwayLength,
                    out _))
            {
                continue;
            }

            longestLength = Mathf.Max(longestLength, runwayLength);
        }

        return longestLength;
    }

    private static void GetCargoCapacity(
        out float storageCapacity,
        out float handlingThroughput)
    {
        storageCapacity = 0f;
        handlingThroughput = 0f;

        foreach (CargoZone zone in CargoZone.RegisteredZones)
        {
            // Only count zones attached to an actual placed building.
            if (zone == null || zone.Building == null)
                continue;

            storageCapacity += zone.StorageCapacityPackages;
            handlingThroughput += zone.HandlingThroughputPackagesPerHour;
        }
    }

    private AirlineInterestRecord FindRecord(
        AirlineDefinition airline,
        AirlineOperationType operationType)
    {
        foreach (AirlineInterestRecord record in interestResults)
        {
            if (record != null &&
                record.airline == airline &&
                record.operationType == operationType)
                return record;
        }

        return null;
    }
}

[Serializable]
public class AirlineInterestRecord
{
    public AirlineDefinition airline;
    public AirlineOperationType operationType;
    public AirlineAirportSnapshot snapshot;
    public AirlineInterestResult result;
}
