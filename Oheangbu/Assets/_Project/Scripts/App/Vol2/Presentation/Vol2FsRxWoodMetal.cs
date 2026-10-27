using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>목>금 잘림 (막힘) - free showcase batch 8, concept wm_a (a thick jade-veined vine lash streams into the enemy's steel blades and is CUT at their
    /// straight edges in a white-gold flash: short vine stubs fall both ways, leaves scatter; half the blades snap and fly; the rest stand).
    /// Facts only: the acting shape is spent at the commit (the lash meets the patch edge, is severed, its two halves recoil and fall); the enemy's metal patch
    /// survives at x0.5 (Blocked(half)), its life not renewed - half of its blades snap off and lie down, the other half stand (still the enemy's dull steel) to
    /// the material's own end (Expired: the blades sink with a last spark). Nothing is damaged, nothing is born.</summary>
    public sealed class Vol2FsRxWoodMetal : Vol2FsReact
    {
        static readonly Color Jade = new Color(0.45f, 1f, 0.55f), Gold = new Color(1f, 0.86f, 0.55f);
        Mesh _lashMesh, _inkMesh, _cutMesh, _scarMesh; Light _keep; Light _flash;
        readonly Vol2FsTubes _fib = new Vol2FsTubes(); readonly Vol2FsStrips _lash = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _cut = new Vol2FsStrips(), _scar = new Vol2FsStrips();
        ParticleSystem _sparks, _shards, _stubs, _leaves, _jade;
        Vector3 _hit, _tail, _bow;
        bool _cutDone; float _lastT;
        readonly List<Vol2FsRxBlockKit.Flyer> _snap = new List<Vol2FsRxBlockKit.Flyer>();
        bool _endFx;

        protected override void OnBuild()
        {
            var root = Root.transform;
            // r3: the lash is three twisted living vines (3D tubes, dark jade with lit veins) - r1 / r2 a flat mint paddle floating in the sky
            Vol2Fs.MeshObject(root, "Lash", Kit.Bark(new Color(0.32f, 0.58f, 0.26f), new Color(0.2f, 1.0f, 0.3f) * 1.6f, 0.3f, "fs_vine", "fs_vein"), out _lashMesh);
            for (int j = 0; j < 3; j++) { var tb = _fib.Add(new Vector3[9], 0.11f - 0.015f * j, 0.045f, 30); tb.Sides = 6; tb.Knots = 0.3f; tb.Sharp = 0.12f; }
            Vol2Fs.MeshObject(root, "LashInk", Kit.InkSolid("fs_stroke2", InkC, Jade * 1.3f, 0.14f, 0.5f, 0.35f, 1.3f), out _inkMesh);
            var cut = M("fs_streak", Gold * 1.5f, 3f, 1f); cut.NoiseAmt = 0.15f; cut.Soft = 0.02f; cut.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "CutLine", Kit.Mat(cut), out _cutMesh);
            Vol2Fs.MeshObject(root, "HalfScar", Vol2FsRxBlockKit.ScarMat(Kit), out _scarMesh);
            _keep = Vol2FsRxBlockKit.KeepLight(root, Contacted);
            _flash = Vol2Fs.PointLight(root, "CutFlash", Gold, 6f);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 5f, Speed1 = 14f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.6f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 500 }, root, "Sparks");
            var steel = Kit.Metal(1.2f, 0.4f) ?? Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.8f, 0.8f, 0.85f), 1f, 0.8f);
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = steel, Mesh = Kit.SharedMesh("rxShard3", () => Vol2FsB1.Shard(3)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.06f, Size1 = 0.14f, Gravity = 2.2f, Drag = 0.4f, Spin = 700f, Max = 80 }, root, "Shards");
            var bark = Kit.Bark(new Color(0.75f, 0.95f, 0.55f), new Color(0.3f, 1.1f, 0.3f) * 2f, 0.25f, "fs_vine", "fs_vein");
            _stubs = Vol2Fs.Build(new Vol2FsPs { Mat = bark, Mesh = Vol2FsRxBlockKit.StubMesh(Kit), Life0 = 1.4f, Life1 = 2.0f, Speed0 = 2f, Speed1 = 4.5f, Size0 = 0.5f, Size1 = 0.85f, Gravity = 2.4f, Drag = 0.5f, Spin = 500f, Max = 60 }, root, "Stubs");
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 1.0f, Life1 = 1.8f, Speed0 = 1.5f, Speed1 = 4.5f, Size0 = 0.1f, Size1 = 0.22f, Gravity = 0.35f, Drag = 1.5f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 300 }, root, "Leaves");
            _jade = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4f, 1f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.03f, Size1 = 0.06f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 2f, Max = 300 }, root, "Jade");
            foreach (var ps in new[] { _sparks, _shards, _stubs, _leaves, _jade }) ps.Play(false);
            // the lash comes along the incoming direction, bowed out to the caster's left (it reads across the screen), and stops at the blades' near edge
            _hit = G(P - Dir * Radius * 0.55f) + Vector3.up * 0.5f;
            _tail = _hit - Dir * 2.7f - Side * 0.75f + Vector3.up * 0.45f;      // r3: low and close to the patch
            _bow = Vector3.Lerp(_tail, _hit, 0.5f) - Side * 1.1f + Vector3.up * 0.35f;
            // half the blades (every other) snap at the cut
            if (Patch != null) for (int i = 0; i < Patch.Props.Count; i += 2) _snap.Add(new Vol2FsRxBlockKit.Flyer { T = Patch.Props[i] });
            Note += " | cut wm_a: blades " + (Patch != null ? Patch.Props.Count : 0) + " snap " + _snap.Count;
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float dt = Mathf.Clamp(t - _lastT, 0f, 0.05f); _lastT = t;
            _lash.Clear(); _ink.Clear(); _cut.Clear();
            const float CutAt = 0.07f;
            if (!_cutDone && t >= CutAt)
            {
                _cutDone = true; HitStop(t, 0.08f);
                for (int k = 0; k < 280; k++) Vol2FsB1.EmitV(_sparks, _hit + Vector3.up * Random.Range(-0.4f, 1.2f), (Random.onUnitSphere + Side * 0.4f + Vector3.up * 0.3f).normalized * Random.Range(5f, 16f), -1f, -1f);
                if (_flash != null) { _flash.transform.position = _hit + Vector3.up * 0.4f; _flash.intensity = 9f; }
                for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_shards, _hit + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(2f, 6f), -1f, -1f);
                for (int k = 0; k < 26; k++) { float s = k % 2 == 0 ? 1f : -1f; Vol2FsB1.EmitV(_stubs, _hit + Random.insideUnitSphere * 0.25f, Side * s * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(1f, 3f) - Dir * Random.Range(0f, 1.5f), -1f, -1f); }
                for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_leaves, _hit + Random.insideUnitSphere * 0.4f, (Random.onUnitSphere + Vector3.up * 0.5f) * Random.Range(1.5f, 4.5f), -1f, -1f);
                for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_jade, _hit, Random.onUnitSphere * Random.Range(0.5f, 2.5f), -1f, -1f);
                foreach (var f in _snap)
                {
                    if (f.T == null) continue;
                    Vector3 o = f.T.position - P; o.y = 0f; o = o.sqrMagnitude > 1e-4f ? o.normalized : Side;
                    f.V = o * Random.Range(1.6f, 3.2f) + Vector3.up * Random.Range(1.5f, 3.2f) + Dir * Random.Range(0.3f, 1.4f);
                    f.W = Random.onUnitSphere * Random.Range(500f, 900f); f.Live = true;
                }
            }
            // the lash: in to the edge (0 .. CutAt), then cut - the back half recoils toward the caster thinning, the cut-off front flings past and drops
            float lashLife = 1f - Vol2Fs.Smooth(0.25f, 1.25f, t);
            if (!_cutDone)
            {
                float reach = Mathf.Clamp01(0.55f + t / CutAt * 0.45f);
                Vines(_hit, _bow, reach, t);
                Vol2FsRxBlockKit.Ribbon(_ink, _tail + Vector3.down * 0.12f, _bow + Vector3.down * 0.12f, _hit + Vector3.down * 0.12f, 0f, reach, 0.06f, 0.32f, 1f, 0.6f, cam, 0.1f, t * 20f + 1f);
            }
            else if (lashLife > 0.01f)
            {
                float a = t - CutAt;
                float back = Mathf.Lerp(0.92f, 0.35f, Vol2Fs.Smooth(0f, 0.6f, a));
                Vector3 droop = Vector3.down * 0.5f * Vol2Fs.Smooth(0f, 0.8f, a);
                Vines(_hit + droop - Dir * 0.3f * a, _bow + droop * 0.5f, back * Mathf.Clamp01(lashLife * 1.5f), t);
                Vol2FsRxBlockKit.Ribbon(_ink, _tail + Vector3.down * 0.12f, _bow + droop * 0.5f + Vector3.down * 0.12f, _hit + droop + Vector3.down * 0.12f, 0f, back, 0.05f, 0.28f, lashLife, 0.6f, cam, 0.14f, t * 14f + 1f);
                // the severed tip flies on past the blades and falls
                Vector3 f0 = _hit + Dir * (0.4f + 2.2f * a) + Side * 0.6f * a + Vector3.up * (0.4f * a - 2.4f * a * a);
                float tipLife = 1f - Vol2Fs.Smooth(0.1f, 0.7f, a);
                if (tipLife > 0.01f)
                {
                    Vector3 f1 = f0 + Dir * 0.9f + Vector3.up * 0.15f;
                    Vol2FsRxBlockKit.Ribbon(_ink, f0, Vector3.Lerp(f0, f1, 0.5f) + Vector3.up * 0.2f, f1, 0f, 1f, 0.5f, 0.1f, tipLife, 0.7f, cam, 0.08f, t * 9f, 10);
                }
                // the cut: a vertical white-gold slash at the blades' edge, fast then a slow afterimage
                float cl = 1f - Vol2Fs.Smooth(0.08f, 1.1f, a);
                Vector3 c0 = _hit + Vector3.up * 1.9f + Side * 0.3f, c1 = _hit - Vector3.up * 0.75f - Side * 0.3f;
                Vol2FsB6.Slash(_cut, c0, c1, Dir * 0.25f, 0.42f, Vol2Fs.Smooth(0f, 0.05f, a), cl, 0.4f, cam);
                Vol2FsB6.Slash(_cut, c0 + Side * 0.4f, c1 + Side * 0.4f, Dir * 0.25f, 0.6f, 1f, cl * 0.35f, 0.7f, cam);
            }
            _scar.Clear(); if (Patch != null) { Vol2FsRxBlockKit.Survivor(_scar, P, Ground, Radius, Dir, t, ContactEndAt, ContactExpireRel, cam); Vol2FsRxBlockKit.KeepLightTick(_keep, P, t, ContactEndAt); }
            if (_cutDone && lashLife <= 0.01f) Vines(_hit, _bow, 0f, t);
            _fib.Write(_lashMesh); _ink.Write(_inkMesh); _cut.Write(_cutMesh); _scar.Write(_scarMesh);
            if (_flash != null) _flash.intensity = Mathf.MoveTowards(_flash.intensity, 0f, dt * 18f);
            // the snapped half of the blades tumbles and lies, then sinks away (only the standing half is the patch now)
            if (_cutDone) foreach (var f in _snap) Vol2FsRxBlockKit.Fly(f, dt, Ground, t - CutAt, 2.0f, 2.8f);
            // the material's end (Expired): the standing blades sink with a last spark
            if (ContactEndAt >= 0f && Patch != null)
            {
                float k = Vol2Fs.Smooth(ContactEndAt, ContactEndAt + 0.7f, t);
                Patch.Pose(1f - k, k * Patch.Height);
                if (!_endFx) { _endFx = true; for (int i = 1; i < Patch.Props.Count; i += 2) if (Patch.Props[i] != null) for (int j = 0; j < 12; j++) Vol2FsB1.EmitV(_sparks, Patch.Props[i].position + Vector3.up * 0.4f, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f); }
            }
        }

        /// <summary>The three vines twisted round the bowed path tail -> head, grown to <paramref name="grow"/> of it.</summary>
        void Vines(Vector3 head, Vector3 bow, float grow, float t)
        {
            for (int j = 0; j < _fib.Tubes.Count; j++)
            {
                var tb = _fib.Tubes[j];
                for (int i = 0; i < tb.Ctrl.Length; i++)
                {
                    float u = i / (float)(tb.Ctrl.Length - 1), ph = j * 2.09f + u * 9f + t * 6f;
                    Vector3 p = Vol2FsRxBlockKit.Bez(_tail, bow, head, u);
                    float rr = 0.13f * (1f - 0.5f * u);
                    tb.Ctrl[i] = p + (Side * Mathf.Cos(ph) + Vector3.up * Mathf.Sin(ph)) * rr;
                }
                Vol2FsRxBlockKit.Refresh(tb);
                tb.Grow = Mathf.Clamp01(grow);
            }
        }

        protected override void OnContactEnd(Vol2MaterialEnd why, float t) { if (why != Vol2MaterialEnd.Expired && Patch != null) Patch.SetActive(false); }
    }

    /// <summary>Batch 8 막힘 (Block) group helpers: a bowed ribbon, short vine stubs, a falling / sinking prop (the half of the enemy patch that goes).</summary>
    public static class Vol2FsRxBlockKit
    {
        public static Vector3 Bez(Vector3 a, Vector3 c, Vector3 b, float u) { float v = 1f - u; return v * v * a + 2f * v * u * c + u * u * b; }

        /// <summary>A ribbon from a (tail) to b (head) bowed through c, drawn over u0..u1 of it, width w0 at the tail to w1 at the head (a short taper at the head).</summary>
        public static void Ribbon(Vol2FsStrips s, Vector3 a, Vector3 c, Vector3 b, float u0, float u1, float w0, float w1, float life, float rnd, Vector3 cam, float wave = 0f, float phase = 0f, int n = 18)
        {
            if (life <= 0.01f || u1 <= u0 + 1e-3f || n < 2) return;
            var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
            Vector3 d = b - a; d.y = 0f;
            Vector3 side = d.sqrMagnitude > 1e-6f ? Vector3.Cross(d.normalized, Vector3.up) : Vector3.right;
            for (int k = 0; k < n; k++)
            {
                float x = k / (float)(n - 1), u = Mathf.Lerp(u0, u1, x);
                pts[k] = Bez(a, c, b, u) + Vector3.up * Mathf.Sin(u * 9f + phase) * wave + side * Mathf.Cos(u * 7f + phase) * wave * 0.6f;
                w[k] = Mathf.Lerp(w0, w1, x) * Mathf.Clamp01((1f - x) * 7f + 0.2f);
                al[k] = life * (0.35f + 0.65f * x);
            }
            s.Strip(pts, w, null, Color.white, al, rnd, cam, u0, u1);
        }

        /// <summary>r2: the half of the enemy patch that went - a ghost outline of its old extent on the near (struck) side, in the enemy's violet ink, drawn in after the
        /// stop and fading by ~3 s, so "it survives at HALF" reads against the stand-in.</summary>
        public static void HalfScar(Vol2FsStrips s, Vector3 p, float ground, float r, Vector3 dir, float t, Vector3 cam)
        {
            float life = Vol2Fs.Smooth(0.12f, 0.35f, t) * (1f - Vol2Fs.Smooth(2.0f, 3.2f, t));
            if (life <= 0.01f) return;
            float a0 = Mathf.Atan2(-dir.z, -dir.x) - Mathf.PI * 0.5f;
            Vol2FsB4A.GroundArc(s, p, ground + 0.02f, r * 1.12f, 0.16f, a0, Mathf.PI, life, 0.2f, cam, 28);
            Vol2FsB4A.GroundArc(s, p, ground + 0.02f, r * 1.32f, 0.07f, a0 + 0.2f, Mathf.PI - 0.4f, life * 0.6f, 0.7f, cam, 24);
        }
        /// <summary>r3: the surviving HALF for its whole remaining life - the lost (near, struck) half as a dashed ghost outline, the kept half as a solid lit rim of the
        /// enemy's violet ink; in after the stop, a little dimmer over the last 1.5 s before ContactExpireRel, out with the material's end.</summary>
        public static void Survivor(Vol2FsStrips s, Vector3 p, float ground, float r, Vector3 dir, float t, float endAt, float expireRel, Vector3 cam)
        {
            float life = Vol2Fs.Smooth(0.12f, 0.4f, t) * (endAt >= 0f ? 1f - Vol2Fs.Smooth(endAt, endAt + 0.6f, t) : 1f) * (expireRel > 0f ? Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((expireRel - t) / 1.5f)) : 1f);
            if (life <= 0.01f) return;
            float lost0 = Mathf.Atan2(-dir.z, -dir.x) - Mathf.PI * 0.5f;
            for (int k = 0; k < 6; k++) Vol2FsB4A.GroundArc(s, p, ground + 0.02f, r * 1.18f, 0.1f, lost0 + k * (Mathf.PI / 6f) + 0.05f, Mathf.PI / 6f - 0.14f, life * 0.8f, 0.2f + 0.1f * k, cam, 6);
            Vol2FsB4A.GroundArc(s, p, ground + 0.025f, r * 1.06f, 0.22f, lost0 + Mathf.PI, Mathf.PI, life, 0.5f, cam, 30);
            Vol2FsB4A.GroundArc(s, p, ground + 0.025f, r * 0.7f, 0.08f, lost0 + Mathf.PI + 0.3f, Mathf.PI - 0.6f, life * 0.5f, 0.8f, cam, 20);
        }

        /// <summary>r3: a soft light on the surviving half (the enemy's violet, a little of the patch's body) - it never goes dark in the remaining life.</summary>
        public static Light KeepLight(Transform root, Body body) => Vol2Fs.PointLight(root, "KeepLight", Color.Lerp(new Color(0.7f, 0.4f, 1f), Vol2FsRun.Accent(body), 0.4f), 3.2f);
        public static void KeepLightTick(Light l, Vector3 p, float t, float endAt)
        {
            if (l == null) return;
            l.transform.position = p + Vector3.up * 0.7f;
            l.intensity = 3.2f * Vol2Fs.Smooth(0.15f, 0.5f, t) * (endAt >= 0f ? 1f - Vol2Fs.Smooth(endAt, endAt + 0.6f, t) : 1f);
        }

        public static Material ScarMat(Vol2FsKit kit) { var m = Vol2FsMat.Make("fs_ring", new Color(0.62f, 0.34f, 0.85f), 2.2f, 0.5f); m.NoiseAmt = 0.45f; m.Erode = 0.3f; m.Soft = 0.1f; return kit.Mat(m); }

        /// <summary>A tube keeps the sample points Add made: refresh them (and its length) after its control points moved.</summary>
        public static void Refresh(Vol2FsTubes.Tube tb)
        {
            int n = Mathf.Max(2, tb.Pts.Count - 1);
            tb.Pts.Clear(); tb.Acc.Clear();
            for (int s = 0; s <= n; s++) tb.Pts.Add(Vol2FsTubes.CatmullRom(tb.Ctrl, s / (float)n));
            float acc = 0f; tb.Acc.Add(0f);
            for (int s = 1; s < tb.Pts.Count; s++) { acc += Vector3.Distance(tb.Pts[s - 1], tb.Pts[s]); tb.Acc.Add(acc); }
            tb.Len = acc;
        }

        /// <summary>A short broken vine stub (unit length along +Y, pivot at its middle) - the kit owns it.</summary>
        public static Mesh StubMesh(Vol2FsKit kit) => kit.SharedMesh("rxBlockStub", () =>
        {
            var tb = new Vol2FsTubes();
            var tube = tb.Add(new[] { new Vector3(0f, -0.5f, 0f), new Vector3(0.04f, -0.1f, 0.02f), new Vector3(-0.03f, 0.25f, -0.02f), new Vector3(0f, 0.5f, 0f) }, 0.07f, 0.055f, 8);
            tube.Grow = 1f; tube.Sharp = 0.02f; tube.Sides = 6; tube.Knots = 0.3f;
            var m = new Mesh { name = "RxStub" }; tb.Write(m); return m;
        });

        /// <summary>A piece of the enemy patch that goes (a snapped blade, a toppled slab): it flies with V / spins with W, lands, lies, then sinks.</summary>
        public sealed class Flyer { public Transform T; public Vector3 V, W; public bool Live, Landed; public float LandedAt = -1f; public Vector3 RestAt; }

        public static void Fly(Flyer f, float dt, float ground, float age, float sinkFrom, float hideAt)
        {
            if (f.T == null || !f.Live) return;
            if (age >= hideAt) { if (f.T.gameObject.activeSelf) f.T.gameObject.SetActive(false); return; }
            if (!f.Landed)
            {
                f.V += Vector3.down * 9.8f * dt;
                var p = f.T.position + f.V * dt;
                f.T.rotation = Quaternion.Euler(f.W * dt) * f.T.rotation;
                if (p.y <= ground + 0.03f && f.V.y < 0f) { p.y = ground + 0.03f; f.Landed = true; f.LandedAt = age; f.RestAt = p; Vector3 fw = f.T.up; fw.y = 0f; if (fw.sqrMagnitude < 1e-4f) fw = Vector3.forward; f.T.rotation = Quaternion.LookRotation(fw.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f); }
                f.T.position = p;
            }
            else if (age > sinkFrom) f.T.position = f.RestAt + Vector3.down * 0.5f * Vol2Fs.Smooth(sinkFrom, hideAt, age);
        }
    }
}
