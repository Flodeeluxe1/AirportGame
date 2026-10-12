using System.Collections.Generic;
using UnityEngine;

public class PlacementPreview : MonoBehaviour
{
    [Header("Preview Colors")]
    [SerializeField]
    private Color validColor =
        new Color(0.2f, 1f, 0.2f, 0.45f);

    [SerializeField]
    private Color invalidColor =
        new Color(1f, 0.15f, 0.15f, 0.45f);

    private readonly List<Renderer> renderers = new();
    private readonly List<Material> previewMaterials = new();

    private void Awake()
    {
        CacheRenderers();
    }

    public void Initialize()
    {
        CacheRenderers();

        foreach (Renderer renderer in renderers)
        {
            // Each renderer gets its own preview material.
            Material previewMaterial =
                CreatePreviewMaterial();

            if (previewMaterial == null)
                continue;

            previewMaterials.Add(
                previewMaterial
            );

            // Replace all material slots with our preview material.
            Material[] materials =
                new Material[
                    renderer.sharedMaterials.Length
                ];

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] =
                    previewMaterial;
            }

            renderer.materials =
                materials;
        }

        SetValid(true);
    }

    private void CacheRenderers()
    {
        renderers.Clear();

        Renderer[] foundRenderers =
            GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in foundRenderers)
        {
            renderers.Add(renderer);
        }
    }

    private Material CreatePreviewMaterial()
    {
        Shader shader =
            Shader.Find(
                "Universal Render Pipeline/Unlit"
            );

        if (shader == null)
        {
            Debug.LogError(
                "Could not find URP Unlit shader. " +
                "Make sure the project is using URP."
            );

            return null;
        }

        Material material =
            new Material(shader);

        material.name =
            "Building Preview Material";

        // --------------------------------------------------
        // TRANSPARENT SURFACE
        // --------------------------------------------------

        material.SetFloat(
            "_Surface",
            1.0f
        );

        material.SetFloat(
            "_Blend",
            0.0f
        );

        // IMPORTANT:
        // Unity 6 URP Unlit requires this keyword
        // to select the transparent shader variant.
        material.EnableKeyword(
            "_SURFACE_TYPE_TRANSPARENT"
        );

        // --------------------------------------------------
        // COLOR
        // --------------------------------------------------

        material.SetColor(
            "_BaseColor",
            validColor
        );

        // --------------------------------------------------
        // BLENDING
        // --------------------------------------------------

        material.SetInt(
            "_SrcBlend",
            (int)UnityEngine.Rendering.BlendMode.SrcAlpha
        );

        material.SetInt(
            "_DstBlend",
            (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
        );

        // Don't write transparent objects into depth.
        material.SetInt(
            "_ZWrite",
            0
        );

        material.SetFloat(
            "_ReceiveShadows",
            0.0f
        );

        // Disable alpha-test / other alpha modes.
        material.DisableKeyword(
            "_ALPHATEST_ON"
        );

        material.DisableKeyword(
            "_ALPHAMODULATE_ON"
        );

        material.DisableKeyword(
            "_ALPHAPREMULTIPLY_ON"
        );

        // Transparent objects should render after opaque geometry.
        material.renderQueue =
            3000;

        return material;
    }

    public void SetValid(bool valid)
    {
        Color color =
            valid
                ? validColor
                : invalidColor;

        foreach (Material material in previewMaterials)
        {
            if (material == null)
                continue;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor(
                    "_BaseColor",
                    color
                );
            }
        }
    }

    private void OnDestroy()
    {
        foreach (Material material in previewMaterials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        previewMaterials.Clear();
    }
}