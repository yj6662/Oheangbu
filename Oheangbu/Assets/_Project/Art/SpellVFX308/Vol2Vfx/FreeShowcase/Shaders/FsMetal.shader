// 2026-10-27 common material pass (showcase120_plan.md step 2): showcase steel (swords, blades, chakrams, stakes, shards).
// Batches 1 - 4 read pink / lavender on the red 적로 ground: URP Lit steel is opaque, so InkWash297 (450, BeforeRenderingTransparents) re-grades it
// (ink..paper tone mapping + warm highlights) like the world. This steel is unlit and drawn in the TRANSPARENT queue (opaque-looking: no blend,
// depth written) - it is drawn after the wash, and VfxAfterFog308 routes it to the after-fog layer, so its silver is the one written here:
//  * a procedural studio environment seen in the reflection (dark ground, cool horizon, a bright softbox band, a few razor streak lights),
//  * brushed lanes (the steel texture) stretching the reflection, a dark graphite body,
//  * fresnel: a white-gold edge glint band, then a thin ink line at the very silhouette (so it does not float un-outlined),
//  * a sharp highlight from the main light, a glint that sweeps along the blade now and then (Phantom Blade principle 4, light only),
//  * _Hot: a warm edge (hot steel) / _Accent: a faint self-light colour.
// Vertex colour alpha < 1 (a mesh particle fading out) dithers the piece away (no blending). Presentation only.
Shader "Oheangbu/FreeShowcase/Metal"
{
    Properties
    {
        _BaseMap ("Brushed lanes", 2D) = "white" {}
        _Tint ("Body tint", Color) = (1, 1, 1, 1)
        _Bright ("Reflection brightness", Float) = 1
        _Accent ("Self-light colour (HDR)", Color) = (0, 0, 0, 0)
        _Hot ("Hot edge (0 cold steel .. 1 white-hot gold)", Range(0, 1)) = 0
        _EdgeGlint ("Edge glint strength", Float) = 1
        _InkRim ("Ink line at the silhouette", Range(0, 1)) = 0.55
        _Sweep ("Sweeping glint strength", Float) = 1
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
                float _Bright, _Hot, _EdgeGlint, _InkRim, _Sweep;
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

            // the studio the steel reflects (world directions)
            float3 Studio(float3 r)
            {
                float h = r.y;
                float3 ground = float3(0.035, 0.04, 0.05);
                float3 horizon = float3(0.2, 0.21, 0.235);      // r2: a little less blue (r1 read cold blue-white)
                float3 sky = float3(0.4, 0.42, 0.46);      // r3: r2 big blades read flat white (a flat face mirrors one sky value)
                float3 c = lerp(ground, horizon, smoothstep(-0.35, 0.02, h));
                c = lerp(c, sky, smoothstep(0.25, 0.95, h));
                c += float3(1.05, 1.08, 1.12) * exp(-pow((h - 0.22) / 0.07, 2.0)) * 1.4;          // the softbox band
                c *= 1.0 - 0.55 * exp(-pow((h - 0.05) / 0.05, 2.0));                              // a dark gap under it (the chrome read)
                float az = atan2(r.z, r.x);
                float streak = pow(saturate(cos(az - 0.6)), 600.0) + pow(saturate(cos(az + 2.1)), 900.0) + 0.6 * pow(saturate(cos(az - 2.9)), 400.0);
                c += float3(1.6, 1.6, 1.65) * streak * smoothstep(-0.15, 0.35, h);
                return c;
            }

            float Dither(float2 px) { return frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715)))); }

            half4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                if (i.color.a < 0.999 && i.color.a < Dither(i.positionCS.xy)) discard;
                float3 N = normalize(i.normalWS); if (!front) N = -N;
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndv = saturate(dot(N, V));
                float3 R = reflect(-V, N);
                float lanes = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).r;
                // brushed metal: a reflection that is a little smeared along the lanes
                float3 env = Studio(R) * (0.35 + 0.85 * lanes);
                float3 bodyC = float3(0.05, 0.055, 0.065);                                         // dark graphite between the reflections
                float3 col = bodyC + env * _Bright * _Tint.rgb * 0.95;
                // the main light: a sharp highlight
                Light L = GetMainLight();
                float3 H = normalize(L.direction + V);
                col += L.color * pow(saturate(dot(N, H)), 220.0) * 2.2;
                // fresnel: white-gold glint band, then the thin ink line at the very silhouette
                float f = 1.0 - ndv;
                float3 edgeC = lerp(float3(1.0, 0.97, 0.88), float3(1.4, 0.95, 0.45), _Hot);
                col += edgeC * smoothstep(0.55, 0.82, f) * (1.0 - smoothstep(0.86, 0.95, f)) * 0.9 * _EdgeGlint;
                col *= 1.0 - _InkRim * smoothstep(0.9, 0.985, f);
                // a glint sweeping along the piece every ~1.7 s (object space: the blades run along +Z)
                float along = dot(i.positionOS, float3(0.25, 0.35, 1.0));
                float ph = frac(_Time.y * 0.6 + dot(i.positionWS - i.positionOS, float3(0.13, 0.07, 0.11)));
                float sweep = exp(-pow((frac(along * 0.5) - ph) / 0.035, 2.0)) * smoothstep(0.25, 0.7, f + 0.3);
                col += edgeC * sweep * 1.6 * _Sweep;
                // hot steel / self-light
                col += _Accent.rgb + edgeC * _Hot * 0.35 * (0.4 + f);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
