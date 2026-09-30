#ifndef HIGHLAND_SURFACE_293
#define HIGHLAND_SURFACE_293
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
half3 TriColour293(Tri293 t,TEXTURE2D_PARAM(tex,ss))
{return SAMPLE_TEXTURE2D(tex,ss,t.ux).rgb*t.w.x+SAMPLE_TEXTURE2D(tex,ss,t.uy).rgb*t.w.y+SAMPLE_TEXTURE2D(tex,ss,t.uz).rgb*t.w.z;}
half3 TriAverage293(Tri293 t,TEXTURE2D_PARAM(tex,ss))
{return SAMPLE_TEXTURE2D_LOD(tex,ss,t.ux,9).rgb*t.w.x+SAMPLE_TEXTURE2D_LOD(tex,ss,t.uy,9).rgb*t.w.y+SAMPLE_TEXTURE2D_LOD(tex,ss,t.uz,9).rgb*t.w.z;}
half3 TriPerturb293(Tri293 t,TEXTURE2D_PARAM(bump,bs))
{
 half3 ax=UnpackNormal(SAMPLE_TEXTURE2D(bump,bs,t.ux)),ay=UnpackNormal(SAMPLE_TEXTURE2D(bump,bs,t.uy)),az=UnpackNormal(SAMPLE_TEXTURE2D(bump,bs,t.uz));
 return float3(0,ax.y,ax.x*t.sg.x)*t.w.x+float3(ay.x,0,ay.y*t.sg.y)*t.w.y+float3(az.x*t.sg.z,az.y,0)*t.w.z;
}
HighlandSample293 HighlandSurface293(float3 world,half3 normal,float scale,half3 tint,half chroma,float2 nearRange,half2 value,
 TEXTURE2D_PARAM(diffuse,ds),TEXTURE2D_PARAM(bump,bs),TEXTURE2D_PARAM(mask,ms),
 half mossAmount,float mossScale,half2 mossUp,TEXTURE2D_PARAM(moss,mds),TEXTURE2D_PARAM(mossBump,mbs))
{
 HighlandSample293 o;half3 n=normalize(normal);
 Tri293 t=Triplanar293(world,n,scale);
 half nearDetail=1-smoothstep(nearRange.x,nearRange.y,distance(world,_WorldSpaceCameraPos));
 half3 average=TriAverage293(t,TEXTURE2D_ARGS(diffuse,ds));
 half3 col=lerp(average,TriColour293(t,TEXTURE2D_ARGS(diffuse,ds)),nearDetail*.85+.15);
 if(value.x>0)
 {
  // value.x = target linear luminance of the scan's average, value.y = local contrast about that average
  half mean=max(.004,dot(average,half3(.2126,.7152,.0722)));
  col=max(0,average+(col-average)*value.y)*(value.x/mean);
 }
 half3 perturb=TriPerturb293(t,TEXTURE2D_ARGS(bump,bs));
 half3 packed=TriColour293(t,TEXTURE2D_ARGS(mask,ms));
 if(mossAmount>0)
 {
  // lichen settles where water and dust rest: up-facing faces, broken into world-space patches
  Tri293 m=Triplanar293(world,n,mossScale);
  half patch=smoothstep(.38,.62,HighlandValue293(world*.21)+HighlandValue293(world*.83)*.35-.12);
  half cover=smoothstep(mossUp.x,mossUp.y,n.y)*patch*mossAmount;
  col=lerp(col,lerp(TriAverage293(m,TEXTURE2D_ARGS(moss,mds)),TriColour293(m,TEXTURE2D_ARGS(moss,mds)),nearDetail*.85+.15),cover);
  perturb=lerp(perturb,TriPerturb293(m,TEXTURE2D_ARGS(mossBump,mbs)),cover);
 }
 perturb-=n*dot(n,perturb);o.normal=normalize(n+perturb*.58*nearDetail);
 half modulation=lerp(.82,1.12,HighlandValue293(world*float3(.043,.024,.043))+HighlandValue293(world*.12)*.16);
 half grey=dot(col,half3(.299,.587,.114));o.albedo=lerp(grey.xxx,col,chroma)*tint*modulation;
 o.occlusion=lerp(1,lerp(.78,1,packed.r),nearDetail);
 return o;
}
#endif
