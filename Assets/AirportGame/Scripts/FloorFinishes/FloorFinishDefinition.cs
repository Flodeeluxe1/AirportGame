using UnityEngine;

[CreateAssetMenu(
    fileName = "FloorFinish",
    menuName = "Airport/Floor Finishes/Finish Definition"
)]
public class FloorFinishDefinition : ScriptableObject
{
    [Header("Identity")]
    public string id;
    public string displayName;

    [Header("Rendering")]
    [Tooltip("A URP-compatible PBR material used by the chunk renderer.")]
    public Material material;

    [Header("Build Menu")]
    public Texture2D icon;
}
