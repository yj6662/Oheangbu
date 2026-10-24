using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace Oheangbu.Vol2.Rules
{
    public static class GroggyRulesV3
    {
        public enum Source { Parry, ActiveSeed, CounterBlade, Reaction, AutomaticSeed, Retribution, Gather }
        public sealed class Target
        {
            public decimal Posture { get; }
            public int WindowNumber { get; }
            public decimal TotalWindowSeconds { get; }
            public long EndsUs { get; }
            internal Target(decimal posture=0,int number=0,decimal total=0,long ends=0)
            { Posture=posture; WindowNumber=number; TotalWindowSeconds=total; EndsUs=ends; }
        }
        public sealed class State
        {
            public DefenceNumbersV3Dto Numbers { get; }
            public ReadOnlyDictionary<string,Target> Targets { get; }
            internal HashSet<string> Seen { get; }
            public long LastUs { get; }
            public State(DefenceNumbersV3Dto n) : this(n,new Dictionary<string,Target>(),new HashSet<string>(),0) { }
            internal State(DefenceNumbersV3Dto n,Dictionary<string,Target> targets,HashSet<string> seen,long last)
            { Numbers=n; Targets=new ReadOnlyDictionary<string,Target>(targets); Seen=seen; LastUs=last; }
            public Target Get(long target,long revision) => Targets.TryGetValue(target+":"+revision,out var t)?t:new Target();
        }
        public sealed class Event
        {
            public long EventId { get; }
            public long TargetId { get; }
            public long LifeRevision { get; }
            public Source Source { get; }
            public char Letter { get; }
            public bool Direct { get; }
            public bool Own { get; }
            public bool Counter { get; }
            public bool Success { get; }
            public Event(long id,long target,Source source,char letter='\0',bool direct=false,bool own=false,
                bool counter=false,bool success=true,long lifeRevision=0)
            { EventId=id; TargetId=target; Source=source; Letter=letter; Direct=direct; Own=own;
              Counter=counter; Success=success; LifeRevision=lifeRevision; }
        }
        public sealed class Step
        {
            public State State { get; }
            public bool Accepted { get; }
            public string Reason { get; }
            public long CommandId { get; }
            public long TargetId { get; }
            public long LifeRevision { get; }
            public string CommandKey => CommandId==0?string.Empty:"groggy:"+TargetId+":"+LifeRevision+":"+CommandId;
            public decimal AddedPosture { get; }
            public long WindowUs { get; }
            public bool Opened => WindowUs>0;
            internal Step(State s,bool accepted,string reason,long id=0,decimal added=0,long window=0,long target=0,long revision=0)
            { State=s; Accepted=accepted; Reason=reason; CommandId=id; AddedPosture=added; WindowUs=window; TargetId=target; LifeRevision=revision; }
        }
        public static Step Apply(State s,Event e,long nowUs)
        {
            if(e.EventId<=0||e.TargetId<=0||e.LifeRevision<0||nowUs<s.LastUs)return new Step(s,false,"Input");
            string key=e.TargetId+":"+e.LifeRevision, eventKey=key+":"+e.EventId;
            if(s.Seen.Contains(eventKey))return new Step(s,false,"DuplicateEvent");
            var seen=new HashSet<string>(s.Seen){eventKey}; var targets=new Dictionary<string,Target>(s.Targets);
            var t=s.Get(e.TargetId,e.LifeRevision); var n=s.Numbers;
            int number=t.WindowNumber; decimal total=t.TotalWindowSeconds;
            if(number>0&&nowUs>=t.EndsUs&&nowUs-t.EndsUs>=n.ResetUs){number=0;total=0;}
            decimal amount=0;
            if(e.Success&&nowUs>=t.EndsUs)
            {
                if(e.Source==Source.Parry&&e.Counter)amount=n.ParryPosture;
                if(GlyphRulesV3.TryDecode(e.Letter,out var glyph))
                {
                    if(e.Source==Source.ActiveSeed&&glyph.Frame==Frame.Path&&glyph.Verb==Verb.Seed&&e.Direct&&e.Own)amount=n.ActiveSeedPosture;
                    if(e.Source==Source.CounterBlade&&glyph.Frame==Frame.Attachment&&glyph.Verb==Verb.Split&&e.Direct&&e.Own&&e.Counter)amount=n.BladePosture;
                }
            }
            decimal posture=t.Posture+amount; long window=0,ends=t.EndsUs;
            if(posture>=n.PostureThreshold)
            {
                decimal seconds=n.FirstWindowSeconds; for(int i=0;i<number;i++)seconds*=n.WindowDecay;
                seconds=Math.Max(0,Math.Min(seconds,n.WindowCapSeconds-total));
                // 외부 시계 정밀도보다 짧은 꼬리는 0µs로 내린다. 합 상한을 넘겨 올림하지 않는다.
                window=RuleTypesV3.Microseconds(seconds); total+=seconds; number++; posture=0; ends=checked(nowUs+window);
            }
            targets[key]=new Target(posture,number,total,ends);
            return new Step(new State(n,targets,seen,nowUs),true,window>0?"Opened":amount>0?"Accumulated":"NoPosture",e.EventId,amount,window,e.TargetId,e.LifeRevision);
        }
    }
}
