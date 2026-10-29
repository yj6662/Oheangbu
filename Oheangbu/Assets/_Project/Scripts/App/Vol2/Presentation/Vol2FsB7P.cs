using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅏ 돌아온다 base of batch 7 (P5 강 낭 망 상 앙): the head runs OUT along the path exactly where the executor's head is (36 m/s, to 12 m or a wall), turns
    /// where it stopped (the report's BackStartUs) and runs BACK home at the same speed. The trip is drawn as a closed loop on the ground (CSV 자국 윤곽: 닫힌 곡선 ·
    /// 착점 한 줄기): the out lane a little left of the axis, a U at the far end, the back lane a little right (the corridor is the executor's - the lanes are the
    /// look's), a stem of ink rising where it turned; the loop lingers after home (Phantom Blade principle 5). The glyph draws the head (<see cref="DrawHead"/>),
    /// what it throws off along the run (<see cref="Run"/>), the hits.</summary>
    public abstract class Vol2FsBackPath : Vol2FsBack
    {
        protected ParticleSystem PCharge;
        Mesh _loopMesh, _loopGlowMesh, _stemMesh; readonly Vol2FsStrips _loop = new Vol2FsStrips(), _loopGlow = new Vol2FsStrips(), _stem = new Vol2FsStrips();
        protected Light HeadLight;
        float _lastAlong; bool _lastBack;
        /// <summary>How far the lanes sit either side of the axis (m), how high the head flies, where the trip starts along the path.</summary>
        protected virtual float Lane => 0.42f;
        protected virtual float HeadHeight => 0.95f;
        /// <summary>The back lane flies a little higher (the loop reads from the shoulder as a closed curve in the air too).</summary>
        protected virtual float BackHeight => 1.35f;
        /// <summary>Where the head flew (look clock), for the glyph's aerial trail / tether; kept HistorySpan seconds.</summary>
        protected readonly List<(Vector3 P, float T)> HeadTrail = new List<(Vector3, float)>(256);
        protected virtual float HistorySpan => 1.6f;
        protected virtual float StartAlong => 0.6f;
        protected virtual float LoopWidth => 0.16f;
        /// <summary>The loop's glow colour (the accent, hotter).</summary>
        protected virtual Color LoopGlow => Acc * 1.5f;
        /// <summary>How long the loop lingers after home (s).</summary>
        protected virtual float LoopTail => 1.1f;

        protected abstract void OnSetup();
        /// <summary>The head now: <paramref name="pos"/> (3D), its travel direction, out or back, 0..1 standing.</summary>
        protected abstract void DrawHead(float t, Vector3 pos, Vector3 dir, bool back, float life);
        /// <summary>The head ran from <paramref name="prev"/> to <paramref name="along"/> metres this frame (out: increasing; back: decreasing).</summary>
        protected virtual void Run(float t, float along, float prev, bool back) { }
        protected virtual void Live(float t) { }

        protected override void OnBegin()
        {
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            PCharge.Play(false);
            Vol2Fs.MeshObject(root, "Loop", Kit.InkSolid("fs_stroke2", InkC, LoopGlow, 0.2f, 0.45f, 0.3f, 1.4f), out _loopMesh);
            var glow = M("fs_streak", LoopGlow, 2.2f, 1f); glow.NoiseAmt = 0.25f; glow.Soft = 0.03f; glow.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "LoopGlow", Kit.Mat(glow), out _loopGlowMesh);
            Vol2Fs.MeshObject(root, "Stem", Kit.InkSolid("fs_stroke", InkC, LoopGlow, 0.18f, 0.4f, 0.32f, 1.2f), out _stemMesh);
            HeadLight = Vol2Fs.PointLight(root, "HeadLight", Color.Lerp(Acc, Color.white, 0.25f), 4.5f);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Tip() + Random.insideUnitSphere * 0.35f * (1f - k), Vector3.zero, 0.06f + 0.1f * k, 0.2f);
        }

        protected override void OnLaunch(long nowUs) { HeadLight.intensity = 3f; _lastAlong = StartAlong; FindBodies(); }

        /// <summary>The lane offset now (out = left, back = right; a quick swing across at the turn).</summary>
        protected float LaneNow(float t) => !Back(t) ? -Lane : Mathf.Lerp(-Lane, Lane, Vol2Fs.Smooth(TurnRel, TurnRel + 0.08f, t));
        /// <summary>r2: how far the visible trip swings out beside the axis between the bodies (r1: lanes of 0.42 m read as one thin column from the shoulder camera;
        /// the concepts all show a wide S-loop). It pinches back onto the axis at every body that stood in the corridor at the launch (the head still passes
        /// THROUGH each body it touches - the executor's corridor is the axis), and is Lane at the far end (the U).</summary>
        protected virtual float Weave => 2.6f;      // r3: was 2.0
        readonly List<float> _bodyAlong = new List<float>();
        void FindBodies()
        {
            _bodyAlong.Clear();
            var co = Inst.Model.Coordinator; if (co == null || co.Targets == null) return;
            foreach (var tg in co.Targets)
            {
                if (tg == null || !tg.Alive) continue;
                Vector3 rel = tg.Position - Apex; rel.y = 0f;
                float a = Vector3.Dot(rel, F), lat = Mathf.Abs(Vector3.Dot(rel, Side));
                if (a > 0.3f && a < PathLen + 1f && lat < PathHalf + tg.Radius + 0.2f) _bodyAlong.Add(a);
            }
        }
        /// <summary>The lateral offset of the visible trip at <paramref name="along"/> metres (out = left, back = right), given where it turned.</summary>
        protected float Lateral(float along, bool back)
        {
            float far = HeadEnd > 0f ? HeadEnd : PathLen;
            float u = Mathf.Clamp01((along - StartAlong) / Mathf.Max(0.5f, far - StartAlong));
            float pinch = 1f;
            foreach (var b in _bodyAlong) pinch = Mathf.Min(pinch, Vol2Fs.Smooth(0.35f, 1.7f, Mathf.Abs(along - b)));
            float bulge = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.6f) * Weave * pinch * Vol2Fs.Smooth(0f, 0.25f, u);
            return (back ? 1f : -1f) * Mathf.Max(Lane, bulge);
        }
        /// <summary>A point of the visible trip: <paramref name="along"/> m on the out (false) or back (true) side, at its flying height.</summary>
        protected Vector3 TripPoint(float along, bool back) => OnPath(along) + Side * Lateral(along, back) + Vector3.up * (back ? BackHeight : HeadHeight);
        /// <summary>The head's 3D point now (out side, a quick U at the turn, back side).</summary>
        protected Vector3 HeadPos(float t)
        {
            float along = Mathf.Max(StartAlong, HeadAlong(t));
            if (!Back(t)) return TripPoint(along, false);
            float u = Vol2Fs.Smooth(TurnRel, TurnRel + 0.08f, t);
            return Vector3.Lerp(TripPoint(along, false), TripPoint(along, true), u) + F * Lane * 0.8f * Mathf.Sin(u * Mathf.PI);
        }        /// <summary>The trail points flown in the last <paramref name="span"/> seconds (oldest first), ending at the head now.</summary>
        protected List<Vector3> TrailPoints(float t, float span, int maxPts = 40)
        {
            var l = new List<Vector3>(maxPts + 1);
            int first = HeadTrail.Count;
            for (int i = HeadTrail.Count - 1; i >= 0; i--) { if (t - HeadTrail[i].T > span) break; first = i; }
            int n = HeadTrail.Count - first, step = Mathf.Max(1, n / maxPts);
            for (int i = first; i < HeadTrail.Count; i += step) l.Add(HeadTrail[i].P);
            if (HeadTrail.Count > 0 && (l.Count == 0 || l[l.Count - 1] != HeadTrail[HeadTrail.Count - 1].P)) l.Add(HeadTrail[HeadTrail.Count - 1].P);
            return l;
        }
        /// <summary>A point of the trip on the ground: <paramref name="along"/> m, on the out (false) or back (true) lane.</summary>
        protected Vector3 LanePoint(float along, bool back) => OnPath(along) + Side * (back ? Lane : -Lane);
        /// <summary>0..1 of the whole loop drawn so far (out straight, the U, back straight).</summary>
        protected float LoopProgress(float t)
        {
            float far = HeadEnd > 0f ? HeadEnd : Mathf.Max(StartAlong, HeadAlong(t));
            float straight = Mathf.Max(0.01f, far - StartAlong), arc = Mathf.PI * Lane, total = straight * 2f + arc;
            if (!Back(t)) return Mathf.Clamp01((Mathf.Max(StartAlong, HeadAlong(t)) - StartAlong) / total);
            float u = Mathf.Clamp01((t - TurnRel) / 0.08f);
            float back = Mathf.Clamp(far - Mathf.Max(StartAlong, HeadAlong(t)), 0f, straight);
            return Mathf.Clamp01((straight + arc * u + back) / total);
        }
        protected float LoopLife(float t) => TripLife(t, 0.05f, LoopTail);
        /// <summary>r2: the ground loop under the visible trip so far (out side to the head / the U / back side to the head).</summary>
        List<Vector3> LoopPoints(float t, float far, float along)
        {
            var pts = new List<Vector3>(80);
            bool back = Back(t);
            float outTo = back ? far : along;
            int n = Mathf.Clamp(Mathf.CeilToInt((outTo - StartAlong) / 0.25f), 2, 48);
            for (int k = 0; k <= n; k++) { float a = Mathf.Lerp(StartAlong, outTo, k / (float)n); pts.Add(G(OnPath(a) + Side * Lateral(a, false))); }
            if (back)
            {
                float u = Vol2Fs.Smooth(TurnRel, TurnRel + 0.08f, t);
                for (int k = 1; k <= 6; k++) { float v = k / 6f * u; pts.Add(G(OnPath(far) + Side * Mathf.Lerp(Lateral(far, false), Lateral(far, true), v) + F * Lane * 0.8f * Mathf.Sin(v * Mathf.PI))); }
                if (u >= 1f)
                {
                    int m = Mathf.Clamp(Mathf.CeilToInt((far - along) / 0.25f), 1, 48);
                    for (int k = 1; k <= m; k++) { float a = Mathf.Lerp(far, along, k / (float)m); pts.Add(G(OnPath(a) + Side * Lateral(a, true))); }
                }
            }
            for (int k = 0; k < pts.Count; k++) pts[k] += Vector3.up * 0.02f;
            return pts;
        }

        public override void Tick(long nowUs)
        {
            base.Tick(nowUs);
            if (Root == null || !Launched) return;
            float t = Now(nowUs);
            float along = Mathf.Max(StartAlong, HeadAlong(t));
            bool back = Back(t);
            if (back != _lastBack) { _lastAlong = HeadEnd > 0f ? HeadEnd : along; _lastBack = back; }
            if (Mathf.Abs(along - _lastAlong) > 1e-3f && !IsHome)
            {
                try { Run(t, along, _lastAlong, back); } catch (System.Exception e) { Debug.LogException(e); }
                _lastAlong = along;
            }
            Vector3 head = HeadPos(t);
            if (!IsHome && (HeadTrail.Count == 0 || (HeadTrail[HeadTrail.Count - 1].P - head).sqrMagnitude > 1e-6f)) HeadTrail.Add((head, t));
            while (HeadTrail.Count > 0 && t - HeadTrail[0].T > HistorySpan) HeadTrail.RemoveAt(0);
            HeadLight.transform.position = head;
            HeadLight.intensity = IsHome ? Mathf.MoveTowards(HeadLight.intensity, 0f, Time.deltaTime * 12f) : 3f;
            var cam = CamPos;
            _loop.Clear(); _loopGlow.Clear(); _stem.Clear();
            float life = LoopLife(t);
            if (life > 0.01f)
            {
                float far = HeadEnd > 0f ? HeadEnd : along;
                var pts = LoopPoints(t, far, along);
                if (pts.Count >= 2)
                {
                    var w = new float[pts.Count]; var al = new float[pts.Count]; var gw = new float[pts.Count]; var ga = new float[pts.Count];
                    for (int k = 0; k < pts.Count; k++)
                    {
                        float u = k / (float)(pts.Count - 1);
                        w[k] = LoopWidth * (0.6f + 0.4f * Mathf.Sin(u * Mathf.PI)); al[k] = life;
                        // the hot line: bright right behind the head, cooling back along the trip
                        float hot = Vol2Fs.Smooth(0.55f, 1f, u);
                        gw[k] = LoopWidth * 0.35f; ga[k] = life * Mathf.Max(0.25f, hot);
                    }
                    var sides = new Vector3[pts.Count];
                    for (int k = 0; k < pts.Count; k++) { Vector3 d = pts[Mathf.Min(pts.Count - 1, k + 1)] - pts[Mathf.Max(0, k - 1)]; d.y = 0f; sides[k] = Vector3.Cross(Vector3.up, d.sqrMagnitude > 1e-6f ? d.normalized : F); }
                    _loop.Strip(pts, w, sides, Color.white, al, 0.21f + Seed % 5 * 0.07f, cam);
                    for (int k = 0; k < pts.Count; k++) pts[k] += Vector3.up * 0.01f;
                    _loopGlow.Strip(pts, gw, sides, Color.white, ga, 0.53f, cam);
                }
                // the stem where it turned (착점 한 줄기): a short brushed stroke rising out of the ground
                if (TurnAt >= 0f)
                {
                    float g = Vol2Fs.Smooth(TurnAt, TurnAt + 0.12f, t) * life;
                    Vector3 b = OnPath(far + Lane * 0.6f);
                    var sp = new[] { b, b + Vector3.up * 0.55f + F * 0.05f, b + Vector3.up * 1.05f - F * 0.06f };
                    _stem.Strip(sp, new[] { 0.16f, 0.1f, 0.02f }, null, Color.white, new[] { g, g, g * 0.8f }, 0.4f, cam);
                }
            }
            _loop.Write(_loopMesh); _loopGlow.Write(_loopGlowMesh); _stem.Write(_stemMesh);
            float hl = IsHome ? 0f : 1f;
            if (hl > 0f) { try { DrawHead(t, head, back ? -F : F, back, Vol2Fs.Smooth(LaunchAt, LaunchAt + 0.05f, t)); } catch (System.Exception e) { Debug.LogException(e); } }
            else { try { DrawHead(t, head, -F, true, 0f); } catch (System.Exception e) { Debug.LogException(e); } }
            try { Live(t); } catch (System.Exception e) { Debug.LogException(e); }
        }

        protected override void OnLive(float t) { }
    }
}
