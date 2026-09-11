using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Stateless presentation for five water verbs. Existing gameplay Path/Volley plans
    // remain owned by the caller. In particular this helper never handles 오 or 옥.
    public static class Vfx120WaterMotion
    {
        public struct Context
        {
            public float Age, Life, Flight, OriginGround, TargetGround;
            // Every point is in the effect root's local frame. Center is the caller's
            // already-computed visual travel center, not a new movement simulation.
            public Vector3 Origin, Target, Center;
            public Vector3 HitPoint, BottlePoint;
            public bool HasHitPoint, HasBottlePoint;
            public float HitAt, ReleaseAt;
            // BOTH flags are required to synthesize missing cues in an isolated review.
            public bool PreviewControlled, DemonstrationCues;

            public static Context Create()
            {
                return new Context { Life = 3, Flight = .5f, OriginGround = -1, TargetGround = -1,
                    Origin = Vector3.zero, Target = Vector3.forward * 4, Center = Vector3.zero,
                    HitAt = -1, ReleaseAt = -1 };
            }
        }

        public static bool Supports(Vfx120Profile p) => p != null &&
            (p.Glyph == "온" || p.Glyph == "옷" || p.Glyph == "옹" || p.Glyph == "엄" || p.Glyph == "우");

        public static int GetAccentCount(Vfx120Profile p)
        {
            if (!Supports(p)) return 0;
            switch (p.Glyph) { case "온": return 12; case "옷": case "옹": return 10; case "엄": return 6; default: return 9; }
        }

        public static Color GetAccentColor(Vfx120Profile p, int index)
        {
            if (p == null) return Color.white;
            // Cleanup ribbons are dark residue, not additional bright water splashes.
            if (p.Glyph == "온" && index >= 4) return p.Ink;
            if (p.Glyph == "엄") return Color.Lerp(p.Pigment, p.Accent, index % 2 == 0 ? .74f : .22f);
            if (p.Glyph == "옷") return index < 5 ? p.Accent : Color.Lerp(p.Pigment, p.Accent, .24f);
            if (p.Glyph == "옹") return Color.Lerp(p.Pigment, p.Accent, index < 6 ? .28f : .72f);
            return (index & 1) == 0 ? p.Accent : p.Pigment;
        }

        public static bool Apply(Vfx120Profile p, in Context c, int index, int count,
            ref Vector3 center, ref Vector3 local, ref Vector3 axis, ref Vector3 scale)
        {
            if (!Supports(p)) return false;
            local = scale = Vector3.zero; axis = Vector3.forward;
            if (!Valid(c) || !Finite(p.Size) || index < 0 || index >= count) { center = Vector3.zero; return true; }
            float fade = Life(c);
            if (p.Glyph == "엄")
            {
                center = Bottle(c);
                axis = Vector3.up; // Ripple XY faces upward: foot and lip of the vessel.
                if (index > 1) return true;
                float cue = HitTime(c);
                float expand = cue < 0 ? 0 : Smooth(c.Age - cue, .38f);
                float release = ReleaseTime(c);
                float drain = release < 0 ? 1 : 1 - Smooth(c.Age - release, .60f);
                float fill = expand * drain;
                float diameter = index == 0 ? .25f : Mathf.Lerp(.28f, .48f, fill);
                center.y += index == 0 ? .012f : .045f + .195f * fill;
                // A visible empty foot/lip remains when the contents drain; disappearance
                // is still controlled by the real lifetime, not by a fabricated refill.
                scale = Size(p.BodyMesh, new Vector3(diameter, diameter, .035f)) * fade;
                return true;
            }
            if (p.Glyph == "우")
            {
                float a = index * Mathf.PI * 2 / Mathf.Max(1, count);
                Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float height = 1.42f * Smooth(c.Age, .34f) * fade;
                center = c.Origin + radial * Mathf.Clamp(p.Size * .62f, .85f, 1.5f);
                center.y = c.OriginGround + height * .5f + .025f;
                axis = radial; // Vertical Ripple XY frames face out around the caster.
                float width = 2 * Mathf.Clamp(p.Size * .62f, .85f, 1.5f) * Mathf.Sin(Mathf.PI / Mathf.Max(3, count)) * 1.06f;
                scale = Size(p.BodyMesh, new Vector3(width, height, .035f));
                scale.x *= fade;
                return true;
            }

            Vector3 forward = Flat(c.Target - c.Origin), right = Vector3.Cross(Vector3.up, forward);
            float ground = Ground(c, forward), rise = Smooth(c.Age - c.Flight, .22f);
            float widthAll = Mathf.Max(.8f, p.Size * 2);
            center = c.Center; axis = forward;
            Vector3 dimensions;
            if (p.Glyph == "온")
            {
                if (index > 0) return true;
                // Give the rolled crest a readable cross-section. Cleanup still rises
                // behind this leading wash rather than becoming a second wall.
                dimensions = new Vector3(widthAll, .55f, .65f);
            }
            else if (p.Glyph == "옷")
            {
                if (index > 1) return true;
                float side = index == 0 ? -1 : 1;
                center += right * side * widthAll * .22f - forward * index * .16f;
                // Overlapping tapered crests read as a divided current, without the
                // disconnected pair of rectangular tiles in the first review.
                dimensions = new Vector3(widthAll * .58f, .44f, .68f);
            }
            else
            {
                if (index > 0) return true;
                // Broad and shallow, with a long dark trough. Height must not turn the
                // lowering verb into a taller copy of the ordinary wave.
                dimensions = new Vector3(widthAll * 1.13f, .32f, 1.12f);
            }
            scale = Size(p.BodyMesh, dimensions) * rise * fade;
            center.y = ground + dimensions.y * rise * fade * .5f + .025f;
            return true;
        }

        public static bool TrySampleAccent(Vfx120Profile p, in Context c, int index,
            int count, out Vfx120CueMotion.Pose pose)
        {
            pose = new Vfx120CueMotion.Pose { Rotation = Quaternion.identity };
            int wanted = GetAccentCount(p);
            if (wanted == 0) return false;
            if (!Valid(c) || !Finite(p.Size) || index < 0 || index >= count || index >= wanted) return true;
            float fade = Life(c);
            Vector3 point, dimensions;
            Quaternion rotation;
            float alpha = fade;
            if (p.Glyph == "엄")
            {
                float cue = HitTime(c);
                if (cue < 0 || c.Age < cue) return true;
                float fill = Smooth(c.Age - cue, .45f);
                float release = ReleaseTime(c);
                float drain = release < 0 ? 1 : 1 - Smooth(c.Age - release, .62f);
                float a = index * Mathf.PI * 2 / wanted;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float contents = fill * drain;
                Vector3 lower = Bottle(c) + radial * .105f + Vector3.up * .014f;
                Vector3 upper = Bottle(c) + radial * Mathf.Lerp(.14f, .23f, contents)
                    + Vector3.up * (.045f + .195f * contents);
                Vector3 stave = upper - lower;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial);
                point = (lower + upper) * .5f;
                // Six broad curved Ribbon staves give the vessel a side silhouette;
                // six tiny horizontal splashes did not survive the distant camera.
                rotation = Quaternion.LookRotation(stave.normalized, Vector3.Cross(stave, tangent).normalized);
                dimensions = new Vector3(Mathf.Lerp(.13f, .24f, contents), .055f, stave.magnitude);
                alpha *= Smooth(c.Age - cue, .12f);
            }
            else if (p.Glyph == "우")
            {
                float a = index * Mathf.PI * 2 / wanted;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float radius = Mathf.Clamp(p.Size * .62f, .85f, 1.5f);
                float height = 1.32f * Smooth(c.Age, .34f) * fade;
                float sway = Mathf.Sin(c.Age * 1.35f + index * .7f) * .03f;
                point = c.Origin + radial * (radius + sway);
                point.y = c.OriginGround + height * .5f + .030f;
                // Author a panel in XZ with Y radial and Z upward. The shared
                // Ribbon's actual Y-wide strip is mapped into this plane below.
                rotation = Quaternion.LookRotation(Vector3.up, radial);
                dimensions = new Vector3(2 * radius * Mathf.Sin(Mathf.PI / wanted) * .84f, .018f, height);
                alpha *= .46f;
            }
            else
            {
                float cue = HitTime(c);
                if (cue < 0 || c.Age < cue) return true;
                Vector3 contact = c.HasHitPoint ? c.HitPoint : c.Target;
                Vector3 forward = Flat(c.Target - c.Origin), right = Vector3.Cross(Vector3.up, forward);
                float t = c.Age - cue;
                if (p.Glyph == "온")
                    Cleanup(p, c, index, t, contact, forward, right, out point, out rotation, out dimensions, out alpha);
                else if (p.Glyph == "옷")
                    Cooling(c, index, t, contact, forward, right, out point, out rotation, out dimensions, out alpha);
                else
                    Lowering(c, index, t, contact, forward, right, out point, out rotation, out dimensions, out alpha);
                alpha *= fade;
            }
            pose.Position = point;
            // All five water profiles use the same 46-vertex Ribbon accent (GUID
            // 7bc417ca647d78d4b8fed5c3cb2d5e4f). Its middle across-vector is
            // (.070737, .997495, 0): width is local Y, not local X. Authoring a
            // broad XZ panel while crushing Y to depth erased its readable middle.
            // Preserve Y width first, then roll it into the authored XZ plane.
            pose.Rotation = rotation * Quaternion.AngleAxis(-90, Vector3.forward);
            pose.Scale = Size(p.AccentMesh, new Vector3(dimensions.y, dimensions.x, dimensions.z));
            pose.Alpha = Mathf.Clamp01(alpha);
            pose.Visible = pose.Alpha > .001f && pose.Scale.sqrMagnitude > .000001f;
            return true;
        }

        private static void Cleanup(Vfx120Profile p, in Context c, int i, float t,
            Vector3 contact, Vector3 forward, Vector3 right, out Vector3 point,
            out Quaternion rotation, out Vector3 dimensions, out float alpha)
        {
            // Keep residue in front of the struck body, where its silhouette is not
            // swallowed by legs. The supplied cleanup point itself remains unchanged.
            Vector3 groundPoint = new Vector3(contact.x, c.TargetGround + .055f, contact.z)
                - forward * .62f + right * .14f;
            float released = CueReleaseFade(c, .38f);
            if (i < 4)
            {
                // Pale strips appear ONLY at the supplied cleanup event site, never
                // as an unconditional alteration of the terrain or its material.
                point = groundPoint + right * ((i - 1.5f) * .30f) - forward * .20f;
                rotation = Face(forward);
                dimensions = new Vector3(.23f, .055f, 1.05f) * Smooth(t, .24f);
                alpha = .88f * (1 - Smooth(t - 1.65f, .40f)) * released;
                return;
            }
            int thread = i - 4;
            float delayed = t - thread * .065f;
            float phase = Mathf.Clamp01(delayed / 1.35f);
            float angle = thread * Mathf.PI * 2 / 8 + phase * Mathf.PI * 1.35f;
            float radius = Mathf.Lerp(.62f + (thread % 3) * .075f, .10f, phase);
            Vector3 outward = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
            float length = Mathf.Lerp(.52f, .24f, phase);
            point = groundPoint + outward * radius
                + Vector3.up * (.18f + Mathf.Sin(phase * Mathf.PI) * .72f);
            point.y = Mathf.Max(c.TargetGround + length * .5f + .055f, point.y);
            rotation = Quaternion.LookRotation((Vector3.up * .70f - outward * .55f).normalized, -forward);
            dimensions = new Vector3(.115f, .075f, length);
            // Hold opacity through the readable part of the lift; Alpha also drives
            // grain erosion in the shared shader, so a sine fade erased thin strands.
            alpha = Smooth(delayed, .14f) * (1 - Smooth(delayed - 1.25f, .38f)) * released;
        }

        private static void Cooling(in Context c, int i, float t, Vector3 contact,
            Vector3 forward, Vector3 right, out Vector3 point, out Quaternion rotation,
            out Vector3 dimensions, out float alpha)
        {
            int seam = i % 5;
            bool droplet = i >= 5;
            float delayed = t - seam * .045f - (droplet ? .22f : 0);
            float phase = Mathf.Clamp01(delayed / 1.55f);
            float lateral = (seam - 2) * .235f;
            float length = droplet ? .24f : .48f + .12f * (1 - Mathf.Abs(seam - 2) * .5f);
            float top = Mathf.Clamp(contact.y + .24f, c.TargetGround + .98f, c.TargetGround + 1.45f);
            // Five broad wet seams lie on the visible front of the upper body, with
            // five shorter drops below. They slide rather than forming a tall cage.
            point = contact + right * lateral - forward * (droplet ? .48f : .40f);
            point.y = Mathf.Max(c.TargetGround + length * .5f + .055f,
                top - (droplet ? .38f : .10f) - phase * (droplet ? .82f : .58f));
            rotation = Quaternion.LookRotation((Vector3.down + right * Mathf.Sin(t * 2.2f + seam) * .045f).normalized, -forward);
            dimensions = new Vector3(droplet ? .105f : .145f, .065f, length);
            alpha = Smooth(delayed, .14f) * (1 - Smooth(delayed - 1.30f, .36f)) * CueReleaseFade(c, .32f);
        }

        private static void Lowering(in Context c, int i, float t, Vector3 contact,
            Vector3 forward, Vector3 right, out Vector3 point, out Quaternion rotation,
            out Vector3 dimensions, out float alpha)
        {
            float delayed = t - i * .035f;
            float strength = Smooth(delayed, .20f);
            float bottom = c.TargetGround + .065f;
            if (i < 6)
            {
                // A front semicircle remains outside the .44m torso and the arms.
                // Long falling bands reach the ground, unlike 옷's short wet seams.
                float a = (.08f + i * .168f) * Mathf.PI;
                Vector3 radial = right * Mathf.Cos(a) - forward * Mathf.Sin(a);
                float top = Mathf.Clamp(contact.y + .38f, bottom + .95f, c.TargetGround + 1.58f);
                float length = (top - bottom) * strength;
                point = contact + right * Mathf.Cos(a) * .63f - forward * Mathf.Sin(a) * .48f;
                point.y = top - length * .5f;
                rotation = Quaternion.LookRotation(Vector3.down, radial);
                dimensions = new Vector3(.105f + .018f * Mathf.Sin(t * 2.8f + i), .070f, length);
            }
            else
            {
                float a = ((i - 6) + .5f) * Mathf.PI / 4;
                Vector3 radial = right * Mathf.Cos(a) - forward * Mathf.Sin(a);
                point = contact + radial * .48f;
                point.y = bottom;
                rotation = Quaternion.LookRotation(-radial, Vector3.up);
                dimensions = new Vector3(.21f, .045f, .72f) * strength;
            }
            alpha = strength * (1 - Smooth(delayed - 1.55f, .42f)) * CueReleaseFade(c, .32f);
        }

        private static float HitTime(in Context c)
        {
            if (Finite(c.HitAt) && c.HitAt >= 0) return c.HitAt;
            return c.PreviewControlled && c.DemonstrationCues ? Mathf.Max(c.Flight + .1f, c.Life * .28f) : -1;
        }
        private static float ReleaseTime(in Context c)
        {
            if (Finite(c.ReleaseAt) && c.ReleaseAt >= 0) return c.ReleaseAt;
            return c.PreviewControlled && c.DemonstrationCues ? c.Life * .76f : -1;
        }
        private static Vector3 Bottle(in Context c)
        {
            if (c.HasBottlePoint) return c.BottlePoint;
            Vector3 forward = Flat(c.Target - c.Origin), right = Vector3.Cross(Vector3.up, forward);
            Vector3 point = c.Origin + right * .66f - forward * .34f;
            point.y = c.OriginGround + .98f;
            return point;
        }
        private static float CueReleaseFade(in Context c, float seconds)
        {
            float release = ReleaseTime(c);
            return release < 0 ? 1 : 1 - Smooth(c.Age - release, seconds);
        }
        private static float Ground(in Context c, Vector3 forward)
        {
            float length = new Vector2(c.Target.x - c.Origin.x, c.Target.z - c.Origin.z).magnitude;
            return Mathf.Lerp(c.OriginGround, c.TargetGround,
                Mathf.Clamp01(Vector3.Dot(c.Center - c.Origin, forward) / Mathf.Max(.01f, length)));
        }
        private static float Life(in Context c) => c.Age >= c.Life ? 0 :
            Smooth(c.Age, .1f) * (1 - Smooth(c.Age - Mathf.Max(0, c.Life - .6f), Mathf.Min(.6f, c.Life)));
        private static float Smooth(float age, float seconds) { float t = Mathf.Clamp01(age / Mathf.Max(.001f, seconds)); return t * t * (3 - 2 * t); }
        private static Vector3 Flat(Vector3 direction) { direction.y = 0; return direction.sqrMagnitude > .000001f ? direction.normalized : Vector3.forward; }
        private static Quaternion Face(Vector3 axis) => Quaternion.LookRotation(axis.normalized, Mathf.Abs(axis.normalized.y) > .98f ? Vector3.forward : Vector3.up);
        private static Vector3 Size(Mesh mesh, Vector3 dimensions)
        {
            Vector3 bounds = mesh != null ? mesh.bounds.size : Vector3.one;
            return new Vector3(Mathf.Max(0, dimensions.x) / Mathf.Max(.001f, bounds.x),
                Mathf.Max(0, dimensions.y) / Mathf.Max(.001f, bounds.y), Mathf.Max(0, dimensions.z) / Mathf.Max(.001f, bounds.z));
        }
        private static bool Valid(in Context c) => Finite(c.Age) && Finite(c.Life) && c.Life > 0 && Finite(c.Flight)
            && Finite(c.OriginGround) && Finite(c.TargetGround) && Finite(c.Origin) && Finite(c.Target) && Finite(c.Center)
            && (!c.HasHitPoint || Finite(c.HitPoint)) && (!c.HasBottlePoint || Finite(c.BottlePoint));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
