using UnityEngine;

/// <summary>
/// Manages a visiting hobby aircraft's grass parking visit,
/// duration, parking reservation and departure sequence.
/// Airport charges are handled by AircraftAirportFees.
/// Attach only to hobby/VFR aircraft.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AircraftFlightIdentity))]
public class AircraftHobbyVisitController : MonoBehaviour
{
    [Header("Visit Duration")]
    [Min(1)]
    [SerializeField] public int minimumStayMinutes = 60;

    [Min(1)]
    [SerializeField] public int maximumStayMinutes = 180;

    [Header("Departure")]
    [Tooltip("Distance from the parking position before the spot is released.")]
    [Min(0.1f)]
    [SerializeField] private float parkingSpotReleaseDistance = 2f;

    [Tooltip("In-game minutes the aircraft remains parked after the pilot boards.")]
    [Min(0f)]
    [SerializeField] private float postBoardingTurnaroundMinutes = 15f;

    [Header("Pilot Visit")]
    [SerializeField] private GameObject pilotPrefab;
    [Min(0f)]
    [SerializeField] private float pilotSpawnDelayMinutes = 5f;
    [SerializeField] private Vector3 pilotSpawnLocalOffset = new Vector3(2f, 0f, 0f);

    [Header("Runtime Status")]
    [SerializeField] private bool hasParked;
    [SerializeField] private bool departureSequenceStarted;
    [SerializeField] private bool pilotHasBoarded;
    [SerializeField] private bool departureRequested;
    [SerializeField] private float visitMinutesRemaining;
    [SerializeField] private float turnaroundMinutesRemaining;
    [SerializeField] private string debugStatus = "Waiting for landing";

    private AircraftFlightIdentity flightIdentity;
    private AircraftHobbyLandingController landingController;
    private AircraftHobbyLandingTaxiController hobbyTaxiController;
    private HobbyAircraftParkingSpot reservedSpot;
    private GameTimeManager gameTimeManager;

    private double parkedAtGameSeconds;
    private double departureAtGameSeconds;
    private double pilotBoardedAtGameSeconds;
    private GameObject spawnedPilot;

    public bool DepartureRequested => departureRequested;
    public bool HasParked => hasParked;
    public bool IsParkingFeeEligible => hasParked && !departureRequested;
    public double ParkingFeeEndGameSeconds => departureAtGameSeconds;
    public string DebugStatus => debugStatus;

    private void Awake()
    {
        flightIdentity = GetComponent<AircraftFlightIdentity>();
        landingController = GetComponent<AircraftHobbyLandingController>();
        hobbyTaxiController = GetComponent<AircraftHobbyLandingTaxiController>();
        gameTimeManager = FindAnyObjectByType<GameTimeManager>();
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

        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();

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

        if (departureSequenceStarted)
        {
            UpdateDepartureSequence();
            return;
        }

        TrySpawnPilot();
        UpdateParkingVisit();
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
        int stayMinutes = Random.Range(minimumStay, maximumStay + 1);

        parkedAtGameSeconds = now;
        departureAtGameSeconds = now + stayMinutes * 60.0;
        hasParked = true;

        debugStatus = $"Parked for {stayMinutes} in-game minutes";

        Debug.Log(
            $"{flightIdentity.FlightNumber}: parked at {reservedSpot.name}. " +
            $"Planned stay: {stayMinutes} in-game minutes.",
            this);
    }

    private void UpdateParkingVisit()
    {
        if (gameTimeManager == null)
        {
            debugStatus = "Visit paused: GameTimeManager is unavailable";
            return;
        }

        double now = GetCurrentGameSeconds();
        double totalVisitSeconds = now - parkedAtGameSeconds;

        visitMinutesRemaining = Mathf.Max(
            0f,
            (float)((departureAtGameSeconds - now) / 60.0));

        if (now >= departureAtGameSeconds)
        {
            BeginDepartureSequence();

            Debug.Log(
                $"{flightIdentity.FlightNumber}: planned visit completed after " +
                $"{totalVisitSeconds / 60.0:F0} in-game minutes. Pilot departure sequence started.",
                this);
            return;
        }

        debugStatus = $"Parked; {visitMinutesRemaining:F0} in-game minutes remaining";
    }

    private void BeginDepartureSequence()
    {
        if (departureSequenceStarted)
            return;

        departureSequenceStarted = true;
        debugStatus = "Departure: calling pilot back to aircraft";

        if (spawnedPilot == null)
        {
            HandlePilotBoarded(null);
            return;
        }

        PilotDepartureController pilotDeparture =
            spawnedPilot.GetComponent<PilotDepartureController>();

        if (pilotDeparture == null ||
            !pilotDeparture.BeginDeparture(
                transform,
                pilotSpawnLocalOffset,
                () => HandlePilotBoarded(spawnedPilot)))
        {
            // Keep the aircraft departure timer safe if an older pilot prefab
            // without the departure controller is assigned.
            HandlePilotBoarded(spawnedPilot);
        }
    }

    private void UpdateDepartureSequence()
    {
        if (gameTimeManager == null)
        {
            debugStatus = "Departure paused: GameTimeManager is unavailable";
            return;
        }

        if (!pilotHasBoarded)
        {
            debugStatus = "Departure: pilot returning and completing walk-around";
            return;
        }

        double elapsedSinceBoarding =
            GetCurrentGameSeconds() - pilotBoardedAtGameSeconds;

        turnaroundMinutesRemaining = Mathf.Max(
            0f,
            (float)((postBoardingTurnaroundMinutes * 60.0 - elapsedSinceBoarding) / 60.0));

        if (elapsedSinceBoarding < postBoardingTurnaroundMinutes * 60.0)
        {
            debugStatus =
                $"Pilot aboard; aircraft turnaround {turnaroundMinutesRemaining:F1} in-game minutes";
            return;
        }

        departureRequested = true;
        debugStatus = "Turnaround complete; departure taxi requested";

        Debug.Log(
            $"{flightIdentity.FlightNumber}: pilot boarded and " +
            $"{postBoardingTurnaroundMinutes:F0}-minute turnaround completed. " +
            "Departure taxi released.",
            this);
    }

    private void TrySpawnPilot()
    {
        if (spawnedPilot != null ||
            pilotPrefab == null ||
            gameTimeManager == null)
        {
            return;
        }

        double elapsedGameSeconds =
            GetCurrentGameSeconds() - parkedAtGameSeconds;

        if (elapsedGameSeconds < pilotSpawnDelayMinutes * 60.0)
            return;

        Vector3 spawnPosition =
            transform.TransformPoint(pilotSpawnLocalOffset) + Vector3.up * 0.323f;

        Vector3 directionToAircraft = transform.position - spawnPosition;
        directionToAircraft.y = 0f;

        Quaternion spawnRotation =
            directionToAircraft.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(directionToAircraft.normalized, Vector3.up)
                : transform.rotation;

        spawnedPilot = Instantiate(pilotPrefab, spawnPosition, spawnRotation);

        UtilityNeeds needs = spawnedPilot.GetComponent<UtilityNeeds>();
        if (needs != null)
            needs.ResetForNewVisit();

        Debug.Log(
            $"{flightIdentity.FlightNumber}: pilot spawned " +
            $"{pilotSpawnDelayMinutes:F1} in-game minutes after parking.",
            spawnedPilot);
    }

    private void HandlePilotBoarded(GameObject pilot)
    {
        if (pilotHasBoarded)
            return;

        pilotHasBoarded = true;
        pilotBoardedAtGameSeconds = GetCurrentGameSeconds();
        turnaroundMinutesRemaining = postBoardingTurnaroundMinutes;

        if (pilot != null)
        {
            Debug.Log(
                $"{flightIdentity.FlightNumber}: pilot completed walk-around and boarded.",
                pilot);
            Destroy(pilot);
        }

        if (spawnedPilot == pilot || pilot == null)
            spawnedPilot = null;

        debugStatus = "Pilot aboard; beginning 15-minute turnaround";
    }

    private HobbyAircraftParkingSpot FindReservedParkingSpot()
    {
        HobbyAircraftParkingSpot[] spots =
            FindObjectsByType<HobbyAircraftParkingSpot>(FindObjectsSortMode.None);

        foreach (HobbyAircraftParkingSpot spot in spots)
        {
            if (spot != null && spot.ReservedAircraft == gameObject)
                return spot;
        }

        return null;
    }

    private void ReleaseSpotAfterLeaving()
    {
        if (reservedSpot == null)
            return;

        float distance = Vector3.Distance(
            transform.position,
            reservedSpot.ParkingWorldPosition);

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
        if (spawnedPilot != null)
            Destroy(spawnedPilot);

        if (reservedSpot != null)
            reservedSpot.Release(gameObject);
    }

    private void OnValidate()
    {
        postBoardingTurnaroundMinutes = Mathf.Max(0f, postBoardingTurnaroundMinutes);
    }
}