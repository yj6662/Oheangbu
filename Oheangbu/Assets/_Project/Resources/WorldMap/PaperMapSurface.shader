Shader "Oheangbu/UI/TwiceFoldedHanji"
{
    Properties
    {
        [PerRendererData] _MainTex("Worn hanji RGBA",2D)="white"{}
        _MapTex("World terrain",2D)="white"{}
        _DetailTex("Surveyed terrain",2D)="white"{}
        _CaveTex("Cave plan",2D)="white"{}
        _CaveDiscovery("Walked passages",2D)="black"{}
        _ExploreCave("Reveal walked passages",Float)=0
        _HasDetail("Surveyed terrain available",Float)=0
        _HasCave("Cave plan available",Float)=0
        _CaveUv("Cave window",Vector)=(0,0,1,1)
        _FogTex("Discovery",2D)="black"{}
        _InkTex("Printed paths",2D)="black"{}
        _MacroInkTex("Known rivers",2D)="black"{}
        _KnownBase("Illustrated macro geography",Float)=0
        _PaintedRelief("Painted relief",Float)=0
        _UnknownVeil("Unwalked land veil (#304)",Range(0,1))=0
        _UnknownShade("Unwalked: monochrome share of the walked shading (#304 QA)",Range(0,1))=0
        _UnknownRelief("Unwalked: local relief ink gain (#304 QA)",Float)=0
        _UnknownReliefTexels("Unwalked: relief sample radius (map texels)",Float)=8
        _FogSoft("Rounded walked-land edges (#304 QA)",Range(0,1))=0
        _FogNoise("Walked-land edge wobble",Float)=.22
        _PaintedInk("Painted relief as a monochrome ink wash on the sheet (#304 QA2)",Range(0,1))=0
        _PaintedInkRange("Ink wash: no-ink lum, full-ink lum, max ink, zone neutraliser",Vector)=(.55,.36,.66,12)
        _WorldUv("World window",Vector)=(0,0,1,1)
        _MapTileUv("Map tile",Vector)=(0,0,1,1)
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
            sampler2D _MainTex,_MapTex,_DetailTex,_CaveTex,_CaveDiscovery,_FogTex,_InkTex,_MacroInkTex;
            float4 _WorldUv,_MapTileUv,_CaveUv,_MapWindow,_ClipRect,_FogTex_TexelSize,_MapTex_TexelSize;fixed4 _Color;float _ExploreCave,_Interior,_KnownBase,_PaintedRelief,_HasDetail,_HasCave,_UnknownVeil;
            float _UnknownShade,_UnknownRelief,_UnknownReliefTexels,_FogSoft,_FogNoise,_PaintedInk;float4 _PaintedInkRange;
            float Hash304(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float ValueNoise304(float2 p)
            {
                float2 i=floor(p);float2 f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(Hash304(i),Hash304(i+float2(1,0)),f.x),lerp(Hash304(i+float2(0,1)),Hash304(i+float2(1,1)),f.x),f.y);
            }
            float KnownPaper(float2 uv)
            {
                // Feather inward at discovered boundaries without revealing an unknown cell.
                float2 cell=frac(uv*_FogTex_TexelSize.zw);
                float here=1-tex2D(_FogTex,uv).a;
                float known=here;
                float left=tex2D(_FogTex,uv-float2(_FogTex_TexelSize.x,0)).a;
                float right=tex2D(_FogTex,uv+float2(_FogTex_TexelSize.x,0)).a;
                float bottom=tex2D(_FogTex,uv-float2(0,_FogTex_TexelSize.y)).a;
                float top=tex2D(_FogTex,uv+float2(0,_FogTex_TexelSize.y)).a;
                known*=lerp(1,smoothstep(0,.36,cell.x),left);
                known*=lerp(1,smoothstep(0,.36,1-cell.x),right);
                known*=lerp(1,smoothstep(0,.36,cell.y),bottom);
                known*=lerp(1,smoothstep(0,.36,1-cell.y),top);
                // #304 QA (_FogSoft): the 32 m cells read as square steps on the 740 px print. A bilinear field of the walked
                // cells (the fog texture is point sampled, so the four texel centres are read by hand) rounds the corners and a
                // world-anchored value noise wobbles the edge like the mockup's map_fog. Still inward only: `here` keeps every
                // unknown cell at 0, so no road or place ink leaks out of the walked land.
                float2 t=uv*_FogTex_TexelSize.zw-.5;
                float2 f=frac(t);
                float2 b=(floor(t)+.5)*_FogTex_TexelSize.xy;
                float a00=tex2D(_FogTex,b).a;
                float a10=tex2D(_FogTex,b+float2(_FogTex_TexelSize.x,0)).a;
                float a01=tex2D(_FogTex,b+float2(0,_FogTex_TexelSize.y)).a;
                float a11=tex2D(_FogTex,b+_FogTex_TexelSize.xy).a;
                float field=1-lerp(lerp(a00,a10,f.x),lerp(a01,a11,f.x),f.y);
                field+=(ValueNoise304(uv*_FogTex_TexelSize.zw*2.5)-.5)*_FogNoise;
                float soft=here*smoothstep(.5,.95,field);
                return lerp(known,soft,saturate(_FogSoft));
            }
            float MapLum304(float2 uv)
            {
                fixed3 c=tex2D(_MapTex,uv).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                c=LinearToGammaSpace(c);
                #endif
                return dot(c,fixed3(.299,.587,.114));
            }
            float4 _CaveDiscovery_TexelSize;

            float WalkedEdge(float2 uv)
            {
                float2 cell=frac(uv*_CaveDiscovery_TexelSize.zw);
                float k=tex2D(_CaveDiscovery,uv).r;
                k*=lerp(1,smoothstep(0,.8,cell.x),1-tex2D(_CaveDiscovery,uv-float2(_CaveDiscovery_TexelSize.x,0)).r);
                k*=lerp(1,smoothstep(0,.8,1-cell.x),1-tex2D(_CaveDiscovery,uv+float2(_CaveDiscovery_TexelSize.x,0)).r);
                k*=lerp(1,smoothstep(0,.8,cell.y),1-tex2D(_CaveDiscovery,uv-float2(0,_CaveDiscovery_TexelSize.y)).r);
                k*=lerp(1,smoothstep(0,.8,1-cell.y),1-tex2D(_CaveDiscovery,uv+float2(0,_CaveDiscovery_TexelSize.y)).r);
                return k;
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
                float walkedCells=KnownPaper(worldUv);
                float known=lerp(walkedCells,1,saturate(_Interior));
                fixed4 terrainSample=tex2D(_MapTex,worldUv);
                fixed4 detailSample=tex2D(_DetailTex,(worldUv-_MapTileUv.xy)/_MapTileUv.zw);
                terrainSample=lerp(terrainSample,detailSample,walkedCells*_HasDetail);
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
                fixed3 painted=terrain*lerp(fixed3(1,1,1),paper.rgb,.16);
                // #304 QA2 (_PaintedInk): the compact world's painted relief is an olive hillshade with cyan water and pink zone
                // patches; printed in colour it is the old minimap green plus two more accents (DESIGN §2.5). As an ink wash the
                // relief's own shading becomes ink on the sheet: none above _PaintedInkRange.x, full at .y, at most .z of the
                // sheet taken away; the magenta zone paint (red and blue above green, gain .w) prints no ink. The multiply is the
                // mockups' sRGB one (pow 2.2 into the linear target). Default 0 = the coloured relief as before.
                float zone=saturate((min(terrain.r,terrain.b)-terrain.g)*_PaintedInkRange.w);
                float washLum=lerp(luminance,_PaintedInkRange.x,zone);
                float density=saturate((_PaintedInkRange.x-washLum)/max(_PaintedInkRange.x-_PaintedInkRange.y,.001));
                float keep=saturate(1-density*_PaintedInkRange.z);
                #ifndef UNITY_COLORSPACE_GAMMA
                keep=pow(keep,2.2);
                #endif
                painted=lerp(painted,paper.rgb*keep,saturate(_PaintedInk));
                surface=lerp(surface,painted,illustrationMask*_PaintedRelief);
                // #304: only walked land keeps its ink; unwalked cells of the illustration sink under the sheet (default 0 = off)
                // #304 QA: ...as a fog-covered picture (DESIGN §5.13 "안개 덮인 그림"), not a blank sheet: the fog keeps a
                // monochrome share of the walked shading (_UnknownShade) and the relief's own local shading (_UnknownRelief:
                // darker than the ring of samples around = ink). A low-contrast painted relief (the live compact map) otherwise
                // vanished under the .84 sheet. Walked land always stays the stronger ink. Both 0 = the flat veil.
                float paperLum=dot(paper.rgb,fixed3(.299,.587,.114));
                float walkedRatio=saturate(dot(surface,fixed3(.299,.587,.114))/max(paperLum,.001));
                float2 rt=_MapTex_TexelSize.xy*_UnknownReliefTexels;
                float ring=MapLum304(worldUv+float2(rt.x,0))+MapLum304(worldUv-float2(rt.x,0))
                    +MapLum304(worldUv+float2(0,rt.y))+MapLum304(worldUv-float2(0,rt.y))
                    +MapLum304(worldUv+rt*.7071)+MapLum304(worldUv-rt*.7071)
                    +MapLum304(worldUv+float2(rt.x,-rt.y)*.7071)+MapLum304(worldUv+float2(-rt.x,rt.y)*.7071);
                float shade=saturate(((ring+luminance)/9-luminance)*_UnknownRelief);
                fixed3 fogged=paper.rgb*(1+(walkedRatio-1)*_UnknownShade)*(1-shade);
                surface=lerp(surface,fogged,(1-known)*_UnknownVeil*illustrationMask);
                fixed4 macroInk=tex2D(_MacroInkTex,printUv);
                surface=lerp(surface,macroInk.rgb,macroInk.a*illustrationMask);
                fixed4 ink=tex2D(_InkTex,printUv);
                surface=lerp(surface,lerp(surface*ink.rgb*.8,ink.rgb,_KnownBase),ink.a*inkMask);
                fixed3 cave=tex2D(_CaveTex,(worldUv-_CaveUv.xy)/max(_CaveUv.zw,.0001)).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                cave=LinearToGammaSpace(cave);
                #endif
                float2 cuv=(worldUv-_CaveUv.xy)/max(_CaveUv.zw,.0001);
                float caveInside=step(0,cuv.x)*step(cuv.x,1)*step(0,cuv.y)*step(cuv.y,1);
                float walked=lerp(1,WalkedEdge(cuv),_ExploreCave)*caveInside;
                surface=lerp(surface,cave,front*printInside*worldInside*_Interior*_HasCave*walked);
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
