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
        _MiniInk("Minimap ink (#307, _MINI_HUD only)",Color)=(.08,.08,.075,1)
        _MiniPaper("Minimap hanji (#307)",Color)=(.87,.86,.82,1)
        _MiniVeil("Minimap unwalked hanji (#307, user: whole disc hanji)",Color)=(.65,.64,.6,.94)
        _MiniWash("Minimap wash: alpha, linear ink gamma, grain tile, grain share",Vector)=(.62,1.8,.29,1)
        _MiniLand("Minimap land: hanji alpha, rim ink, relief gain, relief max",Vector)=(.94,.14,2.5,.2)
        _MiniDisc("Minimap frame: fade in, fade out, edge wobble, width/height",Vector)=(.8,.97,.05,1)
        _MiniTurn("Minimap ink turn (cos, sin) - follow view",Vector)=(1,0,0,0)
        _MiniCave("Minimap cave: wall from, wall to = floor from, floor to, wall ink",Vector)=(.44,.56,.64,.85)
        _WashTex("Minimap wash grain (wash_tile, alpha)",2D)="white"{}
        _Terrain308("#308 terrain: R slope wash, G forest density, B water distance, A elevation (_MAP308 only)",2D)="black"{}
        _Pattern308("#308 patterns: R forest dots, G ripples, B rock strokes, A grain",2D)="gray"{}
        _M308Paper("#308 paper (sRGB values; minimap)",Vector)=(.875,.859,.816,1)
        _M308Unknown("#308 unwalked wash (sRGB values)",Vector)=(.561,.545,.506,1)
        _M308Ink("#308 ink (sRGB values)",Vector)=(.078,.078,.075,1)
        _M308Wash("#308 wash: slope max, elevation max, elevation share (whole-world band)",Vector)=(.18,.14,0,0)
        _M308Elev("#308 elevation wash from, to (share of A); ripple feather m",Vector)=(.225,.825,4,0)
        _M308Forest("#308 forest: show from density, cluster gain, dot ink",Vector)=(.12,6,.62,0)
        _M308Rock("#308 rock: slope from, rock ink; shore ink, ripples from m",Vector)=(.88,.7,.8,3)
        _M308Water("#308 water: field range m, shore half width m, one px in m, ripple ink",Vector)=(32,1,1.2,.5)
        _M308Tile("#308 pattern repeats across the world (u, v)",Vector)=(25,37.5,0,0)
        _M308Grain("#308 grain share: paper fibre (minimap), unwalked wash",Vector)=(0,.05,0,0)
        _M308Rim("#308 walked-edge ink rim: ink alpha over the unwalked wash, width px (inside the edge); sheet only (no _MINI_HUD): whole thin rim 0 / 1, its width px",Vector)=(.45,2,0,0)
        _M308Edge("#308 crisp walked edge: threshold from, threshold span, noise cells 1, noise cells 2",Vector)=(.14,.42,1.6,3.7)
        _M308Cave("#308 cave plan: wall from, wall to = floor from, floor to, wall ink",Vector)=(.44,.56,.64,.85)
        _M308Border("#308 sheet: the picture feathers out before the world border: width px, grain wobble px",Vector)=(18,10,0,0)
        _RealmBorder308("#308 map 6 realm borders: per colour class the signed distance to its borders, code 128 = on it (sheet only)",2D)="white"{}
        _RealmGround308("#308 map 6 realm ground: RGB wash colour, A relief ink (sheet only)",2D)="black"{}
        _R308A("#308 map 6: on 0 / 1, veil of the old unwalked wash, faint terrain share, relief ink",Vector)=(0,1,0,0)
        _R308B("#308 map 6: wash unwalked, wash walked, border ink unwalked, border ink walked",Vector)=(0,0,0,0)
        _R308C("#308 map 6 border: half width screen px, range m, 1 / bleed screen px, bleed ink",Vector)=(.9,64,.15,0)
        _R308D("#308 map 6 border gaps: noise cells across the world (u, v), gap share; bleed share on walked land",Vector)=(57,86,0,.5)
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
            // #307 HUD minimap (HudMinimap304.ApplyLook enables it on its own runtime material): a runtime keyword, so
            // multi_compile (shader_feature would strip the variant from builds). Without it the unfolded sheet is unchanged.
            #pragma multi_compile_local _ _MINI_HUD
            // #308 notation (SPEC-MAP-OVERHAUL-308): WorldMapPresenter enables it on the sheet and the minimap materials when the
            // map has a MapNotation308SO bundle. A runtime keyword, so multi_compile. Without it both maps are unchanged.
            #pragma multi_compile_local _ _MAP308
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            // the walked-land function, shared with UI/MapStroke308 (declares _FogTex, _FogTex_TexelSize, _FogSoft, _FogNoise)
            #include "Assets/_Project/Art/UI/UI308/Map/Shaders/MapFog308.cginc"
            struct appdata {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float4 face:TEXCOORD1;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float4 face:TEXCOORD1;float4 world:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO};
            sampler2D _MainTex,_MapTex,_DetailTex,_CaveTex,_CaveDiscovery,_InkTex,_MacroInkTex;
            float4 _WorldUv,_MapTileUv,_CaveUv,_MapWindow,_ClipRect,_MapTex_TexelSize;fixed4 _Color;float _ExploreCave,_Interior,_KnownBase,_PaintedRelief,_HasDetail,_HasCave,_UnknownVeil;
            float _UnknownShade,_UnknownRelief,_UnknownReliefTexels,_PaintedInk;float4 _PaintedInkRange;
            #ifdef _MINI_HUD
            sampler2D _WashTex;fixed4 _MiniInk,_MiniPaper,_MiniVeil;float4 _MiniWash,_MiniLand,_MiniDisc,_MiniCave,_MiniTurn;
            #endif
            #ifdef _MAP308
            sampler2D _Terrain308,_Pattern308;
            float4 _M308Paper,_M308Unknown,_M308Ink,_M308Wash,_M308Elev,_M308Forest,_M308Rock,_M308Water,_M308Tile,_M308Grain,_M308Rim,_M308Edge,_M308Cave,_M308Border;
            #ifndef _MINI_HUD
            // #308 map 6 (D308-30): the realm sheets - the unfolded sheet only; the minimap variant does not even declare them
            sampler2D _RealmBorder308,_RealmGround308;float4 _R308A,_R308B,_R308C,_R308D;
            #endif
            #endif
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
                float2 inkUv=i.uv;
                #ifdef _MINI_HUD
                // follow view: the frame stays put and the ink turns inside it (about the centre, in square metre space)
                {float asp=max(_MiniDisc.w,1e-3);float2 q=(i.uv-.5)*float2(asp,1);q=float2(q.x*_MiniTurn.x+q.y*_MiniTurn.y,-q.x*_MiniTurn.y+q.y*_MiniTurn.x);inkUv=q/float2(asp,1)+.5;}
                #endif
                float2 printUv=(inkUv-_MapWindow.xy)/_MapWindow.zw;
                float printInside=step(0,printUv.x)*step(printUv.x,1)*step(0,printUv.y)*step(printUv.y,1);
                float2 worldUv=_WorldUv.xy+printUv*_WorldUv.zw;
                float worldInside=step(0,worldUv.x)*step(worldUv.x,1)*step(0,worldUv.y)*step(worldUv.y,1);
                float front=1-step(.5,i.face.x);
                float walkedCells=MapFog308_Known(worldUv);
                float known=lerp(walkedCells,1,saturate(_Interior));
                #ifndef _MAP308
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
                #ifdef _MINI_HUD
                // #307 style A "먹 씻김 위 한지" (SPEC-MINIMAP-307): the RawImage is the whole disc (uvRect fixed at 0..1, the window
                // moves by _MapWindow), so i.uv is the disc space. The disc is drawn and faded here (no stencil Mask): alpha falls
                // from _MiniDisc.x to .y of the radius, wobbled by the wash grain, under the enso ring. Unwalked land = a flat ink
                // wash (no terrain, AC-1e); walked land = hanji with a faint high-pass relief shade (the 8-tap ring above) and ink
                // roads; caves = the walked passages only (_CaveDiscovery also masks the ink here, unlike the sheet): floor = hanji,
                // wall = an ink line, rock = wash. No new colours; the cinnabar arrow is a separate Image.
                // user 2026-09-30: a rectangle, not a disc: rho = 1 - distance to the nearest edge in half-height units
                // (_MiniDisc.w = width / height), so the torn fade band is as wide on the sides as on top and bottom
                float2 edge=1-abs(i.uv-.5)*2;
                float rho=1-min(edge.x*max(_MiniDisc.w,1e-3),edge.y);
                float grain=tex2D(_WashTex,i.uv*_MiniWash.z).a;
                float disc=1-smoothstep(_MiniDisc.x,_MiniDisc.y,rho+(grain-.5)*_MiniDisc.z);
                float interiorOn=saturate(_Interior);
                float caveWalked=WalkedEdge(cuv)*caveInside;
                float miniWalked=lerp(walkedCells,lerp(1,caveWalked,_ExploreCave),interiorOn)*printInside*worldInside;
                float hp=saturate(((ring+luminance)/9-luminance)*_MiniLand.z)*_MiniLand.w*(1-interiorOn);
                // user 2026-09-30 "원판 전체를 한지로": unwalked land is a darker hanji (_MiniVeil), walked land the light sheet;
                // both carry the wash grain as paper fibre. Still no terrain under the unwalked hanji (AC-1e).
                float fibre=lerp(.93,1.05,grain);
                fixed3 veil=_MiniVeil.rgb*fibre;
                fixed3 land=_MiniPaper.rgb*fibre*(1-hp);
                float cl=dot(cave,fixed3(.299,.587,.114));
                float fl=smoothstep(_MiniCave.y,_MiniCave.z,cl);
                float wl=smoothstep(_MiniCave.x,_MiniCave.y,cl)*(1-fl);
                float caveOn=interiorOn*_HasCave;
                land=lerp(land,lerp(lerp(veil,_MiniPaper.rgb*fibre,fl),_MiniInk.rgb,wl*_MiniCave.w),caveOn);
                land=lerp(land,_MiniInk.rgb,ink.a*miniWalked);                                   // roads / detail lines: walked only
                land=lerp(land,_MiniInk.rgb,smoothstep(_MiniDisc.x-.18,_MiniDisc.x,rho)*_MiniLand.y);   // rim ink, not a shadow
                float veilA=_MiniVeil.a;
                result=fixed4(lerp(veil,land,miniWalked)*i.color.rgb,saturate(lerp(veilA,_MiniLand.x,miniWalked))*disc*i.color.a);
                #endif
                #else
                // ---------------------------------------------------------------- #308 notation (SPEC-MAP-OVERHAUL-308 1, 4, 6.3)
                // One baked terrain picture and one pattern tile, the same for the sheet and the minimap; roads, ridges, cliffs
                // and walls are brush strips (UI/MapStroke308) over this. On the MINIMAP unwalked land is the flat wash and its
                // grain, nothing else (#214): every terrain term below is under `shown`. On the unfolded SHEET map 6 (D308-30)
                // shows the whole world faintly under the walked land - see the `map 6` block. The colour math is in sRGB values,
                // as the offline twin composes; one conversion at the end.
                float interiorOn=saturate(_Interior);
                float inside=printInside*worldInside;
                fixed4 ter=tex2D(_Terrain308,worldUv);
                fixed4 pat=tex2D(_Pattern308,worldUv*_M308Tile.xy);
                fixed3 paperS=paper.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                paperS=LinearToGammaSpace(paperS);
                #endif
                #ifdef _MINI_HUD
                fixed3 sheet=_M308Paper.rgb*lerp(1-_M308Grain.x,1+_M308Grain.x,pat.a);
                #else
                fixed3 sheet=paperS;
                #endif
                fixed3 unk=_M308Unknown.rgb*lerp(1-_M308Grain.y,1+_M308Grain.y,pat.a);
                // walked land: slope wash (the elevation wash in the whole-world band), forest dots x density, rock strokes on
                // the steepest slopes, the shore line from the distance field and ripples inside the water. No colour.
                // dm = signed distance to the shore in metres, + inside the water; water carries no wash
                float dm=(ter.b*255-128)/127*_M308Water.x;
                float wash=lerp(ter.r*_M308Wash.x,smoothstep(_M308Elev.x,_M308Elev.y,ter.a)*_M308Wash.y,saturate(_M308Wash.z))*step(dm,0);
                fixed3 land=sheet*(1-wash);
                // a dot cluster appears where its level in the pattern passes 1 - density (the bake's rule), none under the show-from density
                float forest=saturate((pat.r-(1-ter.g))*_M308Forest.y)*step(_M308Forest.x,ter.g)*_M308Forest.z;
                float rock=pat.b*smoothstep(_M308Rock.x,1,ter.r)*_M308Rock.y;
                float shore=saturate((_M308Water.y-abs(dm))/_M308Water.z+.5)*_M308Rock.z;
                float ripple=pat.g*smoothstep(_M308Rock.w,_M308Rock.w+_M308Elev.z,dm)*_M308Water.w;
                float tink=saturate(max(max(forest,rock),max(ripple,shore)));
                land=lerp(land,_M308Ink.rgb,tink);
                // caves: the cave plan re-inked by lightness on BOTH maps (floor = paper, wall = ink, rock = the wash)
                float2 cuv=(worldUv-_CaveUv.xy)/max(_CaveUv.zw,.0001);
                float caveInside=step(0,cuv.x)*step(cuv.x,1)*step(0,cuv.y)*step(cuv.y,1);
                fixed3 cave=tex2D(_CaveTex,cuv).rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                cave=LinearToGammaSpace(cave);
                #endif
                float cl=dot(cave,fixed3(.299,.587,.114));
                float fl=smoothstep(_M308Cave.y,_M308Cave.z,cl);
                float wl=smoothstep(_M308Cave.x,_M308Cave.y,cl)*(1-fl);
                fixed3 caveLand=lerp(lerp(unk,sheet,fl),_M308Ink.rgb,wl*_M308Cave.w);
                fixed3 inLand=lerp(sheet,caveLand,saturate(_HasCave)*caveInside);
                // _InkTex here = interior detail lines only (a zone without a cave plan); outdoors the strips carry every line
                fixed4 ink=tex2D(_InkTex,printUv);
                inLand=lerp(inLand,_M308Ink.rgb,ink.a*interiorOn);
                float caveWalked=WalkedEdge(cuv)*caveInside;
                // the walked land ends on a CRISP edge (the direction sheet): MapFog308_Edge is > 0 on walked land and its
                // contour always lies inside the walked cells, so no terrain, strip or rim can sit on an unwalked cell (#214)
                // and the 32 m grid never shows. edgePx = signed distance to that edge in screen px; 1 px of anti-aliasing.
                float2 walk=MapFog308_Edge(worldUv,_M308Edge);
                float edgePx=walk.x/max(length(float2(ddx(walk.x),ddy(walk.x))),1e-5);
                float crisp=saturate(edgePx+.5);
                float shown=lerp(crisp,lerp(1,caveWalked,saturate(_ExploreCave)),interiorOn);
                fixed3 unk6=unk;fixed3 land6=land;
                #ifndef _MINI_HUD
                // #308 map 6 (D308-30 "실제 지형과 각 강토를 나눈 선이 보이는 형태, 색으로 강토 구분"): the unfolded sheet shows the WHOLE
                // world from the start - unwalked land = the paper under a veil of the old wash, the SAME terrain picture at a share
                // of its ink, the relief of the height field, the realm's wash colour and the realm border; walked land keeps its
                // full notation (strips, marks: still walked land only) and takes a lighter wash and the border. Two small sheets
                // (MapRealm308SO, baked offline from the location catalogue's realm polygons, the approved five wash colours and
                // the scenes' height field): _RealmGround308 RGB wash colour, A relief ink; _RealmBorder308 per colour class of
                // the realm graph the signed distance to its borders (code 128 = on a border) - the least |distance| of the four,
                // in screen px, is the line, at one width at every zoom (as the shore line). The gaps are a world-anchored value
                // noise (the dry-brush break of the walked edge). Ink and wash only: nothing here adds light. _R308A.x = 0 (no
                // realm asset, or its On = 0) = the sheet of map 5: unk6 = unk, land6 = land. A cave view is untouched.
                // Mirror: Tools/Unity/Stage308_map6/_Tools/map6_twin.py render6 (the preview pictures are drawn by it).
                {
                    fixed4 gr=tex2D(_RealmGround308,worldUv);
                    float4 d4=abs(tex2D(_RealmBorder308,worldUv)*255-128)/127;
                    float dpx=min(min(d4.x,d4.y),min(d4.z,d4.w))*_R308C.y/max(_M308Water.z,1e-5);
                    float gap=lerp(1,smoothstep(.30,.50,MapFog308_Noise(worldUv*_R308D.xy+11.3)),saturate(_R308D.z));
                    float line6=saturate(_R308C.x-dpx+.5)*gap;
                    float bleed6=saturate(1-dpx*_R308C.z);bleed6=bleed6*bleed6*_R308C.w;
                    fixed3 far6=lerp(sheet,unk,saturate(_R308A.y))*(1-wash*_R308A.z)*(1-gr.a*step(dm,0)*_R308A.w);
                    far6=lerp(far6,_M308Ink.rgb,tink*saturate(_R308A.z));
                    far6=lerp(far6,gr.rgb,saturate(_R308B.x));
                    far6=lerp(far6,_M308Ink.rgb,saturate(max(line6*_R308B.z,bleed6)));
                    fixed3 near6=lerp(land,gr.rgb,saturate(_R308B.y));
                    near6=lerp(near6,_M308Ink.rgb,saturate(max(line6*_R308B.w,bleed6*_R308D.w)));
                    float on6=saturate(_R308A.x)*(1-interiorOn);
                    unk6=lerp(unk,far6,on6);land6=lerp(land,near6,on6);
                }
                #endif
                fixed3 col=lerp(unk6,lerp(land6,inLand,interiorOn),shown);
                // ink rim: the outermost _M308Rim.y px of the walked land, ink at _M308Rim.x over the unwalked wash. It is part
                // of the walked land (inside the edge), and never deep inside it (cover = 1 there).
                // #308 map 3: the rim's ink AND width follow the gate (MapFog308_Rim: thick where the brush is pressed, thin to the end of a run)
                float rim=crisp*MapFog308_Rim(walk.y,edgePx,_M308Rim.y)*(1-interiorOn);
                #ifndef _MINI_HUD
                // #308 map 4 (D308-24 answer 9): the unfolded sheet and the whole-world view draw a thin WHOLE rim; the minimap keeps the
                // broken dry-brush rim above (this block is not compiled into the _MINI_HUD variant). The same function with the gate of a
                // whole rim (MapFog308_Edge's .y at weight 1: no rim where field + GATE0 >= 1) and the notation's sheet width
                // (_M308Rim.w screen px; / WMAX because MapFog308_Rim widens a full-pressure rim by WMAX). _M308Rim.z = 0 / 1 (notation
                // values.fogEdge.sheet.whole): 0 = the sheet draws the minimap's rim. Still x crisp: no ink on an unwalked pixel (#214).
                // The literal .92 below IS MapFog308_Edge's `1-.08*weight` at weight 1 (MapFog308.cginc, its `float gate=max(...)` line):
                // change one and the other must follow. Where a fog cell is under (rim px + .5) / .32 screen px (the whole-world view:
                // 4 px per cell) this gate cuts the rim at .32 - .40 cells: 1.5 px stands about 1.2 - 1.3 px wide there, and no
                // _M308Rim.w makes it wider than about 1.5 px.
                float gateW=max(saturate(walk.x+MAPFOG308_E_GATE0),.92);
                float rimW=crisp*MapFog308_Rim(gateW,edgePx,_M308Rim.w/MAPFOG308_E_WMAX)*(1-interiorOn);
                rim=lerp(rim,rimW,saturate(_M308Rim.z));
                #endif
                col=lerp(col,lerp(unk,_M308Ink.rgb,_M308Rim.x),rim);
                #ifdef _MINI_HUD
                // the rectangle of #307 (rho = 1 at the edge); the #308 frame sits on the edge, so the fade is a thin feather
                col=lerp(unk,col,inside);
                float2 edge=1-abs(i.uv-.5)*2;
                float rho=1-min(edge.x*max(_MiniDisc.w,1e-3),edge.y);
                float disc=1-smoothstep(_MiniDisc.x,_MiniDisc.y,rho);
                col=lerp(col,_M308Ink.rgb,smoothstep(_MiniDisc.x-.18,_MiniDisc.x,rho)*_MiniLand.y);
                float alpha=saturate(lerp(_MiniVeil.a,_MiniLand.x,shown*inside))*disc;
                #ifndef UNITY_COLORSPACE_GAMMA
                col=GammaToLinearSpace(col);
                #endif
                #else
                // the sheet's margin and its back stay bare paper. #308 map fix (M3): the picture (walked land AND the unwalked
                // wash) feathers out over the last _M308Border.x screen px before the world's border, pushed in by the pattern
                // grain (0 on the border itself), so the world no longer ends on a hard rectangle against the sheet. It only takes ink away (#214).
                float2 borderPx=min(worldUv,1-worldUv)/max(float2(length(float2(ddx(worldUv.x),ddy(worldUv.x))),length(float2(ddx(worldUv.y),ddy(worldUv.y)))),1e-7);
                float border=saturate((min(borderPx.x,borderPx.y)-pat.a*_M308Border.y)/max(_M308Border.x,1e-3));
                border=border*border*(3-2*border);
                col=lerp(paperS,col,inside*front*border);
                float alpha=paper.a;
                #ifndef UNITY_COLORSPACE_GAMMA
                col=GammaToLinearSpace(col);
                #endif
                col*=lerp(1,.90,1-front);
                #endif
                fixed4 result=fixed4(col*i.color.rgb,alpha*i.color.a);
                #endif
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
