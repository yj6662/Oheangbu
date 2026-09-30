// Derived Nom-only material clipping. Original KTP texture and color graph is unchanged.
// Absolute world position must use float precision for the kilometre-scale world.
#ifndef OHEANGBU_DEMO_FLAME_CONE_CLIP_INCLUDED
#define OHEANGBU_DEMO_FLAME_CONE_CLIP_INCLUDED

void DemoFlameConeMask_float(float3 PositionWS, float3 Origin, float3 Direction,
    float Range, float HalfAngle, float Height, float Feather, out float Mask)
{
    float directionLengthSquared = dot(Direction.xz, Direction.xz);
    if (directionLengthSquared <= 0.00000001 || Range <= 0.0 ||
        HalfAngle <= 0.0 || HalfAngle >= 90.0 || Height <= 0.0)
    {
        Mask = 0.0;
        clip(-1.0);
        return;
    }
    float2 forward = Direction.xz * rsqrt(directionLengthSquared);
    float3 relative = PositionWS - Origin;
    float along = dot(relative.xz, forward);
    float across = abs(relative.x * forward.y - relative.z * forward.x);
    float angle = radians(HalfAngle);
    // Wedge distance is in metres, not an angular threshold or widened cone.
    float wedgeDistance = along * sin(angle) - across * cos(angle);
    float radialDistance = Range - length(relative.xz);
    float verticalDistance = Height - abs(relative.y);
    float insideDistance = min(radialDistance, min(wedgeDistance, verticalDistance));
    // A zero alpha clip threshold in the vendor graph does not discard alpha == 0.
    // Explicit fragment discard guarantees no contribution outside the combat volume.
    clip(insideDistance);
    Mask = Feather > 0.0 ? smoothstep(0.0, Feather, insideDistance) : 1.0;
}

#endif
