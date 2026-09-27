using System;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.Demo
{
    public sealed partial class DemoSummonCombatManager
    {
        void HandleTigerTarget(Actor actor,float dt)
        {
            float distance=HorizontalDistance(actor.Root.transform.position,actor.Target.transform.position);
            var profile=actor.Profile;
            if(distance>=profile.TigerLeapMinRange&&distance<=profile.TigerLeapMaxRange&&Sight(actor,actor.Target))
            {
                if(BeginTigerAttack(actor,true,dt))return;
                // An unsupported straight leap can still approach using the normal safe NavMesh route.
            }
            else if(InAttackRange(actor,actor.Target)&&Sight(actor,actor.Target))
            {BeginTigerAttack(actor,false,dt);return;}
            actor.Clock.SetActivityPhase(SummonPhase.Approaching);
            Navigate(actor,actor.Target.transform.position,dt,profile.AttackRange*.85f,null,false,profile.TigerRunSpeed);
        }

        bool BeginTigerAttack(Actor actor,bool leap,float dt)
        {
            var profile=actor.Profile;
            float duration=(leap?profile.TigerLeapSeconds:0)+profile.TigerClawSeconds*2;
            if(!actor.Clock.CanAttack||actor.Clock.ActivityRemaining<=duration||
                !TryFace(actor,actor.Target.transform.position,dt*240))return false;
            Vector3 start=actor.Root.transform.position,end=start;
            Quaternion rotation=actor.Root.transform.rotation;
            if(leap)
            {
                if(!Ground(profile,actor.Target.transform.position,out var targetGround))return false;
                end=targetGround-actor.Root.transform.forward*profile.TigerLandingStandOff;
                if(!Ground(profile,end,out end))return false;
            }
            if(!TryTigerRoute(actor,start,end,rotation,leap,out var ground))return false;
            var target=actor.Target;
            var plan=new SummonTigerAttackPlan(ground,actor.Root.transform.forward,target.GetInstanceID(),target.LifeRevision,
                leap,actor.Clock.Elapsed,profile.TigerLeapPrepareSeconds,profile.TigerLeapLandSeconds,profile.TigerLeapSeconds,
                profile.TigerClawSeconds,profile.TigerClawContactSeconds,profile.TigerLeapArcHeight);
            actor.TigerPlan=plan;actor.TigerTarget=target;actor.TigerNextClaw=0;actor.TigerClawArmed=false;
            if(!ArmTigerClaw(actor))
            {actor.TigerPlan=null;actor.TigerTarget=null;return false;}
            actor.AttackIsRoot=false;actor.HasPath=false;actor.AttackStarted=plan.StartedAt;
            actor.AttackDuration=plan.EndAt-plan.StartedAt;actor.RecoveryUntil=plan.EndAt;
            AttackStarts++;TigerAttackStarts++;if(leap)TigerLeapStarts++;
            return true;
        }

        bool ArmTigerClaw(Actor actor)
        {
            var plan=actor.TigerPlan;
            if(plan==null||actor.TigerNextClaw>1||!actor.Clock.CanAttack)return false;
            float due=actor.TigerNextClaw==0?plan.LeftContactAt:plan.RightContactAt;
            var strike=AttackProvenance.Create(actor.Root,DamageSource.Summon,actor.Cast.Element);
            // The second contact retains its original clip timestamp even if a float boundary delayed arming.
            if(!actor.Clock.TryBeginAttack(strike.AttackId,plan.TargetId,plan.TargetLifeRevision,
                Mathf.Max(0,due-actor.Clock.Elapsed),actor.TigerNextClaw==0?0:actor.Profile.CooldownSeconds))return false;
            actor.Strike=strike;actor.TigerClawArmed=true;return true;
        }

        void TickTigerAttack(Actor actor)
        {
            var plan=actor.TigerPlan;plan.AdvanceTo(actor.Clock.Elapsed);
            if(plan.IsCancelled)return;
            var target=actor.TigerTarget;
            if(!TigerTargetLifeValid(actor)||HorizontalDistance(actor.Root.transform.position,Wiring.SummonPlayer.position)>actor.Profile.LeashRange)
            {CancelTigerAttack(actor);return;}
            if(plan.UsesLeap&&actor.Clock.Elapsed>=plan.TakeoffAt&&actor.Clock.Elapsed<=plan.LandAt+.05f)
            {
                // Position follows only the captured ground route. The baked pelvis arc is visual, not root motion.
                Vector3 next=plan.GroundAt(plan.LeapProgress),current=actor.Root.transform.position;
                if(!TigerStepClear(actor,current,next,actor.Root.transform.rotation,plan.ArcHeight))
                {CancelTigerAttack(actor);return;}
                actor.Root.transform.position=next;
            }
            if(!FootSupport(actor.Profile,actor.Root.transform.position,actor.Root.transform.rotation)||
                !TigerVolumeClear(actor.Profile,actor.Root.transform.position,actor.Root.transform.rotation,
                    plan.Stage==TigerAttackStage.Leaping?plan.ArcHeight:0,0,0))
            {CancelTigerAttack(actor);return;}
            // Missing a landing or a target evading the leap cancels this combo at the last safe pose.
            if(actor.Clock.Elapsed>=plan.LandAt&&(HorizontalDistance(actor.Root.transform.position,plan.End)>.08f||
                !TigerClawCanReach(actor,target)||!Sight(actor,target)))
            {CancelTigerAttack(actor);return;}
            if(actor.TigerNextClaw<2&&!actor.TigerClawArmed)
            {
                float start=actor.TigerNextClaw==0?plan.LeftStartedAt:plan.RightStartedAt;
                if(actor.Clock.Elapsed>=start&&!ArmTigerClaw(actor)&&actor.Clock.PendingAttackId!=0)
                {CancelTigerAttack(actor);return;}
            }
            if(!actor.TigerClawArmed)return;
            float contact=actor.TigerNextClaw==0?plan.LeftContactAt:plan.RightContactAt;
            if(actor.Clock.Elapsed<contact)return;
            bool valid=TigerTargetLifeValid(actor)&&TigerClawCanReach(actor,target)&&Sight(actor,target);
            bool consumed=actor.Clock.TryConsumeHit(actor.Strike.AttackId,plan.TargetId,plan.TargetLifeRevision,valid,valid,valid);
            if(!consumed)
            {
                // Float display time may round up before the clock's double contact boundary.
                if(actor.Clock.PendingAttackId==actor.Strike.AttackId)return;
                CancelTigerAttack(actor);return;
            }
            actor.TigerClawArmed=false;actor.TigerNextClaw++;ConsumedStrikes++;TigerClawContacts++;
            var result=Wiring.ApplySummonHit(target,plan.TargetLifeRevision,actor.TigerPower,StrikeOrigin(actor),actor.Strike,actor.Cast.Letter);
            if(result.AppliedDamage>0){ConfirmedHits++;ConfirmedDamage+=result.AppliedDamage;}
            if(active!=actor||actor.Root==null)return;
            // Shared callbacks can kill/revive or unregister the fixed target; never transfer the next claw.
            if(!TigerTargetLifeValid(actor))CancelTigerAttack(actor);
        }

        bool TigerTargetLifeValid(Actor actor)
        {
            var target=actor.TigerTarget;var plan=actor.TigerPlan;
            return plan!=null&&target!=null&&target.GetInstanceID()==plan.TargetId&&target.LifeRevision==plan.TargetLifeRevision&&
                Registered(target)&&Eligible(actor,target);
        }
        bool TigerClawCanReach(Actor actor,EnemyVitals target)
        {
            if(!InAttackRange(actor,target))return false;
            Vector3 offset=Vector3.ProjectOnPlane(target.transform.position-actor.Root.transform.position,Vector3.up);
            return offset.sqrMagnitude<.001f||Vector3.Dot(offset.normalized,actor.TigerPlan.Direction)>=.5f;
        }
        void CancelTigerAttack(Actor actor)
        {
            if(actor.TigerPlan==null||actor.TigerPlan.IsCancelled)return;
            actor.TigerPlan.Cancel();actor.Clock.CancelAttack();actor.TigerClawArmed=false;actor.HasPath=false;
            actor.RecoveryUntil=Mathf.Max(actor.Clock.Elapsed+actor.Profile.TigerRecoverySeconds,actor.RecoveryUntil);
            TigerAttackCancellations++;
        }

        bool TryTigerRoute(Actor actor,Vector3 start,Vector3 end,Quaternion rotation,bool leap,out Vector3[] ground)
        {
            ground=Array.Empty<Vector3>();var profile=actor.Profile;
            float length=HorizontalDistance(start,end);
            int segments=Mathf.Max(1,Mathf.CeilToInt(length/.15f));
            if(segments>=128)return false;
            var points=new Vector3[segments+1];points[0]=start;
            for(int i=1;i<=segments;i++)
            {
                Vector3 sample=Vector3.Lerp(start,end,(float)i/segments);
                if(!Ground(profile,sample,out var feet)||Mathf.Abs(feet.y-sample.y)>profile.FootHeightTolerance+.05f||
                    !TigerStepClear(actor,points[i-1],feet,rotation,leap?profile.TigerLeapArcHeight:0))return false;
                points[i]=feet;
            }
            ground=points;return true;
        }
        bool TigerStepClear(Actor actor,Vector3 from,Vector3 to,Quaternion rotation,float arc)
        {
            var profile=actor.Profile;
            if(HorizontalDistance(to,Wiring.SummonPlayer.position)>profile.LeashRange||
                !Ground(profile,to,out var actual)||Mathf.Abs(actual.y-to.y)>.08f||
                Mathf.Abs(to.y-from.y)>profile.FootHeightTolerance+.05f||!FootSupport(profile,to,rotation)||
                !NavMesh.SamplePosition(from,out var startNav,.35f,profile.NavMeshAreaMask)||
                !NavMesh.SamplePosition(to,out var endNav,.35f,profile.NavMeshAreaMask)||
                Vector3.Distance(startNav.position,from)>.4f||Vector3.Distance(endNav.position,to)>.4f||
                NavMesh.Raycast(startNav.position,endNav.position,out _,profile.NavMeshAreaMask))return false;
            if(!SweptPoseClear(profile,from,rotation,to,rotation))return false;
            Vector3 midpoint=(from+to)*.5f;
            return TigerVolumeClear(profile,midpoint,rotation,arc,HorizontalDistance(from,to)*.5f,Mathf.Abs(to.y-from.y)*.5f);
        }
        bool TigerVolumeClear(SummonCombatProfile profile,Vector3 feet,Quaternion rotation,float arc,float horizontalPadding,float verticalPadding)
        {
            var physics=gameObject.scene.GetPhysicsScene();if(!physics.IsValid())return false;
            // Raise only the ceiling envelope. Expanding downwards by the pelvis arc would collide with ground.
            Vector3 half=new Vector3(profile.FootprintWidth*.5f+horizontalPadding,
                Mathf.Max(.05f,profile.BodyHeight*.5f-.08f)+arc*.5f+verticalPadding,
                profile.FootprintLength*.5f+horizontalPadding);
            Vector3 center=feet+rotation*profile.FootprintOffset+Vector3.up*(profile.BodyHeight*.5f+.08f+arc*.5f);
            int count=physics.OverlapBox(center,half,overlaps,rotation,profile.GroundMask,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return false;
            for(int i=0;i<count;i++)if(!IgnoreCharacter(overlaps[i]))return false;
            return true;
        }
    }
}
