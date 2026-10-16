
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AirportCargoVehicleController))]
public class AirportCargoIncomingBaggageJob : MonoBehaviour
{
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
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();
    }

    public bool BeginIncomingBaggageJob(
        AircraftTurnaroundController requestingAircraft,
        string operationId,
        int requestedCount)
    {
        if (state != JobState.Idle)
        {
            Debug.LogWarning(
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
            Debug.LogError(
                $"{name}: Assign the CargoBox prefab to " +
                "AirportCargoIncomingBaggageJob.",
                this
            );

            return false;
        }

        if (cargoBoxPrefab.GetComponent<CargoPackage>() == null)
        {
            Debug.LogError(
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
            Debug.LogError(
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
            Debug.LogWarning(
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
            Debug.LogWarning(
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
            Debug.LogWarning(
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

        Debug.Log(
            $"{name}: Arrived at aircraft for {operationId}. " +
            $"Preparing to unload {reservedPackageIds.Count} baggage item(s).",
            this
        );

        return true;
    }

    private bool TrySelectCargoZone()
    {
        CargoZone[] zones =
            FindObjectsByType<CargoZone>(FindObjectsSortMode.None);

        if (zones.Length == 0)
        {
            Debug.LogWarning(
                $"{name}: No CargoZone objects exist in the scene.",
                this
            );

            return false;
        }

        System.Array.Sort(
            zones,
            (a, b) =>
            {
                if (a == null) return 1;
                if (b == null) return -1;

                Vector3 positionA =
                    a.AirportVehicleStopPoint != null
                        ? a.AirportVehicleStopPoint.position
                        : a.transform.position;

                Vector3 positionB =
                    b.AirportVehicleStopPoint != null
                        ? b.AirportVehicleStopPoint.position
                        : b.transform.position;

                return HorizontalDistanceSquared(
                    transform.position,
                    positionA
                ).CompareTo(
                    HorizontalDistanceSquared(
                        transform.position,
                        positionB
                    )
                );
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

        float speedMultiplier =
            gameTimeManager != null
                ? Mathf.Max(0f, gameTimeManager.CurrentSpeedMultiplier)
                : 1f;

        // These waits use in-game seconds and pause when game speed is zero.
        if (speedMultiplier <= 0f)
            return;

        float deltaGameSeconds =
            Time.unscaledDeltaTime * speedMultiplier;

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

        Debug.Log(
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

            if (!aircraft.ConfirmIncomingBaggageTransferred(packageId))
            {
                AbortJob(
                    $"Could not confirm transfer of baggage '{packageId}'."
                );

                return;
            }

            nextPackageIndex++;
            transferredPackageCount++;

            Debug.Log(
                $"{name}: Transferred baggage " +
                $"{transferredPackageCount}/{reservedPackageIds.Count} " +
                $"for {currentOperationId}. Aircraft progress: " +
                $"{aircraft.IncomingBaggageProgressText}.",
                this
            );
        }

        if (nextPackageIndex < reservedPackageIds.Count)
            return;

        phaseElapsedGameSeconds = 0f;
        state = JobState.ClosingCargoDoors;
        jobStatus = "Closing aircraft cargo doors";

        Debug.Log(
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

        if (!aircraft.CompleteIncomingBaggageTransfer())
        {
            AbortJob(
                "The aircraft did not accept completion of baggage unloading."
            );

            return;
        }

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

        Debug.Log(
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

        foreach (string packageId in reservedPackageIds)
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

                Debug.LogError(
                    $"{name}: Spawned cargo box has no CargoPackage.",
                    this
                );

                continue;
            }

            package.Initialize(
                packageId,
                CargoDirection.Incoming,
                string.Empty,
                targetZone
            );

            if (!targetZone.RegisterPackage(package))
            {
                Destroy(box);

                Debug.LogError(
                    $"{name}: Could not register baggage '{packageId}' " +
                    $"in cargo zone '{targetZone.name}'.",
                    targetZone
                );

                continue;
            }

            // The aircraft manifest was already updated at the aircraft,
            // one item at a time. Do not confirm the same baggage twice.
            deliveredCount++;
        }

        Debug.Log(
            $"{name}: Delivered {deliveredCount} incoming baggage " +
            $"package(s) to cargo zone '{targetZone.name}' for " +
            $"{currentOperationId}.",
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

        Debug.LogWarning(
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
            for (int i = nextPackageIndex;
                 i < reservedPackageIds.Count;
                 i++)
            {
                aircraft.ReleaseIncomingBaggageReservation(
                    reservedPackageIds[i]
                );
            }
        }

        reservedPackageIds.Clear();
        reservedPackageCount = 0;
    }

    private void ResetJob()
    {
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
}
