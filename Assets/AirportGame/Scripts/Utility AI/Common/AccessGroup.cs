using UnityEngine;

/// <summary>
/// Reusable access category referenced by pedestrian profiles and doors.
/// Create group assets in the Unity project and share them between prefabs.
/// </summary>
[CreateAssetMenu(
    fileName = "AccessGroup",
    menuName = "Airport Game/Access/Access Group")]
public class AccessGroup : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField] private bool isPublic;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    public bool IsPublic => isPublic;
}