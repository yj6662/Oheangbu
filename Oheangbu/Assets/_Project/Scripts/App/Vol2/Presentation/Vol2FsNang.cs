using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>낭 (fire, ㅏ path, 돌아온다) - free showcase batch 7, concept nang_a (a spinning crescent of white-orange flame flies out along the path and curves back
    /// in a long loop, a ribbon of fire and ink smoke tracing the whole closed loop in the air; both bears burn where it passed twice; the shot bursts in embers).
    /// Executor facts only: the head = a white-hot comet-crescent at the executor's head; behind it a wide ribbon of flame (white core, orange body, a black
    /// ink-smoke underside) laid along the trail the head really flew - out on one lane, the U where the path stopped, back on the other - so the trip draws
    /// itself as a closed loop of fire in the air that lingers 1.1 s and burns out from its tail into rising ash (CSV 끝: 끝부터 타 재로 날림); flame tongues and
    /// embers lick off the ribbon; the first touch sets the body burning (the shared action, 2 s), the return touch = a smaller flare + embers (x0.5).</summary>
    public sealed class Vol2FsNang : Vol2FsBackPath
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.12f), Hot = new Color(1f, 0.85f, 0.55f);
        Mesh _coreMesh, _bodyMesh, _smokeMesh, _headMesh;
        readonly Vol2FsStrips _core = new Vol2FsStrips(), _body = new Vol2FsStrips(), _smoke = new Vol2FsStrips(), _head = new Vol2FsStrips();
        ParticleSystem _licks, _embers, _ash, _soot;
        // the whole loop as flown (kept for the lingering ribbon after the head's history drops it)
        readonly List<(Vector3 P, float T)> _flown = new List<(Vector3, float)>(256);
        protected override Color LoopGlow => new Color(1.6f, 0.6f, 0.2f);
        protected override float LoopTail => 1.4f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            var core = M("fs_streak", Hot * 1.4f, 3f, 1f); core.NoiseAmt = 0.3f; core.Soft = 0.03f; core.Erode = 0.12f; core.Scroll = new Vector2(-4f, 0f);
            Vol2Fs.MeshObject(root, "FireCore", Kit.Mat(core), out _coreMesh);
            var body = M("fs_flame", Orange, 1.8f, 0.85f); body.NoiseAmt = 0.6f; body.Erode = 0.3f; body.Soft = 0.1f; body.Scroll = new Vector2(-2.5f, 0.3f);
            Vol2Fs.MeshObject(root, "FireBody", Kit.Mat(body), out _bodyMesh);
            Vol2Fs.MeshObject(root, "FireSmoke", Kit.InkSolid("fs_stroke2", InkC, Orange * 1.4f, 0.16f, 0.55f, 0.35f, 1.3f), out _smokeMesh);
            var head = M("fs_streak", new Color(1f, 0.55f, 0.2f) * 1.6f, 2.4f, 1f);      // r3: was Hot x1.8 (read as a white paper crescent) head.NoiseAmt = 0.2f; head.Soft = 0.03f;
            Vol2Fs.MeshObject(root, "FireHead", Kit.Mat(head), out _headMesh);
            _licks = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick()), 0.18f, 0.4f, 0.3f, 0.6f, 1.4f, 900), root, "Licks");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.55f, 0.2f), 4f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.02f, Size1 = 0.05f, Gravity = -0.4f, Drag = 0.8f, Noise = 1f, NoiseFreq = 1.5f, Max = 900 }, root, "Embers");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.0f, Life1 = 2.0f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.04f, Size1 = 0.1f, Gravity = -0.3f, Noise = 0.8f, NoiseFreq = 1.2f, Spin = 200f, Max = 500 }, root, "Ash");
            _soot = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot()), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.4f, Size1 = 0.8f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.5f, Gravity = -0.15f, Drag = 1f, Max = 200 }, root, "Soot");
            foreach (var ps in new[] { _licks, _embers, _ash, _soot }) ps.Play(false);
        }

        protected override void Run(float t, float along, float prev, bool back)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(along - prev) * 2.5f), 1, 14);
            for (int k = 0; k < n; k++)
            {
                Vector3 p = TripPoint(Mathf.Lerp(prev, along, (k + 0.5f) / n), back);
                Vol2FsB1.EmitV(_licks, p + Random.insideUnitSphere * 0.15f, Vector3.up * Random.Range(0.6f, 1.6f), -1f, -1f);
                Vol2FsB1.EmitV(_embers, p, Random.insideUnitSphere * 2f + Vector3.up, -1f, -1f);
                if (k % 3 == 0) Vol2FsB1.EmitV(_soot, p + Vector3.down * 0.2f, Vector3.up * 0.3f, -1f, -1f);
            }
        }

        protected override void OnTurn()
        {
            Vector3 p = HeadPos(Now(NowUs));
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_embers, p, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f);
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_licks, p + Random.insideUnitSphere * 0.4f, Vector3.up * Random.Range(1f, 2.5f), -1f, -1f);
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 24; k++) Vol2FsB1.EmitV(_licks, c + Random.insideUnitSphere * 0.4f, Vector3.up * Random.Range(1f, 3f) + Random.insideUnitSphere, -1f, -1f);
            for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_embers, c, Random.onUnitSphere * Random.Range(2f, 7f), -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_licks, c + Random.insideUnitSphere * 0.3f, Vector3.up * Random.Range(0.8f, 2f), -1f, -1f);
            for (int k = 0; k < 35; k++) Vol2FsB1.EmitV(_embers, c, Random.onUnitSphere * Random.Range(1.5f, 4.5f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_embers, s.Pos, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f); }

        protected override void DrawHead(float t, Vector3 pos, Vector3 dir, bool back, float life)
        {
            var cam = CamPos;
            if (life > 0.01f && (_flown.Count == 0 || (_flown[_flown.Count - 1].P - pos).sqrMagnitude > 1e-6f)) _flown.Add((pos, t));
            _core.Clear(); _body.Clear(); _smoke.Clear(); _head.Clear();
            float loop = LoopLife(t);
            if (_flown.Count >= 2 && loop > 0.01f)
            {
                // burns out from its tail after home: the oldest part goes first, each point rising ash as it goes
                float burn = IsHome ? Mathf.Clamp01((t - HomeAt) / LoopTail) : 0f;
                int from = Mathf.Min(_flown.Count - 2, Mathf.FloorToInt(burn * (_flown.Count - 1)));
                var pts = new List<Vector3>(); var wc = new List<float>(); var wb = new List<float>(); var ws = new List<float>(); var ac = new List<float>(); var ab = new List<float>();
                for (int k = from; k < _flown.Count; k++)
                {
                    float age = t - _flown[k].T, u = (k - from) / Mathf.Max(1f, _flown.Count - 1 - from);
                    float wave = Mathf.Sin(k * 0.9f + t * 12f) * 0.06f;
                    pts.Add(_flown[k].P + Vector3.up * wave);
                    float fresh = 1f - Vol2Fs.Smooth(0.1f, 1.0f, age);
                    wc.Add(0.12f * (0.3f + 0.7f * fresh)); wb.Add(0.9f * (0.5f + 0.5f * fresh) * Mathf.Max(0.2f, u)); ws.Add(0.42f);
                    ac.Add(loop * (0.35f + 0.65f * fresh)); ab.Add(loop * (0.5f + 0.5f * fresh));
                }
                if (pts.Count >= 2)
                {
                    _body.Strip(pts, wb, null, Color.white, ab, 0.33f, cam);
                    _core.Strip(pts, wc, null, Color.white, ac, 0.61f, cam);
                    var low = new List<Vector3>(); foreach (var p in pts) low.Add(p + Vector3.down * 0.22f);
                    _smoke.Strip(low, ws, null, Color.white, ab, 0.12f, cam);
                }
                if (IsHome && from > 0 && Time.frameCount % 2 == 0) for (int k = 0; k < 4; k++) Vol2FsB1.EmitV(_ash, _flown[Mathf.Min(_flown.Count - 1, from + k)].P, Vector3.up * Random.Range(0.5f, 1.4f), -1f, -1f);
            }
            if (life > 0.01f)
            {
                // the head: a crescent of white-hot flame facing its travel, spinning a little
                Vector3 a = Vector3.Cross(dir, Vector3.up).normalized;
                float spin = t * 20f;
                Vector3 up = Quaternion.AngleAxis(spin * Mathf.Rad2Deg * 0.15f, dir) * Vector3.up;
                Vol2FsB6.Slash(_head, pos - a * 0.85f + up * 0.25f - dir * 0.25f, pos + a * 0.85f + up * 0.25f - dir * 0.25f, dir * 0.5f + up * 0.2f, 0.6f, 1f, life, 0.5f, cam);
                Vol2FsB6.Slash(_head, pos - up * 0.5f - dir * 0.1f, pos + up * 0.5f - dir * 0.1f, dir * 0.3f, 0.3f, 1f, life * 0.8f, 0.8f, cam);
                if (Time.frameCount % 1 == 0) Vol2FsB1.EmitV(_licks, pos + Random.insideUnitSphere * 0.2f, -dir * 2f + Vector3.up, -1f, -1f);
            }
            _core.Write(_coreMesh); _body.Write(_bodyMesh); _smoke.Write(_smokeMesh); _head.Write(_headMesh);
        }
    }
}
