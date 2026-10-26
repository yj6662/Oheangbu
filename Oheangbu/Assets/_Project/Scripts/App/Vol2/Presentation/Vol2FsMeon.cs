using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>먼 (earth, ㅓ attachment, 번진다) - free showcase batch 4, concept meon_b (the swing smashes the close bear and heavy stones with glowing seams burst off it
    /// and bound on to the other bears in dusty arcs trailing ink, slamming them back; craters at their feet). Unlike 먹 (a stone mace on the brush, 남긴다) the
    /// brush here carries LOOSE gravel: a ring of seamed pebbles orbits the shaft for the attachment host's 6 s (HostLife, rattling in its last 0.4 s). Beats, all
    /// executor facts: the swing's real motion drags dust and a heavy ink stroke -> the "Instant" touch = a dust explosion and rock chunks on that body -> every
    /// "Spread" contact = one big seamed stone thrown out of that body in a high bounding arc (an ink trail behind it) onto the neighbour, its size by the share ->
    /// the Mark (3 s) = the stone half-buried in a cracked crater at the neighbour's feet; warning = the stone trembles -> natural expiry (CSV: 가라앉으며 둘레 땅이
    /// 판으로 들림) = the stone sinks and the ground round it lifts as plates, then settles; other reasons = the stone crumbles.</summary>
    public sealed class Vol2FsMeon : Vol2FsSpread
    {
        static readonly Color DustC = new Color(0.55f, 0.42f, 0.34f, 1f), Soil = new Color(0.3f, 0.18f, 0.12f, 1f), Ink = new Color(0.03f, 0.025f, 0.02f), Seam = new Color(1f, 0.55f, 0.2f);
        ParticleSystem _chunks, _grit, _cloud, _clods, _ring, _speed; Light _seamLight, _hitLight;
        Material _rock, _rockSeam;
        Mesh _inkMesh, _crackMesh, _trailMesh;
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _crack = new Vol2FsStrips(), _trail = new Vol2FsStrips();
        readonly List<(Transform T, float Ang, float U, float S)> _gravel = new List<(Transform, float, float, float)>();
        sealed class Stone { public Transform T; public Vector3 A, B; public float Size, Spin; public Vector3 Axis; public bool Landed; public Light L; }
        sealed class Crater { public Transform Stone; public List<(Transform T, Vector3 Base, Quaternion Rot, Vector3 Out)> Plates = new List<(Transform, Vector3, Quaternion, Vector3)>(); public bool Lifted; public Vector3 Rest; public float Size; }
        // r3 (Phantom Blade Zero reference, principles 2 + 5): the swing as a blade-edged crescent + two phantom afterimages in the body's accent colour
        Mesh _phantomMesh; readonly Vol2FsStrips _phantom = new Vol2FsStrips(); float _lastFast = -9f;
        protected override float HopSpeed => 6.5f;
        protected override float HopMin => 0.45f;
        protected override float HopMax => 0.7f;
        protected override float HopStagger => 0.14f;
        protected override float MarkExpireSeconds => 1.2f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _rock = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.82f, 0.66f, 0.54f), 0f, 0.1f, new Color(0.12f, 0.08f, 0.06f), "fs_rock");      // a little self-light: backlit rock read black
            _rockSeam = Vol2FsB1.Lit(Kit, "fs_rock_crack", new Color(0.72f, 0.57f, 0.47f), 0f, 0.1f, Seam * 1.5f, "fs_vein");
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            for (int k = 0; k < 10; k++)
            {
                var r = Vol2FsB1.Prop(root, "Gravel" + k, Kit.SharedMesh("lump" + (k % 3), () => Vol2FsB1.Rock(70 + k % 3, 0.5f, 0.8f, 9, 7)), k % 2 == 0 ? _rockSeam : _rock, false);
                r.transform.localScale = Vector3.one * 0.001f;
                _gravel.Add((r.transform, k * 2.39996f, 0.35f + 0.6f * (k % 5) / 4f, Mathf.Lerp(0.09f, 0.15f, Vol2Fs.Hash(Seed, k))));
            }
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.9f, Life1 = 1.6f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.1f, Size1 = 0.34f, Radius = 0.3f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 220 }, root, "Chunks");
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.5f, Life1 = 1f, Speed0 = 0.1f, Speed1 = 0.8f, Size0 = 0.025f, Size1 = 0.07f, Radius = 0.08f, Gravity = 2.4f, Spin = 400f, Max = 400 }, root, "Grit");
            _cloud = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.45f)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 1f, Speed1 = 3.5f, Size0 = 0.6f, Size1 = 1.2f, Radius = 0.5f, Drag = 2f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.4f, 0f), Max = 120, SortFudge = 10 }, root, "Cloud");
            var clod = M("fs_ink", Soil, 1f, 0f); clod.NoiseAmt = 0.4f; clod.Soft = 0.1f;
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 7f, Size0 = 0.06f, Size1 = 0.22f, Radius = 0.3f, Gravity = 2.6f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 500 }, root, "Clods");
            var ring = M("fs_ring", DustC, 1f, 0f); ring.Opacity = 0.8f; ring.NoiseAmt = 0.5f; ring.Soft = 0.02f;
            _ring = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ring), Life0 = 0.6f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0f, Size0 = 3.6f, Size1 = 3.6f, Radius = 0.01f, Horizontal = true, SizeStart = 0.1f, SizePeak = 0.85f, SizePeakAt = 0.5f, SizeEnd = 1f, AlphaHoldTo = 0.3f, Max = 8 }, root, "ShockRing");
            var streak = M("fs_streak", DustC, 1f, 0f); streak.Opacity = 0.9f; streak.NoiseAmt = 0.4f;
            _speed = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(streak), Life0 = 0.3f, Life1 = 0.55f, Speed0 = 8f, Speed1 = 14f, Size0 = 0.2f, Size1 = 0.4f, Radius = 0.4f, Drag = 2f, Stretch = true, StretchVel = 0.09f, StretchLen = 2f, Max = 120 }, root, "Shove");
            foreach (var ps in new[] { _chunks, _grit, _cloud, _clods, _ring, _speed }) ps.Play(false);
            Vol2Fs.MeshObject(root, "InkArcs", Kit.InkSolid("fs_stroke2", Ink, new Color(0.9f, 0.45f, 0.15f, 1f), 0.1f, 0.5f, 0.3f, 1.5f), out _inkMesh);
            var tr = M("fs_stroke", DustC, 1f, 0f); tr.Opacity = 0.85f; tr.NoiseAmt = 0.6f; tr.Erode = 0.35f; tr.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "DustTrail", Kit.Mat(tr), out _trailMesh);
            var cr = M("fs_crack", new Color(0.12f, 0.07f, 0.05f, 1f), 1f, 0f); cr.Soft = 0.001f; cr.NoiseAmt = 0.3f; cr.Opacity = 0.9f; cr.Edge = new Color(1.4f, 0.6f, 0.2f, 1f); cr.EdgeWidth = 0.1f;
            Vol2Fs.MeshObject(root, "Craters", Kit.Mat(cr), out _crackMesh);
            _seamLight = Vol2Fs.PointLight(root, "SeamLight", Seam, 2.5f);
            _hitLight = Vol2Fs.PointLight(root, "HitLight", Seam, 5f);
            Vol2Fs.MeshObject(root, "Phantoms", Vol2FsB4A.PhantomMat(Kit, new Color(0.85f, 0.55f, 0.2f), 1.1f), out _phantomMesh);
        }

        protected override void OnCharge(float t, float k) { if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_grit, Tip() + Random.insideUnitSphere * 0.3f, 1); }
        protected override void OnLaunch(long nowUs) { Vol2FsB1.Emit(_clods, Tip(), 16); }
        protected override void OnHit(long nowUs) { }

        void Smash(Vector3 c, Vector3 feet, Vector3 away, float k)
        {
            Vol2FsB1.Emit(_cloud, c + Vector3.up * 0.4f, Mathf.RoundToInt(2 * k));      // r2: half the dust (r1 clouds hid the bears ~1 s) Vol2FsB1.Emit(_chunks, c, Mathf.RoundToInt(30 * k)); Vol2FsB1.Emit(_clods, c, Mathf.RoundToInt(60 * k)); Vol2FsB1.Emit(_grit, c, 40);
            Vol2FsB1.Emit(_ring, G(feet) + Vector3.up * 0.04f, 1);
            for (int i = 0; i < 10; i++) Vol2FsB1.EmitV(_speed, c + Random.insideUnitSphere * 0.4f, (away + Random.insideUnitSphere * 0.25f).normalized * 12f, -1f, -1f);
            _hitLight.transform.position = c; _hitLight.intensity = 6f * k;
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s);
            Smash(c, FeetOf(s), FlatN(FeetOf(s) - Inst.Origin), s.Kind == "Second" ? 0.5f : 1f);
        }

        protected override void OnHop(Hop h)
        {
            var root = Root.transform; float k = ShareScale(h.Share);
            var st = new Stone { Size = 0.8f * k, Spin = 380f + 200f * Vol2Fs.Hash(Seed, (int)h.TargetId), Axis = Random.onUnitSphere };
            var r = Vol2FsB1.Prop(root, "Stone_" + h.TargetId, Kit.SharedMesh("hopstone" + (h.Order % 2), () => Vol2FsB1.Rock(140 + h.Order % 2, 0.5f, 0.85f, 12, 9)), _rockSeam);
            st.T = r.transform; st.T.localScale = Vector3.one * 0.001f;
            st.A = h.FromP + Vector3.up * 0.9f; st.B = CentreNow(h);
            st.L = Vol2Fs.PointLight(root, "StoneLight", Seam, 3f);
            h.Data = st;
        }

        protected override void OnLand(Hop h)
        {
            if (!(h.Data is Stone st)) return;
            st.Landed = true; st.L.intensity = 0f;
            Smash(CentreNow(h), FeetOf(h), FlatN(FeetOf(h) - h.FromP), ShareScale(h.Share));
        }

        protected override void OnMark(Mark m)
        {
            var root = Root.transform; var cr = new Crater { Size = ShareScale(m.Share) };
            // the stone that landed stays half-buried in the crater; plates round it wait for the natural expiry
            if (m.Hop != null && m.Hop.Data is Stone st) cr.Stone = st.T;
            cr.Rest = m.Centre + FlatN(m.Centre - Inst.Origin) * -0.45f;
            for (int k = 0; k < 7; k++)
            {
                float a = k / 7f * Mathf.PI * 2f + Vol2Fs.Hash(Seed + (int)m.Id, k) * 0.5f;
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p = Vol2FsB1.Prop(root, "Plate" + m.Id + "_" + k, Kit.SharedMesh("mplate" + (k % 3), () => Vol2FsB1.Slab(160 + k % 3, new Vector3(0.9f, 0.25f, 0.6f), 0.85f)), k % 3 == 0 ? _rockSeam : _rock);
                Vector3 b = m.Centre + o * (m.Radius * 1.5f * cr.Size); b.y = Ground - 0.3f;
                var rot = Quaternion.LookRotation(o, Vector3.up);
                p.transform.SetPositionAndRotation(b, rot); p.transform.localScale = Vector3.one * (0.8f + 0.4f * Vol2Fs.Hash(Seed, k + 30)) * cr.Size;
                cr.Plates.Add((p.transform, b, rot, o));
            }
            m.Data = cr;
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (!(m.Data is Crater cr) || m.Why == Vol2MaterialEnd.Aborted) return;
            if (Natural(m)) { Vol2FsB1.Emit(_cloud, m.Centre + Vector3.up * 0.3f, 2); Vol2FsB1.Emit(_clods, m.Centre, 30); }
            else { Vol2FsB1.Emit(_chunks, m.Centre + Vector3.up * 0.4f, 16); Vol2FsB1.Emit(_cloud, m.Centre, 2); }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _ink.Clear(); _crack.Clear(); _trail.Clear(); _phantom.Clear();
            _hitLight.intensity = Mathf.MoveTowards(_hitLight.intensity, 0f, Time.deltaTime * 18f);
            // the dress: seamed gravel orbiting the shaft for the host's life, rattling in its warning, dropping off at its end
            float dress = HostLife(t, 0.25f, 0.4f), warn = HostWarn(t);
            Vector3 axis = ShaftAxis; Vector3 n1 = Vector3.Cross(axis, Vector3.up); if (n1.sqrMagnitude < 1e-4f) n1 = Right; n1.Normalize(); Vector3 n2 = Vector3.Cross(axis, n1);
            for (int i = 0; i < _gravel.Count; i++)
            {
                var g = _gravel[i];
                float a = g.Ang + t * (4f + 0.6f * (i % 3));
                Vector3 c = Shaft(g.U) + (n1 * Mathf.Cos(a) + n2 * Mathf.Sin(a)) * (0.16f + 0.06f * (i % 2)) + Random.insideUnitSphere * 0.02f * warn;
                if (dress < 0.99f && EmitterEnd > 0f && t > EmitterEnd) c += Vector3.down * 2.5f * (t - EmitterEnd) * (t - EmitterEnd);
                g.T.SetPositionAndRotation(c, Quaternion.Euler(t * 200f + i * 40f, t * 130f + i * 70f, i * 13f));
                g.T.localScale = Vector3.one * g.S * Mathf.Max(0.001f, dress);
            }
            _seamLight.transform.position = Shaft(0.75f); _seamLight.intensity = 1.6f * dress * (1f - 0.5f * warn);
            if (dress > 0.05f && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_grit, Shaft(Random.Range(0.3f, 1f)), 1);
            if (TipSpeed() > 3f) _lastFast = t;
            if (dress > 0.05f && t - _lastFast < 0.12f) { Vol2FsB4A.BladeTrail(_trail, TipPath, t, 0.25f, 0.5f, dress, 0.4f, cam); Vol2FsB4A.BladeTrail(_ink, TipPath, t, 0.2f, 0.18f, dress, 0.2f, cam); Vol2FsB4A.Phantoms(_phantom, TipPath, t, 0.22f, 0.45f, dress, cam); }
            // the thrown stones: a high bounding arc with an ink trail, spinning; at the landing they settle into the crater (the mark)
            foreach (var hop in Hops)
            {
                if (!(hop.Data is Stone st) || t < hop.Start) continue;
                float g = HopK(hop, t); st.B = CentreNow(hop);
                float lob = 1.5f + 0.3f * hop.Order;
                if (!st.Landed)
                {
                    Vector3 p = Vol2FsB4.ArcPoint(st.A, st.B, lob, g);
                    st.T.SetPositionAndRotation(p, Quaternion.AngleAxis((t - hop.Start) * st.Spin, st.Axis));
                    st.T.localScale = Vector3.one * st.Size * Vol2Fs.Smooth(0f, 0.08f, t - hop.Start);
                    st.L.transform.position = p; st.L.intensity = 3f;
                    if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_grit, p, 2);
                }
                float after = g >= 1f ? t - hop.Start - hop.Travel : 0f;
                float life = 1f - Vol2Fs.Smooth(0.25f, 0.8f, after);
                float tail = g < 1f ? Mathf.Max(0f, g - 0.65f) : Mathf.Min(1f, 0.35f + after * 1.2f);
                Vol2FsB4.Arc(_ink, st.A, st.B, lob, tail, g, 0.42f * ShareScale(hop.Share), life, 0.3f + hop.Order * 0.21f, cam, 16);
            }
            // the marks: the stone half-buried in a cracked crater; trembling in the warning; natural expiry sinks it and the plates round it lift, then settle
            foreach (var m in MarkList)
            {
                if (!(m.Data is Crater cr)) continue;
                float age = MarkAge(m, t); if (age < 0f) continue;
                float ex = MarkExit(m, t), warn2 = MarkWarn(m, t);
                float sl = Vol2Fs.Smooth(0f, 0.25f, age) * (1f - ex);
                if (sl > 0.01f) _crack.Quad(m.Centre + Vector3.up * 0.02f, Right, Fwd, 2.6f * cr.Size, new Color(1f, 1f, 1f, sl), 0.25f + m.Id % 7 * 0.1f);
                if (cr.Stone != null)
                {
                    bool natural = Natural(m) && m.EndAt >= 0f;
                    float sink = natural ? Vol2Fs.Smooth(0f, 0.7f, ex) : 0f;
                    float settle = Vol2Fs.Smooth(0f, 0.2f, age);
                    Vector3 rest = m.Centre + Vector3.up * (0.12f * cr.Size - sink * 0.6f * cr.Size);
                    Vector3 from = m.Hop != null && m.Hop.Data is Stone st0 ? st0.B : rest;
                    Vector3 p = Vector3.Lerp(from, rest, settle) + Random.insideUnitSphere * 0.04f * warn2;
                    cr.Stone.position = p;
                    if (!natural && m.EndAt >= 0f) cr.Stone.localScale = Vector3.one * Mathf.Max(0.001f, 0.8f * cr.Size * (1f - ex));
                }
                // the plates: only on the natural expiry (CSV) - lift outward (0..0.35 of the exit) then settle back
                bool lift = Natural(m) && m.EndAt >= 0f;
                foreach (var pl in cr.Plates)
                {
                    float up = lift ? Vol2Fs.Smooth(0.05f, 0.35f, ex) * (1f - Vol2Fs.Smooth(0.6f, 1f, ex)) : 0f;
                    Vector3 pos = pl.Base + Vector3.up * (0.3f * up + (up > 0f ? 0.28f : 0f));
                    if (!lift) pos = pl.Base;
                    pl.T.SetPositionAndRotation(pos, pl.Rot * Quaternion.Euler(-38f * up, 0f, 0f));
                }
                if (lift && !cr.Lifted && ex > 0.05f) { cr.Lifted = true; Vol2FsB1.Emit(_clods, m.Centre, 40); Vol2FsB1.Emit(_cloud, m.Centre, 3); }
                if (ex <= 0f && Time.frameCount % 8 == (int)(m.Id % 8)) Vol2FsB1.Emit(_grit, m.Centre + Vector3.up * 0.3f, 1);
            }
            _ink.Write(_inkMesh); _crack.Write(_crackMesh); _trail.Write(_trailMesh); _phantom.Write(_phantomMesh);
        }
    }
}
