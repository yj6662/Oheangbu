using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>갓 (wood, ㅏ path, 가른다) - free showcase batch 6, concept gat_b (a row of razor jade leaf-blades bursts up out of the ground one after another along
    /// the path like a running saw, the stone wall split down the middle by them, the last blades slicing up across the bear and flinging its crystal armour off;
    /// vines wrap the exposed seam). Executor facts only: the run = the visible cut (the first body at the SingleCue impact clock), every 0.3 m it passes a jade
    /// blade springs up out of the gouge and sinks again (the saw runs), the walls the executor split part at the run (Vol2FsCut's halves, jade sparks and leaves
    /// out of the cut), the Instant on the first body = a cluster of tall blades slicing up across it + a jade crescent, the strip book opens the seam (3 s) and
    /// the body action binds it (vines 2 s -> rootlets reaching forward: CSV 끝).</summary>
    public sealed class Vol2FsGat : Vol2FsCutPath
    {
        static readonly Color Jade = new Color(0.4f, 1f, 0.5f);
        // r2: taller, wider, a zigzag row, and they stand ~1 s (r1: 0.5 - 1.3 m blades gone in 0.9 s, the row stacked behind the caster's head)
        const float Step = 0.28f, Hold = 0.95f, Sink = 0.6f;
        ParticleSystem _leaves, _sparks, _dust;
        Material _blade;
        Mesh _spike, _slashMesh; readonly Vol2FsStrips _slash = new Vol2FsStrips();
        float _nextAt;
        sealed class Blade { public Transform T; public Vector3 Pos; public Quaternion Rot; public float H, W, Born, Rise; public bool Big; }
        readonly List<Blade> _blades = new List<Blade>();
        readonly List<(Vector3 A, Vector3 B, Vector3 Bow, float At)> _cuts = new List<(Vector3, Vector3, Vector3, float)>();
        protected override Color CutGlow => new Color(0.45f, 1.5f, 0.55f);
        protected override float CrackWidth => 0.24f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _blade = Kit.Metal(1.15f, 0f, new Color(0.05f, 0.28f, 0.1f), new Color(0.5f, 1f, 0.62f)) ?? Kit.Bark(new Color(0.5f, 1f, 0.6f), Jade * 2f);
            _spike = Kit.SharedMesh("gatSpike", () => Vol2FsB6.Spike(0.26f, 0.06f, 0.3f, 0.12f));
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.7f, Speed0 = 1.5f, Speed1 = 5f, Size0 = 0.1f, Size1 = 0.24f, Gravity = 0.4f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 600 }, root, "Leaves");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4.5f, 1f)), Life0 = 0.3f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 7f, Size0 = 0.025f, Size1 = 0.06f, Drag = 1.3f, Gravity = 0.4f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 700 }, root, "Sparks");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.46f, 0.33f, 0.26f, 1f), 0.45f)), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 0.3f, Speed1 = 1.4f, Size0 = 0.2f, Size1 = 0.45f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.5f, Drag = 1.3f, Max = 200 }, root, "Dust");
            foreach (var ps in new[] { _leaves, _sparks, _dust }) ps.Play(false);
            var sl = M("fs_streak", Jade, 3.2f, 1f); sl.NoiseAmt = 0.15f; sl.Soft = 0.02f; sl.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "JadeSlash", Kit.Mat(sl), out _slashMesh);
            _jade = Vol2Fs.PointLight(root, "JadeLight", Jade, 5f);
        }

        void Spawn(Vector3 at, Quaternion rot, float h, float w, float born, bool big)
        {
            var r = Vol2FsB1.Prop(Root.transform, "Blade" + _blades.Count, _spike, _blade, false);
            var b = new Blade { T = r.transform, Pos = at, Rot = rot, H = h, W = w, Born = born, Rise = big ? 0.09f : 0.07f, Big = big };
            b.T.SetPositionAndRotation(at, rot); b.T.localScale = new Vector3(w, 0.01f, w);
            _blades.Add(b);
        }

        protected override void OnLaunch(long nowUs) { base.OnLaunch(nowUs); _nextAt = StartAlong + 0.15f; }

        protected override void Run(float t, float along, float prev)
        {
            Vector3 side = Vector3.Cross(Vector3.up, F);
            while (_nextAt <= along && _nextAt < AlongA - 0.5f)
            {
                int k = _blades.Count;
                float u = Mathf.Clamp01(_nextAt / Mathf.Max(1f, AlongA));
                float h = Mathf.Lerp(1.0f, 2.1f, u) * (0.8f + 0.4f * Vol2Fs.Hash(Seed, k));
                float zig = k % 2 == 0 ? 1f : -1f;
                float lean = 16f + 12f * Vol2Fs.Hash(Seed, k + 30), tilt = zig * (14f + 12f * Vol2Fs.Hash(Seed, k + 60));
                Vector3 at = OnPath(_nextAt) + side * zig * (0.22f + 0.18f * Vol2Fs.Hash(Seed, k + 90));
                var rot = Quaternion.AngleAxis(tilt, F) * Quaternion.AngleAxis(lean, side) * Quaternion.LookRotation(Vector3.Cross(side, Vector3.up), Vector3.up);
                Spawn(at, rot, h, 1.4f + 0.6f * Vol2Fs.Hash(Seed, k + 120), t, false);
                for (int i = 0; i < 6; i++) Vol2FsB1.EmitV(_sparks, at + Vector3.up * Random.Range(0.1f, h), (Random.insideUnitSphere + Vector3.up * 0.5f + F * 0.4f).normalized * Random.Range(2f, 6f), -1f, -1f);
                if (k % 2 == 0) { Vol2FsB1.Emit(_leaves, at + Vector3.up * h * 0.6f, 3); Vol2FsB1.EmitV(_dust, at + Vector3.up * 0.1f, side * (k % 4 == 0 ? 1f : -1f) * 0.8f + Vector3.up * 0.4f, -1f, -1f); }
                if (k % 3 == 0) Vol2FsB1.EmitV(PInk, at + Vector3.up * h * 0.5f, (side * (k % 2 == 0 ? 1f : -1f) * 2f + Vector3.up * 1.5f), -1f, -1f);
                _nextAt += Step;
            }
        }

        protected override void OnStruck(Struck s)
        {
            if (s.Kind != "Instant") return;
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            Vector3 feet = G(FeetOf(s)), side = Vector3.Cross(Vector3.up, F), toCam = FlatN(CamPos - b.center);
            float t = Now(NowUs);
            // the last blades: a fan of tall ones bursting up through the body's front, slicing up across it
            for (int k = 0; k < 7; k++)
            {
                float v = (k / 6f - 0.5f) * 2f;
                Vector3 at = feet + toCam * (Mathf.Max(b.extents.x, b.extents.z) * 0.7f) + side * v * (b.extents.x + 0.25f);
                float h = (2.5f + 0.9f * Vol2Fs.Hash(Seed, k + 400)) * (1f - 0.25f * Mathf.Abs(v));
                var rot = Quaternion.AngleAxis(-v * 26f, F) * Quaternion.AngleAxis(-12f - 10f * Vol2Fs.Hash(Seed, k + 410), side) * Quaternion.LookRotation(Vector3.Cross(side, Vector3.up), Vector3.up);
                Spawn(at, rot, h, 2.0f + 0.7f * Vol2Fs.Hash(Seed, k + 420), t + 0.012f * k, true);
            }
            if (_jade != null) { _jade.transform.position = b.center + Vector3.up * 0.8f; _jadeUntil = t + 1.2f; }
            Vol2FsB1.Emit(_leaves, b.center, 90); Vol2FsB1.Emit(_sparks, b.center, 160);
            for (int k = 0; k < 18; k++) Vol2FsB1.EmitV(PInk, b.center, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(3f, 7f), -1f, -1f);
            // the jade crescent: thin-to-wide up across the body
            Vector3 c = b.center + toCam * (Mathf.Max(b.extents.x, b.extents.z) + 0.2f);
            _cuts.Add((c - side * 1.8f - Vector3.up * 0.9f, c + side * 1.5f + Vector3.up * 2.2f, -side * 0.4f + toCam * 0.25f, t));
        }
        Light _jade; float _jadeUntil = -1f;

        protected override void OnCleave(Cleave c)
        {
            Vector3 at = c.Centre + Vector3.up * 0.5f;
            Vol2FsB1.Emit(_leaves, at, 30); Vol2FsB1.Emit(_sparks, at, 60);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            foreach (var b in _blades)
            {
                float a = t - b.Born;
                if (a < 0f) { b.T.gameObject.SetActive(false); continue; }
                float rise = Vol2Fs.EaseOutBack(Mathf.Clamp01(a / b.Rise)), hold = b.Big ? Hold + 0.5f : Hold;
                float sink = Vol2Fs.Smooth(hold, hold + Sink, a);
                float hNow = b.H * Mathf.Max(0.01f, rise) * (1f - 0.85f * sink);
                b.T.gameObject.SetActive(sink < 0.99f);
                b.T.SetPositionAndRotation(b.Pos + Vector3.down * 0.25f * sink * b.H, b.Rot);
                b.T.localScale = new Vector3(b.W, hNow, b.W);
            }
            _slash.Clear();
            foreach (var c in _cuts)
            {
                float a = t - c.At; if (a < 0f || a > 1.2f) continue;
                // fast (0.06 s) -> held -> a slow lingering afterimage (Phantom Blade principle 3)
                float reveal = Vol2Fs.Smooth(0f, 0.06f, a), life = 1f - Vol2Fs.Smooth(0.25f, 1.2f, a);
                Vol2FsB6.Slash(_slash, c.A, c.B, c.Bow, 0.5f, reveal, life, 0.33f, cam);
                Vol2FsB6.Slash(_slash, c.A - c.Bow * 0.35f, c.B - c.Bow * 0.35f, c.Bow, 0.42f, Vol2Fs.Smooth(0.03f, 0.1f, a), life * 0.4f, 0.66f, cam);      // the phantom
                Vol2FsB6.Slash(_slash, c.A - c.Bow * 0.7f, c.B - c.Bow * 0.7f, c.Bow, 0.36f, Vol2Fs.Smooth(0.06f, 0.14f, a), life * 0.2f, 0.81f, cam);
            }
            _slash.Write(_slashMesh);
            if (_jade != null) _jade.intensity = t < _jadeUntil ? 4f * (_jadeUntil - t) / 1.2f : 0f;
        }
    }
}
