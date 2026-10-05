// #308 UI/InkVessel308 (SPEC-HUD-LIQUID-308 §2.6, D308-11; theme SPEC-UI-THEME-308, D308-15). One quad = one HUD element: a glass
// vessel with its liquid (code 0 = ink, 1 = HP), an action mark (code 2 dodge, 3 jump, 4 vehicle, 5 vehicle out) or the key
// glyph of a mark (code 6, D308-11b: a second quad of the mark's own graphic).
// numpy twin: Tools/Art/hud308_twin.py.
// Structure = UI/InkMeter (built-in UI/Default: stencil, RectMask2D, alpha clip). The texture is ONE atlas (hud308_atlas.png,
// linear data, made by Tools/Art/hud308_assets.py); InkVesselGraphic308 hands it over as _MainTex, so vessels and marks batch.
// ART-INK: no emission, no HDR property, no bloom input. Every output colour is a mix of _InkColor, _PaperColor, _AlertColor,
// _Lacquer, the nacre colour (a low-chroma LDR colour, never above the paper value) and the vertex colour, then saturate():
// nothing can be brighter than the paper value. Glass light, the impact light and the shell are VALUE, not light.
//
// Theme (D308-15), two switches in _Theme:
//   x  the vessel wears a lacquer neck band with one cut-shell line (atlas G of the vessel cell: 0 none, .5 lacquer, 1 nacre).
//      The lip above the band stays glass (an open mouth, no stopper); the line runs along the band's lower edge, so a pearl
//      line always lies between the lacquer and the ink below it
//   y  a mark is a najeon lid: a round lacquer lid, a cut-shell line by its rim and the pictogram set in shell pieces (the lid
//      cells of the atlas: rgb = baked shell colour as sRGB code values, a = shell coverage). Off = the ink pictogram + paper rim
// Nothing in the theme moves: the shell colours are still (no time term, no shimmer).
//
// D308-11b: NOTHING in this shader moves on its own. There is no time term; the surface's slope, ripple amplitude and ripple
// phase arrive per vertex from LiquidSim308, which moves them only in answer to what the player really did and hands over
// zeros - a flat line - while the player stands still.
// Key glyph (code 6): a lacquer keycap with precise right angles, a paper hairline, a paper lip and ONE key symbol of the atlas
// key block (alpha of cell TEXCOORD3.z; one cell = one key, so no word and no sentence can be drawn). The symbol dims with its
// mark; reduced motion, the flash setting and an impact frame leave it as it is.
//
// Impact frame (D308-10b): the DIRECTION of the value light is taken here, per pixel, from the same point every live HUD shader
// uses (ImpactHud308.hlsl: _OhImpactHudPoint308 = the impact point in the pixel frame of the overlay target, written by
// ImpactFrameDirector308 and by nothing else). The screen derivatives of the quad uv carry that pixel-frame vector into the
// quad's own frame, so no projection flag is read and a flipped target cannot flip the light. The STRENGTH comes per vertex
// (HudVessels308: its gate decides when the cluster may react), and without a live impact (_OhImpact308.z = 0) there is no light.
//
// Per vertex (InkVesselGraphic308; the canvas needs TexCoord1..3):
//   COLOR      liquid / glyph colour (cinnabar or ink), a = whole-element alpha
//   TEXCOORD0  0..1 over the quad (vessels: the design rect + _QuadRef.z px margin on every side; marks: the 44 px rect + 4 px
//              margin on every side = one atlas cell, so the paper rim is never cut at the rect), y up
//   TEXCOORD1  vessel: level 0..1, tilt (height / width), wave amplitude (design px), wave phase       mark / key: x = wet 0..1
//   TEXCOORD2  x = level of the wet film top (y > 0) or of the fresh share bottom (y < 0), y = film alpha / -fresh share,
//              z = HP: danger level (tide rings), ink: level left after the stroke cost (< 0 = cannot pay, drawn at |z|, 0 = off),
//              w = low state 0..1
//   TEXCOORD3  x = code, y = impact light strength 0..1 (0 = no light), z = key: cell 0..47 of the key block (else 0),
//              w = vessel: pour thread 0..1, mark: re-wetting progress 0..1 along the stroke order
// "px" are DESIGN px of the HP vessel (114 x 150 rect); the ink vessel is the same drawing at 0.88. Anti-aliasing uses fwidth.
Shader "UI/InkVessel308"
{
    Properties
    {
        [PerRendererData] _MainTex ("Atlas (hud308_atlas, set per renderer)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _PaperColor ("Paper (UiStyle304.Paper)", Color) = (0.902, 0.886, 0.843, 1)
        _InkColor ("Ink (UiStyle304.Ink)", Color) = (0.078, 0.078, 0.075, 1)
        _AlertColor ("Cost line when the ink cannot pay (UiStyle304.CinnabarLift)", Color) = (0.839, 0.353, 0.263, 1)

        _CellVessel ("Atlas cell: vessel (uv x, y, w, h)", Vector) = (0.015625, 0.197266, 0.297852, 0.771484)
        _CellDodge ("Atlas cell: dodge", Vector) = (0.335938, 0.5625, 0.203125, 0.40625)
        _CellJump ("Atlas cell: jump", Vector) = (0.546875, 0.5625, 0.203125, 0.40625)
        _CellVehicle ("Atlas cell: vehicle", Vector) = (0.757812, 0.5625, 0.203125, 0.40625)
        _CellVehicleOut ("Atlas cell: vehicle out", Vector) = (0.335938, 0.125, 0.203125, 0.40625)
        _CellDodgeN ("Atlas cell: dodge lid (najeon)", Vector) = (0.548828, 0.269531, 0.128906, 0.257812)
        _CellJumpN ("Atlas cell: jump lid (najeon)", Vector) = (0.681641, 0.269531, 0.128906, 0.257812)
        _CellVehicleN ("Atlas cell: vehicle lid (najeon)", Vector) = (0.548828, 0.003906, 0.128906, 0.257812)
        _CellVehicleOutN ("Atlas cell: vehicle out lid (najeon)", Vector) = (0.681641, 0.003906, 0.128906, 0.257812)
        _QuadRef ("Vessel quad: width px, height px, margin px, SDF range px", Vector) = (122, 158, 4, 16)
        _Level ("Level: floor uv, full uv, wave length px, neck top uv", Vector) = (0.06013, 0.82943, 102.6, 0.96835)

        _Glass ("Glass: outline px, paper rim px, empty wash alpha, highlight alpha", Vector) = (2.6, 2.2, 0.14, 0.55)
        _Glass2 ("Glass: wall px, rim alpha, outline alpha, liquid alpha", Vector) = (4, 0.72, 0.95, 0.96)
        _Pool ("Liquid: pool edge px, pool edge dark, core lighten, fresh share lighten", Vector) = (5, 0.22, 0.06, 0.18)
        _Meniscus ("Surface: line px, line alpha, climb px, climb range px", Vector) = (1.1, 0.78, 2, 6)
        _Low ("Low: HP darken, tide alpha, tide rings, ink dry streak", Vector) = (0.22, 0.22, 3, 0.45)
        _Cost ("Cost line: dash px, gap px, line px, alpha", Vector) = (6, 4, 1.2, 0.9)
        _Pour ("Pour thread: width px", Vector) = (2, 0, 0, 0)
        _Impact ("Impact: shadow px, shadow alpha, liquid shift, far rim ink", Vector) = (9, 0.5, 0.1, 0.6)
        _Mark ("Mark: dry alpha, rim alpha, re-wet edge", Vector) = (0.3, 0.92, 0.04, 0)
        _Look ("Look: glass wall paper alpha, body depth darken, outline pressure, lens back-line alpha", Vector) = (0.34, 0.12, 0.28, 0.34)
        _Lens ("Lens: HP px, ink px, paper mix, range px", Vector) = (4.2, 3.6, 0.29, 24)

        _Theme ("Theme (D308-15): collar on, lids on, lid dry alpha, family share green + blue", Vector) = (1, 1, 0.3, 0.85)
        _Lacquer ("Theme: lacquer (theme308_tokens Lacquer)", Color) = (0.0667, 0.0588, 0.0510, 1)
        _NacreN0 ("Theme: nacre N0 (sRGB code values; rgb = N0 + A cos h + B sin h)", Vector) = (0.7254, 0.7273, 0.7280, 0)
        _NacreA ("Theme: nacre A", Vector) = (0.0845, -0.0314, -0.0034, 0)
        _NacreB ("Theme: nacre B", Vector) = (0.0322, 0.0003, -0.0900, 0)
        _NacreHue ("Theme: nacre hue families deg (green, blue, pink), swing deg inside a piece", Vector) = (170, 245, 320, 28)
        _NacreArc ("Theme: nacre hue arc min deg, max deg, hue period px, family share green", Vector) = (150, 340, 30, 0.42)
        _NacreCut ("Theme: collar line half length px, piece px", Vector) = (16.88, 6.752, 0, 0)
        _Button ("Theme: lid radius px, mark quad px, lid cell px, rim line inner px", Vector) = (22, 52, 44, 18.5)

        _Key ("Key glyph (D308-11b): keycap px, hairline px, lip px, symbol alpha of a dry mark", Vector) = (18, 1, 2, 0.42)
        _Key2 ("Key glyph: symbol box px, symbol lift px, hairline alpha, lip alpha", Vector) = (14, 1, 0.8, 0.28)
        _KeyGrid ("Key block: uv x, y of cell 0 (top-left), pitch u, pitch v", Vector) = (0.819336, 0.466797, 0.029297, 0.058594)
        _KeyCell ("Key block: cell uv w, h, columns", Vector) = (0.027344, 0.054688, 6, 0)

        _LinearInkGamma ("Linear-space ink gamma (1 = off; sRGB mockup match)", Range(1, 3)) = 1.8
        _LinearPaperGamma ("Linear-space paper gamma (1 = off)", Range(1, 3)) = 1.3

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            // #308 D308-10b: the impact globals and the screen uv of the overlay target, shared with UI/InkMeter and UI/InkReveal
            // (SPEC-SPELL-DEPLOY-308 §9). Only the declarations and OhHudScreenUv are used here; the vessel has its own reaction.
            #include "Assets/_Project/Art/UI/UI304/Shaders/ImpactHud308.hlsl"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 liquid   : TEXCOORD1;
                float4 marks    : TEXCOORD2;
                float4 extra    : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 mask          : TEXCOORD2;
                float4 liquid        : TEXCOORD3;
                float4 marks         : TEXCOORD4;
                float4 extra         : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            fixed4 _PaperColor;
            fixed4 _InkColor;
            fixed4 _AlertColor;
            float4 _CellVessel;
            float4 _CellDodge;
            float4 _CellJump;
            float4 _CellVehicle;
            float4 _CellVehicleOut;
            float4 _CellDodgeN;
            float4 _CellJumpN;
            float4 _CellVehicleN;
            float4 _CellVehicleOutN;
            float4 _QuadRef;
            float4 _Level;
            float4 _Glass;
            float4 _Glass2;
            float4 _Pool;
            float4 _Meniscus;
            float4 _Low;
            float4 _Cost;
            float4 _Pour;
            float4 _Impact;
            float4 _Mark;
            float4 _Look;
            float4 _Lens;
            float4 _Theme;
            fixed4 _Lacquer;
            float4 _NacreN0;
            float4 _NacreA;
            float4 _NacreB;
            float4 _NacreHue;
            float4 _NacreArc;
            float4 _NacreCut;
            float4 _Button;
            float4 _Key;
            float4 _Key2;
            float4 _KeyGrid;
            float4 _KeyCell;
            float _LinearInkGamma;
            float _LinearPaperGamma;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float4 vPosition = UnityObjectToClipPos(v.vertex);
                OUT.worldPosition = v.vertex;
                OUT.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                OUT.texcoord = v.texcoord.xy;
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                {
                    v.color.rgb = UIGammaToLinear(v.color.rgb);
                }
                OUT.color = v.color * _Color;
                OUT.liquid = v.liquid;
                OUT.marks = v.marks;
                OUT.extra = v.extra;
                return OUT;
            }

            // anti-aliased coverage of lo <= d < hi (aa = d units per screen px)
            inline float Band(float d, float lo, float hi, float aa)
            {
                return saturate((d - lo) / aa + 0.5) * saturate((hi - d) / aa + 0.5);
            }

            // straight-alpha "src over dst": the layers of one element are composited inside the shader
            inline float4 Over(float4 dst, float3 rgb, float a)
            {
                a = saturate(a);
                float outA = a + dst.a * (1.0 - a);
                float3 outRgb = (rgb * a + dst.rgb * (dst.a * (1.0 - a))) / max(outA, 1e-5);
                return float4(outRgb, outA);
            }

            // #304: uGUI composites in linear light, the design alphas are sRGB composites. Same remap as UI/InkMeter, per layer:
            // an ink layer lands at 1-(1-a)^g, a paper layer at a^g (HudTokens304.ShaderInkGamma reads the same properties).
            inline float Remap(float3 rgb, float a)
            {
                a = saturate(a);
                #ifndef UNITY_COLORSPACE_GAMMA
                float lum = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                float darkA = 1.0 - pow(max(1.0 - a, 0.0), max(1.0, _LinearInkGamma));
                float lightA = pow(a, max(1.0, _LinearPaperGamma));
                a = lerp(darkA, lightA, saturate((lum - 0.2) * 2.5));
                #endif
                return a;
            }

            // sRGB code values (the theme's data, the lid cells of the atlas) -> the colour space this project composites in
            inline float3 FromCode(float3 c)
            {
                #ifndef UNITY_COLORSPACE_GAMMA
                c = GammaToLinearSpace(c);
                #endif
                return c;
            }

            // SPEC-UI-THEME-308 §1: the nacre colour at a hue (degrees, kept on the theme's arc). LDR by construction.
            inline float3 Nacre(float hueDeg)
            {
                float h = radians(clamp(hueDeg, _NacreArc.x, _NacreArc.y));
                return FromCode(saturate(_NacreN0.rgb + _NacreA.rgb * cos(h) + _NacreB.rgb * sin(h)));
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                const half alphaPrecision = half(0xff);
                const half invAlphaPrecision = half(1.0 / alphaPrecision);
                IN.color.a = round(IN.color.a * alphaPrecision) * invAlphaPrecision;

                float2 uv = IN.texcoord;
                float code = IN.extra.x;
                float isMark = step(1.5, code);
                float lid = isMark * step(0.5, _Theme.y);                  // this mark is drawn as a najeon lid
                float4 cell = _CellVessel;
                cell = lerp(cell, lerp(_CellDodge, _CellDodgeN, lid), step(1.5, code));
                cell = lerp(cell, lerp(_CellJump, _CellJumpN, lid), step(2.5, code));
                cell = lerp(cell, lerp(_CellVehicle, _CellVehicleN, lid), step(3.5, code));
                cell = lerp(cell, lerp(_CellVehicleOut, _CellVehicleOutN, lid), step(4.5, code));
                // a lid cell holds the central _Button.z px of the mark quad (_Button.y px); every other cell holds its whole quad
                float2 cuv = lerp(saturate(uv), saturate((uv - 0.5) * (_Button.y / max(_Button.z, 1.0)) + 0.5), lid);
                // key glyph (code 6): the cell of the key block named by extra.z; the symbol box (_Key2.x px) sits _Key2.y px
                // above the centre of the keycap (_Key.x px)
                float isKey = step(5.5, code);
                float keyIndex = floor(IN.extra.z + 0.5);
                float keyRow = floor((keyIndex + 0.5) / max(_KeyCell.z, 1.0));
                float keyCol = keyIndex - keyRow * _KeyCell.z;
                float2 pk = (uv - 0.5) * _Key.x;                            // design px from the keycap centre, y up
                float2 kuv = (pk - float2(0.0, _Key2.y)) / max(_Key2.x, 1.0) + 0.5;
                float keyIn = step(0.0, kuv.x) * step(kuv.x, 1.0) * step(0.0, kuv.y) * step(kuv.y, 1.0);
                cell = lerp(cell, float4(_KeyGrid.x + keyCol * _KeyGrid.z, _KeyGrid.y - keyRow * _KeyGrid.w, _KeyCell.x, _KeyCell.y), isKey);
                cuv = lerp(cuv, saturate(kuv), isKey);
                // every texture read and screen derivative happens here, before any state-dependent maths
                float4 tex = tex2D(_MainTex, cell.xy + cuv * cell.zw);
                float aa = max(fwidth(uv.y) * _QuadRef.y, 1e-3);            // design px per screen px (vessel)
                float aaM = max(fwidth(uv.y) * _Button.y, 1e-3);            // design px per screen px (mark)
                float aaK = max(fwidth(uv.y) * _Key.x, 1e-3);               // design px per screen px (key glyph)
                // impact frame: the vector from this pixel to the impact point, carried from the pixel frame of the overlay
                // target into the quad's own frame (x right, y up, design px) by the uv derivatives
                float2 quadPx = lerp(float2(_QuadRef.x, _QuadRef.y), float2(_Button.y, _Button.y), isMark);
                float2 toPixel = (_OhImpactHudPoint308.xy - OhHudScreenUv(IN.vertex)) * _ScreenParams.xy;
                float2 toQuad = float2(dot(float2(ddx(uv.x), ddy(uv.x)), toPixel), dot(float2(ddx(uv.y), ddy(uv.y)), toPixel)) * quadPx;
                float toLength = length(toQuad);
                float2 toImpact = toLength > 1e-4 ? toQuad / toLength : float2(0.0, 1.0);

                float3 paper = _PaperColor.rgb;
                float3 ink = _InkColor.rgb;
                float3 lacquer = _Lacquer.rgb;
                float3 tint = IN.color.rgb;
                // unit direction to the impact point x the strength HudVessels308 lets through; nothing without a live impact
                float2 light = toImpact * saturate(IN.extra.y) * step(1e-4, _OhImpact308.z);

                // ================= vessel (code 0 ink, 1 HP)
                float W = _QuadRef.x;
                float H = _QuadRef.y;
                float2 q = float2((uv.x - 0.5) * W, uv.y * H);              // design px, origin = bottom centre of the quad
                float d = (tex.r - 0.5) * 2.0 * _QuadRef.w;                 // signed distance to the glass (< 0 inside)
                float dIn = d + _Glass2.x;                                  // ... to the cavity wall
                float inside = saturate(0.5 - d / aa);
                float cavity = saturate(0.5 - dIn / aa);
                float isHp = step(0.5, code) * (1.0 - isMark);
                float isInk = 1.0 - step(0.5, code);
                float low = saturate(IN.marks.w);
                float level = saturate(IN.liquid.x);
                float hasLiquid = step(0.004, level);
                float wall = saturate(1.0 + dIn / max(_Meniscus.w, 0.01)); // 1 at the cavity wall, 0 from _Meniscus.w px inside
                float climb = _Meniscus.z * wall * wall;                    // the meniscus climbs the glass
                // the tilt turns about the vessel centre: the centre column keeps the value (AC-H3.4)
                float surf = lerp(_Level.x, _Level.y, level) * H + IN.liquid.y * q.x
                    + IN.liquid.z * sin(q.x / max(_Level.z, 1.0) * 6.2831853 + IN.liquid.w) + climb;
                float below = saturate((surf - q.y) / aa + 0.5);

                // outward direction of the egg (ellipse gradient): which side faces the impact point
                float2 grad = float2(q.x / (W * W), (q.y - 0.5 * H) / (H * H));
                float2 nrm = grad / max(length(grad), 1e-6);
                float facing = dot(nrm, light);
                float litSide = saturate(facing);
                float farSide = saturate(-facing);

                float3 liquid = lerp(tint, ink, isHp * low * _Low.x);       // low HP sinks towards ink (no flash, no pulse)

                float4 o = float4(0.0, 0.0, 0.0, 0.0);
                // 1 paper rim outside the glass; the side away from an impact light turns towards ink
                float3 rimCol = lerp(paper, ink, _Impact.w * farSide);
                o = Over(o, rimCol, Remap(rimCol, Band(d, 0.0, _Glass.y, aa) * _Glass2.y));
                // 2 empty glass: a faint paper wash
                o = Over(o, paper, Remap(paper, inside * _Glass.z));
                // 2b the thickness of the glass: a paper value in the gap between the ink line and the liquid, so ink in the vessel
                //    never fuses with the ink line on dark ground (concept sheet; _Look.x = 0 turns it off)
                o = Over(o, paper, Remap(paper, Band(d, -_Glass2.x - 0.2, -1.2, aa) * _Look.x));
                // 3 wet film left on the glass above the surface (marks.y > 0); thinner towards its top. Like the #304 cinnabar
                //   lag it is a coloured translucent layer, so it is NOT remapped (QA2: the dark remap made α.34 read as α.60)
                float markY = lerp(_Level.x, _Level.y, saturate(IN.marks.x)) * H;
                float wetSpan = max(markY + climb - surf, 1.0);
                float wetFade = lerp(1.0, 0.4, saturate((q.y - surf) / wetSpan));
                float wetA = max(IN.marks.y, 0.0) * saturate((markY + climb - q.y) / aa + 0.5) * (1.0 - below) * cavity
                    * lerp(0.55, 1.0, tex.a) * wetFade;
                o = Over(o, liquid, wetA);
                // 4 dried tide rings on the wall above a low HP level (HP only: marks.z = the danger level). Fixed levels, no motion
                float tideTop = saturate(IN.marks.z);
                float ring = saturate(1.0 + dIn / 7.0);
                float tideY0 = lerp(_Level.x, _Level.y, tideTop) * H + climb;
                float tideY1 = lerp(_Level.x, _Level.y, tideTop * 0.72) * H + climb;
                float tideY2 = lerp(_Level.x, _Level.y, tideTop * 0.44) * H + climb;
                float tide = saturate(1.0 - abs(q.y - tideY0) / 1.2) * step(0.5, _Low.z)
                    + saturate(1.0 - abs(q.y - tideY1) / 1.2) * step(1.5, _Low.z)
                    + saturate(1.0 - abs(q.y - tideY2) / 1.2) * step(2.5, _Low.z);
                float tideA = isHp * saturate(low * 6.0) * _Low.y * saturate(tide) * ring * (1.0 - below) * cavity;
                float3 tideCol = lerp(tint, ink, 0.5);
                o = Over(o, tideCol, Remap(tideCol, tideA));
                // 5 liquid body: darker pooling edge by the glass, slightly lighter core
                float pool = saturate(1.0 + dIn / max(_Pool.x, 0.01));
                float3 body = liquid * (1.0 - _Pool.y * pool);
                body = lerp(body, paper, _Pool.z * (1.0 - pool));
                //   thick ink settles: a value step down towards the floor (not a gloss gradient; _Look.y = 0 turns it off)
                float baseY = lerp(_Level.x, _Level.y, level) * H;
                body *= 1.0 - _Look.y * saturate((baseY - q.y) / max(baseY - _Level.x * H, 6.0));
                //   fresh share (marks.y < 0): the band between the old level (marks.x) and the surface is lighter
                float fresh = saturate(-IN.marks.y) * saturate((q.y - markY) / aa + 0.5);
                body = lerp(body, paper, _Pool.w * fresh);
                //   impact light: at most _Impact.z of paper on the side facing it (hue kept, surface line untouched)
                float2 across = float2(q.x / (0.5 * W), (q.y - 0.5 * H) / (0.5 * H));
                body = lerp(body, paper, _Impact.z * saturate(dot(across, light)));
                float bodyCov = cavity * below * hasLiquid;
                //   ink below one spell cost: the body splits into dry-brush streaks, paper shows through
                float split = smoothstep(_Low.w - 0.06, _Low.w + 0.06, tex.a);
                bodyCov *= lerp(1.0, split, isInk * low);
                o = Over(o, body, Remap(body, bodyCov * _Glass2.w));
                // 5b the far half of the surface seen through the glass: a thin paler lens ABOVE the value line, thinner by the
                //    wall and the floor, closed by a faint paper line. The value is still read at the surface line (layer 9).
                //    Not drawn while the ink is split into dry-brush streaks. _Lens.xy = 0 turns it off
                float lensH = lerp(_Lens.y, _Lens.x, isHp) * sqrt(saturate(-dIn / max(_Lens.w, 1.0)));
                float lensOn = cavity * hasLiquid * (1.0 - isInk * low) * saturate(lensH);
                float3 lensCol = lerp(liquid, paper, _Lens.z);
                o = Over(o, lensCol, Remap(lensCol, lensOn * (1.0 - below) * saturate((surf + lensH - q.y) / aa + 0.5) * 0.92));
                o = Over(o, paper, Remap(paper, lensOn * saturate(1.0 - abs(q.y - surf - lensH) / 0.8) * step(0.6, lensH) * _Look.w));
                // 6 cost of the stroke being drawn (ink only): dashed line at the level left after paying
                float costLevel = abs(IN.marks.z);
                float costY = lerp(_Level.x, _Level.y, saturate(costLevel)) * H;
                float period = max(_Cost.x + _Cost.y, 0.5);
                float dash = step(frac(q.x / period + 0.5), _Cost.x / period);
                float costA = isInk * step(1e-4, costLevel) * cavity * saturate(1.0 - abs(q.y - costY) / max(_Cost.z, 0.1)) * dash * _Cost.w;
                float3 costCol = lerp(paper, _AlertColor.rgb, step(IN.marks.z, -1e-4));
                o = Over(o, costCol, Remap(costCol, costA));
                // 7 pour thread from the neck down to the surface while the level rises
                float thread = saturate((0.5 * _Pour.x - abs(q.x)) / aa + 0.5) * saturate((q.y - surf) / aa + 0.5)
                    * saturate((_Level.w * H - q.y) / aa + 0.5);
                o = Over(o, liquid, Remap(liquid, saturate(IN.extra.w) * thread * inside * (1.0 - isMark)));
                // 8 impact: ink shadow crescent inside the far wall. Drawn UNDER the surface line (review 2026-10-04: over it, the
                //   9 px band hid the ends of the value line for the impact cells; D308-10b wants HP / ink readable throughout)
                float shadowA = inside * saturate(1.0 + d / max(_Impact.x, 0.01)) * farSide * _Impact.y;
                o = Over(o, ink, Remap(ink, shadowA));
                // 9 surface line (paper value): the value is read here, nothing darkens it
                float surfA = cavity * hasLiquid * saturate(1.0 - abs(q.y - surf) / max(_Meniscus.x, 0.1)) * _Meniscus.y;
                o = Over(o, paper, Remap(paper, surfA));
                // 10 highlight stroke: paper value, static, no gradient
                o = Over(o, paper, Remap(paper, tex.b * _Glass.w * inside));
                // 11 glass line: ink; paper on the side facing the impact point
                //    brush pressure: the line swells towards the lower left and thins towards the upper right (_Look.z = 0: even)
                float3 glassCol = lerp(ink, paper, litSide);
                float press = 1.0 + _Look.z * dot(nrm, float2(-0.469, -0.883));
                o = Over(o, glassCol, Remap(glassCol, Band(d, -_Glass.x * press, 0.0, aa) * _Glass2.z));
                // 12 theme: the lacquer neck band and its cut-shell line. Drawn last: the band is outside the glass (the pour
                //    thread passes behind it; the lip above it is still glass). One hue family per cut piece, picked from the
                //    piece's index, and a slow swing inside the piece: a still colour, no time term
                float lacquerA = saturate(tex.g * 2.0) * _Theme.x * (1.0 - isMark);
                float nacreA = saturate(tex.g * 2.0 - 1.0) * _Theme.x * (1.0 - isMark);
                o = Over(o, lacquer, Remap(lacquer, lacquerA));
                float piece = floor((q.x + _NacreCut.x) / max(_NacreCut.y, 0.5)) + 2.0 * isInk;
                float family = frac(piece * 0.618034 + 0.31);
                float hue = lerp(lerp(_NacreHue.x, _NacreHue.y, step(_NacreArc.w, family)), _NacreHue.z, step(_Theme.w, family))
                    + _NacreHue.w * sin(q.x * (6.2831853 / max(_NacreArc.z, 1.0)) + piece * 2.4);
                //    an impact light never raises the shell: the side away from it sinks towards ink, like the paper rim
                float3 collarShell = lerp(Nacre(hue), ink, _Impact.w * farSide);
                o = Over(o, collarShell, Remap(collarShell, nacreA));

                // ================= mark (code 2..5): ink glyph + paper rim. wet = usable, dry = not now, re-wetting = coming back
                float wet = saturate(IN.liquid.x);
                float refill = saturate(IN.extra.w);
                float rewet = (1.0 - smoothstep(refill - _Mark.z, refill + _Mark.z, tex.b)) * step(1e-3, refill);
                float wetness = saturate(max(wet, rewet));
                float dryA = saturate(_Mark.x * lerp(0.55, 1.45, tex.a));    // dry-brush bristles, mean = _Mark.x
                float facingM = dot((uv - 0.5) * 2.0, light);
                float3 markRim = lerp(paper, ink, _Impact.w * saturate(-facingM));
                float4 m = float4(0.0, 0.0, 0.0, 0.0);
                m = Over(m, markRim, Remap(markRim, saturate(tex.g * (1.0 + saturate(facingM))) * _Mark.y));
                m = Over(m, tint, Remap(tint, tex.r * lerp(dryA, 1.0, wetness)));

                // ================= mark as a najeon lid (theme): a round lacquer lid, the cut-shell line by its rim, the pictogram
                // in shell pieces. seated = usable, fallen out (the seat shows at _Theme.z) = not now, seated again in stroke order =
                // coming back. The rim line is the lid's frame and never dims. No paper rim, no ink glyph, no vertex colour.
                float2 pm = (uv - 0.5) * _Button.y;                          // design px from the lid centre, y up
                float rm = length(pm);
                float disc = saturate((_Button.x - rm) / aaM + 0.5);
                float rimLine = saturate((rm - _Button.w) / aaM + 0.5);
                float isJump = step(2.5, code) * (1.0 - step(3.5, code));
                float order = saturate(lerp(pm.x, pm.y, isJump) / (2.0 * max(_Button.w, 1.0)) + 0.5);   // along the run; upwards for jump
                float reseat = (1.0 - smoothstep(refill - _Mark.z, refill + _Mark.z, order)) * step(1e-3, refill);
                float seated = saturate(max(wet, reseat));
                float facingL = dot(pm / max(rm, 1e-3), light);
                //   impact: the lit edge of the lid takes the paper value, the shell on the far side sinks towards ink
                float3 shell = lerp(FromCode(tex.rgb), ink, _Impact.w * saturate(-facingL));
                float4 n = float4(0.0, 0.0, 0.0, 0.0);
                n = Over(n, lacquer, Remap(lacquer, disc));
                n = Over(n, paper, Remap(paper, Band(rm, _Button.x - _Glass.y, _Button.x, aaM) * saturate(facingL)));
                n = Over(n, shell, Remap(shell, tex.a * disc * lerp(lerp(_Theme.z, 1.0, seated), 1.0, rimLine)));
                m = lerp(m, n, step(0.5, _Theme.y));

                // ================= key glyph (code 6, D308-11b): the key that triggers the mark it hangs on. Lacquer keycap (radius 0:
                // the keycap is the one precise right angle of this UI), paper hairline, paper lip along the lower edge, one symbol.
                // wet = its mark can be used; a dry mark dims the symbol to _Key.w. No time term, no impact reaction: it is never
                // hidden or changed by reduced motion, the flash setting or an impact frame.
                float dk = max(abs(pk.x), abs(pk.y)) - 0.5 * _Key.x;        // signed distance to the keycap edge (< 0 inside)
                float faceK = saturate(0.5 - dk / aaK);
                float innerK = saturate(0.5 - (dk + _Key.y) / aaK);         // inside the hairline
                float lipK = innerK * saturate((_Key.y + _Key.z - 0.5 * _Key.x - pk.y) / aaK + 0.5);
                float4 kq = float4(0.0, 0.0, 0.0, 0.0);
                kq = Over(kq, lacquer, Remap(lacquer, faceK));
                kq = Over(kq, paper, Remap(paper, lipK * _Key2.w));
                kq = Over(kq, paper, Remap(paper, (faceK - innerK) * _Key2.z));
                kq = Over(kq, paper, Remap(paper, tex.a * keyIn * innerK * lerp(_Key.w, 1.0, wet)));

                float4 res = lerp(lerp(o, m, isMark), kq, isKey);
                half4 color;
                color.rgb = saturate(res.rgb);          // LDR clamp; nothing here is emissive
                color.a = saturate(res.a) * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                half2 clipMask = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= clipMask.x * clipMask.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
