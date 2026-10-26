using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅏ 가른다 base of batch 6 (P4 갓 낫 맛 삿 앗): the cut RUNS along the path from the caster's feet at the visible run speed (the first body at the
    /// SingleCue impact clock; 삿 runs on through the bodies the executor strips, never past where its head stopped) and leaves the gouged crack line behind it
    /// (CSV 자국 윤곽: 아래로 파인 균열선) in the glyph's cut colour. The glyph draws the cutting head (<see cref="Run"/>), the cut on each struck body and its body action.
    /// The shared book (Vol2FsCut) parts the walls the executor split and opens / closes the stripped bodies' seams on the strip book.</summary>
    public abstract class Vol2FsCutPath : Vol2FsCut
    {
        protected ParticleSystem PCharge, PInk;
        Mesh _crackMesh, _crackGlowMesh; readonly Vol2FsStrips _crack = new Vol2FsStrips(), _crackGlow = new Vol2FsStrips();
        protected Light HeadLight;
        float _lastAlong;
        /// <summary>How wide the gouge is (m) and how long it stays after the look's last end.</summary>
        protected virtual float CrackWidth => 0.2f;
        protected virtual float CrackJag => 0.14f;
        /// <summary>Where the cut starts along the path (m: just ahead of the caster's feet).</summary>
        protected virtual float StartAlong => 0.45f;

        protected abstract void OnSetup();
        /// <summary>The cutting head ran from <paramref name="prev"/> to <paramref name="along"/> metres this frame (ground point = <see cref="OnPath"/>).</summary>
        protected abstract void Run(float t, float along, float prev);
        protected abstract void Live(float t, float h);

        protected override void OnBegin()
        {
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            var ink = M("fs_ink", InkC, 1f, 0f); ink.Edge = Acc * 1.4f; ink.EdgeWidth = 0.15f; ink.NoiseAmt = 0.3f; ink.Soft = 0.1f;
            PInk = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.06f, Size1 = 0.2f, Gravity = 1.2f, Drag = 0.6f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "Ink");
            foreach (var ps in new[] { PCharge, PInk }) ps.Play(false);
            Vol2Fs.MeshObject(root, "Gouge", Kit.InkSolid("fs_stroke2", InkC, CutGlow, 0.2f, 0.45f, 0.3f, 1.4f), out _crackMesh);
            var glow = M("fs_streak", CutGlow, 2.2f, 1f); glow.NoiseAmt = 0.25f; glow.Soft = 0.03f; glow.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "GougeGlow", Kit.Mat(glow), out _crackGlowMesh);
            HeadLight = Vol2Fs.PointLight(root, "HeadLight", Color.Lerp(Acc, Color.white, 0.25f), 4f);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Tip() + Random.insideUnitSphere * 0.35f * (1f - k), Vector3.zero, 0.06f + 0.1f * k, 0.2f);
        }

        protected override void OnLaunch(long nowUs) { HeadLight.intensity = 3f; HeadLight.transform.position = OnPath(StartAlong) + Vector3.up * 0.5f; _lastAlong = StartAlong; }
        protected override void OnHit(long nowUs) { Vol2FsB1.Emit(PInk, HitPoint, 14); }

        /// <summary>The gouge: from the start to where the cut has run (or stopped); hot behind the head, cooling; it closes with the look's end.</summary>
        protected float GougeLife(float t)
        {
            float end = -1f;
            foreach (var s in Strips) end = Mathf.Max(end, s.Ended ? s.EndAt : -1f);
            bool open = false; foreach (var s in Strips) if (!s.Ended) open = true;
            if (open || Strips.Count == 0 && EndRel < 0f) return 1f;
            if (end < 0f) end = EndRel >= 0f ? Mathf.Max(EndRel, HitAt + 1.2f) : t;
            return 1f - Mathf.Clamp01((t - end) / 0.8f);
        }

        protected override void OnLive(float t, float h) { }

        public override void Tick(long nowUs)
        {
            base.Tick(nowUs);
            if (Root == null || !Launched) return;
            float t = Now(nowUs);
            float along = RunAlong(t);
            if (along > _lastAlong + 1e-3f)
            {
                try { Run(t, along, _lastAlong); } catch (System.Exception e) { Debug.LogException(e); }
                HeadLight.transform.position = OnPath(along) + Vector3.up * 0.4f;
                _lastAlong = along;
            }
            else HeadLight.intensity = Mathf.MoveTowards(HeadLight.intensity, 0f, Time.deltaTime * 12f);
            var cam = CamPos;
            _crack.Clear(); _crackGlow.Clear();
            float life = GougeLife(t);
            if (along > StartAlong + 0.05f && life > 0.01f)
            {
                Vector3 a = OnPath(StartAlong), b = OnPath(along);
                Vol2FsB6.Crack(_crack, a, b, Ground, CrackWidth, 0f, 1f, life, 0.17f + Seed % 7 * 0.05f, Seed, CrackJag);
                // the hot line in the gouge: bright right behind the head, cooling over ~1 s along the run
                float since = t - LaunchAt;
                float hot = (1f - Vol2Fs.Smooth(0.25f, 1.6f, since)) * life;
                Vol2FsB6.Crack(_crackGlow, a, b, Ground, CrackWidth * 0.32f, 0f, 1f, Mathf.Max(hot, 0.22f * life), 0.53f, Seed, CrackJag, 0.03f);
            }
            _crack.Write(_crackMesh); _crackGlow.Write(_crackGlowMesh);
            if (IsHit) { try { Live(t, t - HitAt); } catch (System.Exception e) { Debug.LogException(e); } }
        }
    }
}
