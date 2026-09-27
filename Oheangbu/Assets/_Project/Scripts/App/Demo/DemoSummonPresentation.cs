using System;
using Oheangbu.App.SpellVFX120;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Oheangbu.App.Demo
{
    // The actor owns position and combat time. This component owns only a replaceable visual child.
    public sealed class DemoSummonPresentation : MonoBehaviour
    {
        GameObject model, sealObject, debrisObject;
        Renderer[] renderers;
        ParticleSystem[] debris;
        SummonCombatProfile profile;
        MaterialPropertyBlock block;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable idle, walk, attack, rootAttackClip, backwardWalk, flameAttackClip;
        AnimationClipPlayable tigerRun, tigerLeap, tigerClawLeft, tigerClawRight;
        AnimationClipPlayable formationPose, dissolvePose;
        DemoSummonRootPresentation rootPresentation;
        DemoSummonFlamePresentation flamePresentation;
        DemoSummonWaterPresentation waterPresentation;
        DemoSummonPhaseParticles phaseParticles;
        DemoWoodDeerFootPlacement footPlacement;
        bool currentAttackIsRoot, currentAttackIsFlame;
        bool currentAttackIsTiger;
        float runWeight;
        public TigerAttackStage TigerVisualStage { get; private set; } = TigerAttackStage.Finished;
        public float TigerVisualClipTime { get; private set; }
        float lastAge, distance, movementWeight, attackStarted = float.NegativeInfinity;
        long attackId;
        bool debrisStarted, configured;
        Vector3 sealPosition;
        Quaternion sealRotation;
        public int SkinnedRendererCount { get; private set; }
        public bool HasAnimationGraph => graph.IsValid();
        public float LastRevealAge { get; private set; }

        public void Configure(SummonCombatProfile value)
        {
            if (configured) throw new InvalidOperationException("Presentation is configured once per prepared summon.");
            if (value == null || value.PresentationPrefab == null) throw new ArgumentException("Summon model missing");
            profile = value;
            block = new MaterialPropertyBlock();
            model = Instantiate(value.PresentationPrefab, transform, false);
            model.name = "ApprovedAppearance_CombatDerivative";
            model.SetActive(true);
            // Decorative geometry is never a combat blocker or target.
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
            renderers = model.GetComponentsInChildren<Renderer>(true);
            SkinnedRendererCount = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = null;
            if (profile.IdleClip != null && profile.WalkClip != null && profile.AttackClip != null)
            {
                graph = PlayableGraph.Create("Demo summon pose clock");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                mixer = AnimationMixerPlayable.Create(graph, 12);
                idle = AnimationClipPlayable.Create(graph, profile.IdleClip);
                walk = AnimationClipPlayable.Create(graph, profile.WalkClip);
                attack = AnimationClipPlayable.Create(graph, profile.AttackClip);
                graph.Connect(idle, 0, mixer, 0); graph.Connect(walk, 0, mixer, 1); graph.Connect(attack, 0, mixer, 2);
                idle.SetSpeed(0); walk.SetSpeed(0); attack.SetSpeed(0);
                if (profile.RootAttackClip != null)
                {
                    rootAttackClip = AnimationClipPlayable.Create(graph, profile.RootAttackClip);
                    graph.Connect(rootAttackClip, 0, mixer, 3); rootAttackClip.SetSpeed(0);
                }
                if (profile.BackwardWalkClip != null)
                {
                    backwardWalk = AnimationClipPlayable.Create(graph, profile.BackwardWalkClip);
                    graph.Connect(backwardWalk, 0, mixer, 4); backwardWalk.SetSpeed(0);
                }
                if (profile.FlameAttackClip != null)
                {
                    flameAttackClip = AnimationClipPlayable.Create(graph, profile.FlameAttackClip);
                    graph.Connect(flameAttackClip, 0, mixer, 5); flameAttackClip.SetSpeed(0);
                }
                if(profile.TigerRunClip!=null){tigerRun=AnimationClipPlayable.Create(graph,profile.TigerRunClip);graph.Connect(tigerRun,0,mixer,6);tigerRun.SetSpeed(0);}
                if(profile.TigerLeapClip!=null){tigerLeap=AnimationClipPlayable.Create(graph,profile.TigerLeapClip);graph.Connect(tigerLeap,0,mixer,7);tigerLeap.SetSpeed(0);}
                if(profile.TigerClawLeftClip!=null){tigerClawLeft=AnimationClipPlayable.Create(graph,profile.TigerClawLeftClip);graph.Connect(tigerClawLeft,0,mixer,8);tigerClawLeft.SetSpeed(0);}
                if(profile.TigerClawRightClip!=null){tigerClawRight=AnimationClipPlayable.Create(graph,profile.TigerClawRightClip);graph.Connect(tigerClawRight,0,mixer,9);tigerClawRight.SetSpeed(0);}
                if(profile.FormationClip!=null){formationPose=AnimationClipPlayable.Create(graph,profile.FormationClip);graph.Connect(formationPose,0,mixer,10);formationPose.SetSpeed(0);}
                if(profile.DissolveClip!=null){dissolvePose=AnimationClipPlayable.Create(graph,profile.DissolveClip);graph.Connect(dissolvePose,0,mixer,11);dissolvePose.SetSpeed(0);}
                var output = AnimationPlayableOutput.Create(graph, "Rig", animator); output.SetSourcePlayable(mixer);
                mixer.SetInputWeight(0, 1); graph.Play(); graph.Evaluate(0);
            }
            if (profile.Letter == "곰" && SkinnedRendererCount > 0 && model.GetComponentsInChildren<Transform>(true).Length > 20)
            {
                footPlacement = gameObject.AddComponent<DemoWoodDeerFootPlacement>();
                footPlacement.Configure(model, profile);
            }
            configured = true;
        }

        void StartSeal()
        {
            if (profile.FormationSeal == null) return;
            sealObject = new GameObject("Summon_KTP_Bottom");
            sealObject.transform.SetParent(transform, false);
            sealPosition = transform.position + Vector3.up * .025f;
            sealRotation = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
            sealObject.transform.SetPositionAndRotation(sealPosition, sealRotation);
            var motif = sealObject.AddComponent<Vfx120TraditionalMotif>();
            var settings = Vfx120TraditionalMotif.Settings.DefaultFor(Vfx120TraditionalMotif.Role.Summon);
            settings.PreserveAuthored = true; settings.HierarchyScaling = true; settings.PatternFocus = true;
            settings.FiniteWindow = true; settings.Lifetime = Mathf.Max(.2f, profile.FormationSeconds);
            settings.FadeSeconds = .3f; settings.Brightness = .65f; settings.PreviewControlled = false;
            Color pigment=new Color(.24f,.42f,.27f),ink=new Color(.07f,.12f,.08f);
            switch(profile.Element)
            {
                case Oheangbu.Core.Domain.Element.Fire: pigment=new Color(.65f,.21f,.065f);ink=new Color(.16f,.045f,.02f);break;
                case Oheangbu.Core.Domain.Element.Metal: pigment=new Color(.67f,.73f,.78f);ink=new Color(.15f,.17f,.19f);break;
                case Oheangbu.Core.Domain.Element.Earth: pigment=new Color(.48f,.38f,.24f);ink=new Color(.14f,.10f,.06f);break;
                case Oheangbu.Core.Domain.Element.Water: pigment=new Color(.17f,.31f,.41f);ink=new Color(.04f,.08f,.13f);break;
            }
            motif.Configure(profile.FormationSeal, pigment, ink,
                Vfx120TraditionalMotif.Role.Summon, settings);
            sealObject.transform.localScale = Vector3.one * 1.1f;
        }

        public void Sample(SummonCombatClock clock, float moveSpeed, float attackProgress, SummonRootAttackPlan rootPlan = null, bool rootAttack = false,
            SummonFlameAttackPlan flamePlan = null, bool movingBackward = false, SummonTigerAttackPlan tigerPlan = null,
            SummonWaterAttackPlan waterPlan = null)
        {
            if (!configured || model == null || clock == null) return;
            // Candidate configuration is inactive. Do not start audio/particles before cost commits.
            if (gameObject.activeInHierarchy && sealObject == null && lastAge == 0 && clock.Phase == SummonPhase.Forming) StartSeal();
            float dt = Mathf.Max(0, clock.Elapsed - lastAge); lastAge = clock.Elapsed;
            float speed = float.IsFinite(moveSpeed) ? Mathf.Max(0, moveSpeed) : 0;
            if (clock.Phase == SummonPhase.Forming || clock.Phase == SummonPhase.Dissolving || clock.Phase == SummonPhase.Ended) speed = 0;
            distance += speed * dt;
            movementWeight = Mathf.MoveTowards(movementWeight, speed > .03f ? 1 : 0, dt * 7);
            if (clock.Phase == SummonPhase.Attacking && attackId != clock.LastAcceptedAttackId)
            { attackId = clock.LastAcceptedAttackId; attackStarted = clock.Elapsed - clock.PhaseElapsed; currentAttackIsRoot = rootAttack; currentAttackIsFlame = flamePlan != null; currentAttackIsTiger=tigerPlan!=null; }
            if (graph.IsValid())
            {
                float age = clock.Elapsed - attackStarted;
                float clipLength = currentAttackIsRoot && profile.RootAttackClip != null ? profile.RootAttackClip.length :
                    currentAttackIsFlame && profile.FlameAttackClip != null ? profile.FlameAttackClip.length : profile.AttackClip.length;
                float attackWeight = !currentAttackIsTiger && age >= 0 && age < clipLength && clock.IsCombatActive
                    ? Mathf.Min(Mathf.Clamp01(age / .1f), Mathf.Clamp01((clipLength - age) / .15f)) : 0;
                TigerVisualStage=tigerPlan!=null?tigerPlan.Stage:TigerAttackStage.Finished;
                TigerVisualClipTime=tigerPlan!=null?tigerPlan.StageTime:0;
                bool tigerActive=tigerPlan!=null&&!tigerPlan.IsFinished&&clock.IsCombatActive;
                bool leapStage=tigerActive&&(TigerVisualStage==TigerAttackStage.Preparing||TigerVisualStage==TigerAttackStage.Leaping||TigerVisualStage==TigerAttackStage.LandingRecovery);
                float tigerWeight=tigerActive?Mathf.Clamp01((clock.Elapsed-tigerPlan.StartedAt)/.08f):0;
                float baseWeight=1-Mathf.Max(attackWeight,tigerWeight);
                float runTarget=profile.TigerAttackEnabled&&tigerRun.IsValid()?Mathf.InverseLerp(1.2f,2.4f,speed):0;
                runWeight=Mathf.MoveTowards(runWeight,runTarget,dt*8);
                idle.SetTime(clock.Elapsed % Mathf.Max(.01f, profile.IdleClip.length));
                walk.SetTime((distance / Mathf.Max(.1f, profile.WalkClipMetresPerSecond)) % Mathf.Max(.01f, profile.WalkClip.length));
                if (backwardWalk.IsValid()) backwardWalk.SetTime((distance / Mathf.Max(.1f, profile.WalkClipMetresPerSecond)) % Mathf.Max(.01f, profile.BackwardWalkClip.length));
                attack.SetTime(Mathf.Clamp(age, 0, profile.AttackClip.length));
                mixer.SetInputWeight(0, (1 - movementWeight) * baseWeight);
                bool back = movingBackward && backwardWalk.IsValid();
                mixer.SetInputWeight(1, back ? 0 : movementWeight * baseWeight*(1-runWeight));
                bool hasRootClip = currentAttackIsRoot && rootAttackClip.IsValid();
                bool hasFlameClip = currentAttackIsFlame && flameAttackClip.IsValid();
                mixer.SetInputWeight(2, hasRootClip || hasFlameClip ? 0 : attackWeight);
                mixer.SetInputWeight(3, hasRootClip ? attackWeight : 0);
                mixer.SetInputWeight(4, back ? movementWeight * baseWeight : 0);
                mixer.SetInputWeight(5, hasFlameClip ? attackWeight : 0);
                mixer.SetInputWeight(6,back?0:movementWeight*baseWeight*runWeight);
                mixer.SetInputWeight(7,leapStage?tigerWeight:0);
                mixer.SetInputWeight(8,tigerActive&&TigerVisualStage==TigerAttackStage.ClawLeft?tigerWeight:0);
                mixer.SetInputWeight(9,tigerActive&&TigerVisualStage==TigerAttackStage.ClawRight?tigerWeight:0);
                if(tigerRun.IsValid())tigerRun.SetTime((distance/Mathf.Max(.1f,profile.TigerRunClipMetresPerSecond))%Mathf.Max(.01f,profile.TigerRunClip.length));
                if(tigerLeap.IsValid())tigerLeap.SetTime(Mathf.Clamp(TigerVisualClipTime,0,profile.TigerLeapClip.length));
                if(tigerClawLeft.IsValid())tigerClawLeft.SetTime(Mathf.Clamp(TigerVisualClipTime,0,profile.TigerClawLeftClip.length));
                if(tigerClawRight.IsValid())tigerClawRight.SetTime(Mathf.Clamp(TigerVisualClipTime,0,profile.TigerClawRightClip.length));
                if (rootAttackClip.IsValid()) rootAttackClip.SetTime(Mathf.Clamp(age, 0, profile.RootAttackClip.length));
                if (flameAttackClip.IsValid()) flameAttackClip.SetTime(Mathf.Clamp(age, 0, profile.FlameAttackClip.length));
                mixer.SetInputWeight(10,0);mixer.SetInputWeight(11,0);
                if(clock.Phase==SummonPhase.Forming && formationPose.IsValid())
                {
                    for(int i=0;i<10;i++)mixer.SetInputWeight(i,0);
                    formationPose.SetTime(clock.FormationProgress01*profile.FormationClip.length);mixer.SetInputWeight(10,1);
                }
                else if(clock.Phase==SummonPhase.Dissolving && dissolvePose.IsValid())
                {
                    // Crossfade from the last combat pose as the existing dissolve clock starts.
                    float w=Mathf.SmoothStep(0,1,clock.DissolveProgress01*5);
                    for(int i=0;i<10;i++)mixer.SetInputWeight(i,mixer.GetInputWeight(i)*(1-w));
                    dissolvePose.SetTime(clock.DissolveProgress01*profile.DissolveClip.length);mixer.SetInputWeight(11,w);
                }
                graph.Evaluate(0);
                if (footPlacement != null) footPlacement.Sample(clock, speed > .03f);
            }
            LastRevealAge = clock.Phase == SummonPhase.Forming ? clock.FormationProgress01 * 1.2f
                : clock.Phase == SummonPhase.Dissolving ? 3.8f + clock.DissolveProgress01 * .8f
                : clock.Phase == SummonPhase.Ended ? 4.6f : 2f;
            float ground = transform.position.y - .03f, top = ground + Mathf.Max(.1f, profile.BodyHeight);
            foreach (var r in renderers) if (r != null) top = Mathf.Max(top, r.bounds.max.y + .04f);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                block.Clear(); block.SetFloat("_Age", LastRevealAge); block.SetFloat("_MotionTime", clock.Elapsed);
                block.SetFloat("_Ground", ground); block.SetFloat("_Height", top - ground);
                r.SetPropertyBlock(block); r.enabled = clock.Phase != SummonPhase.Ended;
            }
            if (sealObject != null)
            {
                sealObject.transform.SetPositionAndRotation(sealPosition, sealRotation);
                if (clock.Phase != SummonPhase.Forming) { Destroy(sealObject); sealObject = null; }
            }
            if (profile.Letter == "놈" || profile.Letter == "솜" || profile.Letter == "몸" || profile.Letter == "옴")
            {
                if(phaseParticles==null&&gameObject.activeInHierarchy)phaseParticles=gameObject.AddComponent<DemoSummonPhaseParticles>();
                if(phaseParticles!=null)phaseParticles.Sample(profile,clock);
            }
            if (profile.Letter != "놈" && profile.Letter != "솜" && profile.Letter != "몸" && profile.Letter != "옴" && clock.Phase == SummonPhase.Dissolving && !debrisStarted)
            {
                debrisStarted = true;
                if (profile.DissolveDebris != null)
                {
                    debrisObject = Instantiate(profile.DissolveDebris, transform, false);
                    debris = debrisObject.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (var ps in debris) { ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); ps.useAutoRandomSeed = false; ps.randomSeed = 601; ps.Play(false); }
                }
            }
            if (rootPlan != null && rootPresentation == null)
                rootPresentation = gameObject.AddComponent<DemoSummonRootPresentation>();
            if (rootPresentation != null) rootPresentation.Sample(profile, rootPlan, clock.Elapsed, clock.IsCombatActive);
            if (flamePlan != null && flamePresentation == null)
                flamePresentation = gameObject.AddComponent<DemoSummonFlamePresentation>();
            if (flamePresentation != null) flamePresentation.Sample(profile, flamePlan, clock.Elapsed, clock.IsCombatActive);
            if (waterPlan != null && waterPresentation == null)
                waterPresentation = gameObject.AddComponent<DemoSummonWaterPresentation>();
            if (waterPresentation != null) waterPresentation.Sample(profile, waterPlan, clock.Elapsed, clock.IsCombatActive);
            if (clock.Phase == SummonPhase.Ended) model.SetActive(false);
        }
        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
