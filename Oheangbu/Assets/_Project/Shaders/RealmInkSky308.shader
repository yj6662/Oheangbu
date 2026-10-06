// #308 step 2 realm sky (SPEC-REGION-SKY-308 "2 강토 모양 하늘", D308-5; every number TEST). Base = Teaser300/InkWashSky300
// (copied, untouched; so is Shaders/InkCloudSky): ink-wash cloud masses over the realm gradient, colours on the ink..paper
// axis with the realm's hue (담채), nothing emits, output saturated (LDR, ART-INK). Wet edges (pigment pooled just inside the
// wash boundary) and darker cores give the 담묵 -> 농묵 read; clouds thin into the horizon mist so far ridges dissolve into paper.
// Additions over InkWashSky300:
//  * common clock (D208): one _Time.y x _CloudSpeed (x _DriftScale) phase for every realm. The drift is applied in band space
//    (along _BandYaw = the way the wind blows), so blending yaw/stretch between realms never swings the field by the time
//    accumulated so far; the warp field drifts at _Evolve of that speed, so shapes slowly change while they travel.
//    _CloudSpeed 0 (render-ref captures, AC-S14) freezes the sky.
//  * coverage = cloud pixel fraction: the fbm threshold comes from its calibrated mean/sigma (6 octaves .4995/.1336,
//    4 octaves .4997/.1399; logistic quantile), so _Coverage .22 means about 22 % cloud as a LONG-RUN mean (calculation, port).
//    The visible dome spans only a few first-octave cells, so the instantaneous dome fraction swings with the drift phase
//    (port, 5..60 deg, _Scale 1.15: std .12-.17 per realm over time, Play start t=0 is .07-.13 below the seeds); the realms move
//    together (shared phase, corr .81-.98), so their order holds while the absolute level wanders. Storm side:
//    + _StormGain x storm, opposite side - _ClearGain x _StormGain x clear (a calm realm with gain 0 has neither).
//  * InkCloudSky capital atmosphere term (same constants, _CapitalAzimuth radians from +Z toward +X).
//  * _Octaves (6; 4 = Perf307 fallback, AC-S19), horizon mist tinted toward the far fog (_FogTint, _MistTint), zenith fade over
//    the visible band (h .12 -> .9) so _ZenithFade shapes what the player camera actually sees.
// WorldLookDriver writes _Horizon/_Zenith/_Cloud (step-1 state) and, when declared here and the profile has step-2 data,
// _CloudInk _Coverage _Softness _Stretch _BandYaw _StormYaw (degrees, blended as unit vectors) _StormGain _WetEdge _CoreInk
// _MistHeight _ZenithFade _FogTint (rgb = far fog, a = 1). InkSkyProfile.Apply writes _CloudSpeed/_CapitalAzimuth/
// _CapitalAtmosphere (and _CloudDensity/_CloudScale, which this shader does not declare: phase-1 only). The rest are
// material constants, common to every realm.
Shader "Oheangbu/Realm Ink Sky 308"
{
 Properties {
  [Header(Realm state  WorldLookDriver)]
  _Horizon("Horizon (paper side)", Color)=(.92,.88,.76,1)
  _Zenith("Zenith", Color)=(.72,.71,.62,1)
  _Cloud("Cloud light wash", Color)=(.84,.80,.70,1)
  _CloudInk("Cloud ink core", Color)=(.48,.44,.36,1)
  _FogTint("Far fog (rgb), mist tint weight (a, 0 = horizon)", Color)=(.80,.78,.70,0)
  _Coverage("Coverage (cloud fraction)", Range(0,1))=.22
  _Softness("Edge softness", Range(.005,.4))=.12
  _Stretch("Stretch along the bands", Range(.1,8))=3.6
  _BandYaw("Band and wind direction (deg, world yaw it blows toward)", Float)=90
  _StormYaw("Storm side (deg, world yaw)", Float)=195
  _StormGain("Storm coverage gain", Range(0,1))=0
  _WetEdge("Wet edge ink", Range(0,1))=.35
  _CoreInk("Core ink", Range(0,1))=.4
  _MistHeight("Horizon mist height", Range(.01,.5))=.10
  _ZenithFade("Clouds thin toward the zenith", Range(0,1))=.5
  [Header(Shared clock and capital anchor  InkSkyProfile and driver)]
  _CloudSpeed("Cloud speed (common phase, D208)", Float)=.002
  _CapitalAzimuth("Capital azimuth (rad, +Z toward +X)", Float)=.26
  _CapitalAtmosphere("Capital atmosphere", Range(0,.08))=0
  [Header(Material constants  common to every realm)]
  _Scale("Cloud scale", Float)=1.15
  _Warp("Domain warp", Float)=1.1
  _DriftScale("Drift per unit of cloud speed", Float)=1
  _Evolve("Warp drift (share of the cloud drift)", Range(0,1))=.5
  _StormWidth("Storm half width (deg)", Range(1,180))=100
  _StormInk("Storm extra ink", Range(0,1))=.15
  _ClearGain("Clearing opposite the storm (x storm gain)", Range(0,2))=1
  _MistTint("Horizon mist toward the far fog", Range(0,1))=.5
  [IntRange] _Octaves("Octaves (6, Perf307 fallback 4)", Range(1,6))=6
 }
 SubShader {
  Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline"}
  Cull Off ZWrite Off
  Pass {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Horizon,_Zenith,_Cloud,_CloudInk,_FogTint;
   float _Coverage,_Softness,_Stretch,_BandYaw,_StormYaw,_StormGain,_WetEdge,_CoreInk,_MistHeight,_ZenithFade;
   float _CloudSpeed,_CapitalAzimuth,_CapitalAtmosphere;
   float _Scale,_Warp,_DriftScale,_Evolve,_StormWidth,_StormInk,_ClearGain,_MistTint,_Octaves;
   CBUFFER_END
   // fbm statistics (calibrated offline over 400k samples of the warped fbm below; Tools/Unity/Stage308_sky2/preview)
   static const float FbmMean=.5;
   static const float FbmSigma=.134;
   struct A {float4 positionOS:POSITION;}; struct V {float4 positionCS:SV_POSITION;float3 direction:TEXCOORD0;};
   V vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.direction=a.positionOS.xyz;return o;}
   float hash21(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
   float vnoise(float2 p){float2 i=floor(p),f=frac(p);float2 u=f*f*f*(f*(f*6-15)+10);
    float a=hash21(i),b=hash21(i+float2(1,0)),c=hash21(i+float2(0,1)),d=hash21(i+1);return lerp(lerp(a,b,u.x),lerp(c,d,u.x),u.y);}
   // octave count is a material uniform (6 default, 4 = perf fallback); normalised so the mean stays .5 for either count
   float fbm(float2 p,int octaves){
    const float2x2 m=float2x2(1.6,1.2,-1.2,1.6);
    float s=0,a=.5,norm=0;
    [loop] for(int k=0;k<octaves;k++){s+=a*vnoise(p);norm+=a;p=mul(m,p)+17.3;a*=.5;}
    return s/max(norm,1e-3);
   }
   half4 frag(V i):SV_Target{
    float3 d=normalize(i.direction);float h=d.y;float hs=saturate(h);
    // clouds on a flattened dome, banded along _BandYaw (InkWashSky300 mapping)
    float2 q=d.xz/(hs+.22);
    float by=radians(_BandYaw);float2 axis=float2(sin(by),cos(by));float2 perp=float2(axis.y,-axis.x);
    float2 p=float2(dot(q,axis)/max(_Stretch,.1),dot(q,perp))*_Scale;
    // common clock (D208), drift in band space: every realm shares the phase; the field travels toward _BandYaw
    float drift=_Time.y*_CloudSpeed*_DriftScale;
    int octaves=(int)clamp(round(_Octaves),1.0,6.0);
    float2 pw=p-float2(drift*_Evolve,0);
    float2 w=float2(fbm(pw+float2(1.7,9.2),octaves),fbm(pw+float2(8.3,2.8),octaves));
    float n=fbm(p-float2(drift,0)+_Warp*(w-.5),octaves);
    // storm side thickens, the far side clears (angle from the horizontal view direction; tapered toward the zenith where the
    // azimuth is undefined)
    float hl=length(d.xz);float2 hd=d.xz/max(hl,1e-4);
    float sy=radians(_StormYaw);
    float da=degrees(acos(clamp(dot(hd,float2(sin(sy),cos(sy))),-1.0,1.0)));
    float taper=smoothstep(.12,.55,hl);
    float storm=(1-smoothstep(0.0,max(_StormWidth,1.0),da))*taper;
    float clear=smoothstep(90.0,180.0,da)*taper;
    // coverage -> fbm threshold (cloud fraction ~ coverage): logistic approximation of the normal quantile
    float cover=clamp(_Coverage+_StormGain*storm-_ClearGain*_StormGain*clear,.02,.98);
    float thr=FbmMean+FbmSigma*log((1-cover)/cover)/1.702;
    float mist=max(_MistHeight,.01);
    float elev=smoothstep(.015,.015+mist,h)*(1-_ZenithFade*smoothstep(.12,.9,h));
    // edges: softness plus the pixel footprint (fwidth) so sharp realms stay clean near the horizon
    float aa=fwidth(n);
    float soft=max(_Softness,.005)+aa;
    float mask=smoothstep(thr-soft,thr+soft,n)*elev;
    float core=smoothstep(thr,thr+.3,n);
    float e=(n-thr-.5*_Softness)/max(_Softness*1.6+aa,1e-3);
    float edge=exp(-e*e)*elev;
    half3 sky=lerp(_Horizon.rgb,_Zenith.rgb,pow(hs,.6));
    half3 cloud=lerp(_Cloud.rgb,_CloudInk.rgb,saturate(core*_CoreInk+storm*_StormInk*core));
    half3 c=lerp(sky,cloud,mask);
    c=lerp(c,c*(1-.55*_WetEdge),edge*.7);
    // horizon mist: the horizon, pulled toward the far fog the screen passes use (ridges meet the sky)
    half3 mistColour=lerp(_Horizon.rgb,_FogTint.rgb,saturate(_MistTint*_FogTint.a));
    c=lerp(mistColour,c,smoothstep(-.02,mist,h));
    // capital atmosphere (InkCloudSky term: LDR additive, faint, near the horizon toward the capital)
    float direction=max(0,dot(hd,float2(sin(_CapitalAzimuth),cos(_CapitalAzimuth))));
    c+=_CapitalAtmosphere*pow(direction,12)*(1-smoothstep(.03,.4,h))*half3(.35,.22,.05);
    return half4(saturate(c),1);
   }
   ENDHLSL
  }
 }
 Fallback Off
}
