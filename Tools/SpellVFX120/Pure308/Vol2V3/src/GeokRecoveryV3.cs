using System;
using System.Collections.Generic;
namespace Oheangbu.Vol2.Rules
{
    // D308-145 걱의 HP 장부. 옛 CSV 공격 예산과 분리한 묶음 4의 자기 회복 전이이다.
    public static class GeokRecoveryV3
    {
        public enum Action { Begin, Tick, Damage, Cancel, Rest }
        public sealed class State
        {
            public DefenceNumbersV3Dto Numbers { get; }
            public decimal OutsideRemaining { get; }
            public bool Active { get; }
            public long CastId { get; }
            public long StartedUs { get; }
            public long LastUs { get; }
            public decimal Reserved { get; }
            public decimal ElapsedBudget { get; }
            internal HashSet<string> Encounters { get; }
            internal HashSet<long> Casts { get; }
            internal HashSet<long> Events { get; }
            internal HashSet<long> Windows { get; }
            public State(DefenceNumbersV3Dto n) : this(n,n.OutsideHealingBudget,false,0,0,0,0,0,
                new HashSet<string>(),new HashSet<long>(),new HashSet<long>(),new HashSet<long>()) { }
            internal State(DefenceNumbersV3Dto n,decimal outside,bool active,long cast,long start,long last,decimal reserved,decimal elapsed,
                HashSet<string> encounters,HashSet<long> casts,HashSet<long> events,HashSet<long> windows)
            { Numbers=n; OutsideRemaining=outside; Active=active; CastId=cast; StartedUs=start; LastUs=last; Reserved=reserved;
              ElapsedBudget=elapsed; Encounters=encounters; Casts=casts; Events=events; Windows=windows; }
        }
        public sealed class Snapshot
        {
            public decimal OutsideRemaining { get; }
            public bool Active { get; }
            public long CastId { get; }
            public long StartedUs { get; }
            public long LastUs { get; }
            public decimal Reserved { get; }
            public decimal ElapsedBudget { get; }
            public string[] Encounters { get; }
            public long[] Casts { get; }
            public long[] Events { get; }
            public long[] Windows { get; }
            public Snapshot(decimal outsideRemaining,bool active,long castId,long startedUs,long lastUs,decimal reserved,decimal elapsedBudget,
                string[] encounters,long[] casts,long[] events,long[] windows)
            { OutsideRemaining=outsideRemaining; Active=active; CastId=castId; StartedUs=startedUs; LastUs=lastUs; Reserved=reserved;
              ElapsedBudget=elapsedBudget; Encounters=(string[])encounters.Clone(); Casts=(long[])casts.Clone();
              Events=(long[])events.Clone(); Windows=(long[])windows.Clone(); }
        }
        public static Snapshot Save(State s) => new Snapshot(s.OutsideRemaining,s.Active,s.CastId,s.StartedUs,s.LastUs,
            s.Reserved,s.ElapsedBudget,new List<string>(s.Encounters).ToArray(),new List<long>(s.Casts).ToArray(),
            new List<long>(s.Events).ToArray(),new List<long>(s.Windows).ToArray());
        public static State Restore(DefenceNumbersV3Dto n,Snapshot data)
        {
            if(data.OutsideRemaining<0||data.OutsideRemaining>n.OutsideHealingBudget||data.Reserved<0||data.Reserved>n.HealingHp||
                data.ElapsedBudget<0||data.ElapsedBudget>data.Reserved||data.StartedUs<0||data.LastUs<data.StartedUs||data.CastId<0||
                data.Active&&(data.CastId==0||data.Reserved==0))throw new ArgumentException("Recovery snapshot");
            return new State(n,data.OutsideRemaining,data.Active,data.CastId,data.StartedUs,data.LastUs,data.Reserved,data.ElapsedBudget,
                new HashSet<string>(data.Encounters),new HashSet<long>(data.Casts),new HashSet<long>(data.Events),new HashSet<long>(data.Windows));
        }
        public sealed class Input
        {
            public long EventId { get; }
            public long CastId { get; }
            public long NowUs { get; }
            public Action Action { get; }
            public decimal Hp { get; }
            public decimal MaxHp { get; }
            public decimal Ink { get; }
            public decimal ActualDamage { get; }
            public bool Engaged { get; }
            public long EncounterId { get; }
            public long BossPhaseId { get; }
            public long WindowId { get; }
            public bool WindowOpen { get; }
            public Input(long id,Action action,long nowUs,long castId=0,decimal hp=0,decimal maxHp=0,decimal ink=0,
                decimal actualDamage=0,bool engaged=false,long encounterId=0,long bossPhaseId=0,long windowId=0,bool windowOpen=false)
            { EventId=id; Action=action; NowUs=nowUs; CastId=castId; Hp=hp; MaxHp=maxHp; Ink=ink; ActualDamage=actualDamage;
              Engaged=engaged; EncounterId=encounterId; BossPhaseId=bossPhaseId; WindowId=windowId; WindowOpen=windowOpen; }
        }
        public sealed class Step
        {
            public State State { get; }
            public bool Accepted { get; }
            public string Reason { get; }
            public long CommandId { get; }
            public string CommandKey => CommandId==0?string.Empty:"recovery:"+CommandId;
            public decimal HpDelta { get; }
            public decimal InkDelta { get; }
            public bool IsReactionMaterial => false;
            public bool PausesInkRecovery => false;
            public decimal AttackBudget => 0;
            public bool ConsumesWindow { get; }
            internal Step(State s,bool accepted,string reason,long id=0,decimal hp=0,decimal ink=0,bool window=false)
            { State=s; Accepted=accepted; Reason=reason; CommandId=id; HpDelta=hp; InkDelta=ink; ConsumesWindow=window; }
        }
        public static Step Apply(State s,Input e)
        {
            if((uint)e.Action>(uint)Action.Rest||e.EventId<=0||e.NowUs<s.LastUs||e.Hp<0||e.MaxHp<e.Hp||e.Ink<0||e.ActualDamage<0)return new Step(s,false,"Input");
            if(s.Events.Contains(e.EventId))return new Step(s,false,"DuplicateEvent");
            var n=s.Numbers; var encounters=new HashSet<string>(s.Encounters);var casts=new HashSet<long>(s.Casts);
            var events=new HashSet<long>(s.Events);var windows=new HashSet<long>(s.Windows);
            decimal outside=s.OutsideRemaining,reserved=s.Reserved,elapsed=s.ElapsedBudget,hp=0,ink=0;
            long cast=s.CastId,start=s.StartedUs;bool active=s.Active,window=false;
            if(e.Action==Action.Begin)
            {
                if(e.CastId<=0||casts.Contains(e.CastId)||active||s.CastId!=0&&e.NowUs-s.StartedUs<n.HealingCooldownUs)return new Step(s,false,"CooldownOrCast");
                if(e.Ink<n.HealingCost||e.Hp<=0)return new Step(s,false,"InkOrDead");
                string encounter=e.EncounterId+":"+e.BossPhaseId;
                if(e.Engaged)
                {
                    if(e.EncounterId<=0||e.BossPhaseId<0||!e.WindowOpen||e.WindowId<=0||windows.Contains(e.WindowId)||encounters.Contains(encounter))return new Step(s,false,"EncounterOrWindow");
                    reserved=n.HealingHp;encounters.Add(encounter);windows.Add(e.WindowId);window=true;
                }
                else {reserved=Math.Min(n.HealingHp,outside);if(reserved<=0)return new Step(s,false,"OutsideBudget");outside-=reserved;}
                cast=e.CastId;start=e.NowUs;elapsed=0;active=true;ink=-n.HealingCost;casts.Add(cast);
            }
            else if(e.Action==Action.Rest)
            { outside=n.OutsideHealingBudget; active=false; }
            else
            {
                if(e.CastId!=s.CastId||!active)return new Step(s,false,"InactiveOrStaleCast");
                if(e.Action==Action.Cancel||e.Action==Action.Damage&&e.ActualDamage>0)active=false;
                else if(e.Action==Action.Tick)
                {
                    decimal due=reserved*Math.Min(e.NowUs-start,n.HealingLifeUs)/n.HealingLifeUs;
                    hp=e.Hp>0?Math.Min(e.MaxHp-e.Hp,due-elapsed):0;elapsed=due;
                    if(e.NowUs-start>=n.HealingLifeUs||e.Hp<=0)active=false;
                }
            }
            events.Add(e.EventId);
            return new Step(new State(n,outside,active,cast,start,e.NowUs,reserved,elapsed,encounters,casts,events,windows),
                true,"Applied",e.EventId,hp,ink,window);
        }
    }
}
