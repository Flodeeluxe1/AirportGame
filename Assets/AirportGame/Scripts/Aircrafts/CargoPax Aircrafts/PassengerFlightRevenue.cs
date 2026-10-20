
using UnityEngine;

public class PassengerFlightRevenue : MonoBehaviour
{
    [Header("Passenger Capacity")]
    [Tooltip("Number of seats available on this aircraft.")]
    [SerializeField, Min(1)]
    private int seatCapacity = 19;

    [Header("Runtime")]
    [SerializeField]
    private bool departureRevenueRecorded;

    public int SeatCapacity => Mathf.Max(1, seatCapacity);

    public int BookedPassengers => SeatCapacity;

    public bool DepartureRevenueRecorded =>
        departureRevenueRecorded;

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
