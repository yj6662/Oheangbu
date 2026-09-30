Shader "Oheangbu/EarlyRegionFoliageCard"
{
 Properties {
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
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float _Billboard,_BillboardViews,_Far,_Cutoff;
   float4 _BaseMap_TexelSize;
   float _CIEnabled,_CIDarkNear,_CIDetailContrast,_CINormalStrength;
   float4 _CIInk,_CIPaper,_CIAir,_CIDetailRange,_CIRockRange,_CIMountainRange,_CIAirRange,_CITones;
            float4 _CIMountainTones,_CIMountainSlope;
            float _CIBroadBrush;
            float4 _CIBroadBrushScale,_CIBroadBrushCoverage,_CIBroadBrushTones,_CIBroadBrushBands;
   float _CIAtmosphereOverride,_CIPainterly;
   float4 _CIMidAirRange,_CIFarAirRange,_CIAirStrengths,_CIStrokeSpacingWidth,_CIStrokeFadeStrength,_CIPigmentContrast,_CIFoliagePainterly;
   CBUFFER_END
   #include "CompactInkLandscape.hlsl"
   #include "CompactPainterlyFoliage.hlsl"
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
    float noise=frac(52.9829189*frac(dot(floor(i.p.xy),float2(.06711056,.00583715))));clip(saturate((_Far-i.distance)/120)-noise-.001);
    if(_CIEnabled>.5){float d=distance(_WorldSpaceCameraPos,i.world);c.rgb=CIPainterlyFoliageAlbedo(i.uv,c.rgb,d,_BillboardViews);c.rgb=CIPainterlyFoliageColour(c.rgb,d);c.rgb=CIAtmosphere(CIAttenuateChroma(c.rgb,d),d);}
    else c.rgb=lerp(c.rgb,half3(.37,.40,.36),saturate((i.distance-450)/3500)*.55);
    return half4(c.rgb,1);
   }
   ENDHLSL
  }
 }
}
