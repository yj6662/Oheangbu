Shader "Oheangbu/Compact/Footpath"
{
 Properties{
  _BaseColor("Worn earth",Color)=(.48,.44,.36,.45)
  _BaseMap("Soil detail",2D)="gray"{}
  [Normal] _NormalMap("Soil normal",2D)="bump"{}
  _Scale("Repeats per metre",Float)=.6
 }
 SubShader{
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Geometry+31" "RenderType"="Transparent"}
  Pass{
   Tags{"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Back
   Offset -1,-1
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   TEXTURE2D(_NormalMap);SAMPLER(sampler_NormalMap);
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;float _Scale;
   CBUFFER_END
   struct A{float4 p:POSITION;float3 n:NORMAL;half4 color:COLOR;};
   struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half alpha:TEXCOORD2;};
   V Vert(A a){V v;v.w=TransformObjectToWorld(a.p.xyz);v.p=TransformWorldToHClip(v.w);v.n=TransformObjectToWorldNormal(a.n);v.alpha=a.color.a;return v;}
   half4 Frag(V v):SV_Target{
    float2 uv=v.w.xz*_Scale;
    half grain=dot(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb,half3(.2126,.7152,.0722));
    half3 fine=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,uv));
    half3 n=normalize(v.n+half3(fine.x,0,fine.y)*.35);
    Light light=GetMainLight(TransformWorldToShadowCoord(v.w));
    half shade=.58+.42*saturate(dot(n,light.direction))*light.shadowAttenuation;
    half variation=lerp(.8,1.18,saturate(grain*2.8));
    // Vertex edge fades expose the existing detailed terrain, without a raised opaque strip.
    return half4(_BaseColor.rgb*variation*shade,v.alpha*_BaseColor.a*lerp(.65,1,saturate(grain*3)));
   }
   ENDHLSL
  }
 }
}
