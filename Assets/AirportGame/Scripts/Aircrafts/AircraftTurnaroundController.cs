
using System;
using System.Collections.Generic;
using UnityEngine;

public enum AircraftTurnaroundTaskStatus
{
    NotStarted,
    InProgress,
    Completed
}

public class AircraftTurnaroundController : MonoBehaviour
{
    public static event Action<AircraftTurnaroundController> PassengersDeboardingStarted;
    public static event Action<AircraftTurnaroundController> PassengersDeboarded;
    public static event Action<AircraftTurnaroundController> PassengersBoardingStarted;

    private const float DeboardingStartDelaySeconds = 120f;

    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    private interface ITaskProgress
    {
        float DurationMinutes { get; }
        float DurationSeconds { get; }
        AircraftTurnaroundTaskStatus Status { get; }
        void ResetTask();
        void StartTask();
        bool Advance(float deltaGameSeconds);
        void MarkCompleted();
    }

    [Serializable]
    private sealed class TaskProgress : ITaskProgress
    {
        [SerializeField, Min(0f)]
        private float durationMinutes;

        [SerializeField]
        private AircraftTurnaroundTaskStatus status =
            AircraftTurnaroundTaskStatus.NotStarted;

        [SerializeField, Min(0f)]
        private float elapsedGameSeconds;

        public float DurationMinutes => Mathf.Max(0f, durationMinutes);
        public float DurationSeconds => DurationMinutes * 60f;
        public AircraftTurnaroundTaskStatus Status => status;

        public TaskProgress(float defaultDurationMinutes)
        {
            durationMinutes = defaultDurationMinutes;
        }

        public void ResetTask()
        {
            status = AircraftTurnaroundTaskStatus.NotStarted;
            elapsedGameSeconds = 0f;
        }

        public void StartTask()
        {
            if (status != AircraftTurnaroundTaskStatus.NotStarted)
                return;

            status = AircraftTurnaroundTaskStatus.InProgress;
            elapsedGameSeconds = 0f;
        }

        public bool Advance(float deltaGameSeconds)
        {
            if (status != AircraftTurnaroundTaskStatus.InProgress)
                return false;

            elapsedGameSeconds = Mathf.Min(
                elapsedGameSeconds + deltaGameSeconds,
                DurationSeconds
            );

            return elapsedGameSeconds >= DurationSeconds;
        }

        public void MarkCompleted()
        {
            status = AircraftTurnaroundTaskStatus.Completed;
            elapsedGameSeconds = DurationSeconds;
        }
    }

    [Serializable]
    private sealed class BaggageUnloadingProgress : ITaskProgress
    {
        [SerializeField, HideInInspector, Min(0f)]
        private float durationMinutes = 10f;

        [SerializeField]
        private AircraftTurnaroundTaskStatus status =
            AircraftTurnaroundTaskStatus.NotStarted;

        [SerializeField, HideInInspector, Min(0f)]
        private float elapsedGameSeconds;

        public float DurationMinutes => Mathf.Max(0f, durationMinutes);
        public float DurationSeconds => DurationMinutes * 60f;
        public AircraftTurnaroundTaskStatus Status => status;

        public BaggageUnloadingProgress(float defaultDurationMinutes)
        {
            durationMinutes = defaultDurationMinutes;
        }

        public void ResetTask()
        {
            status = AircraftTurnaroundTaskStatus.NotStarted;
            elapsedGameSeconds = 0f;
        }

        public void StartTask()
        {
            if (status != AircraftTurnaroundTaskStatus.NotStarted)
                return;

            status = AircraftTurnaroundTaskStatus.InProgress;
            elapsedGameSeconds = 0f;
        }

        public bool Advance(float deltaGameSeconds)
        {
            if (status != AircraftTurnaroundTaskStatus.InProgress)
                return false;

            elapsedGameSeconds = Mathf.Min(
                elapsedGameSeconds + deltaGameSeconds,
                DurationSeconds
            );

            return elapsedGameSeconds >= DurationSeconds;
        }

        public void MarkCompleted()
        {
            status = AircraftTurnaroundTaskStatus.Completed;
            elapsedGameSeconds = DurationSeconds;
        }
    }

    [Serializable]
    private sealed class BaggageLoadingProgress : ITaskProgress
    {
        [SerializeField, HideInInspector, Min(0f)]
        private float durationMinutes = 10f;

        [SerializeField]
        private AircraftTurnaroundTaskStatus status =
            AircraftTurnaroundTaskStatus.NotStarted;

        [SerializeField, HideInInspector, Min(0f)]
        private float elapsedGameSeconds;

        public float DurationMinutes => Mathf.Max(0f, durationMinutes);
        public float DurationSeconds => DurationMinutes * 60f;
        public AircraftTurnaroundTaskStatus Status => status;

        public BaggageLoadingProgress(float defaultDurationMinutes)
        {
            durationMinutes = defaultDurationMinutes;
        }

        public void ResetTask()
        {
            status = AircraftTurnaroundTaskStatus.NotStarted;
            elapsedGameSeconds = 0f;
        }

        public void StartTask()
        {
            if (status != AircraftTurnaroundTaskStatus.NotStarted)
                return;

            status = AircraftTurnaroundTaskStatus.InProgress;
            elapsedGameSeconds = 0f;
        }

        public bool Advance(float deltaGameSeconds)
        {
            if (status != AircraftTurnaroundTaskStatus.InProgress)
                return false;

            elapsedGameSeconds = Mathf.Min(
                elapsedGameSeconds + deltaGameSeconds,
                DurationSeconds
            );

            return elapsedGameSeconds >= DurationSeconds;
        }

        public void MarkCompleted()
        {
            status = AircraftTurnaroundTaskStatus.Completed;
            elapsedGameSeconds = DurationSeconds;
        }
    }

    [Serializable]
    private sealed class FuelingProgress : ITaskProgress
    {
        [SerializeField, Min(0f)]
        private float actualFuelLitres;

        [SerializeField, Min(0f)]
        private float maximumFuelLitres;

        [SerializeField]
        private AircraftTurnaroundTaskStatus status =
            AircraftTurnaroundTaskStatus.NotStarted;

        public float DurationMinutes => 0f;
        public float DurationSeconds => 0f;
        public AircraftTurnaroundTaskStatus Status => status;

        public void UpdateFuel(AircraftFuelTank tank)
        {
            if (tank == null)
                return;

            actualFuelLitres = tank.CurrentFuelLitres;
            maximumFuelLitres = tank.MaximumFuelLitres;
        }

        public void ResetTask()
        {
            status = AircraftTurnaroundTaskStatus.NotStarted;
        }

        public void StartTask()
        {
            if (status == AircraftTurnaroundTaskStatus.NotStarted)
                status = AircraftTurnaroundTaskStatus.InProgress;
        }

        public bool Advance(float deltaGameSeconds) => false;

        public void MarkCompleted()
        {
            status = AircraftTurnaroundTaskStatus.Completed;
            actualFuelLitres = maximumFuelLitres;
        }
    }

    private enum TaskType
    {
        Deboarding,
        BaggageUnloading,
        Cleaning,
        Fueling,
        BaggageLoading,
        Boarding
    }

    [Header("Time")]
    [SerializeField]
    private GameTimeManager gameTimeManager;

    [Header("Aircraft Inventory")]
    [SerializeField]
    private AircraftLoadingManifest loadingManifest;

    [Header("Task Durations (In-Game Minutes)")]

    [SerializeField]
    private TaskProgress deboarding = new TaskProgress(10f);

    [Header("Baggage Unloading")]
    [SerializeField]
    private BaggageUnloadingProgress baggageUnloading =
        new BaggageUnloadingProgress(10f);

    [SerializeField]
    private TaskProgress cleaning = new TaskProgress(10f);

    [SerializeField]
    private FuelingProgress fueling = new FuelingProgress();

    [Header("Baggage Loading")]
    [SerializeField]
    private BaggageLoadingProgress baggageLoading =
        new BaggageLoadingProgress(10f);

    [SerializeField]
    private TaskProgress boarding = new TaskProgress(15f);

    [Header("Incoming Baggage Transfer Progress")]

    [SerializeField]
    private int incomingBaggageTransferTotal;

    [SerializeField]
    private int incomingBaggageTransferred;

    [SerializeField]
    private bool incomingBaggageTransferControlled;

    /// <summary>
    /// Progress for unloading baggage from the aircraft.
    /// Example: "2/10".
    /// </summary>
    public string IncomingBaggageProgressText =>
        $"{incomingBaggageTransferred}/{incomingBaggageTransferTotal}";

    public int IncomingBaggageTransferTotal =>
        incomingBaggageTransferTotal;

    public int IncomingBaggageTransferred =>
        incomingBaggageTransferred;

    [Header("Outgoing Baggage Transfer Progress")]

    [SerializeField]
    private int outgoingBaggageTransferTotal;

    [SerializeField]
    private int outgoingBaggageTransferred;

    [SerializeField]
    private bool outgoingBaggageTransferControlled;

    /// <summary>
    /// Progress for loading baggage onto the aircraft.
    /// Example: "2/10".
    /// </summary>
    public string OutgoingBaggageProgressText =>
        $"{outgoingBaggageTransferred}/{outgoingBaggageTransferTotal}";

    public int OutgoingBaggageTransferTotal =>
        outgoingBaggageTransferTotal;

    public int OutgoingBaggageTransferred =>
        outgoingBaggageTransferred;

    [Header("Departure Readiness")]

    [Tooltip(
        "When enabled, Ready To Depart is set automatically when all six tasks finish. " +
        "Disable this to control Ready To Depart manually in the Inspector."
    )]
    [SerializeField]
    private bool setReadyToDepartAutomatically = true;

    [Tooltip(
        "The departure-readiness flag. When automatic mode is disabled, " +
        "you can set this to true or false manually."
    )]
    [SerializeField]
    private bool readyToDepart;

    [Header("Runtime Status")]
    [SerializeField]
    private bool turnaroundStarted;

    [SerializeField, HideInInspector]
    private bool externalFuelingControlled;

    [SerializeField, HideInInspector]
    private float fuelTruckRequestRetryTimer;

    [SerializeField, HideInInspector]
    private bool externalCleaningControlled;

    [SerializeField, HideInInspector]
    private bool externalDeboardingControlled;

    [SerializeField, HideInInspector]
    private bool externalBoardingControlled;

    [SerializeField, HideInInspector]
    private float deboardingStartDelayRemaining;

    [SerializeField, HideInInspector]
    private bool deboardingStartPending;

    [SerializeField, HideInInspector]
    private float cleaningVehicleRequestRetryTimer;

    [SerializeField, HideInInspector]
    private float baggageVehicleRequestRetryTimer;

    private long baggageServiceRetryEventId;

    public bool TurnaroundStarted => turnaroundStarted;
    public bool ReadyToDepart => readyToDepart;
    public bool IsTurnaroundComplete => AllTasksCompleted();

    public AircraftTurnaroundTaskStatus DeboardingStatus =>
        deboarding.Status;

    public AircraftTurnaroundTaskStatus BaggageUnloadingStatus =>
        baggageUnloading.Status;

    public AircraftTurnaroundTaskStatus CleaningStatus =>
        cleaning.Status;

    public AircraftTurnaroundTaskStatus FuelingStatus =>
        fueling.Status;

    public AircraftTurnaroundTaskStatus BaggageLoadingStatus =>
        baggageLoading.Status;

    public AircraftTurnaroundTaskStatus BoardingStatus =>
        boarding.Status;

    public AircraftLoadingManifest LoadingManifest => loadingManifest;

    public bool IsBaggageReadyForCollection =>
        turnaroundStarted &&
        baggageUnloading.Status ==
            AircraftTurnaroundTaskStatus.InProgress &&
        loadingManifest != null &&
        loadingManifest.IsInitialized &&
        loadingManifest.HasIncomingBaggageRemaining;

    private AircraftFuelTank aircraftFuelTank;

    private void Awake()
    {
        aircraftFuelTank = GetComponent<AircraftFuelTank>();

        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();

        if (loadingManifest == null)
            loadingManifest = GetComponent<AircraftLoadingManifest>();
    }

    private void Update()
    {
        if (aircraftFuelTank == null)
            aircraftFuelTank = GetComponent<AircraftFuelTank>();

        fueling.UpdateFuel(aircraftFuelTank);

        if (!turnaroundStarted)
            return;

        if (fueling.Status == AircraftTurnaroundTaskStatus.InProgress &&
            externalFuelingControlled)
        {
            fuelTruckRequestRetryTimer -= GameTimeManager.SimulationDeltaTime;
            if (fuelTruckRequestRetryTimer <= 0f)
                RequestExternalFuelServiceIfAvailable();
        }

        if (cleaning.Status == AircraftTurnaroundTaskStatus.InProgress &&
            externalCleaningControlled)
        {
            cleaningVehicleRequestRetryTimer -= GameTimeManager.SimulationDeltaTime;
            if (cleaningVehicleRequestRetryTimer <= 0f)
                RequestExternalCleaningServiceIfAvailable();
        }

        if (NeedsExternalBaggageService())
        {
            if (gameTimeManager == null || gameTimeManager.EventScheduler == null)
            {
                baggageVehicleRequestRetryTimer -= GameTimeManager.SimulationDeltaTime;
                if (baggageVehicleRequestRetryTimer <= 0f)
                    RequestExternalBaggageServiceIfAvailable();
            }
            else if (baggageServiceRetryEventId == 0)
            {
                ScheduleBaggageServiceRetry();
            }
        }
        else
        {
            baggageVehicleRequestRetryTimer = 0f;
            CancelBaggageServiceRetry();
        }

        float deltaGameSeconds = GameTimeManager.SimulationDeltaTime;

        if (deboardingStartPending && deltaGameSeconds > 0f)
        {
            deboardingStartDelayRemaining -= deltaGameSeconds;
            if (deboardingStartDelayRemaining <= 0f)
            {
                deboardingStartPending = false;
                deboardingStartDelayRemaining = 0f;
                StartTask(TaskType.Deboarding);
            }
        }

        if (deltaGameSeconds > 0f)
        {

            bool tickDeboarding =
                deboarding.Status == AircraftTurnaroundTaskStatus.InProgress;

            bool tickBaggageUnloading =
                !incomingBaggageTransferControlled &&
                baggageUnloading.Status ==
                    AircraftTurnaroundTaskStatus.InProgress;

            // Cleaning is completed by the dispatched cleaning vehicle,
            // not by the generic turnaround timer.
            bool tickCleaning =
                cleaning.Status == AircraftTurnaroundTaskStatus.InProgress &&
                !externalCleaningControlled;

            // Aircraft fueling is completed only by an airport fuel truck.
            // It is never advanced by the turnaround timer.
            bool tickFueling = false;

            bool tickBaggageLoading =
                !outgoingBaggageTransferControlled &&
                baggageLoading.Status ==
                    AircraftTurnaroundTaskStatus.InProgress;

            bool tickBoarding =
                boarding.Status == AircraftTurnaroundTaskStatus.InProgress &&
                !externalBoardingControlled;

            if (tickDeboarding)
                AdvanceTask(TaskType.Deboarding, deltaGameSeconds);

            if (tickBaggageUnloading)
                AdvanceTask(TaskType.BaggageUnloading, deltaGameSeconds);

            if (tickCleaning)
                AdvanceTask(TaskType.Cleaning, deltaGameSeconds);

            if (tickBaggageLoading)
                AdvanceTask(TaskType.BaggageLoading, deltaGameSeconds);

            if (tickBoarding)
                AdvanceTask(TaskType.Boarding, deltaGameSeconds);
        }

        if (setReadyToDepartAutomatically)
            readyToDepart = AllTasksCompleted();
    }

    /// <summary>
    /// Called when the aircraft has finished taxiing and is parked at its gate.
    /// Safe to call more than once.
    /// </summary>
    public void BeginTurnaround()
    {
        if (turnaroundStarted)
            return;

        if (loadingManifest == null)
            loadingManifest = GetComponent<AircraftLoadingManifest>();

        if (loadingManifest != null)
        {
            loadingManifest.InitializeManifest();
        }
        else
        {
            LogWarning(
                $"{name}: No AircraftLoadingManifest is attached. " +
                "Baggage collection will not be available.",
                this
            );
        }

        incomingBaggageTransferred = 0;
        incomingBaggageTransferTotal = 0;
        incomingBaggageTransferControlled =
            loadingManifest != null &&
            loadingManifest.HasIncomingBaggageRemaining;

        if (incomingBaggageTransferControlled)
        {
            incomingBaggageTransferTotal =
                loadingManifest.TotalIncomingBaggage;
        }

        outgoingBaggageTransferred = 0;
        outgoingBaggageTransferTotal =
            loadingManifest != null
                ? loadingManifest.ExpectedOutgoingBaggageCount
                : 0;

        // Outgoing baggage loading is controlled by the cargo vehicle job,
        // rather than by the normal task-duration timer.
        outgoingBaggageTransferControlled =
            loadingManifest != null &&
            loadingManifest.ExpectedOutgoingBaggageCount > 0;

        AircraftFlightIdentity identity =
            GetComponent<AircraftFlightIdentity>();

        bool isCargoOnlyFlight =
            identity != null &&
            identity.IsInitialized &&
            identity.Definition != null &&
            identity.Definition.OperationType == FlightOperationType.CargoOnly;

        if (isCargoOnlyFlight)
        {
            // Cargo-only flights do not perform passenger turnaround tasks.
            deboarding.MarkCompleted();
            cleaning.MarkCompleted();
            boarding.MarkCompleted();
        }

        turnaroundStarted = true;

        Log(
            $"{name}: Turnaround started.",
            this
        );

        if (isCargoOnlyFlight)
        {
            // Fueling still applies to cargo-only aircraft.
            StartTask(TaskType.Fueling);
            RequestExternalFuelServiceIfAvailable();
        }
        else
        {
            // Deboarding begins two in-game minutes after the aircraft parks.
            // Its completion is driven by the actual passenger spawn/deboarding job.
            deboardingStartPending = true;
            deboardingStartDelayRemaining = DeboardingStartDelaySeconds;
        }

        StartTask(TaskType.BaggageUnloading);

        if (setReadyToDepartAutomatically)
            readyToDepart = AllTasksCompleted();

        RequestExternalBaggageServiceIfAvailable();
    }

    private bool NeedsExternalBaggageService()
    {
        if (!turnaroundStarted ||
            loadingManifest == null ||
            !loadingManifest.IsInitialized)
        {
            return false;
        }

        bool incomingNeeded =
            baggageUnloading.Status ==
                AircraftTurnaroundTaskStatus.InProgress &&
            loadingManifest.HasIncomingBaggageRemaining;

        bool outgoingNeeded =
            baggageLoading.Status ==
                AircraftTurnaroundTaskStatus.InProgress &&
            loadingManifest.HasOutgoingBaggageRemaining;

        return incomingNeeded || outgoingNeeded;
    }

    private void ScheduleBaggageServiceRetry()
    {
        if (baggageServiceRetryEventId != 0 || gameTimeManager == null ||
            gameTimeManager.EventScheduler == null)
            return;

        baggageServiceRetryEventId = gameTimeManager.EventScheduler.ScheduleAfter(
            5.0,
            HandleScheduledBaggageServiceRetry);
    }

    private void HandleScheduledBaggageServiceRetry()
    {
        baggageServiceRetryEventId = 0;
        if (NeedsExternalBaggageService())
            RequestExternalBaggageServiceIfAvailable();
        else
            baggageVehicleRequestRetryTimer = 0f;
    }

    private void CancelBaggageServiceRetry()
    {
        if (baggageServiceRetryEventId == 0)
            return;

        if (gameTimeManager != null && gameTimeManager.EventScheduler != null)
            gameTimeManager.EventScheduler.Cancel(baggageServiceRetryEventId);

        baggageServiceRetryEventId = 0;
    }

    private void OnDisable()
    {
        CancelBaggageServiceRetry();
    }

    private void OnEnable()
    {
        if (turnaroundStarted && NeedsExternalBaggageService())
            ScheduleBaggageServiceRetry();
    }

    private void RequestExternalBaggageServiceIfAvailable()
    {
        if (!NeedsExternalBaggageService())
        {
            baggageVehicleRequestRetryTimer = 0f;
            CancelBaggageServiceRetry();
            return;
        }

        ScheduleBaggageServiceRetry();
        baggageVehicleRequestRetryTimer = 5f;

        AirportBaggageDispatcher dispatcher =
            AirportBaggageDispatcher.Instance;

        if (dispatcher == null)
        {
            LogWarning(
                $"${name}: Cannot request baggage service because the baggage dispatcher is unavailable. Retrying.",
                this
            );
            return;
        }

        AircraftFlightIdentity identity =
            GetComponent<AircraftFlightIdentity>();

        if (identity == null ||
            !identity.IsInitialized ||
            !identity.HasActualGate)
        {
            LogWarning(
                $"${name}: Cannot request baggage service because the aircraft has no valid actual gate. Retrying.",
                this
            );
            return;
        }

        AircraftGateRegistry gateRegistry =
            FindAnyObjectByType<AircraftGateRegistry>();

        if (gateRegistry == null ||
            !gateRegistry.TryGetGate(
                identity.ActualGateStableId,
                out AircraftGate actualGate))
        {
            LogWarning(
                $"${name}: Cannot request baggage service because actual gate '${identity.ActualGateStableId}' could not be found. Retrying.",
                this
            );
            return;
        }

        bool incomingNeeded =
            baggageUnloading.Status ==
                AircraftTurnaroundTaskStatus.InProgress &&
            loadingManifest.HasIncomingBaggageRemaining;

        bool requestAccepted = incomingNeeded
            ? dispatcher.RequestBaggageService(this, actualGate)
            : dispatcher.RequestOutgoingBaggageService(this, actualGate);

        if (!requestAccepted)
        {
            LogWarning(
                $"${name}: Baggage service request was not accepted. It will be retried.",
                this
            );
        }
    }

    public List<string> ReserveIncomingBaggage(int maximumCount)
    {
        if (!IsBaggageReadyForCollection || loadingManifest == null)
            return new List<string>();

        return loadingManifest.ReserveIncomingBaggage(maximumCount);
    }

    public bool ConfirmIncomingBaggageTransferred(string packageId)
    {
        if (loadingManifest == null ||
            baggageUnloading.Status !=
                AircraftTurnaroundTaskStatus.InProgress)
        {
            return false;
        }

        bool confirmed =
            loadingManifest.ConfirmIncomingBaggageTransferred(packageId);

        if (!confirmed)
            return false;

        incomingBaggageTransferred = Mathf.Clamp(
            incomingBaggageTransferTotal -
                loadingManifest.RemainingIncomingBaggage,
            0,
            incomingBaggageTransferTotal
        );

        return true;
    }

    public bool ReleaseIncomingBaggageReservation(string packageId)
    {
        if (loadingManifest == null)
            return false;

        return loadingManifest.ReleaseIncomingBaggageReservation(packageId);
    }

    /// <summary>
    /// Called by the incoming baggage job after all bags have been transferred
    /// and the aircraft cargo doors have finished closing.
    /// </summary>
    public bool CompleteIncomingBaggageTransfer()
    {
        if (!incomingBaggageTransferControlled)
            return false;

        if (loadingManifest == null ||
            loadingManifest.HasIncomingBaggageRemaining)
        {
            LogWarning(
                $"{name}: Cannot complete baggage unloading because " +
                "incoming baggage remains on the aircraft.",
                this
            );

            return false;
        }

        if (baggageUnloading.Status !=
            AircraftTurnaroundTaskStatus.InProgress)
        {
            return false;
        }

        incomingBaggageTransferred = incomingBaggageTransferTotal;

        CompleteTask(TaskType.BaggageUnloading);
        return true;
    }

    /// <summary>
    /// Called by the outgoing baggage job when the cargo vehicle reaches
    /// the aircraft and is ready to begin its timed loading sequence.
    /// </summary>
    public bool BeginOutgoingBaggageTransfer()
    {
        if (!turnaroundStarted || loadingManifest == null)
            return false;

        if (baggageLoading.Status !=
            AircraftTurnaroundTaskStatus.InProgress)
        {
            LogWarning(
                $"{name}: Cannot begin outgoing baggage transfer because " +
                "the baggage-loading task is not in progress.",
                this
            );

            return false;
        }

        outgoingBaggageTransferTotal =
            loadingManifest.ExpectedOutgoingBaggageCount;

        outgoingBaggageTransferred = Mathf.Clamp(
            loadingManifest.TotalOutgoingBaggageLoaded,
            0,
            outgoingBaggageTransferTotal
        );

        outgoingBaggageTransferControlled = true;

        return true;
    }

    /// <summary>
    /// Confirms one outgoing bag as loaded and updates aircraft progress.
    /// </summary>
    public bool ConfirmOutgoingBaggageTransferred(
        string packageId,
        string operationId)
    {
        if (loadingManifest == null ||
            baggageLoading.Status !=
                AircraftTurnaroundTaskStatus.InProgress ||
            !outgoingBaggageTransferControlled)
        {
            return false;
        }

        bool confirmed =
            loadingManifest.ConfirmOutgoingBaggageLoaded(
                packageId,
                operationId
            );

        if (!confirmed)
            return false;

        outgoingBaggageTransferred = Mathf.Clamp(
            loadingManifest.TotalOutgoingBaggageLoaded,
            0,
            outgoingBaggageTransferTotal
        );

        return true;
    }

    /// <summary>
    /// Called by the outgoing baggage job after all outgoing bags have been
    /// loaded and the aircraft cargo doors have finished closing.
    /// </summary>
    public bool CompleteOutgoingBaggageTransfer()
    {
        if (!outgoingBaggageTransferControlled ||
            loadingManifest == null)
        {
            return false;
        }

        if (loadingManifest.HasOutgoingBaggageRemaining)
        {
            LogWarning(
                $"{name}: Cannot complete baggage loading because " +
                "outgoing baggage remains to be loaded.",
                this
            );

            return false;
        }

        if (baggageLoading.Status !=
            AircraftTurnaroundTaskStatus.InProgress)
        {
            return false;
        }

        outgoingBaggageTransferred =
            loadingManifest.TotalOutgoingBaggageLoaded;

        outgoingBaggageTransferTotal =
            loadingManifest.ExpectedOutgoingBaggageCount;

        outgoingBaggageTransferControlled = false;

        CompleteTask(TaskType.BaggageLoading);
        return true;
    }

    /// <summary>
    /// Keeps boarding under passenger-coordinator control until every
    /// terminal passenger has boarded the aircraft.
    /// </summary>
    public void CompleteExternalDeboarding()
    {
        if (!externalDeboardingControlled ||
            deboarding.Status != AircraftTurnaroundTaskStatus.InProgress)
            return;

        externalDeboardingControlled = false;
        CompleteTask(TaskType.Deboarding);
    }

    public void CompleteExternalBoarding()
    {
        if (!externalBoardingControlled ||
            boarding.Status != AircraftTurnaroundTaskStatus.InProgress)
            return;

        externalBoardingControlled = false;
        CompleteTask(TaskType.Boarding);
    }

    /// <summary>
    /// Keeps cleaning under vehicle control until a cleaning vehicle
    /// completes its preparation, passenger cleaning and exit phases.
    /// </summary>
    public void EnableExternalCleaning()
    {
        if (cleaning.Status == AircraftTurnaroundTaskStatus.InProgress)
        {
            externalCleaningControlled = true;
            cleaningVehicleRequestRetryTimer = 0f;
        }
    }

    public void CompleteExternalCleaning()
    {
        if (cleaning.Status != AircraftTurnaroundTaskStatus.InProgress)
            return;

        ChargeCleaningFee();

        externalCleaningControlled = false;
        cleaningVehicleRequestRetryTimer = 0f;
        CompleteTask(TaskType.Cleaning);
    }

    /// <summary>
    /// Keeps cleaning under external vehicle control after an assignment fails.
    /// The turnaround retries the request instead of completing a simulated task.
    /// </summary>
    public void CancelExternalCleaning()
    {
        externalCleaningControlled =
            cleaning.Status == AircraftTurnaroundTaskStatus.InProgress;
        cleaningVehicleRequestRetryTimer = 5f;
    }

    /// <summary>
    /// Requests an available cleaning vehicle. If none is available,
    /// Update retries the request after the configured delay.
    /// </summary>
    private void RequestExternalCleaningServiceIfAvailable()
    {
        if (cleaning.Status != AircraftTurnaroundTaskStatus.InProgress)
            return;

        externalCleaningControlled = true;
        cleaningVehicleRequestRetryTimer = 5f;
        AircraftCleaningVehicleController.RequestCleaning(this);
    }

    private void ChargeCleaningFee()
    {
        AircraftStats aircraftStats =
            GetComponent<AircraftStats>();

        if (aircraftStats == null)
        {
            LogWarning(
                name + ": Cannot determine the cleaning fee because AircraftStats is missing.",
                this);
            return;
        }

        AircraftAirportFees airportFees =
            GetComponent<AircraftAirportFees>();

        if (airportFees == null)
        {
            LogError(
                name + ": AircraftAirportFees is missing; cleaning revenue could not be recorded.",
                this);
            return;
        }

        airportFees.ChargeCleaningFee(aircraftStats.maxPassengers);
    }

    /// <summary>
    /// Pauses the normal timed fueling task while the airport fuel truck
    /// performs the actual fuel transfer.
    /// </summary>
    public void EnableExternalFueling()
    {
        if (fueling.Status == AircraftTurnaroundTaskStatus.InProgress)
        {
            externalFuelingControlled = true;
            fuelTruckRequestRetryTimer = 5f;
        }
    }

    /// <summary>
    /// Completes the aircraft fueling task after the truck fills its tank.
    /// </summary>
    public void CompleteExternalFueling()
    {
        if (fueling.Status != AircraftTurnaroundTaskStatus.InProgress)
            return;

        externalFuelingControlled = false;
        fuelTruckRequestRetryTimer = 0f;
        CompleteTask(TaskType.Fueling);
    }

    /// <summary>
    /// Restores the normal timed fueling fallback if a truck assignment fails.
    /// </summary>
    public void CancelExternalFueling()
    {
        // A failed or unavailable truck must never restore simulated fueling.
        externalFuelingControlled =
            fueling.Status == AircraftTurnaroundTaskStatus.InProgress;
        fuelTruckRequestRetryTimer = 5f;
    }

    private void RequestExternalFuelServiceIfAvailable()
    {
        if (fueling.Status != AircraftTurnaroundTaskStatus.InProgress)
            return;

        externalFuelingControlled = true;
        fuelTruckRequestRetryTimer = 5f;
        AirportFuelTruckController.RequestRefuelling(this);
    }

    private void AdvanceTask(TaskType taskType, float deltaGameSeconds)
    {
        ITaskProgress task = GetTask(taskType);

        if (task.Advance(deltaGameSeconds))
            CompleteTask(taskType);
    }

    private void StartTask(TaskType taskType)
    {
        ITaskProgress task = GetTask(taskType);

        if (task.Status != AircraftTurnaroundTaskStatus.NotStarted)
            return;

        task.StartTask();

        if (taskType == TaskType.Deboarding)
        {
            externalDeboardingControlled = true;
            PassengersDeboardingStarted?.Invoke(this);
        }
        else if (taskType == TaskType.Boarding)
        {
            externalBoardingControlled = true;
            PassengersBoardingStarted?.Invoke(this);
        }

        Log(
            $"{name}: {GetTaskName(taskType)} started " +
            $"({task.DurationMinutes:0.#} in-game minutes).",
            this
        );

        // External baggage transfers must not auto-complete from their
        // simulated duration, even when that duration is configured as zero.
        bool externallyControlledBaggage =
            (taskType == TaskType.BaggageUnloading &&
             incomingBaggageTransferControlled) ||
            (taskType == TaskType.BaggageLoading &&
             outgoingBaggageTransferControlled);
        bool externallyControlledPassengers =
            taskType == TaskType.Deboarding ||
            taskType == TaskType.Boarding;

        // Fueling and externally controlled baggage tasks are completed
        // only by their corresponding airport vehicles.
        if (taskType != TaskType.Fueling &&
            !externallyControlledBaggage &&
            !externallyControlledPassengers &&
            task.DurationSeconds <= 0f)
        {
            CompleteTask(taskType);
        }
    }

    private void CompleteTask(TaskType taskType)
    {
        ITaskProgress task = GetTask(taskType);

        if (task.Status == AircraftTurnaroundTaskStatus.Completed)
            return;

        task.MarkCompleted();

        if (taskType == TaskType.Deboarding)
            externalDeboardingControlled = false;

        if (taskType == TaskType.Boarding)
            externalBoardingControlled = false;

        if (taskType == TaskType.Deboarding)
        {
            int listenerCount = PassengersDeboarded != null
                ? PassengersDeboarded.GetInvocationList().Length
                : 0;
            Log(
                $"{name}: Deboarding completed; raising PassengersDeboarded event to {listenerCount} listener(s).",
                this);
            PassengersDeboarded?.Invoke(this);
        }

        Log(
            $"{name}: {GetTaskName(taskType)} completed.",
            this
        );

        switch (taskType)
        {
            case TaskType.Deboarding:
                PassengerFlightRevenue passengerRevenue =
                    GetComponent<PassengerFlightRevenue>();

                if (passengerRevenue == null ||
                    passengerRevenue.IncomingPassengers <= 0)
                {
                    // No passengers were carried, so no cleaning vehicle is needed.
                    CompleteTask(TaskType.Cleaning);
                }
                else
                {
                    StartTask(TaskType.Cleaning);
                    EnableExternalCleaning();
                    RequestExternalCleaningServiceIfAvailable();
                }

                StartTask(TaskType.Fueling);
                RequestExternalFuelServiceIfAvailable();
                break;

            case TaskType.BaggageUnloading:
                StartTask(TaskType.BaggageLoading);
                RequestExternalBaggageServiceIfAvailable();
                break;

            case TaskType.Cleaning:
                StartTask(TaskType.Boarding);
                break;
        }

        if (setReadyToDepartAutomatically)
            readyToDepart = AllTasksCompleted();
    }

    private ITaskProgress GetTask(TaskType taskType)
    {
        switch (taskType)
        {
            case TaskType.Deboarding:
                return deboarding;

            case TaskType.BaggageUnloading:
                return baggageUnloading;

            case TaskType.Cleaning:
                return cleaning;

            case TaskType.Fueling:
                return fueling;

            case TaskType.BaggageLoading:
                return baggageLoading;

            case TaskType.Boarding:
                return boarding;

            default:
                throw new ArgumentOutOfRangeException(nameof(taskType));
        }
    }

    private string GetTaskName(TaskType taskType)
    {
        switch (taskType)
        {
            case TaskType.Deboarding:
                return "Deboarding";

            case TaskType.BaggageUnloading:
                return "Baggage unloading";

            case TaskType.Cleaning:
                return "Cleaning";

            case TaskType.Fueling:
                return "Fueling";

            case TaskType.BaggageLoading:
                return "Baggage loading";

            case TaskType.Boarding:
                return "Boarding";

            default:
                return taskType.ToString();
        }
    }

    private bool AllTasksCompleted()
    {
        return deboarding.Status == AircraftTurnaroundTaskStatus.Completed
            && baggageUnloading.Status == AircraftTurnaroundTaskStatus.Completed
            && cleaning.Status == AircraftTurnaroundTaskStatus.Completed
            && fueling.Status == AircraftTurnaroundTaskStatus.Completed
            && baggageLoading.Status == AircraftTurnaroundTaskStatus.Completed
            && boarding.Status == AircraftTurnaroundTaskStatus.Completed;
    }

    [ContextMenu("Debug/Begin Turnaround")]
    private void DebugBeginTurnaround()
    {
        BeginTurnaround();
    }

    [ContextMenu("Debug/Reset Turnaround")]
    private void DebugResetTurnaround()
    {
        turnaroundStarted = false;
        deboardingStartPending = false;
        deboardingStartDelayRemaining = 0f;
        externalDeboardingControlled = false;
        externalFuelingControlled = false;
        fuelTruckRequestRetryTimer = 0f;
        externalCleaningControlled = false;
        externalBoardingControlled = false;
        cleaningVehicleRequestRetryTimer = 0f;
        baggageVehicleRequestRetryTimer = 0f;

        deboarding.ResetTask();
        baggageUnloading.ResetTask();
        cleaning.ResetTask();
        fueling.ResetTask();
        baggageLoading.ResetTask();
        boarding.ResetTask();

        incomingBaggageTransferTotal = 0;
        incomingBaggageTransferred = 0;
        incomingBaggageTransferControlled = false;

        outgoingBaggageTransferTotal = 0;
        outgoingBaggageTransferred = 0;
        outgoingBaggageTransferControlled = false;

        readyToDepart = false;

        Log($"{name}: Turnaround reset.", this);
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
