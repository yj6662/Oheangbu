// #308 소환수 먹 기운 (SPEC-SPELL-VFX-OVERHAUL-308 11.15, D308-83d) — 입자 Trails 리본이 쓴다.
// 계약: UV0.x = 리본 길이(_Flip으로 뿌리 0 → 끝 1에 맞춘다), UV0.y = 폭(0..1),
//       정점색 a = 남은 먹(입자 수명), 정점색 r = 가닥마다 다른 씨앗(입자 시작색에서 물려받음).
// 한 리본 = 털처럼 가는 먹 가닥 여러 올 + 그 둘레의 옅은 먹 안개. 몸에 붙은 뿌리는 짙고, 끝으로 갈수록 올이 성기고 끊긴다.
// 먹이지 빛이 아니다(ART-INK): 알파 블렌드만, 발광 없음. 표현 전용.
Shader "Oheangbu/SpellVFX308/InkStrand"
{
    Properties
    {
        _InkColor("Ink Color", Color) = (0.05, 0.047, 0.045, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.9
        _Strands("Strands across the ribbon", Range(2, 40)) = 9
        _Thin("Thinning toward the free end", Range(0, 1.5)) = 0.8
        _Haze("Haze round the strands", Range(0, 1)) = 0.3
        _Flip("Flip length (1 = UV.x 1 is the root)", Range(0, 1)) = 0
        _NearFade("Near Fade (x 안은 0, y부터 온전, m)", Vector) = (0.45, 1.2, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "InkStrand"
            Tags { "LightMode" = "UniversalForward" }     // the same pass as InkBleed308: layer 20 is drawn after the realm fog
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _InkColor;
                half _Opacity;
                half _Strands;
                half _Thin;
                half _Haze;
                half _Flip;
                float4 _NearFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 uvd : TEXCOORD0; half4 color : COLOR; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.uvd = float3(input.uv, distance(world, _WorldSpaceCameraPos));
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
                float u = lerp(input.uvd.x, 1.0 - input.uvd.x, step(0.5, _Flip));     // 0 at the body, 1 at the free end
                float v = input.uvd.y;
                float seed = input.color.r * 37.0;
                half left = input.color.a;
                half across = abs(v - 0.5) * 2.0;
                // the hairs: lines that run the length of the ribbon, wandering a little across it as they go
                float wander = (Noise(float2(u * 2.3 + seed, 1.7)) - 0.5) * 0.22;
                half hair = Noise(float2(u * 1.4 + seed, (v + wander) * _Strands + seed));
                half fine = Noise(float2(u * 4.5 + seed * 1.3, (v + wander) * _Strands * 2.7));
                half grain = hair * 0.68 + fine * 0.32;
                // dense at the root, fewer and broken hairs toward the end and as the ink runs out
                half thin = 0.2 + _Thin * u * 0.62 + (1.0 - left) * 0.5 + across * across * 0.2;
                half strand = smoothstep(thin - 0.07, thin + 0.09, grain);
                // a soft dark breath round the hairs (the gas the hairs are combed out of)
                half haze = _Haze * (1.0 - across * across) * (1.0 - u * 0.75) * (0.6 + 0.4 * Noise(float2(u * 3.0 + seed, v * 2.0)));
                half edge = 1.0 - smoothstep(0.62, 0.98, across + (grain - 0.5) * 0.5);
                half ends = smoothstep(0.0, 0.05, u) * (1.0 - smoothstep(0.7, 1.0, u));
                half near = smoothstep(_NearFade.x, _NearFade.y, input.uvd.z);
                half alpha = _Opacity * saturate(max(strand, haze)) * edge * ends * left * near;
                clip(alpha - 0.004);
                return half4(_InkColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
