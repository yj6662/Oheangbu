using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>금>화 무뎌짐 (막힘) - free showcase batch 8, concept mf_b (a thin white-gold steel lance with black ink streamers drives into the enemy's fire; its point
    /// goes red-hot, softens, droops and drips molten metal into the flames - blunted to a stub - while the fire holds, lower).
    /// Facts only: the acting shape is spent at the commit (the lance's point meets the flames, glows, melts and droops, drips; the blunted lance slides back and
    /// flakes away into ink); the enemy's fire patch survives at x0.5 (Blocked(half)), its life not renewed - the flames settle at ~0.55 of their height with half
    /// the burning, and stay (the enemy's dark-cored fire) to the material's own end (Expired: they die down to smoking coals). Nothing is damaged, nothing is born.</summary>
    public sealed class Vol2FsRxMetalFire : Vol2FsReact
    {
        static readonly Color Gold = new Color(1f, 0.86f, 0.55f), Molten = new Color(1f, 0.42f, 0.1f);
        Mesh _shaftMesh, _tipMesh, _glintMesh, _inkMesh, _scarMesh; Light _keep; Light _heat;
        readonly Vol2FsTubes _shaft = new Vol2FsTubes(), _tip = new Vol2FsTubes();
        readonly Vol2FsStrips _glint = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _scar = new Vol2FsStrips();
        Vol2FsTubes.Tube _s, _m;
        ParticleSystem _sparks, _drips, _flare, _flakes, _smoke, _hot;
        Vector3 _tail, _point, _axis;
        bool _touched; float _flamesRate = -1f;
        const float In = 0.07f, MeltEnd = 0.7f, BackFrom = 0.75f;
        /// <summary>2026-10-27 weak-points pass: the common half tears while the spear melts.</summary>
        protected override float HalfAt => 0.25f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            var steel = Kit.Metal(1.7f, 0.55f, Gold * 0.08f) ??      // r3: brighter (r2 a long grey pole)
                Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.85f, 0.85f, 0.9f), 1f, 0.85f);
            Vol2Fs.MeshObject(root, "Lance", steel, out _shaftMesh);
            var molten = Kit.Bark(new Color(0.25f, 0.07f, 0.02f), new Color(5f, 1.5f, 0.3f), 0.5f, "fs_bark", "fs_bark_glow");
            Vol2Fs.MeshObject(root, "MoltenTip", molten, out _tipMesh);
            var glint = M("fs_streak", Gold * 1.5f, 3f, 1f); glint.NoiseAmt = 0.15f; glint.Soft = 0.02f; glint.Scroll = new Vector2(-5f, 0f);
            Vol2Fs.MeshObject(root, "Glint", Kit.Mat(glint), out _glintMesh);
            Vol2Fs.MeshObject(root, "InkStreamers", Kit.InkSolid("fs_stroke2", InkC, Gold * 1.2f, 0.14f, 0.55f, 0.35f, 1.3f), out _inkMesh);
            Vol2Fs.MeshObject(root, "HalfScar", Vol2FsRxBlockKit.ScarMat(Kit), out _scarMesh);
            _keep = Vol2FsRxBlockKit.KeepLight(root, Contacted);
            _heat = Vol2Fs.PointLight(root, "TipHeat", Molten, 5f);
            var hot = M("fs_dot", Molten * 1.3f, 3.2f, 1f); hot.Soft = 0.6f;
            _hot = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(hot), Life0 = 0.12f, Life1 = 0.18f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.45f, Size1 = 0.7f, Radius = 0.02f, Max = 30, SortFudge = 10 }, root, "TipGlow");
            _hot.Play(false);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.15f, Life1 = 0.4f, Speed0 = 4f, Speed1 = 11f, Size0 = 0.02f, Size1 = 0.045f, Gravity = 1.4f, Drag = 1.2f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, EndTint = new Color(1f, 0.3f, 0.05f), Max = 400 }, root, "Sparks");
            _drips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Molten * 1.4f, 4f, 1f)), Life0 = 0.4f, Life1 = 0.7f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.06f, Size1 = 0.11f, Gravity = 2.8f, Stretch = true, StretchVel = 0.07f, StretchLen = 1.5f, EndTint = new Color(0.3f, 0.06f, 0.02f), Max = 300 }, root, "Drips");
            _flare = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick()), 0.25f, 0.5f, 0.3f, 0.55f, 1.3f, 300), root, "Flare");
            _flakes = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 0.7f, Life1 = 1.4f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.04f, Size1 = 0.1f, Gravity = -0.2f, Noise = 0.8f, NoiseFreq = 1.2f, Spin = 300f, Max = 300 }, root, "Flakes");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsPolish.Soot(0.5f)), Life0 = 1f, Life1 = 1.8f, Speed0 = 0.3f, Speed1 = 1f, Size0 = 0.35f, Size1 = 0.7f, SizeStart = 0.6f, SizePeak = 1f, SizeEnd = 1.6f, VelOverLife = new Vector3(0f, 0.6f, 0f), Max = 80 }, root, "Smoke");
            foreach (var ps in new[] { _sparks, _drips, _flare, _flakes, _smoke }) ps.Play(false);
            // the lance drives in from the caster's left, slightly down, its point into the flames' near side
            _point = G(P - Dir * Radius * 0.25f) + Vector3.up * 0.55f;
            _tail = _point - Dir * 1.9f - Side * 1.0f + Vector3.up * 1.1f;      // r3: a short spear from just above-left (r2: a long pole from the sky; r1: a flat bar along the bottom)
            _axis = (_point - _tail).normalized;
            _s = _shaft.Add(new[] { _tail, Vector3.Lerp(_tail, _point, 0.5f), _point }, 0.06f, 0.05f, 16); _s.Sides = 6; _s.Sharp = 0.25f;
            _m = _tip.Add(new[] { _point, _point + _axis * 0.25f, _point + _axis * 0.5f }, 0.07f, 0.04f, 8); _m.Sides = 6; _m.Sharp = 0.05f;
            if (Patch != null && Patch.Ps.Count > 0 && Patch.Ps[0] != null) _flamesRate = Patch.Ps[0].emission.rateOverTime.constant;
            Note += " | blunt mf_b";
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            _glint.Clear(); _ink.Clear();
            if (!_touched && t >= In)
            {
                _touched = true; HitStop(t, 0.06f);
                for (int k = 0; k < 180; k++) Vol2FsB1.EmitV(_sparks, _point, (Random.onUnitSphere - _axis * 0.6f + Vector3.up * 0.3f).normalized * Random.Range(3f, 11f), -1f, -1f);
                for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_flare, _point + Random.insideUnitSphere * 0.25f, Vector3.up * Random.Range(1f, 2.4f), -1f, -1f);
            }
            float melt = Vol2Fs.Smooth(In, MeltEnd, t);                      // the point softening (0 .. 1)
            float back = Vol2Fs.Smooth(BackFrom, BackFrom + 0.6f, t);          // the blunted lance slides back and flakes away
            float drive = Mathf.Clamp01(t / In);
            Vector3 slide = -_axis * 0.6f * back;
            // the shaft: grows in, then shortens from its point side as it slides back and goes to ink
            Vector3 pt = Vector3.Lerp(_tail, _point, 0.35f + 0.65f * drive) + slide;
            Vector3 tl = _tail + slide;
            _s.Ctrl[0] = tl; _s.Ctrl[1] = Vector3.Lerp(tl, pt, 0.5f); _s.Ctrl[2] = pt;
            Vol2FsRxBlockKit.Refresh(_s);
            _s.Grow = 1f - Vol2Fs.Smooth(0.2f, 1f, back);
            // the molten point: grows from the touch, droops down and shortens as it drips away
            float droop = 0.65f * melt;
            _m.Ctrl[0] = pt; _m.Ctrl[1] = pt + _axis * 0.22f + Vector3.down * droop * 0.35f; _m.Ctrl[2] = pt + _axis * 0.3f + Vector3.down * droop;
            Vol2FsRxBlockKit.Refresh(_m);
            _m.Grow = t < In ? 0f : Mathf.Clamp01(melt * 3f) * (1f - Vol2Fs.Smooth(0.55f, 1f, melt)) * (1f - back);
            _shaft.Write(_shaftMesh); _tip.Write(_tipMesh);
            if (t >= In && melt < 0.98f && back < 0.2f)
            {
                Vector3 tipNow = Vol2FsTubes.TipOf(_m);
                Vol2FsB1.EmitV(_drips, tipNow, Vector3.down * Random.Range(0.2f, 0.8f) + Random.insideUnitSphere * 0.2f, -1f, -1f);
                if (Random.value < 0.4f) Vol2FsB1.EmitV(_sparks, tipNow, (Random.onUnitSphere + Vector3.up).normalized * Random.Range(1f, 3f), -1f, -1f);
                if (Random.value < 0.3f) Vol2FsB1.EmitV(_smoke, tipNow, Vector3.up * 0.6f, -1f, -1f);
            }
            if (back > 0.05f && back < 0.95f && Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_flakes, Vector3.Lerp(tl, pt, Random.value), Random.insideUnitSphere * 0.6f + Vector3.up * 0.3f, -1f, -1f);
            // the strike's white-gold glint along the lance (fast), its afterimage and two black ink streamers
            float gl = 1f - Vol2Fs.Smooth(0.1f, 0.8f, t);
            Vector3 bow = Vector3.Lerp(tl, pt, 0.5f) + Vector3.up * 0.15f;
            Vol2FsRxBlockKit.Ribbon(_glint, tl, bow, pt, 0f, 1f, 0.04f, 0.24f, gl, 0.4f, cam);
            Vol2FsRxBlockKit.Ribbon(_glint, tl - Side * 0.12f, bow - Side * 0.2f, pt - Side * 0.12f, 0f, 1f, 0.06f, 0.32f, gl * 0.3f, 0.7f, cam);
            float il = 1f - Vol2Fs.Smooth(0.3f, 1.2f, t);
            Vol2FsRxBlockKit.Ribbon(_ink, tl - Side * 0.5f + Vector3.up * 0.5f, bow - Side * 1.1f + Vector3.up * 0.9f, pt - Dir * 0.6f + Vector3.up * 0.2f, 0f, 1f, 0.08f, 0.75f, il, 0.2f, cam, 0.2f, t * 9f);
            Vol2FsRxBlockKit.Ribbon(_ink, tl - Side * 0.2f - Vector3.up * 0.2f, bow - Side * 0.6f - Vector3.up * 0.3f, pt - Dir * 0.4f, 0f, 1f, 0.06f, 0.5f, il * 0.8f, 0.55f, cam, 0.16f, t * 11f + 2f);
            _scar.Clear(); if (Patch != null) { Vol2FsRxBlockKit.Survivor(_scar, P, Ground, Radius, Dir, t, ContactEndAt, ContactExpireRel, cam); Vol2FsRxBlockKit.KeepLightTick(_keep, P, t, ContactEndAt); }
            _glint.Write(_glintMesh); _ink.Write(_inkMesh); _scar.Write(_scarMesh);
            // the red-hot point: a glow and a light on it while it melts
            float heat = t < In ? 0f : Mathf.Clamp01(melt * 4f) * (1f - Vol2Fs.Smooth(0.7f, 1f, melt)) * (1f - back);
            Vector3 tipAt = Vol2FsTubes.TipOf(_m);
            if (_heat != null) { _heat.transform.position = tipAt; _heat.intensity = 8f * heat; }
            if (heat > 0.05f) Vol2FsB1.EmitV(_hot, tipAt, Vector3.zero, 0.45f + 0.35f * heat, 0.15f);
            // the enemy's fire: half the burning stays (~0.55 of its height, half the tongues); at its end it dies down to smoking coals
            if (Patch != null)
            {
                float halve = Vol2Fs.Smooth(In + 0.05f, In + 0.5f, t);
                float end = ContactEndAt >= 0f ? Vol2Fs.Smooth(ContactEndAt, ContactEndAt + 0.8f, t) : 0f;
                // 2026-10-27 weak-points pass: the lost half is the common tear (Vol2FsRxHalf); the kept coals only slump a little (r3 shrank the whole fire to 0.55)
                Patch.Pose(Mathf.Lerp(1f, 0.85f, halve) * Mathf.Lerp(1f, 0.25f, end), 0f);
                if (_flamesRate > 0f && Patch.Ps.Count > 0 && Patch.Ps[0] != null) { var em = Patch.Ps[0].emission; em.rateOverTime = _flamesRate * Mathf.Lerp(1f, 0.5f, halve) * (1f - end); }
            }
        }

        protected override void OnContactEnd(Vol2MaterialEnd why, float t)
        {
            if (Patch == null) return;
            Patch.StopParticles();
            if (why == Vol2MaterialEnd.Expired) for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_smoke, G(P + Random.insideUnitSphere * Radius * 0.6f) + Vector3.up * 0.15f, Vector3.up * Random.Range(0.4f, 1f), -1f, -1f);
            else Patch.SetActive(false);
        }
    }
}
