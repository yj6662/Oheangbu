using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>금>토 옮겨붙음 (metal catches onto the enemy's earth) - free showcase batch 8, concept me_a (the slabs crack open with a flash and straight bright
    /// steel blades of the caster's own stand up out of every crack; a wide ink swirl round them). Facts only: at the commit the enemy's earth patch and the
    /// metal shape are consumed and a metal CHILD (mine) stands in the patch's footprint for its live 10 s. The look: white-gold crack lines race across the
    /// patch from the side the steel came in, the enemy's slabs burst apart and sink, and from the cracks a radiant cluster of tall mirror-steel spikes shoots
    /// up (centre tallest, rim leaning out), each tip flashing with sparks, a black ink crescent sweeping round the cluster; the steel then stands, glints
    /// running up its edges, dimming over its life (monotone) and in its last second. Expired = the blades snap at their feet with shards flung sideways
    /// (CSV 금 끝); Consumed / ChildConsumed = they dull and sink in place in 0.3 s.</summary>
    public sealed class Vol2FsRxMetalEarth : Vol2FsReact
    {
        static readonly Color Gold = new Color(1f, 0.86f, 0.55f), White = new Color(1f, 0.96f, 0.86f);
        sealed class Spike { public Transform T; public Vector3 Foot; public Quaternion Rot; public float H, At, Snap; public Vector3 Out; }
        readonly List<Spike> _spikes = new List<Spike>();
        readonly List<(Vector3 A, Vector3 B, float At)> _cracks = new List<(Vector3, Vector3, float)>();
        Mesh _crackMesh, _inkMesh, _flashMesh;
        readonly Vol2FsStrips _crack = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _flash = new Vol2FsStrips();
        ParticleSystem _sparks, _glint, _shards, _dust, _rocks;
        Light _light;
        bool _snapped;

        protected override float Burst => 1.8f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            bool dark = EnemyIgniter;
            // r3: darker reflective chrome with a gold hot edge (r2 bright 1.2 read as flat white paper)
            var steel = Kit.Metal(dark ? 0.6f : 0.78f, dark ? 0f : 0.3f, dark ? new Color(0.01f, 0.006f, 0.014f) : Gold * 0.03f, dark ? new Color(0.7f, 0.66f, 0.78f) : new Color(0.72f, 0.74f, 0.8f)) ?? Vol2FsB4.Steel(Kit, 1f);
            var spikeMesh = Kit.SharedMesh("rxme_spike", () => Vol2FsB6.Spike(0.26f, 0.06f, 0.22f, 0.04f));
            // the cracks: from the entry rim across the patch to the far side, and between the enemy's slab feet (the blades rise from them)
            var slabs = new List<(Vector3 Foot, Vector3 Out)>();
            Vol2FsRxTransferKit.EarthSlabs(V.ContactId, Radius, slabs);
            Vector3 entry = G(P - Dir * Radius);
            foreach (var s in slabs)
            {
                Vector3 f = G(P + s.Foot);
                _cracks.Add((entry, f, 0.02f + (f - entry).magnitude / 9f));
                _cracks.Add((f, G(P + s.Foot + s.Out * Radius * 1.4f), 0.08f + (f - entry).magnitude / 9f));
            }
            // the spikes: a radiant cluster - along each crack and at its foot, centre tallest, the rim leaning out
            // r2: 1.7x wider and higher, more blades (r1: a ~1 m tuft beside the bear)
            int n = 28; const float S = 1.7f;
            for (int k = 0; k < n; k++)
            {
                Vector3 foot; Vector3 outw;
                if (k < slabs.Count) { foot = slabs[k].Foot * 0.85f * S; outw = slabs[k].Out; }
                else
                {
                    float a = Vol2Fs.Hash(Seed, k) * Mathf.PI * 2f, r = Radius * S * (0.1f + 0.85f * Mathf.Sqrt(Mathf.Max(0f, Vol2Fs.Hash(Seed, k + 30))));
                    outw = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); foot = outw * r;
                }
                float r01 = Mathf.Clamp01(foot.magnitude / Mathf.Max(0.1f, Radius * S));
                float h = Mathf.Lerp(3.2f, 1.1f, r01) * (0.75f + 0.45f * Vol2Fs.Hash(Seed, k + 60));
                Vector3 axis = (Vector3.up + outw * (0.12f + 0.55f * r01)).normalized;
                var go = Vol2FsB1.Prop(root, "Blade" + k, spikeMesh, steel);
                var sp = new Spike { T = go.transform, Foot = G(P + foot) + Vector3.down * 0.05f, Rot = Quaternion.FromToRotation(Vector3.up, axis) * Quaternion.Euler(0f, Vol2Fs.Hash(Seed, k + 90) * 180f, 0f), H = h,
                    At = 0.06f + 0.18f * r01 + 0.04f * Vol2Fs.Hash(Seed, k + 120), Snap = 0.25f * Vol2Fs.Hash(Seed, k + 150), Out = outw };
                sp.T.localScale = Vector3.zero;
                _spikes.Add(sp);
            }
            // r3: a dark gouged crack ring with a gold edge (r2 a white ring at the blades' feet)
            Vol2Fs.MeshObject(root, "DarkCracks", Kit.InkSolid("fs_crack", InkC, Gold * (dark ? 0.6f : 1.8f), 0.18f, 0.45f, 0.3f, 1.2f), out _crackMesh);
            Vol2Fs.MeshObject(root, "InkSwirl", Kit.InkSolid("fs_stroke2", InkC, Gold * 1.3f, 0.12f, 0.5f, 0.35f, 1.2f), out _inkMesh);
            var fl = M("fs_streak", Gold, dark ? 1.0f : 2.0f, 1f); fl.NoiseAmt = 0.2f; fl.Soft = 0.03f;
            Vol2Fs.MeshObject(root, "EdgeFlash", Kit.Mat(fl), out _flashMesh);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.2f, Life1 = 0.55f, Speed0 = 4f, Speed1 = 12f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.9f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 900 }, root, "Sparks");
            _glint = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 4f, 1f)), Life0 = 0.15f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.08f, Size1 = 0.18f, SizeStart = 0.2f, SizePeak = 1f, SizePeakAt = 0.3f, SizeEnd = 0f, Max = 200 }, root, "Glints");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = steel, Mesh = Kit.SharedMesh("rxme_shard", () => Vol2FsB1.Shard(77)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.2f, Gravity = 2.2f, Drag = 0.4f, Spin = 900f, Max = 300 }, root, "Shards");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.55f, 0.44f, 0.36f, 1f), 0.5f)), Life0 = 0.7f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.4f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.7f, Drag = 1.5f, Max = 200 }, root, "Dust");
            _rocks = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.5f, 0.43f, 0.43f), 0f, 0.12f), Mesh = Kit.SharedMesh("rxme_rock", () => Vol2FsB1.Rock(533, 0.5f)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2.5f, Speed1 = 6f, Size0 = 0.06f, Size1 = 0.16f, Gravity = 2.4f, Drag = 0.3f, Spin = 500f, Max = 120 }, root, "Rocks");
            foreach (var ps in new[] { _sparks, _glint, _shards, _dust, _rocks }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "SteelLight", Gold, 7f); _light.transform.position = P + Vector3.up * 1.5f;
            Vector3 c = G(P) + Vector3.up * 0.3f;
            for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_sparks, entry + Vector3.up * 0.3f, (Random.onUnitSphere + Vector3.up * 0.4f + Dir * 0.6f).normalized * Random.Range(4f, 11f), -1f, -1f);
            for (int k = 0; k < 24; k++) Vol2FsB1.EmitV(_rocks, c + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.8f).normalized * Random.Range(2.5f, 6f), -1f, -1f);
            for (int k = 0; k < 16; k++) Vol2FsB1.EmitV(_dust, G(P) + Random.insideUnitSphere * Radius + Vector3.up * 0.2f, Random.insideUnitSphere * 2f + Vector3.up * 0.5f, -1f, -1f);
            HitStop(0f, 0.08f);
        }

        protected override void OnChildEnd(Vol2MaterialEnd why, float t)
        {
            if (!Vol2FsRxTransferKit.Natural(why) || _snapped) return;
            _snapped = true;
            foreach (var s in _spikes)
            {
                if (s.T == null) continue;
                Vector3 mid = s.Foot + s.Rot * Vector3.up * s.H * 0.4f;
                Vector3 side = Vector3.Cross(Vector3.up, s.Out);
                for (int k = 0; k < 4; k++) Vol2FsB1.EmitV(_shards, mid + Random.insideUnitSphere * 0.15f, (side * (Random.value < 0.5f ? -1f : 1f) * Random.Range(2f, 6f) + Vector3.up * Random.Range(1f, 3f)), -1f, -1f);
                for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_sparks, s.Foot + Vector3.up * 0.1f, (side * (Random.value < 0.5f ? -1f : 1f) + Vector3.up * 0.4f).normalized * Random.Range(3f, 8f), -1f, -1f);
            }
            HitStop(t, 0.05f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float exitK = Vol2FsRxTransferKit.Exit01(t, ChildEndAt, ChildWhy);
            bool natural = ChildEndAt >= 0f && Vol2FsRxTransferKit.Natural(ChildWhy);
            float left = HasChild && ChildExpireRel > 0f ? ChildExpireRel - t : 10f;
            float shine = Mathf.Lerp(1f, 0.55f, Vol2Fs.Smooth(0.4f, 4f, t)) * Mathf.Lerp(0.45f, 1f, Vol2Fs.Smooth(0f, 1f, left)) * (1f - exitK);
            // the enemy's slabs burst apart and sink under the rising steel
            if (Patch != null)
            {
                float k = Vol2Fs.Smooth(0.03f, 0.5f, t);
                Patch.Pose(Mathf.Max(0.02f, 1f - 0.85f * k), k * 0.35f, 1f + 0.35f * k);
                if (t > 0.9f && Patch.Go != null && Patch.Go.activeSelf) Patch.SetActive(false);
            }
            bool rising = false;
            foreach (var s in _spikes)
            {
                if (s.T == null) continue;
                if (!HasChild) { s.T.localScale = Vector3.zero; continue; }
                float a = (t - s.At) / 0.11f;
                if (a <= 0f) { s.T.localScale = Vector3.zero; continue; }
                float g = Vol2Fs.EaseOutBack(Mathf.Clamp01(a));
                if (a < 1f) rising = true;
                if (a >= 1f && a < 1.15f) { Vector3 tip = s.Foot + s.Rot * Vector3.up * s.H; for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_sparks, tip, Random.onUnitSphere * Random.Range(2f, 6f) + Vector3.up, -1f, -1f); Vol2FsB1.EmitV(_glint, tip, Vector3.zero, -1f, -1f); }
                float h = s.H * g, w = 1f;
                Vector3 pos = s.Foot;
                if (ChildEndAt >= 0f)
                {
                    if (natural) { float e = Mathf.Clamp01((exitK - s.Snap) / 0.4f); h *= 1f - e; pos += Vector3.down * 0.05f * e; }
                    else { w = 1f - exitK; h *= 1f - exitK; pos += Vector3.down * 0.3f * exitK; }
                }
                s.T.SetPositionAndRotation(pos, s.Rot);
                s.T.localScale = new Vector3(w * (1.1f + 0.5f * Mathf.Min(1.5f, s.H)), Mathf.Max(0.001f, h), w * (1.1f + 0.5f * Mathf.Min(1.5f, s.H)));
                // a glint running up the edge now and then
                if (a > 1.2f && shine > 0.05f && Random.value < 0.012f * shine) Vol2FsB1.EmitV(_glint, s.Foot + s.Rot * Vector3.up * s.H * Random.Range(0.3f, 0.95f), Vector3.zero, -1f, -1f);
            }
            _crack.Clear(); _ink.Clear(); _flash.Clear();
            float crackLife = (HasChild ? Mathf.Lerp(1f, 0.35f, Vol2Fs.Smooth(0.5f, 3f, t)) : 1f - Vol2Fs.Smooth(0.3f, 0.6f, t)) * (1f - exitK);
            int i = 0;
            foreach (var c in _cracks) { Vol2FsB6.Crack(_crack, c.A, c.B, Ground, 0.14f, 0f, Mathf.Clamp01((t - c.At) / 0.12f), crackLife, 0.15f * i, Seed + i * 7, 0.08f, 0.03f); i++; }
            // the ink crescent sweeping round the cluster (0.05 - 0.9 s), widening then thinning
            float sweep = Vol2Fs.Smooth(0.04f, 0.32f, t), inkLife = 1f - Vol2Fs.Smooth(0.5f, 1.1f, t);
            if (inkLife > 0.01f)
            {
                int m = 26; var pts = new Vector3[m]; var w2 = new float[m]; var al = new float[m];
                float a0 = Mathf.Atan2(Dir.z, Dir.x) + Mathf.PI * 0.7f;
                for (int k = 0; k < m; k++)
                {
                    float u = k / (float)(m - 1), ang = a0 + u * Mathf.PI * 1.7f * sweep;
                    float rr = Radius * (2.2f + 0.4f * u);
                    pts[k] = G(P) + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rr + Vector3.up * (0.3f + 1.6f * u * u);
                    w2[k] = 0.5f * Mathf.Sin(Mathf.Clamp01(u * 0.95f + 0.04f) * Mathf.PI) * (0.6f + 0.4f * sweep); al[k] = inkLife;
                }
                _ink.Strip(pts, w2, null, Color.white, al, 0.37f, cam);
            }
            // the first flash across the cluster (a thin-to-wide white-gold slash, Phantom Blade principle 2)
            float fLife = 1f - Vol2Fs.Smooth(0.05f, 0.4f, t);
            if (fLife > 0.01f && HasChild) Vol2FsB6.Slash(_flash, G(P - Side * Radius * 2.2f) + Vector3.up * 0.5f, G(P + Side * Radius * 2.2f) + Vector3.up * 2.4f, Vector3.up * 0.5f - Dir * 0.3f, 0.32f, Vol2Fs.Smooth(0f, 0.08f, t), fLife, 0.4f, cam);
            _crack.Write(_crackMesh); _ink.Write(_inkMesh); _flash.Write(_flashMesh);
            if (_light != null) _light.intensity = 4f * shine * (HasChild ? 1f : 0f) + (t < 0.15f ? 12f * (1f - t / 0.15f) : 0f) + (rising ? 3f : 0f);
        }
    }
}
