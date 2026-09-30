#ifndef OH_INK_CHROMA_268
#define OH_INK_CHROMA_268
// Opt-in, per-material colour response. No texture or full-screen pass.
float3 InkChroma268(float3 colour,float3 world)
{
    float inside=step(_InkChromaRect268.x,world.x)*step(world.x,_InkChromaRect268.z)
                *step(_InkChromaRect268.y,world.z)*step(world.z,_InkChromaRect268.w);
    float retention=lerp(1,saturate(_InkChromaRetain268),inside);
    float luminance=dot(colour,float3(.2126,.7152,.0722));
    return lerp(luminance.xxx,colour,retention);
}
#endif
