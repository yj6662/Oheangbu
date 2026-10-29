using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>토>화 옮겨붙음 (earth catches onto the enemy's fire) - free showcase batch 8, concept ef_a (falling earth smothers the enemy's flames, smoke billows,
    /// flat stepped slabs of earth settle in their forked shape). Facts only: at the commit the enemy's fire patch and the earth shape are consumed and an earth
    /// CHILD (mine) lies in the patch's footprint for its live 10 s. The look: a slam of grit and dust and flat black ink strokes spraying low over the ground, the
    /// enemy's flames choked flat under it (they stop and sink), then flat stepped earth plates drop and settle in the fire's FORKED shape - three tongues of stone
    /// fanning from the centre the way the flames forked - hot cracks glowing between them and cooling, smoke wisps thinning (monotone). Expired = the plates
    /// sink back into the ground, the ground lifting in a ring of dust; Consumed / ChildConsumed = they dry pale and go in 0.3 s.</summary>
    public sealed class Vol2FsRxEarthFire : Vol2FsReact
    {
        static readonly Color Ember = new Color(1f, 0.45f, 0.12f), Amber = new Color(0.95f, 0.66f, 0.3f);
        sealed class Plate { public Transform T; public Vector3 Rest; public Quaternion Rot; public float At, Drop; public Vector3 Scale; }
        readonly List<Plate> _plates = new List<Plate>();
        readonly List<(Vector3 A, Vector3 B)> _cracks = new List<(Vector3, Vector3)>();
        Mesh _crackMesh, _inkMesh, _ringMesh;
        readonly Vol2FsStrips _crack = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _ring = new Vol2FsStrips();
        List<Vol2FsB1.Stroke> _burst;
        ParticleSystem _dust, _grit, _smoke, _embers, _rocks;
        Light _light;
        bool _landed; float _landAt = -1f;

        protected override float Burst => 1.8f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            bool dark = EnemyIgniter;
            // r3: lighter warm stone with glowing veins (r2 dark on the dark ground)
            var stone = Vol2FsB1.Lit(Kit, "fs_rock", dark ? new Color(0.3f, 0.26f, 0.27f) : new Color(1f, 0.86f, 0.66f), 0f, 0.12f, dark ? default : new Color(1.6f, 0.6f, 0.18f), "fs_rock_crack");
            // the forked shape: three tongues of stone fanning from the centre (the flames' forks), stepped - big plates near the root, smaller toward the tips
            int forks = 3; float spread = 1.15f;
            float baseAng = Mathf.Atan2(Dir.z, Dir.x) + Mathf.PI;          // the forks open toward the camera side, away from the incoming direction
            int idx = 0;
            for (int f = 0; f < forks; f++)
            {
                float ang = baseAng + (f - 1) * spread * 0.5f + (Vol2Fs.Hash(Seed, f) - 0.5f) * 0.2f;
                Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                for (int s = 0; s < 4; s++)
                {
                    float u = (s + 0.4f) / 4f;
                    // r2: 1.8x wider forks, bigger stepped plates (r1: ~1 m, lost under the dust)
                    Vector3 foot = d * Radius * 1.9f * u + Vector3.Cross(Vector3.up, d) * (Vol2Fs.Hash(Seed, f * 9 + s) - 0.5f) * 0.3f;
                    float size = Mathf.Lerp(1.15f, 0.55f, u);
                    var mesh = Kit.SharedMesh("rxef_slab" + ((Seed + idx) % 9), () => Vol2FsB1.Slab(400 + (Seed + idx) % 9, Vector3.one, 0.8f));
                    var r = Vol2FsB1.Prop(root, "Plate" + idx, mesh, stone);
                    var pl = new Plate { T = r.transform, Rest = G(P + foot) + Vector3.down * 0.02f, Rot = Quaternion.LookRotation(d, Vector3.up) * Quaternion.Euler(-6f - 6f * s, (Vol2Fs.Hash(Seed, idx + 40) - 0.5f) * 40f, (Vol2Fs.Hash(Seed, idx + 50) - 0.5f) * 10f),
                        Scale = new Vector3(size, 0.22f + 0.32f * (1f - u) + 0.1f * Vol2Fs.Hash(Seed, idx + 60), size * 0.85f), At = 0.04f + 0.04f * s + 0.02f * f, Drop = 0.5f + 0.3f * Vol2Fs.Hash(Seed, idx + 70) };
                    pl.T.localScale = Vector3.zero;
                    _plates.Add(pl);
                    if (s > 0) _cracks.Add((_plates[_plates.Count - 2].Rest, pl.Rest));
                    idx++;
                }
                _cracks.Add((G(P), _plates[_plates.Count - 4].Rest));
            }
            var crackM = M("fs_crack", Ember, 3.4f, 0.95f); crackM.NoiseAmt = 0.35f; crackM.Soft = 0.05f; crackM.Erode = 0.15f;
            Vol2Fs.MeshObject(root, "HotCracks", Kit.Mat(crackM), out _crackMesh);
            Vol2Fs.MeshObject(root, "InkSpray", Kit.InkSolid("fs_stroke2", InkC, Amber * 1.2f, 0.12f, 0.5f, 0.35f, 1.3f), out _inkMesh);
            var ring = M("fs_puff", new Color(0.82f, 0.68f, 0.52f), 1.1f, 0.1f); ring.Opacity = 0.9f; ring.NoiseAmt = 0.55f; ring.Erode = 0.3f;
            Vol2Fs.MeshObject(root, "DustRing", Kit.Mat(ring), out _ringMesh);
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.55f, 0.42f, 0.32f, 1f), 0.6f)), Life0 = 0.8f, Life1 = 1.8f, Speed0 = 0.6f, Speed1 = 3.5f, Size0 = 0.35f, Size1 = 0.75f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.5f, Drag = 1.6f, Max = 400 }, root, "Dust");
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.35f, 0.27f, 0.2f), 1f, 0f)), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.03f, Size1 = 0.07f, Gravity = 2.4f, Drag = 0.4f, Max = 600 }, root, "Grit");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.32f)), Life0 = 1.0f, Life1 = 1.8f, Speed0 = 0.3f, Speed1 = 0.9f, Size0 = 0.3f, Size1 = 0.6f, SizeStart = 0.5f, SizePeak = 1.1f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 0.55f, 0f), Noise = 0.5f, NoiseFreq = 0.8f, Max = 300 }, root, "Smoke");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Ember, 4f, 1f)), Life0 = 0.5f, Life1 = 1.2f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.02f, Size1 = 0.05f, Gravity = -0.3f, Drag = 0.9f, Noise = 1f, NoiseFreq = 1.4f, Max = 400 }, root, "Embers");
            _rocks = Vol2Fs.Build(new Vol2FsPs { Mat = stone, Mesh = Kit.SharedMesh("rxef_rock", () => Vol2FsB1.Rock(431, 0.5f)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.06f, Size1 = 0.18f, Gravity = 2.6f, Drag = 0.3f, Spin = 500f, Max = 160 }, root, "Rocks");
            foreach (var ps in new[] { _dust, _grit, _smoke, _embers, _rocks }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "EmberLight", Ember, 6f); _light.transform.position = P + Vector3.up * 0.4f;
            // the slam: grit, rocks and dust thrown out low, flat black ink strokes sprayed along the ground (ef_a), a short hit-stop
            Vector3 c = G(P) + Vector3.up * 0.25f;
            for (int k = 0; k < 120; k++) Vol2FsB1.EmitV(_grit, c, (Flat(Random.insideUnitSphere) * 1.6f + Vector3.up * Random.Range(0.2f, 1.2f)).normalized * Random.Range(3f, 8f), -1f, -1f);
            for (int k = 0; k < 28; k++) Vol2FsB1.EmitV(_rocks, c + Random.insideUnitSphere * 0.3f, (Flat(Random.insideUnitSphere) + Vector3.up * Random.Range(0.4f, 1.2f)).normalized * Random.Range(3f, 7f), -1f, -1f);
            for (int k = 0; k < 12; k++) Vol2FsB1.EmitV(_dust, G(P) + Flat(Random.insideUnitSphere) * Radius + Vector3.up * 0.2f, Flat(Random.insideUnitSphere) * Random.Range(1f, 3.5f) + Vector3.up * 0.4f, -1f, -1f);
            _burst = Vol2FsB1.Burst(Seed, 18, Side, Dir, 2.0f, 4.0f, 0.12f, 0.32f, 0.12f, 0.6f);
            HitStop(0f, 0.08f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float exitK = Vol2FsRxTransferKit.Exit01(t, ChildEndAt, ChildWhy);
            bool natural = ChildEndAt >= 0f && Vol2FsRxTransferKit.Natural(ChildWhy);
            float left = HasChild && ChildExpireRel > 0f ? ChildExpireRel - t : 10f;
            // the enemy's flames choke: they stop at once and the bed sinks flat under the earth
            if (Patch != null)
            {
                if (t > 0.04f) Patch.StopParticles();
                float k = Vol2Fs.Smooth(0.05f, 0.45f, t);
                Patch.Pose(Mathf.Max(0.02f, 1f - k), k * 0.12f, 1f);
                if (t > 0.6f && Patch.Go != null && Patch.Go.activeSelf) Patch.SetActive(false);
            }
            // the plates drop into place (staggered, from above), settle; at the end they sink back (Expired) or dry and go (others)
            bool any = false;
            foreach (var pl in _plates)
            {
                if (pl.T == null) continue;
                if (!HasChild) { pl.T.localScale = Vector3.zero; continue; }
                float a = (t - pl.At) / 0.1f;
                if (a <= 0f) { pl.T.localScale = Vector3.zero; continue; }
                float fall = Mathf.Clamp01(a);
                float y = pl.Drop * (1f - fall * fall);
                float bounce = a > 1f ? Mathf.Sin(Mathf.Min(1f, (a - 1f) * 0.6f) * Mathf.PI) * 0.04f * Mathf.Exp(-(a - 1f) * 0.5f) : 0f;
                float sink = natural ? exitK * (pl.Scale.y + 0.1f) : 0f;
                float dry = !natural && ChildEndAt >= 0f ? 1f - exitK : 1f;
                pl.T.SetPositionAndRotation(pl.Rest + Vector3.up * (y + bounce - sink), pl.Rot);
                pl.T.localScale = pl.Scale * dry;
                if (fall >= 1f && a < 1.3f && !any) any = true;
            }
            if (any && !_landed)
            {
                _landed = true; _landAt = t;
                foreach (var pl in _plates) for (int k = 0; k < 1; k++) Vol2FsB1.EmitV(_dust, pl.Rest + Vector3.up * 0.15f, Flat(Random.insideUnitSphere) * Random.Range(0.8f, 2f) + Vector3.up * 0.3f, -1f, -1f);
            }
            // smoke from between the plates: thick at first, thinning (monotone), stops after the end
            float smoke = HasChild ? Mathf.Lerp(1f, 0.12f, Vol2Fs.Smooth(0.4f, 3.5f, t)) * (ChildEndAt >= 0f ? 0f : 1f) : (t < 0.6f ? 1f : 0f);
            if (Random.value < 0.1f * smoke) Vol2FsB1.EmitV(_smoke, G(P) + Flat(Random.insideUnitSphere) * Radius * Random.value + Vector3.up * 0.25f, Vector3.up * Random.Range(0.3f, 0.8f), -1f, -1f);
            float heat = HasChild ? Mathf.Lerp(1f, 0.25f, Vol2Fs.Smooth(0.3f, 4f, t)) * Mathf.Lerp(0.5f, 1f, Vol2Fs.Smooth(0f, 1f, left)) * (1f - exitK) * Vol2Fs.Smooth(0.12f, 0.3f, t) : 0f;
            if (Random.value < 0.4f * heat && _cracks.Count > 0) { var cr = _cracks[Random.Range(0, _cracks.Count)]; Vol2FsB1.EmitV(_embers, Vector3.Lerp(cr.A, cr.B, Random.value) + Vector3.up * 0.05f, Vector3.up * Random.Range(0.6f, 1.8f), -1f, -1f); }
            if (natural && exitK < 1f && Random.value < 0.6f) Vol2FsB1.EmitV(_dust, G(P) + Flat(Random.insideUnitSphere) * Radius * (0.8f + 0.5f * Random.value), Vector3.up * 0.5f + Flat(Random.insideUnitSphere) * 0.8f, -1f, -1f);
            _crack.Clear(); _ink.Clear(); _ring.Clear();
            int i = 0;
            foreach (var cr in _cracks) Vol2FsB6.Crack(_crack, cr.A, cr.B, Ground, 0.24f, 0f, Mathf.Clamp01((t - 0.12f) / 0.2f), heat, 0.2f + 0.1f * i, Seed + i++ * 13, 0.06f, 0.03f);
            if (_burst != null && t < 1.4f) Vol2FsB1.DrawBurst(_ink, _burst, G(P) + Vector3.up * 0.12f, t, cam, 0.08f, 0.08f);
            // the dust ring of the slam rolling outward (and again, lower, when the plates sink back)
            float ringLife = (1f - Vol2Fs.Smooth(0.1f, 0.9f, t)) * 0.9f;
            if (ringLife > 0.01f) Vol2FsB4A.GroundArc(_ring, P, Ground + 0.05f, Radius * (0.8f + 2.2f * Vol2Fs.Smooth(0f, 0.6f, t)), 0.6f, 0f, Mathf.PI * 2f, ringLife, 0.4f, cam, 40);
            // r3: the landing's own dust ring, low and clear on the ground (out-reads the acting glyph's dust column)
            if (_landAt > 0f) { float la = t - _landAt, lr = 1f - Vol2Fs.Smooth(0.15f, 1.1f, la); if (lr > 0.01f) { Vol2FsB4A.GroundArc(_ring, P, Ground + 0.04f, Radius * (1.4f + 1.8f * Vol2Fs.Smooth(0f, 0.7f, la)), 0.7f, 0f, Mathf.PI * 2f, lr, 0.55f, cam, 48); Vol2FsB4A.GroundArc(_ring, P, Ground + 0.05f, Radius * (1.0f + 1.2f * Vol2Fs.Smooth(0f, 0.5f, la)), 0.4f, 0f, Mathf.PI * 2f, lr * 0.8f, 0.85f, cam, 40); } }
            if (natural && exitK < 1f) Vol2FsB4A.GroundArc(_ring, P, Ground + 0.05f, Radius * (1.8f + 0.8f * exitK), 0.45f, 0f, Mathf.PI * 2f, (1f - exitK) * 0.7f, 0.7f, cam, 40);
            _crack.Write(_crackMesh); _ink.Write(_inkMesh); _ring.Write(_ringMesh);
            if (_light != null) _light.intensity = 8f * heat + (t < 0.12f ? 10f * (1f - t / 0.12f) : 0f);
        }
    }
}
