// #308 D308-127 후속: 무기 변신 휘두르기의 오방색 잔상 — 공중에 놓인 붓 획 하나(액체 먹 + 스민 안료).
// 계약(InkTrail308와 같다): UV0.x = 머리(0) → 꼬리(1), UV0.y = 폭 방향(0..1), 정점색 a = 농도(머리 진함 → 꼬리 0).
// 먹이지 빛이 아니다(ART-INK): 가산 · HDR · 발광 없음, 알파 블렌드만. 색은 _PigmentColor(원소) · _InkColor(심),
// _RimShare = 폭 가운데 먹 심이 차지하지 않는 몫(1 = 온통 안료, 0.4 = 가장자리 안료 테). 표현 전용.
Shader "Oheangbu/PigmentTrail308"
{
    Properties
    {
        _PigmentColor("Pigment Color", Color) = (0.24, 0.55, 0.42, 1)
        _InkColor("Ink Core Color", Color) = (0.05, 0.047, 0.045, 1)
        _RimShare("Rim Share (안료 테 몫)", Range(0, 1)) = 0.55
        _Opacity("Opacity", Range(0, 1)) = 0.9
        _StreakDensity("Streak Density (갈필 결 수)", Range(2, 60)) = 14
        _DryStrength("Dry Strength (꼬리 갈필 세기)", Range(0, 1)) = 0.75
        _EdgeSoftness("Edge Softness", Range(0.02, 0.6)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "PigmentTrail"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _PigmentColor;
                half4 _InkColor;
                half _RimShare;
                half _Opacity;
                half _StreakDensity;
                half _DryStrength;
                half _EdgeSoftness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            half Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                // the brush's bristle lines run along the stroke: one value per streak across, stretched along
                half streak = Noise(float2(uv.x * 2.0, uv.y * _StreakDensity));
                half fine = Noise(float2(uv.x * 7.0, uv.y * _StreakDensity * 2.3 + 3.7));
                // dry brush: toward the tail (and a little at the edges) streaks break off
                half dry = saturate(_DryStrength * (uv.x * 1.25 - 0.1));
                half across = abs(uv.y - 0.5) * 2.0;
                half edge = 1.0 - smoothstep(1.0 - _EdgeSoftness * 2.0, 1.0, across + (streak - 0.5) * 0.25);
                half keep = smoothstep(dry - 0.12, dry + 0.12, lerp(streak, fine, 0.35));
                half alpha = input.color.a * _Opacity * edge * keep;
                clip(alpha - 0.003);
                // ink core in the middle of the width, pigment soaked into the rim (rim share 1 = all pigment)
                half core = 1.0 - smoothstep(saturate(1.0 - _RimShare) - 0.08, saturate(1.0 - _RimShare) + 0.08, across);
                half3 colour = lerp(_PigmentColor.rgb, _InkColor.rgb, core);
                // a watery stroke is lighter where it is thin (tail), never brighter than its pigment
                colour = lerp(colour, colour * 0.85 + 0.15 * _PigmentColor.rgb, saturate(uv.x));
                return half4(colour * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
