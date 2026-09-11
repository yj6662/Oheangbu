using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Presentation-only additions to the shared folk-tiger core. The caller supplies
    // the FIRST body's final grounded pose, after its animation/layout corrections.
    // No spawn, motion, target selection, damage or other creature mechanics occur here.
    public static class Vfx120SummonMotion
    {
        public static int GetAccentCount(Vfx120Profile profile)
        {
            if (profile == null) return 0;
            switch (profile.Glyph)
            {
                case "곰": case "놈": return 12;
                case "솜": case "옴": return 10;
                default: return 0;
            }
        }

        // All output positions, rotations and scales use the effect-root local frame.
        // Existing profile accents are Leaf / Flame / Shard / Ripple respectively.
        // Use the element's readable pigment/material, not opaque black ink for every
        // accent: the flame/water materials are part of the intended distinction.
        public static bool TrySampleAccent(Vfx120Profile profile, float age, float life,
            Vector3 bodyPosition, Quaternion bodyRotation, Vector3 bodyScale,
            int index, int count, out Vfx120CueMotion.Pose pose)
        {
            pose = new Vfx120CueMotion.Pose { Rotation = Quaternion.identity };
            int required = GetAccentCount(profile);
            if (required == 0) return false;
            if (index < 0 || index >= count || index >= required || !Finite(age) || !Finite(life)
                || !Finite(bodyPosition) || !Finite(bodyRotation) || !Finite(bodyScale)
                || bodyScale.x <= 0 || bodyScale.y <= 0 || bodyScale.z <= 0) return true;

            Vector3 meshBounds = profile.BodyMesh != null ? profile.BodyMesh.bounds.size
                : new Vector3(.35965f, .562f, 1);
            Vector3 span = Vector3.Scale(meshBounds, bodyScale);
            float alpha = Appear(age, .18f) * (1 - Appear(age - Mathf.Max(0, life - .65f), .65f));
            if (alpha <= .001f || span.sqrMagnitude < .000001f) return true;

            Vector3 offset, dimensions;
            Quaternion rotation;
            switch (profile.Glyph)
            {
                case "곰": Wood(age, index, span, out offset, out rotation, out dimensions); break;
                case "놈": Fire(age, index, span, out offset, out rotation, out dimensions, out float flameAlpha);
                    alpha *= flameAlpha; break;
                case "솜": Steel(age, index, span, out offset, out rotation, out dimensions);
                    alpha *= Appear(age - index * .025f, .13f); break;
                case "옴": Water(age, index, span, out offset, out rotation, out dimensions, out float waterAlpha);
                    alpha *= waterAlpha; break;
                default: return false;
            }
            pose.Position = bodyPosition + bodyRotation * offset;
            pose.Rotation = bodyRotation * rotation;
            pose.Scale = Dimensions(profile.AccentMesh, dimensions);
            pose.Alpha = Mathf.Clamp01(alpha);
            pose.Visible = pose.Alpha > .001f;
            return true;
        }

        private static void Wood(float age, int index, Vector3 span, out Vector3 offset,
            out Quaternion rotation, out Vector3 dimensions)
        {
            if (index < 8)
            {
                // A leafy horseshoe opens above and beside the face. Wide leaves add
                // negative space to the head outline rather than recolouring the fur.
                float a = Mathf.Lerp(-.18f, Mathf.PI + .18f, index / 7f);
                Vector3 outward = new Vector3(Mathf.Cos(a), Mathf.Sin(a), -.14f);
                offset = new Vector3(outward.x * span.x * .52f,
                    span.y * (.12f + outward.y * .32f), span.z * .27f);
                float sway = Mathf.Sin(age * 1.7f + index * .8f) * .065f;
                rotation = Face(outward + Vector3.forward * sway);
                dimensions = new Vector3(span.z * .105f, span.z * .012f,
                    span.z * (.18f + (index % 3) * .022f));
            }
            else
            {
                // Four narrow overlapping leaf strips become bark/vine ribs. These
                // reuse the authored Leaf mesh; they do not claim a hollow body remesh.
                int strip = index - 8;
                float side = (strip & 1) == 0 ? -1 : 1;
                offset = new Vector3(side * span.x * .48f, span.y * .07f,
                    span.z * (strip < 2 ? -.18f : .06f));
                rotation = Face(new Vector3(side * .12f, .25f, 1));
                dimensions = new Vector3(span.z * .052f, span.z * .009f, span.z * .25f);
            }
        }

        private static void Fire(float age, int index, Vector3 span, out Vector3 offset,
            out Quaternion rotation, out Vector3 dimensions, out float alpha)
        {
            if (index < 8)
            {
                // Individually breathing, upward-open tongues replace a solid mane.
                float a = Mathf.Lerp(-.15f, Mathf.PI + .15f, index / 7f);
                float flicker = .78f + .22f * Mathf.Sin(age * 5.7f + index * 1.9f);
                offset = new Vector3(Mathf.Cos(a) * span.x * .56f,
                    span.y * (.15f + Mathf.Sin(a) * .24f), span.z * .24f);
                Vector3 direction = new Vector3(Mathf.Cos(a) * .42f, .85f + Mathf.Sin(a) * .3f,
                    -.28f + Mathf.Sin(age * 4.1f + index) * .10f);
                rotation = Face(direction);
                dimensions = new Vector3(span.z * .115f, span.z * .065f, span.z * .31f * flicker);
                alpha = .80f + .20f * flicker;
            }
            else
            {
                // Four detached tail/back tongues rise and die instead of forming a
                // rigid second body. Each reset is invisible at both ends of its cycle.
                int trail = index - 8;
                float t = Mathf.Repeat(age * .82f + trail * .25f, 1);
                float side = (trail & 1) == 0 ? -1 : 1;
                offset = new Vector3(side * span.x * (.20f + t * .16f),
                    span.y * (.12f + t * .39f), -span.z * (.16f + trail * .08f + t * .08f));
                rotation = Face(new Vector3(side * .18f, 1, -.4f));
                dimensions = new Vector3(span.z * .075f, span.z * .045f, span.z * .21f) * (1 - t * .4f);
                alpha = Mathf.Sin(t * Mathf.PI);
            }
        }

        private static void Steel(float age, int index, Vector3 span, out Vector3 offset,
            out Quaternion rotation, out Vector3 dimensions)
        {
            // Five paired, shingled rib plates are rigid and nearly still. Their gaps
            // and silhouette distinguish armour from the flame/leaf organic mane.
            int rib = index / 2;
            float side = (index & 1) == 0 ? -1 : 1;
            float chest = Mathf.Sin((rib + .5f) / 5 * Mathf.PI);
            offset = new Vector3(side * span.x * (.44f + chest * .10f),
                span.y * (.07f + chest * .08f), Mathf.Lerp(-.29f, .28f, rib / 4f) * span.z);
            rotation = Face(new Vector3(side * .38f, 1, -.12f));
            dimensions = new Vector3(span.z * .12f, span.z * .028f,
                span.y * (.57f + chest * .11f));
        }

        private static void Water(float age, int index, Vector3 span, out Vector3 offset,
            out Quaternion rotation, out Vector3 dimensions, out float alpha)
        {
            if (index < 6)
            {
                // Open cross-section rings wrap the animal. Ripples lie in XY, so
                // their normals follow the spine; no opaque duplicate belly is added.
                float u = index / 5f;
                float wave = Mathf.Sin(age * 2.3f - u * 4.8f);
                float radius = .85f + .12f * Mathf.Sin(u * Mathf.PI) + wave * .035f;
                offset = new Vector3(0, span.y * .035f * wave, Mathf.Lerp(-.35f, .30f, u) * span.z);
                rotation = Quaternion.Euler(wave * 4, 0, age * 8 + index * 19);
                dimensions = new Vector3(span.x * 1.18f, span.y * radius, span.z * .012f);
                alpha = .72f + .16f * (wave * .5f + .5f);
            }
            else
            {
                // Four low horizontal wakes trail behind the beast's grounded pose.
                // The first body height already includes ground correction; never
                // sample terrain or move the body in this helper.
                int trail = index - 6;
                float t = Mathf.Repeat(age * .5f + trail * .25f, 1);
                float diameter = span.x * (.62f + t * .65f);
                offset = new Vector3(Mathf.Sin(trail * 2.2f) * span.x * .10f,
                    -span.y * .5f + .024f, -span.z * (.51f + trail * .13f));
                rotation = Quaternion.Euler(90, 0, 0);
                dimensions = new Vector3(diameter, diameter, span.z * .008f);
                alpha = Mathf.Sin(t * Mathf.PI) * .72f;
            }
        }

        private static Vector3 Dimensions(Mesh mesh, Vector3 dimensions)
        {
            Vector3 bounds = mesh != null ? mesh.bounds.size : Vector3.one;
            return new Vector3(dimensions.x / Mathf.Max(.001f, bounds.x),
                dimensions.y / Mathf.Max(.001f, bounds.y), dimensions.z / Mathf.Max(.001f, bounds.z));
        }

        private static Quaternion Face(Vector3 axis)
        {
            if (axis.sqrMagnitude < .000001f) return Quaternion.identity;
            axis.Normalize();
            return Quaternion.LookRotation(axis, Mathf.Abs(axis.y) > .98f ? Vector3.forward : Vector3.up);
        }
        private static float Appear(float age, float seconds)
        {
            float t = Mathf.Clamp01(age / Mathf.Max(.001f, seconds));
            return t * t * (3 - 2 * t);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
    }
}
