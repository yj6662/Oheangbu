using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>몽 (earth, ㅗ fan, 돌아온다) - free showcase batch 7, concept mong_b (a shoal of flying stones sweeps out in a fan like a school of fish and swings back
    /// to the caster in a loop, battering both bears). Executor facts only: the shoal rides the executor's sector where it is now (the base's TripFraction: out
    /// FanTravel in the out leg, home in the back leg) - fourteen tumbling chipped stones spread across it low over the ground, each trailing a ribbon of dust with
    /// an ink streak and an amber edge (where it really flew); at the turn they swing across to the other side of the fan so out and back draw closed loops;
    /// the first touch = a burst of grit and dust and the shared push (the body's own skid), the return touch = a smaller crack of grit (x0.5); a shot taken
    /// shatters on a stone. Home: the stones drop round the caster's feet and sink into the ground in dust (CSV 끝: 가라앉으며 둘레 땅이 판으로 들림).</summary>
    public sealed class Vol2FsMong : Vol2FsBackFan
    {
        static readonly Color Amber = new Color(0.95f, 0.62f, 0.3f);
        const int N = 14;
        sealed class Stone { public Transform T; public float U, V, H, Ph, Size; public readonly List<(Vector3 P, float T)> Trail = new List<(Vector3, float)>(64); public Vector3 Pos, Drop; }
        readonly Stone[] _st = new Stone[N];
        Mesh _dustMesh, _inkMesh, _edgeMesh;
        readonly Vol2FsStrips _dust = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _edge = new Vol2FsStrips();
        ParticleSystem _grit, _puff, _chips;
        float _dropAt = -1f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            var stone = Kit.Stone("fs_rock", new Color(0.62f, 0.52f, 0.44f), Amber * 0.6f, "fs_vein") ?? Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.6f, 0.5f, 0.42f), 0f, 0.12f);
            for (int i = 0; i < N; i++)
            {
                var s = new Stone { U = -0.9f + 1.8f * i / (N - 1) + (Vol2Fs.Hash(Seed, i) - 0.5f) * 0.12f, V = 0.25f + 0.7f * Vol2Fs.Hash(Seed, i + 20), H = 0.55f + 1.3f * Vol2Fs.Hash(Seed, i + 40), Ph = Vol2Fs.Hash(Seed, i + 60) * 6.28f, Size = 1.0f + 0.9f * Vol2Fs.Hash(Seed, i + 80) };
                s.T = Vol2FsB1.Prop(root, "Stone" + i, Kit.SharedMesh("mongRock" + (i % 4), () => Vol2FsB1.Rock(560 + i % 4, 0.3f, 0.8f)), stone).transform;
                s.T.gameObject.SetActive(false);
                _st[i] = s;
            }
            Vol2Fs.MeshObject(root, "DustTrails", Kit.Mat(Vol2FsB3.Dust(new Color(0.5f, 0.38f, 0.28f, 1f), 0.5f)), out _dustMesh);
            Vol2Fs.MeshObject(root, "InkTrails", Kit.InkSolid("fs_stroke2", InkC, Amber * 1.5f, 0.18f, 0.45f, 0.32f, 1.4f), out _inkMesh);
            var g = M("fs_streak", Amber * 1.5f, 2.2f, 1f); g.NoiseAmt = 0.3f; g.Soft = 0.04f; g.Erode = 0.15f;
            Vol2Fs.MeshObject(root, "AmberEdges", Kit.Mat(g), out _edgeMesh);
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.58f, 0.5f, 0.44f), 0f, 0.12f), Mesh = Kit.SharedMesh("mangGrit", () => Vol2FsB1.Rock(17, 0.5f)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.05f, Size1 = 0.14f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 400 }, root, "Grit");
            _puff = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.48f, 0.34f, 0.25f, 1f), 0.55f)), Life0 = 0.7f, Life1 = 1.5f, Speed0 = 0.3f, Speed1 = 2f, Size0 = 0.35f, Size1 = 0.8f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.3f, Max = 400 }, root, "Puff");
            _chips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Amber, 3.5f, 1f)), Life0 = 0.3f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.6f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 600 }, root, "Chips");
            foreach (var ps in new[] { _grit, _puff, _chips }) ps.Play(false);
        }

        Vector3 StonePoint(Stone s, Vector3 apex, float t)
        {
            float w = TurnRel >= 0f ? Vol2Fs.Smooth(TurnRel - 0.05f, TurnRel + 0.25f, t) : 0f;
            float u = s.U * Mathf.Cos(Mathf.PI * w), v = s.V + 0.18f * Mathf.Sin(Mathf.PI * w);
            float h = s.H + 0.5f * Mathf.Sin(Mathf.PI * w) + 0.12f * Mathf.Sin(t * 9f + s.Ph);
            // r2 (r1 review: a narrow column from the shoulder): the shoal swings out WIDE beside the sector (up to ~2.6 m at mid depth, the outer stones most;
            // the middle ones stay narrow and pass through the struck bodies)
            float wide = 2.6f * u * Mathf.Abs(u) * Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(v))) * Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.12f, t);
            return FanPoint(apex, u, v) + Side * wide + Vector3.up * h;
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s); HitStop(Now(NowUs), 0.1f);
            for (int k = 0; k < 24; k++) Vol2FsB1.EmitV(_grit, c, (Random.onUnitSphere + FlatN(c - Apex) * 0.6f + Vector3.up * 0.5f).normalized * Random.Range(3f, 7f), -1f, -1f);
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_puff, c + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * 2f, -1f, -1f);
            for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_chips, c, Random.onUnitSphere * Random.Range(3f, 9f), -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_grit, c, (Random.onUnitSphere + FlatN(Apex - c) * 0.5f + Vector3.up * 0.4f).normalized * Random.Range(2f, 5f), -1f, -1f);
            for (int k = 0; k < 4; k++) Vol2FsB1.EmitV(_puff, c + Random.insideUnitSphere * 0.3f, Random.onUnitSphere, -1f, -1f);
            for (int k = 0; k < 20; k++) Vol2FsB1.EmitV(_chips, c, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_chips, s.Pos, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f); }

        protected override void OnTurn() { foreach (var s in _st) Vol2FsB1.EmitV(_puff, s.Pos, Random.onUnitSphere, -1f, -1f); }

        protected override void OnHome()
        {
            if (_dropAt >= 0f) return;
            _dropAt = Now(NowUs);
            for (int i = 0; i < N; i++)
            {
                float a = (i / (float)N - 0.5f) * 2.2f;
                _st[i].Drop = G(Apex + Dir(a) * (1.0f + 0.5f * Vol2Fs.Hash(Seed, i + 100)));
            }
        }

        protected override void DrawFan(float t, Vector3 apex, float k, bool back, float life)
        {
            var cam = CamPos;
            _dust.Clear(); _ink.Clear(); _edge.Clear();
            bool fly = life > 0.01f && !IsHome;
            for (int i = 0; i < N; i++)
            {
                var s = _st[i];
                if (fly)
                {
                    Vector3 p = StonePoint(s, apex, t);
                    s.Pos = p;
                    s.T.gameObject.SetActive(true);
                    s.T.SetPositionAndRotation(p, Quaternion.Euler(t * 500f + s.Ph * 50f, t * 330f + i * 40f, s.Ph * 30f));
                    s.T.localScale = Vector3.one * s.Size * Vol2Fs.EaseOutBack(Mathf.Clamp01((t - LaunchAt) / 0.12f));
                    if (s.Trail.Count == 0 || (s.Trail[s.Trail.Count - 1].P - p).sqrMagnitude > 1e-5f) s.Trail.Add((p, t));
                    if (Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_puff, p, Random.insideUnitSphere * 0.5f, -1f, -1f);
                }
                else if (_dropAt >= 0f)
                {
                    // dropped round the caster's feet, then sunk into the ground
                    float a = t - _dropAt;
                    float fall = Vol2Fs.Smooth(0f, 0.25f, a), sink = Vol2Fs.Smooth(0.5f, 1.2f, a);
                    Vector3 p = Vector3.Lerp(s.Pos, s.Drop + Vector3.up * 0.12f * s.Size, fall) + Vector3.down * 0.4f * s.Size * sink;
                    s.T.position = p;
                    s.T.gameObject.SetActive(sink < 0.99f);
                    if (a < 0.3f && a + Time.deltaTime >= 0.25f) { Vol2FsB1.EmitV(_puff, s.Drop, Random.onUnitSphere, -1f, -1f); Vol2FsB1.EmitV(_grit, s.Drop, Vector3.up * 2f + Random.insideUnitSphere, -1f, -1f); }
                }
                else s.T.gameObject.SetActive(false);
                while (s.Trail.Count > 0 && t - s.Trail[0].T > 0.7f) s.Trail.RemoveAt(0);
                if (s.Trail.Count >= 2)
                {
                    int n = s.Trail.Count; var pts = new Vector3[n]; var wd = new float[n]; var wi = new float[n]; var we = new float[n]; var al = new float[n];
                    for (int j = 0; j < n; j++)
                    {
                        float u = j / (float)(n - 1), age = t - s.Trail[j].T;
                        pts[j] = s.Trail[j].P; wd[j] = 0.35f + 0.7f * (1f - u); wi[j] = 0.24f * Mathf.Max(0.1f, u); we[j] = 0.05f * Mathf.Max(0.1f, u);
                        al[j] = 1f - Vol2Fs.Smooth(0.15f, 0.7f, age);
                    }
                    _dust.Strip(pts, wd, null, Color.white, al, 0.45f + i * 0.03f, cam);
                    _ink.Strip(pts, wi, null, Color.white, al, 0.2f, cam);
                    _edge.Strip(pts, we, null, Color.white, al, 0.6f, cam);
                }
            }
            _dust.Write(_dustMesh); _ink.Write(_inkMesh); _edge.Write(_edgeMesh);
        }

        /// <summary>The dropped stones sink over 1.2 s after home.</summary>
        protected override float Tail => 1.0f;
    }
}
