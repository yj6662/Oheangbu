// D308-153b variant 우/M (2026-10-29): the painted wave models made by Meshy from the concept uks_1, drawn UNLIT - the painted colour map is the whole look.
//   colour = _MainTex x _Tint (no lighting, no fog, no normal map: the painting's indigo / white scale lines / cream outline stay as painted).
//   erosion = _Erode 0..1: a noise cut (world-space, so it eats the same way whatever the instance's size) with a cream burn line along the cut. alpha clip, no blending.
//   outline = a cream inverted hull (second pass, _OutlineW metres along the normal; 0 = off, the hull collapses in the vertex stage), eroded together with the paint.
// Rules (the ink-wash and the fog post of the main look paint a depth-writing piece as a dark silhouette):
//   (a) queue Transparent-1 (2999) so the transparent-range routing (VfxAfterFog308.Route) takes it; forward passes only - NO DepthOnly / DepthNormals / ShadowCaster passes;
//   (b) no function with an `out` parameter is ever called inside `?:` (HLSL evaluates both sides; here there is no such call and no `?:` on a call at all).
Shader "Oheangbu/SpellFx/MeshPaint"
{
    Properties
    {
        _MainTex ("Paint (colour map)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Erode ("Erode 0..1", Range(0, 1)) = 0
        _BurnW ("Burn line width", Float) = 0.07
        _BurnCol ("Burn colour (cream)", Color) = (0.93, 0.88, 0.76, 1)
        _OutlineW ("Outline width (m, 0 = off)", Float) = 0
        _OutlineCol ("Outline colour", Color) = (0.93, 0.88, 0.76, 1)
        _NoiseSeed ("Noise seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST, _Tint, _BurnCol, _OutlineCol;
            float _Erode, _BurnW, _OutlineW, _NoiseSeed;
        CBUFFER_END

        float hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float vnoise(float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(hash21(i), hash21(i + float2(1.0, 0.0)), f.x), lerp(hash21(i + float2(0.0, 1.0)), hash21(i + float2(1.0, 1.0)), f.x), f.y);
        }
        // the erosion field 0..1 at a world point: low values are eaten first (a little bias from below: the foot goes first, then the crest curls away)
        float ErodeField(float3 wp)
        {
            float2 q = wp.xz * 2.3 + wp.y * float2(1.7, -1.3) + _NoiseSeed * 17.0;
            return vnoise(q) * 0.62 + vnoise(q * 3.1 + 5.0) * 0.38;
        }
        float ErodeThreshold() { return lerp(-0.12, 1.08, saturate(_Erode)); }
        ENDHLSL

        Pass
        {
            Name "Paint"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                float3 wp = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(wp);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.wp = wp;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float n = ErodeField(i.wp);
                float thr = ErodeThreshold();
                clip(n - thr);
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * _Tint.rgb;
                float burn = saturate(1.0 - (n - thr) / max(_BurnW, 0.001)) * step(0.0001, _Erode);
                c = lerp(c, _BurnCol.rgb, burn);
                return half4(c, 1.0);
            }
            ENDHLSL
        }

        // the cream outline: an inverted hull. No LightMode tag = SRPDefaultUnlit, which URP draws after the forward pass (the second pass of one material).
        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct A { float4 pos : POSITION; float3 nrm : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                float3 wp = TransformObjectToWorld(i.pos.xyz);
                float3 wn = normalize(TransformObjectToWorldNormal(i.nrm));
                wp += wn * _OutlineW;
                o.pos = TransformWorldToHClip(wp);
                if (_OutlineW <= 0.0001) o.pos = float4(0.0, 0.0, 2.0, 1.0);     // off: a degenerate triangle, nothing is rasterised
                o.wp = wp;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                clip(ErodeField(i.wp) - ErodeThreshold());
                return half4(_OutlineCol.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
