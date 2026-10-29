using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    /// <summary>송 (metal, ㅗ fan, 돌아온다) - free showcase batch 7, concept song_b (a fan of thin steel ribbons lashes out across the area and snaps back to the brush,
    /// cutting both bears; ink splashes and sparks along them). Executor facts only: five mirror-steel ribbons (chrome, Kit.Metal, a real ribbon mesh with normals
    /// that twists so the light runs along it; white-gold glints on both edges; an ink underside) run from the brush tip, bowing out WIDE beside the executor's
    /// sector (up to ~2.6 m at mid depth), to its far edge where it is now (the base's TripFraction: out FanTravel in the out leg, home in the back leg); at the turn
    /// each ribbon whips across to the other side of the fan in an S so out and back draw closed loops, and on the way back they reel in to the brush; sparks stream
    /// off them; the first touch = sparks flung sideways and the shared cut, the return touch = a thinner slash (x0.5); a shot taken is sliced. Home: the ribbons
    /// snap at the brush with a ring - shards to the side (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐).
    /// r2 (the main agent's r1 review: flat white paper sheets in a narrow column): chrome mesh ribbons, gold edges, ink under, the wide swing.</summary>
    public sealed class Vol2FsSong : Vol2FsBackFan
    {
        static readonly Color Gold = new Color(1f, 0.86f, 0.55f);
        const int N = 5, P = 26;
        const float Swing = 2.6f;
        Mesh _ribbonMesh, _edgeMesh, _inkMesh, _slashMesh;
        readonly Vol2FsStrips _edge = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _slash = new Vol2FsStrips();
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>(); readonly List<Color> _c = new List<Color>(); readonly List<Vector2> _uv = new List<Vector2>(); readonly List<int> _i = new List<int>();
        ParticleSystem _sparks, _shards;
        sealed class Cut { public Vector3 C, N; public float At; public bool Weak; }
        readonly List<Cut> _cuts = new List<Cut>();
        bool _snapped;

        protected override void OnSetup()
        {
            var root = Root.transform;
            var steel = Kit.Metal(1.35f, 0.2f, new Color(0.05f, 0.04f, 0.02f)) ?? Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.92f, new Color(1.1f, 1.05f, 1f));
            // r3 (r2 review: white-grey paper bows): a darker warm chrome for the ribbons themselves
            var chrome = Kit.Metal(1.0f, 0.15f, new Color(0.03f, 0.025f, 0.01f), new Color(0.6f, 0.58f, 0.55f)) ?? steel;
            var r = Vol2Fs.MeshObject(root, "Ribbons", chrome, out _ribbonMesh); r.shadowCastingMode = ShadowCastingMode.Off;
            var e = M("fs_streak", new Color(1f, 0.78f, 0.4f) * 1.8f, 4.2f, 1f); e.NoiseAmt = 0.15f; e.Soft = 0.02f; e.Erode = 0.08f; e.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "RibbonEdges", Kit.Mat(e), out _edgeMesh);
            Vol2Fs.MeshObject(root, "RibbonInk", Kit.InkSolid("fs_stroke2", InkC, Gold * 1.4f, 0.12f, 0.5f, 0.32f, 1.4f), out _inkMesh);
            var s = M("fs_streak", new Color(1f, 0.9f, 0.68f), 2.6f, 0.9f); s.NoiseAmt = 0.15f; s.Soft = 0.02f; s.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Cuts", Kit.Mat(s), out _slashMesh);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Gold, Color.white, 0.4f), 4.5f, 1f)), Life0 = 0.2f, Life1 = 0.55f, Speed0 = 5f, Speed1 = 13f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.9f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 1200 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = steel, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.05f, Size1 = 0.14f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 300 }, root, "Shards");
            _sparks.Play(false); _shards.Play(false);
        }

        void SparkSide(Vector3 at, int n, float s0 = 5f, float s1 = 13f)
        {
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - at));
            for (int k = 0; k < n; k++) Vol2FsB1.EmitV(_sparks, at, (side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.7f + Vector3.up * 0.3f).normalized * Random.Range(s0, s1), -1f, -1f);
        }

        protected override void OnStruck(Struck s) { var c = CentreNow(s); SparkSide(c, 90); _cuts.Add(new Cut { C = c, N = FlatN(CamPos - c), At = Now(NowUs) }); }
        protected override void OnReturnHit(Struck s) { var c = CentreNow(s); SparkSide(c, 40, 4f, 9f); _cuts.Add(new Cut { C = c + Vector3.up * 0.15f, N = FlatN(CamPos - c), At = Now(NowUs), Weak = true }); }
        protected override void OnShot(Shot s) { SparkSide(s.Pos, 50); for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_shards, s.Pos, Random.onUnitSphere * 4f, -1f, -1f); }
        protected override void OnTurn() { SparkSide(FanPoint(ApexNow(Now(NowUs)), 0f, 1f) + Vector3.up * 1f, 80); }

        protected override void OnHome()
        {
            if (_snapped) return; _snapped = true;
            Vector3 p = Tip(), side = Vector3.Cross(Vector3.up, FlatN(CamPos - p));
            for (int k = 0; k < 18; k++) Vol2FsB1.EmitV(_shards, p + Random.insideUnitSphere * 0.2f, side * (k % 2 == 0 ? 1f : -1f) * Random.Range(3f, 8f) + Vector3.up * Random.Range(0.5f, 3f), -1f, -1f);
            SparkSide(p, 60);
        }

        /// <summary>A point of the fan flared WIDE beside it (the outer u most, nothing at the apex and the far edge).</summary>
        Vector3 Wide(Vector3 apex, float u, float v, float grow) => FanPoint(apex, Mathf.Clamp(u, -1f, 1f), v) + Side * (Swing * u * Mathf.Abs(u) * Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(v))) * grow);

        protected override void DrawFan(float t, Vector3 apex, float k, bool back, float life)
        {
            var cam = CamPos;
            _edge.Clear(); _ink.Clear(); _slash.Clear();
            _v.Clear(); _n.Clear(); _c.Clear(); _uv.Clear(); _i.Clear();
            if (life > 0.01f && !IsHome)
            {
                Vector3 tip = Tip();
                float whip = TurnRel >= 0f ? Vol2Fs.Smooth(TurnRel - 0.05f, TurnRel + 0.25f, t) : 0f;
                float grow = Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.12f, t);
                for (int r = 0; r < N; r++)
                {
                    float u = (-0.95f + 1.9f * r / (N - 1)) * Mathf.Cos(Mathf.PI * whip);
                    Vector3 end = Wide(apex, u, 1f, grow) + Vector3.up * (1.0f + 0.4f * Mathf.Sin(r * 1.7f + t * 6f));
                    Vector3 mid = Wide(apex, u * 1.25f, 0.5f, grow) + Vector3.up * (1.5f + 0.35f * (r % 2) + 0.5f * Mathf.Sin(Mathf.PI * whip));
                    var pts = new Vector3[P]; var sides = new Vector3[P]; var wd = new float[P];
                    for (int j = 0; j < P; j++) pts[j] = Vol2FsB4F.Bez(tip, mid, end, j / (float)(P - 1));
                    int b0 = _v.Count;
                    for (int j = 0; j < P; j++)
                    {
                        float s = j / (float)(P - 1);
                        Vector3 tan = (pts[Mathf.Min(P - 1, j + 1)] - pts[Mathf.Max(0, j - 1)]); tan = tan.sqrMagnitude > 1e-8f ? tan.normalized : F;
                        Vector3 a = Vector3.Cross(Vector3.up, tan); a = a.sqrMagnitude > 1e-6f ? a.normalized : Side;
                        Vector3 b = Vector3.Cross(tan, a).normalized;
                        float th = s * 6f + t * 9f + r * 1.3f;
                        Vector3 sd = a * Mathf.Cos(th) + b * Mathf.Sin(th);
                        float w = 0.42f * Mathf.Max(0.12f, Mathf.Sin(Mathf.Clamp01(s * 0.9f + 0.1f) * Mathf.PI * 0.5f + 0.25f));
                        sides[j] = sd; wd[j] = w;
                        Vector3 nrm = Vector3.Cross(tan, sd).normalized;
                        _v.Add(pts[j] - sd * w * 0.5f); _v.Add(pts[j] + sd * w * 0.5f);
                        _n.Add(nrm); _n.Add(nrm);
                        var col = new Color(1f, 1f, 1f, life); _c.Add(col); _c.Add(col);
                        _uv.Add(new Vector2(s * 3f, 0f)); _uv.Add(new Vector2(s * 3f, 1f));
                    }
                    for (int j = 0; j < P - 1; j++) { int a0 = b0 + j * 2; _i.Add(a0); _i.Add(a0 + 2); _i.Add(a0 + 1); _i.Add(a0 + 1); _i.Add(a0 + 2); _i.Add(a0 + 3); }
                    // gold glints along both edges (brightest where the twist turns the edge toward the camera), the ink underside
                    for (int e = 0; e < 2; e++)
                    {
                        var ep = new Vector3[P]; var ew = new float[P]; var ea = new float[P];
                        for (int j = 0; j < P; j++)
                        {
                            ep[j] = pts[j] + sides[j] * wd[j] * 0.5f * (e == 0 ? 1f : -1f);
                            float face = Mathf.Abs(Vector3.Dot(sides[j], (cam - pts[j]).normalized));
                            ew[j] = 0.05f + 0.06f * (1f - face); ea[j] = life * (0.65f + 0.35f * (1f - face));
                        }
                        _edge.Strip(ep, ew, null, Color.white, ea, 0.5f + r * 0.1f + e * 0.05f, cam);
                    }
                    var ip = new Vector3[P]; var iw = new float[P]; var ia = new float[P];
                    for (int j = 0; j < P; j++) { float s = j / (float)(P - 1); ip[j] = pts[j] + Vector3.down * 0.14f; iw[j] = wd[j] * 0.75f; ia[j] = life * 0.95f; }
                    _ink.Strip(ip, iw, null, Color.white, ia, 0.3f + r * 0.1f, cam);
                    if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_sparks, pts[Random.Range(P / 2, P)], Random.insideUnitSphere * 4f, -1f, -1f);
                }
            }
            _ribbonMesh.Clear();
            _ribbonMesh.SetVertices(_v); _ribbonMesh.SetNormals(_n); _ribbonMesh.SetColors(_c); _ribbonMesh.SetUVs(0, _uv); _ribbonMesh.SetTriangles(_i, 0); _ribbonMesh.RecalculateBounds();
            foreach (var c in _cuts)
            {
                float a = t - c.At; if (a < 0f || a > 0.55f) continue;
                float open = Vol2Fs.Smooth(0f, 0.05f, a), lf = 1f - Vol2Fs.Smooth(0.15f, 0.55f, a);
                Vector3 right = Vector3.Cross(Vector3.up, c.N).normalized, at = c.C + c.N * 0.5f;
                for (int j = 0; j < (c.Weak ? 1 : 2); j++)
                {
                    Vector3 d = (right * (j == 0 ? 1f : -1f) + Vector3.up * 1.1f).normalized;
                    _slash.Strip(new[] { at - d * 0.9f * open, at, at + d * 0.9f * open }, new[] { 0.02f, c.Weak ? 0.07f : 0.13f, 0.02f }, null, Color.white, new[] { lf, lf, lf }, 0.3f + j * 0.4f, cam);
                }
            }
            _edge.Write(_edgeMesh); _ink.Write(_inkMesh); _slash.Write(_slashMesh);
        }
    }
}
