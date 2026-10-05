using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 one impact frame as the HUD sees it (D308-10b).</summary>
    public struct ImpactFrame308
    {
        public Vector2 Viewport;    // impact point, viewport 0..1, origin bottom-left (may lie outside the screen)
        public Vector2 PointPixel;  // the same point in the pixel frame of the overlay target (what the HUD shaders compare with
                                    // SV_POSITION; rows from the top on D3D / Vulkan / Metal). (0, 0) while no impact runs
        public float Light;         // 0..1 strength of the value "light" (0 = this impact frame carries none, or the HUD reaction is off)
        public int Cell;            // 0 = no impact frame, 1..3 = the running cell (42 ms each)
        public bool ValueSwap;      // the world is value-inverted in this cell (cell 1 of a medium / high cast)
        public float ShadowPx;      // shadow offset the deploy layer suggests for uGUI images (not used by the vessel shader)
        public bool Active => Cell >= 1;
    }

    /// <summary>#308 THE C# reader of the impact-frame contract (SPEC-HUD-LIQUID-308 §4.1). Reconciled 2026-10-04 with the LIVE
    /// deploy layer (SPEC-SPELL-DEPLOY-308 §8 / §9): ImpactFrameDirector308 - and nothing else - writes three shader globals
    ///   _OhImpact308         = (viewport u, v with v up, strength 0..1, cell number 0..3)
    ///   _OhImpactHud308      = (rim strength, inner strength, shadow px, value swap)   0 = every consumer behaves as usual
    ///   _OhImpactHudPoint308 = the same point in the PIXEL frame of the overlay target (xy; 0 while no impact runs)
    /// and resets them to 0 when an impact ends, on disable and at SubsystemRegistration. This class never writes them.
    /// Who uses what: the DIRECTION of the light is taken in UI/InkVessel308 from _OhImpactHudPoint308 through the shared
    /// include ImpactHud308.hlsl - the same point and the same frame as UI/InkMeter / UI/InkReveal, so one impact lights every
    /// HUD element from the same side. C# only decides WHEN and HOW STRONGLY the cluster reacts (ImpactGate308) and which way
    /// the liquid is kicked (the x of the viewport point: never flipped by any target convention).
    /// When that Spec renames or reshapes the globals, this file and the include line of the shader are the two places to
    /// change. Read = Shader.GetGlobalVector (no allocation). The only statics are the three property ids.</summary>
    public static class ImpactFrameProbe308
    {
        public const string ImpactGlobal = "_OhImpact308";
        public const string HudGlobal = "_OhImpactHud308";
        public const string HudPointGlobal = "_OhImpactHudPoint308";
        static readonly int ImpactId = Shader.PropertyToID(ImpactGlobal);
        static readonly int HudId = Shader.PropertyToID(HudGlobal);
        static readonly int HudPointId = Shader.PropertyToID(HudPointGlobal);

        /// <summary>followHudGlobal: scale the light by _OhImpactHud308.x, so the user's HUD-reaction switch (that global at 0)
        /// and the "reduced" impact setting (half strength) apply here too.</summary>
        public static void Read(bool followHudGlobal, out ImpactFrame308 frame)
        {
            Vector4 impact = Shader.GetGlobalVector(ImpactId);
            Vector4 hud = Shader.GetGlobalVector(HudId);
            frame = default;
            frame.Viewport = new Vector2(impact.x, impact.y);
            Vector4 point = Shader.GetGlobalVector(HudPointId);
            frame.PointPixel = new Vector2(point.x, point.y);
            frame.Cell = impact.w >= .5f ? Mathf.RoundToInt(impact.w) : 0;
            float light = Mathf.Clamp01(impact.z);
            if (followHudGlobal) light *= Mathf.Clamp01(hud.x);
            frame.Light = frame.Cell >= 1 ? light : 0f;
            frame.ShadowPx = hud.z;
            frame.ValueSwap = hud.w >= .5f;
        }
    }

    /// <summary>#308 per-HUD guard over the impact frames (SPEC §4.2): the kick happens once when an impact begins, the light
    /// reaction at most once per MinInterval, and a global left non-zero for longer than StuckSeconds stops the reaction until
    /// it reads 0 again. Plain struct owned by HudVessels308.</summary>
    public struct ImpactGate308
    {
        public bool WasActive, Lighting, Stuck;
        public float ActiveSeconds, SinceLight;

        public void Clear() { this = default; SinceLight = 1e6f; }

        /// <summary>Light strength to show this frame (0 = none). began = an impact frame started on this frame.</summary>
        public float Step(in ImpactFrame308 frame, float realDt, float stuckSeconds, float minInterval, out bool began)
        {
            began = false;
            SinceLight += realDt;
            if (!frame.Active) { WasActive = false; Lighting = false; Stuck = false; ActiveSeconds = 0f; return 0f; }
            if (!WasActive)
            {
                WasActive = true; ActiveSeconds = 0f; began = true;
                Lighting = frame.Light > 0f && SinceLight >= minInterval;
                if (Lighting) SinceLight = 0f;
            }
            else ActiveSeconds += realDt;
            if (ActiveSeconds > stuckSeconds) Stuck = true;
            return Stuck || !Lighting ? 0f : frame.Light;
        }
    }
}
