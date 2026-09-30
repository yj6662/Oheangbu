#ifndef OHEANGBU_STOCHASTIC_296
#define OHEANGBU_STOCHASTIC_296

// Three continuous, world-anchored samples of an existing natural surface.
// Each lattice vertex owns one offset and optional quarter-turn. All channels
// use the same frame; explicit gradients never include the discontinuous hash.
// This is variance-compensated blending, not histogram-preserving synthesis.
struct SurfaceFrame296
{
    float2 uv, dx, dy;
    float2 offset0, offset1, offset2;
    float2 rotation0, rotation1, rotation2;
    float3 weights;
};

uint SurfaceHash296(uint x)
{
    x ^= x >> 16; x *= 0x7feb352du;
    x ^= x >> 15; x *= 0x846ca68bu;
    return x ^ (x >> 16);
}
float2 SurfaceRotate296(float2 p, float2 r)
{
    return float2(p.x*r.x-p.y*r.y,p.x*r.y+p.y*r.x);
}
void SurfaceVertex296(float2 vertex, uint salt, float rotate, out float2 offset, out float2 rotation)
{
    uint2 v=(uint2)(int2)vertex;
    uint h=SurfaceHash296(v.x ^ SurfaceHash296(v.y+salt*0x9e3779b9u));
    uint j=SurfaceHash296(h+0x68bc21ebu);
    offset=float2(h & 65535u,j & 65535u)/65536.0;
    uint turn=(j >> 16) & 3u;
    rotation=turn==0u?float2(1,0):turn==1u?float2(0,1):turn==2u?float2(-1,0):float2(0,-1);
    if(rotate<.5)rotation=float2(1,0); // Preserve the direction of bedded rock.
}
SurfaceFrame296 SurfaceFrameGrad296(float2 uv, float2 dx, float2 dy, uint salt, float rotate)
{
    SurfaceFrame296 f; f.uv=uv; f.dx=dx; f.dy=dy;
    // The original texel/metre scale is unchanged. Patch size is about two repeats.
    float2 grid=float2(uv.x-uv.y*.577350269,uv.y*1.154700538)*.55;
    float2 cell=floor(grid),s=frac(grid),a,b,c;
    if(s.x+s.y<1)
    {
        a=cell;b=cell+float2(1,0);c=cell+float2(0,1);
        f.weights=float3(1-s.x-s.y,s.x,s.y);
    }
    else
    {
        a=cell+1;b=cell+float2(0,1);c=cell+float2(1,0);
        f.weights=float3(s.x+s.y-1,1-s.x,1-s.y);
    }
    // Soft dominance keeps small stones legible without hard Voronoi boundaries.
    f.weights*=f.weights;
    f.weights/=max(dot(f.weights,float3(1,1,1)),1e-6);
    SurfaceVertex296(a,salt,rotate,f.offset0,f.rotation0);
    SurfaceVertex296(b,salt,rotate,f.offset1,f.rotation1);
    SurfaceVertex296(c,salt,rotate,f.offset2,f.rotation2);
    return f;
}
SurfaceFrame296 SurfaceFrame296At(float2 uv, uint salt, float rotate)
{
    return SurfaceFrameGrad296(uv,ddx(uv),ddy(uv),salt,rotate);
}
float4 SurfaceTap296(TEXTURE2D_PARAM(tex,ss), SurfaceFrame296 f, float2 offset, float2 rotation)
{
    return SAMPLE_TEXTURE2D_GRAD(tex,ss,SurfaceRotate296(f.uv,rotation)+offset,
        SurfaceRotate296(f.dx,rotation),SurfaceRotate296(f.dy,rotation));
}
float4 SurfaceData296(TEXTURE2D_PARAM(tex,ss), SurfaceFrame296 f)
{
    return SurfaceTap296(TEXTURE2D_ARGS(tex,ss),f,f.offset0,f.rotation0)*f.weights.x+
        SurfaceTap296(TEXTURE2D_ARGS(tex,ss),f,f.offset1,f.rotation1)*f.weights.y+
        SurfaceTap296(TEXTURE2D_ARGS(tex,ss),f,f.offset2,f.rotation2)*f.weights.z;
}
float3 SurfaceColourMean296(TEXTURE2D_PARAM(tex,ss), SurfaceFrame296 f, float3 mean)
{
    float3 mixed=SurfaceData296(TEXTURE2D_ARGS(tex,ss),f).rgb;
    // Bounded compensation about the scan's own mean avoids the milky look of
    // plain three-way interpolation. No tint, scale, or material identity change.
    float gain=min(1.35,rsqrt(max(dot(f.weights,f.weights),1e-5)));
    return saturate(mean+(mixed-mean)*gain);
}
float3 SurfaceColour296(TEXTURE2D_PARAM(tex,ss), SurfaceFrame296 f)
{
    float3 mean=SAMPLE_TEXTURE2D_LOD(tex,ss,float2(.5,.5),12).rgb;
    return SurfaceColourMean296(TEXTURE2D_ARGS(tex,ss),f,mean);
}
float3 SurfaceNormalTap296(TEXTURE2D_PARAM(tex,ss), SurfaceFrame296 f, float2 offset, float2 rotation, float strength)
{
    float3 n=UnpackNormalScale(SurfaceTap296(TEXTURE2D_ARGS(tex,ss),f,offset,rotation),strength);
    // Inverse rotation maps the sampled tangent vector back to the source plane.
    n.xy=SurfaceRotate296(n.xy,float2(rotation.x,-rotation.y));
    return n;
}
float3 SurfaceNormal296(TEXTURE2D_PARAM(tex,ss), SurfaceFrame296 f, float strength)
{
    float3 n=SurfaceNormalTap296(TEXTURE2D_ARGS(tex,ss),f,f.offset0,f.rotation0,strength)*f.weights.x+
        SurfaceNormalTap296(TEXTURE2D_ARGS(tex,ss),f,f.offset1,f.rotation1,strength)*f.weights.y+
        SurfaceNormalTap296(TEXTURE2D_ARGS(tex,ss),f,f.offset2,f.rotation2,strength)*f.weights.z;
    return normalize(n);
}
#endif
