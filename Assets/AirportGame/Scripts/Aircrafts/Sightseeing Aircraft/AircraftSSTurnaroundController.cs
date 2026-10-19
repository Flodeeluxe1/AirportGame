
using UnityEngine;

/// <summary>
/// Manages the sightseeing aircraft's turnaround at its parking spot.
/// Waits for arrival, runs a boarding timer, and exposes boarding
/// completion for the departure taxi controller.
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

    [Header("Boarding")]
    [Min(0.1f)]
    [SerializeField]
    private float boardingDurationSeconds = 10f;

    [Header("Debug")]
    [SerializeField]
    private bool logProgress = true;

    [SerializeField]
    private TurnaroundState state = TurnaroundState.WaitingForParking;

    [SerializeField]
    private float boardingTimeRemaining;

    private SightseeingAircraftParkingSpot reservedSpot;
    private bool boardingStarted;
    private bool boardingCompleted;

    public TurnaroundState State => state;

    public bool IsBoarding => state == TurnaroundState.Boarding;

    public bool IsBoardingComplete => boardingCompleted;

    public float BoardingTimeRemaining =>
        Mathf.Max(0f, boardingTimeRemaining);

    public SightseeingAircraftParkingSpot ReservedSpot => reservedSpot;

    private void Awake()
    {
        if (landingTaxiController == null)
        {
            landingTaxiController =
                GetComponent<AircraftSSLandingTaxiController>();
        }

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

        // The spawner reserves the spot immediately after Instantiate.
        // Keep looking until that reservation becomes visible here.
        if (reservedSpot == null)
            FindReservedParkingSpot();

        if (landingTaxiController == null)
            return;

        // Do not begin boarding until the aircraft has completed
        // its landing taxi and reached its parking position.
        if (!landingTaxiController.HasArrivedAtParking)
            return;

        if (reservedSpot == null)
        {
            if (logProgress)
            {
                Debug.LogWarning(
                    $"SIGHTSEEING TURNAROUND WAITING | {name} has arrived " +
                    "at parking, but its reserved spot could not be found.",
                    this);
            }

            return;
        }

        if (reservedSpot.ReservedAircraft != gameObject)
        {
            if (logProgress)
            {
                Debug.LogWarning(
                    $"SIGHTSEEING TURNAROUND WAITING | {name} no longer " +
                    "owns its expected parking reservation.",
                    this);
            }

            return;
        }

        if (!boardingStarted)
            StartBoarding();

        if (state != TurnaroundState.Boarding)
            return;

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
            if (spot != null &&
                spot.ReservedAircraft == gameObject)
            {
                reservedSpot = spot;
                return;
            }
        }
    }

    private void StartBoarding()
    {
        boardingStarted = true;
        boardingTimeRemaining = Mathf.Max(0.1f, boardingDurationSeconds);
        state = TurnaroundState.Boarding;

        if (logProgress)
        {
            AircraftFlightIdentity identity =
                GetComponent<AircraftFlightIdentity>();

            string flightNumber =
                identity != null ? identity.FlightNumber : name;

            Debug.Log(
                $"SIGHTSEEING BOARDING STARTED | Flight: {flightNumber} | " +
                $"Parking: {reservedSpot.name} | " +
                $"Duration: {boardingTimeRemaining:F1} seconds",
                this);
        }
    }

    private void CompleteBoarding()
    {
        if (boardingCompleted)
            return;

        boardingCompleted = true;
        boardingTimeRemaining = 0f;
        state = TurnaroundState.ReadyForDeparture;

        if (logProgress)
        {
            AircraftFlightIdentity identity =
                GetComponent<AircraftFlightIdentity>();

            string flightNumber =
                identity != null ? identity.FlightNumber : name;

            Debug.Log(
                $"SIGHTSEEING BOARDING COMPLETE | Flight: {flightNumber} | " +
                "Ready for departure taxi.",
                this);
        }
    }

    private void OnDestroy()
    {
        if (reservedSpot != null)
            reservedSpot.Release(gameObject);
    }
}
