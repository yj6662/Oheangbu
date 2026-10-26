using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>머 (earth, ㅓ guard, 한 번) - free showcase batch 2, concept meo_a (as the paw comes down a heavy upright slab of rough stone tears up out of the red
    /// ground in front of the guard, the stone crust also running up the brush shaft and the sleeve like a bracer; the paw slams into the slab: a heavy shock ring
    /// of dust, the stone cracks and spits chunks, and (the CSV's end) the slab sinks while the ground round it lifts in tilted plates, nothing falling). Without a
    /// parry the slab sinks back when the window closes.</summary>
    public sealed class Vol2FsMeo : Vol2FsAttach
    {
        static readonly Color Soil = new Color(0.62f, 0.42f, 0.33f), Ink = new Color(0.04f, 0.03f, 0.025f);
        ParticleSystem _dust, _chunks, _clods, _drops, _bigDust; Light _light;
        Transform _slab; readonly List<Transform> _crust = new List<Transform>();
        sealed class Plate { public Transform T; public Vector3 Base, Out; public float Tilt, At; }
        readonly List<Plate> _plates = new List<Plate>();
        Mesh _burstMesh, _ringMesh, _crackMesh; readonly Vol2FsStrips _burst = new Vol2FsStrips(), _ring = new Vol2FsStrips(), _crack = new Vol2FsStrips();
        List<Vol2FsB1.Stroke> _strokes;
        Vector3 _slabBase; Quaternion _slabRot; bool _rose, _slabSet;
        const float SlabH = 1.4f;      // polish: chest-high (r4: a 2 m wall hid the whole bear and read as a box)
        bool _shattered;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);

        protected override void OnBegin()
        {
            var root = Root.transform;
            var stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.78f, 0.6f, 0.5f), 0f, 0.12f);
            _slab = Vol2FsB1.Prop(root, "Slab", Kit.SharedMesh("meo_slab", () => Vol2FsB1.Slab(43, new Vector3(1.35f, SlabH, 0.5f), 0.55f)), stone).transform;
            _slab.gameObject.SetActive(false);
            for (int k = 0; k < 8; k++)
            {
                int s = k % 4;
                _crust.Add(Vol2FsB1.Prop(root, "Crust", Kit.SharedMesh("meo_rock" + s, () => Vol2FsB1.Rock(60 + s, 1f, 0.8f, 10, 7)), stone, false).transform);
            }
            for (int k = 0; k < 10; k++)
            {
                var p = new Plate { T = Vol2FsB1.Prop(root, "Plate", Kit.SharedMesh("meo_plate" + (k % 3), () => Vol2FsB1.Slab(70 + k % 3, new Vector3(0.95f, 0.28f, 0.75f), 0.9f)), stone).transform };
                p.T.gameObject.SetActive(false);
                _plates.Add(p);
            }
            var dust = M("fs_puff", Soil, 1f, 0f); dust.Opacity = 0.7f; dust.Erode = 0.35f; dust.NoiseAmt = 0.55f;
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.5f, Speed1 = 2f, Size0 = 0.5f, Size1 = 1.1f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Drag = 1.6f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizePeakAt = 0.4f, SizeEnd = 1.7f, Max = 300, SortFudge = 8 }, root, "Dust");
            _bigDust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 1.0f, Life1 = 1.9f, Speed0 = 2f, Speed1 = 6f, Size0 = 1.0f, Size1 = 2.0f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.3f, Drag = 2.4f, Tiles = 2,
                SizeStart = 0.35f, SizePeak = 1.2f, SizePeakAt = 0.35f, SizeEnd = 1.8f, Max = 120, SortFudge = 10 }, root, "BigDust");
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = stone, Mesh = Kit.SharedMesh("meo_chunk", () => Vol2FsB1.Rock(81, 1f, 0.75f, 8, 6)), Life0 = 0.9f, Life1 = 1.6f, Speed0 = 4f, Speed1 = 9f, Size0 = 0.1f, Size1 = 0.4f,
                Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f, Gravity = 2.2f, Drag = 0.2f, Spin = 400f, Max = 320 }, root, "Chunks");
            var clod = M("fs_ink", new Color(0.3f, 0.2f, 0.15f, 1f), 1f, 0f); clod.Soft = 0.1f; clod.NoiseAmt = 0.3f;
            _clods = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(clod), Life0 = 0.7f, Life1 = 1.4f, Speed0 = 2f, Speed1 = 7f, Size0 = 0.05f, Size1 = 0.16f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.4f, Gravity = 2.4f, Tiles = 2, Max = 400 }, root, "Clods");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(0.9f, 0.55f, 0.35f, 1f); drop.EdgeWidth = 0.12f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.26f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f, Gravity = 2.2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 250 }, root, "InkDrops");
            foreach (var ps in new[] { _dust, _bigDust, _chunks, _clods, _drops }) ps.Play(false);
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", Ink, new Color(1.2f, 0.75f, 0.5f, 1f), 0.06f, 0.5f, 0.26f, 1.5f), out _burstMesh);
            var ring = M("fs_ring", new Color(0.55f, 0.4f, 0.32f, 1f), 1f, 0f); ring.Soft = 0.001f; ring.Opacity = 0.9f; ring.NoiseAmt = 0.6f;
            Vol2Fs.MeshObject(root, "ShockRing", Kit.Mat(ring), out _ringMesh);
            var crack = M("fs_crack", new Color(0.08f, 0.05f, 0.04f, 1f), 1f, 0f); crack.Soft = 0.001f; crack.NoiseAmt = 0.4f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(crack), out _crackMesh);
            _light = Vol2Fs.PointLight(root, "Flash", new Color(1f, 0.8f, 0.6f), 5f);
        }

        void PlaceSlab(float rise, float sink, float tilt)
        {
            if (!_slabSet)
            {
                _slabSet = true;
                _slabBase = Inst.Origin + Away * 0.75f; _slabBase.y = Ground;
                _slabRot = Quaternion.LookRotation(Away, Vector3.up);
            }
            float h = SlabH * (Mathf.Clamp01(rise) - sink * 1.05f);
            _slab.gameObject.SetActive(h > -SlabH + 0.05f && !_shattered);
            _slab.SetPositionAndRotation(_slabBase + Vector3.up * (h - SlabH), _slabRot * Quaternion.Euler(-tilt, 0f, 0f));
        }

        protected override void OnGuard(float t, float open)
        {
            float rise = Vol2Fs.EaseOutBack(Mathf.Clamp01((t - WinStart) / 0.12f));
            float sink = IsParried ? Vol2Fs.Smooth(0.55f, 1.25f, t - ParryAt) : Vol2Fs.Smooth(WinEnd, WinEnd + 0.45f, t);
            float tilt = IsParried ? 9f * Vol2Fs.Smooth(0f, 0.12f, t - ParryAt) : 0f;
            PlaceSlab(rise, sink, tilt);
            if (!_rose && t >= WinStart) { _rose = true; for (int k = 0; k < 10; k++) { Vector3 o = Vector3.Cross(Vector3.up, Away) * Mathf.Lerp(-0.8f, 0.8f, k / 9f); Vol2FsB1.Emit(_dust, _slabBase + o, 1); Vol2FsB1.Emit(_clods, _slabBase + o, 4); } }
            // the stone bracer on the sleeve and up the shaft
            float o2 = Mathf.Clamp01(Vol2Fs.EaseOutBack(open));
            Vector3 ax = ShaftAxis, n = Vector3.Cross(ax, Vector3.up).normalized; if (n.sqrMagnitude < 1e-4f) n = Right;
            for (int k = 0; k < _crust.Count; k++)
            {
                float u = Mathf.Lerp(-0.18f, 0.45f, k / (_crust.Count - 1f));
                float a = k * 2.3f;
                Vector3 off = Quaternion.AngleAxis(a * Mathf.Rad2Deg, ax) * n * 0.05f;
                _crust[k].SetPositionAndRotation(Shaft(u) + off, Quaternion.LookRotation(ax) * Quaternion.Euler(k * 37f, k * 53f, 0f));
                _crust[k].localScale = Vector3.one * Mathf.Lerp(0.09f, 0.15f, Vol2Fs.Hash(Seed, k)) * o2 * (u < 0f ? 1.3f : 1f);
            }
            _light.intensity = IsParried ? 9f * Mathf.Exp(-(t - ParryAt) * 7f) : 0f;
        }

        protected override void OnParry()
        {
            _strokes = WideX();     // polish (meo_a): a wide heavy X of ink thrown out to both sides of the wall (r4: low ground strokes were hidden behind the caster)
            _light.transform.position = ParryPoint - Away * 0.3f;
            Vol2FsB1.Emit(_bigDust, ParryPoint, 14); Vol2FsB1.Emit(_dust, ParryPoint, 20); Vol2FsB1.Emit(_drops, ParryPoint, 40);
            for (int k = 0; k < 44; k++) Vol2FsB1.EmitV(_chunks, ParryPoint + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere * 0.9f + Vector3.up * 0.5f - Away * 0.2f).normalized * Random.Range(3f, 9f));
            Vol2FsB1.Emit(_bigDust, _slabBase + Vector3.up * 1.2f, 10);
            for (int k = 0; k < 14; k++) { Vector3 o = Vector3.Cross(Vector3.up, Away) * Mathf.Lerp(-1f, 1f, k / 13f); Vol2FsB1.Emit(_dust, _slabBase + o, 1); Vol2FsB1.Emit(_clods, _slabBase + o, 6); }
            // the plates round the slab (they lift as it sinks)
            Vector3 side = Vector3.Cross(Vector3.up, Away);
            for (int k = 0; k < _plates.Count; k++)
            {
                float a = Mathf.Lerp(-1.25f, 1.25f, k / (_plates.Count - 1f)) + (Vol2Fs.Hash(Seed, k + 50) - 0.5f) * 0.2f;
                Vector3 d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * Away;
                float r = Mathf.Lerp(1.0f, 1.7f, Vol2Fs.Hash(Seed, k + 60));
                var p = _plates[k]; p.Base = _slabBase + d * r * (k % 2 == 0 ? 1f : 0.75f) - Away * 0.2f; p.Base.y = Ground; p.Out = d; p.Tilt = Mathf.Lerp(22f, 42f, Vol2Fs.Hash(Seed, k + 70)); p.At = 0.5f + 0.06f * Vol2Fs.Hash(Seed, k + 80);
                p.T.gameObject.SetActive(true);
            }
        }

        protected override void OnReaction(float t, float h)
        {
            var cam = CamPos;
            _burst.Clear(); Vol2FsB1.DrawBurst(_burst, _strokes, ParryPoint - Away * 0.1f + Vector3.up * 0.2f, h, cam, 0.35f, 0.12f); _burst.Write(_burstMesh);
            // polish: the wall bursts 0.1 s after the blow - big pieces flung out to the sides and up, so the bear stands clear behind it
            if (!_shattered && h > 0.1f)
            {
                _shattered = true; _slab.gameObject.SetActive(false);
                Vector3 sd = Vector3.Cross(Vector3.up, Away);
                for (int k = 0; k < 34; k++)
                {
                    Vector3 at = _slabBase + sd * Random.Range(-0.65f, 0.65f) + Vector3.up * Random.Range(0.1f, SlabH);
                    Vector3 v = (sd * Random.Range(-1.6f, 1.6f) + Vector3.up * Random.Range(0.6f, 1.6f) + Away * Random.Range(-0.2f, 0.7f)).normalized * Random.Range(3f, 8f);
                    Vol2FsB1.EmitV(_chunks, at, v, Random.Range(0.25f, 0.6f), Random.Range(1.2f, 1.8f));
                }
                Vol2FsB1.Emit(_bigDust, _slabBase + Vector3.up * 0.7f, 10); Vol2FsB1.Emit(_clods, _slabBase + Vector3.up * 0.6f, 60);
            }
            // the dust shock ring on the ground round the slab
            _ring.Clear();
            float rr = Mathf.Lerp(0.4f, 3.4f, 1f - Mathf.Pow(1f - Mathf.Clamp01(h / 0.45f), 2.5f)), ra = 1f - Vol2Fs.Smooth(0.25f, 0.9f, h);
            if (ra > 0.01f) Vol2FsB2.GroundRing(_ring, _slabBase, Away, Ground, Mathf.Max(0f, rr - 0.7f), rr, 40, null, 0.4f, cam, 0.04f);
            _ring.Write(_ringMesh);
            if (h < 0.45f && Time.frameCount % 2 == 0) for (int k = 0; k < 4; k++) { float a = Random.Range(-Mathf.PI, Mathf.PI); var p = _slabBase + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rr; Vol2FsB1.Emit(_dust, p, 1); }
            // cracks spreading on the ground
            _crack.Clear();
            float cg = Vol2Fs.Smooth(0f, 0.25f, h) * (1f - Vol2Fs.Smooth(1.0f, 1.6f, h));
            if (cg > 0.01f) _crack.Quad(_slabBase + Vector3.up * 0.03f, Vector3.Cross(Vector3.up, Away), Away, 4.2f * Vol2Fs.Smooth(0f, 0.3f, h), new Color(1f, 1f, 1f, cg), 0.61f);
            _crack.Write(_crackMesh);
            // the plates lift as the slab sinks (nothing falls), then settle back
            foreach (var p in _plates)
            {
                float a = h - p.At;
                float up = Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.16f)) * (1f - Vol2Fs.Smooth(0.5f, 0.85f, a));
                p.T.gameObject.SetActive(a > -0.05f);
                Vector3 axis = Vector3.Cross(Vector3.up, p.Out);
                p.T.SetPositionAndRotation(p.Base + Vector3.up * (0.12f * up - 0.18f), Quaternion.AngleAxis(-p.Tilt * up, axis) * Quaternion.LookRotation(p.Out));
                if (a > 0f && a < 0.03f) { Vol2FsB1.Emit(_dust, p.Base, 2); Vol2FsB1.Emit(_clods, p.Base, 5); }
            }
        }
    
        /// <summary>polish (meo_a): heavy ink thrown out to both sides of the wall in a wide X (up-left / up-right long, down-left / down-right shorter).</summary>
        List<Vol2FsB1.Stroke> WideX()
        {
            var list = new List<Vol2FsB1.Stroke>();
            Vector3 sd = Vector3.Cross(Vector3.up, Away).normalized;
            for (int k = 0; k < 12; k++)
            {
                float s = k % 2 == 0 ? 1f : -1f; bool high = k % 4 < 2;
                float up = high ? Mathf.Lerp(0.35f, 0.85f, Vol2Fs.Hash(Seed, k + 700)) : -Mathf.Lerp(0.05f, 0.3f, Vol2Fs.Hash(Seed, k + 700));
                Vector3 d = (sd * s + Vector3.up * up + Away * Mathf.Lerp(-0.1f, 0.35f, Vol2Fs.Hash(Seed, k + 710))).normalized;
                list.Add(new Vol2FsB1.Stroke { Dir = d, Len = (high ? 1f : 0.65f) * Mathf.Lerp(2.6f, 4.4f, Vol2Fs.Hash(Seed, k + 720)), Width = Mathf.Lerp(0.45f, 0.85f, Vol2Fs.Hash(Seed, k + 730)), Delay = 0.02f * Vol2Fs.Hash(Seed, k + 740), Life = 1.0f + 0.4f * Vol2Fs.Hash(Seed, k + 750), Rnd = Vol2Fs.Hash(Seed, k + 760) });
            }
            return list;
        }
    }
}