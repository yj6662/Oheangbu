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
                axis = Vector3.up; // Ripple XY faces upward: two small vessel rims.
                if (index > 1) return true;
                float cue = HitTime(c);
                float expand = cue < 0 ? 0 : Smooth(c.Age - cue, .38f);
                float release = ReleaseTime(c);
                float drain = release < 0 ? 1 : 1 - Smooth(c.Age - release, .60f);
                float diameter = index == 0 ? .19f : .31f;
                float opening = index == 0 ? 1 : expand * drain;
                center.y += index == 0 ? .012f : .068f * opening;
                scale = Size(p.BodyMesh, new Vector3(diameter, diameter, .025f)) * fade * opening;
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
                // A low narrow leading wash leaves room behind it for cleanup threads.
                dimensions = new Vector3(widthAll, .31f, .60f);
            }
            else if (p.Glyph == "옷")
            {
                if (index > 1) return true;
                float side = index == 0 ? -1 : 1;
                center += right * side * widthAll * .24f - forward * index * .12f;
                dimensions = new Vector3(widthAll * .48f, .19f, .78f);
            }
            else
            {
                if (index > 0) return true;
                // Broad and shallow, with a long dark trough. Height must not turn the
                // lowering verb into a taller copy of the ordinary wave.
                dimensions = new Vector3(widthAll * 1.13f, .20f, 1.30f);
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
                point = Bottle(c) + radial * (.065f + .065f * fill);
                point.y += .020f + .030f * fill * drain;
                rotation = Face(new Vector3(-radial.z, .25f, radial.x));
                dimensions = new Vector3(.030f, .012f, .095f) * fill * drain;
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
                // Ribbon lies XZ with its normal along Y: Z up, Y radial makes a
                // vertical water panel inside each outward-facing Ripple frame.
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
            pose.Rotation = rotation;
            pose.Scale = Size(p.AccentMesh, dimensions);
            pose.Alpha = Mathf.Clamp01(alpha);
            pose.Visible = pose.Alpha > .001f && pose.Scale.sqrMagnitude > .000001f;
            return true;
        }

        private static void Cleanup(Vfx120Profile p, in Context c, int i, float t,
            Vector3 contact, Vector3 forward, Vector3 right, out Vector3 point,
            out Quaternion rotation, out Vector3 dimensions, out float alpha)
        {
            Vector3 groundPoint = new Vector3(contact.x, c.TargetGround + .045f, contact.z);
            if (i < 4)
            {
                // Pale strips appear ONLY at the supplied cleanup event site, never
                // as an unconditional alteration of the terrain or its material.
                point = groundPoint + right * ((i - 1.5f) * .27f) - forward * .32f;
                rotation = Face(forward);
                dimensions = new Vector3(.18f, .014f, .82f) * Smooth(t, .24f);
                alpha = .62f * (1 - Smooth(t - 1.20f, .55f));
                return;
            }
            int thread = i - 4;
            float phase = Mathf.Clamp01((t - thread * .045f) / .72f);
            float angle = thread * Mathf.PI * 2 / 8 + phase * Mathf.PI * 1.75f;
            float radius = (1 - phase) * (.25f + thread * .032f);
            Vector3 outward = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
            point = groundPoint + outward * radius + Vector3.up * (Mathf.Sin(phase * Mathf.PI) * .26f);
            rotation = Face(-outward + Vector3.up * .35f);
            dimensions = new Vector3(.036f, .015f, .23f) * (1 - phase * .75f);
            alpha = Mathf.Sin(phase * Mathf.PI) * .95f;
        }

        private static void Cooling(in Context c, int i, float t, Vector3 contact,
            Vector3 forward, Vector3 right, out Vector3 point, out Quaternion rotation,
            out Vector3 dimensions, out float alpha)
        {
            float phase = Mathf.Clamp01((t - i * .025f) / 1.35f);
            float u = i / 9f;
            // Thin wet seams run down the struck upper body and arms; never tall spikes.
            point = contact + right * ((u - .5f) * .68f) - forward * .18f;
            point.y = Mathf.Max(c.TargetGround + .035f, contact.y + .20f - phase * .76f);
            rotation = Face(Vector3.down + right * (u - .5f) * .2f);
            dimensions = new Vector3(.047f, .015f, .22f + .09f * Mathf.Sin(u * Mathf.PI));
            alpha = Smooth(t - i * .025f, .12f) * (1 - Smooth(phase - .68f, .32f));
        }

        private static void Lowering(in Context c, int i, float t, Vector3 contact,
            Vector3 forward, Vector3 right, out Vector3 point, out Quaternion rotation,
            out Vector3 dimensions, out float alpha)
        {
            float a = i * Mathf.PI * 2 / 10;
            Vector3 radial = right * Mathf.Cos(a) + forward * Mathf.Sin(a);
            float strength = Smooth(t - i * .018f, .18f);
            float bottom = c.TargetGround + .035f;
            float top = Mathf.Max(bottom + .22f, contact.y + .20f);
            float length = (top - bottom) * strength;
            point = contact + radial * (.20f + i % 2 * .055f);
            point.y = top - length * .5f;
            rotation = Face(Vector3.down + radial * .08f);
            dimensions = new Vector3(.029f, .013f, length);
            alpha = strength * (1 - Smooth(t - 1.35f, .42f));
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
        private static Vector3 Bottle(in Context c) => c.HasBottlePoint ? c.BottlePoint
            : new Vector3(c.Origin.x - .28f, c.OriginGround + .92f, c.Origin.z + .22f);
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
