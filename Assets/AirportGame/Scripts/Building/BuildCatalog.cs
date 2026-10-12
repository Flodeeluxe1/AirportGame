using UnityEngine;

[CreateAssetMenu(
    fileName = "BuildCatalog",
    menuName = "Airport/Building/Build Catalog"
)]
public class BuildCatalog : ScriptableObject
{
    public BuildableDefinition[] buildables;
}