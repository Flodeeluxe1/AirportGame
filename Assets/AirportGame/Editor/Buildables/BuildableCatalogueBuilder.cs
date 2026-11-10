using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BuildableCatalogueBuilder
{
    private const string CataloguePath =
        "Assets/AirportGame/ScriptableObjects/Buildables/BuildCatalog.asset";

    private const string LegacyDefinitionsPath =
        "Assets/AirportGame/ScriptableObjects/Buildables";

    private const string BuildablesRootPath =
        "Assets/AirportGame/Prefabs/Buildables";

    private static bool rebuildQueued;
    private static bool rebuilding;

    static BuildableCatalogueBuilder()
    {
        EditorApplication.delayCall += RebuildCatalogue;
    }

    [MenuItem("Airport/Buildables/Rebuild Catalogue")]
    public static void RebuildCatalogue()
    {
        if (rebuilding)
            return;

        rebuilding = true;

        try
        {
            BuildCatalog catalog =
                AssetDatabase.LoadAssetAtPath<BuildCatalog>(CataloguePath);

            if (catalog == null)
            {
                Debug.LogError(
                    "BuildableCatalogueBuilder: BuildCatalog asset was not found at " +
                    CataloguePath);
                return;
            }

            List<string> searchFolders = new List<string>();

            if (AssetDatabase.IsValidFolder(LegacyDefinitionsPath))
                searchFolders.Add(LegacyDefinitionsPath);

            if (AssetDatabase.IsValidFolder(BuildablesRootPath))
                searchFolders.Add(BuildablesRootPath);

            if (searchFolders.Count == 0)
            {
                Debug.LogError(
                    "BuildableCatalogueBuilder: No buildable asset folders were found.");
                return;
            }

            string[] guids =
                AssetDatabase.FindAssets("t:BuildableDefinition", searchFolders.ToArray());

            List<BuildableDefinition> definitions = new List<BuildableDefinition>();
            Dictionary<string, BuildableDefinition> definitionsById =
                new Dictionary<string, BuildableDefinition>(StringComparer.Ordinal);

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                BuildableDefinition definition =
                    AssetDatabase.LoadAssetAtPath<BuildableDefinition>(assetPath);

                if (definition == null)
                    continue;

                if (definition.prefab == null)
                {
                    Debug.LogError(
                        "BuildableCatalogueBuilder: Definition '" +
                        definition.name + "' has no prefab assigned. Asset: " +
                        assetPath);
                }

                string menuCategory = GetMenuCategory(assetPath, definition);

                if (definition.menuCategory != menuCategory)
                {
                    definition.menuCategory = menuCategory;
                    EditorUtility.SetDirty(definition);
                }

                if (string.IsNullOrWhiteSpace(definition.id))
                {
                    Debug.LogError(
                        "BuildableCatalogueBuilder: Definition '" +
                        definition.name + "' has an empty ID. Asset: " +
                        assetPath);
                }
                else if (definitionsById.TryGetValue(
                    definition.id, out BuildableDefinition existing))
                {
                    Debug.LogError(
                        "BuildableCatalogueBuilder: Duplicate buildable ID '" +
                        definition.id + "' on '" + existing.name + "' and '" +
                        definition.name + "'.");
                }
                else
                {
                    definitionsById.Add(definition.id, definition);
                }

                definitions.Add(definition);
            }

            definitions = definitions
                .OrderBy(d => d.menuCategory, StringComparer.Ordinal)
                .ThenBy(d => d.displayName, StringComparer.Ordinal)
                .ThenBy(d => d.id, StringComparer.Ordinal)
                .ToList();

            BuildableDefinition[] rebuilt =
                definitions.ToArray();

            bool catalogueChanged =
                catalog.buildables == null ||
                !catalog.buildables.SequenceEqual(rebuilt);

            if (catalogueChanged)
            {
                catalog.buildables = rebuilt;
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();

            Debug.Log(
                "BuildableCatalogueBuilder: Catalogue validation finished. " +
                definitions.Count + " definitions discovered; " +
                definitions.Count(d => d.prefab != null) +
                " have prefabs assigned.");
        }
        finally
        {
            rebuilding = false;
            rebuildQueued = false;
        }
    }

    private static string GetMenuCategory(
        string assetPath,
        BuildableDefinition definition)
    {
        string normalizedPath = assetPath.Replace('\\\\', '/');
        string airportRoot = BuildablesRootPath + "/Airport/";

        if (normalizedPath.StartsWith(
            airportRoot, StringComparison.OrdinalIgnoreCase))
        {
            string relativePath = normalizedPath.Substring(airportRoot.Length);
            int separatorIndex = relativePath.IndexOf('/');

            if (separatorIndex > 0)
                return relativePath.Substring(0, separatorIndex);

            Debug.LogWarning(
                "BuildableCatalogueBuilder: Airport buildable '" +
                definition.name + "' is not inside a category folder: " +
                assetPath);
            return "Uncategorised";
        }

        return definition.category.ToString();
    }

    private static void QueueRebuild()
    {
        if (rebuildQueued || rebuilding)
            return;

        rebuildQueued = true;
        EditorApplication.delayCall += RebuildCatalogue;
    }

    private class BuildableAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (TouchesBuildableAssets(importedAssets) ||
                TouchesBuildableAssets(deletedAssets) ||
                TouchesBuildableAssets(movedAssets) ||
                TouchesBuildableAssets(movedFromAssetPaths))
            {
                QueueRebuild();
            }
        }

        private static bool TouchesBuildableAssets(string[] paths)
        {
            foreach (string path in paths)
            {
                if (path.StartsWith(
                        LegacyDefinitionsPath + "/",
                        StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(
                        BuildablesRootPath + "/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
