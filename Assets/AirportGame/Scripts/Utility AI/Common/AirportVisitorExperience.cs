using UnityEngine;

/// <summary>
/// Records a visitor's needs satisfaction and experienced environment over
/// in-game time. Attach to any visitor with UtilityNeeds and EnvironmentScoreMonitor.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(EnvironmentScoreMonitor))]
public class AirportVisitorExperience : MonoBehaviour
{
    private static readonly UtilityNeeds.NeedType[] NeedTypes =
        (UtilityNeeds.NeedType[])System.Enum.GetValues(typeof(UtilityNeeds.NeedType));

    [Header("Runtime Experience")]
    [SerializeField, Range(0f, 100f)] private float currentNeedSatisfaction = 50f;
    [SerializeField, Range(0f, 100f)] private float currentEnvironmentExperience = 50f;
    [SerializeField] private double recordedVisitGameMinutes;

    private UtilityNeeds needs;
    private EnvironmentScoreMonitor environmentMonitor;
    private GameTimeManager gameTimeManager;
    private AirportRatingManager ratingManager;
    private long lastRecordedGameSecond;
    private bool isTracking;

    public float CurrentNeedSatisfaction => currentNeedSatisfaction;
    public float CurrentEnvironmentExperience => currentEnvironmentExperience;
    public double RecordedVisitGameMinutes => recordedVisitGameMinutes;

    private void Awake()
    {
        needs = GetComponent<UtilityNeeds>();
        environmentMonitor = GetComponent<EnvironmentScoreMonitor>();
        gameTimeManager = FindAnyObjectByType<GameTimeManager>();
    }

    private void OnEnable()
    {
        if (needs == null)
            needs = GetComponent<UtilityNeeds>();
        if (environmentMonitor == null)
            environmentMonitor = GetComponent<EnvironmentScoreMonitor>();
        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();

        if (needs == null || environmentMonitor == null || gameTimeManager == null)
        {
            Debug.LogWarning(
                name + ": Airport visitor experience requires UtilityNeeds, " +
                "EnvironmentScoreMonitor and GameTimeManager.",
                this);
            return;
        }

        environmentMonitor.RefreshNow();
        currentNeedSatisfaction = CalculateNeedSatisfaction();
        currentEnvironmentExperience = environmentMonitor.SmoothedEnvironmentScore;
        lastRecordedGameSecond = GetCurrentGameSecond();

        needs.NeedsChanged += HandleNeedsChanged;
        environmentMonitor.ScoreUpdated += HandleEnvironmentScoreUpdated;

        ratingManager = AirportRatingManager.RegisterVisitor(this);
        isTracking = ratingManager != null;
        if (!isTracking)
        {
            needs.NeedsChanged -= HandleNeedsChanged;
            environmentMonitor.ScoreUpdated -= HandleEnvironmentScoreUpdated;
            Debug.LogWarning(
                name + ": No active AirportRatingManager found. Add the component to your scene's AirportRatingManager GameObject.",
                this);
        }
    }

    private void OnDisable()
    {
        if (!isTracking)
            return;

        AccumulateExperienceToNow();
        needs.NeedsChanged -= HandleNeedsChanged;
        environmentMonitor.ScoreUpdated -= HandleEnvironmentScoreUpdated;
        ratingManager.UnregisterVisitor(this);

        ratingManager = null;
        isTracking = false;
    }

    public void AccumulateExperienceToNow()
    {
        if (!isTracking || gameTimeManager == null || ratingManager == null)
            return;

        long currentGameSecond = GetCurrentGameSecond();
        if (currentGameSecond <= lastRecordedGameSecond)
        {
            lastRecordedGameSecond = currentGameSecond;
            return;
        }

        long elapsedSeconds = currentGameSecond - lastRecordedGameSecond;
        ratingManager.AddExperience(
            elapsedSeconds,
            currentNeedSatisfaction,
            currentEnvironmentExperience);

        recordedVisitGameMinutes += elapsedSeconds / 60d;
        lastRecordedGameSecond = currentGameSecond;
    }

    private void HandleNeedsChanged()
    {
        AccumulateExperienceToNow();
        currentNeedSatisfaction = CalculateNeedSatisfaction();
    }

    private void HandleEnvironmentScoreUpdated(float rawScore, float smoothedScore)
    {
        AccumulateExperienceToNow();
        currentEnvironmentExperience = smoothedScore;
    }

    private float CalculateNeedSatisfaction()
    {
        if (needs == null || NeedTypes.Length == 0)
            return 50f;

        float total = 0f;
        for (int i = 0; i < NeedTypes.Length; i++)
            total += needs.GetValue(NeedTypes[i]);

        return total / NeedTypes.Length;
    }

    private long GetCurrentGameSecond()
    {
        return (gameTimeManager.CurrentDay - 1) * 86400L +
               gameTimeManager.GetGameTimeSeconds();
    }
}
