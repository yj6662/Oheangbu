using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>뭉 (earth, ㅜ ring, 돌아온다) - free showcase batch 7, concept mung_b (a ring of flying rocks bursts outward from the caster to 4 m and is pulled back in,
    /// ink sweeps and dust trailing them). Executor facts only: sixteen chipped rocks (the kit's stone) burst up out of the 2 m ring at the launch and fly with the
    /// band's radius (the report's legs: 2 m -> 4 m, back to 2 m) - an arc up and out going, tumbling, drawn back low coming home; each rock trails an ink-and-dust
    /// streak through where it really was over the last 0.3 s; the first touch = a rock burst + the shared push (the body's own skid), the return touch = a smaller
    /// crack of grit (x0.5); a shot taken shatters on them; the turn = the rocks hang for an instant (hit-stop) and shed grit. Home: the rocks slam down round the
    /// 2 m ring in a ring of dust and sink, the ground round them lifting in plates (CSV 끝: 가라앉으며 둘레 땅이 판으로 들림).</summary>
    public sealed class Vol2FsMung : Vol2FsBackRing
    {
        static readonly Color Amber = new Color(0.95f, 0.62f, 0.3f);
        const int Rocks = 16;
        readonly Transform[] _rocks = new Transform[Rocks];
        Mesh _trailMesh, _dustMesh, _plateMesh; readonly Vol2FsStrips _trail = new Vol2FsStrips(), _dustTrail = new Vol2FsStrips(), _plates = new Vol2FsStrips();
        ParticleSystem _grit, _dust, _chips;
        float _landAt = -1f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            var stone = Kit.Stone("fs_rock", new Color(0.62f, 0.52f, 0.44f), Amber * 0.5f, "fs_vein") ?? Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.6f, 0.5f, 0.42f), 0f, 0.12f);
            for (int k = 0; k < Rocks; k++)
            {
                int s = k % 4;
                _rocks[k] = Vol2FsB1.Prop(root, "Rock" + k, Kit.SharedMesh("mungRock" + s, () => Vol2FsB1.Rock(811 + s, 0.5f, 0.8f)), stone).transform;
                _rocks[k].gameObject.SetActive(false);
            }
            Vol2Fs.MeshObject(root, "RockInk", Kit.InkSolid("fs_stroke2", InkC, Amber * 1.4f, 0.16f, 0.45f, 0.3f, 1.4f), out _trailMesh);
            Vol2Fs.MeshObject(root, "RockDust", Kit.Mat(Vol2FsB3.Dust(new Color(0.5f, 0.38f, 0.28f, 1f), 0.5f)), out _dustMesh);
            Vol2Fs.MeshObject(root, "Plates", Kit.InkSolid("fs_stroke2", new Color(0.18f, 0.12f, 0.09f, 1f), Amber * 1.2f, 0.12f, 0.35f, 0.3f, 1.2f), out _plateMesh);
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.58f, 0.5f, 0.44f), 0f, 0.12f), Mesh = Kit.SharedMesh("mangGrit", () => Vol2FsB1.Rock(17, 0.5f)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.05f, Size1 = 0.14f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 500 }, root, "Grit");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.48f, 0.34f, 0.25f, 1f), 0.55f)), Life0 = 0.7f, Life1 = 1.5f, Speed0 = 0.3f, Speed1 = 2f, Size0 = 0.35f, Size1 = 0.8f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.3f, Max = 500 }, root, "Dust");
            _chips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Amber, 3.5f, 1f)), Life0 = 0.3f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.6f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 500 }, root, "Chips");
            foreach (var ps in new[] { _grit, _dust, _chips }) ps.Play(false);
        }

        float Ang(int k) => k * Vol2ReturnGeometry.Tau / Rocks + (Vol2Fs.Hash(Seed, k) - 0.5f) * 0.25f;
        float Size(int k) => 0.55f + 0.45f * Vol2Fs.Hash(Seed, k + 30);      // r2: a bit bigger
        /// <summary>A rock's point at look time tt: on the band's radius, up in an arc while the trip is out, low coming back; on the 2 m ring at home.</summary>
        Vector3 RockAt(int k, float tt)
        {
            float r = RingNow(tt) + (Vol2Fs.Hash(Seed, k + 60) - 0.5f) * 0.5f, k01 = Trip01(tt);
            bool back = Back(tt);
            float h = back ? 0.35f + 0.9f * k01 : 0.3f + 1.6f * Mathf.Sin(Mathf.Clamp01(k01) * Mathf.PI * 0.75f);
            if (IsHome && tt >= HomeAt) h = Mathf.Lerp(0.3f, -Size(k) * 0.9f, Vol2Fs.Smooth(HomeAt + 0.25f, HomeAt + 1.0f, tt));
            return RingPoint(Ang(k), r) + Vector3.up * (h + 0.25f * Vol2Fs.Hash(Seed, k + 90));
        }

        protected override void OnLaunch(long nowUs)
        {
            base.OnLaunch(nowUs);
            for (int k = 0; k < Rocks; k++) { if (Front(Ang(k)) < 0.3f) continue; Vector3 p = RingPoint(Ang(k), R0); for (int i = 0; i < 2; i++) Vol2FsB1.EmitV(_dust, p + Vector3.up * 0.2f, Dir(Ang(k)) * 1.5f + Vector3.up, -1f, -1f); }
        }

        protected override void OnTurn()
        {
            float t = Now(NowUs); HitStop(t, 0.08f);
            for (int k = 0; k < Rocks; k++) { if (Front(Ang(k)) < 0.3f) continue; Vector3 p = RockAt(k, t); for (int i = 0; i < 3; i++) Vol2FsB1.EmitV(_grit, p, Random.insideUnitSphere * 2f, -1f, -1f); Vol2FsB1.EmitV(_chips, p, Random.onUnitSphere * 5f, -1f, -1f); }
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s); HitStop(Now(NowUs), 0.11f);
            for (int k = 0; k < 22; k++) Vol2FsB1.EmitV(_grit, c, (Random.onUnitSphere + FlatN(c - Apex) * 0.6f + Vector3.up * 0.5f).normalized * Random.Range(3f, 7f), -1f, -1f);
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_dust, c + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * 2f, -1f, -1f);
            for (int k = 0; k < 45; k++) Vol2FsB1.EmitV(_chips, c, Random.onUnitSphere * Random.Range(3f, 9f), -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 9; k++) Vol2FsB1.EmitV(_grit, c, (Random.onUnitSphere - FlatN(c - Apex) * 0.5f + Vector3.up * 0.4f).normalized * Random.Range(2f, 5f), -1f, -1f);
            for (int k = 0; k < 4; k++) Vol2FsB1.EmitV(_dust, c + Random.insideUnitSphere * 0.3f, Random.onUnitSphere, -1f, -1f);
            for (int k = 0; k < 18; k++) Vol2FsB1.EmitV(_chips, c, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_chips, s.Pos, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f); }

        protected override void OnHome()
        {
            _landAt = Now(NowUs); HitStop(_landAt, 0.09f);
            for (int k = 0; k < Rocks; k++) { if (Front(Ang(k)) < 0.25f) continue; Vector3 p = RingPoint(Ang(k), R0); for (int i = 0; i < 3; i++) Vol2FsB1.EmitV(_dust, p + Random.insideUnitSphere * 0.3f, (Dir(Ang(k)) + Vector3.up * 0.3f) * Random.Range(0.8f, 2f), -1f, -1f); for (int i = 0; i < 3; i++) Vol2FsB1.EmitV(_grit, p, Random.insideUnitSphere * 3f + Vector3.up * 2f, -1f, -1f); }
        }

        protected override void DrawRing(float t, float r, float k01, bool back, float life)
        {
            var cam = CamPos;
            _trail.Clear(); _dustTrail.Clear(); _plates.Clear();
            float grow = Vol2Fs.EaseOutBack(Mathf.Clamp01((t - LaunchAt) / 0.12f));
            bool gone = _landAt >= 0f && t > _landAt + 1.1f;
            for (int k = 0; k < Rocks; k++)
            {
                var rk = _rocks[k];
                rk.gameObject.SetActive(Launched && !gone);
                if (!Launched || gone) continue;
                Vector3 p = RockAt(k, t);
                rk.SetPositionAndRotation(p, Quaternion.Euler(t * (200f + 90f * k) % 360f, k * 47f + t * 160f, k * 23f));
                rk.localScale = Vector3.one * Size(k) * grow;
                if (IsHome) continue;
                float fr = Front(Ang(k));
                int n = 10; var pts = new Vector3[n]; var wi = new float[n]; var wd = new float[n]; var al = new float[n]; var ad = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float u = i / (float)(n - 1), tt = Mathf.Max(LaunchAt, t - 0.3f * (1f - u));
                    pts[i] = RockAt(k, tt);
                    wi[i] = 0.2f * Size(k) * 2f * Mathf.Max(0.1f, u); wd[i] = 0.5f * (1f - u) + 0.15f;
                    al[i] = life * (0.25f + 0.75f * fr) * u; ad[i] = life * 0.6f * fr * u;
                }
                _trail.Strip(pts, wi, null, Color.white, al, 0.3f + k * 0.05f, cam);
                _dustTrail.Strip(pts, wd, null, Color.white, ad, 0.5f + k * 0.03f, cam);
                if (fr > 0.4f && Time.frameCount % 4 == k % 4) Vol2FsB1.EmitV(_grit, p, Random.insideUnitSphere * 1.5f, -1f, -1f);
            }
            // home: the ground round where they sank lifts in short plates
            if (_landAt >= 0f)
            {
                float a = t - _landAt, lf = Vol2Fs.Smooth(0.2f, 0.45f, a) * (1f - Vol2Fs.Smooth(1.2f, 1.8f, a));
                if (lf > 0.01f)
                    for (int k = 0; k < Rocks; k++)
                    {
                        float ang = Ang(k); if (Front(ang) < 0.25f) continue;
                        Vector3 c = RingPoint(ang, R0 + 0.35f), sd = Vector3.Cross(Vector3.up, Dir(ang));
                        Vector3 p0 = c - sd * 0.3f, p1 = c + sd * 0.3f + Vector3.up * 0.12f * lf;
                        _plates.Strip(new[] { p0 + Vector3.up * 0.03f, p1 }, new[] { 0.28f, 0.22f }, new[] { Dir(ang), Dir(ang) }, Color.white, new[] { lf, lf }, 0.4f + k * 0.07f, cam);
                    }
            }
            _trail.Write(_trailMesh); _dustTrail.Write(_dustMesh); _plates.Write(_plateMesh);
        }
    }
}
