
using UnityEngine;

/// <summary>
/// Charges landing and recurring parking fees for an aircraft.
/// Attach this component to the aircraft prefab.
/// </summary>
public class AircraftAirportFees : MonoBehaviour
{
    [Header("Landing Fee")]
    [Min(0)]
    [SerializeField] private int landingFee = 150;

    [Header("Parking Fee")]
    [Tooltip("Fee charged for each completed parking interval.")]
    [Min(0)]
    [SerializeField] private int parkingFee = 25;

    [Tooltip("Game-time minutes between parking charges.")]
    [Min(1)]
    [SerializeField] private int parkingIntervalMinutes = 30;

    [Header("References")]
    [SerializeField] private AirportEconomyManager economyManager;
    [SerializeField] private GameTimeManager gameTimeManager;

    [SerializeField]
    private AircraftCargoPaxLandingController cargoPaxLandingController;

    [SerializeField]
    private AircraftTrainingLandingController trainingLandingController;

    [SerializeField]
    private AircraftHobbyLandingController hobbyLandingController;

    [SerializeField]
    private AircraftTurnaroundController turnaroundController;

    private bool landingFeePaid;
    private bool wasParked;
    private long nextParkingChargeTime;

    private const long SecondsPerDay = 86400L;

    private void Awake()
    {
        if (economyManager == null)
        {
            economyManager =
                FindFirstObjectByType<AirportEconomyManager>();
        }

        if (gameTimeManager == null)
        {
            gameTimeManager =
                FindFirstObjectByType<GameTimeManager>();
        }

        if (cargoPaxLandingController == null)
        {
            cargoPaxLandingController =
                GetComponent<AircraftCargoPaxLandingController>();
        }

        if (trainingLandingController == null)
        {
            trainingLandingController =
                GetComponent<AircraftTrainingLandingController>();
        }

        if (hobbyLandingController == null)
        {
            hobbyLandingController =
                GetComponent<AircraftHobbyLandingController>();
        }

        if (turnaroundController == null)
        {
            turnaroundController =
                GetComponent<AircraftTurnaroundController>();
        }
    }

    private void Update()
    {
        if (economyManager == null ||
            gameTimeManager == null)
        {
            return;
        }

        ChargeLandingFeeIfDue();
        UpdateParkingFees();
    }

    private void ChargeLandingFeeIfDue()
    {
        if (landingFeePaid || !IsLandingComplete())
            return;

        // Mark as paid before adding revenue so this aircraft
        // can never charge its landing fee more than once.
        landingFeePaid = true;

        if (landingFee <= 0)
            return;

        economyManager.AddRevenue(
            landingFee,
            $"Landing fee - {gameObject.name}"
        );

        Debug.Log(
            $"AIRPORT INCOME | Landing fee: " +
            $"+${landingFee:N0} | Aircraft: {gameObject.name}",
            this
        );
    }

    private bool IsLandingComplete()
    {
        if (cargoPaxLandingController != null)
        {
            return cargoPaxLandingController.IsLandingComplete;
        }

        if (trainingLandingController != null)
        {
            return trainingLandingController.IsLandingComplete;
        }

        if (hobbyLandingController != null)
        {
            return hobbyLandingController.IsLandingComplete;
        }

        return false;
    }

    private void UpdateParkingFees()
    {
        bool isParked =
            turnaroundController != null &&
            turnaroundController.TurnaroundStarted;

        if (!isParked)
        {
            wasParked = false;
            return;
        }

        long currentGameTime = GetTotalGameSeconds();

        // Start a new parking period when the aircraft first
        // begins turnaround. The first charge is not immediate.
        if (!wasParked)
        {
            wasParked = true;

            nextParkingChargeTime =
                currentGameTime +
                Mathf.Max(1, parkingIntervalMinutes) * 60L;

            return;
        }

        if (currentGameTime < nextParkingChargeTime)
            return;

        long intervalSeconds =
            Mathf.Max(1, parkingIntervalMinutes) * 60L;

        // Catch up correctly if game time advances past more
        // than one parking interval in a single update.
        while (currentGameTime >= nextParkingChargeTime)
        {
            nextParkingChargeTime += intervalSeconds;

            if (parkingFee <= 0)
                continue;

            economyManager.AddRevenue(
                parkingFee,
                $"Parking fee - {gameObject.name}"
            );

            Debug.Log(
                $"AIRPORT INCOME | Parking fee: " +
                $"+${parkingFee:N0} | Aircraft: {gameObject.name}",
                this
            );
        }
    }

    private long GetTotalGameSeconds()
    {
        return gameTimeManager.CurrentDay * SecondsPerDay +
               gameTimeManager.GetGameTimeSeconds();
    }
}
