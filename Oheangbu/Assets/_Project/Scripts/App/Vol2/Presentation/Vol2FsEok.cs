using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>억 (water, ㅓ attachment, 남긴다) - free showcase batch 3, concept eok_a (a long ribbon of clear water flows round the caster's arm and spirals off the
    /// brush head like a living WHIP, black ink swirling inside it; the swing lashes the bear with a heavy splash crown). Unlike 어's spinning disc guard it is a
    /// water whip that STAYS ON the weapon for the material's 6 s. Beats, executor facts only: the launch = water wells up the sleeve and the shaft and uncoils off
    /// the bristles -> worn for the Attached material's life (the whip coils and drips; it trails the brush's real motion); a body the swing touched = a splash crown,
    /// spray, the body drips for its 4 s wetness (a mirror) -> the last 0.4 s = the whip thins and drips -> natural expiry (CSV: 아래로 흘러내림) = it loses its shape
    /// and pours DOWN off the brush and the arm onto the ground, a wet patch at the caster's feet.</summary>
    public sealed class Vol2FsEok : Vol2FsWear
    {
        static readonly Color Ink = new Color(0.02f, 0.03f, 0.04f), Foam = new Color(0.9f, 0.95f, 0.95f);
        ParticleSystem _spray, _drops, _foam, _drip, _bodyDrip; Light _light;
        Mesh _whipMesh, _inkMesh, _sheathMesh, _crownMesh, _wetMesh;
        readonly Vol2FsStrips _whip = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _sheath = new Vol2FsStrips(), _crown = new Vol2FsStrips(), _wet = new Vol2FsStrips();
        readonly System.Collections.Generic.List<(Vector3 C, float T)> _crowns = new System.Collections.Generic.List<(Vector3, float)>();
        Vector3 _feet, _lastWhipEnd; bool _exitFx;
        const int N = 22;
        readonly Vector3[] _pts = new Vector3[N]; readonly float[] _w = new float[N], _al = new float[N];
        protected override float ExpireSeconds => 1.2f;
        Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB3.M(tex, tint, i, a);

        protected override void OnBegin()
        {
            var root = Root.transform;
            var water = M("fs_splash", Color.white, 1.6f, 0.3f); water.Ramp = "fs_ramp_water"; water.RampScale = 1.5f; water.NoiseAmt = 0.4f; water.Soft = 0.1f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Life0 = 0.5f, Life1 = 1f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.3f, Size1 = 0.8f, Radius = 0.2f, Gravity = 1.6f, Drag = 0.5f, Tiles = 2,
                SizeStart = 0.4f, SizePeak = 1.2f, SizeEnd = 0.8f, Spin = 120f, Max = 1400 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.8f, 0.95f, 1f), 2.2f, 0.6f)), Life0 = 0.5f, Life1 = 1.2f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.03f, Size1 = 0.08f, Radius = 0.05f, Gravity = 1.8f,
                Stretch = true, StretchVel = 0.03f, Max = 2500 }, root, "Drops");
            var foam = M("fs_puff", Foam, 1.3f, 0.2f); foam.Opacity = 0.7f; foam.Erode = 0.4f; foam.NoiseAmt = 0.6f;
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(foam), Life0 = 0.5f, Life1 = 1f, Speed0 = 0.3f, Speed1 = 1.5f, Size0 = 0.2f, Size1 = 0.5f, Radius = 0.2f, Drag = 1.5f, Tiles = 2, Max = 500 }, root, "Foam");
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.08f, Size1 = 0.18f, Radius = 0.05f, Gravity = 2f, Tiles = 2, Stretch = true, StretchVel = 0.08f, Max = 400 }, root, "Drip");
            _bodyDrip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(water), Rate = 0f, Loop = true, Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.4f, Size0 = 0.1f, Size1 = 0.22f, Radius = 0.5f, Gravity = 2f, Tiles = 2, Stretch = true, StretchVel = 0.08f, Max = 300 }, root, "BodyDrip");
            foreach (var ps in new[] { _spray, _drops, _foam, _drip, _bodyDrip }) ps.Play(false);
            var flow = M("fs_water", Color.white, 1.9f, 0.4f); flow.Ramp = "fs_ramp_water"; flow.RampScale = 1.6f; flow.NoiseAmt = 0.45f; flow.NoiseScale = 1.4f; flow.Erode = 0.22f; flow.Soft = 0.15f; flow.Scroll = new Vector2(-1.8f, 0f); flow.Edge = new Color(0.9f, 1f, 1f, 1f) * 0.6f; flow.EdgeWidth = 0.12f;
            Vol2Fs.MeshObject(root, "Whip", Kit.Mat(flow), out _whipMesh);
            Vol2Fs.MeshObject(root, "Sheath", Kit.Mat(flow), out _sheathMesh);
            Vol2Fs.MeshObject(root, "InkInWhip", Kit.InkSolid("fs_stroke2", Ink, new Color(0.5f, 0.9f, 1f, 1f) * 0.8f, 0.05f, 0.45f, 0.26f, 1.4f), out _inkMesh);
            Vol2Fs.MeshObject(root, "Crowns", Vol2FsB1.Water(Kit, 1.7f, 0.35f, "fs_splash"), out _crownMesh);
            var wet = M("fs_wet", new Color(0.05f, 0.04f, 0.04f, 1f), 1f, 0f); wet.Soft = 0.001f; wet.Opacity = 0.75f; wet.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "Wet", Kit.Mat(wet), out _wetMesh);
            _light = Vol2Fs.PointLight(root, "WaterLight", new Color(0.6f, 0.85f, 1f), 4f);
        }

        protected override void OnCharge(float t, float k) { if (Time.frameCount % 3 == 0) Vol2FsB1.Emit(_drops, Tip(), 1); }
        protected override void OnLaunch(long nowUs) { _feet = Inst.Origin; }
        protected override void OnHit(long nowUs) { Vol2FsB1.Emit(_spray, Tip(), 14); }

        void Lash(Vector3 c) { _crowns.Add((c, Inst.Rel(NowUs))); Vol2FsB1.Emit(_spray, c, 50); Vol2FsB1.Emit(_drops, c, 160); Vol2FsB1.Emit(_foam, c, 20); }
        protected override void OnStruck(Struck s) { Lash(CentreNow(s)); }
        protected override void OnTouch(Struck s) { if (Hits.Exists(h => h.Id == s.Id && Mathf.Abs(h.At - s.At) < 0.05f)) return; Lash(CentreNow(s)); }
        protected override void OnResidueEnd() { _feet = Inst.Origin; if (!NaturalEnd) Vol2FsB1.Emit(_spray, Tip(), 20); }

        /// <summary>The tip's position <paramref name="lag"/> s ago (the whip trails the brush's real motion).</summary>
        Vector3 TipAgo(float t, float lag)
        {
            if (TipPath.Count == 0) return Tip();
            for (int i = TipPath.Count - 1; i >= 0; i--) if (t - TipPath[i].T >= lag) return TipPath[i].P;
            return TipPath[0].P;
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float worn = Worn(t), warn = Warn(NowUs), ex = ExitK(t), pulse = DrawPulse(t);
            Vector3 tip = Tip(), grip = Grip(), sleeve = Sleeve();
            Vector3 ax = (tip - grip).sqrMagnitude > 1e-6f ? (tip - grip).normalized : Vector3.up;
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(tip - cam));
            float uncoil = Vol2Fs.EaseOutBack(Mathf.Clamp01(h / 0.4f));
            float pour = Exiting ? (NaturalEnd ? Vol2Fs.Smooth(0f, 0.8f, ex) : Vol2Fs.Smooth(0f, 0.4f, ex)) : 0f;
            float thin = 1f - 0.45f * warn;
            _whip.Clear(); _ink.Clear(); _sheath.Clear(); _crown.Clear(); _wet.Clear();
            if (worn > 0.02f || (Exiting && ex < 1f))
            {
                float vis = Exiting ? 1f - Vol2Fs.Smooth(0.5f, 1f, ex) : worn;
                // the whip: off the bristles, coiling up and back over the shoulder (2.6 m), trailing the brush's motion; on the end it sags and pours down
                float L = 2.6f * uncoil * thin;
                for (int k = 0; k < N; k++)
                {
                    float u = k / (float)(N - 1);
                    Vector3 b = TipAgo(t, 0.18f * u);
                    Vector3 coil = (side * Mathf.Sin(u * 7.5f + t * 2.6f) + Vector3.up * Mathf.Cos(u * 7.5f + t * 2.6f)) * (0.12f + 0.42f * u) * L / 2.6f;
                    Vector3 p = b + ax * 0.15f * u + Vector3.up * 0.9f * u * L / 2.6f + side * 0.55f * u * L / 2.6f + coil;
                    p += Vector3.down * pour * (1.5f * u + 0.6f) * (0.6f + 0.4f * Mathf.Sin(u * 9f));
                    if (p.y < Ground + 0.05f) p.y = Ground + 0.05f;
                    _pts[k] = p;
                    _w[k] = (0.36f - 0.2f * u) * thin * (1f + 0.25f * pulse) * (1f - 0.6f * pour);
                    _al[k] = vis * (0.95f - 0.35f * u);
                }
                _whip.Strip(_pts, _w, null, Color.white, _al, 0.31f, cam, 0f, 3f);
                _lastWhipEnd = _pts[N - 1];
                // ink swirling inside the whip (a thin dark thread, out of phase)
                for (int k = 0; k < N; k++) { _pts[k] += side * 0.05f * Mathf.Sin(k * 1.3f + t * 6f); _w[k] *= 0.28f; _al[k] *= 0.85f; }
                _ink.Strip(_pts, _w, null, Color.white, _al, 0.77f, cam, 0f, 1f);
                // the sheath: water spiralling up the shaft and round the sleeve
                for (int j = 0; j < 2; j++)
                {
                    var pts = new Vector3[12]; var w = new float[12]; var al = new float[12];
                    Vector3 n0 = Vector3.Cross(ax, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(ax, Vector3.up).normalized : Right;
                    for (int k = 0; k < 12; k++)
                    {
                        float u = k / 11f; float along = Mathf.Lerp(-0.25f, 1f, u);
                        Vector3 c = along < 0f ? Vector3.Lerp(grip, sleeve, -along / 0.25f) : Vector3.LerpUnclamped(grip, tip, along);
                        pts[k] = c + Quaternion.AngleAxis(u * 900f + t * 260f + j * 180f, ax) * n0 * (along < 0f ? 0.13f : 0.07f) + Vector3.down * pour * 0.4f * u;
                        w[k] = (along < 0f ? 0.16f : 0.09f) * thin; al[k] = vis * 0.9f;
                    }
                    _sheath.Strip(pts, w, null, Color.white, al, 0.5f + 0.2f * j, cam, 0f, 2f);
                }
                if (!Exiting && Time.frameCount % (warn > 0f ? 1 : 3) == 0) Vol2FsB1.EmitV(_drip, Vector3.Lerp(grip, tip, Random.value), Vector3.down * 0.3f, -1f, -1f);
                if (!Exiting && Time.frameCount % 5 == 0) Vol2FsB1.EmitV(_drops, _lastWhipEnd, Random.insideUnitSphere * 0.6f, -1f, -1f);
                if (Exiting && NaturalEnd && ex < 0.8f) { Vol2FsB1.EmitV(_drip, Vector3.Lerp(tip, _lastWhipEnd, Random.value), Vector3.down * 1.5f, -1f, -1f); Vol2FsB1.EmitV(_drip, Vector3.Lerp(grip, sleeve, Random.value), Vector3.down, -1f, -1f); }
            }
            if (Exiting && !_exitFx) { _exitFx = true; if (NaturalEnd) Vol2FsB1.Emit(_foam, new Vector3(_feet.x, Ground + 0.05f, _feet.z) + Right * 0.3f, 16); }
            // the wet patch at the caster's feet where the water ran down (natural end)
            if (Exiting && NaturalEnd)
            {
                float wl = Vol2Fs.Smooth(0.25f, 0.6f, ex);
                _wet.Quad(new Vector3(_feet.x, Ground + 0.02f, _feet.z) + Right * 0.35f + Fwd * 0.3f, Right, Fwd, 2.4f, new Color(1f, 1f, 1f, wl), 0.27f);
            }
            // the splash crowns on the struck bodies
            foreach (var c in _crowns)
            {
                float a = t - c.T, l = 1f - Vol2Fs.Smooth(0.15f, 0.7f, a); if (l <= 0.01f) continue;
                Vol2FsB3.Plume(_crown, c.C + Vector3.down * 0.3f, Vector3.up, side, 1.4f * Vol2Fs.EaseOutBack(a / 0.15f), 1.6f, 0.3f, a * 6f, l, 0.53f, cam, 6, 0.2f);
            }
            _whip.Write(_whipMesh); _ink.Write(_inkMesh); _sheath.Write(_sheathMesh); _crown.Write(_crownMesh); _wet.Write(_wetMesh);
            // the struck bodies drip for their 4 s wetness (a mirror of the body action)
            bool drip = false;
            foreach (var s in Hits) if (t - s.At < 4f) { drip = true; _bodyDrip.transform.position = CentreNow(s); }
            var em = _bodyDrip.emission; em.rateOverTime = drip ? 40f : 0f;
            _light.transform.position = Vector3.Lerp(tip, _lastWhipEnd, 0.4f);
            _light.intensity = 2.2f * worn * (1f + pulse);
        }
    }
}
