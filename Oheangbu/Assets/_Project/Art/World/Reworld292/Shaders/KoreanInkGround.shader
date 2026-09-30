// Isolated single-view ink painting study. Parent source/materials are preserved.
// Compact-scene derivative. Provider textures and the original ink terrain stay untouched.
Shader "Oheangbu/Reworld292/KoreanInkGround"
{
    Properties
    {
        _BankStrength294("Candidate riverbank material",Range(0,1))=0
        _BankMask294("Bank coverage / weighted wetness",2D)="black"{}
        _BankGravel294("CC0 bank gravel",2D)="gray"{}
        _BankMud294("CC0 bank mud",2D)="gray"{}
        _StrataStrength276("Terrain material rules (candidate only)",Range(0,1))=0
        _StrataRange276("Rule to distant ink transition metres",Vector)=(160,650,0,0)
        _StrataField276("Curvature and local exposure data",2D)="gray"{}
        _Realm293("Authored realm pigments",2D)="gray"{}
        _RealmStrength293("Realm pigment",Range(0,1))=0
        _Floor293("Realm floor weight map (r = forest floor)",2D)="black"{}
        _FloorMap293("Realm floor albedo (CC0 forest ground)",2D)="gray"{}
        _FloorStrength293("Near realm floor material",Range(0,1))=0
        _FloorScale293("Floor repeats per metre",Float)=.31
        _FloorChroma293("Floor colour kept",Range(0,1))=.5
        _FloorRange293("Floor near start/end metres",Vector)=(45,260,0,0)
        _FloorTint293("Floor tint",Color)=(1,1,1,1)
        _FloorWash293("Mid-field floor colour wash",Range(0,1))=0
        _FloorFar293("Wash fade start/end metres",Vector)=(900,2200,0,0)
        _FloorRealms293("Realm floors beyond Cheongrim (floor map g/b/a + remainder)",Range(0,1))=0
        _Scorch293("Jeokro ink wash (strength, low/high mass, rock weight)",Vector)=(0,.5,.9,.45)
        _FloorMapJ293("Jeokro floor albedo",2D)="gray"{}
        _FloorMapC293("Cheolong floor albedo",2D)="gray"{}
        _FloorMapH293("Hyeongang floor albedo",2D)="gray"{}
        _FloorMapW293("Hwanggyeong floor albedo",2D)="gray"{}
        _FloorTintJ293("Jeokro floor tint (rgb), chroma (a)",Vector)=(1,1,1,.5)
        _FloorTintC293("Cheolong floor tint (rgb), chroma (a)",Vector)=(1,1,1,.5)
        _FloorTintH293("Hyeongang floor tint (rgb), chroma (a)",Vector)=(1,1,1,.5)
        _FloorTintW293("Hwanggyeong floor tint (rgb), chroma (a)",Vector)=(1,1,1,.5)
        _Canopy293("Canopy density (r, world XZ)",2D)="black"{}
        _CanopyStrength293("Ground under canopy masses",Range(0,1))=0
        _CanopyTint293("Under-canopy multiplier (linear rgb)",Vector)=(.5,.58,.46,0)
        _CanopyRange293("Near start/end metres, near weight",Vector)=(30,220,.35,0)
        _CanopyForest293("Forested ground hue (linear rgb), strength",Vector)=(.9,1.03,.93,0)
        _StrataFracture276("Metre scale joint cavities",2D)="white"{}
        [Normal] _StrataFractureNormal276("Joint relief",2D)="bump"{}
        _InkChromaRetain268("Candidate regional chroma retention",Range(0,1))=1
        _InkChromaRect268("Candidate regional XZ bounds",Vector)=(0,0,0,0)
        _GrassCover267("Candidate meadow coverage (opt in)",2D)="black"{}
        _GrassCoverStrength267("Candidate meadow ground",Range(0,1))=0
        _RebuildGravel("Rebuild gravel height / cavity",2D)="white"{}
        [Normal] _RebuildGravelNormal("Rebuild gravel normal",2D)="bump"{}
        _RebuildGravelStrength("Rebuild gravel relief (opt in)",Range(0,1))=0
        _RebuildDetailStrength("Rebuild near material detail (opt in)",Range(0,1))=0
        _RebuildDetailRange("Rebuild material blend metres",Vector)=(25,160,0,0)
        _RebuildScreenFog("Camera owns distance fog (opt in)",Float)=0
        [Header(Independent Mountain Ink Study)]
        _PaintedInkAirWash("Air wash retained inside loaded ink",Range(0,1))=.18
        [ToggleUI] _PaintedWashEnabled("Connected ink face wash",Float)=0
        [ToggleUI] _PaintedEdgeWash("Adjacent ink-tone edge wash",Float)=0
        [ToggleUI] _PaintedFarPath("Distant mountain path ink ceiling",Float)=0
        [ToggleUI] _PaintedFarPathAir("Distant path uses mountain atmosphere mass",Float)=0
        [ToggleUI] _PaintedPathMountain("Far Ground paths share mountain ink",Float)=0
        _PaintedPathMountainRange("Mountain path start/end/strength",Vector)=(250,550,.9,0)
        _PaintedFarPathDistance("Far path start/end metres",Vector)=(250,900,0,0)
        _PaintedFarPathAppearance("Far path luminance ceiling/strength",Vector)=(.10,1,0,0)
        _PaintedEdgeWashStrengths("Loaded-middle / middle-lit mixing",Vector)=(.30,.25,0,0)
        _PaintedEdgeWashResponse("Edge texture neutral / contrast",Vector)=(.5,1,0,0)
        [NoScaleOffset] _PaintedRockWash("Connected ink bristles R",2D)="white"{}
        _PaintedWashTiling("Horizontal vertical metres and strength",Vector)=(640,760,.85,0)
        [ToggleUI] _PaintedInkEnabled("Painted mountain atlas",Float)=1
        [NoScaleOffset] _PaintedInkAtlas("Ink atlas R linear white paper black ink",2D)="white"{}
        _PaintedInkScale("Full stroke width and height metres",Vector)=(150,310,440,780)
        _PaintedInkBlack("Loaded ink linear RGB",Vector)=(.008,.007,.006,0)
        _PaintedInkMassTones("Loaded middle lit ink and bristle reserve",Vector)=(.012,.040,.100,.035)
        _PaintedInkPaperTones("Paper reserve linear low high",Vector)=(.20,.40,0,0)
        _PaintedInkPaperTint("Warm paper linear RGB multiplier",Vector)=(1,.95,.86,0)
        _PaintedInkLoad("Ink variation min max opacity",Vector)=(1,1.9,1,0)
        _PaintedInkResponse("Ink mask low high",Vector)=(.03,.92,0,0)
        _PaintedInkLayout("Grid width height jitter warp",Vector)=(.75,.64,.35,.12)
        _PaintedInkTileCrop("Stroke cell UV left bottom right top",Vector)=(.18,0,.82,1)
        _PaintedInkSlope("Paper mountain slope start full degrees",Vector)=(18,42,0,0)
        [NoScaleOffset] _PaintedGeoField("Actual terrain ink regions and flow",2D)="black"{}
        _PaintedGeoRect("Terrain field origin size XZ",Vector)=(0,0,1,1)
        [ToggleUI] _PaintedGeoEnabled("Use terrain field",Float)=0
        _PaintedRoadBank("Local road bank ground blend",Range(0,1))=0
        [NoScaleOffset] _PaintedRoadBankField("Fitted road weight/weighted height",2D)="black"{}
        _PaintedRoadBankRect("Road bank world rect",Vector)=(-2000,-3000,4000,6000)
        _PaintedRoadBankParams("Height and near distance fade",Vector)=(2,6,80,250)
        [ToggleUI] _PaintedRoadBankSteep("Release distant steep road bank",Float)=0
        _PaintedRoadBankSteepParams("Steep cosine thresholds and distance",Vector)=(.7071067811865476,.5,80,140)
        [ToggleUI] _PaintedFormEnabled("Form-led mountain ink",Float)=0
        [ToggleUI] _PaintedFormAuthored("Use authored form RGBA map",Float)=0
        [NoScaleOffset] _PaintedFormMap("Form R ink G paper B dry A override",2D)="black"{}
        _PaintedFormMapRect("Form map world origin XZ/size",Vector)=(-2000,-3000,4000,6000)
        _PaintedFormRelief("Upper load/foot reserve relief ramps",Vector)=(45,145,55,135)
        _PaintedFormDirection("Broad ink-load direction XZ",Vector)=(.8,.6,0,0)
        _PaintedFormTones("Loaded/middle/lower/dry ink tones",Vector)=(.006,.045,.16,.018)
        _PaintedFormBrush("Cross/relief metres/dry thresholds",Vector)=(180,480,.86,.99)
        _PaintedFormStrokeBody("Broad form brush body strength",Range(0,1))=0
        _PaintedFormAspectContrast("Upper form aspect contrast",Range(0,1))=0
        _PaintedFormPaper("Foot strength/paper fraction",Vector)=(.65,.82,0,0)
        _PaintedFormPaperDistance("Form reserve near/far metres",Vector)=(250,900,0,0)
        [ToggleUI] _PaintedValleyReserve("Distant low-relief paper reserve",Float)=0
        _PaintedValleyDistance("Reserve horizontal distance metres",Vector)=(250,900,0,0)
        _PaintedValleyRelief("Reserve relief ramps metres",Vector)=(10,30,55,125)
        _PaintedValleyAppearance("Reserve strength/paper fraction",Vector)=(.45,.72,0,0)
        [ToggleUI] _CIEnabled("Compact ink landscape",Float)=0
        [ToggleUI] _CIDarkNear("Dark foreground ink revision",Float)=0
        _CIMountainTones("Mountain ink tone interval",Vector)=(.06,.18,0,0)
        _CIMountainSlope("Mountain slope degrees",Vector)=(12,37,0,0)
        [ToggleUI] _CIBroadBrush("Broad mountain ink coats",Float)=0
        _CIBroadBrushScale("Broad stroke width/length metres",Vector)=(80,240,420,1000)
        _CIBroadBrushCoverage("Coat opacity/edge/second/warp",Vector)=(.90,.18,.76,.20)
        _CIBroadBrushTones("Broad dark/soft coat tones",Vector)=(.015,.07,0,0)
        _CIBroadBrushBands("Mountain band thresholds/softness/amount",Vector)=(.33,.66,.12,.8)
        [ToggleUI] _CIAtmosphereOverride("Independent compact atmosphere",Float)=0
        [ToggleUI] _CIPainterly("Painterly compact revision",Float)=0
        _CIMidAirRange("Middle atmosphere range",Vector)=(500,1500,0,0)
        _CIFarAirRange("Far atmosphere range",Vector)=(1500,3400,0,0)
        _CIAirStrengths("Middle/far atmosphere strengths",Vector)=(.18,.64,0,0)
        _CIStrokeSpacingWidth("Stroke spacing/width metres",Vector)=(20,40,2,7)
        _CIStrokeFadeStrength("Stroke fade range/ink strength",Vector)=(200,1400,.02,0)
        _CIPigmentContrast("Mountain/ground pigment contrast",Vector)=(.03,.65,0,0)
        _CIFoliagePainterly("Foliage transition/mip/contrast",Vector)=(30,180,1.5,.3)
        _CIInk("Compact ink",Color)=(.165,.149,.133,1)
        _CIPaper("Compact paper",Color)=(.969,.945,.894,1)
        _CIAir("Compact horizon atmosphere",Color)=(.9,.88,.83,1)
        _CIDetailRange("Near detail range",Vector)=(30,150,0,0)
        _CIRockRange("Rock detail range",Vector)=(150,350,0,0)
        _CIMountainRange("Mountain form range",Vector)=(350,900,0,0)
        _CIAirRange("Far atmosphere range",Vector)=(900,2200,0,0)
        _CITones("Ground/path tone intervals",Vector)=(.27,.56,.24,.48)
        _CIDetailContrast("Near texture contrast",Float)=.22
        _CINormalStrength("Near normal strength",Float)=.5

        _GrassMap("Grass surface",2D)="white"{}
        [Normal] _GrassNormal("Grass normal",2D)="bump"{}
        _DirtMap("Soil surface",2D)="white"{}
        [Normal] _DirtNormal("Soil normal",2D)="bump"{}
        _RockMap("Rock surface",2D)="white"{}
        [Normal] _RockNormal("Rock normal",2D)="bump"{}
        _GroundPathMask("Authored path mask",2D)="black"{}
        _RealmPigment("Existing realm pigment",2D)="white"{}
        _GroundPathRect("World mask origin / inverse size",Vector)=(0,0,1,1)
        _GroundPath("Use path mask",Float)=0
        _SourceUV("Use source soil UV",Float)=0
        _SourceUVTransform("Source soil UV scale and offset",Vector)=(1,1,0,0)
        _GroundKind("0 terrain, 1 soil ribbon",Float)=0
        _EdgeFade("Use vertex alpha edge",Float)=0
        _Tiling("Grass, soil, rock repeats per metre",Vector)=(.5,.33,.25,0)
        _BumpScale("Surface relief",Range(0,2))=.85
        _Brightness("Material brightness",Range(.25,2))=1.1
        _Saturation("Material saturation",Range(0,1))=.8
        _AmbientLevel("Diffuse floor",Range(0,1))=.3
        _LightResponse("Direct response",Range(0,2))=.7
        _RealmTintStrength("Region material tint",Range(0,1))=.15
        _WashStart("Existing distance wash start",Float)=800
        _WashEnd("Existing distance wash end",Float)=2400
        _WashStrength("Existing distance wash strength",Range(0,1))=.8
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Cull Back ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        TEXTURE2D(_GrassMap);SAMPLER(sampler_GrassMap);
        TEXTURE2D(_GrassNormal);SAMPLER(sampler_GrassNormal);
        TEXTURE2D(_DirtMap);SAMPLER(sampler_DirtMap);
        TEXTURE2D(_DirtNormal);SAMPLER(sampler_DirtNormal);
        TEXTURE2D(_RockMap);SAMPLER(sampler_RockMap);
        TEXTURE2D(_RockNormal);SAMPLER(sampler_RockNormal);
        TEXTURE2D(_GroundPathMask);SAMPLER(sampler_GroundPathMask);
        TEXTURE2D(_RealmPigment);SAMPLER(sampler_RealmPigment);
        TEXTURE2D(_RebuildGravel);SAMPLER(sampler_RebuildGravel);
        TEXTURE2D(_RebuildGravelNormal);SAMPLER(sampler_RebuildGravelNormal);
        TEXTURE2D(_GrassCover267); // Reuse the clamp/linear path sampler; D3D11 has 16 sampler slots.
        TEXTURE2D(_StrataField276); TEXTURE2D(_Realm293); TEXTURE2D(_Floor293); TEXTURE2D(_FloorMap293); TEXTURE2D(_Canopy293); // floor/canopy reuse existing samplers
        TEXTURE2D(_FloorMapJ293); TEXTURE2D(_FloorMapC293); TEXTURE2D(_FloorMapH293); TEXTURE2D(_FloorMapW293);
        TEXTURE2D(_StrataFracture276);
        TEXTURE2D(_StrataFractureNormal276); // Share existing clamp/repeat samplers.
        TEXTURE2D(_BankMask294);TEXTURE2D(_BankGravel294);TEXTURE2D(_BankMud294);
        CBUFFER_START(UnityPerMaterial)
        float _BankStrength294;
        float _RealmStrength293; float _StrataStrength276;float4 _StrataRange276;
        float _FloorStrength293,_FloorScale293,_FloorChroma293,_FloorWash293;float4 _FloorRange293,_FloorTint293,_FloorFar293;
        float _CanopyStrength293;float4 _CanopyTint293,_CanopyRange293,_CanopyForest293;
        float _FloorRealms293;float4 _FloorTintJ293,_FloorTintC293,_FloorTintH293,_FloorTintW293,_Scorch293;
            float _InkChromaRetain268;float4 _InkChromaRect268;
        float _GrassCoverStrength267;
        float _RebuildGravelStrength;
        float4 _GroundPathRect,_Tiling,_SourceUVTransform;float _SourceUV;
        float _GroundPath,_GroundKind,_EdgeFade,_BumpScale,_Brightness,_Saturation;
        float _AmbientLevel,_LightResponse,_RealmTintStrength,_WashStart,_WashEnd,_WashStrength;
        float _RebuildDetailStrength,_RebuildScreenFog;float4 _RebuildDetailRange;
        
            float4 _PaintedGeoRect;float _PaintedGeoEnabled;
            float _PaintedRoadBank;float4 _PaintedRoadBankRect,_PaintedRoadBankParams;
            float _PaintedRoadBankSteep;float4 _PaintedRoadBankSteepParams;
            float _PaintedEdgeWash;
            float _PaintedFarPath;
            float _PaintedFarPathAir;
            float _PaintedPathMountain;float4 _PaintedPathMountainRange;
            float4 _PaintedFarPathDistance,_PaintedFarPathAppearance;
            float4 _PaintedEdgeWashStrengths,_PaintedEdgeWashResponse;
            float _PaintedFormEnabled,_PaintedFormAuthored;
            float _PaintedFormStrokeBody;
            float _PaintedFormAspectContrast;
            float4 _PaintedFormMapRect,_PaintedFormRelief,_PaintedFormDirection;
            float4 _PaintedFormTones,_PaintedFormBrush,_PaintedFormPaper,_PaintedFormPaperDistance;
            float _PaintedValleyReserve;
            float4 _PaintedValleyDistance,_PaintedValleyRelief,_PaintedValleyAppearance;
            float _CIEnabled,_CIDarkNear,_CIDetailContrast,_CINormalStrength;
            float4 _CIInk,_CIPaper,_CIAir,_CIDetailRange,_CIRockRange,_CIMountainRange,_CIAirRange,_CITones;
            float4 _CIMountainTones,_CIMountainSlope;
            float _CIBroadBrush;
            float4 _CIBroadBrushScale,_CIBroadBrushCoverage,_CIBroadBrushTones,_CIBroadBrushBands;
            float _CIAtmosphereOverride,_CIPainterly;
            float4 _CIMidAirRange,_CIFarAirRange,_CIAirStrengths,_CIStrokeSpacingWidth,_CIStrokeFadeStrength,_CIPigmentContrast,_CIFoliagePainterly;
            float _PaintedInkEnabled,_PaintedInkAirWash,_PaintedWashEnabled;float4 _PaintedWashTiling;
            float4 _PaintedInkMassTones;
            float4 _PaintedInkAtlas_TexelSize,_PaintedInkScale,_PaintedInkBlack,_PaintedInkPaperTones,_PaintedInkPaperTint;
            float4 _PaintedInkLoad,_PaintedInkResponse,_PaintedInkLayout,_PaintedInkTileCrop,_PaintedInkSlope;
        CBUFFER_END
        #include "Assets/_Project/Shaders/InkChroma268.hlsl"
        // Only this shader has the two far albedo fetches to exchange.
        #define IP_STUDY_PATH_MOUNTAIN_EXCHANGE 1
        #include "Assets/_Project/Shaders/InkPaintingStudy/InkPaintingStudy.hlsl"
        float4 _OhPaperColor;
        struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 color:COLOR;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
        struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float alpha:TEXCOORD2;float2 uv:TEXCOORD3;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
        V Vert(A i)
        {
            V o=(V)0;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);
            o.normal=TransformObjectToWorldNormal(i.normalOS);o.alpha=i.color.a;o.uv=i.uv*_SourceUVTransform.xy+_SourceUVTransform.zw;return o;
        }
        void ClipEdge(V i)
        {
            if(_EdgeFade>.5)clip(i.alpha-frac(52.9829189*frac(dot(floor(i.positionCS.xy),float2(.06711056,.00583715))))-.0001);
        }
        float Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
        float Noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);}
        float3 SoilNormal(float3 geometric,float3 tangentNormal)
        {
            float3 t=normalize(float3(geometric.y,-geometric.x,0));
            float3 b=normalize(cross(t,geometric));
            return normalize(t*tangentNormal.x+b*tangentNormal.y+geometric*tangentNormal.z);
        }
        #include "Assets/_Project/Art/World/Reworld292/Shaders/KoreanSurface292.hlsl"
        ENDHLSL
        Pass
        {
            Name "Forward" Tags {"LightMode"="UniversalForwardOnly"}
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n=normalize(i.normal),p=i.world;
                IPStudyDerivatives derivatives=IPPrepareStudyDerivatives(p,_CIEnabled>.5);
                ClipEdge(i);
                float2 uv=(p.xz-_GroundPathRect.xy)*_GroundPathRect.zw;
                float inside=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
                float path=_GroundKind>.5?1:(_GroundPath>.5?SAMPLE_TEXTURE2D(_GroundPathMask,sampler_GroundPathMask,uv).r*inside:0);
                float soil=smoothstep(.08,.8,path);
                bool darkNear=_CIEnabled>.5&&_CIDarkNear>.5;
                float cameraDistance=distance(_WorldSpaceCameraPos,p);
                float4 geography=derivatives.geography;
                float mountainMass=_CIEnabled>.5?IPStudyMountainMass(geography,n,soil,p,cameraDistance):0;
                // Soil is already fixed by the original mask; vertex alpha and
                // ClipEdge are untouched. The helper guarantees zero albedo/detail
                // contribution and a fully active, two-fetch form mountain.
                bool exchangePathAlbedo=IPStudyPathMountainWeight(soil,geography.r,cameraDistance)>0 && (_RebuildDetailStrength<=0 || cameraDistance>=_RebuildDetailRange.y);
                float ruleWeight=RuleWeight276(cameraDistance);
                float3 color=float3(0,0,0);
                [branch] if(ruleWeight<.9999)
                {
                // Exact endpoints only: transition pixels and every nonzero road mask
                // retain the original soil textures, normal response and lighting.
                [branch] if(darkNear&&_PaintedInkEnabled>.5&&mountainMass>=1&&soil<=0&&(_RebuildDetailStrength<=0||cameraDistance>=_RebuildDetailRange.y))
                {
                    color=IPMountain(p,n,derivatives);
                }
                else
                {
                    float grassSlope=smoothstep(.40,.72,n.y);
                    if(darkNear)grassSlope=max(grassSlope,soil);
                    float3 albedo,detailNormal=n;
                    float detail=_CIEnabled>.5 ? (1-CIProgress(cameraDistance,_CIDetailRange.xy))*_CINormalStrength : 1-smoothstep(80,230,cameraDistance);
                    // Keep a fixed normal sample and fade its subtractive pigment response below.
                    // Fading the normal itself can turn a dark mark into a bright one along the way.
                    if(darkNear)detail=cameraDistance<_CIDetailRange.y?_CINormalStrength:0;
                    if(grassSlope>.001)
                    {
                        float2 gUV=p.xz*_Tiling.x,dUV=_SourceUV>.5?i.uv:p.xz*_Tiling.y;
                        float macro=Noise(p.xz*.045);
                        float3 grass=float3(.5,.5,.5),dirt=float3(.5,.5,.5);
                        [branch] if(!exchangePathAlbedo)
                        {
                            grass=SAMPLE_TEXTURE2D(_GrassMap,sampler_GrassMap,gUV).rgb;
                            dirt=SAMPLE_TEXTURE2D(_DirtMap,sampler_DirtMap,dUV).rgb;
                        }
                        // Second frequency only near the viewer; broad modulation continues in the distance.
                        if(cameraDistance<180&&_SourceUV<.5)
                        {
                            grass=lerp(grass,SAMPLE_TEXTURE2D(_GrassMap,sampler_GrassMap,float2(-gUV.y,gUV.x)*.413+17).rgb,.18);
                            dirt=lerp(dirt,SAMPLE_TEXTURE2D(_DirtMap,sampler_DirtMap,float2(-dUV.y,dUV.x)*.413+7).rgb,.18);
                        }
                        albedo=lerp(grass,dirt,soil)*lerp(.80,1.13,macro);
                        if(detail>.001)
                        {
                            float3 gn=UnpackNormalScale(SAMPLE_TEXTURE2D(_GrassNormal,sampler_GrassNormal,gUV),_BumpScale*detail);
                            float3 dn=UnpackNormalScale(SAMPLE_TEXTURE2D(_DirtNormal,sampler_DirtNormal,dUV),_BumpScale*detail);
                            detailNormal=SoilNormal(n,normalize(lerp(gn,dn,soil)));
                        }
                    }
                    else albedo=0;
                    if(grassSlope<.999)
                    {
                        float3 w=pow(abs(n),4);w/=max(.001,w.x+w.y+w.z);float3 q=p*_Tiling.z;
                        float3 rock=SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,q.zy).rgb*w.x+
                            SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,q.xz).rgb*w.y+SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,q.xy).rgb*w.z;
                        albedo=lerp(rock*.65,albedo,grassSlope);
                        if(detail>.001)
                        {
                            float3 x=UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal,sampler_RockNormal,q.zy),_BumpScale*detail);
                            float3 y=UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal,sampler_RockNormal,q.xz),_BumpScale*detail);
                            float3 z=UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal,sampler_RockNormal,q.xy),_BumpScale*detail);
                            float3 rn=normalize(float3(x.z*sign(n.x),x.y,x.x)*w.x+float3(y.x,y.z*sign(n.y),y.y)*w.y+float3(z.x,z.y,z.z*sign(n.z))*w.z);
                            detailNormal=normalize(lerp(rn,detailNormal,grassSlope));
                        }
                    }
                    [branch] if(_RebuildGravelStrength>0 && cameraDistance<60)
                    {
                        float gravelWeight=(1-smoothstep(18,60,cameraDistance))*_RebuildGravelStrength*grassSlope*lerp(.55,1,soil);
                        float2 uv=p.xz*.5;
                        float height=SAMPLE_TEXTURE2D(_RebuildGravel,sampler_RebuildGravel,uv).r;
                        float3 view=normalize(_WorldSpaceCameraPos-p);
                        uv-=clamp(view.xz/max(.28,abs(view.y)),-2,2)*((height-.3)*.012*.5);
                        float3 gravel=SAMPLE_TEXTURE2D(_RebuildGravel,sampler_RebuildGravel,uv).rgb;
                        float3 gn=UnpackNormalScale(SAMPLE_TEXTURE2D(_RebuildGravelNormal,sampler_RebuildGravelNormal,uv),.45);
                        detailNormal=normalize(lerp(detailNormal,SoilNormal(n,gn),gravelWeight*.85));
                        albedo*=lerp(1,(.72+gravel.r*1.1)*lerp(.58,1,gravel.g),gravelWeight);
                    }
                    float lum=dot(albedo,float3(.2126,.7152,.0722));albedo=lerp(lum.xxx,albedo,_Saturation)*_Brightness;
                    Light main=GetMainLight(TransformWorldToShadowCoord(p));
                    float3 energy=main.color*main.shadowAttenuation*main.distanceAttenuation*saturate(dot(detailNormal,main.direction));
                    #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData=(InputData)0;inputData.positionWS=p;inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                    uint count=GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                    for(uint k=0;k<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);k++)
                    {Light l=GetAdditionalLight(k,p,half4(1,1,1,1));energy+=l.color*l.distanceAttenuation*l.shadowAttenuation*saturate(dot(detailNormal,l.direction));}
                    #endif
                    LIGHT_LOOP_BEGIN(count)
                    Light l=GetAdditionalLight(lightIndex,p,half4(1,1,1,1));energy+=l.color*l.distanceAttenuation*l.shadowAttenuation*saturate(dot(detailNormal,l.direction));
                    LIGHT_LOOP_END
                    #endif
                    float ao=1;
                    #if defined(_SCREEN_SPACE_OCCLUSION)
                    ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS)).indirectAmbientOcclusion;
                    #endif
                    color=albedo*(_AmbientLevel*lerp(.5,1,ao)+_LightResponse*energy/(1+energy));
                    if(_CIEnabled>.5)
                    {
                        float shade=saturate(dot(detailNormal,main.direction))*lerp(.25,1,main.shadowAttenuation)*ao;
                        if(darkNear)
                        {
                            float broadShade=saturate(dot(n,main.direction))*lerp(.25,1,main.shadowAttenuation)*ao;
                            color=IPStudyGroundDark(albedo,p,n,soil,broadShade,shade,cameraDistance,mountainMass,geography.r,derivatives);
                        }
                        else color=CIGround(albedo,p,n,soil,shade,cameraDistance);
                        // Real metre-scale source textures survive the broad painting on nearby ground.
                        // Fade continuously into the existing ink masses; no screen noise or vertex changes.
                        [branch] if(_RebuildDetailStrength>0)
                        {
                            float nearWeight=(1-smoothstep(_RebuildDetailRange.x,_RebuildDetailRange.y,cameraDistance))*_RebuildDetailStrength;
                            float detailTone=saturate(.27+(lum-.25)*1.1+(shade-.45)*.32);
                            float3 nearColour=lerp(_CIInk.rgb,_CIPaper.rgb,detailTone);
                            float3 chroma=clamp(albedo/max(.02,CILuma(albedo)),.7,1.3);
                            nearColour*=lerp(1,chroma,.28);
                            color=lerp(color,nearColour,nearWeight);
                        }
                    }
                }
                }
                [branch] if(ruleWeight>0)
                {
                    float3 masks=RuleMasks276(p,n,soil);
                    float3 rn=RuleNormal276(p,n,masks,cameraDistance);
                    Light light=GetMainLight(TransformWorldToShadowCoord(p));
                    float shade=saturate(dot(rn,light.direction));
                    float shadow=lerp(.38,1,light.shadowAttenuation);
                    float3 illumination=.40+.78*shade*shadow*light.color;
                    #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData=(InputData)0;inputData.positionWS=p;inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                    uint count=GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                    for(uint k=0;k<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);k++)
                    {Light l=GetAdditionalLight(k,p,half4(1,1,1,1));illumination+=l.color*l.distanceAttenuation*l.shadowAttenuation*saturate(dot(rn,l.direction))*.7;}
                    #endif
                    LIGHT_LOOP_BEGIN(count)
                    Light l=GetAdditionalLight(lightIndex,p,half4(1,1,1,1));illumination+=l.color*l.distanceAttenuation*l.shadowAttenuation*saturate(dot(rn,l.direction))*.7;
                    LIGHT_LOOP_END
                    #endif
                    float3 ruleColor=RuleColour276(p,n,masks,cameraDistance)*illumination;
                    #if defined(_SCREEN_SPACE_OCCLUSION)
                    ruleColor*=lerp(.70,1,GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS)).indirectAmbientOcclusion);
                    #endif
                    color=lerp(color,ruleColor,ruleWeight);
                }
                if(_RealmTintStrength>.001)
                {
                    float3 realm=SAMPLE_TEXTURE2D(_RealmPigment,sampler_RealmPigment,uv).rgb;
                    float3 chroma=clamp(realm/max(dot(realm,float3(.2126,.7152,.0722)),.02),.6,1.4);
                    float beforeLuma=CILuma(color);
                    color*=lerp(1,chroma,_RealmTintStrength*inside);
                    if(_CIEnabled>.5)color*=beforeLuma/max(.001,CILuma(color));
                }
                if(_GrassCoverStrength267>.001){
                    float2 meadowUV=p.xz/float2(4000,6000);
                    float meadow=SAMPLE_TEXTURE2D(_GrassCover267,sampler_GroundPathMask,meadowUV).r;
                    meadow*=step(0,meadowUV.x)*step(meadowUV.x,1)*step(0,meadowUV.y)*step(meadowUV.y,1);
                    // A low turf layer keeps the same path edge beyond the blade distance.
                    // It inherits existing light, relief, ink and atmospheric treatment.
                    float tuft=lerp(.90,1.04,Noise(p.xz*1.8));
                    color=lerp(color,color*float3(.64,.75,.58)*tuft,meadow*_GrassCoverStrength267);
                }
                [branch] if(_CanopyStrength293>0)
                {
                    // #293 forest masses: ground under dense canopy settles toward the canopy's ink tone,
                    // lightly near the eye (real tree shadows exist) and fully in the mid and far fields.
                    float2 canopyUV=saturate(p.xz/float2(4000,6000));
                    float2 canopy=SAMPLE_TEXTURE2D(_Canopy293,sampler_GroundPathMask,canopyUV).rg;
                    float3 pigment=SAMPLE_TEXTURE2D(_Realm293,sampler_GroundPathMask,canopyUV).rgb;
                    float reach=lerp(_CanopyRange293.z,1,smoothstep(_CanopyRange293.x,_CanopyRange293.y,cameraDistance));
                    // forested hillsides lose the bare-sand warmth (low-saturation grey-green, same value) ...
                    color=lerp(color,CILuma(color)*_CanopyForest293.rgb,saturate(canopy.g*_CanopyForest293.a*reach));
                    // ... and the ground under the stands themselves sinks into the canopy's ink tone
                    float3 under=_CanopyTint293.rgb*lerp(float3(1,1,1),clamp(pigment/max(.05,CILuma(pigment)),.7,1.3),.35);
                    color=lerp(color,color*under,saturate(canopy.r*_CanopyStrength293*reach));
                }
                color=InkChroma268(color,p);
                if(_RebuildScreenFog>.5)return half4(color,1);
                if(_CIEnabled>.5)return half4(IPStudyAtmosphere(color,cameraDistance,IPStudyAtmosphereMass(mountainMass,soil,geography.r,cameraDistance),geography,p,derivatives.form),1);
                float wash=smoothstep(_WashStart,max(_WashStart+1,_WashEnd),cameraDistance)*_WashStrength;
                return half4(lerp(color,_OhPaperColor.rgb,wash),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Depth
            #pragma multi_compile_instancing
            half4 Depth(V i):SV_Target {ClipEdge(i);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags {"LightMode"="DepthNormalsOnly"}
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Normals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 Normals(V i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                ClipEdge(i);float3 n=normalize(i.normal);
                float distanceWS=distance(_WorldSpaceCameraPos,i.world);
                float weight=RuleWeight276(distanceWS);
                if(weight>0)
                {
                    float2 uv=(i.world.xz-_GroundPathRect.xy)*_GroundPathRect.zw;
                    float inside=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
                    float path=_GroundKind>.5?1:(_GroundPath>.5?SAMPLE_TEXTURE2D(_GroundPathMask,sampler_GroundPathMask,uv).r*inside:0);
                    n=normalize(lerp(n,RuleNormal276(i.world,n,RuleMasks276(i.world,n,smoothstep(.08,.8,path)),distanceWS),weight));
                }
                #if defined(_GBUFFER_NORMALS_OCT)
                return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*.5+.5)),0);
                #else
                return half4(n,0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Shadow
            #pragma fragment Depth
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection,_LightPosition;
            V Shadow(A i)
            {
                V o=Vert(i);float3 direction=_LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                direction=normalize(_LightPosition-o.world);
                #endif
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.world,normalize(o.normal),direction));
                #if UNITY_REVERSED_Z
                o.positionCS.z=min(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
                #else
                o.positionCS.z=max(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            half4 Depth(V i):SV_Target {ClipEdge(i);return 0;}
            ENDHLSL
        }
    }
}
