using System;
using System.Linq;
using Oheangbu.Vol2.Rules;
using R=Oheangbu.Vol2.Rules.ReactionRulesV3;
namespace Vol2V3.Tests
{
    public static class Cases_Reaction
    {
        static R.Material M(long id,Body body,long cast=0,decimal amount=1,long created=0,long expires=20000000,
            int depth=0,long owner=7,long faction=1,bool material=true,char letter='\0') =>
            new R.Material(id,cast==0?id:cast,body,amount,created,expires,owner,faction,id*10,depth,material,letter:letter);
        public static void Run(Report r,Context c)
        {
            r.Check("import-ready",c.Import.CanCast);if(!c.Import.CanCast)return;
            var n=new ReactionNumbersV3Dto(c.Numbers);
            var golden=c.Golden.RootElement.GetProperty("reactionCells").EnumerateArray().ToArray();
            string[] names={"비화","먹임","옮겨붙음","꺾음","막힘"};
            for(int a=0;a<5;a++)for(int b=0;b<5;b++)
            {
                string id="cell/"+a+"/"+b; var old=new R.State(new[]{M(1,(Body)a),M(2,(Body)b)});
                var e=new R.ContactPair(1,1,2,100,3,firstMoving:true,directAreaBudget:1);
                var step=R.Resolve(old,e,n);r.Check(id+"/accepted",step.Accepted,step.Reason);if(!step.Accepted)continue;
                var cmd=step.Commands.Single();string text=golden[a*5+b].GetProperty("text").GetString();
                r.Check(id+"/golden",text.StartsWith(names[(int)cmd.Reaction]));
                r.Check(id+"/immutable",old.Materials[1].Remaining==1&&!old.Materials[1].Consumed&&old.Materials.Count==2);
                r.Check(id+"/ownership",cmd.OwnerId==7&&cmd.FactionId==1&&cmd.ShapeId==20&&!cmd.Affects(1)&&cmd.Affects(2));
                r.Check(id+"/no-side-effects",cmd.Groggy==0&&!cmd.CanDetonate&&!cmd.IsMaterial);
                r.Check(id+"/replay",!R.Resolve(step.State,e,n).Accepted);
                if(cmd.Reaction==R.Kind.Feed||cmd.Reaction==R.Kind.Transfer)
                {
                    r.Check(id+"/parents",step.State.Materials[1].Consumed&&step.State.Materials[2].Consumed);
                    r.Check(id+"/child",cmd.Child.Remaining==1&&cmd.Child.Depth==1&&cmd.Child.ExpiresUs==10000100&&cmd.Child.ShapeId==20);
                    r.Equal(id+"/recipient",cmd.Reaction==R.Kind.Feed?(Body)b:(Body)a,cmd.Child.Body);
                }
                else if(cmd.Reaction==R.Kind.Same)
                    r.Check(id+"/same",step.State.Materials[2].Remaining==2.4m&&step.State.Materials[2].Thickened&&cmd.Product==R.Product.None);
                else if(cmd.Reaction==R.Kind.Block)
                    r.Check(id+"/block",step.State.Materials[1].Consumed&&!step.State.Materials[2].Consumed&&step.State.Materials[2].Remaining==0.5m);
                else
                {
                    bool slow=a==0||a==2;r.Equal(id+"/product",slow?R.Product.Slow:R.Product.Damage,cmd.Product);
                    r.Equal(id+"/damage",slow?0m:0.8m,cmd.DamageBudget);
                    r.Equal(id+"/duration",a==0||a==1?2000000L:a==2?3000000L:0L,cmd.DurationUs);
                }
            }
            var shared=new R.State(new[]{M(1,Body.Wood),M(2,Body.Fire)});
            var tick=R.Spend(shared,2,10,0,0.4m);
            r.Check("tick-shares-reaction-balance",tick.Accepted&&tick.Amount==0.4m&&tick.State.Materials[2].Remaining==0.6m&&shared.Materials[2].Remaining==1);
            r.Check("tick-idempotent",!R.Spend(tick.State,2,10,0,0.4m).Accepted);
            var fed=R.Resolve(tick.State,new R.ContactPair(11,1,2,1,3,true),n);
            r.Equal("child-from-remaining-only",0.8m,fed.Commands[0].Child.Remaining);
            r.Check("consumed-cannot-tick",!R.Spend(fed.State,2,12,1,1).Accepted);
            r.Check("tick-exhaustion",R.Spend(tick.State,2,12,1,10).State.Materials[2].Consumed);
            var state=new R.State(new[]{M(1,Body.Fire,owner:9,faction:2),M(2,Body.Wood,owner:7),M(3,Body.Water)});
            var forecast=R.Resolve(state,new R.ContactPair(1,1,2,100,4,true,forecast:true),n);
            r.Check("forecast-no-reservation",ReferenceEquals(state,forecast.State)&&forecast.Commands.Count==0);
            var first=R.Resolve(state,new R.ContactPair(1,1,2,100,4,true),n);
            r.Check("forecast-real-same-event",first.Accepted);
            r.Check("steal-owner",first.Commands[0].Child.OwnerId==9&&first.Commands[0].Child.FactionId==2);
            var race=R.Resolve(first.State,new R.ContactPair(2,3,2,100,5,true),n);
            r.Check("race-loser-no-consume",!race.Accepted&&!race.State.Materials[3].Consumed&&race.Commands.Count==0);
            var same=new R.State(new[]{M(1,Body.Wood,cast:4),M(2,Body.Fire,cast:4)});
            r.Equal("same-cast","SameCast",R.Resolve(same,new R.ContactPair(1,1,2,0,3,true),n).Reason);
            foreach(bool aChild in new[]{false,true})foreach(bool reverse in new[]{false,true})
            {
                var s=new R.State(new[]{M(1,reverse?Body.Fire:Body.Wood,depth:aChild?1:0),M(2,reverse?Body.Wood:Body.Fire,depth:aChild?0:1)});
                var x=R.Resolve(s,new R.ContactPair(1,1,2,1,firstMoving:true),n);
                r.Check("child-consume/"+aChild+reverse,x.Accepted&&x.Commands[0].Product==R.Product.None&&x.Commands[0].Child==null&&
                    x.Commands[0].DamageBudget==0&&x.Commands[0].DurationUs==0&&x.Commands[0].DryUs==300000&&x.Commands[0].ChildConsumedIds.Count==1);
                r.Check("child-consume-replay/"+aChild+reverse,!R.Resolve(x.State,new R.ContactPair(2,1,2,2,firstMoving:true),n).Accepted);
            }
            foreach(var a in new[]{Body.Water,Body.Metal,Body.Fire})
            {
                var s=new R.State(new[]{M(1,a),M(2,Body.Fire,depth:1)});
                var x=R.Resolve(s,new R.ContactPair(1,1,2,1,3,true,directAreaBudget:2),n);
                r.Check("child-other/"+a,x.Accepted&&x.Commands[0].DryUs==0);
                if(a==Body.Water)r.Equal("child-break",R.Product.Damage,x.Commands[0].Product);
                if(a==Body.Metal)r.Equal("child-block",0.5m,x.State.Materials[2].Remaining);
                if(a==Body.Fire)r.Equal("child-same",2.4m,x.State.Materials[2].Remaining);
            }
            var twice=new R.State(new[]{M(1,Body.Wood),M(2,Body.Wood),M(3,Body.Wood)});
            var thick=R.Resolve(twice,new R.ContactPair(1,1,2,1,firstMoving:true),n);
            r.Equal("same-once","SameBodyOnce",R.Resolve(thick.State,new R.ContactPair(2,3,2,2,firstMoving:true),n).Reason);
            var ordering=new R.State(new[]{M(1,Body.Wood,created:10),M(2,Body.Fire,created:20)});
            r.Equal("moving-precedes-newer",1L,R.Resolve(ordering,new R.ContactPair(1,1,2,20,3,true),n).Commands[0].ActingId);
            r.Equal("static-newer",2L,R.Resolve(ordering,new R.ContactPair(1,1,2,20,3),n).Commands[0].ActingId);
            r.Equal("static-cast-tie",2L,R.Resolve(new R.State(new[]{M(1,Body.Wood),M(2,Body.Fire)}),new R.ContactPair(1,1,2,0,3),n).Commands[0].ActingId);
            r.Equal("both-moving-tie",2L,R.Resolve(new R.State(new[]{M(1,Body.Wood),M(2,Body.Fire)}),new R.ContactPair(1,1,2,0,3,true,true),n).Commands[0].ActingId);
            var returns=new R.State(new[]{M(1,Body.Wood,cast:8),M(2,Body.Metal),M(3,Body.Wood,cast:8),M(4,Body.Metal)});
            var outbound=R.Resolve(returns,new R.ContactPair(1,1,2,1,firstMoving:true),n);
            r.Equal("return-old-material","ReturnSameMaterial",R.Resolve(outbound.State,new R.ContactPair(2,3,2,2,firstMoving:true,returning:true),n).Reason);
            r.Check("return-other-material",R.Resolve(outbound.State,new R.ContactPair(2,3,4,2,firstMoving:true,returning:true),n).Accepted);
            foreach(long time in new[]{999999L,1000000L})
                r.Equal("life-boundary/"+time,time<1000000,R.Resolve(new R.State(new[]{M(1,Body.Wood,expires:1000000),M(2,Body.Fire)}),new R.ContactPair(1,1,2,time,3,true),n).Accepted);
            r.Equal("geok-not-material","NotMaterial",R.Resolve(new R.State(new[]{M(1,Body.Fire),M(2,Body.Wood,letter:'걱')}),new R.ContactPair(1,1,2,0,3,true),n).Reason);
            r.Equal("shape-not-material","NotMaterial",R.Resolve(new R.State(new[]{M(1,Body.Fire),M(2,Body.Wood,material:false)}),new R.ContactPair(1,1,2,0,3,true),n).Reason);
            r.Equal("enemy-damage-cap",0.4m,R.Resolve(new R.State(new[]{M(1,Body.Water),M(2,Body.Fire)}),new R.ContactPair(1,1,2,0,firstMoving:true,directAreaBudget:1,enemyIgniter:true),n).Commands[0].DamageBudget);
            r.Equal("hp-budget-not-action-units",80m,R.Resolve(new R.State(new[]{M(1,Body.Water),M(2,Body.Fire)}),new R.ContactPair(1,1,2,0,firstMoving:true,directAreaBudget:100),n).Commands[0].DamageBudget);
            var added=first.State.Add(M(8,Body.Earth,created:100));
            r.Check("registration-preserves-ledger",!R.Resolve(added,new R.ContactPair(1,8,4,100,9,true),n).Accepted&&first.State.Materials.Count==4);
            var lineage=R.Resolve(first.State.Add(M(8,Body.Water,cast:2,created:100)),new R.ContactPair(2,8,4,101,9,true),n);
            r.Equal("child-parent-cast-prohibited","SameCast",lineage.Reason);
            r.Equal("invalid-depth","Depth",R.Resolve(new R.State(new[]{M(1,Body.Fire),M(2,Body.Wood,depth:2)}),new R.ContactPair(1,1,2,0,3,true),n).Reason);
            r.Equal("duplicate-child-id","ChildId",R.Resolve(state,new R.ContactPair(1,1,2,0,3,true),n).Reason);
            r.Equal("out-of-order","Input",R.Resolve(first.State,new R.ContactPair(2,3,4,99,5,true),n).Reason);
        }
    }
}
