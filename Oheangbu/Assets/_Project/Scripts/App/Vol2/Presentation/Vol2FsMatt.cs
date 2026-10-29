using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>맛 (earth - class Vol2FsMatt: Vol2FsMat is the kit's material recipe, ㅏ path, 가른다) - free showcase batch 6, concept mat_b (a spinning flat disc of slate skims along the ground on edge, gouging a deep straight
    /// trench, a trail of ink-stroke afterimage rings behind it, slicing through the wall in a spray of grit and slamming into the bear: its crystal armour
    /// shatters off and it is shoved back). Executor facts only: the run = the slate wheel rolling on edge at the visible run speed (spinning, grit thrown up
    /// behind it, three ink rings lagging as its phantoms - Phantom Blade principle 5), the split walls part where it passes (a burst of grit), at the impact
    /// clock it slams into the first body and bursts into slate shards (hit-stop), the strip book opens the seam (3 s), the body action shoves it (the push
    /// the game applies: dust, rocks, the skid of its feet; CSV 끝: 가라앉으며 둘레 땅이 판으로 들림 - the trench closes as the plates settle).</summary>
    public sealed class Vol2FsMatt : Vol2FsCutPath
    {
        static readonly Color Amber = new Color(0.95f, 0.66f, 0.3f);
        const float WheelR = 1.0f;      // r2: was 0.72
        ParticleSystem _grit, _dust, _slate;
        Transform _wheel; Material _stone;
        Mesh _ringMesh, _curlMesh; readonly Vol2FsStrips _rings = new Vol2FsStrips(), _curls = new Vol2FsStrips();
        float _spin, _burstAt = -1f; Vector3 _burstPos;
        protected override Color CutGlow => new Color(1.5f, 0.95f, 0.45f);
        protected override float CrackWidth => 0.34f;
        protected override float CrackJag => 0.04f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.5f, 0.47f, 0.45f), 0f, 0.15f, new Color(0.9f, 0.55f, 0.2f) * 0.5f, "fs_rock_crack");
            var r = Vol2FsB1.Prop(root, "SlateWheel", Kit.SharedMesh("matWheel", () => Vol2FsB1.Rock(23, 1f, 1f, 18, 12)), _stone);
            _wheel = r.transform; _wheel.gameObject.SetActive(false);
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = _stone, Mesh = Kit.SharedMesh("matGrit", () => Vol2FsB1.Rock(7, 0.5f)), Life0 = 0.5f, Life1 = 1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.04f, Size1 = 0.12f, Gravity = 2.4f, Drag = 0.3f, Spin = 600f, Max = 600 }, root, "Grit");
            _slate = Vol2Fs.Build(new Vol2FsPs { Mat = _stone, Mesh = Kit.SharedMesh("matSlate", () => Vol2FsB1.Shard(31)), Life0 = 0.8f, Life1 = 1.4f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.25f, Size1 = 0.55f, Gravity = 2.6f, Drag = 0.2f, Spin = 700f, Max = 120 }, root, "Slate");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.5f, 0.36f, 0.26f, 1f), 0.5f)), Life0 = 0.6f, Life1 = 1.3f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.3f, Size1 = 0.7f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.3f, Max = 300 }, root, "Dust");
            foreach (var ps in new[] { _grit, _dust, _slate }) ps.Play(false);
            var ring = M("fs_stroke2", InkC, 1f, 0f); ring.Edge = Amber * 1.6f; ring.EdgeWidth = 0.3f; ring.NoiseAmt = 0.35f; ring.Soft = 0.06f;
            Vol2Fs.MeshObject(root, "PhantomRings", Kit.Mat(ring), out _ringMesh);
            Vol2Fs.MeshObject(root, "InkCurls", Kit.InkSolid("fs_stroke2", InkC, Amber * 1.4f, 0.1f, 0.45f, 0.3f, 1.3f), out _curlMesh);
        }

        Vector3 WheelAt(float along) => OnPath(along) + Vector3.up * WheelR * 0.98f;
        // r2: the wheel was on screen ~7 frames (the cut runs 4.6 m in 0.12 s): its ink rings now stay where it passed and fade slowly (Phantom Blade principle 5),
        // and at the body it LODGES spinning in its front for a beat (hit-stop) before it bursts
        readonly List<(Vector3 P, float At)> _ghosts = new List<(Vector3, float)>();
        float _nextGhost, _lodgeAt = -1f; Vector3 _lodgePos;
        const float LodgeSeconds = 0.3f;

        protected override void OnLaunch(long nowUs) { base.OnLaunch(nowUs); _wheel.gameObject.SetActive(true); _wheel.position = WheelAt(StartAlong); _wheel.localScale = new Vector3(WheelR, WheelR, WheelR * 0.17f); _nextGhost = StartAlong + 0.4f; }

        protected override void Run(float t, float along, float prev)
        {
            Vector3 side = Vector3.Cross(Vector3.up, F);
            _spin += (along - prev) / WheelR * Mathf.Rad2Deg;
            while (_nextGhost <= along) { _ghosts.Add((WheelAt(_nextGhost), t)); _nextGhost += 0.55f; }
            if (_lodgeAt < 0f)
            {
                _wheel.SetPositionAndRotation(WheelAt(along), Quaternion.AngleAxis(-_spin, side) * Quaternion.AngleAxis(4f, F) * Quaternion.LookRotation(side, Vector3.up));
                Vector3 contact = OnPath(along);
                int n = Mathf.Max(1, Mathf.CeilToInt((along - prev) * 6f));
                for (int k = 0; k < n; k++)
                {
                    Vol2FsB1.EmitV(_grit, contact + Random.insideUnitSphere * 0.1f, (-F * Random.Range(1f, 3f) + Vector3.up * Random.Range(2f, 4.5f) + side * Random.Range(-1.5f, 1.5f)), -1f, -1f);
                    Vol2FsB1.EmitV(_dust, contact + Vector3.up * 0.15f, -F * 0.6f + Vector3.up * 0.6f + side * Random.Range(-1.2f, 1.2f), -1f, -1f);
                }
            }
        }

        protected override void OnStruck(Struck s)
        {
            if (s.Kind != "Instant" || _lodgeAt >= 0f) return;
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            _lodgeAt = Now(NowUs);
            _lodgePos = b.center + FlatN(CamPos - b.center) * Mathf.Max(b.extents.x, b.extents.z) * 0.35f;
            HitStop(_lodgeAt, 0.1f);
        }

        void Burst()
        {
            _burstAt = Now(NowUs); _burstPos = _lodgePos;
            _wheel.gameObject.SetActive(false);
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_slate, _burstPos + Random.insideUnitSphere * 0.5f, (Random.onUnitSphere + Vector3.up * 0.6f + F * 0.5f).normalized * Random.Range(3f, 9f), -1f, -1f);
            for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_grit, _burstPos, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(3f, 8f), -1f, -1f);
            for (int k = 0; k < 22; k++) Vol2FsB1.EmitV(_dust, _burstPos + Random.insideUnitSphere * 0.6f, Random.onUnitSphere * 1.8f + Vector3.up * 0.7f, -1f, -1f);
            for (int k = 0; k < 16; k++) Vol2FsB1.EmitV(PInk, _burstPos, (Random.onUnitSphere + Vector3.up * 0.3f).normalized * Random.Range(3f, 7f), -1f, -1f);
            FlashLight(_burstPos, 6f, _burstAt + 0.25f);
        }

        protected override void OnCleave(Cleave c)
        {
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_grit, c.Centre + Vector3.up * Random.Range(0.1f, 0.8f), c.Side * (k % 2 == 0 ? 1f : -1f) * Random.Range(2f, 5f) + Vector3.up * Random.Range(1f, 3f) + F * Random.Range(0f, 2f), -1f, -1f);
            for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_dust, c.Centre + Vector3.up * 0.3f, c.Side * (k % 2 == 0 ? 1f : -1f) * 1.4f + Vector3.up * 0.5f, -1f, -1f);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; _rings.Clear(); _curls.Clear();
            Vector3 side = Vector3.Cross(Vector3.up, F);
            // lodged: spinning in the body's front, grit and sparks sprayed off it, then the burst
            if (_lodgeAt >= 0f && _burstAt < 0f)
            {
                float a = t - _lodgeAt;
                _spin += Time.deltaTime * 1400f;
                Vector3 p = Vector3.Lerp(_wheel.position, _lodgePos, Vol2Fs.Smooth(0f, 0.05f, a));
                _wheel.SetPositionAndRotation(p, Quaternion.AngleAxis(-_spin, side) * Quaternion.AngleAxis(4f, F) * Quaternion.LookRotation(side, Vector3.up));
                if (Time.frameCount % 1 == 0) for (int k = 0; k < 4; k++) Vol2FsB1.EmitV(_grit, p + Vector3.up * WheelR * 0.6f, (Vector3.up * 2.5f + side * Random.Range(-2f, 2f) - F * 1.5f) + Random.insideUnitSphere, -1f, -1f);
                if (a >= LodgeSeconds) Burst();
            }
            // the ink rings where the wheel passed: slowly fading afterimages
            foreach (var g in _ghosts)
            {
                float a = t - g.At; if (a < 0f || a > 1.1f) continue;
                float life = 1f - Vol2Fs.Smooth(0.15f, 1.1f, a);
                Vol2FsB5.Halo(_rings, g.P, side, WheelR * (0.97f + 0.12f * a), 0.12f, life * 0.85f, 0.2f + 0.13f * (g.At * 7f % 1f), cam, 30, _spin * Mathf.Deg2Rad * 0.2f);
            }
            float fade = _burstAt < 0f ? 1f : 1f - Vol2Fs.Smooth(_burstAt, _burstAt + 0.35f, t);
            float along = RunAlong(t);
            if (fade > 0.01f && along > StartAlong)
            {
                Vector3 top = _wheel.position + Vector3.up * WheelR * 0.9f;
                for (int k = 0; k < 2; k++)
                {
                    Vector3 a = top - F * (0.2f + 0.3f * k), b = top - F * (2.0f + 0.8f * k) + Vector3.up * (0.6f - 0.3f * k) + side * (k == 0 ? 0.6f : -0.5f);
                    Vol2FsB6.Slash(_curls, a, b, Vector3.up * 0.35f, 0.18f - 0.04f * k, 1f, fade * 0.85f, 0.37f + 0.3f * k, cam);
                }
            }
            _rings.Write(_ringMesh); _curls.Write(_curlMesh);
        }
    }
}
