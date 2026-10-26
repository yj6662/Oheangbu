using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅜ 가른다 base of batch 6 (R4 굿 눗 뭇 숫 웃): the executor lays a 10 s LINE on the 2 m band at the launch (Vol2TimedEmitter, NOT a material):
    /// the cut circle is gouged into the ground there (CSV 자국 윤곽: 아래로 파인 균열선 · 반경 2 m 고리(앞쪽이 보임)) - drawn racing round from the front in
    /// <see cref="RaceSeconds"/> (a presentation of the one launch: the executor cuts the whole band at once), standing, flickering in the template's warning
    /// window before the emitter's EndUs, and leaving by the glyph's own end clause. The arc right behind the caster (between the caster and the shoulder
    /// camera) is drawn lower and thinner (<see cref="RingK"/>). Every body that crosses the band (an Instant contact: at the launch for the one standing on it,
    /// later for a walker) gets the base's crossing flash and the glyph's <see cref="Crossed"/> at its angle. The shared book (Vol2FsCut) parts the walls the
    /// executor split on the band and opens / closes the stripped bodies' seams; the shared Act draws the body action.</summary>
    public abstract class Vol2FsCutRing : Vol2FsCut
    {
        protected ParticleSystem PCharge, PInk;
        Mesh _gougeMesh, _glowMesh, _flashMesh; readonly Vol2FsStrips _gouge = new Vol2FsStrips(), _glow = new Vol2FsStrips(), _flash = new Vol2FsStrips();
        readonly List<(float Ang, float At, Vector3 C, float H)> _crosses = new List<(float, float, Vector3, float)>();
        /// <summary>Seconds the cut takes to run from the front to straight behind (both ways).</summary>
        protected virtual float RaceSeconds => 0.16f;
        protected virtual float GougeWidth => 0.2f;
        /// <summary>How long the glyph's end clause takes after the line's end (the base gouge closes over it).</summary>
        protected virtual float EndSeconds => 0.9f;
        protected virtual int RingN => 96;

        protected abstract void OnSetup();
        /// <summary>The line is down (the launch): the glyph's cut appears (it races round with <see cref="Reached"/>).</summary>
        protected virtual void Cut(float t) { }
        /// <summary>A body crossed the band (or stood on it at the launch) and was cut: ang = its angle round the ring.</summary>
        protected virtual void Crossed(Struck s, float ang) { }
        /// <summary>The line's end (the emitter's EndUs): the glyph's end clause begins.</summary>
        protected virtual void Ended() { }
        protected abstract void Live(float t, float h);

        /// <summary>A point's weight round the ring: 1 on the front and sides, low on the short arc behind the caster.</summary>
        protected static float RingK(float ang) => Mathf.Lerp(0.3f, 1f, Front(ang));
        /// <summary>0..1: has the racing cut reached angle <paramref name="ang"/> yet (ramped over a few degrees)?</summary>
        protected float Reached(float ang, float t)
        {
            if (!Launched) return 0f;
            float reach = Vol2Fs.Smooth(LaunchAt, LaunchAt + RaceSeconds, t) * 185f;
            return Mathf.Clamp01((reach - Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg))) / 12f);
        }
        /// <summary>Seconds since the racing cut passed angle <paramref name="ang"/> (-1 = not yet).</summary>
        protected float SincePassed(float ang, float t)
        {
            if (!Launched) return -1f;
            float at = LaunchAt + RaceSeconds * Mathf.Abs(Mathf.DeltaAngle(0f, ang * Mathf.Rad2Deg)) / 180f;
            return t < at ? -1f : t - at;
        }
        /// <summary>0..1 over the glyph's end clause after the line's end.</summary>
        protected float EndK(float t) => !LineEnded ? 0f : Mathf.Clamp01((t - LineEndedAt) / Mathf.Max(0.05f, EndSeconds));
        /// <summary>The line standing: up at the launch, down over the end clause.</summary>
        protected float Standing(float t) => !Launched ? 0f : Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.05f, t) * (1f - EndK(t));
        protected float Flick(float t) { float w = LineWarn(t); return w > 0f ? (Mathf.Sin(t * 40f) > 0f ? 1f : 0.45f) : 1f; }
        protected Vector3 Tangent(float ang) => Vector3.Cross(Vector3.up, Dir(ang));

        protected override void OnBegin()
        {
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            var ink = M("fs_ink", InkC, 1f, 0f); ink.Edge = Acc * 1.4f; ink.EdgeWidth = 0.15f; ink.NoiseAmt = 0.3f; ink.Soft = 0.1f;
            PInk = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.06f, Size1 = 0.2f, Gravity = 1.2f, Drag = 0.6f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "Ink");
            foreach (var ps in new[] { PCharge, PInk }) ps.Play(false);
            Vol2Fs.MeshObject(root, "RingGouge", Kit.InkSolid("fs_stroke2", InkC, CutGlow, 0.2f, 0.45f, 0.3f, 1.4f), out _gougeMesh);
            var glow = M("fs_streak", CutGlow, 2.2f, 1f); glow.NoiseAmt = 0.25f; glow.Soft = 0.03f; glow.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "RingGlow", Kit.Mat(glow), out _glowMesh);
            var fl = M("fs_streak", Color.Lerp(Acc, Color.white, 0.12f), 1.9f, 0.7f); fl.NoiseAmt = 0.2f; fl.Soft = 0.04f; fl.Erode = 0.14f;      // r3: the accent, not a white bar
            Vol2Fs.MeshObject(root, "CrossFlash", Kit.Mat(fl), out _flashMesh);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Tip() + Random.insideUnitSphere * 0.35f * (1f - k), Vector3.zero, 0.06f + 0.1f * k, 0.2f);
        }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs)
        {
            float t = Now(nowUs);
            for (int k = 0; k < 16; k++) { float a = k / 16f * Mathf.PI * 2f; Vol2FsB1.EmitV(PInk, OnBand(a, R) + Vector3.up * 0.1f, Dir(a) * Random.Range(1f, 3f) + Vector3.up * 1.2f, -1f, -1f); }
            try { Cut(t); } catch (System.Exception e) { Debug.LogException(e); }
        }

        protected override void OnStruck(Struck s)
        {
            if (s.Kind != "Instant") return;
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            float ang = AngleOf(FeetOf(s));
            _crosses.Add((ang, Now(NowUs), b.center, b.size.y));
            try { Crossed(s, ang); } catch (System.Exception e) { Debug.LogException(e); }
        }

        protected override void OnLineEnd() { try { Ended(); } catch (System.Exception e) { Debug.LogException(e); } }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _gouge.Clear(); _glow.Clear(); _flash.Clear();
            float stand = Standing(t), flick = Flick(t);
            if (stand > 0.01f)
            {
                int n = RingN;
                var pts = new List<Vector3>(n + 1); var wd = new List<float>(n + 1); var al = new List<float>(n + 1); var sd = new List<Vector3>(n + 1);
                var gpts = new List<Vector3>(n + 1); var gwd = new List<float>(n + 1); var gal = new List<float>(n + 1);
                float hot = 1f - Vol2Fs.Smooth(0.15f, 1.4f, t - LaunchAt);
                for (int k = 0; k <= n; k++)
                {
                    float ang = -Mathf.PI + k / (float)n * Mathf.PI * 2f, rk = RingK(ang), reach = Reached(ang, t);
                    float j = (Vol2Fs.Hash(Seed, k % n) - 0.5f) * 0.05f;
                    Vector3 p = OnBand(ang, R + j); p.y = Ground + 0.02f;
                    pts.Add(p); sd.Add(Dir(ang)); wd.Add(GougeWidth * rk * (0.75f + 0.5f * Vol2Fs.Hash(Seed, k % n + 200)) * reach); al.Add(stand * reach);
                    Vector3 g = p; g.y = Ground + 0.03f;
                    gpts.Add(g); gwd.Add(GougeWidth * 0.32f * rk * reach); gal.Add(stand * reach * flick * Mathf.Max(hot, 0.3f));
                }
                _gouge.Strip(pts, wd, sd, Color.white, al, 0.13f, cam, 0f, 12f);
                _glow.Strip(gpts, gwd, sd, Color.white, gal, 0.57f, cam, 0f, 12f);
            }
            // the crossing slash (Phantom Blade principles 3 + 5): drawn up the body in 0.1 s, the blade itself gone by 0.45 s, two lagged phantoms lingering
            // slowly to 0.8 / 1.0 s (each a little wider and offset, fading)
            foreach (var c in _crosses)
            {
                float a = t - c.At; if (a < 0f || a > 1.05f) continue;
                Vector3 d = Dir(c.Ang), basePt = OnBand(c.Ang, R) + Vector3.up * 0.05f, top = basePt + Vector3.up * (c.H + 0.6f);
                Vector3 bow = FlatN(cam - c.C) * 0.2f + Tangent(c.Ang) * 0.15f, side = Tangent(c.Ang);
                Vol2FsB6.Slash(_flash, basePt, top, bow, 0.32f, Vol2Fs.Smooth(0f, 0.1f, a), 1f - Vol2Fs.Smooth(0.12f, 0.45f, a), 0.29f, cam);
                Vol2FsB6.Slash(_flash, basePt - d * 0.08f + side * 0.1f, top + Vector3.up * 0.1f + side * 0.12f, bow * 1.4f, 0.42f, Vol2Fs.Smooth(0.03f, 0.14f, a), 0.38f * (1f - Vol2Fs.Smooth(0.2f, 0.8f, a)), 0.77f, cam);
                Vol2FsB6.Slash(_flash, basePt - d * 0.16f - side * 0.12f, top + Vector3.up * 0.2f - side * 0.1f, bow * 1.8f, 0.52f, Vol2Fs.Smooth(0.06f, 0.18f, a), 0.2f * (1f - Vol2Fs.Smooth(0.3f, 1.0f, a)), 0.53f, cam);
            }
            _gouge.Write(_gougeMesh); _glow.Write(_glowMesh); _flash.Write(_flashMesh);
            try { Live(t, h); } catch (System.Exception e) { Debug.LogException(e); }
        }

        protected override float Tail => EndSeconds + 0.3f;
    }
}
