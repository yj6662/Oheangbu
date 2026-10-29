using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>망 (earth, ㅏ path, 돌아온다) - free showcase batch 7, concept mang_b (a big rock on a chain of packed earth and ink swings out along the path like a meteor
    /// hammer, crashes into the bears and is yanked back to the brush, a curved dust trail tracing the out-and-back loop). Executor facts only: the head = a spinning
    /// chipped boulder (0.5 m) at the executor's head, bound to the brush tip by a sagging chain of ink with an amber edge (taut on the way out, slack and snapping
    /// on the way back); grit and dust stream off it along the trail it flew (the base's loop on the ground + a dust trail in the air); at the turn it stops dead
    /// (hit-stop) and the chain snaps taut; the first touch = rock burst + the shared push (the body's own skid); the return touch = a smaller crack of grit (x0.5);
    /// a shot taken = it shatters on the rock. Home: the rock crumbles into the brush, its pieces dropping (CSV 끝: 가라앉으며 둘레 땅이 판으로 들림 - the
    /// ground's plates are the shared push's end).</summary>
    public sealed class Vol2FsMang : Vol2FsBackPath
    {
        static readonly Color Amber = new Color(0.95f, 0.62f, 0.3f);
        Transform _rock; Mesh _chainMesh, _chainGlowMesh, _dustTrailMesh;
        readonly Vol2FsStrips _chain = new Vol2FsStrips(), _chainGlow = new Vol2FsStrips(), _dustTrail = new Vol2FsStrips();
        ParticleSystem _grit, _dust, _chips;
        bool _crumbled;
        protected override Color LoopGlow => new Color(1.4f, 0.85f, 0.4f);
        protected override float LoopWidth => 0.22f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            var stone = Kit.Stone("fs_rock", new Color(0.62f, 0.52f, 0.44f), Amber * 0.6f, "fs_vein") ?? Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.6f, 0.5f, 0.42f), 0f, 0.12f);
            _rock = Vol2FsB1.Prop(root, "Boulder", Kit.SharedMesh("mangRock", () => Vol2FsB1.Rock(733, 0.5f, 0.85f)), stone).transform;
            _rock.gameObject.SetActive(false);
            Vol2Fs.MeshObject(root, "Chain", Kit.InkSolid("fs_stroke2", InkC, Amber * 1.5f, 0.2f, 0.4f, 0.3f, 1.5f), out _chainMesh);
            var g = M("fs_streak", Amber * 1.5f, 2.2f, 1f); g.NoiseAmt = 0.3f; g.Soft = 0.04f; g.Erode = 0.15f; g.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "ChainGlow", Kit.Mat(g), out _chainGlowMesh);
            var dt = Vol2FsB3.Dust(new Color(0.5f, 0.38f, 0.28f, 1f), 0.5f);
            Vol2Fs.MeshObject(root, "DustTrail", Kit.Mat(dt), out _dustTrailMesh);
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.58f, 0.5f, 0.44f), 0f, 0.12f), Mesh = Kit.SharedMesh("mangGrit", () => Vol2FsB1.Rock(17, 0.5f)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.05f, Size1 = 0.15f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 400 }, root, "Grit");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.48f, 0.34f, 0.25f, 1f), 0.55f)), Life0 = 0.7f, Life1 = 1.5f, Speed0 = 0.3f, Speed1 = 2f, Size0 = 0.35f, Size1 = 0.8f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.3f, Max = 400 }, root, "Dust");
            _chips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Amber, 3.5f, 1f)), Life0 = 0.3f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.6f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 500 }, root, "Chips");
            foreach (var ps in new[] { _grit, _dust, _chips }) ps.Play(false);
        }

        protected override void Run(float t, float along, float prev, bool back)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(along - prev) * 1.5f), 1, 10);
            for (int k = 0; k < n; k++)
            {
                Vector3 p = TripPoint(Mathf.Lerp(prev, along, (k + 0.5f) / n), back);
                Vol2FsB1.EmitV(_dust, p + Random.insideUnitSphere * 0.25f, Random.insideUnitSphere * 0.6f, -1f, -1f);
                if (k % 2 == 0) Vol2FsB1.EmitV(_grit, p, Random.insideUnitSphere * 2f + Vector3.up, -1f, -1f);
            }
        }

        protected override void OnTurn()
        {
            Vector3 p = HeadPos(Now(NowUs));
            for (int k = 0; k < 16; k++) Vol2FsB1.EmitV(_dust, p + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * 1.5f, -1f, -1f);
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_chips, p, Random.onUnitSphere * Random.Range(3f, 8f), -1f, -1f);
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s); HitStop(Now(NowUs), 0.11f);
            for (int k = 0; k < 24; k++) Vol2FsB1.EmitV(_grit, c, (Random.onUnitSphere + F * 0.6f + Vector3.up * 0.5f).normalized * Random.Range(3f, 7f), -1f, -1f);
            for (int k = 0; k < 12; k++) Vol2FsB1.EmitV(_dust, c + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * 2f, -1f, -1f);
            for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_chips, c, Random.onUnitSphere * Random.Range(3f, 9f), -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_grit, c, (Random.onUnitSphere - F * 0.5f + Vector3.up * 0.4f).normalized * Random.Range(2f, 5f), -1f, -1f);
            for (int k = 0; k < 5; k++) Vol2FsB1.EmitV(_dust, c + Random.insideUnitSphere * 0.3f, Random.onUnitSphere, -1f, -1f);
            for (int k = 0; k < 20; k++) Vol2FsB1.EmitV(_chips, c, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_chips, s.Pos, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f); }

        protected override void OnHome()
        {
            if (_crumbled) return; _crumbled = true;
            Vector3 p = _rock.position;
            for (int k = 0; k < 20; k++) Vol2FsB1.EmitV(_grit, p + Random.insideUnitSphere * 0.3f, Random.insideUnitSphere * 1.5f, -1f, -1f);
            for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_dust, p, Random.onUnitSphere * 0.8f, -1f, -1f);
        }

        protected override void DrawHead(float t, Vector3 pos, Vector3 dir, bool back, float life)
        {
            var cam = CamPos;
            _chain.Clear(); _chainGlow.Clear(); _dustTrail.Clear();
            bool show = life > 0.01f;
            _rock.gameObject.SetActive(show);
            if (show)
            {
                _rock.SetPositionAndRotation(pos, Quaternion.AngleAxis(t * 900f, Vector3.Cross(Vector3.up, dir)) * Quaternion.Euler(Seed % 90, Seed % 37, 0f));
                _rock.localScale = Vector3.one * (Vol2Fs.EaseOutBack(Mathf.Clamp01((t - LaunchAt) / 0.1f)) * 1.45f);
                // the chain: brush tip -> rock; taut out, a sagging snapping arc back
                Vector3 tip = Tip();
                float sag = back ? 0.6f + 0.25f * Mathf.Sin(t * 22f) : 0.12f;
                int n = 24; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var gw = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float u = k / (float)(n - 1);
                    pts[k] = Vector3.Lerp(tip, pos, u) + Vector3.down * sag * 4f * u * (1f - u) + Vector3.Cross(dir, Vector3.up) * 0.08f * Mathf.Sin(u * 25f + t * 30f) * (back ? 1f : 0.3f);
                    w[k] = 0.07f + 0.04f * Mathf.Abs(Mathf.Sin(u * 40f)); al[k] = life; gw[k] = 0.025f;
                }
                _chain.Strip(pts, w, null, Color.white, al, 0.3f, cam);
                _chainGlow.Strip(pts, gw, null, Color.white, al, 0.7f, cam);
                // the dust trail in the air behind the rock
                var tr = TrailPoints(t, 0.45f, 24);
                if (tr.Count >= 2)
                {
                    var tw = new float[tr.Count]; var ta = new float[tr.Count];
                    for (int k = 0; k < tr.Count; k++) { float u = k / (float)(tr.Count - 1); tw[k] = 0.25f + 0.55f * (1f - u); ta[k] = life * 0.8f * u; }
                    _dustTrail.Strip(tr, tw, null, Color.white, ta, 0.45f, cam);
                }
            }
            _chain.Write(_chainMesh); _chainGlow.Write(_chainGlowMesh); _dustTrail.Write(_dustTrailMesh);
        }
    }
}
