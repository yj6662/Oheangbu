// 2026-10-27 common material pass (showcase120_plan.md step 2): showcase steel (swords, blades, chakrams, stakes, shards).
// Batches 1 - 4 read pink / lavender on the red 적로 ground: URP Lit steel is opaque, so InkWash297 (450, BeforeRenderingTransparents) re-grades it
// (ink..paper tone mapping + warm highlights) like the world. This steel is unlit and drawn in the TRANSPARENT queue (opaque-looking: no blend,
// depth written) - it is drawn after the wash, and VfxAfterFog308 routes it to the after-fog layer, so its colour is the one written here.
//
// 2026-10-27 weak-points pass (batches 6 - 8 left "a light grey / white steel" as their common limit; the user: "강철 회색 ... 부터 다듬어 줘"):
// the cause, measured with the diagnostic switch below (_FsMetalDiag) on 사 and 금>토 (Docs/Research/vfx120/free_showcase_ledger.md "공통 약점 다듬기"):
//  * thin spikes / blades seen from the shoulder (금>토, mean luma of the steel pixels 0.51): the reflection alone was DARK (0.14 - navy, the look we want);
//    the white came from the fresnel glint band (f .55 - .86 covers most of a thin blade seen edge-on: without it 0.38, p90 0.78 -> 0.45) and the _Hot warm
//    term, a flat lift over the WHOLE surface (0.4 + f), plus HDR into bloom (clamped to 1: 0.41);
//  * broad flat faces (사's sword): the reflection itself was light grey - a flat face mirrors ONE value of a bright studio (a 0.40 - 0.46 sky and a broad
//    1.5 softbox band right where a blade seen from the shoulder reflects, R.y 0.1 - 0.5), times _Bright 1.0 - 1.7;
//  * not the ink wash (the steel is drawn after it) and not the tone mapping alone. The steel now reflects a DARK ink world
//    (r3 of this pass, measured again with the switch: a body of linear 0.04 and a broad sun sheen with the sun's HDR colour still displayed the whole
//    blade as slate grey after sRGB + the look post - both cut; a cold accent self-light is kept faint):
//  * a navy-black body (남흑) and a dark reflection (ground / horizon / sky 0.01 - 0.09), warped by a slow normal wobble + the brushed lanes so a flat face
//    shows a gradient, not one value;
//  * only razor-thin hot lines are bright: a thin band, three razor streak lights, the sun's pin highlight, and the facet edges of the mesh (normal
//    discontinuities: the edge / ridge of a blade catches a hairline of light) - Phantom Blade principle 2 (날선 형태);
//  * a thin gold / white-gold (platinum) rim then the ink line at the silhouette, so the form stands on the dark ground - principle 1 (one accent);
//  * _Bright scales the hot lines only (never the base): the callers' 0.45 - 1.7 keep their meaning (a brighter mirror = more light on its edges).
// Vertex colour alpha < 1 (a mesh particle fading out) dithers the piece away (no blending). Presentation only.
Shader "Oheangbu/FreeShowcase/Metal"
{
    Properties
    {
        _BaseMap ("Brushed lanes", 2D) = "white" {}
        _Tint ("Body tint", Color) = (1, 1, 1, 1)
        _Bright ("Hot-line brightness", Float) = 1
        _Accent ("Self-light colour (HDR)", Color) = (0, 0, 0, 0)
        _Hot ("Hot edge (0 cold steel .. 1 white-hot gold)", Range(0, 1)) = 0
        _EdgeGlint ("Rim strength", Float) = 1
        _InkRim ("Ink line at the silhouette", Range(0, 1)) = 0.55
        _Sweep ("Sweeping glint strength", Float) = 1
        // 2026-10-27 ink-world pass: 1 = silver two-tone chrome (paper-silver above a sharp horizon, ink below), 0 = the dark ink world of the weak-points pass
        _Chrome ("Silver two-tone chrome", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "FsMetal"
            Tags { "LightMode"="UniversalForward" }
            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Tint, _Accent;
                float _Bright, _Hot, _EdgeGlint, _InkRim, _Sweep, _Chrome;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float4 color : COLOR; float3 positionOS : TEXCOORD3; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            // the dark ink world the steel reflects (world directions): base = the dim surroundings, hot = the razor lights only
            void Studio(float3 r, out float3 baseC, out float hot)
            {
                float h = r.y;
                float3 ground = float3(0.012, 0.013, 0.02);
                float3 horizon = float3(0.03, 0.034, 0.048);
                float3 sky = float3(0.05, 0.055, 0.072);
                baseC = lerp(ground, horizon, smoothstep(-0.3, 0.02, h));
                baseC = lerp(baseC, sky, smoothstep(0.2, 0.9, h));
                // 2026-10-27 ink-world pass: a two-tone chrome. Above a SHARP horizon the paper / silver sky (brightest in a band just over the horizon), below it the ink
                // world; a blade face turning through the horizon flips between silver and ink. (The weak-points pass lost the grey by going dark everywhere; the grey
                // came from "no contrast", not from brightness - the concept's steel is silver blades on black ink.)
                float up = smoothstep(-0.015, 0.025, h);
                float skyTone = lerp(0.80, 0.50, smoothstep(0.02, 0.85, h)) * (0.8 + 0.2 * smoothstep(0.0, 0.1, h));
                float3 silver = skyTone * float3(0.93, 0.935, 0.95);
                float3 inkGround = lerp(float3(0.014, 0.014, 0.018), float3(0.05, 0.05, 0.058), smoothstep(-0.9, -0.03, h));
                baseC = lerp(baseC, lerp(inkGround, silver, up), _Chrome);
                float band = exp(-pow((h - 0.2) / 0.024, 2.0));                     // one thin hot band (was a broad softbox)
                float az = atan2(r.z, r.x);
                float streak = pow(saturate(cos(az - 0.6)), 1600.0) + pow(saturate(cos(az + 2.1)), 2400.0) + 0.6 * pow(saturate(cos(az - 2.9)), 1200.0);
                hot = band * 1.6 + streak * 2.2 * smoothstep(-0.1, 0.4, h);
            }

            float Dither(float2 px) { return frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715)))); }
            // a diagnostic switch (global, never set by a look; 0 = the normal steel): 1 = the reflection term only, 2 = without the reflection,
            // 3 = the output clamped to 1 (no HDR into bloom), 4 = hidden (the mask of the steel), 5 = without the rim / sweep / facet edges.
            float _FsMetalDiag;

            half4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                int diag = (int)round(_FsMetalDiag);
                if (diag == 4) discard;
                if (i.color.a < 0.999 && i.color.a < Dither(i.positionCS.xy)) discard;
                float3 N0 = normalize(i.normalWS); if (!front) N0 = -N0;
                // facet edges: where the flat-shaded normal jumps (a blade's edge / ridge) - a hairline of light
                float facet = saturate(length(fwidth(N0)) * 3.0 - 0.5);      // r2: only real edges (r1 lit small far blades whole)
                float lanes = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).r;
                // a slow wobble + the brushed lanes bend the reflection: a flat face shows a gradient, not one mirrored value
                float3 p = i.positionWS * 2.3;
                float3 wob = float3(sin(p.y * 1.7 + p.z * 0.9), sin(p.z * 1.3 + p.x * 1.1), sin(p.x * 1.5 + p.y * 0.7));
                float3 N = normalize(N0 + wob * 0.16 + (lanes - 0.5) * 0.22 * float3(0.3, 1.0, 0.5));
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndv = saturate(dot(N0, V));
                float3 R = reflect(-V, N);
                float3 baseC; float hot;
                Studio(R, baseC, hot);
                float3 bodyC = float3(0.004, 0.006, 0.013);      // 남흑: navy-black between the lights (r3: linear 0.04 already displays as slate grey after sRGB + the look post)
                float3 refl = baseC * _Tint.rgb * (0.6 + 0.4 * lanes) + float3(1.0, 0.98, 0.94) * hot * _Bright * _Tint.rgb * (0.25 + 0.95 * lanes);
                if (diag == 1) return half4(refl, 1.0);
                float3 col = bodyC + (diag == 2 ? 0 : refl);
                // the sun: a pin highlight (razor), a faint sheen
                Light L = GetMainLight();
                float3 H = normalize(L.direction + V);
                float nh = saturate(dot(N, H));
                col += L.color * pow(nh, 700.0) * 3.2;      // r3: no broad sheen (with the sun's HDR colour it lifted the whole body to slate grey - diag 2 / 5)
                // rim: a thin gold / platinum line, then the ink line at the very silhouette; the facet edges in the same light
                float f = 1.0 - ndv;
                float3 rimC = lerp(float3(1.35, 1.02, 0.55), float3(1.6, 0.95, 0.42), _Hot);      // gold / platinum-gold
                float edgeK = diag == 5 ? 0.0 : 1.0;
                float rim = smoothstep(0.8, 0.93, f) * (1.0 - smoothstep(0.95, 0.985, f));
                col += rimC * rim * 0.95 * _EdgeGlint * edgeK;
                col += rimC * facet * 0.55 * saturate(0.4 + 0.6 * _Bright) * edgeK;
                col *= 1.0 - _InkRim * smoothstep(0.93, 0.99, f);
                // a glint sweeping along the piece every ~1.7 s (object space: the blades run along +Z)
                float along = dot(i.positionOS, float3(0.25, 0.35, 1.0));
                float ph = frac(_Time.y * 0.6 + dot(i.positionWS - i.positionOS, float3(0.13, 0.07, 0.11)));
                float sweep = exp(-pow((frac(along * 0.5) - ph) / 0.03, 2.0)) * smoothstep(0.25, 0.7, f + 0.3);
                col += rimC * sweep * 1.5 * _Sweep * edgeK;
                // hot steel / self-light
                col += _Accent.rgb * lerp(0.3, 1.0, _Hot) + rimC * _Hot * (0.03 + 0.45 * f * f * f);      // r3: a cold faint self-light stays faint (it lifted the body); hot steel keeps its glow      // the hot edge gathers at the silhouette (was a flat lift over the whole surface)
                if (diag == 3) col = saturate(col);
                return half4(max(col, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
}
