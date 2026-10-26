using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>목 (earth, ㅗ fan, 남긴다) - free showcase batch 3, concept mok_b (the fan rises into a broken field of hexagonal basalt columns of different heights,
    /// a raised stone plateau with steps, faint hot seams between them; the bears thrown back off its edge, dust and cracks at its border). Unlike 모's one-time
    /// eruption of flat slabs it is a plateau that STAYS (it blocks shots and can be climbed - the rule's). Beats, executor facts only: the launch = the columns punch up
    /// out of the ground in a wave from the apex outward (dust jets, rock chunks) -> they stand for the residue material's life (grit trickling, seams breathing);
    /// a body the strike or the field touched = a dust explosion and shove streaks on it (the shove itself is the executor's) -> the last 0.4 s = the columns grind
    /// and tremble -> natural expiry (CSV: 가라앉으며 둘레 땅이 판으로 들림) = they sink back while the earth round the fan lifts as plates and settles.</summary>
    public sealed class Vol2FsMok : Vol2FsLeave
    {
        static readonly Color DustC = new Color(0.55f, 0.42f, 0.34f, 1f), Soil = new Color(0.3f, 0.18f, 0.12f, 1f), Seam = new Color(1f, 0.5f, 0.18f);
        ParticleSystem _chunks, _bits, _cloud, _jets, _speed, _clods; Light[] _lights;
        Material _basalt, _basaltSeam, _rock;
        sealed class Col { public Transform T; public Vector3 Base; public float H, At, Rnd; }
        readonly List<Col> _cols = new List<Col>();
        readonly List<(Transform T, Vector3 Base, Quaternion Rot)> _plates = new List<(Transform, Vector3, Quaternion)>();
        Mesh _crackMesh; readonly Vol2FsStrips _crackS = new Vol2FsStrips();
        bool _built, _exitFx;
        protected override float ExpireSeconds => 1.4f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _basalt = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.66f, 0.58f, 0.54f), 0f, 0.14f);      // r3: lighter (r2: black boxes from behind)
            _basaltSeam = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.6f, 0.52f, 0.48f), 0f, 0.12f, Seam * 0.55f, "fs_vein");      // r2: vein mask (fs_crack is all white: r1 columns glowed white)
            _rock = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.5f, 0.42f), 0f, 0.1f);
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.9f, Life1 = 1.6f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.08f, Size1 = 0.3f, Radius = 0.25f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 300 }, root, "Chunks");
            _bits = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.5f, Life1 = 1f, Speed0 = 0.1f, Speed1 = 0.6f, Size0 = 0.02f, Size1 = 0.06f, Radius = 0.1f, Gravity = 2.4f, Spin = 400f, Max = 400 }, root, "Grit");
            _cloud = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.6f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 1.2f, Size1 = 2.4f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.4f, Drag = 2f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.3f, 0f), Max = 160, SortFudge = 10 }, root, "Cloud");
            _jets = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.5f)), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 2f, Speed1 = 4f, Size0 = 0.4f, Size1 = 0.8f, Shape = ParticleSystemShapeType.Cone, Angle = 18f, Radius = 0.2f, ShapeRot = new Vector3(-90f, 0f, 0f), Drag = 2f, Tiles = 2, SizeEnd = 1.8f, Max = 300, SortFudge = 8 }, root, "DustJets");
            var streak = M("fs_streak", DustC, 1f, 0f); streak.Opacity = 0.9f; streak.NoiseAmt = 0.4f;
            _speed = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(streak), Life0 = 0.3f, Life1 = 0.55f, Speed0 = 8f, Speed1 = 14f, Size0 = 0.2f, Size1 = 0.45f, Radius = 0.4f, Drag = 2f, Stretch = true, StretchVel = 0.09f, StretchLen = 2f, Max = 120 }, root, "Shove");
            var clod = M("fs_ink", Soil, 1f, 0f); clod.NoiseAmt = 0.4f; clod.Soft = 0.1f;
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.06f, Size1 = 0.2f, Radius = 0.3f, Gravity = 2.6f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 500 }, root, "Clods");
            foreach (var ps in new[] { _chunks, _bits, _cloud, _jets, _speed, _clods }) ps.Play(false);
            var cr = M("fs_crack", new Color(0.08f, 0.05f, 0.04f, 1f), 1f, 0f); cr.Soft = 0.001f; cr.NoiseAmt = 0.4f; cr.Erode = 0.3f; cr.Opacity = 0.9f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(cr), out _crackMesh);
            _lights = new Light[2];
            for (int k = 0; k < 2; k++) _lights[k] = Vol2Fs.PointLight(root, "SeamLight" + k, Seam, 4f);
        }

        protected override void OnCharge(float t, float k) { if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_bits, Tip() + Random.insideUnitSphere * 0.4f, 1); }
        protected override void OnLaunch(long nowUs) { }

        protected override void OnHit(long nowUs)
        {
            if (_built) return;
            _built = true;
            var root = Root.transform;
            // a hex grid over the fan: columns rise taller toward the far edge (a stepped plateau), lower near the caster (they must not wall the shoulder camera)
            float sp = 0.56f, rowH = sp * 0.866f;
            Vector3 side = Vector3.Cross(Vector3.up, F);
            int n = 0;
            for (int row = 0; row < 12; row++)
                for (int c = -8; c <= 8; c++)
                {
                    float along = 0.6f + row * rowH, across = (c + (row % 2) * 0.5f) * sp;
                    Vector3 p = Apex + F * along + side * across; p.y = Ground;
                    Vector3 d = p - Apex; d.y = 0f;
                    float dist = d.magnitude, ang = Vector3.Angle(F, d) * Mathf.Deg2Rad;
                    if (dist > Depth * 0.98f || ang > Half * 0.98f || dist < 0.9f) continue;
                    float u = dist / Depth;
                    int seed = Seed + n * 13;
                    var mesh = Kit.SharedMesh("hex" + (n % 6), () => Vol2FsB3.Hex(500 + n % 6));
                    var r = Vol2FsB1.Prop(root, "Col" + n, mesh, Vol2Fs.Hash(seed, 3) < 0.25f ? _basaltSeam : _basalt);
                    float h = Mathf.Lerp(0.25f, 1.55f, Mathf.Clamp01((u - 0.15f) / 0.85f)) * Mathf.Lerp(0.7f, 1.15f, Vol2Fs.Hash(seed, 5));
                    if (Vol2Fs.Hash(seed, 7) < 0.12f) h *= 1.5f;
                    var col = new Col { T = r.transform, Base = p + Vector3.down * 0.05f, H = h, At = u * 0.32f + 0.03f * Vol2Fs.Hash(seed, 9), Rnd = Vol2Fs.Hash(seed, 11) };
                    col.T.SetPositionAndRotation(col.Base, Quaternion.AngleAxis(Vol2Fs.Hash(seed, 13) * 60f, Vector3.up) * Quaternion.Euler((Vol2Fs.Hash(seed, 15) - 0.5f) * 6f, 0f, (Vol2Fs.Hash(seed, 17) - 0.5f) * 6f));
                    col.T.localScale = new Vector3(sp * 1.12f, 0.001f, sp * 1.12f);
                    _cols.Add(col); n++;
                }
            for (int k = 0; k < 2; k++) _lights[k].transform.position = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.7f, k == 0 ? -0.4f : 0.4f, Ground) + Vector3.up * 1.6f;
        }

        void Shove(Struck s)
        {
            Vector3 c = CentreNow(s), away = FlatN(c - Apex);
            Vol2FsB1.Emit(_cloud, c, 3); Vol2FsB1.Emit(_chunks, c, 14); Vol2FsB1.Emit(_clods, c, 30);
            for (int k = 0; k < 12; k++) Vol2FsB1.EmitV(_speed, c + Random.insideUnitSphere * 0.4f, (away + Random.insideUnitSphere * 0.3f) * Random.Range(8f, 14f), -1f, -1f);
        }
        protected override void OnStruck(Struck s) { Shove(s); }
        protected override void OnTouch(Struck s) { if (!Hits.Exists(h => h.Id == s.Id && Mathf.Abs(h.At - s.At) < 0.1f)) Shove(s); }

        protected override void OnResidueEnd()
        {
            if (!NaturalEnd) { foreach (var c in _cols) if (c.Rnd < 0.25f) Vol2FsB1.Emit(_chunks, c.Base + Vector3.up * c.H * 0.6f, 2); return; }
            var root = Root.transform;
            // the earth round the fan lifts as plates (left / right edges and the far arc)
            for (int k = 0; k < 14; k++)
            {
                float v = (k / 13f) * 2f - 1f;
                Vector3 p = Vol2FsB3.InFan(Apex, F, Depth * 1.12f, Half * 1.12f, k % 2 == 0 ? 1f : 0.6f, v, Ground);
                Vector3 o = FlatN(p - Apex);
                if (k % 2 == 1) o = Vector3.Cross(Vector3.up, F) * Mathf.Sign(v);
                var r = Vol2FsB1.Prop(root, "Plate" + k, Kit.SharedMesh("plate" + (k % 3), () => Vol2FsB1.Slab(400 + k % 3, new Vector3(1f, 0.22f, 0.8f), 0.9f)), _rock);
                r.transform.localScale = new Vector3(Mathf.Lerp(0.7f, 1.1f, Vol2Fs.Hash(Seed, k + 70)), 1f, 0.8f);
                _plates.Add((r.transform, p + Vector3.down * 0.2f, Quaternion.LookRotation(o, Vector3.up)));
            }
        }

        protected override void OnLive(float t, float h)
        {
            float warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            float noRes = Res == null && EndRel >= 0f ? Vol2Fs.Smooth(EndRel, EndRel + 0.8f, t) : 0f;
            float sink = NaturalEnd ? Vol2Fs.Smooth(0f, 0.85f, ex) : Vol2Fs.Smooth(0f, 0.4f, ex);
            foreach (var c in _cols)
            {
                float a = h - c.At;
                float g = a <= 0f ? 0f : Vol2Fs.EaseOutBack(a / 0.18f);
                if (a > 0f && a < 0.05f) { Vol2FsB1.Emit(_jets, c.Base, 1); if (c.Rnd < 0.4f) Vol2FsB1.Emit(_chunks, c.Base + Vector3.up * 0.3f, 1); }
                float hh = c.H * g * (1f - noRes);
                Vector3 shake = Vector3.zero;
                if (warn > 0f) shake = new Vector3(Mathf.PerlinNoise(t * 31f, c.Rnd * 9f) - 0.5f, 0f, Mathf.PerlinNoise(c.Rnd * 7f, t * 29f) - 0.5f) * 0.06f * warn;
                if (pulse > 0f) shake += Vector3.up * 0.04f * pulse * Mathf.Sin(t * 60f + c.Rnd * 10f);
                c.T.position = c.Base + shake + Vector3.down * hh * sink * 1.05f;
                c.T.localScale = new Vector3(c.T.localScale.x, Mathf.Max(0.001f, hh), c.T.localScale.z);
                bool on = ex < 0.999f && hh >= 0.002f;
                if (c.T.gameObject.activeSelf != on) c.T.gameObject.SetActive(on);
            }
            if (!Exiting && h > 0.6f && _cols.Count > 0)
            {
                if (Time.frameCount % (warn > 0f ? 1 : 6) == 0) { var c = _cols[Random.Range(0, _cols.Count)]; Vol2FsB1.Emit(_bits, c.Base + Vector3.up * c.H, warn > 0f ? 2 : 1); }
                if (Time.frameCount % 12 == 0) Vol2FsB1.Emit(_cloud, Vol2FsB3.InFan(Apex, F, Depth * 1.05f, Half, 1f, Random.Range(-1f, 1f), Ground), 1);
            }
            if (_plates.Count > 0)
            {
                if (!_exitFx) { _exitFx = true; foreach (var p in _plates) Vol2FsB1.Emit(_cloud, p.Base, 1); }
                float lift = Mathf.Sin(Mathf.Clamp01(ex / 0.85f) * Mathf.PI);
                for (int k = 0; k < _plates.Count; k++)
                {
                    var p = _plates[k]; Vector3 o = p.Rot * Vector3.forward;
                    p.T.SetPositionAndRotation(p.Base + Vector3.up * 0.18f * lift, Quaternion.AngleAxis(-26f * lift, Vector3.Cross(Vector3.up, o)) * p.Rot);
                    p.T.gameObject.SetActive(ex < 0.97f);
                }
            }
            _crackS.Clear();
            float cl = Vol2Fs.Smooth(0f, 0.4f, h) * (1f - Vol2Fs.Smooth(0.6f, 1f, ex)) * (1f - noRes);
            if (cl > 0.01f)
            {
                var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var sd = new List<Vector3>();
                for (int k = 0; k <= 14; k++) { float v = (k / 14f) * 2f - 1f; Vector3 d = Dir(v * Half * 1.1f); var p = Apex + d * Depth * 0.66f; p.y = Ground + 0.02f; pts.Add(p); w.Add(Depth * 0.8f); al.Add(cl); sd.Add(d); }      // r3: starts 1/4 out (the near strips stood up in front of the camera)
                _crackS.Strip(pts, w, sd, Color.white, al, 0.29f, CamPos, 0f, 3f);
            }
            _crackS.Write(_crackMesh);
            for (int k = 0; k < _lights.Length; k++) _lights[k].intensity = 1.6f * Vol2Fs.Smooth(0.2f, 0.6f, h) * (1f - sink) * (1f - noRes) * (0.7f + 0.3f * Mathf.Sin(t * 2.3f + k)) * (1f + pulse);
        }
    }
}
