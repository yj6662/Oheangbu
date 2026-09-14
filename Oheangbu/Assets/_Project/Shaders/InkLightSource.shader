// 상등 광원 실체 셰이더 — SPEC-SPIKE-WORLD-LOOKDEV §5-2 (광맥·봉수 — 지속 발광이 존재하는 유일한 파일, #152).
// Unlit: 색 = 팔레트 전역(_OhVeinColor = 오행 원석 탁화 / _OhBeaconColor = 봉수 오행색) × _SourceIntensity. LDR 기본(1.0 —
// 최대 채널 ≈0.41 < Bloom knee 0.5) — 「빛은 전구가 아니라 먹이다」. 빛 퍼짐(역번짐)은 이 셰이더가 아니라 프리팹의 Point Light가
// 이웃 InkWorld 램프를 소지로 물러나게 해서 생긴다 — 여기는 빛의 실체(광물 띠)만 그린다.
// 띠 마스크 = 판의 v 중심 띠를 월드 Fbm이 굽히고(_BandNoise) 끊는다(_VeinBreakup). 마스크 밖은 clip — 판 밖 벽이 그대로 보인다.
// 마스크 가장자리(_CoreStart 아래)는 먹 테두리(마스크 밖 = 먹, §5-2 문면). DepthNormals 알파 −1 = 광원 마커(포스트 씻김 면제, #151-ii).
// 한글 [Tooltip] 금지. 인식 불가침: 표현 전용.
Shader "Oheangbu/InkLightSource"
{
    Properties
    {
        [Header(Source TEST)]
        [ToggleUI] _UseVeinColor("Use Vein Color (1 = _OhVeinColor 광맥 / 0 = _OhBeaconColor 봉수)", Float) = 1
        _SourceIntensity("Source Intensity (1.0 = LDR 기본 · A/B 1.6)", Range(0, 2)) = 1

        [Header(Band Mask TEST)]
        _BandWidth("Band Width (판 높이 대비 띠 폭)", Range(0.05, 1)) = 0.45
        _BandEdge("Band Edge (띠 가장자리 부드러움)", Range(0.01, 0.5)) = 0.18
        _BandNoiseScale("Band Noise Scale (월드 m당 셀 수)", Float) = 2.5
        _BandNoiseStrength("Band Noise Strength (띠 중심이 굽는 폭)", Range(0, 1)) = 0.35
        _VeinBreakup("Vein Breakup (띠가 끊기는 정도)", Range(0, 1)) = 0.6
        _Cutoff("Cutoff (이 마스크값 아래 = clip)", Range(0.001, 0.5)) = 0.02
        _CoreStart("Core Start (이 마스크값부터 완전한 광물색 — 아래는 먹 테두리)", Range(0.05, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "InkNoise3D.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float2 uv         : TEXCOORD2;
        };

        CBUFFER_START(UnityPerMaterial)
            half _UseVeinColor;
            half _SourceIntensity;
            half _BandWidth;
            half _BandEdge;
            half _BandNoiseScale;
            half _BandNoiseStrength;
            half _VeinBreakup;
            half _Cutoff;
            half _CoreStart;
            half _Cull;
        CBUFFER_END

        // 팔레트 전역(WorldLookDriver) — CBUFFER 밖. 재질에 색 저장 0
        float4 _OhInkColor;
        float4 _OhVeinColor;
        float4 _OhBeaconColor;

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.uv = v.uv;
            return o;
        }

        // 띠 마스크 0~1 — 3패스 공유(보이는 형상 = 깊이 = 법선 = 마커)
        float BandMask(float3 posWS, float2 uv)
        {
            float center = 0.5 + (OhFbm3(posWS * _BandNoiseScale) - 0.5) * _BandNoiseStrength;
            float d = abs(uv.y - center) / max(_BandWidth * 0.5, 0.001);
            float band = 1.0 - smoothstep(1.0 - _BandEdge, 1.0 + _BandEdge, d);
            float breakup = smoothstep(0.3, 0.55, OhFbm3(posWS * _BandNoiseScale * 2.7 + 7.1));
            return band * lerp(1.0, breakup, _VeinBreakup);
        }
        ENDHLSL

        // ---- (1) 색 — Unlit 광물 띠 + 먹 테두리 ----
        Pass
        {
            Name "InkLightSourceForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings i) : SV_Target
            {
                float mask = BandMask(i.positionWS, i.uv);
                clip(mask - _Cutoff);
                float glow = smoothstep(_Cutoff, _CoreStart, mask);
                float3 src = lerp(_OhBeaconColor.rgb, _OhVeinColor.rgb, _UseVeinColor) * _SourceIntensity;
                float3 col = lerp(_OhInkColor.rgb, src, glow);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // ---- (2) 깊이 ----
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment DepthOnlyFrag

            half DepthOnlyFrag(Varyings i) : SV_TARGET
            {
                clip(BandMask(i.positionWS, i.uv) - _Cutoff);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        // ---- (3) 깊이+법선 — 알파 −1 = 광원 마커(법선 텍스처 R8G8B8A8_SNorm: 클리어 a=1 · 표준 a=0 · 광원 a=−1) ----
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            void DepthNormalsFrag(
                Varyings input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                clip(BandMask(input.positionWS, input.uv) - _Cutoff);

                #if defined(_GBUFFER_NORMALS_OCT)
                float3 normalWS = normalize(input.normalWS);
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                outNormalWS = half4(packedNormalWS, -1.0);
                #else
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                outNormalWS = half4(normalWS, -1.0);
                #endif

                #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
}
