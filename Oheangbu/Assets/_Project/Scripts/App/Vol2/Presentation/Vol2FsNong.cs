using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>농 (fire, ㅗ fan, 돌아온다) - free showcase batch 7, concept nong_b (a flock of birds of flame flies out in a fan over the area, wheels round at the far
    /// edge and streams back to the brush, passing through both bears twice). Executor facts only: the flock rides the executor's sector where it is now (the base's
    /// TripFraction: out FanTravel in the out leg, home in the back leg), swinging out WIDE beside it (up to ~2.6 m at mid depth; the middle birds stay narrow and
    /// pass through the struck bodies) - seven big swallows of flame (2.4 m wings of orange-red flame over black ink wings, a burning tail of where each flew, an ink
    /// smoke shadow under it); at the turn they WHEEL (each crosses to the other side of the fan) so out and back draw closed loops; the first touch sets the body
    /// burning (the shared action, 2 s) with a flare of tongues, the return touch is a smaller flare (x0.5); a shot taken bursts in embers. Home: the flock folds into
    /// the brush tip in a puff of embers and ash rising (CSV 끝: 끝부터 타 재로 날림).
    /// r2 (the main agent's r1 review: small white V shapes in a narrow column): fewer, bigger birds, flame + ink wings, longer tails, the wide swing, no white.</summary>
    public sealed class Vol2FsNong : Vol2FsBackFan
    {
        static readonly Color Orange = new Color(1f, 0.42f, 0.1f), Red = new Color(1f, 0.25f, 0.06f);
        const int N = 7;
        const float Swing = 2.6f, Span = 1.2f;
        sealed class Bird { public float U, V, H, Ph; public readonly List<(Vector3 P, float T)> Trail = new List<(Vector3, float)>(96); public Vector3 Pos, Dir = Vector3.forward; public bool Seen; }
        readonly Bird[] _birds = new Bird[N];
        Mesh _wingMesh, _inkWingMesh, _tailMesh, _smokeMesh;
        readonly Vol2FsStrips _wing = new Vol2FsStrips(), _inkWing = new Vol2FsStrips(), _tail = new Vol2FsStrips(), _smoke = new Vol2FsStrips();
        ParticleSystem _licks, _embers, _ash;
        bool _folded;

        protected override void OnSetup()
        {
            var root = Root.transform;
            for (int i = 0; i < N; i++)
                _birds[i] = new Bird { U = -0.95f + 1.9f * i / (N - 1) + (Vol2Fs.Hash(Seed, i) - 0.5f) * 0.1f, V = 0.35f + 0.55f * Vol2Fs.Hash(Seed, i + 20), H = 1.0f + 1.2f * Vol2Fs.Hash(Seed, i + 40), Ph = Vol2Fs.Hash(Seed, i + 60) * 6.28f };
            var w = M("fs_flame", Orange, 1.7f, 0.8f); w.NoiseAmt = 0.55f; w.Erode = 0.25f; w.Soft = 0.1f; w.Scroll = new Vector2(-2f, 0.4f);
            Vol2Fs.MeshObject(root, "Wings", Kit.Mat(w), out _wingMesh);
            Vol2Fs.MeshObject(root, "InkWings", Kit.InkSolid("fs_stroke2", InkC, Red * 1.5f, 0.2f, 0.5f, 0.32f, 1.3f), out _inkWingMesh);
            var tl = M("fs_flame", Red, 1.6f, 0.8f); tl.NoiseAmt = 0.6f; tl.Erode = 0.3f; tl.Soft = 0.1f; tl.Scroll = new Vector2(-2.5f, 0.3f);
            Vol2Fs.MeshObject(root, "Tails", Kit.Mat(tl), out _tailMesh);
            Vol2Fs.MeshObject(root, "TailSmoke", Kit.InkSolid("fs_stroke2", InkC, Orange * 1.3f, 0.16f, 0.55f, 0.35f, 1.3f), out _smokeMesh);
            _licks = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick()), 0.2f, 0.45f, 0.3f, 0.6f, 1.4f, 900), root, "Licks");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.5f, 0.15f), 3.5f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.025f, Size1 = 0.06f, Gravity = -0.4f, Drag = 0.8f, Noise = 1f, NoiseFreq = 1.5f, Max = 1200 }, root, "Embers");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.0f, Life1 = 2.0f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.05f, Size1 = 0.12f, Gravity = -0.3f, Noise = 0.8f, NoiseFreq = 1.2f, Spin = 200f, Max = 400 }, root, "Ash");
            foreach (var ps in new[] { _licks, _embers, _ash }) ps.Play(false);
        }

        /// <summary>A bird's point now: on the sector going out (flared wide beside it), wheeling across to the other side at the turn, streaming home.</summary>
        Vector3 BirdPoint(Bird b, Vector3 apex, float t)
        {
            float s = TurnRel >= 0f ? Vol2Fs.Smooth(TurnRel - 0.05f, TurnRel + 0.25f, t) : 0f;
            float u = b.U * Mathf.Cos(Mathf.PI * s);
            float v = b.V + 0.2f * Mathf.Sin(Mathf.PI * s);
            float h = b.H + 0.6f * Mathf.Sin(Mathf.PI * s) + 0.15f * Mathf.Sin(t * 7f + b.Ph);
            float wide = Swing * u * Mathf.Abs(u) * Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(v))) * Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.12f, t);
            return FanPoint(apex, u, v) + Side * wide + Vector3.up * h;
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 28; k++) Vol2FsB1.EmitV(_licks, c + Random.insideUnitSphere * 0.4f, Vector3.up * Random.Range(1f, 3f) + Random.insideUnitSphere, -1f, -1f);
            for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_embers, c, Random.onUnitSphere * Random.Range(2f, 7f), -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 12; k++) Vol2FsB1.EmitV(_licks, c + Random.insideUnitSphere * 0.3f, Vector3.up * Random.Range(0.8f, 2f), -1f, -1f);
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_embers, c, Random.onUnitSphere * Random.Range(1.5f, 4.5f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_embers, s.Pos, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f); }

        protected override void OnTurn()
        {
            foreach (var b in _birds) for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_embers, b.Pos, Random.onUnitSphere * Random.Range(1f, 4f), -1f, -1f);
        }

        protected override void OnHome()
        {
            if (_folded) return; _folded = true;
            Vector3 tip = Tip();
            for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_embers, tip, Random.onUnitSphere * Random.Range(1f, 4f), -1f, -1f);
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_ash, tip + Random.insideUnitSphere * 0.5f, Vector3.up * Random.Range(0.5f, 1.4f), -1f, -1f);
        }

        /// <summary>One wing: a flame stroke from the shoulder out to the wing tip, swept back and bowed, thin-to-wide (Vol2FsB6.Slash).</summary>
        void Wing(Vol2FsStrips s, Vector3 p, Vector3 dir, Vector3 side, float sgn, float flap, float scale, float width, float life, float rnd, Vector3 cam)
        {
            Vector3 tipW = p + side * sgn * Span * scale + Vector3.up * (0.45f * flap * scale) - dir * 0.45f * scale;
            Vol2FsB6.Slash(s, p + dir * 0.15f * scale, tipW, Vector3.up * 0.18f * scale - dir * 0.15f * scale, width * scale, 1f, life, rnd, cam, 10);
        }

        protected override void DrawFan(float t, Vector3 apex, float k, bool back, float life)
        {
            var cam = CamPos;
            _wing.Clear(); _inkWing.Clear(); _tail.Clear(); _smoke.Clear();
            bool fly = life > 0.01f && !IsHome;
            foreach (var b in _birds)
            {
                if (fly)
                {
                    Vector3 p = BirdPoint(b, apex, t);
                    if (back) p = Vector3.Lerp(p, Tip(), (1f - Vol2Fs.Smooth(0f, 0.25f, k)) * 0.6f);
                    Vector3 d = p - b.Pos; if (b.Seen && d.sqrMagnitude > 1e-5f) b.Dir = Vector3.Slerp(b.Dir, d.normalized, 0.5f);
                    b.Pos = p; b.Seen = true;
                    if (b.Trail.Count == 0 || (b.Trail[b.Trail.Count - 1].P - p).sqrMagnitude > 1e-5f) b.Trail.Add((p, t));
                    Vector3 fl = b.Dir; fl.y = 0f; fl = fl.sqrMagnitude > 1e-4f ? fl.normalized : (back ? -F : F);
                    Vector3 side = Vector3.Cross(Vector3.up, fl);
                    float flap = Mathf.Sin(t * 22f + b.Ph);
                    // the ink wing under, the flame wing over (a little smaller), the body a short flame stroke
                    for (int w = 0; w < 2; w++)
                    {
                        float sgn = w == 0 ? 1f : -1f;
                        Wing(_inkWing, p - Vector3.up * 0.05f, fl, side, sgn, flap, 1.05f, 0.42f, life, 0.2f + w * 0.3f, cam);
                        Wing(_wing, p, fl, side, sgn, flap, 0.95f, 0.36f, life, 0.4f + w * 0.3f, cam);
                    }
                    Vol2FsB6.Slash(_wing, p - fl * 0.5f, p + fl * 0.45f, Vector3.up * 0.05f, 0.3f, 1f, life, 0.7f, cam, 6);
                    if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_licks, p, -fl * 1.5f + Vector3.up * 0.6f, -1f, -1f);
                    if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_embers, p + side * Random.Range(-1f, 1f), Random.insideUnitSphere * 1.5f, -1f, -1f);
                }
                while (b.Trail.Count > 0 && t - b.Trail[0].T > 0.8f) b.Trail.RemoveAt(0);
                if (b.Trail.Count >= 2)
                {
                    int n = b.Trail.Count; var pts = new Vector3[n]; var w0 = new float[n]; var al = new float[n]; var sp = new Vector3[n]; var sw = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        float u = i / (float)(n - 1), age = t - b.Trail[i].T;
                        pts[i] = b.Trail[i].P; w0[i] = 0.55f * Mathf.Max(0.1f, u); al[i] = (1f - Vol2Fs.Smooth(0.15f, 0.8f, age)) * Mathf.Max(0.3f, life);
                        sp[i] = pts[i] + Vector3.down * 0.3f; sw[i] = 0.3f * Mathf.Max(0.1f, u);
                    }
                    _tail.Strip(pts, w0, null, Color.white, al, 0.33f + b.Ph * 0.02f, cam);
                    _smoke.Strip(sp, sw, null, Color.white, al, 0.12f, cam);
                }
            }
            _wing.Write(_wingMesh); _inkWing.Write(_inkWingMesh); _tail.Write(_tailMesh); _smoke.Write(_smokeMesh);
        }
    }
}
