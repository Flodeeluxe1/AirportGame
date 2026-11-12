using UnityEngine;

[CreateAssetMenu(
    fileName = "WallFinish",
    menuName = "Airport/Wall Finishes/Finish Definition"
)]
public class WallFinishDefinition : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName;

    [Header("Rendering")]
    [Tooltip("A URP-compatible PBR material used by the wall renderer.")]
    public Material material;

    [Header("Build Menu")]
    public Texture2D icon;
}
