using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // #308 D308-115 (SPEC-DEMO-SUMMON-COMBAT "D308-115 개별 행동"): one behaviour per summon letter, each behind its own
    // profile switch (all off by default). 곰 root mark · 놈 judge · 몸 beat · 솜 hunt · 옴 ranged ward.
    // The switches are copied into the actor when it is accepted, so a profile edited mid-fight never changes a running summon.
    // Event callbacks only queue; every hit is applied in the next tick through ApplySummonHit (no re-entry from a damage event).
    // Release (EndBehaviour308) drops every mark, subscription and the ward binding.
    public sealed partial class DemoSummonCombatManager : IRangedWard308
    {
        public int RootMarksSet {get;private set;}
        public int RootBursts {get;private set;}
        public int SinMarksSet {get;private set;}
        public int JudgeShots {get;private set;}
        public int BeatTriggers {get;private set;}
        public int BeatHits {get;private set;}
        public int BeatMisses {get;private set;}
        public int HuntPicks {get;private set;}
        public int HuntBoostedHits {get;private set;}
        public int WardBlocks {get;private set;}
        public int WardCharges308=>active?.Ward!=null?active.Ward.Charges:0;
        public bool WardBound308=>wardBound308;
        public int BehaviourSubscriptions308=>watched308.Count+watchedControllers308.Count;
        public int BehaviourMarks308=>rootMarks308.Count+sinMarks308.Count;
        public bool BeatPending308=>active!=null&&active.BeatTarget!=null;

        readonly Dictionary<EnemyVitals,SummonMark308> rootMarks308=new Dictionary<EnemyVitals,SummonMark308>();
        readonly Dictionary<EnemyVitals,SummonMark308> sinMarks308=new Dictionary<EnemyVitals,SummonMark308>();
        readonly List<EnemyVitals> watched308=new List<EnemyVitals>();
        readonly List<EnemyController> watchedControllers308=new List<EnemyController>();
        readonly Dictionary<EnemyController,bool> legacyAttacking308=new Dictionary<EnemyController,bool>();
        readonly List<EnemyVitals> burstQueue308=new List<EnemyVitals>();
        readonly List<EnemyVitals> beatQueue308=new List<EnemyVitals>();
        readonly List<EnemyVitals> sinQueue308=new List<EnemyVitals>();
        readonly List<EnemyVitals> scratch308=new List<EnemyVitals>();
        CombatLoopWiring parryHooked308;
        PlayerVitals wardVitals308;
        bool wardBound308;

        static bool AnyBehaviour(Actor actor)=>actor.RootMark308||actor.Judge308||actor.Beat308||actor.Hunt308||actor.Ward308;

        void BeginBehaviour308(Actor actor)
        {
            var p=actor.Profile;
            actor.RootMark308=p.RootMark308Enabled;actor.Judge308=p.JudgeEnabled;actor.Beat308=p.BeatEnabled;
            actor.Hunt308=p.HuntEnabled;actor.Ward308=p.WardEnabled;
            if(actor.Ward308)
            {
                actor.Ward=new WardCharges308(p.WardCharges,p.WardRechargeSeconds);
                if(PlayerVitals!=null&&PlayerVitals.BindRangedWard308(this)){wardVitals308=PlayerVitals;wardBound308=true;}
            }
            if(actor.Beat308&&Wiring!=null){parryHooked308=Wiring;Wiring.ParryCountered+=OnParryCountered308;}
        }

        void EndBehaviour308(Actor actor)
        {
            if(actor!=null){actor.BeatTarget=null;actor.Ward=null;}
            if(parryHooked308!=null){parryHooked308.ParryCountered-=OnParryCountered308;parryHooked308=null;}
            if(wardBound308){if(wardVitals308!=null)wardVitals308.UnbindRangedWard308(this);wardVitals308=null;wardBound308=false;}
            foreach(var target in watched308)if(target!=null)target.DamageResolved-=OnTargetDamage308;
            watched308.Clear();
            foreach(var controller in watchedControllers308)if(controller!=null)controller.AttackPresentationEnded-=OnAttackEnded308;
            watchedControllers308.Clear();legacyAttacking308.Clear();
            rootMarks308.Clear();sinMarks308.Clear();burstQueue308.Clear();beatQueue308.Clear();sinQueue308.Clear();
        }

        // Once per frame before the physical steps. Refreshes the watch lists, drops dead marks and applies the queued hits.
        void TickBehaviour308()
        {
            var actor=active;
            if(actor==null||actor.Root==null||!AnyBehaviour(actor))return;
            float now=actor.Clock.Elapsed;
            RefreshWatch308(actor);
            PruneMarks308(rootMarks308,now);PruneMarks308(sinMarks308,now);
            if(!actor.Clock.IsCombatActive){burstQueue308.Clear();beatQueue308.Clear();sinQueue308.Clear();actor.BeatTarget=null;return;}
            if(actor.Judge308)
            {
                foreach(var target in sinQueue308)
                    if(Eligible(actor,target)&&Registered(target))
                    {sinMarks308[target]=new SummonMark308(now+actor.Profile.JudgeMarkSeconds,target.LifeRevision);SinMarksSet++;}
                sinQueue308.Clear();
            }
            if(actor.RootMark308&&burstQueue308.Count>0)
            {
                scratch308.Clear();scratch308.AddRange(burstQueue308);burstQueue308.Clear();
                foreach(var target in scratch308)
                {
                    if(!rootMarks308.TryGetValue(target,out var mark))continue;
                    rootMarks308.Remove(target);
                    if(target==null||!target.isActiveAndEnabled||!target.IsAlive||!mark.Live(now,target.LifeRevision)||!Registered(target))continue;
                    var attack=AttackProvenance.Create(actor.Root,DamageSource.Summon,actor.Cast.Element);
                    var result=Wiring.ApplySummonHit(target,mark.Revision,actor.BasePower*actor.Profile.RootMarkBurstMultiplier,
                        target.transform.position+Vector3.up*.6f,attack,actor.Cast.Letter);
                    if(result.AppliedDamage>0){ConfirmedHits++;ConfirmedDamage+=result.AppliedDamage;RootBursts++;}
                    if(active!=actor||actor.Root==null)return;
                }
            }
            if(actor.Beat308)
            {
                if(actor.BeatTarget==null)
                    foreach(var target in beatQueue308)
                        if(SummonBeatRule308.CanQueue(actor.BeatTarget!=null,now,actor.BeatCooldownUntil)&&target!=null&&target.IsAlive&&Registered(target))
                        {actor.BeatTarget=target;actor.BeatRevision=target.LifeRevision;actor.BeatDueAt=now+actor.Profile.BeatDelay;BeatTriggers++;}
                beatQueue308.Clear();
                if(actor.BeatTarget!=null&&now>=actor.BeatDueAt)ResolveBeat308(actor,now);
            }
        }

        void ResolveBeat308(Actor actor,float now)
        {
            var target=actor.BeatTarget;uint revision=actor.BeatRevision;actor.BeatTarget=null;
            var p=actor.Profile;
            Vector3 origin=actor.Root.transform.position+Vector3.up*p.ClubOriginHeight;
            bool valid=target!=null&&target.isActiveAndEnabled&&target.IsAlive&&target.LifeRevision==revision&&Registered(target)&&Eligible(actor,target)&&
                HorizontalDistance(actor.Root.transform.position,target.transform.position)<=p.ClubRange&&
                Mathf.Abs(ClubAimPoint(target).y-origin.y)<=p.ClubHeightTolerance+p.BodyHeight&&Sight(actor,target);
            if(!valid){BeatMisses++;return;}
            var attack=AttackProvenance.Create(actor.Root,DamageSource.Summon,actor.Cast.Element);
            var result=Wiring.ApplySummonHit(target,revision,actor.ClubPower*p.BeatMultiplier,origin,attack,actor.Cast.Letter);
            if(result.AppliedDamage>0){ConfirmedHits++;ConfirmedDamage+=result.AppliedDamage;BeatHits++;actor.BeatCooldownUntil=now+p.BeatCooldown;}
            else BeatMisses++;
        }

        void RefreshWatch308(Actor actor)
        {
            var targets=Wiring.SummonTargets;
            bool vitals=actor.RootMark308||actor.Beat308,controllers=actor.Judge308;
            for(int i=watched308.Count-1;i>=0;i--)
            {
                var target=watched308[i];
                if(target!=null&&vitals&&Registered(target))continue;
                if(!ReferenceEquals(target,null))target.DamageResolved-=OnTargetDamage308;
                watched308.RemoveAt(i);
            }
            for(int i=watchedControllers308.Count-1;i>=0;i--)
            {
                var controller=watchedControllers308[i];
                if(controller!=null&&controllers&&Registered(controller.GetComponent<EnemyVitals>()))continue;
                if(!ReferenceEquals(controller,null))controller.AttackPresentationEnded-=OnAttackEnded308;
                watchedControllers308.RemoveAt(i);legacyAttacking308.Remove(controller);
            }
            for(int i=0;i<targets.Count;i++)
            {
                var target=targets[i];if(target==null)continue;
                if(vitals&&!watched308.Contains(target)){target.DamageResolved+=OnTargetDamage308;watched308.Add(target);}
                if(!controllers)continue;
                var controller=target.GetComponent<EnemyController>();if(controller==null)continue;
                if(!watchedControllers308.Contains(controller)){controller.AttackPresentationEnded+=OnAttackEnded308;watchedControllers308.Add(controller);}
                // Legacy (unauthored) attacks have no end event: an attack that ran (not stun-only) and fell back is "finished".
                bool attacking=controller.AttackProfile==null&&controller.AttackInProgress&&!controller.IsStunned;
                legacyAttacking308.TryGetValue(controller,out bool was);
                if(was&&!attacking&&controller.AttackProfile==null&&target.IsAlive)sinQueue308.Add(target);
                if(controller.AttackProfile==null&&controller.IsStunned)attacking=false;
                legacyAttacking308[controller]=attacking;
            }
        }

        static void PruneMarks308(Dictionary<EnemyVitals,SummonMark308> marks,float now)
        {
            if(marks.Count==0)return;
            List<EnemyVitals> dead=null;
            foreach(var pair in marks)
                if(pair.Key==null||!pair.Key.IsAlive||!pair.Value.Live(now,pair.Key.LifeRevision))(dead??=new List<EnemyVitals>()).Add(pair.Key);
            if(dead!=null)foreach(var key in dead)marks.Remove(key);
        }

        void OnTargetDamage308(EnemyDamageResult result)
        {
            var actor=active;var target=result.Target;
            if(actor==null||target==null||result.AppliedDamage<=0)return;
            if(actor.RootMark308&&result.Attack.Source==DamageSource.PlayerDirect&&rootMarks308.ContainsKey(target)&&!burstQueue308.Contains(target))
                burstQueue308.Add(target);
            if(actor.Beat308&&(result.Attack.Source==DamageSource.Retaliation||result.Attack.Instigator is Oheangbu.App.World.Riposte301))
                beatQueue308.Add(target);
        }

        void OnParryCountered308(EnemyVitals attacker,ParryImpactResult result,float groggy01,bool blossomed)
        {
            var actor=active;
            if(actor!=null&&actor.Beat308&&attacker!=null&&result.Outcome==ParryOutcome.Success)beatQueue308.Add(attacker);
        }

        void OnAttackEnded308(AttackProvenance attack,bool cancelled)
        {
            var actor=active;
            if(actor==null||!actor.Judge308||cancelled)return;
            if(attack.Instigator is EnemyVitals source&&source!=null&&!sinQueue308.Contains(source))sinQueue308.Add(source);
        }

        // 곰: a confirmed basic or root hit marks the target (one mark per enemy, a new hit refreshes it).
        void MarkRoot308(Actor actor,EnemyVitals target)
        {
            if(active!=actor||actor.Root==null||!actor.RootMark308||target==null||!target.IsAlive)return;
            rootMarks308[target]=new SummonMark308(actor.Clock.Elapsed+actor.Profile.RootMarkSeconds,target.LifeRevision);RootMarksSet++;
        }

        bool SinMarked308(Actor actor,EnemyVitals target)=>target!=null&&sinMarks308.TryGetValue(target,out var mark)&&mark.Live(actor.Clock.Elapsed,target.LifeRevision);

        // 놈: only a marked enemy may be burned, once per interval.
        bool JudgeAllows308(Actor actor)=>!actor.Judge308||actor.Clock.Elapsed>=actor.JudgeNextAt&&SinMarked308(actor,actor.Target);

        // FindTarget's behaviour step. Returns true when the behaviour decided (target may be null = stay near the player).
        bool TryBehaviourTarget308(Actor actor,out EnemyVitals chosen)
        {
            chosen=null;
            if(actor.Hunt308)
            {
                var targets=Wiring.SummonTargets;float bestHp=float.MaxValue,bestDistance=float.MaxValue;
                for(int i=0;i<targets.Count;i++)
                {
                    var target=targets[i];if(!Eligible(actor,target)||target.Hp01>actor.Profile.HuntHpThreshold)continue;
                    float distance=(target.transform.position-actor.Root.transform.position).sqrMagnitude;
                    if(SummonHuntRule308.Better(target.Hp01,distance,bestHp,bestDistance)&&Sight(actor,target))
                    {chosen=target;bestHp=target.Hp01;bestDistance=distance;}
                }
                if(chosen!=null){HuntPicks++;return true;}
                return false;
            }
            if(actor.Judge308)
            {
                var locked=Wiring.SummonLockTarget;
                if(Registered(locked)&&Eligible(actor,locked)&&SinMarked308(actor,locked)&&Sight(actor,locked)){chosen=locked;return true;}
                float best=float.MaxValue;var targets=Wiring.SummonTargets;
                for(int i=0;i<targets.Count;i++)
                {
                    var target=targets[i];if(!Eligible(actor,target)||!SinMarked308(actor,target))continue;
                    float distance=(target.transform.position-actor.Root.transform.position).sqrMagnitude;
                    if(distance<best&&Sight(actor,target)){chosen=target;best=distance;}
                }
                return true;
            }
            return false;
        }

        float HuntScale308(Actor actor,EnemyVitals target)
        {
            if(!actor.Hunt308||target==null||target.Hp01>actor.Profile.HuntHpThreshold)return 1f;
            return actor.Profile.HuntMultiplier;
        }

        float FollowDistance308(Actor actor)=>actor.Judge308?actor.Profile.JudgeFollowDistance:actor.Ward308?actor.Profile.WardFollowDistance:actor.Profile.FollowDistance;

        // The following spot: behind the player, or for 몸 beside the lock-on target (BeatSideDistance), on the player's side.
        Vector3 FollowSpot308(Actor actor,Vector3 player)
        {
            if(actor.Beat308)
            {
                var locked=Wiring.SummonLockTarget;
                if(locked!=null&&locked.IsAlive&&Registered(locked)&&HorizontalDistance(locked.transform.position,player)<=actor.Profile.LeashRange)
                {
                    Vector3 toward=Vector3.ProjectOnPlane(player-locked.transform.position,Vector3.up);
                    if(toward.sqrMagnitude<.0001f)toward=-Wiring.SummonPlayer.forward;
                    Vector3 side=Vector3.Cross(Vector3.up,toward.normalized);
                    Vector3 a=locked.transform.position+side*actor.Profile.BeatSideDistance,b=locked.transform.position-side*actor.Profile.BeatSideDistance;
                    Vector3 spot=HorizontalDistance(a,actor.Root.transform.position)<=HorizontalDistance(b,actor.Root.transform.position)?a:b;
                    spot.y=actor.Root.transform.position.y;return spot;
                }
            }
            Vector3 forward=Vector3.ProjectOnPlane(Wiring.SummonPlayer.forward,Vector3.up).normalized;
            return player-forward*FollowDistance308(actor);
        }
        bool BesideTarget308(Actor actor)=>actor.Beat308&&Wiring.SummonLockTarget!=null&&Wiring.SummonLockTarget.IsAlive&&Registered(Wiring.SummonLockTarget)&&
            HorizontalDistance(Wiring.SummonLockTarget.transform.position,Wiring.SummonPlayer.position)<=actor.Profile.LeashRange;

        // 옴: a ranged strike that would land is stopped when the player has no parry guard and a charge remains.
        public bool TryBlockRanged308(EnemyVitals attacker,float amount,IncomingDamageKind kind)
        {
            var actor=active;
            if(actor==null||actor.Root==null||!actor.Ward308||actor.Ward==null||!actor.Clock.IsCombatActive||Wiring==null)return false;
            if(kind!=IncomingDamageKind.Ranged&&kind!=IncomingDamageKind.ElementalRanged)return false;
            if(Wiring.DeployGuardStands308(out _))return false;
            if(!actor.Ward.TrySpend(actor.Clock.Elapsed))return false;
            WardBlocks++;
            Vector3 chest=Wiring.SummonPlayer.position+Vector3.up*1.2f;
            if(attacker!=null)
            {
                Vector3 toward=attacker.transform.position+Vector3.up*1f-chest;
                if(toward.sqrMagnitude>.0001f)chest+=toward.normalized*.6f;
            }
            Wiring.SpawnWardSplash308(chest,actor.Cast.Element);
            return true;
        }

        // 곰 · 놈 marks drawn on the enemy (PresentState308 bits 128 = root, 256 = sin). Read only.
        public int Mark308(EnemyVitals target)
        {
            var actor=active;if(actor==null||target==null)return 0;
            float now=actor.Clock.Elapsed;int bits=0;
            if(rootMarks308.TryGetValue(target,out var root)&&root.Live(now,target.LifeRevision))bits|=128;
            if(sinMarks308.TryGetValue(target,out var sin)&&sin.Live(now,target.LifeRevision))bits|=256;
            return bits;
        }
    }

    // PURE308: a mark that lives until a summon-clock time, only for one enemy life.
    public readonly struct SummonMark308
    {
        public readonly float ExpiresAt;
        public readonly uint Revision;
        public SummonMark308(float expiresAt,uint revision){ExpiresAt=expiresAt;Revision=revision;}
        public bool Live(float now,uint revision)=>now<ExpiresAt&&revision==Revision;
    }

    // PURE308: 옴's charges. Starts full; one charge returns per recharge period, counted from the first spend while full.
    public sealed class WardCharges308
    {
        public readonly int Max;
        public readonly float Recharge;
        public int Charges {get;private set;}
        public float NextAt {get;private set;}
        public WardCharges308(int max,float recharge)
        {
            if(max<1||!float.IsFinite(recharge)||recharge<=0)throw new ArgumentOutOfRangeException(nameof(max));
            Max=max;Recharge=recharge;Charges=max;NextAt=float.PositiveInfinity;
        }
        public void Tick(float now)
        {
            while(Charges<Max&&now>=NextAt){Charges++;NextAt=Charges<Max?NextAt+Recharge:float.PositiveInfinity;}
        }
        public bool TrySpend(float now)
        {
            Tick(now);if(Charges<=0)return false;
            if(Charges==Max)NextAt=now+Recharge;
            Charges--;return true;
        }
    }

    // PURE308: 솜 picks the lowest HP fraction; equal fractions go to the nearer one.
    public static class SummonHuntRule308
    {
        public static bool Better(float hp01,float sqrDistance,float bestHp01,float bestSqrDistance)
            =>hp01<bestHp01||Mathf.Approximately(hp01,bestHp01)&&sqrDistance<bestSqrDistance;
    }

    // PURE308: 몸 holds one beat at a time and waits out the cooldown counted from its last landed beat.
    public static class SummonBeatRule308
    {
        public static bool CanQueue(bool pending,float now,float cooldownUntil)=>!pending&&now>=cooldownUntil;
    }
}
