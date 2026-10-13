using Oheangbu.Data.Spell;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    // #308 (user request 2026-10-13): the ink afterimage behind the brush tip, also while just walking.
    // Follows the world brush's tip (WorldMacroPlayerGestureRig.WorldTip) after the rig has placed the brush for the frame, and
    // lets a TrailRenderer draw the ribbon in world space. Presentation only: it reads a transform and writes nothing back, so
    // recognition, power and judgement cannot see it. Ink, not light: an alpha-blended dark ribbon, no emission.
    // Added by the gesture rig when the profile exists in Resources ("Trail308/BrushInkTrail308Profile"); no profile = no trail.
    [DefaultExecutionOrder(1100)]   // after WorldMacroPlayerGestureRig (1000): the brush is where it will be drawn
    public sealed class BrushInkTrail308 : MonoBehaviour
    {
        public const string ResourcePath = "Trail308/BrushInkTrail308Profile";
        public BrushInkTrail308Profile Profile;

        WorldMacroPlayerGestureRig rig;
        Transform anchor;
        TrailRenderer trail;
        Material material;
        Renderer[] brushRenderers;
        Camera view;
        Vector3 previous;
        bool has, near;

        /// <summary>Read by checks: the ribbon is being fed this frame.</summary>
        public bool Emitting => trail != null && trail.emitting;
        public int PointCount => trail != null ? trail.positionCount : 0;

        public static void Ensure(WorldMacroPlayerGestureRig owner)
        {
            if (owner == null || !Application.isPlaying || owner.GetComponent<BrushInkTrail308>() != null) return;
            var profile = Resources.Load<BrushInkTrail308Profile>(ResourcePath);
            if (profile == null || !profile.IsValid) return;
            owner.gameObject.AddComponent<BrushInkTrail308>().Profile = profile;
        }

        void OnEnable() { has = false; if (trail != null) { trail.Clear(); trail.enabled = true; } }
        void OnDisable() { if (trail != null) { trail.Clear(); trail.enabled = false; } has = false; }
        void OnDestroy()
        {
            if (anchor != null) Destroy(anchor.gameObject);
            if (material != null) Destroy(material);
        }

        bool Build()
        {
            if (Profile == null || !Profile.IsValid) return false;
            if (rig == null) rig = GetComponent<WorldMacroPlayerGestureRig>();
            if (rig == null || rig.WorldTip == null || rig.EffectTip == null) return false;
            if (trail != null) return true;
            var holder = new GameObject("BrushInkTrail308");   // not parented: the ribbon lives in world space and takes no scale
            anchor = holder.transform;
            material = new Material(Profile.TrailShader) { name = "BrushInkTrail308 (runtime)" };
            trail = holder.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.lightProbeUsage = LightProbeUsage.Off;
            trail.reflectionProbeUsage = ReflectionProbeUsage.Off;
            trail.numCapVertices = 2;
            trail.numCornerVertices = 2;
            trail.autodestruct = false;
            trail.emitting = false;
            brushRenderers = rig.WorldTip.root != null ? BrushRenderers(rig.WorldTip) : null;
            Apply(1f);
            return true;
        }

        static Renderer[] BrushRenderers(Transform tip)
        {
            // the brush root is the nearest ancestor that carries renderers of its own
            for (Transform t = tip.parent; t != null; t = t.parent)
            {
                var found = t.GetComponentsInChildren<Renderer>(true);
                if (found.Length > 0) return found;
            }
            return null;
        }

        void Apply(float widthScale)
        {
            var p = Profile;
            trail.time = p.Seconds;
            trail.minVertexDistance = p.MinVertexDistance;
            trail.widthMultiplier = p.HeadWidth * widthScale;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.25f, .9f), new Keyframe(1f, p.TailWidthShare));
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.75f, .35f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            material.SetColor("_InkColor", p.InkColor);
            material.SetFloat("_Opacity", p.Opacity);
            material.SetFloat("_StreakDensity", p.StreakDensity);
            material.SetFloat("_DryStrength", p.DryStrength);
            material.SetFloat("_EdgeSoftness", p.EdgeSoftness);
        }

        bool BrushShown()
        {
            if (!rig.isActiveAndEnabled || !rig.WorldTip.gameObject.activeInHierarchy) return false;
            if (brushRenderers == null) return true;
            for (int i = 0; i < brushRenderers.Length; i++)
                if (brushRenderers[i] != null && brushRenderers[i].enabled && brushRenderers[i].gameObject.activeInHierarchy) return true;
            return false;
        }

        void LateUpdate()
        {
            if (!Build()) return;
            // the tip the rig shows this frame: the close-up brush's in the draw mode (first person), the world brush's otherwise.
            // 2026-10-13: following the world brush alone left the ribbon away from the tip the player sees in the close-up.
            Vector3 tip = rig.EffectTip.position;
            bool nearNow = (tip - rig.WorldTip.position).sqrMagnitude > .0004f;
            if (view == null) view = Camera.main;
            // 2026-10-13 (user): in the draw mode the afterimage covered the picture - none there, and what was left is cleared
            bool shown = nearNow ? false
                : BrushShown() && (view == null || (view.transform.position - tip).sqrMagnitude > Profile.HideWithinCamera * Profile.HideWithinCamera);
            float dt = Time.deltaTime;
            if (has && (nearNow != near || (tip - previous).sqrMagnitude > Profile.ClearJump * Profile.ClearJump)) { trail.Clear(); has = false; }   // no streak between the two brushes
            near = nearNow;
            if (nearNow) { if (trail.positionCount > 0) trail.Clear(); has = false; }
            if (has && dt > 0f)
            {
                // faster tip = thinner, drier line (the width is the whole ribbon's: a TrailRenderer has one multiplier)
                float speed = (tip - previous).magnitude / dt;
                float thin = Mathf.Lerp(1f, Profile.ThinShare, Mathf.Clamp01(speed / Profile.ThinAtSpeed));
                trail.widthMultiplier = Mathf.Lerp(trail.widthMultiplier, Profile.HeadWidth * thin, 1f - Mathf.Exp(-10f * dt));
            }
            anchor.position = tip;
            trail.emitting = shown;
            previous = tip; has = true;
        }
    }
}
