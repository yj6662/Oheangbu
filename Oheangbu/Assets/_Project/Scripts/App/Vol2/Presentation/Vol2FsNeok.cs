using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>넉 (fire, ㅓ attachment, 남긴다) - free showcase batch 3, concept neok_a (the caster's arm, sleeve and the whole great brush are wreathed in roaring
    /// flame like a burning mantle, the brush head a torch, every swing leaves an arc of fire and burning ink) + neok_b's glowing coal plates on the shaft. Unlike
    /// 너's fire wheel guard (a parry flash) it is a fire that STAYS ON the weapon for the material's 6 s. Beats, executor facts only: the launch = the fire catches
    /// from the bristles down the shaft to the sleeve -> worn for the Attached material's life (flames off the shaft, a torch at the tip, embers, coal plates);
    /// the brush's real motion leaves an arc of fire; a body the swing touched (the executor's contacts) = a burst on it and its 2 s burn (a mirror) -> the last
    /// 0.4 s = the flames gutter -> natural expiry (CSV: 끝부터 타 재로 날림) = the fire burns back from the tip to the hand into rising ash.</summary>
    public sealed class Vol2FsNeok : Vol2FsWear
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.1f), Hot = new Color(1f, 0.8f, 0.4f), Ink = new Color(0.035f, 0.025f, 0.02f);
        ParticleSystem _fire, _tall, _embers, _smoke, _ash, _drops; Light _torch, _sleeveLight;
        Mesh _trailMesh, _inkTrailMesh, _platesMesh, _burstMesh; readonly Vol2FsStrips _trail = new Vol2FsStrips(), _inkTrail = new Vol2FsStrips(), _burstS = new Vol2FsStrips();
        readonly Vol2FsTubes _plates = new Vol2FsTubes();
        System.Collections.Generic.List<Vol2FsB1.Stroke> _burst; Vector3 _burstAt; float _burstT = -1f;
        bool _exitFx;
        protected override float ExpireSeconds => 1.2f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _fire = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.7f, 0.4f)), Life0 = 0.25f, Life1 = 0.5f, Speed0 = 0.1f, Speed1 = 0.5f, Size0 = 0.18f, Size1 = 0.42f, Radius = 0.05f, Tiles = 2,
                VelOverLife = new Vector3(0f, 1.8f, 0f), Noise = 0.8f, NoiseFreq = 1.5f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 0.3f, Max = 2400 }, root, "Fire");
            _tall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Fire(1.8f, 0.4f)), Life0 = 0.35f, Life1 = 0.6f, Speed0 = 0.5f, Speed1 = 1.5f, Size0 = 0.5f, Size1 = 0.9f, Radius = 0.1f, Tiles = 2,
                VelOverLife = new Vector3(0f, 2.4f, 0f), Noise = 1f, SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 0.4f, Max = 600 }, root, "TallFire");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Hot, 5f, 1f)), Life0 = 0.6f, Life1 = 1.6f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.025f, Size1 = 0.055f, Radius = 0.1f, Gravity = -0.2f,
                Noise = 1.5f, NoiseFreq = 2f, Stretch = true, StretchVel = 0.05f, Max = 1400 }, root, "Embers");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Smoke(0.45f)), Life0 = 1f, Life1 = 1.8f, Speed0 = 0.2f, Speed1 = 0.6f, Size0 = 0.5f, Size1 = 0.9f, Radius = 0.1f, Drag = 0.8f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 1.8f, VelOverLife = new Vector3(0f, 1f, 0f), Max = 200, SortFudge = 12 }, root, "Smoke");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1f, Life1 = 2f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.03f, Size1 = 0.1f, Radius = 0.08f, VelOverLife = new Vector3(0f, 1.5f, 0f),
                Noise = 1.2f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.6f, Max = 900 }, root, "Ash");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(1.8f, 0.6f, 0.1f, 1f); drop.EdgeWidth = 0.18f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.5f, Life1 = 1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.06f, Size1 = 0.2f, Radius = 0.15f, Gravity = 1.8f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "InkDrops");
            foreach (var ps in new[] { _fire, _tall, _embers, _smoke, _ash, _drops }) ps.Play(false);
            var tr = M("fs_stroke", Color.white, 2.6f, 0.75f); tr.Ramp = "fs_ramp_fire"; tr.RampScale = 1.4f; tr.NoiseAmt = 0.55f; tr.Erode = 0.3f; tr.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "FireTrail", Kit.Mat(tr), out _trailMesh);
            Vol2Fs.MeshObject(root, "InkTrail", Kit.InkSolid("fs_stroke2", Ink, new Color(2.4f, 0.75f, 0.12f, 1f), 0.08f, 0.5f, 0.3f, 1.5f), out _inkTrailMesh);
            Vol2Fs.MeshObject(root, "CoalPlates", Kit.Bark(new Color(0.14f, 0.1f, 0.08f), new Color(3f, 1f, 0.18f), 0.1f, "fs_bark", "fs_vein"), out _platesMesh);
            Vol2Fs.MeshObject(root, "HitInk", Kit.InkSolid("fs_stroke2", Ink, new Color(2.4f, 0.8f, 0.12f, 1f), 0.12f, 0.5f, 0.28f, 1.6f), out _burstMesh);
            _torch = Vol2Fs.PointLight(root, "Torch", Orange, 6f);
            _sleeveLight = Vol2Fs.PointLight(root, "SleeveLight", Orange, 4f);
        }

        protected override void OnCharge(float t, float k) { _torch.transform.position = Tip(); _torch.intensity = 2.5f * k; if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_embers, Tip(), 1); }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs) { Vol2FsB1.Emit(_tall, Tip(), 6); Vol2FsB1.Emit(_embers, Tip(), 60); }

        void Struck(Vector3 c, Vector3 away)
        {
            _burstAt = c; _burstT = Inst.Rel(NowUs);
            var side = Vector3.Cross(Vector3.up, away);
            _burst = Vol2FsB1.Burst(Seed + Hits.Count * 7 + Touches.Count, 8, side, away, 0.8f, 1.8f, 0.16f, 0.32f, 0.6f, 0.4f);
            Vol2FsB1.Emit(_tall, c, 8); Vol2FsB1.Emit(_fire, c, 50); Vol2FsB1.Emit(_embers, c, 160); Vol2FsB1.Emit(_drops, c, 30); Vol2FsB1.Emit(_smoke, c + Vector3.up * 0.4f, 4);
        }
        protected override void OnStruck(Struck s) { Struck(CentreNow(s), FlatN(CentreNow(s) - Inst.Origin)); }
        protected override void OnTouch(Struck s) { if (Hits.Exists(h => h.Id == s.Id && Mathf.Abs(h.At - s.At) < 0.05f)) return; Struck(CentreNow(s), FlatN(CentreNow(s) - Inst.Origin)); }
        protected override void OnChanged(string what) { if (what == "extended" || what == "thickened") Vol2FsB1.Emit(_tall, Tip(), 6); }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float worn = Worn(t), warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            Vector3 tip = Tip(), grip = Grip(), sleeve = Sleeve();
            // the fire runs down from the bristles to the sleeve as it catches; on the natural end it burns back from the tip to the hand
            float reachDown = Vol2Fs.Smooth(0f, 0.3f, h);                          // 0 = only the tip burns .. 1 = down to the sleeve
            float burnBack = Exiting ? (NaturalEnd ? Vol2Fs.Smooth(0f, 0.9f, ex) : 1f) : 0f;   // 0 = all burns .. 1 = nothing left
            float uTop = 1.05f - 1.25f * burnBack, uLow = 1.05f - 1.3f * reachDown;
            bool lit = worn > 0.02f && uTop > uLow;
            if (lit)
            {
                int perFrame = warn > 0f ? 3 : 9;
                for (int k = 0; k < perFrame; k++)
                {
                    if (warn > 0f && Random.value < warn * 0.6f) continue;
                    float u = Random.Range(Mathf.Max(-0.2f, uLow), uTop);
                    Vector3 p = u < 0f ? Vector3.Lerp(grip, sleeve, -u / 0.2f) : Vector3.LerpUnclamped(grip, tip, u);
                    Vol2FsB1.EmitV(_fire, p + Random.insideUnitSphere * 0.05f, Vector3.up * (0.9f + Random.value) + Random.insideUnitSphere * 0.25f, (0.14f + 0.16f * Random.value) * (1f + 0.5f * pulse), -1f);      // r2: smaller tongues along the whole shaft
                }
                if (uTop > 0.95f)
                {
                    // the torch at the bristles
                    if (Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_tall, tip + Random.insideUnitSphere * 0.06f, Vector3.up * 1.3f, (0.3f + 0.22f * Random.value) * (1f - 0.5f * warn), -1f);      // r2: a torch, not a fireball
                    if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_embers, tip, 2);
                    if (Time.frameCount % 9 == 0) Vol2FsB1.Emit(_smoke, tip + Vector3.up * 0.5f, 1);
                }
                if (uLow < 0f) Vol2FsB1.EmitV(_fire, sleeve + Random.insideUnitSphere * 0.14f, Vector3.up * 1.1f, 0.2f + 0.15f * Random.value, -1f);
            }
            if (Exiting && NaturalEnd && ex < 1f && Time.frameCount % 2 == 0)
            {
                Vector3 front = Vector3.LerpUnclamped(grip, tip, Mathf.Max(0f, uTop));
                Vol2FsB1.Emit(_ash, front, 4); Vol2FsB1.Emit(_embers, front, 2);
            }
            if (Exiting && !_exitFx) { _exitFx = true; if (!NaturalEnd) { Vol2FsB1.Emit(_smoke, tip, 5); Vol2FsB1.Emit(_ash, Vector3.Lerp(grip, tip, 0.5f), 30); } }
            // coal plates along the shaft (they glow while the fire lives, crumble with it)
            _plates.Tubes.Clear();
            if (worn > 0.02f)
            {
                Vector3 ax = (tip - grip).normalized, n = Vector3.Cross(ax, Vector3.up).normalized; if (n.sqrMagnitude < 1e-4f) n = Right;
                for (int k = 0; k < 5; k++)
                {
                    float u = 0.15f + 0.16f * k; if (u > uTop) continue;
                    Vector3 c = Vector3.LerpUnclamped(grip, tip, u), o = Quaternion.AngleAxis(k * 77f, ax) * n;
                    var tb = _plates.Add(new[] { c - ax * 0.09f + o * 0.04f, c + o * 0.06f, c + ax * 0.09f + o * 0.04f }, 0.07f * worn, 0.045f * worn, 4);
                    tb.Grow = 1f; tb.Sides = 5; tb.Sharp = 0.02f;
                }
            }
            _plates.Write(_platesMesh);
            // the swing's arc of fire and burning ink (only while the brush really moves)
            _trail.Clear(); _inkTrail.Clear();
            float sp = TipSpeed(t);
            float tl = worn * Vol2Fs.Smooth(1.5f, 4f, sp);
            if (tl > 0.01f) { SwingTrail(_trail, t, 0.22f, 0.7f, tl, 0.37f); SwingTrail(_inkTrail, t, 0.18f, 0.22f, tl, 0.81f, 0.02f); }
            _trail.Write(_trailMesh); _inkTrail.Write(_inkTrailMesh);
            // the struck body's ink burst and its 2 s burn (a mirror of the body action)
            _burstS.Clear();
            if (_burst != null && _burstT >= 0f) Vol2FsB1.DrawBurst(_burstS, _burst, _burstAt, t - _burstT, cam, 0.4f, 0.1f);
            _burstS.Write(_burstMesh);
            foreach (var s in Hits) if (t - s.At < 2f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_fire, CentreNow(s) + Random.insideUnitSphere * 0.35f, Vector3.up * 1.2f, 0.4f + 0.3f * Random.value, -1f);
            float flick = 0.75f + 0.25f * Mathf.PerlinNoise(t * 10f, 2.3f);
            _torch.transform.position = tip + Vector3.up * 0.2f;
            _torch.intensity = (lit && uTop > 0.9f ? 5f : 0f) * worn * flick * (1f - 0.6f * warn) * (1f + 0.6f * pulse);
            _sleeveLight.transform.position = sleeve;
            _sleeveLight.intensity = lit ? 2.5f * worn * flick * (1f - 0.6f * warn) : 0f;
        }
    }
}
