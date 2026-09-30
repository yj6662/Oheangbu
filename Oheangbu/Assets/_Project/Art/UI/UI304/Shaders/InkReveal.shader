// #304 UI/InkReveal (IMPLEMENTATION §5.2). Based on the built-in UI/Default structure (stencil, RectMask2D, alpha clip).
// Reveal comes PER VERTEX so one shared material batches every underlay, veil, toast and shade:
//   TEXCOORD1 = (reveal 0..1, rectU, rectV, (mode + 1) * 1024 + aspect)   written by InkRevealEffect (BaseMeshEffect).
//   TEXCOORD1.w == 0 (no InkRevealEffect, or the canvas lacks TexCoord1) -> material _Reveal / _DirMode, sprite uv as rect uv.
// Noise is sampled in CANVAS units (vertex position / _NoiseTile), so its grain is the same on a 78 px underlay, a toast and
// the 3200 px veil (a rect-relative uv would minify the 1024 px wash_tile ~13x on small strokes and sparkle).
// Mask: m = noise                                  (mode 0, 번짐 bleed)
//       m = lerp(1 - u, noise, _NoiseAmt)          (mode 1, 좌->우 wipe in the stroke direction)
//       m = lerp(1 - edgeDist * 2, noise, _NoiseAmt) (mode 2, 가장자리->안, rest / death shade)
//   t = lerp(1 + _Edge, -_Edge, reveal); alpha *= smoothstep(t - _Edge, t + _Edge, m)
//   => reveal 0 is fully hidden, reveal 1 is fully drawn, high m appears first.
Shader "UI/InkReveal"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _NoiseTex ("Noise (wash_tile, alpha)", 2D) = "white" {}
        _NoiseTile ("Noise tile size (canvas units per repeat)", Float) = 640
        _NoiseAmt ("Noise Amount (wipe / edges)", Range(0, 1)) = 0.3
        _Edge ("Edge Softness", Range(0.001, 0.5)) = 0.08
        _LinearInkGamma ("Linear-space ink gamma (1 = off; sRGB mockup match)", Range(1, 3)) = 1.8
        _LinearPaperGamma ("Linear-space paper gamma (1 = off)", Range(1, 3)) = 1.3
        [Enum(Bleed,0,Wipe,1,Edges,2)] _DirMode ("Direction without InkRevealEffect", Float) = 1
        _Reveal ("Reveal without InkRevealEffect", Range(0, 1)) = 1

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
                float4 ink           : TEXCOORD3;   // x rectU, y rectV, z reveal, w mode
                float2 noiseUV       : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            sampler2D _NoiseTex;
            float4 _NoiseTex_ST;
            float _NoiseTile;
            float _NoiseAmt;
            float _Edge;
            float _LinearInkGamma;
            float _LinearPaperGamma;
            float _DirMode;
            float _Reveal;

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
                OUT.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                {
                    v.color.rgb = UIGammaToLinear(v.color.rgb);
                }
                OUT.color = v.color * _Color;

                // decode the InkRevealEffect payload (constant over one graphic)
                float code = floor(v.payload.w / 1024.0);   // (mode + 1); the remainder (rect aspect) is informational
                float hasFx = step(0.5, code);
                float2 rectUV = lerp(v.texcoord.xy, v.payload.yz, hasFx);
                float reveal = lerp(_Reveal, v.payload.x, hasFx);
                float mode = lerp(_DirMode, code - 1.0, hasFx);
                OUT.ink = float4(rectUV, reveal, mode);
                float2 nuv = v.vertex.xy / max(_NoiseTile, 1.0);
                OUT.noiseUV = TRANSFORM_TEX(nuv, _NoiseTex);
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Round up the alpha color coming from the interpolator (to 1.0/256.0 steps), as UI/Default does.
                const half alphaPrecision = half(0xff);
                const half invAlphaPrecision = half(1.0 / alphaPrecision);
                IN.color.a = round(IN.color.a * alphaPrecision) * invAlphaPrecision;

                half4 color = IN.color * (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);

                float u = IN.ink.x;
                float w = IN.ink.y;
                float reveal = saturate(IN.ink.z);
                float mode = IN.ink.w;
                float n = tex2D(_NoiseTex, IN.noiseUV).a;
                float wipe = 1.0 - saturate(u);
                float edges = 1.0 - saturate(min(min(u, 1.0 - u), min(w, 1.0 - w)) * 2.0);
                float dir = mode > 1.5 ? edges : wipe;
                float m = mode < 0.5 ? n : lerp(dir, n, _NoiseAmt);
                float t = lerp(1.0 + _Edge, -_Edge, reveal);
                color.a *= smoothstep(t - _Edge, t + _Edge, m);
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
