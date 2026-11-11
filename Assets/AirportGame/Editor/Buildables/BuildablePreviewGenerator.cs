using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BuildablePreviewGenerator
{
    private const string GeneratedFolder =
        "Assets/AirportGame/Generated/BuildablePreviews";

    private const int PreviewSize = 256;

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
        EnsureGeneratedFolder();

        int generated = 0;
        int skipped = 0;
        int failed = 0;

        try
        {
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

                if (GeneratePreview(definition))
                    generated++;
                else
                    failed++;
            }
        }
        finally
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log(
            "BuildablePreviewGenerator finished. Generated/updated: " +
            generated + ", preserved existing icons: " + skipped +
            ", failed: " + failed + "."
        );
    }

    private static bool GeneratePreview(
        BuildableDefinition definition)
    {
        PreviewRenderUtility previewUtility = null;
        Texture2D renderedTexture = null;

        try
        {
            previewUtility = new PreviewRenderUtility();

            GameObject instance =
                PrefabUtility.InstantiatePrefab(definition.prefab) as GameObject;

            if (instance == null)
            {
                Debug.LogWarning(
                    "Could not instantiate preview for " + definition.name,
                    definition
                );
                return false;
            }

            instance.name = definition.prefab.name + "_Preview";
            instance.transform.position = Vector3.zero;
            instance.transform.rotation =
                Quaternion.Euler(0f, 45f, 30f);

            // Prevent custom prefab scripts and animators from changing the
            // preview object. Renderers and their original materials remain.
            MonoBehaviour[] scripts =
                instance.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (MonoBehaviour script in scripts)
            {
                if (script != null)
                    script.enabled = false;
            }

            Animator[] animators =
                instance.GetComponentsInChildren<Animator>(true);

            foreach (Animator animator in animators)
            {
                if (animator != null)
                    animator.enabled = false;
            }

            previewUtility.AddSingleGO(instance);

            Bounds bounds;
            if (!TryGetRenderBounds(instance, out bounds))
            {
                Debug.LogWarning(
                    "No enabled renderers were found for " + definition.name,
                    definition
                );
                return false;
            }

            Vector3 viewDirection =
                new Vector3(1f, 1f, 1f).normalized;

            float objectRadius = Mathf.Max(bounds.extents.magnitude, 0.5f);
            float cameraDistance = objectRadius * 4f + 2f;

            Camera previewCamera = previewUtility.camera;
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = objectRadius * 1.2f;
            previewCamera.aspect = 1f;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane =
                cameraDistance + objectRadius * 5f;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor =
                new Color(0.16f, 0.18f, 0.21f, 1f);

            previewCamera.transform.position =
                bounds.center + viewDirection * cameraDistance;
            previewCamera.transform.LookAt(bounds.center, Vector3.up);

            if (previewUtility.lights != null &&
                previewUtility.lights.Length > 0 &&
                previewUtility.lights[0] != null)
            {
                previewUtility.lights[0].intensity = 1.25f;
                previewUtility.lights[0].transform.rotation =
                    Quaternion.Euler(35f, -35f, 0f);
            }

            if (previewUtility.lights != null &&
                previewUtility.lights.Length > 1 &&
                previewUtility.lights[1] != null)
            {
                previewUtility.lights[1].intensity = 0.7f;
                previewUtility.lights[1].transform.rotation =
                    Quaternion.Euler(25f, 145f, 0f);
            }

            Rect previewRect =
                new Rect(0f, 0f, PreviewSize, PreviewSize);

            previewUtility.BeginStaticPreview(previewRect);
            previewCamera.Render();
            renderedTexture = previewUtility.EndStaticPreview();

            if (renderedTexture == null)
            {
                Debug.LogWarning(
                    "Unity did not return a rendered preview for " +
                    definition.name,
                    definition
                );
                return false;
            }

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

            File.WriteAllBytes(
                absolutePath,
                renderedTexture.EncodeToPNG()
            );

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
                importer.maxTextureSize = PreviewSize;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }

            Texture2D previewAsset =
                AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

            if (previewAsset == null)
            {
                Debug.LogWarning(
                    "Generated preview could not be loaded: " + assetPath,
                    definition
                );
                return false;
            }

            definition.icon = previewAsset;
            EditorUtility.SetDirty(definition);

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Failed to generate preview for " + definition.name +
                ": " + exception,
                definition
            );
            return false;
        }
        finally
        {
            if (renderedTexture != null)
                UnityEngine.Object.DestroyImmediate(renderedTexture);

            if (previewUtility != null)
                previewUtility.Cleanup();
        }
    }

    private static bool TryGetRenderBounds(
        GameObject root,
        out Bounds bounds)
    {
        Renderer[] renderers =
            root.GetComponentsInChildren<Renderer>(true);

        bool foundRenderer = false;
        bounds = new Bounds(root.transform.position, Vector3.zero);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
                continue;

            if (!foundRenderer)
            {
                bounds = renderer.bounds;
                foundRenderer = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return foundRenderer;
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
