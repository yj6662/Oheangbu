Shader "Oheangbu/Ink Cloud Sky"
{
 Properties {
  _Horizon("Horizon",Color)=(.9,.88,.83,1)
  _Zenith("Zenith",Color)=(.73,.75,.72,1)
  _Cloud("Cloud",Color)=(.55,.58,.55,1)
  _CloudDensity("Cloud density",Float)=.14
  _CloudScale("Cloud scale",Float)=3.2
  _CloudSpeed("Cloud speed",Float)=.002
  _CapitalAzimuth("Capital azimuth",Float)=.26
  _CapitalAtmosphere("Capital atmosphere",Float)=.018
 }
 SubShader {
  Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline"}
  Cull Off ZWrite Off
  Pass {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Horizon,_Zenith,_Cloud; float _CloudDensity,_CloudScale,_CloudSpeed,_CapitalAzimuth,_CapitalAtmosphere;
   CBUFFER_END
   struct A {float4 positionOS:POSITION;}; struct V {float4 positionCS:SV_POSITION;float3 direction:TEXCOORD0;};
   V vert(A a){ V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.direction=a.positionOS.xyz;return o; }
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
   half4 frag(V i):SV_Target{
    float3 d=normalize(i.direction);float h=saturate(d.y);
    float2 p=d.xz/(.35+h)*_CloudScale+float2(_Time.y*_CloudSpeed,0);
    float n=noise(p)*.57+noise(p*2.13+8)*.28+noise(p*4.1+19)*.15;
    float clouds=smoothstep(.45,.75,n)*smoothstep(.015,.28,h)*_CloudDensity;
    half3 c=lerp(_Horizon.rgb,_Zenith.rgb,pow(h,.65));c=lerp(c,_Cloud.rgb,clouds);
    float direction=max(0,dot(normalize(d.xz+float2(.00001,0)),float2(sin(_CapitalAzimuth),cos(_CapitalAzimuth))));
    c+=_CapitalAtmosphere*pow(direction,12)*(1-smoothstep(.03,.4,h))*half3(.35,.22,.05);
    return half4(c,1);
   }
   ENDHLSL
  }
 }
}
