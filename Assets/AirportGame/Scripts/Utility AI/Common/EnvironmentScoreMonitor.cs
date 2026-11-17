using System;
using UnityEngine;

/// <summary>
/// Shared cached environment score for any moving AI agent, including pilots
/// and passengers. Updates are scheduled centrally rather than per-agent Update.
/// </summary>
[DisallowMultipleComponent]
public class EnvironmentScoreMonitor : MonoBehaviour
{
    [Header("Sampling")]
    [Min(0.1f)]
    [SerializeField] private float movementThreshold = 1f;

    [Min(0.01f)]
    [SerializeField] private float smoothingTime = 5f;

    [Header("Runtime")]
    [SerializeField, Range(0f, 100f)]
    private float rawEnvironmentScore = EnvironmentScoreCalculator.NeutralScore;

    [SerializeField, Range(0f, 100f)]
    private float smoothedEnvironmentScore = EnvironmentScoreCalculator.NeutralScore;

    private Vector3 lastSampledPosition;
    private int lastEnvironmentVersion = -1;
    private float lastSmoothingTime;
    private bool hasSample;

    public event Action<float, float> ScoreUpdated;

    public float RawEnvironmentScore => rawEnvironmentScore;
    public float SmoothedEnvironmentScore => smoothedEnvironmentScore;

    // Kept as the main score property so existing AI consumers use the smoothed value.
    public float CurrentEnvironmentScore => smoothedEnvironmentScore;

    private void OnEnable()
    {
        EnvironmentScoreScheduler.Register(this);
    }

    private void OnDisable()
    {
        EnvironmentScoreScheduler.Unregister(this);
    }

    public void RefreshIfNeeded()
    {
        Vector3 currentPosition = transform.position;
        int currentVersion = EnvironmentScoreCalculator.Version;
        float threshold = Mathf.Max(0.1f, movementThreshold);

        bool needsRawRefresh = !hasSample ||
            currentVersion != lastEnvironmentVersion ||
            (currentPosition - lastSampledPosition).sqrMagnitude >= threshold * threshold;

        RefreshScore(currentPosition, currentVersion, needsRawRefresh);
    }

    public void RefreshNow()
    {
        RefreshScore(transform.position, EnvironmentScoreCalculator.Version, true);
    }

    private void RefreshScore(Vector3 position, int environmentVersion, bool refreshRawScore)
    {
        float previousRawScore = rawEnvironmentScore;
        float previousSmoothedScore = smoothedEnvironmentScore;

        if (refreshRawScore)
        {
            rawEnvironmentScore = EnvironmentScoreCalculator.CalculateScore(position);
            lastSampledPosition = position;
            lastEnvironmentVersion = environmentVersion;
        }

        float now = Time.unscaledTime;
        if (!hasSample)
        {
            smoothedEnvironmentScore = rawEnvironmentScore;
            hasSample = true;
        }
        else
        {
            float elapsed = Mathf.Max(0f, now - lastSmoothingTime);
            float timeConstant = Mathf.Max(0.01f, smoothingTime);
            float alpha = 1f - Mathf.Exp(-elapsed / timeConstant);
            smoothedEnvironmentScore +=
                alpha * (rawEnvironmentScore - smoothedEnvironmentScore);
        }

        lastSmoothingTime = now;

        if (!Mathf.Approximately(previousRawScore, rawEnvironmentScore) ||
            !Mathf.Approximately(previousSmoothedScore, smoothedEnvironmentScore))
        {
            ScoreUpdated?.Invoke(rawEnvironmentScore, smoothedEnvironmentScore);
        }
    }

    private void OnValidate()
    {
        movementThreshold = Mathf.Max(0.1f, movementThreshold);
        smoothingTime = Mathf.Max(0.01f, smoothingTime);
    }
}
