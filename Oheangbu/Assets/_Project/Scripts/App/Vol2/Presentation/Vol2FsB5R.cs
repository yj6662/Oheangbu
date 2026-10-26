using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅜ 심는다 base of batch 5 (R3 굼 눔 뭄 숨 움): the seed is the executor's ONE Ring Mark material made at the start, waiting on the band
    /// (Apex / R / Inner / Outer of the spec). The base keeps the seeds' places round the band (smaller on the short arc behind the caster, where the shoulder
    /// camera looks over it), draws the shared reading of the wait - a burning-down ink arc just inside the band, centred on the front, on the live expiry
    /// (信 holds it), flaring in the last 0.4 s - and finds where the trigger came from: the body whose last step crossed the band (the registry's own
    /// Previous -> Position, the same reading the executor's crossing test makes on that tick), or the caster's place for 뭄 (the hub's caster position). The
    /// glyph's eruption races round the ring from that angle (<see cref="RaceDelay"/>; a presentation of the one trigger - the executor strikes every body on
    /// the band at once).</summary>
    public abstract class Vol2FsSeedRing : Vol2FsSeed
    {
        protected Color Acc;
        protected static readonly Color InkC = new Color(0.03f, 0.025f, 0.02f, 1f);
        protected ParticleSystem PCharge;
        Mesh _countMesh; readonly Vol2FsStrips _count = new Vol2FsStrips();
        /// <summary>How many seeds lie round the band.</summary>
        protected virtual int SeedCount => 24;
        /// <summary>How far inside the band the countdown arc burns (m).</summary>
        protected virtual float CountInset => 0.42f;
        /// <summary>Seconds the eruption takes to run half way round the ring (from the trigger to the opposite side).</summary>
        protected virtual float RaceSeconds => 0.32f;
        /// <summary>The seeds' angles round the band (rad from the cast's forward, positive = right).</summary>
        protected readonly List<float> SeedAng = new List<float>();
        /// <summary>The angle the trigger came from (rad); the cast's front until it is known.</summary>
        protected float TrigAng;
        protected bool TrigKnown;
        protected Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);
        protected Pod The => Pods.Count > 0 ? Pods[0] : null;
        protected float AngleOf(Vector3 p) => Vector3.SignedAngle(F, FlatN(p - Apex), Vector3.up) * Mathf.Deg2Rad;
        /// <summary>A seed's size against its place: full on the front and sides, smaller on the arc right behind the caster.</summary>
        protected static float SeedScale(float ang) => Mathf.Lerp(0.45f, 1f, Front(ang));
        /// <summary>Seconds after the trigger the eruption reaches angle <paramref name="ang"/>.</summary>
        protected float RaceDelay(float ang) => Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, TrigAng * Mathf.Rad2Deg)) / 180f * RaceSeconds;
        protected Vector3 Tangent(float ang) => Vector3.Cross(Vector3.up, Dir(ang));

        protected abstract void OnSetup();
        /// <summary>The seed ring appears (the launch).</summary>
        protected abstract void Planted(Pod p);
        /// <summary>The seed worked: TrigAng is known.</summary>
        protected abstract void Fired(Pod p);
        protected abstract void Live(float t, float h);

        protected override void OnBegin()
        {
            Acc = Accent(Element);
            int n = SeedCount;
            for (int k = 0; k < n; k++) SeedAng.Add(Vol2FsB2.Angle(k, n, Seed + 911, 0.3f));
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            PCharge.Play(false);
            var ink = M("fs_stroke2", InkC, 1f, 0f); ink.Edge = Acc * 2f; ink.EdgeWidth = 0.35f; ink.NoiseAmt = 0.25f; ink.Soft = 0.05f;
            Vol2Fs.MeshObject(root, "Countdown", Kit.Mat(ink), out _countMesh);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Tip() + Random.insideUnitSphere * 0.35f * (1f - k), Vector3.zero, 0.05f + 0.1f * k, 0.2f);
        }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs) { }
        protected override void OnPlant(Pod p) { Planted(p); }

        protected override void OnFire(Pod p)
        {
            FindTrigger();
            Fired(p);
        }

        /// <summary>뭄: the caster's place; else the body whose last step crossed the band (the executor's own crossing reading of this tick).</summary>
        void FindTrigger()
        {
            TrigAng = 0f; TrigKnown = false;
            if (Letter == '뭄') { TrigAng = AngleOf(CasterNow()); TrigKnown = true; return; }
            var co = Inst.Model.Coordinator; if (co == null) return;
            float best = -1f;
            foreach (var t in co.Targets)
            {
                if (t == null || !t.Alive) continue;
                Vector3 b = t.Position - Apex; b.y = 0f;
                float db = b.magnitude;
                bool onNow = db + t.Radius >= Inner && db - t.Radius <= Outer;
                float moved = (t.Position - t.Previous).magnitude;
                if (onNow && moved > best) { best = moved; TrigAng = AngleOf(t.Position); TrigKnown = true; }
            }
        }

        protected override void OnStrike(Pod p, Struck s)
        {
            if (!TrigKnown && !s.World) { TrigAng = AngleOf(FeetOf(s)); TrigKnown = true; }
            Hit(p, s);
        }
        /// <summary>One body on the band struck by the working seed (뭄: World = true, no body).</summary>
        protected virtual void Hit(Pod p, Struck s) { }

        /// <summary>The bodies standing inside the ring's outer edge now (a presentation reading of the registry: 뭄's locks).</summary>
        protected List<(long Id, Transform T, Vector3 Feet, float Radius)> BodiesInside()
        {
            var l = new List<(long, Transform, Vector3, float)>();
            var co = Inst.Model.Coordinator; if (co == null) return l;
            foreach (var t in co.Targets)
            {
                if (t == null || !t.Alive) continue;
                Vector3 d = t.Position - Apex; d.y = 0f;
                if (d.magnitude - t.Radius <= Outer) l.Add((t.Id, TargetTransform(t.Id), t.Position, t.Radius));
            }
            return l;
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _count.Clear();
            var p = The;
            if (p != null && p.Started && !p.Ended)
            {
                float life = PodLife(p, t, 0.35f), warn = PodWarn(p, t), beat = Beat(p), rem = Remain01(p, t);
                float flick = warn > 0f ? (Mathf.Sin(t * 40f) > 0f ? 1f : 0.45f) : 1f;
                float front = Mathf.Atan2(F.x, F.z);
                Vol2FsB5.Countdown(_count, Apex, Ground, Mathf.Max(0.5f, R - CountInset), 0.06f * (1f + 0.6f * warn), rem, life * flick * (0.5f + 0.25f * beat), 0.41f, cam, front - rem * Mathf.PI, 72);
            }
            _count.Write(_countMesh);
            Live(t, h);
        }
    }
}
