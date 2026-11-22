
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AirportCargoVehicleController))]
public class AirportCargoIncomingBaggageJob : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    private enum JobState
    {
        Idle,
        OpeningCargoDoors,
        TransferringBaggage,
        ClosingCargoDoors,
        DrivingToCargoZone
    }

    private const float DoorOpeningSeconds = 120f;
    private const float SecondsPerBaggageItem = 30f;
    private const float DoorClosingSeconds = 120f;

    [Header("References")]
    [SerializeField] private GameObject cargoBoxPrefab;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Runtime Status")]
    [SerializeField] private string currentOperationId;
    [SerializeField] private string jobStatus = "Idle";
    [SerializeField] private int reservedPackageCount;
    [SerializeField] private int transferredPackageCount;
    [SerializeField] private float phaseElapsedGameSeconds;



    private AirportCargoVehicleController vehicle;
    private AircraftTurnaroundController aircraft;
    private AircraftLoadingManifest manifest;
    private JobState state = JobState.Idle;
    public bool IsBusy => state != JobState.Idle;

    private readonly List<string> reservedPackageIds =
        new List<string>();

    private CargoZone targetZone;
    private int nextPackageIndex;

    private void Awake()
    {
        vehicle = GetComponent<AirportCargoVehicleController>();

        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();
    }

    public bool BeginIncomingBaggageJob(
        AircraftTurnaroundController requestingAircraft,
        string operationId,
        int requestedCount)
    {
        if (state != JobState.Idle)
        {
            LogWarning(
                $"{name}: Cannot begin another baggage job; " +
                "this vehicle already has an active job.",
                this
            );

            return false;
        }

        if (requestingAircraft == null ||
            string.IsNullOrWhiteSpace(operationId))
        {
            return false;
        }

        if (cargoBoxPrefab == null)
        {
            LogError(
                $"{name}: Assign the CargoBox prefab to " +
                "AirportCargoIncomingBaggageJob.",
                this
            );

            return false;
        }

        if (cargoBoxPrefab.GetComponent<CargoPackage>() == null)
        {
            LogError(
                $"{name}: The assigned CargoBox prefab must have a " +
                "CargoPackage component on its root GameObject.",
                cargoBoxPrefab
            );

            return false;
        }

        aircraft = requestingAircraft;
        manifest = aircraft.LoadingManifest;
        currentOperationId = operationId;

        if (manifest == null || !manifest.IsInitialized)
        {
            LogError(
                $"{name}: The aircraft's loading manifest is unavailable.",
                aircraft
            );

            ResetJob();
            return false;
        }

        int capacity = Mathf.Min(
            vehicle.AvailableCargoCapacity,
            Mathf.Max(0, requestedCount)
        );

        if (capacity <= 0)
        {
            LogWarning(
                $"{name}: No available cargo capacity for " +
                $"{operationId}.",
                this
            );

            ResetJob();
            return false;
        }

        // Find a destination before taking baggage off the aircraft.
        if (!TrySelectCargoZone())
        {
            LogWarning(
                $"{name}: No suitable cargo zone was found for " +
                $"{operationId}.",
                this
            );

            ResetJob();
            return false;
        }

        reservedPackageIds.Clear();
        reservedPackageIds.AddRange(
            aircraft.ReserveIncomingBaggage(capacity)
        );

        reservedPackageCount = reservedPackageIds.Count;

        if (reservedPackageIds.Count == 0)
        {
            LogWarning(
                $"{name}: No incoming baggage could be reserved for " +
                $"{operationId}.",
                this
            );

            ResetJob();
            return false;
        }

        nextPackageIndex = 0;
        transferredPackageCount = 0;
        phaseElapsedGameSeconds = 0f;

        state = JobState.OpeningCargoDoors;
        jobStatus = "Opening aircraft cargo doors";

        Log(
            $"{name}: Arrived at aircraft for {operationId}. " +
            $"Preparing to unload {reservedPackageIds.Count} baggage item(s).",
            this
        );

        return true;
    }

    private bool TrySelectCargoZone()
    {
        List<CargoZone> zones =
            CargoZone.GetZonesSortedByDistance(transform.position);

        if (zones.Count == 0)
        {
            LogWarning(
                $"{name}: No CargoZone objects exist in the scene.",
                this
            );

            return false;
        }

        foreach (CargoZone zone in zones)
        {
            if (zone == null ||
                zone.AirportVehicleStopPoint == null ||
                zone.CargoAreaPoint == null)
            {
                continue;
            }

            if (!zone.TryReserveForFlight(currentOperationId))
                continue;

            targetZone = zone;
            return true;
        }

        targetZone = null;
        return false;
    }

    private void Update()
    {
        if (state == JobState.Idle)
            return;

        if (vehicle == null)
        {
            AbortJob("The cargo vehicle was destroyed.");
            return;
        }

        if (aircraft == null || manifest == null)
        {
            AbortJob("The aircraft or its loading manifest is unavailable.");
            return;
        }

        // These waits use the central in-game clock and pause at zero speed.
        float deltaGameSeconds = GameTimeManager.SimulationDeltaTime;

        switch (state)
        {
            case JobState.OpeningCargoDoors:
                UpdateOpeningDoors(deltaGameSeconds);
                break;

            case JobState.TransferringBaggage:
                UpdateBaggageTransfer(deltaGameSeconds);
                break;

            case JobState.ClosingCargoDoors:
                UpdateClosingDoors(deltaGameSeconds);
                break;

            case JobState.DrivingToCargoZone:
                UpdateDrivingToCargoZone();
                break;
        }
    }

    private void UpdateOpeningDoors(float deltaGameSeconds)
    {
        phaseElapsedGameSeconds += deltaGameSeconds;

        if (phaseElapsedGameSeconds < DoorOpeningSeconds)
            return;

        phaseElapsedGameSeconds = 0f;
        state = JobState.TransferringBaggage;
        jobStatus = "Transferring baggage";

        Log(
            $"{name}: Aircraft cargo doors are open. " +
            "Beginning baggage transfer.",
            this
        );
    }

    private void UpdateBaggageTransfer(float deltaGameSeconds)
    {
        phaseElapsedGameSeconds += deltaGameSeconds;

        while (phaseElapsedGameSeconds >= SecondsPerBaggageItem &&
               nextPackageIndex < reservedPackageIds.Count)
        {
            phaseElapsedGameSeconds -= SecondsPerBaggageItem;

            string packageId =
                reservedPackageIds[nextPackageIndex];

            // Keep the package in the aircraft manifest until it has
            // been registered at the destination cargo zone. If the route
            // fails, the reservation can be released and the package retried.
            nextPackageIndex++;
            transferredPackageCount++;

            Log(
                $"{name}: Moved baggage " +
                $"{transferredPackageCount}/{reservedPackageIds.Count} " +
                $"into the vehicle for {currentOperationId}.",
                this
            );
        }

        if (nextPackageIndex < reservedPackageIds.Count)
            return;

        phaseElapsedGameSeconds = 0f;
        state = JobState.ClosingCargoDoors;
        jobStatus = "Closing aircraft cargo doors";

        Log(
            $"{name}: All reserved baggage has been transferred. " +
            "Closing aircraft cargo doors.",
            this
        );
    }

    private void UpdateClosingDoors(float deltaGameSeconds)
    {
        phaseElapsedGameSeconds += deltaGameSeconds;

        if (phaseElapsedGameSeconds < DoorClosingSeconds)
            return;

        phaseElapsedGameSeconds = 0f;

        if (targetZone == null ||
            !vehicle.MoveToCargoZone(targetZone))
        {
            AbortJob(
                "Could not start the route to the selected cargo zone."
            );

            return;
        }

        state = JobState.DrivingToCargoZone;
        jobStatus = "Driving to cargo zone";

        Log(
            $"{name}: Cargo doors closed. Driving to cargo zone " +
            $"'{targetZone.name}'.",
            this
        );
    }

    private void UpdateDrivingToCargoZone()
    {
        if (vehicle.IsMoving)
            return;

        if (vehicle.MovementStatus.StartsWith("Failed:"))
        {
            AbortJob(
                "The route to the cargo zone failed: " +
                vehicle.MovementStatus
            );

            return;
        }

        if (vehicle.MovementStatus != "Arrived")
            return;

        DeliverPackagesToZone();
    }

    private void DeliverPackagesToZone()
    {
        if (targetZone == null ||
            targetZone.CargoAreaPoint == null)
        {
            AbortJob("The destination cargo zone is unavailable.");
            return;
        }

        int deliveredCount = 0;
        List<string> packagesToDeliver =
            new List<string>(reservedPackageIds);

        foreach (string packageId in packagesToDeliver)
        {
            GameObject box = Instantiate(
                cargoBoxPrefab,
                targetZone.CargoAreaPoint.position,
                targetZone.CargoAreaPoint.rotation
            );

            CargoPackage package = box.GetComponent<CargoPackage>();

            if (package == null)
            {
                Destroy(box);
                AbortJob(
                    $"Spawned cargo box for '{packageId}' has no CargoPackage component."
                );
                return;
            }

            package.Initialize(
                packageId,
                CargoDirection.Incoming,
                currentOperationId,
                targetZone
            );
            package.SetVisualPrefab(cargoBoxPrefab);

            if (!targetZone.RegisterPackage(package.Data))
            {
                Destroy(box);
                AbortJob(
                    $"Could not register baggage '{packageId}' in cargo zone '{targetZone.name}'."
                );
                return;
            }

            // Commit removal from the aircraft only after the physical
            // package exists in, and is registered with, its destination.
            if (!aircraft.ConfirmIncomingBaggageTransferred(packageId))
            {
                targetZone.UnregisterPackage(package.Data);
                Destroy(box);
                AbortJob(
                    $"The aircraft manifest rejected delivered baggage '{packageId}'."
                );
                return;
            }

            reservedPackageIds.Remove(packageId);
            deliveredCount++;
        }

        targetZone.ReleaseFlightReservation(currentOperationId);

        if (!manifest.HasIncomingBaggageRemaining &&
            !aircraft.CompleteIncomingBaggageTransfer())
        {
            AbortJob(
                "The aircraft did not accept completion after all incoming baggage reached a cargo zone."
            );
            return;
        }

        Log(
            $"{name}: Delivered {deliveredCount} incoming baggage " +
            $"package(s) to cargo zone '{targetZone.name}' for " +
            $"{currentOperationId}. Remaining on aircraft: " +
            $"{manifest.RemainingIncomingBaggage}.",
            this
        );

        string completedOperationId = currentOperationId;
        CargoZone completedZone = targetZone;

        reservedPackageIds.Clear();
        reservedPackageCount = 0;
        state = JobState.Idle;
        jobStatus = "Completed";

        if (AirportBaggageDispatcher.Instance != null)
        {
            AirportBaggageDispatcher.Instance
                .NotifyIncomingRequestCompletedAtCargoZone(
                    vehicle,
                    completedOperationId,
                    completedZone
                );
        }

        aircraft = null;
        manifest = null;
        targetZone = null;
        currentOperationId = string.Empty;
        nextPackageIndex = 0;
        phaseElapsedGameSeconds = 0f;
    }

    private void AbortJob(string reason)
    {
        ReleaseUntransferredReservations();

        LogWarning(
            $"{name}: Incoming baggage job failed. {reason}",
            this
        );

        if (AirportBaggageDispatcher.Instance != null)
        {
            AirportBaggageDispatcher.Instance.NotifyAssignmentFailed(
                vehicle,
                currentOperationId,
                reason
            );
        }

        ResetJob();
    }

    private void ReleaseUntransferredReservations()
    {
        if (aircraft != null)
        {
            foreach (string packageId in reservedPackageIds)
            {
                aircraft.ReleaseIncomingBaggageReservation(packageId);
            }
        }

        reservedPackageIds.Clear();
        reservedPackageCount = 0;
    }

    private void ResetJob()
    {
        if (targetZone != null)
            targetZone.ReleaseFlightReservation(currentOperationId);

        aircraft = null;
        manifest = null;
        targetZone = null;
        currentOperationId = string.Empty;
        reservedPackageIds.Clear();
        reservedPackageCount = 0;
        transferredPackageCount = 0;
        nextPackageIndex = 0;
        phaseElapsedGameSeconds = 0f;
        state = JobState.Idle;
        jobStatus = "Idle";
    }

    private float HorizontalDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
