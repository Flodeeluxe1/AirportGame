Shader "Airport/Wall Finish Texture Array"
{
    Properties
    {
        [NoScaleOffset] _BaseColorArray ("Base Color Array", 2DArray) = "" {}
        [NoScaleOffset] _NormalArray ("Normal Array", 2DArray) = "" {}
        [NoScaleOffset] _MaskArray ("Metallic AO Smoothness Array", 2DArray) = "" {}
        _BaseMap ("Original Wall Texture", 2D) = "white" {}
        _BaseColor ("Original Wall Color", Color) = (1,1,1,1)
        [Normal] _OriginalNormalMap ("Original Wall Normal Map", 2D) = "bump" {}
        _OriginalNormalScale ("Original Wall Normal Scale", Float) = 1
        _InnerFinishIndex ("Inner Finish Index", Float) = -1
        _OuterFinishIndex ("Outer Finish Index", Float) = -1
        _InnerSideSign ("Inner Side Sign", Float) = 1
        _InnerTextureST ("Inner Texture Scale Offset", Vector) = (1,1,0,0)
        _OuterTextureST ("Outer Texture Scale Offset", Vector) = (1,1,0,0)
        _OriginalMetallic ("Original Metallic", Float) = 0
        _OriginalSmoothness ("Original Smoothness", Float) = 0.2
        _PreviewMode ("Preview Mode", Float) = 0
        _PreviewSideSign ("Preview Side Sign", Float) = 1
        _PreviewFinishIndex ("Preview Finish Index", Float) = -1
        _PreviewColor ("Preview Color", Color) = (1,0.08,0.02,0.35)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fog
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_BaseColorArray); SAMPLER(sampler_BaseColorArray);
            TEXTURE2D_ARRAY(_NormalArray); SAMPLER(sampler_NormalArray);
            TEXTURE2D_ARRAY(_MaskArray); SAMPLER(sampler_MaskArray);
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_OriginalNormalMap); SAMPLER(sampler_OriginalNormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _OriginalNormalMap_ST;
                float _OriginalNormalScale;
                half4 _BaseColor;
                float _InnerFinishIndex;
                float _OuterFinishIndex;
                float _InnerSideSign;
                float4 _InnerTextureST;
                float4 _OuterTextureST;
                float _OriginalMetallic;
                float _OriginalSmoothness;
                float _PreviewMode;
                float _PreviewSideSign;
                float _PreviewFinishIndex;
                half4 _PreviewColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 wallForwardWS : TEXCOORD3;
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD4;
                #endif
                half fogFactor : TEXCOORD5;

                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 6);
                #ifdef DYNAMICLIGHTMAP_ON
                float2 dynamicLightmapUV : TEXCOORD7;
                #endif
                #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD8;
                #endif

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.wallForwardWS =
                    TransformObjectToWorldDir(float3(0,0,1));

                OUTPUT_LIGHTMAP_UV(
                    input.staticLightmapUV,
                    unity_LightmapST,
                    output.staticLightmapUV);

                #ifdef DYNAMICLIGHTMAP_ON
                output.dynamicLightmapUV =
                    input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy +
                    unity_DynamicLightmapST.zw;
                #endif

                OUTPUT_SH4(
                    positionInputs.positionWS,
                    output.normalWS.xyz,
                    GetWorldSpaceNormalizeViewDir(positionInputs.positionWS),
                    output.vertexSH,
                    output.probeOcclusion);

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(positionInputs);
                #endif

                output.fogFactor =
                    ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            // Match the InputData setup used by URP/Lit's Forward pass.
            void InitializeWallInputData(Varyings input, half3 normalWS, out InputData inputData)
            {
                inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(normalWS);
                inputData.viewDirectionWS =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord =
                    TransformWorldToShadowCoord(inputData.positionWS);
                #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif

                inputData.fogCoord = InitializeInputDataFog(
                    float4(input.positionWS, 1.0),
                    input.fogFactor);
                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
            }

            // Match URP/Lit's baked-GI branch structure while retaining the
            // screen-space irradiance variant used by this project.
            void InitializeWallBakedGIData(
                Varyings input,
                inout InputData inputData)
            {
                #if defined(_SCREEN_SPACE_IRRADIANCE)
                inputData.bakedGI = SAMPLE_GI(
                    _ScreenSpaceIrradiance,
                    input.positionCS.xy,
                    inputData.normalWS);
                #elif defined(DYNAMICLIGHTMAP_ON)
                inputData.bakedGI = SAMPLE_GI(
                    input.staticLightmapUV,
                    input.dynamicLightmapUV,
                    input.vertexSH,
                    inputData.normalWS);
                inputData.shadowMask =
                    SAMPLE_SHADOWMASK(input.staticLightmapUV);
                #elif !defined(LIGHTMAP_ON) && \
                    (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                inputData.bakedGI = SAMPLE_GI(
                    input.vertexSH,
                    GetAbsolutePositionWS(inputData.positionWS),
                    inputData.normalWS,
                    inputData.viewDirectionWS,
                    input.positionCS.xy,
                    input.probeOcclusion,
                    inputData.shadowMask);
                #else
                inputData.bakedGI = SAMPLE_GI(
                    input.staticLightmapUV,
                    input.vertexSH,
                    inputData.normalWS);
                inputData.shadowMask =
                    SAMPLE_SHADOWMASK(input.staticLightmapUV);
                #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half3 geometricNormal = normalize(input.normalWS);
                float sideDot = dot(
                    geometricNormal,
                    normalize(input.wallForwardWS));

                // Only the two broad faces of the wall receive wall finishes.
                bool isWallSide = abs(sideDot) > 0.75;
                float faceSign = sideDot >= 0 ? 1.0 : -1.0;
                bool isInnerFace = faceSign == _InnerSideSign;

                float finishIndex =
                    isInnerFace ? _InnerFinishIndex : _OuterFinishIndex;
                float4 finishST =
                    isInnerFace ? _InnerTextureST : _OuterTextureST;

                bool previewThisFace =
                    isWallSide &&
                    _PreviewMode > 0.5 &&
                    faceSign == _PreviewSideSign;

                if (previewThisFace && _PreviewMode < 1.5)
                    finishIndex = _PreviewFinishIndex;

                half4 baseSample = SAMPLE_TEXTURE2D(
                    _BaseMap,
                    sampler_BaseMap,
                    input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw) * _BaseColor;

                // TEMPORARY DIAGNOSTIC:
                // Disable both original and finish normal maps on every wall face.
                half3 normalTS = half3(0.0h, 0.0h, 1.0h);
                half metallic = _OriginalMetallic;
                half smoothness = _OriginalSmoothness;
                half occlusion = 1.0h;

                if (isWallSide && finishIndex >= 0.0)
                {
                    float2 finishUV =
                        input.uv * finishST.xy + finishST.zw;

                    baseSample = SAMPLE_TEXTURE2D_ARRAY(
                        _BaseColorArray,
                        sampler_BaseColorArray,
                        finishUV,
                        finishIndex);

                    half4 maskSample = SAMPLE_TEXTURE2D_ARRAY(
                        _MaskArray,
                        sampler_MaskArray,
                        finishUV,
                        finishIndex);

                    // TEMPORARY DIAGNOSTIC:
                    // Keep finished wall faces geometrically flat to test whether
                    // finish normal maps cause the opposite face to darken.
                    normalTS = half3(0.0h, 0.0h, 1.0h);

                    metallic = maskSample.r;
                    occlusion = maskSample.g;
                    smoothness = maskSample.a;
                }

                if (previewThisFace && _PreviewMode > 1.5)
                {
                    baseSample.rgb = lerp(
                        baseSample.rgb,
                        _PreviewColor.rgb,
                        _PreviewColor.a);
                }

// Build the tangent basis from the actual wall surface position
                // and UV derivatives. This avoids relying on imported mesh tangents,
                // which can be missing or inconsistent between wall prefabs.
                float3 positionDx = ddx(input.positionWS);
                float3 positionDy = ddy(input.positionWS);
                float2 uvDx = ddx(input.uv);
                float2 uvDy = ddy(input.uv);
                float uvDeterminant = uvDx.x * uvDy.y - uvDx.y * uvDy.x;
                float inverseUvDeterminant =
                    abs(uvDeterminant) > 1e-8
                        ? rcp(uvDeterminant)
                        : 0.0;

                float3 tangentWS =
                    (positionDx * uvDy.y - positionDy * uvDx.y) *
                    inverseUvDeterminant;
                float3 bitangentWS =
                    (-positionDx * uvDy.x + positionDy * uvDx.x) *
                    inverseUvDeterminant;

                tangentWS = normalize(
                    tangentWS - geometricNormal *
                    dot(geometricNormal, tangentWS));

                float tangentHandedness =
                    dot(cross(geometricNormal, tangentWS), bitangentWS) < 0.0
                        ? -1.0
                        : 1.0;
                bitangentWS = normalize(
                    cross(geometricNormal, tangentWS) * tangentHandedness);

                half3 normalWS = normalize(
                    tangentWS * normalTS.x +
                    bitangentWS * normalTS.y +
                    geometricNormal * normalTS.z);

                InputData inputData;
                InitializeWallInputData(input, normalWS, inputData);
                InitializeWallBakedGIData(input, inputData);

                // TEMPORARY DIAGNOSTIC: display baked GI directly, bypassing
                // the PBR/direct-light calculation to compare both wall faces.
                return half4(max(inputData.bakedGI, half3(0, 0, 0)), 1.0h);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseSample.rgb;
                surfaceData.metallic = metallic;
                surfaceData.specular = half3(0.04h,0.04h,0.04h);
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.occlusion = occlusion;
                surfaceData.emission = half3(0,0,0);
                surfaceData.alpha = baseSample.a;
                surfaceData.clearCoatMask = 0;
                surfaceData.clearCoatSmoothness = 0;

half4 color =
                    UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // Preserve the original wall's ability to cast shadows after its
        // material is replaced by this custom shader.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }

    FallBack Off
}
