using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>수>목 먹임 - free showcase batch 8, concept aw_a (drinking vines): the water shot splashes into the enemy's thorny tangle and is DRUNK UP from below - glowing
    /// blue-green streaks run up the stems - while the dark old vines wither under it and thick new jade vines of the caster's own swell up out of the same ground,
    /// curling high (2 m) with leaves unfolding, black ink sweeps lashing round them. The child (wood, 10 s) stands, the water still climbing it; Expired = the vines
    /// loosen and sink back into rootlets reaching over the ground, leaves falling; Consumed / ChildConsumed = they dry in place 0.3 s.</summary>
    public sealed class Vol2FsRxWaterWood : Vol2FsReact
    {
        ParticleSystem _splash, _climb, _leafFall, _jade, _drip; Light _light;
        Mesh _vineMesh, _leafMesh, _rootMesh, _inkMesh, _streakMesh, _sealMesh, _sealGlowMesh;
        readonly Vol2FsTubes _vines = new Vol2FsTubes(), _roots = new Vol2FsTubes();
        readonly Vol2FsLeaves _leaves = new Vol2FsLeaves();
        readonly List<float> _leafAt = new List<float>(); readonly List<int> _leafTube = new List<int>();
        readonly Vol2FsStrips _ink = new Vol2FsStrips(), _streak = new Vol2FsStrips(), _seal = new Vol2FsStrips(), _sealGlow = new Vol2FsStrips();
        bool _mine, _splashed, _fell; float _lifeS = 10f;
        protected override float Burst => 2.2f;

        protected override void OnBuild()
        {
            _mine = !EnemyIgniter; _lifeS = V.ChildLifeUs > 0 ? V.ChildLifeUs / 1e6f : 10f;
            var root = Root.transform;
            var bark = _mine ? Kit.Bark(new Color(0.7f, 1f, 0.5f), new Color(0.3f, 1.3f, 0.4f) * 6f, 0.35f, "fs_vine", "fs_vein") : Kit.Bark(new Color(0.42f, 0.36f, 0.42f), Color.black, 0.2f, "fs_vine", "fs_vein");
            Vol2Fs.MeshObject(root, "Vines", bark, out _vineMesh);
            Vol2Fs.MeshObject(root, "Rootlets", bark, out _rootMesh);
            Vol2Fs.MeshObject(root, "Leaves", _mine ? Kit.Leaf(new Color(0.75f, 1f, 0.2f), new Color(0.5f, 1.1f, 0.2f) * 0.9f) : Kit.Leaf(new Color(0.45f, 0.5f, 0.4f), Color.black), out _leafMesh);
            Vol2Fs.MeshObject(root, "Ink", Vol2FsRxFeedKit.InkMat(Kit), out _inkMesh);
            Vol2Fs.MeshObject(root, "Streaks", Vol2FsRxFeedKit.GlowMat(Kit, new Color(0.35f, 0.95f, 0.85f), _mine ? 2.6f : 1f), out _streakMesh);
            Vol2Fs.MeshObject(root, "Seal", Vol2FsRxFeedKit.InkMat(Kit), out _sealMesh);
            Vol2Fs.MeshObject(root, "SealGlow", Vol2FsRxFeedKit.GlowMat(Kit, _mine ? new Color(0.35f, 0.95f, 0.55f) : new Color(0.3f, 0.35f, 0.3f), 2.2f), out _sealGlowMesh);
            if (!HasChild) return;
            // seven thick vines rising out of the patch and curling outward, tallest at the back
            // r2: nine vines, x1.6 (r1 the tangle stood ~1 m against the concept's 3 m)
            for (int j = 0; j < 9; j++)
            {
                float a = j * 0.7f + Vol2Fs.Hash(Seed, j) * 0.5f, r0 = Radius * (0.2f + 0.9f * Vol2Fs.Hash(Seed, j + 10));
                Vector3 o = Dir * Mathf.Cos(a) + Side * Mathf.Sin(a), tang = Vector3.Cross(Vector3.up, o);
                float h = 2.3f + 1.4f * Vol2Fs.Hash(Seed, j + 20), curl = (Vol2Fs.Hash(Seed, j + 30) - 0.5f) * 3f;
                Vector3 b = G(P + o * r0) + Vector3.down * 0.05f;
                var ctrl = new[] { b, b + Vector3.up * h * 0.3f + o * 0.25f, b + Vector3.up * h * 0.6f + o * 0.75f + tang * curl * 0.45f, b + Vector3.up * h * 0.88f + o * 1.25f + tang * curl * 0.75f, b + Vector3.up * h * 0.78f + o * 1.7f + tang * curl * 1.1f };
                var tb = _vines.Add(ctrl, 0.17f + 0.07f * Vol2Fs.Hash(Seed, j + 40), 0.05f, 30); tb.Knots = 0.35f; tb.Sides = 7; tb.Grow = 0f; tb.Sharp = 0.15f;
                for (int k = 0; k < 5; k++)
                {
                    float u = 0.3f + 0.15f * k;
                    Vector3 q = Vol2FsTubes.CatmullRom(ctrl, u), n = (o + tang * (k % 2 == 0 ? 1f : -1f) * 0.8f + Vector3.up * 0.3f);
                    _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = q, Normal = n, Up = (Vector3.up + n).normalized, Size = 0.45f + 0.15f * Vol2Fs.Hash(Seed, j * 5 + k), Grow = 0f, Cell = (j + k) % 4 });
                    _leafAt.Add(u); _leafTube.Add(j);
                }
            }
            // the rootlets of the end: thin roots reaching forward over the ground (grown only at the Expired end)
            for (int j = 0; j < 8; j++)
            {
                float a = j * 0.785f + Vol2Fs.Hash(Seed, j + 60) * 0.4f;
                Vector3 o = Dir * Mathf.Cos(a) + Side * Mathf.Sin(a);
                Vector3 b = G(P + o * Radius * 0.4f) + Vector3.up * 0.02f;
                var tb = _roots.Add(new[] { b, b + o * Radius * 1.1f + Vector3.up * 0.05f, b + o * Radius * 2.0f + Vector3.Cross(Vector3.up, o) * 0.3f, b + o * Radius * 2.9f + Vector3.up * 0.01f }, 0.06f, 0.012f, 16);
                tb.Sides = 5; tb.Grow = 0f; tb.Sharp = 0.2f;
            }
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.4f, "fs_splash"), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 5.5f, Size0 = 0.14f, Size1 = 0.34f, Gravity = 1.6f, Drag = 0.6f, Max = 400 }, root, "Splash");
            _climb = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.4f, 1f, 0.9f), 3.5f, 1f)), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.11f, Max = 500 }, root, "Climb");
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafFall = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1f, Life1 = 1.8f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.12f, Size1 = 0.24f, Gravity = 0.35f, Drag = 1.5f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 300 }, root, "LeafFall");
            _jade = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.4f, 1f, 0.5f), 4f, 1f)), Life0 = 0.4f, Life1 = 1f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.03f, Size1 = 0.07f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 2f, Max = 300 }, root, "Jade");
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.55f, 0.85f, 1f), 2.4f, 0.7f)), Life0 = 0.4f, Life1 = 0.7f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.025f, Size1 = 0.05f, Gravity = 2.6f, Stretch = true, StretchVel = 0.06f, StretchLen = 1.2f, Max = 300 }, root, "Drip");
            _light = Vol2Fs.PointLight(root, "JadeLight", new Color(0.4f, 1f, 0.6f), 9f);
            _light.transform.position = P + Vector3.up * 1.8f;
       }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            if (!HasChild) { if (Patch != null) Vol2FsRxFeedKit.Consume(Patch, t, 0f, Mathf.Max(0.1f, DryRel), 0.2f); return; }
            bool dried = ChildWhy == Vol2MaterialEnd.Consumed || ChildWhy == Vol2MaterialEnd.ChildConsumed;
            float outK = Vol2FsRxFeedKit.Out(t, ChildEndAt, dried ? 0.3f : 1.3f);
            if (!_splashed)
            {
                _splashed = true; HitStop(t, 0.07f);
                for (int k = 0; k < 90; k++) Vol2FsB1.EmitV(_splash, P + Vector3.up * 0.2f + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.9f).normalized * Random.Range(2f, 5.5f));
            }
            // the old dark vines wither down as the water is drunk (0.1 - 0.6 s)
            Vol2FsRxFeedKit.Consume(Patch, t, 0.1f, 0.6f, 0.5f);
            // the new vines swell up (0.25 - 0.85 s), leaves unfold behind their tips; at the end they draw back into the ground
            float back = dried ? 0f : Vol2Fs.Smooth(ChildEndAt < 0f ? 999f : ChildEndAt, (ChildEndAt < 0f ? 999f : ChildEndAt) + 1.0f, t);
            float dryK = dried ? 1f - outK : 0f;
            for (int j = 0; j < _vines.Tubes.Count; j++)
            {
                float g = Vol2Fs.Smooth(0.14f + 0.03f * j, 0.7f + 0.03f * j, t);
                _vines.Tubes[j].Grow = g * (1f - back) * (1f - dryK);
            }
            _vines.Write(_vineMesh);
            for (int i = 0; i < _leaves.Items.Count; i++)
            {
                var l = _leaves.Items[i]; var tb = _vines.Tubes[_leafTube[i]];
                l.Grow = Mathf.Max(0f, Vol2Fs.EaseOutBack(Mathf.Clamp01((tb.Grow - _leafAt[i]) * 4f)));
                _leaves.Items[i] = l;
            }
            _leaves.Write(_leafMesh);
            if (!_fell && back > 0.05f)
            {
                _fell = true;
                foreach (var l in _leaves.Items) Vol2FsB1.Emit(_leafFall, l.Pos, 2);
            }
            foreach (var tb in _roots.Tubes) tb.Grow = Vol2Fs.Smooth(0f, 0.8f, back) * (1f - Vol2Fs.Smooth(0.8f, 1f, back) * 0f);
            _roots.Write(_rootMesh);
            // the water climbing the stems: glowing streaks running up each vine, drops of light along them
            _streak.Clear();
            float climb = Vol2Fs.Smooth(0.15f, 0.4f, t) * outK * (1f - back);
            if (climb > 0.01f)
            {
                var pts = new Vector3[6]; var w = new float[6]; var al = new float[6];
                for (int j = 0; j < _vines.Tubes.Count; j++)
                {
                    var tb = _vines.Tubes[j];
                    float speed = t < 1.2f ? 1.6f : 0.6f, head = Mathf.Repeat(t * speed + j * 0.37f, 1.25f) * tb.Grow;
                    for (int k = 0; k < 6; k++)
                    {
                        float u = Mathf.Clamp01(head - 0.25f + 0.05f * k);
                        pts[k] = Vol2FsTubes.CatmullRom(tb.Ctrl, u) + Vector3.up * 0.02f; w[k] = 0.1f + 0.14f * k / 5f; al[k] = climb * (k / 5f);
                    }
                    if (head > 0.05f) _streak.Strip(pts, w, null, Color.white, al, 0.3f + 0.1f * j, cam);
                    if (Random.value < 0.5f * climb) Vol2FsB1.EmitV(_climb, Vol2FsTubes.CatmullRom(tb.Ctrl, Mathf.Clamp01(head)), Vector3.up * 0.3f);
                }
            }
            _streak.Write(_streakMesh);
            if (t > 0.3f && t < 1.2f && Random.value < 0.8f) Vol2FsB1.Emit(_jade, P + Random.insideUnitSphere * Radius + Vector3.up * Random.Range(0.4f, 2.6f), 3);
            if (t > 0.3f && t < 1.5f && Random.value < 0.5f) Vol2FsB1.Emit(_drip, P + Random.insideUnitSphere * Radius * 0.6f + Vector3.up * Random.Range(0.6f, 1.6f), 1);
            if (_light != null) _light.intensity = (_mine ? 10f : 2f) * Vol2Fs.Smooth(0.2f, 0.5f, t) * (0.45f + 0.55f * (1f - Vol2Fs.Smooth(0.8f, 2.2f, t))) * outK * (1f - back);
            // r3: the ink sweeps UP with the water - five brush strokes spiralling up round the vines (r2: the spiky black star several cells share)
            _ink.Clear();
            float h = t - 0.15f;
            if (h > 0f && h < 2.2f)
            {
                var pts = new Vector3[16]; var w = new float[16]; var al = new float[16];
                for (int s = 0; s < 5; s++)
                {
                    float d0 = 0.06f * s, reveal = Vol2Fs.Smooth(d0, d0 + 0.45f, h), life = 1f - Vol2Fs.Smooth(0.7f + 0.1f * s, 1.8f + 0.1f * s, h);
                    if (reveal <= 0.01f || life <= 0.01f) continue;
                    float a0 = Seed * 0.4f + s * 1.26f, turns = 0.9f + 0.25f * Vol2Fs.Hash(Seed, s + 200), top = 2.6f + 1.2f * Vol2Fs.Hash(Seed, s + 210);
                    for (int k = 0; k < 16; k++)
                    {
                        float u = k / 15f * reveal, a = a0 + u * turns * Mathf.PI * 2f, rr = Radius * Mathf.Lerp(1.7f, 0.7f, u);
                        pts[k] = P + (Dir * Mathf.Cos(a) + Side * Mathf.Sin(a)) * rr + Vector3.up * (0.1f + u * top);
                        w[k] = (0.06f + 0.26f * Mathf.Sin(Mathf.Clamp01(k / 15f) * Mathf.PI)) * (0.6f + 0.4f * Vol2Fs.Hash(Seed, s + 220));
                        al[k] = life * (1f - 0.4f * k / 15f);
                    }
                    _ink.Strip(pts, w, null, Color.white, al, 0.2f * s, cam);
                }
            }
            _ink.Write(_inkMesh);
            _seal.Clear(); _sealGlow.Clear();
            Vol2FsRxFeedKit.Seal(_seal, _sealGlow, P, Ground, Radius * 2.0f, Vol2Fs.Smooth(0.35f, 0.8f, t), Vol2FsRxFeedKit.Left(t, ChildExpireRel, _lifeS), outK, cam, Seed * 0.1f);
            _seal.Write(_sealMesh); _sealGlow.Write(_sealGlowMesh);
        }

        protected override void OnChildEnd(Vol2MaterialEnd why, float t) { Note += " | wood child " + (why == Vol2MaterialEnd.Expired ? "loosened into rootlets" : "dried in place"); }
    }
}
