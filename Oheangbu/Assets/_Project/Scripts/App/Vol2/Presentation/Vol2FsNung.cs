using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>눙 (fire, ㅜ ring, 돌아온다) - free showcase batch 7, concept nung_b (a ring of small flame spirits dances outward to 4 m and back in, each trailing a curling
    /// tail of fire and ink). Executor facts only: eight comet spirits (a white-hot head, licking flame) ride the band's radius at its angle (the report's legs: 2 m ->
    /// 4 m and back), circling slowly as they go; each tail is drawn through where its head really was over the last 0.35 s (radius from the trip's own clock), so
    /// the trails curl outward as spirals going out and fold back inward coming home; flame core + orange body + an ink-smoke underside; embers fall off; the
    /// first touch sets the body burning (the shared action, 2 s), the return touch = a smaller flare (x0.5); a shot taken bursts in embers; the turn = every
    /// spirit flares. Home: the spirits sink into the 2 m ring and burn out from their tails into rising ash (CSV 끝: 끝부터 타 재로 날림).</summary>
    public sealed class Vol2FsNung : Vol2FsBackRing
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.12f), Hot = new Color(1f, 0.85f, 0.55f);
        const int Spirits = 8; const float Spin = 0.9f, TailSpan = 0.6f;      // r2: was 0.35 (short tails: the spiral did not read)
        Mesh _coreMesh, _bodyMesh, _smokeMesh;
        readonly Vol2FsStrips _core = new Vol2FsStrips(), _body = new Vol2FsStrips(), _smoke = new Vol2FsStrips();
        ParticleSystem _licks, _embers, _ash;
        float _ashAt = -1f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            var core = M("fs_streak", Hot * 1.4f, 3f, 1f); core.NoiseAmt = 0.3f; core.Soft = 0.03f; core.Erode = 0.12f; core.Scroll = new Vector2(-4f, 0f);
            Vol2Fs.MeshObject(root, "SpiritCore", Kit.Mat(core), out _coreMesh);
            var body = M("fs_flame", Orange, 1.8f, 0.85f); body.NoiseAmt = 0.6f; body.Erode = 0.3f; body.Soft = 0.1f; body.Scroll = new Vector2(-2.5f, 0.3f);
            Vol2Fs.MeshObject(root, "SpiritBody", Kit.Mat(body), out _bodyMesh);
            Vol2Fs.MeshObject(root, "SpiritSmoke", Kit.InkSolid("fs_stroke2", InkC, Orange * 1.4f, 0.16f, 0.55f, 0.35f, 1.3f), out _smokeMesh);
            _licks = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick()), 0.3f, 0.6f, 0.3f, 0.6f, 1.6f, 1200), root, "Licks");      // r2: bigger heads
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.55f, 0.2f), 4f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.02f, Size1 = 0.05f, Gravity = -0.4f, Drag = 0.8f, Noise = 1f, NoiseFreq = 1.5f, Max = 900 }, root, "Embers");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.0f, Life1 = 2.0f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.04f, Size1 = 0.1f, Gravity = -0.3f, Noise = 0.8f, NoiseFreq = 1.2f, Spin = 200f, Max = 500 }, root, "Ash");
            foreach (var ps in new[] { _licks, _embers, _ash }) ps.Play(false);
        }

        float Ang(int k, float t) => k * Vol2ReturnGeometry.Tau / Spirits + Spin * Mathf.Max(0f, t - LaunchAt);
        float Hgt(int k, float t) => 0.75f + 0.45f * Mathf.Sin(t * 5f + k * 1.7f);
        Vector3 SpiritAt(int k, float t) => RingPoint(Ang(k, t), RingNow(t)) + Vector3.up * Hgt(k, t);

        protected override void OnTurn()
        {
            float t = Now(NowUs);
            for (int k = 0; k < Spirits; k++)
            {
                Vector3 p = SpiritAt(k, t); if (Front(Ang(k, t)) < 0.3f) continue;
                for (int i = 0; i < 4; i++) Vol2FsB1.EmitV(_licks, p + Random.insideUnitSphere * 0.2f, Vector3.up * Random.Range(1f, 2.5f), -1f, -1f);
                for (int i = 0; i < 10; i++) Vol2FsB1.EmitV(_embers, p, Random.onUnitSphere * Random.Range(1.5f, 4f), -1f, -1f);
            }
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 22; k++) Vol2FsB1.EmitV(_licks, c + Random.insideUnitSphere * 0.4f, Vector3.up * Random.Range(1f, 3f) + Random.insideUnitSphere, -1f, -1f);
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_embers, c, Random.onUnitSphere * Random.Range(2f, 7f), -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 9; k++) Vol2FsB1.EmitV(_licks, c + Random.insideUnitSphere * 0.3f, Vector3.up * Random.Range(0.8f, 2f), -1f, -1f);
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_embers, c, Random.onUnitSphere * Random.Range(1.5f, 4.5f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_embers, s.Pos, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f); }

        protected override void OnHome() { _ashAt = Now(NowUs); }

        protected override void DrawRing(float t, float r, float k01, bool back, float life)
        {
            var cam = CamPos;
            _core.Clear(); _body.Clear(); _smoke.Clear();
            float burn = _ashAt >= 0f ? Mathf.Clamp01((t - _ashAt) / 0.7f) : 0f;
            if (life > 0.01f)
            {
                for (int s = 0; s < Spirits; s++)
                {
                    float fr = Front(Ang(s, t));
                    // the tail: where this spirit's head was over the last TailSpan s (the trip's own clock) - burning out from its tail at home
                    int n = 22; var pts = new List<Vector3>(); var wc = new List<float>(); var wb = new List<float>(); var al = new List<float>();
                    for (int i = n - 1; i >= 0; i--)
                    {
                        float u = i / (float)(n - 1);
                        if (u > 1f - burn) continue;
                        float tt = Mathf.Max(LaunchAt, t - TailSpan * u);
                        Vector3 p = SpiritAt(s, tt) + Vector3.up * 0.05f * Mathf.Sin(u * 9f + t * 14f);
                        if (_ashAt >= 0f) p += Vector3.down * 0.4f * Vol2Fs.Smooth(_ashAt, _ashAt + 0.6f, t);
                        pts.Add(p); float head = 1f - u;
                        wc.Add(0.2f * Mathf.Max(0.15f, head)); wb.Add(0.95f * Mathf.Max(0.1f, Mathf.Sin(Mathf.Max(0f, head) * Mathf.PI * 0.85f + 0.2f)));
                        al.Add(life * (0.3f + 0.7f * fr) * (0.35f + 0.65f * head));
                    }
                    if (pts.Count >= 2)
                    {
                        _body.Strip(pts, wb, null, Color.white, al, 0.31f + s * 0.07f, cam);
                        _core.Strip(pts, wc, null, Color.white, al, 0.6f + s * 0.03f, cam);
                        var low = new List<Vector3>(); var lw = new List<float>(); foreach (var p in pts) { low.Add(p + Vector3.down * 0.3f); lw.Add(0.6f); }
                        _smoke.Strip(low, lw, null, Color.white, al, 0.12f + s * 0.05f, cam);
                    }
                    Vector3 hd = SpiritAt(s, t);
                    if (burn < 0.5f && fr > 0.2f) for (int i = 0; i < 2; i++) Vol2FsB1.EmitV(_licks, hd + Random.insideUnitSphere * 0.2f, Vector3.up * Random.Range(0.6f, 1.6f), -1f, -1f);
                    if (fr > 0.2f && Time.frameCount % 3 == s % 3) Vol2FsB1.EmitV(_embers, hd, Random.insideUnitSphere * 1.5f + Vector3.up * 0.5f, -1f, -1f);
                    if (_ashAt >= 0f && burn < 1f && Time.frameCount % 2 == 0 && fr > 0.2f) Vol2FsB1.EmitV(_ash, hd + Vector3.down * 0.3f, Vector3.up * Random.Range(0.5f, 1.4f), -1f, -1f);
                }
            }
            _core.Write(_coreMesh); _body.Write(_bodyMesh); _smoke.Write(_smokeMesh);
        }
    }
}
