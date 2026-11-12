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

            // Keep the lighting variants used by URP/Lit, including vertex lights
            // and the clustered-light path used by this project.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
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

            // The vertex and probe data layout below follows URP 17.6 LitForwardPass.
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 wallForwardWS : TEXCOORD3;

                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD5;
                #else
                half fogFactor : TEXCOORD5;
                #endif

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD6;
                #endif

                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
                #ifdef DYNAMICLIGHTMAP_ON
                float2 dynamicLightmapUV : TEXCOORD9;
                #endif

                #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD10;
                #endif

                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS, input.tangentOS);

                half3 vertexLight =
                    VertexLighting(positionInputs.positionWS, normalInputs.normalWS);

                output.uv = input.uv;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.wallForwardWS =
                    TransformObjectToWorldDir(float3(0, 0, 1));

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
                    output.normalWS,
                    GetWorldSpaceNormalizeViewDir(positionInputs.positionWS),
                    output.vertexSH,
                    output.probeOcclusion);

                half fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
                #else
                output.fogFactor = fogFactor;
                #endif

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(positionInputs);
                #endif

                output.positionCS = positionInputs.positionCS;
                return output;
            }

            // Mirrors URP/Lit's Forward-pass InputData setup.
            void InitializeWallInputData(
                Varyings input,
                half3 normalWS,
                out InputData inputData)
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

                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                inputData.fogCoord = InitializeInputDataFog(
                    float4(input.positionWS, 1.0),
                    input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
                #else
                inputData.fogCoord = InitializeInputDataFog(
                    float4(input.positionWS, 1.0),
                    input.fogFactor);
                #endif

                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
            }

            // This is the URP 17.6 Lit baked-GI branch structure verbatim.
            void InitializeWallBakedGIData(
                Varyings input,
                inout InputData inputData)
            {
                #if defined(_SCREEN_SPACE_IRRADIANCE)
                inputData.bakedGI = SAMPLE_GI(
                    _ScreenSpaceIrradiance,
                    input.positionCS.xy);
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

                half3 geometricNormal = NormalizeNormalPerPixel(input.normalWS);
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

                // Keep normal maps disabled during this lighting comparison.
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

                InputData inputData;
                InitializeWallInputData(input, geometricNormal, inputData);
                InitializeWallBakedGIData(input, inputData);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseSample.rgb;
                surfaceData.metallic = metallic;
                surfaceData.specular = half3(0.04h, 0.04h, 0.04h);
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.occlusion = occlusion;
                surfaceData.emission = half3(0, 0, 0);
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
