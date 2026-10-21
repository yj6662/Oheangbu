// #308 D308-125 5 (c) UI/VesselSmoke308: soot in the empty head of the HP vessel while the player is scorched. One quad = the
// HP vessel's own quad (VesselSmoke308: same rect and margin, drawn just before the vessel, so its liquid covers the soot below
// the surface and its glass wash lies over the rest). The cavity comes from the vessel's own SDF cell of hud308_atlas (the same
// cell, quad and wall numbers as UI/InkVessel308, copied onto this material by VesselSmoke308).
// ART-INK: dark value only - the soot colour times a coverage, no emission, no HDR, nothing brighter than the colour.
// D308-11b: nothing moves on its own. There is no time term; the noise is fixed to the quad. _Smoke (share of soot) and _Top
// (the soot's top: 1 = the neck top, 0 = the floor) are set by VesselSmoke308 only in answer to the scorch.
Shader "UI/VesselSmoke308"
{
    Properties
    {
        [PerRendererData] _MainTex ("Atlas (hud308_atlas, set per renderer)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _SootColor ("Soot (#2A2622)", Color) = (0.165, 0.149, 0.133, 1)
        _Smoke ("Share of soot 0..1 (VesselSmoke308)", Range(0, 1)) = 0
        _Top ("Soot top 0..1 (1 = neck top, 0 = floor)", Range(0, 1)) = 1
        _Soot ("Soot: puff px, alpha, top soft px, haze floor", Vector) = (18, 0.5, 6, 0.3)

        _CellVessel ("Atlas cell: vessel (uv x, y, w, h) = UI/InkVessel308", Vector) = (0.015625, 0.197266, 0.297852, 0.771484)
        _QuadRef ("Vessel quad: width px, height px, margin px, SDF range px = UI/InkVessel308", Vector) = (122, 158, 4, 16)
        _Level ("Level: floor uv, full uv, wave length px, neck top uv = UI/InkVessel308", Vector) = (0.06013, 0.82943, 102.6, 0.96835)
        _Glass2 ("Glass: wall px, rim alpha, outline alpha, liquid alpha = UI/InkVessel308", Vector) = (4, 0.72, 0.95, 0.96)
        _LinearInkGamma ("Linear-space ink gamma (1 = off) = UI/InkVessel308", Range(1, 3)) = 1.8

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

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 mask          : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            fixed4 _SootColor;
            float _Smoke;
            float _Top;
            float4 _Soot;
            float4 _CellVessel;
            float4 _QuadRef;
            float4 _Level;
            float4 _Glass2;
            float _LinearInkGamma;

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
                return OUT;
            }

            // fixed value noise (no time term): the soot keeps its shape on the glass
            inline float Hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            inline float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            inline float Fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += a * Noise(p); p = p * 2.03 + float2(17.1, 9.7); a *= 0.5; }
                return s / 0.9375;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                const half alphaPrecision = half(0xff);
                const half invAlphaPrecision = half(1.0 / alphaPrecision);
                IN.color.a = round(IN.color.a * alphaPrecision) * invAlphaPrecision;

                float2 uv = IN.texcoord;
                // the texture read and the derivative come first, before anything that depends on the state
                float4 tex = tex2D(_MainTex, _CellVessel.xy + saturate(uv) * _CellVessel.zw);
                float aa = max(fwidth(uv.y) * _QuadRef.y, 1e-3);            // design px per screen px
                float d = (tex.r - 0.5) * 2.0 * _QuadRef.w;                 // signed distance to the glass (< 0 inside)
                float cavity = saturate(0.5 - (d + _Glass2.x) / aa);        // inside the cavity wall

                // the soot's top, from the neck top (_Top = 1) down to the floor (_Top = 0), softened over _Soot.z px
                float floorUv = _Level.x, neckUv = _Level.w;
                float topUv = lerp(floorUv, neckUv, saturate(_Top));
                float topMask = saturate((topUv - uv.y) * _QuadRef.y / max(_Soot.z, 0.5));
                // soot gathers under the neck: thicker high, thinner low
                float rise = lerp(0.6, 1.0, saturate((uv.y - floorUv) / max(neckUv - floorUv, 1e-3)));

                float2 px = float2((uv.x - 0.5) * _QuadRef.x, uv.y * _QuadRef.y);
                float n = Fbm(px / max(_Soot.x, 1.0));
                float puff = lerp(_Soot.w, 1.0, smoothstep(0.45, 0.8, n));
                float a = puff * rise * _Soot.y * saturate(_Smoke) * cavity * topMask;

                #ifndef UNITY_COLORSPACE_GAMMA
                a = 1.0 - pow(max(1.0 - saturate(a), 0.0), max(1.0, _LinearInkGamma));   // an ink layer: the sRGB design alpha
                #endif

                half4 color = half4(saturate(_SootColor.rgb * IN.color.rgb), saturate(a) * IN.color.a);

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= m.x * m.y;
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
