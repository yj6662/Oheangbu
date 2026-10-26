using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>석 (metal, ㅓ attachment, 남긴다) - free showcase batch 3, concept seok_a (steel blades have grown along the whole brush shaft like the branches of a steel
    /// tree, turning the brush into a long GLAIVE, thin blades along the forearm; the swing cuts with a brilliant white slash and sparks). Unlike 서's single crescent
    /// guard it is a branching blade-tree that STAYS ON the weapon for the material's 6 s. Beats, executor facts only: the launch = blades sprout up the shaft in
    /// sequence, a long blade extends past the bristles -> worn for the Attached material's life (glints run along the edges); the brush's real motion leaves a
    /// razor-thin silver arc; a body the swing touched = white slash lines and sparks on it -> the last 0.4 s = the blades shiver and ring -> natural expiry (CSV:
    /// 쨍하게 끊기며 파편이 옆으로 튐) = they snap off with a ring and the shards fly SIDEWAYS.</summary>
    public sealed class Vol2FsSeok : Vol2FsWear
    {
        static readonly Color White = new Color(1f, 0.97f, 0.9f), Gold = new Color(1f, 0.85f, 0.55f), Ink = new Color(0.03f, 0.03f, 0.035f);
        ParticleSystem _sparks, _shards, _glints, _flash; Light _light;
        Material _steel, _steelHot;
        sealed class B { public Transform T; public float U, Ang, Len, Tilt, At, Rnd; public bool Arm; }
        readonly List<B> _blades = new List<B>();
        Mesh _trailMesh, _slashMesh, _inkMesh; readonly Vol2FsStrips _trail = new Vol2FsStrips(), _slash = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        readonly List<(Vector3 C, Vector3 Side, float T)> _cuts = new List<(Vector3, Vector3, float)>();
        bool _snapped;
        protected override float ExpireSeconds => 0.9f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);
        static Quaternion Look(Vector3 d, Vector3 up) => Quaternion.LookRotation(d, Mathf.Abs(Vector3.Dot(d.normalized, up.normalized)) > 0.97f ? Vector3.forward : up);

        protected override void OnBegin()
        {
            var root = Root.transform;
            _steel = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.82f, 0.86f, 0.95f), 0.5f, 0.82f, new Color(0.5f, 0.56f, 0.68f), "fs_steel");      // r2: less self-light
            _steelHot = Vol2FsB1.Lit(Kit, "fs_steel", new Color(0.95f, 0.93f, 0.88f), 0.6f, 0.85f, new Color(0.95f, 0.85f, 0.6f), "fs_steel");
            var blade = Kit.SharedMesh("blade", () => Vol2FsB1.Blade());
            // the glaive's long blade past the bristles, the branch blades up the shaft (both sides, angled forward like leaves on a twig), and thin blades on the forearm
            AddBlade(root, blade, 1.02f, 0f, 1.25f, 0f, 0f, false, true);
            for (int k = 0; k < 12; k++)
            {
                float u = Mathf.Lerp(0.18f, 0.95f, k / 11f);
                AddBlade(root, blade, u, (k % 2 == 0 ? 1f : -1f) * (55f + 25f * Vol2Fs.Hash(Seed, k)) + 15f * Mathf.Sin(k * 1.7f), Mathf.Lerp(0.55f, 1.0f, Vol2Fs.Hash(Seed, k + 10)) * (0.6f + 0.6f * u), Mathf.Lerp(30f, 50f, Vol2Fs.Hash(Seed, k + 20)), 0.02f * k, false, k % 3 == 0);
            }
            for (int k = 0; k < 5; k++) AddBlade(root, blade, -0.05f - 0.04f * k, 90f + 60f * k, 0.45f, 70f, 0.03f * k, true, false);
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 6f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 4f, Speed1 = 14f, Size0 = 0.025f, Size1 = 0.055f, Radius = 0.12f, Gravity = 1f, Drag = 1.6f, Stretch = true, StretchVel = 0.035f, Max = 900 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steelHot, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.8f, Life1 = 1.3f, Speed0 = 1f, Speed1 = 2f, Size0 = 0.1f, Size1 = 0.24f, Radius = 0.05f, Gravity = 1.3f, Drag = 0.7f, Spin = 900f, Max = 160 }, root, "Shards");
            _glints = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 7f, 1f)), Life0 = 0.08f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.14f, Size1 = 0.28f, Radius = 0.01f, Max = 120, SortFudge = -10 }, root, "Glints");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", White, 6f, 1f)), Life0 = 0.12f, Life1 = 0.16f, Speed0 = 0f, Speed1 = 0f, Size0 = 1.1f, Size1 = 1.5f, Radius = 0.01f, SizeStart = 0.3f, SizePeak = 1f, SizePeakAt = 0.2f, SizeEnd = 0.8f, Max = 8, SortFudge = -30 }, root, "Flash");
            foreach (var ps in new[] { _sparks, _shards, _glints, _flash }) ps.Play(false);
            var tr = M("fs_streak", White, 5f, 1f); tr.NoiseAmt = 0.15f;
            Vol2Fs.MeshObject(root, "SilverArc", Kit.Mat(tr), out _trailMesh);
            Vol2Fs.MeshObject(root, "Slashes", Kit.Mat(tr), out _slashMesh);
            Vol2Fs.MeshObject(root, "InkThreads", Kit.InkSolid("fs_stroke2", Ink, new Color(0.9f, 0.85f, 0.7f, 1f), 0.05f, 0.45f, 0.3f, 1.5f), out _inkMesh);
            _light = Vol2Fs.PointLight(root, "SteelLight", new Color(1f, 0.95f, 0.85f), 4f);
        }

        void AddBlade(Transform root, Mesh mesh, float u, float ang, float len, float tilt, float at, bool arm, bool hot)
        {
            var r = Vol2FsB1.Prop(root, "Blade" + _blades.Count, mesh, hot ? _steelHot : _steel, false);
            r.transform.localScale = Vector3.one * 0.001f;
            _blades.Add(new B { T = r.transform, U = u, Ang = ang, Len = len, Tilt = tilt, At = at, Rnd = Vol2Fs.Hash(Seed, _blades.Count + 90), Arm = arm });
        }

        protected override void OnCharge(float t, float k) { if (Random.value < 0.3f * k) Vol2FsB1.Emit(_glints, Tip(), 1); }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs) { Vol2FsB1.Emit(_glints, Tip(), 3); Vol2FsB1.Emit(_sparks, Tip(), 40); }      // r2: no big flash at the tip

        void Cut(Vector3 c, Vector3 away)
        {
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(c - CamPos));
            _cuts.Add((c, side, Inst.Rel(NowUs)));
            Vol2FsB1.Emit(_flash, c, 1); Vol2FsB1.Emit(_sparks, c, 220); Vol2FsB1.Emit(_glints, c, 6);
        }
        protected override void OnStruck(Struck s) { Cut(CentreNow(s), FlatN(CentreNow(s) - Inst.Origin)); }
        protected override void OnTouch(Struck s) { if (Hits.Exists(h => h.Id == s.Id && Mathf.Abs(h.At - s.At) < 0.05f)) return; Cut(CentreNow(s), FlatN(CentreNow(s) - Inst.Origin)); }

        protected override void OnResidueEnd()
        {
            if (_snapped) return;
            _snapped = true;
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(Inst.Origin - CamPos));
            Vol2FsB1.Emit(_flash, Tip(), NaturalEnd ? 2 : 1);
            foreach (var b in _blades)
            {
                if (b.T.localScale.z < 0.01f) continue;
                Vector3 mid = b.T.position + b.T.forward * b.T.localScale.z * 0.5f;
                int n = NaturalEnd ? 3 : 1;
                for (int j = 0; j < n; j++)
                    Vol2FsB1.EmitV(_shards, mid, NaturalEnd ? side * (Random.value < 0.5f ? -1f : 1f) * Random.Range(4f, 9f) + Vector3.up * Random.Range(0.5f, 2.5f) : Random.insideUnitSphere * 1.2f, Random.Range(0.1f, 0.22f), -1f);
                if (NaturalEnd) Vol2FsB1.EmitV(_sparks, mid, side * Random.Range(-10f, 10f) + Vector3.up * 1.5f, -1f, -1f);
                b.T.gameObject.SetActive(false);
            }
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float worn = Worn(t), warn = Warn(NowUs), pulse = DrawPulse(t);
            Vector3 tip = Tip(), grip = Grip(), sleeve = Sleeve();
            Vector3 ax = (tip - grip).sqrMagnitude > 1e-6f ? (tip - grip).normalized : Vector3.up;
            Vector3 n0 = Vector3.Cross(ax, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(ax, Vector3.up).normalized : Right;
            float grow = h;
            foreach (var b in _blades)
            {
                if (_snapped) break;
                float g = Vol2Fs.EaseOutBack(Mathf.Clamp01((grow - b.At - (b.Arm ? 0.1f : 0.2f * Mathf.Clamp01(b.U))) / 0.18f)) * worn;
                Vector3 at = b.U < 0f ? Vector3.Lerp(grip, sleeve, -b.U / 0.25f) : Vector3.LerpUnclamped(grip, tip, b.U);
                Vector3 outN = Quaternion.AngleAxis(b.Ang, ax) * n0;
                Vector3 dir = b.U >= 1f ? ax : (Quaternion.AngleAxis(-b.Tilt, Vector3.Cross(outN, ax)) * outN);
                dir = (dir + ax * (b.Arm ? 0.2f : 0.55f)).normalized;
                if (warn > 0f) dir = Quaternion.Euler((Mathf.PerlinNoise(t * 47f, b.Rnd * 9f) - 0.5f) * 10f * warn, (Mathf.PerlinNoise(b.Rnd * 7f, t * 43f) - 0.5f) * 10f * warn, 0f) * dir;
                b.T.SetPositionAndRotation(at, Look(dir, ax) * Quaternion.AngleAxis(b.Rnd * 90f, Vector3.forward));
                b.T.localScale = new Vector3(b.U >= 1f ? 3.2f : 2.2f, 2.2f, Mathf.Max(0.001f, b.Len * g));
                if (g > 0.9f && Random.value < 0.01f + 0.05f * warn) Vol2FsB1.Emit(_glints, at + dir * b.Len * Random.Range(0.3f, 1f), 1);
            }
            // the razor-thin silver arc of the swing (real motion only) + a few ink threads
            _trail.Clear(); _ink.Clear();
            float tl = worn * Vol2Fs.Smooth(1.5f, 4f, TipSpeed(t));
            if (tl > 0.01f) { SwingTrail(_trail, t, 0.16f, 0.12f, tl, 0.21f); SwingTrail(_ink, t, 0.2f, 0.05f, tl * 0.8f, 0.61f, 0.03f); }
            _trail.Write(_trailMesh); _ink.Write(_inkMesh);
            // the cut lines on the struck bodies
            _slash.Clear();
            foreach (var c in _cuts)
            {
                float a = t - c.T, l = 1f - Vol2Fs.Smooth(0.08f, 0.45f, a); if (l <= 0.01f) continue;
                for (int j = 0; j < 3; j++)
                {
                    Vector3 d = (c.Side * Mathf.Cos(0.5f + 0.2f * j) - Vector3.up * Mathf.Sin(0.5f + 0.2f * j)).normalized; float s = 0.9f * Vol2Fs.EaseOutBack(a / 0.06f);
                    Vector3 o = Vector3.up * (0.22f * (j - 1));
                    _slash.Strip(new[] { c.C + o - d * s, c.C + o, c.C + o + d * s }, new[] { 0.015f, 0.09f, 0.015f }, null, Color.white, new[] { l, l, l }, 0.3f + 0.2f * j, cam, 0f, 1f);
                }
            }
            _slash.Write(_slashMesh);
            _light.transform.position = Vector3.Lerp(grip, tip, 0.7f);
            _light.intensity = (_snapped ? Mathf.Max(0f, 8f * (1f - ExitK(t) * 3f)) : 1.6f * worn * (1f + 1.5f * pulse) * (warn > 0f ? 0.6f + 0.4f * Mathf.Sin(t * 60f) : 1f));
        }
    }
}
