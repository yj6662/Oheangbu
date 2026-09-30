// #304 UI/InkMeter (IMPLEMENTATION §5.1, numpy twin: design/FINAL/assets/make_assets.py inkmeter()).
// Based on the built-in UI/Default structure (stencil, RectMask2D, alpha clip). The rect is the FULL (max) stroke.
// Per vertex (InkMeterGraphic): TEXCOORD0 = rect uv 0..1, TEXCOORD1 = (fill 0..1, rectW px, rectH px, mode).
// TEXCOORD1.y == 0 (a plain Image) -> material _Fill / _RectW / _RectH.
//   px = u * W, f = fill * W, tail = (_TailPx > 0 ? _TailPx : H), t0 = f - tail
//   body u = px / max(W, H * _BodyAspect)      (keeps meter_body's aspect; longer meters stretch the body)
//   tail u = (px - t0) / tail, r = saturate((px - t0) / _BlendPx)
//   a = lerp(body, tail, r)  (px < t0 -> body), and nothing at px >= f: the split bristles END at the value.
// mode 1 (ghost edge) = (rim) - (cut body/tail): only the paper edge of the 담묵 ghost. mode = max(vertex mode, _Mode).
Shader "UI/InkMeter"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _BodyTex ("Body (meter_body or meter_body_rim)", 2D) = "white" {}
        _TailTex ("Tail (meter_tail or meter_tail_rim)", 2D) = "white" {}
        _CutBodyTex ("Ghost-edge cut body (meter_body)", 2D) = "black" {}
        _CutTailTex ("Ghost-edge cut tail (meter_tail)", 2D) = "black" {}
        _Fill ("Fill without InkMeterGraphic", Range(0, 1)) = 1
        _TailPx ("Tail px (0 = rect height)", Float) = 0
        _BlendPx ("Body to tail blend px", Float) = 16
        _BodyAspect ("Body texture aspect (1200/64)", Float) = 18.75
        _RectW ("Rect width without InkMeterGraphic", Float) = 500
        _RectH ("Rect height without InkMeterGraphic", Float) = 42
        [Enum(Value,0,GhostEdge,1)] _Mode ("Mode", Float) = 0
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

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 payload  : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 mask          : TEXCOORD2;
                float4 meter         : TEXCOORD3;   // x fill, y W, z H, w mode
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            sampler2D _BodyTex;
            sampler2D _TailTex;
            sampler2D _CutBodyTex;
            sampler2D _CutTailTex;
            float _Fill;
            float _TailPx;
            float _BlendPx;
            float _BodyAspect;
            float _RectW;
            float _RectH;
            float _Mode;
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

                float hasPayload = step(0.5, v.payload.y);
                float4 fallback = float4(_Fill, _RectW, _RectH, 0);
                float4 meter = lerp(fallback, v.payload, hasPayload);
                meter.w = max(meter.w, _Mode);
                OUT.meter = meter;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                const half alphaPrecision = half(0xff);
                const half invAlphaPrecision = half(1.0 / alphaPrecision);
                IN.color.a = round(IN.color.a * alphaPrecision) * invAlphaPrecision;

                float W = max(IN.meter.y, 1.0);
                float H = max(IN.meter.z, 1.0);
                float px = IN.texcoord.x * W;
                float f = saturate(IN.meter.x) * W;
                float tailPx = _TailPx > 0.0 ? _TailPx : H;
                float t0 = f - tailPx;
                float bodyLen = max(W, H * _BodyAspect);
                float2 bodyUV = float2(px / bodyLen, IN.texcoord.y);
                float2 tailUV = float2(saturate((px - t0) / tailPx), IN.texcoord.y);
                float r = saturate((px - t0) / max(_BlendPx, 0.001));

                half a = lerp(tex2D(_BodyTex, bodyUV).a, tex2D(_TailTex, tailUV).a, r);
                half cut = lerp(tex2D(_CutBodyTex, bodyUV).a, tex2D(_CutTailTex, tailUV).a, r);
                a = lerp(a, saturate(a - cut), step(0.5, IN.meter.w));
                a *= step(px, f);

                half4 color = IN.color;
                color.a *= a;
                #ifndef UNITY_COLORSPACE_GAMMA
                // #304: uGUI composites in linear light, the design alphas are sRGB composites (mockups). Remap alpha so an
                // ink layer over paper/world (1-(1-a)^g) and a paper layer over ink (a^g) land where the mockup puts them.
                {
                    half lum = dot(color.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                    half g = max(1.0h, (half)_LinearInkGamma);
                    half ca = saturate(color.a);
                    half darkA = 1.0h - pow(1.0h - ca, g);
                    half lightA = pow(ca, max(1.0h, (half)_LinearPaperGamma));
                    color.a = lerp(darkA, lightA, saturate((lum - 0.2h) * 2.5h));
                }
                #endif


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
