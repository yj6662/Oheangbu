using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>넝 (fire, ㅓ attachment, 돌아온다) - free showcase batch 7, concept neong_a (a ball of white-orange flame orbits the caster at chest height trailing a burning
    /// ring of fire and ink smoke right round the caster; the close bear catches fire where it passes; the incoming shots burst into embers on the ring). Executor
    /// facts only: the orbiter = a sun of fire (orange-red licking tongues radiating from a small white-hot core) at the executor's orbit point (1.6 m, one turn a
    /// second, 6 s); behind it a thick burning band over two thirds of a turn lying round the caster (a flat orange flame band, a hot core line, a flat ink smoke band
    /// under it), embers flung outward; the first pass = the shared burn (2 s, ash after) + a flare; the second contact (a later turn) = a smaller flare; a shot taken
    /// = a burst of embers. Home (the host's end): the band gutters and burns out into rising ash (CSV 끝: 끝부터 타 재로 날림(재가 일어 위로)).
    /// r2 (r1: the head read as flat white discs with rings, the band thin): no disc - flame plumes round a small core; the band 0.6 m wide.</summary>
    public sealed class Vol2FsNeong : Vol2FsBackOrbit
    {
        static readonly Color Orange = new Color(1f, 0.42f, 0.1f), Hot = new Color(1f, 0.8f, 0.5f);
        Mesh _coreMesh, _bodyMesh, _smokeMesh, _sunMesh, _plumeMesh;
        readonly Vol2FsStrips _core = new Vol2FsStrips(), _body = new Vol2FsStrips(), _smoke = new Vol2FsStrips(), _sun = new Vol2FsStrips(), _plume = new Vol2FsStrips();
        ParticleSystem _licks, _embers, _ash;
        float _homeAt = -1f;
        const float Span = 4.2f;
        protected override Color TrailGlow => new Color(1.6f, 0.6f, 0.18f);

        protected override void OnSetup()
        {
            var root = Root.transform;
            var core = M("fs_streak", Hot * 1.3f, 2.6f, 1f); core.NoiseAmt = 0.3f; core.Soft = 0.03f; core.Erode = 0.12f; core.Scroll = new Vector2(-4f, 0f);
            Vol2Fs.MeshObject(root, "RingCore", Kit.Mat(core), out _coreMesh);
            // r3 (r2: sparse separate spikes, the flat fs_flame band barely showed): a continuous orange-red streak ribbon facing the camera
            var body = M("fs_streak", new Color(1f, 0.38f, 0.1f), 2.2f, 0.9f); body.NoiseAmt = 0.55f; body.Erode = 0.25f; body.Soft = 0.08f; body.Scroll = new Vector2(-3f, 0.2f);
            Vol2Fs.MeshObject(root, "RingFlame", Kit.Mat(body), out _bodyMesh);
            Vol2Fs.MeshObject(root, "RingSmoke", Kit.InkSolid("fs_stroke2", InkC, Orange * 1.4f, 0.16f, 0.55f, 0.35f, 1.3f), out _smokeMesh);
            var sun = M("fs_dot", Hot * 1.2f, 2.2f, 1f); sun.NoiseAmt = 0.4f; sun.Soft = 0.05f; sun.Erode = 0.2f;
            Vol2Fs.MeshObject(root, "SunCore", Kit.Mat(sun), out _sunMesh);
            var plume = M("fs_flame", new Color(1f, 0.35f, 0.08f), 1.6f, 0.8f); plume.NoiseAmt = 0.65f; plume.Erode = 0.32f; plume.Soft = 0.1f; plume.Scroll = new Vector2(0f, -2.5f);
            Vol2Fs.MeshObject(root, "SunPlumes", Kit.Mat(plume), out _plumeMesh);
            _licks = Vol2Fs.Build(Vol2FsPolish.Licks(Kit.Mat(Vol2FsPolish.Lick()), 0.2f, 0.45f, 0.25f, 0.55f, 1.5f, 900), root, "Licks");
            _embers = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(1f, 0.55f, 0.2f), 4f, 1f)), Life0 = 0.6f, Life1 = 1.4f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.02f, Size1 = 0.05f, Gravity = -0.4f, Drag = 0.8f, Noise = 1f, NoiseFreq = 1.5f, Max = 1000 }, root, "Embers");
            _ash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Ash()), Life0 = 1.0f, Life1 = 2.0f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.04f, Size1 = 0.1f, Gravity = -0.3f, Noise = 0.8f, NoiseFreq = 1.2f, Spin = 200f, Max = 400 }, root, "Ash");
            foreach (var ps in new[] { _licks, _embers, _ash }) ps.Play(false);
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s); bool second = s.Kind == "Second";
            for (int k = 0; k < (second ? 10 : 24); k++) Vol2FsB1.EmitV(_licks, c + Random.insideUnitSphere * 0.4f, Vector3.up * Random.Range(1f, 3f) + Random.insideUnitSphere, -1f, -1f);
            for (int k = 0; k < (second ? 30 : 70); k++) Vol2FsB1.EmitV(_embers, c, Random.onUnitSphere * Random.Range(2f, second ? 4.5f : 7f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_embers, s.Pos, Random.onUnitSphere * Random.Range(2f, 6f), -1f, -1f); for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_licks, s.Pos, Vector3.up * 1.5f, -1f, -1f); }

        protected override void OnHome() { _homeAt = Now(NowUs); }

        protected override void DrawOrbiter(float t, Vector3 pos, Vector3 tangent, float psi, float life)
        {
            var cam = CamPos;
            float band = FrontNow > 0.01f ? life / FrontNow : 0f;
            _core.Clear(); _body.Clear(); _smoke.Clear(); _sun.Clear(); _plume.Clear();
            Vector3 c0 = G(Apex), outw = FlatN(pos - c0);
            if (band > 0.01f)
            {
                // the burning band lying round the caster: flat flame, a hot line, flat ink smoke under it, a standing flame skin on top
                Vol2FsOrbitBand.Arc(_smoke, c0, F, Ground, OrbitR, psi, Span, OrbitH - 0.28f, 0.42f, band * 0.95f, 0.12f, cam, false, 44, 0.05f, t);
                Vol2FsOrbitBand.Arc(_body, c0, F, Ground, OrbitR, psi, Span, OrbitH, 0.6f, band, 0.33f, cam, false, 44, 0.07f, t, 0.6f, 0.2f, 0f, 0f, 3f);
                Vol2FsOrbitBand.Arc(_plume, c0, F, Ground, OrbitR + 0.04f, psi, Span * 0.8f, OrbitH + 0.18f, 0.5f, band * 0.85f, 0.71f, cam, false, 40, 0.1f, t, 0.7f, 0.2f, 0f, 0f, 3f);
                Vol2FsOrbitBand.Arc(_core, c0, F, Ground, OrbitR, psi, Span * 0.8f, OrbitH + 0.03f, 0.1f, band, 0.61f, cam, false, 40, 0.06f, t);
            }
            if (life > 0.01f)
            {
                // the sun: orange-red tongues radiating from a small white-hot core (no disc)
                for (int k = 0; k < 9; k++)
                {
                    float a = k / 9f * Mathf.PI * 2f + t * 3f;
                    Vector3 d = (Vector3.up * Mathf.Cos(a) + Vector3.Cross(Vector3.up, FlatN(cam - pos)) * Mathf.Sin(a)).normalized;
                    d = (d + Vector3.up * 0.35f - tangent * 0.4f).normalized;
                    float len = 0.55f + 0.25f * Mathf.Sin(t * 17f + k * 2.1f);
                    var pts = new[] { pos + d * 0.05f, pos + d * len * 0.5f + Random.insideUnitSphere * 0.03f, pos + d * len };
                    _plume.Strip(pts, new[] { 0.28f, 0.22f, 0.04f }, null, Color.white, new[] { life, life, life * 0.6f }, 0.1f * k, cam);
                }
                Vol2FsB5.Halo(_sun, pos, FlatN(cam - pos), 0.05f, 0.1f, life, 0.4f, cam, 14, t * 4f);
                if (Random.value < FrontNow) Vol2FsB1.EmitV(_licks, pos + Random.insideUnitSphere * 0.2f, Vector3.up * Random.Range(0.6f, 1.6f) - tangent * 1.5f, -1f, -1f);
                if (Time.frameCount % 2 == 0 && Random.value < FrontNow) Vol2FsB1.EmitV(_embers, pos, outw * Random.Range(1f, 3f) + Random.insideUnitSphere + Vector3.up, -1f, -1f);
                for (int k = 0; k < 3; k++) { float a = psi - Random.Range(0.1f, Span * 0.75f); if (Vol2FsOrbitBand.FrontOf(a) > 0.4f) Vol2FsB1.EmitV(_licks, OrbitPoint(a, OrbitH + 0.2f), Vector3.up * Random.Range(0.6f, 1.4f), -1f, -1f); }
            }
            if (_homeAt >= 0f)
            {
                float a = t - _homeAt;
                if (a < 0.8f && Time.frameCount % 2 == 0) for (int k = 0; k < 6; k++) { float b = psi - Random.Range(0f, Span); if (Vol2FsOrbitBand.FrontOf(b) > 0.35f) Vol2FsB1.EmitV(_ash, OrbitPoint(b, OrbitH), Vector3.up * Random.Range(0.5f, 1.4f) + Random.insideUnitSphere * 0.3f, -1f, -1f); }
            }
            _core.Write(_coreMesh); _body.Write(_bodyMesh); _smoke.Write(_smokeMesh); _sun.Write(_sunMesh); _plume.Write(_plumeMesh);
        }

        protected override float Tail => 1.2f;
    }
}
