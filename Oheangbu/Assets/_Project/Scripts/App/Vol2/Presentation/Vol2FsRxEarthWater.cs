using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>토>수 진창 (꺾음: earth overcomes water; new - no prefab ever existed) - free showcase batch 8, concept ea_c (the earth falls into the enemy's pool and
    /// the two set into a low heaving quagmire: flat slabs half-sunk, hollow swirling currents in the mud, ropes of mud curling up the bear's legs). Facts only: the
    /// commit consumes the shape and the enemy's water patch (no child); the product is the SLOW on the bodies in the footprint (bind x0.5) for Dur (3 s) - the
    /// mud lives exactly that long, then dries, cracks and sinks. No damage, no explosion: low and wide.</summary>
    public sealed class Vol2FsRxEarthWater : Vol2FsReact
    {
        static readonly Color MudLight = new Color(0.62f, 0.46f, 0.3f), Amber = new Color(0.95f, 0.66f, 0.3f);
        Material _mud, _dry, _stone;
        readonly List<Transform> _slabs = new List<Transform>();
        readonly List<Vector3> _slabRest = new List<Vector3>(); readonly List<Quaternion> _slabRot = new List<Quaternion>();
        Mesh _swirlMesh, _ropeMesh, _inkMesh, _crackMesh, _sheetMesh; readonly Vol2FsStrips _sheet = new Vol2FsStrips(); ParticleSystem _ropeDrip;
        readonly Vol2FsStrips _swirl = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _cracks = new Vol2FsStrips();
        readonly Vol2FsTubes _ropes = new Vol2FsTubes();
        List<Vol2FsB1.Stroke> _burst;
        ParticleSystem _splash, _blobs, _bubbles, _dust;
        readonly List<Transform> _sinks = new List<Transform>();
        bool _dried; MeshRenderer _poolR;

        protected override float Burst => 2.2f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            _mud = Kit.Custom("rxbreak_mud", () =>
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fs_RxMud" };
                m.SetTexture("_BaseMap", Kit.Tex("fs_wet")); m.SetColor("_BaseColor", new Color(0.17f, 0.115f, 0.08f)); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.88f);      // r2: darker, glossier (r1 brown on brown)
                return m;
            });
            _dry = Kit.Custom("rxbreak_drymud", () =>
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fs_RxDryMud" };
                m.SetTexture("_BaseMap", Kit.Tex("fs_crack")); m.SetColor("_BaseColor", new Color(0.55f, 0.43f, 0.33f)); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.12f);
                return m;
            });
            _stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.5f, 0.4f), 0f, 0.15f);
            if (Patch != null)
            {
                if (Patch.Pool != null) _poolR = Patch.Pool.GetComponent<MeshRenderer>();
                var rip = Vol2FsRxBreakKit.Prop(Patch, "Ripples"); if (rip != null) rip.gameObject.SetActive(false);
                Patch.StopParticles();
            }
            else
            {
                // no stand-in (a non-disc footprint in the lab): the look lays its own mud bed
                _poolR = Vol2FsB1.Prop(root, "MudBed", Vol2FsB7.PoolMesh(Kit, Seed % 97), _mud, false);
                _poolR.transform.position = G(P); _poolR.transform.localScale = new Vector3(Radius, 1f, Radius);
            }
            // six flat slabs fall in and half-sink, tilted
            var slabMesh = Kit.SharedMesh("rxbreak_mudslab", () => Vol2FsB1.Slab(613, Vector3.one, 0.8f));
            for (int k = 0; k < 6; k++)
            {
                float a = (k / 6f + 0.1f * Vol2Fs.Hash(Seed, k)) * Mathf.PI * 2f, r = Radius * (0.6f + 1.2f * Vol2Fs.Hash(Seed, k + 10));
                var s = Vol2FsB1.Prop(root, "Slab" + k, slabMesh, _stone).transform;
                Vector3 at = G(P) + (Dir * Mathf.Cos(a) + Side * Mathf.Sin(a)) * r;
                s.localScale = new Vector3(1.1f + 0.6f * Vol2Fs.Hash(Seed, k + 20), 0.26f + 0.1f * Vol2Fs.Hash(Seed, k + 30), 0.8f + 0.4f * Vol2Fs.Hash(Seed, k + 40));
                var rot = Quaternion.Euler((Vol2Fs.Hash(Seed, k + 50) - 0.5f) * 40f, Vol2Fs.Hash(Seed, k + 60) * 360f, (Vol2Fs.Hash(Seed, k + 70) - 0.5f) * 40f);
                s.SetPositionAndRotation(at + Vector3.up * 1.4f, rot);
                _slabs.Add(s); _slabRest.Add(at + Vector3.down * 0.06f); _slabRot.Add(rot);
            }
            // r3: the mud itself as a wide churning sheet (r2: the dark glossy pool did not read against the ground; the amber currents read as a gold ring)
            var sheet = M("fs_swirl", new Color(0.4f, 0.27f, 0.17f), 1f, 0f); sheet.Opacity = 1f; sheet.Soft = 0.3f; sheet.NoiseAmt = 0.35f; sheet.Erode = 0.15f; sheet.Scroll = new Vector2(-0.22f, 0f);
            Vol2Fs.MeshObject(root, "MudSheet", Kit.Mat(sheet), out _sheetMesh);
            var sw = M("fs_swirl", new Color(0.78f, 0.6f, 0.42f), 1.15f, 0.12f); sw.NoiseAmt = 0.35f; sw.Soft = 0.2f; sw.Scroll = new Vector2(-0.6f, 0f);
            Vol2Fs.MeshObject(root, "Currents", Kit.Mat(sw), out _swirlMesh);
            var ropeMat = Kit.Custom("rxbreak_mudrope", () => { var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fs_RxMudRope" }; m.SetTexture("_BaseMap", Kit.Tex("fs_wet")); m.SetColor("_BaseColor", new Color(0.42f, 0.29f, 0.19f)); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.85f); return m; });
            var rope = Vol2Fs.MeshObject(root, "MudRopes", ropeMat, out _ropeMesh); rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Vol2Fs.MeshObject(root, "InkMud", Kit.InkSolid("fs_stroke2", Vol2FsRxBreakKit.Ink, Amber * 1.8f, 0.14f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            Vol2Fs.MeshObject(root, "DryCracks", Kit.InkSolid("fs_stroke2", new Color(0.12f, 0.08f, 0.06f), Amber * 0.8f, 0.1f, 0.4f, 0.3f, 1.2f), out _crackMesh);
            _burst = Vol2FsB1.Burst(Seed, 16, Side, Dir, 2.4f, 4.0f, 0.3f, 0.56f, 0.22f, 0.3f);
            var drop = M("fs_dot", new Color(0.32f, 0.22f, 0.15f, 1f), 1f, 0f); drop.Opacity = 0.95f; drop.NoiseAmt = 0.25f;
            _splash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 2f, Speed1 = 5f, Size0 = 0.08f, Size1 = 0.2f, Gravity = 2.2f, Drag = 0.3f, Max = 400 }, root, "MudSplash");
            _blobs = Vol2Fs.Build(new Vol2FsPs { Mat = _mud, Mesh = Kit.SharedMesh("rxbreak_blob", () => Vol2FsB1.Rock(617, 0.5f, 0.6f, 10, 7)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 2.5f, Speed1 = 5f, Size0 = 0.08f, Size1 = 0.2f, Gravity = 2.6f, Spin = 300f, Max = 80 }, root, "MudBlobs");
            _bubbles = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_ring", new Color(0.7f, 0.55f, 0.4f), 1.2f, 0.3f)), Loop = true, Rate = 0f, Life0 = 0.5f, Life1 = 0.9f, Speed0 = 0f, Speed1 = 0.05f, Size0 = 0.12f, Size1 = 0.3f, Horizontal = true, SizeStart = 0.2f, SizePeak = 1f, SizeEnd = 1.3f, Radius = Radius * 1.1f, ShapeScale = new Vector3(1f, 0.02f, 1f), Max = 80 }, root, "Bubbles");
            _bubbles.transform.position = G(P) + Vector3.up * 0.04f;
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.6f, 0.47f, 0.35f, 1f), 0.45f)), Life0 = 0.9f, Life1 = 1.6f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.4f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.6f, Drag = 1.2f, Max = 80 }, root, "DryDust");
            _ropeDrip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.05f, Size1 = 0.11f, Gravity = 2.2f, Max = 200 }, root, "RopeDrip");
            foreach (var ps in new[] { _splash, _blobs, _bubbles, _dust, _ropeDrip }) ps.Play(false);
            var be = _bubbles.emission; be.rateOverTime = 14f;
            Vector3 c = G(P) + Vector3.up * 0.15f;
            for (int k = 0; k < 220; k++) Vol2FsB1.EmitV(_splash, c + Random.insideUnitSphere * Radius * 0.7f, (Vol2FsRxBreakKit.Flat(Random.insideUnitSphere) * 2.4f + Vector3.up * Random.Range(0.6f, 1.6f)) * Random.Range(1.2f, 3f), -1f, -1f);
            for (int k = 0; k < 26; k++) Vol2FsB1.EmitV(_blobs, c + Random.insideUnitSphere * 0.4f, (Vol2FsRxBreakKit.Flat(Random.insideUnitSphere) * 2.2f + Vector3.up * 1.8f) * Random.Range(1f, 1.8f), -1f, -1f);
            // a mud sink under each body (its feet held in the mud)
            foreach (var s in Bodies)
            {
                var sink = Vol2FsB1.Prop(root, "Sink", Vol2FsB7.PoolMesh(Kit, (Seed + s.Index * 13) % 97), _mud, false).transform;
                sink.position = G(s.Feet) + Vector3.up * 0.01f; sink.localScale = new Vector3(0.01f, 1f, 0.01f);
                _sinks.Add(sink);
            }
            HitStop(0f, 0.07f);
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            float fall = Vol2Fs.Smooth(0f, 0.18f, t);
            float dryK = Vol2Fs.Smooth(Dur, Dur + 0.6f, t), sinkK = Vol2Fs.Smooth(Dur + 0.4f, Dur + 1.3f, t);
            // the pool turns to mud and spreads (0.25 s), dries after the slow (Dur), then sinks
            if (_poolR != null)
            {
                if (t > 0.04f && !_dried && _poolR.sharedMaterial != _mud) _poolR.sharedMaterial = _mud;
                if (!_dried && dryK > 0.5f) { _dried = true; _poolR.sharedMaterial = _dry; var be = _bubbles.emission; be.rateOverTime = 0f; Vol2FsB1.Emit(_dust, G(P) + Vector3.up * 0.2f, 14); }
            }
            if (Patch != null) Patch.Pose(1f - 0.8f * Vol2Fs.Smooth(0.02f, 0.3f, t), 0.3f * sinkK, 1f + 1.4f * Vol2Fs.Smooth(0.02f, 0.3f, t));
            else if (_poolR != null) { float sp = 1f + 1.4f * Vol2Fs.Smooth(0.02f, 0.3f, t); _poolR.transform.localScale = new Vector3(Radius * sp, 1f, Radius * sp); _poolR.transform.position = G(P) + Vector3.down * 0.3f * sinkK; }
            // the slabs fall in (0.18 s), heave slowly in the mud, sink when it dries
            for (int i = 0; i < _slabs.Count; i++)
            {
                if (_slabs[i] == null) continue;
                float bob = Mathf.Sin(t * 2.2f + i) * 0.025f * (1f - dryK);
                Vector3 rest = _slabRest[i] + Vector3.up * bob + Vector3.down * (0.06f * Vol2Fs.Smooth(0.2f, Dur, t) + 0.35f * sinkK);
                _slabs[i].position = Vector3.Lerp(rest + Vector3.up * 1.4f, rest, fall * fall);
                _slabs[i].rotation = _slabRot[i] * Quaternion.Euler(Mathf.Sin(t * 1.7f + i * 2f) * 4f * (1f - dryK), 0f, 0f);
            }
            // r3: the mud sheet - filled annuli out to 2.4 R, the swirl texture scrolling (churning), alive for the slow (Dur), fading as it dries
            _sheet.Clear();
            float mudLife = Vol2Fs.Smooth(0.02f, 0.25f, t) * (1f - Vol2Fs.Smooth(Dur, Dur + 0.9f, t));
            float sheetR = Radius * 2.4f * Vol2Fs.Smooth(0.02f, 0.3f, t);
            if (mudLife > 0.01f) for (int k = 0; k < 6; k++) { float w0 = sheetR / 6f; Vol2FsB4A.GroundArc(_sheet, P, Ground + 0.035f + 0.002f * k, w0 * (k + 0.5f), w0 * 1.08f, t * 0.35f * (k % 2 == 0 ? 1f : -1f), Mathf.PI * 2f, mudLife * (k < 5 ? 1f : 0.6f), 0.13f * k, cam, 36); }
            _sheet.Write(_sheetMesh);
            // hollow currents: lighter wet ridges spiralling slowly in the mud (gone as it dries)
            _swirl.Clear();
            float live = Vol2Fs.Smooth(0.05f, 0.3f, t) * (1f - dryK);
            if (live > 0.01f)
                for (int arm = 0; arm < 4; arm++)
                {
                    int n = 18; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var side = new Vector3[n];
                    for (int k = 0; k < n; k++)
                    {
                        float u = k / (float)(n - 1), r = Radius * Mathf.Lerp(0.3f, 2.3f, u), a = arm * 1.571f + t * 0.9f + u * 3.2f;
                        Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        pts[k] = G(P) + d * r + Vector3.up * 0.06f; w[k] = 0.16f * Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) + 0.03f; al[k] = live; side[k] = d;
                    }
                    _swirl.Strip(pts, w, side, Color.white, al, 0.2f + arm * 0.3f, cam, 0f, 2f);
                }
            _swirl.Write(_swirlMesh);
            // ropes of mud curling up each body's legs for the slow, sliding down after it
            _ropes.Tubes.Clear();
            float grip = Vol2Fs.Smooth(0.06f, 0.36f, t) * (1f - Vol2Fs.Smooth(Dur - 0.1f, Dur + 0.5f, t));
            for (int si = 0; si < Bodies.Count; si++)
            {
                var s = Bodies[si];
                if (!Vol2FsRxBreakKit.Now(s, out var b, out var feet)) { if (si < _sinks.Count && _sinks[si] != null) _sinks[si].gameObject.SetActive(false); continue; }
                if (si < _sinks.Count && _sinks[si] != null)
                {
                    float e = Mathf.Max(b.extents.x, b.extents.z) + 0.8f, k = Vol2Fs.Smooth(0.04f, 0.3f, t) * (1f - sinkK);
                    _sinks[si].position = G(feet) + Vector3.up * 0.012f; _sinks[si].localScale = new Vector3(Mathf.Max(0.01f, e * k), 1f, Mathf.Max(0.01f, e * k));
                }
                if (grip <= 0.01f) continue;
                for (int k = 0; k < 5; k++)
                {
                    var tb = _ropes.Add(Vol2FsB4F.Coil(b, Ground - 0.05f, 10, k * 1.257f + s.Index + t * 0.3f, 1.2f, 0.66f, 0.08f), 0.16f, 0.06f, 20);
                    tb.Grow = grip; tb.Knots = 0.5f; tb.Sides = 6; tb.Sharp = 0.2f;
                }
                if (grip > 0.3f && Random.value < 0.6f) Vol2FsB1.EmitV(_ropeDrip, new Vector3(b.center.x, b.min.y + b.size.y * 0.5f, b.center.z) + Vol2FsRxBreakKit.Flat(Random.insideUnitSphere) * (Mathf.Max(b.extents.x, b.extents.z) + 0.05f), Vector3.zero, -1f, -1f);
            }
            _ropes.Write(_ropeMesh);
            // the low splash of ink and mud at the fall; drying cracks at the end
            _ink.Clear(); Vol2FsB1.DrawBurst(_ink, _burst, G(P) + Vector3.up * 0.2f, t, cam, 0.15f, 0.12f); _ink.Write(_inkMesh);
            _cracks.Clear();
            float crack = dryK * (1f - sinkK);
            if (crack > 0.01f)
                for (int k = 0; k < 7; k++)
                {
                    float a = k * 0.9f + Vol2Fs.Hash(Seed, k + 300) * 0.6f;
                    Vector3 d = Dir * Mathf.Cos(a) + Side * Mathf.Sin(a);
                    Vol2FsB6.Crack(_cracks, G(P) + d * Radius * 0.2f, G(P) + d * Radius * 1.4f, Ground + 0.03f, 0.07f, 0f, dryK, crack, 0.3f + k * 0.1f, Seed + k, 0.12f, 0.03f);
                }
            _cracks.Write(_crackMesh);
        }
    }
}
