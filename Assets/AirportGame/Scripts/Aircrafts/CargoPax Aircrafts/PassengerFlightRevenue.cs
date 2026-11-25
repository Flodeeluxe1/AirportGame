using UnityEngine;

[RequireComponent(typeof(AircraftStats))]
public class PassengerFlightRevenue : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;

    [Header("Runtime")]
    [SerializeField]
    private bool departureRevenueRecorded;

    private AircraftStats aircraftStats;

    public int SeatCapacity =>
        aircraftStats != null ? Mathf.Max(1, aircraftStats.maxPassengers) : 1;

    public int IncomingPassengers =>
        aircraftStats != null ? aircraftStats.IncomingPassengers : 0;

    public int BookedPassengers =>
        aircraftStats != null ? aircraftStats.OutgoingPassengers : 0;

    public bool DepartureRevenueRecorded =>
        departureRevenueRecorded;

    private void Awake()
    {
        aircraftStats = GetComponent<AircraftStats>();

        if (aircraftStats == null)
        {
            LogWarning($"{name}: AircraftStats is missing; passenger counts cannot be initialized.", this);
            return;
        }

        // Preserve the existing independent random passenger-count behaviour.
        aircraftStats.SetIncomingPassengers(
            Random.Range(0, SeatCapacity + 1));
        aircraftStats.SetOutgoingPassengers(
            Random.Range(0, SeatCapacity + 1));
    }

    /// <summary>
    /// Called by the takeoff controller when the aircraft
    /// successfully completes its departure.
    /// </summary>
    public void RecordSuccessfulDeparture()
    {
        if (departureRevenueRecorded)
            return;

        AircraftFlightIdentity identity =
            GetComponent<AircraftFlightIdentity>();

        if (identity == null ||
            !identity.IsInitialized ||
            identity.Definition == null)
        {
            LogWarning(
                $"{name}: Cannot record passenger revenue because " +
                "the flight identity is not initialized.",
                this
            );

            return;
        }

        if (identity.Definition.OperationType !=
                FlightOperationType.PassengerOnly &&
            identity.Definition.OperationType !=
                FlightOperationType.CargoAndPassengers)
        {
            return;
        }

        AirportPassengerRevenueManager revenueManager =
            AirportPassengerRevenueManager.Instance;

        if (revenueManager == null)
        {
            LogError(
                $"{name}: No AirportPassengerRevenueManager exists. " +
                "Passenger revenue was not recorded.",
                this
            );

            return;
        }

        if (revenueManager.RecordPassengerDeparture(
                identity,
                BookedPassengers))
        {
            departureRevenueRecorded = true;
        }
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
