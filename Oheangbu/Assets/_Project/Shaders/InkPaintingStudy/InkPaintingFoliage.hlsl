#ifndef OH_INK_PAINTING_FOLIAGE
#define OH_INK_PAINTING_FOLIAGE

// Optional forward-colour treatment for leaf meshes and their distant cards.
// Call on lit RGB after the existing RGB-only mip filtering, in place of
// CIPainterlyFoliageColour, then apply the caller's chroma/atmosphere once.
// This file deliberately has no textures, uniforms, fog, alpha, wind or LOD code.

float IPFoliageInkTone(float luminance)
{
    float y=max(0,luminance);
    // Soft loaded ink, middle wash and lit wash. The transition widths keep the
    // transfer monotone and reduce local contrast rather than sharpening leaves.
    // Fade the first band from zero so true canopy occlusion stays black.
    float bands=.020*smoothstep(0,.045,y);
    bands+=.040*smoothstep(.030,.150,y);
    bands+=.080*smoothstep(.180,.420,y);
    // Retain a little continuous shading within each band: dark species and
    // shadowed clusters must not collapse to a single flat silhouette.
    return lerp(bands,min(y,.60),.12);
}

float3 IPFoliageInkColour(float3 litColour,float amount)
{
    float weight=saturate(amount);
    // Exact legacy passthrough when the optional material control is disabled.
    if(weight<=0)return litColour;
    float3 source=max(0,litColour);
    float luminance=dot(source,float3(.2126,.7152,.0722));
    float tone=IPFoliageInkTone(luminance);
    // Preserve 20% of the source's relative hue at the new ink luminance. This
    // avoids a common green overlay while retaining individual species colours.
    float3 relativeChroma=(source-luminance.xxx)/max(.0001,luminance);
    float3 painted=tone*(1+relativeChroma*.20);
    return lerp(litColour,painted,weight);
}

// Optional narrower canopy masses. Endpoints are bounded by the existing
// bands, so this mode cannot introduce pale leaf highlights or fill alpha holes.
float IPFoliageCanopyTone(float luminance,float4 tones)
{
    float y=max(0,luminance);
    float loaded=clamp(tones.x,0,.020);
    float middle=clamp(tones.y,loaded,.060);
    float pale=clamp(tones.z,middle,.140);
    float bands=loaded*smoothstep(0,.045,y);
    bands+=(middle-loaded)*smoothstep(.030,.150,y);
    bands+=(pale-middle)*smoothstep(.180,.420,y);
    return lerp(bands,min(y,.60),clamp(tones.w,0,.12));
}

float3 IPFoliageInkColour(float3 litColour,float amount,float canopyMode,float4 canopyTones)
{
    float3 result=litColour;
    [branch] if(canopyMode>.5)
    {
        float weight=saturate(amount);
        [branch] if(weight>0)
        {
            float3 source=max(0,litColour);
            float luminance=dot(source,float3(.2126,.7152,.0722));
            float tone=IPFoliageCanopyTone(luminance,canopyTones);
            float3 relativeChroma=(source-luminance.xxx)/max(.0001,luminance);
            float3 painted=tone*(1+relativeChroma*.20);
            result=lerp(litColour,painted,weight);
        }
    }
    else result=IPFoliageInkColour(litColour,amount);
    return result;
}


// Optional forward-only canopy suppression. All inputs reuse existing samples;
// this code has no alpha, discard, fog, sampler, wind or geometry operations.
float IPCanopyQuietWeight(float d,float2 range)
{
    return smoothstep(max(0,range.x),max(max(0,range.x)+.001,range.y),d);
}
float3 IPCanopyQuietAlbedo(float3 source,float3 materialTint,float weight,float4 settings)
{
    float3 result=source;
    [branch] if(weight>0)
    {
        float3 positive=max(0,source);
        float y=dot(positive,float3(.2126,.7152,.0722));
        // The per-material mean is in linear texture space, before material tint.
        // Preserve relative hue here; existing ink conversion keeps its20% later.
        float centre=max(.001,settings.z)*dot(max(0,materialTint),float3(.2126,.7152,.0722));
        float target=lerp(y,centre,weight*saturate(settings.x));
        float3 relativeChroma=(positive-y.xxx)/max(.0001,y);
        result=target*(1+relativeChroma);
    }
    return result;
}
float3 IPCanopyQuietNormal(float3 mapped,float3 geometric,float weight,float retained)
{
    float3 result=mapped;
    [branch] if(weight>0)
    {
        float3 blended=lerp(mapped,geometric,weight*(1-saturate(retained)));
        result=blended*rsqrt(max(.000001,dot(blended,blended)));
    }
    return result;
}

#endif
