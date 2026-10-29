using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>넌 (fire, ㅓ attachment, 번진다 극치) - free showcase batch 4, concept neon_a (the brush is a torch carrying THREE equal fire spirits; the swing strikes
    /// the close bear and spirits shoot from it in great S-arcs of burning ink to the other bears, each exploding equally hard). The peak's rule is drawn as a
    /// BUNDLE: the executor gives 넌 a 6 s Carrier material (amount 0) holding a fixed spread budget of 1.5 = three portions of 0.5; the look shows exactly three
    /// equal spirits circling the torch for the carrier's life, and each forward "Spread" contact spends one (HopsSent) - it leaves the torch, passes over the struck
    /// body (the executor spreads from it) and dives onto its neighbour with the same blast every time (fixed 0.5, no thinning). Whatever the effect-target cap
    /// leaves unspent keeps circling until the carrier ends (in the lab layout the first swing reaches A + two neighbours = the cap, the second swing's touch spreads
    /// nothing: one spirit stays). Marks (3 s) = a burning ink pool with a ring of low flames; warning = gutter; natural expiry (CSV: 끝부터 타 재로 날림) = the
    /// flames burn down from their tips into rising ash. The carrier's end = the torch burns down and the last spirit falls to ash.</summary>
    public sealed class Vol2FsNeon : Vol2FsSpread
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f), Hot = new Color(1f, 0.8f, 0.4f), Ink = new Color(0.035f, 0.025f, 0.02f);
        static readonly Color FireRim = new Color(2.6f, 0.9f, 0.15f, 1f);
        ParticleSystem _torch, _fire, _tall, _embers, _smoke, _ash, _drops, _spiritFire; Light _torchLight, _blastLight;
        Mesh _ribbonMesh, _glowMesh, _burstMesh, _poolMesh, _tongueMesh;
        readonly Vol2FsStrips _ribbon = new Vol2FsStrips(), _glow = new Vol2FsStrips(), _burstS = new Vol2FsStrips(), _pool = new Vol2FsStrips(), _tongues = new Vol2FsStrips();
        readonly List<(List<Vol2FsB1.Stroke> S, Vector3 At, float T)> _bursts = new List<(List<Vol2FsB1.Stroke>, Vector3, float)>();
        readonly Light[] _spiritLight = new Light[3];
        sealed class Shot { public Vector3 P0, P1, P2; public int Spirit; public Light L; }
        const int Portions = 3;                                   // the bundle: peak.넌 budget 1.5 / 0.5 per spread body (drawn, never decided here)
        float _carrierWarn;

        // r3 (Phantom Blade Zero reference, principles 2 + 5): the swing as a blade-edged crescent + two phantom afterimages in the body's accent colour
        Mesh _phantomMesh; readonly Vol2FsStrips _phantom = new Vol2FsStrips(); float _lastFast = -9f;
        protected override float HopSpeed => 7f;
        protected override float HopMin => 0.45f;
        protected override float HopMax => 0.65f;
        protected override float HopStagger => 0.16f;
        protected override float MarkExpireSeconds => 1.1f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _torch = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.4f)), Rate = 0f, Loop = true, Life0 = 0.25f, Life1 = 0.5f, Speed0 = 0.3f, Speed1 = 1f, Size0 = 0.2f, Size1 = 0.42f, Radius = 0.08f, Tiles = 2,
                VelOverLife = new Vector3(0f, 1.8f, 0f), Noise = 0.8f, NoiseFreq = 1.5f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 0.3f, Max = 600 }, root, "Torch");
            _torch.Play(false);
            _torchLight = Vol2Fs.PointLight(root, "TorchLight", Orange, 4.5f);
            _fire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.6f, 0.35f)), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0.2f, Speed1 = 0.9f, Size0 = 0.3f, Size1 = 0.6f, Radius = 0.12f, Tiles = 2,
                VelOverLife = new Vector3(0f, 2f, 0f), Noise = 0.9f, NoiseFreq = 1.3f, SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 0.35f, Max = 5000 }, root, "Fire");
            _spiritFire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.3f, 0.3f)), Life0 = 0.15f, Life1 = 0.3f, Speed0 = 0.1f, Speed1 = 0.4f, Size0 = 0.14f, Size1 = 0.26f, Radius = 0.04f, Tiles = 2,
                VelOverLife = new Vector3(0f, 0.8f, 0f), SizeEnd = 0.3f, Max = 900 }, root, "SpiritFire");
            _tall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.35f)), Life0 = 0.45f, Life1 = 0.8f, Speed0 = 1.2f, Speed1 = 3f, Size0 = 0.8f, Size1 = 1.4f, Radius = 0.3f, Tiles = 2,
                VelOverLife = new Vector3(0f, 3f, 0f), Noise = 1f, NoiseFreq = 0.9f, SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.35f, SizeEnd = 0.5f, Max = 500 }, root, "TallFire");
            // r2: orange, unstretched, slow (r1: white stretched streaks rained over the whole frame - embers at the torch next to the camera, intensity 5)
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.5f, 0.12f), 2.2f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.025f, Size1 = 0.05f, Radius = 0.25f, Gravity = -0.25f,
                Drag = 1.2f, Noise = 1.2f, NoiseFreq = 1.6f, Max = 800 }, root, "Embers");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.5f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 0.3f, Speed1 = 0.9f, Size0 = 0.8f, Size1 = 1.4f, Radius = 0.3f, Drag = 0.8f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.5f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 1.1f, 0f), Max = 300, SortFudge = 12 }, root, "Smoke");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.2f, Life1 = 2.4f, Speed0 = 0.2f, Speed1 = 1f, Size0 = 0.04f, Size1 = 0.12f, Radius = 0.3f, VelOverLife = new Vector3(0f, 1.6f, 0f),
                Noise = 1.2f, NoiseFreq = 1f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 1500 }, root, "Ash");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(1.8f, 0.6f, 0.1f, 1f); drop.EdgeWidth = 0.18f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.26f, Radius = 0.25f, Gravity = 1.8f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 500 }, root, "InkDrops");
            foreach (var ps in new[] { _fire, _spiritFire, _tall, _embers, _smoke, _ash, _drops }) ps.Play(false);
            // the spirits' S-arcs: a fat burning-ink stroke (solid, fire rim) + a fire glow line on it
            Vol2Fs.MeshObject(root, "InkFireArcs", Kit.InkSolid("fs_stroke2", Ink, FireRim, 0.22f, 0.45f, 0.3f, 1.4f), out _ribbonMesh);
            var glow = M("fs_streak", Color.white, 1.6f, 0.6f); glow.Ramp = "fs_ramp_fire"; glow.RampScale = 1.0f; glow.NoiseAmt = 0.5f;      // r2: fire, not white (r1 saturated)
            Vol2Fs.MeshObject(root, "FireGlow", Kit.Mat(glow), out _glowMesh);
            Vol2Fs.MeshObject(root, "InkBursts", Kit.InkSolid("fs_stroke2", Ink, new Color(2.4f, 0.8f, 0.12f, 1f), 0.12f, 0.5f, 0.28f, 1.6f), out _burstMesh);
            var pool = M("fs_crack", new Color(0.05f, 0.03f, 0.02f, 1f), 1f, 0f); pool.Soft = 0.001f; pool.NoiseAmt = 0.35f; pool.Erode = 0.3f; pool.Opacity = 0.92f; pool.Edge = new Color(2.6f, 0.8f, 0.12f, 1f); pool.EdgeWidth = 0.2f;
            Vol2Fs.MeshObject(root, "BurningPools", Kit.Mat(pool), out _poolMesh);
            // r2: the three spirits are three equal flame TONGUES riding the torch (r1 particle balls read as one white disc)
            var tongue = M("fs_flame", Color.white, 1.5f, 0.35f); tongue.Ramp = "fs_ramp_fire"; tongue.RampScale = 1.2f; tongue.NoiseAmt = 0.55f; tongue.Erode = 0.25f; tongue.Scroll = new Vector2(0f, -1.6f);
            Vol2Fs.MeshObject(root, "SpiritTongues", Kit.Mat(tongue), out _tongueMesh);
            _blastLight = Vol2Fs.PointLight(root, "BlastLight", Orange, 8f);
            Vol2Fs.MeshObject(root, "Phantoms", Vol2FsB4A.PhantomMat(Kit, new Color(1f, 0.28f, 0.08f)), out _phantomMesh);
            for (int i = 0; i < Portions; i++) _spiritLight[i] = Vol2Fs.PointLight(root, "SpiritLight" + i, Orange, 2.5f);
        }

        protected override void OnCharge(float t, float k) { if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_torch, Tip(), Vector3.up * 0.5f, 0.2f + 0.2f * k, -1f); }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs) { }
        protected override void OnCarrier() { Vol2FsB1.Emit(_fire, Tip(), 20); }

        protected override void OnCarrierEnd()
        {
            if (CarrierWhy == Vol2MaterialEnd.Aborted) return;
            Vol2FsB1.Emit(_smoke, Tip(), 3); Vol2FsB1.Emit(_ash, Tip(), 40); Vol2FsB1.Emit(_embers, Tip(), 12);
        }

        void Blast(Vector3 at, Vector3 feet, float k)
        {
            Vector3 fwd = FlatN(at - Inst.Origin), side = Vector3.Cross(Vector3.up, fwd);
            _bursts.Add((Vol2FsB1.Burst(Seed + _bursts.Count * 23, 8, side, fwd, 0.9f, 1.9f, 0.17f, 0.32f, 0.85f, 0.25f), at, -1f));
            Vol2FsB1.Emit(_fire, at, Mathf.RoundToInt(70 * k)); Vol2FsB1.Emit(_tall, feet + Vector3.up * 0.3f, Mathf.RoundToInt(10 * k)); Vol2FsB1.Emit(_embers, at, Mathf.RoundToInt(50 * k));
            Vol2FsB1.Emit(_drops, at, Mathf.RoundToInt(45 * k)); Vol2FsB1.Emit(_smoke, at + Vector3.up * 0.5f, 4);
            _blastLight.transform.position = at; _blastLight.intensity = 12f;
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s);
            if (s.Kind == "Second") { Vol2FsB1.Emit(_fire, c, 30); Vol2FsB1.Emit(_embers, c, 25); _blastLight.transform.position = c; _blastLight.intensity = 6f; return; }
            Blast(c, G(FeetOf(s)), 0.8f);
        }

        protected override void OnHop(Hop h)
        {
            // the spirit (the oldest unspent portion) leaves the torch, arcs high over the struck body and dives onto the neighbour
            int idx = 0; foreach (var o in Hops) if (!o.Back && o != h) idx++;
            var sh = new Shot { Spirit = Mathf.Min(idx, Portions - 1), L = Vol2Fs.PointLight(Root.transform, "ShotLight", Orange, 5f) };
            sh.P0 = Tip();
            Vector3 src = h.FromP + Vector3.up * 1.0f;
            sh.P2 = CentreNow(h);
            sh.P1 = Vector3.Lerp(src, sh.P2, 0.35f) + Vector3.up * 2.4f + Vector3.Cross(Vector3.up, FlatN(sh.P2 - src)) * (h.Order % 2 == 0 ? 0.6f : -0.6f);
            h.Data = sh;
        }

        protected override void OnLand(Hop h)
        {
            if (h.Data is Shot sh) sh.L.intensity = 0f;
            Blast(CentreNow(h), G(FeetOf(h)), 1f);               // every portion the same blast: the peak's fixed 0.5
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            Vector3 c = m.Centre + Vector3.up * 0.5f;
            if (Natural(m)) Vol2FsB1.Emit(_smoke, c + Vector3.up * 0.4f, 2); else { Vol2FsB1.Emit(_embers, c, 30); Vol2FsB1.Emit(_smoke, c, 4); }
        }

        static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float u) { float v = 1f - u; return v * v * a + 2f * v * u * b + u * u * c; }

        void ArcRibbon(Vol2FsStrips s, Shot sh, float u0, float u1, float width, float life, float rnd, Vector3 cam, float t)
        {
            if (life <= 0.01f || u1 - u0 < 0.01f) return;
            const int n = 18; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
            for (int k = 0; k < n; k++)
            {
                float v = k / (float)(n - 1), u = Mathf.Lerp(u0, u1, v);
                pts[k] = Bez(sh.P0, sh.P1, sh.P2, u) + Vector3.up * Mathf.Sin(u * 13f + t * 6f) * 0.08f;
                w[k] = width * (0.25f + 0.75f * Mathf.Sin(Mathf.Clamp01(v * 0.85f + 0.15f) * Mathf.PI)); al[k] = life * (0.45f + 0.55f * v);
            }
            s.Strip(pts, w, null, Color.white, al, rnd, cam, u0, u1);
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _blastLight.intensity = Mathf.MoveTowards(_blastLight.intensity, 0f, Time.deltaTime * 30f);
            // the torch and its bundle: the carrier's life (its own warning = the template's window before its ExpiresUs)
            float carry = CarrierLife(t, 0.5f);
            _carrierWarn = 0f;
            if (Carrier != null && CarrierEndAt < 0f) { float w = Inst.Template != null ? Mathf.Max(0.05f, Inst.Template.expiryWarnSeconds) : 0.4f; float e = Inst.Rel(Carrier.ExpiresUs); _carrierWarn = Vol2Fs.Smooth(e - w, e, t); }
            Vector3 tip = Tip(), axis = ShaftAxis;
            if (carry > 0.01f)
            {
                int n = Mathf.RoundToInt(3 * carry * (1f - 0.6f * _carrierWarn));
                for (int i = 0; i < n; i++) Vol2FsB1.EmitV(_torch, tip + axis * 0.08f + Random.insideUnitSphere * 0.07f, Vector3.up * 1.2f + axis * 0.4f, (0.3f + 0.2f * Random.value) * (1f - 0.4f * _carrierWarn), -1f);
                if (Time.frameCount % 8 == 0) Vol2FsB1.Emit(_embers, tip, 1);
                _torchLight.transform.position = tip; _torchLight.intensity = 3.5f * carry * (_carrierWarn > 0f ? 0.4f + 0.6f * Mathf.PerlinNoise(t * 23f, 0.4f) : 1f);
            }
            else _torchLight.intensity = 0f;
            if (CarrierEndAt >= 0f && t - CarrierEndAt < 0.6f && CarrierWhy != Vol2MaterialEnd.Aborted && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_ash, tip, 3);
            // the three spirits circling the torch head; spent ones have left (HopsSent)
            int spent = Mathf.Min(Portions, HopsSent(t));
            Vector3 n1 = Vector3.Cross(axis, Vector3.up); if (n1.sqrMagnitude < 1e-4f) n1 = Right; n1.Normalize(); Vector3 n2 = Vector3.Cross(axis, n1);
            _ribbon.Clear(); _glow.Clear(); _tongues.Clear(); _phantom.Clear();
            for (int i = 0; i < Portions; i++)
            {
                bool home = i >= spent && carry > 0.01f;
                if (!home) { _spiritLight[i].intensity = 0f; continue; }
                float a = t * 5.5f + i * Mathf.PI * 2f / Portions;
                Vector3 c = tip + axis * 0.12f + (n1 * Mathf.Cos(a) + n2 * Mathf.Sin(a)) * 0.42f;
                float sz = (0.3f + 0.04f * Mathf.Sin(t * 14f + i)) * carry * (1f - 0.5f * _carrierWarn);
                // a flame tongue standing up off the spirit (flickering), a small ink core under it
                {
                    var tp = new Vector3[7]; var tw = new float[7]; var ta = new float[7];
                    float hgt = 0.62f * carry * (1f - 0.4f * _carrierWarn) * (0.9f + 0.15f * Mathf.PerlinNoise(t * 9f, i * 2.3f));
                    for (int k = 0; k < 7; k++) { float v = k / 6f; tp[k] = c + Vector3.up * (hgt * v - 0.06f) + (n1 * Mathf.Cos(a + 1.6f) + n2 * Mathf.Sin(a + 1.6f)) * 0.07f * Mathf.Sin(v * 3f + t * 11f + i); tw[k] = 0.26f * Mathf.Sin(Mathf.Min(1f, v * 1.4f + 0.15f) * Mathf.PI) * (1f - 0.5f * v); ta[k] = carry * (1f - 0.3f * v); }
                    _tongues.Strip(tp, tw, null, Color.white, ta, 0.13f + i * 0.29f, cam, 0f, 1f);
                }
                if (Time.frameCount % 3 == i) Vol2FsB1.EmitV(_spiritFire, c, Vector3.up * 0.6f, sz * 0.6f, 0.2f);
                
                _spiritLight[i].transform.position = c; _spiritLight[i].intensity = 1.6f * carry;
                // a short burning tail behind each spirit (along its orbit)
                var pts = new Vector3[6]; var wd = new float[6]; var al = new float[6];
                for (int k = 0; k < 6; k++) { float b = a - k * 0.28f; pts[k] = tip + axis * 0.12f + (n1 * Mathf.Cos(b) + n2 * Mathf.Sin(b)) * 0.42f; wd[k] = 0.16f * (1f - k / 6f) * carry; al[k] = carry * (1f - k / 6f); }
                _glow.Strip(pts, wd, null, Color.white, al, 0.2f + i * 0.17f, cam, 0f, 1f);
            }
            // the swing's fire arc (the real brush path)
            if (TipSpeed() > 3f) _lastFast = t;
            if (carry > 0.01f && t - _lastFast < 0.12f) { Vol2FsB4A.BladeTrail(_glow, TipPath, t, 0.2f, 0.4f, carry, 0.66f, cam); Vol2FsB4A.BladeTrail(_ribbon, TipPath, t, 0.22f, 0.2f, carry * 0.8f, 0.12f, cam); Vol2FsB4A.Phantoms(_phantom, TipPath, t, 0.22f, 0.4f, carry, cam); }
            // the spent spirits: a fat S-arc of burning ink from the torch over the struck body to the neighbour, burning away from its tail after the landing
            foreach (var hop in Hops)
            {
                if (!(hop.Data is Shot sh) || t < hop.Start) continue;
                float g = HopK(hop, t);
                sh.P2 = CentreNow(hop);
                float after = g >= 1f ? t - hop.Start - hop.Travel : 0f;
                float tail = g < 1f ? Mathf.Max(0f, g - 0.6f) : Mathf.Min(1f, 0.4f + after * 1.4f);
                float life = 1f - Vol2Fs.Smooth(0.3f, 0.9f, after);
                ArcRibbon(_ribbon, sh, tail, g, 0.42f, life, 0.31f + hop.Order * 0.17f, cam, t);
                ArcRibbon(_glow, sh, tail, g, 0.2f, life, 0.51f + hop.Order * 0.13f, cam, t);
                if (g < 1f)
                {
                    Vector3 p = Bez(sh.P0, sh.P1, sh.P2, g);
                    Vol2FsB1.EmitV(_fire, p + Random.insideUnitSphere * 0.12f, Vector3.up * 0.8f, 0.55f, 0.3f);
                    if (Time.frameCount % 2 == 0) { Vol2FsB1.Emit(_embers, p, 1); Vol2FsB1.Emit(_drops, p, 1); }
                    sh.L.transform.position = p; sh.L.intensity = 5f;
                }
                else if (life > 0.05f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_ash, Bez(sh.P0, sh.P1, sh.P2, tail), 2);
            }
            _ribbon.Write(_ribbonMesh); _glow.Write(_glowMesh); _tongues.Write(_tongueMesh); _phantom.Write(_phantomMesh);
            // the ink bursts (struck body and every landing)
            _burstS.Clear();
            for (int i = 0; i < _bursts.Count; i++) { var bs = _bursts[i]; if (bs.T < 0f) { bs.T = t; _bursts[i] = bs; } Vol2FsB1.DrawBurst(_burstS, bs.S, bs.At, t - bs.T, cam, 0.5f, 0.1f); }
            _burstS.Write(_burstMesh);
            // the struck body burns its 2 s (the body action mirror)
            foreach (var s in Direct) if (s.Kind == "Instant" && t - s.At < 2f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, CentreNow(s) + Random.insideUnitSphere * 0.35f, Vector3.up * 1.2f, 0.45f + 0.3f * Random.value, -1f);
            // the marks: a burning ink pool + a ring of low flames round the feet; gutter in the warning; burn down to ash on natural expiry
            _pool.Clear();
            foreach (var m in MarkList)
            {
                float age = MarkAge(m, t); if (age < 0f) continue;
                float life = MarkLife(m, t, 0.2f), warn = MarkWarn(m, t), ex = MarkExit(m, t), k = ShareScale(m.Share);
                if (life > 0.01f || (m.EndAt >= 0f && ex < 1f)) _pool.Quad(m.Centre + Vector3.up * 0.025f, Right, Fwd, 2.2f * k, new Color(1f, 1f, 1f, Mathf.Max(life, 0.6f * (1f - ex))), 0.35f + m.Id % 5 * 0.1f);
                if (m.EndAt < 0f)
                {
                    if (warn > 0.01f && Random.value < warn * 0.7f) continue;
                    float a = Random.value * Mathf.PI * 2f;
                    Vector3 ringP = m.Centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * m.Radius * 1.3f * k;
                    Vol2FsB1.EmitV(_fire, ringP + Vector3.up * 0.1f, Vector3.up * 1.6f, (0.4f + 0.3f * Random.value) * (1f - 0.5f * warn), -1f);
                    if (m.Hop != null && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, CentreNow(m.Hop) + Random.insideUnitSphere * 0.35f, Vector3.up * 1.3f, 0.45f + 0.3f * Random.value, -1f);
                    if (Time.frameCount % 6 == 0) Vol2FsB1.Emit(_embers, m.Centre + Vector3.up * 0.3f, 1);
                }
                else if (Natural(m) && ex < 1f && Time.frameCount % 2 == 0) { Vol2FsB1.Emit(_ash, (m.Hop != null ? CentreNow(m.Hop) : m.Centre) + Random.insideUnitSphere * 0.4f, 4); Vol2FsB1.Emit(_embers, m.Centre + Vector3.up * 0.3f, 1); }
            }
            _pool.Write(_poolMesh);
        }
    }
}
