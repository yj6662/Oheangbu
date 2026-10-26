using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>거 (wood, ㅓ guard, 한 번) - free showcase batch 2, concept geo_b (a bamboo cross guard: the brush shaft and the sleeve are wound with spiralling vine,
    /// two bamboo stalks shoot out crosswise from the shaft like a staff block; the bear's paw strikes the crossing and is caught - splinters, torn leaves and
    /// black ink spray outward, root tendrils snap round the paw). On the parry the vines bind the paw, then (the CSV's end) unravel into rootlets reaching forward
    /// along the ground. Without a parry the cross withdraws into the shaft when the window closes.</summary>
    public sealed class Vol2FsGeo : Vol2FsAttach
    {
        static readonly Color Jade = new Color(0.45f, 1.0f, 0.35f), Ink = new Color(0.03f, 0.04f, 0.02f);
        ParticleSystem _leaves, _splinter, _drops, _sparks, _dust; Light _light;
        Mesh _wrapMesh, _crossMesh, _bindMesh, _rootMesh, _burstMesh, _leafMesh;
        readonly Vol2FsTubes _wrap = new Vol2FsTubes(), _cross = new Vol2FsTubes(), _bind = new Vol2FsTubes(), _roots = new Vol2FsTubes();
        readonly Vol2FsStrips _burst = new Vol2FsStrips();
        readonly Vol2FsLeaves _crossLeaves = new Vol2FsLeaves();
        List<Vol2FsB1.Stroke> _strokes;
        Transform _paw; Vector3 _pawLocal, _crossAt, _crossN; bool _shed, _unravelled;
        Vector3[] _rootEnds;

        Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsB1.M(tex, tint, intensity, addMix);

        protected override void OnBegin()
        {
            var root = Root.transform;
            var vine = Kit.Bark(new Color(0.42f, 0.5f, 0.28f), Jade * 1.1f, 0.2f, "fs_vine", "fs_vein");
            var bamboo = Kit.Bark(new Color(0.85f, 0.95f, 0.55f), new Color(0.5f, 1.1f, 0.25f) * 0.55f, 0.35f, "fs_bamboo", "fs_vein");
            Vol2Fs.MeshObject(root, "ShaftVines", vine, out _wrapMesh);
            Vol2Fs.MeshObject(root, "BambooCross", bamboo, out _crossMesh);
            Vol2Fs.MeshObject(root, "PawBind", vine, out _bindMesh);
            Vol2Fs.MeshObject(root, "Rootlets", Kit.Bark(new Color(0.3f, 0.26f, 0.2f), Jade * 0.5f, 0.15f), out _rootMesh);
            Vol2Fs.MeshObject(root, "CrossLeaves", Kit.Leaf(new Color(0.42f, 0.72f, 0.28f), new Color(0.08f, 0.3f, 0.04f)), out _leafMesh);
            Vol2Fs.MeshObject(root, "InkBurst", Kit.InkSolid("fs_stroke2", Ink, new Color(0.9f, 2.4f, 0.5f, 1f), 0.08f, 0.45f, 0.26f, 1.5f), out _burstMesh);
            var leaf = M("fs_leaf", new Color(0.55f, 0.85f, 0.35f, 1f), 1f, 0f); leaf.Soft = 0.05f; leaf.NoiseAmt = 0.2f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.8f, Speed0 = 2f, Speed1 = 7f, Size0 = 0.08f, Size1 = 0.2f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.25f, Gravity = 0.5f, Drag = 1.2f,
                Noise = 1.2f, NoiseFreq = 1.4f, Tiles = 2, Spin = 400f, AlphaHoldTo = 0.6f, Max = 500 }, root, "Leaves");
            var spl = M("fs_streak", new Color(0.6f, 0.55f, 0.32f, 1f), 1.2f, 0.1f);
            _splinter = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spl), Life0 = 0.35f, Life1 = 0.8f, Speed0 = 4f, Speed1 = 11f, Size0 = 0.04f, Size1 = 0.09f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.1f, Gravity = 1.2f, Drag = 0.6f,
                Stretch = true, StretchVel = 0.06f, StretchLen = 2f, Max = 300 }, root, "Splinters");
            var drop = M("fs_ink", Ink, 1f, 0f); drop.Edge = new Color(0.6f, 1.6f, 0.3f, 1f); drop.EdgeWidth = 0.15f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.08f, Size1 = 0.26f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.15f, Gravity = 2f, Drag = 0.4f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "InkDrops");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.7f, 1f, 0.45f), 5f, 1f)), Life0 = 0.4f, Life1 = 1.0f, Speed0 = 1f, Speed1 = 5f, Size0 = 0.025f, Size1 = 0.06f, Shape = ParticleSystemShapeType.Sphere, Radius = 0.2f,
                Gravity = -0.2f, Noise = 1.4f, NoiseFreq = 2f, Stretch = true, StretchVel = 0.04f, Max = 400 }, root, "JadeSparks");
            var dust = M("fs_puff", new Color(0.42f, 0.3f, 0.24f, 1f), 1f, 0f); dust.Opacity = 0.55f; dust.Erode = 0.35f;
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(dust), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 0.4f, Speed1 = 1.4f, Size0 = 0.5f, Size1 = 1.1f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.3f, Drag = 1.2f, Tiles = 2,
                SizeStart = 0.5f, SizePeak = 1.2f, SizeEnd = 1.6f, Max = 80, SortFudge = 10 }, root, "Dust");
            foreach (var ps in new[] { _leaves, _splinter, _drops, _sparks, _dust }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "JadeLight", Jade, 4.5f);
            // the cross: two stalks through one point on the shaft, in the plane facing the strike (built now, revealed by the guard)
            for (int k = 0; k < 18; k++)
                _crossLeaves.Items.Add(new Vol2FsLeaves.Leaf { Size = Mathf.Lerp(0.16f, 0.3f, Vol2Fs.Hash(Seed, k + 40)), Cell = k % 4 });
        }

        void BuildWrap(float open)
        {
            _wrap.Tubes.Clear();
            Vector3 g = Grip(), tip = Tip(), ax = ShaftAxis;
            Vector3 n = Vector3.Cross(ax, Vector3.up); if (n.sqrMagnitude < 1e-4f) n = Right; n.Normalize();
            Vector3 b = Vector3.Cross(ax, n);
            // three vines spiral up the shaft from below the grip (the sleeve) to near the tip
            for (int v = 0; v < 3; v++)
            {
                var pts = new Vector3[10];
                for (int j = 0; j < 10; j++)
                {
                    float u = Mathf.Lerp(-0.22f, 0.86f, j / 9f);
                    float a = v * 2.09f + u * 13f;
                    float rad = u < 0f ? 0.08f + 0.05f * (-u / 0.22f) : 0.045f;     // fatter round the sleeve
                    pts[j] = Vector3.LerpUnclamped(g, tip, u) + (n * Mathf.Cos(a) + b * Mathf.Sin(a)) * rad;
                }
                var t = _wrap.Add(pts, 0.026f, 0.012f, 26); t.Knots = 0.3f; t.Grow = open; t.Sides = 6;
            }
            _wrap.Write(_wrapMesh);
        }

        void BuildCross(float grow, float crack)
        {
            _cross.Tubes.Clear();
            _crossAt = Shaft(0.62f);
            Vector3 face = Away - Vector3.Dot(Away, ShaftAxis) * ShaftAxis; if (face.sqrMagnitude < 1e-4f) face = Fwd; face.Normalize();
            _crossN = face;
            Vector3 s1 = Vector3.Cross(face, Vector3.up).normalized;                     // across
            Vector3 s2 = (Quaternion.AngleAxis(62f, face) * s1).normalized;               // the second stalk, crossing
            float len = 1.75f * grow;
            foreach (var s in new[] { s1, s2 })
            {
                if (len < 0.02f) continue;
                Vector3 a = _crossAt - s * len, c = _crossAt + s * len;
                Vector3 bow = face * 0.12f * (1f - crack) + Vector3.down * 0.25f * crack;
                var t = _cross.Add(new[] { a, Vector3.Lerp(a, _crossAt, 0.5f) + bow, _crossAt + face * 0.05f, Vector3.Lerp(_crossAt, c, 0.5f) + bow, c }, 0.095f, 0.075f, 22);
                t.Grow = 1f; t.NodeEvery = 0.34f; t.Sharp = 0.15f; t.Sides = 8;
            }
            _cross.Write(_crossMesh);
            for (int k = 0; k < _crossLeaves.Items.Count; k++)
            {
                var l = _crossLeaves.Items[k];
                Vector3 s = k % 2 == 0 ? s1 : s2; float side = (k / 2) % 2 == 0 ? 1f : -1f; float along = Mathf.Lerp(0.35f, 1.15f, Vol2Fs.Hash(Seed, k + 60)) * side;
                l.Pos = _crossAt + s * along * len + Vector3.up * 0.02f;
                l.Normal = (face + Vector3.up * 0.4f).normalized; l.Up = (s * side + Vector3.up * 0.6f + face * 0.3f * (Vol2Fs.Hash(Seed, k + 80) - 0.5f)).normalized;
                l.Grow = grow * (1f - crack);
                _crossLeaves.Items[k] = l;
            }
            _crossLeaves.Write(_leafMesh);
        }

        protected override void OnGuard(float t, float open)
        {
            float o = Mathf.Clamp01(Vol2Fs.EaseOutBack(open));
            float crack = IsParried ? Vol2Fs.Smooth(0.25f, 0.55f, t - ParryAt) : 0f;
            BuildWrap(open * (1f - crack * 0.6f));
            BuildCross(o, crack);
            _light.transform.position = _crossAt + _crossN * 0.2f;
            _light.intensity = 3.2f * o + (IsParried ? 9f * Mathf.Exp(-(t - ParryAt) * 5f) : 0f);
            if (open > 0.1f && open < 0.98f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_leaves, _crossAt, 2);
            if (crack > 0.05f && !_shed)
            {
                _shed = true;                      // the cross splinters and falls away after it caught the blow
                for (int k = 0; k < 10; k++) Vol2FsB1.Emit(_splinter, _crossAt + _crossN * 0.1f + Random.insideUnitSphere * 0.6f, 4);
                Vol2FsB1.Emit(_leaves, _crossAt, 30);
            }
        }

        protected override void OnParry()
        {
            _strokes = Vol2FsB1.Burst(Seed, 7, Vector3.Cross(Vector3.up, Away).normalized, Away, 0.9f, 1.9f, 0.14f, 0.3f, 0.9f, 0.5f);     // r2: fewer, shorter (r1 read as 나's star burst)
            _paw = Vol2FsB2.NearestBone(Attacker, ParryPoint);
            if (_paw != null) _pawLocal = _paw.InverseTransformPoint(ParryPoint + Away * 0.1f);
            Vol2FsB1.Emit(_leaves, ParryPoint, 160); Vol2FsB1.Emit(_drops, ParryPoint, 80); Vol2FsB1.Emit(_sparks, ParryPoint, 140);
            for (int k = 0; k < 40; k++)
            {
                Vector3 d = (Away * 0.6f + Random.onUnitSphere).normalized;
                Vol2FsB1.EmitV(_splinter, ParryPoint, d * Random.Range(5f, 11f));
            }
            Vector3 feet = HasAttacker ? new Vector3(AttackerB.center.x, Ground, AttackerB.center.z) : ParryPoint + Away * 0.8f;
            Vol2FsB1.Emit(_dust, feet, 6);
            // rootlets for the end: from the bear's feet forward along the ground
            _rootEnds = new Vector3[9];
            for (int k = 0; k < _rootEnds.Length; k++)
            {
                float a = (k / (_rootEnds.Length - 1f) - 0.5f) * 1.6f;
                Vector3 d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * Away;
                _rootEnds[k] = feet + d * Mathf.Lerp(1.4f, 2.6f, Vol2Fs.Hash(Seed, k + 90));
            }
        }

        protected override void OnReaction(float t, float h)
        {
            var cam = CamPos;
            // the ink burst at the contact
            _burst.Clear();
            Vol2FsB1.DrawBurst(_burst, _strokes, ParryPoint, h, cam, 0.9f);
            _burst.Write(_burstMesh);
            // the vines snapped round the paw: a helix round the gripped bone, holding, then (the end) loosening into rootlets reaching forward
            Vector3 c = _paw != null ? _paw.TransformPoint(_pawLocal) : ParryPoint + Away * 0.15f;
            float grip = Vol2Fs.EaseOutBack(Mathf.Clamp01(h / 0.16f));
            float loose = Vol2Fs.Smooth(0.75f, 1.05f, h);
            _bind.Tubes.Clear();
            Vector3 ax = (Away + Vector3.up * 0.3f).normalized, n = Vector3.Cross(ax, Vector3.up).normalized, b = Vector3.Cross(ax, n);
            for (int v = 0; v < 5; v++)
            {
                var pts = new Vector3[9];
                for (int j = 0; j < 9; j++)
                {
                    float u = j / 8f; float a = v * 1.26f + u * 9f;
                    float rad = Mathf.Lerp(0.22f, 0.16f, u) * (1f + 0.8f * loose);
                    pts[j] = c + ax * Mathf.Lerp(-0.35f, 0.3f, u) + (n * Mathf.Cos(a) + b * Mathf.Sin(a)) * rad + Vector3.down * loose * u * 0.6f;
                }
                pts[0] = Vector3.Lerp(Shaft(0.62f), pts[1], 0.4f);       // each vine comes from the crossing on the shaft
                var tb = _bind.Add(pts, 0.035f, 0.018f, 20); tb.Knots = 0.35f; tb.Grow = grip * (1f - Vol2Fs.Smooth(0.95f, 1.25f, h)); tb.Sides = 6;
            }
            _bind.Write(_bindMesh);
            if (loose > 0.01f && !_unravelled) { _unravelled = true; Vol2FsB1.Emit(_leaves, c, 25); }
            // rootlets running forward along the ground from the bear's feet (the release)
            _roots.Tubes.Clear();
            if (_rootEnds != null)
            {
                Vector3 feet = HasAttacker && Attacker != null ? Attacker.position : (_rootEnds[0] + _rootEnds[_rootEnds.Length - 1]) * 0.5f - Away * 1.6f;
                float run = Vol2Fs.Smooth(0.8f, 1.25f, h), sink = Vol2Fs.Smooth(1.25f, 1.6f, h);
                for (int k = 0; k < _rootEnds.Length; k++)
                {
                    Vector3 e = _rootEnds[k]; e.y = Ground + 0.02f;
                    Vector3 m = Vector3.Lerp(feet, e, 0.5f) + Vector3.up * 0.12f + Vector3.Cross(Vector3.up, Away) * (Vol2Fs.Hash(Seed, k + 120) - 0.5f) * 0.5f;
                    var tb = _roots.Add(new[] { feet + Vector3.up * 0.05f, m, e }, 0.03f, 0.008f, 12); tb.Knots = 0.5f; tb.Grow = run * (1f - sink); tb.Sides = 5;
                }
            }
            _roots.Write(_rootMesh);
            if (h < 0.5f && Time.frameCount % 3 == 0) Vol2FsB1.Emit(_sparks, c, 3);
        }
    }
}
