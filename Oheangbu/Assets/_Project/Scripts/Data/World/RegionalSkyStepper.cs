using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>#308 (SPEC-REGION-SKY-308 1b): the regional sky's time behaviour, pure and allocation-free (WorldLookDriver owns one
    /// instance; the editor checks drive another one to test AC-S8/AC-S10 without Play).
    ///  * exponential response toward the evaluated target (ResponseSeconds), first step = target;
    ///  * snap: a one-step move above profile.SnapDistance (> 0) jumps to the target (teleport, respawn, load);
    ///  * interior hold: while <c>interior</c> is true the state (and so the ambient) is frozen at the value it had on entry; a
    ///    first step or a snap that lands inside takes the evaluated value there once, then holds;
    ///  * capital anchor bearing from the eye toward the capital realm's area centroid (radians, InkCloudSky convention).
    /// No static state.</summary>
    public sealed class RegionalSkyStepper
    {
        RegionalInkSkyProfile.SkyState state;
        bool hasState, hasEye, holding, hasCentroid;
        Vector3 lastEye;
        float[] weights;
        Vector2 centroid;
        WorldMacroSheetSO centroidSheet;
        RealmId centroidRealm;

        public RegionalInkSkyProfile.SkyState State => state;
        public RegionalInkSkyProfile.SkyState Target { get; private set; }
        public bool HasState => hasState;
        public bool Holding => holding;
        /// <summary>True when the last Step jumped straight to its target (first step or snap).</summary>
        public bool Snapped { get; private set; }
        public float CapitalAzimuth { get; private set; }
        public bool HasCapitalBearing => hasCentroid;
        public Vector2 CapitalCentroid => centroid;
        /// <summary>Normalized region weights of the last evaluation (RegionSpec order, after override accents).</summary>
        public float[] Weights => weights;

        public void Reset()
        {
            hasState = false; hasEye = false; holding = false; Snapped = false;
        }

        /// <summary>Advances the state. Returns false when the state did not change (interior hold).</summary>
        public bool Step(RegionalInkSkyProfile profile, WorldMacroSheetSO sheet, Vector3 eye, float dt, bool interior, bool forceSnap)
        {
            Snapped = false;
            if (profile == null) return false;
            float moved = hasEye ? Vector3.Distance(eye, lastEye) : float.PositiveInfinity;
            lastEye = eye; hasEye = true;
            bool snap = forceSnap || !hasState || profile.SnapDistance > 0f && moved > profile.SnapDistance;
            if (interior)
            {
                holding = true;
                if (!snap) return false;
                // a start or a teleport inside takes the value of that spot once, then holds it
                Target = Evaluate(profile, sheet, eye);
                state = Target; hasState = true; Snapped = true;
                UpdateBearing(profile, sheet, eye);
                return true;
            }
            holding = false;
            Target = Evaluate(profile, sheet, eye);
            if (snap) { state = Target; Snapped = true; }
            else
            {
                float weight = 1f - Mathf.Exp(-Mathf.Max(0, dt) / Mathf.Max(.01f, profile.ResponseSeconds));
                state = RegionalInkSkyProfile.Blend(state, Target, weight);
            }
            hasState = true;
            UpdateBearing(profile, sheet, eye);
            return true;
        }

        /// <summary>Evaluate at a point without touching the live state (editor preview, captures).</summary>
        public RegionalInkSkyProfile.SkyState Evaluate(RegionalInkSkyProfile profile, WorldMacroSheetSO sheet, Vector3 position)
        {
            int count = sheet != null && sheet.Regions != null ? sheet.Regions.Length : 0;
            if (weights == null || weights.Length < count) weights = new float[Mathf.Max(8, count)];   // once per sheet size
            return profile.Evaluate(sheet, position, weights);
        }

        void UpdateBearing(RegionalInkSkyProfile profile, WorldMacroSheetSO sheet, Vector3 eye)
        {
            if (centroidSheet != sheet || centroidRealm != profile.CapitalRealm)
            {
                centroidSheet = sheet; centroidRealm = profile.CapitalRealm;
                hasCentroid = RegionalInkSkyProfile.Centroid(sheet, profile.CapitalRealm, out centroid);
            }
            if (hasCentroid) CapitalAzimuth = RegionalInkSkyProfile.AzimuthTo(eye, centroid);
        }

        /// <summary>Drops the cached centroid (call after the geography asset changed in the editor).</summary>
        public void InvalidateGeometry() { centroidSheet = null; }
    }
}
