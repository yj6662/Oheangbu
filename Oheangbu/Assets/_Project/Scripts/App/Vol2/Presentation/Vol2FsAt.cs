using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>앗 (water, ㅏ path, 가른다) - free showcase batch 6, concept at_b (a tall thin crescent blade of clear water flung from the brush skims along the path
    /// edge-first, ink swirling inside it, cutting the wall in two - water sheeting down the cut faces - and slashing across the bear, its armour flying off in a
    /// splash; it drips, soaked). Executor facts only: the run = the upright water crescent racing along the path at the visible run speed (a foam-white edge,
    /// ink threads inside, spray thrown off both sides of its base, water running in the gouge behind it), the split walls part where it passes with water
    /// pouring down their cut faces, at the impact clock the crescent slashes across the first body and bursts into a crown of splash, the strip book opens the
    /// seam (3 s), the body action soaks it (4 s, drips; CSV 끝: 아래로 흘러내림).</summary>
    public sealed class Vol2FsAt : Vol2FsCutPath
    {
        static readonly Color Teal = new Color(0.35f, 0.85f, 1f);
        const float BladeH = 3.0f;      // r2: was 2.3 (a sliver from the shoulder)
        ParticleSystem _spray, _drops, _mist;
        Mesh _bladeMesh, _foamMesh, _inkMesh; readonly Vol2FsStrips _blade = new Vol2FsStrips(), _foam = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        float _burstAt = -1f; Vector3 _burstPos;
        readonly List<(Cleave C, float T)> _falls = new List<(Cleave, float)>();
        protected override Color CutGlow => new Color(0.55f, 1.3f, 1.6f);
        protected override float CrackWidth => 0.24f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.45f, "fs_splash"), Life0 = 0.35f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.32f, Gravity = 1.8f, Drag = 0.6f, Max = 900 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.7f, 0.95f, 1f), 2.6f, 0.75f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.025f, Size1 = 0.06f, Gravity = 2.4f, Drag = 0.3f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 1200 }, root, "Drops");
            var mist = M("fs_puff", new Color(0.75f, 0.9f, 1f, 1f), 1f, 0.3f); mist.Opacity = 0.35f; mist.NoiseAmt = 0.5f; mist.Erode = 0.3f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.4f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.5f, Drag = 1.2f, Max = 200 }, root, "Mist");
            foreach (var ps in new[] { _spray, _drops, _mist }) ps.Play(false);
            Vol2Fs.MeshObject(root, "WaterBlade", Vol2FsB1.Water(Kit, 1.7f, 0.4f), out _bladeMesh);
            var foam = M("fs_streak", new Color(0.9f, 1f, 1f), 2.6f, 0.9f); foam.NoiseAmt = 0.3f; foam.Soft = 0.05f; foam.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "Foam", Kit.Mat(foam), out _foamMesh);
            Vol2Fs.MeshObject(root, "InkInside", Kit.InkSolid("fs_stroke2", InkC, Teal * 1.2f, 0.08f, 0.45f, 0.32f, 1.3f), out _inkMesh);
        }

        /// <summary>The crescent at a ground point: a tall arc in the plane of the path and up, bowed back (its leading edge forward), points: base .. top.</summary>
        void Crescent(Vector3 at, float s, float life, Vector3 cam, float wobble)
        {
            int n = 16; var pe = new Vector3[n]; var pb = new Vector3[n]; var w = new float[n]; var al = new float[n];
            Vector3 lat = Vector3.Cross(Vector3.up, F);
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)(n - 1);
                float bow = 0.75f * Mathf.Sin(u * Mathf.PI) * s;                        // the bow: the middle runs ahead, the tips trail
                // a canted crescent: it rises from lower-left to upper-right across the path so its face is seen from the shoulder camera
                Vector3 p = at + Vector3.up * BladeH * s * u + lat * (u - 0.45f) * 2.2f * s + F * (bow - 0.35f * s) + lat * Mathf.Sin(u * 9f + wobble) * 0.03f;
                float thick = Mathf.Sin(Mathf.Pow(u, 0.85f) * Mathf.PI) * 0.75f * s;    // the sheet's width behind the edge
                pe[k] = p; pb[k] = p - F * thick * 0.6f + Vector3.down * thick * 0.25f; w[k] = thick; al[k] = life;
            }
            // the sheet: a camera-facing band between the leading edge and its back
            var mid = new Vector3[n];
            for (int k = 0; k < n; k++) mid[k] = (pe[k] + pb[k]) * 0.5f;
            _blade.Strip(mid, w, null, Color.white, al, 0.37f, cam, 0f, 2f);
            var we = new float[n]; for (int k = 0; k < n; k++) we[k] = 0.05f + 0.05f * Mathf.Sin(k / (float)(n - 1) * Mathf.PI);
            _foam.Strip(pe, we, null, Color.white, al, 0.71f, cam);
            // two ink threads curling inside the sheet
            for (int j = 0; j < 2; j++)
            {
                var pi = new Vector3[n]; var wi = new float[n];
                for (int k = 0; k < n; k++) { float u = k / (float)(n - 1); pi[k] = Vector3.Lerp(pe[k], pb[k], 0.35f + 0.3f * j + 0.12f * Mathf.Sin(u * 7f + wobble * (j + 1))); wi[k] = 0.035f * Mathf.Sin(u * Mathf.PI); }
                _ink.Strip(pi, wi, null, Color.white, al, 0.2f + 0.4f * j, cam);
            }
        }

        protected override void Run(float t, float along, float prev)
        {
            Vector3 side = Vector3.Cross(Vector3.up, F);
            int n = Mathf.Max(1, Mathf.CeilToInt((along - prev) * 5f));
            for (int k = 0; k < n; k++)
            {
                Vector3 p = OnPath(Mathf.Lerp(prev, along, (k + 0.5f) / n)) + Vector3.up * 0.08f;
                for (int i = 0; i < 4; i++) Vol2FsB1.EmitV(_spray, p, side * (i % 2 == 0 ? 1f : -1f) * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(1f, 3.5f) - F * Random.Range(0f, 1f), -1f, -1f);
                if (k % 2 == 0) Vol2FsB1.EmitV(_drops, p + Vector3.up * Random.Range(0.2f, BladeH), -F * Random.Range(0.5f, 1.5f) + Random.insideUnitSphere, -1f, -1f);
            }
        }

        protected override void OnStruck(Struck s)
        {
            if (s.Kind != "Instant" || _burstAt >= 0f) return;
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            _burstAt = Now(NowUs); _burstPos = b.center;
            for (int k = 0; k < 160; k++) Vol2FsB1.EmitV(_spray, b.center + Random.insideUnitSphere * 0.4f, (Random.onUnitSphere + Vector3.up * 0.9f + F * 0.3f).normalized * Random.Range(2f, 7f), -1f, -1f);
            for (int k = 0; k < 120; k++) Vol2FsB1.EmitV(_drops, b.center, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(2f, 6f), -1f, -1f);
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_mist, b.center + Random.insideUnitSphere * 0.5f, Random.onUnitSphere * 0.8f, -1f, -1f);
            for (int k = 0; k < 12; k++) Vol2FsB1.EmitV(PInk, b.center, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(2f, 5f), -1f, -1f);
        }

        protected override void OnCleave(Cleave c) { _falls.Add((c, Now(NowUs))); for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_spray, c.Centre + Vector3.up * Random.Range(0.2f, 1f), c.Side * (k % 2 == 0 ? 1f : -1f) * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(0.5f, 2.5f), -1f, -1f); }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; _blade.Clear(); _foam.Clear(); _ink.Clear();
            float along = RunAlong(t);
            if (_burstAt < 0f && along > StartAlong)
            {
                float grow = Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.08f, t);
                Crescent(OnPath(along), grow, 0.95f, cam, t * 18f);
            }
            else if (_burstAt >= 0f)
            {
                // r2: the crescent passes on through the body and away (0.45 s), and a ghost of it stays where it cut (Phantom Blade principle 5)
                float a = t - _burstAt, life = 1f - Vol2Fs.Smooth(0.1f, 0.45f, a);
                if (life > 0.01f) Crescent(OnPath(AlongA) + F * 3.5f * a, 1f + 0.3f * a, life, cam, t * 18f);
                float ghost = 0.45f * (1f - Vol2Fs.Smooth(0.05f, 1.0f, a));
                if (ghost > 0.01f) Crescent(OnPath(AlongA - 0.2f), 1.05f, ghost, cam, _burstAt * 18f);
            }
            // water pours down the cut faces of the split walls for a moment
            foreach (var f in _falls)
            {
                float a = t - f.T; if (a < 0f || a > 1.4f || Time.frameCount % 2 != 0) continue;
                var c = f.C; float hgt = Vol2FsB6.WallDims(c.Radius).y;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 face = (k % 2 == 0 ? c.PosA + c.Side * c.HalfR * 0.7f : c.PosB - c.Side * c.HalfR * 0.7f) + Vector3.up * hgt * Random.Range(0.7f, 1f);
                    Vol2FsB1.EmitV(_drops, face, Vector3.down * Random.Range(0.5f, 1.5f), -1f, -1f);
                }
            }
            _blade.Write(_bladeMesh); _foam.Write(_foamMesh); _ink.Write(_inkMesh);
        }
    }
}
