Shader "Oheangbu/Prototype/Timber289"
{
 Properties { _BaseMap("Rock albedo",2D)="white"{} _BumpMap("Rock normal",2D)="bump"{} _MaskMap("AO roughness metal",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) _WorldScale("Repeats per metre",Float)=.37037 _Cutoff("Cutoff",Float)=.5 _AmbientFloor("Diffuse fill",Range(0,1))=.28 _SurfaceLift("Weathered stone value",Range(0,.2))=.025 }
 SubShader {
 Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);TEXTURE2D(_MaskMap);SAMPLER(sampler_MaskMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;float _WorldScale;float _Cutoff;half _AmbientFloor;half _SurfaceLift;
 CBUFFER_END
 #include "GraniteSurface285.hlsl"
 struct A {float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 clip:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;half fog:TEXCOORD2;float2 uv:TEXCOORD3;half4 tangent:TEXCOORD4;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
 V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=TransformObjectToWorld(i.vertex.xyz);o.clip=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normal);o.fog=ComputeFogFactor(o.clip.z);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);o.tangent=half4(TransformObjectToWorldDir(i.tangent.xyz),i.tangent.w*GetOddNegativeScale());return o;}
 GraniteSample285 SampleRock(V i){
  GraniteSample285 s;half3 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb;
  c=lerp(dot(c,half3(.3,.59,.11)).xxx,c,.35);s.albedo=c*_BaseColor.rgb;
  half3 n=normalize(i.normal);half3 t=normalize(i.tangent.xyz);half3 b=normalize(cross(n,t))*i.tangent.w;
  half3 detail=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),.65);
  s.normal=normalize(t*detail.x+b*detail.y+n*detail.z);
  half3 arm=SAMPLE_TEXTURE2D(_MaskMap,sampler_MaskMap,i.uv).rgb;s.occlusion=lerp(.7,1,arm.r);s.smoothness=(1-arm.g)*.12;
  return s;
 }
 ENDHLSL
 Pass {
 Name "ForwardLit" Tags{"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma target 3.5
 #pragma multi_compile_instancing
 #pragma multi_compile_fog
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 half4 Frag(V i):SV_Target {
  UNITY_SETUP_INSTANCE_ID(i);GraniteSample285 s=SampleRock(i);
  Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
  half3 ambient=clamp(SampleSH(s.normal),_AmbientFloor.xxx,(_AmbientFloor+.1).xxx);
  half diffuse=saturate(dot(s.normal,sun.direction));
  half3 col=(s.albedo+_SurfaceLift)*(ambient+min(sun.color,half3(2,2,2))*diffuse*sun.shadowAttenuation*.65)*s.occlusion;
  return half4(MixFog(col,i.fog),1);
 }
 ENDHLSL
 }
 // Keep the exact UnityPerMaterial layout in every pass. Reusing URP Lit's
 // ShadowCaster would introduce a different material CBUFFER layout.
 Pass {Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ZWrite On ZTest LEqual ColorMask 0
 HLSLPROGRAM
 #pragma vertex ShadowVert
 #pragma fragment ShadowFrag
 #pragma multi_compile_instancing
 #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
 float3 _LightDirection;float3 _LightPosition;
 float4 ShadowVert(A i):SV_POSITION {
  UNITY_SETUP_INSTANCE_ID(i);
  float3 world=TransformObjectToWorld(i.vertex.xyz);float3 n=TransformObjectToWorldNormal(i.normal);
  #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
  float3 direction=normalize(_LightPosition-world);
  #else
  float3 direction=_LightDirection;
  #endif
  float4 clip=TransformWorldToHClip(ApplyShadowBias(world,n,direction));
  #if UNITY_REVERSED_Z
  clip.z=min(clip.z,clip.w*UNITY_NEAR_CLIP_VALUE);
  #else
  clip.z=max(clip.z,clip.w*UNITY_NEAR_CLIP_VALUE);
  #endif
  return clip;
 }
 half4 ShadowFrag():SV_Target{return 0;}
 ENDHLSL }
 Pass {Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask R ZWrite On
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Depth
 #pragma multi_compile_instancing
 half Depth(V i):SV_Target{return i.clip.z;}
 ENDHLSL }
 Pass {Name "DepthNormals" Tags{"LightMode"="DepthNormals"} ZWrite On
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Normals
 #pragma multi_compile_instancing
 #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
 half4 Normals(V i):SV_Target {UNITY_SETUP_INSTANCE_ID(i);half3 n=SampleRock(i).normal;
 #if defined(_GBUFFER_NORMALS_OCT)
 return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*.5+.5)),0);
 #else
 return half4(n,0);
 #endif
 }
 ENDHLSL }
 }
}
