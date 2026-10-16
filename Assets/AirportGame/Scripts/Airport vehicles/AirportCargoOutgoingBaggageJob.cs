
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AirportCargoVehicleController))]
public class AirportCargoOutgoingBaggageJob : MonoBehaviour
{
    private enum JobState
    {
        Idle,
        DrivingToCargoZone,
        DrivingToAircraftGate,
        OpeningCargoDoors,
        LoadingBaggage,
        ClosingCargoDoors
    }

    private const float DoorOpeningSeconds = 120f;
    private const float SecondsPerBaggageItem = 30f;
    private const float DoorClosingSeconds = 120f;

    [Header("Time")]
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Runtime Status")]
    [SerializeField] private string jobStatus = "Idle";
    [SerializeField] private string currentOperationId;
    [SerializeField] private int reservedPackageCount;
    [SerializeField] private int loadedPackageCount;

    [Header("Transfer Timing")]
    [SerializeField] private float phaseElapsedGameSeconds;

    private AirportCargoVehicleController vehicle;
    private AircraftTurnaroundController aircraft;
    private AircraftLoadingManifest manifest;
    private AircraftGate actualGate;
    private CargoZone cargoZone;

    private JobState state = JobState.Idle;

    private readonly List<string> reservedPackageIds =
        new List<string>();

    public bool IsBusy => state != JobState.Idle;
    public string JobStatus => jobStatus;

    private void Awake()
    {
        vehicle = GetComponent<AirportCargoVehicleController>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();
    }

    public bool BeginOutgoingBaggageJob(
        AircraftTurnaroundController requestingAircraft,
        AircraftGate assignedActualGate,
        string operationId,
        CargoZone startingZone = null)
    {
        if (IsBusy)
        {
            Debug.LogWarning(
                $"{name}: Cannot start outgoing baggage. " +
                "Another outgoing job is already active.",
                this
            );

            return false;
        }

        if (vehicle == null ||
            requestingAircraft == null ||
            assignedActualGate == null ||
            string.IsNullOrWhiteSpace(operationId))
        {
            Debug.LogWarning(
                $"{name}: Outgoing baggage job has missing references.",
                this
            );

            return false;
        }

        if (vehicle.IsMoving || vehicle.CarriedPackageCount > 0)
        {
            Debug.LogWarning(
                $"{name}: Vehicle must be stationary and empty " +
                "before starting outgoing baggage loading.",
                this
            );

            return false;
        }

        AircraftFlightIdentity identity =
            requestingAircraft.GetComponent<AircraftFlightIdentity>();

        if (identity == null ||
            !identity.IsInitialized ||
            identity.OperationId != operationId ||
            !identity.HasActualGate ||
            identity.ActualGateStableId != assignedActualGate.StableId)
        {
            Debug.LogWarning(
                $"{name}: Outgoing job rejected because the aircraft " +
                "identity or actual gate does not match.",
                this
            );

            return false;
        }

        AircraftLoadingManifest requestedManifest =
            requestingAircraft.LoadingManifest;

        if (requestedManifest == null ||
            !requestedManifest.IsInitialized)
        {
            Debug.LogWarning(
                $"{name}: Aircraft loading manifest is unavailable.",
                this
            );

            return false;
        }

        if (!requestedManifest.HasOutgoingBaggageRemaining)
        {
            Debug.Log(
                $"{name}: No outgoing baggage remains for {operationId}.",
                this
            );

            return false;
        }

        aircraft = requestingAircraft;
        manifest = requestedManifest;
        actualGate = assignedActualGate;
        currentOperationId = operationId;

        reservedPackageIds.Clear();
        reservedPackageCount = 0;
        loadedPackageCount = 0;
        phaseElapsedGameSeconds = 0f;

        if (startingZone != null)
        {
            // Chained task: the incoming job has just left this
            // vehicle at the supplied cargo zone.
            if (startingZone.AirportVehicleStopPoint == null ||
                startingZone.CargoAreaPoint == null)
            {
                Debug.LogWarning(
                    $"{name}: The supplied starting cargo zone " +
                    "is missing a required stop or cargo-area point.",
                    this
                );

                ResetJob();
                return false;
            }

            cargoZone = startingZone;
            state = JobState.DrivingToCargoZone;
            jobStatus = "Collecting outgoing baggage at cargo zone";

            Debug.Log(
                $"{name}: Starting outgoing baggage for {operationId} " +
                $"directly at cargo zone '{cargoZone.name}'. " +
                $"Destination gate: {actualGate.StableId}.",
                this
            );

            CollectAndReservePackages();

            // After initialization, failures are handled by AbortJob
            // and the dispatcher callback.
            return true;
        }

        // Standalone task: find a cargo zone and drive there first.
        if (!TryDriveToCargoZone())
        {
            ResetJob();

            Debug.LogWarning(
                $"{name}: No reachable cargo zone was found for " +
                $"{operationId}.",
                this
            );

            return false;
        }

        state = JobState.DrivingToCargoZone;
        jobStatus = "Driving to cargo zone";

        Debug.Log(
            $"{name}: Outgoing baggage job started for {operationId}. " +
            $"Destination gate: {actualGate.StableId}.",
            this
        );

        return true;
    }

    private bool TryDriveToCargoZone()
    {
        CargoZone[] zones =
            FindObjectsByType<CargoZone>(FindObjectsSortMode.None);

        System.Array.Sort(
            zones,
            (a, b) =>
            {
                if (a == null) return 1;
                if (b == null) return -1;

                float distanceA = HorizontalDistanceSquared(
                    transform.position,
                    a.AirportVehicleStopPoint != null
                        ? a.AirportVehicleStopPoint.position
                        : a.transform.position
                );

                float distanceB = HorizontalDistanceSquared(
                    transform.position,
                    b.AirportVehicleStopPoint != null
                        ? b.AirportVehicleStopPoint.position
                        : b.transform.position
                );

                return distanceA.CompareTo(distanceB);
            }
        );

        foreach (CargoZone zone in zones)
        {
            if (zone == null ||
                zone.AirportVehicleStopPoint == null ||
                zone.CargoAreaPoint == null)
            {
                continue;
            }

            if (vehicle.MoveToCargoZone(zone))
            {
                cargoZone = zone;
                return true;
            }
        }

        cargoZone = null;
        return false;
    }

    private void Update()
    {
        if (state == JobState.Idle || vehicle == null)
            return;

        // These phases are driven by in-game time, not vehicle movement.
        if (state == JobState.OpeningCargoDoors ||
            state == JobState.LoadingBaggage ||
            state == JobState.ClosingCargoDoors)
        {
            UpdateTimedLoadingSequence();
            return;
        }

        if (vehicle.IsMoving)
            return;

        if (vehicle.MovementStatus.StartsWith("Failed:"))
        {
            AbortJob(
                "Vehicle navigation failed: " +
                vehicle.MovementStatus
            );

            return;
        }

        if (vehicle.MovementStatus != "Arrived")
            return;

        switch (state)
        {
            case JobState.DrivingToCargoZone:
                CollectAndReservePackages();
                break;

            case JobState.DrivingToAircraftGate:
                BeginAircraftLoadingSequence();
                break;
        }
    }


private void CollectAndReservePackages()
    {
        if (cargoZone == null)
        {
            AbortJob("The selected cargo zone is no longer available.");
            return;
        }

        int collected = vehicle.CollectOutgoingPackages(
            cargoZone,
            currentOperationId
        );

        // The selected zone may be empty because road trucks delivered
        // the packages to another zone. Search the other zones before
        // failing the outgoing baggage job.
        if (collected <= 0)
        {
            if (TryDriveToZoneWithAvailablePackages(cargoZone))
            {
                state = JobState.DrivingToCargoZone;
                jobStatus = "Driving to cargo zone with available baggage";

                Debug.Log(
                    $"{name}: No matching packages at the current zone. " +
                    $"Searching another cargo zone for flight " +
                    $"{currentOperationId}.",
                    this
                );

                return;
            }

            AbortJob(
                $"No available outgoing packages were found in any other " +
                $"cargo zone for flight {currentOperationId}."
            );

            return;
        }

        // Reserve each collected package in the aircraft manifest.
        // Packages that cannot be reserved are returned to the zone.
        List<CargoPackage> carriedPackages =
            new List<CargoPackage>(vehicle.CarriedPackages);

        foreach (CargoPackage package in carriedPackages)
        {
            if (package == null)
                continue;

            bool reserved = manifest.TryReserveOutgoingBaggage(
                package.PackageId,
                currentOperationId
            );

            if (reserved)
            {
                reservedPackageIds.Add(package.PackageId);
                continue;
            }

            ReturnPackageToZone(package);

            Debug.LogWarning(
                $"{name}: Package '{package.PackageId}' could not be " +
                "reserved in the aircraft manifest and was returned.",
                this
            );
        }

        reservedPackageCount = reservedPackageIds.Count;

        if (reservedPackageCount == 0)
        {
            AbortJob(
                "None of the collected packages could be reserved " +
                "in the aircraft manifest."
            );

            return;
        }

        Transform stopPoint = FindGateStopPoint(actualGate.transform);

        if (stopPoint == null)
        {
            AbortJob(
                $"Gate '{actualGate.name}' has no " +
                "CargoVehicleStopPoint child."
            );

            return;
        }

        jobStatus = "Driving to aircraft gate";

        Debug.Log(
            $"{name}: Reserved {reservedPackageCount} outgoing package(s). " +
            $"Driving to actual gate '{actualGate.StableId}'.",
            this
        );

        if (!vehicle.MoveTo(stopPoint.position))
        {
            AbortJob(
                "Could not calculate a route to the aircraft gate."
            );

            return;
        }

        state = JobState.DrivingToAircraftGate;
    }

    private bool TryDriveToZoneWithAvailablePackages(
        CargoZone excludedZone)
    {
        CargoZone[] zones =
            FindObjectsByType<CargoZone>(FindObjectsSortMode.None);

        System.Array.Sort(
            zones,
            (a, b) =>
            {
                if (a == null) return 1;
                if (b == null) return -1;

                float distanceA = HorizontalDistanceSquared(
                    transform.position,
                    a.AirportVehicleStopPoint != null
                        ? a.AirportVehicleStopPoint.position
                        : a.transform.position
                );

                float distanceB = HorizontalDistanceSquared(
                    transform.position,
                    b.AirportVehicleStopPoint != null
                        ? b.AirportVehicleStopPoint.position
                        : b.transform.position
                );

                return distanceA.CompareTo(distanceB);
            }
        );

        foreach (CargoZone zone in zones)
        {
            if (zone == null || zone == excludedZone)
                continue;

            if (zone.AirportVehicleStopPoint == null ||
                zone.CargoAreaPoint == null)
            {
                continue;
            }

            // Only visit zones that currently have available outgoing
            // packages for this specific aircraft operation.
            List<CargoPackage> availablePackages =
                zone.GetAvailablePackages(
                    CargoDirection.Outgoing,
                    currentOperationId
                );

            if (availablePackages.Count == 0)
                continue;

            if (!vehicle.MoveToCargoZone(zone))
                continue;

            cargoZone = zone;

            Debug.Log(
                $"{name}: Found {availablePackages.Count} available " +
                $"outgoing package(s) for {currentOperationId} at " +
                $"cargo zone '{zone.name}'.",
                this
            );

            return true;
        }

        return false;
    }


    private void BeginAircraftLoadingSequence()
    {
        if (aircraft == null ||
            manifest == null ||
            actualGate == null)
        {
            AbortJob("Aircraft or manifest references were lost.");
            return;
        }

        AircraftFlightIdentity identity =
            aircraft.GetComponent<AircraftFlightIdentity>();

        if (identity == null ||
            !identity.IsInitialized ||
            identity.OperationId != currentOperationId ||
            !identity.HasActualGate ||
            identity.ActualGateStableId != actualGate.StableId)
        {
            AbortJob(
                "The aircraft identity or actual gate changed " +
                "during the job."
            );

            return;
        }

        if (!aircraft.BeginOutgoingBaggageTransfer())
        {
            AbortJob(
                "The aircraft could not begin its outgoing baggage transfer. " +
                "Check that its baggage-loading task is in progress."
            );

            return;
        }

        phaseElapsedGameSeconds = 0f;
        state = JobState.OpeningCargoDoors;
        jobStatus = "Opening aircraft cargo doors";

        Debug.Log(
            $"{name}: Arrived at aircraft gate '{actualGate.StableId}'. " +
            "Opening cargo doors; loading begins after 2 in-game minutes.",
            this
        );
    }

    private void UpdateTimedLoadingSequence()
    {
        float speedMultiplier =
            gameTimeManager != null
                ? Mathf.Max(0f, gameTimeManager.CurrentSpeedMultiplier)
                : 1f;

        if (speedMultiplier <= 0f)
            return;

        phaseElapsedGameSeconds +=
            Time.unscaledDeltaTime * speedMultiplier;

        // Use a loop so a large game-time step can advance the sequence
        // without losing elapsed time between phases.
        while (state != JobState.Idle)
        {
            switch (state)
            {
                case JobState.OpeningCargoDoors:
                    if (phaseElapsedGameSeconds < DoorOpeningSeconds)
                        return;

                    phaseElapsedGameSeconds -= DoorOpeningSeconds;
                    state = JobState.LoadingBaggage;
                    jobStatus = "Loading outgoing baggage";

                    Debug.Log(
                        $"{name}: Cargo doors are open. " +
                        "Loading one bag every 30 in-game seconds.",
                        this
                    );

                    break;

                case JobState.LoadingBaggage:
                    if (reservedPackageIds.Count == 0)
                    {
                        phaseElapsedGameSeconds = 0f;
                        state = JobState.ClosingCargoDoors;
                        jobStatus = "Closing aircraft cargo doors";

                        Debug.Log(
                            $"{name}: All reserved bags have been loaded. " +
                            "Closing cargo doors.",
                            this
                        );

                        break;
                    }

                    if (phaseElapsedGameSeconds < SecondsPerBaggageItem)
                        return;

                    phaseElapsedGameSeconds -= SecondsPerBaggageItem;

                    if (!LoadNextPackage())
                        return;

                    if (reservedPackageIds.Count == 0)
                    {
                        phaseElapsedGameSeconds = 0f;
                        state = JobState.ClosingCargoDoors;
                        jobStatus = "Closing aircraft cargo doors";

                        Debug.Log(
                            $"{name}: All reserved bags have been loaded. " +
                            "Closing cargo doors.",
                            this
                        );
                    }

                    break;

                case JobState.ClosingCargoDoors:
                    if (phaseElapsedGameSeconds < DoorClosingSeconds)
                        return;

                    phaseElapsedGameSeconds = 0f;
                    FinishAircraftLoadingSequence();
                    return;

                default:
                    return;
            }
        }
    }

    private bool LoadNextPackage()
    {
        List<CargoPackage> carriedPackages =
            new List<CargoPackage>(vehicle.CarriedPackages);

        CargoPackage packageToLoad = null;

        foreach (CargoPackage package in carriedPackages)
        {
            if (package == null)
                continue;

            if (reservedPackageIds.Contains(package.PackageId))
            {
                packageToLoad = package;
                break;
            }
        }

        if (packageToLoad == null)
        {
            AbortJob(
                "A reserved baggage package could not be found " +
                "on the cargo vehicle."
            );

            return false;
        }

        // Confirm the individual bag through the aircraft controller.
        // This updates both the manifest and the outgoing progress counter.
        bool confirmed = aircraft.ConfirmOutgoingBaggageTransferred(
            packageToLoad.PackageId,
            currentOperationId
        );

        if (!confirmed)
        {
            AbortJob(
                $"The aircraft manifest rejected package " +
                $"'{packageToLoad.PackageId}'."
            );

            return false;
        }

        // Remove the successfully confirmed bag from the vehicle.
        if (!vehicle.ResolveCarriedPackage(packageToLoad, true))
        {
            AbortJob(
                $"Package '{packageToLoad.PackageId}' was confirmed by " +
                "the manifest but could not be removed from the vehicle."
            );

            return false;
        }

        reservedPackageIds.Remove(packageToLoad.PackageId);
        loadedPackageCount++;
        reservedPackageCount = reservedPackageIds.Count;

        Debug.Log(
            $"{name}: Loaded outgoing bag {loadedPackageCount}/" +
            $"{manifest.ExpectedOutgoingBaggageCount} for " +
            $"{currentOperationId}.",
            this
        );

        return true;
    }

    private void FinishAircraftLoadingSequence()
    {
        if (aircraft == null)
        {
            AbortJob("Aircraft reference was lost before loading completed.");
            return;
        }

        if (!aircraft.CompleteOutgoingBaggageTransfer())
        {
            AbortJob(
                "The aircraft could not complete baggage loading. " +
                "Some outgoing baggage may remain in the manifest."
            );

            return;
        }

        Debug.Log(
            $"{name}: Outgoing baggage job completed for " +
            $"{currentOperationId}. Loaded: {loadedPackageCount}. " +
            $"Remaining manifest load: {manifest.RemainingOutgoingBaggage}.",
            this
        );

        jobStatus = "Completed";

        string completedOperationId = currentOperationId;

        ResetJob(keepStatus: true);

        if (AirportBaggageDispatcher.Instance != null)
        {
            AirportBaggageDispatcher.Instance
                .NotifyOutgoingRequestCompleted(
                    vehicle,
                    completedOperationId
                );
        }

        if (vehicle != null)
        {
            vehicle.BeginReturnHomeIfIdle();
        }
    }

    private void ReturnPackageToZone(CargoPackage package)
    {
        if (package == null || cargoZone == null)
            return;

        if (manifest != null &&
            !string.IsNullOrWhiteSpace(package.PackageId))
        {
            manifest.ReleaseOutgoingBaggageReservation(
                package.PackageId
            );
        }

        if (cargoZone.CargoAreaPoint != null)
        {
            package.transform.SetPositionAndRotation(
                cargoZone.CargoAreaPoint.position,
                cargoZone.CargoAreaPoint.rotation
            );
        }

        vehicle.ResolveCarriedPackage(
            package,
            false,
            cargoZone
        );

        reservedPackageIds.Remove(package.PackageId);
        reservedPackageCount = reservedPackageIds.Count;
    }

    private Transform FindGateStopPoint(Transform gateRoot)
    {
        Transform[] children =
            gateRoot.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child != gateRoot &&
                child.name == "CargoVehicleStopPoint")
            {
                return child;
            }
        }

        return null;
    }

    private void AbortJob(string reason)
    {
        Debug.LogWarning(
            $"{name}: Outgoing baggage job failed. {reason}",
            this
        );

        // Return any cargo still on the tug to the selected zone.
        List<CargoPackage> carriedPackages =
            new List<CargoPackage>(vehicle.CarriedPackages);

        foreach (CargoPackage package in carriedPackages)
        {
            if (package != null)
                ReturnPackageToZone(package);
        }

        // Release any reservations that remain.
        if (manifest != null)
        {
            foreach (string packageId in reservedPackageIds)
            {
                manifest.ReleaseOutgoingBaggageReservation(packageId);
            }
        }

        reservedPackageIds.Clear();
        jobStatus = "Failed: " + reason;

        string failedOperationId = currentOperationId;

        ResetJob(keepStatus: true);

        if (AirportBaggageDispatcher.Instance != null &&
            !string.IsNullOrWhiteSpace(failedOperationId))
        {
            AirportBaggageDispatcher.Instance
                .NotifyOutgoingAssignmentFailed(
                    vehicle,
                    failedOperationId,
                    reason
                );
        }
    }

    private void ResetJob(bool keepStatus = false)
    {
        aircraft = null;
        manifest = null;
        actualGate = null;
        cargoZone = null;
        currentOperationId = string.Empty;

        reservedPackageIds.Clear();
        reservedPackageCount = 0;

        phaseElapsedGameSeconds = 0f;
        state = JobState.Idle;

        if (!keepStatus)
            jobStatus = "Idle";
    }

    private float HorizontalDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}
