using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class BuildableSurfaceMigration
{
    static BuildableSurfaceMigration()
    {
        // Run after the AssetDatabase is ready. The operation is idempotent:
        // assets with an explicitly configured flags value are left untouched.
        EditorApplication.delayCall += MigrateLegacySurfaceValues;
    }

    [MenuItem("Tools/Airport/Migrate Buildable Surface Flags")]
    private static void RunMigrationFromMenu()
    {
        MigrateLegacySurfaceValues();
    }

    private static void MigrateLegacySurfaceValues()
    {
        string[] assetGuids = AssetDatabase.FindAssets("t:BuildableDefinition");
        int migratedCount = 0;

        foreach (string guid in assetGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            BuildableDefinition definition =
                AssetDatabase.LoadAssetAtPath<BuildableDefinition>(path);

            if (definition == null ||
                definition.allowedSurfaces != BuildSurfaceOptions.None)
            {
                continue;
            }

            BuildSurfaceOptions migratedValue = ConvertLegacySurface(
                definition.requiredSurface
            );

            if (migratedValue == BuildSurfaceOptions.None)
                continue;

            definition.allowedSurfaces = migratedValue;
            EditorUtility.SetDirty(definition);
            migratedCount++;
        }

        if (migratedCount == 0)
            return;

        AssetDatabase.SaveAssets();
        Debug.Log(
            $"Buildable surface migration: restored allowed surfaces on {migratedCount} BuildableDefinition assets."
        );
    }

    private static BuildSurfaceOptions ConvertLegacySurface(BuildSurface surface)
    {
        return surface switch
        {
            BuildSurface.Grass => BuildSurfaceOptions.Grass,
            BuildSurface.Foundation => BuildSurfaceOptions.Foundation,
            BuildSurface.Tarmac => BuildSurfaceOptions.Tarmac,
            BuildSurface.Taxiway => BuildSurfaceOptions.Taxiway,
            BuildSurface.Runway => BuildSurfaceOptions.Runway,
            BuildSurface.PedestrianTarmac => BuildSurfaceOptions.PedestrianTarmac,
            BuildSurface.Any => BuildSurfaceOptions.All,
            _ => BuildSurfaceOptions.None
        };
    }
}
