using UnityEngine;

/// <summary>
/// Periodically samples the environment score at this agent's world position.
/// </summary>
[DisallowMultipleComponent]
public class PilotEnvironmentMonitor : MonoBehaviour
{
    [Header("Sampling")]
    [Min(0.1f)]
    [SerializeField] private float updateInterval = 1f;

    [Header("Runtime")]
    [SerializeField, Range(0f, 100f)] private float currentEnvironmentScore =
        EnvironmentScoreCalculator.NeutralScore;

    private float nextUpdateTime;

    public float CurrentEnvironmentScore => currentEnvironmentScore;

    private void OnEnable()
    {
        RefreshScore();
        nextUpdateTime = Time.time + Mathf.Max(0.1f, updateInterval);
    }

    private void Update()
    {
        if (Time.time < nextUpdateTime)
            return;

        RefreshScore();
        nextUpdateTime = Time.time + Mathf.Max(0.1f, updateInterval);
    }

    public void RefreshScore()
    {
        currentEnvironmentScore =
            EnvironmentScoreCalculator.CalculateScore(transform.position);
    }

    private void OnValidate()
    {
        updateInterval = Mathf.Max(0.1f, updateInterval);
    }
}
