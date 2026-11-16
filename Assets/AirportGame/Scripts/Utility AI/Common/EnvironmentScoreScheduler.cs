using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centrally staggers environment-score checks so agents do not each run Update.
/// </summary>
[DefaultExecutionOrder(10000)]
public class EnvironmentScoreScheduler : MonoBehaviour
{
    private const int MaximumAgentsPerFrame = 250;
    private static EnvironmentScoreScheduler instance;
    private readonly List<EnvironmentScoreMonitor> monitors = new List<EnvironmentScoreMonitor>();
    private readonly HashSet<EnvironmentScoreMonitor> registered = new HashSet<EnvironmentScoreMonitor>();
    private int nextIndex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; }

    public static void Register(EnvironmentScoreMonitor monitor)
    {
        if (monitor == null) return;
        EnsureInstance();
        if (instance.registered.Add(monitor)) instance.monitors.Add(monitor);
    }

    public static void Unregister(EnvironmentScoreMonitor monitor)
    {
        if (instance == null || ReferenceEquals(monitor, null)) return;
        if (!instance.registered.Remove(monitor)) return;
        int index = instance.monitors.IndexOf(monitor);
        if (index >= 0)
        {
            instance.monitors.RemoveAt(index);
            if (index < instance.nextIndex) instance.nextIndex--;
        }
        if (instance.monitors.Count == 0) instance.nextIndex = 0;
        else instance.nextIndex %= instance.monitors.Count;
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;
        GameObject schedulerObject = new GameObject("Environment Score Scheduler");
        DontDestroyOnLoad(schedulerObject);
        instance = schedulerObject.AddComponent<EnvironmentScoreScheduler>();
    }

    private void Update()
    {
        int processed = 0;
        while (processed < MaximumAgentsPerFrame && monitors.Count > 0)
        {
            if (nextIndex >= monitors.Count) nextIndex = 0;
            EnvironmentScoreMonitor monitor = monitors[nextIndex];
            if (monitor == null)
            {
                registered.Remove(monitor);
                monitors.RemoveAt(nextIndex);
                continue;
            }
            nextIndex = (nextIndex + 1) % monitors.Count;
            monitor.RefreshIfNeeded();
            processed++;
        }
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
