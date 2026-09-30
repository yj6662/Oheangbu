// #300 teaser sky (presentation only, not a realm sky): ink-wash cloud masses over the realm gradient. Colours stay on the
// ink..paper axis with the realm's hue, nothing emits. Wet edges (pigment pooled at the wash boundary) and darker cores give
// the layered 담묵 -> 농묵 read; the clouds thin into the horizon mist so the far mountains dissolve into paper.
// A storm side (yaw) thickens the cover, the opposite side clears. The world ink post still tones it (sky = half ink tone).
Shader "Oheangbu/Teaser/Ink Wash Sky 300"
{
 Properties {
  _Horizon("Horizon (paper side)", Color)=(.90,.87,.79,1)
  _Zenith("Zenith", Color)=(.64,.66,.64,1)
  _Wash("Cloud light wash", Color)=(.72,.72,.68,1)
  _Ink("Cloud ink core", Color)=(.30,.31,.31,1)
  _Coverage("Coverage", Range(0,1))=.46
  _Softness("Edge softness", Range(.005,.4))=.07
  _Scale("Scale", Float)=1.15
  _Stretch("Stretch along the bands", Float)=2.2
  _BandYaw("Band direction (deg, world yaw)", Float)=90
  _Warp("Domain warp", Float)=1.1
  _WetEdge("Wet edge ink", Range(0,1))=.45
  _CoreInk("Core ink", Range(0,1))=.6
  _StormYaw("Storm centre (deg, world yaw)", Float)=90
  _StormWidth("Storm half width (deg)", Float)=70
  _StormGain("Storm coverage gain", Range(0,1))=.25
  _StormInk("Storm extra ink", Range(0,1))=.25
  _ClearGain("Clearing on the opposite side", Range(0,1))=.25
  _MistHeight("Horizon mist height", Range(.01,.5))=.10
  _ZenithFade("Clouds thin toward the zenith", Range(0,1))=.35
  _Offset("Noise offset (xy)", Vector)=(0,0,0,0)
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
   half4 _Horizon,_Zenith,_Wash,_Ink; float4 _Offset;
   float _Coverage,_Softness,_Scale,_Stretch,_BandYaw,_Warp,_WetEdge,_CoreInk,_StormYaw,_StormWidth,_StormGain,_StormInk,_ClearGain,_MistHeight,_ZenithFade;
   CBUFFER_END
   struct A {float4 positionOS:POSITION;}; struct V {float4 positionCS:SV_POSITION;float3 direction:TEXCOORD0;};
   V vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.direction=a.positionOS.xyz;return o;}
   float hash21(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
   float vnoise(float2 p){float2 i=floor(p),f=frac(p);float2 u=f*f*f*(f*(f*6-15)+10);
    float a=hash21(i),b=hash21(i+float2(1,0)),c=hash21(i+float2(0,1)),d=hash21(i+1);return lerp(lerp(a,b,u.x),lerp(c,d,u.x),u.y);}
   float fbm(float2 p){float s=0,a=.5;const float2x2 m=float2x2(1.6,1.2,-1.2,1.6);
    [unroll] for(int k=0;k<6;k++){s+=a*vnoise(p);p=mul(m,p)+17.3;a*=.5;} return s/.984375;}
   float deltaAngle(float a,float b){return fmod(b-a+540.0,360.0)-180.0;}
   half4 frag(V i):SV_Target{
    float3 d=normalize(i.direction);float h=d.y;float hs=saturate(h);
    // clouds on a flattened dome, banded along _BandYaw
    float2 q=d.xz/(hs+.22);
    float by=radians(_BandYaw);float2 axis=float2(sin(by),cos(by));float2 perp=float2(axis.y,-axis.x);
    float2 p=float2(dot(q,axis)/max(_Stretch,.1),dot(q,perp))*_Scale+_Offset.xy;
    float2 w=float2(fbm(p+float2(1.7,9.2)),fbm(p+float2(8.3,2.8)));
    float n=fbm(p+_Warp*(w-.5));
    // storm side thickens, the far side clears
    float da=abs(deltaAngle(degrees(atan2(d.x,d.z)),_StormYaw));
    float storm=1-smoothstep(0,max(_StormWidth,1),da);
    float clear=smoothstep(90,180,da);
    float thr=1-saturate(_Coverage+_StormGain*storm-_ClearGain*clear);
    float elev=smoothstep(.015,.015+_MistHeight,h)*(1-_ZenithFade*smoothstep(.45,1,h));
    float mask=smoothstep(thr-_Softness,thr+_Softness,n)*elev;
    float core=smoothstep(thr,thr+.3,n);
    float edge=exp(-pow((n-thr)/max(_Softness*1.6,1e-3),2))*elev;
    half3 sky=lerp(_Horizon.rgb,_Zenith.rgb,pow(hs,.6));
    half3 cloud=lerp(_Wash.rgb,_Ink.rgb,saturate(core*_CoreInk+storm*_StormInk*core));
    half3 c=lerp(sky,cloud,mask);
    c=lerp(c,c*(1-.55*_WetEdge),edge*.7);
    c=lerp(_Horizon.rgb,c,smoothstep(-.02,_MistHeight,h));
    return half4(c,1);
   }
   ENDHLSL
  }
 }
}
