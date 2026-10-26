using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>엄 (water, ㅓ attachment, 심는다) - free showcase batch 5, concept eom_a (a bead of clear water with ink turning inside rides the brush shaft; when
    /// the caster is struck it bursts into a ring wave of water racing out from the caster's feet, crashing over every bear that close, spray everywhere).
    /// Unlike 어 (a vortex disc held in front), 억 (a water whip), 언 (drops that float off) and 우 (a ring of geysers) it is one low circular wave that
    /// rolls OUT from the caster. Executor facts only: the start = a bead wells up on the shaft -> the wait (it wobbles and swells on the heartbeat, the ink
    /// inside turns, now and then a drop runs down to the sleeve, the ink dial and the 2 m ground arc burn down; the last 0.4 s it quivers) -> Consumed (the
    /// player really lost HP) = the bead bursts and the wave rises from the feet and rolls out past the reach (crest spray, foam ring, a darkened wet ground),
    /// each struck bear (OnStrike) takes a splash crown and stays drenched with dripping water for the wet's 4 s (the status sink's; mirrored); the water
    /// settles and runs down (CSV 끝: 아래로 흘러내림); Expired = the bead runs down the shaft and drips off the sleeve into a small wet patch.</summary>
    public sealed class Vol2FsEom : Vol2FsSeedWear
    {
        const float WetSeconds = 4f, RollSeconds = 0.45f;
        ParticleSystem _spray, _drips, _foam, _inkSwirl;
        Material _water;
        Mesh _bead, _waveMesh, _wetMesh; readonly Vol2FsStrips _waveS = new Vol2FsStrips(), _wetS = new Vol2FsStrips();
        readonly List<Struck> _soaked = new List<Struck>();
        Vector3 _runFrom;
        protected override float SeedU => 0.55f;
        protected override float FireSeconds => WetSeconds;
        protected override float ExpireSeconds => 1.3f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _water = Vol2FsB1.Water(Kit, 1.5f, 0.35f);
            var spray = M("fs_splash", new Color(0.5f, 0.78f, 0.95f, 1f), 1.15f, 0.45f);      // r3: blue-teal spray (r2: white popcorn) spray.NoiseAmt = 0.3f; spray.Soft = 0.1f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spray), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.4f, Radius = 0.25f, Gravity = 1.6f, Drag = 0.6f, Tiles = 2, Spin = 120f, Max = 900 }, root, "Spray");
            _drips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.7f, 0.9f, 1f, 1f), 1.6f, 0.6f)), Life0 = 0.35f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.03f, Size1 = 0.06f, Radius = 0.08f, Gravity = 1.6f, Stretch = true, StretchVel = 0.06f, Max = 500 }, root, "Drips");
            var foam = M("fs_puff", new Color(0.62f, 0.82f, 0.92f, 1f), 1.0f, 0.3f); foam.Opacity = 0.5f; foam.NoiseAmt = 0.5f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.5f, Speed1 = 1.5f, Size0 = 0.3f, Size1 = 0.7f, Radius = 0.2f, Drag = 3f, Tiles = 2, SizeEnd = 1.6f, Max = 400, SortFudge = 6 }, root, "Foam");
            var ink = M("fs_ink", InkC, 1f, 0f); ink.NoiseAmt = 0.3f; ink.Soft = 0.15f;
            _inkSwirl = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.25f, Life1 = 0.4f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.03f, Size1 = 0.05f, Tiles = 2, Max = 200, SortFudge = -4 }, root, "InkInside");
            foreach (var ps in new[] { _spray, _drips, _foam, _inkSwirl }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Bead", _water, out _bead);
            Vol2Fs.MeshObject(root, "Wave", Vol2FsB1.Water(Kit, 1.6f, 0.4f, "fs_water"), out _waveMesh);
            var wet = M("fs_wet", new Color(0.15f, 0.2f, 0.24f, 1f), 1f, 0f); wet.Opacity = 0.6f; wet.NoiseAmt = 0.4f; wet.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "Wet", Kit.Mat(wet), out _wetMesh);
        }

        protected override void OnPlant(Pod p) { Vol2FsB1.Emit(_spray, SeedPoint(), 10); }

        protected override void Fired(Pod p)
        {
            _bead.Clear();
            Vector3 c = SeedPoint();
            Vol2FsB1.Emit(_spray, c, 120); HitStop(Now(NowUs), 0.07f);
            for (int k = 0; k < 60; k++) { float a = k / 60f * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); Vol2FsB1.EmitV(_foam, FireFeet + d * 0.4f + Vector3.up * 0.05f, d * Random.Range(2f, 4f), -1f, -1f); }
        }

        protected override void OnStrike(Pod p, Struck s)
        {
            if (s.World) return;
            _soaked.Add(s);
            Vector3 c = CentreNow(s);
            for (int k = 0; k < 90; k++) Vol2FsB1.EmitV(_spray, c + Vector3.down * 0.3f, Random.onUnitSphere * 2f + Vector3.up * Random.Range(2f, 5f), Random.Range(0.15f, 0.4f), -1f);
        }

        protected override void OnPodEnd(Pod p) { if (!Worked(p)) _runFrom = SeedPoint(); }

        /// <summary>The ring wave at s s after the trigger: a standing band of water at radius r, its height h (lower on the short arc behind the caster), a lip curling out on top.</summary>
        void Wave(Vector3 feet, float s, Vector3 cam)
        {
            float k = Expand(s, RollSeconds), r = Mathf.Lerp(0.3f, Reach + 0.5f, k);
            float rise = Vol2Fs.Smooth(0f, 0.12f, s), fall = Vol2Fs.Smooth(RollSeconds * 0.7f, RollSeconds + 0.4f, s);
            float hMax = 1.25f * rise * (1f - fall);
            if (hMax < 0.02f) return;
            const int n = 56;
            var pts = new List<Vector3>(n + 1); var w = new List<float>(n + 1); var al = new List<float>(n + 1); var side = new List<Vector3>(n + 1);
            var lip = new List<Vector3>(n + 1); var lw = new List<float>(n + 1); var lside = new List<Vector3>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float ang = (i / (float)n * 2f - 1f) * Mathf.PI, front = Mathf.Lerp(0.2f, 1f, Front(ang));
                Vector3 d = Dir(ang);
                float h = hMax * front * (0.85f + 0.15f * Mathf.Sin(ang * 7f + s * 9f));
                pts.Add(G(feet + d * r) + Vector3.up * h * 0.5f); w.Add(h); side.Add(Vector3.up); al.Add(0.85f * (1f - fall * 0.5f));
                Vector3 curl = (d * 0.7f + Vector3.up * 0.7f).normalized;
                lip.Add(G(feet + d * (r + 0.12f)) + Vector3.up * h); lw.Add(0.35f * front * rise * (1f - fall)); lside.Add(curl);
                if (i % 4 == 0 && Time.frameCount % 2 == 0 && h > 0.2f) Vol2FsB1.EmitV(_spray, G(feet + d * r) + Vector3.up * h, d * Random.Range(1f, 3f) + Vector3.up * Random.Range(1f, 3f), -1f, -1f);
            }
            _waveS.Strip(pts, w, side, Color.white, al, 0.27f, cam, 0f, 6f);
            _waveS.Strip(lip, lw, lside, Color.white, al, 0.61f, cam, 0f, 6f);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; var p = The;
            _waveS.Clear(); _wetS.Clear();
            if (p != null && !Worked(p))
            {
                float life = PodLife(p, t, 0.3f), beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t);
                if (p.Started && !p.Ended)
                {
                    Vector3 c = SeedPoint() + Vector3.Cross(ShaftAxis, Vector3.up).normalized * 0.03f;
                    float r = 0.11f * (1f + 0.12f * beat + 0.12f * warn) * life, wob = 0.07f + 0.15f * warn, ph = t * (5f + 22f * warn) * (1f - Held01);
                    Vol2FsB1.WaterSphere(_bead, c, r, -1f, cam, 0.9f * life, d => d * (1f + wob * Mathf.Sin(ph + d.x * 4f + d.y * 3f)), 18, 10, 1.5f);
                    if (Time.frameCount % 2 == 0) { float a = t * 4f; Vol2FsB1.EmitV(_inkSwirl, c + new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.3f) * 0.5f, Mathf.Sin(a)) * r * 0.45f, Vector3.zero, -1f, -1f); }
                    if (Time.frameCount % 23 == 0) Vol2FsB1.EmitV(_drips, Vector3.Lerp(c, Sleeve(), 0.5f), Vector3.down * 0.2f, -1f, -1f);
                }
                else if (Withered(p))
                {
                    // the bead runs down the shaft to the sleeve and drips off; a small wet patch
                    float run = Vol2Fs.Smooth(0f, 0.55f, ex);
                    Vector3 c = Vector3.Lerp(_runFrom, Sleeve(), run);
                    float r = 0.11f * (1f - run * 0.7f);
                    if (ex < 0.6f) Vol2FsB1.WaterSphere(_bead, c, r, -1f, cam, 0.9f, d => d + Vector3.down * 0.5f * Mathf.Max(0f, -d.y), 18, 10, 1.5f); else _bead.Clear();
                    if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_drips, c, Vector3.down * 0.5f, -1f, -1f);
                    float wet = Vol2Fs.Smooth(0.3f, 0.6f, ex) * (1f - Vol2Fs.Smooth(0.85f, 1f, ex));
                    if (wet > 0.01f) _wetS.Quad(FeetNow() + Vector3.up * 0.02f + Right * 0.25f, Vector3.right, Vector3.forward, 0.9f, new Color(1f, 1f, 1f, wet), 0.4f);
                }
                else _bead.Clear();
                _waveS.Write(_waveMesh); _wetS.Write(_wetMesh);
                return;
            }
            if (p == null) { _waveS.Write(_waveMesh); _wetS.Write(_wetMesh); return; }
            float s = SinceFire(p, t);
            Wave(FireFeet, s, cam);
            // the darkened wet ground behind the wave, drying over the wet's 4 s
            float wg = Vol2Fs.Smooth(0.1f, RollSeconds, s) * (1f - Vol2Fs.Smooth(WetSeconds - 0.8f, WetSeconds, s));
            if (wg > 0.01f) _wetS.Quad(FireFeet + Vector3.up * 0.02f, Vector3.right, Vector3.forward, (Reach + 0.6f) * 2f, new Color(1f, 1f, 1f, wg), 0.35f);
            if (s > RollSeconds * 0.8f && s < RollSeconds + 0.5f && Time.frameCount % 2 == 0) { float ang = Random.Range(-Mathf.PI, Mathf.PI); Vol2FsB1.Emit(_foam, G(FireFeet + Dir(ang) * (Reach + 0.4f)) + Vector3.up * 0.05f, 2); }
            foreach (var b in _soaked)
            {
                float ba = t - b.At; if (ba > WetSeconds || Time.frameCount % 2 != 0) continue;
                var bb = b.T != null ? BodyBounds(b.T, b.T.position, 0.5f) : b.B;
                Vol2FsB1.EmitV(_drips, bb.center + new Vector3(Random.Range(-bb.extents.x, bb.extents.x), Random.Range(-0.3f, bb.extents.y), Random.Range(-bb.extents.z, bb.extents.z)), Vector3.down * 0.3f, -1f, -1f);
            }
            _waveS.Write(_waveMesh); _wetS.Write(_wetMesh);
        }
    }
}
