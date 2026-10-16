// #308 휘두르기 먹 획 (SPEC-SPELL-VFX-OVERHAUL-308 11.10) — SwingInk308의 리본 메시가 쓴다.
// 계약: UV0.x = 머리(0, 붓끝) → 꼬리(1), UV0.y = 폭 방향(0..1), 정점색 a = 남은 먹(1 → 0).
// D308-90(11.34 20차): UV1.x = 스민 안료 비율(0 = 먹색 그대로), 정점색 rgb = 스미는 안료(선형). 꼴 · 알파 · 마름은 스밈과 무관.
// 붓끝 자취(InkTrail308, 기체)와 달리 액체다: 사라질 때 투명해지지 않고 붓털 결을 따라 갈라지며 끊긴다.
// 먹이지 빛이 아니다(ART-INK): 가산 · HDR 없음, 알파 블렌드만. 표현 전용.
Shader "Oheangbu/SwingInk308/InkArc"
{
    Properties
    {
        _InkColor("Ink Color", Color) = (0.05, 0.047, 0.045, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.95
        _StreakDensity("Streak Density (폭 방향 붓털 줄 수)", Range(2, 60)) = 14
        _DryStrength("Dry Strength (갈필 세기)", Range(0, 1.5)) = 0.85
        _EdgeSoftness("Edge Softness", Range(0.02, 0.6)) = 0.12
        _NearFade("Near Fade (x 안은 0, y부터 온전, m)", Vector) = (0.45, 1.2, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "InkArc"
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
                half _StreakDensity;
                half _DryStrength;
                half _EdgeSoftness;
                float4 _NearFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 uvd : TEXCOORD0; half soak : TEXCOORD1; half4 color : COLOR; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.uvd = float3(input.uv, distance(world, _WorldSpaceCameraPos));
                output.soak = (half)saturate(input.uv1.x);
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
                float2 uv = input.uvd.xy;
                half left = input.color.a;                                   // ink left in this part of the stroke
                // bristle lines: they run along the stroke (slow along x, fast across y), with a finer set inside
                half hair = Noise(float2(uv.x * 1.6, uv.y * _StreakDensity));
                half fine = Noise(float2(uv.x * 5.0 + 3.7, uv.y * _StreakDensity * 2.6));
                half grain = hair * 0.7 + fine * 0.3;
                half across = abs(uv.y - 0.5) * 2.0;
                // the edge: a firm wet edge at the head, ragged where the bristles run out toward the tail
                // the width is not even: the brush swells and pinches along the stroke (a slow wobble), more toward the tail
                half swell = (Noise(float2(uv.x * 3.1 + 9.2, 0.5)) - 0.5) * (0.18 + 0.3 * uv.x);
                half edge = across + swell + (grain - 0.5) * (0.2 + 0.7 * uv.x);
                half body = 1.0 - smoothstep(0.9 - _EdgeSoftness, 0.9, edge);     // the edge ends inside the ribbon: no line at the mesh border
                // drying: the threshold climbs toward the tail, toward the edges and as the ink runs out - the stroke
                // splits along its bristle lines and breaks, it does not turn transparent
                half dry = _DryStrength * (uv.x * 1.05 + across * across * 0.35) + (1.0 - left) * 1.1 - 0.1;
                half wet = smoothstep(dry - 0.05, dry + 0.06, grain + 0.3 * (1.0 - uv.x));
                half near = smoothstep(_NearFade.x, _NearFade.y, input.uvd.z);
                half alpha = _Opacity * body * wet * near * smoothstep(0.0, 0.08, left);
                clip(alpha - 0.004);
                // one ink colour: a paler edge read as an outline round the stroke (2026-10-14 stills)
                // D308-90: the spell's pigment soaks into it unevenly, along the bristle grain (no light: an LDR pigment, blended)
                half s = saturate(input.soak * (0.75 + 0.5 * grain));
                half3 ink = lerp(_InkColor.rgb, input.color.rgb, s);
                return half4(ink, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
