using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace Oheangbu.Vol2.Rules
{
    public static class NoInkSwingV3
    {
        public readonly struct TargetKey : IEquatable<TargetKey>
        {
            public long EncounterId { get; }
            public long TargetId { get; }
            public TargetKey(long encounterId,long targetId){EncounterId=encounterId;TargetId=targetId;}
            public bool Equals(TargetKey other)=>EncounterId==other.EncounterId&&TargetId==other.TargetId;
            public override bool Equals(object other)=>other is TargetKey key&&Equals(key);
            public override int GetHashCode()=>EncounterId.GetHashCode()*397^TargetId.GetHashCode();
        }
        public sealed class State
        {
            public DefenceNumbersV3Dto Numbers { get; }
            public ReadOnlyDictionary<TargetKey,decimal> Remaining { get; }
            public long SwingId { get; }
            public long StartedUs { get; }
            public long LastUs { get; }
            public bool Hit { get; }
            internal HashSet<long> Swings { get; }
            internal HashSet<long> Events { get; }
            public State(DefenceNumbersV3Dto n) : this(n,new Dictionary<TargetKey,decimal>(),0,0,0,false,new HashSet<long>(),new HashSet<long>()) { }
            internal State(DefenceNumbersV3Dto n,Dictionary<TargetKey,decimal> remaining,long swing,long start,long last,bool hit,HashSet<long> swings,HashSet<long> events)
            { Numbers=n; Remaining=new ReadOnlyDictionary<TargetKey,decimal>(remaining); SwingId=swing;
              StartedUs=start; LastUs=last; Hit=hit; Swings=swings; Events=events; }
        }
        public sealed class Carry
        {
            public long CastId { get; }
            public long MaterialId { get; }
            public long OwnerId { get; }
            public char Letter { get; }
            public bool Seed { get; }
            public Carry(long castId,long materialId,long ownerId,char letter,bool seed=false)
            { CastId=castId; MaterialId=materialId; OwnerId=ownerId; Letter=letter; Seed=seed; }
        }
        public sealed class Input
        {
            public long EventId { get; }
            public long SwingId { get; }
            public long NowUs { get; }
            public long OwnerId { get; }
            public TargetKey Target { get; }
            public long LifeRevision { get; }
            public bool Enemy { get; }
            public bool Boss { get; }
            public decimal Hp { get; }
            public decimal Ink { get; }
            public decimal InkCapacity { get; }
            public bool CriticalWindow { get; }
            public ReadOnlyCollection<Carry> Carries { get; }
            public Input(long eventId,long swingId,long nowUs,long encounterId=0,long targetId=0,decimal hp=0,
                decimal ink=0,decimal inkCapacity=0,bool enemy=true,bool boss=false,long ownerId=0,long lifeRevision=0,
                bool criticalWindow=false,IEnumerable<Carry> carries=null)
            { EventId=eventId; SwingId=swingId; NowUs=nowUs; Target=new TargetKey(encounterId,targetId); Hp=hp; Ink=ink;
              InkCapacity=inkCapacity; Enemy=enemy; Boss=boss; OwnerId=ownerId; LifeRevision=lifeRevision;
              CriticalWindow=criticalWindow; Carries=RuleTypesV3.Freeze(carries??new Carry[0]); }
        }
        public sealed class Step
        {
            public State State { get; }
            public bool Accepted { get; }
            public string Reason { get; }
            public long CommandId { get; }
            public string CommandKey => CommandId==0?string.Empty:"gather:"+CommandId;
            public decimal Damage { get; }
            public decimal InkDelta { get; }
            public decimal Groggy => 0;
            public bool CreatesMaterial => false;
            public ReadOnlyCollection<Contact> Contacts { get; }
            internal Step(State s,bool accepted,string reason,long id=0,decimal damage=0,decimal ink=0,IEnumerable<Contact> contacts=null)
            { State=s; Accepted=accepted; Reason=reason; CommandId=id; Damage=damage; InkDelta=ink;
              Contacts=RuleTypesV3.Freeze(contacts??new Contact[0]); }
        }
        public static Step Resolve(State s,Input e)
        {
            if(e.EventId<=0||e.SwingId<=0||e.NowUs<s.LastUs||e.Hp<0||e.Ink<0||e.InkCapacity<e.Ink||e.LifeRevision<0||
                e.Target.EncounterId<0||e.Target.TargetId<0)return new Step(s,false,"Input");
            if(s.Events.Contains(e.EventId))return new Step(s,false,"DuplicateEvent");
            bool fresh=e.SwingId!=s.SwingId;
            if(fresh&&(s.Swings.Contains(e.SwingId)||s.SwingId!=0&&e.NowUs-s.StartedUs<s.Numbers.SwingCooldownUs))return new Step(s,false,"CooldownOrOldSwing");
            if(!fresh&&s.Hit)return new Step(s,false,"FirstEnemyOnly");
            var remaining=new Dictionary<TargetKey,decimal>(s.Remaining); var events=new HashSet<long>(s.Events){e.EventId};
            var swings=new HashSet<long>(s.Swings){e.SwingId}; var contacts=new List<Contact>();
            decimal damage=0,ink=0; bool hit=e.Enemy&&e.Target.TargetId>0&&e.Target.EncounterId>0&&e.Hp>0;
            if(hit)
            {
                var n=s.Numbers;
                // D308-147의 모든 피해 B를 적용한 뒤 비치명 바닥을 자른다. 세·상극은 없다.
                damage=Math.Min(Math.Max(0,e.Hp-n.HpFloor),n.Damage(n.SwingDamage,1,1,false,false,e.CriticalWindow));
                decimal budget=remaining.TryGetValue(e.Target,out var value)?value:e.Boss?n.GatherBoss:n.GatherNormal;
                ink=Math.Min(budget,Math.Min(n.GatherPerHit,e.InkCapacity-e.Ink)); remaining[e.Target]=budget-ink;
                var carried=new HashSet<string>();
                foreach(var carry in e.Carries)
                {
                    if(carry.CastId<=0||carry.MaterialId<=0||carry.OwnerId!=e.OwnerId||!GlyphRulesV3.TryDecode(carry.Letter,out var glyph)||glyph.Letter=='걱')continue;
                    bool eligible=carry.Seed?glyph.Frame==Frame.Path&&glyph.Verb==Verb.Seed:
                        glyph.Frame==Frame.Attachment&&!glyph.IsParry&&glyph.Verb!=Verb.Seed;
                    if(!eligible||!carried.Add(carry.CastId+":"+carry.MaterialId))continue;
                    contacts.Add(new Contact(e.EventId,carry.CastId,e.Target.TargetId,e.NowUs,e.SwingId,
                        carry.Seed?ContactKind.Activate:ContactKind.Touch,trigger:TriggerKind.OwnerAttack,
                        lifeRevision:e.LifeRevision,materialId:carry.MaterialId));
                }
            }
            return new Step(new State(s.Numbers,remaining,e.SwingId,fresh?e.NowUs:s.StartedUs,e.NowUs,hit,swings,events),
                true,hit?"Gathered":"Swing",e.EventId,damage,ink,contacts);
        }
        // 스트리밍은 State를 그대로 넘긴다. 명시적 로드에서 복원된 생존 적만 영으로 표시한다.
        public static State RestoreLoaded(State s,IEnumerable<TargetKey> restoredAlive)
        {
            var remaining=new Dictionary<TargetKey,decimal>(s.Remaining);
            foreach(var key in restoredAlive)remaining[key]=0;
            return new State(s.Numbers,remaining,s.SwingId,s.StartedUs,s.LastUs,s.Hit,new HashSet<long>(s.Swings),new HashSet<long>(s.Events));
        }
    }
}
