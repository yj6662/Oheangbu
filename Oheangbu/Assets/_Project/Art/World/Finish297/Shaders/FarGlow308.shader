// #308 먼 불빛 (SPEC-ATTRACTION-LIGHT-308, TEST): one camera-facing card per man-made attraction light (주막 등롱 · 성황당 촛불).
// Why: at 150-400 m the flame / lantern body is under one pixel (DIAGNOSIS: 0.4 px at 221 m) and the altar LOD2 has no flame at
// all, so the light the re-layout leans on is not drawn. The card keeps a MINIMUM on-screen radius (in 1080p-reference pixels:
// the same fraction of the screen on every render scale) from _NearFade.y outward, fades out by _FarFade.y, and is never drawn
// inside _NearFade.x (the real flame / paper is there).
// It is a thing in the world, not a marker (review F3): between _NearFade.y and _FarFade.x the pixel floor falls from _MinPx to
// _MinPxFar and the strength from 1 to _FarPeak (aerial perspective: colour AND coverage thin together), and the world radius
// never exceeds _MaxWorld — past that distance the card shrinks on screen exactly like any object of that size.
// Canon limits (ART-INK 발광 상한, 「빛은 전구가 아니라 먹이다」):
//   * Blend One OneMinusSrcAlpha with alpha = shape * _Cover: the centre result is  bg * (1 - _Cover) + colour * _Peak.
//     With the profile caps (_Peak * (1 + _Flicker) + (1 - _Cover) <= 1.10) the centre stays under the Bloom threshold 1.15
//     over a background of 1.0. That is NOT "no bloom": URP converts the threshold to linear (1.15 -> 1.36) and has a soft knee
//     of half the threshold, so a centre of 1.086 feeds about 6 % of itself into Bloom (scene Bloom intensity 0.3). Over the
//     lamp's own HDR body (paper 1.8, flame 3) the centre can pass 1.10; at card distances that body is under a pixel.
//     A warm dab of pigment, not a bulb.
//   * ZTest LEqual, ZWrite Off: hills, trunks, eaves and walls hide it like the lamp itself — except within _Pull metres in
//     front of the lamp (the pull that clears the lamp's own body also clears a post or lattice that close). The halo is wider
//     than the lamp (at most _MaxWorld), so a ridge that hides the lamp by less than that leaves the rim of the halo showing.
//   * no sky beam, no screen-space marker: the size is clamped between _MinPx and _MaxPx and the card sits ON the light.
// One pass, no keywords, no textures. Drawn on the VfxAfterFog layer (Renderer297 RenderObjects, event 550), so InkWash297 (450)
// never washes it. Presentation only: nothing reads it back.
Shader "Oheangbu/Finish297/FarGlow308"
{
    Properties
    {
        [Header(Light)]
        _Color("Light colour (hue)", Color) = (1, .58, .26, 1)
        _Peak("Peak (linear, below the Bloom threshold)", Range(0, 1.1)) = .8
        _FarPeak("Strength left at _FarFade.x (aerial perspective)", Range(0, 1)) = .8
        _Cover("Centre coverage (1 = paint over, 0 = additive)", Range(0, 1)) = .85
        _Chroma("Chroma push (pre-compensates the post desaturation)", Range(1, 2)) = 1.1
        _Falloff("Gaussian falloff (core = half maximum)", Range(1, 12)) = 4
        [Header(Size)]
        _Radius("World radius (m)", Float) = .35
        _MinPx("Minimum radius at _NearFade.y (px at _RefHeight)", Float) = 7
        _MinPxFar("Minimum radius at _FarFade.x (px at _RefHeight)", Float) = 4
        _MaxWorld("Maximum world radius (m) - wins over the pixel floor", Float) = 1.6
        _MaxPx("Maximum radius (px at _RefHeight)", Float) = 16
        _RefHeight("Reference screen height (px)", Float) = 1080
        _NearFade("Near: hidden below x m, full from y m", Vector) = (40, 90, 0, 0)
        _FarFade("Far: full to x m, gone at y m", Vector) = (600, 800, 0, 0)
        _Pull("Pull toward the camera (m)", Float) = .6
        [Header(Ink)]
        _Flicker("Flicker amount", Range(0, .2)) = .04
        _FlickerSpeed("Flicker speed", Range(0, 4)) = 1.1
        _InkRing("Ink ring (dark border, 역번짐)", Range(0, .5)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "FarGlow308"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GlowVertex
            #pragma fragment GlowFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Peak, _FarPeak, _Cover, _Chroma, _Falloff, _Radius, _MinPx, _MinPxFar, _MaxPx, _MaxWorld, _RefHeight, _Pull, _Flicker, _FlickerSpeed, _InkRing;
                float4 _NearFade, _FarFade;
            CBUFFER_END
            float4 _OhInkColor;   // palette global (WorldLookDriver), outside the CBUFFER

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 q : TEXCOORD0;      // -1..1 across the card
                float3 fade : TEXCOORD1;   // x = near * far fade, y = flicker, z = air (1 near -> _FarPeak far)
            };

            float Hash11(float p) { p = frac(p * .1031); p *= p + 33.33; p *= p + p; return frac(p); }
            float Noise1(float t) { float i = floor(t), f = frac(t); return lerp(Hash11(i), Hash11(i + 1), f * f * (3 - 2 * f)); }

            Varyings GlowVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                // the card ignores the object's rotation and scale: only its origin matters (the scale just widens the culling bounds)
                float3 centreWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 vs = TransformWorldToView(centreWS);
                float d = max(-vs.z, .01);
                float fade = smoothstep(_NearFade.x, max(_NearFade.y, _NearFade.x + .01), d) * (1 - smoothstep(_FarFade.x, max(_FarFade.y, _FarFade.x + .01), d));
                // world size of one reference pixel at this depth: the real target height cancels out, so the card covers the same
                // fraction of the screen at render scale 0.8 (Mobile) and 0.85 (PC)
                float p11 = max(abs(UNITY_MATRIX_P[1][1]), 1e-4);   // abs: the projection is Y-flipped when rendering into a texture
                float perPx = (unity_OrthoParams.w > .5 ? 2.0 : 2.0 * d) / (p11 * max(_RefHeight, 1));
                // distance law: the pixel floor and the strength fall off between _NearFade.y and _FarFade.x ...
                float far01 = smoothstep(_NearFade.y, max(_FarFade.x, _NearFade.y + .01), d);
                float floorPx = lerp(_MinPx, min(_MinPxFar, _MinPx), far01);
                float size = clamp(_Radius, floorPx * perPx, max(_MaxPx, floorPx) * perPx);
                // ... and the world size is capped: beyond the distance where the floor would need more, the card shrinks on screen
                size = min(size, max(_MaxWorld, _Radius));
                float pull = min(_Pull, d * .25);
                float k = (d - pull) / d;   // same on-screen size after the pull
                vs.z += pull;
                vs.xy += input.positionOS.xy * 2 * size * k;
                o.positionCS = TransformWViewToHClip(vs);
                // nothing to draw: put every vertex on one point outside the clip volume so no fragment runs
                if (fade <= 0) o.positionCS = float4(-2, -2, -2, 1);
                o.q = input.uv * 2 - 1;
                float seed = Hash11(dot(centreWS, float3(.13, .07, .11))) * 31;
                float t = _Time.y * _FlickerSpeed;
                // slightly irregular breathing, never a regular pulse (same recipe as InkBeacon297)
                float breath = 1 + _Flicker * ((Noise1(t * 1.9 + seed) - .5) * 1.4 + (Noise1(t * 5.3 + seed * 2.1) - .5) * .6);
                o.fade = float3(fade, breath, lerp(1, saturate(_FarPeak), far01));
                return o;
            }

            half4 GlowFragment(Varyings i) : SV_Target
            {
                float r2 = dot(i.q, i.q);
                float shape = exp(-_Falloff * r2) * saturate(1 - r2) * i.fade.x * i.fade.z;
                float3 c = _Color.rgb; float l = dot(c, float3(.2126, .7152, .0722));
                c = max(0, l + _Chroma * (c - l));
                c /= max(max(c.r, max(c.g, c.b)), 1e-3);
                float3 col = c * _Peak * min(i.fade.y, 1 + _Flicker) * shape;
                // optional ink ring: the paper darkens a little around the light (역번짐), premultiplied toward ink
                float ring = _InkRing * smoothstep(.35, .6, r2) * (1 - smoothstep(.6, .98, r2)) * i.fade.x;
                float3 ink = any(_OhInkColor.rgb) ? _OhInkColor.rgb : float3(.024, .019, .016);
                return half4(col + ink * ring, saturate(shape * _Cover + ring));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
