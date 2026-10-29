using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>옷 (water, ㅗ fan, 가른다) - free showcase batch 6, concept ot_a (a broad thin sheet of water sweeps across the whole fan like a scythe blade, ink swirling
    /// in it, its leading edge a bright foaming rim, cutting a long arc into the ground; the wall sliced with water sheeting off the cut faces; both bears'
    /// armour washed off in a burst of spray, soaked). Executor facts only: the launch = the one cut: the water blade - a wide flat crescent sheet knee-high over
    /// the fan - sweeps from the right edge to the left (a presentation of the one tick), a white foaming rim on its leading edge, ink swirls inside, spray
    /// thrown off its outer edge; the gouges open behind it and run with water while the strips stand; the sheet then drops and splashes along the far edge;
    /// the split wall's faces stream water; the stripped bodies are soaked (the body action: 4 s of drips and a dark wet ground, then it runs down - CSV 끝).</summary>
    public sealed class Vol2FsOt : Vol2FsCutFan
    {
        static readonly Color Teal = new Color(0.45f, 0.85f, 0.95f);
        ParticleSystem _spray, _drops, _mist;
        Mesh _sheetMesh, _swirlMesh, _rimMesh; readonly Vol2FsStrips _sheet = new Vol2FsStrips(), _swirl = new Vol2FsStrips(), _rim = new Vol2FsStrips();
        bool _dropped;
        protected override Color CutGlow => new Color(0.5f, 1.2f, 1.5f);
        protected override int GougeCount => 6;
        protected override float SweepSeconds => 0.12f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.4f, "fs_splash"), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.08f, Size1 = 0.22f, Gravity = 1.6f, Drag = 0.6f, Max = 900 }, root, "Spray");      // r2: smaller (r1 white popcorn)
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.6f, 0.9f, 1f), 2.6f, 0.7f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.025f, Size1 = 0.06f, Gravity = 2.4f, Stretch = true, StretchVel = 0.06f, StretchLen = 1.2f, Max = 900 }, root, "Drops");
            var mist = M("fs_puff", new Color(0.75f, 0.88f, 0.92f, 1f), 1.2f, 0.3f); mist.Opacity = 0.35f; mist.NoiseAmt = 0.5f; mist.Erode = 0.3f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.4f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.5f, Drag = 1.3f, Max = 160 }, root, "Mist");
            foreach (var ps in new[] { _spray, _drops, _mist }) ps.Play(false);
            // r3: one smooth continuous water band (r2: the eroded water sheet read as a heap of teal particles)
            var wsh = M("fs_streak", new Color(0.5f, 0.86f, 0.98f), 1.7f, 0.55f); wsh.NoiseAmt = 0.1f; wsh.Erode = 0.06f; wsh.Soft = 0.35f; wsh.Scroll = new Vector2(-1.4f, 0f);
            Vol2Fs.MeshObject(root, "Sheet", Kit.Mat(wsh), out _sheetMesh);
            var sw = M("fs_swirl", InkC, 1f, 0f); sw.Edge = Teal * 1.2f; sw.EdgeWidth = 0.2f; sw.NoiseAmt = 0.35f; sw.Soft = 0.1f; sw.Opacity = 0.85f;
            Vol2Fs.MeshObject(root, "InkSwirl", Kit.Mat(sw), out _swirlMesh);
            var rim = M("fs_streak", Teal, 3f, 1f); rim.NoiseAmt = 0.35f; rim.Soft = 0.05f;
            Vol2Fs.MeshObject(root, "Foam", Kit.Mat(rim), out _rimMesh);
        }

        protected override void Sweep(float t, float k, float prevK)
        {
            // spray thrown off the sheet's outer edge and leading rim
            float ang = SweepAng(k);
            Vector3 outer = InFan(0.95f, ang, 0.5f), lead = InFan(0.6f, ang, 0.55f), tang = Dir(ang - Mathf.PI * 0.5f);
            for (int i = 0; i < 2; i++) Vol2FsB1.EmitV(_spray, Vector3.Lerp(lead, outer, Random.value), (tang * 3f + Dir(ang) * 2f + Vector3.up * 1.5f + Random.insideUnitSphere).normalized * Random.Range(2f, 6f), -1f, -1f);
            for (int i = 0; i < 4; i++) Vol2FsB1.EmitV(_drops, Vector3.Lerp(InFan(0.3f, ang, 0.5f), outer, Random.value), tang * Random.Range(2f, 5f) + Vector3.up * Random.Range(0.5f, 2.5f), -1f, -1f);
        }

        protected override void OnCleave(Cleave c)
        {
            for (int k = 0; k < 16; k++) Vol2FsB1.EmitV(_spray, c.Centre + Vector3.up * Random.Range(0.2f, 0.9f), c.Side * (k % 2 == 0 ? 1f : -1f) * Random.Range(1f, 3f) + Vector3.up * Random.Range(1f, 3f), -1f, -1f);
            Vol2FsB1.Emit(_mist, c.Centre + Vector3.up * 0.5f, 6);
        }

        protected override void OnStrip(Strip s)
        {
            var b = BodyNow(s);
            for (int k = 0; k < 16; k++) Vol2FsB1.EmitV(_spray, b.center + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.7f).normalized * Random.Range(2f, 6f), -1f, -1f);
            Vol2FsB1.Emit(_mist, b.center, 8);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            _sheet.Clear(); _swirl.Clear(); _rim.Clear();
            float since = t - LaunchAt, k = SweepK(t);
            // r2: the sheet is drawn in the sweep and LINGERS (r1: a flat band, gone in 0.4 s, read edge-on as nothing): tilted toward the camera, brighter,
            // a glowing teal blade edge round its outer rim, a fainter phantom sheet lagging above it (Phantom Blade principles 3 + 5)
            float hold = Linger(t, 0.15f, 0.9f);
            if (hold > 0.01f)
            {
                float a1 = SweepAng(k) - 0.04f, a0 = Half * 1.05f;
                float drop = Vol2Fs.Smooth(SweepSeconds + 0.3f, SweepSeconds + 1.0f, since);
                float y = 0.6f - 0.45f * drop;
                Crescent(_sheet, a0, a1, Depth * 0.64f, y, Depth * 0.62f, hold, 0.33f, 30, 0.08f, 0.5f);
                Crescent(_sheet, a0, a1 + 0.14f, Depth * 0.66f, y + 0.28f, Depth * 0.56f, hold * 0.3f, 0.57f, 30, 0.08f, 0.6f);
                Crescent(_swirl, a0, a1, Depth * 0.64f, y + 0.03f, Depth * 0.4f, hold * 0.8f, 0.61f, 30, 0.08f, 0.5f);
                Crescent(_rim, a0, a1, Depth * 0.95f, y + 0.02f, 0.26f, hold, 0.81f, 30, 0.08f, 0.4f);
                // r3: three bright current lines running round in the band (it reads as one sweeping stream, not a cloud)
                for (int i = 0; i < 3; i++) Crescent(_rim, a0, a1, Depth * (0.42f + 0.16f * i), y + 0.04f, 0.07f, hold * 0.75f, 0.2f + 0.2f * i, 30, 0.08f, 0.45f);
                var pts = new Vector3[8]; var wd = new float[8]; var al = new float[8];
                for (int i = 0; i < 8; i++)
                {
                    float u = i / 7f; pts[i] = InFan(Mathf.Lerp(0.33f, 0.96f, u), a1, y + 0.05f + 0.06f * Mathf.Sin(t * 30f + i));
                    wd[i] = 0.3f + 0.12f * Mathf.Sin(u * Mathf.PI); al[i] = hold;
                }
                _rim.Strip(pts, wd, null, Color.white, al, 0.71f, cam, 0f, 1f);
                if (since > SweepSeconds + 0.3f && !_dropped)
                {
                    _dropped = true;
                    // the sheet drops: a splash along the far edge and down its length
                    for (int i = 0; i < 18; i++) { float ang = Random.Range(-Half, Half); Vol2FsB1.EmitV(_spray, InFan(Random.Range(0.45f, 1f), ang, 0.2f), (Vector3.up * 2.5f + Random.insideUnitSphere * 1.5f), -1f, -1f); }
                    for (int i = 0; i < 3; i++) Vol2FsB1.EmitV(_mist, InFan(Random.Range(0.5f, 1f), Random.Range(-Half, Half), 0.3f), Vector3.up * 0.5f, -1f, -1f);
                }
            }
            // the gouges run with water while they are open (glints of drops sliding along them)
            float gl = GougeLife(t);
            if (gl > 0.2f && since > SweepSeconds && since < 3.5f && Time.frameCount % 2 == 0 && Gouges.Count > 0)
            {
                var g = Gouges[Random.Range(0, Gouges.Count)];
                Vol2FsB1.EmitV(_drops, Vector3.Lerp(g.A, g.B, Random.value) + Vector3.up * 0.03f, (g.B - g.A).normalized * 0.8f + Vector3.up * 0.4f, -1f, -1f);
            }
            _sheet.Write(_sheetMesh); _swirl.Write(_swirlMesh); _rim.Write(_rimMesh);
        }
    }
}