// #308 brush strips of both maps (SPEC-MAP-OVERHAUL-308 §2, §6.2): roads, ridges, cliffs, walls, streams, bridges, ticks.
// One uGUI graphic (MapStrokes308Graphic) on the stroke atlas: 8 rows x 64 px, u = along the stroke (repeats), A = ink
// cover, R = the paper band under a road. Vertex payload: uv0 = (arc length / tile, row v), uv1 = (world u, world v, class
// ink alpha, layer: 0 = ink, 1 = paper band + ink, 2 = paper band only), colour alpha = class ink alpha x canvas group alpha.
// Roads are laid as two runs of strips (every paper band, then every ink), so one road's band never cuts another's ink.
// The fragment is drawn only on walked land: MapFog308_Edge is the paper's own crisp walked edge on the same _FogTex (the
// same thresholds, _S308Edge = the paper's _M308Edge), so a strip ends exactly where the paper's land ends and a newly walked
// cell shows its strips with no mesh rebuild. The window is clipped here (no Mask / RectMask2D): the view
// rectangle in world uv, turned with the map when the minimap follows the view. LDR only: no emission, no HDR property,
// the colour is a blend of two sRGB tokens.
Shader "UI/MapStroke308"
{
    Properties
    {
        [PerRendererData] _MainTex("Stroke atlas (A ink, R paper band)",2D)="black"{}
        _FogTex("Discovery (the paper's fog texture)",2D)="black"{}
        _FogSoft("Rounded walked-land edges",Range(0,1))=1
        _FogNoise("Walked-land edge wobble",Float)=.22
        _S308Ink("Ink (sRGB values)",Vector)=(.078,.078,.075,1)
        _S308Paper("Paper band (sRGB values)",Vector)=(.875,.859,.816,1)
        _S308View("View in world uv: min u, min v, 1 / width, 1 / height",Vector)=(0,0,1,1)
        _S308Turn("Map turn cos, sin; window width / height",Vector)=(1,0,1,0)
        _S308Frame("Window px: width, height; edge feather px; paper band alpha",Vector)=(294,210,1,1)
        _S308Opt("fog on, window clip on, linear ink gamma, max v footprint",Vector)=(1,1,2.2,.008)
        _S308Edge("crisp walked edge: threshold from, threshold span, noise cells 1, noise cells 2 (= the paper's _M308Edge)",Vector)=(.14,.42,1.6,3.7)
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
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "MapFog308.cginc"
            struct appdata {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float4 data:TEXCOORD1;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float4 data:TEXCOORD1;float4 world:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO};
            sampler2D _MainTex;
            float4 _S308Ink,_S308Paper,_S308View,_S308Turn,_S308Frame,_S308Opt,_S308Edge,_ClipRect;fixed4 _Color;
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.data=v.data;o.color=v.color*_Color;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                // Narrow strips minify the 52 px row a lot; an unclamped mip would pull the neighbouring rows in (the pads are
                // 6 px). The gradients are scaled down together so the footprint across the row stays under _S308Opt.w.
                float2 dx=ddx(i.uv);float2 dy=ddy(i.uv);
                float across=max(abs(dx.y),abs(dy.y));
                float keep=min(1,_S308Opt.w/max(across,1e-6));
                fixed4 tex=tex2Dgrad(_MainTex,i.uv,dx*keep,dy*keep);
                float2 worldUv=i.data.xy;
                // the paper's crisp walked edge: the same field, the same 1 px of anti-aliasing (nothing outside the rim, #214)
                float walk=MapFog308_Edge(worldUv,_S308Edge).x;
                float known=lerp(1,saturate(walk/max(length(float2(ddx(walk),ddy(walk))),1e-5)+.5),saturate(_S308Opt.x));
                // window clip in frame space: the frame never turns, the map (and these strips) may (follow view)
                float asp=max(_S308Turn.z,1e-3);
                float2 q=((worldUv-_S308View.xy)*_S308View.zw-.5)*float2(asp,1);
                q=float2(q.x*_S308Turn.x-q.y*_S308Turn.y,q.x*_S308Turn.y+q.y*_S308Turn.x)/float2(asp,1);
                float2 edgePx=(.5-abs(q))*_S308Frame.xy;
                float window=saturate(min(edgePx.x,edgePx.y)/max(_S308Frame.z,1e-3)+.5);
                window=lerp(1,window,saturate(_S308Opt.y));
                // colour alpha = class ink alpha x inherited canvas alpha; uv1.z carries the class alpha alone
                float group=saturate(i.color.a/max(i.data.z,.004));
                float ink=saturate(tex.a*i.data.z)*saturate(2-i.data.w);
                float band=saturate(tex.r)*saturate(i.data.w)*saturate(_S308Frame.w);
                fixed3 inkRgb=_S308Ink.rgb;fixed3 paperRgb=_S308Paper.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                // The blend runs in linear light but the notation's values are sRGB composites. The ink alpha is remapped so that
                // ink over the paper token gives exactly lerp(paper, ink, a) in sRGB (the offline twin's composite); over a
                // darker ground (slope wash, the walked edge) it stays close.
                float g=max(_S308Opt.z,1);
                float paperS=max(dot(paperRgb,fixed3(.2126,.7152,.0722)),1e-4);
                float inkS=max(dot(inkRgb,fixed3(.2126,.7152,.0722)),1e-4);
                float paperL=pow(paperS,g);
                float inkL=pow(inkS,g);
                ink=saturate((paperL-pow(max(lerp(paperS,inkS,ink),1e-4),g))/max(paperL-inkL,1e-4));
                inkRgb=GammaToLinearSpace(inkRgb);paperRgb=GammaToLinearSpace(paperRgb);
                #endif
                // the paper band first, the ink over it, as one layer
                float a=1-(1-band)*(1-ink);
                fixed3 rgb=(paperRgb*band*(1-ink)+inkRgb*ink)/max(a,1e-4);
                fixed4 result=fixed4(saturate(rgb)*i.color.rgb,saturate(a*known*window*group));
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
