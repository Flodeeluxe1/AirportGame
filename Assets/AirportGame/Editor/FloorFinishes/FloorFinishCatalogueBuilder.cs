#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class FloorFinishCatalogueBuilder
{
    private const string GeneratedRoot =
        "Assets/AirportGame/Generated/FloorFinishes";

    private const string DefinitionsFolder =
        GeneratedRoot + "/Definitions";

    private const string MaterialsFolder =
        GeneratedRoot + "/Materials";

    private const string TextureArrayFolder =
        GeneratedRoot + "/TextureArrays";

    private const string BaseColorArrayPath =
        TextureArrayFolder + "/FloorFinishBaseColorArray.asset";

    private const string NormalArrayPath =
        TextureArrayFolder + "/FloorFinishNormalArray.asset";

    private const string MaskArrayPath =
        TextureArrayFolder + "/FloorFinishMaskArray.asset";

    private const string SharedMaterialPath =
        MaterialsFolder + "/FloorFinishShared.mat";

    private const string SharedShaderName =
        "Airport/Floor Finish Texture Array";

    private const int TextureArrayResolution = 512;

    private const string CatalogueFolder =
        "Assets/AirportGame/ScriptableObjects/FloorFinishes";

    private const string CataloguePath =
        CatalogueFolder + "/FloorFinishCatalogue.asset";

    private static readonly string[] SearchRoots =
    {
        "Assets/Floor materials pack",
        "Assets/Texture_and_materials/Tiles_floor",
        "Assets/Texture_and_materials/Paving_stone"
    };

    [MenuItem("Airport/Floor Finishes/Build Catalogue From Project Materials")]
    public static void BuildCatalogue()
    {
        EnsureFolder("Assets/AirportGame/Generated");
        EnsureFolder(GeneratedRoot);
        EnsureFolder(DefinitionsFolder);
        EnsureFolder(MaterialsFolder);
        EnsureFolder(TextureArrayFolder);
        EnsureFolder("Assets/AirportGame/ScriptableObjects");
        EnsureFolder(CatalogueFolder);

        List<string> materialGuids = new List<string>();

        foreach (string root in SearchRoots)
        {
            if (!AssetDatabase.IsValidFolder(root))
            {
                Debug.LogWarning(
                    $"FloorFinishCatalogueBuilder: Search folder not found: {root}");
                continue;
            }

            materialGuids.AddRange(
                AssetDatabase.FindAssets("t:Material", new[] { root }));
        }

        List<FloorFinishDefinition> definitions =
            new List<FloorFinishDefinition>();

        HashSet<string> processedNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string guid in materialGuids.Distinct())
        {
            string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
            Material source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);

            if (source == null || !IsFloorMaterial(source.name))
                continue;

            if (!processedNames.Add(source.name))
            {
                Debug.LogWarning(
                    $"FloorFinishCatalogueBuilder: Duplicate material name " +
                    $"'{source.name}' was skipped.");
                continue;
            }

            Material finishMaterial = CreateOrUpdateUrpMaterial(source);
            if (finishMaterial == null)
                continue;

            string id = "floor." + Sanitize(source.name);
            string definitionPath =
                DefinitionsFolder + "/" + Sanitize(source.name) + ".asset";

            FloorFinishDefinition definition =
                AssetDatabase.LoadAssetAtPath<FloorFinishDefinition>(
                    definitionPath);

            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<FloorFinishDefinition>();
                AssetDatabase.CreateAsset(definition, definitionPath);
            }

            definition.id = id;
            definition.displayName = MakeDisplayName(source.name);
            definition.material = finishMaterial;
            definition.icon = GetBaseTexture(finishMaterial);

            EditorUtility.SetDirty(definition);
            definitions.Add(definition);
        }

        definitions = definitions
            .OrderBy(x => x.displayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (definitions.Count == 0)
        {
            Debug.LogError(
                "FloorFinishCatalogueBuilder: No floor finish definitions were found. " +
                "The existing catalogue was left unchanged.");
            return;
        }

        Texture2DArray baseColorArray = BuildBaseColorArray(definitions);
        Texture2DArray normalArray = BuildNormalArray(definitions);
        Texture2DArray maskArray = BuildMaskArray(definitions);
        Material sharedRenderMaterial = CreateSharedRenderMaterial(
            baseColorArray,
            normalArray,
            maskArray);

        if (sharedRenderMaterial == null)
            return;

        FloorFinishCatalogue catalogue =
            AssetDatabase.LoadAssetAtPath<FloorFinishCatalogue>(CataloguePath);

        if (catalogue == null)
        {
            catalogue = ScriptableObject.CreateInstance<FloorFinishCatalogue>();
            AssetDatabase.CreateAsset(catalogue, CataloguePath);
        }

        SerializedObject serializedCatalogue = new SerializedObject(catalogue);
        SerializedProperty finishes =
            serializedCatalogue.FindProperty("finishes");
        SerializedProperty sharedMaterialProperty =
            serializedCatalogue.FindProperty("sharedRenderMaterial");

        finishes.arraySize = definitions.Count;
        sharedMaterialProperty.objectReferenceValue = sharedRenderMaterial;

        for (int i = 0; i < definitions.Count; i++)
            finishes.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];

        serializedCatalogue.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalogue);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"FloorFinishCatalogueBuilder: Created/updated {definitions.Count} " +
            $"floor finishes. Catalogue: {CataloguePath}");
    }

    private static Texture2DArray BuildBaseColorArray(
        List<FloorFinishDefinition> definitions)
    {
        return CreateTextureArrayAsset(
            BaseColorArrayPath,
            definitions.Count,
            false,
            index =>
            {
                Material material = definitions[index].material;
                Texture texture = GetTexture(material, "_BaseMap", "_MainTex");
                Color[] pixels = ReadTexturePixels(
                    texture,
                    TextureArrayResolution,
                    false,
                    Color.white);

                Color tint = material != null &&
                    material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor")
                    : Color.white;

                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] *= tint;

                return pixels;
            });
    }

    private static Texture2DArray BuildNormalArray(
        List<FloorFinishDefinition> definitions)
    {
        return CreateTextureArrayAsset(
            NormalArrayPath,
            definitions.Count,
            true,
            index =>
            {
                Material material = definitions[index].material;
                Texture texture = GetTexture(material, "_BumpMap");

                return ReadTexturePixels(
                    texture,
                    TextureArrayResolution,
                    true,
                    new Color(0.5f, 0.5f, 1f, 1f));
            });
    }

    private static Texture2DArray BuildMaskArray(
        List<FloorFinishDefinition> definitions)
    {
        return CreateTextureArrayAsset(
            MaskArrayPath,
            definitions.Count,
            true,
            index =>
            {
                Material material = definitions[index].material;
                Texture metallicTexture =
                    GetTexture(material, "_MetallicGlossMap", "_MetallicMap");
                Texture occlusionTexture =
                    GetTexture(material, "_OcclusionMap");

                Color[] metallicPixels = ReadTexturePixels(
                    metallicTexture,
                    TextureArrayResolution,
                    true,
                    Color.white);
                Color[] occlusionPixels = ReadTexturePixels(
                    occlusionTexture,
                    TextureArrayResolution,
                    true,
                    Color.white);

                bool hasMetallicMap = metallicTexture != null;
                bool hasOcclusionMap = occlusionTexture != null;

                float metallicValue = GetMaterialFloat(
                    material, "_Metallic", 0f);
                float smoothnessValue = GetMaterialFloat(
                    material, "_Smoothness", 0.5f);
                float bumpScale = GetMaterialFloat(
                    material, "_BumpScale", 1f);

                Color[] maskPixels = new Color[metallicPixels.Length];

                for (int i = 0; i < maskPixels.Length; i++)
                {
                    float metallic = hasMetallicMap
                        ? metallicPixels[i].r
                        : metallicValue;

                    float smoothness = hasMetallicMap
                        ? metallicPixels[i].a
                        : smoothnessValue;

                    float occlusion = hasOcclusionMap
                        ? occlusionPixels[i].g
                        : 1f;

                    // R = metallic, G = occlusion, B = normal strength,
                    // A = smoothness.
                    maskPixels[i] = new Color(
                        metallic,
                        occlusion,
                        bumpScale,
                        smoothness);
                }

                return maskPixels;
            });
    }

    private static Texture2DArray CreateTextureArrayAsset(
        string assetPath,
        int depth,
        bool linear,
        Func<int, Color[]> getSlicePixels)
    {
        Texture2DArray array =
            AssetDatabase.LoadAssetAtPath<Texture2DArray>(assetPath);

        bool needsRecreation =
            array == null ||
            array.width != TextureArrayResolution ||
            array.height != TextureArrayResolution ||
            array.depth != depth ||
            array.format != TextureFormat.RGBA32 ||
            !array.isReadable;

        if (needsRecreation)
        {
            if (array != null)
                AssetDatabase.DeleteAsset(assetPath);

            array = new Texture2DArray(
                TextureArrayResolution,
                TextureArrayResolution,
                depth,
                TextureFormat.RGBA32,
                true,
                linear)
            {
                name = Path.GetFileNameWithoutExtension(assetPath)
            };

            AssetDatabase.CreateAsset(array, assetPath);
        }

        array.wrapMode = TextureWrapMode.Repeat;
        array.filterMode = FilterMode.Trilinear;
        array.anisoLevel = 8;

        for (int slice = 0; slice < depth; slice++)
        {
            Color[] pixels = getSlicePixels(slice);
            array.SetPixels(pixels, slice, 0);
        }

        array.Apply(true, false);
        EditorUtility.SetDirty(array);

        return array;
    }

    private static Color[] ReadTexturePixels(
        Texture source,
        int resolution,
        bool linear,
        Color fallback)
    {
        if (source == null)
        {
            Color[] fallbackPixels =
                new Color[resolution * resolution];

            for (int i = 0; i < fallbackPixels.Length; i++)
                fallbackPixels[i] = fallback;

            return fallbackPixels;
        }

        RenderTextureReadWrite readWrite = linear
            ? RenderTextureReadWrite.Linear
            : RenderTextureReadWrite.Default;

        RenderTexture renderTexture = RenderTexture.GetTemporary(
            resolution,
            resolution,
            0,
            RenderTextureFormat.ARGB32,
            readWrite);

        RenderTexture previousActive = RenderTexture.active;
        Texture2D readableTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBA32,
            false,
            linear);

        try
        {
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;
            readableTexture.ReadPixels(
                new Rect(0, 0, resolution, resolution),
                0,
                0);
            readableTexture.Apply(false, false);
            return readableTexture.GetPixels();
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(renderTexture);
            UnityEngine.Object.DestroyImmediate(readableTexture);
        }
    }

    private static Material CreateSharedRenderMaterial(
        Texture2DArray baseColorArray,
        Texture2DArray normalArray,
        Texture2DArray maskArray)
    {
        Shader shader = Shader.Find(SharedShaderName);

        if (shader == null)
        {
            Debug.LogError(
                $"FloorFinishCatalogueBuilder: Shader '{SharedShaderName}' " +
                "was not found. Check the shader compile errors.");
            return null;
        }

        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);

        if (material == null)
        {
            material = new Material(shader)
            {
                name = "Floor Finish Shared"
            };
            AssetDatabase.CreateAsset(material, SharedMaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.SetTexture("_BaseColorArray", baseColorArray);
        material.SetTexture("_NormalArray", normalArray);
        material.SetTexture("_MaskArray", maskArray);
        material.enableInstancing = true;

        EditorUtility.SetDirty(material);
        return material;
    }

    private static float GetMaterialFloat(
        Material material,
        string property,
        float fallback)
    {
        return material != null && material.HasProperty(property)
            ? material.GetFloat(property)
            : fallback;
    }

    private static Material CreateOrUpdateUrpMaterial(Material source)
    {
        string safeName = Sanitize(source.name);
        string targetPath = MaterialsFolder + "/" + safeName + "_URP.mat";

        Material target =
            AssetDatabase.LoadAssetAtPath<Material>(targetPath);

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError(
                "FloorFinishCatalogueBuilder: URP/Lit shader was not found.");
            return null;
        }

        if (target == null)
        {
            target = new Material(urpLit);
            target.name = source.name + " URP";
            AssetDatabase.CreateAsset(target, targetPath);
        }
        else
        {
            target.shader = urpLit;
        }

        Texture baseMap = GetTexture(source, "_BaseMap", "_MainTex");
        Texture normalMap = GetTexture(source, "_BumpMap");
        Texture metallicMap = GetTexture(
            source, "_MetallicGlossMap", "_MetallicMap");
        Texture occlusionMap = GetTexture(source, "_OcclusionMap");

        SetTextureAndTransform(target, "_BaseMap", baseMap, source,
            "_BaseMap", "_MainTex");
        SetTextureAndTransform(target, "_BumpMap", normalMap, source,
            "_BumpMap");
        SetTextureAndTransform(target, "_MetallicGlossMap", metallicMap, source,
            "_MetallicGlossMap", "_MetallicMap");
        SetTextureAndTransform(target, "_OcclusionMap", occlusionMap, source,
            "_OcclusionMap");

        if (source.HasProperty("_Color") && target.HasProperty("_BaseColor"))
            target.SetColor("_BaseColor", source.GetColor("_Color"));
        else if (source.HasProperty("_BaseColor") && target.HasProperty("_BaseColor"))
            target.SetColor("_BaseColor", source.GetColor("_BaseColor"));

        if (source.HasProperty("_Metallic") && target.HasProperty("_Metallic"))
            target.SetFloat("_Metallic", source.GetFloat("_Metallic"));

        if (source.HasProperty("_Glossiness") && target.HasProperty("_Smoothness"))
            target.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
        else if (source.HasProperty("_Smoothness") && target.HasProperty("_Smoothness"))
            target.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));

        if (normalMap != null)
            target.EnableKeyword("_NORMALMAP");

        if (metallicMap != null)
            target.EnableKeyword("_METALLICGLOSSMAP");

        target.enableInstancing = true;
        EditorUtility.SetDirty(target);

        return target;
    }

    private static Texture GetTexture(Material material, params string[] properties)
    {
        foreach (string property in properties)
        {
            if (material.HasProperty(property))
            {
                Texture texture = material.GetTexture(property);
                if (texture != null)
                    return texture;
            }
        }

        return null;
    }

    private static void SetTextureAndTransform(
        Material target,
        string targetProperty,
        Texture texture,
        Material source,
        params string[] sourceProperties)
    {
        if (!target.HasProperty(targetProperty) || texture == null)
            return;

        target.SetTexture(targetProperty, texture);

        foreach (string sourceProperty in sourceProperties)
        {
            if (!source.HasProperty(sourceProperty))
                continue;

            target.SetTextureScale(
                targetProperty,
                source.GetTextureScale(sourceProperty));

            target.SetTextureOffset(
                targetProperty,
                source.GetTextureOffset(sourceProperty));

            break;
        }
    }

    private static Texture2D GetBaseTexture(Material material)
    {
        Texture texture = GetTexture(material, "_BaseMap", "_MainTex");
        return texture as Texture2D;
    }

    private static bool IsFloorMaterial(string materialName)
    {
        return materialName.StartsWith("Floor_", StringComparison.OrdinalIgnoreCase) ||
               materialName.StartsWith("Tiles_floor", StringComparison.OrdinalIgnoreCase) ||
               materialName.StartsWith("Paving_stone", StringComparison.OrdinalIgnoreCase);
    }

    private static string MakeDisplayName(string value)
    {
        return value.Replace("_", " ").Replace("(", " ").Replace(")", "").Trim();
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = value.ToLowerInvariant();

        foreach (char character in invalid)
            result = result.Replace(character.ToString(), "");

        result = result.Replace(" ", "_")
            .Replace("(", "")
            .Replace(")", "")
            .Replace("-", "_");

        return result;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
        string folderName = Path.GetFileName(path);

        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
            return;

        EnsureFolder(parent);

        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, folderName);
    }
}
#endif
