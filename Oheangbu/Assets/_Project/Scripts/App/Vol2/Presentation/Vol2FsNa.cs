using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>나 (fire, ㅏ path, 한 번) - free showcase, concepts na_b (a fire-ink spear from the brush tip, corkscrew flame) + na_c (the hit: fire inside
    /// huge ink strokes bursting outward). Beats, all on the scope clock: charge at the brush tip until the launch (the emitter's StartUs) -> the projectile
    /// flies the vol.2 path head's own speed to the predicted first body (Vol2LegacyPathRecipe.SingleCue: target + impact clock) and draws a solid burning
    /// ink spear behind it from the brush to the body (r2: the flight is only ~0.1 s at 36 m/s, so the line it leaves carries the read) -> the hit at
    /// launch + impact clock -> the body burns until the cast ends (the executor ends the cast only when its 2 s burn ticks are done).</summary>
    public sealed class Vol2FsNa : Vol2FsRun
    {
        static readonly Color Orange = new Color(1f, 0.42f, 0.08f), Hot = new Color(1f, 0.78f, 0.35f), Ink = new Color(0.035f, 0.025f, 0.02f);
        static readonly Color Rim = new Color(4f, 1.3f, 0.25f, 1f);

        ParticleSystem _chargeFlame, _chargeGlow; Light _chargeLight;
        Transform _proj; ParticleSystem _core, _shell, _drops, _embers, _smokeTrail, _lineFire; Light _projLight;
        Mesh _spear, _spearGlow, _helix; readonly Vol2FsStrips _spearS = new Vol2FsStrips(), _glowS = new Vol2FsStrips(), _helixS = new Vol2FsStrips();
        ParticleSystem _flash, _fireball, _hitDrops, _hitEmbers, _smoke, _ring; Light _hitLight;
        Mesh _strokes; readonly Vol2FsStrips _strip = new Vol2FsStrips();
        Mesh _scorch; readonly Vol2FsStrips _scorchStrip = new Vol2FsStrips();
        ParticleSystem _burnFire, _burnSmoke, _burnEmbers; Light _burnLight; Transform _burnT;

        bool _launched, _hit; float _impactRel = -1f, _hitAt = -1f, _endRel = -1f, _lastFrac;
        Vector3 _from, _to, _hitPoint; long _targetId; Transform _target; Bounds _bounds; bool _hasTarget;
        struct Stroke { public Vector3 Dir; public float Len, Width, Delay, Life, Rnd; }
        readonly List<Stroke> _burst = new List<Stroke>();

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsMat.Make(tex, tint, intensity, addMix);
        Vol2FsMat FireMat(float intensity, float addMix) { var m = M("fs_flame", Color.white, intensity, addMix); m.Ramp = "fs_ramp_fire"; m.RampScale = 1.7f; m.NoiseAmt = 0.65f; m.Erode = 0.28f; return m; }

        public override void Began(long nowUs)
        {
            var root = Root.transform;
            _chargeFlame = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(FireMat(2.4f, 0.65f)), Rate = 110f, Loop = true, Life0 = 0.18f, Life1 = 0.3f, Speed0 = -2.4f, Speed1 = -3.8f, Size0 = 0.2f, Size1 = 0.42f,
                Shape = ParticleSystemShapeType.Sphere, Radius = 0.6f, Tiles = 2, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 0.3f, World = false, Max = 200 }, root, "ChargeFlame");
            _chargeGlow = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Orange, 3f, 1f)), Rate = 30f, Loop = true, Life0 = 0.12f, Life1 = 0.18f, Speed0 = 0f, Speed1 = 0f,
                Size0 = 0.5f, Size1 = 0.7f, Radius = 0.01f, World = false, Max = 20 }, _chargeFlame.transform, "ChargeGlow");
            _chargeLight = Vol2Fs.PointLight(_chargeFlame.transform, "ChargeLight", Orange, 4f);
            _chargeFlame.transform.position = Tip();
            _chargeFlame.Play(true);
        }

        void Launch(long nowUs)
        {
            _launched = true;
            var root = Root.transform;
            _chargeFlame.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f;
            var cue = Vol2LegacyPathRecipe.SingleCue(Inst, new Vol2LegacyCue { Letter = '나', Body = Inst.Log.Glyph.Body, Origin = Inst.Log.Spec.Origin, Forward = Inst.Log.Spec.Forward, PathLength = Inst.Log.Spec.PathLength }, nowUs);
            _from = Tip();
            _impactRel = Now(nowUs) + Mathf.Max(0.02f, cue.ImpactClock);
            _hasTarget = cue.HasTarget; _targetId = cue.TargetId;
            if (_hasTarget)
            {
                _target = TargetTransform(_targetId);
                var t = Target(_targetId);
                _bounds = BodyBounds(_target, cue.TargetPoint, t != null ? t.Radius : 0.5f);
                _to = _bounds.center + Vector3.up * _bounds.extents.y * 0.15f;
                Vector3 toward = _from - _to; toward.y = 0f;
                _hitPoint = _to + toward.normalized * Mathf.Min(_bounds.extents.x, _bounds.extents.z) * 0.6f;
            }
            else { _to = cue.FallbackPoint + Vector3.up * 1.1f; _hitPoint = _to; }
            Note = "launch target=" + _targetId + " impact=" + cue.ImpactClock.ToString("0.000") + "s";

            _proj = new GameObject("Projectile").transform; _proj.SetParent(root, false); _proj.position = _from;
            _core = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Rate = 120f, Loop = true, Life0 = 0.06f, Life1 = 0.1f, Speed0 = 0f, Speed1 = 0f, Size0 = 1.3f, Size1 = 1.7f, Radius = 0.02f, World = false, Max = 30, SortFudge = -20 }, _proj, "Core");
            _shell = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(FireMat(2.6f, 0.55f)), RatePerMeter = 30f, Rate = 160f, Loop = true, Life0 = 0.25f, Life1 = 0.55f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.5f, Size1 = 1.1f,
                Radius = 0.22f, Tiles = 2, Noise = 1.2f, NoiseFreq = 1.6f, SizeStart = 0.7f, SizePeak = 1.25f, SizeEnd = 0.5f, VelOverLife = new Vector3(0f, 1.2f, 0f), Max = 500 }, _proj, "Shell");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(2.2f, 0.6f, 0.1f, 1f); drop.EdgeWidth = 0.18f; drop.NoiseAmt = 0.3f; drop.Soft = 0.15f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), RatePerMeter = 12f, Loop = true, Life0 = 0.5f, Life1 = 0.9f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.08f, Size1 = 0.22f, Radius = 0.3f, Gravity = 1.4f, Tiles = 2, Max = 300 }, _proj, "InkDrops");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), RatePerMeter = 20f, Loop = true, Life0 = 0.5f, Life1 = 1.1f, Speed0 = 1f, Speed1 = 3.5f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.25f, Gravity = -0.25f,
                Noise = 1.5f, NoiseFreq = 2f, Stretch = true, StretchVel = 0.05f, Max = 300 }, _proj, "Embers");
            var smoke = M("fs_puff", new Color(0.08f, 0.06f, 0.05f, 1f), 1f, 0f); smoke.Opacity = 0.55f; smoke.NoiseAmt = 0.6f; smoke.Erode = 0.35f;
            _smokeTrail = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(smoke), RatePerMeter = 6f, Loop = true, Life0 = 0.7f, Life1 = 1.3f, Speed0 = 0.2f, Speed1 = 0.6f, Size0 = 0.5f, Size1 = 0.9f, Radius = 0.2f,
                Tiles = 2, SizeStart = 0.6f, SizePeak = 1.4f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 0.6f, 0f), Max = 200 }, _proj, "SmokeTrail");
            // flames that burn along the spear line (emitted where the head has passed)
            _lineFire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(FireMat(2.2f, 0.6f)), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 0.2f, Speed1 = 0.9f, Size0 = 0.3f, Size1 = 0.7f, Radius = 0.12f, Tiles = 2,
                VelOverLife = new Vector3(0f, 1.6f, 0f), Noise = 0.8f, SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 0.4f, Max = 400 }, root, "LineFire");
            _lineFire.Play(false);
            // the spear: solid black ink with a burning rim, a glow along it and three corkscrew ribbons round it
            Vol2Fs.MeshObject(root, "Spear", Kit.InkSolid("fs_stroke2", Ink, Rim, 0.16f, 0.5f, 0.28f, 1.6f), out _spear);
            var glow = M("fs_dot", Orange, 2.4f, 1f); glow.NoiseAmt = 0.6f; glow.NoiseScale = 2f; glow.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "SpearGlow", Kit.Mat(glow), out _spearGlow);
            Vol2Fs.MeshObject(root, "Helix", Kit.Mat(M("fs_dot", Hot, 3f, 1f)), out _helix);
            _projLight = Vol2Fs.PointLight(_proj, "ProjLight", Orange, 7f); _projLight.intensity = 6f;
            foreach (var ps in new[] { _core, _shell, _drops, _embers, _smokeTrail }) ps.Play(false);
            Place(nowUs);
        }

        /// <summary>The flight line: a shallow lob from the brush tip to the hit (r4: seen from behind the shoulder a straight line is end-on; the lob gives it a readable arch).</summary>
        Vector3 Line(float s) => Vector3.Lerp(_from, _hitPoint, s) + Vector3.up * 4f * LobHeight * s * (1f - s);
        const float LobHeight = 0.9f;

        float Frac(long nowUs) => _impactRel > LaunchRel ? Mathf.Clamp01((Now(nowUs) - LaunchRel) / (_impactRel - LaunchRel)) : 1f;

        void Place(long nowUs)
        {
            float f = _hit ? 1f : Frac(nowUs);
            Vector3 p = Line(f);
            _proj.position = p;
            // fire along the stretch the head crossed this frame
            int n = Mathf.CeilToInt((f - _lastFrac) * 60f);
            for (int k = 0; k < n; k++)
                _lineFire.Emit(new ParticleSystem.EmitParams { position = Line(Mathf.Lerp(_lastFrac, f, (k + 0.5f) / n)), applyShapeToPosition = true }, 1);
            _lastFrac = f;
        }

        void DrawSpear(float t)
        {
            _spearS.Clear(); _glowS.Clear(); _helixS.Clear();
            float f = _hit ? 1f : _lastFrac;
            if (f <= 0.01f) { _spearS.Write(_spear); _glowS.Write(_spearGlow); _helixS.Write(_helix); return; }
            float since = _hit ? t - _hitAt : 0f;
            float life = 1f - Vol2Fs.Smooth(0.35f, 1.1f, since);
            float glowLife = 1f - Vol2Fs.Smooth(0.05f, 0.7f, since);
            var cam = CamPos;
            Vector3 head = Vector3.Lerp(_from, _hitPoint, f);
            Vector3 axis = (_hitPoint - _from).normalized;
            Vector3 nrm = Vector3.Cross(axis, Vector3.up).normalized, bin = Vector3.Cross(axis, nrm);
            const int N = 24;
            var pts = new Vector3[N]; var w = new float[N]; var al = new float[N]; var gw = new float[N]; var ga = new float[N];
            for (int k = 0; k < N; k++)
            {
                float u = k / (N - 1f);                                     // 0 = the head (fat) .. 1 = the brush (dry tail)
                pts[k] = Line(f * (1f - u)) + Vector3.up * 0.05f * Mathf.Sin(u * 9f);
                w[k] = Mathf.Lerp(0.75f, 0.2f, u);
                al[k] = life * (1f - 0.55f * u * Vol2Fs.Smooth(0f, 0.4f, since));      // the tail end burns away first
                gw[k] = Mathf.Lerp(1.0f, 0.35f, u); ga[k] = glowLife;
            }
            if (life > 0.01f) _spearS.Strip(pts, w, null, Color.white, al, 0.13f, cam, 0f, 1f);
            if (glowLife > 0.01f) _glowS.Strip(pts, gw, null, Color.white, ga, 0.4f, cam, 0f, 1f);
            // corkscrew ribbons round the line (3 turns over the flight), only up to the head
            float len = Vector3.Distance(_from, head);
            for (int s = 0; s < 2 && glowLife > 0.01f; s++)
            {
                var hp = new List<Vector3>(); var hw = new List<float>(); var ha = new List<float>();
                int M2 = 40;
                for (int k = 0; k < M2; k++)
                {
                    float u = k / (M2 - 1f);
                    float along = len * u;
                    float ang = along * Mathf.PI * 2f / 1.5f + s * Mathf.PI;
                    float rad = 0.34f * Mathf.Clamp01(along / 0.6f) * (1f + 0.4f * since);
                    hp.Add(Line(f * u) + (nrm * Mathf.Cos(ang) + bin * Mathf.Sin(ang)) * rad);
                    hw.Add(0.12f * (0.4f + 0.6f * u)); ha.Add(glowLife * Mathf.Clamp01(u * 3f));
                }
                _helixS.Strip(hp, hw, null, Color.white, ha, 0.7f + s, cam, 0f, 1f);
            }
            _spearS.Write(_spear); _glowS.Write(_spearGlow); _helixS.Write(_helix);
        }

        void Hit(long nowUs)
        {
            _hit = true; _hitAt = Now(nowUs);
            var root = Root.transform;
            Place(nowUs);
            foreach (var ps in new[] { _core, _shell, _drops, _embers, _smokeTrail }) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _projLight.intensity = 0f;
            var at = new GameObject("Hit").transform; at.SetParent(root, false); at.position = _hitPoint;
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Orange, 5f, 1f)), Burst = 2, Life0 = 0.1f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 1.8f, Size1 = 2.4f, Radius = 0.05f, SizeStart = 0.4f, SizePeak = 1f, SizePeakAt = 0.2f, SizeEnd = 1.1f, Max = 4, SortFudge = -30 }, at, "Flash");
            _fireball = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(FireMat(2.3f, 0.3f)), Burst = 30, Life0 = 0.4f, Life1 = 0.85f, Speed0 = 2.5f, Speed1 = 6.5f, Size0 = 0.9f, Size1 = 2.0f, Radius = 0.3f, Drag = 1.0f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizePeakAt = 0.25f, SizeEnd = 0.9f, VelOverLife = new Vector3(0f, 1.4f, 0f), Max = 60 }, at, "Fireball");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(2.5f, 0.7f, 0.12f, 1f); drop.EdgeWidth = 0.2f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _hitDrops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Burst = 130, Life0 = 0.8f, Life1 = 1.6f, Speed0 = 5f, Speed1 = 13f, Size0 = 0.12f, Size1 = 0.45f, Radius = 0.25f, Gravity = 2.2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 140 }, at, "InkDrops");
            _hitEmbers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 6f, 1f)), Burst = 260, Life0 = 0.6f, Life1 = 1.7f, Speed0 = 5f, Speed1 = 15f, Size0 = 0.03f, Size1 = 0.08f, Radius = 0.2f, Gravity = 1.1f, Drag = 1.2f,
                Stretch = true, StretchVel = 0.045f, Noise = 1f, NoiseFreq = 2f, Max = 280 }, at, "Embers");
            var smoke = M("fs_puff", new Color(0.07f, 0.05f, 0.045f, 1f), 1f, 0f); smoke.Opacity = 0.7f; smoke.NoiseAmt = 0.6f; smoke.Erode = 0.35f;
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(smoke), Burst = 12, Life0 = 1.4f, Life1 = 2.4f, Speed0 = 0.8f, Speed1 = 2.0f, Size0 = 1.1f, Size1 = 2.0f, Radius = 0.5f, Drag = 1.5f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.3f, SizePeakAt = 0.5f, SizeEnd = 1.7f, VelOverLife = new Vector3(0f, 0.8f, 0f), Max = 20, SortFudge = 10 }, at, "Smoke");
            var ringT = new GameObject("RingAt").transform; ringT.SetParent(root, false); ringT.position = new Vector3(_hitPoint.x, Ground + 0.06f, _hitPoint.z);
            var ring = M("fs_ring", Orange, 3.2f, 1f); ring.NoiseAmt = 0.4f; ring.Soft = 0.02f;
            _ring = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ring), Burst = 1, Life0 = 0.42f, Life1 = 0.42f, Speed0 = 0f, Speed1 = 0f, Size0 = 5.5f, Size1 = 5.5f, Radius = 0.01f, Horizontal = true,
                SizeStart = 0.08f, SizePeak = 0.8f, SizePeakAt = 0.6f, SizeEnd = 1f, AlphaHoldTo = 0.3f, Max = 2 }, ringT, "Ring");
            _hitLight = Vol2Fs.PointLight(at, "HitLight", Orange, 10f);
            foreach (var ps in new[] { _flash, _fireball, _hitDrops, _hitEmbers, _smoke, _ring }) ps.Play(false);
            // the ink flung out of the hit (picture 3 / na_c): 12 solid strokes, thick at their flung ends, burning rims; kept low and sideways so they stay over the ground
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", Ink, Rim, 0.16f, 0.5f, 0.28f, 1.6f), out _strokes);
            int seed = (int)(Inst.Cast.Value & 0x7fff);
            Vector3 fwd = (_hitPoint - _from); fwd.y = 0f; fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            for (int k = 0; k < 16; k++)
            {
                float a = (k / 16f + Vol2Fs.Hash(seed, k) * 0.05f) * Mathf.PI * 2f;
                float up = Mathf.Sin(a);
                Vector3 d = side * Mathf.Cos(a) * 1.15f + Vector3.up * (up > 0f ? up * 0.55f : up * 0.25f) + fwd * (0.35f * (Vol2Fs.Hash(seed, k + 50) - 0.3f));
                _burst.Add(new Stroke { Dir = d.normalized, Len = 2.6f + 2.9f * Vol2Fs.Hash(seed, k + 100), Width = 0.42f + 0.4f * Vol2Fs.Hash(seed, k + 200), Delay = 0.025f * Vol2Fs.Hash(seed, k + 300), Life = 0.85f + 0.5f * Vol2Fs.Hash(seed, k + 400), Rnd = Vol2Fs.Hash(seed, k + 500) });
            }
            var sc = M("fs_scorch", new Color(0.03f, 0.02f, 0.015f, 1f), 1f, 0f); sc.Edge = new Color(1.6f, 0.4f, 0.05f, 1f); sc.EdgeWidth = 0.12f; sc.Soft = 0.001f; sc.NoiseAmt = 0.5f; sc.Opacity = 0.85f;
            Vol2Fs.MeshObject(root, "Scorch", Kit.Mat(sc), out _scorch);
            if (_hasTarget)
            {
                _burnT = new GameObject("Burn").transform; _burnT.SetParent(root, false);
                float rx = Mathf.Max(0.3f, _bounds.extents.x * 0.8f), ry = Mathf.Max(0.3f, _bounds.extents.y * 0.7f), rz = Mathf.Max(0.3f, _bounds.extents.z * 0.8f);
                _burnFire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(FireMat(2.4f, 0.55f)), Rate = 70f, Loop = true, Life0 = 0.35f, Life1 = 0.7f, Speed0 = 0.2f, Speed1 = 0.6f, Size0 = 0.35f, Size1 = 0.85f, Shape = ParticleSystemShapeType.Sphere,
                    Radius = 1f, ShapeScale = new Vector3(rx, ry, rz), Tiles = 2, VelOverLife = new Vector3(0f, 2.2f, 0f), Noise = 0.8f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 0.3f, Max = 200 }, _burnT, "BurnFire");
                _burnSmoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(smoke), Rate = 9f, Loop = true, Life0 = 1.2f, Life1 = 2f, Speed0 = 0.2f, Speed1 = 0.5f, Size0 = 0.7f, Size1 = 1.3f, Radius = 1f, ShapeScale = new Vector3(rx, ry, rz),
                    Tiles = 2, VelOverLife = new Vector3(0f, 1.4f, 0f), SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.8f, Max = 40, SortFudge = 10 }, _burnT, "BurnSmoke");
                _burnEmbers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Rate = 30f, Loop = true, Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 1.5f, Size0 = 0.03f, Size1 = 0.06f, Radius = 1f,
                    ShapeScale = new Vector3(rx, ry, rz), VelOverLife = new Vector3(0f, 1.8f, 0f), Noise = 1.4f, NoiseFreq = 2f, Stretch = true, StretchVel = 0.05f, Max = 100 }, _burnT, "BurnEmbers");
                _burnLight = Vol2Fs.PointLight(_burnT, "BurnLight", Orange, 5f);
                FollowBurn();
                foreach (var ps in new[] { _burnFire, _burnSmoke, _burnEmbers }) ps.Play(false);
            }
        }

        void FollowBurn()
        {
            if (_burnT == null) return;
            _burnT.position = _target != null ? BodyBounds(_target, _target.position, 0.5f).center : _bounds.center;
        }

        public override void Tick(long nowUs)
        {
            if (Root == null) return;
            float t = Now(nowUs);
            if (!_launched)
            {
                _chargeFlame.transform.position = Tip();
                float k = LaunchRel > 0.02f ? Mathf.Clamp01(t / LaunchRel) : 1f;
                _chargeLight.intensity = 4f * k;
                _chargeFlame.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.3f, k);
                if (t >= LaunchRel - 1e-4f) Launch(nowUs);
                else return;
            }
            if (!_hit)
            {
                Place(nowUs);
                DrawSpear(t);
                if (t >= _impactRel) Hit(nowUs);
                return;
            }
            DrawSpear(t);
            float h = t - _hitAt;
            _hitLight.intensity = h < 0.05f ? Mathf.Lerp(0f, 16f, h / 0.05f) : 16f * Mathf.Exp(-(h - 0.05f) * 6f);
            _strip.Clear();
            var cam = CamPos;
            var pts = new Vector3[7]; var w = new float[7]; var al = new float[7];
            foreach (var s in _burst)
            {
                float a = h - s.Delay; if (a <= 0f) continue;
                float grow = Vol2Fs.EaseOutBack(a / 0.12f);
                float life = 1f - Vol2Fs.Smooth(0.25f, s.Life, a);
                if (life <= 0.01f) continue;
                Vector3 p0 = _hitPoint + s.Dir * 0.15f;
                Vector3 droop = Vector3.down * 0.35f * s.Len * (0.3f + 0.7f * s.Rnd) * Vol2Fs.Smooth(0.05f, 0.6f, a);
                Vector3 curl = Vector3.Cross(s.Dir, (cam - _hitPoint).normalized).normalized * s.Len * 0.35f * (s.Rnd - 0.5f);
                for (int k = 0; k < 7; k++)
                {
                    float u = k / 6f;                                         // 0 = at the hit (the wet fat head) .. 1 = the flung tip (dry streaks)
                    pts[k] = p0 + s.Dir * s.Len * grow * u + (droop + curl) * u * u;
                    w[k] = s.Width * (0.55f + 0.75f * Mathf.Sin(Mathf.Min(1f, u * 1.4f + 0.1f) * Mathf.PI)) * (1f - 0.55f * u);
                    al[k] = life * (1f - 0.3f * u * Vol2Fs.Smooth(0.2f, 0.8f, a));
                }
                _strip.Strip(pts, w, null, Color.white, al, s.Rnd, cam, 0f, 1f);
            }
            _strip.Write(_strokes);
            _scorchStrip.Clear();
            float scorchLife = _endRel > 0f ? 1f - Vol2Fs.Smooth(_endRel, _endRel + 1.2f, t) : 1f;
            float sg = Vol2Fs.Smooth(0f, 0.25f, h);
            if (scorchLife > 0.01f) _scorchStrip.Quad(new Vector3(_hitPoint.x, Ground + 0.025f, _hitPoint.z), Right, Fwd, 2.8f * (0.4f + 0.6f * sg), new Color(1f, 1f, 1f, scorchLife), 0.37f);
            _scorchStrip.Write(_scorch);
            FollowBurn();
            bool ended = Inst.CastIsEnded || Inst.Log.Finished;
            if (ended && _endRel < 0f)
            {
                _endRel = t;
                foreach (var ps in new[] { _burnFire, _burnSmoke, _burnEmbers }) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            if (_burnLight != null) _burnLight.intensity = _endRel > 0f ? Mathf.Max(0f, 3.5f * (1f - (t - _endRel) / 0.6f)) : 3f + 1.5f * Mathf.PerlinNoise(t * 9f, 3.3f);
        }

        public override bool Done(long nowUs)
        {
            if (Root == null) return true;
            float t = Now(nowUs);
            if (!_hit) return t > LaunchRel + 3f;
            if (_endRel < 0f) return false;
            if (t > _endRel + 6f) return true;
            return t > _endRel + 1.3f && !ParticlesAlive();
        }
    }
}
