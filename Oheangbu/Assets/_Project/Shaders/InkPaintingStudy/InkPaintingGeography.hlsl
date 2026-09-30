#ifndef OH_INK_PAINTING_GEOGRAPHY
#define OH_INK_PAINTING_GEOGRAPHY
TEXTURE2D(_PaintedGeoField);
SAMPLER(sampler_PaintedGeoField);
#include "InkPaintingForm.hlsl"
float4 IPGeography(float3 p)
{
    float2 uv=(p.xz-_PaintedGeoRect.xy)/max(float2(1,1),_PaintedGeoRect.zw);
    float valid=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1)*saturate(_PaintedGeoEnabled);
    return SAMPLE_TEXTURE2D_LOD(_PaintedGeoField,sampler_PaintedGeoField,uv,0)*valid;
}
// This is a relative-relief art-direction mask, not physical altitude fog.
// The bake stores A=relief/256m. It does not encode independent crest identity:
// low isolated summits can share a value with a foot and need visual review.
float IPValleyReserveWeight(float4 geography,float horizontalDistance)
{
    float relief=saturate(geography.a)*256;
    // The lower ramp rejects flat and invalid (A=0) samples. The upper ramp
    // keeps high-relative-relief crests entirely on their existing ink response.
    float foot=CIProgress(relief,_PaintedValleyRelief.xy)
        *(1-CIProgress(relief,_PaintedValleyRelief.zw));
    return foot*CIProgress(horizontalDistance,_PaintedValleyDistance.xy)
        *saturate(_PaintedValleyAppearance.x);
}
float3 IPGeographyInkAtmosphere(float3 colour,float d,float mass,float4 geography,float3 p,IPFormData form)
{
    // The existing atmosphere is evaluated exactly once. Keep these operations
    // unchanged so the default-off material option preserves the current output.
    float denseInk=1-smoothstep(.025,.13,CILuma(colour));
    float retain=lerp(1,lerp(.24,.85,CIProgress(d,float2(1300,3400))),mass*denseInk);
    float3 atmospheric=lerp(colour,CIAtmosphere(colour,d),retain);
    float3 result=atmospheric;
    [branch] if((_PaintedValleyReserve>.5||form.active>0)&&_CIDarkNear>.5&&_PaintedGeoEnabled>.5)
    {
        // Ground and foliage at the same XZ receive the same reserve reach.
        // Canopy Y must not move crowns into a different band from their ground.
        float horizontalDistance=length(p.xz-_WorldSpaceCameraPos.xz);
        float weight=_PaintedValleyReserve>.5?IPValleyReserveWeight(geography,horizontalDistance):0;
        float paperFraction=saturate(_PaintedValleyAppearance.y);
        [branch] if(form.active>0)
        {
            float formWeight=IPFormReserveWeight(form,horizontalDistance);
            float protectLoaded=1-smoothstep(.65,.95,form.load);
            weight=max(weight,formWeight)*lerp(1,protectLoaded,form.active);
            paperFraction=lerp(paperFraction,saturate(_PaintedFormPaper.y),form.active);
        }
        [branch] if(weight>0)
        {
            float3 paper=max(float3(0,0,0),_CIPaper.rgb)*paperFraction;
            // Only brighten; no second atmosphere or change to alpha or geometry.
            result=lerp(atmospheric,max(atmospheric,paper),weight);
        }
    }
    return result;
}
float3 IPGeographyAtmosphere(float3 colour,float d,float3 p)
{
    float4 geography=IPGeography(p);
    IPFormData form=IPPrepareForm(p,geography);
    return IPGeographyInkAtmosphere(colour,d,geography.r,geography,p,form);
}
#endif
