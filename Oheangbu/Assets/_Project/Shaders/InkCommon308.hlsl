// SPEC-SPELL-DEPLOY-308: shared helpers of the spell deploy layer (InkBurst308 / InkResidue308 / ImpactFrame308 / InkFlat308).
// Hash / ValueNoise / Fbm are a COPY of the ones in Oheangbu/InkStroke (that shader is not touched).
// The callers clamp their final colour to the ink LDR ceiling (.85, as VehicleInkShell308 does). The one light term of the
// layer is the strokes' MOMENTARY glow in InkBurst308 (D308-10c, ART-INK middle tier: "the stroke glows at the moment it is
// drawn and sinks into ink"); it has its own, lower ceiling below. Residue, the flat hit redraw, the impact pass and the HUD
// include have no glow term.
#ifndef OH_INK_COMMON_308
#define OH_INK_COMMON_308

// Both ceilings are DISPLAY (sRGB) values. A shader that writes a linear colour clamps with OH_INK_CEILING_OUT, so the
// promise "no ink brighter than .85 on screen" holds in a linear project too (sRGB .85 = linear .692).
#define OH_INK_CEILING 0.85
#define OH_PAPER_CEILING 0.90
#ifdef UNITY_COLORSPACE_GAMMA
    #define OH_INK_CEILING_OUT 0.85
#else
    #define OH_INK_CEILING_OUT 0.692
#endif

// D308-10c momentary glow. OH_GLOW_MAX = the largest amount a material may ask for (SpellDeploy308ProfileSO.MaxGlowAmount).
// OH_GLOW_CEILING_OUT = the value the glow may lift a stroke pixel to at most: linear .45 (display .70,
// SpellDeploy308ProfileSO.GlowOutputCeiling). URP's bloom only adds above half its linear threshold (Bloom.shader prefilter:
// the world profiles' 1.15 -> 1.36 -> .68; the lowest in the project, 1.0 -> .5), so the glow can never bloom.
#define OH_GLOW_MAX 0.35
#ifdef UNITY_COLORSPACE_GAMMA
    #define OH_GLOW_CEILING_OUT 0.70
#else
    #define OH_GLOW_CEILING_OUT 0.45
#endif

float OhHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float OhValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(OhHash(i), OhHash(i + float2(1, 0)), f.x),
        lerp(OhHash(i + float2(0, 1)), OhHash(i + float2(1, 1)), f.x), f.y);
}

float OhFbm(float2 p)
{
    return OhValueNoise(p) * 0.6 + OhValueNoise(p * 2.13 + 17.7) * 0.4;
}

// interleaved gradient noise on the pixel grid (0..1), used for the first-person near fade
float OhDither(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// 1 = fully drawn, 0 = inside the near clip. Dithered in between (cutout shaders have no alpha).
float OhNearVisibility(float eyeDepth, float nearClip, float nearFade)
{
    return saturate((eyeDepth - nearClip) / max(nearFade - nearClip, 0.001));
}

// deploy atlas: 4 x 4 cells, cell 0 = top-left of the image, index = col + row * 4. Returns (x, y, w, h) in uv (v up).
float4 OhCellRect(float index)
{
    float i = clamp(floor(index + 0.5), 0.0, 15.0);
    float row = floor(i * 0.25);
    float col = i - row * 4.0;
    return float4(col * 0.25, 1.0 - (row + 1.0) * 0.25, 0.25, 0.25);
}

float OhLuma(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

#endif
