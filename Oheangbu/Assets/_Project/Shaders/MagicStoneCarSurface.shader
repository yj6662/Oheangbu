// MagicStoneCar only: standard URP metallic PBR with local diffuse indirect-light support.
// The macro scene deliberately has zero ambient/reflection intensity. No global lighting writes.
Shader "Oheangbu/MagicStoneCarSurface"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Range(0,2)) = 1
        _MetallicGlossMap("Metallic R / Smoothness A", 2D) = "white" {}
        _Metallic("Metallic Scale", Range(0,1)) = 1
        _Smoothness("Smoothness Scale", Range(0,1)) = 0.85
        _OcclusionMap("Occlusion G", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0,1)) = 1
        _EmissionMap("Verified Core / Lantern Surface", 2D) = "white" {}
        [HDR] _EmissionColor("Core Emission", Color) = (0,0,0,1)
        [ToggleUI] _EmissionEnabled("Core Or Lantern Only", Float) = 0
        _Saturation("Texture Saturation", Range(0,1)) = 0.7
        _AmbientFloor("Local Diffuse Ambient Floor", Range(0,1)) = 0.4
        _WashStart("Wash Start (metres)", Float) = 800
        _WashEnd("Wash End (metres)", Float) = 9500
        _WashStrength("Wash Strength", Range(0,1)) = 0.58
        [ToggleUI] _AlphaClip("Alpha Clipping", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
        TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        // Identical material layout in every pass, including depth and shadow rendering.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Saturation;
            half _AmbientFloor;
            half _Metallic;
            half _Smoothness;
            half _OcclusionStrength;
            half _EmissionEnabled;
            half4 _EmissionColor;
            float _WashStart;
            float _WashEnd;
            half _WashStrength;
            half _AlphaClip;
            half _Cutoff;
            half _Cull;
        CBUFFER_END
        float4 _OhPaperColor;

        struct SurfaceAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct SurfaceVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float2 uv : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            half4 tangentWS : TEXCOORD3;
            half3 vertexLighting : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        SurfaceVaryings SurfaceVertex(SurfaceAttributes input)
        {
            SurfaceVaryings output = (SurfaceVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.normalWS = normal.normalWS;
            output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
            output.vertexLighting = VertexLighting(position.positionWS, normal.normalWS);
            return output;
        }
        half4 ReadAlbedo(float2 uv)
        {
            half4 colour = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            if (_AlphaClip > 0.5h) clip(colour.a - _Cutoff);
            return colour;
        }
        half3 ReadNormal(SurfaceVaryings input, half faceSign)
        {
            half3 normal = NormalizeNormalPerPixel(input.normalWS);
            // Imported meshes without tangents retain geometric normals rather than a broken TBN.
            if (dot(input.tangentWS.xyz, input.tangentWS.xyz) < 0.01h) return normal * faceSign;
            half3 tangent = normalize(input.tangentWS.xyz);
            half3 bitangent = input.tangentWS.w * cross(normal, tangent);
            half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
            return NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(tangent, bitangent, normal))) * faceSign;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend One Zero
            ColorMask RGBA
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SurfaceVertex
            #pragma fragment SurfaceFragment
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            half4 SurfaceFragment(SurfaceVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 albedo = ReadAlbedo(input.uv).rgb;
                half luminance = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
                albedo = lerp(luminance.xxx, albedo, saturate(_Saturation));
                half3 normal = ReadNormal(input, IS_FRONT_VFACE(facing, 1.0h, -1.0h));
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normal;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                inputData.shadowCoord = shadowCoord;
                inputData.shadowMask = half4(1,1,1,1);
                inputData.vertexLighting = input.vertexLighting;
                // Local irradiance enters the standard BRDF's indirect diffuse path, never surface emission.
                // A slight up/down difference preserves volume in the deliberately unlit macro environment.
                half localIrradiance = max(0.0h, _AmbientFloor) * lerp(0.85h,1.0h,saturate(normal.y*.5h+.5h));
                inputData.bakedGI = max(SampleSH(normal),localIrradiance.xxx);
                half4 packed = SAMPLE_TEXTURE2D(_MetallicGlossMap,sampler_MetallicGlossMap,input.uv);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.metallic = saturate(packed.r*_Metallic);
                surface.smoothness = saturate(packed.a*_Smoothness);
                surface.normalTS = half3(0,0,1); // World normal above already contains the authored normal map.
                surface.occlusion = lerp(1.0h,SAMPLE_TEXTURE2D(_OcclusionMap,sampler_OcclusionMap,input.uv).g,saturate(_OcclusionStrength));
                surface.emission = _EmissionEnabled>.5h ? SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,input.uv).rgb*_EmissionColor.rgb : half3(0,0,0);
                half3 colour = UniversalFragmentPBR(inputData,surface).rgb;
                float distanceWS = distance(_WorldSpaceCameraPos, input.positionWS);
                float wash = smoothstep(_WashStart, max(_WashEnd, _WashStart + 0.001), distanceWS) * saturate(_WashStrength);
                return half4(lerp(colour, _OhPaperColor.rgb, wash), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            SurfaceVaryings ShadowVertex(SurfaceAttributes input)
            {
                SurfaceVaryings output = SurfaceVertex(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - output.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, lightDirectionWS)));
                return output;
            }
            half4 ShadowFragment(SurfaceVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ReadAlbedo(input.uv);
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SurfaceVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            half DepthFragment(SurfaceVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ReadAlbedo(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SurfaceVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            void NormalsFragment(SurfaceVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC,
                out half4 outNormalWS : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
                #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ReadAlbedo(input.uv);
                float3 normal = ReadNormal(input, IS_FRONT_VFACE(facing, 1.0h, -1.0h));
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormal = PackNormalOctQuadEncode(normal);
                outNormalWS = half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), 0);
                #else
                outNormalWS = half4(normal, 0);
                #endif
                #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
