
using UnityEngine;

/// <summary>
/// Manages a visiting hobby aircraft's landing revenue,
/// grass parking fees, visit duration and departure request.
/// Attach only to hobby/VFR aircraft.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AircraftFlightIdentity))]
public class AircraftHobbyVisitController : MonoBehaviour
{
    [Header("Landing Fee")]
    [Min(0)]
    [SerializeField] public int landingFee = 20;

    [Header("Parking Fee")]
    [Min(0)]
    [SerializeField] public int parkingFee = 5;

    [Min(1)]
    [SerializeField] public int parkingIntervalMinutes = 10;

    [Header("Visit Duration")]
    [Min(1)]
    [SerializeField] public int minimumStayMinutes = 60;

    [Min(1)]
    [SerializeField] public int maximumStayMinutes = 180;

    [Header("Departure")]
    [Tooltip("Distance from the parking position before the spot is released.")]
    [Min(0.1f)]
    [SerializeField] private float parkingSpotReleaseDistance = 2f;

    [Header("Runtime Status")]
    [SerializeField] private bool landingFeeCharged;
    [SerializeField] private bool hasParked;
    [SerializeField] private bool departureRequested;
    [SerializeField] private float visitMinutesRemaining;
    [SerializeField] private string debugStatus = "Waiting for landing";

    public int LandingFee => landingFee;
    public int ParkingFee => parkingFee;

    private AircraftFlightIdentity flightIdentity;
    private AircraftHobbyLandingController landingController;
    private AircraftHobbyLandingTaxiController hobbyTaxiController;
    private HobbyAircraftParkingSpot reservedSpot;
    private AirportEconomyManager economyManager;
    private GameTimeManager gameTimeManager;

    private double parkedAtGameSeconds;
    private double departureAtGameSeconds;
    private double nextParkingFeeAtGameSeconds;

    public bool DepartureRequested => departureRequested;
    public bool HasParked => hasParked;
    public string DebugStatus => debugStatus;

    private void Awake()
    {
        flightIdentity = GetComponent<AircraftFlightIdentity>();
        landingController = GetComponent<AircraftHobbyLandingController>();
        hobbyTaxiController = GetComponent<AircraftHobbyLandingTaxiController>();

        economyManager =
            FindFirstObjectByType<AirportEconomyManager>();

        gameTimeManager =
            FindFirstObjectByType<GameTimeManager>();
    }

    private void Update()
    {
        if (flightIdentity == null ||
            !flightIdentity.IsInitialized ||
            !flightIdentity.IsHobbyFlight)
        {
            debugStatus = "Waiting for initialized hobby flight identity";
            return;
        }

        if (landingController == null)
            landingController = GetComponent<AircraftHobbyLandingController>();

        if (hobbyTaxiController == null)
            hobbyTaxiController = GetComponent<AircraftHobbyLandingTaxiController>();

        if (economyManager == null)
            economyManager = FindFirstObjectByType<AirportEconomyManager>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        ChargeLandingFeeIfNeeded();

        if (!hasParked)
        {
            TryBeginParkingVisit();
            return;
        }

        if (departureRequested)
        {
            ReleaseSpotAfterLeaving();
            return;
        }

        UpdateParkingVisit();
    }

    private void ChargeLandingFeeIfNeeded()
    {
        if (landingFeeCharged ||
            landingController == null ||
            !landingController.IsLandingComplete)
        {
            return;
        }

        if (economyManager == null)
        {
            debugStatus = "Landed; waiting for AirportEconomyManager";
            return;
        }

        if (landingFee > 0)
        {
            economyManager.AddRevenue(
                landingFee,
                $"Hobby aircraft landing fee ({flightIdentity.FlightNumber})"
            );
        }

        landingFeeCharged = true;

        debugStatus = landingFee > 0
            ? $"Landed; collected ${landingFee} landing fee"
            : "Landed; no landing fee charged";

        Debug.Log(
            $"{flightIdentity.FlightNumber}: hobby landing fee " +
            $"processed (${landingFee}).",
            this
        );
    }

    private void TryBeginParkingVisit()
    {
        if (landingController == null ||
            !landingController.IsLandingComplete ||
            hobbyTaxiController == null ||
            !hobbyTaxiController.HasArrivedAtParking)
        {
            return;
        }

        reservedSpot = FindReservedParkingSpot();

        if (reservedSpot == null)
        {
            debugStatus = "At parking stage, but no reserved grass spot was found";
            return;
        }

        if (gameTimeManager == null)
        {
            debugStatus = "Waiting for GameTimeManager before starting visit";
            return;
        }

        double now = GetCurrentGameSeconds();

        int minimumStay = Mathf.Max(1, minimumStayMinutes);
        int maximumStay = Mathf.Max(minimumStay, maximumStayMinutes);

        int stayMinutes = Random.Range(
            minimumStay,
            maximumStay + 1
        );

        parkedAtGameSeconds = now;

        departureAtGameSeconds =
            now + stayMinutes * 60.0;

        nextParkingFeeAtGameSeconds =
            now + Mathf.Max(1, parkingIntervalMinutes) * 60.0;

        hasParked = true;

        debugStatus =
            $"Parked for {stayMinutes} in-game minutes";

        Debug.Log(
            $"{flightIdentity.FlightNumber}: parked at " +
            $"{reservedSpot.name}. Planned stay: {stayMinutes} " +
            "in-game minutes.",
            this
        );
    }

    private void UpdateParkingVisit()
    {
        if (gameTimeManager == null)
        {
            debugStatus = "Visit paused: GameTimeManager is unavailable";
            return;
        }

        double now = GetCurrentGameSeconds();

        double totalVisitSeconds =
            now - parkedAtGameSeconds;

        visitMinutesRemaining = Mathf.Max(
            0f,
            (float)((departureAtGameSeconds - now) / 60.0)
        );

        // Charge only for completed parking intervals before departure.
        while (parkingFee > 0 &&
               nextParkingFeeAtGameSeconds <= now &&
               nextParkingFeeAtGameSeconds < departureAtGameSeconds)
        {
            if (economyManager == null)
            {
                economyManager =
                    FindFirstObjectByType<AirportEconomyManager>();

                if (economyManager == null)
                {
                    debugStatus = "Parking fee pending: economy manager unavailable";
                    return;
                }
            }

            economyManager.AddRevenue(
                parkingFee,
                $"Hobby aircraft parking fee ({flightIdentity.FlightNumber})"
            );

            nextParkingFeeAtGameSeconds +=
                Mathf.Max(1, parkingIntervalMinutes) * 60.0;
        }

        if (now >= departureAtGameSeconds)
        {
            departureRequested = true;

            debugStatus = "Visit complete; requesting departure taxi";

            Debug.Log(
                $"{flightIdentity.FlightNumber}: visit complete after " +
                $"{totalVisitSeconds / 60.0:F0} in-game minutes. " +
                "Departure requested.",
                this
            );

            return;
        }

        debugStatus =
            $"Parked; {visitMinutesRemaining:F0} in-game minutes remaining";
    }

    private HobbyAircraftParkingSpot FindReservedParkingSpot()
    {
        HobbyAircraftParkingSpot[] spots =
            FindObjectsByType<HobbyAircraftParkingSpot>(
                FindObjectsSortMode.None
            );

        foreach (HobbyAircraftParkingSpot spot in spots)
        {
            if (spot != null &&
                spot.ReservedAircraft == gameObject)
            {
                return spot;
            }
        }

        return null;
    }

    private void ReleaseSpotAfterLeaving()
    {
        if (reservedSpot == null)
            return;

        float distance = Vector3.Distance(
            transform.position,
            reservedSpot.ParkingWorldPosition
        );

        if (distance < parkingSpotReleaseDistance)
            return;

        reservedSpot.Release(gameObject);
        reservedSpot = null;

        debugStatus = "Aircraft left parking; spot released";
    }

    private double GetCurrentGameSeconds()
    {
        return (gameTimeManager.CurrentDay - 1) * 86400.0
               + gameTimeManager.GetGameTimeSeconds();
    }

    private void OnDestroy()
    {
        if (reservedSpot != null)
            reservedSpot.Release(gameObject);
    }
}
