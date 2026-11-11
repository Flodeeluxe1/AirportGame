using System;
using System.Collections;
using System.IO;
using UnityEngine;

public enum AirportFeeCategory
{
    None,
    Hobby,
    FlightSchool,
    Sightseeing,
    Cargo,
    Passenger
}

/// <summary>
/// Central authority for airport operating permissions and airport fee settings.
/// A valid CEO Office allows new aircraft arrivals. A valid CFO Office unlocks
/// custom fees; otherwise aircraft use their prefab default fees.
/// </summary>
[DisallowMultipleComponent]
public class AirportManagementController : MonoBehaviour
{
    public static AirportManagementController Instance { get; private set; }

    [Serializable]
    public class AircraftFeeProfile
    {
        [Min(0)] public int landingFee;
        [Min(0)] public int parkingFee;
        [Min(1)] public int parkingIntervalMinutes = 15;
    }

    [Serializable]
    public class FeeSettings
    {
        public AircraftFeeProfile hobby = new AircraftFeeProfile();
        public AircraftFeeProfile flightSchool = new AircraftFeeProfile();
        public AircraftFeeProfile sightseeing = new AircraftFeeProfile();
        public AircraftFeeProfile cargo = new AircraftFeeProfile();
        public AircraftFeeProfile passenger = new AircraftFeeProfile();

        [Min(0)] public int cleaningFeeUpTo10Passengers = 100;
        [Min(0)] public int cleaningFeeUpTo50Passengers = 200;
        [Min(0)] public int cleaningFeeUpTo100Passengers = 350;
        [Min(0)] public int cleaningFeeUpTo200Passengers = 500;
        [Min(0)] public int cleaningFeeUpTo300Passengers = 750;
        [Min(0)] public int cleaningFeeAbove300Passengers = 1000;
    }

    [Serializable]
    private class FeeSettingsSaveData
    {
        public int version = 1;
        public FeeSettings settings;
    }

    [Header("References")]
    [SerializeField] private RoomPurposeController roomPurposeController;
    [SerializeField] private RoomDetectionController roomDetectionController;

    public event Action StateChanged;

    [Header("Current Office Status")]
    [SerializeField] private bool hasValidCEOOffice;
    [SerializeField] private bool hasValidCFOOffice;

    [Header("Custom Airport Fees")]
    [SerializeField] private FeeSettings customFeeSettings = new FeeSettings();

    private FeeSettings defaultFeeSettings;
    private Coroutine pendingRefresh;

    private const string FeeSettingsFileName = "airport_fee_settings.json";

    private string FeeSettingsFilePath =>
        Path.Combine(Application.persistentDataPath, FeeSettingsFileName);

    public bool IsAirportOperational => hasValidCEOOffice;
    public bool CanAdjustFees => hasValidCFOOffice;
    public bool HasValidCEOOffice => hasValidCEOOffice;
    public bool HasValidCFOOffice => hasValidCFOOffice;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToChanges();
        QueueStatusRefresh();
    }

    private void Start()
    {
        InitializeFeeSettings();
        RefreshOfficeStatus();
    }

    private void OnDisable()
    {
        UnsubscribeFromChanges();

        if (pendingRefresh != null)
        {
            StopCoroutine(pendingRefresh);
            pendingRefresh = null;
        }

        if (Instance == this)
            Instance = null;
    }

    private void ResolveReferences()
    {
        if (roomPurposeController == null)
        {
            roomPurposeController = GetComponent<RoomPurposeController>();

            if (roomPurposeController == null)
                roomPurposeController =
                    FindFirstObjectByType<RoomPurposeController>();
        }

        if (roomDetectionController == null)
            roomDetectionController =
                FindFirstObjectByType<RoomDetectionController>();
    }

    private void SubscribeToChanges()
    {
        if (roomPurposeController != null)
            roomPurposeController.AssignmentsChanged += QueueStatusRefresh;

        if (roomDetectionController != null)
            roomDetectionController.RoomsChanged += QueueStatusRefresh;

        PlacedBuilding.BuildingChanged += HandleBuildingChanged;
    }

    private void UnsubscribeFromChanges()
    {
        if (roomPurposeController != null)
            roomPurposeController.AssignmentsChanged -= QueueStatusRefresh;

        if (roomDetectionController != null)
            roomDetectionController.RoomsChanged -= QueueStatusRefresh;

        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
    }

    private void HandleBuildingChanged(PlacedBuilding building)
    {
        QueueStatusRefresh();
    }

    private void QueueStatusRefresh()
    {
        if (!isActiveAndEnabled || pendingRefresh != null)
            return;

        pendingRefresh = StartCoroutine(RefreshAfterChanges());
    }

    private IEnumerator RefreshAfterChanges()
    {
        yield return null;
        pendingRefresh = null;
        RefreshOfficeStatus();
    }

    [ContextMenu("Refresh Office Status")]
    public void RefreshOfficeStatus()
    {
        ResolveReferences();

        bool newCEOStatus =
            roomPurposeController != null &&
            roomPurposeController.HasValidAssignedOffice(RoomPurpose.CEOOffice);

        bool newCFOStatus =
            roomPurposeController != null &&
            roomPurposeController.HasValidAssignedOffice(RoomPurpose.CFOOffice);

        if (newCEOStatus == hasValidCEOOffice &&
            newCFOStatus == hasValidCFOOffice)
        {
            return;
        }

        bool previousCEOStatus = hasValidCEOOffice;
        bool previousCFOStatus = hasValidCFOOffice;

        hasValidCEOOffice = newCEOStatus;
        hasValidCFOOffice = newCFOStatus;

        if (previousCEOStatus != hasValidCEOOffice)
        {
            Debug.Log(
                hasValidCEOOffice
                    ? "AirportManagementController: Valid CEO Office found. New aircraft arrivals are enabled."
                    : "AirportManagementController: Valid CEO Office missing. New aircraft arrivals are disabled.",
                this);
        }

        if (previousCFOStatus != hasValidCFOOffice)
        {
            Debug.Log(
                hasValidCFOOffice
                    ? "AirportManagementController: Valid CFO Office found. Custom airport fees are available."
                    : "AirportManagementController: Valid CFO Office missing. Default airport fees must be used.",
                this);
        }

        StateChanged?.Invoke();
    }

    private void InitializeFeeSettings()
    {
        defaultFeeSettings = new FeeSettings
        {
            hobby = new AircraftFeeProfile
            {
                landingFee = 20,
                parkingFee = 5,
                parkingIntervalMinutes = 10
            },
            flightSchool = new AircraftFeeProfile
            {
                landingFee = 20,
                parkingFee = 5,
                parkingIntervalMinutes = 10
            },
            sightseeing = new AircraftFeeProfile
            {
                landingFee = 250,
                parkingFee = 100,
                parkingIntervalMinutes = 15
            },
            cargo = new AircraftFeeProfile
            {
                landingFee = 250,
                parkingFee = 100,
                parkingIntervalMinutes = 15
            },
            passenger = new AircraftFeeProfile
            {
                landingFee = 250,
                parkingFee = 100,
                parkingIntervalMinutes = 15
            }
        };

        HobbyAircraftTrafficSpawner hobbySpawner =
            FindFirstObjectByType<HobbyAircraftTrafficSpawner>();
        if (hobbySpawner != null)
            CopyPrefabDefaults(hobbySpawner.HobbyAircraftPrefab, defaultFeeSettings.hobby);

        FlightSchoolManager flightSchoolManager =
            FindFirstObjectByType<FlightSchoolManager>();
        if (flightSchoolManager != null)
            CopyPrefabDefaults(
                flightSchoolManager.TrainingAircraftPrefab,
                defaultFeeSettings.flightSchool);

        SightseeingAircraftSpawner sightseeingSpawner =
            FindFirstObjectByType<SightseeingAircraftSpawner>();
        if (sightseeingSpawner != null)
            CopyPrefabDefaults(
                sightseeingSpawner.SightseeingAircraftPrefab,
                defaultFeeSettings.sightseeing);

        ScheduledAircraftSpawner scheduledSpawner =
            FindFirstObjectByType<ScheduledAircraftSpawner>();

        if (scheduledSpawner != null)
        {
            CopyPrefabDefaults(
                scheduledSpawner.CargoAircraftPrefab,
                defaultFeeSettings.cargo);

            CopyPrefabDefaults(
                scheduledSpawner.PassengerAircraftPrefab,
                defaultFeeSettings.passenger);
        }

        AircraftAirportFees cleaningDefaults = FindCleaningDefaults(
            hobbySpawner,
            sightseeingSpawner,
            scheduledSpawner);

        if (cleaningDefaults != null)
            cleaningDefaults.CopyDefaultCleaningFeesTo(defaultFeeSettings);

        if (File.Exists(FeeSettingsFilePath))
        {
            try
            {
                FeeSettingsSaveData saved =
                    JsonUtility.FromJson<FeeSettingsSaveData>(
                        File.ReadAllText(FeeSettingsFilePath));

                if (saved != null && saved.settings != null)
                {
                    customFeeSettings = SanitizeFeeSettings(saved.settings);
                    return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "AirportManagementController: Could not load saved airport fees. " +
                    exception.Message,
                    this);
            }
        }

        customFeeSettings = CloneFeeSettings(defaultFeeSettings);
    }

    private AircraftAirportFees FindCleaningDefaults(
        HobbyAircraftTrafficSpawner hobbySpawner,
        SightseeingAircraftSpawner sightseeingSpawner,
        ScheduledAircraftSpawner scheduledSpawner)
    {
        AircraftAirportFees fees = GetPrefabFees(
            hobbySpawner != null ? hobbySpawner.HobbyAircraftPrefab : null);
        if (fees != null)
            return fees;

        fees = GetPrefabFees(
            sightseeingSpawner != null
                ? sightseeingSpawner.SightseeingAircraftPrefab
                : null);
        if (fees != null)
            return fees;

        fees = GetPrefabFees(
            scheduledSpawner != null ? scheduledSpawner.CargoAircraftPrefab : null);
        if (fees != null)
            return fees;

        return GetPrefabFees(
            scheduledSpawner != null
                ? scheduledSpawner.PassengerAircraftPrefab
                : null);
    }

    private void CopyPrefabDefaults(
        GameObject prefab,
        AircraftFeeProfile destination)
    {
        AircraftAirportFees fees = GetPrefabFees(prefab);
        if (fees == null || destination == null)
            return;

        destination.landingFee = fees.DefaultLandingFee;
        destination.parkingFee = fees.DefaultParkingFee;
        destination.parkingIntervalMinutes = fees.DefaultParkingIntervalMinutes;
    }

    private AircraftAirportFees GetPrefabFees(GameObject prefab)
    {
        return prefab != null
            ? prefab.GetComponent<AircraftAirportFees>()
            : null;
    }

    public FeeSettings GetFeeSettingsForDisplay()
    {
        FeeSettings source = CanAdjustFees
            ? customFeeSettings
            : defaultFeeSettings;

        return CloneFeeSettings(source);
    }

    public bool SaveCustomFeeSettings(FeeSettings settings)
    {
        if (!CanAdjustFees || settings == null)
            return false;

        FeeSettings candidate = SanitizeFeeSettings(settings);

        try
        {
            FeeSettingsSaveData saveData = new FeeSettingsSaveData
            {
                version = 1,
                settings = candidate
            };

            File.WriteAllText(
                FeeSettingsFilePath,
                JsonUtility.ToJson(saveData, true));

            customFeeSettings = candidate;
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "AirportManagementController: Could not save airport fees. " +
                exception.Message,
                this);
            return false;
        }
    }

    public bool TryGetActiveAircraftFees(
        AirportFeeCategory category,
        out int activeLandingFee,
        out int activeParkingFee,
        out int activeParkingIntervalMinutes)
    {
        activeLandingFee = 0;
        activeParkingFee = 0;
        activeParkingIntervalMinutes = 1;

        if (!CanAdjustFees || category == AirportFeeCategory.None)
            return false;

        AircraftFeeProfile profile =
            GetProfile(customFeeSettings, category);

        if (profile == null)
            return false;

        activeLandingFee = Mathf.Max(0, profile.landingFee);
        activeParkingFee = Mathf.Max(0, profile.parkingFee);
        activeParkingIntervalMinutes =
            Mathf.Max(1, profile.parkingIntervalMinutes);

        return true;
    }

    public bool TryGetActiveCleaningFee(
        int seatCapacity,
        out int activeCleaningFee)
    {
        activeCleaningFee = 0;

        if (!CanAdjustFees || customFeeSettings == null)
            return false;

        activeCleaningFee =
            GetCleaningFee(customFeeSettings, seatCapacity);

        return true;
    }

    private static AircraftFeeProfile GetProfile(
        FeeSettings settings,
        AirportFeeCategory category)
    {
        if (settings == null)
            return null;

        switch (category)
        {
            case AirportFeeCategory.Hobby:
                return settings.hobby;
            case AirportFeeCategory.FlightSchool:
                return settings.flightSchool;
            case AirportFeeCategory.Sightseeing:
                return settings.sightseeing;
            case AirportFeeCategory.Cargo:
                return settings.cargo;
            case AirportFeeCategory.Passenger:
                return settings.passenger;
            default:
                return null;
        }
    }

    private static int GetCleaningFee(
        FeeSettings settings,
        int seatCapacity)
    {
        if (seatCapacity <= 10)
            return Mathf.Max(0, settings.cleaningFeeUpTo10Passengers);
        if (seatCapacity <= 50)
            return Mathf.Max(0, settings.cleaningFeeUpTo50Passengers);
        if (seatCapacity <= 100)
            return Mathf.Max(0, settings.cleaningFeeUpTo100Passengers);
        if (seatCapacity <= 200)
            return Mathf.Max(0, settings.cleaningFeeUpTo200Passengers);
        if (seatCapacity <= 300)
            return Mathf.Max(0, settings.cleaningFeeUpTo300Passengers);

        return Mathf.Max(0, settings.cleaningFeeAbove300Passengers);
    }

    private static FeeSettings CloneFeeSettings(FeeSettings source)
    {
        if (source == null)
            return new FeeSettings();

        return JsonUtility.FromJson<FeeSettings>(
            JsonUtility.ToJson(source));
    }

    private static FeeSettings SanitizeFeeSettings(FeeSettings source)
    {
        FeeSettings result = CloneFeeSettings(source);

        result.hobby ??= new AircraftFeeProfile();
        result.flightSchool ??= new AircraftFeeProfile();
        result.sightseeing ??= new AircraftFeeProfile();
        result.cargo ??= new AircraftFeeProfile();
        result.passenger ??= new AircraftFeeProfile();

        SanitizeProfile(result.hobby);
        SanitizeProfile(result.flightSchool);
        SanitizeProfile(result.sightseeing);
        SanitizeProfile(result.cargo);
        SanitizeProfile(result.passenger);

        result.cleaningFeeUpTo10Passengers =
            Mathf.Max(0, result.cleaningFeeUpTo10Passengers);
        result.cleaningFeeUpTo50Passengers =
            Mathf.Max(0, result.cleaningFeeUpTo50Passengers);
        result.cleaningFeeUpTo100Passengers =
            Mathf.Max(0, result.cleaningFeeUpTo100Passengers);
        result.cleaningFeeUpTo200Passengers =
            Mathf.Max(0, result.cleaningFeeUpTo200Passengers);
        result.cleaningFeeUpTo300Passengers =
            Mathf.Max(0, result.cleaningFeeUpTo300Passengers);
        result.cleaningFeeAbove300Passengers =
            Mathf.Max(0, result.cleaningFeeAbove300Passengers);

        return result;
    }

    private static void SanitizeProfile(AircraftFeeProfile profile)
    {
        profile.landingFee = Mathf.Max(0, profile.landingFee);
        profile.parkingFee = Mathf.Max(0, profile.parkingFee);
        profile.parkingIntervalMinutes =
            Mathf.Max(1, profile.parkingIntervalMinutes);
    }
}
