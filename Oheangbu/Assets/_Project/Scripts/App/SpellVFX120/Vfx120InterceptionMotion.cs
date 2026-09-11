using System;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Supplied local-space shot plans and confirmed presentation hits. No target
    // search, collision, freeze, damage, or alteration of enemy projectiles.
    public static class Vfx120InterceptionMotion
    {
        public const int MaxTargets = 6;
        [Serializable] public struct Target
        {
            public Vector3 Point;
            public float LaunchAt, ArrivalAt, GroundY;
            public bool HitConfirmed;
            public float HitAt;
        }
        public static bool IsPrepared(Vfx120Profile p) => p != null && p.Glyph == "송"
            && p.BodyMesh != null && p.BodyMesh.name == "VFX120_FrostNeedle_078"
            && p.AccentMesh != null && p.AccentMesh.name == Vfx120FrostCordMotion.CordName;
        public static bool Valid(in Target t, float life) => Finite(t.Point) && Finite(t.GroundY)
            && Finite(t.LaunchAt) && Finite(t.ArrivalAt) && t.LaunchAt >= 0
            && t.ArrivalAt > t.LaunchAt && t.ArrivalAt < life;
        public static bool Confirmed(in Target t, float life) => Valid(t, life) && t.HitConfirmed
            && Finite(t.HitAt) && t.HitAt >= t.LaunchAt && t.HitAt < life;
        public static int FirstConfirmed(Target[] targets, float life)
        {
            int chosen = -1; float first = float.PositiveInfinity;
            for (int i = 0; targets != null && i < Mathf.Min(MaxTargets, targets.Length); i++)
                if (Confirmed(targets[i], life) && targets[i].HitAt < first) { chosen = i; first = targets[i].HitAt; }
            return chosen;
        }
        public static Vfx120CueMotion.Pose Sample(in Vfx120CueMotion.Context c,
            Vfx120CueMotion.PartRole role, int index)
        {
            int slot = role == Vfx120CueMotion.PartRole.Body ? index : index / 3;
            var hidden = new Vfx120CueMotion.Pose { Position = c.Origin, Rotation = Quaternion.identity, Scale = Vector3.zero };
            if (slot < 0 || slot >= MaxTargets || c.Interceptions == null || slot >= c.Interceptions.Length) return hidden;
            var t = c.Interceptions[slot];
            if (!Valid(t, c.Duration) || c.Age < t.LaunchAt) return hidden;
            var single = c;
            single.Origin = c.Origin + new Vector3((slot - 2.5f) * .022f, .04f, .06f);
            single.Target = t.Point; single.GroundY = t.GroundY;
            single.Age = c.Age - t.LaunchAt; single.Duration = c.Duration - t.LaunchAt;
            single.ImpactTime = t.ArrivalAt - t.LaunchAt;
            single.HitAt = Confirmed(t, c.Duration) ? t.HitAt - t.LaunchAt : -1;
            return Vfx120FrostCordMotion.Sample(single, role, role == Vfx120CueMotion.PartRole.Body ? 0 : index % 3);
        }
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }
}
