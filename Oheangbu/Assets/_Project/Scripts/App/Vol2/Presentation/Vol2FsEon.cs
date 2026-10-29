using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>언 (water, ㅓ attachment, 번진다) - free showcase batch 4, concept eon_b (the brush drips with water; the swing hits the close bear and a cluster of big
    /// wobbling water bubbles - black ink curling inside them - bursts off it and drifts to the other bears, popping over them and drenching them; rippling puddles).
    /// Unlike 어 (a whirlpool disc guard) and 억 (a water whip, 남긴다) the water here travels as BUBBLES. Beats, all executor facts: the attachment host's 6 s = water
    /// winding round the brush and the sleeve, small bubbles budding at the brush head (HostLife; dripping off in its last 0.4 s) + a sheet of spray along the real
    /// swing -> the "Instant" touch = a splash crown, the body streaming -> every "Spread" contact = a cluster of ink-filled bubbles drifting on a soft arc from that
    /// body to the neighbour (the big bubble's size by the share) -> they pop over it (splash, spray) -> the Mark (3 s) = a rippling puddle at its feet and the body
    /// dripping; warning = the ripples still -> natural expiry (CSV: 아래로 흘러내림) = the water runs down off the body and the puddle drains into the ground.</summary>
    public sealed class Vol2FsEon : Vol2FsSpread
    {
        static readonly Color Ink = new Color(0.02f, 0.03f, 0.04f), Foam = new Color(0.9f, 0.97f, 1f), Teal = new Color(0.4f, 0.75f, 0.85f);
        ParticleSystem _spray, _drops, _drip, _mist, _foam; Light _waterLight;
        Material _water; Mesh _ribbonMesh, _swirlMesh, _puddleMesh, _ringMesh;
        readonly Vol2FsStrips _ribbon = new Vol2FsStrips(), _swirl = new Vol2FsStrips(), _puddle = new Vol2FsStrips(), _rings = new Vol2FsStrips();
        sealed class Bubble { public Mesh Mesh; public MeshRenderer R; public float Size, Phase; public Vector3 Off; }
        sealed class Cluster { public List<Bubble> B = new List<Bubble>(); public Vector3 A, P; public bool Popped; }
        readonly List<Bubble> _buds = new List<Bubble>();
        // r3 (Phantom Blade Zero reference, principles 2 + 5): the swing as a blade-edged crescent + two phantom afterimages in the body's accent colour
        Mesh _phantomMesh; readonly Vol2FsStrips _phantom = new Vol2FsStrips(); float _lastFast = -9f;
        protected override float HopSpeed => 5f;
        protected override float HopMin => 0.5f;
        protected override float HopMax => 0.8f;
        protected override float HopStagger => 0.14f;
        protected override float MarkExpireSeconds => 1.0f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            // r2: CLEAR water for the bubbles (r1 WaterDeep read as frosted ice balls): little erosion / noise, half additive, the body made see-through per vertex (Clear)
            var clear = M("fs_water", Color.white, 1.25f, 0.55f); clear.Ramp = "fs_ramp_water"; clear.RampScale = 1.5f; clear.NoiseAmt = 0.15f; clear.NoiseScale = 1.2f; clear.Erode = 0.08f; clear.Soft = 0.1f;
            clear.Scroll = new Vector2(-0.5f, 0.2f); clear.Edge = new Color(0.9f, 1f, 1f, 1f) * 0.6f; clear.EdgeWidth = 0.1f;
            _water = Kit.Mat(clear);
            var spray = M("fs_splash", Foam, 1.4f, 0.3f); spray.Ramp = "fs_ramp_water"; spray.RampScale = 1.5f; spray.NoiseAmt = 0.4f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(spray), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.25f, Size1 = 0.6f, Radius = 0.25f, Gravity = 1.6f, Drag = 0.6f, Tiles = 2, SizeEnd = 1.4f, Max = 600 }, root, "Spray");
            var drop = M("fs_dot", Teal, 1.6f, 0.5f);
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.5f, Life1 = 1.1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.04f, Size1 = 0.1f, Radius = 0.2f, Gravity = 2.4f, Stretch = true, StretchVel = 0.04f, Max = 1500 }, root, "Drops");
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(drop), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.03f, Size1 = 0.06f, Radius = 0.05f, Gravity = 2.6f, Stretch = true, StretchVel = 0.06f, Max = 1200 }, root, "Drip");
            var mist = M("fs_puff", new Color(0.85f, 0.92f, 0.95f, 1f), 1f, 0f); mist.Opacity = 0.35f; mist.NoiseAmt = 0.6f; mist.Erode = 0.4f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.8f, Life1 = 1.4f, Speed0 = 0.3f, Speed1 = 1f, Size0 = 0.8f, Size1 = 1.4f, Radius = 0.4f, Drag = 1.5f, Tiles = 2, SizeEnd = 1.8f, Max = 80, SortFudge = 10 }, root, "Mist");
            var foam = M("fs_splash", Foam, 1.2f, 0.2f); foam.NoiseAmt = 0.5f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.3f, Life1 = 0.5f, Speed0 = 0f, Speed1 = 0f, Size0 = 1.6f, Size1 = 2.2f, Radius = 0.05f, SizeStart = 0.3f, SizePeak = 1f, SizePeakAt = 0.4f, SizeEnd = 1.2f, Tiles = 2, Max = 20, SortFudge = -5 }, root, "Pop");
            foreach (var ps in new[] { _spray, _drops, _drip, _mist, _foam }) ps.Play(false);
            Vol2Fs.MeshObject(root, "WaterRibbons", Vol2FsB1.Water(Kit, 1.5f, 0.4f, "fs_water"), out _ribbonMesh);      // r2: the older, clearer water (flows along u)
            Vol2Fs.MeshObject(root, "InkSwirls", Kit.InkSolid("fs_stroke", Ink, new Color(0.3f, 0.6f, 0.8f, 1f), 0.1f, 0.45f, 0.3f, 1.4f), out _swirlMesh);
            var wet = M("fs_wet", new Color(0.18f, 0.2f, 0.22f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.NoiseAmt = 0.35f; wet.Opacity = 0.8f; wet.Edge = new Color(0.5f, 0.75f, 0.85f, 1f); wet.EdgeWidth = 0.08f;
            Vol2Fs.MeshObject(root, "Puddles", Kit.Mat(wet), out _puddleMesh);
            var ring = M("fs_ring", Foam, 1.2f, 0.3f); ring.NoiseAmt = 0.3f; ring.Soft = 0.02f;
            Vol2Fs.MeshObject(root, "Ripples", Kit.Mat(ring), out _ringMesh);
            _waterLight = Vol2Fs.PointLight(root, "WaterLight", Teal, 3f);
            Vol2Fs.MeshObject(root, "Phantoms", Vol2FsB4A.PhantomMat(Kit, new Color(0.25f, 0.6f, 1f)), out _phantomMesh);
            for (int i = 0; i < 3; i++) _buds.Add(NewBubble("Bud" + i, 0.12f + 0.04f * i, i * 2.1f));
        }

        Bubble NewBubble(string name, float size, float phase)
        {
            var b = new Bubble { Size = size, Phase = phase };
            b.R = Vol2Fs.MeshObject(Root.transform, name, _water, out b.Mesh);
            return b;
        }

        void DrawBubble(Bubble b, Vector3 c, float size, float life, float t, Vector3 cam)
        {
            if (life <= 0.01f || size <= 0.01f) { b.Mesh.Clear(); return; }
            float ph = b.Phase + t * 7f;
            Vol2FsB1.WaterSphere(b.Mesh, c, size, -1f, cam, life, d => new Vector3(d.x * (1f + 0.08f * Mathf.Sin(ph + d.y * 3f)), d.y * (1f + 0.08f * Mathf.Cos(ph * 1.3f + d.x * 3f)), d.z * (1f + 0.08f * Mathf.Sin(ph * 0.8f + d.z * 4f))), 20, 12, 1.8f);
            Clear(b.Mesh, life);
            // the ink curling inside: a short spiral stroke, turning
            const int n = 10; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
            for (int k = 0; k < n; k++)
            {
                float u = k / (n - 1f), a = u * Mathf.PI * 2.4f + t * 3f + b.Phase;
                pts[k] = c + new Vector3(Mathf.Cos(a) * size * 0.5f * (1f - 0.5f * u), (u - 0.5f) * size * 0.9f, Mathf.Sin(a) * size * 0.5f * (1f - 0.5f * u));
                w[k] = size * 0.36f * Mathf.Sin(u * Mathf.PI); al[k] = life;
            }
            _swirl.Strip(pts, w, null, Color.white, al, b.Phase * 0.1f, cam, 0f, 1f);
        }

        /// <summary>A bubble: a thin bright rim and a see-through body - WaterSphere writes rgb = lerp(0.55, rim 1.8, fresnel^2); the alpha is remapped to the rim.</summary>
        static void Clear(Mesh m, float life)
        {
            var cs = m.colors;
            for (int i = 0; i < cs.Length; i++) { float f2 = Mathf.Clamp01((cs[i].r - 0.55f) / 1.25f); float a = life * (0.05f + 0.95f * f2 * f2 * (3f - 2f * f2)); cs[i] = new Color(0.8f + 1.0f * f2, 0.8f + 1.0f * f2, 0.8f + 1.0f * f2, a); }
            m.colors = cs;
        }

        protected override void OnCharge(float t, float k) { if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_drip, Tip(), 1); }
        protected override void OnLaunch(long nowUs) { Vol2FsB1.Emit(_spray, Tip(), 10); }
        protected override void OnHit(long nowUs) { }

        void Splash(Vector3 c, Vector3 feet, float k)
        {
            Vol2FsB1.Emit(_spray, c, Mathf.RoundToInt(60 * k)); Vol2FsB1.Emit(_drops, c, Mathf.RoundToInt(160 * k)); Vol2FsB1.Emit(_mist, c, 3); Vol2FsB1.Emit(_foam, c, 1);
            _waterLight.transform.position = c; _waterLight.intensity = 3f;
        }

        protected override void OnStruck(Struck s) { Splash(CentreNow(s), FeetOf(s), s.Kind == "Second" ? 0.5f : 1f); }

        protected override void OnHop(Hop h)
        {
            var cl = new Cluster { A = h.FromP + Vector3.up * 1.1f };
            float k = ShareScale(h.Share);
            for (int i = 0; i < 3; i++)
            {
                var b = NewBubble("Bubble_" + h.TargetId + "_" + i, (i == 0 ? 0.55f : 0.24f + 0.08f * i) * k, i * 1.7f + h.Order);
                b.Off = i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(i * 2.3f), 0.4f * (i - 1.5f), Mathf.Sin(i * 2.3f)) * 0.7f * k;
                cl.B.Add(b);
            }
            h.Data = cl;
        }

        protected override void OnLand(Hop h)
        {
            if (!(h.Data is Cluster cl)) return;
            cl.Popped = true; foreach (var b in cl.B) b.Mesh.Clear();
            Splash(CentreNow(h), FeetOf(h), ShareScale(h.Share));
        }

        protected override void OnMarkEnd(Mark m)
        {
            if (m.Why == Vol2MaterialEnd.Aborted) return;
            if (!Natural(m)) { Vol2FsB1.Emit(_mist, m.Centre + Vector3.up * 0.3f, 3); Vol2FsB1.Emit(_drops, m.Centre + Vector3.up * 0.3f, 40); }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _ribbon.Clear(); _swirl.Clear(); _puddle.Clear(); _rings.Clear(); _phantom.Clear();
            _waterLight.intensity = Mathf.MoveTowards(_waterLight.intensity, 0f, Time.deltaTime * 8f);
            // the dress: water winding round the brush shaft and the sleeve, small bubbles budding at the head; drips off in the warning, runs down at the end
            float dress = HostLife(t, 0.25f, 0.5f), warn = HostWarn(t);
            Vector3 axis = ShaftAxis; Vector3 n1 = Vector3.Cross(axis, Vector3.up); if (n1.sqrMagnitude < 1e-4f) n1 = Right; n1.Normalize(); Vector3 n2 = Vector3.Cross(axis, n1);
            if (dress > 0.01f)
            {
                Vector3 g = Grip(), tp = Tip(), sl = Sleeve();
                for (int v = 0; v < 2; v++)
                {
                    var pts = new Vector3[16]; var w = new float[16]; var al = new float[16];
                    for (int j = 0; j < 16; j++)
                    {
                        float u = j / 15f;
                        Vector3 on = u < 0.25f ? Vector3.Lerp(sl, g, u / 0.25f) : Vector3.Lerp(g, tp + axis * 0.1f, (u - 0.25f) / 0.75f);
                        float side = (v == 0 ? 1f : -1f) * (0.045f + 0.02f * Mathf.Sin(u * 9f + t * 8f + v));
                        pts[j] = on + n1 * side + Vector3.down * (0.03f + 0.1f * warn * u);
                        w[j] = (v == 0 ? 0.13f : 0.08f) * (0.55f + 0.45f * Mathf.Sin(u * Mathf.PI)); al[j] = dress * (1f - 0.4f * warn);
                    }
                    _ribbon.Strip(pts, w, null, Color.white, al, 0.2f + v * 0.3f, cam, 0f, 1f);
                }
                if (Time.frameCount % (warn > 0f ? 1 : 3) == 0) Vol2FsB1.Emit(_drip, Shaft(Random.Range(0.2f, 1f)), 1);
                for (int i = 0; i < _buds.Count; i++)
                {
                    float a = t * 2.2f + i * 2.1f;
                    Vector3 c = tp + axis * 0.1f + (n1 * Mathf.Cos(a) + n2 * Mathf.Sin(a)) * 0.22f;
                    DrawBubble(_buds[i], c, _buds[i].Size * dress * (1f - warn), dress, t, cam);
                }
                if (TipSpeed() > 3f) _lastFast = t;
                if (t - _lastFast < 0.12f) { Vol2FsB4A.BladeTrail(_ribbon, TipPath, t, 0.22f, 0.45f, dress, 0.8f, cam); Vol2FsB4A.Phantoms(_phantom, TipPath, t, 0.22f, 0.45f, dress, cam); }
            }
            else foreach (var b in _buds) b.Mesh.Clear();
            // the bubble clusters drifting from the struck body to the neighbour
            foreach (var hop in Hops)
            {
                if (!(hop.Data is Cluster cl)) continue;
                if (cl.Popped || t < hop.Start) { if (!cl.Popped) foreach (var b in cl.B) b.Mesh.Clear(); continue; }
                float g = HopK(hop, t); float e = g * g * (3f - 2f * g);
                Vector3 bpos = CentreNow(hop) + Vector3.up * 0.3f;
                Vector3 c = Vol2FsB4.ArcPoint(cl.A, bpos, 0.9f, e);
                float grow = Vol2Fs.Smooth(0f, 0.12f, t - hop.Start);
                for (int i = 0; i < cl.B.Count; i++)
                {
                    var b = cl.B[i]; float lag = i * 0.06f;
                    Vector3 ci = Vol2FsB4.ArcPoint(cl.A, bpos, 0.9f, Mathf.Clamp01(e - lag)) + b.Off * (1f - e * 0.6f) + Vector3.up * Mathf.Sin(t * 5f + b.Phase) * 0.06f;
                    DrawBubble(b, ci, b.Size * grow, 1f, t, cam);
                }
                if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_drip, c, 1);
            }
            // the struck body streams for a moment
            foreach (var s in Direct) if (t - s.At < 1.5f && Time.frameCount % 2 == 0) Vol2FsB1.Emit(_drip, CentreNow(s) + Random.insideUnitSphere * 0.4f, 2);
            // the marks: a rippling puddle and the body dripping; ripples still in the warning; natural expiry runs down and drains
            foreach (var m in MarkList)
            {
                float age = MarkAge(m, t); if (age < 0f) continue;
                float ex = MarkExit(m, t), warn2 = MarkWarn(m, t), k = ShareScale(m.Share);
                bool natural = Natural(m) && m.EndAt >= 0f;
                float size = m.Radius * 3.2f * k * Vol2Fs.Smooth(0f, 0.3f, age) * (natural ? 1f - 0.6f * ex : 1f);
                float life = (natural ? 1f - Vol2Fs.Smooth(0.3f, 1f, ex) : 1f - ex);
                if (life > 0.01f) _puddle.Quad(m.Centre + Vector3.up * 0.02f, Right, Fwd, size, new Color(1f, 1f, 1f, life), 0.3f + m.Id % 7 * 0.1f);
                // ripples: rings growing out from the feet (slower / fewer in the warning, none after the end)
                if (m.EndAt < 0f)
                {
                    float speed = 1f - 0.7f * warn2;
                    for (int r = 0; r < 3; r++)
                    {
                        float ph = Mathf.Repeat(age * 0.8f * speed + r / 3f, 1f);
                        Vol2FsB4A.GroundArc(_rings, m.Centre, Ground + 0.01f, (0.2f + ph * 1.1f) * k, 0.05f, 0f, Mathf.PI * 2f, (1f - ph) * 0.8f * Vol2Fs.Smooth(0f, 0.2f, age), 0.1f + r * 0.2f, cam, 28);
                    }
                }
                if (m.Hop != null)
                {
                    int every = natural ? 1 : 3;
                    if ((m.EndAt < 0f || (natural && ex < 0.8f)) && Time.frameCount % every == 0) Vol2FsB1.Emit(_drip, CentreNow(m.Hop) + Random.insideUnitSphere * 0.45f, natural ? 4 : 1);
                }
            }
            _ribbon.Write(_ribbonMesh); _swirl.Write(_swirlMesh); _puddle.Write(_puddleMesh); _rings.Write(_ringMesh); _phantom.Write(_phantomMesh);
        }
    }
}
