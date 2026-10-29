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
        _Glass ("Glass tint (rgb) / alpha", Color) = (0.20, 0.32, 0.50, 0.15)
        _Rim ("Fresnel rim colour / alpha", Color) = (0.55, 0.72, 0.95, 0.35)
        _Line ("Cell border colour (HDR)", Color) = (1.6, 1.46, 1.16, 1)
        _Pattern ("Pattern line colour / alpha", Color) = (0.96, 0.97, 1.0, 0.62)
        _BaseRing ("Base ring colour (HDR)", Color) = (1.8, 1.7, 1.5, 1)
        _EdgeW ("Border width (edge units)", Range(0.003, 0.3)) = 0.009
        _Halo ("Border halo colour", Color) = (0.95, 0.86, 0.66, 1)
        _HitCol ("Hit ripple colour (HDR)", Color) = (1.9, 1.75, 1.35, 1)
        _HaloA ("Border halo alpha", Range(0, 1)) = 0.22
        _CorePxMin ("Core width min (render px)", Float) = 0.3
        _CorePxMax ("Core width max (render px)", Float) = 0.5
        _HaloPx ("Halo width (render px)", Float) = 6
        _CoreA ("Border core alpha", Range(0, 1)) = 0.8
        _PatternScale ("Pattern size in the cell", Range(0.5, 1.5)) = 0.74
        _Age ("Age (s)", Float) = 0
        _Fade ("Fade 0..1", Range(0, 1)) = 0
        _RiseTime ("Ground-to-top reveal (s)", Float) = 0.45
        _HitPos ("Hit position (world)", Vector) = (0, -1000, 0, 0)
        _HitAge ("Seconds since the hit (<0 none)", Float) = -1
        _PatternFrom ("Patterns start at this cell height (0 ground .. 1 top)", Range(0, 1)) = 0.12
        _BackLine ("Far side: line / pattern strength", Range(0, 1)) = 0.35
        _BloomSpread ("Pattern bloom moments spread over (s)", Float) = 1.5
        _BloomBoost ("Bloom flash HDR boost", Float) = 2.5
        _FacingFrom ("Near wall: facing ratio where the fade starts", Range(0, 1)) = 0.45
        _FacingPattern ("Near wall facing the camera: pattern strength", Range(0, 1)) = 0.1
        _FacingBorder ("Near wall facing the camera: border strength", Range(0, 1)) = 0.15
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
            half4 _Glass, _Rim, _Line, _Pattern, _BaseRing, _Halo, _HitCol;
            float _HaloA, _CoreA, _CorePxMin, _CorePxMax, _HaloPx, _EdgeW, _PatternScale, _Age, _Fade, _RiseTime, _HitAge, _BackLine, _BackGlass, _PatternFrom, _FacingFrom, _FacingPattern, _FacingBorder, _BloomSpread, _BloomBoost;
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
                // round 5: by DIRECTION from the dome centre - the struck body stands inside, 1.5 m+ from any wall, so a distance test lit nothing (Fable r4: no flash seen)
                // round 8 (Fable r7: the rings came as a full-height arc with no origin): a CONTACT point on the wall - the wall point in the body's direction at
                //      body height - and the rings grow from 0 there (3D distance), never beyond 0.6 dome radius; a short flash at the contact itself
                float3 c = TransformObjectToWorld(float3(0, 0, 0));
                float domeR = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float2 dh = _HitPos.xz - c.xz;
                float3 contact = float3(c.x, c.y + 0.7, c.z) + float3(normalize(dh + 1e-4).x, 0, normalize(dh + 1e-4).y) * domeR * 0.93;
                float dist = distance(i.posWS, contact);
                float maxR = domeR * 0.6;
                // round 7 (Fable r6: the hit looked like one more random bloom): its own shape - two concentric rings running out from the struck side, warm, ~0.5 s
                float life = saturate(1 - _HitAge / 0.55) * smoothstep(maxR, maxR * 0.7, dist);
                float r1 = abs(dist - _HitAge * 3.2), r2 = abs(dist - (_HitAge - 0.12) * 3.2);
                ripple = (smoothstep(0.1, 0.0, r1) + 0.7 * smoothstep(0.08, 0.0, r2) * step(0.12, _HitAge)) * life;
                touch = saturate(1 - dist / 0.9) * exp(-_HitAge * 5.0) * 0.6;
                ripple += smoothstep(0.35, 0.0, dist) * saturate(1 - _HitAge / 0.1) * 1.5;      // the contact flash (6 frames)
            }
            float boost = 1 + touch * 1.0;
            // ---- layers ----
            float3 n = normalize(i.nrmWS) * side;
            float3 v = normalize(GetWorldSpaceViewDir(i.posWS));
            float fres = pow(1 - saturate(abs(dot(n, v))), 4.0);
            // round 7 (Fable r6: the hairline broke into dots on cells facing the camera - under a pixel): never thinner than ~1.5 px on screen
            float fw = max(fwidth(edge), 1e-4);
            float coreW = clamp(_EdgeW, fw * _CorePxMin, fw * _CorePxMax);     // the core is held between ~1.2 and 2 px on screen whatever the cell size (r7 bigger cells made it a neon bar)
            float border = smoothstep(1 - coreW - fw * 0.75, 1 - coreW + fw * 0.25, edge);
            float2 q = (i.uv0 - 0.5) / _PatternScale;                     // -0.5..0.5 inside the pattern's square
            float inCircle = smoothstep(0.5, 0.47, length(q));            // the pattern is clipped to a circle: never a square in a hexagon
            float2 auv = (float2(fmod(pat, 4), 1 - floor(pat / 4)) + saturate(q + 0.5)) / float2(4, 2);
            float line_ = SAMPLE_TEXTURE2D(_Patterns, sampler_Patterns, auv).r * inCircle * (1 - border);
            // the near wall faces the camera and stands between it and the caster: where it faces the camera it is faint (rim bright, middle clear) - the far inner wall carries the patterns
            float facing = smoothstep(_FacingFrom, 0.95, abs(dot(n, v)));
            float lineK = front ? lerp(1, _FacingPattern, facing) : _BackLine, glassK = front ? lerp(1, 0.5, facing) : _BackGlass;
            float borderK = front ? lerp(1, _FacingBorder, facing) : _BackLine;
            line_ *= smoothstep(_PatternFrom, _PatternFrom + 0.2, height);        // hx_1: the low ring of cells is plain glass (the caster inside stays clear), patterns from the shoulder up

            float front_ = outline * (1 - outline) * 4 * (1 - gone);                   // round 7: the rising front glows (no bare-wire moment while the glass comes)
            float sil = lerp(0.25, 1, pow(1 - saturate(abs(dot(n, v))), 1.5));    // round 7: almost clear face-on (no flat blue sheet over the caster), tinted toward the silhouette
            float3 col = _Glass.rgb * (1 + touch * 1.5); float a = _Glass.a * fill * glassK * sil * (1 + touch) + front_ * 0.18 * glassK;
            float rimK = front ? 1 : 0.3;                                        // round 6: the far side's rim stays faint (it read as a hazy band across the caster)
            col = lerp(col, _Rim.rgb, fres * rimK); a = max(a, _Rim.a * fres * fill * glassK * rimK);
            // ---- sporadic bloom (round 3, the user: "문양이 처음부터 모든 곳에 새겨지는 것이 아니라, 산발적으로 Bloom하는 방식"): every cell's pattern blooms at its own
            //      random moment (a bright flash that settles), stays a while, and most fade again - at any moment only some cells carry a pattern, new ones keep blooming ----
            float h2 = frac(hash * 7.13 + 0.37), h3 = frac(hash * 13.7 + 0.11);
            // round 5 (Fable r4: blooms piled up until every cell was lit): each cell blooms on its own period (1.4 - 2.8 s, random phase), lit ~0.55 s a time ->
            //      about a quarter of the cells lit at any moment, never all; nothing before the cell is revealed
            float period = 1.4 + h3 * 1.4;
            float cyc = (_Age - t0 - 0.1) / period + h2;
            float since = frac(cyc) * period;
            float ready = saturate((_Age - t0 - 0.1) / 0.15);                    // round 6: a soft start (a step here drew a hard line rising with the wipe - Fable r5)
            float bloomVis = saturate(since / 0.08) * (1 - saturate((since - 0.3) / 0.25)) * ready;
            float flash = exp(-since * 7.0) * ready;
            float noNew = (1 - saturate(_Fade * 2.5)) * smoothstep(0.2, 0.35, abs(dot(n, v)));    // round 8: no bloom where the two walls overlap near the silhouette                             // no fresh bloom while the dome goes
            bloomVis *= noNew; flash *= noNew;

            float pa = _Pattern.a * line_ * fill * flick * lineK * bloomVis;
            pa = saturate(pa + line_ * fill * flash * 0.5 * lineK);
            col = lerp(col, _Pattern.rgb * (boost + flash * _BloomBoost), saturate(pa / max(a + pa, 1e-3)));
            a = a + pa * (1 - a);
            // round 5 (Fable r4: flat opaque white wires): a thin bright core inside a soft halo
            float lineFade = 1 - saturate(_Fade * 1.6);                         // round 7: the grid goes before the glass (no bare wire left)
            float halo = smoothstep(1 - fw * _HaloPx, 1 - coreW * 0.5, edge) * _HaloA * outline * borderK * lineFade;
            col = lerp(col, _Halo.rgb * boost, saturate(halo / max(a + halo, 1e-3))); a = a + halo * (1 - a);
            float ba = border * outline * (0.85 + 0.15 * flick) * borderK * _CoreA * lineFade;
            col = lerp(col, _Line.rgb * boost, saturate(ba / max(a, 1e-3)));
            a = max(a, saturate(ba));
            // ---- the base ring: a bright line where the dome meets the ground ----
            float ring = exp(-i.yOS / 0.03) * outline * (front ? 1 : 0.25) * (1 - saturate(_Fade * 3));
            col = lerp(col, _BaseRing.rgb, saturate(ring)); a = max(a, saturate(ring));
            float rip = saturate(ripple) * outline;
            col = lerp(col, _HitCol.rgb, saturate(rip / max(a + rip, 1e-3))); a = a + rip * 0.85 * (1 - a);
            a = saturate(a * (1 + touch * 0.5)) * (1 - smoothstep(0.45, 1.0, _Fade));        // round 5: the wipe plus an overall fade (no comb of edges left on the ground)
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
