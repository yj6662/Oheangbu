// C2 only: one shared surface, with camera-to-fragment atmospheric transmittance.
// No layer index, object-distance tint, emission or second post-process wash.
Shader "Oheangbu/CodexMountain"
{
    Properties
    {
        _RockTone("Shared rock palette tone", Range(0,1)) = .16
        _Ambient("Sky diffuse", Range(0,1)) = .30
        _Diffuse("Sun diffuse", Range(0,2)) = .95
        _Variation("Rock weathering", Range(0,.5)) = .18
        _AtmosphereStart("Clear air distance (m)", Float) = 65
        _AtmosphereDensity("Extinction per metre", Float) = .00085
        _AtmosphereTone("Atmosphere palette tone", Range(0,1)) = .68
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "InkNoise3D.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _RockTone, _Ambient, _Diffuse, _Variation;
            float _AtmosphereStart, _AtmosphereDensity, _AtmosphereTone;
        CBUFFER_END
        float4 _OhInkColor, _OhPaperColor;
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float cavity : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings MountainVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.cavity = input.color.r;
            return output;
        }
        ENDHLSL
        Pass
        {
            Name "MountainForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MountainVertex
            #pragma fragment MountainFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half4 MountainFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(input.positionCS);
                #endif
                float3 n = normalize(input.normalWS);
                Light sun = GetMainLight();
                // Continuous diffuse shading retains the same albedo on every mountain.
                float light = _Ambient * lerp(.65, 1.0, saturate(n.y))
                    + saturate(dot(n, sun.direction)) * Luminance(sun.color) * _Diffuse;
                float3 p = input.positionWS;
                float broad = OhFbm3(p * .075);
                float fine = OhValueNoise3(p * .42 + 17.2);
                float footprint = max(length(ddx(p)), length(ddy(p)));
                float resolved = 1.0 - smoothstep(1.0, 4.0, footprint);
                float weathering = (broad - .5) * 1.4 + (fine - .5) * .35 * resolved;
                float3 albedo = lerp(_OhInkColor.rgb, _OhPaperColor.rgb, saturate(_RockTone));
                float3 surface = albedo * max(.08, light) * (1.0 + weathering * _Variation)
                    * lerp(.72, 1.0, saturate(input.cavity));
                float d = distance(GetCameraPositionWS(), input.positionWS);
                float transmission = exp(-max(0.0, _AtmosphereDensity) * max(0.0, d - _AtmosphereStart));
                float3 air = lerp(_OhInkColor.rgb, _OhPaperColor.rgb, saturate(_AtmosphereTone));
                return half4(lerp(air, surface, transmission), 1.0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MountainVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            half DepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(input.positionCS);
                #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MountainVertex
            #pragma fragment NormalFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(input.positionCS);
                #endif
                float3 normal = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normal);
                return half4(PackFloat2To888(saturate(oct * .5 + .5)), 0);
                #else
                return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
