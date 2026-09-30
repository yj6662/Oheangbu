Shader "Oheangbu/Compact/MountainMist263"
{
 Properties {
  _FogColor("Distance wash",Color)=(.72,.76,.73,1)
  _FogRange("Start end",Vector)=(170,1250,0,0)
  _MistColor("Mountain cloud",Color)=(.79,.81,.78,1)
  _MistBand("Bottom top density speed",Vector)=(145,300,.006,1.6)
  _MistClock("Preview time (-1 runtime)",Float)=-1
 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline"} ZWrite Off ZTest Always Cull Off
 Pass {
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _FogColor,_FogRange,_MistColor,_MistBand;float _MistClock;
 CBUFFER_END
 float hash3(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
 float noise3(float3 p){float3 q=floor(p),f=frac(p);f=f*f*(3-2*f);
 return lerp(lerp(lerp(hash3(q),hash3(q+float3(1,0,0)),f.x),lerp(hash3(q+float3(0,1,0)),hash3(q+float3(1,1,0)),f.x),f.y),
 lerp(lerp(hash3(q+float3(0,0,1)),hash3(q+float3(1,0,1)),f.x),lerp(hash3(q+float3(0,1,1)),hash3(q+1),f.x),f.y),f.z);}
 half4 Frag(Varyings i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 half4 source=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,i.texcoord);float raw=SampleSceneDepth(i.texcoord);
 #if UNITY_REVERSED_Z
 bool sky=raw<.000001;
 #else
 bool sky=raw>.999999;raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
 #endif
 float3 world=ComputeWorldSpacePosition(i.texcoord,raw,UNITY_MATRIX_I_VP);
 float3 ray=normalize(world-_WorldSpaceCameraPos);float distanceToSurface=sky?1600:distance(world,_WorldSpaceCameraPos);
 float t=saturate((distanceToSurface-_FogRange.x)/max(1,_FogRange.y-_FogRange.x));
 float3 color=sky?source.rgb:lerp(source.rgb,_FogColor.rgb,smoothstep(0,1,t));
 // Integrate only air in front of the actual depth, never paint mist through foreground silhouettes.
 float nearD=110,farD=min(distanceToSurface,1600),stepD=max(0,farD-nearD)/16;
 float clock=_MistClock>=0?_MistClock:_Time.y;float optical=0;
 [unroll]for(int k=0;k<16;k++){
 float d=nearD+(k+.5)*stepD;float3 p=_WorldSpaceCameraPos+ray*d;
 float band=smoothstep(_MistBand.x,_MistBand.x+28,p.y)*(1-smoothstep(_MistBand.y-45,_MistBand.y,p.y));
 float3 cell=(p+float3(clock*_MistBand.w,0,clock*.32))*float3(.009,.027,.009);
 float n=noise3(cell);float breathe=.5+.5*sin(clock*.027+cell.x*.57+cell.z*.71);
 float density=smoothstep(.48,.78,n+breathe*.15)*band;
 optical+=density*stepD*_MistBand.z*smoothstep(100,300,d);
 }
 float fog=1-exp(-optical);return half4(lerp(color,_MistColor.rgb,fog),source.a);
 }
 ENDHLSL
 }
 }
}
