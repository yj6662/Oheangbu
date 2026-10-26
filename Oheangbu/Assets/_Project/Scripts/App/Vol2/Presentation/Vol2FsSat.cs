using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>삿 (metal, ㅏ path, 가른다 - the PEAK: pierces) - free showcase batch 6, concept sat_a (one needle-thin line of white-gold steel light pierces straight
    /// through the wall and all three bears at once, frozen in the instant: each bear's crystal armour bursting off in turn along the line, the first the most
    /// violently, sparks streaming from every contact, the second wall split; black ink ribbons swirl round the line). Executor facts only: the run = the visible
    /// cut (the first body at the SingleCue impact clock, then on through the bodies the executor stripped - never past where its head stopped); the line is drawn
    /// from the brush tip to the run's head at chest height, white-hot at its head, and STAYS as a thinning afterimage; each body the head passes = a burst of
    /// sparks streaming forward along the line (the first body: the Instant x1.5 = a bigger flare + the metal action's X; the second and third were refused a paid
    /// cut - FirstDamageOnly - and are only stripped: their bursts are smaller), the strip book opens three seams, the split walls part where the line crosses;
    /// the line ends with a ring of light snapping off (CSV 끝: 쨍하게 끊기며 파편이 옆으로).</summary>
    public sealed class Vol2FsSat : Vol2FsCutPath
    {
        static readonly Color Gold = new Color(1f, 0.9f, 0.62f);
        const float Height = 1.05f;
        ParticleSystem _sparks, _shards, _flash;
        Mesh _lineMesh, _glowMesh, _inkMesh; readonly Vol2FsStrips _line = new Vol2FsStrips(), _glow = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        Vector3 _from; float _stopAt = -1f, _snapAt = -1f;
        readonly List<(Vector3 At, float T, bool First)> _pierces = new List<(Vector3, float, bool)>();
        readonly HashSet<long> _seen = new HashSet<long>();
        protected override Color CutGlow => new Color(1.7f, 1.45f, 0.95f);
        protected override float CrackWidth => 0.12f;
        protected override float CrackJag => 0.03f;
        protected override int ArmourShards => 30;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 5f, 1f)), Life0 = 0.25f, Life1 = 0.7f, Speed0 = 4f, Speed1 = 14f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.2f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.6f, Max = 1500 }, root, "Sparks");
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.9f, new Color(0.05f, 0.06f, 0.08f)), Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 4f, Speed1 = 9f, Size0 = 0.05f, Size1 = 0.13f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 200 }, root, "Shards");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Gold, 2.4f, 1f)), Life0 = 0.06f, Life1 = 0.1f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.3f, Size1 = 0.45f, Max = 40 }, root, "Flash");      // r2: was white 0.8 - 1.2 (white discs)
            foreach (var ps in new[] { _sparks, _shards, _flash }) ps.Play(false);
            var core = M("fs_streak", Color.white, 5f, 1f); core.NoiseAmt = 0.08f; core.Soft = 0.02f; core.Erode = 0.05f; core.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "Needle", Kit.Mat(core), out _lineMesh);
            var glow = M("fs_streak", Gold, 2f, 1f); glow.NoiseAmt = 0.3f; glow.Soft = 0.2f; glow.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "NeedleGlow", Kit.Mat(glow), out _glowMesh);
            Vol2Fs.MeshObject(root, "InkRibbons", Kit.InkSolid("fs_stroke2", InkC, Gold * 1.2f, 0.08f, 0.45f, 0.3f, 1.3f), out _inkMesh);
        }

        Vector3 HeadAt(float along) => OnPath(along) + Vector3.up * Height;

        protected override void OnLaunch(long nowUs)
        {
            base.OnLaunch(nowUs);
            _from = Tip();
            Vol2FsB1.EmitV(_flash, _from, Vector3.zero, 0.9f, 0.1f);
        }

        protected override void Run(float t, float along, float prev)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt((along - prev) * 5f));
            for (int k = 0; k < n; k++)
            {
                Vector3 p = HeadAt(Mathf.Lerp(prev, along, (k + 0.5f) / n));
                for (int i = 0; i < 3; i++) Vol2FsB1.EmitV(_sparks, p, (F * Random.Range(0.5f, 1.5f) + Random.insideUnitSphere * 0.6f).normalized * Random.Range(3f, 9f), -1f, -1f);
            }
            // each stripped body the head passes: sparks stream forward along the line (the executor's strip book says who; the run says when)
            foreach (var s in Strips)
            {
                if (_seen.Contains(s.Id)) continue;
                float a = AlongOf(FeetOf(s));
                if (along + 0.2f < a) continue;
                _seen.Add(s.Id);
                bool first = s.Id == TargetId;
                var b = BodyNow(s);
                Vector3 at = new Vector3(b.center.x, Ground + Height, b.center.z) - F * Mathf.Max(b.extents.x, b.extents.z) * 0.5f;
                _pierces.Add((at, t, first));
                int sparks = first ? 260 : 120;
                for (int i = 0; i < sparks; i++) Vol2FsB1.EmitV(_sparks, at, (F * Random.Range(0.6f, 2f) + Random.insideUnitSphere).normalized * Random.Range(5f, 15f), -1f, -1f);
                for (int i = 0; i < (first ? 24 : 10); i++) Vol2FsB1.EmitV(_shards, at, (Random.insideUnitSphere + F * 0.8f + Vector3.up * 0.3f).normalized * Random.Range(3f, 8f), -1f, -1f);
                Vol2FsB1.EmitV(_flash, at, Vector3.zero, first ? 0.55f : 0.4f, 0.08f);
                FlashLight(at, first ? 10f : 6f, t + 0.2f);
            }
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            _line.Clear(); _glow.Clear(); _ink.Clear();
            float along = RunAlong(t);
            var p = Inst.Path;
            bool stopped = p != null && p.Finished && along >= Mathf.Max(AlongA, p.HeadAlong) - 0.01f;
            if (stopped && _stopAt < 0f) _stopAt = t;
            // the line: needle core + soft glow, from the brush tip (where it was fired) to the head; it holds, then thins away as an afterimage and snaps
            float hold = _stopAt < 0f ? 1f : 1f - Vol2Fs.Smooth(_stopAt + 0.7f, _stopAt + 1.9f, t);      // r2: the needle holds longer
            if (hold > 0.01f && along > 0.2f)
            {
                Vector3 a = _from, b = HeadAt(along);
                int n = 24; var pts = new Vector3[n]; var wc = new float[n]; var wg = new float[n]; var al = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float u = k / (float)(n - 1);
                    pts[k] = Vector3.Lerp(a, b, u);
                    float head = Vol2Fs.Smooth(0.8f, 1f, u);
                    wc[k] = (0.025f + 0.05f * head) * Mathf.Lerp(0.4f, 1f, hold); wg[k] = (0.16f + 0.22f * head) * hold; al[k] = hold * Mathf.Lerp(0.55f, 1f, u);
                }
                _line.Strip(pts, wc, null, Color.white, al, 0.13f, cam);
                _glow.Strip(pts, wg, null, Color.white, al, 0.57f, cam);
            }
            if (stopped && _snapAt < 0f && t > _stopAt + 1.75f)
            {
                _snapAt = t;
                Vector3 e = HeadAt(along), side = Vector3.Cross(Vector3.up, F);
                for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_shards, Vector3.Lerp(_from, e, Random.value), side * (k % 2 == 0 ? 1f : -1f) * Random.Range(2f, 5f) + Vector3.up * Random.Range(0.5f, 2f), -1f, -1f);
                for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_sparks, Vector3.Lerp(_from, e, Random.value), side * (k % 2 == 0 ? 1f : -1f) * Random.Range(3f, 8f) + Vector3.up * Random.Range(0f, 2f), -1f, -1f);
            }
            // the frozen instant: a short white flare round each pierce, the first one bigger, plus a ring of light opening across the line
            foreach (var pc in _pierces)
            {
                float a = t - pc.T; if (a < 0f || a > 1.2f) continue;
                float life = 1f - Vol2Fs.Smooth(0.08f, 0.6f, a), slow = 1f - Vol2Fs.Smooth(0.3f, 1.2f, a);
                Vol2FsB5.Halo(_glow, pc.At, F, (pc.First ? 0.45f : 0.3f) + a * (pc.First ? 1.6f : 1.0f), pc.First ? 0.07f : 0.05f, life * 0.7f, 0.31f, cam, 28);
                Vector3 side = Vector3.Cross(Vector3.up, F);
                Vol2FsB6.Slash(_line, pc.At - side * (pc.First ? 1.5f : 1.0f) + Vector3.up * 0.5f, pc.At + side * (pc.First ? 1.5f : 1.0f) - Vector3.up * 0.4f, F * 0.1f, pc.First ? 0.18f : 0.12f, Vol2Fs.Smooth(0f, 0.05f, a), slow, 0.77f, cam);
                // r2: a vertical slit of light through each pierced body, lingering (three in a row read as "through all three" from behind the caster)
                Vector3 foot = new Vector3(pc.At.x, Ground, pc.At.z);
                Vol2FsB6.Slash(_glow, foot - Vector3.up * 0.1f, foot + Vector3.up * (pc.First ? 3.0f : 2.4f), side * 0.08f, pc.First ? 0.32f : 0.24f, Vol2Fs.Smooth(0f, 0.06f, a), slow, 0.13f, cam);
                Vol2FsB6.Slash(_line, foot, foot + Vector3.up * (pc.First ? 2.8f : 2.2f), side * 0.05f, pc.First ? 0.07f : 0.05f, Vol2Fs.Smooth(0f, 0.05f, a), slow, 0.43f, cam);
            }
            // black ink ribbons swirling round the start of the line (the swing's ink), sweeping out and drying
            float ia = t - LaunchAt;
            if (ia >= 0f && ia < 1.8f)
            {
                // r2: wider, out to the sides of the frame (r1: they curled behind the caster's head)
                float life = 1f - Vol2Fs.Smooth(0.6f, 1.8f, ia), reveal = Vol2Fs.Smooth(0f, 0.12f, ia);
                Vector3 side = Vector3.Cross(Vector3.up, F), o = Inst.Origin + Vector3.up * 1.3f + F * 2.2f;
                for (int k = 0; k < 4; k++)
                {
                    // r3: thinner and curling (r2: four heavy black bars across the frame)
                    float sgn = k % 2 == 0 ? 1f : -1f, r = 1.5f + 0.5f * k;
                    Vector3 a = o + side * sgn * r + Vector3.up * (0.9f - 0.45f * k) - F * 0.4f, b = o + F * (2.0f + 1.0f * k) - side * sgn * r * 0.2f - Vector3.up * (0.1f + 0.15f * k);
                    Vol2FsB6.Slash(_ink, a, b, Vector3.up * (0.8f - 0.35f * k) + side * sgn * 0.6f, 0.13f - 0.02f * k, reveal, life, 0.21f + 0.17f * k, cam);
                }
            }
            _line.Write(_lineMesh); _glow.Write(_glowMesh); _ink.Write(_inkMesh);
        }
    }
}
