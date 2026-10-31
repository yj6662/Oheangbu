// 2026-10-31 D308-161 / D308-162 (110 attack glyphs = sharp ink brush stroke + body material). Variant B of the A/B pair (same mesh + property contract as A).
// Look = the approved riposte stroke's ink language (RiposteInk308) on a flying ribbon, written from scratch for the stroke spells:
//   matte sumi ink, NO gloss / highlight / translucent film; dry-brush strands along the length only (the bright grain is the BACKGROUND showing
//   through a gap, never a light colour); wet round head -> dry comb tail; written in brush order (ragged front, never a soft fade);
//   dries by a crack field that opens and drops pieces (never thinned into a film); both ends are razor flicks.
//   The accent colour is LIGHT only: a hair-thin core line along the centre (vertex r * _CoreK) + a faint tinted spill beside it. No bloom needed.
// OPAQUE CUTOUT, double sided, depth tested, writes depth. DepthOnly / DepthNormals run the very same cut (without them InkWash297 / realm fog take the
// pixels for sky and paint paper over them).
// Mesh contract (shared with variant A - do not change):
//   uv0.x = along 0 (tail / brush side) .. 1 (head / target side), uv0.y = across 0..1 (0.5 = centre)
//   uv1.x = stroke seed 0..1, uv1.y = layer: 0 main stroke, 0.5 afterimage, 1 ground stain
//   vertex colour: r = core line strength, g = wetness (0 dry .. 1 wet), b = reserved, a = ink density / dissolve (1 loaded, lower = starved)
// Layers: main = full look. afterimage (0.25..0.75) = the same strands but thinned by _AfterAlpha * a (whole bristle strands drop out, a little screen dither
//   softens the cut), drier, no core line, only a faint accent rim from vertex r. ground stain (> 0.75) = mostly wet solid ink (few gaps - the red earth would
//   swallow the grain), brushy ragged edge, faint accent rim from vertex r. The ground ribbon must be lifted 1-2 cm by the builder (no depth offset here).
// Cannot be compiled by the offline compiler: written conservatively after RiposteInk308 / SpellFxVine (same includes, same pass skeleton).
Shader "Oheangbu/SpellFx/InkStrokeB"
{
    Properties
    {
        _InkWet("Wet ink", Color) = (0.0549, 0.0510, 0.0471, 1)
        _InkDry("Dry grey-black", Color) = (0.2000, 0.1843, 0.1686, 1)
        [HDR] _Accent("Accent (core line light)", Color) = (0.4, 1.4, 0.9, 1)
        _Reveal("Written up to (uv0.x)", Range(0, 1)) = 1
        _TailCut("Tail eaten up to (uv0.x)", Range(0, 1)) = 0
        _Dry("Drying: cracks open, pieces fall (0 wet .. 1 gone)", Range(0, 1)) = 0
        _CoreK("Core line strength (x vertex r)", Range(0, 1)) = 1
        _BristleScale("Bristle density (1 = 22 strands across the width)", Float) = 1
        [NoScaleOffset] _BristleTex("Bristle mask (R; optional - without one a procedural strand field is used)", 2D) = "white" {}
        _AfterAlpha("Afterimage ink density", Range(0, 1)) = 0.6
        _Cutoff("Cut bias (0.5 = neutral; higher eats more)", Range(0, 1)) = 0.5
        _HeadRag("Ragged written front", Range(0, 0.2)) = 0.06
        _NearClip("Nothing nearer than (m)", Float) = 0.12
        _NearFade("Whole from (m)", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; float4 col : TEXCOORD2; float eye : TEXCOORD3; };
        TEXTURE2D(_BristleTex); SAMPLER(sampler_BristleTex);
        CBUFFER_START(UnityPerMaterial)
            half4 _InkWet, _InkDry; half4 _Accent;
            float4 _BristleTex_TexelSize;
            float _Reveal, _TailCut, _Dry, _CoreK, _BristleScale, _AfterAlpha, _Cutoff, _HeadRag, _NearClip, _NearFade;
        CBUFFER_END
        // ink ceiling (display .85): the ink never gets lighter than this; only the accent may exceed it
        #ifdef UNITY_COLORSPACE_GAMMA
            #define IK_INK_CEILING 0.85
        #else
            #define IK_INK_CEILING 0.692
        #endif

        float IkHash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }
        float IkNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(IkHash(i), IkHash(i + float2(1, 0)), f.x), lerp(IkHash(i + float2(0, 1)), IkHash(i + float2(1, 1)), f.x), f.y);
        }
        float IkDither(float2 pixel) { return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))); }
        // cellular cracks: returns F2 - F1 (0 on a crack line, grows towards a flake centre) and the hash of the flake
        float IkCrack(float2 p, out float cellHash)
        {
            float2 ip = floor(p), fp = frac(p);
            float f1 = 8.0, f2 = 8.0; cellHash = 0.0;
            [unroll] for (int y = -1; y <= 1; y++)
            {
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 cell = ip + g;
                    float2 o = float2(IkHash(cell), IkHash(cell + 19.3));
                    float2 r = g + o - fp;
                    float d = dot(r, r);
                    if (d < f1) { f2 = f1; f1 = d; cellHash = IkHash(cell + 7.1); }
                    else if (d < f2) { f2 = d; }
                }
            }
            return sqrt(f2) - sqrt(f1);
        }

        Varyings vert(Attributes v)
        {
            Varyings o;
            float3 ws = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(ws);
            o.uv = v.uv; o.uv1 = v.uv1; o.col = v.color;
            o.eye = -TransformWorldToView(ws).z;
            return o;
        }

        struct InkInfo { float S; float wet; float dryN; float across; float core; float coreAmt; float halo; float ghost; float ground; float coreIn; };

        // The one cut. Forward, DepthOnly and DepthNormals all run it so the depth / normals prepass sees exactly the pixels the colour pass draws.
        void InkCut(Varyings i, out InkInfo k)
        {
            float u = i.uv.x, v = i.uv.y;
            float pxv = fwidth(v);                    // before any clip: one screen pixel in uv.y units (~1 / the ribbon's width in px)
            float seed = i.uv1.x * 41.7;
            float layer = i.uv1.y;
            float ghost = step(0.25, layer) * step(layer, 0.75);
            float ground = step(0.75, layer);
            float a = i.col.a;
            float cutBias = (_Cutoff - 0.5) * 0.5;

            clip(a - 0.02);
            clip(_Reveal - 0.001);

            // ---- bristle strands (lengthwise only): one random lane per strand, smooth along the stroke ----
            float strands = 22.0 * max(_BristleScale, 0.05);
            float x = v * strands + seed * 3.1;
            float id = floor(x), fx = frac(x);
            float hs = IkHash(float2(id, seed));
            float hs2 = IkHash(float2(id + 71.0, seed + 3.0));
            float lane = IkNoise(float2(id * 1.7 + seed, u * 2.6 + hs * 9.0));
            float fine = IkNoise(float2(x * 0.6, u * 11.0 + seed));
            float S = hs * 0.40 + lane * 0.40 + fine * 0.20;
            S = saturate((S - 0.5) * 1.5 + 0.5);
            // an authored mask replaces the procedural field (the built-in default texture is 4x4; anything bigger counts as authored)
            float tex = SAMPLE_TEXTURE2D(_BristleTex, sampler_BristleTex, float2(u * 3.0 * _BristleScale + seed * 0.37, v + seed * 0.11)).r;
            S = lerp(S, tex, step(8.0, _BristleTex_TexelSize.z));
            // hairline gap between neighbouring strands (only shows where the ink runs dry)
            float prof = smoothstep(0.0, 0.2, fx) * (1.0 - smoothstep(0.8, 1.0, fx));
            S = saturate(S - (1.0 - prof) * 0.20);

            // ---- wet head -> dry tail ----
            float wet = saturate(i.col.g) * smoothstep(0.05, 0.9, u);
            wet *= 1.0 - ghost * 0.6;
            wet = lerp(wet, max(wet, 0.85), ground);
            float starve = saturate(1.0 - a);
            float dryN = saturate((1.0 - wet) + starve * 0.6 + _Dry * 0.5);
            dryN *= 1.0 - ground * 0.7;

            // ---- core line (exempt from the dry-brush gaps only, not from the strand drop-out, the silhouette or the cracks) ----
            // 2026-10-31 fix: capped at 6 % of the width - on a ribbon only 6 - 10 px wide (a coil, a cage branch, a far or end-on stroke) the line was 20 - 50 % of it, the x3.5 spill
            // covered the rest and the HDR accent turned the whole stroke white
            float halfw = clamp(pxv * 0.9, 0.025, 0.06);
            float dv = abs(v - 0.5);
            float core = saturate(1.0 - dv / halfw);
            float coreAmt = i.col.r * _CoreK * (1.0 - ghost) * (1.0 - ground) * (1.0 - smoothstep(0.35, 0.85, _Dry));
            // a ribbon narrower than ~14 px on screen carries no light line: it stays ink (gone below ~6 px; TEST)
            coreAmt *= 1.0 - smoothstep(0.07, 0.16, pxv);
            coreAmt *= smoothstep(0.0, 0.25, u);      // the line fades into the dry tail instead of running bare past the last bristle
            float coreHit = step(0.02, core * coreAmt);

            // ---- written in brush order: the front is ragged by the strands (each strand leads or lags), never faded ----
            float rag = _HeadRag * saturate((1.0 - _Reveal) * 40.0);
            clip(_Reveal + (hs - 0.5) * rag * 2.0 + (S - 0.5) * rag - u);
            // the tail eaten away (strands let go one by one)
            clip(u - (_TailCut + (hs2 - 0.5) * 0.08 * saturate(_TailCut * 40.0)));
            // dry comb tail: each strand lets go at its own place, so the tail ends as separate bristles
            float drop = hs2 * 0.42 * saturate(1.0 - wet) * (1.0 - ground * 0.8);
            clip(u - drop);

            // ---- silhouette: razor flick at both ends, strands eat the rim when dry ----
            float across = 1.0 - abs(v * 2.0 - 1.0);
            float tT = smoothstep(0.0, 0.16, u);
            float tH = 1.0 - smoothstep(0.92, 1.0, u);
            float env = across - (1.0 - tT) * 0.92 - (1.0 - tH) * 0.85;
            float edgeNoise = (S - 0.5) * (0.12 + 0.55 * dryN);
            edgeNoise += (IkNoise(float2(u * 38.0 + seed, v * 7.0)) - 0.5) * 0.25 * ground;
            clip(env + edgeNoise - 0.03 - cutBias);

            // ---- dry-brush gaps (the background shows through) ----
            float gapThr = dryN * (0.38 + 0.58 * (1.0 - across));
            clip(S - gapThr + 0.05 + coreHit - cutBias);

            // ---- afterimage: whole strands drop out by _AfterAlpha * a ----
            float ag = saturate(_AfterAlpha * a);
            clip(lerp(1.0, S - (1.0 - ag) + (IkDither(i.positionCS.xy) - 0.5) * 0.08, ghost));

            // ---- drying: a crack field opens and pieces fall; the tail dries before the head; the stroke keeps its shape while it breaks ----
            float dryLocal = saturate(pow(saturate(_Dry), 1.6) * 1.4 - u * 0.3);
            if (_Dry > 0.001)
            {
                float2 p = float2(u * 7.0, v * 2.4);
                p += (IkNoise(p * 2.3 + seed) - 0.5) * 0.45;
                p += seed * 0.37;
                float h1;
                float ed = IkCrack(p, h1);
                float crack = saturate(ed * 1.8 + (h1 - 0.5) * 0.35);
                clip(crack - dryLocal - 0.001);
            }

            // ---- first person: nothing is cut hard at the near plane (dithered) ----
            float nv = saturate((i.eye - _NearClip) / max(_NearFade - _NearClip, 0.001));
            clip(nv - IkDither(i.positionCS.xy) - 0.001);

            k.S = S; k.wet = wet; k.dryN = dryN; k.across = across;
            k.core = core; k.coreAmt = coreAmt;
            k.halo = pow(saturate(1.0 - dv / (halfw * 3.5)), 2.0);
            k.ghost = ghost; k.ground = ground; k.coreIn = i.col.r;
        }
        ENDHLSL
        Pass
        {
            Name "InkStroke"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                InkInfo k;
                InkCut(i, k);
                // flat matte ink: wet #0E0D0C-ish -> dry #332F2B-ish; no light, no specular
                float dried = saturate(k.dryN * 0.85 + _Dry * 0.6 + k.ghost * 0.35);
                float3 ink = lerp(_InkWet.rgb, _InkDry.rgb, dried);
                // wet head: dark rim, a little paler inside
                float interior = smoothstep(0.45, 1.0, k.across) * k.wet;
                ink = lerp(ink, _InkDry.rgb, interior * 0.30);
                ink *= 0.90 + 0.20 * k.S;
                ink = min(ink, IK_INK_CEILING);
                // accent = light only: the hair-thin core line, a faint tinted spill beside it, the rim of the afterimage / ground stain
                float3 rgb = ink + _Accent.rgb * (k.halo * k.coreAmt * 0.16);
                rgb = lerp(rgb, _Accent.rgb, saturate(k.core * k.coreAmt * 1.4));
                float edge = 1.0 - smoothstep(0.0, 0.35, k.across);
                rgb += _Accent.rgb * (k.coreIn * edge * (k.ghost * 0.30 + k.ground * 0.25));
                return half4(rgb, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target { InkInfo k; InkCut(i, k); return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target { InkInfo k; InkCut(i, k); return half4(-GetViewForwardDir(), 0); }
            ENDHLSL
        }
    }
}
