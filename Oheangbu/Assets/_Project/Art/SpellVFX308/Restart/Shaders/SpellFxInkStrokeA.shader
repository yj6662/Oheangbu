// D308-161 / PLAN-308-INK-STROKE-SPELLS-20261031 s1 + s4: the attack-stroke shader, variant A (derived from the approved RiposteInk308 ink language).
// A "sharp ink brush stroke": matte sumi ink, dry-brush bristle streaks along the stroke, wet head -> dry tail, written in brush order,
// drying by cracking into flakes, plus ONE accent colour used only as light (a 1-2 px core line, drawn ON the ink - no additive, no blend).
// OPAQUE CUTOUT, Cull Off, ZWrite On, layer 0 / AlphaTest queue. DepthOnly + DepthNormals share the exact same cut (without them the
// InkWash297 post and the realm fog take the stroke's pixels for sky and paint paper over it). Never put a depth-writing material on VfxAfterFog.
//
// MESH CONTRACT (the parts build the ribbon; this shader only treats the edges)
//   uv0.x = along the stroke, 0 = TAIL (brush side) .. 1 = HEAD (target side)   <- REVERSED vs RiposteInk308 (there 0 = wet start)
//   uv0.y = across the width, 0..1, 0.5 = centre
//   uv1.x = stroke seed 0..1 (every noise is shifted by it)      uv1.y = layer: 0 main stroke, 0.5 afterimage, 1 ground smear mark
//   vertex colour r = core-line strength 0..1 (x _CoreK) | g = wetness (0 dry, 1 wet) | b = spare | a = ink density / dissolve 0..1
// Everything that can be cut is decided in ONE function (IsACut) that all three passes call.
Shader "Oheangbu/SpellFx/InkStrokeA"
{
    Properties
    {
        _InkWet ("Wet ink", Color) = (0.0549, 0.0510, 0.0471, 1)
        _InkDry ("Dry ink (grey-black)", Color) = (0.2, 0.1843, 0.1686, 1)
        [HDR] _Accent ("Accent (the core line's light, HDR)", Color) = (0.4, 1.4, 0.9, 1)
        _Reveal ("Written up to (uv0.x <= this is visible; the head edge is bristle-ragged)", Range(0, 1)) = 1
        _TailCut ("Tail drawn back to (uv0.x < this is gone)", Range(0, 1)) = 0
        _Dry ("Drying: cracks open, flakes fall (0 wet .. 1 gone)", Range(0, 1)) = 0
        _CoreK ("Core line strength multiplier", Range(0, 1)) = 1
        _BristleScale ("Bristle streak density along the stroke (repeats per stroke length)", Float) = 8
        [NoScaleOffset] _BristleTex ("Bristle coverage mask (R); white = procedural only", 2D) = "white" {}
        _AfterAlpha ("Afterimage layer strength (uv1.y = 0.5)", Range(0, 1)) = 0.55
        _Cutoff ("Dry-brush coverage cutoff", Range(0, 1)) = 0.12
        _CoreW ("Core line half width in screen px", Range(0.3, 3)) = 1
        _HeadRag ("Ragged head / tail edge (fraction of length)", Range(0, 0.3)) = 0.06
        _CrackTiling ("Crack cells (along, across)", Vector) = (10, 2.2, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On
        ZTest LEqual
        // a hair toward the camera: the ground smear layer (uv1.y = 1) lies on the terrain and the cutout writes depth (all three passes carry the same offset)
        Offset -1, -1

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
        struct Varyings { float4 positionCS : SV_POSITION; float4 color : TEXCOORD2; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };

        TEXTURE2D(_BristleTex); SAMPLER(sampler_BristleTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _InkWet, _InkDry, _Accent, _CrackTiling;
            float _Reveal, _TailCut, _Dry, _CoreK, _BristleScale, _AfterAlpha, _Cutoff, _CoreW, _HeadRag;
        CBUFFER_END

        // own helpers (not InkCommon308: its relative include does not resolve from this folder; names are prefixed so a later include cannot clash)
        float IsAHash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }
        float IsANoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(IsAHash(i), IsAHash(i + float2(1, 0)), f.x), lerp(IsAHash(i + float2(0, 1)), IsAHash(i + float2(1, 1)), f.x), f.y);
        }
        // interleaved gradient noise on the pixel grid (stable on screen, 0..1)
        float IsADither(float2 px)
        {
            return frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715))));
        }
        // cellular noise: x = distance to the nearest feature point, y = (second - first) = 0 on a cell border, growing into the cell; cell = hash of the nearest cell
        float2 IsAWorley(float2 p, out float cell)
        {
            float2 ip = floor(p), fp = frac(p);
            float f1 = 8.0, f2 = 8.0;
            cell = 0.0;
            [unroll] for (int j = -1; j <= 1; j++)
            {
                [unroll] for (int k = -1; k <= 1; k++)
                {
                    float2 g = float2(k, j);
                    float2 o = 0.1 + 0.8 * float2(IsAHash(ip + g), IsAHash(ip + g + 17.3));
                    float d = length(g + o - fp);
                    if (d < f1) { f2 = f1; f1 = d; cell = IsAHash(ip + g + 53.1); }
                    else if (d < f2) { f2 = d; }
                }
            }
            return float2(f1, f2 - f1);
        }
        // bristle streaks: noise that is HIGH frequency across the width (strands) and SLOW along the stroke (long hairs). sid = the strand's own constant (0..1).
        float IsAStrand(float u, float v, float seed, out float sid)
        {
            float sv = v * 26.0 + seed * 57.0;
            float si = floor(sv);
            sid = IsAHash(float2(si, seed * 13.1 + 0.7));
            float along = IsANoise(float2(si * 5.37 + seed * 19.0, u * _BristleScale * 1.4));
            float s = lerp(sid, along, 0.6);
            // a finer second set of hairs, so a strand is not one flat band
            float fi = floor(v * 71.3 + seed * 31.0);
            float fine = IsANoise(float2(fi * 3.91 + seed * 7.0, u * _BristleScale * 2.6));
            s = s * 0.65 + fine * 0.35;
            return saturate((s - 0.5) * 1.7 + 0.5);
        }

        #ifdef UNITY_COLORSPACE_GAMMA
            #define IS_A_INK_CEIL 0.85
        #else
            #define IS_A_INK_CEIL 0.692
        #endif

        struct CutInfo { float strand; float dryAmt; float wetSolid; float across; float core; float halo; float coreI; };

        Varyings vert(Attributes v)
        {
            Varyings o;
            float3 ws = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(ws);
            o.color = v.color; o.uv0 = v.uv0; o.uv1 = v.uv1;
            return o;
        }

        // The one decision of what is ink and what is the paper showing through. Clips; returns what the colour needs.
        CutInfo IsACut(Varyings i)
        {
            float u = i.uv0.x, v = i.uv0.y, seed = i.uv1.x, layer = i.uv1.y;
            float wet = saturate(i.color.g), alpha = saturate(i.color.a);
            float isAfter = step(0.25, layer) * step(layer, 0.75);
            float isGround = step(0.75, layer);
            float pxv = fwidth(v);                       // before any discard: the width of one screen pixel in uv0.y units

            float sid;
            float strand = IsAStrand(u, v, seed, sid);
            strand *= SAMPLE_TEXTURE2D(_BristleTex, sampler_BristleTex, float2(u * _BristleScale * 0.5 + seed, v)).r;

            // written in brush order: head side beyond _Reveal is cut, the edge steps strand by strand (a comb, never a soft fade)
            // (the range is widened by the ragged amount so that _Reveal = 1 shows the whole stroke and 0 shows nothing)
            float rag = _HeadRag;
            clip(lerp(-rag, 1.0 + rag, _Reveal) + (sid - 0.5) * rag - u);
            // the tail is drawn back (dries / withdraws first): raggedier than the head
            float tailRag = 0.16;
            clip(u - (lerp(-tailRag, 1.0 + tailRag, _TailCut) + (sid - 0.5) * tailRag));
            clip(alpha - 0.02);

            // dryness: vertex g is the main driver (head wet, tail dry); a starved density (a < 1) also dries; ground smears and afterimages are always dry-brush
            float dryAmt = saturate((1.0 - wet) + (1.0 - alpha) * 0.7);
            dryAmt = max(dryAmt, isGround * 0.55);
            dryAmt = max(dryAmt, isAfter * 0.5);
            float wetSolid = (1.0 - dryAmt) * (1.0 - dryAmt);

            // coverage: strands where it is dry (the rim goes first, the centre spine last), solid where it is wet
            float across = abs(v * 2.0 - 1.0);          // 0 = centre, 1 = the ribbon's edge
            float edgeShape = lerp(0.25, 1.0, pow(across, 0.8));
            float cov = lerp(strand, 1.0, wetSolid) - dryAmt * edgeShape - (1.0 - alpha) * (1.0 - alpha) * 0.6;
            // the wet head's rim is a slightly uneven bleed, not a ruler edge
            clip(1.0 - across - (IsANoise(float2(u * 40.0 + seed * 9.0, v * 3.0)) - 0.5) * 0.16 * wetSolid + 0.002);

            // core line: a constant 1-2 px line along the centre (the ground mark gets two edge lines instead, so it still reads on red soil)
            // 2026-10-31 fix: capped at 6 % of the width - on a ribbon only 6 - 10 px wide (a coil, a cage branch, a far or end-on stroke) "1 px" was 20 - 50 % of it, the x4 halo covered the rest
            // and the HDR accent turned the whole stroke white
            float coreHalf = clamp(pxv * _CoreW, 0.004, 0.06);
            float wob = (IsANoise(float2(u * 9.0 + seed * 5.0, 1.3)) - 0.5) * 0.02;
            float dCentre = abs(v - 0.5 + wob);
            float dEdge = abs(across - 0.86) * 0.5;
            float dv = lerp(dCentre, dEdge, isGround);
            float core = 1.0 - smoothstep(coreHalf * 0.5, coreHalf * 1.4, dv);
            float halo = 1.0 - smoothstep(0.0, coreHalf * 4.0, dv);
            // the thread of light thins out into the two pointed ends instead of floating past the ink
            float coreI = saturate(i.color.r * _CoreK) * smoothstep(0.0, 0.16, u) * (1.0 - smoothstep(0.96, 1.0, u));
            // a ribbon narrower than ~14 px on screen carries no light line: it stays ink (gone below ~6 px; TEST)
            coreI *= 1.0 - smoothstep(0.07, 0.16, pxv);
            // afterimage: not a blend - strands drop out against the layer strength (+ a little dither), so it reads as a thinner, ghostly copy of the brush
            float ghost = _AfterAlpha * alpha - (sid * 0.88 + IsADither(i.positionCS.xy) * 0.12);
            clip(lerp(1.0, ghost, isAfter));
            // the core line is not cut by the bristle gaps (a continuous thread of light), but it is by the reveal / tail / cracks
            clip(cov - _Cutoff + core * coreI * 2.0);

            // drying through a crack field: cracks open first, then the flakes fall in a random order; the tail dries before the head (u = 0 is the tail)
            float dryLocal = saturate(_Dry * 1.35 - u * 0.35);
            [branch] if (dryLocal > 0.0005)
            {
                float2 cp = float2(u * _CrackTiling.x, v * _CrackTiling.y) + seed * float2(23.7, 11.3);
                float cellH;
                float2 w = IsAWorley(cp, cellH);
                clip(w.y - dryLocal * 0.55);
                clip(cellH - (dryLocal - 0.35) * 1.9);
            }

            CutInfo c;
            c.strand = strand; c.dryAmt = dryAmt; c.wetSolid = wetSolid; c.across = across;
            c.core = core; c.halo = halo; c.coreI = coreI;
            return c;
        }

        half4 FragForward(Varyings i) : SV_Target
        {
            CutInfo c = IsACut(i);
            // matte ink: wet -> dry grey-black; a wet head's interior is a little paler than its dark rim ("the ink pools at the edge")
            float dried = saturate(c.dryAmt * 0.85 + _Dry * 0.9);
            float3 ink = lerp(_InkWet.rgb, _InkDry.rgb, dried);
            float rim = smoothstep(0.6, 0.97, c.across);
            ink = lerp(ink, _InkDry.rgb, (1.0 - rim) * c.wetSolid * 0.15);
            ink *= 0.9 + 0.2 * c.strand;                   // grain of the hairs, no highlight
            ink = min(ink, IS_A_INK_CEIL);                 // the ink itself stays under the ink ceiling; only the core line may exceed it
            // light drawn ON the ink: a faint in-ink halo, then the 1-2 px core
            float3 col = lerp(ink, _Accent.rgb * 0.5, c.halo * c.coreI * 0.3);
            col = lerp(col, _Accent.rgb, saturate(c.core * c.coreI));
            return half4(col, 1);
        }

        half4 FragDepth(Varyings i) : SV_Target
        {
            IsACut(i);
            return i.positionCS.z;
        }

        half4 FragNormals(Varyings i) : SV_Target
        {
            IsACut(i);
            float3 n = -GetViewForwardDir();               // a flat ribbon read as facing the camera (as RiposteInk308)
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
                return half4(PackFloat2To888(oct), 0.0);
            #else
                return half4(n, 0.0);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "InkStrokeForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment FragForward
            ENDHLSL
        }
        // without these the depth / normals prepass never sees the stroke and the ink wash post paints paper over it
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment FragDepth
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment FragNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
}
