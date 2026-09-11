using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Metre-authored meshes: a small needle and three open angular frost cords.
    // The target/HitAt are supplied presentation data, never a collision result made here.
    public static class Vfx120FrostCordMotion
    {
        public const string CordName = "VFX120_FrostCord_078";
        public static bool IsPrepared(Vfx120Profile p) => p != null && p.Glyph == "상"
            && p.AccentMesh != null && p.AccentMesh.name == CordName;

        public static Vfx120CueMotion.Pose Sample(in Vfx120CueMotion.Context c,
            Vfx120CueMotion.PartRole role, int index)
        {
            Vector3 axis = c.Target - c.Origin;
            axis = axis.sqrMagnitude > .000001f ? axis.normalized : Vector3.forward;
            var facing = Quaternion.LookRotation(axis, Mathf.Abs(axis.y) > .98f ? Vector3.forward : Vector3.up);
            float flight = Mathf.Max(.05f, c.ImpactTime);
            bool hit = c.HitAt >= 0 && c.Age >= c.HitAt;
            float fade = 1 - Smooth(Mathf.Max(0, c.Duration - .35f), c.Duration, c.Age);
            if (role == Vfx120CueMotion.PartRole.Body)
            {
                float alpha = hit ? 1 - Smooth(0, .08f, c.Age - c.HitAt)
                    : 1 - Smooth(flight + .02f, flight + .22f, c.Age);
                alpha *= Smooth(0, Mathf.Min(.04f, flight * .28f), c.Age) * fade;
                Vector3 tip = Vector3.Lerp(c.Origin, c.Target, Mathf.Clamp01(c.Age / flight));
                return Pose(tip - axis * .12f, facing, c.BaseScale, index == 0 ? alpha : 0);
            }
            if (!hit || index >= 3) return Pose(c.Target, facing, Vector3.one, 0);

            float age = c.Age - c.HitAt;
            float close = Smooth(0, .16f, age), split = Smooth(.42f, .82f, age);
            float angle = index * 120f + 17f;
            Vector3 outward = facing * Quaternion.Euler(0, 0, angle) * Vector3.right;
            Vector3 center = c.Target - axis * .015f - Vector3.up * Drop(age, c.Target.y, c.GroundY)
                + outward * (split * (.025f + index * .006f));
            Quaternion rotation = facing * Quaternion.Euler(split * (index - 1) * 15f, 0, angle);
            float size = Mathf.Lerp(1.32f, 1f, close);
            float alphaCord = Smooth(0, .04f, age) * (1 - Smooth(.68f, 1.08f, age)) * fade;
            return Pose(center, rotation, Vector3.one * size, alphaCord);
        }

        // Shared only with the explicitly labelled capture proxy, not a real projectile.
        public static float Drop(float hitAge, float targetY, float groundY)
        {
            float falling = Mathf.Max(0, hitAge - .24f);
            return Mathf.Min(Mathf.Max(0, targetY - groundY - .12f), falling * falling * 2.8f);
        }
        static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, t));
        static Vfx120CueMotion.Pose Pose(Vector3 p, Quaternion r, Vector3 s, float a) =>
            new Vfx120CueMotion.Pose { Position = p, Rotation = r, Scale = s,
                Alpha = Mathf.Clamp01(a), Visible = a > .001f };
    }
}
