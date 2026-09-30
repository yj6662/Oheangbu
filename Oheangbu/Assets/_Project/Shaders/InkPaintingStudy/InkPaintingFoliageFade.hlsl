#ifndef OH_INK_PAINTING_FOLIAGE_FADE
#define OH_INK_PAINTING_FOLIAGE_FADE
// Only the current render target's integer pixel is a seed. Both LODs use this
// same helper: no time, object, instance, world-position or LOD input is allowed.
float IPFoliageFadeNoise(float2 positionCS,float stochastic)
{
    float2 pixel=floor(positionCS);
    float noise=0;
    [branch] if(stochastic>.5)
    {
        uint2 p=(uint2)pixel;
        uint h=p.x*0x9E3779B9u+p.y*0x85EBCA6Bu+0xC2B2AE35u;
        h^=h>>16;
        h*=0x7FEB352Du;
        h^=h>>15;
        h*=0x846CA68Bu;
        h^=h>>16;
        // The top24 bits are exactly representable as float; never return1.
        noise=(float)(h>>8)*(1.0/16777216.0);
    }
    else
    {
        // Exact legacy IGN when the optional material control is off.
        noise=frac(52.9829189*frac(dot(pixel,float2(.06711056,.00583715))));
    }
    return noise;
}
#endif
