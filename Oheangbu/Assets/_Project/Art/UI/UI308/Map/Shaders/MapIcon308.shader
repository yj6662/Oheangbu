// #308 map icons (SPEC-MAP-OVERHAUL-308 §3): one cell of the icon atlas per quad (MapGlyph308Graphic). The atlas holds the
// ink drawing in R, the hanji rim in G and a SECOND ink in B (A = max(R, G)); this shader lays the rim (material colour,
// alpha from uv1.x) under the drawing (vertex colour: ink, cinnabar for the player mark, nacre on a lacquer plate) and the
// second ink over it (_Ink2 = the ink token, or the rim colour on a dark plate: uv1.z; its alpha: uv1.y), so an icon is one
// quad, not two Images, and every icon shares one material. Only the brush mark of the current position has a second ink
// (ferrule, handle, a thin edge round the cinnabar tuft); B = 0 everywhere else, and R holds the whole figure, so the result
// for every other glyph is what it was. _InkChan / _RimChan / _Ink2Chan pick the channels, so the same shader draws a
// channel of the pattern texture for the legend samples (forest dots, water ripples: _Ink2Chan = 0 there).
// LDR only: no emission, no HDR property.
Shader "UI/MapIcon308"
{
    Properties
    {
        [PerRendererData] _MainTex("Icon atlas (R ink, G rim)",2D)="black"{}
        _Rim("Rim (hanji)",Color)=(.875,.859,.816,1)
        _InkChan("Ink channel weights",Vector)=(1,0,0,0)
        _RimChan("Rim channel weights",Vector)=(0,1,0,0)
        _Ink2Chan("Second ink channel weights",Vector)=(0,0,1,0)
        _Ink2("Second ink (the ink token)",Color)=(.078,.078,.075,1)
        _I308Gamma("Linear-space ink gamma (dark ink only)",Float)=2.2
        _Color("Tint",Color)=(1,1,1,1)
        _StencilComp("Stencil Comparison",Float)=8
        _Stencil("Stencil ID",Float)=0
        _StencilOp("Stencil Operation",Float)=0
        _StencilWriteMask("Stencil Write Mask",Float)=255
        _StencilReadMask("Stencil Read Mask",Float)=255
        _ColorMask("Color Mask",Float)=15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip",Float)=0
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False"}
        Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float4 data:TEXCOORD1;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float4 data:TEXCOORD1;float4 world:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO};
            sampler2D _MainTex;
            fixed4 _Rim,_Color,_Ink2;float4 _InkChan,_RimChan,_Ink2Chan,_ClipRect;float _I308Gamma;
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.data=v.data;o.color=v.color*_Color;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 tex=tex2D(_MainTex,i.uv);
                float ink=saturate(dot(tex,_InkChan));
                float rim=saturate(dot(tex,_RimChan))*saturate(i.data.x)*_Rim.a;
                // second ink: over the first, in the ink token (uv1.z = 0) or the rim colour (uv1.z = 1: a dark plate under it)
                float ink2=saturate(dot(tex,_Ink2Chan))*saturate(i.data.y);
                fixed3 ink2Rgb=lerp(_Ink2.rgb,_Rim.rgb,saturate(i.data.z));
                #ifndef UNITY_COLORSPACE_GAMMA
                // a dark drawing is composited as in sRGB (see UI/MapStroke308); a light one (nacre on lacquer) is left alone
                float dark=saturate(1-dot(i.color.rgb,fixed3(.2126,.7152,.0722))*3);
                ink=lerp(ink,1-pow(saturate(1-ink),max(_I308Gamma,1)),dark);
                float dark2=saturate(1-dot(ink2Rgb,fixed3(.2126,.7152,.0722))*3);
                ink2=lerp(ink2,1-pow(saturate(1-ink2),max(_I308Gamma,1)),dark2);
                #endif
                // rim, then the first ink over it, then the second ink over both (premultiplied; ink2 = 0 gives the old result)
                float a=1-(1-rim)*(1-ink)*(1-ink2);
                fixed3 rgb=((_Rim.rgb*rim*(1-ink)+i.color.rgb*ink)*(1-ink2)+ink2Rgb*ink2)/max(a,1e-4);
                fixed4 result=fixed4(saturate(rgb),saturate(a*i.color.a));
                #ifdef UNITY_UI_CLIP_RECT
                result.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a-.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
