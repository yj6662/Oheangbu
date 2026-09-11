using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Small metal details use real local-axis dimensions, not the generic family's
    // max-axis normalization. Their pivots (especially the chime suspension point)
    // are authored by Vfx120MetalDetailBuilder and must not be recentered.
    public static class Vfx120MetalDetailMotion
    {
        public const string MeshPrefix = "VFX120_MetalDetail_";

        public static bool IsPrepared(Vfx120Profile p)
        {
            return p != null && (p.Glyph == "손" || p.Glyph == "석" || p.Glyph == "섬" || p.Glyph == "선") &&
                   p.BodyMesh != null && p.BodyMesh.name.StartsWith(MeshPrefix, System.StringComparison.Ordinal);
        }

        public static bool SuppressGenericAccents(Vfx120Profile p) { return IsPrepared(p); }

        // Returns true only for prepared profiles. The caller then skips Variant.Apply.
        // Coordinates, aim and ground heights are effect-root local. No event, stack,
        // prediction, target movement or hit result is inferred here.
        public static bool TryApply(Vfx120Profile p, float age, float flight, float life,
            int index, int count, Vector3 aim, ref Vector3 center, ref Vector3 local,
            ref Vector3 axis, ref Vector3 scale, float size,
            float originGround = -1f, float targetGround = -1f,
            bool hasProjectileAnchor = false, Vector3 projectileAnchor = default,
            Vector3 projectileForward = default, float launchedAt = 0)
        {
            if (!IsPrepared(p)) return false;
            local = Vector3.zero;
            axis = Vector3.forward;
            if (!Finite(age) || !Finite(life) || life <= 0f || age < 0f || age >= life ||
                index < 0 || index >= count || !Finite(aim) || !Finite(originGround))
            {
                center = Vector3.zero;
                scale = Vector3.zero;
                return true;
            }

            float enter = Mathf.SmoothStep(0f, 1f, age / Mathf.Min(.18f, life * .18f));
            float leave = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.82f, 1f, age / life));
            float visible = enter * leave;
            Vector3 dimensions = p.PartScale;

            if (p.Glyph == "손")
            {
                // Three separated open ferrules at the caster's forearm/firing area.
                // A gentle breath conveys retained heat, not fictional extra stacks.
                center = new Vector3(.40f + index * .007f,
                    originGround + 1.06f + index * .012f, .23f + index * .052f);
                axis = new Vector3(.10f, -.10f, 1f).normalized;
                dimensions *= 1f + .018f * Mathf.Sin(age * 3.1f - index * .7f);
            }
            else if (p.Glyph == "석")
            {
                // Three independent top anchors; the small plates swing beneath
                // them without orbiting the actor or crossing the face.
                float offset = index - 1f;
                center = new Vector3(.53f + offset * .19f,
                    originGround + 1.15f - Mathf.Abs(offset) * .025f, .29f);
                float swing = Mathf.Sin(age * 1.7f + index * .9f) * 3.5f * Mathf.Deg2Rad;
                axis = new Vector3(0f, Mathf.Sin(swing), Mathf.Cos(swing));
                dimensions *= index == 1 ? 1f : .92f;
            }
            else if (p.Glyph == "섬")
            {
                // Focus the supplied target only. No trajectory or enemy attribute
                // is inferred from aim; those require authoritative gameplay data.
                Vector3 forward = new Vector3(aim.x, 0f, aim.z);
                forward = forward.sqrMagnitude > .0001f ? forward.normalized : Vector3.forward;
                center = aim + Vector3.up * .09f - forward * .20f;
                axis = forward;
                // Brief opening, then a quiet held pair with an unobstructed center.
                float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.18f, .55f, age));
                dimensions.x *= Mathf.Lerp(1.16f, 1f, settle);
                dimensions.y *= 1f + .012f * Mathf.Sin(age * 2.7f);
            }
            else if (p.Glyph == "선")
            {
                // Without a supplied projectile, retain a small ready pair at the
                // firing area. A supplied anchor is read only: never propel the shot.
                Vector3 forward = hasProjectileAnchor ? projectileForward : aim;
                forward = Finite(forward) && forward.sqrMagnitude > .0001f ? forward.normalized : Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                right = right.sqrMagnitude > .0001f ? right.normalized : Vector3.right;
                float side = index == 0 ? -1 : 1;
                float beatAge = hasProjectileAnchor ? Mathf.Max(0, age - launchedAt) : age;
                float opened = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.08f, .26f, beatAge));
                Vector3 anchor = hasProjectileAnchor ? projectileAnchor - forward * .19f
                    : new Vector3(.40f, originGround + 1.08f, .37f);
                center = anchor + right * (side * Mathf.Lerp(.065f, .030f, opened));
                axis = (forward + right * (side * Mathf.Lerp(.28f, 0f, opened))).normalized;
                dimensions.z *= Mathf.Lerp(.58f, 1f, opened);
            }
            // Axiswise conversion keeps thin closed geometry thin in world metres.
            Vector3 bounds = p.BodyMesh.bounds.size;
            scale = new Vector3(Divide(dimensions.x, bounds.x),
                Divide(dimensions.y, bounds.y), Divide(dimensions.z, bounds.z)) * visible;
            if (!Finite(center) || !Finite(scale)) { center = Vector3.zero; scale = Vector3.zero; }
            return true;
        }

        static float Divide(float value, float extent)
        {
            return Finite(value) && value > 0f && Finite(extent) && extent > .000001f ? value / extent : 0f;
        }
        static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
    }
}
