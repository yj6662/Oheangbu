using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    // #308 D308-77: one pooled body of a townsperson. Appearance only - no collider, no EnemyVitals, no interaction point, so lock-on
    // (LockOn.SetCandidates takes EnemyVitals), aim assist, spell hits and the HUD never see it. It has no Update: TownWalkers308
    // places it and calls Pose once a frame. The pose comes from a manual PlayableGraph (Idle / Walk / one job clip), like
    // EnemyRigMotion298: the walk clip is driven by metres travelled / stride, so the feet keep pace with the ground at any speed.
    // The graph is not evaluated while no renderer of the body is drawn, and only at FarPoseHz beyond FullRateDistance.
    // The head turn is a clamped world-space swing applied after the clip pose, so it works on Humanoid and Generic rigs alike.
    [DisallowMultipleComponent]
    public sealed class TownWalkerBody308 : MonoBehaviour
    {
        public enum Job { Idle, Examine, Bored, Lift, Wipe, Look }
        [Serializable] public struct TintSlot { public Renderer Renderer; public int Material; }

        public Animator Animator;
        public Renderer[] Renderers = Array.Empty<Renderer>();
        public Transform Head;
        public AnimationClip Idle, Walk;
        [Tooltip("Job 번호 순. 비어 있으면 Idle 로 선다(Generic 몸).")]
        public AnimationClip[] Jobs = Array.Empty<AnimationClip>();
        [Tooltip("걷기 클립 한 바퀴에 몸이 나아가는 거리(m). bodies:import 가 발의 궤적에서 잰다.")]
        [Min(0)] public float StrideMetres;
        [Tooltip("이 몸 종류가 동시에 설 수 있는 수(무거운 몸의 상한).")]
        [Min(0)] public int MaxActive;
        [Tooltip("옷 색 변형 재질(사람의 tint 번호 순). 있으면 모든 LOD 렌더러의 재질을 통째로 바꾼다(SRP 배처 유지). 없으면 TintSlots 에 색을 곱한다.")]
        public Material[] Variants = Array.Empty<Material>();
        [Tooltip("옷 색을 곱할 재질 칸(Variants 가 비었을 때만).")]
        public TintSlot[] TintSlots = Array.Empty<TintSlot>();
        [Tooltip("bodies:import 가 적는 값(검사 · 보고용).")]
        public int Triangles;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        PlayableGraph graph; AnimationMixerPlayable mixer; AnimationClipPlayable idle, walk, job; bool hasJob; int jobIndex = -1;
        float wWalk, wJob, phase, idleTime, jobTime, look, debt; bool shadows = true; MaterialPropertyBlock block;

        public bool Usable => Animator != null && Idle != null && Walk != null && StrideMetres > 0 && Renderers != null && Renderers.Length > 0 && MaxActive > 0;
        public bool Drawn { get { for (int i = 0; i < Renderers.Length; i++) if (Renderers[i] != null && Renderers[i].isVisible) return true; return false; } }
        public float LookWeight => look;

        public void Begin(Color tint, int variant, float scale, int seed, int jobNumber)
        {
            transform.localScale = Vector3.one * scale;
            if (Variants != null && Variants.Length > 0)
            {
                var m = Variants[Mathf.Abs(variant) % Variants.Length];
                if (m != null) for (int i = 0; i < Renderers.Length; i++) if (Renderers[i] != null && Renderers[i].sharedMaterial != m) Renderers[i].sharedMaterial = m;
            }
            if (block == null) block = new MaterialPropertyBlock();
            for (int i = 0; i < TintSlots.Length; i++)
            {
                var r = TintSlots[i].Renderer; if (r == null) continue;
                r.GetPropertyBlock(block, TintSlots[i].Material); block.SetColor(BaseColor, tint); r.SetPropertyBlock(block, TintSlots[i].Material);
            }
            Build();
            uint h = (uint)seed * 2654435761u; phase = (h & 0xffff) / 65535f; idleTime = ((h >> 16) & 0xff) * .11f; jobTime = ((h >> 8) & 0xff) * .07f;
            wWalk = 0; wJob = 0; look = 0; debt = float.MaxValue * .5f; SetJob(jobNumber);
        }

        void Build()
        {
            if (graph.IsValid() || !Usable) return;
            Animator.enabled = true; Animator.runtimeAnimatorController = null; Animator.applyRootMotion = false; Animator.fireEvents = false;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // the graph is manual: Pose decides when a pose is worth computing
            graph = PlayableGraph.Create("TownWalker308"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 3);
            idle = AnimationClipPlayable.Create(graph, Idle); walk = AnimationClipPlayable.Create(graph, Walk);
            idle.SetSpeed(0); walk.SetSpeed(0); idle.SetApplyFootIK(false); walk.SetApplyFootIK(false);
            graph.Connect(idle, 0, mixer, 0); graph.Connect(walk, 0, mixer, 1);
            AnimationPlayableOutput.Create(graph, "Townsperson bones", Animator).SetSourcePlayable(mixer); graph.Play();
            hasJob = false; jobIndex = -1;
        }

        public void SetJob(int number)
        {
            if (!graph.IsValid() || number == jobIndex) return;
            if (hasJob) { graph.Disconnect(mixer, 2); job.Destroy(); hasJob = false; }
            jobIndex = number;
            var clip = number > 0 && Jobs != null && number < Jobs.Length ? Jobs[number] : null; if (clip == null) return;
            job = AnimationClipPlayable.Create(graph, clip); job.SetSpeed(0); job.SetApplyFootIK(false); graph.Connect(job, 0, mixer, 2); hasJob = true;
        }

        public void SetShadows(bool on)
        {
            if (on == shadows) return; shadows = on;
            for (int i = 0; i < Renderers.Length; i++) if (Renderers[i] != null) Renderers[i].shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        // moved = metres travelled since the last call; working = play the job clip while standing; lookAt / lookTarget = the head turn
        public void Pose(float dt, float moved, bool walking, bool working, Vector3 lookAt, float lookTarget, float minInterval, float crossFade, float headBlend, float yawClamp, float pitchClamp)
        {
            if (!graph.IsValid()) return;
            phase = Mathf.Repeat(phase + moved / StrideMetres, 1f); idleTime += dt; jobTime += dt; debt += dt;
            float step = crossFade > 0 ? dt / crossFade : 1f;
            wWalk = Mathf.MoveTowards(wWalk, walking ? 1 : 0, step); wJob = Mathf.MoveTowards(wJob, !walking && working && hasJob ? 1 : 0, step);
            look = Mathf.MoveTowards(look, lookTarget, dt / headBlend);
            if (debt < minInterval || !Drawn) return;
            debt = 0;
            float j = hasJob ? wJob * (1 - wWalk) : 0, w = wWalk, i = Mathf.Max(0, 1 - w - j);
            idle.SetTime(Mathf.Repeat(idleTime, Idle.length)); walk.SetTime(phase * Walk.length);
            if (hasJob) { var c = job.GetAnimationClip(); job.SetTime(c.isLooping ? Mathf.Repeat(jobTime, c.length) : Mathf.PingPong(jobTime, c.length)); }
            mixer.SetInputWeight(0, i); mixer.SetInputWeight(1, w); if (hasJob) mixer.SetInputWeight(2, j);
            graph.Evaluate(0);
            if (look <= .001f || Head == null) return;
            Vector3 fwd = transform.forward, to = lookAt - Head.position; if (to.sqrMagnitude < .01f) return;
            Vector3 flat = new Vector3(to.x, 0, to.z); if (flat.sqrMagnitude < .0001f) return;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(fwd, flat, Vector3.up), -yawClamp, yawClamp);
            float pitch = Mathf.Clamp(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg, -pitchClamp, pitchClamp);
            var swing = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, transform.right);
            Head.rotation = Quaternion.Slerp(Quaternion.identity, swing, look) * Head.rotation;
        }

        void OnDisable() { if (graph.IsValid()) graph.Destroy(); hasJob = false; jobIndex = -1; }
    }
}
