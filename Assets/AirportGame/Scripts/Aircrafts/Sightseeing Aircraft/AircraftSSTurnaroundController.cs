
using UnityEngine;

/// <summary>
/// Manages sightseeing parking, boarding, and passenger allocation.
/// </summary>
[DisallowMultipleComponent]
public class AircraftSSTurnaroundController : MonoBehaviour
{
    public enum TurnaroundState
    {
        WaitingForParking,
        Boarding,
        ReadyForDeparture
    }

    [Header("References")]
    [SerializeField]
    private AircraftSSLandingTaxiController landingTaxiController;

    [Header("Aircraft Capacity")]
    [Min(0)]
    [SerializeField] private int passengerCapacity = 6;

    [Header("Boarding")]
    [Min(0.1f)]
    [SerializeField] private float boardingDurationSeconds = 10f;

    [Header("Debug")]
    [SerializeField] private bool logProgress = true;

    [SerializeField]
    private TurnaroundState state =
        TurnaroundState.WaitingForParking;

    [SerializeField] private float boardingTimeRemaining;
    [SerializeField] private int passengersOnBoard;

    private SightseeingAircraftParkingSpot reservedSpot;
    private SightseeingFlightEconomy sightseeingEconomy;
    private bool boardingStarted;
    private bool boardingCompleted;

    public TurnaroundState State => state;
    public bool IsBoarding => state == TurnaroundState.Boarding;
    public bool IsBoardingComplete => boardingCompleted;
    public float BoardingTimeRemaining =>
        Mathf.Max(0f, boardingTimeRemaining);

    public SightseeingAircraftParkingSpot ReservedSpot => reservedSpot;
    public int PassengersOnBoard => passengersOnBoard;
    public int PassengerCapacity => passengerCapacity;

    private void Awake()
    {
        if (landingTaxiController == null)
        {
            landingTaxiController =
                GetComponent<AircraftSSLandingTaxiController>();
        }

        sightseeingEconomy = SightseeingFlightEconomy.Instance;
        boardingTimeRemaining = boardingDurationSeconds;

        if (landingTaxiController == null)
        {
            Debug.LogError(
                $"{nameof(AircraftSSTurnaroundController)} on {name}: " +
                "AircraftSSLandingTaxiController is missing.",
                this);
        }
    }

    private void Update()
    {
        if (boardingCompleted)
            return;

        if (sightseeingEconomy == null)
            sightseeingEconomy = SightseeingFlightEconomy.Instance;

        if (reservedSpot == null)
            FindReservedParkingSpot();

        if (landingTaxiController == null ||
            !landingTaxiController.HasArrivedAtParking)
            return;

        if (reservedSpot == null)
        {
            LogWaiting(
                "Aircraft has arrived, but its reserved parking spot " +
                "could not be found.");
            return;
        }

        if (reservedSpot.ReservedAircraft != gameObject)
        {
            LogWaiting(
                "Aircraft no longer owns its expected parking reservation.");
            return;
        }

        if (!boardingStarted)
            StartBoarding();

        if (state != TurnaroundState.Boarding)
            return;

        // Use the existing boarding timer behaviour.
        boardingTimeRemaining -= Time.deltaTime;

        if (boardingTimeRemaining <= 0f)
            CompleteBoarding();
    }

    private void FindReservedParkingSpot()
    {
        SightseeingAircraftParkingSpot[] spots =
            FindObjectsByType<SightseeingAircraftParkingSpot>(
                FindObjectsSortMode.None);

        foreach (SightseeingAircraftParkingSpot spot in spots)
        {
            if (spot != null && spot.ReservedAircraft == gameObject)
            {
                reservedSpot = spot;
                return;
            }
        }
    }

    private void StartBoarding()
    {
        boardingStarted = true;
        boardingTimeRemaining =
            Mathf.Max(0.1f, boardingDurationSeconds);

        state = TurnaroundState.Boarding;

        if (logProgress)
        {
            Debug.Log(
                $"SIGHTSEEING BOARDING STARTED | Aircraft: {name} | " +
                $"Duration: {boardingTimeRemaining:F1} seconds",
                this);
        }
    }

    private void CompleteBoarding()
    {
        if (boardingCompleted)
            return;

        // Determine passengers exactly once, when boarding finishes.
        if (sightseeingEconomy != null)
        {
            passengersOnBoard =
                sightseeingEconomy.CalculatePassengerDemand(
                    passengerCapacity);
        }
        else
        {
            passengersOnBoard = 0;

            Debug.LogError(
                $"{name}: SightseeingFlightEconomy is missing. " +
                "No passengers were assigned.",
                this);
        }

        passengersOnBoard =
            Mathf.Clamp(passengersOnBoard, 0, passengerCapacity);

        boardingCompleted = true;
        boardingTimeRemaining = 0f;
        state = TurnaroundState.ReadyForDeparture;

        AircraftFlightIdentity identity =
            GetComponent<AircraftFlightIdentity>();

        string flightNumber =
            identity != null ? identity.FlightNumber : name;

        if (logProgress)
        {
            int price = sightseeingEconomy != null
                ? sightseeingEconomy.TicketPrice
                : 0;

            Debug.Log(
                $"SIGHTSEEING BOARDING COMPLETE | Flight: {flightNumber} | " +
                $"Passengers: {passengersOnBoard}/{passengerCapacity} | " +
                $"Ticket price: ${price}",
                this);
        }
    }

    private void LogWaiting(string message)
    {
        if (logProgress)
        {
            Debug.LogWarning(
                $"SIGHTSEEING TURNAROUND WAITING | {name} | {message}",
                this);
        }
    }

    private void OnDestroy()
    {
        if (reservedSpot != null)
            reservedSpot.Release(gameObject);
    }
}
