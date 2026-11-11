using UnityEngine;

/// <summary>
/// Central billing component for aircraft-related airport charges.
/// Attach to every aircraft prefab. Service systems calculate their own
/// prices and submit the resulting charge through RecordCharge.
/// </summary>
[DisallowMultipleComponent]
public class AircraftAirportFees : MonoBehaviour
{
    private AirportFeeCategory feeCategory = AirportFeeCategory.None;
    private AirportManagementController airportManagementController;

    [Header("Landing Fee")]
    [Min(0)]
    [SerializeField] private int landingFee = 20;

    [Header("Parking Fee")]
    [Min(0)]
    [SerializeField] private int parkingFee = 5;

    [Tooltip("Game-time minutes between parking charges.")]
    [Min(1)]
    [SerializeField] private int parkingIntervalMinutes = 10;

    [Header("Cleaning Fee By Aircraft Capacity")]
    [SerializeField, Min(0)] private int cleaningFeeUpTo10Passengers = 100;
    [SerializeField, Min(0)] private int cleaningFeeUpTo50Passengers = 200;
    [SerializeField, Min(0)] private int cleaningFeeUpTo100Passengers = 350;
    [SerializeField, Min(0)] private int cleaningFeeUpTo200Passengers = 500;
    [SerializeField, Min(0)] private int cleaningFeeUpTo300Passengers = 750;
    [SerializeField, Min(0)] private int cleaningFeeAbove300Passengers = 1000;

    [Header("References")]
    [SerializeField] private AirportEconomyManager economyManager;
    [SerializeField] private GameTimeManager gameTimeManager;
    [SerializeField] private AircraftStatus aircraftStatus;
    [SerializeField] private AircraftTurnaroundController turnaroundController;
    [SerializeField] private AircraftHobbyVisitController hobbyVisitController;
    [SerializeField] private AircraftHobbyLandingController hobbyLandingController;

    private bool landingFeePaid;
    private bool cleaningFeePaid;
    private bool wasParked;
    private long nextParkingChargeTime;
    private int activeParkingIntervalMinutes;
    private const long SecondsPerDay = 86400L;

    public int DefaultLandingFee => Mathf.Max(0, landingFee);
    public int DefaultParkingFee => Mathf.Max(0, parkingFee);
    public int DefaultParkingIntervalMinutes => Mathf.Max(1, parkingIntervalMinutes);

    public int LandingFee => GetEffectiveFees(out _, out _);

    public int ParkingFee
    {
        get
        {
            GetEffectiveFees(out int fee, out _);
            return fee;
        }
    }

    public int ParkingIntervalMinutes
    {
        get
        {
            GetEffectiveFees(out _, out int interval);
            return interval;
        }
    }

    public void SetFeeCategory(AirportFeeCategory category)
    {
        feeCategory = category;
    }

    public void CopyDefaultCleaningFeesTo(
        AirportManagementController.FeeSettings settings)
    {
        if (settings == null)
            return;

        settings.cleaningFeeUpTo10Passengers = cleaningFeeUpTo10Passengers;
        settings.cleaningFeeUpTo50Passengers = cleaningFeeUpTo50Passengers;
        settings.cleaningFeeUpTo100Passengers = cleaningFeeUpTo100Passengers;
        settings.cleaningFeeUpTo200Passengers = cleaningFeeUpTo200Passengers;
        settings.cleaningFeeUpTo300Passengers = cleaningFeeUpTo300Passengers;
        settings.cleaningFeeAbove300Passengers = cleaningFeeAbove300Passengers;
    }

    private int GetEffectiveFees(out int effectiveParkingFee, out int interval)
    {
        effectiveParkingFee = Mathf.Max(0, parkingFee);
        interval = Mathf.Max(1, parkingIntervalMinutes);

        if (airportManagementController == null)
            airportManagementController = AirportManagementController.Instance;

        if (airportManagementController != null &&
            airportManagementController.TryGetActiveAircraftFees(
                feeCategory,
                out int activeLandingFee,
                out int activeParkingFee,
                out int activeInterval))
        {
            effectiveParkingFee = activeParkingFee;
            interval = activeInterval;
            return activeLandingFee;
        }

        return Mathf.Max(0, landingFee);
    }

    private void Awake()
    {
        if (economyManager == null)
            economyManager = FindFirstObjectByType<AirportEconomyManager>();
        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        aircraftStatus = aircraftStatus != null ? aircraftStatus : GetComponent<AircraftStatus>();
        turnaroundController = turnaroundController != null ? turnaroundController : GetComponent<AircraftTurnaroundController>();
        hobbyVisitController = hobbyVisitController != null ? hobbyVisitController : GetComponent<AircraftHobbyVisitController>();
        hobbyLandingController = hobbyLandingController != null ? hobbyLandingController : GetComponent<AircraftHobbyLandingController>();
    }

    private void Update()
    {
        if (economyManager == null)
            economyManager = FindFirstObjectByType<AirportEconomyManager>();
        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();
        if (economyManager == null || gameTimeManager == null)
            return;

        if (airportManagementController == null)
            airportManagementController = AirportManagementController.Instance;

        ChargeLandingFeeIfDue();
        UpdateParkingFees();
    }

    private bool HasCompletedLanding()
    {
        return (aircraftStatus != null && aircraftStatus.HasCompletedLanding) ||
               (hobbyLandingController != null && hobbyLandingController.IsLandingComplete);
    }

    private bool IsEligibleForParkingFees()
    {
        if (hobbyVisitController != null)
            return hobbyVisitController.IsParkingFeeEligible;

        return (turnaroundController != null && turnaroundController.TurnaroundStarted) ||
               (aircraftStatus != null && aircraftStatus.IsParked);
    }

    private void ChargeLandingFeeIfDue()
    {
        if (landingFeePaid || !HasCompletedLanding())
            return;

        int activeLandingFee = GetEffectiveFees(out _, out _);
        if (activeLandingFee <= 0)
        {
            landingFeePaid = true;
            return;
        }

        if (RecordCharge(activeLandingFee, "Landing fee - " + name))
            landingFeePaid = true;
    }

    private void UpdateParkingFees()
    {
        if (!IsEligibleForParkingFees())
        {
            wasParked = false;
            return;
        }

        long now = GetTotalGameSeconds();
        GetEffectiveFees(out int activeParkingFee, out int activeIntervalMinutes);
        long intervalSeconds = activeIntervalMinutes * 60L;

        if (!wasParked)
        {
            wasParked = true;
            activeParkingIntervalMinutes = activeIntervalMinutes;
            nextParkingChargeTime = now + intervalSeconds;
            return;
        }

        if (activeParkingIntervalMinutes != activeIntervalMinutes)
        {
            activeParkingIntervalMinutes = activeIntervalMinutes;
            nextParkingChargeTime = now + intervalSeconds;
            return;
        }

        while (now >= nextParkingChargeTime)
        {
            if (hobbyVisitController != null &&
                nextParkingChargeTime >= hobbyVisitController.ParkingFeeEndGameSeconds)
                break;

            if (activeParkingFee > 0 && !RecordCharge(activeParkingFee, "Parking fee - " + name))
                return;

            nextParkingChargeTime += intervalSeconds;
        }
    }

    /// <summary>
    /// Records a calculated aircraft-related charge as airport revenue.
    /// The caller remains responsible for preventing duplicate requests.
    /// </summary>
    public bool RecordCharge(int amount, string description)
    {
        if (amount <= 0)
            return true;

        if (economyManager == null)
            economyManager = FindFirstObjectByType<AirportEconomyManager>();

        if (economyManager == null)
        {
            Debug.LogError(name + ": Cannot record airport charge; AirportEconomyManager is missing.", this);
            return false;
        }

        bool recorded = economyManager.AddRevenue(amount, description);
        if (!recorded)
            Debug.LogWarning(name + ": Airport charge could not be recorded: " + description + " ($" + amount + ").", this);

        return recorded;
    }

    /// <summary>
    /// Charges the aircraft's configured cleaning fee based on seat capacity.
    /// </summary>
    public bool ChargeCleaningFee(int seatCapacity)
    {
        if (cleaningFeePaid)
            return true;

        if (airportManagementController == null)
            airportManagementController = AirportManagementController.Instance;

        int fee;
        if (airportManagementController != null &&
            airportManagementController.TryGetActiveCleaningFee(
                seatCapacity, out int customFee))
        {
            fee = customFee;
        }
        else
        {
            fee = seatCapacity <= 10 ? cleaningFeeUpTo10Passengers :
                  seatCapacity <= 50 ? cleaningFeeUpTo50Passengers :
                  seatCapacity <= 100 ? cleaningFeeUpTo100Passengers :
                  seatCapacity <= 200 ? cleaningFeeUpTo200Passengers :
                  seatCapacity <= 300 ? cleaningFeeUpTo300Passengers :
                  cleaningFeeAbove300Passengers;
        }

        if (fee <= 0)
        {
            cleaningFeePaid = true;
            return true;
        }

        if (!RecordCharge(fee, "Aircraft cleaning (" + seatCapacity + " seats)"))
            return false;

        cleaningFeePaid = true;
        Debug.Log(name + ": Cleaning revenue of " + fee + " received for an aircraft with " + seatCapacity + " passenger seats.", this);
        return true;
    }

    private long GetTotalGameSeconds()
    {
        return (gameTimeManager.CurrentDay - 1L) * SecondsPerDay +
               gameTimeManager.GetGameTimeSeconds();
    }
}
