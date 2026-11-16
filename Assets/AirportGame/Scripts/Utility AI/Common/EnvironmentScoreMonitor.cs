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

    [Header("Runtime")]
    [SerializeField, Range(0f, 100f)] private float currentEnvironmentScore = EnvironmentScoreCalculator.NeutralScore;

    private Vector3 lastSampledPosition;
    private int lastEnvironmentVersion = -1;
    private bool hasSample;

    public float CurrentEnvironmentScore => currentEnvironmentScore;

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

        if (hasSample && currentVersion == lastEnvironmentVersion &&
            (currentPosition - lastSampledPosition).sqrMagnitude < threshold * threshold)
            return;

        RefreshScore(currentPosition, currentVersion);
    }

    public void RefreshNow()
    {
        RefreshScore(transform.position, EnvironmentScoreCalculator.Version);
    }

    private void RefreshScore(Vector3 position, int environmentVersion)
    {
        currentEnvironmentScore = EnvironmentScoreCalculator.CalculateScore(position);
        lastSampledPosition = position;
        lastEnvironmentVersion = environmentVersion;
        hasSample = true;
    }

    private void OnValidate()
    {
        movementThreshold = Mathf.Max(0.1f, movementThreshold);
    }
}
