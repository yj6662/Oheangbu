#ifndef GRANITE_SURFACE_285
#define GRANITE_SURFACE_285
struct GraniteSample285 { half3 albedo; half3 normal; half occlusion; half smoothness; };
float RockValue285(float3 p) {
 float3 a=floor(p),f=frac(p);f=f*f*(3-2*f);
 float4 h=frac(sin(float4(dot(a,float3(127.1,311.7,74.7)),dot(a+float3(1,0,0),float3(127.1,311.7,74.7)),dot(a+float3(0,1,0),float3(127.1,311.7,74.7)),dot(a+float3(1,1,0),float3(127.1,311.7,74.7))))*43758.5453);
 float4 j=frac(sin(float4(dot(a+float3(0,0,1),float3(127.1,311.7,74.7)),dot(a+float3(1,0,1),float3(127.1,311.7,74.7)),dot(a+float3(0,1,1),float3(127.1,311.7,74.7)),dot(a+1,float3(127.1,311.7,74.7))))*43758.5453);
 return lerp(lerp(lerp(h.x,h.y,f.x),lerp(h.z,h.w,f.x),f.y),lerp(lerp(j.x,j.y,f.x),lerp(j.z,j.w,f.x),f.y),f.z);
}
GraniteSample285 Granite285(float3 world,half3 normal,float scale,half3 tint,
 TEXTURE2D_PARAM(diffuse,ds),TEXTURE2D_PARAM(bump,bs),TEXTURE2D_PARAM(mask,ms))
{
 GraniteSample285 o;half3 n=normalize(normal);half3 w=pow(abs(n),4);w/=max(dot(w,1),.0001);
 float3 p=world*scale;float3 sg=sign(n);
 float2 ux=float2(p.z*sg.x,p.y),uy=float2(p.x,p.z*sg.y),uz=float2(p.x*sg.z,p.y);
 half3 col=SAMPLE_TEXTURE2D(diffuse,ds,ux).rgb*w.x+SAMPLE_TEXTURE2D(diffuse,ds,uy).rgb*w.y+SAMPLE_TEXTURE2D(diffuse,ds,uz).rgb*w.z;
 half3 ax=UnpackNormal(SAMPLE_TEXTURE2D(bump,bs,ux));
 half3 ay=UnpackNormal(SAMPLE_TEXTURE2D(bump,bs,uy));
 half3 az=UnpackNormal(SAMPLE_TEXTURE2D(bump,bs,uz));
 half3 perturb=float3(0,ax.y,ax.x*sg.x)*w.x+float3(ay.x,0,ay.y*sg.y)*w.y+float3(az.x*sg.z,az.y,0)*w.z;
 half nearDetail=1-smoothstep(12,90,distance(world,_WorldSpaceCameraPos));
 perturb-=n*dot(n,perturb);o.normal=normalize(n+perturb*.58*nearDetail);
 half3 packed=SAMPLE_TEXTURE2D(mask,ms,ux).rgb*w.x+SAMPLE_TEXTURE2D(mask,ms,uy).rgb*w.y+SAMPLE_TEXTURE2D(mask,ms,uz).rgb*w.z;
 half modulation=lerp(.8,1.12,RockValue285(world*float3(.043,.024,.043))+RockValue285(world*.12)*.16);
 // Large samples only modulate value: never stretch a photograph over a slab.
 half localValue=dot(col,half3(.299,.587,.114));
 col=lerp(localValue.xxx,col,.68);
 col=lerp(half3(.19,.192,.188),col,nearDetail*.9+.04);
 half grey=dot(col,half3(.299,.587,.114));o.albedo=lerp(grey.xxx,col,.12)*tint*modulation;
 o.occlusion=lerp(1,lerp(.78,1,packed.r),nearDetail);o.smoothness=(1-packed.g)*.18;
 return o;
}
#endif
