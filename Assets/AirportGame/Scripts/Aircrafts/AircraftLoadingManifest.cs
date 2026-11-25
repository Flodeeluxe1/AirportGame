
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AircraftStats))]
public class AircraftLoadingManifest : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    [Header("Initial Baggage")]
    [Min(0)]
    [SerializeField] private int initialIncomingBaggageCount = 10;

    [Min(0)]
    [SerializeField] private int expectedOutgoingBaggageCount = 10;

    [Header("Incoming Baggage Runtime Status")]
    [SerializeField] private int totalIncomingBaggage;
    [SerializeField] private int remainingIncomingBaggage;
    [SerializeField] private int reservedIncomingBaggage;

    [Header("Outgoing Baggage Runtime Status")]
    [SerializeField] private int totalOutgoingBaggageLoaded;
    [SerializeField] private int remainingOutgoingBaggage;
    [SerializeField] private int reservedOutgoingBaggage;

    private AircraftFlightIdentity flightIdentity;
    private AircraftStats aircraftStats;

    private readonly List<string> incomingPackageIds =
        new List<string>();

    private readonly HashSet<string> reservedIncomingPackageIds =
        new HashSet<string>();

    private readonly HashSet<string> reservedOutgoingPackageIds =
        new HashSet<string>();

    private readonly HashSet<string> loadedOutgoingPackageIds =
        new HashSet<string>();

    public int TotalIncomingBaggage => totalIncomingBaggage;
    public int RemainingIncomingBaggage => remainingIncomingBaggage;
    public int ReservedIncomingBaggage => reservedIncomingBaggage;

    public int AvailableIncomingBaggage =>
        remainingIncomingBaggage - reservedIncomingBaggage;

    public int ExpectedOutgoingBaggageCount =>
        expectedOutgoingBaggageCount;

    public int TotalOutgoingBaggageLoaded =>
        totalOutgoingBaggageLoaded;

    public int RemainingOutgoingBaggage =>
        remainingOutgoingBaggage;

    public int ReservedOutgoingBaggage =>
        reservedOutgoingBaggage;

    public int AvailableOutgoingBaggage =>
        remainingOutgoingBaggage - reservedOutgoingBaggage;

    public bool IsInitialized { get; private set; }

    public bool HasIncomingBaggageRemaining =>
        remainingIncomingBaggage > 0;

    public bool HasOutgoingBaggageRemaining =>
        remainingOutgoingBaggage > 0;

    private void Awake()
    {
        flightIdentity = GetComponent<AircraftFlightIdentity>();
        aircraftStats = GetComponent<AircraftStats>();
    }

    public bool InitializeManifest()
    {
        if (IsInitialized)
            return true;

        if (flightIdentity == null)
            flightIdentity = GetComponent<AircraftFlightIdentity>();

        if (aircraftStats == null)
            aircraftStats = GetComponent<AircraftStats>();

        if (flightIdentity == null ||
            !flightIdentity.IsInitialized)
        {
            LogWarning(
                $"{name}: Cannot initialize the loading manifest " +
                "until AircraftFlightIdentity is initialized.",
                this
            );

            return false;
        }

        incomingPackageIds.Clear();
        reservedIncomingPackageIds.Clear();
        reservedOutgoingPackageIds.Clear();
        loadedOutgoingPackageIds.Clear();

        string operationId = flightIdentity.OperationId;

        for (int i = 1; i <= initialIncomingBaggageCount; i++)
        {
            string packageId = $"{operationId}:IN:{i:00}";
            incomingPackageIds.Add(packageId);
        }

        totalIncomingBaggage = incomingPackageIds.Count;
        remainingIncomingBaggage = totalIncomingBaggage;
        reservedIncomingBaggage = 0;

        if (aircraftStats != null)
            aircraftStats.SetIncomingCargo(remainingIncomingBaggage);

        totalOutgoingBaggageLoaded = 0;
        if (aircraftStats != null)
            aircraftStats.SetOutgoingCargo(0);
        remainingOutgoingBaggage =
            Mathf.Max(0, expectedOutgoingBaggageCount);
        reservedOutgoingBaggage = 0;

        IsInitialized = true;

        Log(
            $"{name}: Loading manifest initialized for " +
            $"{flightIdentity.FlightNumber} | " +
            $"Operation: {operationId} | " +
            $"Incoming baggage: {totalIncomingBaggage} | " +
            $"Expected outgoing baggage: " +
            $"{expectedOutgoingBaggageCount}.",
            this
        );

        return true;
    }

    // -----------------------------
    // INCOMING BAGGAGE
    // -----------------------------

    public List<string> ReserveIncomingBaggage(int maximumCount)
    {
        List<string> result = new List<string>();

        if (!IsInitialized && !InitializeManifest())
            return result;

        if (maximumCount <= 0)
            return result;

        foreach (string packageId in incomingPackageIds)
        {
            if (result.Count >= maximumCount)
                break;

            if (reservedIncomingPackageIds.Contains(packageId))
                continue;

            reservedIncomingPackageIds.Add(packageId);
            result.Add(packageId);
        }

        reservedIncomingBaggage =
            reservedIncomingPackageIds.Count;

        return result;
    }

    public bool ConfirmIncomingBaggageTransferred(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return false;

        if (!reservedIncomingPackageIds.Remove(packageId))
            return false;

        if (!incomingPackageIds.Remove(packageId))
        {
            reservedIncomingPackageIds.Add(packageId);

            reservedIncomingBaggage =
                reservedIncomingPackageIds.Count;

            return false;
        }

        remainingIncomingBaggage = incomingPackageIds.Count;

        if (aircraftStats != null)
            aircraftStats.SetIncomingCargo(remainingIncomingBaggage);

        reservedIncomingBaggage =
            reservedIncomingPackageIds.Count;

        Log(
            $"{name}: Baggage transferred: {packageId} | " +
            $"Remaining on aircraft: {remainingIncomingBaggage}.",
            this
        );

        return true;
    }

    public bool ReleaseIncomingBaggageReservation(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return false;

        bool released =
            reservedIncomingPackageIds.Remove(packageId);

        reservedIncomingBaggage =
            reservedIncomingPackageIds.Count;

        return released;
    }

    // -----------------------------
    // OUTGOING BAGGAGE
    // -----------------------------

    public bool TryReserveOutgoingBaggage(
        string packageId,
        string operationId)
    {
        if (!IsInitialized && !InitializeManifest())
            return false;

        if (string.IsNullOrWhiteSpace(packageId) ||
            string.IsNullOrWhiteSpace(operationId))
        {
            return false;
        }

        if (flightIdentity == null ||
            operationId != flightIdentity.OperationId)
        {
            LogWarning(
                $"{name}: Rejected outgoing package '{packageId}'. " +
                "It belongs to a different flight operation.",
                this
            );

            return false;
        }

        if (loadedOutgoingPackageIds.Contains(packageId) ||
            reservedOutgoingPackageIds.Contains(packageId))
        {
            return false;
        }

        if (remainingOutgoingBaggage <= 0)
            return false;

        if (reservedOutgoingPackageIds.Count >=
            remainingOutgoingBaggage)
        {
            return false;
        }

        if (aircraftStats != null &&
            !aircraftStats.HasCargoCapacityFor(
                reservedOutgoingPackageIds.Count + 1))
        {
            return false;
        }

        reservedOutgoingPackageIds.Add(packageId);

        reservedOutgoingBaggage =
            reservedOutgoingPackageIds.Count;

        return true;
    }

    public bool ConfirmOutgoingBaggageLoaded(
        string packageId,
        string operationId)
    {
        if (string.IsNullOrWhiteSpace(packageId) ||
            string.IsNullOrWhiteSpace(operationId))
        {
            return false;
        }

        if (flightIdentity == null ||
            !flightIdentity.IsInitialized ||
            operationId != flightIdentity.OperationId)
        {
            LogWarning(
                $"{name}: Rejected outgoing baggage '{packageId}'. " +
                "The flight operation does not match this aircraft.",
                this
            );

            return false;
        }

        if (!reservedOutgoingPackageIds.Contains(packageId))
        {
            LogWarning(
                $"{name}: Cannot confirm outgoing baggage " +
                $"'{packageId}' because it has not been reserved.",
                this
            );

            return false;
        }

        if (loadedOutgoingPackageIds.Contains(packageId))
            return false;

        reservedOutgoingPackageIds.Remove(packageId);
        loadedOutgoingPackageIds.Add(packageId);

        totalOutgoingBaggageLoaded =
            loadedOutgoingPackageIds.Count;

        if (aircraftStats != null)
            aircraftStats.SetOutgoingCargo(totalOutgoingBaggageLoaded);

        remainingOutgoingBaggage = Mathf.Max(
            0,
            expectedOutgoingBaggageCount -
            totalOutgoingBaggageLoaded
        );

        reservedOutgoingBaggage =
            reservedOutgoingPackageIds.Count;

        Log(
            $"{name}: Outgoing baggage loaded: {packageId} | " +
            $"Flight: {operationId} | " +
            $"Loaded: {totalOutgoingBaggageLoaded}/" +
            $"{expectedOutgoingBaggageCount} | " +
            $"Remaining: {remainingOutgoingBaggage}.",
            this
        );

        return true;
    }

    public bool ReleaseOutgoingBaggageReservation(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return false;

        bool released =
            reservedOutgoingPackageIds.Remove(packageId);

        reservedOutgoingBaggage =
            reservedOutgoingPackageIds.Count;

        return released;
    }

    public bool IsOutgoingBaggageLoaded(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return false;

        return loadedOutgoingPackageIds.Contains(packageId);
    }

 
    private void Log(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.Log(message, context); else Debug.Log(message);
    }

    private void LogWarning(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message);
    }

    private void LogError(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogError(message, context); else Debug.LogError(message);
    }

    private void LogException(System.Exception exception, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception);
    }
}
