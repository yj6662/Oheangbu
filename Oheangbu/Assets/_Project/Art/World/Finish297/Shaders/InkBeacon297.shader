// #297 candidate-only 광원 실체 "마석 불빛" (SPEC-WORLD-FINISH-297 attraction, TEST): the 산정 마석 봉수 core, the 주막 등불
// paper and the 성황당 candle flame. Derived from Oheangbu/InkLightSource (shared, untouched) but HDR: colour = _Color (hue;
// peak-normalised so every 오행색 reaches the same HDR peak) x _Intensity, a small chroma push that pre-compensates the post
// desaturation. No neon: an irregular ink-noise flicker rising through the body and a 역번짐 dark rim (ink creeping in at the
// silhouette) — 「빛은 전구가 아니라 먹이다」. DepthNormals alpha -1 = light-source marker (InkWash297 keeps the HDR colour,
// RealmFog297 keeps it out of the air). No shadow caster (a light source casts no shadow). Presentation only.
Shader "Oheangbu/Finish297/InkBeacon"
{
    Properties
    {
        [Header(Light)]
        _Color("Light colour (hue)", Color) = (.95, .36, .18, 1)
        _Intensity("HDR peak intensity", Range(0, 8)) = 4
        [ToggleUI] _NormalizePeak("Normalise the colour peak to 1", Float) = 1
        _Chroma("Chroma push (pre-compensates the post desaturation)", Range(1, 2)) = 1.25
        _CoreLow("Body brightness away from the hot core", Range(0, 1)) = .55
        [Header(Ink)]
        _Flicker("Flicker amount", Range(0, .5)) = .12
        _FlickerSpeed("Flicker speed", Range(0, 4)) = 1.1
        _NoiseScale("Ink noise cells per metre", Float) = 2.2
        _RimInk("Dark rim (ink bleeding in at the silhouette)", Range(0, 1)) = .55
        _RimPower("Dark rim width (power)", Range(.5, 8)) = 2.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Intensity, _NormalizePeak, _Chroma, _CoreLow, _Flicker, _FlickerSpeed, _NoiseScale, _RimInk, _RimPower, _Cull;
        CBUFFER_END
        float4 _OhInkColor;   // palette global (WorldLookDriver), outside the CBUFFER

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float3 originWS : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings BeaconVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.originWS = TransformObjectToWorld(float3(0, 0, 0));
            return output;
        }

        float Hash31(float3 p) { p = frac(p * .1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }
        float Noise3(float3 p)
        {
            float3 i = floor(p), f = frac(p); float3 u = f * f * (3 - 2 * f);
            float a = lerp(Hash31(i), Hash31(i + float3(1, 0, 0)), u.x), b = lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), u.x);
            float c = lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), u.x), d = lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + 1), u.x);
            return lerp(lerp(a, b, u.y), lerp(c, d, u.y), u.z);
        }
        float Noise1(float t) { float i = floor(t), f = frac(t); return lerp(Hash31(i.xxx), Hash31((i + 1).xxx), f * f * (3 - 2 * f)); }

        // hue with a chroma push, peak-normalised (the HDR level is then _Intensity for every 오행색)
        float3 LightColour()
        {
            float3 c = _Color.rgb; float l = dot(c, float3(.2126, .7152, .0722));
            c = max(0, l + _Chroma * (c - l));
            return _NormalizePeak > .5 ? c / max(max(c.r, max(c.g, c.b)), 1e-3) : c;
        }
        ENDHLSL

        // ---- (1) colour: unlit HDR body, rising ink-noise flicker, 역번짐 dark rim ----
        Pass
        {
            Name "InkBeaconForward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex BeaconVertex
            #pragma fragment BeaconFragment
            #pragma multi_compile_instancing

            half4 BeaconFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 n = normalize(input.normalWS); float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float ndv = saturate(abs(dot(n, v)));
                float seed = Hash31(input.originWS * .37 + 11.3) * 31;
                float t = _Time.y * _FlickerSpeed;
                // body turbulence rising through the flame (object-relative, so the noise does not swim with the world)
                float3 p = (input.positionWS - input.originWS) * _NoiseScale + seed;
                float turb = Noise3(p + float3(0, -t * 1.6, 0)) * .65 + Noise3(p * 2.3 + float3(5.2, -t * 2.9, 1.7)) * .35;
                // slightly irregular breathing, never a regular pulse
                float breath = 1 + _Flicker * ((Noise1(t * 1.9 + seed) - .5) * 1.4 + (Noise1(t * 5.3 + seed * 2.1) - .5) * .6);
                float heat = saturate(pow(ndv, 1.5) * .7 + turb * .55 - .1);
                float3 col = LightColour() * _Intensity * breath * lerp(_CoreLow, 1, heat);
                float3 ink = any(_OhInkColor.rgb) ? _OhInkColor.rgb : float3(.024, .019, .016);
                float rim = saturate(pow(1 - ndv, _RimPower) * _RimInk * (.7 + turb * .6));
                return half4(lerp(col, ink, rim), 1);
            }
            ENDHLSL
        }

        // ---- (2) depth ----
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex BeaconVertex
            #pragma fragment BeaconDepthFragment
            #pragma multi_compile_instancing

            half BeaconDepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // ---- (3) depth + normals: alpha -1 = light-source marker (R8G8B8A8_SNorm: clear a=1, standard a=0, light a=-1) ----
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex BeaconVertex
            #pragma fragment BeaconNormalsFragment
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            void BeaconNormalsFragment(
                Varyings input,
                FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC,
                out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                outNormalWS = half4(packedNormalWS, -1.0);
                #else
                outNormalWS = half4(normalWS, -1.0);
                #endif
                #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
