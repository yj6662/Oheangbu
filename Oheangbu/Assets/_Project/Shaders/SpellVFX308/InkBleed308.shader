// SPEC-SPELL-VFX-OVERHAUL-308 second look (D308-53 (3) "softer, bleeding ink"): ink dropped in water.
// Sheet (bleed308.py, After Effects pass): R heat (fire only) - G wet heart - B pooled rim (the dark edge where the ink
// gathers) - A ink density (soft wash, strands, brush fibre). Alpha blended, soft edges.
// The stroke fades by PALING (vertex alpha) while the particle swells - it does not thin or break as the cutout look did.
// Frames are interpolated: the renderer sends UV2 + AnimBlend (custom vertex streams Position, Color, UV, UV2, AnimBlend).
// Ink, not light: no emission, no HDR; the colour is clamped to the ink ceiling. The one light is the birth glow
// (_Radial 1): a small soft round lift of the pigment for two cels, capped at OH_GLOW_CEILING_OUT (D308-10c) - soft to
// nothing at its rim, so it reads as a faint light and not as a flat pale shape. Heat is a warm PAINT tone under the ceiling.
// Fades out softly against geometry (_SoftDepth) and toward the eye (nothing nearer than _NearClip).
// D308-125 5 (c) _Paint: the sheet is a PAINTED colour sheet (fire308.py *_vfx: sRGB colour + alpha) instead of the channel
// sheet - its colour times the vertex colour (x _Tint), greyed first by _Desat, still under the ink ceiling.
//   _Paint 1: the particle system frames it (texture sheet animation, Blend streams as above).
//   _Paint 2: framed here - a sheet of variants, each _Grid.z cels laid row after row (_Grid.x columns, _Grid.y rows): the
//   streams Position, Color, UV, Custom1.xy (variant, first cel), AgePercent; the variant's cels from the first one play once
//   over the particle's life, blended cel to cel. (Texture sheet animation cannot: one variant spans two rows of the brush sheet.)
Shader "Oheangbu/SpellVFX308/InkBleed"
{
    Properties
    {
        [MainTexture] _MainTex("Sheet (R heat, G heart, A density)", 2D) = "black" {}
        _Ink("Heart ink (a = how far the heart sinks to it)", Color) = (.075,.065,.058,.9)
        _HeartLo("Heart starts", Range(0,1)) = .2
        _HeartHi("Heart full", Range(0,1)) = .7
        _Rim("Pooled rim (darkens and thickens the edge of the wash)", Range(0,2)) = 1
        _Tint("Tint (times the vertex colour; for meshes, which carry none)", Color) = (1,1,1,1)
        _Opacity("Opacity", Range(0,1)) = 1
        _HeatColor("Heat colour (LDR)", Color) = (.66,.24,.05,1)
        _Heat("Heat amount", Range(0,2)) = 0
        _Glow("Birth glow (<= .35)", Range(0,.35)) = 0
        [Toggle] _Radial("Round glow instead of the sheet", Float) = 0
        _SoftDepth("Fade near geometry (m, 0 = off)", Float) = .5
        _NearClip("Nothing nearer than (m)", Float) = .45
        _NearFade("Whole from (m)", Float) = 1.2
        _SoakInk("Soak: first colour (ink) - D308-84 the body forms black, then the pigment soaks in", Color) = (.05,.047,.045,1)
        _SoakFrom("Soak starts (particle age 0-1)", Range(0,1)) = 0
        _SoakTo("Soak full (particle age 0-1, 0 = off)", Range(0,1)) = 0
        _Paint("Painted colour sheet (0 = channel sheet, 1 = framed by the system, 2 = framed here)", Float) = 0
        _Grid("Paint 2: columns, rows, cels per variant", Vector) = (8,8,16,0)
        _Desat("Paint: grey the sheet before the tint (1 = ash)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "../InkCommon308.hlsl"
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float4 uv:TEXCOORD0; float2 blend:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float4 uv:TEXCOORD0; float3 misc:TEXCOORD1; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; half4 _Ink, _HeatColor, _Tint, _SoakInk; float4 _Grid;
            float _HeartLo, _HeartHi, _Rim, _Opacity, _Heat, _Glow, _Radial, _SoftDepth, _NearClip, _NearFade, _SoakFrom, _SoakTo, _Paint, _Desat;
            CBUFFER_END
            // _Paint 2: the sheet uv of cel k of a variant (the image's first row is at the top)
            float2 PaintCel(float2 uv, float variant, float k)
            {
                float index = variant * _Grid.z + k;
                float row = floor(index / _Grid.x), col = index - row * _Grid.x;
                return float2((col + uv.x) / _Grid.x, (_Grid.y - 1.0 - row + uv.y) / _Grid.y);
            }
            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.color = v.color * _Tint; o.uv = v.uv * _MainTex_ST.xyxy + _MainTex_ST.zwzw; o.misc = float3(v.blend.x, -TransformWorldToView(ws).z, v.blend.y);   // y of TEXCOORD1 = AgePercent (0 where the stream is absent)
                return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                float3 rgb; float a;
                if (_Radial > .5)
                {
                    float2 p = i.uv.xy * 2.0 - 1.0;
                    float k = saturate(1.0 - dot(p, p));
                    a = k * k * i.color.a;
                    rgb = min(i.color.rgb + min(_Glow, OH_GLOW_MAX) * (.5 + i.color.rgb), OH_GLOW_CEILING_OUT);
                }
                else if (_Paint > .5)
                {
                    half4 t;
                    if (_Paint > 1.5)
                    {
                        float2 q = saturate(i.uv.xy);
                        float cels = max(_Grid.z, 1.0), first = clamp(i.uv.w, 0.0, cels - 1.0);
                        float f = min(first + saturate(i.misc.x) * (cels - first), cels - .001);
                        float k0 = floor(f), k1 = min(k0 + 1.0, cels - 1.0), variant = floor(max(i.uv.z, 0.0));
                        t = lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, PaintCel(q, variant, k0)), SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, PaintCel(q, variant, k1)), f - k0);
                    }
                    else t = lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy), SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.zw), saturate(i.misc.x));
                    float3 c = lerp(t.rgb, dot(t.rgb, float3(.2126, .7152, .0722)).xxx, saturate(_Desat));
                    rgb = min(c * i.color.rgb, OH_INK_CEILING_OUT);   // paint, not light
                    a = t.a * i.color.a;
                }
                else
                {
                    half4 t = lerp(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy), SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.zw), saturate(i.misc.x));
                    float rim = saturate(t.b * _Rim);
                    a = saturate(t.a + rim * .5) * i.color.a;
                    rgb = lerp(i.color.rgb, _Ink.rgb, saturate(smoothstep(_HeartLo, _HeartHi, t.g) * _Ink.a + rim * .8));
                    rgb = lerp(rgb, _HeatColor.rgb, smoothstep(.3, .75, t.r * _Heat));
                    if (_SoakTo > 0.0) rgb = lerp(_SoakInk.rgb, rgb, smoothstep(_SoakFrom, _SoakTo, i.misc.z));   // colour only, no light
                    rgb = min(rgb, OH_INK_CEILING_OUT);
                }
                float eye = i.misc.y;
                a *= smoothstep(_NearClip, _NearFade, eye);
                if (_SoftDepth > 0.0)
                {
                    float2 screenUV = i.positionCS.xy / _ScaledScreenParams.xy;
                    float scene = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    a *= saturate((scene - eye) / _SoftDepth);
                }
                return half4(rgb, a * _Opacity);
            }
            ENDHLSL
        }
    }
}
