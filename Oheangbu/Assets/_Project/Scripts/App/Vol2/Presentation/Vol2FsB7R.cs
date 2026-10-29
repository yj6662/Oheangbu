using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅜ 돌아온다 base of batch 7 (R5 궁 눙 뭉 숭 웅): the band (the executor's: its thickness) SPREADS from the spec's radius (2 m) to
    /// space.returnRing.outer (4 m) in the report's out leg and shrinks back in the back leg (TripFraction). Shared here: the closed ring where the band is now (front arc
    /// brighter - the shoulder camera stands inside it; CSV 자국 윤곽: 닫힌 곡선 · 반경 2 m 고리), a ghost ring at its widest that lingers after the turn, a faint
    /// ring at its start radius. The glyph draws the ring's body (<see cref="DrawRing"/>).</summary>
    public abstract class Vol2FsBackRing : Vol2FsBack
    {
        protected ParticleSystem PCharge;
        Mesh _lineMesh, _inkMesh; readonly Vol2FsStrips _line = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        protected Light RingLight;
        protected virtual float LineWidth => 0.07f;
        protected virtual Color LineGlow => Acc * 1.4f;
        protected virtual float GhostTail => 1.0f;

        protected abstract void OnSetup();
        /// <summary>The ring now: its centre radius, 0..1 of its spread, out or back, 0..1 standing.</summary>
        protected abstract void DrawRing(float t, float r, float k, bool back, float life);
        protected virtual void Live(float t) { }

        protected override void OnBegin()
        {
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            PCharge.Play(false);
            var glow = M("fs_streak", LineGlow, 2.2f, 1f); glow.NoiseAmt = 0.25f; glow.Soft = 0.03f; glow.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "RingLine", Kit.Mat(glow), out _lineMesh);
            Vol2Fs.MeshObject(root, "RingInk", Kit.InkSolid("fs_stroke2", InkC, LineGlow, 0.18f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            RingLight = Vol2Fs.PointLight(root, "RingLight", Color.Lerp(Acc, Color.white, 0.25f), 6f);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Tip() + Random.insideUnitSphere * 0.35f * (1f - k), Vector3.zero, 0.06f + 0.1f * k, 0.2f);
        }

        protected override void OnLaunch(long nowUs) { RingLight.intensity = 3f; }

        /// <summary>A point of a ring of radius <paramref name="r"/> at angle <paramref name="ang"/> (0 = ahead, + to the right), on the ground.</summary>
        protected Vector3 RingPoint(float ang, float r) => G(Apex + Dir(ang) * r);

        protected void Circle(Vol2FsStrips s, float r, float width, float life, float rnd, Vector3 cam, float lift = 0.03f, int n = 72)
        {
            if (life <= 0.01f) return;
            var pts = new Vector3[n + 1]; var w = new float[n + 1]; var al = new float[n + 1]; var sd = new Vector3[n + 1];
            for (int k = 0; k <= n; k++)
            {
                float a = k / (float)n * Vol2ReturnGeometry.Tau;
                pts[k] = RingPoint(a, r) + Vector3.up * lift; w[k] = width; al[k] = life * (0.35f + 0.65f * Front(a));
                sd[k] = Dir(a);
            }
            s.Strip(pts, w, sd, Color.white, al, rnd, cam);
        }

        public override void Tick(long nowUs)
        {
            base.Tick(nowUs);
            if (Root == null || !Launched) return;
            float t = Now(nowUs);
            var cam = CamPos;
            float life = TripLife(t, 0.06f, 0.6f), k = Trip01(t), r = RingNow(t);
            bool back = Back(t);
            RingLight.transform.position = RingPoint(0f, r) + Vector3.up * 1.1f; RingLight.intensity = 3f * life;
            _line.Clear(); _ink.Clear();
            Circle(_line, r, LineWidth, life, 0.37f, cam);
            Circle(_ink, r, LineWidth * 2.4f, life * 0.9f, 0.2f, cam, 0.02f);
            Circle(_line, R0, LineWidth * 0.5f, life * 0.35f, 0.83f, cam);
            if (TurnAt >= 0f) Circle(_line, R1, LineWidth * 0.8f, (1f - Vol2Fs.Smooth(0.05f, GhostTail, t - TurnAt)) * 0.6f, 0.61f, cam);
            _line.Write(_lineMesh); _ink.Write(_inkMesh);
            try { DrawRing(t, r, k, back, life); } catch (System.Exception e) { Debug.LogException(e); }
            try { Live(t); } catch (System.Exception e) { Debug.LogException(e); }
        }

        protected override void OnLive(float t) { }
    }
}
