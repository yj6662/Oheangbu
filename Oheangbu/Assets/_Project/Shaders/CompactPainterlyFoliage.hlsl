#ifndef OH_COMPACT_PAINTERLY_FOLIAGE
#define OH_COMPACT_PAINTERLY_FOLIAGE
// Forward-colour helpers only. Coverage, shadow/depth samples, wind and LOD masks
// keep their original texture sample and geometry in the calling shaders.
float CIPainterlyFoliageWeight(float d)
{
    return _CIEnabled>.5&&_CIPainterly>.5?CIProgress(d,_CIFoliagePainterly.xy):0;
}
float3 CIPainterlyFoliageAlbedo(float2 uv,float3 original,float d,float atlasViews)
{
    // Evaluate derivatives before the distance branch so edge quads keep valid
    // gradients; only the additional texture fetch is conditional.
    float2 dxUV=ddx(uv),dyUV=ddy(uv);
    float weight=CIPainterlyFoliageWeight(d);
    float3 result=original;
    UNITY_BRANCH if(weight>0)
    {
        float bias=clamp(_CIFoliagePainterly.z,0,1.5)*weight;
        float3 filtered=original;
        UNITY_BRANCH if(atlasViews>1.5)
        {
            // The owned directional atlases use horizontally adjacent power-of-two
            // tiles. Restrict both mip footprint and UV to this view; never bias alpha.
            float views=max(1,floor(atlasViews+.5));
            float2 size=max(float2(1,1),_BaseMap_TexelSize.zw);
            float2 texel=rcp(size);
            float2 dx=dxUV*size,dy=dyUV*size;
            float baseMip=.5*log2(max(1,max(dot(dx,dx),dot(dy,dy))));
            float2 viewPixels=float2(size.x/views,size.y);
            float maxMip=max(0,floor(log2(max(1,min(viewPixels.x,viewPixels.y))))-1);
            float mip=min(baseMip+bias,maxMip);
            // A continuous conservative border includes the coarser trilinear tap;
            // avoid introducing a colour step whenever the selected mip crosses an integer.
            float2 inset=min(exp2(mip)*texel,float2(.5/views,.5));
            float index=clamp(floor(uv.x*views),0,views-1);
            float2 minimum=float2(index/views,0)+inset;
            float2 maximum=float2((index+1)/views,1)-inset;
            float2 safeUV=clamp(uv,minimum,maximum);
            filtered=SAMPLE_TEXTURE2D_LOD(_BaseMap,sampler_BaseMap,safeUV,mip).rgb;
        }
        // Scaling gradients by exp2(bias) adds exactly that many mip levels while
        // retaining anisotropic filtering for ordinary leaf/branch UVs.
        else filtered=SAMPLE_TEXTURE2D_GRAD(_BaseMap,sampler_BaseMap,uv,dxUV*exp2(bias),dyUV*exp2(bias)).rgb;
        // The blend also makes the atlas-safe sampling policy continuous at 30 m.
        result=lerp(original,filtered*_BaseColor.rgb,weight);
    }
    return result;
}
float3 CIPainterlyFoliageColour(float3 colour,float d)
{
    float strength=CIPainterlyFoliageWeight(d)*clamp(_CIFoliagePainterly.w,0,.3);
    // Smooth luminance contrast compression about middle grey in linear light.
    // Preserve source hue; no quantization, screen filter or new pigment noise.
    float luminance=CILuma(colour);
    float target=lerp(luminance,.18,strength);
    return colour*(1+(target-luminance)/max(.001,luminance));
}
#endif
