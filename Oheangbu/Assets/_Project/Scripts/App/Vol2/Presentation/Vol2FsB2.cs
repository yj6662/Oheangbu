using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    // D308-150 V12 free showcase batch 2 (2026-10-26, the user: "쇼케이스 방향이 낫다. 이 방식으로 다른 술식도 만들어 가자."): A0 거 너 머 서 어 · R0 구 누 무 수 우.
    // The two beat skeletons, read only from the executor's facts on the scope clock (like batch 1's Vol2FsShot / Vol2FsSweep):
    //   - Vol2FsAttach (ㅓ 한 번): the guard dresses the brush shaft / sleeve for the parry window (Log.WindowStartUs .. WindowEndUs); a successful parry (the combat
    //     wiring's verdict, relayed by the scope as Vol2VfxModel.Parried) gives the reaction at the impact point; nothing is decided here.
    //   - Vol2FsRing (ㅜ 한 번): one instant strike on the hollow band round the cast point (Spec.RingRadius / RingInner / RingOuter); the bodies the executor touched
    //     (Contact) get their struck state. The ring's eruption runs outward from the caster's feet in a few frames (the contact already happened: presentation only).
    // Presentation only: nothing here is read by a rule, nothing decides or delays anything.

    /// <summary>The ㅓ guard skeleton of 거 / 너 / 머 / 서 / 어.</summary>
    public abstract class Vol2FsAttach : Vol2FsRun
    {
        protected float WinStart, WinEnd, ParryAt = -1f, EndRel = -1f;
        protected Vector3 ParryPoint, Away;
        protected Transform Attacker; protected Bounds AttackerB; protected bool HasAttacker;
        protected int Seed;
        protected bool IsParried => ParryAt >= 0f;
        /// <summary>How long the parry reaction plays (the look's own time; the cast may end before).</summary>
        protected virtual float ReactionSeconds => 1.2f;
        protected virtual float Linger => 3f;
        protected virtual float MinLinger => 0.5f;

        public override void Began(long nowUs)
        {
            Seed = (int)(Inst.Cast.Value & 0x7fff);
            var log = Inst.Log;
            WinStart = log.ParryWindowOpened ? Inst.Rel(log.WindowStartUs) : 0f;
            WinEnd = log.ParryWindowOpened ? Inst.Rel(log.WindowEndUs) : 0.5f;
            Away = Fwd;
            OnBegin();
        }

        protected abstract void OnBegin();
        /// <summary>Every frame: <paramref name="open"/> 0..1 = the guard standing (rises at the window's start, falls after its end unless parried).</summary>
        protected abstract void OnGuard(float t, float open);
        /// <summary>The parry landed (once): <see cref="ParryPoint"/>, <see cref="Away"/> (flat, caster -> impact), the striking body if one stands near.</summary>
        protected abstract void OnParry();
        /// <summary>After the parry: h = seconds since it.</summary>
        protected abstract void OnReaction(float t, float h);
        protected virtual void OnCastEnded(float t) { }

        public override bool Parried(Vector3 point, long nowUs)
        {
            if (Root == null || ParryAt >= 0f) return false;
            ParryAt = Now(nowUs); ParryPoint = point;
            Vector3 d = point - Inst.Origin; d.y = 0f; Away = d.sqrMagnitude > 1e-4f ? d.normalized : Fwd;
            // the striking body: the nearest living target to the impact (presentation only - the verdict is the rules')
            var c = Inst.Model.Coordinator;
            IVol2Target best = null; float bestD = 3.5f;
            if (c != null)
                foreach (var tg in c.Targets)
                {
                    if (tg == null || !tg.Alive) continue;
                    Vector3 e = tg.Position - point; e.y = 0f;
                    float m = e.magnitude - tg.Radius;
                    if (m < bestD) { bestD = m; best = tg; }
                }
            if (best != null)
            {
                Attacker = TargetTransform(best.Id);
                AttackerB = BodyBounds(Attacker, best.Position, best.Radius);
                HasAttacker = true;
                Watch(Attacker);
            }
            Note = "parried at " + ParryAt.ToString("0.00") + "s" + (HasAttacker ? " by " + best.Id : "");
            OnParry();
            HitStop(ParryAt);      // 2026-10-27: a short hit-stop of the look's particles (Phantom Blade principle 3)
            return true;
        }

        /// <summary>The guard's standing amount at t: up in 0.08 s from the window's start, held to its end (or through the reaction when parried), down in 0.25 s.</summary>
        protected float Open(float t)
        {
            float up = Vol2Fs.Smooth(WinStart, WinStart + 0.08f, t);
            float endAt = IsParried ? Mathf.Max(WinEnd, ParryAt + 0.35f) : WinEnd;
            return up * (1f - Vol2Fs.Smooth(endAt, endAt + 0.25f, t));
        }

        /// <summary>A point on the brush shaft: 0 = the grip (sleeve), 1 = the tip.</summary>
        protected Vector3 Shaft(float u) => Vector3.LerpUnclamped(Grip(), Tip(), u);
        protected Vector3 ShaftAxis { get { var d = Tip() - Grip(); return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up; } }

        /// <summary>The striking body's centre now, else in front of the impact.</summary>
        protected Vector3 AttackerNow() => Attacker != null ? BodyBounds(Attacker, Attacker.position, 0.5f).center : HasAttacker ? AttackerB.center : ParryPoint + Away * 0.6f;

        public override void Tick(long nowUs)
        {
            if (Root == null) return;
            float t = Now(nowUs);
            bool ended = Inst.CastIsEnded || Inst.Log.Finished;
            if (ended && EndRel < 0f) { EndRel = t; OnCastEnded(t); }
            OnGuard(t, Open(t));
            if (IsParried) OnReaction(t, t - ParryAt);
        }

        public override bool Done(long nowUs)
        {
            if (Root == null) return true;
            float t = Now(nowUs);
            if (EndRel < 0f) return false;
            float by = Mathf.Max(EndRel, IsParried ? ParryAt + ReactionSeconds : WinEnd + 0.35f);
            if (t > by + Linger) return true;
            return t > by + MinLinger && !ParticlesAlive();
        }
    }

    /// <summary>The ㅜ ring skeleton of 구 / 누 / 무 / 수 / 우.</summary>
    public abstract class Vol2FsRing : Vol2FsRun
    {
        protected sealed class Struck { public long Id; public Transform T; public Bounds B; public Vector3 Feet; public float Ang, At; public bool Started, Ended; public object Data; }
        protected readonly List<Struck> Hits = new List<Struck>();
        protected Vector3 Apex, F; protected float R, Inner, Outer; protected int Seed;
        protected bool Launched; protected float LaunchAt = -1f, EndRel = -1f;
        /// <summary>The eruption runs from the feet out to the band in this many seconds.</summary>
        protected virtual float ExpandSeconds => 0.1f;
        protected virtual float Linger => 3f;
        protected virtual float MinLinger => 1f;
        protected abstract float LookSeconds { get; }

        /// <summary>The flat direction at <paramref name="ang"/> radians from the ring's forward (positive = to the right).</summary>
        protected Vector3 Dir(float ang) => Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * F;
        protected Vector3 OnBand(float ang, float r) { var p = Apex + Dir(ang) * r; p.y = Ground; return p; }
        /// <summary>0 in front of the caster .. 1 straight behind (toward the shoulder camera): the near arc stays low there so it does not wall the camera in.</summary>
        protected static float Back(float ang) => Vol2Fs.Smooth(0.55f, 0.9f, Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)) / 180f);
        protected virtual float StruckAt(float ang) => ExpandSeconds;

        public override void Began(long nowUs)
        {
            var spec = Inst.Log.Spec;
            Apex = spec.Origin; F = spec.Forward; F.y = 0f; F = F.sqrMagnitude > 1e-6f ? F.normalized : Fwd;
            R = Mathf.Max(0.5f, spec.RingRadius); Inner = spec.RingInner; Outer = spec.RingOuter;
            Seed = (int)(Inst.Cast.Value & 0x7fff);
            OnBegin();
        }

        protected abstract void OnBegin();
        protected virtual void OnCharge(float t, float k) { }
        protected abstract void OnLaunch(long nowUs);
        /// <summary>a = seconds since the launch.</summary>
        protected abstract void OnRing(float t, float a, bool ended);
        protected abstract void OnStruck(Struck s);
        protected virtual void OnCastEnded(float t) { }

        public override void Contact(Vol2ContactRecord rec)
        {
            if (!rec.BudgetAccepted || Hits.Exists(s => s.Id == rec.TargetId)) return;
            var t = Target(rec.TargetId); var tr = TargetTransform(rec.TargetId);
            if (t == null) return;
            var s = new Struck { Id = rec.TargetId, T = tr, Feet = t.Position, B = BodyBounds(tr, t.Position, t.Radius) };
            Vector3 d = t.Position - Apex; d.y = 0f;
            s.Ang = Vector3.SignedAngle(F, d, Vector3.up) * Mathf.Deg2Rad;
            s.At = StruckAt(s.Ang);
            Hits.Add(s);
            Watch(tr);
            OnStruck(s);
        }

        protected Vector3 CentreNow(Struck s) => s.T != null ? BodyBounds(s.T, s.T.position, 0.5f).center : s.B.center;
        protected Vector3 FeetNow(Struck s) => s.T != null ? s.T.position : s.Feet;

        public override void Tick(long nowUs)
        {
            if (Root == null) return;
            float t = Now(nowUs);
            if (!Launched)
            {
                float k = LaunchRel > 0.02f ? Mathf.Clamp01(t / LaunchRel) : 1f;
                OnCharge(t, k);
                if (t >= LaunchRel - 1e-4f) { Launched = true; LaunchAt = t; OnLaunch(nowUs); } else return;
            }
            bool ended = Inst.CastIsEnded || Inst.Log.Finished;
            if (ended && EndRel < 0f) { EndRel = t; OnCastEnded(t); }
            OnRing(t, t - LaunchAt, ended);
        }

        public override bool Done(long nowUs)
        {
            if (Root == null) return true;
            float t = Now(nowUs);
            if (!Launched) return t > LaunchRel + 3f;
            float a = t - LaunchAt;
            if (a < LookSeconds) return false;
            if (EndRel < 0f) return false;
            if (t > EndRel + Linger && a > LookSeconds + 2.5f) return true;
            return t > EndRel + MinLinger && !ParticlesAlive();
        }
    }

    /// <summary>Batch 2 helpers.</summary>
    public static class Vol2FsB2
    {
        /// <summary>A closed ring strip on the ground (a band from <paramref name="r0"/> to <paramref name="r1"/>), its alpha per angle from <paramref name="alpha"/>.</summary>
        public static void GroundRing(Vol2FsStrips strip, Vector3 apex, Vector3 fwd, float ground, float r0, float r1, int n, System.Func<float, float> alpha, float rnd, Vector3 cam, float lift = 0.03f, float u0 = 0f, float u1 = 1f)
        {
            var pts = new List<Vector3>(n + 1); var w = new List<float>(n + 1); var al = new List<float>(n + 1); var side = new List<Vector3>(n + 1);
            for (int k = 0; k <= n; k++)
            {
                float ang = (k / (float)n * 2f - 1f) * Mathf.PI;                // from straight behind, round the front, back to behind
                Vector3 d = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * fwd;
                Vector3 p = apex + d * (r0 + r1) * 0.5f; p.y = ground + lift;
                pts.Add(p); w.Add(r1 - r0); al.Add(alpha != null ? alpha(ang) : 1f); side.Add(d);
            }
            strip.Strip(pts, w, side, Color.white, al, rnd, cam, u0, u1);
        }

        /// <summary>Points round a circle (one per <paramref name="n"/>), angle 0 = <paramref name="fwd"/>, jittered by the seed.</summary>
        public static float Angle(int k, int n, int seed, float jitter = 0.35f) => ((k + 0.5f) / n + (Vol2Fs.Hash(seed, k + 7000) - 0.5f) * jitter / n) * Mathf.PI * 2f - Mathf.PI;

        /// <summary>The bone of an animator nearest a world point (for a vine / flame to grip the striking paw), else null.</summary>
        public static Transform NearestBone(Transform root, Vector3 p, float maxDist = 1.4f)
        {
            if (root == null) return null;
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null || smr.bones == null) return null;
            Transform best = null; float bd = maxDist;
            foreach (var b in smr.bones) { if (b == null) continue; float d = Vector3.Distance(b.position, p); if (d < bd) { bd = d; best = b; } }
            return best;
        }
    }
}
