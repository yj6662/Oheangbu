using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>토>목 걸림 (막힘) - free showcase batch 8, concept ew_b (a great stepped slab of earth hurtles in and is SNAGGED mid-air in a net of the enemy's thorny
    /// vines, which shoot up out of the patch and catch it dead; it hangs, cracks with amber light and breaks apart into falling rocks; half the vines tear away).
    /// Facts only: the acting shape is spent at the commit (the slab flies in, is caught over the patch, hangs ~0.4 s, cracks and crumbles); the enemy's wood
    /// patch survives at x0.5 (Blocked(half)), its life not renewed - three of its six catching vines snap and recoil with a burst of splinters and leaves, the
    /// tangle settles at ~0.6 of its height and stays (the enemy's dark vines) to the material's own end (Expired: it withers and sinks). Nothing is damaged,
    /// nothing is born.</summary>
    public sealed class Vol2FsRxEarthWood : Vol2FsReact
    {
        static readonly Color Amber = new Color(0.95f, 0.66f, 0.3f);
        Transform _slab; Vector3 _from, _caught, _slabFwd;
        Mesh _vineMesh, _crackMesh, _trailMesh, _scarMesh; Light _keep; Light _glow;
        readonly Vol2FsTubes _vines = new Vol2FsTubes();
        readonly Vol2FsStrips _crack = new Vol2FsStrips(), _trail = new Vol2FsStrips(), _scar = new Vol2FsStrips();
        readonly List<Vol2FsTubes.Tube> _tendrils = new List<Vol2FsTubes.Tube>();
        readonly List<Vector3[]> _ctrl0 = new List<Vector3[]>();
        ParticleSystem _rocks, _dust, _chips, _leaves, _splinters;
        bool _caughtDone, _torn, _broken;
        const float FlyEnd = 0.1f, TearAt = 0.38f, BreakAt = 0.6f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            var stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.52f, 0.42f), 0f, 0.12f);      // r3: plain rough rock, no emission map (r1 / r2 the crack-vein emission read as a leopard print); the cracks are the look's own strips
            var dims = new Vector3(2.5f, 0.62f, 0.85f);
            _slab = Vol2FsB1.Prop(root, "Slab", Kit.SharedMesh("rxBlockSlab2", () => Vol2FsB1.Slab(91, dims, 0.85f)), stone).transform;
            _slab.gameObject.SetActive(false);
            _caught = G(P + Dir * 0.1f) + Vector3.up * 1.35f;
            _from = _caught - Dir * 3.0f - Side * 0.9f + Vector3.down * 0.6f;      // r3: it comes in low and rises into the vines (r2 floated in from the far left sky)
            _slabFwd = Side;
            var bark = Kit.Bark(new Color(0.78f, 0.66f, 0.74f), new Color(0.35f, 0.75f, 0.25f), 0.15f, "fs_vine", "fs_vein");      // r2: brighter (r1: the catching vines vanished against the ground)     // the enemy's own dark vines (same as its patch)
            Vol2Fs.MeshObject(root, "Snag", bark, out _vineMesh);
            var crack = M("fs_crack", Amber * 1.6f, 2.6f, 1f); crack.NoiseAmt = 0.2f; crack.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(crack), out _crackMesh);
            Vol2Fs.MeshObject(root, "Trail", Kit.InkSolid("fs_stroke2", InkC, Amber * 1.3f, 0.16f, 0.55f, 0.35f, 1.3f), out _trailMesh);
            Vol2Fs.MeshObject(root, "HalfScar", Vol2FsRxBlockKit.ScarMat(Kit), out _scarMesh);
            _keep = Vol2FsRxBlockKit.KeepLight(root, Contacted);
            _glow = Vol2Fs.PointLight(root, "CrackGlow", Amber, 5f);
            _rocks = Vol2Fs.Build(new Vol2FsPs { Mat = stone, Mesh = Kit.SharedMesh("rxBlockRock", () => Vol2FsB1.Rock(77, 0.5f)), Life0 = 1.2f, Life1 = 1.8f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.18f, Size1 = 0.42f, Gravity = 2.4f, Drag = 0.2f, Spin = 400f, Max = 80 }, root, "Rocks");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.55f, 0.42f, 0.32f, 1f), 0.55f)), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.4f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.7f, Drag = 1.4f, Max = 200 }, root, "Dust");
            _chips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Amber, 3.5f, 1f)), Life0 = 0.3f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.02f, Size1 = 0.045f, Gravity = 1.8f, Drag = 0.7f, Stretch = true, StretchVel = 0.04f, StretchLen = 1.2f, Max = 300 }, root, "Chips");
            var leaf = M("fs_leaf", new Color(0.6f, 0.7f, 0.5f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.0f, Life1 = 1.8f, Speed0 = 1.5f, Speed1 = 4f, Size0 = 0.1f, Size1 = 0.2f, Gravity = 0.35f, Drag = 1.5f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 200 }, root, "Leaves");
            _splinters = Vol2Fs.Build(new Vol2FsPs { Mat = bark, Mesh = Vol2FsRxBlockKit.StubMesh(Kit), Life0 = 1.0f, Life1 = 1.6f, Speed0 = 2f, Speed1 = 4.5f, Size0 = 0.25f, Size1 = 0.45f, Gravity = 2.2f, Drag = 0.5f, Spin = 600f, Max = 60 }, root, "Splinters");
            foreach (var ps in new[] { _rocks, _dust, _chips, _leaves, _splinters }) ps.Play(false);
            // six catching vines: from points round the patch up to the slab's underside, wrapping over its ends
            for (int k = 0; k < 6; k++)
            {
                float a = (k / 6f) * Mathf.PI * 2f + 0.3f;
                Vector3 root0 = G(P + (Dir * Mathf.Cos(a) + Side * Mathf.Sin(a)) * Radius * 0.75f) + Vector3.down * 0.05f;
                float along = (k % 3 - 1) * 0.9f;
                Vector3 hold = _caught + _slabFwd * along + Vector3.down * 0.05f;
                Vector3 over = hold + Vector3.up * 0.62f + Dir * (k < 3 ? -0.3f : 0.3f);
                var c = new[] { root0, Vector3.Lerp(root0, hold, 0.4f) + (root0 - P).normalized * 0.25f + Vector3.up * 0.1f, hold - Vector3.up * 0.15f, over, over + _slabFwd * 0.15f * (k % 2 == 0 ? 1f : -1f) - Vector3.up * 0.2f };
                var tb = _vines.Add(c, 0.11f, 0.045f, 20); tb.Knots = 0.35f; tb.Sides = 6; tb.Grow = 0f; tb.Sharp = 0.08f;
                _tendrils.Add(tb); _ctrl0.Add((Vector3[])c.Clone());
            }
            Note += " | snag ew_b";
        }

        Vector3 SlabPos(float t)
        {
            if (t < FlyEnd) { float u = Mathf.Clamp01(t / FlyEnd); return Vector3.Lerp(_from, _caught, u) + Vector3.up * 0.4f * u * (1f - u); }
            float a = t - FlyEnd;
            // caught: a hard jolt, a little sag as the vines take the weight
            return _caught + Vector3.down * (0.12f * Vol2Fs.Smooth(0f, 0.2f, a)) + new Vector3(Mathf.Sin(t * 97f), Mathf.Sin(t * 131f), Mathf.Sin(t * 71f)) * (a < 0.12f ? 0.04f * (1f - a / 0.12f) : 0f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            _crack.Clear(); _trail.Clear();
            // the slab: flies in, is caught, hangs, cracks, breaks
            if (!_broken)
            {
                _slab.gameObject.SetActive(true);
                Vector3 p = SlabPos(t);
                float tilt = t < FlyEnd ? 25f * (1f - t / FlyEnd) : -6f * Vol2Fs.Smooth(0f, 0.3f, t - FlyEnd);
                _slab.SetPositionAndRotation(p - Vector3.up * 0.31f, Quaternion.LookRotation(Dir, Vector3.up) * Quaternion.Euler(tilt, 0f, 8f));
                if (t < FlyEnd + 0.4f)
                {
                    // the slab's flight streak (ink with an amber edge), fading after the catch
                    float tl = 1f - Vol2Fs.Smooth(FlyEnd, FlyEnd + 0.4f, t);
                    Vol2FsRxBlockKit.Ribbon(_trail, _from - Dir * 0.8f, Vector3.Lerp(_from, _caught, 0.5f) + Vector3.up * 0.3f, p, Mathf.Clamp01(t / FlyEnd * 0.6f), 1f, 0.2f, 1.1f, tl, 0.3f, cam, 0.08f, t * 10f);
                }
            }
            if (!_caughtDone && t >= FlyEnd)
            {
                _caughtDone = true; HitStop(t, 0.09f);
                for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_dust, _caught + Random.insideUnitSphere * 0.5f, Random.onUnitSphere * Random.Range(0.6f, 2.5f), -1f, -1f);
                for (int k = 0; k < 80; k++) Vol2FsB1.EmitV(_chips, _caught, (Random.onUnitSphere + Vector3.up * 0.3f).normalized * Random.Range(3f, 8f), -1f, -1f);
                if (_glow != null) { _glow.transform.position = _caught + Vector3.up * 0.3f - Dir * 0.6f; _glow.intensity = 7f; }
                for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_leaves, _caught + Random.insideUnitSphere * 0.8f, Random.onUnitSphere * Random.Range(1f, 3f), -1f, -1f);
            }
            // the vines shoot up to catch (0 .. FlyEnd + 0.05), three tear at TearAt and recoil, the others let the broken slab drop and sag back
            for (int k = 0; k < _tendrils.Count; k++)
            {
                var tb = _tendrils[k]; bool tears = k % 2 == 1;
                float grow = Vol2Fs.Smooth(-0.02f, FlyEnd + 0.04f, t);
                if (tears && t >= TearAt) grow *= 1f - Vol2Fs.Smooth(TearAt, TearAt + 0.3f, t);
                else if (!tears && t >= BreakAt) grow *= Mathf.Lerp(1f, 0.35f, Vol2Fs.Smooth(BreakAt, BreakAt + 0.7f, t));
                if (ContactEndAt >= 0f) grow *= 1f - Vol2Fs.Smooth(ContactEndAt, ContactEndAt + 0.5f, t);
                tb.Grow = grow;
                // the hold follows the sagging slab
                var c0 = _ctrl0[k]; Vector3 sag = (t >= FlyEnd && !_broken ? SlabPos(t) - _caught : Vector3.zero);
                for (int i = 2; i < tb.Ctrl.Length; i++) tb.Ctrl[i] = c0[i] + sag * (i / (float)(tb.Ctrl.Length - 1));
            }
            RebuildTubes();
            _vines.Write(_vineMesh);
            if (!_torn && t >= TearAt)
            {
                _torn = true;
                for (int k = 1; k < _tendrils.Count; k += 2)
                {
                    Vector3 q = Vol2FsTubes.CatmullRom(_tendrils[k].Ctrl, 0.6f);
                    for (int j = 0; j < 4; j++) Vol2FsB1.EmitV(_splinters, q + Random.insideUnitSphere * 0.15f, (q - P).normalized * Random.Range(1.5f, 3.5f) + Vector3.up * Random.Range(1f, 2.5f), -1f, -1f);
                    for (int j = 0; j < 14; j++) Vol2FsB1.EmitV(_leaves, q, Random.onUnitSphere * Random.Range(1f, 3.5f), -1f, -1f);
                }
            }
            // cracks race over the hanging slab, then it breaks into falling rocks
            if (!_broken && t >= FlyEnd)
            {
                float cr = Vol2Fs.Smooth(FlyEnd, BreakAt, t);
                Vector3 sp = _slab.position + Vector3.up * 0.64f - Dir * 0.44f;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 a0 = sp + _slabFwd * ((k - 1.5f) * 0.55f), a1 = a0 + Vector3.up * -0.62f * cr + _slabFwd * 0.18f * (k % 2 == 0 ? 1f : -1f);
                    Vol2FsB6.Slash(_crack, a0, a1, -Dir * 0.05f, 0.15f, cr, 1f, 0.3f + 0.1f * k, cam, 6);
                }
            }
            if (!_broken && t >= BreakAt)
            {
                _broken = true; _slab.gameObject.SetActive(false); HitStop(t, 0.06f);
                Vector3 c = _slab.position + Vector3.up * 0.31f;
                for (int k = 0; k < 18; k++) Vol2FsB1.EmitV(_rocks, c + _slabFwd * Random.Range(-1.15f, 1.15f) + Random.insideUnitSphere * 0.15f, Random.insideUnitSphere * 1.5f + Vector3.up * Random.Range(0f, 1.2f), Random.Range(0.2f, 0.45f), -1f);
                for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_dust, c + _slabFwd * Random.Range(-0.9f, 0.9f), Random.insideUnitSphere * 1.5f + Vector3.down * 0.4f, -1f, -1f);
                for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_chips, c, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f);
            }
            _scar.Clear(); if (Patch != null) { Vol2FsRxBlockKit.Survivor(_scar, P, Ground, Radius, Dir, t, ContactEndAt, ContactExpireRel, cam); Vol2FsRxBlockKit.KeepLightTick(_keep, P, t, ContactEndAt); }
            _crack.Write(_crackMesh); _trail.Write(_trailMesh); _scar.Write(_scarMesh);
            if (_glow != null) _glow.intensity = _broken ? Mathf.MoveTowards(_glow.intensity, 0f, Time.deltaTime * 20f) : t >= FlyEnd ? 4f + 4f * Vol2Fs.Smooth(FlyEnd, BreakAt, t) : 0f;
            // the enemy's tangle: half of it goes (it settles at ~0.6 height, ~0.85 spread) after the tear; at its end it withers and sinks
            if (Patch != null)
            {
                float halve = Vol2Fs.Smooth(TearAt, TearAt + 0.5f, t);
                float end = ContactEndAt >= 0f ? Vol2Fs.Smooth(ContactEndAt, ContactEndAt + 0.8f, t) : 0f;
                Patch.Pose(Mathf.Lerp(1f, 0.58f, halve) * (1f - end), end * 0.3f, Mathf.Lerp(1f, 0.82f, halve));
            }
        }

        void RebuildTubes() { foreach (var tb in _tendrils) Vol2FsRxBlockKit.Refresh(tb); }

        protected override void OnContactEnd(Vol2MaterialEnd why, float t)
        {
            if (Patch == null) return;
            if (why == Vol2MaterialEnd.Expired) for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_leaves, G(P + Random.insideUnitSphere * Radius) + Vector3.up * 0.3f, Vector3.up * Random.Range(0.2f, 1f) + Random.insideUnitSphere * 0.5f, -1f, -1f);
            else Patch.SetActive(false);
        }
    }
}
