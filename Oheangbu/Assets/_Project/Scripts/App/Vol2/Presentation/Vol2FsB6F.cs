using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅗ 가른다 base of batch 6 (F4 곳 놋 못 솟 옷): the fan is cut ONCE at the launch (the executor strips every body in it and splits every material
    /// in it on that tick). The look draws that one cut as a sweep across the fan from its right edge to its left (<see cref="SweepSeconds"/> - a presentation of
    /// the one tick, the facts all land at the launch) and leaves the gouged crack lines in the ground (CSV 자국 윤곽: 아래로 파인 균열선 · 전방 다발 면): by
    /// default radial lines from a quarter of the depth out (the near band stays out of the shoulder camera's face), a glyph may lay its own (놋's parallel
    /// claws). Each line opens when the sweep passes its angle, glows hot, cools, and closes when the look's strips are over. The shared book (Vol2FsCut) parts
    /// the split walls and opens / closes the stripped bodies' seams; the glyph draws its sweep (<see cref="Sweep"/>) and its element.</summary>
    public abstract class Vol2FsCutFan : Vol2FsCut
    {
        protected ParticleSystem PCharge, PInk;
        Mesh _crackMesh, _crackGlowMesh; readonly Vol2FsStrips _crack = new Vol2FsStrips(), _crackGlow = new Vol2FsStrips();
        protected Light SweepLight;
        /// <summary>One gouge: from a to b on the ground, opened at its own time (the sweep reaching it).</summary>
        public struct Gouge { public Vector3 A, B; public float Width, OpenAt, Rnd; public int Seed; }
        protected readonly List<Gouge> Gouges = new List<Gouge>();
        float _lastK = -1f;
        /// <summary>How long the drawn sweep takes edge to edge (s) - presentation of the one launch tick.</summary>
        /// <summary>r2 (main's r1 review: from the shoulder camera the whole fan crowds behind the caster and the bears): the sweep is drawn in 0.1 s and lingers
        /// (<see cref="Linger"/>), the gouges are much wider and stay hot 1.5 s.</summary>
        protected virtual float SweepSeconds => 0.1f;
        protected virtual float CrackWidth => 0.36f;
        protected virtual float CrackJag => 0.025f;      // r3: half (r2 hot lines read as lightning zigzags)
        protected virtual int GougeCount => 7;
        /// <summary>How long the hot line in a gouge stays bright (s) before it cools.</summary>
        protected virtual float HotSeconds => 1.5f;
        /// <summary>1 while the sweep is drawn, then a slow fade over <paramref name="linger"/> s (Phantom Blade principle 3: fast stroke, slow afterimage).</summary>
        protected float Linger(float t, float hold = 0.15f, float linger = 0.8f) { float since = t - LaunchAt; return !Launched ? 0f : 1f - Vol2Fs.Smooth(SweepSeconds + hold, SweepSeconds + hold + linger, since); }
        /// <summary>The gouges start this far out (0..1 of the depth) and reach this far.</summary>
        protected virtual float GougeU0 => 0.28f;
        protected virtual float GougeU1 => 1.0f;

        protected abstract void OnSetup();
        /// <summary>The sweep ran from <paramref name="prevK"/> to <paramref name="k"/> (0 = the right edge, 1 = the left edge) this frame.</summary>
        protected abstract void Sweep(float t, float k, float prevK);
        protected abstract void Live(float t, float h);

        /// <summary>The fan angle (rad, + = right) the sweep is at for progress k.</summary>
        protected float SweepAng(float k) => Mathf.Lerp(Half, -Half, k);
        /// <summary>0..1 sweep progress at t (0 before the launch).</summary>
        protected float SweepK(float t) => !Launched ? 0f : Mathf.Clamp01((t - LaunchAt) / Mathf.Max(0.02f, SweepSeconds));
        /// <summary>When the sweep passes angle <paramref name="ang"/> (look clock).</summary>
        protected float SweepAt(float ang) => LaunchAt + Mathf.InverseLerp(Half, -Half, ang) * SweepSeconds;
        /// <summary>A point in the fan: radial u (0..1 of the depth), angle (rad), height above the ground.</summary>
        protected Vector3 InFan(float u, float ang, float up = 0f) { var p = Apex + Dir(ang) * Depth * u; p.y = Ground + up; return p; }

        /// <summary>Lays the gouges (called at the launch). Default: <see cref="GougeCount"/> radial lines across the fan.</summary>
        protected virtual void LayGouges()
        {
            int n = Mathf.Max(1, GougeCount);
            for (int k = 0; k < n; k++)
            {
                float v = n == 1 ? 0f : k / (float)(n - 1) * 2f - 1f;
                float ang = v * Half * 0.92f + (Vol2Fs.Hash(Seed, k + 700) - 0.5f) * 0.08f;
                float u0 = GougeU0 + 0.08f * Vol2Fs.Hash(Seed, k + 710), u1 = GougeU1 - 0.12f * Vol2Fs.Hash(Seed, k + 720);
                Gouges.Add(new Gouge { A = InFan(u0, ang), B = InFan(u1, ang + (Vol2Fs.Hash(Seed, k + 730) - 0.5f) * 0.1f), Width = CrackWidth * (0.8f + 0.4f * Vol2Fs.Hash(Seed, k + 740)), OpenAt = SweepAt(ang), Rnd = Vol2Fs.Hash(Seed, k + 750), Seed = Seed + k * 11 });
            }
        }

        protected override void OnBegin()
        {
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            var ink = M("fs_ink", InkC, 1f, 0f); ink.Edge = Acc * 1.4f; ink.EdgeWidth = 0.15f; ink.NoiseAmt = 0.3f; ink.Soft = 0.1f;
            PInk = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.06f, Size1 = 0.2f, Gravity = 1.2f, Drag = 0.6f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 500 }, root, "Ink");
            foreach (var ps in new[] { PCharge, PInk }) ps.Play(false);
            Vol2Fs.MeshObject(root, "FanGouge", Kit.InkSolid("fs_stroke2", InkC, CutGlow, 0.2f, 0.45f, 0.3f, 1.4f), out _crackMesh);
            var glow = M("fs_streak", CutGlow, 2.8f, 1f); glow.NoiseAmt = 0.25f; glow.Soft = 0.03f; glow.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "FanGougeGlow", Kit.Mat(glow), out _crackGlowMesh);
            SweepLight = Vol2Fs.PointLight(root, "SweepLight", Color.Lerp(Acc, Color.white, 0.25f), 6f);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Tip() + Random.insideUnitSphere * 0.35f * (1f - k), Vector3.zero, 0.06f + 0.1f * k, 0.2f);
        }

        protected override void OnLaunch(long nowUs) { LayGouges(); SweepLight.intensity = 5f; }
        protected override void OnHit(long nowUs) { }

        /// <summary>The gouges stand while any strip is open (or the cast runs), then close over 0.8 s.</summary>
        protected float GougeLife(float t)
        {
            float end = -1f; bool open = false;
            foreach (var s in Strips) { if (!s.Ended) open = true; else end = Mathf.Max(end, s.EndAt); }
            if (open || (Strips.Count == 0 && EndRel < 0f)) return 1f;
            if (Metal != null && MetalEndAt < 0f) return 1f;
            if (end < 0f) end = EndRel >= 0f ? Mathf.Max(EndRel, LaunchAt + 1.2f) : t;
            if (MetalEndAt >= 0f) end = Mathf.Max(end, MetalEndAt);
            return 1f - Mathf.Clamp01((t - end) / 0.8f);
        }

        protected override void OnLive(float t, float h) { }

        public override void Tick(long nowUs)
        {
            base.Tick(nowUs);
            if (Root == null || !Launched) return;
            float t = Now(nowUs);
            float k = SweepK(t);
            if (k > _lastK + 1e-4f)
            {
                try { Sweep(t, k, Mathf.Max(0f, _lastK)); } catch (System.Exception e) { Debug.LogException(e); }
                SweepLight.transform.position = InFan(0.6f, SweepAng(k), 0.6f);
                _lastK = k;
            }
            else SweepLight.intensity = Mathf.MoveTowards(SweepLight.intensity, 0f, Time.deltaTime * 14f);
            _crack.Clear(); _crackGlow.Clear();
            float life = GougeLife(t);
            if (life > 0.01f)
                foreach (var g in Gouges)
                {
                    float a = t - g.OpenAt; if (a < 0f) continue;
                    float reveal = Vol2Fs.Smooth(0f, 0.09f, a);
                    Vol2FsB6.Crack(_crack, g.A, g.B, Ground, g.Width, 0f, reveal, life, g.Rnd, g.Seed, CrackJag);
                    float hot = (1f - Vol2Fs.Smooth(HotSeconds, HotSeconds + 1.1f, a)) * life;
                    // r3: the dark gouge is the subject; the hot line is a thin core in it (0.4x of r2)
                    Vol2FsB6.Crack(_crackGlow, g.A, g.B, Ground, g.Width * 0.18f, 0f, reveal, Mathf.Max(hot, 0.3f * life), g.Rnd + 0.37f, g.Seed, CrackJag, 0.03f);
                }
            _crack.Write(_crackMesh); _crackGlow.Write(_crackGlowMesh);
            if (IsHit) { try { Live(t, t - HitAt); } catch (System.Exception e) { Debug.LogException(e); } }
        }

        /// <summary>r2: the sweep's crescent - the whole arc from the right edge to where the sweep is, drawn in <see cref="SweepSeconds"/> and lingering
        /// (<see cref="Linger"/>), with two phantom copies lagging behind it, higher and fainter (Phantom Blade principles 3 + 5).</summary>
        protected void SweepArc(Vol2FsStrips main, Vol2FsStrips phantom, float t, float r, float y, float w, float rnd, float tilt = 0.6f, float rise = 0.15f, float hold = 0.15f, float linger = 0.8f)
        {
            float life = Linger(t, hold, linger); if (life <= 0.01f) return;
            float a0 = Half * 1.05f, a1 = SweepAng(SweepK(t)) - 0.05f;
            Crescent(main, a0, a1, r, y, w, life, rnd, 30, rise, tilt);
            if (phantom == null) return;
            Crescent(phantom, a0, a1 + 0.12f, r + 0.3f, y + 0.18f, w * 1.15f, life * 0.4f, rnd + 0.3f, 30, rise, tilt);
            Crescent(phantom, a0, a1 + 0.24f, r - 0.32f, y + 0.34f, w * 1.3f, life * 0.2f, rnd + 0.6f, 30, rise, tilt);
        }

        /// <summary>A ground-parallel crescent band of the sweep: the arc from angle a0 to a1 at radius r (m), height y, width w (radial), its leading end widest
        /// (thin-to-wide, Phantom Blade principle 2).</summary>
        protected void Crescent(Vol2FsStrips s, float a0, float a1, float r, float y, float w, float life, float rnd, int n = 20, float rise = 0f, float tilt = 0f)
        {
            if (life <= 0.01f || Mathf.Abs(a1 - a0) < 1e-3f) return;
            var pts = new Vector3[n]; var wd = new float[n]; var al = new float[n]; var side = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1), ang = Mathf.Lerp(a0, a1, u);
                Vector3 d = Dir(ang);
                pts[i] = Apex + d * r + Vector3.up * (y + rise * u); pts[i].y = Ground + y + rise * u;
                side[i] = (d + Vector3.up * tilt).normalized; wd[i] = w * Mathf.Sin(Mathf.Pow(u, 1.3f) * Mathf.PI * 0.92f + 0.05f); al[i] = life * (0.3f + 0.7f * u);
            }
            s.Strip(pts, wd, side, Color.white, al, rnd, CamPos, 0f, 1f);
        }
    }
}
