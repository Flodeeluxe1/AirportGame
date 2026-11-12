using System.Collections.Generic;
using UnityEngine;

public class WallFinishSurface : MonoBehaviour
{
    private sealed class RendererState
    {
        public Renderer renderer;
        public Material[] originalMaterials;
        public MaterialPropertyBlock originalPropertyBlock;
    }

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int OriginalNormalMapId = Shader.PropertyToID("_OriginalNormalMap");
    private static readonly int OriginalNormalMapStId = Shader.PropertyToID("_OriginalNormalMap_ST");
    private static readonly int OriginalNormalScaleId = Shader.PropertyToID("_OriginalNormalScale");
    private static readonly int InnerFinishIndexId = Shader.PropertyToID("_InnerFinishIndex");
    private static readonly int OuterFinishIndexId = Shader.PropertyToID("_OuterFinishIndex");
    private static readonly int InnerSideSignId = Shader.PropertyToID("_InnerSideSign");
    private static readonly int InnerTextureStId = Shader.PropertyToID("_InnerTextureST");
    private static readonly int OuterTextureStId = Shader.PropertyToID("_OuterTextureST");
    private static readonly int OriginalMetallicId = Shader.PropertyToID("_OriginalMetallic");
    private static readonly int OriginalSmoothnessId = Shader.PropertyToID("_OriginalSmoothness");
    private static readonly int PreviewModeId = Shader.PropertyToID("_PreviewMode");
    private static readonly int PreviewSideSignId = Shader.PropertyToID("_PreviewSideSign");
    private static readonly int PreviewFinishIndexId = Shader.PropertyToID("_PreviewFinishIndex");

    private readonly List<RendererState> rendererStates =
        new List<RendererState>();

    private WallFinishCatalogue catalogue;
    private bool isInitialized;
    private string innerFinishId = "";
    private string outerFinishId = "";
    private int innerSideSign = 1;
    private int previewMode;
    private int previewSideSign = 1;
    private int previewFinishIndex = -1;

    public string InnerFinishId => innerFinishId;
    public string OuterFinishId => outerFinishId;
    public int InnerSideSign => innerSideSign;
    public bool HasAnyFinish =>
        !string.IsNullOrWhiteSpace(innerFinishId) ||
        !string.IsNullOrWhiteSpace(outerFinishId);

    public void Initialize(
        WallFinishCatalogue finishCatalogue,
        Material sharedMaterial,
        int resolvedInnerSideSign)
    {
        if (finishCatalogue == null || sharedMaterial == null)
            return;

        catalogue = finishCatalogue;
        innerSideSign = resolvedInnerSideSign >= 0 ? 1 : -1;

        if (!isInitialized)
        {
            Renderer[] renderers =
                GetComponentsInChildren<Renderer>(true);

            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer == null)
                    continue;

                Material[] originals = targetRenderer.sharedMaterials;
                if (originals == null || originals.Length == 0)
                    originals = new[] { targetRenderer.sharedMaterial };

                MaterialPropertyBlock originalBlock =
                    new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(originalBlock);

                RendererState state = new RendererState
                {
                    renderer = targetRenderer,
                    originalMaterials = originals,
                    originalPropertyBlock = originalBlock
                };
                rendererStates.Add(state);

                Material original = originals.Length > 0 ? originals[0] : null;
                Material[] sharedMaterials =
                    new Material[Mathf.Max(1, originals.Length)];

                for (int i = 0; i < sharedMaterials.Length; i++)
                    sharedMaterials[i] = sharedMaterial;

                targetRenderer.sharedMaterials = sharedMaterials;

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(block);

                Texture originalTexture = GetTexture(
                    original, "_BaseMap", "_MainTex");

                Vector2 originalScale = GetTextureScale(
                    original, "_BaseMap", "_MainTex");
                Vector2 originalOffset = GetTextureOffset(
                    original, "_BaseMap", "_MainTex");

                Texture originalNormalTexture = GetTexture(
                    original, "_BumpMap", "_NormalMap");
                Vector2 originalNormalScale = GetTextureScale(
                    original, "_BumpMap", "_NormalMap");
                Vector2 originalNormalOffset = GetTextureOffset(
                    original, "_BumpMap", "_NormalMap");

                block.SetTexture(
                    BaseMapId,
                    originalTexture != null
                        ? originalTexture
                        : Texture2D.whiteTexture);

                if (!block.HasColor(BaseColorId))
                    block.SetColor(BaseColorId, GetBaseColor(original));

                block.SetTexture(
                    OriginalNormalMapId,
                    originalNormalTexture != null
                        ? originalNormalTexture
                        : Texture2D.normalTexture);
                block.SetVector(
                    OriginalNormalMapStId,
                    new Vector4(
                        originalNormalScale.x,
                        originalNormalScale.y,
                        originalNormalOffset.x,
                        originalNormalOffset.y));
                block.SetFloat(
                    OriginalNormalScaleId,
                    GetFloat(original, "_BumpScale", 1f));

                block.SetVector(
                    BaseMapStId,
                    new Vector4(
                        originalScale.x,
                        originalScale.y,
                        originalOffset.x,
                        originalOffset.y));

                block.SetFloat(
                    OriginalMetallicId,
                    GetFloat(original, "_Metallic", 0f));

                block.SetFloat(
                    OriginalSmoothnessId,
                    GetFloat(original, "_Smoothness", 0.2f));

                targetRenderer.SetPropertyBlock(block);
            }

            isInitialized = rendererStates.Count > 0;
        }

        ApplyFinishProperties();
    }

    public void SetFinish(bool innerSide, WallFinishDefinition finish)
    {
        string finishId =
            finish != null && !string.IsNullOrWhiteSpace(finish.id)
                ? finish.id
                : "";

        if (innerSide)
            innerFinishId = finishId;
        else
            outerFinishId = finishId;

        ApplyFinishProperties();
        RestoreOriginalMaterialsIfUnused();
    }

    public void SetFinishIds(string innerId, string outerId)
    {
        innerFinishId = innerId ?? "";
        outerFinishId = outerId ?? "";
        ApplyFinishProperties();
        RestoreOriginalMaterialsIfUnused();
    }

    public void ShowPreview(
        int faceSign,
        WallFinishDefinition finish,
        bool erase)
    {
        previewMode = erase ? 2 : 1;
        previewSideSign = faceSign >= 0 ? 1 : -1;
        previewFinishIndex =
            finish != null && catalogue != null
                ? catalogue.GetIndex(finish)
                : -1;

        ApplyFinishProperties();
    }

    public void HidePreview()
    {
        previewMode = 0;
        previewFinishIndex = -1;
        ApplyFinishProperties();
        RestoreOriginalMaterialsIfUnused();
    }

    public bool IsInnerFace(int faceSign)
    {
        return (faceSign >= 0 ? 1 : -1) == innerSideSign;
    }


    private void RestoreOriginalMaterialsIfUnused()
    {
        if (!isInitialized || HasAnyFinish || previewMode != 0)
            return;

        foreach (RendererState state in rendererStates)
        {
            if (state.renderer == null)
                continue;

            state.renderer.sharedMaterials = state.originalMaterials;
            state.renderer.SetPropertyBlock(state.originalPropertyBlock);
        }

        rendererStates.Clear();
        isInitialized = false;
    }

    private void ApplyFinishProperties()
    {
        if (!isInitialized || catalogue == null)
            return;

        WallFinishDefinition innerFinish =
            catalogue.FindById(innerFinishId);
        WallFinishDefinition outerFinish =
            catalogue.FindById(outerFinishId);

        int innerIndex =
            innerFinish != null && innerFinish.material != null
                ? catalogue.GetIndex(innerFinish)
                : -1;

        int outerIndex =
            outerFinish != null && outerFinish.material != null
                ? catalogue.GetIndex(outerFinish)
                : -1;

        Vector4 innerSt = GetFinishTextureSt(innerFinish);
        Vector4 outerSt = GetFinishTextureSt(outerFinish);

        foreach (RendererState state in rendererStates)
        {
            if (state.renderer == null)
                continue;

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            state.renderer.GetPropertyBlock(block);

            block.SetFloat(InnerFinishIndexId, innerIndex);
            block.SetFloat(OuterFinishIndexId, outerIndex);
            block.SetFloat(InnerSideSignId, innerSideSign);
            block.SetVector(InnerTextureStId, innerSt);
            block.SetVector(OuterTextureStId, outerSt);
            block.SetFloat(PreviewModeId, previewMode);
            block.SetFloat(PreviewSideSignId, previewSideSign);
            block.SetFloat(PreviewFinishIndexId, previewFinishIndex);

            state.renderer.SetPropertyBlock(block);
        }
    }

    private static Vector4 GetFinishTextureSt(
        WallFinishDefinition finish)
    {
        if (finish == null || finish.material == null)
            return new Vector4(1f, 1f, 0f, 0f);

        Vector2 scale =
            finish.material.GetTextureScale("_BaseMap");
        Vector2 offset =
            finish.material.GetTextureOffset("_BaseMap");

        if (scale == Vector2.zero)
            scale = Vector2.one;

        return new Vector4(
            scale.x,
            scale.y,
            offset.x,
            offset.y);
    }

    private static Texture GetTexture(
        Material material,
        string primary,
        string fallback)
    {
        if (material == null)
            return null;

        if (material.HasProperty(primary))
            return material.GetTexture(primary);

        return material.HasProperty(fallback)
            ? material.GetTexture(fallback)
            : null;
    }

    private static Vector2 GetTextureScale(
        Material material,
        string primary,
        string fallback)
    {
        if (material == null)
            return Vector2.one;

        return material.HasProperty(primary)
            ? material.GetTextureScale(primary)
            : material.HasProperty(fallback)
                ? material.GetTextureScale(fallback)
                : Vector2.one;
    }

    private static Vector2 GetTextureOffset(
        Material material,
        string primary,
        string fallback)
    {
        if (material == null)
            return Vector2.zero;

        return material.HasProperty(primary)
            ? material.GetTextureOffset(primary)
            : material.HasProperty(fallback)
                ? material.GetTextureOffset(fallback)
                : Vector2.zero;
    }

    private static Color GetBaseColor(Material material)
    {
        if (material == null)
            return Color.white;

        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");

        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");

        return Color.white;
    }

    private static float GetFloat(
        Material material,
        string property,
        float fallback)
    {
        return material != null && material.HasProperty(property)
            ? material.GetFloat(property)
            : fallback;
    }
}
