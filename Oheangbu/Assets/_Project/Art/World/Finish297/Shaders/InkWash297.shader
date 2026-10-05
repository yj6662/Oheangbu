// #297 candidate-only 수묵담채 screen pass (SPEC-WORLD-FINISH-297 §1b), derived from Oheangbu/InkWorldPost (untouched).
// FullScreenPassRendererFeature on Finish297/Renderer297 at 450 BeforeRenderingTransparents (depth/normals/SSAO valid; spell
// strokes stay outside). Same gate and palette as the lookdev pass: _OhWorldPost < 0.5 returns the source (driverless scenes
// unchanged); ink = _OhInkColor, paper = _OhPaperColor (WorldLookDriver globals, no colour stored here).
// Order: edge-preserving wash (flattens photographed texture into brushed washes) -> ink-tone mapping (luminance onto the
// ink..paper ramp, only a little of the hue kept: 담채) -> land readability (ceiling, re-injected local detail, ink on steep
// faces) -> pigment pooling at wash borders -> ink lines (depth + normal Sobel, broken by a low-frequency brush noise, thinning
// with distance) -> distance paper wash (capped; light sources exempt) -> 한지 fibre.
// v2 (attraction pass, TEST): every new property defaults to "no effect" (a material at shader defaults renders as v1); the
// attraction command writes the TEST values into M_InkWash297 and attraction-revert restores the recorded ones.
//  * light sources: normals alpha < -0.5 (InkLightSource / CodexInkLantern / InkBeacon297 write -1), point-loaded (no half-
//    marked fringes). With _LightExempt they return their HDR colour * _LightGain untouched (Bloom catches them). An HDR,
//    chromatic scene colour (_EmissiveMark) is an additional soft marker (restored after pooling, paper-wash exempt).
//  * white-out: _LandCeiling caps the ramp below bare paper, _LandDetail re-injects the local detail the wash removed,
//    _SlopeInk / _SlopeWashKeep keep steep faces inked, _PaperWashMax caps the distance wash; optional soft highlight
//    shoulder (_ShoulderValue/_ShoulderSlope/_PaperPoint) and a far tint (_TintFar, -1 = same as _Tint).
// Recognition untouched: presentation only (spell strokes are transparent and drawn after this pass).
// v3 (#308, TEST; every addition renders v2 at its default):
//  * _SkyInkTone (SPEC-REGION-SKY-308): sky pixels move onto the ink..paper ramp by _InkTone * _SkyInkTone (.5 = v2).
//  * 권역 담채 (SPEC-EVENT-WASH-308 §2): world-space zones of available campaign stages, written as globals by
//    EventWashDriver308 (_OhEventWashCount 0 = skipped by a branch). Not a light: ink recedes toward the paper (역번짐, more
//    in the darks) and a faint meaning colour is laid in at the same perceptual value; nothing rises above max(before, paper)
//    (LDR, below the Bloom threshold). Sky, light-source markers and emissive pixels are skipped; the hanji fibre lies on top.
//  * debug: global _OhInkWashDebug308 > 0 overrides _DebugView (no shared material write); 7 = wash weight (R), overlapping
//    zones / 8 (G), clipped excess x 10 (B). 6 and 7 are separate branches.
// v4 (D308-6b, SPEC-EVENT-WASH-308 §2b, TEST; _OhEventWashVolA.x = 0 runs the v3 math only, _OhEventWashCount 0 the v2 math only):
//  * slots [0, split) of the 권역 담채 arrays are progress VOLUMES, slots [split, count) keep the v3 surface term (rest, story).
//    split = _OhEventWashVolA.x, clamped to the count. Volume slots carry Band = 0, so RealmFog297's far-air term ignores them.
//  * a volume is a wide, flat ellipsoid in the air over the place (centre (Zone.x, Volume.x, Zone.y), half axes (Zone.z,
//    Volume.y, Zone.z)), density rho0 (1 - |p|^2), zero with zero slope at its skin. Optical depth along the pixel ray is
//    the closed form of that density between NearClip and min(surface distance, sky = unbounded): no marching, no samples.
//    A noise attached to the volume (closest point of the ray, unit space) bends the skin and mottles the density (번짐).
//  * ground, figures and props stay clear: the volume floor is baked above the place (editor), the first NearClip metres
//    are never integrated, near surfaces keep only SurfKeep, and an eye standing at the place keeps only NearKeep.
//  * applied as a pigment glaze in perceptual space (multiply: never brighter) with the value restored by ValueKeep (a
//    tinted haze, not smoke); output <= max(before, paper) like v3; light-source markers and emissive pixels are skipped,
//    sky pixels are included (the colour reads above the ridges), the hanji fibre lies on top.
//  * debug 8 = (alpha / cap) R, optical depth / 2 G, clipped excess x 10 B. Checked before 7.
Shader "Oheangbu/Finish297/InkWash"
{
    Properties
    {
        [Header(Wash)]
        _WashRadius("Wash radius (px at 1080p)", Range(0, 8)) = 3.5
        _WashAmount("Wash amount (texture flattening)", Range(0, 1)) = .65
        _WashSigma("Wash edge keep (luminance sigma)", Range(.02, .5)) = .12
        [Header(Ink tone)]
        _InkTone("Ink tone mapping", Range(0, 1)) = .7
        _SkyInkTone("Sky ink tone (x _InkTone; .5 = v2)", Range(0, 1)) = .5
        _Tint("Colour kept (담채)", Range(0, 1)) = .45
        _TintFar("Colour kept at the paper-wash distance (-1 = same as _Tint)", Range(-1, 1)) = -1
        _ToneGamma("Tone gamma (<1 = more paper)", Range(.3, 2)) = .85
        _InkLow("Ink tone: perceptual value that is full ink", Range(0, .5)) = .08
        _InkHigh("Ink tone: perceptual value that is bare paper (shoulder start with _ShoulderValue < 1)", Range(.5, 1)) = .8
        _Pool("Pigment pooling at wash borders", Range(0, 1)) = .25
        [Header(Highlight shoulder)]
        _ShoulderValue("Ramp value at _InkHigh (1 = bare paper, v1)", Range(.5, 1)) = 1
        _ShoulderSlope("Ramp end slope below _InkHigh (0 = v1 smoothstep)", Range(0, 3)) = 0
        _PaperPoint("Perceptual value that becomes bare paper", Range(.5, 1.2)) = .96
        [Header(Land readability)]
        _LandCeiling("Ramp ceiling on land (1 = off)", Range(.5, 1)) = 1
        _LandDetail("Local detail re-injected (original - washed)", Range(0, 1)) = 0
        _SlopeInk("Ink on steep faces", Range(0, 1)) = 0
        _SlopeWashKeep("Steep faces kept from the paper wash", Range(0, 1)) = 0
        [Header(Ink line)]
        _DepthEdgeStrength("Depth edge", Range(0, 4)) = 1.6
        _DepthEdgeBias("Depth edge bias", Range(.001, .2)) = .02
        // #307 black specks (user 2026-10-01): the first-order depth difference grows on any receding plane, so distant ground and
        // slopes crossed the line threshold and the brush noise broke them into blocky dark bands. 1 = second difference of 1/depth
        // (exactly zero on a plane, the same response across a silhouette); 0 = v1.
        [ToggleUI] _DepthEdgeMode307("Depth edge: plane-invariant (1/z second difference)", Float) = 0
        _DepthEdgeFloor307("Depth edge: relative floor (precision)", Range(0, .05)) = .004
        _NormalEdgeStrength("Normal edge", Range(0, 4)) = .55
        _LineLo("Line threshold low", Range(0, 1)) = .25
        _LineHi("Line threshold high", Range(0, 1)) = .7
        _NormalEdgeFadeDistance("Normal edge fade (m)", Float) = 160
        _GroundNormalEdge("Normal edge kept on upward ground (1 = v1; #306)", Range(0, 1)) = 1
        _GroundNormalUp("Upward ground ramp on normal.y (start, full)", Vector) = (.7, .9, 0, 0)
        _LineFar("Line fade start/end (m)", Vector) = (80, 900, 0, 0)
        _LineBreak("Brush break-up", Range(0, 1)) = .35
        _LineInk("Line darkness", Range(0, 1)) = .85
        [Header(Paper)]
        _PaperFadeStart("Paper wash start (m)", Float) = 180
        _PaperFadeEnd("Paper wash end (m)", Float) = 1600
        _WashStrength("Paper wash strength", Range(0, 1)) = .55
        _WashKeep("Ink kept in the wash", Range(0, 1)) = .6
        _PaperWashMax("Paper wash cap (1 = off)", Range(0, 1)) = 1
        [Header(Near hills 302)]
        _NearHillInk("Near mountains toward ink (농묵 near, 담묵 far)", Range(0, 1)) = 0
        _NearHill("Near hill band (m): in start, in full, out start, out end", Vector) = (70, 220, 700, 1600)
        _Grain("Hanji fibre", Range(0, .2)) = .045
        _GrainScale("Hanji fibre scale", Range(.25, 4)) = 1
        _HighlightPaper("Highlights toward paper", Range(0, 1)) = .25
        [Header(Light sources)]
        [ToggleUI] _LightExempt("Marked light sources keep their HDR colour (normals alpha -1)", Float) = 0
        _LightGain("Light source gain", Range(0, 4)) = 1
        _EmissiveMark("HDR emissive detection amount (0 = off)", Range(0, 1)) = 0
        _EmissiveLo("Emissive detection: HDR peak where exemption starts", Range(.5, 4)) = 1.1
        _EmissiveHi("Emissive detection: HDR peak of full exemption", Range(.5, 8)) = 2.0
        _EmissiveChroma("Emissive detection: minimum chroma (0 = any colour)", Range(0, 1)) = .2
        [Header(Debug)]
        _DebugView("Debug (0 off / 1 edges / 2 wash / 3 tone / 4 light marker / 5 land: slope, detail / 6 near-hill band, depth / 7 region wash / 8 progress volume)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "InkWash297"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D_X(_CameraNormalsTexture); SAMPLER(sampler_CameraNormalsTexture);
            float4 _OhInkColor; float4 _OhPaperColor; float _OhWorldPost;
            // #308 권역 담채 globals (EventWashDriver308; arrays always 8 long) and the debug override (capture tools)
            float _OhEventWashCount; float4 _OhEventWashZone[8]; float4 _OhEventWashColour[8]; float4 _OhEventWashBand[8];
            float4 _OhEventWashParams; float _OhInkWashDebug308;
            // D308-6b progress volumes (slots [0, split)): Volume = (centre y, half height, rho0 /m, noise seed);
            // VolA = (split, cap, near clip m, near keep); VolB = (near ramp m, surface keep, surface ramp start m, end m);
            // VolC = (edge noise, mottle, noise scale, value keep). Unset globals read 0 = split 0 = no volume.
            float4 _OhEventWashVolume[8]; float4 _OhEventWashVolA; float4 _OhEventWashVolB; float4 _OhEventWashVolC;
            CBUFFER_START(UnityPerMaterial)
                half _WashRadius, _WashAmount, _WashSigma, _InkTone, _SkyInkTone, _Tint, _TintFar, _ToneGamma, _InkLow, _InkHigh, _Pool, _LineLo, _LineHi;
                half _ShoulderValue, _ShoulderSlope, _PaperPoint, _LandCeiling, _LandDetail, _SlopeInk, _SlopeWashKeep;
                half _DepthEdgeMode307, _DepthEdgeFloor307;
                half _DepthEdgeStrength, _DepthEdgeBias, _NormalEdgeStrength, _NormalEdgeFadeDistance, _GroundNormalEdge; float4 _GroundNormalUp; float4 _LineFar; half _LineBreak, _LineInk;
                half _PaperFadeStart, _PaperFadeEnd, _WashStrength, _WashKeep, _PaperWashMax, _Grain, _GrainScale, _HighlightPaper;
                half _NearHillInk; float4 _NearHill;
                half _LightExempt, _LightGain, _EmissiveMark, _EmissiveLo, _EmissiveHi, _EmissiveChroma, _DebugView;
            CBUFFER_END

            float Hash21(float2 p) { p = frac(p * float2(123.34, 345.45)); p += dot(p, p + 34.345); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), u.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + 1), u.x), u.y);
            }
            float Lin(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }
            half3 Src(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb; }
            // smoothstep that tolerates a == b (a zero-width threshold is a hard step, never NaN)
            half Ramp01(half x, half a, half b) { half t = saturate((x - a) / max(b - a, 1e-4)); return t * t * (3 - 2 * t); }
            // the same in float, for metre-scale distances (km range: half would quantise them)
            float RampM(float x, float a, float b) { float t = saturate((x - a) / max(b - a, 1e-3)); return t * t * (3 - 2 * t); }

            half NearHillBand(float d) { return smoothstep(_NearHill.x, _NearHill.y, d) * (1 - smoothstep(_NearHill.z, _NearHill.w, d)); }
            // Perceptual value -> ramp position (0 ink .. 1 paper). Below _InkHigh a Hermite toe (flat at full ink so the darks
            // hold) rising to _ShoulderValue; above it a C1 soft shoulder that reaches bare paper only at _PaperPoint.
            // _ShoulderValue 1 + _ShoulderSlope 0 is exactly v1 (smoothstep to paper at _InkHigh).
            half ToneRamp(half tp)
            {
                half span = max(_InkHigh - _InkLow, 1e-3);
                half x = (tp - _InkLow) / span;
                half H = _ShoulderValue, s1 = min(_ShoulderSlope, 3.0);
                half t;
                if (x <= 1)
                {
                    half xs = saturate(x);
                    t = H * ((3 - s1) * xs * xs + (s1 - 2) * xs * xs * xs);
                }
                else
                {
                    half pspan = _PaperPoint - _InkHigh;
                    if (pspan <= 1e-3) t = 1;
                    else
                    {
                        half u = saturate((tp - _InkHigh) / pspan);
                        half M = clamp(H * s1 * pspan / max((1 - H) * span, 1e-4), 0, 3);
                        t = H + (1 - H) * (M * u + (3 - 2 * M) * u * u + (M - 2) * u * u * u);
                    }
                }
                return pow(saturate(t), _ToneGamma);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_OhWorldPost < 0.5) return source;
                float2 texel = _BlitTexture_TexelSize.xy; float scale = _BlitTexture_TexelSize.w / 1080.0;
                float rawD = SampleSceneDepth(uv); float d = LinearEyeDepth(rawD, _ZBufferParams);
                // #308 sky fix: raw depth test like CompactMountainMist. The old "d >= far - 1" failed because the eye depth reconstructed from
                // _ZBufferParams drifts by more than 1 m at a 22 km far plane, so every sky pixel was washed as distant ground (paper).
                #if UNITY_REVERSED_Z
                bool sky = rawD < .000001;
                #else
                bool sky = rawD > .999999;
                #endif
                // light-source marker, point-loaded (a filtered sample half-marks the fringe texels)
                float4 nCw = LOAD_TEXTURE2D_X(_CameraNormalsTexture, int2(uv * _BlitTexture_TexelSize.zw));
                half markN = step(nCw.w, -.5);
                if (_LightExempt > .5 && markN > .5) return half4(source.rgb * _LightGain, source.a);
                half3 ink = _OhInkColor.rgb, paper = _OhPaperColor.rgb;
                float fade = saturate((d - _PaperFadeStart) / max(_PaperFadeEnd - _PaperFadeStart, 1));
                float3 nC = nCw.xyz;
                float hasN = step(.25, dot(nC, nC));
                half slope = hasN * smoothstep(.2, .75, 1 - abs(nC.y));
                // HDR, chromatic scene colour = emissive (soft marker; neutral sunlit paper/plaster never qualifies)
                half peak = max(source.r, max(source.g, source.b)), low = min(source.r, min(source.g, source.b));
                half emissive = sky ? 0 : _EmissiveMark * Ramp01(peak, _EmissiveLo, _EmissiveHi) * Ramp01((peak - low) / max(peak, 1e-3), _EmissiveChroma * .5, _EmissiveChroma);

                // 1. edge-preserving wash: 12 taps on two rings, weighted by luminance similarity (no bleed across forms)
                half3 c = source.rgb; half lc = Luminance(c);
                half3 acc = c; half wsum = 1;
                float r = _WashRadius * scale;
                [unroll] for (int k = 0; k < 12; k++)
                {
                    float a = k * 0.5235988 + (k & 1) * 0.26; float rr = r * ((k & 1) ? 1.0 : 0.55);
                    half3 s = Src(uv + float2(cos(a), sin(a)) * rr * texel);
                    half dl = Luminance(s) - lc; half w = exp(-dl * dl / (2 * _WashSigma * _WashSigma));
                    acc += s * w; wsum += w;
                }
                half3 washed = acc / wsum;
                half3 col = lerp(c, washed, sky ? 0 : _WashAmount);

                // 2. ink tone: luminance onto ink..paper, keep a part of the hue (light colour over ink = 담채)
                // perceptual value (linear buffer -> ~sRGB), then a paper-favouring curve: most of the frame is 여백, ink holds the darks
                half L = saturate(Luminance(col)); half tp = pow(L, 1 / 2.2);
                half t = ToneRamp(tp);
                // land readability: bright ground stops below bare paper, the texture/relief the wash removed comes back (added
                // after the ceiling so bright ground keeps its detail), steep faces hold more ink
                half detail = pow(saturate(lc), .4545) - pow(saturate(Luminance(washed)), .4545);
                if (!sky)
                {
                    t = min(t, _LandCeiling);
                    t = saturate(t + _LandDetail * 3 * detail);
                    t *= 1 - _SlopeInk * slope;
                }
                // interpolate in perceptual space so a dark cave stays ink (a linear-space blend lifts the darks to grey)
                half3 ramp = pow(lerp(pow(ink, 1 / 2.2), pow(paper, 1 / 2.2), t), 2.2);
                half3 chroma = col / max(L, 1e-3) * Luminance(ramp);
                half3 toned = lerp(ramp, chroma, lerp(_Tint, _TintFar < 0 ? _Tint : _TintFar, fade));
                col = lerp(col, toned, sky ? _InkTone * _SkyInkTone : _InkTone);
                col = lerp(col, paper, _HighlightPaper * smoothstep(.6, 1, L));

                // 3. pigment pooling: washes darken slightly where the washed colour changes (dried border of a stroke)
                half3 cx = Src(uv + float2(texel.x * 1.5 * scale, 0)), cy = Src(uv + float2(0, texel.y * 1.5 * scale));
                half border = saturate((abs(Luminance(cx) - lc) + abs(Luminance(cy) - lc)) * 3);
                col = lerp(col, col * lerp(1, .78, _Pool), sky ? 0 : border);
                // emissive surfaces keep their HDR colour (not saturated: Bloom catches them after this pass)
                col = lerp(col, source.rgb * _LightGain, emissive);

                // 4. ink lines: depth + normal Sobel, thinned with distance, broken by brush noise
                float dL = Lin(uv - float2(texel.x, 0)), dR = Lin(uv + float2(texel.x, 0)), dD = Lin(uv - float2(0, texel.y)), dU = Lin(uv + float2(0, texel.y));
                float depthEdge = saturate((abs(dL - dR) + abs(dD - dU)) / max(d * .08, _DepthEdgeBias)) * _DepthEdgeStrength;
                if (_DepthEdgeMode307 > .5)
                {
                    // 1/z is affine in screen space on any plane: its second difference is zero on flat ground and slopes and equals
                    // the relative depth step across a silhouette (both sides of the step, as v1). Normalised like v1 (/.08).
                    float iC = 1 / max(d, 1e-4), iL = 1 / max(dL, 1e-4), iR = 1 / max(dR, 1e-4), iD = 1 / max(dD, 1e-4), iU = 1 / max(dU, 1e-4);
                    float relative = (abs(iL + iR - 2 * iC) + abs(iD + iU - 2 * iC)) / iC;
                    depthEdge = saturate(max(0, relative - _DepthEdgeFloor307) / .08) * _DepthEdgeStrength;
                }
                float3 nL = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, uv - float2(texel.x, 0)).xyz;
                float3 nR = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, uv + float2(texel.x, 0)).xyz;
                float3 nD = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, uv - float2(0, texel.y)).xyz;
                float3 nU = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, uv + float2(0, texel.y)).xyz;
                float normalEdge = saturate(length(nL - nR) + length(nD - nU)) * _NormalEdgeStrength * hasN * (1 - saturate(d / max(_NormalEdgeFadeDistance, 1)));
                // #306: upward ground (terrain, grass cover, litter) keeps only _GroundNormalEdge of its normal lines — its dense
                // small-scale normals drew a web of black hairlines around the camera (user playtest 2026-09-30); vertical faces
                // (walls, trunks, figures) keep full lines, depth edges are unchanged
                normalEdge *= lerp(1, _GroundNormalEdge, hasN * smoothstep(_GroundNormalUp.x, _GroundNormalUp.y, nC.y));
                float3 world = ComputeWorldSpacePosition(uv, rawD, UNITY_MATRIX_I_VP);
                float brush = Noise(world.xz * .35 + world.y * .2) * .6 + Noise(input.positionCS.xy / (9 * scale)) * .4;
                float edge = smoothstep(_LineLo, _LineHi, saturate(depthEdge + normalEdge)) * lerp(1, smoothstep(.15, .75, brush), _LineBreak);
                edge *= 1 - smoothstep(_LineFar.x, _LineFar.y, d);
                edge = sky ? 0 : edge;
                col = lerp(col, ink, edge * _LineInk);

                // 5. distance paper wash (ink holds out longer), capped, less on steep faces; light sources exempt
                float pw = sky ? 0 : fade * _WashStrength * (1 - _WashKeep * (1 - saturate(Luminance(col)))) * (1 - .6 * _NearHillInk * NearHillBand(d));
                pw = min(pw, _PaperWashMax) * (1 - _SlopeWashKeep * slope) * (1 - max(markN, emissive));
                col = lerp(col, paper, pw);
                // #302 near mountains read as a dark wash (농묵 near, 담묵 far): a distance band, strongest on mountain faces.
                // Applied last (after the highlight paper and the paper wash, which would lift it back); light sources exempt.
                // perceptual factor (a linear multiply barely reads); the fog pass thins its air over the same band
                // strongest where the land rises above the eye (a mountain, not the valley floor) or stands steep
                float3 hillWorld = ComputeWorldSpacePosition(uv, rawD, UNITY_MATRIX_I_VP);
                half rise = smoothstep(-15, 45, hillWorld.y - _WorldSpaceCameraPos.y);
                if (!sky) col *= pow(saturate(1 - _NearHillInk * .68 * NearHillBand(d) * lerp(.3, 1, max(slope, rise)) * (1 - max(markN, emissive))), 2.2);

                // 5b. #308 권역 담채: zone weight w = (1 - smoothstep(r(1 - feather), r, dXZ + (noise - .5) edge r)) * band(y) * strength,
                // sum capped at 1, colour = weight average. Perceptual space: lift toward paper (more where dark), then a hue move at
                // the same value (chroma 0 zones = 서사: lift only). Output <= max(before, paper).
                // D308-6b: only the surface slots [split, count) (rest, story) are read here; split 0 = the v3 loop.
                int ewCount = (int)min(_OhEventWashCount, 8.0);
                int ewSplit = (int)min(max(_OhEventWashVolA.x + .5, 0), (float)max(ewCount, 0));
                half washW = 0, washN = 0, washOver = 0;
                if (_OhEventWashCount > .5 && !sky && ewCount > ewSplit)
                {
                    float ewFeather = saturate(_OhEventWashParams.x);
                    float ewNoise = Noise(world.xz * max(_OhEventWashParams.y, 1e-4)) - .5;
                    float ewSum = 0, ewChroma = 0, ewLift = 0; float3 ewTint = 0;
                    [loop] for (int ez = ewSplit; ez < ewCount; ez++)
                    {
                        float4 ewZone = _OhEventWashZone[ez], ewBand = _OhEventWashBand[ez], ewColour = _OhEventWashColour[ez];
                        float ewR = max(ewZone.z, 1e-3);
                        float ewD = distance(world.xz, ewZone.xy) + ewNoise * ewBand.z * ewR;
                        float ewY = smoothstep(ewBand.x - 4, ewBand.x, world.y) * (1 - smoothstep(ewBand.y, ewBand.y + 4, world.y));
                        float ewW = max(0, (1 - smoothstep(ewR * (1 - ewFeather), ewR, ewD)) * ewY * ewZone.w);
                        ewSum += ewW; washN += step(.001, ewW);
                        ewLift += ewW * ewColour.a;
                        ewChroma += ewW * ewBand.w; ewTint += ewW * ewBand.w * ewColour.rgb;
                    }
                    half exempt = 1 - max(markN, emissive);
                    washW = saturate(ewSum) * exempt;
                    if (washW > 0)
                    {
                        half3 ewBefore = col;
                        half3 pc = pow(max(col, 0), 1 / 2.2), pp = pow(max(paper, 0), 1 / 2.2);
                        half lift = min(ewLift / max(ewSum, 1e-4), 1) * _OhEventWashParams.w * washW;
                        pc = lerp(pc, pp, lift * (1 - saturate(Luminance(pc))));
                        if (ewChroma > 1e-4)
                        {
                            half3 tp3 = pow(max(ewTint / ewChroma, 0), 1 / 2.2);
                            half3 tinted = tp3 / max(Luminance(tp3), 1e-3) * Luminance(pc);
                            pc = lerp(pc, tinted, _OhEventWashParams.z * saturate(ewChroma) * exempt);
                        }
                        half3 ewAfter = pow(max(pc, 0), 2.2);
                        half3 ewCeil = max(ewBefore, paper);
                        washOver = max(0, max(max(ewAfter.r - ewCeil.r, ewAfter.g - ewCeil.g), ewAfter.b - ewCeil.b));
                        col = min(ewAfter, ewCeil);
                    }
                }

                // 5c. D308-6b progress volumes (slots [0, split)): a wide, flat world-axis ellipsoid in the air over each place,
                // density rho0 (1 - |p|^2) (zero with zero slope at its skin). Optical depth along the pixel ray in closed form:
                // with q the ray's closest approach (unit space), K = 1 - |q|^2 and u = t - tm, the density is K - a u^2, so
                // I = K (u1 - u0) - a (u1^3 - u0^3) / 3 over [near clip, min(surface, sky = through)] cut to the roots +-sqrt(K/a).
                // u stays within +-R (no km-cubed terms). Perceptual glaze (multiply, never brighter), value kept by ValueKeep;
                // output <= max(before, paper). Sky pixels included (the colour reads above the ridges); ground and near figures
                // stay clear (floor baked above the place, near clip, near-surface keep, near-eye keep). Perspective only.
                half volAlpha = 0, volTau = 0, volOver = 0;
                if (ewSplit > 0 && unity_OrthoParams.w < .5)
                {
                    float3 evO = _WorldSpaceCameraPos;
                    float3 evRay = world - evO;
                    float3 evV = evRay * rsqrt(max(dot(evRay, evRay), 1e-8));
                    // Euclidean distance to the surface from the eye depth (right on every depth convention; world gives the direction)
                    float evDist = d / max(dot(evV, GetViewForwardDir()), 1e-3);
                    float evMax = sky ? 1e6 : evDist, evNear = max(_OhEventWashVolA.z, 0), evEdge = max(_OhEventWashVolC.x, 0);
                    float evTau = 0; float3 evPig = 0;
                    [loop] for (int vz = 0; vz < ewSplit; vz++)
                    {
                        float4 vZone = _OhEventWashZone[vz], vVol = _OhEventWashVolume[vz];
                        if (vZone.w <= 0 || vVol.z <= 0) continue;   // faded-out / undensified slot: no work
                        float3 vInvS = float3(1 / max(vZone.z, 1), 1 / max(vVol.y, 1), 1 / max(vZone.z, 1));
                        float3 vo = (evO - float3(vZone.x, vVol.x, vZone.y)) * vInvS, vv = evV * vInvS;
                        float va = dot(vv, vv), vtm = -dot(vo, vv) / va;
                        float3 vq = vo + vtm * vv;
                        float vK = 1 - dot(vq, vq);
                        if (vK <= -.5 * evEdge) continue;   // Noise - .5 lies in [-.5, .5]: even the widest skin misses
                        // ink-diffusion skin and mottle, attached to the volume (closest point, unit space), not to the screen
                        float vn = Noise(vq.xz * _OhEventWashVolC.z + vq.y * 1.7 + vVol.w) - .5;
                        vK += evEdge * vn;
                        if (vK <= 0) continue;
                        float vh = sqrt(vK / va);
                        float vu0 = max(-vh, evNear - vtm), vu1 = min(vh, evMax - vtm);
                        if (vu1 <= vu0) continue;
                        // factored u1^3 - u0^3 = (u1 - u0)(u1^2 + u1 u0 + u0^2): no cancellation on short spans, and >= 0 since |u| <= h
                        float vI = (vu1 - vu0) * (vK - va * (vu1 * vu1 + vu1 * vu0 + vu0 * vu0) / 3);
                        float vNearK = lerp(_OhEventWashVolA.w, 1, RampM(distance(evO.xz, vZone.xy), vZone.z, vZone.z + _OhEventWashVolB.x));
                        float vTi = max(0, vI) * vVol.z * vZone.w * vNearK * max(0, 1 + _OhEventWashVolC.y * 2 * vn);
                        evTau += vTi; evPig += vTi * _OhEventWashColour[vz].rgb;
                    }
                    float evSurf = sky ? 1 : lerp(_OhEventWashVolB.y, 1, RampM(evDist, _OhEventWashVolB.z, _OhEventWashVolB.w));
                    volTau = evTau * evSurf * (1 - max(markN, emissive));
                    if (volTau > 1e-4)
                    {
                        half3 vBefore = col;
                        half3 vPig = evPig / max(evTau, 1e-6);
                        volAlpha = saturate(_OhEventWashVolA.y) * (1 - exp(-volTau));
                        half3 vpc = pow(max(col, 0), 1 / 2.2);
                        half3 vg = vpc * lerp(half3(1, 1, 1), vPig, volAlpha);
                        vg *= lerp(1, Luminance(vpc) / max(Luminance(vg), 1e-4), saturate(_OhEventWashVolC.w));
                        half3 vAfter = pow(max(vg, 0), 2.2), vCeil = max(vBefore, paper);
                        volOver = max(0, max(max(vAfter.r - vCeil.r, vAfter.g - vCeil.g), vAfter.b - vCeil.b));
                        col = min(vAfter, vCeil);
                    }
                }

                // 6. hanji fibre (screen space, stretched fibres + fine tooth), also over the sky
                float2 fp = input.positionCS.xy / (scale * _GrainScale);
                float fibre = Noise(fp * float2(.06, .9)) * .55 + Noise(fp * float2(.9, .07) + 17) * .25 + Hash21(floor(fp * .5)) * .2;
                col *= 1 + (fibre - .5) * 2 * _Grain;

                half debugView = _OhInkWashDebug308 > 0 ? (half)_OhInkWashDebug308 : _DebugView;
                if (debugView > .5)
                {
                    if (debugView < 1.5) return half4(edge.xxx, 1);
                    if (debugView < 2.5) return half4(washed, 1);
                    if (debugView < 3.5) return half4(toned, 1);
                    if (debugView < 4.5) return half4(markN, emissive, 0, 1);
                    if (debugView > 7.5) return half4(volAlpha / max(saturate(_OhEventWashVolA.y), 1e-4), saturate(volTau / 2), saturate(volOver * 10), 1);
                    if (debugView > 6.5) return half4(washW, washN / 8, saturate(washOver * 10), 1);
                    if (debugView > 5.5) return half4(NearHillBand(d), saturate(d / 2000), sky ? 1 : 0, 1);
                    return half4(slope, saturate(detail * 4 + .5), t, 1);
                }
                return half4(col, source.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
