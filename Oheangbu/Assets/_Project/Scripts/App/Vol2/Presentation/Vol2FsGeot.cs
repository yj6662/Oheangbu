using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>것 (wood, ㅓ attachment, 가른다) - free showcase batch 6, concept geot_a (a serrated saw of living thorny wood grows along the brush shaft; the swing
    /// rips through the sill at the feet - two halves falling apart - and across the bear, tearing its crystal plates off, jade vines snaking into the seam and
    /// binding it). Executor facts only: the attachment's 6 s = the thorn saw on the brush (a jade-veined wooden spine past the bristles, two rows of hooked jade
    /// thorns along its edges, two vines spiralling up the shaft; leaves drift off it), every swing contact = a jade crescent (the base) + a burst of leaves
    /// and torn thorns, the strip book opens the seam, the body action binds it (2 s -> rootlets reaching forward). The attachment's end = the saw withers: the
    /// thorns drop, the spine browns and falls apart in leaves.</summary>
    public sealed class Vol2FsGeot : Vol2FsCutWear
    {
        static readonly Color Jade = new Color(0.4f, 1f, 0.5f);
        const int Teeth = 16;
        Transform _spine; readonly List<Transform> _teeth = new List<Transform>();
        Material _bark, _thorn, _dry;
        Mesh _vineMesh, _glowMesh; MeshRenderer _vineR; readonly Vol2FsStrips _glow = new Vol2FsStrips();
        ParticleSystem _leaves, _jade;
        float _endAt = -1f;
        protected override Color SlashTint => new Color(0.45f, 1f, 0.55f);
        protected override Color CutGlow => new Color(0.45f, 1.5f, 0.55f);

        protected override void OnSetup()
        {
            var root = Root.transform;
            _bark = Kit.Bark(new Color(0.62f, 0.55f, 0.4f), new Color(0.35f, 1.3f, 0.3f) * 3f, 0.25f, "fs_bark", "fs_vein");
            _dry = Kit.Bark(new Color(0.5f, 0.38f, 0.25f), Color.black, 0.2f, "fs_bark", "fs_vein");
            _thorn = Kit.Metal(1.1f, 0f, new Color(0.05f, 0.25f, 0.08f), new Color(0.55f, 1f, 0.62f)) ?? _bark;
            _spine = Vol2FsB1.Prop(root, "SawSpine", Kit.SharedMesh("geotSpine", () => Vol2FsB6.Spike(0.2f, 0.07f, 0.55f, 0f)), _bark, false).transform;
            var tm = Kit.SharedMesh("geotThorn", () => Vol2FsB6.Spike(0.09f, 0.035f, 0.25f, 0.18f));
            for (int k = 0; k < Teeth; k++) _teeth.Add(Vol2FsB1.Prop(root, "Thorn" + k, tm, _thorn, false).transform);
            var gl = M("fs_streak", Jade, 3.2f, 1f); gl.NoiseAmt = 0.25f; gl.Soft = 0.03f; gl.Erode = 0.12f; gl.Scroll = new Vector2(-1.2f, 0f);
            Vol2Fs.MeshObject(root, "SawGlow", Kit.Mat(gl), out _glowMesh);      // r2: a strong jade light down the saw (r1 read as plain wood)
            _vineR = Vol2Fs.MeshObject(root, "ShaftVines", Kit.Bark(new Color(0.8f, 1f, 0.6f), new Color(0.35f, 1.3f, 0.3f) * 4f, 0.3f, "fs_vine", "fs_vein"), out _vineMesh);
            var leaf = M("fs_leaf", new Color(0.85f, 1f, 0.7f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leaves = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(leaf), Life0 = 0.9f, Life1 = 1.7f, Speed0 = 1f, Speed1 = 4.5f, Size0 = 0.1f, Size1 = 0.22f, Gravity = 0.35f, Drag = 1.4f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 300f, AlphaHoldTo = 0.75f, Max = 500 }, root, "Leaves");
            _jade = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Jade, 4f, 1f)), Life0 = 0.3f, Life1 = 0.8f, Speed0 = 0.3f, Speed1 = 2f, Size0 = 0.025f, Size1 = 0.055f, Drag = 1.2f, Noise = 1f, NoiseFreq = 2f, Max = 400 }, root, "Jade");
            foreach (var ps in new[] { _leaves, _jade }) ps.Play(false);
            SetVisible(false);
        }

        void SetVisible(bool on) { _spine.gameObject.SetActive(on); foreach (var t in _teeth) t.gameObject.SetActive(on); _vineR.enabled = on; }

        protected override void OnLineEnd()
        {
            _endAt = Now(NowUs);
            Vector3 mid = Shaft(1.1f);
            Vol2FsB1.Emit(_leaves, mid, 50); Vol2FsB1.Emit(_jade, mid, 40);
            foreach (var r in _spine.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = _dry;
        }

        protected override void Blade(float t, float life, float warn)
        {
            _glow.Clear();
            if (life <= 0.01f) { SetVisible(false); _glow.Write(_glowMesh); return; }
            SetVisible(true);
            float wither = _endAt < 0f ? 0f : Vol2Fs.Smooth(_endAt, _endAt + 0.45f, t);
            Vector3 ax = ShaftAxis, side = ShaftSide, base0 = Shaft(0.42f);
            float len = (Tip() - base0).magnitude + 1.5f;      // r2: 1.5 m past the bristles (r1: 0.7 m, small against the concept)
            float grow = Vol2Fs.Smooth(0f, 1f, life) * (1f - 0.35f * wither);
            var rot = AlongShaft();
            _spine.SetPositionAndRotation(base0 + Vector3.down * 0.25f * wither, rot);
            _spine.localScale = new Vector3(1.35f + 0.15f * warn * Mathf.Sin(t * 50f), len * grow, 1.2f);
            float gl2 = life * (1f - wither) * (0.8f + 0.2f * Mathf.Sin(t * 6f));
            Vector3 g0 = base0 + ax * 0.05f, g1 = base0 + ax * len * grow * 0.97f;
            _glow.Strip(new[] { g0, Vector3.Lerp(g0, g1, 0.35f), g1 }, new[] { 0.16f, 0.24f, 0.03f }, null, Color.white, new[] { gl2 * 0.7f, gl2, gl2 * 0.8f }, 0.27f, CamPos);
            // two rows of hooked thorns along the spine's edges, pointing back toward the grip (a saw), the near-tip ones the largest
            for (int k = 0; k < Teeth; k++)
            {
                int row = k % 2; float u = 0.12f + 0.78f * (k / 2) / (Teeth / 2f - 1f);
                float sgn = row == 0 ? 1f : -1f;
                float w = 0.135f * Mathf.Lerp(0.5f, 1f, Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI)) + 0.02f;
                Vector3 at = base0 + ax * len * grow * u + side * sgn * w;
                Vector3 up = (side * sgn * 0.85f - ax * 0.45f).normalized;
                float drop = wither * (0.4f + 0.6f * Vol2Fs.Hash(Seed, k));
                at += Vector3.down * 0.9f * drop * drop;
                var view = Vector3.ProjectOnPlane(CamPos - at, up); if (view.sqrMagnitude < 1e-4f) view = Vector3.forward;
                _teeth[k].SetPositionAndRotation(at, Quaternion.LookRotation(view.normalized, up));
                float ts = Vol2Fs.Smooth(u * 0.5f, u * 0.5f + 0.45f, life) * (0.18f + 0.12f * Vol2Fs.Hash(Seed, k + 20)) * (1f - Vol2Fs.Smooth(0.6f, 1f, wither));
                _teeth[k].localScale = new Vector3(1.4f, Mathf.Max(0.001f, ts * 3.1f), 1.3f);
            }
            // two vines spiralling up the shaft from the grip into the spine
            var tubes = new Vol2FsTubes();
            for (int v = 0; v < 2; v++)
            {
                var pts = new Vector3[10];
                for (int i = 0; i < 10; i++)
                {
                    float u = i / 9f, a = u * 9f + v * Mathf.PI + t * 0.8f;
                    Vector3 n1 = side, n2 = Vector3.Cross(ax, side);
                    pts[i] = Shaft(Mathf.Lerp(0.05f, 1.05f, u)) + (n1 * Mathf.Cos(a) + n2 * Mathf.Sin(a)) * (0.045f + 0.02f * Mathf.Sin(u * 7f));
                }
                var tb = tubes.Add(pts, 0.026f, 0.012f, 30); tb.Grow = Vol2Fs.Smooth(0f, 0.8f, life) * (1f - wither); tb.Sides = 5;
            }
            tubes.Write(_vineMesh); _glow.Write(_glowMesh);
            if (_endAt < 0f && Time.frameCount % 5 == 0) { Vol2FsB1.EmitV(_leaves, base0 + ax * len * Random.value, Random.insideUnitSphere * 0.6f + Vector3.down * 0.2f, -1f, -1f); Vol2FsB1.EmitV(_jade, base0 + ax * len * Random.value, Vector3.up * 0.3f, -1f, -1f); }
        }

        protected override void OnSlash(Slash s, Struck by)
        {
            if (s.Arc) { for (int k = 0; k < 12; k++) Vol2FsB1.EmitV(_leaves, ArcPoint(s, k / 11f), (ArcPoint(s, Mathf.Min(1f, k / 11f + 0.1f)) - ArcPoint(s, k / 11f)).normalized * 4f + Random.insideUnitSphere, -1f, -1f); return; }
            Vector3 c = (s.A + s.B) * 0.5f;
            Vol2FsB1.Emit(_leaves, c, 40); Vol2FsB1.Emit(_jade, c, 60);
            for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(BookShards, c, (Random.onUnitSphere + Vector3.up * 0.3f) * Random.Range(3f, 6f), -1f, -1f);
        }

        protected override void Live(float t, float h) { }
    }
}
