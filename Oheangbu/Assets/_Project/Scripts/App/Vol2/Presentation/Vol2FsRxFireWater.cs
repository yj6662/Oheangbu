using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>화>수 꺼짐 (막힘) - free showcase batch 8, concept fa_a (a roaring stream of fire and ink smoke runs low into the enemy's dark pool and is QUENCHED at the
    /// waterline: its head folds flat on the water and dies black with a hiss, a burst of white steam and dark smoke, splashes; the pool survives, smaller).
    /// Facts only: the acting shape is spent at the commit (the flame stream reaches the pool's edge, flattens and is snuffed from its head back); the enemy's water
    /// patch survives at x0.5 (Blocked(half)), its life not renewed - the pool draws in to ~0.7 of its spread (half its area) under a short steam veil and stays
    /// (the enemy's dark water) to the material's own end (Expired: it drains into the ground). Nothing is damaged, nothing is born.</summary>
    public sealed class Vol2FsRxFireWater : Vol2FsReact
    {
        static readonly Color Orange = new Color(1f, 0.45f, 0.12f), Hot = new Color(1f, 0.85f, 0.55f);
        Mesh _bodyMesh, _coreMesh, _smokeMesh, _flatMesh, _scarMesh; Light _keep; Light _glow; float _lastT;
        readonly Vol2FsStrips _body = new Vol2FsStrips(), _core = new Vol2FsStrips(), _smoke = new Vol2FsStrips(), _flat = new Vol2FsStrips(), _scar = new Vol2FsStrips();
        ParticleSystem _licks, _embers, _steam, _soot, _splash, _drops;
        Vector3 _hit, _tail, _bow;
        bool _quenched; float _veilUntil = -1f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            var body = M("fs_flame", Orange, 1.6f, 0.8f); body.NoiseAmt = 0.35f; body.Erode = 0.16f;      // r3: was noise .6 / erode .3 (it broke into flat orange chunks)
            body.Soft = 0.1f; body.Scroll = new Vector2(-2.5f, 0.3f);
            Vol2Fs.MeshObject(root, "FireBody", Kit.Mat(body), out _bodyMesh);
            var core = M("fs_streak", Hot * 1.3f, 2.6f, 1f); core.NoiseAmt = 0.3f; core.Soft = 0.03f; core.Erode = 0.12f; core.Scroll = new Vector2(-4f, 0f);
            Vol2Fs.MeshObject(root, "FireCore", Kit.Mat(core), out _coreMesh);
            Vol2Fs.MeshObject(root, "FireSmoke", Kit.InkSolid("fs_stroke2", InkC, Orange * 1.3f, 0.16f, 0.55f, 0.35f, 1.3f), out _smokeMesh);
            // the head flattened on the water: black char with a dying orange rim
            Vol2Fs.MeshObject(root, "Snuffed", Kit.InkSolid("fs_scorch", InkC, Orange * 2f, 0.2f, 0.6f, 0.3f, 1.2f), out _flatMesh);
            Vol2Fs.MeshObject(root, "HalfScar", Vol2FsRxBlockKit.ScarMat(Kit), out _scarMesh);
            _keep = Vol2FsRxBlockKit.KeepLight(root, Contacted);
            _glow = Vol2Fs.PointLight(root, "FireGlow", Orange, 7f);
            _licks = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick()), 0.2f, 0.45f, 0.25f, 0.5f, 1.2f, 600), root, "Licks");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.55f, 0.2f), 4f, 1f)), Life0 = 0.3f, Life1 = 0.8f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.2f, Drag = 0.8f, EndTint = new Color(0.1f, 0.08f, 0.08f), Max = 500 }, root, "Embers");
            var steam = Vol2FsB3.Dust(new Color(0.58f, 0.66f, 0.72f, 1f), 0.26f); steam.NoiseAmt = 0.75f; steam.Erode = 0.45f; steam.Soft = 0.6f;      // r2: was 0.9 white x0.6 (white popcorn)
            _steam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(steam), Life0 = 0.9f, Life1 = 1.7f, Speed0 = 0.6f, Speed1 = 2.6f, Size0 = 0.8f, Size1 = 1.4f, SizeStart = 0.5f, SizePeak = 1.1f, SizeEnd = 2.0f, Drag = 1.6f, VelOverLife = new Vector3(0f, 0.9f, 0f), Noise = 0.6f, NoiseFreq = 0.5f, Max = 200 }, root, "Steam");
            _soot = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.55f)), Life0 = 0.9f, Life1 = 1.6f, Speed0 = 0.4f, Speed1 = 1.8f, Size0 = 0.4f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.6f, Drag = 1.4f, VelOverLife = new Vector3(0f, 0.8f, 0f), Max = 120 }, root, "Soot");
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.4f, "fs_splash"), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 5.5f, Size0 = 0.14f, Size1 = 0.32f, Gravity = 1.6f, Drag = 0.6f, Max = 300 }, root, "Splash");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.55f, 0.85f, 1f), 2.4f, 0.7f)), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.025f, Size1 = 0.05f, Gravity = 2.6f, Stretch = true, StretchVel = 0.06f, StretchLen = 1.2f, Max = 400 }, root, "Drops");
            foreach (var ps in new[] { _licks, _embers, _steam, _soot, _splash, _drops }) ps.Play(false);
            // the stream runs low from the caster's left into the pool's near edge
            _hit = G(P - Dir * Radius * 0.75f) + Vector3.up * 0.22f;
            _tail = _hit - Dir * 3.0f - Side * 0.6f + Vector3.up * 0.3f;      // r3: low along the ground into the pool
            _bow = Vector3.Lerp(_tail, _hit, 0.5f) - Side * 1.0f + Vector3.up * 0.15f;
            Note += " | quench fa_a";
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            _body.Clear(); _core.Clear(); _smoke.Clear(); _flat.Clear();
            const float At = 0.06f;
            if (!_quenched && t >= At)
            {
                _quenched = true; HitStop(t, 0.07f); _veilUntil = t + 1.4f;
                if (_glow != null) { _glow.transform.position = _hit + Vector3.up * 0.4f; _glow.intensity = 10f; }
                // r3: the steam fans out ALONG the waterline (r2: a white ball over the hit)
                for (int k = 0; k < 18; k++) { float sx = Random.Range(-1f, 1f); Vol2FsB1.EmitV(_steam, _hit + Side * sx * 1.5f + Vector3.up * 0.1f, Side * Mathf.Sign(sx) * Random.Range(0.6f, 1.8f) + Vector3.up * Random.Range(0.5f, 1.4f) + Dir * 0.5f, -1f, -1f); }
                for (int k = 0; k < 32; k++) Vol2FsB1.EmitV(_soot, _hit + Side * Random.Range(-0.9f, 0.9f) + Random.insideUnitSphere * 0.3f, Vector3.up * Random.Range(0.8f, 2.4f) + Random.insideUnitSphere, -1f, -1f);
                for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_splash, _hit + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 1.2f + Dir * 0.4f).normalized * Random.Range(2f, 5.5f), -1f, -1f);
                for (int k = 0; k < 80; k++) Vol2FsB1.EmitV(_drops, _hit + Random.insideUnitSphere * 0.5f, (Random.onUnitSphere + Vector3.up).normalized * Random.Range(1f, 4f), -1f, -1f);
                for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_embers, _hit, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(1f, 4f), -1f, -1f);
            }
            // the stream: in (0 .. At), then snuffed from its head backward - the head turns to black ink as the fire goes out
            if (!_quenched)
            {
                float reach = Mathf.Clamp01(0.5f + t / At * 0.5f);
                Stream(cam, 0f, reach, 1f, t);
            }
            else
            {
                float a = t - At;
                float dead = Vol2Fs.Smooth(0f, 0.45f, a);                     // how much of the stream (from the head) is out
                float life = 1f - Vol2Fs.Smooth(0.35f, 1.1f, a);
                if (life > 0.01f) Stream(cam, 0f, Mathf.Lerp(1f, 0.25f, dead), life, t);
                // the head folded flat on the water: a black scorched fan spreading along the waterline, its orange rim dying
                float fl = 1f - Vol2Fs.Smooth(0.4f, 1.4f, a);
                if (fl > 0.01f)
                {
                    float spread = Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.15f));
                    var pts = new Vector3[9]; var w = new float[9]; var al = new float[9]; var sd = new Vector3[9];
                    for (int k = 0; k < 9; k++)
                    {
                        float u = k / 8f - 0.5f;
                        pts[k] = G(_hit + Side * u * 2.8f * spread + Dir * (0.15f + 0.25f * (1f - Mathf.Abs(u) * 2f))) + Vector3.up * 0.03f;
                        w[k] = 1.0f * spread * (1f - Mathf.Abs(u) * 1.4f + 0.35f); al[k] = fl; sd[k] = Dir;
                    }
                    _flat.Strip(pts, w, sd, Color.white, al, 0.4f, cam);
                }
                // the last licks gasp up and die; a thin steam veil hangs over the pool for a moment
                if (a < 0.25f && Time.frameCount % 2 == 0) for (int k = 0; k < 4; k++) Vol2FsB1.EmitV(_licks, _hit + Side * Random.Range(-0.5f, 0.5f), Vector3.up * Random.Range(0.4f, 1.2f), 0.25f, 0.25f);
                if (t < _veilUntil && Time.frameCount % 5 == 0) Vol2FsB1.EmitV(_steam, G(P + Random.insideUnitSphere * Radius * 0.6f) + Vector3.up * 0.1f, Vector3.up * Random.Range(0.6f, 1.4f), Random.Range(0.4f, 0.8f), -1f);
            }
            _scar.Clear(); if (Patch != null) { Vol2FsRxBlockKit.Survivor(_scar, P, Ground, Radius, Dir, t, ContactEndAt, ContactExpireRel, cam); Vol2FsRxBlockKit.KeepLightTick(_keep, P, t, ContactEndAt); }
            _body.Write(_bodyMesh); _core.Write(_coreMesh); _smoke.Write(_smokeMesh); _flat.Write(_flatMesh); _scar.Write(_scarMesh);
            float dt = Mathf.Clamp(t - _lastT, 0f, 0.05f); _lastT = t;
            if (_glow != null) { if (!_quenched) { _glow.transform.position = Vol2FsRxBlockKit.Bez(_tail, _bow, _hit, 0.8f); _glow.intensity = 6f; } else _glow.intensity = Mathf.MoveTowards(_glow.intensity, 0f, dt * 22f); }
            // the enemy's pool: half its area stays (spread x0.7 = area x0.5), drawn in right after the quench; at its end it drains
            if (Patch != null)
            {
                float halve = Vol2Fs.Smooth(At + 0.1f, At + 0.45f, t);
                float spread = Mathf.Lerp(1f, 0.71f, halve);
                float drain = ContactEndAt >= 0f ? Vol2Fs.Smooth(ContactEndAt, ContactEndAt + 0.9f, t) : 0f;
                Patch.Pose(1f, drain * 0.05f, Mathf.Max(0.02f, spread * (1f - drain)));
            }
        }

        void Stream(Vector3 cam, float u0, float u1, float life, float t)
        {
            Vol2FsRxBlockKit.Ribbon(_body, _tail, _bow, _hit, u0, u1, 0.3f, 1.05f, life, 0.33f, cam, 0.08f, t * 18f);
            Vol2FsRxBlockKit.Ribbon(_core, _tail, _bow, _hit, u0, u1, 0.08f, 0.32f, life, 0.61f, cam, 0.14f, t * 18f);
            Vol2FsRxBlockKit.Ribbon(_smoke, _tail + Vector3.up * 0.25f, _bow + Vector3.up * 0.3f, _hit + Vector3.up * 0.3f, u0, u1, 0.2f, 0.6f, life, 0.12f, cam, 0.1f, t * 15f + 1f);
            if (life > 0.3f)
                for (int k = 0; k < 8; k++) { float u = Random.Range(u0, u1); Vol2FsB1.EmitV(_licks, Vol2FsRxBlockKit.Bez(_tail, _bow, _hit, u) + Random.insideUnitSphere * 0.15f, Vector3.up * Random.Range(0.5f, 1.4f), -1f, -1f); }
        }

        protected override void OnContactEnd(Vol2MaterialEnd why, float t)
        {
            if (Patch == null) return;
            Patch.StopParticles();
            if (why != Vol2MaterialEnd.Expired) Patch.SetActive(false);
        }
    }
}
