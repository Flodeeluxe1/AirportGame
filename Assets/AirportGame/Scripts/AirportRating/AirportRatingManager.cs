using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Session-wide airport rating built from the time-weighted experiences of
/// active and completed visitors. Add this component to a scene GameObject manually.
/// </summary>
[DisallowMultipleComponent]
public class AirportRatingManager : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    private const int MaximumVisitorsPerFrame = 250;
    private static AirportRatingManager instance;

    [Header("Airport Progression")]
    [SerializeField, Range(0, 5)] private int airportLevel = 5;

    [Header("Rating Weights")]
    [SerializeField, Min(0f)] private float visitorSatisfactionWeight = 0.75f;
    [SerializeField, Min(0f)] private float environmentWeight = 0.25f;
    [SerializeField, Min(0.01f)] private float ratingSmoothingTimeSeconds = 30f;

    [Header("Runtime Statistics")]
    [SerializeField] private int completedVisitors;
    [SerializeField] private double recordedVisitorHours;
    [SerializeField, Range(0f, 100f)] private float visitorSatisfaction = 50f;
    [SerializeField, Range(0f, 100f)] private float environmentExperience = 50f;
    [SerializeField, Range(0f, 100f)] private float rawAirportRating = 50f;
    [SerializeField, Range(0f, 100f)] private float airportRating = 50f;

    private GameTimeManager gameTimeManager;

    private readonly List<AirportVisitorExperience> activeVisitors =
        new List<AirportVisitorExperience>();
    private readonly HashSet<AirportVisitorExperience> registeredVisitors =
        new HashSet<AirportVisitorExperience>();

    private double totalVisitorSeconds;
    private double satisfactionSeconds;
    private double environmentSeconds;
    private int nextVisitorIndex;

    public static AirportRatingManager Instance => instance;
    public int ActiveVisitorCount => activeVisitors.Count;
    public int CompletedVisitors => completedVisitors;
    public double RecordedVisitorHours => recordedVisitorHours;
    public float VisitorSatisfaction => visitorSatisfaction;
    public float EnvironmentExperience => environmentExperience;
    public float RawAirportRating => rawAirportRating;
    public float AirportRating => airportRating;
    public int AirportLevel => airportLevel;

    public event Action<float> AirportRatingChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    private void OnEnable()
    {
        if (instance != null && instance != this)
        {
            LogError("Only one AirportRatingManager may be active in the scene.", this);
            enabled = false;
            return;
        }

        instance = this;
        TrySubscribeToGameTimeManager();
    }

    private void OnDisable()
    {
        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;

        gameTimeManager = null;
    }

    private void TrySubscribeToGameTimeManager()
    {
        if (gameTimeManager != null)
            return;

        gameTimeManager = FindAnyObjectByType<GameTimeManager>();
        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
    }

    private void HandleGameTimeAdvanced(long previousSecond, long currentSecond)
    {
        long elapsedGameSeconds = currentSecond - previousSecond;
        if (elapsedGameSeconds <= 0)
            return;

        float smoothingTime = Mathf.Max(0.01f, ratingSmoothingTimeSeconds);
        float alpha = 1f - Mathf.Exp(-elapsedGameSeconds / smoothingTime);
        float previousRating = airportRating;
        airportRating = Mathf.Lerp(airportRating, rawAirportRating, alpha);

        if (!Mathf.Approximately(previousRating, airportRating))
            AirportRatingChanged?.Invoke(airportRating);
    }

    public static AirportRatingManager RegisterVisitor(AirportVisitorExperience visitor)
    {
        if (visitor == null)
            return null;

        if (instance == null)
            instance = FindAnyObjectByType<AirportRatingManager>();

        if (instance == null || !instance.isActiveAndEnabled)
            return null;

        if (instance.registeredVisitors.Add(visitor))
            instance.activeVisitors.Add(visitor);

        return instance;
    }

    public void UnregisterVisitor(AirportVisitorExperience visitor)
    {
        if (ReferenceEquals(visitor, null) || !registeredVisitors.Remove(visitor))
            return;

        int index = activeVisitors.IndexOf(visitor);
        if (index >= 0)
        {
            activeVisitors.RemoveAt(index);
            if (index < nextVisitorIndex)
                nextVisitorIndex--;
        }

        if (activeVisitors.Count == 0)
            nextVisitorIndex = 0;
        else
            nextVisitorIndex %= activeVisitors.Count;

        completedVisitors++;
        RefreshDisplayedRating();
    }

    public void AddExperience(double elapsedGameSeconds, float satisfaction, float environment)
    {
        if (elapsedGameSeconds <= 0d)
            return;

        double clampedSatisfaction = Mathf.Clamp(satisfaction, 0f, 100f);
        double clampedEnvironment = Mathf.Clamp(environment, 0f, 100f);

        totalVisitorSeconds += elapsedGameSeconds;
        satisfactionSeconds += clampedSatisfaction * elapsedGameSeconds;
        environmentSeconds += clampedEnvironment * elapsedGameSeconds;

        RefreshDisplayedRating();
    }

    private void Update()
    {
        TrySubscribeToGameTimeManager();

        int processed = 0;
        while (processed < MaximumVisitorsPerFrame && activeVisitors.Count > 0)
        {
            if (nextVisitorIndex >= activeVisitors.Count)
                nextVisitorIndex = 0;

            AirportVisitorExperience visitor = activeVisitors[nextVisitorIndex];
            if (visitor == null)
            {
                registeredVisitors.Remove(visitor);
                activeVisitors.RemoveAt(nextVisitorIndex);
                continue;
            }

            nextVisitorIndex = (nextVisitorIndex + 1) % activeVisitors.Count;
            visitor.AccumulateExperienceToNow();
            processed++;
        }
    }

    private void RefreshDisplayedRating()
    {
        if (totalVisitorSeconds <= 0d)
        {
            visitorSatisfaction = 50f;
            environmentExperience = 50f;
            rawAirportRating = 50f;
            recordedVisitorHours = 0d;
            return;
        }

        visitorSatisfaction = (float)(satisfactionSeconds / totalVisitorSeconds);
        environmentExperience = (float)(environmentSeconds / totalVisitorSeconds);
        recordedVisitorHours = totalVisitorSeconds / 3600d;

        float totalWeight = visitorSatisfactionWeight + environmentWeight;
        if (totalWeight <= 0f)
        {
            rawAirportRating = 50f;
            return;
        }

        rawAirportRating = Mathf.Clamp(
            (visitorSatisfaction * visitorSatisfactionWeight +
             environmentExperience * environmentWeight) / totalWeight,
            0f,
            100f);
    }

    private void OnValidate()
    {
        airportLevel = Mathf.Clamp(airportLevel, 0, 5);
        visitorSatisfactionWeight = Mathf.Max(0f, visitorSatisfactionWeight);
        environmentWeight = Mathf.Max(0f, environmentWeight);
        ratingSmoothingTimeSeconds = Mathf.Max(0.01f, ratingSmoothingTimeSeconds);
        if (Application.isPlaying)
            RefreshDisplayedRating();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
