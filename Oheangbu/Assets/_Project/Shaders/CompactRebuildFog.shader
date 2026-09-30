Shader "Oheangbu/Compact/RebuildFog"
{
 Properties { _FogColor("Mist colour",Color)=(.72,.76,.73,1) _FogRange("Start/end metres",Vector)=(170,1250,0,0) }
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
   CBUFFER_START(UnityPerMaterial)
   float4 _FogColor,_FogRange;
   CBUFFER_END
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
    float d=distance(world,_WorldSpaceCameraPos);
    float t=saturate((d-_FogRange.x)/max(1,_FogRange.y-_FogRange.x));
    float fog=smoothstep(0,1,t);
    return half4(lerp(source.rgb,_FogColor.rgb,fog),source.a);
   }
   ENDHLSL
  }
 }
}
