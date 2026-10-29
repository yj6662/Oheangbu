using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>웅 (water, ㅜ ring, 돌아온다 - the PEAK) - free showcase batch 7, concept ung_a (a low clear ring of water sweeps outward over the ground like a broom of
    /// water, ink swirling in it; the dark pools it passes boil away into white mist and spray; it slides back in, leaving the ground clean and wet; the caster's own
    /// stone stands glistening). Executor facts only: a wide band of clear water (foam on its leading edge, two ink swirls riding in it) at the band's radius (the
    /// report's legs: 2 m -> 4 m and back), spray thrown off the front arc as it moves; every enemy material the report says it Cleansed (the lab's stand-in pool
    /// goes the same frame) boils away where it lay - a column of white mist and spray, the corruption flung up as dark droplets that fade - and leaves a clean wet
    /// patch; the fifth pool (past the cap of four) gets nothing; my material in the band (the report's WetRequests) is left wet - a dark wet ring at its foot, a
    /// sheen running down its face, drips for its wet time; the first touch soaks (the shared action, 4 s), the return touch = a lighter splash crown (x0.5); a shot
    /// taken = a pop of spray; the pollution gate is a field note only (no picture). Home: the water sinks into the 2 m ring and runs down into the ground (CSV 끝:
    /// 아래로 흘러내림), a wet ring left on the earth.</summary>
    public sealed class Vol2FsUng : Vol2FsBackRing
    {
        static readonly Color Teal = new Color(0.35f, 0.85f, 1f);
        Mesh _waterMesh, _foamMesh, _inkMesh, _wetMesh, _colMesh; readonly Vol2FsStrips _col = new Vol2FsStrips();
        readonly Vol2FsStrips _water = new Vol2FsStrips(), _foam = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _wet = new Vol2FsStrips();
        ParticleSystem _spray, _drops, _mist, _dirt;
        float _homeAt = -1f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            // r3: the clear-water mode (Vol2FsB1.Water) read as a thin line on the dark ground in r1 / r2 - a tinted foaming teal body instead
            var body = M("fs_water", new Color(0.32f, 0.78f, 0.88f), 1.5f, 0.45f); body.NoiseAmt = 0.45f; body.Soft = 0.12f; body.Erode = 0.18f; body.Scroll = new Vector2(-1.5f, 0.2f);
            Vol2Fs.MeshObject(root, "WashBand", Kit.Mat(body), out _waterMesh);
            var col = M("fs_splash", new Color(0.55f, 0.88f, 0.95f), 1.6f, 0.55f); col.NoiseAmt = 0.5f; col.Soft = 0.1f; col.Erode = 0.25f; col.Scroll = new Vector2(0f, 1.8f);
            Vol2Fs.MeshObject(root, "WashColumns", Kit.Mat(col), out _colMesh);
            var foam = M("fs_streak", new Color(0.9f, 1f, 1f), 2.6f, 0.9f); foam.NoiseAmt = 0.35f; foam.Soft = 0.05f; foam.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "WashFoam", Kit.Mat(foam), out _foamMesh);
            Vol2Fs.MeshObject(root, "WashInk", Kit.InkSolid("fs_stroke2", InkC, Teal * 1.2f, 0.1f, 0.45f, 0.32f, 1.3f), out _inkMesh);
            var wet = M("fs_wet", new Color(0.1f, 0.16f, 0.2f, 1f), 1f, 0f); wet.Opacity = 0.6f; wet.NoiseAmt = 0.3f; wet.Soft = 0.2f;
            Vol2Fs.MeshObject(root, "WetGround", Kit.Mat(wet), out _wetMesh);
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.15f, 0.35f, "fs_splash"), Life0 = 0.3f, Life1 = 0.7f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.06f, Size1 = 0.17f, Gravity = 1.8f, Drag = 0.6f, Max = 1500 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.7f, 0.95f, 1f), 2.6f, 0.75f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.025f, Size1 = 0.06f, Gravity = 2.4f, Drag = 0.3f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 1200 }, root, "Drops");
            // r2: r1 mist read as white popcorn balls (the batch 5 r2 mistake) - fine, thin, teal, low-opacity veils instead
            var mist = M("fs_puff", new Color(0.55f, 0.82f, 0.92f, 1f), 0.8f, 0.2f); mist.Opacity = 0.18f; mist.NoiseAmt = 0.7f; mist.Erode = 0.45f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 0.6f, Speed1 = 1.8f, Size0 = 0.18f, Size1 = 0.38f, SizeStart = 0.6f, SizePeak = 1f, SizeEnd = 1.4f, Gravity = -0.2f, Drag = 1.0f, Stretch = true, StretchVel = 0.12f, StretchLen = 1.6f, Max = 300 }, root, "Mist");
            var dirt = M("fs_ink", new Color(0.12f, 0.05f, 0.15f, 1f), 1f, 0f); dirt.NoiseAmt = 0.4f; dirt.Erode = 0.35f; dirt.Soft = 0.1f;
            _dirt = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dirt), Life0 = 0.5f, Life1 = 1.0f, Speed0 = 1.5f, Speed1 = 4.5f, Size0 = 0.05f, Size1 = 0.14f, Gravity = 1.6f, Drag = 0.6f, Tiles = 2, AlphaHoldTo = 0.3f, Max = 400 }, root, "Corruption");
            foreach (var ps in new[] { _spray, _drops, _mist, _dirt }) ps.Play(false);
        }

        protected override void OnTurn()
        {
            float t = Now(NowUs), r = RingNow(t);
            for (int k = 0; k < 24; k++) { float a = k * Vol2ReturnGeometry.Tau / 24f; if (Front(a) < 0.3f) continue; for (int i = 0; i < 4; i++) Vol2FsB1.EmitV(_spray, RingPoint(a, r) + Vector3.up * 0.15f, (Dir(a) * 1.5f + Vector3.up * 2f + Random.insideUnitSphere).normalized * Random.Range(2f, 5f), -1f, -1f); }
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_spray, c + Random.insideUnitSphere * 0.4f, (Random.onUnitSphere + Vector3.up * 0.9f).normalized * Random.Range(2f, 6f), -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 25; k++) Vol2FsB1.EmitV(_spray, c + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.8f).normalized * Random.Range(1.5f, 4.5f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_spray, s.Pos, Random.onUnitSphere * Random.Range(1.5f, 5f), -1f, -1f); }

        protected override void OnCleanse(Patch p)
        {
            // the corruption boils away: dark droplets flung up and fading, a column of clean white mist, spray where the band passed
            Vector3 c = p.Centre;
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_dirt, c + new Vector3(Random.Range(-1f, 1f), 0.05f, Random.Range(-1f, 1f)) * p.Radius, (Vector3.up * 1.4f + Random.insideUnitSphere).normalized * Random.Range(1.5f, 4.5f), -1f, -1f);
            for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_mist, c + new Vector3(Random.Range(-1f, 1f), 0.1f, Random.Range(-1f, 1f)) * p.Radius * 0.8f, Vector3.up * Random.Range(1f, 2.2f) + Random.insideUnitSphere * 0.3f, -1f, -1f);
            // r2: the wash at the band: a thin fountain of clear water bursting up out of the pool and a broom of spray thrown outward with the band
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_drops, c + Random.insideUnitSphere * p.Radius * 0.5f, (Vector3.up * 2.4f + Random.insideUnitSphere * 0.5f).normalized * Random.Range(3f, 6.5f), -1f, -1f);
            Vector3 outw = FlatN(c - Apex);
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_spray, c + Random.insideUnitSphere * p.Radius * 0.6f, (Vector3.up * 1.2f + outw * 1.2f + Random.insideUnitSphere * 0.6f).normalized * Random.Range(2f, 5.5f), -1f, -1f);
            FlashLight(c + Vector3.up * 0.6f, 2.5f, Now(NowUs) + 0.25f);
            HitStop(Now(NowUs), 0.05f);
        }

        protected override void OnWet(Patch p)
        {
            var dims = Vol2FsB7.StoneDims(p.Radius);
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_spray, p.Centre + Vector3.up * Random.Range(0.1f, dims.y), (Random.onUnitSphere + Vector3.up * 0.5f).normalized * Random.Range(1.5f, 3.5f), -1f, -1f);
        }

        protected override void OnHome() { _homeAt = Now(NowUs); }

        void Band(float t, float r, float life, Vector3 cam, bool back)
        {
            int n = 72; var pts = new Vector3[n + 1]; var ww = new float[n + 1]; var aw = new float[n + 1]; var sd = new Vector3[n + 1];
            var fp = new Vector3[n + 1]; var fw = new float[n + 1];
            float lead = back ? -1f : 1f;
            for (int k = 0; k <= n; k++)
            {
                float a = k / (float)n * Vol2ReturnGeometry.Tau, fr = Front(a);
                float wave = 0.06f * Mathf.Sin(a * 9f + t * 11f);
                pts[k] = RingPoint(a, r) + Vector3.up * (0.08f + 0.05f * Mathf.Sin(a * 6f - t * 9f)); ww[k] = 0.85f + wave;      // r3: ~0.8 m of water aw[k] = life * (0.3f + 0.7f * fr); sd[k] = Dir(a);
                fp[k] = RingPoint(a, r + lead * 0.42f) + Vector3.up * (0.4f + 0.05f * Mathf.Sin(a * 7f + t * 10f)); fw[k] = 0.11f;
            }
            _water.Strip(pts, ww, sd, Color.white, aw, 0.37f, cam, 0f, 6f);
            _foam.Strip(fp, fw, null, Color.white, aw, 0.71f, cam);
            // r2: the leading lip - a low standing wall of foaming water (camera-facing) on the edge the band moves toward, and an inner foam rim
            var lp = new Vector3[n + 1]; var lw = new float[n + 1]; var ip2 = new Vector3[n + 1]; var iw2 = new float[n + 1];
            for (int k = 0; k <= n; k++)
            {
                float a = k / (float)n * Vol2ReturnGeometry.Tau;
                lp[k] = RingPoint(a, r + lead * 0.4f) + Vector3.up * (0.21f + 0.06f * Mathf.Sin(a * 7f + t * 10f)); lw[k] = 0.42f;
                ip2[k] = RingPoint(a, r - lead * 0.42f) + Vector3.up * 0.1f; iw2[k] = 0.07f;
            }
            _water.Strip(lp, lw, null, Color.white, aw, 0.61f, cam, 0f, 6f);
            _foam.Strip(ip2, iw2, null, Color.white, aw, 0.33f, cam);
            // two ink swirls riding the band
            for (int s = 0; s < 2; s++)
            {
                int m = 20; var ip = new Vector3[m]; var iw = new float[m]; var ia = new float[m];
                float a0 = s * Mathf.PI + 1.8f * Mathf.Max(0f, t - LaunchAt);
                for (int i = 0; i < m; i++)
                {
                    float u = i / (float)(m - 1), a = a0 + u * 2.2f;
                    ip[i] = RingPoint(a, r + Mathf.Sin(u * Mathf.PI * 2f + s) * Thick * 0.35f) + Vector3.up * 0.12f; iw[i] = 0.07f * Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)) + 0.01f; ia[i] = life * Front(a);
                }
                _ink.Strip(ip, iw, null, Color.white, ia, 0.25f + s * 0.4f, cam);
            }
        }

        protected override void DrawRing(float t, float r, float k01, bool back, float life)
        {
            var cam = CamPos;
            _water.Clear(); _foam.Clear(); _ink.Clear(); _wet.Clear(); _col.Clear();
            // r3: each wiped pool = a splash column 1.3 m tall that stands ~0.6 s where the pool lay, spraying, then the clean wet patch
            foreach (var p in Cleansed)
            {
                float a = t - p.At; if (a < 0f || a > 0.75f) continue;
                float h = 1.35f * Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.12f)) * (1f - 0.4f * Vol2Fs.Smooth(0.45f, 0.75f, a)), lf = 1f - Vol2Fs.Smooth(0.5f, 0.75f, a);
                int m = 8; var cp = new Vector3[m]; var cw = new float[m]; var ca = new float[m];
                for (int i = 0; i < m; i++) { float u = i / (float)(m - 1); cp[i] = p.Centre + Vector3.up * (0.02f + h * u) + Vector3.Cross(Vector3.up, FlatN(cam - p.Centre)) * 0.06f * Mathf.Sin(u * 6f + t * 20f); cw[i] = (p.Radius * 1.3f) * (1f - 0.65f * u) + 0.1f * Mathf.Max(0f, Mathf.Sin(u * 9f + t * 15f)); ca[i] = lf * (1f - 0.5f * u); }
                _col.Strip(cp, cw, null, Color.white, ca, 0.3f + p.Id % 5 * 0.1f, cam, 0f, 2f);
                var fp2 = new Vector3[m]; var fw2 = new float[m]; for (int i = 0; i < m; i++) { fp2[i] = cp[i]; fw2[i] = cw[i] * 0.3f; }
                _foam.Strip(fp2, fw2, null, Color.white, ca, 0.5f, cam);
                if (lf > 0.05f && Time.frameCount % 2 == 0) for (int i = 0; i < 4; i++) Vol2FsB1.EmitV(_spray, p.Centre + Vector3.up * h * Random.Range(0.5f, 1f), (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(1.5f, 3.5f), -1f, -1f);
            }
            float sink = _homeAt >= 0f ? Vol2Fs.Smooth(_homeAt, _homeAt + 0.5f, t) : 0f;
            if (life > 0.01f && sink < 1f) Band(t, r, life * (1f - sink), cam, back);
            // spray off the moving front arc
            if (life > 0.01f && !IsHome && Time.frameCount % 2 == 0)
                for (int k = 0; k < 8; k++) { float a = Random.Range(-1.6f, 1.6f); Vol2FsB1.EmitV(_spray, RingPoint(a, r) + Vector3.up * 0.1f, (Dir(a) * (back ? -1.2f : 1.5f) + Vector3.up * 1.4f + Random.insideUnitSphere * 0.4f).normalized * Random.Range(1.5f, 4f), -1f, -1f); }
            if (_homeAt >= 0f && t - _homeAt < 0.6f && Time.frameCount % 2 == 0)
                for (int k = 0; k < 6; k++) { float a = Random.Range(-1.6f, 1.6f); Vol2FsB1.EmitV(_drops, RingPoint(a, R0) + Vector3.up * 0.3f, Vector3.down * 1.5f, -1f, -1f); }
            // the ground it washed: wet where it went (a wide wet annulus fading slowly), clean wet patches where the pools were, my stone wet
            float wl = Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.3f, t) * (1f - Vol2Fs.Smooth(EndRel >= 0f ? EndRel + 2.5f : 99f, EndRel >= 0f ? EndRel + 3.5f : 100f, t));
            if (wl > 0.01f)
            {
                Vector3 fwd = FlatN(cam - Apex), right = Vector3.Cross(Vector3.up, fwd);
                foreach (var p in Cleansed) if (t >= p.At) _wet.Quad(new Vector3(p.Centre.x, Ground + 0.02f, p.Centre.z), right, fwd, p.Radius * 2.6f, new Color(1f, 1f, 1f, wl * Vol2Fs.Smooth(p.At, p.At + 0.3f, t)), 0.3f + p.Id % 7 * 0.1f);
                foreach (var p in WetMine)
                {
                    float a = t - p.At; if (a < 0f) continue;
                    var dims = Vol2FsB7.StoneDims(p.Radius);
                    float wetL = Vol2Fs.Smooth(0f, 0.3f, a) * (1f - Vol2Fs.Smooth(4f, 5f, a));
                    _wet.Quad(new Vector3(p.Centre.x, Ground + 0.025f, p.Centre.z), right, fwd, dims.x * 2.6f, new Color(1f, 1f, 1f, wetL), 0.6f);
                    // a sheen running down the stone's face toward the camera
                    Vector3 face = p.Centre + FlatN(cam - p.Centre) * (dims.z * 0.55f);
                    for (int s = 0; s < 3; s++)
                    {
                        Vector3 top = face + right * (s - 1) * dims.x * 0.28f + Vector3.up * dims.y * (0.95f - 0.1f * s), bot = face + right * (s - 1) * dims.x * 0.3f + Vector3.up * 0.05f;
                        float run = Mathf.Repeat(a * 0.6f + s * 0.33f, 1f);
                        _foam.Strip(new[] { top, Vector3.Lerp(top, bot, run), bot }, new[] { 0.015f, 0.03f, 0.015f }, null, Color.white, new[] { wetL * 0.3f, wetL * 0.8f, wetL * 0.3f }, 0.2f + s * 0.3f, cam);
                    }
                    if (wetL > 0.05f && Time.frameCount % 4 == 0) Vol2FsB1.EmitV(_drops, face + Vector3.up * Random.Range(0.1f, dims.y * 0.9f) + right * Random.Range(-0.5f, 0.5f) * dims.x, Vector3.down * 0.4f, -1f, -1f);
                }
            }
            _water.Write(_waterMesh); _foam.Write(_foamMesh); _ink.Write(_inkMesh); _wet.Write(_wetMesh); _col.Write(_colMesh);
        }
    }
}
