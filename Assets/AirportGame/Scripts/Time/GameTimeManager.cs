
using System;
using UnityEngine;

public class GameTimeManager : MonoBehaviour
{
    private static GameTimeManager instance;

    /// <summary>
    /// The active game-time controller, if one exists.
    /// </summary>
    public static GameTimeManager Instance => instance;

    /// <summary>
    /// Elapsed simulation seconds for this rendered frame.
    /// This API allows gameplay systems to migrate away from Unity's global time scale.
    /// </summary>
    // Gameplay simulation time is independent of Unity's global time scale.
    // Migrated systems receive the full selected game-speed delta, even when
    // Unity's own timeScale is capped for legacy systems and physics.
    public static float SimulationDeltaTime =>
        instance != null
            ? Time.unscaledDeltaTime * instance.currentSpeedMultiplier
            : Time.deltaTime;

    /// <summary>
    /// Monotonic in-game time in seconds since day one, independent of rendered frames.
    /// Use double precision for timers that can span many game days.
    /// </summary>
    public static double SimulationTime =>
        instance != null
            ? instance.GetAbsoluteGameSecondsPrecise()
            : Time.time;

    /// <summary>
    /// Real elapsed seconds for this rendered frame, unaffected by game speed or pause.
    /// </summary>
    public static float RealDeltaTime => Time.unscaledDeltaTime;
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    private const double SecondsPerDay = 86400.0;

    [Header("Initial Game Time")]
    [Range(0, 23)]
    [SerializeField] private int startHour = 8;

    [Range(0, 59)]
    [SerializeField] private int startMinute = 0;

    [Range(0, 59)]
    [SerializeField] private int startSecond = 0;

    [Header("Runtime")]
    [SerializeField] private int currentSpeedMultiplier = 1;
    [SerializeField] private long currentDay = 1;

    private double gameSeconds;
    private SimulationEventScheduler eventScheduler;

    /// <summary>
    /// Central queue for simulation-time events. Events only advance while game time advances.
    /// </summary>
    public SimulationEventScheduler EventScheduler => eventScheduler;
    private float baseFixedDeltaTime;

    public event Action<long, long> GameTimeAdvanced;
    public event Action<int> GameSpeedChanged;

    public int CurrentSpeedMultiplier =>
        currentSpeedMultiplier;

    public bool IsPaused =>
        currentSpeedMultiplier == 0;

    public long CurrentDay =>
        currentDay;

    public int Hour =>
        (int)(gameSeconds / 3600.0);

    public int Minute =>
        (int)((gameSeconds % 3600.0) / 60.0);

    public int Second =>
        (int)(gameSeconds % 60.0);

    public string FormattedTime =>
        $"{Hour:00}:{Minute:00}:{Second:00}";

    private void Awake()
    {
        instance = this;
        eventScheduler = new SimulationEventScheduler();

        baseFixedDeltaTime =
            Time.fixedDeltaTime;

        gameSeconds =
            startHour * 3600.0 +
            startMinute * 60.0 +
            startSecond;

        currentDay = 1;

        SetSpeed(1);
    }

    private void Update()
    {
        if (currentSpeedMultiplier == 0)
            return;

        long previousAbsoluteSecond = GetAbsoluteGameSecond();

        double totalGameSeconds =
            gameSeconds +
            Time.unscaledDeltaTime *
            currentSpeedMultiplier;

        if (totalGameSeconds >= SecondsPerDay)
        {
            long daysPassed =
                (long)System.Math.Floor(
                    totalGameSeconds / SecondsPerDay
                );

            currentDay += daysPassed;
        }

        gameSeconds =
            totalGameSeconds % SecondsPerDay;

        NotifyGameTimeAdvanced(previousAbsoluteSecond);

        if (eventScheduler != null)
            eventScheduler.ProcessDueEvents(GetAbsoluteGameSecondsPrecise());
    }

    private long GetAbsoluteGameSecond()
    {
        return (currentDay - 1) * (long)SecondsPerDay +
               (long)System.Math.Floor(gameSeconds);
    }

    private double GetAbsoluteGameSecondsPrecise()
    {
        return (currentDay - 1) * SecondsPerDay + gameSeconds;
    }

    private void NotifyGameTimeAdvanced(long previousAbsoluteSecond)
    {
        long currentAbsoluteSecond = GetAbsoluteGameSecond();

        if (currentAbsoluteSecond != previousAbsoluteSecond)
        {
            GameTimeAdvanced?.Invoke(
                previousAbsoluteSecond,
                currentAbsoluteSecond
            );
        }
    }

    public void SetSpeed(int multiplier)
    {
        if (multiplier != 0 &&
            multiplier != 1 &&
            multiplier != 10 &&
            multiplier != 100 &&
            multiplier != 1000)
        {
            LogWarning(
                $"GameTimeManager: Unsupported speed {multiplier}."
            );

            return;
        }

        int previousMultiplier = currentSpeedMultiplier;
        currentSpeedMultiplier = multiplier;

        // Unity Editor limits Time.timeScale to 100. Keep Unity's legacy
        // systems and physics at no more than that; migrated gameplay systems
        // use SimulationDeltaTime and continue at the selected 1000x speed.
        int unityTimeScale = Mathf.Min(multiplier, 100);
        Time.timeScale = unityTimeScale;

        if (multiplier > 0)
        {
            Time.fixedDeltaTime =
                baseFixedDeltaTime * unityTimeScale;
        }

        if (previousMultiplier != multiplier)
            GameSpeedChanged?.Invoke(multiplier);

        Log(
            $"GAME SPEED | " +
            $"{(multiplier == 0 ? "PAUSED" : multiplier + "x")} | " +
            $"Day {currentDay} | Game Time: {FormattedTime}"
        );
    }

    public int GetGameTimeSeconds()
    {
        return Mathf.Clamp(
            (int)System.Math.Floor(gameSeconds),
            0,
            86399
        );
    }

    public void SetGameTimeSeconds(int seconds)
    {
        SetGameDate(currentDay, seconds);
    }

    public void SetGameDate(long day, int seconds)
    {
        long previousAbsoluteSecond = GetAbsoluteGameSecond();
        currentDay = System.Math.Max(1L, day);
        gameSeconds = Mathf.Clamp(seconds, 0, 86399);
        NotifyGameTimeAdvanced(previousAbsoluteSecond);
    }

    public void ResetToStartTime()
    {
        long previousAbsoluteSecond = GetAbsoluteGameSecond();

        gameSeconds =
            startHour * 3600.0 +
            startMinute * 60.0 +
            startSecond;

        currentDay = 1;
        NotifyGameTimeAdvanced(previousAbsoluteSecond);
    }

    public void Pause()
    {
        SetSpeed(0);
    }

    public void SetNormalSpeed()
    {
        SetSpeed(1);
    }

    public void SetTenfoldSpeed()
    {
        SetSpeed(10);
    }

    public void SetHundredfoldSpeed()
    {
        SetSpeed(100);
    }

    public void SetThousandfoldSpeed()
    {
        SetSpeed(1000);
    }

    private void OnDestroy()
    {
        if (eventScheduler != null)
        {
            eventScheduler.Clear();
            eventScheduler = null;
        }

        if (instance == this)
            instance = null;

        Time.timeScale = 1f;

        if (baseFixedDeltaTime > 0f)
        {
            Time.fixedDeltaTime =
                baseFixedDeltaTime;
        }
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
