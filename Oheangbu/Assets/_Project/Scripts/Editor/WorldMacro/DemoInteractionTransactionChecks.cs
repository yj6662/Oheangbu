using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Exercises the actual proposal/commit methods and isolated AtomicJsonStore writes; no live scene or player save.</summary>
    public static class DemoInteractionTransactionChecks
    {
        [Serializable] sealed class Result
        {
            public string status="PASS",scope="Edit-mode Session proposal/commit methods and real AtomicJsonStore failure/retry in a UUID temporary directory. Native interaction and UI-event presentation require Play verification.";
            public List<string> checks=new List<string>();
        }
        const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        static MethodInfo Method(string name)=>typeof(WorldMacroPlaytestSession).GetMethod(name,Private)??throw new MissingMethodException(name);
        static void Field(WorldMacroPlaytestSession session,string name,object value)=>typeof(WorldMacroPlaytestSession).GetField(name,Private).SetValue(session,value);
        static void Progress(WorldMacroPlaytestSession session,WorldMacroProgress value)=>typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session,value);
        static bool Prepare(WorldMacroProgress source,PrologueContentSO.Point point,out WorldMacroProgress candidate,out bool first,out bool record)
        {
            object[] args={source,point,null,false,false,null};
            bool ok=(bool)Method("TryPreparePointInteraction").Invoke(null,args);
            candidate=(WorldMacroProgress)args[2];first=(bool)args[3];record=(bool)args[4];return ok;
        }
        static bool Recovery(WorldMacroProgress source,out WorldMacroProgress candidate,out int amount)
        {
            object[] args={source,null,0,null};bool ok=(bool)Method("TryPrepareCurrencyRecovery").Invoke(null,args);
            candidate=(WorldMacroProgress)args[1];amount=(int)args[2];return ok;
        }
        static bool Commit(WorldMacroPlaytestSession session,WorldMacroProgress candidate,out string error)
        {
            object[] args={candidate,null};bool ok=(bool)Method("TryCommitInteraction").Invoke(session,args);error=(string)args[1];return ok;
        }
        public static string Run()
        {
            var result=new Result();
            Action<bool,string> check=(ok,name)=>{if(!ok)throw new InvalidOperationException(name);result.checks.Add(name);};
            string folder=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"OheangbuInteractionChecks_"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder);
            var host=new GameObject("IsolatedInteractionTransactionTest");
            var session=host.AddComponent<WorldMacroPlaytestSession>();
            session.Actors=Array.Empty<PrologueEncounter>();
            try
            {
                Field(session,"ready",true);
                string blocker=Path.Combine(folder,"not_a_directory");File.WriteAllText(blocker,"intentional write blocker");
                var kinds=new[]{PrologueInteractionKind.Currency,PrologueInteractionKind.Evidence,PrologueInteractionKind.Conversation,PrologueInteractionKind.Conversation};
                var ids=new[]{"demo_logging_cache","mine_inquiry","logger","village_commission"};
                for(int i=0;i<ids.Length;i++)
                {
                    var source=WorldMacroProgress.CreateNew("test",new Vector3(1,2,3),42);
                    source.ledger.currency=11;source.ledger.dropCurrency=7;source.ledger.hp=.4f;source.ledger.ink=.3f;
                    source.ledger.completed.Add("already_completed");
                    string before=JsonUtility.ToJson(source);
                    var point=new PrologueContentSO.Point{Id=ids[i],Kind=kinds[i],Currency=i==3?0:12,Prompt="test",Text="test"};
                    check(Prepare(source,point,out var candidate,out bool first,out _)&&first,"prepare "+point.Id);
                    check(JsonUtility.ToJson(source)==before&&!ReferenceEquals(source,candidate),"detached proposal preserves original "+point.Id);
                    check(candidate.ledger.completed.Contains(point.Id)&&candidate.ledger.currency==11+point.Currency,"candidate contains reward and completion together "+point.Id);
                    Progress(session,source);Field(session,"lastSuccessfulSnapshot",null);
                    Field(session,"store",new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker,point.Id+".json"),WorldMacroProgress.Valid));
                    check(!Commit(session,candidate,out string error)&&!string.IsNullOrEmpty(error),"actual filesystem failure rejected "+point.Id);
                    check(ReferenceEquals(session.Progress,source)&&JsonUtility.ToJson(source)==before,"failed save publishes no reward, completion or record "+point.Id);
                    check(Prepare(source,point,out candidate,out first,out _)&&first,"failed interaction remains claimable "+point.Id);
                    string path=Path.Combine(folder,point.Id+".json");
                    Field(session,"store",new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid));
                    check(Commit(session,candidate,out error)&&error==null&&ReferenceEquals(session.Progress,candidate),"successful retry publishes accepted progress "+point.Id);
                    var loaded=new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid).Load();
                    check(loaded!=null&&loaded.ledger.currency==11+point.Currency&&loaded.ledger.completed.Contains(point.Id)&&loaded.ledger.completed.Contains("already_completed")&&loaded.ledger.dropCurrency==7,"disk retains exact reward and old progress "+point.Id);
                    check(Prepare(session.Progress,point,out var duplicate,out first,out _)&&!first&&duplicate.ledger.currency==loaded.ledger.currency,"duplicate cannot pay again "+point.Id);
                    check(Commit(session,duplicate,out error)&&session.Progress.ledger.currency==loaded.ledger.currency,"duplicate commit remains idempotent "+point.Id);
                }
                var overflow=WorldMacroProgress.CreateNew("test",Vector3.zero,0);overflow.ledger.currency=int.MaxValue;
                var reward=new PrologueContentSO.Point{Id="overflow",Kind=PrologueInteractionKind.Currency,Currency=1};
                check(!Prepare(overflow,reward,out _,out _,out _)&&!overflow.ledger.completed.Contains("overflow")&&overflow.ledger.currency==int.MaxValue,"currency overflow consumes no pickup");
                var recoverySource=WorldMacroProgress.CreateNew("test",Vector3.zero,0);recoverySource.ledger.currency=9;recoverySource.ledger.dropCurrency=17;recoverySource.ledger.dropPosition=new Vector3(9,8,7);
                string recoveryBefore=JsonUtility.ToJson(recoverySource);
                check(Recovery(recoverySource,out var recovery,out int amount)&&amount==17&&recovery.ledger.currency==26&&recovery.ledger.dropCurrency==0,"currency recovery proposes both wallet and drop together");
                Progress(session,recoverySource);Field(session,"lastSuccessfulSnapshot",null);
                Field(session,"store",new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker,"drop.json"),WorldMacroProgress.Valid));
                check(!Commit(session,recovery,out _)&&JsonUtility.ToJson(recoverySource)==recoveryBefore&&ReferenceEquals(session.Progress,recoverySource),"failed currency recovery leaves drop available");
                check(Recovery(recoverySource,out recovery,out amount)&&amount==17,"failed currency recovery can retry");
                string dropPath=Path.Combine(folder,"drop.json");Field(session,"store",new AtomicJsonStore<WorldMacroProgress>(dropPath,WorldMacroProgress.Valid));
                check(Commit(session,recovery,out _)&&session.Progress.ledger.currency==26&&session.Progress.ledger.dropCurrency==0,"currency recovery publishes only after successful save");
                check(!Recovery(session.Progress,out _,out _),"recovered drop cannot pay twice");
                recoverySource.ledger.currency=int.MaxValue;
                check(!Recovery(recoverySource,out _,out _)&&recoverySource.ledger.dropCurrency==17,"overflow leaves dropped currency intact");
                return JsonUtility.ToJson(result,true);
            }
            finally
            {
                // OnDisable must not write even the diagnostic slot during test teardown.
                Field(session,"ready",false);UnityEngine.Object.DestroyImmediate(host);
                string parent=Path.GetDirectoryName(folder).TrimEnd(Path.DirectorySeparatorChar);
                if(string.Equals(parent,Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(folder).StartsWith("OheangbuInteractionChecks_",StringComparison.Ordinal))
                    Directory.Delete(folder,true);
            }
        }
    }
}
