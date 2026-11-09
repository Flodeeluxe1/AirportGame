using UnityEngine;

public class PassengerFlightRevenue : MonoBehaviour
{
    [Header("Passenger Capacity")]
    [Tooltip("Maximum number of seats available on this aircraft.")]
    [SerializeField, Min(1)]
    private int seatCapacity = 19;

    [Header("Incoming Passengers")]
    [Tooltip("Passengers arriving on this flight. Used for deboarding and cleaning.")]
    [SerializeField, Min(0)]
    private int incomingPassengers;

    [Header("Outgoing Booked Passengers")]
    [Tooltip("Passengers booked to depart on this flight. Used for departure revenue.")]
    [SerializeField, Min(0)]
    private int bookedPassengers;

    [Header("Runtime")]
    [SerializeField]
    private bool departureRevenueRecorded;

    public int SeatCapacity => Mathf.Max(1, seatCapacity);

    public int IncomingPassengers =>
        Mathf.Clamp(incomingPassengers, 0, SeatCapacity);

    public int BookedPassengers =>
        Mathf.Clamp(bookedPassengers, 0, SeatCapacity);

    public bool DepartureRevenueRecorded =>
        departureRevenueRecorded;

    private void Awake()
    {
        // Generate arriving and departing passenger counts independently
        // when this flight's aircraft instance is created.
        incomingPassengers = Random.Range(0, SeatCapacity + 1);
        bookedPassengers = Random.Range(0, SeatCapacity + 1);
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
            Debug.LogWarning(
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
            Debug.LogError(
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
}
