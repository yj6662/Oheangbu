// #308 붓끝 먹 잔상 — BrushInkTrail308 (TrailRenderer)가 쓴다.
// 계약: UV0.x = 머리(0) → 꼬리(1), UV0.y = 폭 방향(0..1), 정점색 a = 농도(머리 진함 → 꼬리 0).
// 먹이지 빛이 아니다(ART-INK): 가산 · HDR 없음, 알파 블렌드만. 표현 전용.
Shader "Oheangbu/InkTrail308"
{
    Properties
    {
        _InkColor("Ink Color", Color) = (0.05, 0.047, 0.045, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.82
        _StreakDensity("Streak Density (폭 방향 결 수)", Range(2, 60)) = 18
        _DryStrength("Dry Strength (갈필 세기)", Range(0, 1)) = 0.7
        _EdgeSoftness("Edge Softness", Range(0.02, 0.6)) = 0.22
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "InkTrail"
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
                half4 _InkColor;
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
                // 2026-10-13 (사용자): 잔상은 "기체" — 붓 글씨(액체)와 섞여 보이지 않게, 먹 연기처럼 옅고 피어오르며 흩어진다.
                float2 uv = input.uv;
                float t = _Time.y;
                // 연기 결: 꼬리로 갈수록 크게 일렁이며 위로(폭 방향) 흘러 풀린다
                float2 flow = float2(uv.x * 3.0 - t * 0.35, uv.y * 2.2 + t * 0.22 + uv.x * 1.5);
                half big = Noise(flow);
                half small = Noise(flow * 2.7 + float2(4.1, -t * 0.5));
                half puff = lerp(big, small, 0.4);
                // 가장자리: 선이 아니라 뭉게 — 폭 전체가 부드럽게 꺼지고, 연기 결이 가장자리를 파먹는다
                half across = abs(uv.y - 0.5 + (big - 0.5) * 0.35 * uv.x) * 2.0;
                half body = 1.0 - smoothstep(0.15, 1.0, across);
                body *= body;
                // 흩어짐: 머리는 조금 진하고 꼬리로 갈수록 연기 결의 임계가 올라가 구멍이 나며 사라진다
                half thin = saturate(_DryStrength * (0.15 + 1.25 * uv.x));
                half wisp = smoothstep(thin * 0.85, thin * 0.85 + 0.45, puff + 0.25 * (1.0 - uv.x));
                half alpha = input.color.a * _Opacity * body * wisp * (1.0 - 0.35 * _EdgeSoftness);
                clip(alpha - 0.003);
                // 연기는 먹보다 옅은 회색으로 풀린다
                half3 smoke = lerp(_InkColor.rgb, _InkColor.rgb + 0.22, saturate(uv.x * 1.2 + (1.0 - body) * 0.5));
                return half4(smoke * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
