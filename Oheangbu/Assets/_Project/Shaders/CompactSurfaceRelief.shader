Shader "Oheangbu/Compact/SurfaceRelief"
{
 Properties {
 _BaseColor("Existing pigment",Color)=(.29,.31,.30,1)
 _Ambient("Existing cave fill",Range(0,1))=.28
 _Relief("Height / cavity / stone",2D)="white"{}
 [Normal] _ReliefNormal("Physical relief normal",2D)="bump"{}
 _FineMap("Existing rock or soil detail",2D)="gray"{}
 [Normal] _FineNormal("Fine surface normal",2D)="bump"{}
 _Scale("Tile repeats per metre",Float)=.25
 _Height("Relief metres",Float)=.09
 _NormalStrength("Relief strength",Float)=1
 _Floor("Soil surface",Float)=0
 [HideInInspector] _BaseMap("Base",2D)="white"{}
 [HideInInspector] _Cull("Cull",Float)=0
 [HideInInspector] _Cutoff("Cutoff",Float)=.5
 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 Cull Off
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_Relief);SAMPLER(sampler_Relief);
 TEXTURE2D(_ReliefNormal);SAMPLER(sampler_ReliefNormal);
 TEXTURE2D(_FineMap);SAMPLER(sampler_FineMap);
 TEXTURE2D(_FineNormal);SAMPLER(sampler_FineNormal);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor,_BaseMap_ST;float _Ambient,_Scale,_Height,_NormalStrength,_Floor,_Cutoff;
 CBUFFER_END
 struct A {float4 p:POSITION;float3 n:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
 V vert(A i){V o=(V)0;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.w=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);return o;}
 void Plane(float2 uv,float3 view,out float3 data,out float2 slope)
 {
  // Bounded view-dependent offset. Height and normal describe the same stones.
  float h=SAMPLE_TEXTURE2D(_Relief,sampler_Relief,uv).r;
  float2 offset=clamp(view.xy/max(.28,abs(view.z)),-2,2)*((h-.3)*_Height*_Scale);
  uv-=offset;
  data=SAMPLE_TEXTURE2D(_Relief,sampler_Relief,uv).rgb;
  float3 normal=UnpackNormalScale(SAMPLE_TEXTURE2D(_ReliefNormal,sampler_ReliefNormal,uv),_NormalStrength);
  float3 fine=UnpackNormalScale(SAMPLE_TEXTURE2D(_FineNormal,sampler_FineNormal,uv*1.17),1.05);
  slope=normal.xy/max(.22,normal.z)+fine.xy*.75;
  float value=dot(SAMPLE_TEXTURE2D(_FineMap,sampler_FineMap,uv*1.17).rgb,float3(.2126,.7152,.0722));
  data.z=clamp(1+(value-.17)*3.8,.40,1.6);
 }
 void Surface(V i,float3 n,out float3 normal,out float pigment)
 {
  float3 weight=pow(abs(n),8);weight/=max(.001,dot(weight,1));
  float3 view=normalize(_WorldSpaceCameraPos-i.w);float3 d=0;float3 perturb=0;
  float3 data;float2 slope;
  if(weight.x>.001){Plane(i.w.zy*_Scale,view.zyx,data,slope);d+=data*weight.x;perturb+=float3(0,slope.y,slope.x)*weight.x;}
  if(weight.y>.001){Plane(i.w.xz*_Scale,view.xzy,data,slope);d+=data*weight.y;perturb+=float3(slope.x,0,slope.y)*weight.y;}
  if(weight.z>.001){Plane(i.w.xy*_Scale,view.xyz,data,slope);d+=data*weight.z;perturb+=float3(slope.x,slope.y,0)*weight.z;}
  float fade=1-smoothstep(24,70,distance(i.w,_WorldSpaceCameraPos));
  normal=normalize(n+(perturb-n*dot(n,perturb))*fade);
  // Scalar material variation preserves the authored hue; cavities darken locally.
  pigment=lerp(1,(.86+d.x*.38)*lerp(.60,1,d.y)*d.z,fade);
 }
 ENDHLSL
 Pass {
 Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma target 4.5
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
 #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
 #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 half4 frag(V i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target {
 UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 float3 geometric=normalize(i.n)*IS_FRONT_VFACE(face,1,-1),n;float pigment;Surface(i,geometric,n,pigment);
 Light sun=GetMainLight(TransformWorldToShadowCoord(i.w));
 float3 illumination=_Ambient*float3(.78,.87,1)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation*.28;
 #if defined(_ADDITIONAL_LIGHTS)
 InputData inputData=(InputData)0;inputData.positionWS=i.w;inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);
 uint count=GetAdditionalLightsCount();LIGHT_LOOP_BEGIN(count)
 Light l=GetAdditionalLight(lightIndex,i.w);illumination+=l.color*l.distanceAttenuation*l.shadowAttenuation*(.1+.9*saturate(dot(n,l.direction)));
 LIGHT_LOOP_END
 #endif
 return half4(_BaseColor.rgb*pigment*illumination,1);
 }
 ENDHLSL
 }
 Pass {
 Tags {"LightMode"="DepthNormals"}
 HLSLPROGRAM
 #pragma target 4.5
 #pragma vertex vert
 #pragma fragment normals
 #pragma multi_compile_instancing
 #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
 half4 normals(V i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target {
 float3 n;float pigment;Surface(i,normalize(i.n)*IS_FRONT_VFACE(face,1,-1),n,pigment);
 #if defined(_GBUFFER_NORMALS_OCT)
 return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*.5+.5)),0);
 #else
 return half4(n,0);
 #endif
 }
 ENDHLSL
 }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
