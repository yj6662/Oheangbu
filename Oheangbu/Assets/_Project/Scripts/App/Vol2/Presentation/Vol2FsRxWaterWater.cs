using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>수>수 비화 - free showcase batch 8, concept aa_a (deepening current): the water shape pours into the enemy's dark pool and becomes a swirling inner
    /// current - the pool spreads to ~2.7 m across and deepens, teal currents wheel round it in parallel arcs over dark ink swirls, a low standing wave lip with
    /// foam and spray runs round its rim. It stays the enemy's pool (violet-black at its heart). The current turns for the thickened patch's renewed life,
    /// slowing toward its end; Expired = the current runs down, the lip settles and the pool shrinks back and drains; any other end = it collapses at once.</summary>
    public sealed class Vol2FsRxWaterWater : Vol2FsReact
    {
        readonly Vol2FsStrips _cur = new Vol2FsStrips(), _dark = new Vol2FsStrips(), _lip = new Vol2FsStrips(), _vortex = new Vol2FsStrips();
        Mesh _curMesh, _darkMesh, _lipMesh, _vortexMesh; Light _teal; ParticleSystem _splash, _spray, _foam; Vol2FsRxSameFx _fx;
        float _phase; bool _drained;

        protected override void OnBuild()
        {
            _fx = new Vol2FsRxSameFx(Kit, Root.transform, AccA, 4f);
            Vol2Fs.MeshObject(Root.transform, "SameCurrent", Vol2FsB1.Water(Kit, 2.2f, 0.55f, "fs_swirl"), out _curMesh);
            var ink = M("fs_swirl", new Color(0.09f, 0.05f, 0.13f, 1f), 1f, 0f); ink.NoiseAmt = 0.35f; ink.Erode = 0.25f; ink.Soft = 0.2f; ink.Scroll = new Vector2(-0.8f, 0f);
            Vol2Fs.MeshObject(Root.transform, "SameDark", Kit.Mat(ink), out _darkMesh);
            Vol2Fs.MeshObject(Root.transform, "SameLip", Vol2FsB1.Water(Kit, 1.5f, 0.4f, "fs_water"), out _lipMesh);
            // r3: the merge is a teal VORTEX drawn over the acting glyph's own white splash (r2: the merge point read as a white popcorn burst)
            Vol2Fs.MeshObject(Root.transform, "SameVortex", Vol2FsB1.Water(Kit, 2.8f, 0.7f, "fs_swirl"), out _vortexMesh);
            _teal = Vol2Fs.PointLight(Root.transform, "SameTeal", new Color(0.3f, 0.85f, 0.95f), 8f); _teal.transform.position = P + Vector3.up * 0.9f;
            // r2: teal water jets, no white puff sheets (r1: a white popcorn splash - the batch 5 / 7 mistake)
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.3f, 0.82f, 0.9f), 2.8f, 0.75f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.07f, Size1 = 0.14f, Gravity = 1.8f, Drag = 0.5f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 700 }, Root.transform, "SameSplash");
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.4f, 0.88f, 0.95f), 2.6f, 0.75f)), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0.6f, Speed1 = 2f, Size0 = 0.04f, Size1 = 0.08f, Gravity = 2f, Max = 500 }, Root.transform, "SameSpray");
            var foam = M("fs_puff", new Color(0.55f, 0.85f, 0.88f, 1f), 1.1f, 0.3f); foam.Opacity = 0.3f; foam.NoiseAmt = 0.6f; foam.Erode = 0.35f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 0.1f, Speed1 = 0.4f, Size0 = 0.12f, Size1 = 0.24f, SizeEnd = 1.3f, Max = 200 }, Root.transform, "SameFoam");
            foreach (var ps in new[] { _splash, _spray, _foam }) ps.Play(false);
            // the merge: water jets fan out low over the widening pool (a ring, not a fountain)
            for (int k = 0; k < 260; k++) { float a = Random.value * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); Vol2FsB1.EmitV(_splash, G(P + o * Radius * 0.6f) + Vector3.up * 0.15f, o * Random.Range(3f, 7f) + Vector3.up * Random.Range(1f, 3.5f), -1f, -1f); }
            HitStop(0f, 0.06f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float pop = Vol2FsRxSameFx.Pop(t, 0.22f), exit = Vol2FsRxSameFx.Exit(t, ContactEndAt, ContactWhy, 1.2f), near = Vol2FsRxSameFx.Near(t, ContactExpireRel, 2.5f);
            float spread = 1f + 1.5f * Mathf.Clamp01(pop) * (1f - exit);      // r2: x2.5 across (r1 x1.7 read ~1 m)
            if (Patch != null) Patch.Pose(1f, 0f, Mathf.Max(0.05f, spread * (1f - 0.9f * exit)));
            // the current: fast at the merge, steady after, slowing toward the end of the renewed life
            float speed = (1.1f + 2.4f * (1f - Vol2Fs.Smooth(0.1f, 0.9f, t))) * (1f - 0.55f * near) * (1f - 0.8f * exit);
            _phase += speed * Mathf.Max(0f, Time.deltaTime);
            float R = Radius * spread, life = Mathf.Clamp01(pop) * (1f - exit);
            _cur.Clear(); _dark.Clear(); _lip.Clear();
            if (life > 0.01f)
            {
                for (int k = 0; k < 6; k++)
                {
                    float r = R * (0.3f + 0.13f * k), w = 0.2f + 0.05f * k;
                    Vol2FsB4A.GroundArc(_cur, P, Ground + 0.035f + 0.004f * k, r, w, _phase * (1.25f - 0.12f * k) + k * 1.3f, Mathf.PI * (0.8f + 0.12f * k), life * (0.85f - 0.08f * k), 0.17f * k, cam, 30);
                    Vol2FsB4A.GroundArc(_cur, P, Ground + 0.035f + 0.004f * k, r, w * 0.8f, _phase * (1.25f - 0.12f * k) + k * 1.3f + Mathf.PI, Mathf.PI * 0.55f, life * 0.6f, 0.5f + 0.1f * k, cam, 24);
                }
                for (int k = 0; k < 3; k++) Vol2FsB4A.GroundArc(_dark, P, Ground + 0.025f, R * (0.12f + 0.1f * k), 0.22f, -_phase * 0.9f + k * 2.1f, Mathf.PI * 1.1f, life * 0.9f, 0.3f * k, cam, 24);
                // the standing wave lip round the rim: a low vertical ribbon of water, swelling as it runs round
                int n = 56; var pts = new Vector3[n + 1]; var wd = new float[n + 1]; var al = new float[n + 1]; var sd = new Vector3[n + 1];
                for (int i = 0; i <= n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f, sw = 0.5f + 0.5f * Mathf.Sin(a * 3f - _phase * 2.2f);
                    Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float hgt = (0.28f + 0.4f * sw) * life;
                    pts[i] = G(P + o * R * 1.02f) + Vector3.up * (hgt * 0.5f + 0.02f); sd[i] = Vector3.up; wd[i] = hgt; al[i] = life * (0.55f + 0.45f * sw);
                }
                _lip.Strip(pts, wd, sd, Color.white, al, 0.42f, cam);
                for (int q = 0; q < 2; q++) if (t > 0.2f && Random.value < 0.9f * life)
                {
                    float a = Random.value * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vol2FsB1.EmitV(_spray, G(P + o * R) + Vector3.up * 0.4f, o * 0.8f + Vector3.up * Random.Range(1.2f, 2.8f) + new Vector3(-o.z, 0f, o.x) * 1.6f, -1f, -1f);
                    if (Random.value < 0.5f) Vol2FsB1.EmitV(_foam, G(P + o * R * 0.98f) + Vector3.up * 0.08f, new Vector3(-o.z, 0f, o.x) * 0.6f, -1f, -1f);
                }
            }
            if (exit > 0f && !_drained) { _drained = true; for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_foam, G(P + Random.insideUnitSphere * R) + Vector3.up * 0.06f, Vector3.up * 0.1f, -1f, -1f); }
            // the vortex: three water ribbons wheel up round the merge point and fall back into the pool (0 - 0.95 s), fast at first
            _vortex.Clear();
            float vl = Vol2Fs.Smooth(0f, 0.06f, t) * (1f - Vol2Fs.Smooth(0.55f, 0.95f, t)) * (1f - exit);
            if (vl > 0.01f)
            {
                float hgt = 2.0f * Vol2Fs.Smooth(0f, 0.3f, t) * (1f - 0.6f * Vol2Fs.Smooth(0.45f, 0.95f, t));
                for (int k = 0; k < 3; k++)
                {
                    int n = 24; var pts = new Vector3[n]; var wd = new float[n]; var al = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        float u = i / (float)(n - 1), a = k * 2.094f + u * 5.5f + t * 9f;
                        float rr = Mathf.Lerp(Radius * 1.6f, Radius * 0.35f, u);
                        pts[i] = G(P) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rr + Vector3.up * (0.1f + hgt * u);
                        wd[i] = (0.32f - 0.2f * u); al[i] = vl * (0.4f + 0.6f * Mathf.Sin(u * Mathf.PI));
                    }
                    _vortex.Strip(pts, wd, null, Color.white, al, 0.21f * k, cam);
                }
            }
            _vortex.Write(_vortexMesh);
            if (_teal != null) _teal.intensity = 5f * vl + 1.2f * life;
            _cur.Write(_curMesh); _dark.Write(_darkMesh); _lip.Write(_lipMesh);
            _fx.Draw(t, P, Dir, Ground, Radius, R, cam, 0.9f);
        }
    }
}
