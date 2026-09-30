Shader "Oheangbu/Demo/WaterJet"
{
 Properties
 {
  _Noise("Existing flow noise",2D)="gray"{}
  _BaseColor("Water tint",Color)=(.12,.32,.39,.65)
  _FlowTime("Shared effect time",Float)=0
  _Fade("Visibility",Range(0,1))=1
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Pass
  {
   Tags{"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_Noise);SAMPLER(sampler_Noise);
   CBUFFER_START(UnityPerMaterial)
    float4 _Noise_ST, _BaseColor;
    float _FlowTime, _Fade;
   CBUFFER_END
   struct Attributes{float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
   struct Varyings{float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;};
   Varyings vert(Attributes v)
   {Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);
    o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=v.uv;return o;}
   half4 frag(Varyings i):SV_Target
   {
    float2 uv=i.uv*float2(2,1.1)-float2(0,_FlowTime*8);
    float n=SAMPLE_TEXTURE2D(_Noise,sampler_Noise,uv).r;
    float fine=SAMPLE_TEXTURE2D(_Noise,sampler_Noise,uv*float2(2.3,.8)+float2(_FlowTime*.4,0)).r;
    float3 N=normalize(i.normalWS),V=GetWorldSpaceNormalizeViewDir(i.positionWS);
    float facing=abs(dot(N,V)),fresnel=pow(1-facing,3);
    Light light=GetMainLight();float3 H=normalize(light.direction+V);
    float spec=pow(abs(dot(N,H)),54)*.35;
    float streak=smoothstep(.60,.86,n*.6+fine*.4);
    float3 color=lerp(_BaseColor.rgb,float3(.71,.85,.87),saturate(fresnel*.65+streak*.48+spec));
    return half4(color,_Fade*_BaseColor.a*(.48+.40*fresnel+.12*streak));
   }
   ENDHLSL
  }
 }
}
