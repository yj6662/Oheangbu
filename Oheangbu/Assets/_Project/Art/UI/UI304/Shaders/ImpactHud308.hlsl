// SPEC-SPELL-DEPLOY-308 section 9 (D308-10b) [TEST]: the HUD reacts to an impact frame as if its value "light" fell on it.
// Included by UI/InkMeter and UI/InkReveal (CGPROGRAM, built-in UI structure) - plain HLSL only, no URP include.
// Globals written by ImpactFrameDirector308 for the same 1-3 frames as the screen pass:
//   _OhImpact308    = (impact point screen u, v (viewport, v up), strength 0..1, frame number 1..3; 0 = no impact)
//   _OhImpactHud308 = (rim strength, fill strength (<= .35), shadow px (mesh path only), value swap 0/1)
//   _OhImpactHudPoint308.xy = the same impact point in the PIXEL frame of the overlay target (rows from the top on D3D /
//                     Vulkan / Metal). SV_POSITION and the alpha gradient are in that frame too, so nothing is flipped here and
//                     no projection flag is read (during the overlay UI pass _ProjectionParams is left over from the last camera).
// All zero = the input colour is returned untouched (the normal frame costs one uniform compare).
// The rim facing the impact point takes the paper value (<= .9 display), the far rim an ink shadow, the inside is tinted by at
// most 35 %. During the inversion frame (value swap) ink and paper values trade places so the element keeps its contrast on
// the black screen. Only rgb changes: alpha (= the fill shape and the fill amount) is returned as it came in.
// No light is added: every output channel stays at or below .9 in display space.
// _OhHudReactOff is a plain per-material uniform (default 0): a material can opt out with material.SetFloat without a new
// Properties entry.
#ifndef OH_IMPACT_HUD_308
#define OH_IMPACT_HUD_308

float4 _OhImpact308;
float4 _OhImpactHud308;
float4 _OhImpactHudPoint308;
float _OhHudReactOff;

// SV_POSITION in a fragment shader -> uv in the pixel frame of the target this UI is drawn to
float2 OhHudScreenUv(float4 svPosition)
{
    return svPosition.xy / max(_ScreenParams.xy, float2(1.0, 1.0));
}

float3 OhHudToDisplay(float3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    return pow(max(c, 0.0), 1.0 / 2.2);
#endif
}

float3 OhHudFromDisplay(float3 c)
{
    c = min(max(c, 0.0), 0.9);
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    return pow(c, 2.2);
#endif
}

// color = the element's colour before the #304 alpha remap, shape = its coverage (alpha) at this pixel
float4 OhImpactHud308(float4 color, float shape, float4 svPosition)
{
    // derivatives are taken before any branch
    float2 grad = float2(ddx(shape), ddy(shape));
    float rim = _OhImpactHud308.x;
    float fill = min(_OhImpactHud308.y, 0.35);
    float swap = _OhImpactHud308.w;
    if (rim + fill + swap <= 0.0001 || _OhImpact308.z <= 0.0001 || _OhHudReactOff > 0.5) return color;

    float2 uv = OhHudScreenUv(svPosition);
    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
    float2 toImpact = (_OhImpactHudPoint308.xy - uv) * float2(aspect, 1.0);
    float distance01 = length(toImpact);
    toImpact = distance01 > 0.0001 ? toImpact / distance01 : float2(0.0, 1.0);

    // the alpha gradient points into the shape, so its negative is the outward edge normal (same pixel frame as toImpact)
    float steep = length(grad);
    float edge = saturate(steep * 3.0);
    float facing = steep > 0.0001 ? dot(-grad / steep, toImpact) : 0.0;

    float strength = saturate(_OhImpact308.z);
    float3 c = OhHudToDisplay(color.rgb);
    float luma = dot(c, float3(0.2126, 0.7152, 0.0722));
    // inversion frame: ink and paper values trade places, the hue is kept
    float3 swapped = c * ((0.95 - luma) / max(luma, 0.04));
    c = lerp(c, min(swapped, 0.9), saturate(swap));

    float3 paper = float3(0.9, 0.88, 0.83);
    float3 shadow = float3(0.06, 0.055, 0.05);
    // (not named lit: that is an HLSL intrinsic)
    float litAmount = saturate(facing) * edge * rim * strength;
    float shadeAmount = saturate(-facing) * edge * rim * strength;
    c = lerp(c, paper, litAmount);
    c = lerp(c, shadow, shadeAmount);
    // the inside takes a little of the value, more on the side nearer to the impact point
    c = lerp(c, paper, fill * strength * saturate(1.0 - distance01 * 0.8) * (1.0 - edge));

    return float4(OhHudFromDisplay(c), color.a);
}

#endif
