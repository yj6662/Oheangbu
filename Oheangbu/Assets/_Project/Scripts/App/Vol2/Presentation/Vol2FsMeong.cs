using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>멍 (earth, ㅓ attachment, 돌아온다) - free showcase batch 7, concept meong_b (a dense ring of whirling sand and grit spins round the caster like a narrow
    /// sandstorm belt with a stone tip leading it, amber-lit streaks in the dust; the close bear is shoved back). Executor facts only: the orbiter = a chipped
    /// stone tip at the executor's orbit point (1.6 m, one turn a second, 6 s) dragging a belt of sand over the last two thirds of the circle (layered dust bands,
    /// amber streaks, ink grit) with grit tumbling off it; the first pass = the shared push (the body's own skid; the push takes the bear off the band, so there
    /// is no second contact - honest) + a burst of rock; a later second contact, if the rules give one, = a smaller burst; a shot taken = it shatters into grit.
    /// Home (the host's end): the belt drops its sand, and the ground round the caster lifts in plates that sink again (CSV 끝: 가라앉으며 둘레 땅이 판으로 들림).</summary>
    public sealed class Vol2FsMeong : Vol2FsBackOrbit
    {
        static readonly Color Amber = new Color(0.95f, 0.62f, 0.3f);
        Transform _tip; Mesh _sandMesh, _streakMesh, _inkMesh, _litMesh;
        readonly Vol2FsStrips _sand = new Vol2FsStrips(), _streak = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _lit = new Vol2FsStrips();
        ParticleSystem _grit, _dust, _chips;
        readonly List<Transform> _plates = new List<Transform>();
        float _homeAt = -1f;
        protected override Color TrailGlow => new Color(1.4f, 0.85f, 0.4f);
        protected override float TrailArc => 4.2f;
        protected override float TrailWidth => 0.12f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            var stone = Kit.Stone("fs_rock", new Color(0.62f, 0.52f, 0.44f), Amber * 0.6f, "fs_vein") ?? Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.6f, 0.5f, 0.42f), 0f, 0.12f);
            _tip = Vol2FsB1.Prop(root, "StoneTip", Kit.SharedMesh("meongTip", () => Vol2FsB1.Rock(911, 0.5f, 0.7f)), stone).transform;
            _tip.gameObject.SetActive(false);
            Vol2Fs.MeshObject(root, "SandBelt", Kit.Mat(Vol2FsB3.Dust(new Color(0.62f, 0.45f, 0.3f, 1f), 0.85f)), out _sandMesh);
            var s = M("fs_streak", Amber * 1.5f, 2.4f, 1f); s.NoiseAmt = 0.3f; s.Soft = 0.04f; s.Erode = 0.15f; s.Scroll = new Vector2(-3f, 0f);
            Vol2Fs.MeshObject(root, "SandStreaks", Kit.Mat(s), out _streakMesh);
            var lit = M("fs_puff", new Color(1f, 0.6f, 0.28f, 1f), 2.0f, 0.65f); lit.NoiseAmt = 0.6f; lit.Erode = 0.3f; lit.Soft = 0.15f; lit.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "SandLit", Kit.Mat(lit), out _litMesh);
            Vol2Fs.MeshObject(root, "SandInk", Kit.InkSolid("fs_stroke2", InkC, Amber * 1.4f, 0.16f, 0.5f, 0.33f, 1.4f), out _inkMesh);
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.58f, 0.5f, 0.44f), 0f, 0.12f), Mesh = Kit.SharedMesh("meongGrit", () => Vol2FsB1.Rock(17, 0.5f)), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.04f, Size1 = 0.12f, Gravity = 2.2f, Drag = 0.4f, Spin = 500f, Max = 500 }, root, "Grit");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.48f, 0.34f, 0.25f, 1f), 0.55f)), Life0 = 0.7f, Life1 = 1.5f, Speed0 = 0.3f, Speed1 = 1.8f, Size0 = 0.35f, Size1 = 0.8f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.3f, Max = 400 }, root, "Dust");
            _chips = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Amber, 3.5f, 1f)), Life0 = 0.3f, Life1 = 0.7f, Speed0 = 3f, Speed1 = 9f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.6f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 600 }, root, "Chips");
            foreach (var ps in new[] { _grit, _dust, _chips }) ps.Play(false);
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s); bool second = s.Kind == "Second";
            if (!second) HitStop(Now(NowUs), 0.11f);
            Vector3 o = FlatN(c - G(Apex));
            for (int k = 0; k < (second ? 10 : 24); k++) Vol2FsB1.EmitV(_grit, c, (Random.onUnitSphere + o * 0.7f + Vector3.up * 0.5f).normalized * Random.Range(3f, 7f), -1f, -1f);
            for (int k = 0; k < (second ? 5 : 12); k++) Vol2FsB1.EmitV(_dust, c + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * 2f, -1f, -1f);
            for (int k = 0; k < (second ? 20 : 50); k++) Vol2FsB1.EmitV(_chips, c, Random.onUnitSphere * Random.Range(3f, 9f), -1f, -1f);
        }

        protected override void OnShot(Shot s)
        {
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_chips, s.Pos, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f);
            for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_grit, s.Pos, Random.onUnitSphere * 3f, -1f, -1f);
        }

        protected override void OnHome()
        {
            float t = Now(NowUs); _homeAt = t;
            for (int k = 0; k < 36; k++) { Vector3 p = OrbitPoint(Random.Range(0f, Vol2ReturnGeometry.Tau), OrbitH); Vol2FsB1.EmitV(_grit, p, Random.insideUnitSphere * 0.8f, -1f, -1f); if (k % 3 == 0) Vol2FsB1.EmitV(_dust, G(p) + Vector3.up * 0.2f, Random.insideUnitSphere * 0.8f, -1f, -1f); }
            var mat = Kit.Stone("fs_rock", new Color(0.5f, 0.38f, 0.3f), Color.black, null) ?? Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.5f, 0.38f, 0.3f), 0f, 0.12f);
            for (int k = 0; k < 10; k++)
            {
                var pr = Vol2FsB1.Prop(Root.transform, "Plate" + k, Kit.SharedMesh("meongPlate" + (k % 3), () => Vol2FsB1.Slab(540 + k % 3, new Vector3(0.7f, 0.12f, 0.5f), 0.95f)), mat);
                _plates.Add(pr.transform);
            }
        }

        protected override void DrawOrbiter(float t, Vector3 pos, Vector3 tangent, float psi, float life)
        {
            var cam = CamPos;
            _sand.Clear(); _streak.Clear(); _ink.Clear(); _lit.Clear();
            _tip.gameObject.SetActive(life > 0.01f);
            if (life > 0.01f)
            {
                _tip.SetPositionAndRotation(pos, Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.AngleAxis(t * 700f, Vector3.forward));
                _tip.localScale = new Vector3(1.0f, 0.85f, 1.5f) * Vol2Fs.EaseOutBack(Mathf.Clamp01((t - LaunchAt) / 0.12f));      // r2: was 0.75 / 0.6 / 1.1 (lost in the dust)
                if (Random.value < FrontNow) for (int k = 0; k < 2; k++) Vol2FsB1.EmitV(_grit, pos - tangent * 0.3f + Random.insideUnitSphere * 0.25f, -tangent * Random.Range(0.5f, 2f) + Random.insideUnitSphere, -1f, -1f);
                if (Time.frameCount % 2 == 0 && Random.value < FrontNow) Vol2FsB1.EmitV(_dust, pos - tangent * 0.5f, -tangent * 0.8f + Random.insideUnitSphere * 0.4f, -1f, -1f);
                if (Random.value < FrontNow) Vol2FsB1.EmitV(_chips, pos - tangent * 0.2f, -tangent * Random.Range(2f, 5f) + Random.insideUnitSphere * 1.5f, -1f, -1f);
            }
            // r2 (r1 read as thin brown dots): a dense belt lying round the caster over two thirds of a turn - flat sand layers, amber-lit sand, hot streaks, an ink band
            float band = FrontNow > 0.01f ? life / FrontNow : 0f;
            if (band > 0.01f)
            {
                Vector3 c0 = G(Apex);
                // r3 (r2: the flat layers read edge-on from the shoulder - still thin): camera-facing layers ~0.6 m thick
                Vol2FsOrbitBand.Arc(_ink, c0, F, Ground, OrbitR, psi, 4.4f, OrbitH - 0.28f, 0.45f, band * 0.95f, 0.3f, cam, false, 44, 0.05f, t);
                Vol2FsOrbitBand.Arc(_sand, c0, F, Ground, OrbitR + 0.04f, psi, 4.4f, OrbitH - 0.05f, 0.65f, band, 0.2f, cam, false, 44, 0.06f, t, 0.55f);
                Vol2FsOrbitBand.Arc(_sand, c0, F, Ground, OrbitR - 0.06f, psi, 3.8f, OrbitH + 0.15f, 0.5f, band * 0.9f, 0.45f, cam, false, 40, 0.08f, t, 0.6f);
                Vol2FsOrbitBand.Arc(_lit, c0, F, Ground, OrbitR, psi, 4.0f, OrbitH + 0.02f, 0.55f, band * 0.9f, 0.65f, cam, false, 44, 0.07f, t, 0.55f);
                for (int k = 0; k < 3; k++) Vol2FsOrbitBand.Arc(_streak, c0, F, Ground, OrbitR - 0.12f + 0.12f * k, psi, 3.6f - 0.4f * k, OrbitH - 0.18f + 0.18f * k, 0.06f, band, 0.3f + 0.2f * k, cam, false, 40, 0.05f, t + k);
                // grit thrown off the belt where it passes in front of the camera
                for (int k = 0; k < 6; k++) { float a = psi - Random.Range(0.1f, 3.6f); if (Vol2FsOrbitBand.FrontOf(a) < 0.5f) continue; Vector3 p = OrbitPoint(a, OrbitH); Vol2FsB1.EmitV(_grit, p + Random.insideUnitSphere * 0.2f, -Tangent(a) * Random.Range(0.5f, 1.5f) + Random.insideUnitSphere * 0.6f, -1f, -1f); }
            }
            if (_homeAt >= 0f)
            {
                float a = t - _homeAt, rise = Vol2Fs.EaseOutBack(Mathf.Clamp01(a / 0.25f)) * (1f - Vol2Fs.Smooth(0.7f, 1.3f, a));
                for (int k = 0; k < _plates.Count; k++)
                {
                    float ang = k / (float)_plates.Count * Vol2ReturnGeometry.Tau + Seed % 7;
                    Vector3 p = G(Apex + Dir(ang) * (OrbitR + 0.2f * Vol2Fs.Hash(Seed, k)));
                    _plates[k].SetPositionAndRotation(p + Vector3.up * (0.12f * rise - 0.12f), Quaternion.LookRotation(Dir(ang), Vector3.up) * Quaternion.Euler(-25f * rise, 0f, 8f * Vol2Fs.Hash(Seed, k + 9)));
                    _plates[k].gameObject.SetActive(rise > 0.01f);
                }
            }
            _sand.Write(_sandMesh); _streak.Write(_streakMesh); _ink.Write(_inkMesh); _lit.Write(_litMesh);
        }

        protected override float Tail => 1.4f;
    }
}
