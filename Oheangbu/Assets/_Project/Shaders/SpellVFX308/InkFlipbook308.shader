// SPEC-SPELL-VFX-OVERHAUL-308 (D308-52): baked ink flipbooks (Blender forms -> stylise308.py).
// Sheet channels: R heat (fire only) - G erosion field - B halo (not used by this cutout shader) - A coverage.
// Ink, not light, and not smoke: the stroke is a hard-edged CUTOUT. A stroke that fades does not go transparent: the vertex
// alpha raises a threshold through the erosion field, so it THINS and breaks into beads (vertex alpha 1 = whole, 0 = gone).
// Cutout also writes depth (DepthOnly / DepthNormals passes), so the full-screen fog and the ink-wash post can treat the
// stroke as the near thing it is (an alpha-blended sprite has no depth of its own), and there is no blended overdraw.
// Whether the blended version really washed out was NOT isolated: the first tests were confounded by a burst-placement bug.
// The frame comes from the particle system's Texture Sheet Animation (stepped cels).
// _Glow is the momentary birth glow (D308-10c): a lift of the pigment, capped at OH_GLOW_CEILING_OUT, used only by the
// two-cel birth layers. Heat is paint, not emission: a warm colour under the same LDR ceiling. No HDR output anywhere.
Shader "Oheangbu/SpellVFX308/InkFlipbook"
{
    Properties
    {
        [MainTexture] _MainTex("Sheet (R heat, G field, B halo, A coverage)", 2D) = "black" {}
        _Ink("Core ink (a = how far the core sinks to it)", Color) = (.075,.065,.058,.85)
        _CoreLo("Core starts at field", Range(0,1)) = .1
        _CoreHi("Core full at field", Range(0,1)) = .4
        _Soft("Erosion edge softness", Range(.005,.4)) = .07
        _ErodeSteps("Erosion cels (0 = smooth)", Float) = 0
        _HeatColor("Heat colour (LDR)", Color) = (.66,.24,.05,1)
        _Heat("Heat amount", Range(0,2)) = 0
        _Glow("Birth glow (<= .35)", Range(0,.35)) = 0
        _NearClip("Nothing nearer than (m)", Float) = .45
        _NearFade("Whole from (m)", Float) = .9
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "../InkCommon308.hlsl"
        struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; float eye:TEXCOORD1; };
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST; half4 _Ink, _HeatColor;
        float _CoreLo, _CoreHi, _Soft, _ErodeSteps, _Heat, _Glow, _NearClip, _NearFade;
        CBUFFER_END
        Varyings vert(Attributes v)
        {
            Varyings o; UNITY_SETUP_INSTANCE_ID(v);
            float3 ws = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(ws);
            o.color = v.color; o.uv = v.uv; o.eye = -TransformWorldToView(ws).z;
            return o;
        }
        // samples the sheet and cuts the stroke out; returns the sample for shading
        half4 Stroke(Varyings i)
        {
            half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
            float erode = 1.0 - saturate(i.color.a);
            if (_ErodeSteps > .5) erode = floor(erode * _ErodeSteps + .5) / _ErodeSteps;
            float thr = -0.01 + 1.03 * erode;
            float a = t.a * smoothstep(thr - _Soft, thr, t.g);
            clip(a - .5);
            clip(OhNearVisibility(i.eye, _NearClip, _NearFade) - OhDither(i.positionCS.xy) - .001);
            return t;
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
            half4 frag(Varyings i):SV_Target
            {
                half4 t = Stroke(i);
                float3 rgb = lerp(i.color.rgb, _Ink.rgb, smoothstep(_CoreLo, _CoreHi, t.g) * _Ink.a);
                rgb = lerp(rgb, _HeatColor.rgb, smoothstep(.3, .75, t.r * _Heat));   // only the hot heart is warm; the rim stays ink
                rgb = min(rgb, OH_INK_CEILING_OUT);
                if (_Glow > 0.0) rgb = max(rgb, min(rgb + min(_Glow, OH_GLOW_MAX) * (.5 + i.color.rgb), OH_GLOW_CEILING_OUT));
                return half4(rgb, 1);
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
            half4 frag(Varyings i):SV_Target { Stroke(i); return i.positionCS.z; }
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
            half4 frag(Varyings i):SV_Target { Stroke(i); return half4(-GetViewForwardDir(), 0); }
            ENDHLSL
        }
    }
}
