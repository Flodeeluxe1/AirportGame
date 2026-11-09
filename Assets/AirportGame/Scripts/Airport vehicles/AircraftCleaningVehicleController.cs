using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AirportGroundVehicleMovement))]
public class AircraftCleaningVehicleController : MonoBehaviour
{
    private enum CleaningState
    {
        Idle,
        DrivingToAircraft,
        PreparingAndEntering,
        CleaningPassengers,
        LeavingAircraft,
        DrivingHome
    }

    private const float PreparationSeconds = 120f;
    private const float SecondsPerPassenger = 6f;
    private const float LeavingSeconds = 120f;

    private static readonly List<AircraftCleaningVehicleController> activeVehicles =
        new List<AircraftCleaningVehicleController>();

    [Header("References")]
    [SerializeField] private AirportGroundVehicleMovement groundVehicleMovement;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Home Parking")]
    [SerializeField] private Transform homeParkingPoint;
    [SerializeField, Min(0.05f)] private float homeArrivalTolerance = 0.3f;

    [Header("Runtime Status")]
    [SerializeField] private string currentStatus = "Idle";
    [SerializeField] private string assignedAircraftName;
    [SerializeField] private int passengersToClean;
    [SerializeField] private float phaseElapsedGameSeconds;

    private CleaningState state = CleaningState.Idle;
    private bool parkedAtHome;
    private AircraftTurnaroundController assignedAircraft;
    private Transform aircraftStopPoint;

    public bool IsBusy => state != CleaningState.Idle || !parkedAtHome;
    public string CurrentStatus => currentStatus;
    public static int ActiveVehicleCount => activeVehicles.Count;

    private void Awake()
    {
        if (groundVehicleMovement == null)
            groundVehicleMovement = GetComponent<AirportGroundVehicleMovement>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (!activeVehicles.Contains(this))
            activeVehicles.Add(this);
    }

    private void OnDestroy()
    {
        activeVehicles.Remove(this);

        if (assignedAircraft != null)
            assignedAircraft.CancelExternalCleaning();
    }

    public void SetHomeParkingPoint(Transform parkingPoint)
    {
        homeParkingPoint = parkingPoint;
        parkedAtHome = homeParkingPoint != null && state == CleaningState.Idle;

        if (groundVehicleMovement == null)
            groundVehicleMovement = GetComponent<AirportGroundVehicleMovement>();

        if (groundVehicleMovement != null && homeParkingPoint != null)
            groundVehicleMovement.SetHomeArea(homeParkingPoint);
    }

    public static bool RequestCleaning(AircraftTurnaroundController aircraft)
    {
        if (aircraft == null)
            return false;

        AircraftCleaningVehicleController selected = null;
        float bestDistance = float.MaxValue;

        foreach (AircraftCleaningVehicleController vehicle in activeVehicles)
        {
            if (vehicle == null || vehicle.IsBusy ||
                vehicle.groundVehicleMovement == null ||
                vehicle.homeParkingPoint == null ||
                !vehicle.IsParkedAtHome())
            {
                continue;
            }

            float distance = (vehicle.transform.position -
                aircraft.transform.position).sqrMagnitude;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                selected = vehicle;
            }
        }

        return selected != null && selected.BeginCleaningJob(aircraft);
    }

    private bool BeginCleaningJob(AircraftTurnaroundController aircraft)
    {
        // A vehicle can only accept another aircraft after it has returned
        // to its assigned lot parking position and facing direction.
        if (state != CleaningState.Idle || aircraft == null || !parkedAtHome)
            return false;

        AircraftFlightIdentity identity =
            aircraft.GetComponent<AircraftFlightIdentity>();

        if (identity == null || !identity.IsInitialized ||
            !identity.HasActualGate)
        {
            Debug.LogWarning(
                $"{name}: Cannot dispatch because the aircraft's actual gate is unavailable.",
                this);
            return false;
        }

        AircraftGateRegistry registry =
            FindFirstObjectByType<AircraftGateRegistry>();

        if (registry == null ||
            !registry.TryGetGate(identity.ActualGateStableId, out AircraftGate gate) ||
            gate == null)
        {
            Debug.LogWarning(
                $"{name}: Could not resolve gate '{identity.ActualGateStableId}' for cleaning.",
                this);
            return false;
        }

        Transform stopPoint =
            gate.transform.Find("CleaningVehicleStopPoint");

        if (stopPoint == null)
        {
            Debug.LogWarning(
                $"{name}: Gate '{gate.name}' has no child named CleaningVehicleStopPoint.",
                gate);
            return false;
        }

        PassengerFlightRevenue passengerRevenue =
            aircraft.GetComponent<PassengerFlightRevenue>();

        int passengerCount =
            passengerRevenue != null ? passengerRevenue.IncomingPassengers : 0;

        parkedAtHome = false;
        assignedAircraft = aircraft;
        aircraftStopPoint = stopPoint;
        passengersToClean = Mathf.Max(0, passengerCount);
        phaseElapsedGameSeconds = 0f;
        assignedAircraftName = aircraft.name;

        bool routeStarted = groundVehicleMovement.MoveTo(
            stopPoint.position,
            stopPoint,
            gate.gameObject);

        if (!routeStarted)
        {
            ClearAssignment();
            Debug.LogWarning(
                $"{name}: Could not start route to aircraft {aircraft.name}. " +
                groundVehicleMovement.MovementStatus,
                this);
            return false;
        }

        state = CleaningState.DrivingToAircraft;
        currentStatus = "Driving to aircraft";

        Debug.Log(
            $"{name}: Dispatched to {aircraft.name} for {passengersToClean} passenger(s).",
            this);

        return true;
    }

    private void Update()
    {
        if (state == CleaningState.Idle)
            return;

        if (assignedAircraft == null && state != CleaningState.DrivingHome)
        {
            BeginReturnHome();
            return;
        }

        switch (state)
        {
            case CleaningState.DrivingToAircraft:
                UpdateDrivingToAircraft();
                break;

            case CleaningState.PreparingAndEntering:
                UpdateTimedPhase(PreparationSeconds, CleaningState.CleaningPassengers);
                break;

            case CleaningState.CleaningPassengers:
                UpdateTimedPhase(
                    passengersToClean * SecondsPerPassenger,
                    CleaningState.LeavingAircraft);
                break;

            case CleaningState.LeavingAircraft:
                UpdateLeavingAircraft();
                break;

            case CleaningState.DrivingHome:
                UpdateDrivingHome();
                break;
        }
    }

    private void UpdateDrivingToAircraft()
    {
        if (groundVehicleMovement.IsMoving)
            return;

        if (groundVehicleMovement.MovementStatus.StartsWith("Failed:"))
        {
            Debug.LogWarning(
                $"{name}: Cleaning route failed. {groundVehicleMovement.MovementStatus}",
                this);
            AircraftTurnaroundController failedAircraft = assignedAircraft;
            assignedAircraft = null;
            failedAircraft?.CancelExternalCleaning();
            BeginReturnHome();
            return;
        }

        if (groundVehicleMovement.MovementStatus != "Arrived")
            return;

        state = CleaningState.PreparingAndEntering;
        currentStatus = "Preparing and entering aircraft";
        phaseElapsedGameSeconds = 0f;
    }

    private void UpdateTimedPhase(float durationSeconds, CleaningState nextState)
    {
        float multiplier = gameTimeManager != null
            ? Mathf.Max(0f, gameTimeManager.CurrentSpeedMultiplier)
            : 1f;

        if (multiplier <= 0f)
            return;

        phaseElapsedGameSeconds += Time.unscaledDeltaTime * multiplier;

        if (phaseElapsedGameSeconds < durationSeconds)
            return;

        phaseElapsedGameSeconds = 0f;
        state = nextState;

        currentStatus = nextState == CleaningState.CleaningPassengers
            ? "Cleaning passenger cabin"
            : "Cleaning passengers completed";
    }

    private void UpdateLeavingAircraft()
    {
        float multiplier = gameTimeManager != null
            ? Mathf.Max(0f, gameTimeManager.CurrentSpeedMultiplier)
            : 1f;

        if (multiplier <= 0f)
            return;

        phaseElapsedGameSeconds += Time.unscaledDeltaTime * multiplier;

        if (phaseElapsedGameSeconds < LeavingSeconds)
            return;

        if (assignedAircraft != null)
            assignedAircraft.CompleteExternalCleaning();

        assignedAircraft = null;
        assignedAircraftName = string.Empty;
        BeginReturnHome();
    }

    private void BeginReturnHome()
    {
        if (homeParkingPoint == null || groundVehicleMovement == null)
        {
            state = CleaningState.Idle;
            currentStatus = "Idle - home parking unavailable";
            ClearAssignment();
            return;
        }

        bool routeStarted = groundVehicleMovement.MoveTo(
            homeParkingPoint.position,
            homeParkingPoint);

        if (!routeStarted)
        {
            state = CleaningState.Idle;
            currentStatus = "Idle - return route failed";
            ClearAssignment();
            Debug.LogWarning(
                $"{name}: Could not start return route to cleaning lot. " +
                groundVehicleMovement.MovementStatus,
                this);
            return;
        }

        state = CleaningState.DrivingHome;
        currentStatus = "Returning to cleaning lot";
    }

    private void UpdateDrivingHome()
    {
        if (groundVehicleMovement.IsMoving)
            return;

        if (groundVehicleMovement.MovementStatus == "Arrived")
        {
            transform.SetPositionAndRotation(
                homeParkingPoint.position,
                homeParkingPoint.rotation);

            state = CleaningState.Idle;
            parkedAtHome = true;
            currentStatus = "Idle";
            phaseElapsedGameSeconds = 0f;
            passengersToClean = 0;
            aircraftStopPoint = null;
            return;
        }

        if (groundVehicleMovement.MovementStatus.StartsWith("Failed:"))
        {
            state = CleaningState.Idle;
            currentStatus = "Idle - return route failed";
            ClearAssignment();
        }
    }

    private void ClearAssignment()
    {
        assignedAircraft = null;
        aircraftStopPoint = null;
        assignedAircraftName = string.Empty;
        passengersToClean = 0;
        phaseElapsedGameSeconds = 0f;
    }
}
