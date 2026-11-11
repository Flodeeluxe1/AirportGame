using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BuildablePreviewGenerator
{
    private const string GeneratedFolder =
        "Assets/AirportGame/Generated/BuildablePreviews";

    private const int MaxPreviewWaitFrames = 300;

    private static List<BuildableDefinition> pendingDefinitions;
    private static int currentIndex;
    private static int generated;
    private static int skipped;
    private static int failed;
    private static int previewWaitFrames;

    [MenuItem("Airport/Buildables/Generate Previews/Selected Definitions")]
    public static void GenerateSelectedDefinitions()
    {
        BuildableDefinition[] definitions =
            Selection.objects
                .OfType<BuildableDefinition>()
                .ToArray();

        if (definitions.Length == 0)
        {
            Debug.LogWarning(
                "Select one or more BuildableDefinition assets first."
            );
            return;
        }

        GenerateDefinitions(definitions);
    }

    [MenuItem("Airport/Buildables/Generate Previews/All Definitions")]
    public static void GenerateAllDefinitions()
    {
        string[] guids = AssetDatabase.FindAssets(
            "t:BuildableDefinition",
            new[] { "Assets/AirportGame" }
        );

        BuildableDefinition[] definitions = guids
            .Select(guid => AssetDatabase.LoadAssetAtPath<BuildableDefinition>(
                AssetDatabase.GUIDToAssetPath(guid)))
            .Where(definition => definition != null)
            .ToArray();

        GenerateDefinitions(definitions);
    }

    private static void GenerateDefinitions(
        BuildableDefinition[] definitions)
    {
        CancelGeneration();
        EnsureGeneratedFolder();

        pendingDefinitions = new List<BuildableDefinition>();
        generated = 0;
        skipped = 0;
        failed = 0;
        currentIndex = 0;
        previewWaitFrames = 0;

        foreach (BuildableDefinition definition in definitions)
        {
            if (definition == null || definition.prefab == null)
            {
                failed++;
                Debug.LogWarning(
                    "BuildablePreviewGenerator: Skipping a definition " +
                    "without an assigned prefab.",
                    definition
                );
                continue;
            }

            string currentIconPath =
                definition.icon != null
                    ? AssetDatabase.GetAssetPath(definition.icon)
                    : string.Empty;

            bool isGeneratedIcon =
                currentIconPath.StartsWith(
                    GeneratedFolder + "/",
                    StringComparison.OrdinalIgnoreCase
                );

            // Preserve manually assigned icons. Generated previews can
            // be safely refreshed whenever this command is run again.
            if (definition.icon != null && !isGeneratedIcon)
            {
                skipped++;
                continue;
            }

            pendingDefinitions.Add(definition);
        }

        if (pendingDefinitions.Count == 0)
        {
            FinishGeneration();
            return;
        }

        EditorApplication.update += ProcessNextPreview;
    }

    private static void ProcessNextPreview()
    {
        if (pendingDefinitions == null)
            return;

        if (currentIndex >= pendingDefinitions.Count)
        {
            FinishGeneration();
            return;
        }

        BuildableDefinition definition =
            pendingDefinitions[currentIndex];

        if (definition == null || definition.prefab == null)
        {
            failed++;
            currentIndex++;
            previewWaitFrames = 0;
            return;
        }

        Texture2D preview = null;

        try
        {
            // Request and retrieve Unity's own prefab asset preview.
            // Unity generates this asynchronously, so poll on editor updates.
            preview = AssetPreview.GetAssetPreview(definition.prefab);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Failed to request Unity asset preview for " +
                definition.name + ": " + exception,
                definition
            );

            failed++;
            currentIndex++;
            previewWaitFrames = 0;
            return;
        }

        if (preview == null)
        {
            previewWaitFrames++;

            if (previewWaitFrames >= MaxPreviewWaitFrames)
            {
                Debug.LogWarning(
                    "Unity did not generate an asset preview for " +
                    definition.name + ". The definition was skipped.",
                    definition
                );

                failed++;
                currentIndex++;
                previewWaitFrames = 0;
            }

            return;
        }

        try
        {
            SavePreview(definition, preview);
            generated++;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Failed to save preview for " + definition.name +
                ": " + exception,
                definition
            );
            failed++;
        }

        currentIndex++;
        previewWaitFrames = 0;
    }

    private static void SavePreview(
        BuildableDefinition definition,
        Texture2D preview)
    {
        string definitionPath =
            AssetDatabase.GetAssetPath(definition);

        string definitionGuid =
            AssetDatabase.AssetPathToGUID(definitionPath);

        string safeId = MakeSafeFileName(definition.id);
        if (string.IsNullOrEmpty(safeId))
            safeId = MakeSafeFileName(definition.name);

        string fileName =
            safeId + "_" + definitionGuid.Substring(0, 8) + ".png";

        string assetPath = GeneratedFolder + "/" + fileName;
        string absolutePath = Path.GetFullPath(assetPath);

        File.WriteAllBytes(absolutePath, preview.EncodeToPNG());

        AssetDatabase.ImportAsset(
            assetPath,
            ImportAssetOptions.ForceUpdate
        );

        TextureImporter importer =
            AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        Texture2D previewAsset =
            AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

        if (previewAsset == null)
        {
            throw new InvalidOperationException(
                "Generated preview could not be loaded: " + assetPath
            );
        }

        definition.icon = previewAsset;
        EditorUtility.SetDirty(definition);
    }

    private static void FinishGeneration()
    {
        EditorApplication.update -= ProcessNextPreview;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.ClearProgressBar();

        Debug.Log(
            "BuildablePreviewGenerator finished. Generated/updated: " +
            generated + ", preserved existing icons: " + skipped +
            ", failed: " + failed + "."
        );

        pendingDefinitions = null;
    }

    private static void CancelGeneration()
    {
        EditorApplication.update -= ProcessNextPreview;
        EditorUtility.ClearProgressBar();
        pendingDefinitions = null;
    }

    private static void EnsureGeneratedFolder()
    {
        EnsureFolder("Assets/AirportGame/Generated");
        EnsureFolder(GeneratedFolder);
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string parent = Path.GetDirectoryName(folderPath)
            .Replace("\\", "/");

        string folderName = Path.GetFileName(folderPath);

        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static string MakeSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        char[] invalidCharacters = Path.GetInvalidFileNameChars();

        return new string(
            value.Select(character =>
                invalidCharacters.Contains(character) ||
                character == '/' ||
                character == '\\'
                    ? '_'
                    : character)
                .ToArray()
        );
    }
}
