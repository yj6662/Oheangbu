#ifndef HIGHLAND_SURFACE_296
#define HIGHLAND_SURFACE_296
#include "Assets/_Project/Art/World/Architecture296/Shaders/Stochastic296.hlsl"
// Highland derivative of GraniteSurface285 (the shared 285 function is untouched):
// - _Chroma keeps part of the photographed colour instead of the 285 near-greyscale (12%)
// - an optional lichen/moss layer settles on upward faces and ledges (world noise breaks it into patches)
// - past the near range the texture fades to its own mip-average tone, not to a flat dark grey
// - optional value normalisation (Step 2 realms): the scan's own mip-average luminance is scaled to a target value
//   and its local contrast can be raised, so a dark or flat CC0 scan reads at the realm's intended value (0 = off)
struct HighlandSample293 { half3 albedo; half3 normal; half occlusion; };
float HighlandValue293(float3 p) {
 float3 a=floor(p),f=frac(p);f=f*f*(3-2*f);
 float4 h=frac(sin(float4(dot(a,float3(127.1,311.7,74.7)),dot(a+float3(1,0,0),float3(127.1,311.7,74.7)),dot(a+float3(0,1,0),float3(127.1,311.7,74.7)),dot(a+float3(1,1,0),float3(127.1,311.7,74.7))))*43758.5453);
 float4 j=frac(sin(float4(dot(a+float3(0,0,1),float3(127.1,311.7,74.7)),dot(a+float3(1,0,1),float3(127.1,311.7,74.7)),dot(a+float3(0,1,1),float3(127.1,311.7,74.7)),dot(a+1,float3(127.1,311.7,74.7))))*43758.5453);
 return lerp(lerp(lerp(h.x,h.y,f.x),lerp(h.z,h.w,f.x),f.y),lerp(lerp(j.x,j.y,f.x),lerp(j.z,j.w,f.x),f.y),f.z);
}
struct Tri293 { float2 ux,uy,uz; half3 w; float3 sg; };
Tri293 Triplanar293(float3 world,half3 n,float scale)
{
 Tri293 t;t.w=pow(abs(n),4);t.w/=max(dot(t.w,1),.0001);float3 p=world*scale;t.sg=sign(n);
 t.ux=float2(p.z*t.sg.x,p.y);t.uy=float2(p.x,p.z*t.sg.y);t.uz=float2(p.x*t.sg.z,p.y);return t;
}
struct TriFrames296 { SurfaceFrame296 x,y,z; };
TriFrames296 HighlandFrames296(Tri293 t)
{
 TriFrames296 f;
 // No quarter-turns on bedded cliff rock: offset variation keeps its direction.
 f.x=SurfaceFrame296At(t.ux,59u,0);
 f.y=SurfaceFrame296At(t.uy,59u,0);
 f.z=SurfaceFrame296At(t.uz,59u,0);
 return f;
}
half3 TriColour296(Tri293 t,TriFrames296 f,TEXTURE2D_PARAM(tex,ss))
{
 float3 mean=SAMPLE_TEXTURE2D_LOD(tex,ss,float2(.5,.5),12).rgb;
 return SurfaceColourMean296(TEXTURE2D_ARGS(tex,ss),f.x,mean)*t.w.x+
  SurfaceColourMean296(TEXTURE2D_ARGS(tex,ss),f.y,mean)*t.w.y+
  SurfaceColourMean296(TEXTURE2D_ARGS(tex,ss),f.z,mean)*t.w.z;
}
half3 TriData296(Tri293 t,TriFrames296 f,TEXTURE2D_PARAM(tex,ss))
{
 return SurfaceData296(TEXTURE2D_ARGS(tex,ss),f.x).rgb*t.w.x+
  SurfaceData296(TEXTURE2D_ARGS(tex,ss),f.y).rgb*t.w.y+
  SurfaceData296(TEXTURE2D_ARGS(tex,ss),f.z).rgb*t.w.z;
}
half3 TriAverage293(Tri293 t,TEXTURE2D_PARAM(tex,ss))
{return SAMPLE_TEXTURE2D_LOD(tex,ss,t.ux,9).rgb*t.w.x+SAMPLE_TEXTURE2D_LOD(tex,ss,t.uy,9).rgb*t.w.y+SAMPLE_TEXTURE2D_LOD(tex,ss,t.uz,9).rgb*t.w.z;}
half3 TriPerturb296(Tri293 t,TriFrames296 f,TEXTURE2D_PARAM(bump,bs))
{
 half3 ax=SurfaceNormal296(TEXTURE2D_ARGS(bump,bs),f.x,1),ay=SurfaceNormal296(TEXTURE2D_ARGS(bump,bs),f.y,1),az=SurfaceNormal296(TEXTURE2D_ARGS(bump,bs),f.z,1);
 return float3(0,ax.y,ax.x*t.sg.x)*t.w.x+float3(ay.x,0,ay.y*t.sg.y)*t.w.y+float3(az.x*t.sg.z,az.y,0)*t.w.z;
}
HighlandSample293 HighlandSurface293(float3 world,half3 normal,float scale,half3 tint,half chroma,float2 nearRange,half2 value,
 TEXTURE2D_PARAM(diffuse,ds),TEXTURE2D_PARAM(bump,bs),TEXTURE2D_PARAM(mask,ms),
 half mossAmount,float mossScale,half2 mossUp,TEXTURE2D_PARAM(moss,mds),TEXTURE2D_PARAM(mossBump,mbs))
{
 HighlandSample293 o;half3 n=normalize(normal);
 Tri293 t=Triplanar293(world,n,scale);
 TriFrames296 frames=HighlandFrames296(t);
 float distanceWS=distance(world,_WorldSpaceCameraPos);
 half nearDetail=1-smoothstep(nearRange.x,nearRange.y,distance(world,_WorldSpaceCameraPos));
 half3 average=TriAverage293(t,TEXTURE2D_ARGS(diffuse,ds));
 half3 sampled=average;
 if(distanceWS<750)sampled=lerp(average,TriColour296(t,frames,TEXTURE2D_ARGS(diffuse,ds)),1-smoothstep(500,750,distanceWS));
 half3 col=lerp(average,sampled,nearDetail*.85+.15);
 if(value.x>0)
 {
  // value.x = target linear luminance of the scan's average, value.y = local contrast about that average
  half mean=max(.004,dot(average,half3(.2126,.7152,.0722)));
  col=max(0,average+(col-average)*value.y)*(value.x/mean);
 }
 half3 perturb=0,packed=1;
 if(nearDetail>0)
 {
  perturb=TriPerturb296(t,frames,TEXTURE2D_ARGS(bump,bs));
  packed=TriData296(t,frames,TEXTURE2D_ARGS(mask,ms));
 }
 if(mossAmount>0)
 {
  // lichen settles where water and dust rest: up-facing faces, broken into world-space patches
  Tri293 m=Triplanar293(world,n,mossScale);
  TriFrames296 mossFrames=HighlandFrames296(m);
  half patch=smoothstep(.38,.62,HighlandValue293(world*.21)+HighlandValue293(world*.83)*.35-.12);
  half cover=smoothstep(mossUp.x,mossUp.y,n.y)*patch*mossAmount;
  half3 mossAverage=TriAverage293(m,TEXTURE2D_ARGS(moss,mds)),mossColour=mossAverage;
  if(distanceWS<750)mossColour=lerp(mossAverage,TriColour296(m,mossFrames,TEXTURE2D_ARGS(moss,mds)),1-smoothstep(500,750,distanceWS));
  col=lerp(col,lerp(mossAverage,mossColour,nearDetail*.85+.15),cover);
  if(nearDetail>0)perturb=lerp(perturb,TriPerturb296(m,mossFrames,TEXTURE2D_ARGS(mossBump,mbs)),cover);
 }
 perturb-=n*dot(n,perturb);o.normal=normalize(n+perturb*.58*nearDetail);
 half modulation=lerp(.82,1.12,HighlandValue293(world*float3(.043,.024,.043))+HighlandValue293(world*.12)*.16);
 half grey=dot(col,half3(.299,.587,.114));o.albedo=lerp(grey.xxx,col,chroma)*tint*modulation;
 o.occlusion=lerp(1,lerp(.78,1,packed.r),nearDetail);
 return o;
}
#endif
