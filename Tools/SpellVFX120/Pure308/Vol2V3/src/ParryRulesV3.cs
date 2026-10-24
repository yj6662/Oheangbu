using System;
using System.Collections.Generic;
namespace Oheangbu.Vol2.Rules
{
    public static class ParryRulesV3
    {
        public sealed class State
        {
            public DefenceNumbersV3Dto Numbers { get; }
            public long AttemptId { get; }
            public long StartedUs { get; }
            public long LastUs { get; }
            public Body Body { get; }
            public bool Pending { get; }
            internal HashSet<long> Attempts { get; }
            internal HashSet<long> Impacts { get; }
            public State(DefenceNumbersV3Dto n) : this(n,0,0,0,Body.Wood,false,new HashSet<long>(),new HashSet<long>()) { }
            internal State(DefenceNumbersV3Dto n,long id,long start,long last,Body body,bool pending,HashSet<long> attempts,HashSet<long> impacts)
            { Numbers=n??throw new ArgumentNullException(nameof(n)); AttemptId=id; StartedUs=start; LastUs=last; Body=body; Pending=pending; Attempts=attempts; Impacts=impacts; }
        }
        public sealed class Input
        {
            public long AttemptId { get; }
            public long NowUs { get; }
            public decimal Ink { get; }
            public Body Body { get; }
            public Input(long attemptId,long nowUs,Body body,decimal ink)
            { AttemptId=attemptId; NowUs=nowUs; Body=body; Ink=ink; }
        }
        public sealed class Impact
        {
            public long EventId { get; }
            public long AttemptId { get; }
            public long TargetId { get; }
            public long LifeRevision { get; }
            public Body? AttackBody { get; }
            public bool Projectile { get; }
            public bool Timeout { get; }
            public long TelegraphUs { get; }
            public decimal IncomingDamage { get; }
            public decimal Ink { get; }
            public Impact(long eventId,long attemptId,decimal ink,decimal damage=0,Body? attackBody=null,
                bool projectile=false,bool timeout=false,long targetId=0,long lifeRevision=0,long telegraphUs=0)
            { EventId=eventId; AttemptId=attemptId; Ink=ink; IncomingDamage=damage; AttackBody=attackBody;
              Projectile=projectile; Timeout=timeout; TargetId=targetId; LifeRevision=lifeRevision; TelegraphUs=telegraphUs; }
        }
        public sealed class Step
        {
            public State State { get; }
            public bool Accepted { get; }
            public string Reason { get; }
            public long CommandId { get; }
            public string CommandKey => CommandId==0?string.Empty:(Reason=="Begin"?"parry:settle:":"parry:impact:")+CommandId;
            public decimal InkDelta { get; }
            public decimal Damage { get; }
            public bool Defended { get; }
            public decimal Posture { get; }
            public long PostureTargetId { get; }
            public long LifeRevision { get; }
            public bool Settled { get; }
            internal Step(State s,bool accepted,string reason,long commandId=0,decimal ink=0,decimal damage=0,
                bool defended=false,decimal posture=0,long target=0,long revision=0,bool settled=false)
            { State=s; Accepted=accepted; Reason=reason; CommandId=commandId; InkDelta=ink; Damage=damage;
              Defended=defended; Posture=posture; PostureTargetId=target; LifeRevision=revision; Settled=settled; }
        }
        public static Step Begin(State s,Input e)
        {
            if(e.AttemptId<=0||e.NowUs<s.LastUs||e.Ink<0||(uint)e.Body>4)return new Step(s,false,"Input");
            if(s.Attempts.Contains(e.AttemptId))return new Step(s,false,"DuplicateAttempt");
            if(s.AttemptId!=0&&e.NowUs-s.StartedUs<s.Numbers.ParryCooldownUs)return new Step(s,false,"Cooldown");
            // 만료된 시도도 새 시도 전에 한 번 정산한다. 먹 0에서 시작하는 길은 막지 않는다.
            decimal cost=s.Pending?Math.Min(e.Ink,s.Numbers.FailureCost):0;
            var attempts=new HashSet<long>(s.Attempts){e.AttemptId};
            return new Step(new State(s.Numbers,e.AttemptId,e.NowUs,e.NowUs,e.Body,true,attempts,new HashSet<long>(s.Impacts)),
                true,"Begin",s.Pending?s.AttemptId:0,-cost,settled:s.Pending);
        }
        public static Step Resolve(State s,Impact e,long nowUs)
        {
            if(e.EventId<=0||nowUs<s.LastUs||e.Ink<0||e.IncomingDamage<0||e.TelegraphUs<0||
                (e.AttackBody.HasValue&&(uint)e.AttackBody.Value>4))return new Step(s,false,"Input");
            if(s.Impacts.Contains(e.EventId))return new Step(s,false,"DuplicateImpact");
            if(e.AttemptId!=s.AttemptId)return new Step(s,false,"StaleAttempt");
            bool inside=s.Pending&&nowUs>=s.StartedUs&&nowUs-s.StartedUs<s.Numbers.ParryWindowUs;
            if(e.Timeout&&inside)return new Step(s,false,"EarlyTimeout");
            var impacts=new HashSet<long>(s.Impacts){e.EventId};
            bool defended=inside&&!e.Projectile&&!e.Timeout;
            // 탄은 패링 기회를 소비하지 않는다. 타임아웃/늦은 임팩트는 실패 후불 한 번이다.
            bool settle=s.Pending&&(defended||!inside);
            decimal cost=settle&&!defended?Math.Min(e.Ink,s.Numbers.FailureCost):0;
            decimal posture=defended&&e.AttackBody.HasValue&&e.TelegraphUs>=s.Numbers.MinimumTelegraphUs&&
                ReactionRulesV3.Beats(s.Body,e.AttackBody.Value)?s.Numbers.ParryPosture:0;
            var state=new State(s.Numbers,s.AttemptId,s.StartedUs,nowUs,s.Body,s.Pending&&!settle,new HashSet<long>(s.Attempts),impacts);
            return new Step(state,true,defended?"Defended":settle?"Failed":"Unblocked",e.EventId,-cost,
                defended||e.Timeout?0:e.IncomingDamage,defended,posture,e.TargetId,e.LifeRevision,settle);
        }
    }
}
