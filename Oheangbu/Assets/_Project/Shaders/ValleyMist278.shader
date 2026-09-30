Shader "Oheangbu/Prototype/ValleyMist278"
{
 Properties{_Color("Mist",Color)=(.72,.72,.68,.48)}
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Pass
  {
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _Color;
   CBUFFER_END
   struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
   struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;};
   V Vert(A i){V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.uv=i.uv;return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
   half4 Frag(V i):SV_Target
   {
    float2 q=i.uv*2-1;float shape=saturate(1-dot(q,q));shape=shape*shape;
    float n=noise(i.world.xz*.018+_Time.y*float2(.006,.003));n=.7*n+.3*noise(i.world.xz*.045-_Time.y*.002);
    float scene=LinearEyeDepth(SampleSceneDepth(i.positionCS.xy/_ScaledScreenParams.xy),_ZBufferParams);
    float eye=-TransformWorldToView(i.world).z;float soft=saturate((scene-eye)/12);
    return half4(_Color.rgb,shape*smoothstep(.2,.72,n)*_Color.a*soft);
   }
   ENDHLSL
  }
 }
}
