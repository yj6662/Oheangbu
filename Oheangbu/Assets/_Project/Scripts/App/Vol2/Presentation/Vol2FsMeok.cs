using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>먹 (earth, ㅓ attachment, 남긴다) - free showcase batch 3, concept meok_a (the caster's arm is encased in a huge rugged gauntlet of rock and packed earth,
    /// the brush head has become a heavy stone MACE with glowing seams; the swing smashes into the bear, dust and rock exploding). Unlike 머's stone wall (a guard in
    /// front) it is stone that STAYS ON the weapon for the material's 6 s. Beats, executor facts only: the launch = rock and soil crawl up the shaft and clump into the
    /// mace head, plates lock round the forearm -> worn for the Attached material's life (grit trickling, the seams breathing); the brush's real motion drags a trail of
    /// dust and heavy ink; a body the swing touched = a dust explosion and rock chunks on it -> the last 0.4 s = the stone cracks and sheds pebbles -> natural expiry
    /// (CSV: 가라앉으며 둘레 땅이 판으로 들림) = the stone sinks back off the brush into the ground while the earth round the caster's feet lifts as plates.</summary>
    public sealed class Vol2FsMeok : Vol2FsWear
    {
        static readonly Color DustC = new Color(0.55f, 0.42f, 0.34f, 1f), Soil = new Color(0.3f, 0.18f, 0.12f, 1f), Ink = new Color(0.03f, 0.025f, 0.02f), Seam = new Color(1f, 0.55f, 0.2f);
        ParticleSystem _chunks, _bits, _cloud, _clods, _ring, _speed; Light _seamLight;
        Material _rock, _rockSeam;
        Transform _mace; readonly List<(Transform T, Vector3 Off, float S)> _maceBits = new List<(Transform, Vector3, float)>();
        readonly List<(Transform T, float U, float Ang, float S)> _gaunt = new List<(Transform, float, float, float)>();
        readonly List<(Transform T, Vector3 Base, Quaternion Rot)> _plates = new List<(Transform, Vector3, Quaternion)>();
        Mesh _trailMesh, _inkMesh, _burstMesh; readonly Vol2FsStrips _trail = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _burstS = new Vol2FsStrips();
        List<Vol2FsB1.Stroke> _burst; Vector3 _burstAt; float _burstT = -1f; bool _exitFx; Vector3 _feet;
        protected override float ExpireSeconds => 1.3f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _rock = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.82f, 0.66f, 0.54f), 0f, 0.1f);
            _rockSeam = Vol2FsB1.Lit(Kit, "fs_rock_crack", new Color(0.72f, 0.57f, 0.47f), 0f, 0.1f, Seam * 1.3f, "fs_vein");      // r2: vein mask (fs_crack is all white: the r1 mace glowed white)
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            _mace = new GameObject("Mace").transform; _mace.SetParent(root, false);
            var core = Vol2FsB1.Prop(_mace, "Core", Kit.SharedMesh("mace0", () => Vol2FsB1.Rock(57, 0.5f, 0.92f, 14, 10)), _rockSeam);
            _maceBits.Add((core.transform, Vector3.zero, 0.55f));
            for (int k = 0; k < 7; k++)
            {
                var r = Vol2FsB1.Prop(_mace, "Lump" + k, Kit.SharedMesh("lump" + (k % 3), () => Vol2FsB1.Rock(70 + k % 3, 0.5f, 0.8f, 9, 7)), k % 2 == 0 ? _rock : _rockSeam);
                Vector3 off = Random.onUnitSphere * 0.22f; off.y = Mathf.Abs(off.y) * 0.6f + 0.05f;
                _maceBits.Add((r.transform, off, Mathf.Lerp(0.22f, 0.34f, Vol2Fs.Hash(Seed, k))));
            }
            foreach (var b in _maceBits) b.T.localScale = Vector3.one * 0.001f;
            for (int k = 0; k < 9; k++)
            {
                var r = Vol2FsB1.Prop(root, "Gauntlet" + k, Kit.SharedMesh("gplate" + (k % 3), () => Vol2FsB1.Slab(90 + k % 3, new Vector3(1f, 1f, 0.5f), 0.7f)), k % 3 == 0 ? _rockSeam : _rock);
                r.transform.localScale = Vector3.one * 0.001f;
                _gaunt.Add((r.transform, Mathf.Lerp(-0.25f, 0.28f, (k % 5) / 4f), k * 2.4f, Mathf.Lerp(0.16f, 0.26f, Vol2Fs.Hash(Seed, k + 20))));
            }
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.9f, Life1 = 1.6f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.1f, Size1 = 0.36f, Radius = 0.3f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 160 }, root, "Chunks");
            _bits = Vol2Fs.Build(new Vol2FsPs { Mat = _rock, Mesh = chunk, Life0 = 0.5f, Life1 = 1f, Speed0 = 0.1f, Speed1 = 0.8f, Size0 = 0.025f, Size1 = 0.07f, Radius = 0.08f, Gravity = 2.4f, Spin = 400f, Max = 300 }, root, "Grit");
            _cloud = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.75f)), Life0 = 1f, Life1 = 2f, Speed0 = 1f, Speed1 = 4f, Size0 = 1.2f, Size1 = 2.6f, Radius = 0.5f, Drag = 2f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.4f, 0f), Max = 80, SortFudge = 10 }, root, "Cloud");
            var clod = M("fs_ink", Soil, 1f, 0f); clod.NoiseAmt = 0.4f; clod.Soft = 0.1f;
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 2f, Speed1 = 7f, Size0 = 0.06f, Size1 = 0.22f, Radius = 0.3f, Gravity = 2.6f, Tiles = 2, AlphaHoldTo = 0.7f, Max = 400 }, root, "Clods");
            var ring = M("fs_ring", DustC, 1f, 0f); ring.Opacity = 0.8f; ring.NoiseAmt = 0.5f; ring.Soft = 0.02f;
            _ring = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ring), Life0 = 0.6f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0f, Size0 = 4.5f, Size1 = 4.5f, Radius = 0.01f, Horizontal = true, SizeStart = 0.1f, SizePeak = 0.85f, SizePeakAt = 0.5f, SizeEnd = 1f, AlphaHoldTo = 0.3f, Max = 4 }, root, "Ring");
            var streak = M("fs_streak", DustC, 1f, 0f); streak.Opacity = 0.9f; streak.NoiseAmt = 0.4f;
            _speed = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(streak), Life0 = 0.3f, Life1 = 0.55f, Speed0 = 8f, Speed1 = 14f, Size0 = 0.2f, Size1 = 0.4f, Radius = 0.4f, Drag = 2f, Stretch = true, StretchVel = 0.09f, StretchLen = 2f, Max = 80 }, root, "Shove");
            foreach (var ps in new[] { _chunks, _bits, _cloud, _clods, _ring, _speed }) ps.Play(false);
            var tr = M("fs_stroke", DustC, 1f, 0f); tr.Opacity = 0.85f; tr.NoiseAmt = 0.6f; tr.Erode = 0.35f; tr.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "DustTrail", Kit.Mat(tr), out _trailMesh);
            Vol2Fs.MeshObject(root, "InkTrail", Kit.InkSolid("fs_stroke2", Ink, new Color(0.4f, 0.25f, 0.12f, 1f), 0.08f, 0.5f, 0.3f, 1.5f), out _inkMesh);
            Vol2Fs.MeshObject(root, "HitInk", Kit.InkSolid("fs_stroke2", Ink, new Color(0.3f, 0.18f, 0.1f, 1f), 0.1f, 0.5f, 0.3f, 1.6f), out _burstMesh);
            _seamLight = Vol2Fs.PointLight(root, "SeamLight", Seam, 3f);
        }

        protected override void OnCharge(float t, float k) { if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_bits, Tip() + Random.insideUnitSphere * 0.4f, 1); }
        protected override void OnLaunch(long nowUs) { _feet = Inst.Origin; }
        protected override void OnHit(long nowUs) { Vol2FsB1.Emit(_clods, Tip(), 20); Vol2FsB1.Emit(_cloud, Tip(), 2); }

        void Smash(Vector3 c, Vector3 away)
        {
            _burstAt = c + Vector3.down * 0.25f; _burstT = Inst.Rel(NowUs);
            var side = Vector3.Cross(Vector3.up, away);
            _burst = new List<Vol2FsB1.Stroke>();
            for (int k = 0; k < 8; k++)
            {
                float a = Mathf.Lerp(-1.2f, 1.2f, k / 7f);
                Vector3 d = away * Mathf.Cos(a) + side * Mathf.Sin(a) + Vector3.up * (0.15f + 0.2f * Vol2Fs.Hash(Seed, k + 300));
                _burst.Add(new Vol2FsB1.Stroke { Dir = d.normalized, Len = 1.1f + 1.2f * Vol2Fs.Hash(Seed, k + 310), Width = 0.28f + 0.2f * Vol2Fs.Hash(Seed, k + 320), Delay = 0.02f * Vol2Fs.Hash(Seed, k + 330), Life = 0.9f, Rnd = Vol2Fs.Hash(Seed, k + 340) });
            }
            Vol2FsB1.Emit(_chunks, c, 34); Vol2FsB1.Emit(_clods, c, 70); Vol2FsB1.Emit(_cloud, c, 6);
            Vol2FsB1.Emit(_ring, new Vector3(c.x, Ground + 0.05f, c.z), 1);
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_speed, c + Random.insideUnitSphere * 0.4f, (away * 1f + Random.insideUnitSphere * 0.3f) * Random.Range(8f, 14f), -1f, -1f);
        }
        protected override void OnStruck(Struck s) { Smash(CentreNow(s), FlatN(CentreNow(s) - Inst.Origin)); }
        protected override void OnTouch(Struck s) { if (Hits.Exists(h => h.Id == s.Id && Mathf.Abs(h.At - s.At) < 0.05f)) return; Smash(CentreNow(s), FlatN(CentreNow(s) - Inst.Origin)); }

        protected override void OnResidueEnd()
        {
            _feet = Inst.Origin;
            if (NaturalEnd)
            {
                var root = Root.transform;
                for (int k = 0; k < 10; k++)
                {
                    float a = (k / 10f) * Mathf.PI * 2f + 0.3f * Vol2Fs.Hash(Seed, k + 60);
                    Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var r = Vol2FsB1.Prop(root, "Plate" + k, Kit.SharedMesh("plate" + (k % 3), () => Vol2FsB1.Slab(400 + k % 3, new Vector3(1f, 0.22f, 0.8f), 0.9f)), _rock);
                    var b = new Vector3(_feet.x, Ground - 0.2f, _feet.z) + o * 1.3f;
                    r.transform.localScale = new Vector3(Mathf.Lerp(0.6f, 0.9f, Vol2Fs.Hash(Seed, k + 70)), 1f, 0.7f);
                    _plates.Add((r.transform, b, Quaternion.LookRotation(o, Vector3.up)));
                }
                Vol2FsB1.Emit(_ring, new Vector3(_feet.x, Ground + 0.05f, _feet.z), 1);
            }
            else { Vol2FsB1.Emit(_chunks, Tip(), 20); Vol2FsB1.Emit(_cloud, Tip(), 3); }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float worn = Worn(t), warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            Vector3 tip = Tip(), grip = Grip(), sleeve = Sleeve();
            Vector3 ax = (tip - grip).sqrMagnitude > 1e-6f ? (tip - grip).normalized : Vector3.up;
            Quaternion frame = Quaternion.LookRotation(ax, Mathf.Abs(ax.y) > 0.95f ? Vector3.forward : Vector3.up);
            // the stone crawls up from the grip to the tip (0.25 s), the mace head clumps on the bristles; on the end it sinks back down off the brush
            float form = Vol2Fs.Smooth(0f, 0.3f, h);
            float sink = NaturalEnd ? Vol2Fs.Smooth(0f, 0.8f, ex) : Vol2Fs.Smooth(0f, 0.35f, ex);
            float jit = warn > 0f ? 0.03f * warn : 0f;
            _mace.SetPositionAndRotation(tip + ax * 0.12f + Vector3.down * 0.5f * sink * (NaturalEnd ? 1f : 0f), frame);
            for (int k = 0; k < _maceBits.Count; k++)
            {
                var b = _maceBits[k];
                float s = b.S * Vol2Fs.EaseOutBack(Mathf.Clamp01((form - 0.4f - 0.05f * k) / 0.5f)) * worn * (NaturalEnd ? 1f : 1f - sink) * (1f + 0.08f * pulse);
                b.T.localScale = Vector3.one * Mathf.Max(0.001f, s) * 1.6f;
                b.T.localPosition = b.Off * 1.8f + Random.insideUnitSphere * jit;
                b.T.localRotation = Quaternion.Euler(k * 37f, k * 71f, 0f);
            }
            for (int k = 0; k < _gaunt.Count; k++)
            {
                var g = _gaunt[k];
                float s = g.S * Vol2Fs.EaseOutBack(Mathf.Clamp01((form * 1.3f - 0.06f * k) / 0.4f)) * worn * (1f - sink);
                Vector3 c = g.U < 0f ? Vector3.Lerp(grip, sleeve, -g.U / 0.25f) : Vector3.LerpUnclamped(grip, tip, g.U);
                Vector3 n = Quaternion.AngleAxis(g.Ang * Mathf.Rad2Deg, ax) * (Vector3.Cross(ax, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(ax, Vector3.up).normalized : Right);
                g.T.SetPositionAndRotation(c + n * 0.05f + Random.insideUnitSphere * jit, Quaternion.LookRotation(ax, n));
                g.T.localScale = new Vector3(s * 1.5f, Mathf.Max(0.001f, s * 0.8f), s * 2.1f);
            }
            // idle: grit trickles off the mace, more pebbles and dust in the warning; the dust follows the swing
            if (worn > 0.3f && !Exiting)
            {
                if (Time.frameCount % (warn > 0f ? 2 : 10) == 0) Vol2FsB1.Emit(_bits, tip + Random.insideUnitSphere * 0.25f, warn > 0f ? 3 : 1);
                if (warn > 0f && Time.frameCount % 6 == 0) Vol2FsB1.Emit(_cloud, tip, 1);
            }
            _trail.Clear(); _ink.Clear();
            float tl = worn * Vol2Fs.Smooth(1.5f, 4f, TipSpeed(t));
            if (tl > 0.01f) { SwingTrail(_trail, t, 0.3f, 0.9f, tl * 0.8f, 0.47f, 0.02f); SwingTrail(_ink, t, 0.2f, 0.28f, tl, 0.13f); }
            _trail.Write(_trailMesh); _ink.Write(_inkMesh);
            _burstS.Clear();
            if (_burst != null && _burstT >= 0f) Vol2FsB1.DrawBurst(_burstS, _burst, _burstAt, t - _burstT, cam, 0.2f, 0.1f);
            _burstS.Write(_burstMesh);
            // the natural end: the earth round the caster's feet lifts as plates and settles (nothing falls)
            if (_plates.Count > 0)
            {
                float lift = Mathf.Sin(Mathf.Clamp01(ex / 0.85f) * Mathf.PI);
                for (int k = 0; k < _plates.Count; k++)
                {
                    var p = _plates[k]; Vector3 o = p.Rot * Vector3.forward;
                    p.T.SetPositionAndRotation(p.Base + Vector3.up * 0.16f * lift, Quaternion.AngleAxis(-24f * lift, Vector3.Cross(Vector3.up, o)) * p.Rot);
                    p.T.gameObject.SetActive(ex < 0.97f);
                }
                if (ex < 0.7f && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_cloud, new Vector3(_feet.x, Ground + 0.1f, _feet.z) + Random.insideUnitSphere * 1.3f, 1);
            }
            if (Exiting && !_exitFx) { _exitFx = true; Vol2FsB1.Emit(_bits, Vector3.Lerp(grip, tip, 0.6f), 30); }
            _seamLight.transform.position = tip + ax * 0.1f;
            _seamLight.intensity = 1.8f * worn * (0.7f + 0.3f * Mathf.Sin(t * 3f)) * (1f + 2f * pulse) * (warn > 0f ? 0.5f + 0.5f * Mathf.PerlinNoise(t * 30f, 0f) : 1f);
        }
    }
}
