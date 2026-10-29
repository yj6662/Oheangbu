// 2026-10-30 초성 ㄱ(목) 경로 ㅏ (concept g308/concepts/가_C.png - "ink core, glass leaves"): the vine tubes of SpellFxVinePart.
// A black sumi-ink core with brush fibres along its length, a faint jade rim, and pale-jade seams that light up at the bite (vertex colour r).
// Vertex colour: r glow, g kind/4, b along 0..1, a fade (the vine dissolves in ink-noise holes, alpha-tested). uv0 = (metres along, around 0..1).
Shader "Oheangbu/SpellFx/Vine"
{
    Properties
    {
        _Ink ("Ink core", Color) = (0.02, 0.02, 0.018, 1)
        _InkHi ("Ink fibre highlight", Color) = (0.13, 0.125, 0.11, 1)
        _Rim ("Jade rim (rgb, a = strength)", Color) = (0.5, 0.8, 0.62, 0.2)
        _Spec ("Cream highlight (rgb, a = strength)", Color) = (0.95, 0.93, 0.85, 0.16)
        _Seam ("Seam light (HDR)", Color) = (0.8, 1.25, 0.9, 1)
        _SeamIdle ("Seam light at rest 0..1", Range(0, 1)) = 0.06
        _Fibres ("Fibres per metre", Float) = 9
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Ink, _InkHi, _Rim, _Seam, _Spec;
                float _SeamIdle, _Fibres;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : TEXCOORD1; float3 nrmWS : TEXCOORD2; float3 posWS : TEXCOORD3; };
            float Hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }
            V Vert(A i)
            {
                V o; o.posWS = TransformObjectToWorld(i.pos.xyz); o.pos = TransformWorldToHClip(o.posWS);
                o.nrmWS = TransformObjectToWorldNormal(i.nrm); o.uv = i.uv; o.col = i.col; return o;
            }
            half4 Frag(V i) : SV_Target
            {
                float along = i.uv.x, around = i.uv.y;
                // ink dissolve: holes open in noise as the fade drops
                float holes = VNoise(float2(along * 7.0, around * 5.0)) * 0.7 + VNoise(float2(along * 23.0, around * 13.0)) * 0.3;
                clip(holes - (1.0 - i.col.a) * 1.05);
                // brush fibres: streaks along the length, a little drift round
                float fib = VNoise(float2(around * 14.0 + along * 0.6, along * _Fibres * 0.15));
                fib = smoothstep(0.45, 0.9, fib) * 0.8 + smoothstep(0.7, 1.0, VNoise(float2(around * 31.0, along * 2.0))) * 0.4;
                float3 col = lerp(_Ink.rgb, _InkHi.rgb, saturate(fib));
                // light from above-front so the tube reads round
                float3 n = normalize(i.nrmWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.posWS));
                float lambert = saturate(dot(n, normalize(float3(0.3, 0.9, -0.2))) * 0.5 + 0.5);
                col *= 0.65 + 0.7 * lambert;
                float fres = pow(1.0 - saturate(dot(n, v)), 3.0);
                // r2 (Fable r1 #1): ink, not green - a thin jade edge (~20 %) and a wet cream highlight running along the stalk
                col += _Rim.rgb * _Rim.a * smoothstep(0.55, 1.0, fres);
                float3 h = normalize(normalize(float3(0.3, 0.9, -0.2)) + v);
                col += _Spec.rgb * _Spec.a * pow(saturate(dot(n, h)), 28.0) * (0.6 + 0.4 * fib);
                // seams: two thin spiral lines along the vine; they glow at the bite, faintly at rest
                float s1 = abs(frac(around * 2.0 + along * 1.7) - 0.5);
                float seam = smoothstep(0.06, 0.0, s1 - 0.0);
                float glow = saturate(_SeamIdle + i.col.r * 0.9);
                col = lerp(col, _Seam.rgb, seam * glow);
                col += _Seam.rgb * i.col.r * 0.5 * smoothstep(0.35, 1.0, fres);      // r3 (Fable r2 #2): the bite lights the EDGES, the stalk stays ink
                // 감's pillar (kind 7 -> g 0.7) is light, not ink: the waiting seed's column
                float pillar = step(abs(i.col.g - 0.7), 0.04);
                col = lerp(col, _Seam.rgb * (0.75 + 0.25 * i.col.r) * (0.7 + 0.3 * fib), pillar * 0.9);
                return half4(col, 1);
            }
            ENDHLSL
        }
        // r2: without these the depth / normals prepass never sees the vine, so InkWash297 and the realm fog took its pixels for sky and painted paper over it (flat mint)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float a : TEXCOORD1; };
            float Hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f); return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y); }
            V Vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.a = i.col.a; return o; }
            half4 Frag(V i) : SV_Target
            {
                float holes = VNoise(float2(i.uv.x * 7.0, i.uv.y * 5.0)) * 0.7 + VNoise(float2(i.uv.x * 23.0, i.uv.y * 13.0)) * 0.3;
                clip(holes - (1.0 - i.a) * 1.05);
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float a : TEXCOORD1; float3 n : TEXCOORD2; };
            float Hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f); return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y); }
            V Vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.a = i.col.a; o.n = TransformObjectToWorldNormal(i.nrm); return o; }
            half4 Frag(V i) : SV_Target
            {
                float holes = VNoise(float2(i.uv.x * 7.0, i.uv.y * 5.0)) * 0.7 + VNoise(float2(i.uv.x * 23.0, i.uv.y * 13.0)) * 0.3;
                clip(holes - (1.0 - i.a) * 1.05);
                return half4(normalize(i.n), 0);
            }
            ENDHLSL
        }
    }
}
