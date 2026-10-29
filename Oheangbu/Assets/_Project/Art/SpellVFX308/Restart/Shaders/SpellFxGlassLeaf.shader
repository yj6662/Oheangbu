// 2026-10-30 초성 ㄱ(목) (concept g308/concepts/가_C.png): the glass leaves of SpellFxVinePart - translucent jade glass, a fine cream edge and midrib,
// one KCISA wood pattern engraved inside (tile chosen by the leaf's seed from the body atlas; slots 1,2,3,4,5,7 like the ward). Lights up at the bite.
// uv0: x across 0..1, y base 0 .. tip 1. Vertex colour: r glow, g seed, b open, a fade.
Shader "Oheangbu/SpellFx/GlassLeaf"
{
    Properties
    {
        _Patterns ("Pattern atlas 4 x 2 (white line on black)", 2D) = "black" {}
        _Glass ("Glass (rgb, a = alpha)", Color) = (0.3, 0.62, 0.44, 0.42)
        _Line ("Edge / rib / pattern (HDR)", Color) = (0.95, 1.12, 0.92, 1)
        _Glow ("Bite glow (HDR)", Color) = (0.75, 1.3, 0.85, 1)
        _PatternA ("Pattern alpha", Range(0, 1)) = 0.75
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Patterns); SAMPLER(sampler_Patterns);
            CBUFFER_START(UnityPerMaterial)
                half4 _Glass, _Line, _Glow;
                float _PatternA;
                float4 _Patterns_ST;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : TEXCOORD1; };
            V Vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.col = i.col; return o; }
            half4 Frag(V i) : SV_Target
            {
                float x = i.uv.x - 0.5, y = i.uv.y;
                float halfW = 0.5 * pow(saturate(sin(3.14159 * saturate(y * 0.96 + 0.02))), 0.75) * (1.0 - 0.2 * y);
                float d = halfW - abs(x);                                    // > 0 inside
                float fw = max(fwidth(d), 1e-4);
                float inside = saturate(d / fw);
                if (inside <= 0.0) discard;
                float edge = smoothstep(fw * 2.5, fw * 0.5, d);
                float rib = smoothstep(fw * 2.0, 0.0, abs(x) - 0.006) * smoothstep(0.98, 0.85, y);
                // the pattern: one tile of the atlas, fitted in the leaf's middle
                float slots[6] = { 1, 2, 3, 4, 5, 7 };
                float slot = slots[(int)floor(saturate(i.col.g) * 5.999)];
                float2 puv = float2(x / max(halfW, 0.05) * 0.5 + 0.5, (y - 0.15) / 0.75);
                float pat = 0;
                if (all(puv > 0) && all(puv < 1))
                {
                    float2 cell = float2(fmod(slot, 4.0), floor(slot / 4.0));
                    float2 auv = (cell + puv) / float2(4.0, 2.0);
                    auv.y = 1.0 - auv.y;
                    pat = SAMPLE_TEXTURE2D(_Patterns, sampler_Patterns, auv).r * _PatternA;
                }
                float lineA = saturate(max(max(edge, rib * 0.8), pat));
                float glow = saturate(i.col.r * 1.5);
                float3 col = lerp(_Glass.rgb, _Line.rgb, lineA);
                col = lerp(col, _Glow.rgb, glow * 0.55);
                float a = lerp(_Glass.a, 0.95, lineA);
                a = saturate(a + glow * 0.3) * inside * i.col.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
