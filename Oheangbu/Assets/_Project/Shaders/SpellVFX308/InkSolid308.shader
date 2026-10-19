// SPEC-SPELL-VFX-OVERHAUL-308 (D308-52; 5th pass "texture and volume"): the solid parts of a spell drawn as ink WITH BODY.
// Volume comes from ink tone, not from light: three flat washes (light side = the pigment, turning = half ink, far side =
// deep ink) laid by a fixed brush light, their borders broken by the surface grain; ink sunk into the grain (wood fibre,
// stone pits, the brushed faces of a blade, the sag of a water bead); and an ink rim where the form turns from the eye.
// The grain is procedural, built from the mesh UVs (u = angle round the flight axis, v = along it; meshes308.py), so it
// stays on a body that rolls, tumbles or is drawn as a particle mesh. It also bends the shading normal (a height bump).
// No highlight, no emission, LDR, clamped to the ink ceiling. Opaque; writes depth (DepthOnly / DepthNormals).
// _Visibility (set per body by the range seam's property block) eats the body away through the grain as it goes.
// 11.47: _GroundY cuts what is below the ground (summon roots), _Soak fades the pigment in from ink (defaults = no change).
Shader "Oheangbu/SpellVFX308/InkSolid"
{
    Properties
    {
        [MainColor] _BaseColor("Pigment (light side)", Color) = (.3,.4,.3,1)
        _Ink("Ink (far side, grain, rim)", Color) = (.075,.065,.058,1)
        _Shade("Mid wash toward ink", Range(0,1)) = .45
        _Split("Light / shade split", Range(-1,1)) = .1
        _Rim("Rim width", Range(0,1)) = .32
        _Grain("Grain domain (round, round, along) and strength", Vector) = (6,6,1.5,.5)
        _Bump("Grain height (m)", Range(0,.2)) = .03
        _Cavity("Ink in the grain", Range(0,1)) = .5
        _Under("Darker underside (pooled)", Range(0,1)) = 0
        _TipInk("Ink toward the tip (along v)", Range(0,1)) = 0
        _Visibility("Visibility (range seam)", Range(0,1)) = 1
        _NearClip("Nothing nearer than (m)", Float) = .45
        _NearFade("Whole from (m)", Float) = .8
        _GroundY("Cut below (world y; 11.47)", Float) = -10000
        _Soak("Pigment soaked in (0 = ink only; 11.47)", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "../InkCommon308.hlsl"
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 positionWS:TEXCOORD1; float2 uv:TEXCOORD2; };
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor, _Ink; float4 _Grain;
        float _Shade, _Split, _Rim, _Bump, _Cavity, _Under, _TipInk, _Visibility, _NearClip, _NearFade, _GroundY, _Soak;
        CBUFFER_END
        Varyings vert(Attributes v)
        {
            Varyings o; UNITY_SETUP_INSTANCE_ID(v);
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.uv = v.uv;
            return o;
        }
        float Hash3(float3 p) { p = frac(p * .3183099 + .1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
        float Noise3(float3 x)
        {
            float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }
        // the grain, 0..1, seamless round the axis
        float Grain(float2 uv)
        {
            float a = uv.x * 6.2831853;
            float3 q = float3(cos(a) * _Grain.x, sin(a) * _Grain.y, uv.y * _Grain.z);
            return Noise3(q) * .65 + Noise3(q * 2.3 + 7.1) * .35;
        }
        float Cut(Varyings i)
        {
            float eye = -TransformWorldToView(i.positionWS).z;
            clip(OhNearVisibility(eye, _NearClip, _NearFade) - OhDither(i.positionCS.xy) - .001);
            clip(i.positionWS.y - _GroundY);               // 11.47: a body rising out of the ground
            float g = Grain(i.uv);
            clip(g * .9 + _Visibility * 1.15 - 1.0);       // whole at 1; eaten through the grain toward 0
            return g;
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
                float g = Cut(i);
                float3 n = normalize(i.normalWS);
                // the grain as a height: bend the normal by its screen-space slope
                float h = g * _Bump * _Grain.w;
                float3 sx = ddx(i.positionWS), sy = ddy(i.positionWS);
                float3 r1 = cross(sy, n), r2 = cross(n, sx); float det = dot(sx, r1);
                float3 grad = sign(det) * (ddx(h) * r1 + ddy(h) * r2);
                float3 nb = normalize(abs(det) * n - grad);
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                float tone = dot(nb, normalize(float3(-.35, .8, -.3))) * .5 + .5 + (g - .5) * .3 * _Grain.w - saturate(-n.y) * _Under * .35;
                float split = _Split * .5 + .5;
                float3 pig = lerp(_Ink.rgb, _BaseColor.rgb, _Soak);     // 11.47: ink first, the pigment soaks in
                float3 mid = lerp(pig, _Ink.rgb, _Shade), deep = lerp(pig, _Ink.rgb, .85);
                float3 rgb = lerp(deep, mid, smoothstep(split - .2, split - .15, tone));
                rgb = lerp(rgb, pig, smoothstep(split + .08, split + .13, tone));
                rgb = lerp(rgb, _Ink.rgb, smoothstep(.36, .22, g) * _Cavity * _Grain.w);             // ink lying in the grain
                rgb = lerp(rgb, _Ink.rgb, saturate(i.uv.y) * _TipInk);
                rgb = lerp(_Ink.rgb, rgb, step(_Rim + (g - .5) * .16, abs(dot(n, view))));
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
            half4 frag(Varyings i):SV_Target { Cut(i); return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
