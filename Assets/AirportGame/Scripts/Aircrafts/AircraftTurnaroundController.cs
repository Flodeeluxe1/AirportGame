
using System;
using UnityEngine;

public enum AircraftTurnaroundTaskStatus
{
    NotStarted,
    InProgress,
    Completed
}

public class AircraftTurnaroundController : MonoBehaviour
{
    [Serializable]
    private sealed class TaskProgress
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

    [Header("Task Durations (In-Game Minutes)")]

    [SerializeField]
    private TaskProgress deboarding = new TaskProgress(10f);

    [SerializeField]
    private TaskProgress baggageUnloading = new TaskProgress(10f);

    [SerializeField]
    private TaskProgress cleaning = new TaskProgress(10f);

    [SerializeField]
    private TaskProgress fueling = new TaskProgress(15f);

    [SerializeField]
    private TaskProgress baggageLoading = new TaskProgress(10f);

    [SerializeField]
    private TaskProgress boarding = new TaskProgress(15f);

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

    private void Awake()
    {
        if (gameTimeManager == null)
        {
            gameTimeManager =
                FindFirstObjectByType<GameTimeManager>();
        }
    }

    private void Update()
    {
        if (!turnaroundStarted)
            return;

        float speedMultiplier =
            gameTimeManager != null
                ? Mathf.Max(0f, gameTimeManager.CurrentSpeedMultiplier)
                : 1f;

        // Ground tasks stop progressing while the game clock is paused.
        if (speedMultiplier > 0f)
        {
            float deltaGameSeconds =
                Time.unscaledDeltaTime * speedMultiplier;

            // Only advance tasks that were already running at the
            // start of this frame. Newly started tasks begin next frame.
            bool tickDeboarding =
                deboarding.Status == AircraftTurnaroundTaskStatus.InProgress;

            bool tickBaggageUnloading =
                baggageUnloading.Status == AircraftTurnaroundTaskStatus.InProgress;

            bool tickCleaning =
                cleaning.Status == AircraftTurnaroundTaskStatus.InProgress;

            bool tickFueling =
                fueling.Status == AircraftTurnaroundTaskStatus.InProgress;

            bool tickBaggageLoading =
                baggageLoading.Status == AircraftTurnaroundTaskStatus.InProgress;

            bool tickBoarding =
                boarding.Status == AircraftTurnaroundTaskStatus.InProgress;

            if (tickDeboarding)
                AdvanceTask(TaskType.Deboarding, deltaGameSeconds);

            if (tickBaggageUnloading)
                AdvanceTask(TaskType.BaggageUnloading, deltaGameSeconds);

            if (tickCleaning)
                AdvanceTask(TaskType.Cleaning, deltaGameSeconds);

            if (tickFueling)
                AdvanceTask(TaskType.Fueling, deltaGameSeconds);

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

        turnaroundStarted = true;

        Debug.Log(
            $"{name}: Turnaround started. Deboarding and baggage unloading begin.",
            this
        );

        StartTask(TaskType.Deboarding);
        StartTask(TaskType.BaggageUnloading);

        if (setReadyToDepartAutomatically)
            readyToDepart = AllTasksCompleted();
    }

    private void AdvanceTask(TaskType taskType, float deltaGameSeconds)
    {
        TaskProgress task = GetTask(taskType);

        if (task.Advance(deltaGameSeconds))
            CompleteTask(taskType);
    }

    private void StartTask(TaskType taskType)
    {
        TaskProgress task = GetTask(taskType);

        if (task.Status != AircraftTurnaroundTaskStatus.NotStarted)
            return;

        task.StartTask();

        Debug.Log(
            $"{name}: {GetTaskName(taskType)} started " +
            $"({task.DurationMinutes:0.#} in-game minutes).",
            this
        );

        // Zero-duration tasks complete immediately, which is helpful
        // for testing and lets later tasks unlock correctly.
        if (task.DurationSeconds <= 0f)
            CompleteTask(taskType);
    }

    private void CompleteTask(TaskType taskType)
    {
        TaskProgress task = GetTask(taskType);

        if (task.Status == AircraftTurnaroundTaskStatus.Completed)
            return;

        task.MarkCompleted();

        Debug.Log(
            $"{name}: {GetTaskName(taskType)} completed.",
            this
        );

        // Start dependent tasks. Independent tasks continue running.
        switch (taskType)
        {
            case TaskType.Deboarding:
                StartTask(TaskType.Cleaning);
                StartTask(TaskType.Fueling);
                break;

            case TaskType.BaggageUnloading:
                StartTask(TaskType.BaggageLoading);
                break;

            case TaskType.Cleaning:
                StartTask(TaskType.Boarding);
                break;
        }

        if (setReadyToDepartAutomatically)
            readyToDepart = AllTasksCompleted();
    }

    private TaskProgress GetTask(TaskType taskType)
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

        deboarding.ResetTask();
        baggageUnloading.ResetTask();
        cleaning.ResetTask();
        fueling.ResetTask();
        baggageLoading.ResetTask();
        boarding.ResetTask();

        readyToDepart = false;

        Debug.Log($"{name}: Turnaround reset.", this);
    }
}