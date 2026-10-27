using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅓ 돌아온다 base of batch 7 (A5 겅 넝 멍 성 엉): nothing flies away - the element ORBITS the cast point on the executor's own circle (Vol2ReturnTuning:
    /// 1.6 m, one turn a second, clockwise from above, starting ahead) for the attachment's 6 s (the host's EndUs), and the closed circle it draws is its whole
    /// trip (CSV 자국 윤곽: 닫힌 곡선 · 붓대 · 소매 - no mark in the world: the brush shaft and the sleeve carry a small ring of it). Shared here: a faint
    /// standing circle, the trailing afterimage arc behind the orbiter, the small rings on the shaft and the sleeve, the warning flicker in the last 0.4 s.
    /// The glyph draws the orbiter (<see cref="DrawOrbiter"/>) and its own end (<see cref="Vol2FsBack.OnHome"/>).</summary>
    public abstract class Vol2FsBackOrbit : Vol2FsBack
    {
        protected ParticleSystem PCharge;
        Mesh _ringMesh, _trailMesh; readonly Vol2FsStrips _ring = new Vol2FsStrips(), _trail = new Vol2FsStrips();
        protected Light OrbLight;
        /// <summary>The orbiter's height above the ground (m), the afterimage arc's length (radians), the standing circle's strength.</summary>
        protected virtual float OrbitH => 1.05f;
        protected virtual float TrailArc => 3.6f;      // r2: was 2.1 (the concepts show a near-full band)
        protected virtual float TrailWidth => 0.3f;
        protected virtual float CircleAlpha => 0.4f;
        protected virtual Color TrailGlow => Acc * 1.4f;

        /// <summary>r2: 1 on the front arc .. 0.15 behind the caster (near the camera); the glyph's own particles at the orbiter should scale by it.</summary>
        protected float FrontNow = 1f;
        protected abstract void OnSetup();
        /// <summary>The orbiter now: its point, its direction of travel (tangent), its angle (unbounded), 0..1 standing (flickers in the warning).</summary>
        protected abstract void DrawOrbiter(float t, Vector3 pos, Vector3 tangent, float psi, float life);
        protected virtual void Live(float t) { }

        protected override void OnBegin()
        {
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            PCharge.Play(false);
            var glow = M("fs_streak", TrailGlow, 2.2f, 1f); glow.NoiseAmt = 0.25f; glow.Soft = 0.03f; glow.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "OrbitRing", Kit.Mat(glow), out _ringMesh);
            var tr = M("fs_streak", TrailGlow, 2.4f, 1f); tr.NoiseAmt = 0.3f; tr.Soft = 0.04f; tr.Erode = 0.15f; tr.Scroll = new Vector2(-2.5f, 0f);
            Vol2Fs.MeshObject(root, "OrbitTrail", Kit.Mat(tr), out _trailMesh);
            OrbLight = Vol2Fs.PointLight(root, "OrbLight", Color.Lerp(Acc, Color.white, 0.25f), 4f);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Tip() + Random.insideUnitSphere * 0.35f * (1f - k), Vector3.zero, 0.06f + 0.1f * k, 0.2f);
        }

        protected override void OnLaunch(long nowUs) { OrbLight.intensity = 2.5f; }

        /// <summary>The orbit's tangent at angle psi (clockwise from above).</summary>
        protected Vector3 Tangent(float psi) { Vector3 a = OrbitPoint(psi, 0f), b = OrbitPoint(psi + 0.02f, 0f); Vector3 d = b - a; d.y = 0f; return d.sqrMagnitude > 1e-8f ? d.normalized : Side; }
        /// <summary>0..1 the circle standing, with the warning flicker in the template's last window.</summary>
        protected float OrbitLife(float t)
        {
            float life = TripLife(t, 0.12f, 0.5f);
            float warn = LineWarn(t);
            return warn > 0f ? life * (Mathf.Sin(t * 42f) > 0f ? 1f : 0.45f) : life;
        }

        public override void Tick(long nowUs)
        {
            base.Tick(nowUs);
            if (Root == null || !Launched) return;
            float t = Now(nowUs);
            var cam = CamPos;
            float life = OrbitLife(t), psi = Psi(t);
            Vector3 p = OrbitPoint(psi, OrbitH);
            OrbLight.transform.position = p; OrbLight.intensity = 2.5f * life;
            _ring.Clear(); _trail.Clear();
            if (life > 0.01f)
            {
                // the closed circle (faint), the afterimage arc behind the orbiter (bright at the head, thinning back)
                int n = 64; var pts = new Vector3[n + 1]; var w = new float[n + 1]; var al = new float[n + 1];
                for (int k = 0; k <= n; k++) { float a = k / (float)n * Vol2ReturnGeometry.Tau; pts[k] = OrbitPoint(a, OrbitH); w[k] = 0.06f; al[k] = life * CircleAlpha * (0.4f + 0.6f * Front(a)); }
                _ring.Strip(pts, w, null, Color.white, al, 0.31f, cam);
                int m = 28; var tp = new Vector3[m]; var tw = new float[m]; var ta = new float[m];
                for (int k = 0; k < m; k++)
                {
                    float u = k / (float)(m - 1);
                    float a = psi - TrailArc * u;
                    if (a < 0f) a = 0f;
                    tp[k] = OrbitPoint(a, OrbitH + 0.05f * Mathf.Sin(u * 6f + t * 4f));
                    tw[k] = TrailWidth * Mathf.Max(0f, 1f - u) * (0.4f + 0.6f * Mathf.Sin(Mathf.Min(1f, u * 4f) * Mathf.PI * 0.5f));
                    ta[k] = life * (1f - u) * Mathf.Lerp(0.12f, 1f, Front(a));
                }
                _trail.Strip(tp, tw, null, Color.white, ta, 0.57f, cam);
                // the small rings the brush shaft and the sleeve carry (붓대 · 소매)
                Vector3 shaft = Shaft(0.78f), sleeve = Sleeve();
                Vol2FsB5.Halo(_ring, shaft, (Tip() - Grip()).normalized, 0.09f, 0.025f, life * 0.9f, 0.7f, cam, 16, t * 6f);
                Vol2FsB5.Halo(_ring, sleeve, Vector3.up, 0.13f, 0.025f, life * 0.8f, 0.9f, cam, 16, -t * 5f);
            }
            _ring.Write(_ringMesh); _trail.Write(_trailMesh);
            // r2: the half-turn behind the caster passes 1.2 m in front of the shoulder camera (r1: leaves / flames / feathers filled the lower frame) - it fades there
            FrontNow = Mathf.Lerp(0.15f, 1f, Front(psi));
            OrbLight.intensity *= FrontNow;
            try { DrawOrbiter(t, p, Tangent(psi), psi, life * FrontNow); } catch (System.Exception e) { Debug.LogException(e); }
            try { Live(t); } catch (System.Exception e) { Debug.LogException(e); }
        }

        protected override void OnLive(float t) { }
    }
}
