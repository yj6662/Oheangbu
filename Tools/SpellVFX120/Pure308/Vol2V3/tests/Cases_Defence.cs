using System;
using System.Linq;
using System.Text;
using Oheangbu.Vol2.Rules;
using P=Oheangbu.Vol2.Rules.ParryRulesV3;
using G=Oheangbu.Vol2.Rules.GroggyRulesV3;
using N=Oheangbu.Vol2.Rules.NoInkSwingV3;
using H=Oheangbu.Vol2.Rules.GeokRecoveryV3;
namespace Vol2V3.Tests
{
    public static class Cases_Defence
    {
        public static void Run(Report r,Context c)
        {
            r.Check("import-ready",c.Import.CanCast);if(!c.Import.CanCast)return;
            var n=new DefenceNumbersV3Dto(c.Numbers);
            r.Run("parry",()=>Parry(r,n));r.Run("groggy",()=>Groggy(r,n));
            r.Run("gather",()=>Gather(r,c,n));r.Run("recovery",()=>Recovery(r,n));
            r.Run("negative-13",()=>Negative(r,c));
            r.Equal("damage-D147-maximum",2.8125m,n.Damage(1,2,1.15m,true,true,true));
            r.Equal("damage-outside-no-K",1.5m,n.Damage(1,2,1.15m,true,true,false));
            r.Equal("damage-normal-window",2.15625m,n.Damage(1,1,1.15m,true,true,true));
            r.Equal("damage-nonspell-B",67.5m,n.Damage(45,2,1.15m,true,false,true));
            r.Equal("damage-equipment-cap",1.32m,n.Damage(1,1,1,false,true,false,0.2m,50));
        }
        static void Parry(Report r,DefenceNumbersV3Dto n)
        {
            foreach(decimal ink in new[]{0m,13m,14m,20m})
            {
                var initial=new P.State(n);var begin=P.Begin(initial,new P.Input(1,0,Body.Wood,ink));
                r.Check("begin/"+ink,begin.Accepted&&begin.InkDelta==0&&begin.State.Pending&&!initial.Pending);
                foreach(Body? attack in new Body?[]{Body.Earth,Body.Fire,null})
                {
                    var hit=P.Resolve(begin.State,new P.Impact(10,1,ink,99,attack,targetId:7,lifeRevision:3,telegraphUs:800000),0);
                    r.Check("defend/"+ink+attack,hit.Accepted&&hit.Defended&&hit.Damage==0&&hit.InkDelta==0&&hit.Settled);
                    r.Equal("posture/"+ink+attack,attack==Body.Earth?40m:0m,hit.Posture);
                    r.Check("target/"+ink+attack,hit.PostureTargetId==7&&hit.LifeRevision==3);
                    r.Check("repeat-impact/"+ink+attack,!P.Resolve(hit.State,new P.Impact(10,1,ink,99,attack),0).Accepted);
                    var timeout=P.Resolve(hit.State,new P.Impact(11,1,ink,timeout:true),500000);
                    r.Check("success-timeout/"+ink+attack,timeout.InkDelta==0&&!timeout.Settled);
                }
                var late=P.Resolve(begin.State,new P.Impact(10,1,ink,20,Body.Earth),500000);
                r.Check("late/"+ink,!late.Defended&&late.Damage==20&&late.InkDelta==-Math.Min(ink,14)&&late.Settled);
                var timeoutFirst=P.Resolve(begin.State,new P.Impact(9,1,ink,timeout:true),500000);
                var lateAfter=P.Resolve(timeoutFirst.State,new P.Impact(10,1,Math.Max(0,ink-14),20),500000);
                r.Check("timeout-impact-single-payment/"+ink,lateAfter.Damage==20&&lateAfter.InkDelta==0&&!lateAfter.Settled);
                r.Check("late-timeout-single-payment/"+ink,P.Resolve(late.State,new P.Impact(11,1,0,timeout:true),500000).InkDelta==0);
            }
            var pending=P.Begin(new P.State(n),new P.Input(1,0,Body.Wood,14)).State;
            r.Check("499999-inside",P.Resolve(pending,new P.Impact(1,1,14,10,Body.Earth,telegraphUs:800000),499999).Defended);
            r.Equal("short-telegraph-no-groggy",0m,P.Resolve(pending,new P.Impact(1,1,14,10,Body.Earth,telegraphUs:799999),1).Posture);
            r.Equal("early-timeout","EarlyTimeout",P.Resolve(pending,new P.Impact(1,1,14,timeout:true),499999).Reason);
            var projectile=P.Resolve(pending,new P.Impact(1,1,14,10,Body.Earth,projectile:true),1);
            r.Check("projectile-excluded",projectile.Damage==10&&!projectile.Defended&&projectile.Posture==0&&projectile.State.Pending&&projectile.InkDelta==0);
            r.Check("projectile-then-melee",P.Resolve(projectile.State,new P.Impact(2,1,14,10,Body.Earth,telegraphUs:800000),2).Defended);
            r.Check("cooldown-799999",!P.Begin(pending,new P.Input(2,799999,Body.Fire,20)).Accepted);
            var retry=P.Begin(pending,new P.Input(2,800000,Body.Fire,20));
            r.Check("cooldown-800000-settles-old",retry.Accepted&&retry.InkDelta==-14&&retry.State.Pending&&retry.State.AttemptId==2);
            r.Equal("stale-timeout","StaleAttempt",P.Resolve(retry.State,new P.Impact(3,1,6,timeout:true),800000).Reason);
            r.Check("duplicate-attempt",!P.Begin(retry.State,new P.Input(1,1600000,Body.Fire,6)).Accepted);
            r.Check("reverse-time",!P.Resolve(retry.State,new P.Impact(3,2,6),799999).Accepted);
        }
        static void Groggy(Report r,DefenceNumbersV3Dto n)
        {
            G.Event Parry(long id,long rev=0)=>new G.Event(id,1,G.Source.Parry,counter:true,lifeRevision:rev);
            G.Event Seed(long id)=>new G.Event(id,1,G.Source.ActiveSeed,'감',true,true);
            G.Event Blade(long id)=>new G.Event(id,1,G.Source.CounterBlade,'것',true,true,true);
            var s=new G.State(n);var a=G.Apply(s,Parry(1),0);
            r.Equal("posture-parry",40m,a.State.Get(1,0).Posture);r.Equal("initial-immutable",0m,s.Get(1,0).Posture);
            var b=G.Apply(a.State,Seed(2),1);
            r.Check("parry-seed-opens",b.Opened&&b.WindowUs==2000000&&b.State.Get(1,0).Posture==0&&b.State.Get(1,0).WindowNumber==1);
            var inside=G.Apply(b.State,Blade(3),2000000);
            r.Equal("inside-no-posture",0m,inside.AddedPosture);
            var end=G.Apply(inside.State,Blade(4),2000001);
            r.Equal("end-exclusive",10m,end.State.Get(1,0).Posture);
            var wait=G.Apply(end.State,new G.Event(5,1,G.Source.Reaction),8000001);
            r.Check("six-second-reset-keeps-posture",wait.State.Get(1,0).WindowNumber==0&&wait.State.Get(1,0).Posture==10);
            var before=G.Apply(end.State,new G.Event(5,1,G.Source.Reaction),8000000);
            r.Equal("reset-minus-one",1,before.State.Get(1,0).WindowNumber);
            r.Check("same-event-rejected",!G.Apply(a.State,Parry(1),1).Accepted);
            r.Equal("revision-independent",40m,G.Apply(a.State,Parry(1,1),1).State.Get(1,1).Posture);
            r.Equal("target-independent",40m,G.Apply(a.State,new G.Event(1,2,G.Source.Parry,counter:true),1).State.Get(2,0).Posture);
            foreach(var source in new[]{G.Source.Reaction,G.Source.AutomaticSeed,G.Source.Retribution,G.Source.Gather})
                r.Equal("excluded/"+source,0m,G.Apply(new G.State(n),new G.Event(1,1,source,'감',true,true,true),0).AddedPosture);
            foreach(var e in new[]{new G.Event(1,1,G.Source.Parry),new G.Event(1,1,G.Source.ActiveSeed,'검',true,true),
                new G.Event(1,1,G.Source.ActiveSeed,'감',false,true),new G.Event(1,1,G.Source.ActiveSeed,'감',true,false),
                new G.Event(1,1,G.Source.CounterBlade,'갓',true,true,true),new G.Event(1,1,G.Source.CounterBlade,'것',true,true,false),
                new G.Event(1,1,G.Source.Parry,counter:true,success:false)})
                r.Equal("unqualified/"+e.Source+e.Letter+e.Direct+e.Own+e.Counter+e.Success,0m,G.Apply(new G.State(n),e,0).AddedPosture);
            var blades=new G.State(n);G.Step bladeStep=null;
            for(int k=1;k<=10;k++){bladeStep=G.Apply(blades,Blade(k),k);blades=bladeStep.State;}
            r.Check("ten-blades-open",bladeStep.Opened&&bladeStep.WindowUs==2000000&&blades.Get(1,0).Posture==0);
            r.Check("command-target-key",a.CommandKey!=G.Apply(s,new G.Event(1,2,G.Source.Parry,counter:true),0).CommandKey);
            s=new G.State(n);long now=0,id=0;decimal sum=0;
            for(int w=1;w<=40;w++)
            {
                var p=G.Apply(s,Parry(++id),now); var seed=G.Apply(p.State,Seed(++id),now);
                decimal expected=2m;for(int k=1;k<w;k++)expected*=0.6m;
                r.Equal("window-series/"+w,(long)(expected*1000000m),seed.WindowUs);
                r.Equal("common-window-n/"+w,w,seed.State.Get(1,0).WindowNumber);
                sum+=expected;r.Equal("series-budget/"+w,sum,seed.State.Get(1,0).TotalWindowSeconds);
                r.Check("series-cap/"+w,seed.State.Get(1,0).TotalWindowSeconds<=5);
                now+=seed.WindowUs;s=seed.State;
            }
            s=new G.State(n);for(int i=0;i<3;i++)s=G.Apply(s,Parry(i+1),i).State;
            r.Equal("overflow-discard",0m,s.Get(1,0).Posture);
            r.Check("time-order",!G.Apply(s,Blade(5),1).Accepted);
            var timeless=G.Apply(new G.State(n),Blade(1),0);
            r.Equal("no-time-decay",10m,G.Apply(timeless.State,new G.Event(2,1,G.Source.Reaction),100000000).State.Get(1,0).Posture);
        }
        static void Gather(Report r,Context c,DefenceNumbersV3Dto n)
        {
            N.Input Hit(long id,long time,long target=1,decimal hp=10,decimal ink=0,decimal cap=100,bool boss=false,
                long encounter=1,long swing=0,bool window=false,long revision=0,N.Carry[] carries=null)=>
                new N.Input(id,swing==0?id:swing,time,encounter,target,hp,ink,cap,boss:boss,ownerId:7,lifeRevision:revision,criticalWindow:window,carries:carries);
            var s=new N.State(n);var hit=N.Resolve(s,Hit(1,0));
            r.Check("gather-first",hit.Damage==0.3m&&hit.InkDelta==4&&hit.Groggy==0&&!hit.CreatesMaterial&&s.Remaining.Count==0);
            r.Check("first-enemy-only",!N.Resolve(hit.State,Hit(2,1,2,swing:1)).Accepted);
            r.Check("duplicate-event",!N.Resolve(hit.State,Hit(1,1100000)).Accepted);
            r.Check("cooldown-1099999",!N.Resolve(hit.State,Hit(2,1099999)).Accepted);
            r.Check("cooldown-1100000",N.Resolve(hit.State,Hit(2,1100000)).Accepted);
            r.Equal("nonlethal",0.1m,N.Resolve(s,Hit(1,0,hp:1.1m)).Damage);
            var floor=N.Resolve(s,Hit(1,0,hp:1));r.Check("floor-still-gathers",floor.Damage==0&&floor.InkDelta==4);
            r.Equal("below-floor-no-heal",0m,N.Resolve(s,Hit(1,0,hp:0.5m)).Damage);
            r.Equal("dead-no-gather",0m,N.Resolve(s,Hit(1,0,hp:0)).InkDelta);
            r.Equal("window-all-damage",0.45m,N.Resolve(s,Hit(1,0,window:true)).Damage);
            r.Equal("window-floor-after-B",0.1m,N.Resolve(s,Hit(1,0,hp:1.1m,window:true)).Damage);
            var full=N.Resolve(s,Hit(1,0,ink:100));r.Equal("full-ink-budget-kept",12m,full.State.Remaining[new N.TargetKey(1,1)]);
            var almost=N.Resolve(s,Hit(1,0,ink:99));r.Check("actual-ink-only",almost.InkDelta==1&&almost.State.Remaining[new N.TargetKey(1,1)]==11);
            foreach(bool boss in new[]{false,true})
            {
                s=new N.State(n);decimal total=0;
                for(int i=0;i<8;i++){var x=N.Resolve(s,Hit(i+1,i*1100000L,boss:boss));total+=x.InkDelta;s=x.State;}
                r.Equal("target-budget/"+boss,boss?24m:12m,total);
                r.Equal("revision-does-not-refill/"+boss,0m,N.Resolve(s,Hit(9,8800000,boss:boss,revision:2)).InkDelta);
                r.Equal("new-encounter/"+boss,4m,N.Resolve(s,Hit(9,8800000,boss:boss,encounter:2)).InkDelta);
            }
            s=hit.State;var stream=N.Resolve(s,Hit(2,1100000));r.Equal("stream-memory",4m,stream.State.Remaining[new N.TargetKey(1,1)]);
            var loaded=N.RestoreLoaded(hit.State,new[]{new N.TargetKey(1,1),new N.TargetKey(1,2)});
            r.Equal("load-known-zero",0m,N.Resolve(loaded,Hit(2,1100000)).InkDelta);
            r.Equal("load-unseen-restored-zero",0m,N.Resolve(loaded,Hit(2,1100000,2)).InkDelta);
            r.Equal("load-new-encounter",4m,N.Resolve(loaded,Hit(2,1100000,3,encounter:2)).InkDelta);
            var seed=c.Plan('감');var seedState=EffectBudgetV3.Apply(new EffectBudgetV3.State(101,seed),new Contact(100,101,1,0,10,ContactKind.Plant),seed).State;
            var carry=new[]{new N.Carry(101,1001,7,'감',true),new N.Carry(102,1002,7,'넉'),new N.Carry(103,1003,7,'걱'),new N.Carry(104,1004,9,'낙')};
            var carried=N.Resolve(new N.State(n),Hit(1,1,carries:carry));r.Equal("carry-original-casts",2,carried.Contacts.Count);
            r.Check("carry-owner-attack",carried.Contacts.All(x=>x.Trigger==TriggerKind.OwnerAttack&&x.SwingId==1));
            var seedHit=EffectBudgetV3.Apply(seedState,carried.Contacts.Single(x=>x.CastId==101),seed);
            r.Check("seed-delivery",seedHit.Accepted&&seedHit.Commands.Sum(x=>x.ActionAmount)==1.2m&&carried.Damage==0.3m);
            var attach=c.Plan('넉');var attached=EffectBudgetV3.Apply(new EffectBudgetV3.State(102,attach),carried.Contacts.Single(x=>x.CastId==102),attach);
            r.Check("attachment-delivery",attached.Accepted&&attached.State.TotalPaid==0.7m);
            var next=N.Resolve(carried.State,Hit(2,1100001,carries:carry));
            var attached2=EffectBudgetV3.Apply(attached.State,next.Contacts.Single(x=>x.CastId==102),attach);
            r.Equal("attachment-second",1m,attached2.State.TotalPaid);
            r.Check("seed-only-once",!EffectBudgetV3.Apply(seedHit.State,next.Contacts.Single(x=>x.CastId==101),seed).Accepted);
            var empty=N.Resolve(new N.State(n),new N.Input(1,1,0));r.Check("air-swing-starts-cooldown",empty.Accepted&&!N.Resolve(empty.State,Hit(2,100)).Accepted);
            var friendly=N.Resolve(new N.State(n),new N.Input(1,1,0,1,2,10,0,100,enemy:false));
            r.Check("friendly-does-not-take-first",N.Resolve(friendly.State,Hit(2,1,swing:1)).InkDelta==4);
        }
        static void Recovery(Report r,DefenceNumbersV3Dto n)
        {
            H.Input Begin(long id,long now=0,long cast=1,bool engaged=false,long encounter=1,long phase=0,long window=1,bool open=true,decimal ink=26)=>
                new H.Input(id,H.Action.Begin,now,cast,50,100,ink,engaged:engaged,encounterId:encounter,bossPhaseId:phase,windowId:window,windowOpen:open);
            var s=new H.State(n);var start=H.Apply(s,Begin(1));
            r.Check("geok-begin",start.Accepted&&start.InkDelta==-26&&start.HpDelta==0&&start.State.OutsideRemaining==0&&s.OutsideRemaining==30);
            r.Check("geok-not-attack",!start.IsReactionMaterial&&!start.PausesInkRecovery&&start.AttackBudget==0);
            var tick=H.Apply(start.State,new H.Input(2,H.Action.Tick,1000000,1,50,100));r.Equal("heal-first-second",10m,tick.HpDelta);
            var tick2=H.Apply(tick.State,new H.Input(3,H.Action.Tick,3000000,1,60,100));r.Check("heal-total-30",tick2.HpDelta==20&&!tick2.State.Active);
            r.Check("heal-after-end",!H.Apply(tick2.State,new H.Input(4,H.Action.Tick,4000000,1,80,100)).Accepted);
            var capped=H.Apply(start.State,new H.Input(2,H.Action.Tick,3000000,1,95,100));r.Equal("heal-hp-cap",5m,capped.HpDelta);
            var stop=H.Apply(tick.State,new H.Input(3,H.Action.Damage,1000001,1,50,100,actualDamage:1));
            r.Check("hurt-cancels-no-refund",!stop.State.Active&&stop.HpDelta==0&&stop.InkDelta==0&&stop.State.OutsideRemaining==0);
            var defended=H.Apply(tick.State,new H.Input(3,H.Action.Damage,1000001,1,60,100,actualDamage:0));r.Check("defended-does-not-cancel",defended.State.Active);
            var snapshot=H.Save(tick.State);
            var json=System.Text.Json.JsonSerializer.Serialize(snapshot);
            var restored=H.Restore(n,System.Text.Json.JsonSerializer.Deserialize<H.Snapshot>(json));
            r.Check("save-load-keeps-budget",restored.OutsideRemaining==0&&restored.ElapsedBudget==10);
            r.Equal("save-load-no-repay",20m,H.Apply(restored,new H.Input(3,H.Action.Tick,3000000,1,60,100)).HpDelta);
            r.Check("save-load-replay-rejected",!H.Apply(restored,new H.Input(2,H.Action.Tick,1000000,1,60,100)).Accepted);
            var cancel=H.Apply(start.State,new H.Input(2,H.Action.Cancel,1,1));
            r.Check("cancel-no-budget-refund",cancel.State.OutsideRemaining==0&&!cancel.State.Active);
            r.Check("outside-budget-persisted",!H.Apply(cancel.State,Begin(3,20000000,2)).Accepted);
            var rest=H.Apply(cancel.State,new H.Input(3,H.Action.Rest,20000000));
            r.Check("rest-resets-outside",rest.State.OutsideRemaining==30&&H.Apply(rest.State,Begin(4,20000000,2)).Accepted);
            r.Check("ink-25-rejected",!H.Apply(s,Begin(1,ink:25)).Accepted);
            r.Check("combat-needs-window",!H.Apply(s,Begin(1,engaged:true,open:false)).Accepted);
            var combat=H.Apply(s,Begin(1,engaged:true));r.Check("combat-reserves-window",combat.Accepted&&combat.ConsumesWindow&&combat.State.OutsideRemaining==30);
            var done=H.Apply(combat.State,new H.Input(2,H.Action.Tick,3000000,1,50,100));
            r.Check("encounter-once",!H.Apply(done.State,Begin(3,20000000,2,true,window:2)).Accepted);
            r.Check("phase-new-once",H.Apply(done.State,Begin(3,20000000,2,true,phase:1,window:2)).Accepted);
            r.Check("cooldown-19999999",!H.Apply(done.State,Begin(3,19999999,2,true,encounter:2,window:2)).Accepted);
            r.Check("cooldown-20000000",H.Apply(done.State,Begin(3,20000000,2,true,encounter:2,window:2)).Accepted);
            r.Check("window-not-reused",!H.Apply(done.State,Begin(3,20000000,2,true,encounter:2,window:1)).Accepted);
            r.Check("event-replay",!H.Apply(start.State,Begin(1)).Accepted);
            r.Check("stale-cast",!H.Apply(start.State,new H.Input(2,H.Action.Cancel,1,99)).Accepted);
            r.Check("encounter-end-does-not-cancel",H.Apply(combat.State,new H.Input(2,H.Action.Tick,3000000,1,50,100,engaged:false)).HpDelta==30);
        }
        static void Negative(Report r,Context c)
        {
            string[] codes={"PeakDeclaration","ParryPayment","NoInkFormula","BorrowedScope","PlayGate","Efficiency","GroggySeries","SharedContract","NoInkScope","SeotDocument","EffectTargetCap","EffectTargetCap","EffectTargetCap"};
            var rows=CsvV3.Parse(c.Sources["table"]).Select(x=>x.Fields.ToArray()).ToList();int i=0;
            foreach(var test in c.Golden.RootElement.GetProperty("negative").EnumerateArray())
            {
                var copy=rows.Select(x=>x.ToArray()).ToList();string doc=Context.Text(c.Sources["markdown"]);
                foreach(var cell in test.GetProperty("cells").EnumerateArray())copy[cell.GetProperty("row").GetInt32()+1][cell.GetProperty("column").GetInt32()]=cell.GetProperty("value").GetString();
                foreach(var patch in test.GetProperty("document").EnumerateArray())doc=doc.Replace(patch.GetProperty("old").GetString(),patch.GetProperty("new").GetString());
                var input=c.Copy();input["table"]=Context.Csv(copy);input["markdown"]=Encoding.UTF8.GetBytes(doc);
                var result=new Vol2ContractValidator().Import(input);
                r.Check("C6/"+(i+1),!result.CanCast&&result.Errors.Any(x=>x.Code==codes[i]),string.Join(" | ",result.Errors));i++;
            }
            r.Equal("C6-count",13,i);
        }
    }
}
