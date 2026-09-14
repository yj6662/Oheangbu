Shader "Oheangbu/EarlyRegionFoliageCard"
{
 Properties { _BaseMap("Owned plant atlas",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) _Billboard("Upright",Float)=1 _BillboardViews("Views",Float)=4 _Far("Distance",Float)=2400 _Cutoff("Cutout",Range(0,1))=.24 }
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
   CBUFFER_END
   struct A { float3 p:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct V { float4 p:SV_POSITION;float2 uv:TEXCOORD0;float distance:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO };
   V vert(A i)
   {
    UNITY_SETUP_INSTANCE_ID(i);V o;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 centre=TransformObjectToWorld(float3(0,0,0));float3 world=TransformObjectToWorld(i.p);
    float3 facing=normalize(float3(_WorldSpaceCameraPos.x-centre.x,0,_WorldSpaceCameraPos.z-centre.z)+float3(0,0,.00001));
    if(_Billboard>.5){float3 right=cross(float3(0,1,0),facing);float sx=length(unity_ObjectToWorld._m00_m10_m20),sy=length(unity_ObjectToWorld._m01_m11_m21);world=centre+right*i.p.x*sx+float3(0,i.p.y*sy,0);}
    o.uv=i.uv;
    if(_BillboardViews>1.5){float3 view=mul((float3x3)unity_WorldToObject,_WorldSpaceCameraPos-centre);float index=floor(frac(atan2(view.x,-view.z)/6.2831853+1+.5/_BillboardViews)*_BillboardViews);o.uv.x=(i.uv.x*.996+.002+index)/_BillboardViews;}
    o.distance=length(_WorldSpaceCameraPos.xz-centre.xz);o.p=TransformWorldToHClip(world);return o;
   }
   half4 frag(V i):SV_Target
   {
    half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;clip(c.a-_Cutoff);
    float noise=frac(52.9829189*frac(dot(floor(i.p.xy),float2(.06711056,.00583715))));clip(saturate((_Far-i.distance)/120)-noise-.001);
    c.rgb=lerp(c.rgb,half3(.37,.40,.36),saturate((i.distance-450)/3500)*.55);return half4(c.rgb,1);
   }
   ENDHLSL
  }
 }
}
