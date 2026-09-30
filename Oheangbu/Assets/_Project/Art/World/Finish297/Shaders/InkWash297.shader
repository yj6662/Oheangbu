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
        _DebugView("Debug (0 off / 1 edges / 2 wash / 3 tone / 4 light marker / 5 land: slope, detail / 6 near-hill band, depth)", Float) = 0
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
            CBUFFER_START(UnityPerMaterial)
                half _WashRadius, _WashAmount, _WashSigma, _InkTone, _Tint, _TintFar, _ToneGamma, _InkLow, _InkHigh, _Pool, _LineLo, _LineHi;
                half _ShoulderValue, _ShoulderSlope, _PaperPoint, _LandCeiling, _LandDetail, _SlopeInk, _SlopeWashKeep;
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
                bool sky = d >= _ProjectionParams.z - 1.0;
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
                col = lerp(col, toned, sky ? _InkTone * .5 : _InkTone);
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

                // 6. hanji fibre (screen space, stretched fibres + fine tooth), also over the sky
                float2 fp = input.positionCS.xy / (scale * _GrainScale);
                float fibre = Noise(fp * float2(.06, .9)) * .55 + Noise(fp * float2(.9, .07) + 17) * .25 + Hash21(floor(fp * .5)) * .2;
                col *= 1 + (fibre - .5) * 2 * _Grain;

                if (_DebugView > .5)
                {
                    if (_DebugView < 1.5) return half4(edge.xxx, 1);
                    if (_DebugView < 2.5) return half4(washed, 1);
                    if (_DebugView < 3.5) return half4(toned, 1);
                    if (_DebugView < 4.5) return half4(markN, emissive, 0, 1);
                    if (_DebugView > 5.5) return half4(NearHillBand(d), saturate(d / 2000), sky ? 1 : 0, 1);
                    return half4(slope, saturate(detail * 4 + .5), t, 1);
                }
                return half4(col, source.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
