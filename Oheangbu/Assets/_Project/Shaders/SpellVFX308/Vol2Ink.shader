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
        [NoScaleOffset] _Sheet("NB20 ink atlas (R tone, G dry pigment, B paper-rim distance, A coverage)", 2D) = "black" {}
        [NoScaleOffset] _SheetTime("NB20 order atlas (R reveal order, G drying order, B fragment)", 2D) = "black" {}
        _PaperColor("NB20 dry paper rim (flat, no light)", Color) = (.80,.76,.66,1)
        _RimPx("NB20 paper rim width (px)", Float) = 3
        _EdgePx("NB20 dry pigment edge band (px)", Float) = 1.75
        [NoScaleOffset] _Crack("stroke20 crack field (R: 0 on a crack .. 1 at a flake centre; RipStroke308_Crack)", 2D) = "white" {}
        _BrushInk("stroke20 wet ink (the 앞잡 ink)", Color) = (.055,.05,.047,1)
        _BrushDry("stroke20 dry grey-black", Color) = (.2,.185,.17,1)
        _BrushTile("stroke20 metres per bristle tile", Float) = .45
        _CrackTile("stroke20 metres per crack tile", Float) = .80
        _BrushRag("stroke20 ragged head (m)", Float) = .07
        _BrushTailDry("stroke20 how dry the tail runs", Range(0,1)) = .55
        _BrushTint("stroke20 body pigment in the starved fibres (0..1)", Range(0,1)) = .5
        _BrushPigment("stroke20 pigment value scale", Range(0,1)) = .8
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
            float4 a : TEXCOORD0; float4 b : TEXCOORD1; float4 c : TEXCOORD2; float4 d : TEXCOORD3; float4 e : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings { float4 positionCS : SV_POSITION; float4 A : TEXCOORD0; float4 B : TEXCOORD1; float4 C : TEXCOORD2; float4 D : TEXCOORD3; float4 E : TEXCOORD4; };
        TEXTURE2D(_Bristle); SAMPLER(sampler_Bristle);
        TEXTURE2D(_Sheet); SAMPLER(sampler_Sheet);
        TEXTURE2D(_SheetTime); SAMPLER(sampler_SheetTime);
        TEXTURE2D(_Crack); SAMPLER(sampler_Crack);
        CBUFFER_START(UnityPerMaterial)
        half4 _InkColor, _PigmentColor, _WetColor, _PaperColor; float4 _EndDir, _Offset;
        float _BristleLen, _BristleBand, _EdgeKind, _EndKind, _Age, _Head, _TailLen, _BaseY, _Fray, _Gap, _HeadRag, _MinPx, _SettleStart, _SettleEnd, _NearClip, _NearFade, _RimPx, _EdgePx;
        half4 _BrushInk, _BrushDry;
        float _BrushTile, _CrackTile, _BrushRag, _BrushTailDry, _BrushTint, _BrushPigment;
        CBUFFER_END

        // ---- NB20 (D308-150 V6): the authored ink atlas (Blender bristle brushes + After Effects bleed / drying order, Tools/Art/SpellVFX308/nb20) ----
        // 4 x 16 cells of 1024 x 256 in a 4096 atlas, 8 px gutter; cell row 0 at the top of the image. uv4 = (cell, flags, -, -): flags 1 paper rim, 2 flip across, 4 least aspect, 8 reveal by the head, 16 (placed reversed by the recipe)
        #define NB_ATLAS 4096.0
        #define NB_CW 1024.0
        #define NB_CH 256.0
        #define NB_GUT 8.0
        float NbBit(float flags, float bit) { return fmod(floor(flags / bit), 2.0); }

        float OhTri(float x) { return abs(frac(x) - 0.5) * 2.0; }

        Varyings vert(Attributes v)
        {
            Varyings o; UNITY_SETUP_INSTANCE_ID(v);
            float s = v.a.x, side = v.a.y, L = v.a.z, hw = v.a.w;
            float t0 = v.b.x, speed = v.b.y, tEnd = v.b.z, endDur = v.b.w;
            float mode = v.c.x, warnDur = v.c.y, seed = v.c.z;
            float layout = v.d.x, role = v.d.z, flags = v.d.w;
            bool brush = layout > 3.5;                                                                // stroke20: the 앞잡 brush ribbon (layouts 4 / 5)
            bool sheet = layout > 1.5 && !brush;
            bool flatL = abs(layout - 1.0) < 0.5 || (layout > 2.5 && layout < 3.5) || layout > 4.5;
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
                if (flatL && (abs(_EndKind - 2.0) < 0.5)) k = 0.0;                          // a flat stroke cannot sink: it breaks into plates instead
                if (flatL && (abs(_EndKind - 4.0) < 0.5)) dir = normalize(v.dirOS + 0.0001);   // water on the ground runs along the line
                if (brush) k *= 0.4;                                                               // stroke20: the body's end is a small accent, not a journey
                P += dir * k;
                if (abs(_EndKind - 2.0) < 0.5 && !flatL) P.y += abs(side) * 0.14 * dry;       // earth: the middle sinks, the rim stays up and lifts
            }
            if (abs(mode - 4.0) < 0.5) P.y = lerp(P.y, _BaseY, dry);                               // replaced: flattened
            float3 T = normalize(TransformObjectToWorldDir(v.dirOS) + 0.00001);
            float3 toCam = _WorldSpaceCameraPos.xyz - P;
            float eye = -TransformWorldToView(P).z;
            float3 sd;
            if (flatL)
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
            if (_EdgeKind > 3.5 && !sheet && !brush)
            {
                float tk = 1.0 - saturate(tipD / span);
                P += sd * (tk * tk * span * 1.6);
            }
            float pxWorld = max(eye, 0.05) / max(0.5 * _ScreenParams.y * unity_CameraProjection._m11, 1.0);
            float pool = follow ? (1.0 - smoothstep(0.0, 0.5, s)) : 0.0;                          // the start is loaded: wider
            float minPx = lerp(14.0, _MinPx, saturate((hw - 0.04) / 0.11)) * (role > 2.5 ? 0.5 : 1.0);   // a wide stroke keeps the full pixel floor, a thin one (a twig, a fleck) a smaller one
            float hwEff = clamp(0.5 * minPx * pxWorld, hw * (1.0 + 0.3 * pool), hw * 4.0);
            float4 E = float4(v.e.x, v.e.y, 1.0, 0.0);
            float3 Pw = P + sd * (side * hwEff);
            o.positionCS = TransformWorldToHClip(Pw);
            if (sheet)
            {
                // NB20: the authored element keeps its real size (no pixel floor: the 0.3 m lane is not widened)
                hwEff = hw;
                Pw = P + sd * (side * hwEff);
                o.positionCS = TransformWorldToHClip(Pw);
                if (!flatL)
                {
                    // r4: a STROKE IN THE VIEW PLANE (a velocity-aligned billboard): the quad lies at the depth of its u = 0 point (the press), its long axis is the screen
                    // direction of the element's world axis, its size is its world size there. A head / tail flying away from the shoulder camera is no longer seen edge-on
                    // (a degenerate ribbon whose texels collapse to noise): it reads as a pressed stroke pointing where it goes, the tail streaming behind it
                    float3 P0w = P - T * s;
                    float3 v0 = TransformWorldToView(P0w), v1 = TransformWorldToView(P0w + T * max(L, 0.05));
                    float2 ds = v1.xy / max(-v1.z, 0.05) - v0.xy / max(-v0.z, 0.05);
                    ds = dot(ds, ds) < 1e-10 ? float2(0.0, 1.0) : normalize(ds);
                    float2 dn = float2(-ds.y, ds.x);
                    float3 vp = v0 + float3(ds * s + dn * (side * hw), 0.0);
                    o.positionCS = TransformWViewToHClip(vp);
                    eye = -vp.z;
                }
                // reveal: by the element's order map over its reveal time (uv1.y), or as far as the path head has gone (the tail)
                float prog = ageS < 0.0 ? -1.0 : (speed < 0.001 ? 1.0 : saturate(ageS / speed));
                if (NbBit(v.e.y, 8.0) > 0.5) prog = ageS < 0.0 ? -1.0 : saturate((_Head - 0.05) / max(L, 0.01));
                E = float4(v.e.x, v.e.y, prog, 1.0);
            }
            if (brush)
            {
                // stroke20: the ribbon lies on its own fixed world plane (tangent = the plane normal; the 앞잡 stroke never follows the camera), or faces the camera around its axis
                // when it has no plane (a flying path, a fleck). Width is per point (uv0.w), as RiposteStroke308Path writes it: pressed start, thin dry end.
                float3 Nw = role > 2.5 ? float3(0, 0, 0) : v.drift.xyz;
                float3 sdB;
                if (dot(Nw, Nw) > 0.01) { sdB = cross(normalize(Nw), T); sdB = dot(sdB, sdB) < 0.000001 ? sd : normalize(sdB); }
                else { sdB = cross(T, toCam); sdB = dot(sdB, sdB) < 0.000001 ? float3(1, 0, 0) : normalize(sdB); }
                float inkV = v.e.z, tl = max(v.e.w, 0.05), headDry = 0.0, hwB = hw;
                if (follow)
                {
                    // the flying stroke: the press is AT the head (blunt front), the ink thins and starves behind it and cracks away past the tail length
                    float dh = _Head - s;
                    float kT = saturate(dh / tl);
                    hwB = hw * lerp(1.0, 0.30, pow(kT, 0.7)) * lerp(0.35, 1.0, sqrt(saturate(dh / 0.22)));               // r6: a rounded press, not a square end
                    inkV *= lerp(1.0, 0.45, kT);
                    headDry = saturate((dh - tl * 0.55) / (tl * 0.45));
                }
                hwB = max(hwB, 1.25 * pxWorld);                                                   // never thinner than 2.5 px across (a hairline would shimmer)
                Pw = P + sdB * (side * hwB);
                o.positionCS = TransformWorldToHClip(Pw);
                hwEff = hwB;
                E = float4(inkV, headDry, hwB, 2.0);
            }
            float wear = (warnDur > 0.0 && tEnd < 900000.0) ? saturate(1.0 - (tEnd - _Age) / warnDur) : 0.0;
            o.A = float4(s, side, L, lim);
            o.B = float4(dry, mode, seed, v.c.w);
            o.C = float4(wear, v.d.y, role, eye);
            o.D = float4(ageS, flags, 2.0 * hwEff / pxWorld, Pw.y);
            o.E = E;
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

        // NB20: cuts an authored element out of the atlas; returns (kind: 0 ink / 1 pigment / 2 paper rim, ink tone 0..1, drying edge 0..1, ash)
        float4 SheetCut(Varyings i)
        {
            float u = saturate(i.A.x / max(i.A.z, 0.001)), v = i.A.y * 0.5 + 0.5;
            float flags = i.E.y, prog = i.E.z;
            if (NbBit(flags, 2.0) > 0.5) v = 1.0 - v;
            float cell = floor(i.E.x + 0.5);
            float row = floor(cell * 0.25), col = cell - row * 4.0;
            float2 texel = float2(u * (NB_CW - 2.0 * NB_GUT), (1.0 - v) * (NB_CH - 2.0 * NB_GUT));
            float2 uv = float2((col * NB_CW + NB_GUT + texel.x) / NB_ATLAS, 1.0 - (row * NB_CH + NB_GUT + texel.y) / NB_ATLAS);
            // texels of the packed cell per screen pixel (the rim is drawn in PIXELS: >= 3 px at every distance, never a fat sticker outline up close)
            float tpp = max(max(length(ddx(texel)), length(ddy(texel))), 1e-4);
            float4 sh = SAMPLE_TEXTURE2D(_Sheet, sampler_Sheet, uv);
            float4 tm = SAMPLE_TEXTURE2D(_SheetTime, sampler_SheetTime, uv);
            float dry = i.B.x, mode = i.B.y, seed = i.B.z;
            // laid in the element's own order (the press first, the needle lines last) - each texel appears whole, never a soft fade
            clip(prog - tm.r + 0.002);
            // drying by the After Effects order: crack lines first, then the thin edges, then fragment by fragment (consume / cleanse fold back to the press instead)
            float edgeDry = 0.0;
            if (dry > 0.0)
            {
                if (abs(mode - 2.0) < 0.5 || abs(mode - 6.0) < 0.5) clip((1.0 - dry * 1.03) - tm.r);
                else
                {
                    clip(tm.g - dry * 1.03);
                    edgeDry = 1.0 - saturate((tm.g - dry * 1.03) / 0.10);
                }
            }
            clip(OhNearVisibility(i.C.w, _NearClip, _NearFade) - OhDither(i.positionCS.xy) - 0.001);
            bool ash = fmod(floor(i.D.y * 0.25), 2.0) > 0.5;
            if (sh.a >= 0.5)
            {
                // r4: the dry pigment edge - within _EdgePx screen pixels of the stroke's own edge (order map B = sqrt(texels in / 160)), broken here and there:
                // every stroke carries its body hue as a thin dry edge (two lightness layers, plan C-2) and so reads on a dark ground without a paper outline
                float dIn = tm.b * tm.b * 160.0 / tpp;
                bool edgePig = dIn < _EdgePx && OhValueNoise(texel / tpp * 0.05 + seed * 1.7) > 0.42;       // r5: a broken dry edge (about half of the outline), never a closed coloured outline
                return float4((sh.g >= 0.5 || edgePig) && !ash ? 1.0 : 0.0, sh.r, edgeDry, ash ? 1.0 : 0.0);
            }
            // outside the ink: the dry paper rim (mine, plan E) - only on elements that carry it, within _RimPx screen pixels of the ink, broken by the paper's tooth
            clip(NbBit(flags, 1.0) - 0.5);
            float dpx = sh.b * sh.b * 160.0 / tpp;                  // the packer stores sqrt(texels / 160)
            clip(_RimPx - dpx);
            clip(0.97 - sh.b);
            // broken by the paper's tooth at a SCREEN scale (a gap every few pixels), so it never turns into salt-and-pepper when the element is small
            clip(OhValueNoise(texel / tpp * 0.06 + seed * 3.1) - 0.14);           // r4: a few long breaks (every ~20 px), not a dotted line
            return float4(2.0, 0.0, edgeDry, 0.0);
        }

        // stroke20 (D308-150 V7, "앞잡 한 획을 작게"): RiposteInk308's Cut() on a small ribbon, tiled in METRES so a 0.3 m stroke keeps the 앞잡's streak and flake sizes on screen.
        // returns (starve 0..1, bristle, across 0 rim .. 1 centre, drying 0..1) for the colour
        float4 BrushCut(Varyings i)
        {
            float s = i.A.x, side = i.A.y, L = max(i.A.z, 0.001), lim = i.A.w;
            float dry = i.B.x, mode = i.B.y, seed = i.B.z;
            float wear = i.C.x, role = i.C.z, eye = i.C.w;
            float ink = i.E.x, headDry = i.E.y, hwB = max(i.E.z, 0.004);
            bool follow = role > 0.5 && role < 1.5;
            float u = saturate(s / L), v = side * 0.5 + 0.5;
            // the bristle rows across: a narrow stroke takes fewer of them (a 0.3 m stroke ~6 streaks, the 1.5 m 앞잡 head all of them)
            float rows = clamp(hwB * 2.0 / 1.2, 0.22, 1.0);
            // along: _BrushTile metres per tile, but never more than 3 tiles over a long stroke (the 앞잡 tiles 3 times over its length: a 4 m ground stroke tiled every 0.45 m read as a ladder)
            float tileL = max(_BrushTile, L / 3.0);
            float bristle = SAMPLE_TEXTURE2D(_Bristle, sampler_Bristle, float2(s / tileL + seed * 0.37, frac(seed * 0.173) * (1.0 - rows) + v * rows)).r;
            // written so far; the head edge is ragged by the bristles, never a soft edge
            clip(lim - s + (bristle - 0.5) * _BrushRag);
            // dry-brush coverage: the edges go first, then the starved ink and the tail
            float across = 1.0 - abs(side);
            // the tail runs dry only where the stroke itself is starved there (a burst stroke that lands pressed keeps its wet end)
            float starve = saturate(1.0 - ink) + (follow ? 0.0 : _BrushTailDry * smoothstep(0.5, 1.0, u) * saturate((1.0 - ink) * 3.0)) + wear * 0.5;
            clip(bristle - starve * 0.8 + (across - 0.3) * 0.9 - 0.35);
            // drying through the crack field, the end of the stroke first (a flying stroke: its tail behind the head)
            float dd = saturate(dry * 1.04);
            float dryLocal = saturate(max(dd > 0.0 ? dd * 1.35 - (1.0 - u) * 0.35 : 0.0, headDry * 1.2));
            if ((abs(mode - 2.0) < 0.5 || abs(mode - 6.0) < 0.5) && dd > 0.0) clip(L * (1.0 - dd) - s + (bristle - 0.5) * 0.05);      // consumed / cleansed: back to its root
            float crack = SAMPLE_TEXTURE2D(_Crack, sampler_Crack, float2(s / _CrackTile + seed * 0.19, side * hwB / _CrackTile + seed * 0.07)).r;
            clip(dryLocal > 0.0 ? crack - dryLocal - 0.001 : 1.0);
            // fire: burnt-through holes where the ink runs dry
            if (abs(_EdgeKind - 1.0) < 0.5 && starve > 0.35) clip(0.70 - OhFbm(float2(s * 7.0 + seed * 5.0, side * hwB * 7.0 + seed)) + (1.0 - saturate(starve)) * 0.3);
            clip(OhNearVisibility(eye, _NearClip, _NearFade) - OhDither(i.positionCS.xy) - 0.001);
            return float4(saturate(starve), bristle, across, dryLocal);
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
                float settle = smoothstep(_SettleStart, _SettleEnd, i.D.x);
                if (i.E.w > 1.5)
                {
                    // stroke20: the 앞잡 ink (near black, dry grey-black where it runs dry); the body hue only in the starved fibres and the rim, never a coloured outline
                    float4 kb = BrushCut(i);
                    float dried = saturate(kb.w * 1.6 + kb.x * 0.45);
                    float3 rgbB = lerp(_BrushInk.rgb, _BrushDry.rgb, dried) * (0.92 + 0.16 * kb.y);
                    float tint = _BrushTint * smoothstep(0.30, 0.95, kb.x + (1.0 - kb.z) * 0.30);
                    float3 bodyC = lerp(_WetColor.rgb, _PigmentColor.rgb, settle) * _BrushPigment;
                    rgbB = lerp(rgbB, bodyC, tint);
                    if (fmod(floor(i.D.y * 0.25), 2.0) > 0.5) rgbB = float3(0.10, 0.095, 0.09);           // fire's ash: burnt ink grey
                    return half4(min(rgbB, OH_INK_CEILING_OUT), 1);
                }
                if (i.E.w > 0.5)
                {
                    // NB20: ink L* ~10 (wet pool) .. ~30 (dry streak) around the reading layer L* 18; dry pigment (body hue, L* 60) settling from the wet colour; the paper rim flat; ash grey
                    float4 k = SheetCut(i);
                    float3 inkC = _InkColor.rgb * lerp(0.45, 2.4, k.y) * (1.0 + 0.5 * k.z);
                    float3 pigC = lerp(_WetColor.rgb, _PigmentColor.rgb, settle) * (1.0 - 0.18 * k.z);
                    float3 rgbS = k.x > 1.5 ? _PaperColor.rgb : (k.x > 0.5 ? pigC : inkC);
                    if (k.w > 0.5) rgbS = float3(0.30, 0.29, 0.28);
                    return half4(min(rgbS, OH_INK_CEILING_OUT), 1);
                }
                float4 c = Cut(i);
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
            half4 frag(Varyings i) : SV_Target { if (i.E.w > 1.5) BrushCut(i); else if (i.E.w > 0.5) SheetCut(i); else Cut(i); return i.positionCS.z; }
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
            half4 frag(Varyings i) : SV_Target { if (i.E.w > 1.5) BrushCut(i); else if (i.E.w > 0.5) SheetCut(i); else Cut(i); return half4(-GetViewForwardDir(), 0); }
            ENDHLSL
        }
    }
}
