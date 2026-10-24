// S5-b slice 9a (D308-150 V5): the ink of the vol.2 spells (plan v10 sections B, C, E). ONE shader for every vol.2 stroke; the recipe (Vol2VfxRecipes) lays the strokes, the body look
// (Vol2BodyLook) sets the colours and the cue, and nothing else decides the picture. OPAQUE CUTOUT, double sided, depth tested, writes depth (DepthOnly / DepthNormals share the cut):
// no emission, no additive, no alpha blend, no light, no glow, no bloom contribution; every pixel is a flat ink colour or it is cut away, clamped to the ink LDR ceiling.
// Mesh = Vol2InkWriter: a CENTRE LINE per stroke with two vertices per point; this shader widens it to a ribbon (camera-facing around its own axis, or flat on the ground) and keeps a least
// width in pixels so a thin line is not lost at 12 m. Per vertex:
//   POSITION centre-line point | NORMAL line direction | TANGENT fleck drift (xyz m/s, w seconds)
//   uv0 = (arc length s, across -1..1, total length L, half width) | uv1 = (appear s, pen speed m/s, end s, end seconds) | uv2 = (end mode 1..6, warn s, seed, hollow) | uv3 = (layout, density, role, flags)
// Per instance (MaterialPropertyBlock): _Age = seconds since the cast began on the scope clock, _Head = how far the path head has written the FollowHead strokes, _Offset = the head's rigid offset.
// Two lightness layers (plan C-2): the rim is the reading ink (L* 18), the band inside it is the dry pigment (L* 60) that settled from the wet colour between 0.18 and 0.30 s; the middle of my
// strokes is hollow (plan E: "마른 테 + 속 빈 획"), the rim is broken by gaps (the dry rim). Body cue (plan C, "큰 가장자리"): 0 torn fibre ends, 1 saw-tooth ends + burnt voids, 2 stepped plates,
// 3 straight cut + short cross breaks, 4 hollow parallel streams (the open hook is bent into the line by the recipe). End (plan B): mode 1 expire = the body's own movement, 2 consume = folds
// back to its root, 3 child = dries in place, 4 replaced = flattens, 5 hit-cancelled = stops and breaks off, 6 cleansed = wiped from the root. Written conservatively after RiposteInk308.
Shader "Oheangbu/SpellVFX308/Vol2Ink"
{
    Properties
    {
        _InkColor("Reading ink layer (L* 18)", Color) = (.1725,.1725,.1725,1)
        _PigmentColor("Dry pigment layer (L* 60)", Color) = (.3,.62,.53,1)
        _WetColor("Wet pigment (just laid)", Color) = (.21,.55,.46,1)
        _EdgeKind("Edge cue (0 fibre 1 saw 2 plate 3 cut 4 hook)", Float) = 0
        _EndKind("End behaviour (0 unravel 1 ash 2 sink 3 snap 4 run)", Float) = 0
        _EndDir("End direction (xyz) and travel (w)", Vector) = (0,0,1,.5)
        _Age("Age (s)", Float) = 0
        _Head("Path head (m along the line)", Float) = 0
        _TailLen("Tail behind the head (m)", Float) = 99
        _Offset("Rigid offset", Vector) = (0,0,0,0)
        _BaseY("Ground y", Float) = 0
        _Fray("Fray", Range(0,1)) = .5
        _Gap("Rim gap", Range(0,.6)) = .18
        _RimW("Rim width (share of the half width)", Range(.05,.45)) = .24
        _HeadRag("Ragged head (m)", Range(0,.5)) = .12
        _MinPx("Least stroke width (px)", Float) = 14
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
        CBUFFER_START(UnityPerMaterial)
        half4 _InkColor, _PigmentColor, _WetColor; float4 _EndDir, _Offset;
        float _EdgeKind, _EndKind, _Age, _Head, _TailLen, _BaseY, _Fray, _Gap, _RimW, _HeadRag, _MinPx, _SettleStart, _SettleEnd, _NearClip, _NearFade;
        CBUFFER_END

        Varyings vert(Attributes v)
        {
            Varyings o; UNITY_SETUP_INSTANCE_ID(v);
            float s = v.a.x, side = v.a.y, L = v.a.z, hw = v.a.w;
            float t0 = v.b.x, speed = v.b.y, tEnd = v.b.z, endDur = v.b.w;
            float mode = v.c.x, warnDur = v.c.y, seed = v.c.z;
            float layout = v.d.x, role = v.d.z, flags = v.d.w;
            float3 P = TransformObjectToWorld(v.positionOS.xyz);
            float ageS = _Age - t0;
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
                if (abs(_EndKind - 3.0) < 0.5) dir *= (frac(seed * 7.13) < 0.5 ? 1.0 : -1.0) * (along < 0.5 ? 1.0 : -1.0);   // metal: the two halves part
                if (layout > 0.5 && (abs(_EndKind - 2.0) < 0.5)) k = 0.0;                          // a flat stroke cannot sink: it breaks into plates instead
                if (layout > 0.5 && (abs(_EndKind - 4.0) < 0.5)) dir = normalize(v.dirOS + 0.0001);   // water on the ground runs along the line
                P += dir * k;
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
            float pxWorld = max(eye, 0.05) / max(0.5 * _ScreenParams.y * unity_CameraProjection._m11, 1.0);
            float hwEff = clamp(0.5 * _MinPx * pxWorld, hw, hw * 3.0);
            float3 Pw = P + sd * (side * hwEff);
            o.positionCS = TransformWorldToHClip(Pw);
            float lim = (role > 0.5 && role < 1.5) ? _Head : (ageS < 0.0 ? -1.0 : (speed > 90000.0 ? 100000.0 : ageS * speed));
            float wear = (warnDur > 0.0 && tEnd < 900000.0) ? saturate(1.0 - (tEnd - _Age) / warnDur) : 0.0;
            o.A = float4(s, side, L, lim);
            o.B = float4(dry, mode, seed, v.c.w);
            o.C = float4(wear, v.d.y, role, eye);
            o.D = float4(ageS, flags, hwEff / max(hw, 0.0001), Pw.y);
            return o;
        }

        float OhTri(float x) { return abs(frac(x) - 0.5) * 2.0; }

        // cuts the stroke out; returns (rim?, grain, tip proximity, 0) for the colour
        float4 Cut(Varyings i)
        {
            float s = i.A.x, side = i.A.y, L = max(i.A.z, 0.001), lim = i.A.w;
            float dry = i.B.x, mode = i.B.y, seed = i.B.z, hollow = i.B.w;
            float wear = i.C.x, dens = i.C.y, role = i.C.z, eye = i.C.w;
            float ageS = i.D.x, flags = i.D.y;
            bool follow = role > 0.5 && role < 1.5;
            float e = abs(side);
            float u = saturate(s / L);
            // written so far, the head edge ragged (never a soft fade)
            float rag = OhValueNoise(float2(s * 6.0 + seed * 13.0, side * 2.5 + seed)) - 0.5;
            clip(lim - s + rag * _HeadRag);
            // the line is a flick: ink further behind the head than the tail length has dried
            if (follow) clip(s - (_Head - _TailLen) + rag * 0.25);
            // distance to the visible end of the stroke (the head for the line, the far end otherwise) and the span the cues live in
            float tipD = follow ? (lim - s) : (L - s);
            float span = clamp(0.2 * L, 0.12, 0.45);
            float tk = 1.0 - saturate(tipD / span);
            bool tipT = fmod(flags, 2.0) > 0.5;
            bool baseT = flags > 1.5;
            // ---- silhouette ----
            float emax = 1.0;
            float fray = _Fray;
            if (tipT && abs(_EdgeKind - 2.0) > 0.5) emax *= lerp(1.0, 0.22, smoothstep(0.0, 1.0, tk));
            if (baseT) emax *= lerp(0.35, 1.0, smoothstep(0.0, 0.22, u));
            float grain = OhFbm(float2(s * 11.0 + seed * 3.7, side * 3.3 + seed * 1.9));
            // body cue 2: stepped plates - the width goes down in stairs toward the end
            if (abs(_EdgeKind - 2.0) < 0.5)
            {
                float step3 = min(3.0, floor(max(tipD, 0.0) / (span * 0.5)));
                emax *= 1.0 - 0.17 * (3.0 - step3);
                fray *= 0.25;
                clip(tipD);
            }
            clip(emax - e + (grain - 0.5) * fray * 0.55);
            // ---- ownership: reading-ink rim (broken), dry pigment band, hollow middle (mine, plan E) ----
            float rimIn = emax * (1.0 - _RimW);
            bool inRim = e > rimIn;
            clip(e - rimIn * 0.55 * hollow);
            float rimN = OhValueNoise(float2(s * 3.1 + seed * 5.0, 1.7 + seed));
            if (inRim) clip(rimN - _Gap);
            if (!inRim) clip(OhValueNoise(float2(s * 24.0 + seed * 2.0, side * 8.0)) - 0.10 - (1.0 - dens) * 0.45);      // dry-brush pinholes; a thin share shows more
            else clip(grain - (1.0 - dens) * 0.35);
            // ---- body cue (plan C: read by the big edge, colour only supports it) ----
            if (_EdgeKind < 0.5)            // wood: torn fibre ends
            {
                float fid = floor((side * 0.5 + 0.5) * 7.0);
                float r = OhHash(float2(fid + seed * 3.0, 4.2));
                clip(tipD - (1.0 - r) * span * 0.9);
                clip(OhValueNoise(float2(side * 16.0 + seed * 7.0, s * 0.6)) - 0.22 * fray);
            }
            else if (_EdgeKind < 1.5)       // fire: saw-tooth end, burnt voids
            {
                clip(tipD - span * 0.5 * OhTri(side * 2.2 + seed));
                if (!inRim) clip(0.86 - OhValueNoise(float2(s * 6.0 + seed * 3.0, side * 4.0)));
            }
            else if (_EdgeKind < 3.5 && _EdgeKind > 2.5)   // metal: straight oblique cut, short cross breaks on one side
            {
                clip(tipD - 0.03 - side * span * 0.25);
                if (side > 0.0) clip(frac(s / 0.4 + seed) - 0.06);
            }
            else if (_EdgeKind > 3.5)       // water: hollow parallel streams inside the band
            {
                if (!inRim) clip(abs(frac(side * 1.5 + 0.5 + seed * 0.3) - 0.5) - 0.10);
            }
            // ---- wear toward the end of a material's life (plan B "만료 예고"): eats the end of the stroke ----
            if (wear > 0.0) clip(OhValueNoise(float2(s * 9.0 + seed * 3.0, side * 5.0)) - wear * (0.2 + 0.8 * tk) * 0.85);
            // ---- the end ----
            if (dry > 0.0)
            {
                float dd = dry * 1.04;
                if (abs(mode - 1.0) < 0.5)
                {
                    if (_EndKind < 0.5)         // wood: the fibres let go one after the other (and travel forward)
                    {
                        float fid = floor((side * 0.5 + 0.5) * 7.0);
                        clip(OhHash(float2(fid + seed * 5.0, 9.1)) * 0.6 + 0.4 - dd);
                    }
                    else if (_EndKind < 1.5) clip(OhValueNoise(float2(s * 7.0 + seed, side * 4.0)) - dd);                        // fire: ash flakes
                    else if (_EndKind < 2.5)    // earth: plates sink one by one
                    {
                        clip(OhHash(float2(floor(s * 5.0), seed)) * 0.5 + 0.5 - dd);
                        clip(i.D.w - (_BaseY - 0.02));
                    }
                    else if (_EndKind < 3.5)    // metal: it snaps in the middle, the halves part and break off
                    {
                        clip(abs(u - 0.5) - dry * 0.3);
                        clip(OhValueNoise(float2(s * 5.0 + seed, side * 3.0)) - (dry - 0.6) * 2.5);
                    }
                    else clip(OhValueNoise(float2(side * 6.0 + seed * 3.0, 1.3)) * 0.6 + 0.4 - dd * 0.98);                    // water: it runs off in streaks
                }
                else if (abs(mode - 2.0) < 0.5) clip(L * (1.0 - dd) - s);                                                         // consumed: folds back to its root
                else if (abs(mode - 3.0) < 0.5) clip(OhValueNoise(float2(s * 13.0 + seed * 3.0, side * 7.0)) - dd);              // child: dries in place, no direction
                else if (abs(mode - 4.0) < 0.5) clip(OhValueNoise(float2(s * 17.0 + seed, side * 6.0)) - dd);                    // replaced: flat erase
                else if (abs(mode - 5.0) < 0.5) clip(OhValueNoise(float2(s * 21.0 + seed, side * 9.0)) - dd);                    // hit-cancelled: broken off
                else if (abs(mode - 6.0) < 0.5) clip(s - dd * L + rag * 0.05);                                                    // cleansed: wiped from the root
            }
            // first person: nothing is cut hard at the camera's near plane (dithered, as the other 308 cutouts)
            clip(OhNearVisibility(eye, _NearClip, _NearFade) - OhDither(i.positionCS.xy) - 0.001);
            return float4(inRim ? 1.0 : 0.0, grain, tk, 0.0);
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
