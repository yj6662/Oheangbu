using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅏ 심는다 base of batch 5 (P3 감 남 맘 삼 암): the seed shot (the body's accent at the brush tip, a seed with an ink trail on the path's lob
    /// to the predicted first body) -> each "Plant" contact = the head goes on through the next body (a short flick of the trail from the last one) and the
    /// glyph plants its mark there -> a camera-facing ink countdown dial round every waiting mark (the live expiry; 信 holds it; the last 0.4 s flare)
    /// -> the glyph's trigger / withering. Every mark rides its host (the executor moves the seed with it).</summary>
    public abstract class Vol2FsSeedPath : Vol2FsSeed
    {
        protected Color Acc;
        protected ParticleSystem PCharge, PTrail, PInk;
        Mesh _dialMesh; readonly Vol2FsStrips _dial = new Vol2FsStrips();
        Light _headLight;
        /// <summary>How high above the host's top the mark waits (0 = in the fur).</summary>
        protected virtual float MarkLift => 0.55f;
        protected virtual float DialR => 0.36f;
        protected virtual float DialWidth => 0.06f;
        protected Vector3 MarkPoint(Pod p) => HostTop(p) + Vector3.up * MarkLift;
        /// <summary>Where the countdown dial is drawn round (the mark).</summary>
        protected virtual Vector3 DialAt(Pod p) => MarkPoint(p);
        protected Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsB1.M(tex, tint, i, a);
        protected static readonly Color InkC = new Color(0.03f, 0.025f, 0.02f, 1f);

        protected abstract void OnSetup();
        /// <summary>The glyph's own mark appears on its host (index 0 = the first body, under the shot).</summary>
        protected abstract void Planted(Pod p);
        protected abstract void Live(float t, float h);

        protected override void OnBegin()
        {
            var root = Root.transform; Acc = Accent(Element);
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            PTrail = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.25f, Life1 = 0.45f, Speed0 = 0f, Speed1 = 0.3f, Size0 = 0.06f, Size1 = 0.16f, Drag = 1f, Max = 400 }, root, "Trail");
            var ink = M("fs_ink", InkC, 1f, 0f); ink.Edge = Acc * 1.4f; ink.EdgeWidth = 0.15f; ink.NoiseAmt = 0.3f; ink.Soft = 0.1f;
            PInk = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.06f, Size1 = 0.2f, Gravity = 1.2f, Drag = 0.6f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "Ink");
            foreach (var ps in new[] { PCharge, PTrail, PInk }) ps.Play(false);
            var dial = M("fs_stroke2", Acc, 2.2f, 0.9f); dial.NoiseAmt = 0.25f; dial.Soft = 0.05f;      // r2: a glowing accent ring (r1: ink rings vanished on the dark bears)
            Vol2Fs.MeshObject(root, "Dials", Kit.Mat(dial), out _dialMesh);
            _headLight = Vol2Fs.PointLight(root, "HeadLight", Acc, 3f);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Tip() + Random.insideUnitSphere * 0.35f * (1f - k), Vector3.zero, 0.06f + 0.1f * k, 0.2f);
        }

        protected override void OnLaunch(long nowUs) { _headLight.intensity = 2.5f; _headLight.transform.position = From; }

        protected override void OnFly(float t, float f, float prev)
        {
            float len = Vector3.Distance(From, HitPoint);
            int n = Mathf.Max(1, Mathf.CeilToInt((f - prev) * len * 8f));
            for (int k = 0; k < n; k++) { var p = Line(Mathf.Lerp(prev, f, (k + 0.5f) / n)); Vol2FsB1.EmitV(PTrail, p, Vector3.zero, 0.14f, 0.35f); if (k % 2 == 0) Vol2FsB1.EmitV(PInk, p, Random.insideUnitSphere * 0.4f, -1f, -1f); }
            _headLight.transform.position = Line(f);
        }

        protected override void OnHit(long nowUs) { _headLight.intensity = 0f; Vol2FsB1.Emit(PInk, HitPoint, 10); }

        protected override void OnPlant(Pod p)
        {
            // the head went on through: a flick of the trail from the previous mark's body to this one
            if (p.Index > 0)
            {
                var prev = Pods[p.Index - 1];
                Vector3 a = HostTop(prev) + Vector3.up * 0.2f, b = HostTop(p) + Vector3.up * 0.2f;
                for (int k = 0; k < 18; k++) { float u = (k + 0.5f) / 18f; Vol2FsB1.EmitV(PTrail, Vector3.Lerp(a, b, u) + Vector3.up * 0.4f * u * (1f - u), (b - a).normalized * 0.4f, 0.12f, 0.25f + 0.2f * u); }
            }
            Vol2FsB1.Emit(PInk, HostTop(p), 8);
            Planted(p);
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            _dial.Clear();
            foreach (var p in Pods)
            {
                if (!p.Started || p.Ended || DialR <= 0f) continue;
                float life = PodLife(p, t, 0.3f), warn = PodWarn(p, t);
                if (life <= 0.01f) continue;
                float flick = warn > 0f ? (Mathf.Sin(t * 40f) > 0f ? 1f : 0.45f) : 1f;
                Vol2FsB5.Dial(_dial, DialAt(p), cam, DialR * (1f + 0.06f * Beat(p)), DialWidth * (1f + 0.6f * warn), Remain01(p, t), life * flick * (0.8f + 0.2f * Beat(p)), 0.21f + 0.17f * p.Index);
            }
            _dial.Write(_dialMesh);
            Live(t, h);
        }
    }
}
