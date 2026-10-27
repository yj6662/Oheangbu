using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>엉 (water, ㅓ attachment, 돌아온다 - the PEAK) - free showcase batch 7, concept eong_a (a ring of clear water circles the caster; each dark shot that touches
    /// it is swallowed and SHOT BACK along a curving watery arc to the far bear who fired it, which is hit with a splash; one shot is just dropped). Executor facts
    /// only: the orbiter = a bulb of clear water with a foam crest at the executor's orbit point (1.6 m, one turn a second, 6 s), dragging a heavy band of water
    /// (foam rim, ink threads) over the last half-turn and a thin standing ring of water round the whole circle; a shot it takes (the report's Intercepts, where the
    /// shot last flew) is wrapped in water at once; if the rules turned it back (Reflected: three at most - the borrowed ledger) it flies back along a high arc from
    /// there to its shooter over the executor's own flight time (distance / ReflectSpeed, capped) as a dark ink orb in a water sheath with a spray trail, and bursts
    /// on the shooter in a splash crown when the Reflect record lands (the borrowed damage); the fourth (ReflectionCount) only drops in a splash. The bears the
    /// ring passes are soaked (the shared wet action, 4 s). Home (the host's end): the ring loses its shape and runs down to the ground in sheets and drops,
    /// leaving a wet ring (CSV 끝: 아래로 흘러내림).</summary>
    public sealed class Vol2FsEong : Vol2FsBackOrbit
    {
        static readonly Color Teal = new Color(0.35f, 0.85f, 1f);
        Mesh _waterMesh, _foamMesh, _inkMesh, _boltMesh, _wetMesh;
        readonly Vol2FsStrips _water = new Vol2FsStrips(), _foam = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _bolt = new Vol2FsStrips(), _wet = new Vol2FsStrips();
        ParticleSystem _spray, _drops, _mist;
        Material _orbMat;
        sealed class Back { public Shot S; public Transform Orb; public Vector3 To; public bool Burst; }
        readonly List<Back> _backs = new List<Back>();
        float _homeAt = -1f;
        protected override Color TrailGlow => new Color(0.55f, 1.3f, 1.6f);
        protected override float TrailArc => 3.1f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "WaterBand", Vol2FsB1.Water(Kit, 1.7f, 0.4f), out _waterMesh);
            var foam = M("fs_streak", new Color(0.9f, 1f, 1f), 2.6f, 0.9f); foam.NoiseAmt = 0.3f; foam.Soft = 0.05f; foam.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "Foam", Kit.Mat(foam), out _foamMesh);
            Vol2Fs.MeshObject(root, "InkThreads", Kit.InkSolid("fs_stroke2", InkC, Teal * 1.2f, 0.08f, 0.45f, 0.32f, 1.3f), out _inkMesh);
            Vol2Fs.MeshObject(root, "ReturnArcs", Vol2FsB1.Water(Kit, 1.9f, 0.5f), out _boltMesh);
            var wet = Vol2FsB1.M("fs_wet", new Color(0.1f, 0.16f, 0.2f, 1f), 1f, 0f); wet.Opacity = 0.6f; wet.NoiseAmt = 0.3f; wet.Soft = 0.2f;
            Vol2Fs.MeshObject(root, "WetRing", Kit.Mat(wet), out _wetMesh);
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.45f, "fs_splash"), Life0 = 0.35f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.32f, Gravity = 1.8f, Drag = 0.6f, Max = 1400 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.7f, 0.95f, 1f), 2.6f, 0.75f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.025f, Size1 = 0.06f, Gravity = 2.4f, Drag = 0.3f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 1400 }, root, "Drops");
            var mist = M("fs_puff", new Color(0.75f, 0.9f, 1f, 1f), 1f, 0.3f); mist.Opacity = 0.35f; mist.NoiseAmt = 0.5f; mist.Erode = 0.3f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.4f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.5f, Drag = 1.2f, Max = 200 }, root, "Mist");
            foreach (var ps in new[] { _spray, _drops, _mist }) ps.Play(false);
            _orbMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.06f, 0.06f, 0.08f), 0f, 0.6f);
        }

        Vector3 ShooterPoint(Back b) => b.S.Shooter != null ? BodyBounds(b.S.Shooter, b.S.Shooter.position, 0.5f).center : b.To;

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s); bool second = s.Kind == "Second";
            for (int k = 0; k < (second ? 40 : 90); k++) Vol2FsB1.EmitV(_spray, c + Random.insideUnitSphere * 0.4f, (Random.onUnitSphere + Vector3.up * 0.9f).normalized * Random.Range(2f, second ? 4.5f : 6f), -1f, -1f);
        }

        protected override void OnShot(Shot s)
        {
            for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_spray, s.Pos, Random.onUnitSphere * Random.Range(1.5f, 5f), -1f, -1f);
            if (!s.Reflected) { for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_drops, s.Pos, Random.insideUnitSphere * 2f + Vector3.down * 1.5f, -1f, -1f); return; }
            var b = new Back { S = s, To = s.Pos + F * 6f };
            b.Orb = Vol2FsB1.Prop(Root.transform, "TurnedShot" + s.Index, Kit.SharedMesh("eongOrb", () => Vol2FsB1.Rock(77, 0.5f, 1f, 12, 8)), _orbMat, false).transform;
            b.Orb.localScale = Vector3.one * 0.32f; b.Orb.position = s.Pos;
            _backs.Add(b);
            HitStop(Now(NowUs), 0.07f);
        }

        protected override void OnReflectHit(Shot s, Struck rec)
        {
            foreach (var b in _backs) if (b.S == s && !b.Burst) Burst(b, rec.T != null ? BodyBounds(rec.T, rec.T.position, 0.5f).center : ShooterPoint(b));
        }

        void Burst(Back b, Vector3 at)
        {
            b.Burst = true; if (b.Orb != null) b.Orb.gameObject.SetActive(false);
            HitStop(Now(NowUs), 0.08f);
            for (int k = 0; k < 140; k++) Vol2FsB1.EmitV(_spray, at + Random.insideUnitSphere * 0.3f, (Random.onUnitSphere + Vector3.up * 1f).normalized * Random.Range(2.5f, 7f), -1f, -1f);
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_drops, at, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(2f, 5f), -1f, -1f);
            for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_mist, at + Random.insideUnitSphere * 0.4f, Random.onUnitSphere, -1f, -1f);
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(BookInk, at, (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(2f, 5f), -1f, -1f);
            FlashLight(at, 5f, Now(NowUs) + 0.3f);
        }

        protected override void OnHome() { _homeAt = Now(NowUs); }

        static Vector3 ArcPoint(Vector3 a, Vector3 b, float u) => Vector3.Lerp(a, b, u) + Vector3.up * 4f * 1.3f * u * (1f - u);

        protected override void DrawOrbiter(float t, Vector3 pos, Vector3 tangent, float psi, float life)
        {
            var cam = CamPos;
            _water.Clear(); _foam.Clear(); _ink.Clear(); _bolt.Clear(); _wet.Clear();
            float runDown = _homeAt >= 0f ? Vol2Fs.Smooth(0f, 0.6f, t - _homeAt) : 0f;
            if (life > 0.01f)
            {
                // the thin standing ring of water round the whole circle
                int nr = 64; var rp = new Vector3[nr + 1]; var rw = new float[nr + 1]; var ra = new float[nr + 1];
                for (int k = 0; k <= nr; k++) { float a = k / (float)nr * Vol2ReturnGeometry.Tau; rp[k] = OrbitPoint(a, OrbitH - 0.9f * runDown + 0.04f * Mathf.Sin(a * 6f + t * 5f)); rw[k] = 0.1f; ra[k] = life * (0.35f + 0.45f * Front(a)); }
                _water.Strip(rp, rw, null, Color.white, ra, 0.21f, cam, 0f, 6f);
                // r2 (r1 read thin): the heavy band behind the bulb over two thirds of a turn - a flat water sheet lying round the caster, a standing water skin,
                // a foam rim and two ink threads (Vol2FsOrbitBand: each point fades by where it is, not by where the bulb is)
                float band = FrontNow > 0.01f ? life / FrontNow : 0f;
                Vector3 c0 = G(Apex);
                float h = OrbitH - 0.9f * runDown;
                Vol2FsOrbitBand.Arc(_water, c0, F, Ground, OrbitR, psi, 4.4f, h - 0.04f, 0.62f, band, 0.37f, cam, true, 46, 0.06f, t, 0.6f, 0.2f, 0f, 0f, 3f);
                Vol2FsOrbitBand.Arc(_water, c0, F, Ground, OrbitR + 0.04f, psi, 3.6f, h + 0.12f, 0.36f, band * 0.85f, 0.52f, cam, false, 40, 0.08f, t, 0.7f, 0.2f, 0f, 0f, 3f);
                Vol2FsOrbitBand.Arc(_foam, c0, F, Ground, OrbitR + 0.3f, psi, 3.8f, h + 0.02f, 0.07f, band, 0.71f, cam, true, 40, 0.06f, t);
                Vol2FsOrbitBand.Arc(_foam, c0, F, Ground, OrbitR - 0.28f, psi, 3.2f, h + 0.0f, 0.05f, band * 0.8f, 0.31f, cam, true, 40, 0.06f, t + 1f);
                for (int j = 0; j < 2; j++) Vol2FsOrbitBand.Arc(_ink, c0, F, Ground, OrbitR - 0.12f + 0.24f * j, psi, 3.6f, h + 0.03f + 0.04f * Mathf.Sin(t * 8f + j * 2f), 0.06f, band, 0.2f + 0.4f * j, cam, true, 40, 0.07f, t + j * 2f);                // the bulb with a foam crest
                Vol2FsB4A.Wrap(_water, pos, 0.36f, 0.36f, pos.y, 0f, Mathf.PI * 2f, 0.55f, life, 0.5f, cam, 0f, 20);
                Vector3 outw = FlatN(pos - G(Apex));
                Vol2FsB6.Slash(_foam, pos - outw * 0.35f + Vector3.up * 0.22f, pos + outw * 0.35f + Vector3.up * 0.22f, tangent * 0.3f + Vector3.up * 0.15f, 0.14f, 1f, life, 0.9f, cam);
                if (runDown <= 0f && Random.value < FrontNow) Vol2FsB1.EmitV(_spray, pos, -tangent * 2f + outw * Random.Range(0.5f, 2f) + Random.insideUnitSphere, -1f, -1f);
            }
            // the turned shots: back along a high arc from where they were taken to their shooter
            foreach (var b in _backs)
            {
                if (b.Burst) continue;
                Vector3 to = ShooterPoint(b);
                float dur = Mathf.Max(0.08f, b.S.ArriveAt - b.S.At), u = Mathf.Clamp01((t - b.S.At) / dur);
                Vector3 at = ArcPoint(b.S.Pos, to, u);
                if (b.Orb != null) { b.Orb.position = at; b.Orb.rotation = Quaternion.Euler(t * 400f, t * 300f, 0f); }
                Vol2FsB4A.Wrap(_bolt, at, 0.24f, 0.24f, at.y, t * 8f, Mathf.PI * 2f, 0.2f, 0.95f, 0.4f, cam, 0f, 14);
                int m = 16; var bp = new Vector3[m]; var bw = new float[m]; var ba = new float[m];
                for (int k = 0; k < m; k++) { float v = Mathf.Max(0f, u - 0.55f * k / (m - 1f)); bp[k] = ArcPoint(b.S.Pos, to, v); bw[k] = 0.22f * (1f - k / (float)m); ba[k] = 0.95f * (1f - k / (float)m); }
                _bolt.Strip(bp, bw, null, Color.white, ba, 0.6f, cam);
                if (Time.frameCount % 1 == 0) Vol2FsB1.EmitV(_drops, at, Random.insideUnitSphere * 1.5f, -1f, -1f);
                // the Reflect record normally lands at the arrival; if the rules booked nothing to deliver (no damage request), it bursts where it arrives
                float hit = b.S.HitAt >= 0f ? b.S.HitAt : b.S.ArriveAt + 0.25f;
                if (t >= hit) Burst(b, to);
            }
            if (_homeAt >= 0f)
            {
                float a = t - _homeAt;
                if (a < 0.7f && Time.frameCount % 1 == 0) for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_drops, OrbitPoint(Random.Range(0f, Vol2ReturnGeometry.Tau), OrbitH * (1f - Vol2Fs.Smooth(0f, 0.6f, a))), Vector3.down * Random.Range(1f, 2.5f), -1f, -1f);
                float wl = Vol2Fs.Smooth(0.2f, 0.6f, a) * (1f - Vol2Fs.Smooth(1.2f, 2.0f, a));
                if (wl > 0.01f) Vol2FsB4A.GroundArc(_wet, G(Apex), Ground, OrbitR, 0.5f, 0f, Mathf.PI * 2f, wl, 0.4f, cam, 48);
            }
            _water.Write(_waterMesh); _foam.Write(_foamMesh); _ink.Write(_inkMesh); _bolt.Write(_boltMesh); _wet.Write(_wetMesh);
        }

        protected override float Tail => 2.0f;
    }
}
