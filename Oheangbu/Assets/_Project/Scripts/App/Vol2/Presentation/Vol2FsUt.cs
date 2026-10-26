using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>웃 (water, ㅜ ring, 가른다) - free showcase batch 6, concept ut_b (a thin line of water races round the ring at high speed, cutting a channel into the
    /// ground, black ink streaming in it; the bear crossing it is slashed by a spray of water, its armour blasted off; the wall on the ring cut through). Executor
    /// facts only: the line's launch = the water races round the band from the front (the base's race) in a cut channel - clear blue-green, foam lip; the 10 s
    /// line stands as a racing stream (ink dashes and glints running round it, spray thrown up off the front arc; the template's warning: the stream slows and
    /// sputters); every Instant (the bear on the band at the launch, the walker at 1.0 s) = a sheet of water bursts up where it crossed (the shared wet soaks
    /// it); the emitter's EndUs = the stream drains down into the channel and leaves a dark wet ring that dries (CSV 끝: 아래로 흘러내림).</summary>
    public sealed class Vol2FsUt : Vol2FsCutRing
    {
        static readonly Color Teal = new Color(0.45f, 0.9f, 1f);
        ParticleSystem _spray, _foam, _drip;
        Mesh _waterMesh, _lipMesh, _inkMesh, _wetMesh, _glowMesh; readonly Vol2FsStrips _water = new Vol2FsStrips(), _lip = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _wet = new Vol2FsStrips(), _glow = new Vol2FsStrips();
        float _phase, _lastT = -1f;
        Light _light;
        protected override Color CutGlow => new Color(0.6f, 1.3f, 1.7f);
        protected override float GougeWidth => 0.34f;
        protected override float EndSeconds => 1.2f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Stream", Vol2FsB1.Water(Kit, 2.3f, 0.55f, "fs_water"), out _waterMesh);
            var gl = M("fs_streak", Teal, 2.2f, 1f); gl.NoiseAmt = 0.35f; gl.Soft = 0.12f; gl.Scroll = new Vector2(-4f, 0f);
            Vol2Fs.MeshObject(root, "StreamGlow", Kit.Mat(gl), out _glowMesh);
            Vol2Fs.MeshObject(root, "FoamLip", Vol2FsB1.Water(Kit, 2.4f, 0.6f, "fs_splash"), out _lipMesh);
            Vol2Fs.MeshObject(root, "InkStream", Kit.InkSolid("fs_stroke2", InkC, Teal * 1.4f, 0.12f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            var wet = M("fs_wet", new Color(0.1f, 0.16f, 0.2f, 1f), 1f, 0f); wet.Opacity = 0.6f; wet.NoiseAmt = 0.3f; wet.Soft = 0.2f;
            Vol2Fs.MeshObject(root, "WetRing", Kit.Mat(wet), out _wetMesh);
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.4f, "fs_splash"), Life0 = 0.35f, Life1 = 0.7f, Speed0 = 1.5f, Speed1 = 4.5f, Size0 = 0.1f, Size1 = 0.26f, Gravity = 1.8f, Drag = 0.6f, Max = 900 }, root, "Spray");
            _foam = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.8f, 0.95f, 1f), 2.4f, 0.7f)), Life0 = 0.3f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.025f, Size1 = 0.06f, Gravity = 1.2f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 800 }, root, "Foam");
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.55f, 0.85f, 1f), 2.4f, 0.7f)), Life0 = 0.4f, Life1 = 0.7f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.025f, Size1 = 0.05f, Gravity = 2.6f, Stretch = true, StretchVel = 0.06f, StretchLen = 1.2f, Max = 400 }, root, "Drip");
            foreach (var ps in new[] { _spray, _foam, _drip }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "StreamLight", Teal, 4f);
        }

        float LipH(float ang) => 0.34f * Mathf.Lerp(0.35f, 1f, Front(ang));

        protected override void Cut(float t)
        {
            for (int k = 0; k < 40; k++) { float a = Random.Range(-Mathf.PI, Mathf.PI); if (Front(a) < 0.3f) continue; Vol2FsB1.EmitV(_spray, OnBand(a, R) + Vector3.up * 0.05f, Tangent(a) * (a > 0f ? 3f : -3f) + Vector3.up * Random.Range(1f, 2.5f) + Dir(a) * 0.6f, -1f, -1f); }
            _light.transform.position = Apex + F * R + Vector3.up * 0.5f; _light.intensity = 4f;
        }

        protected override void Crossed(Struck s, float ang)
        {
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            Vector3 at = OnBand(ang, R) + Vector3.up * 0.05f, d = Dir(ang), tan = Tangent(ang);
            for (int k = 0; k < 110; k++) Vol2FsB1.EmitV(_spray, at + tan * Random.Range(-0.5f, 0.5f), (Vector3.up * Random.Range(2f, 5.5f) + d * Random.Range(-1f, 2f) + tan * Random.Range(-1.5f, 1.5f)), -1f, -1f);
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_foam, b.center + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * Random.Range(1f, 4f), -1f, -1f);
            _light.transform.position = b.center; _light.intensity = 6f;
        }

        protected override void Ended()
        {
            for (int k = 0; k < 40; k++) { float a = Random.Range(-Mathf.PI, Mathf.PI); if (Front(a) < 0.3f) continue; Vol2FsB1.EmitV(_drip, OnBand(a, R) + Vector3.up * 0.15f, Vector3.down * 0.5f, -1f, -1f); }
        }

        /// <summary>A ring strip of the stream: flat (side = radius) or standing (side = up); revealed by the race, drained by the end clause.</summary>
        void Ring(Vol2FsStrips s, float r, float lift, bool standing, float w0, float alpha, float drain, float t, float rnd, Vector3 cam, float u1)
        {
            int n = 110; var pts = new List<Vector3>(n + 1); var wd = new List<float>(n + 1); var al = new List<float>(n + 1); var sd = new List<Vector3>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float ang = -Mathf.PI + i / (float)n * Mathf.PI * 2f, reach = Reached(ang, t), rk = RingK(ang);
                float w = (standing ? LipH(ang) * (0.75f + 0.25f * Mathf.Sin(ang * 23f - _phase * 7f)) : w0 * rk) * reach * (1f - drain);
                pts.Add(OnBand(ang, r) + Vector3.up * (standing ? lift + w * 0.5f : lift - 0.03f * drain)); wd.Add(w); sd.Add(standing ? Vector3.up : Dir(ang));
                al.Add(alpha * reach * (0.8f + 0.2f * Mathf.Sin(ang * 13f - _phase * 5f)));
            }
            s.Strip(pts, wd, sd, Color.white, al, rnd, cam, _phase * 0.5f, _phase * 0.5f + u1);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos;
            float dt = _lastT < 0f ? 0f : Mathf.Clamp(t - _lastT, 0f, 0.1f); _lastT = t;
            float stand = Launched ? Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.05f, t) : 0f, warn = LineWarn(t), end = EndK(t), flick = Flick(t);
            float speed = (1f - 0.6f * warn) * (1f - Vol2Fs.Smooth(0f, 0.5f, end));
            _phase += dt * 6f * speed;
            float drain = Vol2Fs.Smooth(0f, 0.8f, end);
            _water.Clear(); _lip.Clear(); _ink.Clear(); _wet.Clear(); _glow.Clear();
            if (stand > 0.01f && end < 1f)
            {
                Ring(_glow, R, 0.03f, false, 0.7f, stand * flick * 0.8f, drain, t, 0.13f, cam, 10f);
                Ring(_water, R, 0.04f, false, 0.6f, stand * flick, drain, t, 0.33f, cam, 14f);
                Ring(_lip, R + 0.27f, 0.02f, true, 0f, stand * 0.9f * flick, drain, t, 0.71f, cam, 10f);
                // ink dashes racing round in the stream
                for (int k = 0; k < 6; k++)
                {
                    float c = _phase * 1.2f + k * Mathf.PI / 3f, span = 0.5f;
                    int m = 10; var p = new Vector3[m]; var w = new float[m]; var a = new float[m]; var sd = new Vector3[m];
                    for (int i = 0; i < m; i++)
                    {
                        float u = i / (float)(m - 1), ang = c - span * (1f - u);
                        p[i] = OnBand(ang, R + 0.05f * Mathf.Sin(u * 6f + k)) + Vector3.up * 0.045f; sd[i] = Dir(ang);
                        w[i] = 0.12f * RingK(ang) * Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * Reached(ang, t) * (1f - drain); a[i] = stand;
                    }
                    _ink.Strip(p, w, sd, Color.white, a, 0.17f * k, cam, 0f, 2f);
                }
                if (Time.frameCount % 2 == 0)
                    for (int i = 0; i < 7; i++)
                    {
                        float ang = Random.Range(-Mathf.PI, Mathf.PI); if (Random.value > Front(ang) * 0.9f + 0.1f || Reached(ang, t) < 0.5f) continue;
                        Vol2FsB1.EmitV(_spray, OnBand(ang, R + 0.22f) + Vector3.up * 0.1f, Tangent(ang) * 2.5f * speed + Vector3.up * Random.Range(1.6f, 3.4f) * speed + Dir(ang) * 0.8f, Random.Range(0.12f, 0.26f), -1f);
                        Vol2FsB1.EmitV(_foam, OnBand(ang, R) + Vector3.up * 0.06f, Tangent(ang) * 3f * speed + Vector3.up * 0.4f, -1f, -1f);
                    }
            }
            // the wet ring the drained stream leaves, drying
            if (LineEnded)
            {
                float wl = Vol2Fs.Smooth(0f, 0.3f, end) * (1f - Vol2Fs.Smooth(0.6f, 1f, end));
                int n = 72; var p = new Vector3[n + 1]; var w = new float[n + 1]; var a = new float[n + 1]; var sd = new Vector3[n + 1];
                for (int i = 0; i <= n; i++) { float ang = -Mathf.PI + i / (float)n * Mathf.PI * 2f; p[i] = OnBand(ang, R) + Vector3.up * 0.025f; sd[i] = Dir(ang); w[i] = 0.7f * RingK(ang); a[i] = wl; }
                _wet.Strip(p, w, sd, Color.white, a, 0.5f, cam, 0f, 6f);
            }
            _water.Write(_waterMesh); _lip.Write(_lipMesh); _glow.Write(_glowMesh); _ink.Write(_inkMesh); _wet.Write(_wetMesh);
            _light.intensity = Mathf.MoveTowards(_light.intensity, stand * (1f - end) * 1.4f * flick, Time.deltaTime * 10f);
            if (_light.intensity < 2f) _light.transform.position = Apex + F * R + Vector3.up * 0.5f;
        }
    }
}
