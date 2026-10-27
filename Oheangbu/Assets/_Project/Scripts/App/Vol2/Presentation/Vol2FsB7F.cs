using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅗ 돌아온다 base of batch 7 (F5 공 농 몽 송 옹): the whole sector (the executor's: 5 m deep, its half angle) SLIDES OUT FanTravel metres in the
    /// report's out leg and slides home in the back leg (TripFraction, as the executor's Frac). Shared here: the sector's closed outline on the ground where it is now
    /// (CSV 자국 윤곽: 닫힌 곡선 · 전방 다발 면), a ghost of the outline where it turned that lingers (Phantom Blade principle 5), the leading edge brighter on the
    /// way out and the trailing (near) edge brighter on the way back. The glyph fills the sector with its bundle (<see cref="DrawFan"/>).</summary>
    public abstract class Vol2FsBackFan : Vol2FsBack
    {
        protected ParticleSystem PCharge;
        Mesh _lineMesh, _inkMesh; readonly Vol2FsStrips _line = new Vol2FsStrips(), _ink = new Vol2FsStrips();
        protected Light FanLight;
        protected virtual float OutlineWidth => 0.12f;      // r2: was 0.07 (unseen from the shoulder)
        protected virtual Color OutlineGlow => Acc * 1.4f;
        protected virtual float GhostTail => 1.0f;

        protected abstract void OnSetup();
        /// <summary>The sector now: its apex (on the ground), 0..1 of its travel, out or back, 0..1 standing.</summary>
        protected abstract void DrawFan(float t, Vector3 apex, float k, bool back, float life);
        protected virtual void Live(float t) { }

        protected override void OnBegin()
        {
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            PCharge.Play(false);
            var glow = M("fs_streak", OutlineGlow, 3f, 1f); glow.NoiseAmt = 0.25f; glow.Soft = 0.03f; glow.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "FanOutline", Kit.Mat(glow), out _lineMesh);
            Vol2Fs.MeshObject(root, "FanInk", Kit.InkSolid("fs_stroke2", InkC, OutlineGlow, 0.18f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            FanLight = Vol2Fs.PointLight(root, "FanLight", Color.Lerp(Acc, Color.white, 0.25f), 6f);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Tip() + Random.insideUnitSphere * 0.35f * (1f - k), Vector3.zero, 0.06f + 0.1f * k, 0.2f);
        }

        protected override void OnLaunch(long nowUs) { FanLight.intensity = 3f; }

        /// <summary>The sector's apex now (on the ground).</summary>
        protected Vector3 ApexNow(float t) => G(Apex + F * FanOut(t));
        /// <summary>A point of the sector with its apex at <paramref name="apex"/>: u = -1 .. 1 across the angle, v = 0 .. 1 out along the depth.</summary>
        protected Vector3 FanPoint(Vector3 apex, float u, float v) => G(apex + Dir(u * Half) * (Depth * v));

        void Outline(Vol2FsStrips s, Vector3 apex, float life, float lead, float trail, float rnd, Vector3 cam, float width)
        {
            if (life <= 0.01f) return;
            // the closed curve: near arc (r 0.6) -> right edge -> far arc -> left edge -> back
            var pts = new List<Vector3>(); var w = new List<float>(); var al = new List<float>();
            int na = 22;
            void Add(Vector3 p, float a) { p.y = Ground + 0.03f; pts.Add(p); w.Add(width); al.Add(life * a); }
            for (int k = 0; k <= 4; k++) { float u = -1f + 2f * k / 4f; Add(FanPoint(apex, u, 0.12f), 0.5f + 0.5f * trail); }
            for (int k = 1; k <= 8; k++) Add(FanPoint(apex, 1f, 0.12f + 0.88f * k / 8f), 0.6f);
            for (int k = 1; k <= na; k++) { float u = 1f - 2f * k / na; Add(FanPoint(apex, u, 1f), 0.5f + 0.5f * lead); }
            for (int k = 1; k <= 8; k++) Add(FanPoint(apex, -1f, 1f - 0.88f * k / 8f), 0.6f);
            var sides = new Vector3[pts.Count];
            for (int k = 0; k < pts.Count; k++) { Vector3 d = pts[Mathf.Min(pts.Count - 1, k + 1)] - pts[Mathf.Max(0, k - 1)]; d.y = 0f; sides[k] = Vector3.Cross(Vector3.up, d.sqrMagnitude > 1e-6f ? d.normalized : F); }
            s.Strip(pts, w, sides, Color.white, al, rnd, cam);
        }

        public override void Tick(long nowUs)
        {
            base.Tick(nowUs);
            if (Root == null || !Launched) return;
            float t = Now(nowUs);
            var cam = CamPos;
            float life = TripLife(t, 0.06f, 0.6f), k = Trip01(t);
            bool back = Back(t);
            Vector3 apex = ApexNow(t);
            FanLight.transform.position = FanPoint(apex, 0f, 0.55f) + Vector3.up * 1.2f; FanLight.intensity = 3f * life;
            _line.Clear(); _ink.Clear();
            Outline(_line, apex, life, back ? 0.4f : 1f, back ? 1f : 0.4f, 0.37f, cam, OutlineWidth);
            Outline(_ink, apex, life * 0.9f, 1f, 1f, 0.2f, cam, OutlineWidth * 2.2f);
            // the ghost of where it turned (the far outline), lingering
            if (TurnAt >= 0f)
            {
                float g = (1f - Vol2Fs.Smooth(0.05f, GhostTail, t - TurnAt)) * 0.6f;
                Outline(_line, G(Apex + F * Travel), g, 1f, 0.5f, 0.61f, cam, OutlineWidth * 0.8f);
            }
            _line.Write(_lineMesh); _ink.Write(_inkMesh);
            try { DrawFan(t, apex, k, back, life); } catch (System.Exception e) { Debug.LogException(e); }
            try { Live(t); } catch (System.Exception e) { Debug.LogException(e); }
        }

        protected override void OnLive(float t) { }
    }
}
