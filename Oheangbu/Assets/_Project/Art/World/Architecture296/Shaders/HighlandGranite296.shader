// Highland derivative of Granite285 (shared shader untouched): keeps some photographed colour, optional lichen on up-facing faces,
// near detail fades to the texture's own average tone, distant faces dissolve into the realm ground wash.
Shader "Oheangbu/Architecture296/HighlandNatural"
{
 Properties { _BaseMap("Rock albedo",2D)="white"{} _BumpMap("Rock normal",2D)="bump"{} _MaskMap("AO roughness metal",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) _WorldScale("Repeats per metre",Float)=.37037 _Cutoff("Cutoff",Float)=.5 _AmbientFloor("Diffuse fill",Range(0,1))=.28 _SurfaceLift("Weathered stone value",Range(0,.2))=.025 _FarTone("Distant realm ground tone",Color)=(.76,.74,.68,1) _FarRange("Near to far wash metres",Vector)=(45,320,0,0) _FarStrength("Distant wash",Range(0,1))=.6 _Chroma("Photographed colour kept",Range(0,1))=.12 _NearRange("Near detail start/end metres",Vector)=(12,90,0,0) _MossMap("Lichen albedo",2D)="grey"{} _MossBump("Lichen normal",2D)="bump"{} _MossAmount("Lichen cover",Range(0,1))=0 _MossScale("Lichen repeats per metre",Float)=.5 _MossUp("Lichen up-facing start/full",Vector)=(.35,.8,0,0) _Value293("Scan value target (linear luminance, 0 off), local contrast",Vector)=(0,1,0,0) }
 SubShader {
 Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);TEXTURE2D(_MaskMap);SAMPLER(sampler_MaskMap);TEXTURE2D(_MossMap);SAMPLER(sampler_MossMap);TEXTURE2D(_MossBump);SAMPLER(sampler_MossBump);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;float _WorldScale;float _Cutoff;half _AmbientFloor;half _SurfaceLift;half4 _FarTone;float4 _FarRange;half _FarStrength;half _Chroma;float4 _NearRange;half _MossAmount;float _MossScale;float4 _MossUp;float4 _Value293;
 CBUFFER_END
 #include "Assets/_Project/Art/World/Architecture296/Shaders/HighlandSurface296.hlsl"
 struct A {float4 vertex:POSITION;float3 normal:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 clip:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;half fog:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
 V Vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_TRANSFER_INSTANCE_ID(i,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=TransformObjectToWorld(i.vertex.xyz);o.clip=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normal);o.fog=ComputeFogFactor(o.clip.z);return o;}
 HighlandSample293 SampleRock(V i){return HighlandSurface293(i.world,i.normal,_WorldScale,_BaseColor.rgb,_Chroma,_NearRange.xy,_Value293.xy,TEXTURE2D_ARGS(_BaseMap,sampler_BaseMap),TEXTURE2D_ARGS(_BumpMap,sampler_BumpMap),TEXTURE2D_ARGS(_MaskMap,sampler_MaskMap),_MossAmount,_MossScale,_MossUp.xy,TEXTURE2D_ARGS(_MossMap,sampler_MossMap),TEXTURE2D_ARGS(_MossBump,sampler_MossBump));}
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
  UNITY_SETUP_INSTANCE_ID(i);HighlandSample293 s=SampleRock(i);
  Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
  half3 ambient=clamp(SampleSH(s.normal),_AmbientFloor.xxx,(_AmbientFloor+.18).xxx);  // sky light counts once the scene has any
  half diffuse=saturate(dot(s.normal,sun.direction));
  half3 col=(s.albedo+_SurfaceLift)*(ambient+min(sun.color,half3(2,2,2))*diffuse*sun.shadowAttenuation*.65)*s.occlusion;
  // Keep the lit form, but carry it in the same paper tone as the surrounding ground at range.
  half lit=diffuse*sun.shadowAttenuation;
  half3 wash=_FarTone.rgb*(.58+.52*lit)*lerp(.82,1.04,s.occlusion);
  half far=smoothstep(_FarRange.x,_FarRange.y,distance(GetCameraPositionWS(),i.world))*_FarStrength;
  col=lerp(col,wash,far);
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
