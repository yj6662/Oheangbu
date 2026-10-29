// D308-153 우 route H (2026-10-29, the user: "반투명 홀로그램 육각형 막에 문양이 박혀있는 모습" -> "돔 + 다양한 문양"; concept kcisa/concepts/hx_1.png): a translucent dome of
// hexagon cells, a KCISA pattern set into every cell. Mesh = Tools/Art/SpellVFX308/restart/u_h_build.py's Goldberg dome (SpellFxSetupH): uv0 = the pattern uv in the cell,
// uv1 = (cell hash, edge 0 centre .. 1 border), uv2 = (pattern index 0..7 of the 4 x 2 atlas, height 0..1).
// Time is driven by the part (_Age seconds since the launch, _Fade 0..1 at the end, _HitPos / _HitAge for the ripple). Cells come up from the ground (outline first,
// then the glass and the pattern), flicker a little, a hit sends a bright ring across the cells, the end dissolves them cell by cell.
// Two passes: back faces (SRPDefaultUnlit, drawn first) then front faces (UniversalForward): the far side of the dome shows through the near side. Transparent queue,
// no depth write, no depth passes (route P r2: the main look's ink-wash / fog post repaints anything that writes depth). Spell VFX may glow (D308-150 V11).
Shader "Oheangbu/SpellFx/HexDome"
{
    Properties
    {
        _Patterns ("Pattern atlas 4 x 2 (white line on black)", 2D) = "black" {}
        _Glass ("Glass tint (rgb) / alpha", Color) = (0.12, 0.19, 0.32, 0.10)
        _Rim ("Fresnel rim colour / alpha", Color) = (0.62, 0.72, 0.86, 0.30)
        _Line ("Cell border colour (HDR)", Color) = (1.35, 1.28, 1.12, 1)
        _Pattern ("Pattern line colour / alpha", Color) = (0.90, 0.88, 0.80, 0.32)
        _EdgeW ("Border width (edge units)", Range(0.01, 0.3)) = 0.03
        _PatternScale ("Pattern size in the cell", Range(0.5, 1.5)) = 0.92
        _Age ("Age (s)", Float) = 0
        _Fade ("Fade 0..1", Range(0, 1)) = 0
        _RiseTime ("Ground-to-top reveal (s)", Float) = 0.55
        _HitPos ("Hit position (world)", Vector) = (0, -1000, 0, 0)
        _HitAge ("Seconds since the hit (<0 none)", Float) = -1
        _BackDim ("Back side brightness", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Patterns); SAMPLER(sampler_Patterns);
        CBUFFER_START(UnityPerMaterial)
            float4 _Patterns_ST;
            half4 _Glass, _Rim, _Line, _Pattern;
            float _EdgeW, _PatternScale, _Age, _Fade, _RiseTime, _HitAge, _BackDim;
            float4 _HitPos;
        CBUFFER_END

        struct Attributes { float4 pos : POSITION; float3 nrm : NORMAL; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
        struct Varyings { float4 pos : SV_POSITION; float3 nrmWS : TEXCOORD0; float3 posWS : TEXCOORD1; float2 uv0 : TEXCOORD2; float2 uv1 : TEXCOORD3; float2 uv2 : TEXCOORD4; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.posWS = TransformObjectToWorld(i.pos.xyz);
            o.pos = TransformWorldToHClip(o.posWS);
            o.nrmWS = TransformObjectToWorldNormal(i.nrm);
            o.uv0 = i.uv0; o.uv1 = i.uv1; o.uv2 = i.uv2;
            return o;
        }

        half4 Shade(Varyings i, float side)
        {
            float hash = i.uv1.x, edge = i.uv1.y, height = i.uv2.y;
            float pat = floor(i.uv2.x + 0.5);
            // ---- reveal: from the ground up, a little shuffled; outline first, then the glass and the pattern ----
            float t0 = height * _RiseTime + hash * 0.12;
            float outline = saturate((_Age - t0) / 0.07);
            float fill = saturate((_Age - t0 - 0.06) / 0.22);
            // ---- end: whole cells dissolve in a shuffled order ----
            float gone = saturate((_Fade * 1.25 - hash) / 0.25);
            outline *= 1 - gone; fill *= 1 - gone;
            if (outline <= 0.001) discard;
            // ---- flicker: a slow shimmer on every cell, a few cells pulse brighter ----
            float flick = 0.9 + 0.1 * sin(_Age * 7.0 + hash * 40.0);
            flick += step(0.86, hash) * 0.35 * saturate(sin(_Age * 11.0 + hash * 17.0));
            // ---- hit ripple: a ring running out across the dome from the struck point ----
            float ripple = 0;
            if (_HitAge >= 0)
            {
                float d = distance(i.posWS, _HitPos.xyz);
                float front = _HitAge * 5.5;
                ripple = exp(-pow((d - front) / 0.45, 2)) * saturate(1 - _HitAge / 0.9);
            }
            // ---- layers ----
            float3 n = normalize(i.nrmWS) * side;
            float3 v = normalize(GetWorldSpaceViewDir(i.posWS));
            float fres = pow(1 - saturate(abs(dot(n, v))), 3);
            float border = smoothstep(1 - _EdgeW * 1.6, 1 - _EdgeW * 0.4, edge);
            float2 puv = (i.uv0 - 0.5) / _PatternScale + 0.5;
            float inTile = step(0, puv.x) * step(puv.x, 1) * step(0, puv.y) * step(puv.y, 1);
            float2 auv = (float2(fmod(pat, 4), 1 - floor(pat / 4)) + saturate(puv)) / float2(4, 2);
            float line_ = SAMPLE_TEXTURE2D(_Patterns, sampler_Patterns, auv).r * inTile * (1 - border);

            float3 col = _Glass.rgb; float a = _Glass.a * fill;
            col = lerp(col, _Rim.rgb, fres); a = max(a, _Rim.a * fres * fill);
            float pa = _Pattern.a * line_ * fill * flick;
            col = lerp(col, _Pattern.rgb * (1 + ripple * 1.5), saturate(pa / max(a + pa, 1e-3)));
            a = a + pa * (1 - a);
            float ba = border * outline * (0.85 + 0.15 * flick) * (1 + ripple);
            col = lerp(col, _Line.rgb * (1 + ripple * 1.2), saturate(ba));
            a = max(a, saturate(ba));
            col *= side > 0 ? 1 : _BackDim; a *= side > 0 ? 1 : 0.75;
            return half4(col, saturate(a));
        }
        ENDHLSL

        Pass
        {
            Name "Back"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return Shade(i, -1); }
            ENDHLSL
        }
        Pass
        {
            Name "Front"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return Shade(i, 1); }
            ENDHLSL
        }
    }
}
