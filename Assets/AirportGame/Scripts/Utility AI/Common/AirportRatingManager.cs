using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Session-wide airport rating built from the time-weighted experiences of
/// active and completed visitors. Created automatically when the first visitor registers.
/// </summary>
[DisallowMultipleComponent]
public class AirportRatingManager : MonoBehaviour
{
    private const int MaximumVisitorsPerFrame = 250;
    private static AirportRatingManager instance;

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    public static AirportRatingManager RegisterVisitor(AirportVisitorExperience visitor)
    {
        if (visitor == null)
            return null;

        EnsureInstance();
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

    private static void EnsureInstance()
    {
        if (instance != null)
            return;

        GameObject managerObject = new GameObject("Airport Rating Manager");
        DontDestroyOnLoad(managerObject);
        instance = managerObject.AddComponent<AirportRatingManager>();
    }

    private void Update()
    {
        float smoothingTime = Mathf.Max(0.01f, ratingSmoothingTimeSeconds);
        float alpha = 1f - Mathf.Exp(-Time.unscaledDeltaTime / smoothingTime);
        airportRating = Mathf.Lerp(airportRating, rawAirportRating, alpha);

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
}
