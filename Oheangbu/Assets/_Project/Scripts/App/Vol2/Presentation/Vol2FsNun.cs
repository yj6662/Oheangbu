using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>눈 (fire, ㅜ ring, 번진다 극치) - free showcase batch 4, concept nun_a (a fuse of fire: the ring round the caster is a low line of flame; where it
    /// crosses a patch of dry dead vines the vines catch and the fire RUNS outward along the vine trail like a burning fuse to its far end, consuming the wood; the
    /// bear standing in the burning vines is engulfed; an enemy slime patch on the ring burns away). The peak's rule is drawn as it is: fire follows WOOD, not bodies -
    /// the bear struck on the band gets the ring's touch and nothing leaps from it; the bear with no wood under it stays untouched.
    /// Beats, all executor facts: the band of embers and low flame for the timed emitter's life -> Log.Burned (wood Consumed / enemy patches Consumed, read back
    /// through the registry) = each patch ignites in its chain order (its distance from the band) with a running fuse between them, flares, then its vines burn back
    /// into ash; an enemy patch boils away in black smoke -> the one "Spread" contact (the body standing in the burning wood, the whole 1.0) = it is engulfed when its
    /// wood catches -> its Mark (3 s) = a fire bed at its feet + the body burning; warning = the flames gutter -> natural expiry (CSV 끝부터 타 재로 날림) = ash rising.
    /// The wood before it burns is the world's fact (the registry's materials near the ring at the cast's start), drawn here because nothing else draws it in the lab.</summary>
    public sealed class Vol2FsNun : Vol2FsSpread
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f), Hot = new Color(1f, 0.8f, 0.4f), Ink = new Color(0.035f, 0.025f, 0.02f);
        ParticleSystem _charge, _fire, _tall, _embers, _smoke, _ash, _steam; Light _chargeLight; Light[] _ringLights;
        Material _deadVine, _deadLeaf;
        Mesh _lineMesh, _scorchMesh, _fuseMesh, _slimeMesh, _bedMesh;
        readonly Vol2FsStrips _line = new Vol2FsStrips(), _scorch = new Vol2FsStrips(), _fuse = new Vol2FsStrips(), _slime = new Vol2FsStrips(), _bed = new Vol2FsStrips();
        sealed class Patch { public Vol2Material M; public Vector3 C; public float R; public bool Enemy; public Vol2FsTubes Vines = new Vol2FsTubes(); public Mesh VineMesh; public Vol2FsLeaves Dry = new Vol2FsLeaves(); public Mesh DryMesh; public float Ignite = -1f; public bool Burned, Flared; public Light L; public float FuseFrom = -1f; public Vector3 FuseA; }
        readonly List<Patch> _patches = new List<Patch>();
        Struck _first;

        protected override float HopSpeed => 6f;
        protected override float HopMin => 0.2f;
        protected override float HopMax => 0.3f;
        protected override float MarkExpireSeconds => 1.1f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.8f, 0.5f)), Rate = 70f, Loop = true, Life0 = 0.18f, Life1 = 0.3f, Speed0 = -1.6f, Speed1 = -2.6f, Size0 = 0.12f, Size1 = 0.26f, Radius = 0.6f, World = false, Tiles = 2, Max = 90 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Orange, 4f);
            _charge.transform.position = Tip(); _charge.Play(false);
            _fire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.6f, 0.35f)), Life0 = 0.3f, Life1 = 0.65f, Speed0 = 0.2f, Speed1 = 0.9f, Size0 = 0.3f, Size1 = 0.65f, Radius = 0.15f, Tiles = 2,
                VelOverLife = new Vector3(0f, 2f, 0f), Noise = 0.9f, NoiseFreq = 1.3f, SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 0.35f, Max = 5000 }, root, "Fire");
            _tall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.35f)), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 1.2f, Speed1 = 3f, Size0 = 0.9f, Size1 = 1.5f, Radius = 0.3f, Tiles = 2,
                VelOverLife = new Vector3(0f, 3.2f, 0f), Noise = 1f, NoiseFreq = 0.9f, SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.35f, SizeEnd = 0.5f, Max = 600 }, root, "TallFire");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Life0 = 0.7f, Life1 = 1.8f, Speed0 = 0.6f, Speed1 = 3f, Size0 = 0.025f, Size1 = 0.06f, Radius = 0.3f, Gravity = -0.25f,
                Noise = 1.4f, NoiseFreq = 1.6f, Stretch = true, StretchVel = 0.05f, Max = 2500 }, root, "Embers");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.55f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.3f, Speed1 = 0.9f, Size0 = 0.8f, Size1 = 1.4f, Radius = 0.35f, Drag = 0.8f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.5f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 1.1f, 0f), Max = 300, SortFudge = 12 }, root, "Smoke");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 0.2f, Speed1 = 1f, Size0 = 0.04f, Size1 = 0.12f, Radius = 0.45f, VelOverLife = new Vector3(0f, 1.5f, 0f),
                Noise = 1.2f, NoiseFreq = 1f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 2000 }, root, "Ash");
            var steam = M("fs_puff", new Color(0.12f, 0.1f, 0.12f, 1f), 1f, 0f); steam.Opacity = 0.6f; steam.NoiseAmt = 0.6f; steam.Erode = 0.4f;
            _steam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(steam), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 0.5f, Speed1 = 1.6f, Size0 = 0.6f, Size1 = 1.2f, Radius = 0.5f, Tiles = 2, VelOverLife = new Vector3(0f, 1.4f, 0f), SizeEnd = 1.8f, Max = 120, SortFudge = 10 }, root, "SlimeSmoke");
            foreach (var ps in new[] { _fire, _tall, _embers, _smoke, _ash, _steam }) ps.Play(false);
            // the band: a low smouldering line (not a wall), its scorch, the fuse runners, the slime, the fire beds
            var line = M("fs_ring", Color.white, 2.6f, 0.9f); line.Ramp = "fs_ramp_fire"; line.RampScale = 1.2f; line.NoiseAmt = 0.6f; line.NoiseScale = 2f; line.Soft = 0.001f; line.Scroll = new Vector2(0.6f, 0f);
            Vol2Fs.MeshObject(root, "EmberLine", Kit.Mat(line), out _lineMesh);
            var sc = M("fs_scorch", new Color(0.05f, 0.03f, 0.02f, 1f), 1f, 0f); sc.Soft = 0.001f; sc.NoiseAmt = 0.4f; sc.Erode = 0.3f; sc.Opacity = 0.9f; sc.Edge = new Color(2.2f, 0.6f, 0.08f, 1f); sc.EdgeWidth = 0.14f;
            Vol2Fs.MeshObject(root, "Scorch", Kit.Mat(sc), out _scorchMesh);
            var fuse = M("fs_streak", Color.white, 4.6f, 0.95f); fuse.Ramp = "fs_ramp_fire"; fuse.RampScale = 1.3f; fuse.NoiseAmt = 0.5f; fuse.Scroll = new Vector2(1.2f, 0f);
            Vol2Fs.MeshObject(root, "Fuse", Kit.Mat(fuse), out _fuseMesh);
            var slime = M("fs_wet", new Color(0.1f, 0.06f, 0.12f, 1f), 1f, 0f); slime.Soft = 0.001f; slime.NoiseAmt = 0.5f; slime.Opacity = 0.9f; slime.Edge = new Color(0.4f, 0.2f, 0.5f, 1f); slime.EdgeWidth = 0.08f;
            Vol2Fs.MeshObject(root, "Slime", Kit.Mat(slime), out _slimeMesh);
            var bed = M("fs_scorch", new Color(0.06f, 0.03f, 0.02f, 1f), 1f, 0f); bed.Soft = 0.001f; bed.NoiseAmt = 0.5f; bed.Erode = 0.3f; bed.Opacity = 0.95f; bed.Edge = new Color(3f, 1f, 0.15f, 1f); bed.EdgeWidth = 0.22f;
            Vol2Fs.MeshObject(root, "FireBeds", Kit.Mat(bed), out _bedMesh);
            _ringLights = new Light[3];
            for (int k = 0; k < 3; k++) _ringLights[k] = Vol2Fs.PointLight(root, "RingLight" + k, Orange, 5f);
            // the world's wood (dry dead vines and brush) and the enemy slime, as they lie before the fire comes
            _deadVine = Kit.Bark(new Color(0.85f, 0.7f, 0.5f), new Color(0.2f, 0.13f, 0.07f), 0.15f, "fs_vine", "fs_bark");      // r2: lighter dry wood (r1 read as nothing on the red earth)
            _deadLeaf = Kit.Leaf(new Color(0.95f, 0.78f, 0.48f), new Color(0.12f, 0.08f, 0.03f));
            int pi = 0;
            foreach (var m in Fuel)
            {
                var fp = m.Footprint; var p = new Patch { M = m, C = G(fp.Centre), R = Mathf.Max(0.4f, fp.Radius), Enemy = m.FactionId == Vol2Faction.Enemy };
                if (!p.Enemy)
                {
                    Vol2Fs.MeshObject(root, "DeadVines_" + pi, _deadVine, out p.VineMesh).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    Vol2Fs.MeshObject(root, "DryLeaves_" + pi, _deadLeaf, out p.DryMesh);
                    int seed = Seed + 97 * pi;
                    for (int v = 0; v < 9; v++)
                    {
                        // a tangle of dead vines lying on the ground, a few arching up
                        float a0 = Vol2Fs.Hash(seed, v) * Mathf.PI * 2f, len = p.R * (1.4f + 0.8f * Vol2Fs.Hash(seed, v + 10));
                        Vector3 d = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), sd = Vector3.Cross(Vector3.up, d);
                        Vector3 s0 = p.C - d * len * 0.5f + sd * (Vol2Fs.Hash(seed, v + 20) - 0.5f) * p.R;
                        var pts = new Vector3[7];
                        for (int j = 0; j < 7; j++) { float u = j / 6f; pts[j] = s0 + d * len * u + sd * Mathf.Sin(u * 6f + v) * 0.15f + Vector3.up * (0.03f + Mathf.Max(0f, Mathf.Sin(u * Mathf.PI * (1 + v % 3))) * (v % 3 == 0 ? 0.35f : 0.1f)); }
                        var tb = p.Vines.Add(pts, 0.035f, 0.012f, 18); tb.Knots = 0.6f; tb.Sharp = 0.15f; tb.Sides = 5; tb.Grow = 1f;
                    }
                    for (int j = 0; j < 14; j++)
                    {
                        var lp = Vol2FsB3.Spiral(p.C, p.R * 1.1f, j, 14, Ground + 0.03f);
                        Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(seed, j + 40) * 360f, Vector3.up) * Vector3.forward;
                        p.Dry.Items.Add(new Vol2FsLeaves.Leaf { Pos = lp, Normal = (nrm + Vector3.up * 1.5f).normalized, Up = Vector3.Cross(nrm, Vector3.up).normalized, Size = 0.2f + 0.1f * Vol2Fs.Hash(seed, j + 50), Grow = 1f, Cell = j % 4 });
                    }
                    p.Vines.Write(p.VineMesh); p.Dry.Write(p.DryMesh);
                }
                p.L = Vol2Fs.PointLight(root, "PatchLight_" + pi, Orange, 5f);
                p.L.transform.position = p.C + Vector3.up * 0.8f;
                _patches.Add(p); pi++;
            }
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 4f * k; }

        protected override void OnLaunch(long nowUs)
        {
            _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            for (int k = 0; k < 3; k++) _ringLights[k].transform.position = OnBand(-1.2f + k * 1.2f, R) + Vector3.up * 0.6f;
        }

        protected override void OnHit(long nowUs)
        {
            for (int k = 0; k < 12; k++) { float ang = Mathf.Lerp(-0.9f, 0.9f, k / 11f); Vol2FsB1.Emit(_fire, OnBand(ang, R) + Vector3.up * 0.05f, 1); Vol2FsB1.Emit(_embers, OnBand(ang, R), 2); }      // r2: front only, small
        }

        protected override void OnStruck(Struck s)
        {
            if (_first != null) return;
            _first = s;
            Vol2FsB1.Emit(_fire, CentreNow(s), 30); Vol2FsB1.Emit(_embers, CentreNow(s), 60);
        }

        Patch PatchOf(Burn b)
        {
            Patch best = null; float bd = 0.8f;
            foreach (var p in _patches) { if (p.M != null && p.M.Id.Value == b.Id) return p; float d = Vector3.Distance(p.C, G(b.Centre)); if (d < bd) { bd = d; best = p; } }
            return best;
        }

        protected override void OnBurn(Burn b)
        {
            var p = PatchOf(b);
            if (p == null)
            {
                p = new Patch { C = G(b.Centre), R = b.Radius, Enemy = b.Enemy, L = Vol2Fs.PointLight(Root.transform, "PatchLight_x", Orange, 5f) };
                p.L.transform.position = p.C + Vector3.up * 0.8f; _patches.Add(p);
            }
            p.Burned = true;
            p.Ignite = HitAt + 0.12f + b.Delay * 1.6f;                // the chain order: the patch on the band first, the wood next to it after (b.Delay = distance from the band)
            // the fuse runs from the burning wood nearest to it that caught earlier (or from the band)
            Patch src = null; float sd = 99f;
            foreach (var q in _patches) if (q != p && q.Burned && !q.Enemy && q.Ignite < p.Ignite) { float d = Vector3.Distance(q.C, p.C); if (d < sd) { sd = d; src = q; } }
            p.FuseA = src != null ? src.C : G(Apex + FlatN(p.C - Apex) * R);
            p.FuseFrom = src != null ? src.Ignite + 0.05f : HitAt;
            // the body standing in this wood: its hop waits for the fire to reach it
            foreach (var h in Hops) if (!h.Landed && !p.Enemy && Vector3.Distance(G(FeetOf(h)), p.C) < p.R + 1.2f) { h.FromP = p.C; h.Start = Mathf.Max(h.Start, p.Ignite + 0.1f); }
        }

        protected override void OnHop(Hop h)
        {
            foreach (var p in _patches) if (p.Burned && !p.Enemy && Vector3.Distance(G(FeetOf(h)), p.C) < p.R + 1.2f) { h.FromP = p.C; h.Start = Mathf.Max(h.Start, p.Ignite + 0.1f); }
        }

        protected override void OnLand(Hop h)
        {
            Vector3 c = CentreNow(h);
            Vol2FsB1.Emit(_tall, G(FeetOf(h)) + Vector3.up * 0.3f, 6); Vol2FsB1.Emit(_fire, c, 40); Vol2FsB1.Emit(_embers, c, 140);      // r3: smaller (r2 merged into one blob with the patches) Vol2FsB1.Emit(_smoke, c + Vector3.up * 0.8f, 4);
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            Vector3 c = m.Centre + Vector3.up * 0.6f;
            if (Natural(m)) Vol2FsB1.Emit(_smoke, c + Vector3.up * 0.5f, 3); else { Vol2FsB1.Emit(_embers, c, 120); Vol2FsB1.Emit(_smoke, c, 4); }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            // the band of embers and low flame (front arc), the emitter's life
            float life = HostLife(t, 0.15f, 0.6f), warn = HostWarn(t);
            _line.Clear(); _scorch.Clear(); _fuse.Clear(); _slime.Clear(); _bed.Clear();
            if (life > 0.01f) Vol2FsB2.GroundRing(_line, Apex, F, Ground, R - 0.1f, R + 0.1f, 72, a => Vol2FsB4R.Front55(a) * life * (1f - 0.5f * warn), 0.13f, cam, 0.04f, 0f, 5f);      // r2: a thin ember line, front only
            float sl = Vol2Fs.Smooth(0f, 0.3f, h) * (1f - Vol2Fs.Smooth(0f, 1.2f, EmitterEnd > 0f ? t - EmitterEnd - 0.4f : 0f));
            if (sl > 0.01f) Vol2FsB2.GroundRing(_scorch, Apex, F, Ground, R - 0.22f, R + 0.22f, 60, a => Vol2FsB4R.Front55(a) * sl * 0.6f, 0.71f, cam, 0.025f, 0f, 4f);
            if (life > 0.05f && (warn < 0.01f || Random.value > warn * 0.6f))
                for (int k = 0; k < 2; k++)                                       // r2: few small flames on the front line (r1: fire everywhere near the caster)
                {
                    float ang = Random.Range(-0.95f, 0.95f); float fr = Vol2FsB4R.Front55(ang); if (fr < 0.25f) continue;
                    Vector3 p = OnBand(ang, Random.Range(Inner, Outer)) + Vector3.up * 0.05f;
                    Vol2FsB1.EmitV(_fire, p, Vector3.up * 0.7f, (0.22f + 0.15f * Random.value) * fr, -1f);
                    if (k == 0) Vol2FsB1.Emit(_embers, p, 1);
                }
            for (int k = 0; k < _ringLights.Length; k++) _ringLights[k].intensity = 3.5f * life * (0.8f + 0.2f * Mathf.PerlinNoise(t * 8f, k)) * (1f - 0.5f * warn);
            // the body struck on the band: the ring's touch scorches it a moment (nothing leaps from it)
            if (_first != null && t - _first.At < 0.6f && Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_fire, CentreNow(_first) + Random.insideUnitSphere * 0.35f, Vector3.up * 1.1f, 0.3f * (1f - (t - _first.At) / 0.6f) + 0.1f, -1f);
            // the patches: dead brush lying -> the fuse reaches it -> it flares -> its vines burn back into ash; the slime boils away
            foreach (var p in _patches)
            {
                if (!p.Burned)
                {
                    if (p.Enemy) _slime.Quad(p.C + Vector3.up * 0.018f, Right, Fwd, p.R * 2.6f, new Color(1f, 1f, 1f, 1f), 0.27f);
                    continue;
                }
                // the fuse: a runner of flame racing over the ground from the source to this patch
                if (!p.Enemy && p.FuseFrom >= 0f)
                {
                    float fk = Vol2Fs.Smooth(p.FuseFrom, p.Ignite, t), fl = 1f - Vol2Fs.Smooth(p.Ignite + 1.0f, p.Ignite + 2.4f, t);      // r2: the fuse is the hero - it stays lit longer
                    if (fk > 0f && fl > 0.01f)
                    {
                        Vol2FsB4.Runner(_fuse, p.FuseA, p.C, Ground, 0f, fk, 0.62f, fl, 0.31f, cam, 0.18f, p.R * 7f, 14, 0.06f);
                        if (fk < 1f) { Vector3 head = Vector3.Lerp(p.FuseA, p.C, fk); Vol2FsB1.EmitV(_fire, head + Vector3.up * 0.1f, Vector3.up * 1.2f, 0.55f, -1f); Vol2FsB1.Emit(_embers, head, 3); }
                        else if (Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_fire, Vector3.Lerp(p.FuseA, p.C, Random.value) + Vector3.up * 0.05f, Vector3.up * 0.8f, 0.35f * fl, -1f);
                    }
                }
                float since = t - p.Ignite;
                if (since < 0f)
                {
                    if (p.Enemy) _slime.Quad(p.C + Vector3.up * 0.018f, Right, Fwd, p.R * 2.6f, new Color(1f, 1f, 1f, 1f), 0.27f);
                    continue;
                }
                if (!p.Flared)
                {
                    p.Flared = true;
                    if (p.Enemy) { Vol2FsB1.Emit(_fire, p.C + Vector3.up * 0.2f, 18); Vol2FsB1.Emit(_steam, p.C + Vector3.up * 0.3f, 22); }      // r2: it boils away (smoke) rather than blazing
                    else { float o = Mathf.Clamp01((Vector3.Distance(p.C, G(Apex)) - R) / 3.5f); Vol2FsB1.Emit(_tall, p.C + Vector3.up * 0.2f, Mathf.RoundToInt(2 + 8 * o)); Vol2FsB1.Emit(_fire, p.C + Vector3.up * 0.2f, Mathf.RoundToInt(15 + 35 * o)); Vol2FsB1.Emit(_embers, p.C, Mathf.RoundToInt(50 + 150 * o)); }
                }
                // r3: how far out along the chain (0 = the patch on the band, 1 = 3.5 m beyond it): the outer patches flare as TALL columns so the run reads over the
                // caster's shoulder as fire marching outward; the near one stays small (r2: one blob left of the caster)
                float outw = Mathf.Clamp01((Vector3.Distance(p.C, G(Apex)) - R) / 3.5f);
                float burn = Vol2Fs.Smooth(0.35f, 1.6f, since);                    // the vines burn back from their tips
                float flame = Vol2Fs.Smooth(0f, 0.1f, since) * (1f - Vol2Fs.Smooth(0.6f, 1.3f, since));      // r2: a patch flares and burns down quickly (the run reads, not a bonfire)
                if (p.Enemy)
                {
                    float sk = 1f - Vol2Fs.Smooth(0.1f, 1.0f, since);
                    if (sk > 0.01f) _slime.Quad(p.C + Vector3.up * 0.018f, Right, Fwd, p.R * 2.6f * (0.4f + 0.6f * sk), new Color(1f, 1f, 1f, sk), 0.27f);
                    if (flame > 0.05f && Time.frameCount % 2 == 0) { Vol2FsB1.EmitV(_fire, p.C + Random.insideUnitSphere * p.R * 0.6f + Vector3.up * 0.1f, Vector3.up * 1.4f, 0.55f * flame, -1f); if (Time.frameCount % 6 == 0) Vol2FsB1.Emit(_steam, p.C + Vector3.up * 0.4f, 1); }
                }
                else
                {
                    if (p.VineMesh != null)
                    {
                        foreach (var tb in p.Vines.Tubes) tb.Grow = 1f - burn;
                        p.Vines.Write(p.VineMesh);
                        for (int j = 0; j < p.Dry.Items.Count; j++) { var l = p.Dry.Items[j]; l.Grow = 1f - Vol2Fs.Smooth(0.05f + 0.03f * j, 0.4f + 0.03f * j, since); p.Dry.Items[j] = l; }
                        p.Dry.Write(p.DryMesh);
                    }
                    if (flame > 0.05f)
                    {
                        float near = Mathf.Lerp(0.55f, 1f, outw);
                        for (int k = 0; k < 2; k++)
                        {
                            Vector3 q = p.C + Random.insideUnitSphere * p.R; q.y = Ground + 0.1f;
                            Vol2FsB1.EmitV(_fire, q, Vector3.up * (1.1f + 0.8f * flame), (0.45f + 0.35f * Random.value) * flame * near, -1f);
                        }
                        // the column: a 2 - 3 m pillar of flame for the outer patches (stacked tall tongues rising fast), staggered by the chain's ignite order
                        if (outw > 0.2f && since < 1.0f)
                            for (int k = 0; k < 2; k++)
                            {
                                Vector3 q = p.C + Random.insideUnitSphere * 0.25f; q.y = Ground + 0.2f + Random.value * (0.6f + 1.4f * outw);
                                Vol2FsB1.EmitV(_tall, q, Vector3.up * (2.4f + 2.2f * outw), (1.0f + 0.6f * outw) * flame, -1f);
                            }
                        else if (Time.frameCount % 4 == 0) Vol2FsB1.EmitV(_tall, p.C + Vector3.up * 0.2f + Random.insideUnitSphere * 0.3f, Vector3.up * 1.6f, 0.7f * flame * near, -1f);
                        if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_embers, p.C + Vector3.up * 0.3f, 2);
                        if (Time.frameCount % 8 == 0) Vol2FsB1.Emit(_smoke, p.C + Vector3.up * 1.4f, 1);
                    }
                    if (burn > 0.05f && burn < 0.98f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_ash, p.C + Vector3.up * 0.3f, 3);
                    float bl = Vol2Fs.Smooth(0f, 0.2f, since) * (1f - Vol2Fs.Smooth(2.4f, 3.6f, since));
                    if (bl > 0.01f) _bed.Quad(p.C + Vector3.up * 0.022f, Right, Fwd, p.R * 1.8f, new Color(1f, 1f, 1f, bl * 0.8f), 0.41f + p.R);
                }
                p.L.intensity = (4f + 4f * (p.Enemy ? 0f : Mathf.Clamp01((Vector3.Distance(p.C, G(Apex)) - R) / 3.5f))) * flame * (0.75f + 0.25f * Mathf.PerlinNoise(t * 9f, p.C.x));
            }
            // the bodies in the burning wood: engulfed for their mark's life; the mark = a fire bed at the feet; warning gutters; natural end = ash rising
            foreach (var m in MarkList)
            {
                float ml = MarkLife(m, t, 0.2f), mw = MarkWarn(m, t), ex = MarkExit(m, t), age = MarkAge(m, t);
                if (age < 0f) continue;
                if (ml > 0.01f) _bed.Quad(m.Centre + Vector3.up * 0.026f, Right, Fwd, 1.5f * ShareScale(m.Share), new Color(1f, 1f, 1f, ml), 0.63f);
                Vector3 c = m.Hop != null ? CentreNow(m.Hop) : m.Centre + Vector3.up * 0.6f;
                if (m.EndAt < 0f)
                {
                    if (mw > 0.01f && Random.value < mw * 0.7f) continue;
                    if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, c + Random.insideUnitSphere * 0.4f, Vector3.up * 1.3f, 0.38f + 0.25f * Random.value, -1f);      // r3: a little smaller (no single blob)
                    if (Time.frameCount % 6 == 0) Vol2FsB1.EmitV(_tall, m.Centre + Random.insideUnitSphere * 0.3f + Vector3.up * 0.2f, Vector3.up * 1.5f, 0.7f * (1f - 0.6f * mw), -1f);
                    if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_embers, c, 2);
                }
                else if (Natural(m) && ex < 1f && Time.frameCount % 2 == 0) { Vol2FsB1.Emit(_ash, c + Random.insideUnitSphere * 0.4f, 4); Vol2FsB1.Emit(_embers, m.Centre + Vector3.up * 0.4f, 1); }
            }
            _line.Write(_lineMesh); _scorch.Write(_scorchMesh); _fuse.Write(_fuseMesh); _slime.Write(_slimeMesh); _bed.Write(_bedMesh);
        }

        protected override float Tail => 1.0f;
    }
}
