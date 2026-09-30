using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Oheangbu.App.Demo;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Demo;

static class Program
{
    static readonly DemoEconomyRules Rules = DemoEconomyRules.ApprovedDefaults;
    static readonly List<string> Passed = new List<string>();
    static int assertions;

    static int Main()
    {
        try
        {
            Run("approved totals, immutable rules and runtime-only stats", Totals);
            Run("all 19 purchases charge exact tier prices; caps reject", AllPurchases);
            Run("insufficient/exact funds and isolated element effect", Funds);
            Run("duplicate request, stale quote and restored purchase journal", Duplicates);
            Run("save failure/exception preserve active and durable values; retry succeeds", Failure);
            Run("reentrant second click cannot charge", Reentry);
            Run("competing wallet change rejects stale transaction", Conflict);
            Run("malformed state and request rejection", Invalid);
            Run("detached snapshots cannot mutate ledger", Isolation);
            Run("file replacement failure and reload preserve atomic pair", Disk);
            Console.WriteLine(JsonSerializer.Serialize(new { status="PASS", suites=Passed.Count, assertions, passed=Passed,
                scope="Production pure C# rules/service compiled directly; fake/temporary-file persistence adapters. Unity scene, actual campaign store, UI and live HP/ink integration not executed." }, new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    static void Run(string name, Action action) { action(); Passed.Add(name); }
    static void Check(bool ok, string message) { assertions++; if(!ok)throw new Exception(message); }
    static void Near(float actual,float expected,string message) => Check(Math.Abs(actual-expected)<.00001f,message+": "+actual);
    static DemoEconomyService Service(MemoryStore store) => new DemoEconomyService(Rules,store);
    static void Bought(DemoEconomyService service,DemoUpgradeTrack track,int level,string id,out DemoPurchaseReceipt receipt)
    { Check(service.TryPurchase(track,level,id,out receipt),"purchase "+id+" failed: "+receipt.Status);Check(receipt.Status==DemoPurchaseStatus.Purchased,"success status"); }
    static void Rejected(DemoEconomyService service,DemoUpgradeTrack track,int level,string id,DemoPurchaseStatus expected)
    { Check(!service.TryPurchase(track,level,id,out var r),"unexpected purchase");Check(r.Status==expected,"expected "+expected+", got "+r.Status);Check(r.Charged==0&&r.Committed==null,"failure receipt published upgrade"); }

    static void Totals()
    {
        var state=new DemoEconomyState();var stats=new RuntimePlayerStats(state,Rules);
        foreach(Element e in Enum.GetValues<Element>())Near(stats.DamageMultiplier(e),1,"initial element");
        Near(stats.MaxHpMultiplier,1,"initial hp");Near(stats.MaxInkMultiplier,1,"initial ink");
        int[] costs={60,120,240};float[] bonuses={.1f,.2f,.3f};
        var rules=new DemoEconomyRules(costs,bonuses,new[]{100,200},new[]{.1f,.2f});
        costs[0]=1;bonuses[2]=9;Check(rules.CostForNextLevel(DemoUpgradeTrack.Wood,0)==60,"mutable source price leaked");
        state.levels[0]=3;state.levels[5]=state.levels[6]=2;stats=new RuntimePlayerStats(state,rules);
        Near(stats.DamageMultiplier(Element.Wood),1.3f,"tiers must total30%, not compound");
        Near(stats.DamageMultiplier(Element.Fire),1,"wood must not buff fire");Near(stats.MaxHpMultiplier,1.2f,"hp total20%");Near(stats.MaxInkMultiplier,1.2f,"ink total20%");
        state.levels[0]=0;Near(stats.DamageMultiplier(Element.Wood),1.3f,"stats must be detached value");
    }

    static void AllPurchases()
    {
        var store=new MemoryStore(10000);var service=Service(store);int total=0,count=0;
        foreach(DemoUpgradeTrack track in Enum.GetValues<DemoUpgradeTrack>())
        {
            for(int level=0;level<Rules.MaximumLevel(track);level++)
            {
                Check(service.TryQuote(track,out var quote,out _),"quote unavailable");Check(quote.CurrentLevel==level&&quote.CanAfford&&!quote.AtMaximum,"quote wrong");
                int cost=Rules.CostForNextLevel(track,level);Check(quote.Cost==cost,"price changed");
                Bought(service,track,level,track+"-"+level,out var receipt);total+=cost;count++;
                Check(receipt.Charged==cost&&store.Read().Tongbo==10000-total,"wrong debit");
                Check(store.Read().State.Level(track)==level+1&&store.Read().Revision==count,"wrong progression");
            }
            Check(service.TryQuote(track,out var capped,out _)&&capped.AtMaximum&&!capped.CanAfford&&capped.Cost==0,"capped quote");
            Rejected(service,track,Rules.MaximumLevel(track),track+"-max",DemoPurchaseStatus.MaximumLevel);
        }
        Check(count==19&&total==2700&&store.Commits==19,"campaign price budget");
        var stats=service.GetStats();foreach(Element e in Enum.GetValues<Element>())Near(stats.DamageMultiplier(e),1.3f,"all element final");
        Near(stats.MaxHpMultiplier,1.2f,"final hp");Near(stats.MaxInkMultiplier,1.2f,"final ink");
    }

    static void Funds()
    {
        var poor=new MemoryStore(59);var service=Service(poor);var before=poor.Read();
        Check(service.TryQuote(DemoUpgradeTrack.Wood,out var q,out _)&&!q.CanAfford,"insufficient quote");
        Rejected(service,DemoUpgradeTrack.Wood,0,"poor",DemoPurchaseStatus.InsufficientTongbo);Check(poor.Read().SameAs(before)&&poor.Commits==0,"poor transaction mutated");
        var exact=new MemoryStore(60);service=Service(exact);Bought(service,DemoUpgradeTrack.Metal,0,"exact",out _);
        Check(exact.Read().Tongbo==0,"exact payment negative/balance");Near(service.GetStats().DamageMultiplier(Element.Metal),1.1f,"metal mapping");Near(service.GetStats().DamageMultiplier(Element.Earth),1,"enum order earth unaffected");
    }

    static void Duplicates()
    {
        var store=new MemoryStore(1000);var service=Service(store);
        Bought(service,DemoUpgradeTrack.Wood,0,"click-1",out _);var before=store.Read();
        Rejected(service,DemoUpgradeTrack.Wood,1,"click-1",DemoPurchaseStatus.DuplicateRequest);
        Rejected(service,DemoUpgradeTrack.Wood,0,"second-delivery",DemoPurchaseStatus.StaleQuote);
        var restored=new MemoryStore(store.Durable.Tongbo,store.Durable.State);service=Service(restored);
        Rejected(service,DemoUpgradeTrack.Wood,1,"click-1",DemoPurchaseStatus.DuplicateRequest);
        Check(store.Read().SameAs(before)&&restored.Read().SameAs(before),"duplicate changed money/level");
        Bought(service,DemoUpgradeTrack.Wood,1,"click-2",out _);Check(restored.Read().Tongbo==820,"fresh intent failed");
    }

    static void Failure()
    {
        var store=new MemoryStore(1000){Fail=true};var service=Service(store);var before=store.Read();
        Rejected(service,DemoUpgradeTrack.Health,0,"hp",DemoPurchaseStatus.SaveFailed);
        Check(store.Read().SameAs(before)&&store.Durable.SameAs(before),"failed save leaked");Near(service.GetStats().MaxHpMultiplier,1,"failed maxHP applied");
        store.Fail=false;store.Throw=true;Rejected(service,DemoUpgradeTrack.Health,0,"hp",DemoPurchaseStatus.SaveFailed);
        Check(store.Read().SameAs(before)&&store.Durable.SameAs(before),"throw leaked");
        store.Throw=false;Bought(service,DemoUpgradeTrack.Health,0,"hp",out _);Near(service.GetStats().MaxHpMultiplier,1.1f,"retry didn't apply");Check(store.Read().Tongbo==900,"retry double charge");
    }

    static void Reentry()
    {
        var store=new MemoryStore(1000);var service=Service(store);DemoPurchaseStatus nested=DemoPurchaseStatus.Purchased;
        store.BeforeCommit=()=>{bool ok=service.TryPurchase(DemoUpgradeTrack.Ink,0,"nested",out var r);Check(!ok,"nested accepted");nested=r.Status;};
        Bought(service,DemoUpgradeTrack.Health,0,"outer",out _);
        Check(nested==DemoPurchaseStatus.Busy&&store.Commits==1&&store.Read().Tongbo==900,"reentry charged twice");Check(store.Read().State.Level(DemoUpgradeTrack.Ink)==0,"nested level");
    }

    static void Conflict()
    {
        var store=new MemoryStore(1000);var service=Service(store);store.BeforeCommit=()=>store.SetWallet(25);
        Rejected(service,DemoUpgradeTrack.Water,0,"race",DemoPurchaseStatus.Conflict);
        Check(store.Read().Tongbo==25&&store.Read().State.Level(DemoUpgradeTrack.Water)==0&&store.Commits==0,"lost concurrent currency change");
    }

    static void Invalid()
    {
        var state=new DemoEconomyState();state.levels[0]=4;var store=new MemoryStore(100,state);var service=Service(store);
        Rejected(service,DemoUpgradeTrack.Wood,4,"bad-state",DemoPurchaseStatus.InvalidState);Check(store.Commits==0,"invalid state saved");
        foreach(var invalid in new[]{new DemoEconomyState{levels=null},new DemoEconomyState{revision=-1},new DemoEconomyState{purchaseIds=new List<string>{"same","same"}},new DemoEconomyState{version=9}})
            Check(!invalid.IsValid(Rules),"malformed state normalized silently");
        service=Service(new MemoryStore(-1));Rejected(service,DemoUpgradeTrack.Wood,0,"negative-wallet",DemoPurchaseStatus.InvalidState);
        service=Service(new MemoryStore(100));Rejected(service,(DemoUpgradeTrack)77,0,"bad-track",DemoPurchaseStatus.InvalidRequest);
        Rejected(service,DemoUpgradeTrack.Health,0," ",DemoPurchaseStatus.InvalidRequest);Rejected(service,DemoUpgradeTrack.Health,-1,"bad-level",DemoPurchaseStatus.InvalidRequest);
        Check(!service.TryQuote((DemoUpgradeTrack)(-1),out _,out _),"invalid quote");
        bool invalidRules=false;try{new DemoEconomyRules(new[]{0,120,240},new[]{.1f,.2f,.3f},new[]{100,200},new[]{.1f,.2f});}catch(ArgumentException){invalidRules=true;}Check(invalidRules,"free tier accepted");
    }

    static void Isolation()
    {
        var original=new DemoEconomyState();var snapshot=new DemoEconomySnapshot(1000,original);original.levels[0]=3;
        Check(snapshot.State.Level(DemoUpgradeTrack.Wood)==0,"constructor alias");var copy=snapshot.State;copy.levels[1]=2;copy.purchaseIds.Add("leak");
        Check(snapshot.State.Level(DemoUpgradeTrack.Fire)==0&&snapshot.State.purchaseIds.Count==0,"getter alias");
        var store=new MemoryStore(1000);var service=Service(store);Bought(service,DemoUpgradeTrack.Wood,0,"isolation",out var receipt);
        receipt.Committed.State.levels[0]=0;Check(store.Read().State.Level(DemoUpgradeTrack.Wood)==1,"receipt alias");
    }

    static void Disk()
    {
        string directory=Path.Combine(Path.GetTempPath(),"OheangbuEconomyChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"campaign.json");
        try
        {
            var store=new FileStore(path,1000);var service=new DemoEconomyService(Rules,store);
            Bought(service,DemoUpgradeTrack.Ink,0,"disk-1",out _);byte[] bytes=File.ReadAllBytes(path);var before=store.Read();
            store.FailBeforeReplace=true;Rejected(service,DemoUpgradeTrack.Ink,1,"disk-2",DemoPurchaseStatus.SaveFailed);
            Check(bytes.SequenceEqual(File.ReadAllBytes(path))&&store.Read().SameAs(before),"replace failure lost saved pair");
            store=new FileStore(path,0);service=new DemoEconomyService(Rules,store);
            Check(store.Read().Tongbo==900&&store.Read().State.Level(DemoUpgradeTrack.Ink)==1,"disk reload lost pair");
            Rejected(service,DemoUpgradeTrack.Ink,1,"disk-1",DemoPurchaseStatus.DuplicateRequest);
            Bought(service,DemoUpgradeTrack.Ink,1,"disk-2",out _);
            var reopened=new FileStore(path,0);Check(reopened.Read().Tongbo==700&&reopened.Read().State.Level(DemoUpgradeTrack.Ink)==2,"retry pair reload");
        }
        finally { foreach(string file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory); }
    }

    sealed class MemoryStore : IDemoEconomyStore
    {
        DemoEconomySnapshot current;
        public DemoEconomySnapshot Durable {get;private set;}
        public bool Fail,Throw;public int Commits;public Action BeforeCommit;
        public MemoryStore(int tongbo,DemoEconomyState state=null){current=new DemoEconomySnapshot(tongbo,state??new DemoEconomyState());Durable=current;}
        public DemoEconomySnapshot Read()=>new DemoEconomySnapshot(current.Tongbo,current.State);
        public void SetWallet(int value){current=new DemoEconomySnapshot(value,current.State);Durable=current;}
        public DemoEconomyCommitStatus TryCommit(DemoEconomySnapshot expected,DemoEconomySnapshot next,out string error)
        {
            var callback=BeforeCommit;BeforeCommit=null;callback?.Invoke();error=null;
            if(!current.SameAs(expected))return DemoEconomyCommitStatus.Conflict;
            if(Throw)throw new IOException("test write denied");
            if(Fail){error="test save failed";return DemoEconomyCommitStatus.Failed;}
            Durable=new DemoEconomySnapshot(next.Tongbo,next.State);current=Durable;Commits++;return DemoEconomyCommitStatus.Saved;
        }
    }

    sealed class FileStore : IDemoEconomyStore
    {
        sealed class Save {public int tongbo;public DemoEconomyState economy;}
        readonly string path;DemoEconomySnapshot current;public bool FailBeforeReplace;
        static readonly JsonSerializerOptions Options=new JsonSerializerOptions{IncludeFields=true};
        public FileStore(string path,int startingMoney)
        {
            this.path=path;
            if(File.Exists(path)){var data=JsonSerializer.Deserialize<Save>(File.ReadAllText(path),Options);current=new DemoEconomySnapshot(data.tongbo,data.economy);}
            else{current=new DemoEconomySnapshot(startingMoney,new DemoEconomyState());File.WriteAllText(path,Encode(current));}
        }
        public DemoEconomySnapshot Read()=>new DemoEconomySnapshot(current.Tongbo,current.State);
        static string Encode(DemoEconomySnapshot s)=>JsonSerializer.Serialize(new Save{tongbo=s.Tongbo,economy=s.State},Options);
        public DemoEconomyCommitStatus TryCommit(DemoEconomySnapshot expected,DemoEconomySnapshot next,out string error)
        {
            error=null;if(!current.SameAs(expected))return DemoEconomyCommitStatus.Conflict;
            File.WriteAllText(path+".tmp",Encode(next));
            if(FailBeforeReplace){error="injected pre-replace failure";return DemoEconomyCommitStatus.Failed;}
            File.Move(path+".tmp",path,true);current=new DemoEconomySnapshot(next.Tongbo,next.State);return DemoEconomyCommitStatus.Saved;
        }
    }
}
