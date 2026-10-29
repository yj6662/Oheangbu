using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>온 (water, ㅗ fan, 번진다) - free showcase batch 4, concept on_a (spilling flood: the swing floods the fan with a sheet of clear water, black ink
    /// swirling in it; at its edges the water SPILLS OVER in fast rivulets running along the ground to the bears outside and splashes up over them; puddles at
    /// their feet). A flat flood that spills - not 오's curling wave, not 옥's marsh of pillars. Beats, all executor facts: charge -> the launch = the sheet races
    /// out over the fan (the spec's apex / depth / half angle) with a foaming front, the body inside is drenched (Instant) -> when the fan has finished the
    /// executor spreads from its edge: every "Spread" contact = one rivulet running from hop.FromP to that neighbour (its width by the share), a splash crown
    /// over it -> its Mark (3 s) = a rippling puddle at the feet, the body dripping; warning = the puddle shrinks -> natural expiry (CSV: 아래로 흘러내림) = the
    /// water runs down off the body and drains into the ground; other reasons = shorter exits.</summary>
    public sealed class Vol2FsOn : Vol2FsSpread
    {
        static readonly Color Aqua = new Color(0.45f, 0.85f, 0.9f), Ink = new Color(0.02f, 0.03f, 0.04f);
        ParticleSystem _charge, _spray, _drips, _mist, _foam; Light _chargeLight, _sheetLight;
        Mesh _sheetMesh, _inkMesh, _frontMesh, _riverMesh, _crownMesh, _puddleMesh, _wetMesh;
        readonly Vol2FsStrips _sheet = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _front = new Vol2FsStrips(), _river = new Vol2FsStrips(), _crown = new Vol2FsStrips(), _puddle = new Vol2FsStrips(), _wet = new Vol2FsStrips();
        struct InkCurl { public float V, U0, U1, W, Rnd, Bend; }
        readonly List<InkCurl> _curls = new List<InkCurl>();
        readonly List<(Vector3 At, float T, float K)> _crowns = new List<(Vector3, float, float)>();

        protected override float HopSpeed => 8f;
        protected override float HopMin => 0.3f;
        protected override float HopMax => 0.5f;
        protected override float HopStagger => 0.12f;
        protected override float MarkExpireSeconds => 1.0f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _charge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Aqua, 2.5f, 0.8f)), Rate = 80f, Loop = true, Life0 = 0.2f, Life1 = 0.35f, Speed0 = -1.6f, Speed1 = -2.6f, Size0 = 0.05f, Size1 = 0.12f, Radius = 0.6f, World = false, Max = 100 }, root, "Charge");
            _chargeLight = Vol2Fs.PointLight(_charge.transform, "ChargeLight", Aqua, 3f);
            _charge.transform.position = Tip(); _charge.Play(true);
            var spray = M("fs_splash", new Color(0.85f, 0.95f, 1f, 1f), 1.3f, 0.3f); spray.NoiseAmt = 0.4f; spray.Soft = 0.1f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spray), Life0 = 0.35f, Life1 = 0.7f, Speed0 = 1.5f, Speed1 = 3.5f, Size0 = 0.07f, Size1 = 0.18f, Shape = ParticleSystemShapeType.Cone, Angle = 30f, Radius = 0.2f,
                ShapeRot = new Vector3(-90f, 0f, 0f), Gravity = 2.2f, Tiles = 2, Spin = 200f, AlphaHoldTo = 0.6f, Max = 1500 }, root, "Spray");
            var drip = M("fs_dot", new Color(0.7f, 0.9f, 1f, 1f), 1.6f, 0.4f); drip.Soft = 0.05f;
            _drips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drip), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.03f, Size1 = 0.06f, Radius = 0.3f, Gravity = 1.6f, Stretch = true, StretchVel = 0.06f, Max = 1200 }, root, "Drips");
            var mist = M("fs_puff", new Color(0.8f, 0.88f, 0.92f, 1f), 1f, 0.1f); mist.Opacity = 0.3f; mist.NoiseAmt = 0.6f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 0.3f, Speed1 = 1f, Size0 = 0.6f, Size1 = 1.2f, Radius = 0.3f, Drag = 1f, Tiles = 2, SizeEnd = 1.6f, Max = 200, SortFudge = 8 }, root, "Mist");
            var foam = M("fs_puff", new Color(0.95f, 0.98f, 1f, 1f), 1.1f, 0.2f); foam.Opacity = 0.7f; foam.NoiseAmt = 0.6f; foam.Erode = 0.45f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.18f, Size1 = 0.4f, Radius = 0.1f, Tiles = 2, SizeEnd = 1.4f, Max = 900 }, root, "Foam");
            foreach (var ps in new[] { _spray, _drips, _mist, _foam }) ps.Play(false);
            // r2: the older, clearer particle-shader water (r1 with WaterDeep + tall splash strips read as ICE: a row of icicles)
            Vol2Fs.MeshObject(root, "FloodSheet", Vol2FsB1.Water(Kit, 1.3f, 0.4f, "fs_water"), out _sheetMesh);
            Vol2Fs.MeshObject(root, "InkInWater", Kit.InkSolid("fs_stroke2", Ink, new Color(0.35f, 0.75f, 0.85f, 1f), 0.08f, 0.5f, 0.35f, 1.4f), out _inkMesh);
            Vol2Fs.MeshObject(root, "RollingCrest", Vol2FsB1.Water(Kit, 1.5f, 0.35f, "fs_water"), out _frontMesh);
            Vol2Fs.MeshObject(root, "Rivulets", Vol2FsB1.Water(Kit, 1.4f, 0.4f, "fs_water"), out _riverMesh);
            Vol2Fs.MeshObject(root, "SplashSkirts", Vol2FsB1.Water(Kit, 1.4f, 0.35f, "fs_water"), out _crownMesh);
            var pud = M("fs_ring", new Color(0.7f, 0.9f, 1f, 1f), 1.3f, 0.35f); pud.Soft = 0.001f; pud.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "Puddles", Kit.Mat(pud), out _puddleMesh);
            var wet = M("fs_wet", new Color(0.12f, 0.08f, 0.07f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.NoiseAmt = 0.35f; wet.Opacity = 0.75f;
            Vol2Fs.MeshObject(root, "Wet", Kit.Mat(wet), out _wetMesh);
            _sheetLight = Vol2Fs.PointLight(root, "SheetLight", Aqua, 6f);
        }

        protected override void OnCharge(float t, float k) { _charge.transform.position = Tip(); _chargeLight.intensity = 3f * k; }
        protected override void OnLaunch(long nowUs) { _charge.Stop(true, ParticleSystemStopBehavior.StopEmitting); _chargeLight.intensity = 0f; }

        protected override void OnHit(long nowUs)
        {
            int seed = Seed + 67;
            for (int k = 0; k < 8; k++) _curls.Add(new InkCurl { V = (k / 7f) * 2f - 1f + (Vol2Fs.Hash(seed, k) - 0.5f) * 0.15f, U0 = 0.3f + 0.15f * Vol2Fs.Hash(seed, k + 10), U1 = 0.7f + 0.3f * Vol2Fs.Hash(seed, k + 20), W = 0.22f + 0.15f * Vol2Fs.Hash(seed, k + 30), Rnd = Vol2Fs.Hash(seed, k + 40), Bend = (Vol2Fs.Hash(seed, k + 50) - 0.5f) * 1.6f });
            _sheetLight.transform.position = Vol2FsB3.InFan(Apex, F, Depth, Half, 0.65f, 0f, Ground) + Vector3.up * 1f;
        }

        protected override void OnStruck(Struck s)
        {
            _crowns.Add((G(FeetOf(s)), s.At, 1f));
            Vol2FsB1.Emit(_spray, G(FeetOf(s)) + Vector3.up * 0.2f, 25); Vol2FsB1.Emit(_mist, CentreNow(s), 3);
        }

        protected override void OnLand(Hop h)
        {
            float k = ShareScale(h.Share);
            _crowns.Add((G(FeetOf(h)), h.Start + h.Travel, k));
            Vol2FsB1.Emit(_spray, G(FeetOf(h)) + Vector3.up * 0.2f, Mathf.RoundToInt(30 * k)); Vol2FsB1.Emit(_mist, CentreNow(h), 3); Vol2FsB1.Emit(_foam, G(FeetOf(h)) + Vector3.up * 0.1f, 20);
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            if (!Natural(m)) { Vol2FsB1.Emit(_spray, m.Centre + Vector3.up * 0.2f, 15); Vol2FsB1.Emit(_mist, m.Centre + Vector3.up * 0.3f, 2); }
        }

        /// <summary>r2: a low rounded SKIRT of water thrown up round the feet that rolls outward and slumps (r1's ten tall spikes read as icicles).</summary>
        void Crown(Vector3 at, float a, float k, Vector3 cam, float rnd)
        {
            if (a < 0f || a > 0.9f) return;
            float grow = Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.2f)), life = 1f - Vol2Fs.Smooth(0.35f, 0.9f, a), slump = Vol2Fs.Smooth(0.2f, 0.9f, a);
            int n = 28;
            var pts = new Vector3[n + 1]; var w = new float[n + 1]; var al = new float[n + 1]; var side = new Vector3[n + 1];
            float r = (0.45f + 0.6f * Mathf.Clamp01(a / 0.5f)) * k, hgt = 0.55f * k * grow * (1f - 0.75f * slump);
            for (int j = 0; j <= n; j++)
            {
                float ang = j / (float)n * Mathf.PI * 2f; Vector3 o = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float wob = 1f + 0.18f * Mathf.Sin(ang * 5f + rnd * 9f);
                Vector3 lean = (o * (0.45f + 0.6f * slump) + Vector3.up).normalized;
                pts[j] = at + o * r * wob + lean * hgt * wob * 0.5f + Vector3.up * 0.03f; w[j] = hgt * wob; al[j] = life; side[j] = lean;
            }
            _crown.Strip(pts, w, side, Color.white, al, rnd, cam, 0f, 3f);
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            // the flood: a sheet racing out over the fan (0.3 s), standing, then draining (recedes to wet ground)
            float reach = Vol2Fs.Smooth(0f, 0.3f, h);
            float drain = Vol2Fs.Smooth(1.0f, 1.9f, h);
            _sheet.Clear(); _ink.Clear(); _front.Clear(); _wet.Clear();
            Vol2FsB4F.FanCarpet(_sheet, Apex, F, Depth, Half, Ground, Mathf.Lerp(0.25f, 1.02f, reach), 1f - drain, 0.29f, cam, 0.05f + 0.04f * (1f - drain), 0.25f, 1.05f, 2.5f);
            Vol2FsB4F.FanCarpet(_wet, Apex, F, Depth, Half, Ground, Mathf.Lerp(0.25f, 1.02f, reach), Vol2Fs.Smooth(0.2f, 0.6f, h) * (1f - Vol2Fs.Smooth(2.2f, 3.4f, h)), 0.61f, cam, 0.025f);
            // the foaming front arc while it runs out
            if (reach < 1f || h < 0.45f)
            {
                // r2: a LOW rounded rolling crest leaning forward (r1: a camera-facing splash band read as a row of icicles), foam riding on it
                var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>(); var sd = new List<Vector3>();
                float r = Depth * Mathf.Lerp(0.3f, 1.02f, reach), fl = 1f - Vol2Fs.Smooth(0.3f, 0.55f, h);
                for (int k = 0; k <= 20; k++)
                {
                    float v = (k / 20f) * 2f - 1f; Vector3 d = Dir(v * Half * 1.05f);
                    float hh = 0.22f + 0.06f * Mathf.Sin(k * 1.3f + t * 9f);
                    Vector3 lean = (d * 0.8f + Vector3.up * 0.6f).normalized;
                    var p = Apex + d * r + lean * hh * 0.5f; p.y += Ground - Apex.y + 0.02f;
                    pts.Add(p); w.Add(hh * 1.6f); al.Add(fl); sd.Add(lean);
                }
                _front.Strip(pts, w, sd, Color.white, al, 0.47f, cam, 0f, 3f);
                for (int j = 0; j < 2; j++) { var p = Apex + Dir(Random.Range(-1f, 1f) * Half) * r; p.y = Ground + 0.15f; Vol2FsB1.Emit(_foam, p, 1); if (Random.value < 0.4f) Vol2FsB1.Emit(_spray, p, 1); }
            }
            // ink swirling in the water
            var ip = new Vector3[8]; var iw = new float[8]; var ia = new float[8]; var isd = new Vector3[8];
            foreach (var c in _curls)
            {
                float u1 = Mathf.Min(c.U1, Mathf.Lerp(0.25f, 1f, reach)); if (u1 <= c.U0 + 0.02f) continue;
                for (int k = 0; k < 8; k++)
                {
                    float v = k / 7f, u = Mathf.Lerp(c.U0, u1, v), a = c.V * Half + c.Bend * v * v * 0.6f + Mathf.Sin(t * 1.3f + c.Rnd * 6f) * 0.05f;
                    var p = Apex + Dir(a) * Depth * u; p.y = Ground + 0.07f;
                    ip[k] = p; iw[k] = c.W * Mathf.Sin(Mathf.Clamp01(v * 0.9f + 0.1f) * Mathf.PI); ia[k] = (1f - drain) * 0.9f; isd[k] = Dir(a);
                }
                _ink.Strip(ip, iw, isd, Color.white, ia, c.Rnd, cam, 0f, 1f);
            }
            _sheetLight.intensity = (h < 0.2f ? 4f * h / 0.2f : 2.5f) * (1f - drain);
            // the rivulets spilling over the edge to the neighbours (rushing heads with foam), standing as wet runnels while the mark lives
            _river.Clear();
            foreach (var hop in Hops)
            {
                if (t < hop.Start) continue;
                float g = HopK(hop, t), k = ShareScale(hop.Share), ex = hop.Mark != null ? MarkExit(hop.Mark, t) : 0f;
                float hold = 1f - Vol2Fs.Smooth(hop.Start + hop.Travel + 0.5f, hop.Start + hop.Travel + 1.4f, t);
                Vector3 b = G(FeetOf(hop));
                for (int r = 0; r < 2; r++)
                    Vol2FsB4.Runner(_river, hop.FromP, b, Ground, Mathf.Max(0f, g - 1.2f), g, (0.55f - 0.2f * r) * k, Mathf.Max(0.25f, hold) * (1f - ex), 0.4f + r * 0.3f + hop.Order * 0.1f, cam, 0.3f + 0.15f * r, hop.Order * 2f + r, 16, 0.06f + 0.02f * r);
                Vol2FsB4.Runner(_wet, hop.FromP, b, Ground, 0f, g, 0.9f * k, 0.8f * (1f - Vol2Fs.Smooth(0f, 1f, ex)), 0.7f + hop.Order * 0.1f, cam, 0.25f, hop.Order * 2f, 12, 0.025f);
                if (g < 1f) { Vector3 head = Vector3.Lerp(hop.FromP, b, g); if (Time.frameCount % 2 == 0) Vol2FsB1.Emit(_spray, head, 1); Vol2FsB1.Emit(_foam, head + Vector3.up * 0.06f, 2); }
            }
            _river.Write(_riverMesh); _sheet.Write(_sheetMesh); _ink.Write(_inkMesh); _front.Write(_frontMesh);
            // the splash crowns
            _crown.Clear();
            for (int i = 0; i < _crowns.Count; i++) Crown(_crowns[i].At, t - _crowns[i].T, _crowns[i].K, cam, 0.17f * i);
            _crown.Write(_crownMesh);
            // the drenched bodies drip (the 4 s wet is the state sink's; the instant's mirror)
            foreach (var s in Direct) if (t - s.At < 4f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_drips, CentreNow(s) + Vector3.up * 0.2f, 2);
            // the marks: a rippling puddle at the feet; warning = it shrinks; natural expiry = the water runs down off the body and drains away
            _puddle.Clear();
            foreach (var m in MarkList)
            {
                float age = MarkAge(m, t); if (age < 0f) continue;
                float life = MarkLife(m, t, 0.25f), warn = MarkWarn(m, t), ex = MarkExit(m, t), k = ShareScale(m.Share);
                float size = m.Radius * 3f * k * (1f - 0.35f * warn) * (Natural(m) ? 1f - 0.6f * ex : 1f);
                if (life > 0.01f)
                {
                    _wet.Quad(m.Centre + Vector3.up * 0.02f, Right, Fwd, size * 1.15f, new Color(1f, 1f, 1f, Mathf.Min(1f, life + 0.4f * (1f - ex))), 0.3f + m.Id % 5 * 0.1f);
                    for (int r = 0; r < 3; r++)
                    {
                        float ph = Mathf.Repeat(age * 0.9f + r / 3f, 1f);
                        _puddle.Quad(m.Centre + Vector3.up * (0.035f + 0.003f * r), Right, Fwd, size * (0.3f + 0.75f * ph), new Color(1f, 1f, 1f, life * (1f - ph)), 0.2f * r);
                    }
                }
                Vector3 c = m.Hop != null ? CentreNow(m.Hop) : m.Centre + Vector3.up * 0.6f;
                if (m.EndAt < 0f && Time.frameCount % 3 == 0) Vol2FsB1.Emit(_drips, c + Vector3.up * 0.2f, 1);
                if (m.EndAt >= 0f && Natural(m) && ex < 0.8f) { Vol2FsB1.Emit(_drips, c + Random.insideUnitSphere * 0.35f, 3); if (Time.frameCount % 4 == 0) Vol2FsB1.Emit(_foam, m.Centre + Vector3.up * 0.05f, 1); }
            }
            _puddle.Write(_puddleMesh); _wet.Write(_wetMesh);
        }
    }
}
