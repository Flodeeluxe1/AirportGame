using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional access rule for a placed door. An empty allowed-group list
/// leaves the door unrestricted. Otherwise a pedestrian needs at least
/// one matching group. Public is implicitly assigned to every pedestrian.
/// </summary>
[DisallowMultipleComponent]
public class DoorAccessController : MonoBehaviour
{
    public static event System.Action AccessRulesChanged;
    [SerializeField] private List<AccessGroup> allowedGroups =
        new List<AccessGroup>();

    public IReadOnlyList<AccessGroup> AllowedGroups => allowedGroups;

    /// <summary>
    /// Replaces this door's access rules with the groups restored from an
    /// airport save. An empty list intentionally makes the door unrestricted.
    /// </summary>
    public void RestoreAllowedGroups(IReadOnlyList<AccessGroup> groups)
    {
        allowedGroups = groups != null
            ? new List<AccessGroup>(groups)
            : new List<AccessGroup>();

        AccessRulesChanged?.Invoke();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            AccessRulesChanged?.Invoke();
    }

    public bool CanPass(PedestrianAccessProfile pedestrian)
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        // No configured restrictions means the door remains public.
        if (allowedGroups == null || allowedGroups.Count == 0)
            return true;

        // An NPC without a profile is treated as public-only.
        if (pedestrian == null)
        {
            for (int i = 0; i < allowedGroups.Count; i++)
            {
                if (allowedGroups[i] != null && allowedGroups[i].IsPublic)
                    return true;
            }

            return false;
        }

        return pedestrian.HasAnyAccessGroup(allowedGroups);
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("DoorAccessController.CanPass", performanceTimingStart);
        }
    }
}