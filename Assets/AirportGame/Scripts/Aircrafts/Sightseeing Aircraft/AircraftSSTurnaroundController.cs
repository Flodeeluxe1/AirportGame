
using System;
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
    private bool passengerManifestConfigured;
    private bool boardingReady;
    private int expectedPassengers;
    private int boardedPassengers;
    private bool returnArrivalRaised;
    private AircraftSSTakeoffController takeoffController;
    private AircraftSSDepartureTaxiController departureTaxiController;
    private bool readyForNextFlight;

    public event Action<AircraftSSTurnaroundController> BoardingStarted;
    public event Action<AircraftSSTurnaroundController> ReturnedToParking;

    public TurnaroundState State => state;
    public bool IsBoarding => state == TurnaroundState.Boarding;
    public bool IsBoardingComplete => boardingCompleted;
    public float BoardingTimeRemaining =>
        Mathf.Max(0f, boardingTimeRemaining);

    public SightseeingAircraftParkingSpot ReservedSpot => reservedSpot;
    public int PassengersOnBoard => passengersOnBoard;
    public int PassengerCapacity => passengerCapacity;
    public int BoardedPassengers => boardedPassengers;

    private void Awake()
    {
        if (landingTaxiController == null)
        {
            landingTaxiController =
                GetComponent<AircraftSSLandingTaxiController>();
        }

        sightseeingEconomy = SightseeingFlightEconomy.Instance;
        takeoffController = GetComponent<AircraftSSTakeoffController>();
        departureTaxiController = GetComponent<AircraftSSDepartureTaxiController>();
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
        {
            if (!returnArrivalRaised &&
                takeoffController != null &&
                takeoffController.IsCompleted &&
                takeoffController.HasStartedReturnLanding &&
                landingTaxiController != null &&
                landingTaxiController.HasArrivedAtParking)
            {
                returnArrivalRaised = true;
                ReturnedToParking?.Invoke(this);
            }

            return;
        }

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

        if (state != TurnaroundState.Boarding || !boardingReady)
            return;

        // The boarding countdown starts only after every assigned passenger
        // has physically reached the aircraft and been hidden on board.
        boardingTimeRemaining -= Time.deltaTime;

        if (boardingTimeRemaining <= 0f)
            CompleteBoarding();
    }

    private void FindReservedParkingSpot()
    {
        SightseeingAircraftParkingSpot[] spots =
            FindObjectsByType<SightseeingAircraftParkingSpot>();

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
        boardingReady = !passengerManifestConfigured || expectedPassengers <= 0;
        BoardingStarted?.Invoke(this);

        if (logProgress)
        {
            Debug.Log(
                $"SIGHTSEEING BOARDING STARTED | {name} | " +
                $"Expected passengers: {(passengerManifestConfigured ? expectedPassengers.ToString() : "demand-based")}.",
                this);
        }
    }

    /// <summary>
    /// Supplies the real passenger manifest before boarding begins.
    /// This replaces the old abstract demand count with passengers who have
    /// actually checked in and are ready to board.
    /// </summary>
    public void ConfigurePassengerManifest(int passengerCount)
    {
        if (boardingCompleted)
            return;

        passengerManifestConfigured = true;
        expectedPassengers = Mathf.Clamp(passengerCount, 0, passengerCapacity);
        passengersOnBoard = expectedPassengers;
        boardedPassengers = 0;
        boardingReady = expectedPassengers == 0;
    }

    public void NotifyPassengerBoarded()
    {
        if (!boardingStarted || boardingCompleted || !passengerManifestConfigured)
            return;

        boardedPassengers = Mathf.Min(expectedPassengers, boardedPassengers + 1);
        passengersOnBoard = boardedPassengers;
        if (boardedPassengers >= expectedPassengers)
            boardingReady = true;
    }

    public void NotifyPassengerBoardingFailed()
    {
        if (!boardingStarted || boardingCompleted || !passengerManifestConfigured)
            return;

        expectedPassengers = Mathf.Max(boardedPassengers, expectedPassengers - 1);
        passengersOnBoard = expectedPassengers;
        if (boardedPassengers >= expectedPassengers)
            boardingReady = true;
    }

    private void CompleteBoarding()
    {
        if (boardingCompleted)
            return;

        // Keep backwards compatibility for scenes without a passenger
        // manager. Managed flights use the actual boarded passenger count.
        if (passengerManifestConfigured)
        {
            passengersOnBoard = boardedPassengers;
        }
        else if (sightseeingEconomy != null)
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

                    }
    }

    /// <summary>
    /// Keeps this aircraft and its parking reservation so it can operate
    /// the next scheduled sightseeing flight.
    /// </summary>
    public void CompleteReturnAndPrepareForReuse()
    {
        if (!returnArrivalRaised)
            return;

        readyForNextFlight = true;
        if (logProgress)
            Debug.Log($"SIGHTSEEING AIRCRAFT READY FOR REUSE | {name} remains at {reservedSpot}.", this);
    }

    public bool PrepareForNextFlight()
    {
        if (!readyForNextFlight || reservedSpot == null ||
            reservedSpot.ReservedAircraft != gameObject)
            return false;

        readyForNextFlight = false;
        boardingStarted = false;
        boardingCompleted = false;
        passengerManifestConfigured = false;
        boardingReady = false;
        expectedPassengers = 0;
        boardedPassengers = 0;
        passengersOnBoard = 0;
        boardingTimeRemaining = Mathf.Max(0.1f, boardingDurationSeconds);
        returnArrivalRaised = false;
        state = TurnaroundState.WaitingForParking;

        if (departureTaxiController != null)
            departureTaxiController.ResetForNextFlight();
        if (takeoffController != null)
            takeoffController.ResetForNextFlight();

        return true;
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
