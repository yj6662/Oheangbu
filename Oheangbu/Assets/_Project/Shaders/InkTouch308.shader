// #308 (사용자 2026-10-13): 획을 시작한 순간 붓이 닿은 자리에 먹물이 번져 퍼진다 — InkTouchSplat308 이 쓴다.
// 계약: 쿼드 UV 0..1(가운데 0.5), _Birth = 닿은 시각(Time.time), _Seed = 획별 결.
// 먹이지 빛이 아니다(ART-INK): 알파 블렌드만. 표현 전용 — 인식 · 판정은 이 파일을 모른다.
Shader "Oheangbu/InkTouch308"
{
    Properties
    {
        _InkColor("Ink Color", Color) = (0.04, 0.037, 0.034, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.96
        _Birth("Birth (Time.time)", Float) = 0
        _Seed("Seed", Float) = 0
        _SpreadSeconds("Spread Seconds (퍼지는 시간)", Float) = 0.22
        _Fingers("Fingers (가장자리 삐침 세기)", Range(0, 1)) = 0.55
        _Droplets("Droplets (튄 방울)", Range(0, 1)) = 0.6
        _Fling("Fling (1 = 획 끝 흩뿌림: 쿼드 +X 가 그은 방향)", Float) = 0
        _FadeMul("Fade Multiplier (획과 함께 사라짐)", Range(0, 1)) = 1
        _DissolveT("Dissolve (획과 함께 증발)", Range(0, 1)) = 0
        _WetColor("Wet Color (윤기 색)", Color) = (0.42, 0.44, 0.47, 1)
        _WetSheen("Wet Sheen", Range(0, 1)) = 0
        _WetSeconds("Wet Seconds", Float) = 3.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+1" "IgnoreProjector" = "True" }
        Pass
        {
            Name "InkTouch"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _InkColor;
                half4 _WetColor;
                half _Opacity;
                float _Birth;
                float _Seed;
                half _SpreadSeconds;
                half _Fingers;
                half _Droplets;
                half _Fling;
                half _FadeMul;
                half _DissolveT;
                half _WetSheen;
                half _WetSeconds;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_Fling > 0.5)
                {
                    // 획 끝에서 그은 방향(+X)으로 튄 먹 (2026-10-13 다시): 격자가 보이지 않게 크기 · 각도가 다른 세 겹의 흩어진 점.
                    // 가까이는 조금 굵고 몇 개, 멀수록 잘고 드물다. 방울은 둥글다(길쭉한 타원 없음).
                    float along = input.uv.x, across = (input.uv.y - 0.5) * 2.0;
                    float flingAge = max(_Time.y - _Birth, 0.0);
                    float landed = step(along * 0.12, flingAge);
                    float cone = 0.06 + 0.5 * along * along + 0.18 * along;
                    float inCone = 1.0 - smoothstep(cone * 0.6, cone, abs(across));
                    float2 q = float2(along, across * 0.5);                             // 쿼드는 가로 1 : 세로 0.8 — 방울이 둥글게
                    float flung = 0.0;
                    for (int layer = 0; layer < 3; layer++)
                    {
                        float scale = layer == 0 ? 5.0 : layer == 1 ? 9.0 : 15.0;
                        float turn = 0.6 + 1.3 * layer + _Seed;
                        float2x2 spin = float2x2(cos(turn), -sin(turn), sin(turn), cos(turn));
                        float2 g = mul(spin, q) * scale + _Seed * (1.0 + layer);
                        float2 c = floor(g), l = frac(g) - 0.5;
                        float2 offset = (float2(Hash(c + 1.1 + layer), Hash(c + 7.7 + layer)) - 0.5) * 0.7;
                        float radius = lerp(0.06, 0.2, Hash(c + 5.5 + layer)) * lerp(1.0, 0.45, along) * (layer == 0 ? 1.0 : 0.8);
                        float drop = 1.0 - smoothstep(radius - 0.035, radius, length(l - offset));
                        float chance = (layer == 0 ? 0.16 : layer == 1 ? 0.2 : 0.14) * _Droplets * lerp(1.2, 0.5, along);
                        flung = max(flung, drop * step(1.0 - chance, Hash(c + 3.3 + layer)));
                    }
                    flung *= inCone * landed * step(0.03, along);
                    float grainF = Noise(input.uv * 9.0 + _Seed);
                    float goneF = smoothstep(_DissolveT * 1.25, _DissolveT * 1.25 + 0.08, grainF);
                    half alphaF = flung * _Opacity * _FadeMul * goneF;
                    clip(alphaF - 0.004);
                    return half4(_InkColor.rgb * alphaF, alphaF);
                }
                float2 p = (input.uv - 0.5) * 2.0;
                float r = length(p);
                float a = atan2(p.y, p.x);
                float age = max(_Time.y - _Birth, 0.0);
                // 빠르게 번지다 멎는다
                float grow = 1.0 - exp(-age / max(_SpreadSeconds, 1e-3));
                // 번진 먹의 가장자리: 둥근 몸 + 느린 요철 + 몇 군데 삐침
                float2 ring = float2(cos(a), sin(a));
                float lobes = Noise(ring * 1.6 + _Seed) - 0.5;
                float spikes = pow(saturate(Noise(ring * 4.5 + _Seed * 1.7 + 9.0)), 5.0);
                float radius = (0.34 + 0.16 * lobes + _Fingers * 0.42 * spikes) * lerp(0.35, 1.0, grow);
                float blot = 1.0 - smoothstep(radius - 0.05, radius, r);
                // 튄 방울: 몸 바깥에 작은 점 몇 개, 번짐보다 조금 늦게 앉는다
                float2 cell = floor(p * 4.0 + _Seed), local = frac(p * 4.0 + _Seed) - 0.5;
                float pick = Hash(cell + 3.3);
                float2 jitter = (float2(Hash(cell + 1.1), Hash(cell + 7.7)) - 0.5) * 0.5;
                float dotSize = lerp(0.04, 0.11, Hash(cell + 5.5));
                float drop = (1.0 - smoothstep(dotSize - 0.04, dotSize, length(local - jitter)))
                    * step(1.0 - 0.3 * _Droplets, pick) * step(radius + 0.06, r) * step(r, 0.95) * smoothstep(0.35, 0.8, grow);
                float shape = saturate(blot + drop);
                // 획과 같은 증발: 결 임계를 쓸어올린다
                float grain = Noise(p * 9.0 + _Seed);
                float gone = smoothstep(_DissolveT * 1.25, _DissolveT * 1.25 + 0.08, grain);
                half alpha = shape * _Opacity * _FadeMul * gone;
                clip(alpha - 0.004);
                // 젖은 윤기: 한쪽으로 치우친 초승달
                half wet = exp(-age / max(_WetSeconds, 1e-3));
                half sheen = (1.0 - smoothstep(0.03, 0.09, abs(length(p - float2(-0.07, 0.09)) - radius * 0.62))) * step(p.x, 0.05) * blot * wet * _WetSheen;
                half3 colour = lerp(_InkColor.rgb, _WetColor.rgb, sheen);
                return half4(colour * alpha, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
