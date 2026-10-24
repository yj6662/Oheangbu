// S5-b slice 9a round 2 (D308-150 V5): the ink of the vol.2 spells (plan v10 sections B, C, E). ONE shader for every vol.2 stroke; the recipe (Vol2VfxRecipes) lays the strokes, the body look
// (Vol2BodyLook) sets the colours and the cue, nothing else decides the picture. OPAQUE CUTOUT, double sided, depth tested, writes depth (DepthOnly / DepthNormals share the cut):
// no emission, no additive, no alpha blend, no light, no glow, no bloom contribution; every pixel is a flat ink colour or it is cut away, clamped to the ink LDR ceiling.
// The stroke is the riposte one-stroke technique (RiposteInk308): ONE ribbon, dry-brush: the bristle mask (RipStroke308_Bristle, R = ink coverage, long uneven streaks along the stroke,
// tiles along it) decides where the ribbon is ink and where it is a gap. Hollow = the gaps between the 3 - 5 bristle strands, not an outline. Two lightness layers (plan C-2): the
// EDGE of each strand (where coverage is just above the cut) is the reading ink L* 18, the body of a strand is the dry pigment L* 60 in the body hue (it settles from the wet colour between
// 0.18 and 0.30 s). The start of a stroke is pooled (ink-loaded, wider, no gaps), the end is starved: the gaps widen and the fibres end one by one (feathered).
// Mesh = Vol2InkWriter: a CENTRE LINE per stroke with two vertices per point; this shader widens it to a ribbon (camera-facing around its own axis, or flat on the ground) and keeps a least
// width in pixels. Per vertex:
//   POSITION centre-line point | NORMAL line direction | TANGENT fleck drift (xyz m/s, w seconds)
//   uv0 = (arc length s, across -1..1, total length L, half width) | uv1 = (appear s, pen speed m/s, end s, end seconds) | uv2 = (end mode 1..6, warn s, seed, hollow) | uv3 = (layout, density, role, flags)
// flags: bit0 taper tip, bit1 taper base, bit2 ash (a grey fleck of burnt ink). Per instance (MaterialPropertyBlock): _Age, _Head, _Offset, the body colours and cue.
// Body cue (plan C, "큰 가장자리", must read without colour): 0 wood long torn fibres | 1 fire burnt-through holes + saw-tooth ends | 2 earth stacked plates (stair-stepped width, seams) |
// 3 metal straight cut ends + transverse breaks | 4 water open hooked end + hollow parallel streams. End (plan B): mode 1 = the body's own movement, 2 folds back to its root, 3 dries in
// place, 4 flattens, 5 stops and breaks off, 6 wiped from the root. Written conservatively after RiposteInk308.
Shader "Oheangbu/SpellVFX308/Vol2Ink"
{
    Properties
    {
        _InkColor("Reading ink layer (L* 18)", Color) = (.1725,.1725,.1725,1)
        _PigmentColor("Dry pigment layer (L* 60)", Color) = (.3,.62,.53,1)
        _WetColor("Wet pigment (just laid)", Color) = (.21,.55,.46,1)
        [NoScaleOffset] _Bristle("Bristle mask (R: ink; tiles along the stroke)", 2D) = "white" {}
        _BristleLen("Metres per bristle tile", Float) = 1.6
        _BristleBand("Share of the bristle rows across one ribbon", Float) = .075
        _EdgeKind("Edge cue (0 fibre 1 saw 2 plate 3 cut 4 hook)", Float) = 0
        _EndKind("End behaviour (0 unravel 1 ash 2 sink 3 snap 4 run)", Float) = 0
        _EndDir("End direction (xyz) and travel (w)", Vector) = (0,0,1,.5)
        _Age("Age (s)", Float) = 0
        _Head("Path head (m along the line)", Float) = 0
        _TailLen("Tail behind the head (m)", Float) = 99
        _Offset("Rigid offset", Vector) = (0,0,0,0)
        _BaseY("Ground y", Float) = 0
        _Fray("Fray", Range(0,1)) = .5
        _Gap("Gap between strands", Range(0,.6)) = .18
        _HeadRag("Ragged head (m)", Range(0,.6)) = .22
        _MinPx("Least stroke width (px)", Float) = 56
        _SettleStart("Settle start (s)", Float) = .18
        _SettleEnd("Settle end (s)", Float) = .30
        _NearClip("Nothing nearer than (m)", Float) = .12
        _NearFade("Whole from (m)", Float) = .3
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "../InkCommon308.hlsl"
        struct Attributes
        {
            float4 positionOS : POSITION; float3 dirOS : NORMAL; float4 drift : TANGENT;
            float4 a : TEXCOORD0; float4 b : TEXCOORD1; float4 c : TEXCOORD2; float4 d : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings { float4 positionCS : SV_POSITION; float4 A : TEXCOORD0; float4 B : TEXCOORD1; float4 C : TEXCOORD2; float4 D : TEXCOORD3; };
        TEXTURE2D(_Bristle); SAMPLER(sampler_Bristle);
        CBUFFER_START(UnityPerMaterial)
        half4 _InkColor, _PigmentColor, _WetColor; float4 _EndDir, _Offset;
        float _BristleLen, _BristleBand, _EdgeKind, _EndKind, _Age, _Head, _TailLen, _BaseY, _Fray, _Gap, _HeadRag, _MinPx, _SettleStart, _SettleEnd, _NearClip, _NearFade;
        CBUFFER_END

        float OhTri(float x) { return abs(frac(x) - 0.5) * 2.0; }

        Varyings vert(Attributes v)
        {
            Varyings o; UNITY_SETUP_INSTANCE_ID(v);
            float s = v.a.x, side = v.a.y, L = v.a.z, hw = v.a.w;
            float t0 = v.b.x, speed = v.b.y, tEnd = v.b.z, endDur = v.b.w;
            float mode = v.c.x, warnDur = v.c.y, seed = v.c.z;
            float layout = v.d.x, role = v.d.z, flags = v.d.w;
            float3 P = TransformObjectToWorld(v.positionOS.xyz);
            float ageS = _Age - t0;
            bool follow = role > 0.5 && role < 1.5;
            // flecks fly out and slow down (linear drag: eased out)
            if (role > 2.5)
            {
                float k = saturate(ageS / max(v.drift.w, 0.001));
                k = 1.0 - (1.0 - k) * (1.0 - k);
                P += v.drift.xyz * (k * v.drift.w);
            }
            // the loop rides the path head
            if (role > 1.5 && role < 2.5) P += _Offset.xyz;
            float dry = tEnd > 900000.0 ? 0.0 : saturate((_Age - tEnd) / max(endDur, 0.001));
            float along = saturate(s / max(L, 0.001));
            // natural end: the body's own movement (plan C)
            if (abs(mode - 1.0) < 0.5 && dry > 0.0)
            {
                float k = _EndDir.w * dry;
                float3 dir = _EndDir.xyz;
                if (_EndKind < 0.5) k *= 0.35 + 0.65 * along;                                      // wood: the free end travels most
                if (abs(_EndKind - 3.0) < 0.5)                                                     // metal: three shards snap off alternately sideways
                {
                    float seg = min(2.0, floor(along * 2.999));
                    dir *= (seg < 0.5 || seg > 1.5) ? 1.0 : -1.0;
                    dir *= (frac(seed * 7.13) < 0.5 ? 1.0 : -1.0);
                }
                if (layout > 0.5 && (abs(_EndKind - 2.0) < 0.5)) k = 0.0;                          // a flat stroke cannot sink: it breaks into plates instead
                if (layout > 0.5 && (abs(_EndKind - 4.0) < 0.5)) dir = normalize(v.dirOS + 0.0001);   // water on the ground runs along the line
                P += dir * k;
                if (abs(_EndKind - 2.0) < 0.5 && layout < 0.5) P.y += abs(side) * 0.14 * dry;       // earth: the middle sinks, the rim stays up and lifts
            }
            if (abs(mode - 4.0) < 0.5) P.y = lerp(P.y, _BaseY, dry);                               // replaced: flattened
            float3 T = normalize(TransformObjectToWorldDir(v.dirOS) + 0.00001);
            float3 toCam = _WorldSpaceCameraPos.xyz - P;
            float eye = -TransformWorldToView(P).z;
            float3 sd;
            if (layout > 0.5)
            {
                sd = cross(float3(0, 1, 0), T);
                sd = dot(sd, sd) < 0.00001 ? float3(1, 0, 0) : normalize(sd);
            }
            else
            {
                sd = cross(T, toCam);
                sd = dot(sd, sd) < 0.000001 ? float3(1, 0, 0) : normalize(sd);
            }
            float lim = follow ? _Head : (ageS < 0.0 ? -1.0 : (speed > 90000.0 ? 100000.0 : ageS * speed));
            float span = clamp(0.22 * L, 0.2, 0.9);
            float tipD = (follow ? lim : L) - s;
            // water: the end is an open hook - the ribbon bends away sideways over its last stretch
            if (_EdgeKind > 3.5)
            {
                float tk = 1.0 - saturate(tipD / span);
                P += sd * (tk * tk * span * 1.6);
            }
            float pxWorld = max(eye, 0.05) / max(0.5 * _ScreenParams.y * unity_CameraProjection._m11, 1.0);
            float pool = follow ? (1.0 - smoothstep(0.0, 0.5, s)) : 0.0;                          // the start is loaded: wider
            float minPx = lerp(14.0, _MinPx, saturate((hw - 0.04) / 0.11)) * (role > 2.5 ? 0.5 : 1.0);   // a wide stroke keeps the full pixel floor, a thin one (a twig, a fleck) a smaller one
            float hwEff = clamp(0.5 * minPx * pxWorld, hw * (1.0 + 0.3 * pool), hw * 4.0);
            float3 Pw = P + sd * (side * hwEff);
            o.positionCS = TransformWorldToHClip(Pw);
            float wear = (warnDur > 0.0 && tEnd < 900000.0) ? saturate(1.0 - (tEnd - _Age) / warnDur) : 0.0;
            o.A = float4(s, side, L, lim);
            o.B = float4(dry, mode, seed, v.c.w);
            o.C = float4(wear, v.d.y, role, eye);
            o.D = float4(ageS, flags, 2.0 * hwEff / pxWorld, Pw.y);
            return o;
        }

        // cuts the stroke out; returns (ink?, grain, strand body 0..1, ash) for the colour
        float4 Cut(Varyings i)
        {
            float s = i.A.x, side = i.A.y, L = max(i.A.z, 0.001), lim = i.A.w;
            float dry = i.B.x, mode = i.B.y, seed = i.B.z, hollow = i.B.w;
            float wear = i.C.x, dens = i.C.y, role = i.C.z, eye = i.C.w;
            float ageS = i.D.x, flags = i.D.y, pxw = i.D.z;
            bool follow = role > 0.5 && role < 1.5;
            float e = abs(side);
            float u = saturate(s / L);
            // the bristles: rows across the ribbon, long runs along it
            float2 buv = float2(s / _BristleLen + seed * 0.37, frac(seed * 0.173) * 0.8 + (side * 0.5 + 0.5) * _BristleBand);
            float bristle = SAMPLE_TEXTURE2D(_Bristle, sampler_Bristle, buv).r;
            // written so far; the head edge is ragged by the bristles (each strand ends where its own ink ends), never a soft fade
            clip(lim - s + (bristle - 0.5) * _HeadRag);
            // the line is a flick: ink further behind the head than the tail length has dried
            if (follow) clip(s - (_Head - _TailLen) + (bristle - 0.5) * 0.3);
            float tipD = follow ? (lim - s) : (L - s);
            float span = clamp(0.22 * L, 0.2, 0.9);
            float tk = 1.0 - saturate(tipD / span);
            bool tipT = fmod(flags, 2.0) > 0.5;
            bool baseT = fmod(floor(flags * 0.5), 2.0) > 0.5;
            bool ash = flags > 3.5;
            float pool = follow ? (1.0 - smoothstep(0.0, 0.5, s)) : 0.0;
            // ---- silhouette ----
            float emax = 1.0;
            float grain = OhFbm(float2(s * 9.0 + seed * 3.7, side * 3.3 + seed * 1.9));
            float fray = _Fray;
            if (tipT && abs(_EdgeKind - 2.0) > 0.5) emax *= lerp(1.0, 0.7, smoothstep(0.0, 1.0, tk));
            if (baseT) emax *= lerp(0.35, 1.0, smoothstep(0.0, 0.22, u));
            // body cue 2, earth: stacked low plates - over its last stretch the stroke steps down in 3 stairs (each a shorter, narrower plate laid along the stroke), a dark seam on every step
            float seam = 0.0;
            if (abs(_EdgeKind - 2.0) < 0.5 && !ash)
            {
                float stepI = floor(max(tipD, 0.0) / (span * 0.6));          // 0 = the last plate .. 2 = the third from the end
                if (stepI < 3.0)
                {
                    emax *= 0.42 + 0.19 * stepI;
                    float f2 = frac(max(tipD, 0.0) / (span * 0.6));
                    seam = smoothstep(0.84, 1.0, f2);                        // the upper lip of a plate is ink
                }
                fray *= 0.25;
                clip(tipD);
            }            clip(emax - e + (grain - 0.5) * fray * 0.4);
            // ---- the strands (3 - 5, as many as the width in pixels allows with a >= 6 px gap between them) ----
            float nS = 1.0, sid = 0.0, edgeN = 1.0, inkW = 0.0;
            if (!ash)
            {
                nS = pxw < 30.0 ? 1.0 : clamp(floor(pxw / 18.0), 2.0, 5.0);
                float tt = (side * 0.5 + 0.5) * nS;
                sid = floor(min(tt, nS - 0.001));
                float wob = 0.20 * sin(s * 1.7 + OhHash(float2(sid, seed)) * 6.28) * (0.4 + 0.6 * smoothstep(0.3, 1.5, s));      // the strands weave: they touch and part again, they are not rails
                float f = frac(tt + wob);
                float split = smoothstep(0.35 + 0.5 * frac(seed * 3.1), 1.4 + 0.5 * frac(seed * 3.1), s);      // one loaded ribbon at the start: the strands part from 15 - 35% of the way on
                float gf =clamp(7.0 / (pxw / nS), 0.10, 0.38) * split;
                clip(min(f - gf * 0.5, 1.0 - f - gf * 0.5));                                       // the gap between two strands
                float pres = OhValueNoise(float2(s * 1.4 + sid * 7.3 + seed, sid * 3.1 + 0.5));
                clip(pres - (0.10 + 0.55 * tk * tk + 0.25 * wear + (1.0 - dens) * 0.3) * split);   // a strand runs dry and drops out (more toward the end)
                edgeN = min(f - gf * 0.5, 1.0 - f - gf * 0.5) / max((1.0 - gf) * 0.5, 0.01);
                float halfPx = max(pxw / nS * (1.0 - gf) * 0.5, 1.0);
                inkW = clamp(3.5 / halfPx, 0.14, 0.5);                                              // the dry ink edge of a strand: >= 3.5 px
                clip(bristle - (0.12 + 0.5 * tk * tk + 0.10 * fray + hollow * 0.03));              // long dry streaks inside a strand
            }
            // body cue (plan C, "큰 가장자리": the cue is a SHAPE, it must read without colour)
            float ring = 0.0;
            if (_EdgeKind < 0.5)            // wood: long torn fibres - each strand ends at its own length, up to 1.6 spans from the end
            {
                float r = OhHash(float2(sid + seed * 3.0, 4.2));
                clip(tipD - (1.0 - r) * span * 2.4);
            }
            else if (_EdgeKind < 1.5)       // fire: saw-tooth end, burnt-through holes with an ink rim
            {
                clip(tipD - span * 1.1 * OhTri(side * 2.0 + seed));
                float hole = OhFbm(float2(s * 1.5 + seed * 5.0, side * 1.1 + seed));
                clip(0.58 - hole);
                ring = smoothstep(0.46, 0.58, hole);
            }
            else if (_EdgeKind > 2.5 && _EdgeKind < 3.5)   // metal: straight oblique cut at the end, transverse breaks through the whole ribbon
            {
                clip(tipD - 0.04 - side * span * 0.3);
                clip(min(abs(tipD - span * 0.85) - 0.07, abs(tipD - span * 1.9) - 0.07));      // two transverse breaks through the ribbon, near the end
            }
            else if (_EdgeKind > 3.5)       // water: the strands are the hollow parallel streams (the open hook is bent in the vertex stage)
            {
                clip(frac(s / 1.1 + sid * 0.37 + seed) - 0.04 * (1.0 + tk));
            }
            if (wear > 0.0) clip(OhValueNoise(float2(s * 9.0 + seed * 3.0, side * 5.0)) - wear * (0.2 + 0.8 * tk) * 0.8);
            // ---- the end ----
            if (dry > 0.0)
            {
                float dd = dry * 1.04;
                if (abs(mode - 1.0) < 0.5)
                {
                    if (_EndKind < 0.5) clip(OhHash(float2(sid + seed * 5.0, 9.1)) * 0.6 + 0.4 - dd);
                    else if (_EndKind < 1.5) clip(OhValueNoise(float2(s * 7.0 + seed, side * 4.0)) - dd);
                    else if (_EndKind < 2.5) { clip(OhHash(float2(floor(s * 4.0), seed)) * 0.5 + 0.5 - dd); clip(i.D.w - (_BaseY - 0.02)); }
                    else if (_EndKind < 3.5)
                    {
                        float gapA = abs(u - 0.3333) - dry * 0.07, gapB = abs(u - 0.6667) - dry * 0.07;
                        clip(min(gapA, gapB));
                        clip(OhValueNoise(float2(s * 5.0 + seed, side * 3.0)) - (dry - 0.6) * 2.5);
                    }
                    else clip(OhValueNoise(float2(side * 6.0 + seed * 3.0, 1.3)) * 0.6 + 0.4 - dd * 0.98);
                }
                else if (abs(mode - 2.0) < 0.5) clip(L * (1.0 - dd) - s);
                else if (abs(mode - 3.0) < 0.5) clip(OhValueNoise(float2(s * 13.0 + seed * 3.0, side * 7.0)) - dd);
                else if (abs(mode - 4.0) < 0.5) clip(OhValueNoise(float2(s * 17.0 + seed, side * 6.0)) - dd);
                else if (abs(mode - 5.0) < 0.5) clip(OhValueNoise(float2(s * 21.0 + seed, side * 9.0)) - dd);
                else if (abs(mode - 6.0) < 0.5) clip(s - dd * L + (bristle - 0.5) * 0.1);
            }
            clip(OhNearVisibility(eye, _NearClip, _NearFade) - OhDither(i.positionCS.xy) - 0.001);
            // the edge of a strand (and the pooled start, the rings of holes, the lips of plates) is the reading ink; the body of a strand is the dry pigment
            float body = smoothstep(inkW, inkW + 0.10, edgeN) * smoothstep(0.0, 0.10, bristle - (0.12 + 0.5 * tk * tk)) * (1.0 - 0.95 * pool) * (1.0 - ring) * (1.0 - seam);
            float inkEdge = (ash || body >= 0.5) ? 0.0 : 1.0;
            return float4(inkEdge, grain, body, ash ? 1.0 : 0.0);
        }
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            half4 frag(Varyings i) : SV_Target
            {
                float4 c = Cut(i);
                float settle = smoothstep(_SettleStart, _SettleEnd, i.D.x);
                float3 pig = lerp(_WetColor.rgb, _PigmentColor.rgb, settle) * (0.9 + 0.2 * c.y);
                float3 rgb = c.x > 0.5 ? _InkColor.rgb : pig;
                // ash is burnt ink: a neutral grey, whatever the body hue
                if (c.w > 0.5) rgb = float3(0.30, 0.29, 0.28);
                // the ash of a fire stroke ending turns grey as it lets go
                if (abs(_EndKind - 1.0) < 0.5 && abs(i.B.y - 1.0) < 0.5) rgb = lerp(rgb, float3(0.30, 0.29, 0.28), saturate(i.B.x * 1.2));
                return half4(min(rgb, OH_INK_CEILING_OUT), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            half4 frag(Varyings i) : SV_Target { Cut(i); return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            half4 frag(Varyings i) : SV_Target { Cut(i); return half4(-GetViewForwardDir(), 0); }
            ENDHLSL
        }
    }
}
