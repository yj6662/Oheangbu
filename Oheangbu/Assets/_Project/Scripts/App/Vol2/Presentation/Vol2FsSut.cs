using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>숫 (metal, ㅜ ring, 가른다 - the PEAK of the ㅅ book) - free showcase batch 6, concept sut_a (a perfect circle cut deep into the ground and a mirror-steel
    /// edge standing up out of it all the way round like a giant ring blade buried edge-up, black ink brushed across its polished face; the wall on the ring
    /// sheared in two, the crossing bear's armour stripped in a shower of sparks). Executor facts only: the line's launch = a white-gold flash circle spreads
    /// out over the band, the steel band rises out of the gouge racing round from the front, its top edge burning white-gold, ink strokes swept across it;
    /// the 10 s line stands (a glint runs round it; the template's warning: the edge flickers and rings); every Instant (the bear on the band at the launch,
    /// the walker at 1.0 s) = a shower of sparks off the edge where it crossed and a bright glint - its strip is the book's 4.5 s (combat.strip.peakDuration,
    /// read from the strip book, never a look timer); the field note of the cast (a rock / iron gate cut: "in combat - the gate stays shut" in the lab, no gate
    /// stands here) is only noted; the emitter's EndUs = the band snaps with a ring - steel shards flung sideways (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐).</summary>
    public sealed class Vol2FsSut : Vol2FsCutRing
    {
        static readonly Color Gold = new Color(1f, 0.88f, 0.6f);
        ParticleSystem _sparks, _shards, _glints;
        Material _steel;
        Mesh _bandMesh, _edgeMesh, _inkMesh, _flashMesh; readonly Vol2FsStrips _edge = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _flash = new Vol2FsStrips();
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>(); readonly List<Vector2> _uv = new List<Vector2>(); readonly List<int> _i = new List<int>();
        Light _light;
        bool _snapped; string _field = "";
        protected override Color CutGlow => new Color(1.7f, 1.5f, 1.1f);
        protected override float GougeWidth => 0.3f;
        protected override float RaceSeconds => 0.12f;
        protected override float EndSeconds => 0.6f;
        protected override int ArmourShards => 36;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _steel = Kit.Metal(1.7f, 0.15f, new Color(0.06f, 0.05f, 0.03f)) ?? Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.9f);
            var r = Vol2Fs.MeshObject(root, "SteelBand", _steel, out _bandMesh); r.receiveShadows = false;
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 4f, Speed1 = 13f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.5f, Max = 1400 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.9f, new Color(0.05f, 0.06f, 0.08f)), Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 4f, Speed1 = 9f, Size0 = 0.08f, Size1 = 0.2f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 300 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Gold, Color.white, 0.35f), 3.2f, 1f)), Life0 = 0.08f, Life1 = 0.14f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.12f, Size1 = 0.22f, Max = 80 }, root, "Glints");
            foreach (var ps in new[] { _sparks, _shards, _glints }) ps.Play(false);
            var e = M("fs_streak", Gold, 3.6f, 1f); e.NoiseAmt = 0.12f; e.Soft = 0.02f; e.Erode = 0.08f; e.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "Edge", Kit.Mat(e), out _edgeMesh);
            Vol2Fs.MeshObject(root, "InkOnSteel", Kit.InkSolid("fs_stroke2", InkC, Gold * 1.2f, 0.08f, 0.35f, 0.25f, 1.3f), out _inkMesh);      // r3: denser ink (lower cutoff)
            var f = M("fs_streak", Color.white, 3f, 1f); f.NoiseAmt = 0.2f; f.Soft = 0.05f;
            Vol2Fs.MeshObject(root, "FlashRing", Kit.Mat(f), out _flashMesh);
            _light = Vol2Fs.PointLight(root, "SteelLight", Gold, 5f);
        }

        float BandH(float ang) => 0.55f * Mathf.Lerp(0.3f, 1f, Front(ang));
        /// <summary>The band standing now at <paramref name="ang"/>: risen by the race (a short overshoot), down when it snapped.</summary>
        float Rise(float ang, float t) { float s = SincePassed(ang, t); return s < 0f ? 0f : Vol2Fs.EaseOutBack(Mathf.Clamp01(s / 0.12f)); }

        void WriteBand(float t)
        {
            _v.Clear(); _n.Clear(); _uv.Clear(); _i.Clear();
            if (!_snapped)
            {
                int n = 120; float th = 0.02f;
                for (int k = 0; k <= n; k++)
                {
                    float ang = -Mathf.PI + k / (float)n * Mathf.PI * 2f, h = BandH(ang) * Rise(ang, t);
                    Vector3 d = Dir(ang), b = OnBand(ang, R) + Vector3.down * 0.08f, top = b + Vector3.up * (h + 0.08f);
                    // outer face, inner face (two-sided by geometry), each with its own normal
                    _v.Add(b + d * th); _n.Add(d); _uv.Add(new Vector2(k / (float)n * 8f, 0f));
                    _v.Add(top + d * th * 0.2f); _n.Add((d + Vector3.up * 0.3f).normalized); _uv.Add(new Vector2(k / (float)n * 8f, 1f));
                    _v.Add(b - d * th); _n.Add(-d); _uv.Add(new Vector2(k / (float)n * 8f, 0f));
                    _v.Add(top - d * th * 0.2f); _n.Add((-d + Vector3.up * 0.3f).normalized); _uv.Add(new Vector2(k / (float)n * 8f, 1f));
                }
                for (int k = 0; k < n; k++)
                {
                    int a = k * 4, c = a + 4;
                    _i.Add(a); _i.Add(a + 1); _i.Add(c); _i.Add(c); _i.Add(a + 1); _i.Add(c + 1);                    // outer
                    _i.Add(a + 2); _i.Add(c + 2); _i.Add(a + 3); _i.Add(a + 3); _i.Add(c + 2); _i.Add(c + 3);        // inner
                }
            }
            _bandMesh.Clear(); _bandMesh.SetVertices(_v); _bandMesh.SetNormals(_n); _bandMesh.SetUVs(0, _uv); _bandMesh.SetTriangles(_i, 0); _bandMesh.RecalculateBounds();
        }

        protected override void Cut(float t)
        {
            foreach (var f in Inst.Log.FieldNotes) _field = f;
            if (_field.Length > 0) Note += " | field: " + _field;
            _light.transform.position = Apex + F * R + Vector3.up * 0.8f; _light.intensity = 9f;
            for (int k = 0; k < 60; k++) { float a = Random.Range(-Mathf.PI, Mathf.PI); if (Front(a) < 0.3f) continue; Vol2FsB1.EmitV(_sparks, OnBand(a, R) + Vector3.up * BandH(a), Tangent(a) * (a > 0f ? 6f : -6f) + Vector3.up * Random.Range(0.5f, 2.5f), -1f, -1f); }
        }

        protected override void Crossed(Struck s, float ang)
        {
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            Vector3 at = OnBand(ang, R) + Vector3.up * BandH(ang);
            for (int k = 0; k < 160; k++) Vol2FsB1.EmitV(_sparks, at, (Random.insideUnitSphere + Vector3.up * 0.7f + Dir(ang) * 0.5f).normalized * Random.Range(4f, 13f), -1f, -1f);
            Vol2FsB1.EmitV(_glints, at, Vector3.zero, 0.25f, 0.14f); Vol2FsB1.EmitV(_glints, b.center, Vector3.zero, 0.22f, 0.12f);
            _light.transform.position = at; _light.intensity = 10f;
        }

        protected override void Ended()
        {
            if (_snapped) return;
            _snapped = true;
            // it snaps with a ring: shards flung sideways along the ring (the front and sides)
            for (int k = 0; k < 16; k++)
            {
                float ang = -Mathf.PI + (k + 0.5f) / 16f * Mathf.PI * 2f; if (Front(ang) < 0.3f) continue;
                Vector3 at = OnBand(ang, R) + Vector3.up * BandH(ang) * 0.6f, tan = Tangent(ang);
                for (int i = 0; i < 6; i++) Vol2FsB1.EmitV(_shards, at + Random.insideUnitSphere * 0.15f, tan * (i % 2 == 0 ? 1f : -1f) * Random.Range(3f, 7f) + Vector3.up * Random.Range(0.5f, 2.5f), -1f, -1f);
                for (int i = 0; i < 12; i++) Vol2FsB1.EmitV(_sparks, at, (tan * (i % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.5f).normalized * Random.Range(4f, 10f), -1f, -1f);
                Vol2FsB1.EmitV(_glints, at, Vector3.zero, 0.22f, 0.1f);
            }
            HitStop(Now(NowUs), 0.06f);
            _light.transform.position = Apex + F * R + Vector3.up * 0.6f; _light.intensity = 8f;
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            WriteBand(t);
            _edge.Clear(); _ink.Clear(); _flash.Clear();
            float stand = Standing(t), warn = LineWarn(t), flick = Flick(t);
            if (!_snapped && stand > 0.01f)
            {
                int n = 120; var pts = new List<Vector3>(n + 1); var wd = new List<float>(n + 1); var al = new List<float>(n + 1);
                // r2 (the main's r1 sheet: the band read as matte grey rubber): a wide white-gold rim on the top edge, a glint sweeping the WHOLE band
                // (rim + two bright reflection lines on its face, on the side the camera sees - the inside of the front arc)
                float glintAt = Mathf.Repeat(t * 0.9f, 1f) * Mathf.PI * 2f - Mathf.PI;
                var s1 = new List<Vector3>(n + 1); var s2 = new List<Vector3>(n + 1); var sw1 = new List<float>(n + 1); var sw2 = new List<float>(n + 1); var sa = new List<float>(n + 1); var up = new List<Vector3>(n + 1);
                for (int k = 0; k <= n; k++)
                {
                    float ang = -Mathf.PI + k / (float)n * Mathf.PI * 2f, rise = Rise(ang, t), rk = RingK(ang), hh = BandH(ang) * rise;
                    float g = Mathf.Exp(-Mathf.Pow(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, glintAt * Mathf.Rad2Deg) / 28f, 2f));
                    pts.Add(OnBand(ang, R) + Vector3.up * hh); wd.Add((0.06f + 0.08f * g) * rk); al.Add(Mathf.Min(1f, rise) * stand * flick * (0.75f + 0.25f * g));
                    Vector3 face = OnBand(ang, R) - Dir(ang) * 0.035f;
                    s1.Add(face + Vector3.up * hh * 0.68f); s2.Add(face + Vector3.up * hh * 0.32f); up.Add(Vector3.up);
                    sw1.Add(hh * (0.1f + 0.08f * g)); sw2.Add(hh * (0.05f + 0.05f * g)); sa.Add(Mathf.Min(1f, rise) * stand * (0.35f + 0.65f * g));
                }
                _edge.Strip(pts, wd, null, Color.white, al, 0.19f, cam, 0f, 12f);
                _edge.Strip(s1, sw1, up, Color.white, sa, 0.47f, cam, 0f, 9f);
                _edge.Strip(s2, sw2, up, Color.white, sa, 0.81f, cam, 0f, 7f);
                // ink brushed across the polished face (four strokes on the front half, just outside it)
                for (int s = 0; s < 7; s++)
                {
                    float c = (s - 3f) * 0.42f + 0.1f * Mathf.Sin(s * 2.3f), span = 0.34f + 0.08f * (s % 3);
                    int m = 12; var p2 = new Vector3[m]; var w2 = new float[m]; var a2 = new float[m]; var sd = new Vector3[m];
                    for (int k = 0; k < m; k++)
                    {
                        float u = k / (float)(m - 1), ang = c - span * 0.5f + span * u, hh = BandH(ang) * Rise(ang, t);
                        p2[k] = OnBand(ang, R + 0.03f) + Vector3.up * hh * (0.25f + 0.45f * u + 0.1f * (s % 2)); sd[k] = Vector3.up;
                        w2[k] = hh * 0.36f * Mathf.Sin(Mathf.Clamp01(u * 0.9f + 0.05f) * Mathf.PI); a2[k] = stand;
                    }
                    _ink.Strip(p2, w2, sd, Color.white, a2, 0.3f + 0.17f * s, cam, 0f, 1.5f);
                }
                if (warn > 0f && Time.frameCount % 3 == 0) { float a = Random.Range(-1.8f, 1.8f); Vol2FsB1.EmitV(_glints, OnBand(a, R) + Vector3.up * BandH(a), Vector3.zero, 0.18f, 0.08f); }
            }
            // the launch: a halo of white-gold light drops from above onto the band (concept sut_b, folded in: it is the stroke that cuts the circle), then a
            // circle of light spreads out over the band
            float fa = t - LaunchAt;
            if (Launched && fa < 0.3f)
            {
                float fall = Vol2Fs.Smooth(0f, 0.11f, fa), life = 1f - Vol2Fs.Smooth(0.12f, 0.3f, fa);
                Vol2FsB4A.GroundArc(_flash, Apex, Ground + Mathf.Lerp(3.4f, 0.05f, fall), R * Mathf.Lerp(1.15f, 1f, fall), 0.09f, 0f, Mathf.PI * 2f, life, 0.83f, cam, 72);
                Vol2FsB4A.GroundArc(_flash, Apex, Ground + Mathf.Lerp(3.9f, 0.3f, fall), R * Mathf.Lerp(1.2f, 1.02f, fall), 0.04f, 0f, Mathf.PI * 2f, life * 0.4f, 0.37f, cam, 72);
            }
            if (Launched && fa < 0.5f)
            {
                float life = 1f - Vol2Fs.Smooth(0.08f, 0.5f, fa), rr = R + 0.9f * Vol2Fs.Smooth(0f, 0.5f, fa);
                Vol2FsB4A.GroundArc(_flash, Apex, Ground + 0.02f, rr, 0.25f * life + 0.04f, 0f, Mathf.PI * 2f, life, 0.55f, cam, 72);
            }
            _edge.Write(_edgeMesh); _ink.Write(_inkMesh); _flash.Write(_flashMesh);
            _light.intensity = Mathf.MoveTowards(_light.intensity, _snapped ? 0f : stand * 1.6f * flick, Time.deltaTime * 14f);
        }
    }
}
