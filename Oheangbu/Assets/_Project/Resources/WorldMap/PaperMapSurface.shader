Shader "Oheangbu/UI/TwiceFoldedHanji"
{
    Properties
    {
        [PerRendererData] _MainTex("Worn hanji RGBA",2D)="white"{}
        _MapTex("World terrain",2D)="white"{}
        _FogTex("Discovery",2D)="black"{}
        _InkTex("Printed paths",2D)="black"{}
        _MacroInkTex("Known rivers",2D)="black"{}
        _KnownBase("Illustrated macro geography",Float)=0
        _WorldUv("World window",Vector)=(0,0,1,1)
        _MapWindow("Paper print area",Vector)=(.08,.08,.84,.84)
        _Interior("Interior",Float)=0
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
            struct appdata {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float4 face:TEXCOORD1;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float4 face:TEXCOORD1;float4 world:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO};
            sampler2D _MainTex,_MapTex,_FogTex,_InkTex,_MacroInkTex;
            float4 _WorldUv,_MapWindow,_ClipRect,_FogTex_TexelSize;fixed4 _Color;float _Interior,_KnownBase;
            float KnownPaper(float2 uv)
            {
                // Feather inward at discovered boundaries without revealing an unknown cell.
                float2 cell=frac(uv*_FogTex_TexelSize.zw);
                float known=1-tex2D(_FogTex,uv).a;
                float left=tex2D(_FogTex,uv-float2(_FogTex_TexelSize.x,0)).a;
                float right=tex2D(_FogTex,uv+float2(_FogTex_TexelSize.x,0)).a;
                float bottom=tex2D(_FogTex,uv-float2(0,_FogTex_TexelSize.y)).a;
                float top=tex2D(_FogTex,uv+float2(0,_FogTex_TexelSize.y)).a;
                known*=lerp(1,smoothstep(0,.36,cell.x),left);
                known*=lerp(1,smoothstep(0,.36,1-cell.x),right);
                known*=lerp(1,smoothstep(0,.36,cell.y),bottom);
                known*=lerp(1,smoothstep(0,.36,1-cell.y),top);
                return known;
            }
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.face=v.face;o.color=v.color*_Color;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 paper=tex2D(_MainTex,i.uv);
                float2 printUv=(i.uv-_MapWindow.xy)/_MapWindow.zw;
                float printInside=step(0,printUv.x)*step(printUv.x,1)*step(0,printUv.y)*step(printUv.y,1);
                float2 worldUv=_WorldUv.xy+printUv*_WorldUv.zw;
                float worldInside=step(0,worldUv.x)*step(worldUv.x,1)*step(0,worldUv.y)*step(worldUv.y,1);
                float front=1-step(.5,i.face.x);
                float known=KnownPaper(worldUv);
                known=lerp(known,1,saturate(_Interior));
                fixed4 terrainSample=tex2D(_MapTex,worldUv);
                fixed3 terrain=terrainSample.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                terrain=LinearToGammaSpace(terrain);
                #endif
                float luminance=dot(terrain,fixed3(.299,.587,.114));
                float relief=saturate((.69-luminance)*1.65)*(1-_Interior);
                // Terrain and strokes stain the fibers; unknown land never covers them with a flat panel.
                // Baked terrain uses low alpha for faint relief, not visibility of path ink.
                float landInside=step(.001,terrainSample.a);
                float inkMask=front*known*printInside*worldInside*lerp(landInside,1,saturate(_Interior));
                fixed3 surface=paper.rgb*(1-relief*inkMask);
                float illustrationMask=front*printInside*worldInside*landInside*_KnownBase*(1-_Interior);
                // The geographic illustration is known; only the detail strokes use discovery.
                fixed3 pigment=saturate(terrain/fixed3(.94,.91,.84));
                surface=lerp(surface,paper.rgb*pigment,illustrationMask);
                fixed4 macroInk=tex2D(_MacroInkTex,printUv);
                surface=lerp(surface,macroInk.rgb,macroInk.a*illustrationMask);
                fixed4 ink=tex2D(_InkTex,printUv);
                surface=lerp(surface,lerp(surface*ink.rgb*.8,ink.rgb,_KnownBase),ink.a*inkMask);
                surface*=lerp(1,.90,1-front);
                fixed4 result=fixed4(surface*i.color.rgb,paper.a*i.color.a);
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
