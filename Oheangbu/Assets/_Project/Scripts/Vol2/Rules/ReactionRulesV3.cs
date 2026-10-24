using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace Oheangbu.Vol2.Rules
{
    public static class ReactionRulesV3
    {
        public enum Kind { Same, Feed, Transfer, Break, Block }
        public enum Product { None, Child, Damage, Slow }
        public sealed class Material
        {
            public long Id { get; }
            public long CastId { get; }
            public long OwnerId { get; }
            public long FactionId { get; }
            public long ShapeId { get; }
            public long CreatedUs { get; }
            public long ExpiresUs { get; }
            public Body Body { get; }
            public decimal Remaining { get; }
            public int Depth { get; }
            public bool IsMaterial { get; }
            public bool Thickened { get; }
            public bool Consumed { get; }
            public char Letter { get; }
            public ReadOnlyCollection<long> Ancestors { get; }
            public Material(long id, long castId, Body body, decimal remaining, long createdUs, long expiresUs,
                long ownerId = 0, long factionId = 0, long shapeId = 0, int depth = 0, bool isMaterial = true,
                bool thickened = false, bool consumed = false, char letter = '\0', IEnumerable<long> ancestors = null)
            {
                if (id <= 0 || castId <= 0 || remaining < 0 || createdUs < 0 || expiresUs < createdUs || depth < 0 || (uint)body > 4)
                    throw new ArgumentException("Material");
                Id=id; CastId=castId; Body=body; Remaining=remaining; CreatedUs=createdUs; ExpiresUs=expiresUs;
                OwnerId=ownerId; FactionId=factionId; ShapeId=shapeId; Depth=depth; IsMaterial=isMaterial;
                Thickened=thickened; Consumed=consumed; Letter=letter;
                var lineage=new HashSet<long>(ancestors ?? new long[0]); lineage.Add(castId); Ancestors=RuleTypesV3.Freeze(lineage);
            }
            internal Material With(decimal remaining, bool consumed, bool thickened, long expiresUs) =>
                new Material(Id,CastId,Body,remaining,CreatedUs,expiresUs,OwnerId,FactionId,ShapeId,Depth,IsMaterial,thickened,consumed,Letter,Ancestors);
        }
        public sealed class State
        {
            public ReadOnlyDictionary<long,Material> Materials { get; }
            internal HashSet<long> Seen { get; }
            internal HashSet<string> Contacts { get; }
            public long LastUs { get; }
            public State Add(Material material)
            {
                var map=new Dictionary<long,Material>(Materials); map.Add(material.Id,material);
                return new State(map.Values,new HashSet<long>(Seen),new HashSet<string>(Contacts),LastUs);
            }
            public State(IEnumerable<Material> materials) : this(materials,new HashSet<long>(),new HashSet<string>(),0) { }
            internal State(IEnumerable<Material> materials, HashSet<long> seen, HashSet<string> contacts, long lastUs)
            {
                var map=new Dictionary<long,Material>(); foreach(var m in materials)map.Add(m.Id,m);
                Materials=new ReadOnlyDictionary<long,Material>(map); Seen=seen; Contacts=contacts; LastUs=lastUs;
            }
        }
        public sealed class ContactPair
        {
            public long EventId { get; }
            public long FirstId { get; }
            public long SecondId { get; }
            public long NowUs { get; }
            public long ChildId { get; }
            public bool FirstMoving { get; }
            public bool SecondMoving { get; }
            public bool Forecast { get; }
            public bool Returning { get; }
            public decimal DirectAreaBudget { get; }
            public bool EnemyIgniter { get; }
            public ContactPair(long eventId,long firstId,long secondId,long nowUs,long childId=0,
                bool firstMoving=false,bool secondMoving=false,bool forecast=false,bool returning=false,
                decimal directAreaBudget=0,bool enemyIgniter=false)
            { EventId=eventId; FirstId=firstId; SecondId=secondId; NowUs=nowUs; ChildId=childId;
              FirstMoving=firstMoving; SecondMoving=secondMoving; Forecast=forecast; Returning=returning;
              DirectAreaBudget=directAreaBudget; EnemyIgniter=enemyIgniter; }
        }
        public sealed class Command
        {
            public long Id { get; }
            public string Key => "reaction:"+Id;
            public Kind Reaction { get; }
            public Product Product { get; }
            public long ActingId { get; }
            public long ContactId { get; }
            public long OwnerId { get; }
            public long FactionId { get; }
            public long ShapeId { get; }
            public Material Child { get; }
            public ReadOnlyCollection<long> ConsumedIds { get; }
            public ReadOnlyCollection<long> ChildConsumedIds { get; }
            public long DryUs { get; }
            public decimal DamageBudget { get; }
            public long DurationUs { get; }
            public long OcclusionUs { get; }
            public decimal Groggy => 0;
            public bool CanDetonate => false;
            public bool IsMaterial => false;
            public bool Affects(long factionId) => factionId != FactionId;
            internal Command(long id,Kind kind,Product product,Material a,Material b,Material child,
                IEnumerable<long> consumed,IEnumerable<long> dried,long dryUs,decimal damage,long durationUs,long occlusionUs)
            { Id=id; Reaction=kind; Product=product; ActingId=a.Id; ContactId=b.Id; OwnerId=a.OwnerId;
              FactionId=a.FactionId; ShapeId=b.ShapeId; Child=child; ConsumedIds=RuleTypesV3.Freeze(consumed);
              ChildConsumedIds=RuleTypesV3.Freeze(dried); DryUs=dryUs; DamageBudget=damage; DurationUs=durationUs; OcclusionUs=occlusionUs; }
        }
        public sealed class Step
        {
            public State State { get; }
            public bool Accepted { get; }
            public string Reason { get; }
            public ReadOnlyCollection<Command> Commands { get; }
            internal Step(State s,bool accepted,string reason,Command command=null)
            { State=s; Accepted=accepted; Reason=reason; Commands=RuleTypesV3.Freeze(command==null?new Command[0]:new[]{command}); }
        }
        public sealed class BudgetStep
        {
            public State State { get; }
            public decimal Amount { get; }
            public bool Accepted => Amount>0;
            public string CommandKey { get; }
            internal BudgetStep(State state,decimal amount,long eventId)
            { State=state; Amount=amount; CommandKey=amount>0?"reaction:spend:"+eventId:string.Empty; }
        }
        // 이전받은 잔존물의 접촉·틱도 반응과 같은 잔액에서 차감한다.
        public static BudgetStep Spend(State s,long materialId,long eventId,long nowUs,decimal amount)
        {
            if(eventId<=0||amount<=0||nowUs<s.LastUs||s.Seen.Contains(eventId)||!s.Materials.TryGetValue(materialId,out var m)||
                m.Consumed||!m.IsMaterial||m.Letter=='걱'||nowUs<m.CreatedUs||nowUs>=m.ExpiresUs)return new BudgetStep(s,0,0);
            decimal spent=Math.Min(amount,m.Remaining);if(spent<=0)return new BudgetStep(s,0,0);
            var map=new Dictionary<long,Material>(s.Materials);map[m.Id]=m.With(m.Remaining-spent,spent==m.Remaining,m.Thickened,m.ExpiresUs);
            return new BudgetStep(new State(map.Values,new HashSet<long>(s.Seen){eventId},new HashSet<string>(s.Contacts),nowUs),spent,eventId);
        }
        public static bool Beats(Body a,Body b) => a switch
        { Body.Wood=>b==Body.Earth, Body.Fire=>b==Body.Metal, Body.Earth=>b==Body.Water, Body.Metal=>b==Body.Wood, _=>b==Body.Fire };
        public static Kind Relation(Body a,Body b)
        {
            if((uint)a>4||(uint)b>4)throw new ArgumentException("Body");
            if(a==b)return Kind.Same;
            if(((int)a+1)%5==(int)b)return Kind.Feed;
            if(((int)b+1)%5==(int)a)return Kind.Transfer;
            return Beats(a,b)?Kind.Break:Kind.Block;
        }
        public static Step Resolve(State s,ContactPair e,ReactionNumbersV3Dto n)
        {
            if(s==null||e==null||n==null)throw new ArgumentNullException();
            Step Reject(string reason)=>new Step(s,false,reason);
            if(e.Forecast)return Reject("Forecast");
            if(e.EventId<=0||e.NowUs<s.LastUs||e.DirectAreaBudget<0)return Reject("Input");
            if(s.Seen.Contains(e.EventId))return Reject("DuplicateEvent");
            if(e.FirstId==e.SecondId||!s.Materials.TryGetValue(e.FirstId,out var first)||!s.Materials.TryGetValue(e.SecondId,out var second))return Reject("Material");
            bool firstActs=e.FirstMoving!=e.SecondMoving?e.FirstMoving:
                first.CreatedUs!=second.CreatedUs?first.CreatedUs>second.CreatedUs:first.CastId>second.CastId;
            var a=firstActs?first:second; var b=firstActs?second:first;
            if(a.Consumed||b.Consumed||a.Remaining<=0||b.Remaining<=0||e.NowUs<a.CreatedUs||e.NowUs<b.CreatedUs||e.NowUs>=a.ExpiresUs||e.NowUs>=b.ExpiresUs)return Reject("Unavailable");
            if(a.Depth>n.Depth||b.Depth>n.Depth)return Reject("Depth");
            if(a.Letter=='걱'||b.Letter=='걱'||!b.IsMaterial)return Reject("NotMaterial");
            foreach(long cast in a.Ancestors)if(b.Ancestors.Contains(cast))return Reject("SameCast");
            string contact=a.CastId+":"+b.Id;
            if(s.Contacts.Contains(contact))return Reject(e.Returning?"ReturnSameMaterial":"DuplicateContact");
            Kind kind=Relation(a.Body,b.Body);
            if(kind==Kind.Same&&(a.Thickened||b.Thickened))return Reject("SameBodyOnce");
            bool growth=kind==Kind.Feed||kind==Kind.Transfer;
            bool childContact=a.Depth>=n.Depth||b.Depth>=n.Depth;
            if(growth&&!childContact&&(e.ChildId<=0||s.Materials.ContainsKey(e.ChildId)))return Reject("ChildId");
            // 예약과 갱신을 한 Step으로 돌려준다. 호출자는 최신 State에 접촉을 순서대로 적용한다.
            var map=new Dictionary<long,Material>(s.Materials); var consumed=new List<long>(); var dried=new List<long>();
            void Consume(Material m)
            { map[m.Id]=m.With(0,true,m.Thickened,m.ExpiresUs); consumed.Add(m.Id); if(growth&&m.Depth>0)dried.Add(m.Id); }
            Material child=null; Product product=Product.None; decimal damage=0; long duration=0,occlusion=0;
            Consume(a);
            if(kind==Kind.Block)map[b.Id]=b.With(b.Remaining*n.BlockedRemaining,false,b.Thickened,b.ExpiresUs);
            else if(kind==Kind.Same)
                map[b.Id]=b.With((a.Remaining+b.Remaining)*n.SameMultiplier,false,true,checked(e.NowUs+(b.ExpiresUs-b.CreatedUs)));
            else
            {
                Consume(b);
                if(growth&&!childContact)
                {
                    var lineage=new HashSet<long>(a.Ancestors); lineage.UnionWith(b.Ancestors);
                    child=new Material(e.ChildId,a.CastId,kind==Kind.Feed?b.Body:a.Body,(a.Remaining+b.Remaining)*n.ChildMultiplier,
                        e.NowUs,checked(e.NowUs+n.ChildLifeUs),a.OwnerId,a.FactionId,b.ShapeId,n.Depth,ancestors:lineage);
                    map.Add(child.Id,child); product=Product.Child;
                }
                else if(kind==Kind.Break)
                {
                    product=a.Body==Body.Wood||a.Body==Body.Earth?Product.Slow:Product.Damage;
                    if(product==Product.Slow)duration=RuleTypesV3.Microseconds(n.Source.Get(a.Body==Body.Wood?"reaction.woodEarth.slow":"reaction.earthWater.slow"));
                    else
                    {
                        // 작용량과 HP를 섞지 않는다. 산물 전체가 나눠 쓰는 HP 상한이다.
                        damage=e.DirectAreaBudget*n.Source.Get("reaction.damageCap");
                        if(e.EnemyIgniter)damage*=n.Source.Get("reaction.enemy.damageCap");
                        if(a.Body==Body.Fire)duration=RuleTypesV3.Microseconds(n.Source.Get("reaction.fireMetal.damageDuration"));
                        if(a.Body==Body.Water)occlusion=RuleTypesV3.Microseconds(n.Source.Get("reaction.waterFire.occlusion"));
                    }
                }
            }
            var seen=new HashSet<long>(s.Seen){e.EventId}; var contacts=new HashSet<string>(s.Contacts){contact};
            var state=new State(map.Values,seen,contacts,e.NowUs);
            return new Step(state,true,"Resolved",new Command(e.EventId,kind,product,a,b,child,consumed,dried,
                dried.Count>0?n.FadeUs:0,damage,duration,occlusion));
        }
    }
}
