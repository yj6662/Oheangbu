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
        _Glass ("Glass tint (rgb) / alpha", Color) = (0.18, 0.31, 0.58, 0.125)
        _Rim ("Fresnel rim colour / alpha", Color) = (0.55, 0.72, 0.95, 0.08)
        _Line ("Cell border colour (HDR)", Color) = (1.3, 1.27, 1.22, 1)
        _Pattern ("Pattern line colour / alpha", Color) = (0.96, 0.95, 0.9, 0.68)
        _BaseRing ("Base ring colour (HDR)", Color) = (1.8, 1.7, 1.5, 1)
        _EdgeW ("Border width (edge units)", Range(0.003, 0.3)) = 0.009
        _Halo ("Border halo colour (ink bleed)", Color) = (0.06, 0.06, 0.08, 1)
        _LineInk ("Line side colour (ink-toned, cool)", Color) = (0.36, 0.37, 0.40, 1)
        _HitCol ("Hit ripple colour (HDR)", Color) = (1.3, 1.32, 1.38, 1)
        _Accent ("Hit accent colour (HDR) - the one accent", Color) = (0.35, 1.5, 1.7, 1)
        _HaloA ("Border halo alpha", Range(0, 1)) = 0.24
        _CorePxMin ("Core width min (render px)", Float) = 0.2
        _CorePxMax ("Core width max (render px)", Float) = 0.34
        _HaloPx ("Halo width (render px)", Float) = 12
        _SeamPx ("Seam light width (fwidth units - one is several screen px here)", Float) = 2.2
        _SeamA ("Seam light alpha", Range(0, 1)) = 0.4
        _CoreA ("Border core alpha", Range(0, 1)) = 0.8
        _PatternScale ("Pattern size in the cell", Range(0.4, 1.5)) = 0.64
        _Age ("Age (s)", Float) = 0
        _Fade ("Fade 0..1", Range(0, 1)) = 0
        _RiseTime ("Ground-to-top reveal (s)", Float) = 0.45
        _HitPos ("Hit position (world)", Vector) = (0, -1000, 0, 0)
        _HitAge ("Seconds since the hit (<0 none)", Float) = -1
        _HitCell ("Struck cell centre (world, w = 1 when set)", Vector) = (0, -1000, 0, 0)
        _PatternFrom ("Patterns start at this cell height (0 ground .. 1 top)", Range(0, 1)) = 0.12
        _BackLine ("Far side: line / pattern strength", Range(0, 1)) = 0.35
        _BloomSpread ("Pattern bloom moments spread over (s)", Float) = 1.5
        _BloomBoost ("Bloom flash HDR boost", Float) = 5
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
            half4 _Glass, _Rim, _Line, _Pattern, _BaseRing, _Halo, _HitCol, _LineInk, _Accent;
            float _SeamPx, _SeamA, _HaloA, _CoreA, _CorePxMin, _CorePxMax, _HaloPx, _EdgeW, _PatternScale, _Age, _Fade, _RiseTime, _HitAge, _BackLine, _BackGlass, _PatternFrom, _FacingFrom, _FacingPattern, _FacingBorder, _BloomSpread, _BloomBoost;
            float4 _HitPos, _HitCell;
        CBUFFER_END

        float Hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float VNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
            return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
        }

        struct Attributes { float4 pos : POSITION; float3 nrm : NORMAL; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; float3 ctr : TEXCOORD3; };
        struct Varyings { float4 pos : SV_POSITION; float3 nrmWS : TEXCOORD0; float3 posWS : TEXCOORD1; float2 uv0 : TEXCOORD2; float2 uv1 : TEXCOORD3; float2 uv2 : TEXCOORD4; float yOS : TEXCOORD5; float3 ctrWS : TEXCOORD6; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.posWS = TransformObjectToWorld(i.pos.xyz);
            o.pos = TransformWorldToHClip(o.posWS);
            o.nrmWS = TransformObjectToWorldNormal(i.nrm);
            o.uv0 = i.uv0; o.uv1 = i.uv1; o.uv2 = i.uv2; o.yOS = i.pos.y; o.ctrWS = TransformObjectToWorld(i.ctr);
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
            float ripple = 0, touch = 0, speck = 0, cellHit = 0, seamFlash = 0, quietNear = 0, ripEdge = 0;
            if (_HitAge >= 0)
            {
                // round 5: by DIRECTION from the dome centre - the struck body stands inside, 1.5 m+ from any wall, so a distance test lit nothing (Fable r4: no flash seen)
                // round 8 (Fable r7: the rings came as a full-height arc with no origin): a CONTACT point on the wall - the wall point in the body's direction at
                //      body height - and the rings grow from 0 there (3D distance), never beyond 0.6 dome radius; a short flash at the contact itself
                float3 c = TransformObjectToWorld(float3(0, 0, 0));
                float domeR = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float2 dh = _HitPos.xz - c.xz;
                float3 contact = float3(c.x, c.y + 0.45, c.z) + float3(normalize(dh + 1e-4).x, 0, normalize(dh + 1e-4).y) * domeR * 0.93;
                float dist = distance(i.posWS, contact);
                float maxR = domeR * 0.45;
                // round 7 (Fable r6: the hit looked like one more random bloom): its own shape - two concentric rings running out from the struck side, warm, ~0.5 s
                float life = saturate(1 - _HitAge / 0.55) * smoothstep(maxR, maxR * 0.7, dist);
                // round 9 (Fable r8: a soft gaussian band read as a lens smear): an ink profile - a hard inner edge, soft only outward
                float s1 = dist - _HitAge * 3.2, s2 = dist - (_HitAge - 0.12) * 3.2;
                float wv = lerp(0.6, 2.4, VNoise(float2(atan2(i.posWS.y - contact.y, i.posWS.x - contact.x + i.posWS.z - contact.z) * 3.0, 1.7)));     // round 16: the stroke's width breathes (a brush, not a wire)
                float k1 = smoothstep(-0.015 * wv, 0.0, s1) * exp(-max(s1, 0) / (0.012 * wv)), k2 = smoothstep(-0.012 * wv, 0.0, s2) * exp(-max(s2, 0) / (0.01 * wv));
                float dry = smoothstep(0.5, 0.56, VNoise(float2(atan2(i.posWS.y - contact.y, i.posWS.x - contact.x + i.posWS.z - contact.z) * 5.0, 0.37)));     // round 15: breaks by ANGLE only - there from the first frame (Fable r14: only late)     // round 11 / 12: dry-brush breaks round the ring (3 - 5 clear gaps)
                ripEdge = smoothstep(0.035, 0.0, abs(s1)) * saturate(1 - _HitAge / 0.14) * smoothstep(maxR, maxR * 0.7, dist);
                ripple = (k1 + 0.7 * k2 * step(0.12, _HitAge)) * life * (0.04 + 0.96 * dry) * (0.45 + 0.75 * VNoise(i.posWS.xy * 14.0 + i.posWS.z * 9.0));     // round 15: ink density along the stroke
                touch = saturate(1 - dist / 0.9) * exp(-_HitAge * 5.0) * 0.6;
                // round 16 (Fable r15: the hit could not be told from a random bloom): the struck cells answer differently - their seams flash for ~3 frames,
                //      then their patterns bloom faster and larger than the sporadic ones
                // round 17 (Fable r16: only the edges near the contact lit, a 2-frame blink under the clip): the WHOLE struck cell (by its centre), HDR, 6 - 8 frame decay
                float dCell = distance(i.ctrWS, _HitCell.xyz);                // round 17b: the part picks the struck cell (nearest centre) - exactly one cell answers
                cellHit = step(dCell, 0.05) * _HitCell.w;
                seamFlash = cellHit * exp(-_HitAge * 18.0) * 5.0;
                quietNear = smoothstep(2.4, 1.6, dCell) * (1 - cellHit) * saturate(1 - _HitAge / 0.6);     // the neighbours do not bloom for 0.6 s (the struck cell reads alone)
                speck = smoothstep(0.32, 0.12, dist) * saturate(1 - _HitAge / 0.12) * smoothstep(0.74, 0.8, VNoise(i.posWS.xy * 16.0 + i.posWS.z * 11.0));     // round 15: 2 - 4 clear ink dots, not a smear     // round 13 / 14: a short INK speck at the contact (dark, not a light)
            }
            float boost = 1 + touch * 1.0;
            // ---- layers ----
            float3 n = normalize(i.nrmWS) * side;
            float3 v = normalize(GetWorldSpaceViewDir(i.posWS));
            float fres = pow(1 - saturate(abs(dot(n, v))), 4.0);
            // round 7 (Fable r6: the hairline broke into dots on cells facing the camera - under a pixel): never thinner than ~1.5 px on screen
            float fw = max(fwidth(edge), 1e-4);
            float coreW = clamp(_EdgeW, fw * _CorePxMin, fw * _CorePxMax);     // the core is held between ~1.2 and 2 px on screen whatever the cell size (r7 bigger cells made it a neon bar)
            coreW *= lerp(0.55, 1.75, VNoise(float2(i.posWS.x - i.posWS.y, i.posWS.z + i.posWS.y) * 2.2));     // round 10 / 12: the brush line thickens and thins (wider swing)
            float border = smoothstep(1 - coreW - fw * 0.75, 1 - coreW + fw * 0.25, edge);
            float2 q = (i.uv0 - 0.5) / _PatternScale;                     // -0.5..0.5 inside the pattern's square
            float inCircle = smoothstep(0.5, 0.47, length(q));            // the pattern is clipped to a circle: never a square in a hexagon
            float2 auv = (float2(fmod(pat, 4), 1 - floor(pat / 4)) + saturate(q + 0.5)) / float2(4, 2);
            float line_ = SAMPLE_TEXTURE2D(_Patterns, sampler_Patterns, auv).r * inCircle * (1 - border);
            // the near wall faces the camera and stands between it and the caster: where it faces the camera it is faint (rim bright, middle clear) - the far inner wall carries the patterns
            float facing = smoothstep(_FacingFrom, 0.95, abs(dot(n, v)));
            float lineK = front ? lerp(1, _FacingPattern, facing) : _BackLine, glassK = front ? lerp(1, 0.5, facing) : _BackGlass;
            float borderK = front ? lerp(1, _FacingBorder, facing) * smoothstep(0.02, 0.09, abs(dot(n, v))) : _BackLine;     // round 9: the hex lines fade at the silhouette (no twin outline with the fresnel rim)
            line_ *= smoothstep(_PatternFrom, _PatternFrom + 0.2, height);        // hx_1: the low ring of cells is plain glass (the caster inside stays clear), patterns from the shoulder up

            float front_ = outline * (1 - outline) * 4 * (1 - gone);                   // round 7: the rising front glows (no bare-wire moment while the glass comes)
            float sil = lerp(0.25, 1, pow(1 - saturate(abs(dot(n, v))), 1.5));    // round 7: almost clear face-on (no flat blue sheet over the caster), tinted toward the silhouette
            float3 col = _Glass.rgb * (1 + touch * 2.5); float a = _Glass.a * fill * glassK * sil + front_ * 0.18 * glassK;     // round 11: the struck glass brightens, never a darker disc
            float rimK = front ? 1 : 0.3;                                        // round 6: the far side's rim stays faint (it read as a hazy band across the caster)
            col = lerp(col, _Rim.rgb, fres * rimK); a = max(a, _Rim.a * fres * fill * glassK * rimK);
            // ---- sporadic bloom (round 3, the user: "문양이 처음부터 모든 곳에 새겨지는 것이 아니라, 산발적으로 Bloom하는 방식"): every cell's pattern blooms at its own
            //      random moment (a bright flash that settles), stays a while, and most fade again - at any moment only some cells carry a pattern, new ones keep blooming ----
            float h2 = frac(hash * 7.13 + 0.37), h3 = frac(hash * 13.7 + 0.11);
            // round 5 (Fable r4: blooms piled up until every cell was lit): each cell blooms on its own period (1.4 - 2.8 s, random phase), lit ~0.55 s a time ->
            //      about a quarter of the cells lit at any moment, never all; nothing before the cell is revealed
            float period = 0.9 + h3 * 1.1;                                       // round 11 (Fable r10: the hold was a still second): blooms come more often
            float cyc = (_Age - t0 - 0.1) / period + h2;
            float since = frac(cyc) * period;
            float ready = saturate((_Age - t0 - 0.1) / 0.15);                    // round 6: a soft start (a step here drew a hard line rising with the wipe - Fable r5)
            float bloomVis = smoothstep(0, 0.09, since) * (1 - saturate((since - 0.12) / 0.35)) * ready;     // round 11: 5-frame rise, ~25-frame settle     // round 9: an eased 8 - 10 frame attack (Fable r8: a 4-frame pop)
            float flash = smoothstep(0, 0.12, since) * exp(-since * 6.0) * ready;
            // round 9 (Fable r8: a far-wall bloom opened right over the bear's head at the hit): no bloom on pixels seen just behind the struck body
            float3 bodyP = _HitPos.xyz + float3(0, 0.8, 0), camP = GetCameraPositionWS(), ray = i.posWS - camP;
            float tb = dot(bodyP - camP, ray) / max(dot(ray, ray), 1e-4);
            float behind = (tb > 0 && tb < 1) ? smoothstep(0.55, 0.95, length(camP + ray * tb - bodyP)) : 1;
            float noNew = (1 - saturate(_Fade * 2.5)) * smoothstep(0.2, 0.35, abs(dot(n, v)));    // round 8: no bloom where the two walls overlap near the silhouette                             // no fresh bloom while the dome goes
            bloomVis *= noNew * behind * (1 - quietNear); flash *= noNew * behind * (1 - quietNear);
            float hitBloom = cellHit * smoothstep(0.03, 0.07, _HitAge) * (1 - saturate((_HitAge - 0.45) / 0.35));
            bloomVis = max(bloomVis, hitBloom); flash = max(flash, cellHit * exp(-max(_HitAge - 0.03, 0) * 4.0) * step(0.03, _HitAge) * 1.6);
            // round 9 (Fable r8: clean white gradients - a CG glass shell): ink density - lines and patterns carry a world-space ink grain
            // round 10 (Fable r9: the grain did not read at 1280): stronger density swings, and the line width breathes with it
            float inkLo = VNoise(float2(i.posWS.x + i.posWS.y * 0.7, i.posWS.z - i.posWS.y * 0.5) * 4.0);
            float ink = 0.3 + 0.95 * inkLo + 0.2 * VNoise(i.posWS.xz * 31.0 + i.posWS.y * 23.0);

            float bloomK = front ? max(lineK, 0.6 * bloomVis) : lineK;          // round 11: a bloom shows on the near wall too while it is lit
            float pa = _Pattern.a * line_ * fill * flick * bloomK * bloomVis;
            pa = saturate(pa + line_ * fill * flash * 0.5 * lineK);
            pa *= saturate(ink);
            float accentK = cellHit * saturate(1 - max(_HitAge - 0.05, 0) / 0.2) * step(0, _HitAge);
            float3 patCol = lerp(_Pattern.rgb * (boost + flash * _BloomBoost * lineK), _Accent.rgb * 1.6, accentK);      // round 17: the struck cell blooms in the one accent colour first
            col = lerp(col, patCol, saturate(pa / max(a + pa, 1e-3)));     // round 9: the far wall's flash is capped like its lines
            col += _Accent.rgb * cellHit * exp(-_HitAge * 10.0) * 0.35 * step(0, _HitAge) * fill;     // the struck glass glows a moment
            a = a + pa * (1 - a);
            // round 5 (Fable r4: flat opaque white wires): a thin bright core inside a soft halo
            float lineFade = 1 - saturate(_Fade * 1.6);                         // round 7: the grid goes before the glass (no bare wire left)
            float halo = smoothstep(1 - fw * _HaloPx, 1 - coreW * 0.5, edge) * _HaloA * outline * borderK * lineFade * saturate(ink);
            col = lerp(col, _Halo.rgb, saturate(halo / max(a + halo, 1e-3))); a = a + halo * (1 - a);     // round 11: the halo is an ink bleed (dark), not a glow
            float ba = border * outline * (0.85 + 0.15 * flick) * borderK * _CoreA * lineFade * saturate(ink + 0.15);
            ba = max(ba, border * outline * saturate(seamFlash));
            // round 17b (Fable r17: HDR never reached the frame - the lab look tone-maps it): the glow is DRAWN - a soft accent band inside the struck cell's six seams
            float glowBand = smoothstep(0.55, 1.0, edge) * cellHit * exp(-_HitAge * 9.0) * step(0, _HitAge);
            col = lerp(col, _Accent.rgb, saturate(glowBand * 0.85)); a = max(a, glowBand * 0.75 * outline);
            float heart = smoothstep(1 - coreW * 0.55, 1 - coreW * 0.1, edge);                 // round 11 (Fable r10: a pure white glowing wire): only the line's heart glows,
            float3 lineCol = lerp(_LineInk.rgb, _Line.rgb * boost * 1.4, saturate(heart * 0.95 + ink * 0.35));   //   its sides are ink-toned cream, varying with the ink density
            col = lerp(col, lineCol + _Accent.rgb * seamFlash, saturate(ba / max(a, 1e-3)));     // round 17 (the user: "이런 이펙트 순간에는 발광도 넣어"): the struck seams glow in the accent
            // round 13 (Fable r12: a matt cream thread, not a hologram seam): a narrow soft light round the heart (3 - 5 px), over the ink edge
            float seam = smoothstep(1 - fw * _SeamPx, 1 - coreW * 0.4, edge) * _SeamA * outline * borderK * lineFade;
            col = lerp(col, _Line.rgb, saturate(seam / max(a + seam, 1e-3) * 0.6)); a = a + seam * (1 - a);
            a = max(a, saturate(ba));
            // ---- the base ring: a bright line where the dome meets the ground ----
            float ring = exp(-i.yOS / 0.03) * outline * (front ? 1 : 0.25) * (1 - saturate(_Fade * 3));
            col = lerp(col, _BaseRing.rgb, saturate(ring)); a = max(a, saturate(ring));
            float rip = saturate(ripple) * outline;
            // round 17b (Fable r17: the dark body sank into the glass, only broken glowing bits drifted): one continuous glowing ring, ~8 frames
            col = lerp(col, _Accent.rgb, saturate(ripEdge)); a = max(a, saturate(ripEdge) * 0.8 * outline);
            float sp = saturate(speck) * outline;
            col = lerp(col, _Halo.rgb, sp); a = a + sp * 0.8 * (1 - a);
            a = saturate(a * (1 + touch * 0.5)) * (1 - smoothstep(0.3, 0.62, _Fade));        // round 10: the glass ends with the lines (no shell left alone - Fable r9)        // round 5: the wipe plus an overall fade (no comb of edges left on the ground)
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
