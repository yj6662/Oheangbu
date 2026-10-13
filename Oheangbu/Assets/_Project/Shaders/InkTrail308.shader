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
                float2 uv = input.uv;
                // 가장자리: 폭 가운데가 진하고, 가장자리는 길이를 따라 조금 울퉁불퉁하다
                half across = abs(uv.y - 0.5) * 2.0;
                half ragged = (Noise(float2(uv.x * 14.0, uv.y * 3.0)) - 0.5) * 0.25;
                half edge = 1.0 - smoothstep(1.0 - _EdgeSoftness + ragged, 1.0, across);
                // 갈필: 폭 방향으로 촘촘하고 길이 방향으로 긴 결. 꼬리로 갈수록, 옅을수록 결이 갈라진다
                half streak = Noise(float2(uv.y * _StreakDensity, uv.x * 2.5));
                half fine = Noise(float2(uv.y * _StreakDensity * 2.7 + 11.0, uv.x * 9.0));
                half dry = saturate(_DryStrength * (0.25 + 1.1 * uv.x));
                half body = saturate((lerp(streak, fine, 0.35) - dry * 0.75) / max(0.05, 1.0 - dry * 0.75));
                body = lerp(1.0, smoothstep(0.0, 0.5, body), saturate(dry * 1.4));
                half alpha = input.color.a * _Opacity * edge * body;
                clip(alpha - 0.004);
                return half4(_InkColor.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
