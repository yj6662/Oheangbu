// #293 candidate-only screen atmosphere (derived from Oheangbu/Compact/RebuildFog, which stays untouched).
// Distance fog + valley mist that pools in low ground + a slight realm air tint from the authored realm pigments.
// The sky (no depth) is never changed. All values are TEST (SPEC-COMPACT-HIGHLANDS-293).
Shader "Oheangbu/Reworld292/RealmFog293"
{
 Properties
 {
  _FogColor("Mist colour",Color)=(.72,.76,.73,1)
  _FogRange("Start/end metres",Vector)=(170,1250,0,0)
  _Realm293("Realm pigments (world XZ)",2D)="gray"{}
  _RealmFog293("Realm air: near/far metres, fog tint, mist tint",Vector)=(250,1400,.3,.15)
  _Mist293("Valley mist: base height, scale height, density per metre, start metres",Vector)=(95,38,.0015,70)
  _MistColor293("Valley mist colour (a = maximum)",Color)=(.82,.84,.84,.85)
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline"} ZWrite Off ZTest Always Cull Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
   TEXTURE2D(_Realm293);SAMPLER(sampler_Realm293);
   CBUFFER_START(UnityPerMaterial)
   float4 _FogColor,_FogRange,_RealmFog293,_Mist293,_MistColor293;
   CBUFFER_END
   float3 Chroma293(float3 c){return clamp(c/max(.05,dot(c,float3(.2126,.7152,.0722))),.6,1.5);}
   half4 Frag(Varyings i):SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half4 source=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,i.texcoord);
    float raw=SampleSceneDepth(i.texcoord);
    #if UNITY_REVERSED_Z
    if(raw<.000001)return source;
    #else
    if(raw>.999999)return source;
    raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
    #endif
    float3 world=ComputeWorldSpacePosition(i.texcoord,raw,UNITY_MATRIX_I_VP);
    float3 eye=_WorldSpaceCameraPos;float3 ray=world-eye;float d=length(ray);
    float t=saturate((d-_FogRange.x)/max(1,_FogRange.y-_FogRange.x));
    float fog=smoothstep(0,1,t);
    // realm air: the camera's own realm near, the seen ground's realm far (blurred mips, no hard borders)
    float2 extent=float2(4000,6000);
    float3 near=SAMPLE_TEXTURE2D_LOD(_Realm293,sampler_Realm293,saturate(eye.xz/extent),5).rgb;
    float3 far=SAMPLE_TEXTURE2D_LOD(_Realm293,sampler_Realm293,saturate(world.xz/extent),4).rgb;
    float3 tint=Chroma293(lerp(near,far,smoothstep(_RealmFog293.x,_RealmFog293.y,d)));
    float3 fogColour=_FogColor.rgb*lerp(float3(1,1,1),tint,_RealmFog293.z);
    // valley mist: density k*exp(-(y-base)/H) integrated analytically from the start distance to the surface
    float H=max(1,_Mist293.y);float s0=min(_Mist293.w,d);float dy=ray.y/max(d,.001);float y0=eye.y-_Mist293.x;
    float a=exp(-clamp((y0+dy*s0)/H,-20,20)),b=exp(-clamp((y0+dy*d)/H,-20,20));
    float optical=abs(dy)>.0005?(a-b)*H/dy:a*(d-s0);
    float mist=(1-exp(-max(0,optical)*_Mist293.z))*_MistColor293.a;
    float3 colour=lerp(source.rgb,fogColour,fog);
    colour=lerp(colour,_MistColor293.rgb*lerp(float3(1,1,1),tint,_RealmFog293.w),mist);
    return half4(colour,source.a);
   }
   ENDHLSL
  }
 }
}
