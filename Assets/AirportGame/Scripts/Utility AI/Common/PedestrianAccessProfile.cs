using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Access memberships assigned to a human/pedestrian prefab.
/// Public access is implicit for every profile.
/// </summary>
[DisallowMultipleComponent]
public class PedestrianAccessProfile : MonoBehaviour
{
    [SerializeField] private List<AccessGroup> accessGroups =
        new List<AccessGroup>();

    public IReadOnlyList<AccessGroup> AccessGroups => accessGroups;

    public bool HasAccessGroup(AccessGroup group)
    {
        if (group == null)
            return false;

        // Public is a built-in membership shared by every pedestrian.
        return group.IsPublic || accessGroups.Contains(group);
    }

    public bool HasAnyAccessGroup(IReadOnlyList<AccessGroup> groups)
    {
        if (groups == null || groups.Count == 0)
            return true;

        for (int i = 0; i < groups.Count; i++)
        {
            if (HasAccessGroup(groups[i]))
                return true;
        }

        return false;
    }
}