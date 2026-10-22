using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Oheangbu.Vol2.Rules;
namespace Vol2V3.Tests
{
    public static class Cases_GrammarBudget
    {
        public static void Run(Report r,Context c)
        {
            r.Check("canonical",c.Import.CanCast,string.Join(" | ",c.Import.Errors));if(!c.Import.CanCast)return;
            r.Run("grammar",()=>Grammar(r,c));r.Run("golden",()=>Golden(r,c));r.Run("events",()=>Replay(r,c));r.Run("state",()=>StateCases(r,c));
        }
        static void Grammar(Report r,Context c)
        {
            int count=0;for(int ch=0;ch<=char.MaxValue;ch++)if(GlyphRulesV3.TryDecode((char)ch,out _))count++;
            r.Equal("unicode-domain",120,count);
            foreach(var row in c.Import.Spells)
            {
                bool decoded=GlyphRulesV3.TryDecode(row.Letter,out var glyph);
                r.Check("roundtrip/"+row.Letter,decoded&&GlyphRulesV3.Encode(glyph.Body,glyph.Frame,glyph.Verb)==row.Letter&&glyph.Kind==row.Kind);
            }
            foreach(char ch in new[]{'ㄱ','ㅏ','값','까','기','A','\0','한','\uD800'})r.Check("illegal/"+(int)ch,!GlyphRulesV3.TryDecode(ch,out _));
            foreach(var row in c.Import.Spells.Where(x=>x.Kind==SpellKind.Bare))
            {var p=c.Plan(row.Letter);r.Check("bare-material/"+row.Letter,p.Shared==0&&p.Term(EffectKind.Residue)==0&&(!row.Glyph.IsParry||p.Total==0));}
            var types=typeof(GlyphRulesV3).Assembly.GetTypes().Where(t=>t.Namespace=="Oheangbu.Vol2.Rules").ToArray();
            r.Check("system-only-namespace",types.All(t=>t.BaseType==null||t.BaseType.Namespace.StartsWith("System",StringComparison.Ordinal)));
            var globals=types.SelectMany(t=>t.GetFields(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)).Where(f=>!f.IsLiteral&&!f.IsInitOnly&& !f.DeclaringType.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute),false)).ToArray();
            r.Check("no-mutable-static-state",globals.Length==0,string.Join(",",globals.Select(f=>f.DeclaringType.Name+"."+f.Name)));
            r.Check("no-unity-reference",!typeof(GlyphRulesV3).Assembly.GetReferencedAssemblies().Any(a=>a.Name.Contains("Unity")));
            r.Check("no-package-reference",!File.ReadAllText(Path.Combine(c.Stage,"Vol2V3.csproj")).Contains("PackageReference"));
        }
        static void Golden(Report r,Context c)
        {
            foreach(var row in c.Golden.RootElement.GetProperty("rows").EnumerateArray())
            {
                char letter=row.GetProperty("letter").GetString()[0];var p=c.Plan(letter);string id="source/"+letter;
                r.Near(id+"/cost",row.GetProperty("cost").GetDecimal(),p.Cost);
                r.Near(id+"/per-target",row.GetProperty("perTarget").GetDecimal(),p.PerTarget);
                r.Near(id+"/shared",row.GetProperty("shared").GetDecimal(),p.Shared);
                for(int targets=1;targets<=4;targets++)
                {
                    int effective=p.FirstOnly?1:p.Limited?Math.Min(targets,3):targets;
                    decimal total=row.GetProperty("perTarget").GetDecimal()*effective+row.GetProperty("shared").GetDecimal();
                    r.Near(id+"/N"+targets,total,p.TotalFor(targets));
                }
                if(p.Glyph.Kind!=SpellKind.Peak)continue;
                var terms=row.GetProperty("terms").EnumerateArray().ToArray();
                r.Equal(id+"/term-count",terms.Length,p.EffectTerms.Count);
                for(int i=0;i<terms.Length;i++)
                {
                    var t=terms[i];var actual=p.EffectTerms[i];
                    r.Check(id+"/term"+i,t.GetProperty("name").GetString()==actual.Name&&t.GetProperty("amount").GetDecimal()==actual.Amount&&t.GetProperty("count").GetInt32()==actual.Count&&t.GetProperty("shared").GetBoolean()==actual.Shared);
                }
                var peak=PeakRulesV3.Evaluate(p,new ContactProfile(),1,false);
                r.Near(id+"/cap",row.GetProperty("cap").GetDecimal(),peak.StructureForceCap);
                r.Check(id+"/structural-cap",peak.WithinStructuralCap);
                for(int targets=1;targets<=4;targets++)foreach(int contacts in new[]{1,2})
                {
                    var profile=new ContactProfile(targets,contacts);
                    var plain=PeakRulesV3.Evaluate(p,profile,1.15m,false);
                    var counter=PeakRulesV3.Evaluate(p,profile,1.15m,true);
                    r.Check(id+"/force-counter/N"+targets+"/C"+contacts,plain.FinalMultiplier<=1.5m&&counter.FinalMultiplier<=1.875m&&counter.FinalTotal==plain.FinalTotal*1.25m);
                }
            }
            var plans=c.Import.Spells.Select(x=>c.Plan(x.Letter)).ToArray();
            for(int n=1;n<=4;n++)Rank(r,plans,n,c.Golden.RootElement.GetProperty("rankings").GetProperty(n.ToString()),"current",c.Numbers,n<=2);
            using(var balance=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(c.Stage,"Golden/balance.json"))))
            {
                r.Equal("balance-source-hash",Vol2ImportManifestDto.Sha256(c.Sources["checker"]),balance.RootElement.GetProperty("checkerHash").GetString());
                foreach(int targetCount in new[]{1,2})
                {
                    decimal bareAll=0,normalAll=0;int frames=0;
                    foreach(var pair in new[]{(Frame.Path,"ㅏ"),(Frame.Fan,"ㅗ"),(Frame.Ring,"ㅜ")})
                    {
                        string f=RuleTypesV3.FrameKey(pair.Item1);int bareTargets=pair.Item1==Frame.Path?1:targetCount;
                        decimal bare=(decimal)bareTargets/c.Numbers.Get("ink.bare."+f),normal=0;
                        foreach(string verb in new[]{"g","n","m","s","ng"})
                        {
                            int k=pair.Item1==Frame.Path&&verb=="s"?1:targetCount;
                            normal+=(c.Numbers.Get("action.normal."+verb+".perTarget")*k+c.Numbers.Get("action.normal."+verb+".shared"))/c.Numbers.Get("ink.normal."+f)/5;
                        }
                        decimal efficiency=bare/normal;
                        r.Near("efficiency/N"+targetCount+"/"+f,balance.RootElement.GetProperty("efficiency").GetProperty(targetCount.ToString()).GetProperty(pair.Item2).GetDecimal(),efficiency);
                        if(targetCount==1||pair.Item1!=Frame.Path)
                        {
                            r.Check("efficiency-gate/N"+targetCount+"/"+f,efficiency>=0.75m&&(targetCount==1?efficiency<=0.9m:efficiency<1));
                            bareAll+=bare;normalAll+=normal;frames++;
                        }
                    }
                    r.Near("efficiency-overall/N"+targetCount,balance.RootElement.GetProperty("efficiency").GetProperty(targetCount.ToString()).GetProperty("전체").GetDecimal(),(bareAll/frames)/(normalAll/frames));
                }
                var candidate=c.Numbers.CreateEoCandidateProfile();var candidatePlans=c.Import.Spells.Select(x=>c.Plan(x.Letter,candidate)).ToArray();
                foreach(var row in balance.RootElement.GetProperty("candidatePeaks").EnumerateArray())
                {
                    var p=c.Plan(row.GetProperty("letter").GetString()[0],candidate);
                    r.Near("candidate/"+p.Glyph.Letter,row.GetProperty("total").GetDecimal(),p.Total);
                    r.Check("candidate-cap/"+p.Glyph.Letter,PeakRulesV3.Evaluate(p,new ContactProfile(),1,false).WithinStructuralCap);
                }
                for(int n=1;n<=2;n++)Rank(r,candidatePlans,n,balance.RootElement.GetProperty("candidateRankings").GetProperty(n.ToString()),"candidate",candidate,true);
                r.Check("candidate-drawing",candidate.Get("action.eo.n.total")/(candidate.Get("ink.normal.eo")/candidate.Get("ink.regen.combat")+candidate.Get("timing.drawing.eoMax"))>candidate.Get("combat.noInk.damage")/candidate.Get("combat.noInk.cooldown"));
            }
            r.Equal("seot-total",2.1m,c.Plan('섯').Total);
            r.Equal("seot-cap",2.1m,PeakRulesV3.Evaluate(c.Plan('섯'),new ContactProfile(),1,false).StructureForceCap);
            r.Equal("seot-critical-max",1.875m,PeakRulesV3.Evaluate(c.Plan('섯'),new ContactProfile(),1.15m,true).FinalMultiplier);
            r.Equal("normal-critical",1.4375m,PeakRulesV3.Evaluate(c.Plan('넛'),new ContactProfile(),1.15m,true).FinalMultiplier);
            r.Equal("no-contact-no-shared",0m,PeakRulesV3.Evaluate(c.Plan('섯'),new ContactProfile(0,0,false,false,false),1,true).FinalTotal);
            r.Equal("one-contact-cap",1.65m,PeakRulesV3.Evaluate(c.Plan('섯'),new ContactProfile(1,1),1,false).StructureForceCap);
            r.Equal("seed-untriggered",0m,PeakRulesV3.Evaluate(c.Plan('몸'),new ContactProfile(1,0,false,false,false),1,false).FinalTotal);
        }
        static void Rank(Report r,GlyphRulesV3.Plan[] plans,int count,JsonElement golden,string label,NumbersV3Dto numbers,bool gates)
        {
            var ranked=plans.Where(p=>!p.Glyph.IsParry).OrderByDescending(p=>p.ValuePerInk(count)).ToArray();
            var source=golden.EnumerateArray().ToArray();string id="rank/"+label+"/N"+count;
            r.Equal(id+"/count",source.Length,ranked.Length);
            for(int i=0;i<source.Length;i++)
            {
                var expected=source[i];char ch=expected.GetProperty("letter").GetString()[0];decimal score=expected.GetProperty("score").GetDecimal();
                r.Near(id+"/score/"+ch,score,plans.Single(p=>p.Glyph.Letter==ch).ValuePerInk(count));
                // Binary-float ties may have different stable ordering; compare the rank score too.
                r.Near(id+"/position/"+(i+1),score,ranked[i].ValuePerInk(count));
            }
            if(!gates)return;
            var top=ranked.Take((int)numbers.Get("gate.topSample")).ToArray();
            r.Check(id+"/body",top.GroupBy(p=>p.Glyph.Body).Max(x=>x.Count())<=numbers.Get("gate.bodyBias"));
            r.Check(id+"/frame",top.GroupBy(p=>p.Glyph.Frame).Max(x=>x.Count())<=numbers.Get("gate.frameBias"));
            r.Check(id+"/verb",top.GroupBy(p=>p.Glyph.Verb).Max(x=>x.Count())<=numbers.Get("gate.verbBias"));
            r.Check(id+"/top-ten",new HashSet<char>(top.Select(p=>p.Glyph.Letter)).SetEquals(source.Take(10).Select(x=>x.GetProperty("letter").GetString()[0])));
            if(label=="current"&&count==2)
            {
                r.Check("C5-preserved-boundary-warning",ranked[9].Glyph.Letter=='넘'&&ranked[10].Glyph.Letter=='넛'&&ranked[9].ValuePerInk(2)==0.24m&&ranked[10].ValuePerInk(2)==0.24m);
                var swapped=ranked.Take(9).Concat(new[]{ranked[10]}).ToArray();
                r.Check("C5-boundary-swap-gates",swapped.GroupBy(x=>x.Glyph.Body).Max(x=>x.Count())<=4&&swapped.GroupBy(x=>x.Glyph.Frame).Max(x=>x.Count())<=6&&swapped.GroupBy(x=>x.Glyph.Verb).Max(x=>x.Count())<=5);
            }
        }
        static void Replay(Report r,Context c)
        {
            using(var fixture=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(c.Stage,"Golden/events.json"))))
            {
                r.Equal("events-contract-hash",Vol2ImportManifestDto.Sha256(c.Sources["contract"]),fixture.RootElement.GetProperty("contractHash").GetString());
                foreach(var test in fixture.RootElement.GetProperty("cases").EnumerateArray())
                {
                    string id=test.GetProperty("caseId").GetString();var p=c.Plan(test.GetProperty("letter").GetString()[0]);
                    var s=new EffectBudgetV3.State(test.GetProperty("castId").GetInt64(),p,test.GetProperty("startedUs").GetInt64());
                    var commandIds=new HashSet<string>();int index=0;
                    foreach(var item in test.GetProperty("events").EnumerateArray())
                    {
                        var input=item.GetProperty("input");var x=item.GetProperty("expected");
                        long L(string key,long fallback=0)=>input.TryGetProperty(key,out var v)?v.GetInt64():fallback;
                        string S(string key,string fallback)=>input.TryGetProperty(key,out var v)?v.GetString():fallback;
                        decimal amount=input.TryGetProperty("amount",out var a)?a.GetDecimal():0;
                        var e=new Contact(L("eventId"),L("castId",s.CastId),L("targetId"),L("nowUs"),L("swingId"),Enum.Parse<ContactKind>(S("kind","Touch")),Enum.Parse<EffectKind>(S("effect","Residue")),amount,Enum.Parse<TriggerKind>(S("trigger","None")),L("lifeRevision"),L("materialId"));
                        decimal oldPaid=s.TotalPaid,oldShared=s.SharedRemaining;int oldSlots=s.Targets.Count;
                        var step=EffectBudgetV3.Apply(s,e,p);string prefix=id+"/"+(++index);
                        r.Check(prefix+"/immutable",s.TotalPaid==oldPaid&&s.SharedRemaining==oldShared&&s.Targets.Count==oldSlots);
                        r.Equal(prefix+"/accepted",x.GetProperty("accepted").GetBoolean(),step.Accepted);
                        r.Equal(prefix+"/reason",x.GetProperty("reason").GetString(),step.Reason);
                        s=step.State;
                        r.Equal(prefix+"/per",x.GetProperty("per").GetDecimal(),s.PaidPerTarget);
                        r.Equal(prefix+"/shared",x.GetProperty("shared").GetDecimal(),s.PaidShared);
                        r.Equal(prefix+"/remaining",x.GetProperty("remaining").GetDecimal(),s.SharedRemaining);
                        r.Equal(prefix+"/slots",x.GetProperty("slots").GetInt32(),s.Targets.Count);
                        r.Equal(prefix+"/commands",x.GetProperty("commandAmount").GetDecimal(),step.Commands.Sum(v=>v.ActionAmount));
                        if(x.TryGetProperty("transferred",out var transferred))r.Equal(prefix+"/transferred",transferred.GetDecimal(),s.Transferred);
                        if(x.TryGetProperty("borrowed",out var borrowed))r.Equal(prefix+"/borrowed",borrowed.GetDecimal(),s.BorrowedPaid);
                        if(x.TryGetProperty("childBudget",out var child))r.Equal(prefix+"/child",child.GetDecimal(),step.Commands.Sum(v=>v.ChildBudget));
                        if(x.TryGetProperty("hp",out var hp))r.Equal(prefix+"/hp",hp.GetDecimal(),step.Commands.Sum(v=>v.HpDamage));
                        if(x.TryGetProperty("utility",out var utility))r.Equal(prefix+"/utility",utility.GetDecimal(),step.Commands.Sum(v=>v.UtilityValue));
                        r.Check(prefix+"/conservation",s.PaidShared+s.Transferred+s.SharedRemaining==p.Shared&&s.TotalPaid==s.PaidPerTarget+s.PaidShared&&s.Targets.All(t=>t.Paid<=p.PerTarget));
                        r.Check(prefix+"/command-identity",step.Commands.All(cmd=>commandIds.Add(cmd.Id)&&cmd.CastId==e.CastId&&cmd.TargetId==e.TargetId&&cmd.LifeRevision==e.LifeRevision&&cmd.MaterialId==e.MaterialId));
                        if(!step.Accepted)r.Equal(prefix+"/rejected-no-commands",0,step.Commands.Count);
                    }
                }
            }
        }
        static void StateCases(Report r,Context c)
        {
            foreach(var row in c.Import.Spells.Where(x=>c.Plan(x.Letter).Limited))
            {
                var p=c.Plan(row.Letter);var state=new EffectBudgetV3.State(1,p);
                for(int target=1;target<=4;target++)
                {
                    var step=EffectBudgetV3.Apply(state,new Contact(target,1,target,0,1,p.Glyph.Verb==Verb.Seed?ContactKind.Plant:ContactKind.Touch),p);state=step.State;
                    r.Equal("all-30-capped/"+row.Letter+"/"+target,target<=3,step.Accepted);
                }
                r.Equal("all-30-slots/"+row.Letter,3,state.Targets.Count);
            }
            foreach(char ch in new[]{'고','구','녹','눅','몸','뭄'})
            {
                var p=c.Plan(ch);var s=new EffectBudgetV3.State(2,p);
                for(int t=1;t<=4;t++)
                {
                    bool isSeed=p.Glyph.Verb==Verb.Seed;
                    var step=EffectBudgetV3.Apply(s,new Contact(t,2,t,0,1,isSeed?ContactKind.Activate:ContactKind.Touch,trigger:p.Glyph.Frame==Frame.Fan?TriggerKind.EnterFan:TriggerKind.CrossRing),p);s=step.State;
                    r.Check("uncapped-fan-ring/"+ch+"/"+t,step.Accepted,step.Reason);
                }
                r.Equal("uncapped-four/"+ch,4,s.Targets.Count);
            }
            var root=c.Plan('걱');var rs=new EffectBudgetV3.State(3,root);
            for(int i=1;i<=5;i++)
            {
                var step=EffectBudgetV3.Apply(rs,new Contact(i,3,1,i,kind:ContactKind.Shared,amount:2,materialId:i),root);rs=step.State;
                r.Equal("root-material-count/"+i,i<=4,step.Accepted);
                if(i<=4)r.Equal("root-material-amount/"+i,i==1?0.9m:0.25m,step.Commands.Sum(v=>v.ActionAmount));
            }
            r.Equal("root-total",1.65m,rs.PaidShared);
            foreach(char ch in new[]{'난','넌'})
            {
                var p=c.Plan(ch);var s=new EffectBudgetV3.State(30,p);
                var firstSpread=EffectBudgetV3.Apply(s,new Contact(1,30,1,0,kind:ContactKind.Shared,effect:EffectKind.Spread,amount:2,materialId:1),p);s=firstSpread.State;
                r.Equal("peak-spread-per-target/"+ch,0.5m,firstSpread.Commands.Sum(x=>x.ActionAmount));
                r.Equal("peak-spread-once/"+ch,"SpreadTargetExhausted",EffectBudgetV3.Apply(s,new Contact(2,30,1,1,kind:ContactKind.Shared,effect:EffectKind.Spread,amount:0.5m,materialId:1),p).Reason);
                for(int t=2;t<=3;t++)s=EffectBudgetV3.Apply(s,new Contact(t+1,30,t,t,kind:ContactKind.Shared,effect:EffectKind.Spread,amount:0.5m,materialId:1),p).State;
                r.Equal("peak-spread-total/"+ch,1.5m,s.PaidShared);
                r.Check("peak-spread-fourth/"+ch,!EffectBudgetV3.Apply(s,new Contact(5,30,4,4,kind:ContactKind.Shared,effect:EffectKind.Spread,amount:0.5m,materialId:1),p).Accepted);
            }
            var spread=c.Plan('간');var spreadState=new EffectBudgetV3.State(31,spread);
            for(int t=1;t<=4;t++)spreadState=EffectBudgetV3.Apply(spreadState,new Contact(t,31,t,t,kind:ContactKind.Shared,effect:EffectKind.Spread,amount:0.1m,materialId:1),spread).State;
            r.Equal("general-spread-four-targets","SpreadTargetCap",EffectBudgetV3.Apply(spreadState,new Contact(5,31,5,5,kind:ContactKind.Shared,effect:EffectKind.Spread,amount:0.1m,materialId:1),spread).Reason);
            r.Equal("general-spread-unused-budget",0.6m,spreadState.SharedRemaining);
            var reflect=c.Plan('엉');var bs=new EffectBudgetV3.State(4,reflect);
            for(int i=1;i<=4;i++)
            {
                var step=EffectBudgetV3.Apply(bs,new Contact(i,4,1,i,kind:ContactKind.Reflect,amount:0.2m,materialId:i),reflect);bs=step.State;
                r.Equal("reflection-count/"+i,i<=3,step.Accepted);
            }
            r.Equal("borrowed-separate",0m,bs.TotalPaid);r.Equal("borrowed-three",0.3m,bs.BorrowedPaid);
            var ang=c.Plan('앙');r.Equal("ang-no-borrowed","BorrowedScope",EffectBudgetV3.Apply(new EffectBudgetV3.State(5,ang),new Contact(1,5,1,0,kind:ContactKind.Reflect,amount:1,materialId:1),ang).Reason);
            var seed=c.Plan('감');var ss=new EffectBudgetV3.State(6,seed);
            ss=EffectBudgetV3.Apply(ss,new Contact(1,6,1,0,1,ContactKind.Plant),seed).State;
            r.Check("seed-before-expiry",EffectBudgetV3.Apply(ss,new Contact(2,6,1,7999999,2,ContactKind.Activate,trigger:TriggerKind.OwnerAttack),seed).Accepted);
            r.Equal("seed-at-expiry","Expired",EffectBudgetV3.Apply(ss,new Contact(2,6,1,8000000,2,ContactKind.Activate,trigger:TriggerKind.OwnerAttack),seed).Reason);
            var a=c.Plan('넉');var first=EffectBudgetV3.Apply(new EffectBudgetV3.State(7,a),new Contact(1,7,1,0,1),a);
            r.Check("separate-cast-same-target",EffectBudgetV3.Apply(new EffectBudgetV3.State(8,a),new Contact(1,8,1,0,1),a).Accepted);
            r.Equal("second-does-not-free-slot",1,EffectBudgetV3.Apply(first.State,new Contact(2,7,1,300000,2),a).State.Targets.Count);
            var snapshot=first.State.Targets; snapshot[0].Paid=99;
            r.Equal("target-snapshot-defensive-copy",0.7m,first.State.Targets[0].Paid);
            r.Check("budget-negative-rejected",!EffectBudgetV3.Apply(first.State,new Contact(2,7,1,1,kind:ContactKind.Shared,amount:-1,materialId:1),a).Accepted);
            var critical=new PeakRulesV3.State();var one=PeakRulesV3.TryCritical(critical,10,c.Plan('각'));
            r.Check("critical-first",one.Accepted);
            r.Check("critical-repeat-rejected",!PeakRulesV3.TryCritical(one.State,10,c.Plan('각')).Accepted);
            var other=PeakRulesV3.TryCritical(one.State,10,c.Plan('난'));r.Check("critical-other-letter",other.Accepted);
            r.Check("critical-return-after-other",PeakRulesV3.TryCritical(other.State,10,c.Plan('각')).Accepted);
            r.Check("critical-new-window",PeakRulesV3.TryCritical(one.State,11,c.Plan('각')).Accepted);
            r.Check("critical-state-immutable",critical.WindowId==0&&one.State.PreviousPeak=='각');
        }
    }
}
