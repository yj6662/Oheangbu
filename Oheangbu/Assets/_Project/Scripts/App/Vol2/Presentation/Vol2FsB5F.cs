using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅗ 심는다 base of batch 5 (F3 곰 놈 솜 옴; 몸 uses it for the shared readings only): the seed is the executor's ONE Fan Mark material, made at
    /// the start, standing for its life over the spec's fan. The base scatters the glyph's dormant seeds over the fan (from a third out: the near band stays out of
    /// the shoulder camera's face) and draws the two shared readings of the wait - the fan's two side edges as faint ink lines and a burning-down ink arc along its
    /// far edge (both on the live expiry; 信 holds them; the last 0.4 s flare). When the seed works (Consumed) the body that ENTERED is found as the executor finds it
    /// (a struck body that was not in the fan at the start - the rule ignores the first occupants), and the burst runs over the seeds as a wave from where it came
    /// in (each seed's own delay = its distance / <see cref="WaveSpeed"/>: a presentation order of one executor event). Expired = the seeds wither from the far edge.</summary>
    public abstract class Vol2FsSeedFan : Vol2FsSeed
    {
        protected Color Acc;
        protected static readonly Color InkC = new Color(0.03f, 0.025f, 0.02f, 1f);
        protected ParticleSystem PCharge;
        Mesh _lineMesh; readonly Vol2FsStrips _lines = new Vol2FsStrips();
        protected Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        /// <summary>A dormant seed on the fan's ground.</summary>
        public sealed class Spot { public Vector3 P; public float U, V, Rnd, Delay = -1f, BurstAt = -1f; public int K; public bool Burst; public Transform T; public object Data; }
        protected readonly List<Spot> Spots = new List<Spot>();
        protected virtual int SpotCount => 22;
        protected virtual float SpotU0 => 0.32f;
        /// <summary>How fast the burst runs over the seeds from the entry (m/s).</summary>
        protected virtual float WaveSpeed => 10f;
        /// <summary>Where the trigger came from (the entering body's feet), set at the fire.</summary>
        protected Vector3 EntryAt; protected bool HasEntry;
        readonly HashSet<long> _firstIn = new HashSet<long>();
        bool _entryFound;
        protected Pod The => Pods.Count > 0 ? Pods[0] : null;
        protected virtual bool SharedReadings => true;

        protected abstract void OnSetup();
        /// <summary>The glyph's seeds appear (the Spots are laid).</summary>
        protected abstract void Planted(Pod p);
        /// <summary>The wave reached this seed: its burst.</summary>
        protected abstract void SpotBurst(Spot s);
        protected abstract void Live(float t, float h);
        /// <summary>The fire: the wave's start is known (EntryAt), the spots' delays set.</summary>
        protected virtual void Fired(Pod p) { }

        protected override void OnBegin()
        {
            Acc = Accent(Element);
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            PCharge.Play(false);
            var ink = M("fs_stroke2", Acc, 2.1f, 0.85f); ink.NoiseAmt = 0.25f; ink.Soft = 0.05f;      // r3: glowing accent readings (r2: faint ink lines)
            Vol2Fs.MeshObject(root, "FanReadings", Kit.Mat(ink), out _lineMesh);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Tip() + Random.insideUnitSphere * 0.3f * (1f - k), Vector3.zero, 0.05f + 0.1f * k, 0.2f);
        }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs) { }

        protected Vector3 InFan(float u, float v) => Vol2FsB3.InFan(Apex, F, Depth, Half, u, v, Ground);

        protected override void OnPlant(Pod p)
        {
            // the bodies standing in the fan at its start: the rule does not let them trigger it (Vol2SeedExecutor TouchingIds); the entry is a body that was not
            var co = Inst.Model.Coordinator;
            if (co != null && p.M != null && p.M.Footprint != null)
                foreach (var tg in co.Targets) if (tg != null && tg.Alive && p.M.Footprint.Touches(tg, NowUs)) _firstIn.Add(tg.Id);
            var sc = Vol2FsB5.FanScatter(Seed + 17, SpotCount, SpotU0);
            for (int k = 0; k < sc.Count; k++) Spots.Add(new Spot { K = k, U = sc[k].x, V = sc[k].y, P = InFan(sc[k].x, sc[k].y), Rnd = Vol2Fs.Hash(Seed, k + 900) });
            Planted(p);
        }

        protected override void OnFire(Pod p)
        {
            EntryAt = G(InFan(0.75f, 0f)); HasEntry = false; _entryFound = false;
            foreach (var s in Spots) s.Delay = Vector3.Distance(s.P, EntryAt) / WaveSpeed;
            Fired(p);
        }

        protected override void OnStrike(Pod p, Struck s)
        {
            if (s.World) return;
            // the entering body: the first struck body that was not in the fan at its start (else the farthest struck)
            bool entered = !_firstIn.Contains(s.Id);
            if (!_entryFound && (entered || !HasEntry))
            {
                EntryAt = G(FeetOf(s)); HasEntry = true; _entryFound = entered;
                foreach (var sp in Spots) sp.Delay = Vector3.Distance(sp.P, EntryAt) / WaveSpeed;
            }
            OnStruckBody(p, s);
        }
        /// <summary>A body the burst struck (A and B in the capture): its own hit.</summary>
        protected virtual void OnStruckBody(Pod p, Struck s) { }
        /// <summary>When the wave reaches a point (rel time), -1 before the fire.</summary>
        protected float WaveAt(Vector3 at) { var p = The; return p == null || p.FiredAt < 0f ? -1f : p.FiredAt + Vector3.Distance(G(at), EntryAt) / WaveSpeed; }

        /// <summary>0..1 a seed is gone by its natural end: the far edge first (CSV: the end clauses run outward-in), over the exit tail.</summary>
        protected float SpotWither(Spot s, float t)
        {
            var p = The; if (p == null || !Withered(p)) return 0f;
            float ex = PodExit(p, t), at = (1f - s.U) * 0.45f;
            return Vol2Fs.Smooth(at, at + 0.55f, ex);
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos; _lines.Clear();
            var p = The;
            if (SharedReadings && p != null && p.Started && !p.Ended)
            {
                float life = PodLife(p, t, 0.35f), warn = PodWarn(p, t), beat = Beat(p), rem = Remain01(p, t);
                float flick = warn > 0f ? (Mathf.Sin(t * 40f) > 0f ? 1f : 0.45f) : 1f, al = life * flick;
                // the side edges (faint), from a third out
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 a = InFan(SpotU0 - 0.02f, side) + Vector3.up * 0.03f, b = InFan(1f, side) + Vector3.up * 0.03f;
                    Vector3 sd = Vector3.Cross(Vector3.up, (b - a).normalized);
                    _lines.Strip(new[] { a, Vector3.Lerp(a, b, 0.5f), b }, new[] { 0.05f, 0.08f, 0.07f }, new[] { sd, sd, sd }, Color.white, new[] { al * 0.45f, al * 0.8f, al * 0.8f }, 0.13f + side * 0.07f, cam);
                }
                // the countdown: the far edge burns down from the right toward the left
                int n = Mathf.Max(3, Mathf.CeilToInt(30 * rem));
                var pts = new List<Vector3>(); var wd = new List<float>(); var als = new List<float>(); var sides = new List<Vector3>();
                for (int i = 0; i <= n; i++)
                {
                    float u = i / (float)n, v = -1f + 2f * rem * u;
                    Vector3 d = Quaternion.AngleAxis(v * Half * Mathf.Rad2Deg, Vector3.up) * F;
                    pts.Add(G(Apex + d * Depth) + Vector3.up * 0.04f); sides.Add(d);
                    wd.Add(0.12f * (1f + 0.6f * warn) * (u > 0.92f ? 1.8f : 1f)); als.Add(al * (0.6f + 0.25f * beat) * (u > 0.92f ? 1.3f : 1f));
                }
                _lines.Strip(pts, wd, sides, Color.white, als, 0.61f, cam, 0f, rem * 4f);
            }
            _lines.Write(_lineMesh);
            // the wave over the seeds
            if (p != null && p.FiredAt >= 0f)
                foreach (var s in Spots)
                    if (!s.Burst && s.Delay >= 0f && t >= p.FiredAt + s.Delay)
                    {
                        s.Burst = true; s.BurstAt = p.FiredAt + s.Delay;
                        try { SpotBurst(s); } catch (System.Exception e) { Debug.LogException(e); }
                    }
            Live(t, h);
        }

        /// <summary>The wave's own tail after the last seed burst (the seeds' visual delay is longer than the executor's instant end).</summary>
        protected override float PodExitSeconds(Vol2MaterialEnd why) => why == Vol2MaterialEnd.Consumed ? FireSeconds + Depth / WaveSpeed : base.PodExitSeconds(why);
    }
}
