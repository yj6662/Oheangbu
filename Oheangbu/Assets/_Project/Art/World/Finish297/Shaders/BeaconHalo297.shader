// #297 candidate-only 봉수 halo card (SPEC-WORLD-FINISH-297 attraction, TEST): a camera-facing quad on the beacon core whose
// world radius _Radius is clamped to _MinPx.._MaxPx screen pixels, so a 산정 봉수 reads as a small 오행색 point from 150 m to
// 3 km (critical-path attraction always strongest on screen) without growing into a lamp up close (fades in 20-80 m).
// Pass LightMode "BeaconHalo297": never drawn by the regular transparent pass — only by Renderer297's RenderObjects feature
// "BeaconHalo297" at BeforeRenderingPostProcessing, after the CompactMist238 fog (the fog cannot wash it, Bloom still sees it).
// Depth-tested (LEqual) with a pull toward the camera so terrain at the summit does not bite it. Colour = _Color hue,
// peak-normalised x _Intensity (HDR). Blend One OneMinusSrcAlpha: _Cover 0 = purely additive; _Cover > 0 lets the centre cover
// the paper behind it (additive light over bright 한지 loses saturation). Presentation only.
Shader "Oheangbu/Finish297/BeaconHalo"
{
    Properties
    {
        [Header(Light)]
        _Color("Light colour (hue)", Color) = (.95, .36, .18, 1)
        _Intensity("HDR peak intensity", Range(0, 6)) = 1.1
        [ToggleUI] _NormalizePeak("Normalise the colour peak to 1", Float) = 1
        _Chroma("Chroma push (pre-compensates the post desaturation)", Range(1, 2)) = 1.3
        _Cover("Centre coverage (0 = additive)", Range(0, 1)) = 0
        [Header(Size)]
        _Radius("World radius (m)", Float) = 4
        _MinPx("Minimum radius (screen px)", Float) = 10
        _MaxPx("Maximum radius (screen px)", Float) = 24
        _NearFade("Fade in: hidden below x m, full from y m", Vector) = (20, 80, 0, 0)
        _Pull("Pull toward the camera: x m + y * radius", Vector) = (1.5, .5, 0, 0)
        [Header(Ink)]
        _Flicker("Flicker amount", Range(0, .3)) = .06
        _InkRing("Ink ring (dark border)", Range(0, .5)) = 0
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
            Name "BeaconHalo297"
            Tags { "LightMode" = "BeaconHalo297" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex HaloVertex
            #pragma fragment HaloFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity, _NormalizePeak, _Chroma, _Cover, _Radius, _MinPx, _MaxPx, _Flicker, _InkRing;
                float4 _NearFade, _Pull;
            CBUFFER_END
            float4 _OhInkColor;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 q : TEXCOORD0;          // -1..1 across the card
                float2 fade : TEXCOORD1;       // x = near fade, y = flicker
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash11(float p) { p = frac(p * .1031); p *= p + 33.33; p *= p + p; return frac(p); }

            Varyings HaloVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 centreWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 vs = TransformWorldToView(centreWS);
                float d = max(-vs.z, .01);
                // world size of one screen pixel at this depth (perspective; orthographic uses the fixed frustum height)
                float p11 = max(abs(UNITY_MATRIX_P[1][1]), 1e-4);   // abs: the projection is Y-flipped when rendering into a texture
                float perPx = (unity_OrthoParams.w > .5 ? 2.0 : 2.0 * d) / (p11 * _ScreenParams.y);
                float size = clamp(_Radius, _MinPx * perPx, _MaxPx * perPx);
                // pull toward the camera, keeping the same on-screen size
                float pull = min(_Pull.x + _Pull.y * size, d * .5);
                float k = (d - pull) / d;
                vs.z += pull;
                vs.xy += input.positionOS.xy * 2 * size * k;
                o.positionCS = TransformWViewToHClip(vs);
                o.q = input.uv * 2 - 1;
                float phase = Hash11(dot(centreWS, float3(.13, .07, .11))) * 6.2832;
                o.fade = float2(smoothstep(_NearFade.x, _NearFade.y, d), 1 + _Flicker * sin(7.3 * _Time.y + phase));
                return o;
            }

            half4 HaloFragment(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float r2 = dot(i.q, i.q);
                float shape = (exp(-18 * r2) + .35 * exp(-3.5 * r2)) * saturate(1 - r2) * i.fade.x;
                float3 c = _Color.rgb; float l = dot(c, float3(.2126, .7152, .0722));
                c = max(0, l + _Chroma * (c - l));
                if (_NormalizePeak > .5) c /= max(max(c.r, max(c.g, c.b)), 1e-3);
                float3 col = c * _Intensity * i.fade.y * shape;
                // optional ink ring: the paper darkens a little around the light (역번짐), premultiplied toward ink
                float ring = _InkRing * smoothstep(.45, .7, r2) * (1 - smoothstep(.7, .98, r2)) * (1 - saturate(shape)) * i.fade.x;
                float3 ink = any(_OhInkColor.rgb) ? _OhInkColor.rgb : float3(.024, .019, .016);
                return half4(col + ink * ring, saturate(saturate(shape) * _Cover + ring));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
