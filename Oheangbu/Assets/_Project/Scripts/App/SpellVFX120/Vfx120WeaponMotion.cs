using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Material attached to the authored weapon, not a second orbit around the caster.
    public static class Vfx120WeaponMotion
    {
        public static int GetAccentCount(Vfx120Profile p) => p != null && p.Behavior == Vfx120Behavior.Weapon ? 8 : 0;

        public static bool TrySample(Vfx120Profile p, float age, float life, Vector3 body,
            Quaternion rotation, Vector3 scale, int index, out Vfx120CueMotion.Pose pose)
        {
            pose = new Vfx120CueMotion.Pose { Rotation = Quaternion.identity };
            if (GetAccentCount(p) == 0) return false;
            if (index < 0 || index >= 8 || p.BodyMesh == null || p.AccentMesh == null) return true;
            float fade = Mathf.SmoothStep(0, 1, age / .18f)
                * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(life - .65f, life, age)));
            float side = index % 2 == 0 ? -1 : 1;
            float along = (index / 2 + .5f) / 4;
            Bounds bounds = p.BodyMesh.bounds;
            Vector3 point = new Vector3(0, .025f, Mathf.Lerp(bounds.min.z + bounds.size.z * .27f, bounds.max.z, along) * scale.z);
            Vector3 dimensions, axis;
            char glyph = p.Glyph[0];
            if (glyph == '것')
            {
                // Paired new leaves open outwards from the wooden blade's spine.
                point.x = side * (.09f + along * .045f);
                point.y += Mathf.Sin(age * 1.8f + index) * .025f;
                axis = new Vector3(side, .12f, .40f);
                dimensions = new Vector3(.18f, .045f, .31f) * (.8f + along * .2f);
            }
            else if (glyph == '넛')
            {
                float burn = Mathf.Repeat(age * 1.1f + index * .17f, 1);
                point.x = side * (.06f + burn * .12f); point.y += burn * .16f;
                axis = new Vector3(side * .3f, .6f, 1);
                dimensions = new Vector3(.12f, .08f, .42f) * (1 - burn * .6f);
            }
            else if (glyph == '멋')
            {
                // Fixed gritty ribs imply weight; no weightless circular orbit.
                point.x = side * .075f; point.y += (index % 3) * .035f;
                axis = new Vector3(side * .15f, .12f, 1);
                dimensions = new Vector3(.16f, .10f, .23f);
            }
            else if (glyph == '섯')
            {
                point.x = side * .04f; point.y += .035f;
                axis = new Vector3(side * .07f, 0, 1);
                dimensions = new Vector3(.035f, .018f, .33f);
            }
            else
            {
                // Four overlapping water cuffs follow the blade instead of a generic halo.
                point.z += Mathf.Sin(age * 2.4f + index) * .045f;
                point.y = 0; axis = Vector3.forward;
                dimensions = new Vector3(.28f, .28f, .025f);
            }
            Vector3 meshSize = p.AccentMesh.bounds.size;
            pose.Position = body + rotation * point;
            pose.Rotation = rotation * Quaternion.LookRotation(axis,
                Mathf.Abs(axis.normalized.y) > .98f ? Vector3.forward : Vector3.up);
            pose.Scale = new Vector3(dimensions.x / Mathf.Max(.001f, meshSize.x),
                dimensions.y / Mathf.Max(.001f, meshSize.y), dimensions.z / Mathf.Max(.001f, meshSize.z)) * fade;
            pose.Alpha = fade * (glyph == '엇' ? .55f : .9f);
            pose.Visible = fade > .001f;
            return true;
        }
    }
}
