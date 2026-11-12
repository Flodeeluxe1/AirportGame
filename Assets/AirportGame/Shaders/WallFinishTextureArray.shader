Shader "Airport/Wall Finish Texture Array"
{
    Properties
    {
        [NoScaleOffset] _BaseColorArray ("Base Color Array", 2DArray) = "" {}
        [NoScaleOffset] _NormalArray ("Normal Array", 2DArray) = "" {}
        [NoScaleOffset] _MaskArray ("Metallic AO Smoothness Array", 2DArray) = "" {}
        _BaseMap ("Original Wall Texture", 2D) = "white" {}
        _BaseColor ("Original Wall Color", Color) = (1,1,1,1)
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_BaseColorArray); SAMPLER(sampler_BaseColorArray);
            TEXTURE2D_ARRAY(_NormalArray); SAMPLER(sampler_NormalArray);
            TEXTURE2D_ARRAY(_MaskArray); SAMPLER(sampler_MaskArray);
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
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

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(positionInputs);
                #endif

                output.fogFactor =
                    ComputeFogFactor(positionInputs.positionCS.z);
                return output;
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

                half3 normalTS = half3(0,0,1);
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

                    half4 normalSample = SAMPLE_TEXTURE2D_ARRAY(
                        _NormalArray,
                        sampler_NormalArray,
                        finishUV,
                        finishIndex);

                    // Diagnostic: use a flat tangent-space normal to isolate wall lighting.
                    normalTS = half3(0,0,1);
                    // Diagnostic: bypass packed material-mask values while
                    // keeping the full PBR lighting calculation active.
                    metallic = 0.0h;
                    occlusion = 1.0h;
                    smoothness = 0.5h;
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

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord =
                    TransformWorldToShadowCoord(input.positionWS);
                #else
                inputData.shadowCoord = float4(0,0,0,0);
                #endif

                // Diagnostic: force full ambient GI on finished wall faces
                // while retaining direct lighting, shadows and the PBR BRDF.
                inputData.bakedGI =
                    isWallSide && finishIndex >= 0.0
                        ? half3(1.0h, 1.0h, 1.0h)
                        : SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1,1,1,1);

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
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
