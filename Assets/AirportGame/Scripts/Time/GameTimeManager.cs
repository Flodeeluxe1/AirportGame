
using UnityEngine;

public class GameTimeManager : MonoBehaviour
{
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

    private double gameSeconds;
    private float baseFixedDeltaTime;

    public int CurrentSpeedMultiplier =>
        currentSpeedMultiplier;

    public bool IsPaused =>
        currentSpeedMultiplier == 0;

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
        baseFixedDeltaTime =
            Time.fixedDeltaTime;

        gameSeconds =
            startHour * 3600.0 +
            startMinute * 60.0 +
            startSecond;

        SetSpeed(1);
    }

    private void Update()
    {
        if (currentSpeedMultiplier == 0)
            return;

        // Use unscaled time because Time.timeScale is
        // already applied to the rest of the simulation.
        gameSeconds +=
            Time.unscaledDeltaTime *
            currentSpeedMultiplier;

        // Wrap the clock at midnight.
        if (gameSeconds >= SecondsPerDay)
        {
            gameSeconds %= SecondsPerDay;
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
                    Debug.LogWarning(
                        $"GameTimeManager: Unsupported speed {multiplier}."
                    );

                    return;
                }

        currentSpeedMultiplier = multiplier;

        Time.timeScale = multiplier;

        // Keep physics stepping consistent at each
        // simulation speed. While paused, timeScale
        // stops physics without requiring a zero timestep.
        if (multiplier > 0)
        {
            Time.fixedDeltaTime =
                baseFixedDeltaTime * multiplier;
        }

        Debug.Log(
            $"GAME SPEED | " +
            $"{(multiplier == 0 ? "PAUSED" : multiplier + "x")} | " +
            $"Game Time: {FormattedTime}"
        );
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
        // Avoid leaving the editor or next scene paused
        // if this manager is destroyed.
        Time.timeScale = 1f;

        if (baseFixedDeltaTime > 0f)
        {
            Time.fixedDeltaTime =
                baseFixedDeltaTime;
        }
    }
}