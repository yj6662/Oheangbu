Shader "Oheangbu/Study/InkPaintingFoliageCard"
{
 Properties {
        [ToggleUI] _PaintedFoliage("Painted foliage",Float)=1
        _PaintedLodRgbGain("Species LOD ink-input RGB gain",Range(.25,4))=1
        [ToggleUI] _PaintedStochasticFade("One-pixel stochastic distance fade",Float)=0
        [ToggleUI] _PaintedCanopyQuiet("Quiet canopy forward shading",Float)=0
        _PaintedCanopyQuietRange("Quiet near/full distance metres",Vector)=(2,14,0,0)
        _PaintedCanopyQuietSurface("Albedo flatten/normal retain/source mean/NdotL flatten",Vector)=(.80,.18,.18,.65)
        [ToggleUI] _PaintedCanopy("Grouped canopy ink tones",Float)=0
        _PaintedCanopyTones("Loaded/middle/pale/continuous shading",Vector)=(.018,.035,.075,.12)
        [NoScaleOffset] _PaintedGeoField("Actual terrain ink regions and flow",2D)="black"{}
        _PaintedGeoRect("Terrain field origin size XZ",Vector)=(0,0,1,1)
        [ToggleUI] _PaintedGeoEnabled("Use terrain field",Float)=0
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
        [ToggleUI] _CIAtmosphereOverride("Painterly atmosphere override",Float)=0
        [ToggleUI] _CIPainterly("Painterly landscape revision",Float)=0
        _CIMidAirRange("Middle atmosphere range",Vector)=(500,1500,0,0)
        _CIFarAirRange("Far atmosphere range override",Vector)=(1500,3400,0,0)
        _CIAirStrengths("Middle/far atmosphere strengths",Vector)=(.18,.64,0,0)
        _CIStrokeSpacingWidth("Stroke spacing/width metres",Vector)=(20,40,2,7)
        _CIStrokeFadeStrength("Stroke fade/strength",Vector)=(200,1400,.02,0)
        _CIPigmentContrast("Pigment contrast/detail scale",Vector)=(.03,.65,0,0)
        _CIFoliagePainterly("Foliage range/mip bias/contrast",Vector)=(30,180,1.5,.3)
        _CIMountainTones("Mountain ink tone interval",Vector)=(.06,.18,0,0)
        _CIMountainSlope("Mountain slope degrees",Vector)=(12,37,0,0)
        [ToggleUI] _CIBroadBrush("Broad mountain ink coats",Float)=0
        _CIBroadBrushScale("Broad stroke width/length metres",Vector)=(80,240,420,1000)
        _CIBroadBrushCoverage("Coat opacity/edge/second/warp",Vector)=(.90,.18,.76,.20)
        _CIBroadBrushTones("Broad dark/soft coat tones",Vector)=(.015,.07,0,0)
        _CIBroadBrushBands("Mountain band thresholds/softness/amount",Vector)=(.33,.66,.12,.8)
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

 _BaseMap("Owned plant atlas",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) _Billboard("Upright",Float)=1 _BillboardViews("Views",Float)=4 _Far("Distance",Float)=2400 _Cutoff("Cutout",Range(0,1))=.24 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
  Cull Off ZWrite On
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float _Billboard,_BillboardViews,_Far,_Cutoff;
   float4 _BaseMap_TexelSize;
   float _PaintedFoliage;float _PaintedLodRgbGain;
            float _PaintedStochasticFade;
            float _PaintedCanopyQuiet;
            float4 _PaintedCanopyQuietRange,_PaintedCanopyQuietSurface;
            float _PaintedCanopy;
            float4 _PaintedCanopyTones;
            float4 _PaintedGeoRect;float _PaintedGeoEnabled;
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
   CBUFFER_END
   #include "../CompactInkLandscape.hlsl"
   #include "../CompactPainterlyFoliage.hlsl"
        #include "InkPaintingFoliage.hlsl"
        #include "InkPaintingFoliageFade.hlsl"
        #include "InkPaintingGeography.hlsl"
   struct A { float3 p:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct V { float4 p:SV_POSITION;float2 uv:TEXCOORD0;float distance:TEXCOORD1;float3 world:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO };
   V vert(A i)
   {
    UNITY_SETUP_INSTANCE_ID(i);V o;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 centre=TransformObjectToWorld(float3(0,0,0));float3 world=TransformObjectToWorld(i.p);
    float3 facing=normalize(float3(_WorldSpaceCameraPos.x-centre.x,0,_WorldSpaceCameraPos.z-centre.z)+float3(0,0,.00001));
    if(_Billboard>.5){float3 right=cross(float3(0,1,0),facing);float sx=length(unity_ObjectToWorld._m00_m10_m20),sy=length(unity_ObjectToWorld._m01_m11_m21);world=centre+right*i.p.x*sx+float3(0,i.p.y*sy,0);}
    o.uv=i.uv;
    if(_BillboardViews>1.5){float3 view=mul((float3x3)unity_WorldToObject,_WorldSpaceCameraPos-centre);float index=floor(frac(atan2(view.x,-view.z)/6.2831853+1+.5/_BillboardViews)*_BillboardViews);o.uv.x=(i.uv.x*.996+.002+index)/_BillboardViews;}
    o.world=world;o.distance=length(_WorldSpaceCameraPos.xz-centre.xz);o.p=TransformWorldToHClip(world);return o;
   }
   half4 frag(V i):SV_Target
   {
    half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;clip(c.a-_Cutoff);
    float noise=IPFoliageFadeNoise(i.p.xy,_PaintedStochasticFade);
    float distanceVisibility=saturate((_Far-i.distance)/120);
    // Preserve original partial fade; full coverage never discards a hash endpoint.
    if(distanceVisibility<1)clip(distanceVisibility<=0?-1:distanceVisibility-noise-.001);
    if(_CIEnabled>.5){float d=distance(_WorldSpaceCameraPos,i.world);c.rgb=CIPainterlyFoliageAlbedo(i.uv,c.rgb,d,_BillboardViews);float quietWeight=0;if(_PaintedFoliage>.5&&_PaintedCanopyQuiet>.5)quietWeight=IPCanopyQuietWeight(d,_PaintedCanopyQuietRange.xy);c.rgb=IPCanopyQuietAlbedo(c.rgb,_BaseColor.rgb,quietWeight,_PaintedCanopyQuietSurface);c.rgb=_PaintedFoliage>.5?IPFoliageInkColour(c.rgb*clamp(_PaintedLodRgbGain,.25,4),1,_PaintedCanopy,_PaintedCanopyTones):CIPainterlyFoliageColour(c.rgb,d);c.rgb=IPGeographyAtmosphere(CIAttenuateChroma(c.rgb,d),d,i.world);}
    else c.rgb=lerp(c.rgb,half3(.37,.40,.36),saturate((i.distance-450)/3500)*.55);
    return half4(c.rgb,1);
   }
   ENDHLSL
  }
 }
}
