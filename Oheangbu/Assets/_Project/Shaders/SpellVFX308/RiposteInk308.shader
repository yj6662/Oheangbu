// D308-148 (앞잡 = 일필휘지 한 획, step 5): the one huge stroke of the riposte, drawn as matte ink.
// Mesh = RiposteStroke308Mesh (RibbonMeshBuilder, planar, no smoothing): UV0.x = arc length / total length (0 = wet start, 1 = dry tail),
// UV0.y = across the width (0 .. 1, centre .5), vertex alpha = ink density (1 loaded, lower = starved).
// OPAQUE CUTOUT, double sided, depth tested, writes depth (DepthOnly / DepthNormals share the cut). No emission, no specular, no
// additive, no alpha blend, no transparency fade: every pixel is the flat ink colour or it is cut away. Colour is clamped to the ink
// LDR ceiling. The rules of the look checklist (s_final_verdict.md):
//   * written in brush order: _RevealS is the arc fraction written so far; everything beyond it is cut. The head edge is ragged by
//     the bristle mask (_HeadRag), never a soft fade. The whole stroke is always built, so the textures never slide while it is written.
//   * dry brush: the bristle mask (R, tiles along the stroke) is a coverage of long streaks; the bright grain is the background
//     showing through a gap, never a light colour. Starved ink (vertex alpha) and the tail (_TailDry) widen the gaps from the edges in.
//   * drying: _Dry raises a threshold through the crack field (R: 0 on the cracks .. 1 at the centre of each flake). The cracks open first,
//     the flakes shrink after, the tail dries before the head. The stroke keeps its shape while it breaks - it is never thinned
//     into a translucent film or a web. At _Dry = 1 nothing is left (the pieces that fell are the flake particles).
// Cannot be compiled offline: written conservatively after InkFlipbook308 / InkSolid308 (same includes and pass skeleton).
Shader "Oheangbu/SpellVFX308/RiposteInk"
{
    Properties
    {
        _BaseColor("Wet ink", Color) = (.055,.05,.047,1)
        _DryColor("Dry grey-black", Color) = (.2,.185,.17,1)
        [NoScaleOffset] _Bristle("Bristle mask (R: ink; tiles along the stroke)", 2D) = "white" {}
        [NoScaleOffset] _Crack("Crack field (R: 0 on a crack .. 1 at a flake centre)", 2D) = "white" {}
        _Tiling("Bristle repeats (along, across)", Vector) = (3,1,0,0)
        _CrackTiling("Crack repeats (along, across)", Vector) = (9,1.6,0,0)
        _RevealS("Written up to (arc fraction)", Range(0,1)) = 1
        _HeadRag("Ragged head", Range(0,.2)) = .05
        _Dry("Drying (0 wet .. 1 gone)", Range(0,1)) = 0
        _TailDry("How dry the tail runs", Range(0,1)) = .55
        _Seed("Seed (shifts the textures per fire)", Float) = 0
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
        struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; float eye:TEXCOORD1; };
        TEXTURE2D(_Bristle); SAMPLER(sampler_Bristle);
        TEXTURE2D(_Crack); SAMPLER(sampler_Crack);
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor, _DryColor; float4 _Tiling, _CrackTiling;
        float _RevealS, _HeadRag, _Dry, _TailDry, _Seed, _NearClip, _NearFade;
        CBUFFER_END
        Varyings vert(Attributes v)
        {
            Varyings o; UNITY_SETUP_INSTANCE_ID(v);
            float3 ws = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(ws);
            o.color = v.color; o.uv = v.uv; o.eye = -TransformWorldToView(ws).z;
            return o;
        }
        // cuts the stroke out; returns (bristle, starve) for the colour
        float2 Cut(Varyings i)
        {
            float u = i.uv.x, v = i.uv.y;
            float bristle = SAMPLE_TEXTURE2D(_Bristle, sampler_Bristle, float2(u * _Tiling.x + _Seed * .37, v * _Tiling.y)).r;
            // written so far; the head edge is ragged by the bristles (no soft edge)
            clip(_RevealS + (bristle - .5) * _HeadRag - u);
            // dry-brush coverage: the edges go first (across = 1 at the centre, 0 at the rim), then the starved and the tail
            float across = 1.0 - abs(v * 2.0 - 1.0);
            float starve = saturate(1.0 - i.color.a) + _TailDry * smoothstep(.5, 1.0, u);
            clip(bristle - starve * .8 + (across - .3) * .9 - .35);
            // drying through the crack field; the tail dries before the head
            float dryLocal = saturate(_Dry * 1.35 - (1.0 - u) * .35);
            float crack = SAMPLE_TEXTURE2D(_Crack, sampler_Crack, float2(u * _CrackTiling.x + _Seed * .19, v * _CrackTiling.y + _Seed * .07)).r;
            // (only while drying: a wet stroke has no cracks - the texture's 0 lines must not cut a fresh stroke)
            clip(dryLocal > 0.0 ? crack - dryLocal - .001 : 1.0);
            // first person: nothing is cut hard at the camera's near plane (dithered, as the other 308 cutouts)
            clip(OhNearVisibility(i.eye, _NearClip, _NearFade) - OhDither(i.positionCS.xy) - .001);
            return float2(bristle, starve);
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
                float2 c = Cut(i);
                float dried = saturate(_Dry * 1.6 + c.y * .45);
                float3 rgb = lerp(_BaseColor.rgb, _DryColor.rgb, dried) * (.92 + .16 * c.x);
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
            half4 frag(Varyings i):SV_Target { Cut(i); return i.positionCS.z; }
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
            half4 frag(Varyings i):SV_Target { Cut(i); return half4(-GetViewForwardDir(), 0); }
            ENDHLSL
        }
    }
}
