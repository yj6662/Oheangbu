using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>옹 (water, ㅗ fan, 돌아온다 - the PEAK) - free showcase batch 7, concept ong_b (two huge hands of water reach out across the fan, grab both bears and haul
    /// them back toward the caster). Executor facts only: a sheet of clear water fills the executor's sector where it is now (the base's TripFraction: it rushes out
    /// FanTravel in the out leg with a curling crest at its far edge, and pulls home in the back leg with the crest on its near edge - a retreating riptide); from
    /// the caster's side an ARM of water reaches out to every body the first touch soaked (the shared wet action, 4 s) and coils round it; on the way back each
    /// Drag the executor booked (the report's Drags: From -> To over its duration, the status sink really moves the body - the arm follows the body's own transform)
    /// hauls it in, the arm shortening, foam boiling at its feet and two wet furrows scored in the ground from where it stood to where it ends (1.5 m from the
    /// caster); a boss the rules exempt is grabbed but not pulled (no Drag record - the arm lets go). The return touch (x0.5) = a heavier splash on the body.
    /// End: the arms let go and pour down into the ground, the sheet drains (CSV 끝: 아래로 흘러내림).
    /// r2 (the main agent's r1 review: white blocky slabs in a narrow column): deep teal water, arms and crest of three twisting strands with foam rims and ink threads,
    /// the arms swinging out wide and high, a bow wave and a foam trail along every drag, wider furrows.</summary>
    public sealed class Vol2FsOng : Vol2FsBackFan
    {
        static readonly Color Teal = new Color(0.35f, 0.85f, 1f);
        Mesh _sheetMesh, _crestMesh, _armMesh, _foamMesh, _inkMesh, _furrowMesh;
        readonly Vol2FsStrips _sheet = new Vol2FsStrips(), _crest = new Vol2FsStrips(), _arm = new Vol2FsStrips(), _foam = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _furrow = new Vol2FsStrips();
        ParticleSystem _spray, _drops, _mist;
        sealed class Arm { public Struck S; public float At, ReleaseAt = -1f; public Dragged D; public int Hand; }
        readonly List<Arm> _arms = new List<Arm>();
        protected override Color OutlineGlow => new Color(0.55f, 1.3f, 1.6f);

        protected override void OnSetup()
        {
            var root = Root.transform;
            // r2 (the main agent's r1 review: white blocky slabs): deep blue-green water (Vol2FsB4.WaterDeep's recipe with a teal tint), arms of three twisting strands
            Vol2Fs.MeshObject(root, "Sheet", DeepTeal(1.3f, 0.95f), out _sheetMesh);
            Vol2Fs.MeshObject(root, "Crest", DeepTeal(1.5f, 1f), out _crestMesh);
            Vol2Fs.MeshObject(root, "Arms", DeepTeal(1.5f, 1f), out _armMesh);
            var foam = M("fs_streak", new Color(0.92f, 1f, 1f), 2.2f, 0.85f); foam.NoiseAmt = 0.45f; foam.Soft = 0.06f; foam.Erode = 0.2f; foam.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "Foam", Kit.Mat(foam), out _foamMesh);
            Vol2Fs.MeshObject(root, "InkThreads", Kit.InkSolid("fs_stroke2", InkC, Teal * 1.2f, 0.08f, 0.45f, 0.32f, 1.3f), out _inkMesh);
            var wet = M("fs_wet", new Color(0.1f, 0.16f, 0.2f, 1f), 1f, 0f); wet.Opacity = 0.7f; wet.NoiseAmt = 0.3f; wet.Soft = 0.2f;
            Vol2Fs.MeshObject(root, "Furrows", Kit.Mat(wet), out _furrowMesh);
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.45f, "fs_splash"), Life0 = 0.35f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.34f, Gravity = 1.8f, Drag = 0.6f, Max = 1500 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.7f, 0.95f, 1f), 2.6f, 0.75f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.025f, Size1 = 0.06f, Gravity = 2.4f, Drag = 0.3f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 1500 }, root, "Drops");
            var mist = M("fs_puff", new Color(0.75f, 0.9f, 1f, 1f), 1f, 0.3f); mist.Opacity = 0.35f; mist.NoiseAmt = 0.5f; mist.Erode = 0.3f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.4f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.5f, Drag = 1.2f, Max = 300 }, root, "Mist");
            foreach (var ps in new[] { _spray, _drops, _mist }) ps.Play(false);
        }

        Material DeepTeal(float intensity, float opacity)
        {
            // r3 (r2 review: saturated blue plastic curtains): paler, clearer, more see-through water - a desaturated teal, more additive light, lower opacity, torn edges
            var w = Vol2FsMat.Make("fs_water", new Color(0.72f, 0.9f, 0.9f), intensity, 0.42f); w.Ramp = "fs_ramp_water"; w.RampScale = 1.2f; w.NoiseAmt = 0.45f; w.NoiseScale = 1.4f; w.Erode = 0.28f; w.Soft = 0.25f;
            w.Scroll = new Vector2(-1.2f, 0f); w.Edge = new Color(0.85f, 0.95f, 0.95f, 1f) * 0.7f; w.EdgeWidth = 0.12f; w.Opacity = opacity * 0.62f;
            return Kit.Mat(w);
        }

        /// <summary>Three twisting strands of water along a curve (an arm / a crest row): water bodies, a foam rim on the top strand, an ink thread through the middle.</summary>
        void Strands(List<Vector3> pts, float w0, float w1, float life, float t, float rnd, Vector3 cam, Vol2FsStrips water)
        {
            int n = pts.Count; if (n < 2) return;
            for (int k = 0; k < 3; k++)
            {
                var sp = new List<Vector3>(n); var sw = new List<float>(n); var sa = new List<float>(n);
                for (int j = 0; j < n; j++)
                {
                    float u = j / (float)(n - 1);
                    Vector3 tan = pts[Mathf.Min(n - 1, j + 1)] - pts[Mathf.Max(0, j - 1)]; tan = tan.sqrMagnitude > 1e-8f ? tan.normalized : F;
                    Vector3 a = Vector3.Cross(Vector3.up, tan); a = a.sqrMagnitude > 1e-6f ? a.normalized : Side; Vector3 b = Vector3.Cross(tan, a);
                    // r3: strands of different widths (not parallel stripes), wider apart, each breaking up along its length
                    float th = u * (7f + 2.5f * k) + t * (6f + k) + k * 2.1f, r = Mathf.Lerp(w0, w1, u) * (0.45f + 0.15f * k);
                    float kw = k == 0 ? 0.42f : k == 1 ? 0.26f : 0.16f;
                    sp.Add(pts[j] + (a * Mathf.Cos(th) + b * Mathf.Sin(th)) * r); sw.Add(Mathf.Lerp(w0, w1, u) * kw * (0.7f + 0.3f * Mathf.Sin(u * 13f + k * 3f + t * 5f)) + 0.03f); sa.Add(life * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(u * 7f + k + t * 3f))));
                }
                water.Strip(sp, sw, null, Color.white, sa, rnd + k * 0.17f, cam, 0f, 3f);
                if (k != 1) { var fp = new List<Vector3>(n); var fw = new List<float>(n); for (int j = 0; j < n; j++) { fp.Add(sp[j] + Vector3.up * sw[j] * 0.45f); fw.Add(0.06f + 0.03f * Mathf.Sin(j * 0.9f + t * 6f)); } _foam.Strip(fp, fw, null, Color.white, sa, rnd + 0.5f, cam); }
                if (k == 0) { var ip = new List<Vector3>(n); var iw = new List<float>(n); for (int j = 0; j < n; j++) { ip.Add(sp[j]); iw.Add(0.04f); } _ink.Strip(ip, iw, null, Color.white, sa, rnd + 0.3f, cam); }
            }
        }

        protected override void OnStruck(Struck s)
        {
            if (s.Kind != "Instant") return;
            var c = CentreNow(s);
            _arms.Add(new Arm { S = s, At = Now(NowUs), Hand = Vector3.Dot(c - Apex, Side) >= 0f ? 1 : -1 });
            for (int k = 0; k < 90; k++) Vol2FsB1.EmitV(_spray, c + Random.insideUnitSphere * 0.4f, (Random.onUnitSphere + Vector3.up * 0.9f).normalized * Random.Range(2f, 6f), -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 120; k++) Vol2FsB1.EmitV(_spray, c + Random.insideUnitSphere * 0.4f, (Random.onUnitSphere + Vector3.up * 1.0f).normalized * Random.Range(2f, 6.5f), -1f, -1f);
            for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_mist, c, Random.onUnitSphere, -1f, -1f);
        }

        protected override void OnDrag(Dragged d)
        {
            foreach (var a in _arms) if (a.S.Id == d.Id) a.D = d;
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_spray, d.From + Vector3.up * 0.3f, (FlatN(Apex - d.From) * 1.5f + Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(2f, 5f), -1f, -1f);
            HitStop(Now(NowUs), 0.06f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_spray, s.Pos, Random.onUnitSphere * Random.Range(1.5f, 5f), -1f, -1f); }

        protected override void OnHome()
        {
            foreach (var a in _arms) if (a.ReleaseAt < 0f) a.ReleaseAt = Now(NowUs);
        }

        float ReleaseOf(Arm a)
        {
            if (a.ReleaseAt >= 0f) return a.ReleaseAt;
            if (a.D != null) return a.D.At + a.D.Dur + 0.3f;
            return -1f;
        }

        void DrawArm(Arm a, float t, Vector3 cam)
        {
            float rel = ReleaseOf(a);
            float since = t - a.At;
            float reach = Vol2Fs.Smooth(0f, 0.14f, since);
            float gone = rel >= 0f ? Vol2Fs.Smooth(rel, rel + 0.5f, t) : 0f;
            if (gone >= 0.99f) return;
            var b = BodyNow(a.S); Vector3 c = b.center;
            // r2: a big curling arm - from beside the caster it swings out WIDE (2.2 m off the line of sight) and high, comes down on the body and coils round it
            Vector3 root = G(Apex + Side * (0.9f * a.Hand) + F * 0.5f) + Vector3.up * 0.5f;
            bool hauling = a.D != null && t >= a.D.At && t <= a.D.At + a.D.Dur + 0.3f;
            float swing = hauling ? 1.4f : 2.2f;
            Vector3 mid = Vector3.Lerp(root, c, 0.5f) + Vector3.up * (1.6f + 0.3f * Mathf.Sin(t * 4f + a.Hand)) + Side * (swing * a.Hand);
            float life = 1f - gone;
            var pts = new List<Vector3>();
            int n = 20;
            for (int j = 0; j < n; j++)
            {
                float u = j / (float)(n - 1) * reach;
                pts.Add(Vol2FsB4F.Bez(root, mid, c + Vector3.up * b.extents.y * 0.6f, u) + Vector3.down * 1.4f * gone * u);
            }
            if (reach > 0.99f)
            {
                float rx = Mathf.Max(b.extents.x, b.extents.z) + 0.25f;
                int m = 22;
                for (int j = 1; j <= m; j++)
                {
                    float u = j / (float)m, ang = u * Mathf.PI * 3.4f + t * 7f * a.Hand;
                    pts.Add(c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rx * (1f - 0.25f * u) + Vector3.up * (b.extents.y * (0.6f - 1.2f * u)) + Vector3.down * 0.9f * gone);
                }
            }
            Strands(pts, 0.8f, 0.45f, life, t, 0.37f + a.Hand * 0.1f, cam, _arm);
            if (gone > 0.01f && Time.frameCount % 2 == 0) for (int k = 0; k < 5; k++) Vol2FsB1.EmitV(_drops, pts[Random.Range(0, pts.Count)], Vector3.down * Random.Range(1f, 2.5f), -1f, -1f);
            if (life > 0.3f) for (int k = 0; k < 3; k++) Vol2FsB1.EmitV(_spray, pts[Random.Range(0, pts.Count)], Random.insideUnitSphere * 2.5f + Vector3.up * 1.2f, -1f, -1f);
            if (life > 0.3f && Time.frameCount % 2 == 0) for (int k = 0; k < 3; k++) Vol2FsB1.EmitV(_drops, pts[Random.Range(0, pts.Count)], Random.insideUnitSphere * 2f, -1f, -1f);
            // hauling: a bow wave of foam and spray at the body's feet, thrown back the way it is pulled from
            if (hauling)
            {
                Vector3 feet = FeetOf(a.S), toward = FlatN(Apex - feet);
                for (int k = 0; k < 7; k++) Vol2FsB1.EmitV(_spray, feet + Random.insideUnitSphere * 0.35f + Vector3.up * 0.15f, (-toward * 1.4f + Side * Random.Range(-1.2f, 1.2f) + Vector3.up * 1.0f).normalized * Random.Range(2f, 5f), -1f, -1f);
                if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_mist, feet + Vector3.up * 0.3f, -toward + Vector3.up * 0.4f, -1f, -1f);
            }
        }

        void DrawFurrows(float t, Vector3 cam)
        {
            foreach (var a in _arms)
            {
                if (a.D == null) continue;
                float da = t - a.D.At; if (da < 0f) continue;
                float lf = 1f - Vol2Fs.Smooth(1.6f, 2.4f, da);
                if (lf <= 0.01f) continue;
                Vector3 from = a.D.From, to = G(FeetOf(a.S));
                Vector3 d = to - from; d.y = 0f; if (d.sqrMagnitude < 0.04f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, d.normalized) * 0.32f;
                Vol2FsB6.Crack(_furrow, from - side, to - side, Ground, 0.26f, 0f, 1f, lf, 0.3f, Seed + a.Hand, 0.04f);
                Vol2FsB6.Crack(_furrow, from + side, to + side, Ground, 0.26f, 0f, 1f, lf, 0.7f, Seed + a.Hand + 3, 0.04f);
                // the foam trail down the middle of the drag (fresh at the body, fading behind)
                int n = 14; var fp = new Vector3[n]; var fw = new float[n]; var fa = new float[n];
                for (int j = 0; j < n; j++) { float u = j / (float)(n - 1); fp[j] = Vector3.Lerp(from, to, u) + Vector3.up * 0.04f; fw[j] = 0.25f + 0.35f * u; fa[j] = lf * (0.35f + 0.65f * u) * (1f - Vol2Fs.Smooth(0.8f, 2.0f, da) * (1f - u)); }
                var sd = new Vector3[n]; for (int j = 0; j < n; j++) sd[j] = side.normalized;
                _foam.Strip(fp, fw, sd, Color.white, fa, 0.4f + a.Hand * 0.1f, cam);
            }
        }

        protected override void DrawFan(float t, Vector3 apex, float k, bool back, float life)
        {
            var cam = CamPos;
            _sheet.Clear(); _crest.Clear(); _arm.Clear(); _foam.Clear(); _ink.Clear(); _furrow.Clear();
            if (life > 0.01f)
            {
                float grow = Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.12f, t);
                // the sheet: arcs of water across the sector, flaring wide beside it (r2), low over the ground
                for (int r = 0; r < 5; r++)
                {
                    float v = 0.2f + 0.2f * r;
                    int n = 18; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var sd = new Vector3[n];
                    for (int j = 0; j < n; j++)
                    {
                        float u = -1f + 2f * j / (n - 1);
                        pts[j] = FanPoint(apex, u, v) + Side * (2.2f * u * Mathf.Abs(u) * Mathf.Max(0f, Mathf.Sin(Mathf.PI * v)) * grow) + Vector3.up * (0.08f + 0.06f * Mathf.Sin(u * 4f + t * 8f + r));
                        w[j] = Depth * 0.24f; al[j] = life * 0.7f; sd[j] = Dir(u * Half);
                    }
                    _sheet.Strip(pts, w, sd, Color.white, al, 0.2f + r * 0.13f, cam, 0f, 3f);
                }
                // the crest: three curling rows on the leading edge (far edge going out, near edge coming back), the higher rows curling further over
                float cv = back ? 0.12f : 1f, ch = back ? 1.1f : 1.8f, dirSign = back ? -1f : 1f;
                for (int row = 0; row < 3; row++)
                {
                    int n = 20; var pts = new List<Vector3>(n);
                    for (int j = 0; j < n; j++)
                    {
                        float u = -1f + 2f * j / (n - 1);
                        float hh = ch * (0.3f + 0.35f * row) * (0.75f + 0.25f * Mathf.Sin(u * 5f + t * 9f)) * Mathf.Max(0f, 1f - 0.35f * Mathf.Abs(u));
                        Vector3 bp = FanPoint(apex, u, cv) + Side * (2.2f * u * Mathf.Abs(u) * 0.35f * grow);
                        pts.Add(bp + Vector3.up * hh + dirSign * Dir(u * Half) * (0.25f * row * row));
                    }
                    Strands(pts, 0.6f - 0.12f * row, 0.6f - 0.12f * row, life, t + row, 0.45f + row * 0.1f, cam, _crest);
                    if (row == 2 && Time.frameCount % 2 == 0) for (int j = 0; j < 6; j++) Vol2FsB1.EmitV(_spray, pts[Random.Range(0, n)], dirSign * F * 2f + Vector3.up * Random.Range(1f, 3f) + Random.insideUnitSphere, -1f, -1f);
                }
            }
            foreach (var a in _arms) DrawArm(a, t, cam);
            DrawFurrows(t, cam);
            _sheet.Write(_sheetMesh); _crest.Write(_crestMesh); _arm.Write(_armMesh); _foam.Write(_foamMesh); _ink.Write(_inkMesh); _furrow.Write(_furrowMesh);
        }

        /// <summary>The arms let go and the furrows dry after home.</summary>
        protected override float Tail => 1.6f;
    }
}
