// D308-153 우 route H (2026-10-29, the user: "반투명 홀로그램 육각형 막에 문양이 박혀있는 모습" -> "돔 + 다양한 문양"; concept kcisa/concepts/hx_1.png): a translucent dome of
// hexagon cells, a KCISA pattern set into every cell. Mesh = Tools/Art/SpellVFX308/restart/u_h_build.py's Goldberg dome (SpellFxSetupH): uv0 = the pattern uv in the cell,
// uv1 = (cell hash, edge 0 centre .. 1 border), uv2 = (pattern index 0..7 of the 4 x 2 atlas, height 0..1). Object space: unit dome on the ground (y = 0 is the ground).
// Time is driven by the part (_Age seconds since the launch, _Fade 0..1 at the end, _HitPos / _HitAge for the ripple). Look only.
// Round 2 (Fable r1 51/100): pale sky-glass instead of the muddy tint, white line patterns clipped to a circle in the cell, the far side's lines faint (no double outline),
// a clean height wipe (up from the ground, gone from the top down), nothing below the ground, a bright base ring, the cells round the struck body flash.
// Two passes: back faces (SRPDefaultUnlit, first) then front faces (UniversalForward). Transparent queue, no depth write, no depth passes (route P r2). May glow (D308-150 V11).
Shader "Oheangbu/SpellFx/HexDome"
{
    Properties
    {
        _Patterns ("Pattern atlas 4 x 2 (white line on black)", 2D) = "black" {}
        _Glass ("Glass tint (rgb) / alpha", Color) = (0.10, 0.19, 0.34, 0.12)
        _Rim ("Fresnel rim colour / alpha", Color) = (0.55, 0.72, 0.95, 0.35)
        _Line ("Cell border colour (HDR)", Color) = (2.2, 2.1, 1.85, 1)
        _Pattern ("Pattern line colour / alpha", Color) = (0.96, 0.97, 1.0, 0.62)
        _BaseRing ("Base ring colour (HDR)", Color) = (1.8, 1.7, 1.5, 1)
        _EdgeW ("Border width (edge units)", Range(0.01, 0.3)) = 0.03
        _PatternScale ("Pattern size in the cell", Range(0.5, 1.5)) = 0.86
        _Age ("Age (s)", Float) = 0
        _Fade ("Fade 0..1", Range(0, 1)) = 0
        _RiseTime ("Ground-to-top reveal (s)", Float) = 0.45
        _HitPos ("Hit position (world)", Vector) = (0, -1000, 0, 0)
        _HitAge ("Seconds since the hit (<0 none)", Float) = -1
        _PatternFrom ("Patterns start at this cell height (0 ground .. 1 top)", Range(0, 1)) = 0.12
        _BackLine ("Far side: line / pattern strength", Range(0, 1)) = 0.55
        _BloomSpread ("Pattern bloom moments spread over (s)", Float) = 1.5
        _BloomBoost ("Bloom flash HDR boost", Float) = 2.5
        _FacingFrom ("Near wall: facing ratio where the fade starts", Range(0, 1)) = 0.45
        _FacingPattern ("Near wall facing the camera: pattern strength", Range(0, 1)) = 0.1
        _FacingBorder ("Near wall facing the camera: border strength", Range(0, 1)) = 0.3
        _BackGlass ("Far side: glass strength", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Patterns); SAMPLER(sampler_Patterns);
        CBUFFER_START(UnityPerMaterial)
            float4 _Patterns_ST;
            half4 _Glass, _Rim, _Line, _Pattern, _BaseRing;
            float _EdgeW, _PatternScale, _Age, _Fade, _RiseTime, _HitAge, _BackLine, _BackGlass, _PatternFrom, _FacingFrom, _FacingPattern, _FacingBorder, _BloomSpread, _BloomBoost;
            float4 _HitPos;
        CBUFFER_END

        struct Attributes { float4 pos : POSITION; float3 nrm : NORMAL; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
        struct Varyings { float4 pos : SV_POSITION; float3 nrmWS : TEXCOORD0; float3 posWS : TEXCOORD1; float2 uv0 : TEXCOORD2; float2 uv1 : TEXCOORD3; float2 uv2 : TEXCOORD4; float yOS : TEXCOORD5; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.posWS = TransformObjectToWorld(i.pos.xyz);
            o.pos = TransformWorldToHClip(o.posWS);
            o.nrmWS = TransformObjectToWorldNormal(i.nrm);
            o.uv0 = i.uv0; o.uv1 = i.uv1; o.uv2 = i.uv2; o.yOS = i.pos.y;
            return o;
        }

        half4 Shade(Varyings i, float side)
        {
            if (i.yOS < 0) discard;                                       // nothing below the ground
            float hash = i.uv1.x, edge = i.uv1.y, height = i.uv2.y;
            float pat = floor(i.uv2.x + 0.5);
            bool front = side > 0;
            // ---- reveal: a wipe up from the ground (barely shuffled); outline first, then glass and pattern ----
            // round 4 (Fable r2: the wipe stepped cell by cell, empty outlines ahead of the glass): a soft wipe on the pixel's own height, line and glass together
            float yN = saturate(i.yOS / 0.85);
            float t0 = yN * _RiseTime;
            float outline = saturate((_Age - t0) / 0.16);
            float fill = outline;
            // ---- end: the same wipe backwards, from the top down ----
            float gone = saturate((_Fade * 1.35 - (1 - yN)) / 0.35);
            outline *= 1 - gone; fill *= 1 - gone;
            if (outline <= 0.001) discard;
            // ---- shimmer ----
            float flick = 0.92 + 0.08 * sin(_Age * 7.0 + hash * 40.0) + step(0.88, hash) * 0.3 * saturate(sin(_Age * 9.0 + hash * 17.0));
            // ---- hit: a ring running out from the struck point + the cells round it flash ----
            float ripple = 0, touch = 0;
            if (_HitAge >= 0)
            {
                float d = distance(i.posWS, _HitPos.xyz);
                ripple = exp(-pow((d - _HitAge * 4.0) / 0.45, 2)) * saturate(1 - _HitAge / 1.2);
                touch = saturate(1 - d / 1.3) * exp(-_HitAge * 1.6);              // lasts > 0.3 s (Fable r2: the flash fell between the sampled moments)
            }
            float boost = 1 + ripple * 1.6 + touch * 1.4;
            // ---- layers ----
            float3 n = normalize(i.nrmWS) * side;
            float3 v = normalize(GetWorldSpaceViewDir(i.posWS));
            float fres = pow(1 - saturate(abs(dot(n, v))), 4.0);
            float border = smoothstep(1 - _EdgeW * 1.6, 1 - _EdgeW * 0.3, edge);
            float2 q = (i.uv0 - 0.5) / _PatternScale;                     // -0.5..0.5 inside the pattern's square
            float inCircle = smoothstep(0.5, 0.47, length(q));            // the pattern is clipped to a circle: never a square in a hexagon
            float2 auv = (float2(fmod(pat, 4), 1 - floor(pat / 4)) + saturate(q + 0.5)) / float2(4, 2);
            float line_ = SAMPLE_TEXTURE2D(_Patterns, sampler_Patterns, auv).r * inCircle * (1 - border);
            // the near wall faces the camera and stands between it and the caster: where it faces the camera it is faint (rim bright, middle clear) - the far inner wall carries the patterns
            float facing = smoothstep(_FacingFrom, 0.95, abs(dot(n, v)));
            float lineK = front ? lerp(1, _FacingPattern, facing) : _BackLine, glassK = front ? lerp(1, 0.5, facing) : _BackGlass;
            float borderK = front ? lerp(1, _FacingBorder, facing) : _BackLine;
            line_ *= smoothstep(_PatternFrom, _PatternFrom + 0.2, height);        // hx_1: the low ring of cells is plain glass (the caster inside stays clear), patterns from the shoulder up

            float3 col = _Glass.rgb; float a = _Glass.a * fill * glassK;
            col = lerp(col, _Rim.rgb, fres); a = max(a, _Rim.a * fres * fill * glassK);
            // ---- sporadic bloom (round 3, the user: "문양이 처음부터 모든 곳에 새겨지는 것이 아니라, 산발적으로 Bloom하는 방식"): every cell's pattern blooms at its own
            //      random moment (a bright flash that settles), stays a while, and most fade again - at any moment only some cells carry a pattern, new ones keep blooming ----
            float h2 = frac(hash * 7.13 + 0.37), h3 = frac(hash * 13.7 + 0.11);
            float tb = t0 + 0.15 + h2 * _BloomSpread;
            float since = _Age - tb;
            float bloomIn = saturate(since / 0.1);
            float hold = 0.35 + h3 * 0.9;
            float bloomOut = h3 > 0.8 ? 0 : saturate((since - hold) / 0.4);           // a few cells keep theirs to the end
            float bloomVis = bloomIn * (1 - bloomOut);
            float flash = since >= 0 ? exp(-since * 7.0) : 0;
            float pa = _Pattern.a * line_ * fill * flick * lineK * bloomVis;
            pa = saturate(pa + line_ * fill * flash * 0.5 * lineK);
            col = lerp(col, _Pattern.rgb * (boost + flash * _BloomBoost), saturate(pa / max(a + pa, 1e-3)));
            a = a + pa * (1 - a);
            float ba = border * outline * (0.85 + 0.15 * flick) * borderK;
            col = lerp(col, _Line.rgb * boost, saturate(ba));
            a = max(a, saturate(ba));
            // ---- the base ring: a bright line where the dome meets the ground ----
            float ring = exp(-i.yOS / 0.018) * outline * (front ? 1 : 0.6);
            col = lerp(col, _BaseRing.rgb, saturate(ring)); a = max(a, saturate(ring));
            a = saturate(a * (1 + touch * 0.5));
            return half4(col, a);
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
