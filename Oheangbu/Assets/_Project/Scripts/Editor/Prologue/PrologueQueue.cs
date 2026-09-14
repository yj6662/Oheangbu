using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Oheangbu.EditorTools.Prologue
{
 [InitializeOnLoad] public static class PrologueQueue
 {
  [Serializable]class Request{public string id,method;}
  [Serializable]class Response{public string status,result,error;}
  static double next;
  static PrologueQueue(){EditorApplication.update+=Tick;}
  static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.25;
   string folder=PrologueBuilder.Output,path=folder+"/command.json";if(!File.Exists(path))return;var r=JsonUtility.FromJson<Request>(File.ReadAllText(path));if(r==null||string.IsNullOrEmpty(r.id))return;
   File.Move(path,folder+"/request_"+r.id+".json");var reply=new Response();try{switch(r.method){
    case "Walk":reply.result=ProloguePlayAudit.Begin();break;
    case "CombatTest":reply.result=ProloguePlayAudit.Combat();break;
    case "SpellPoll":reply.result=Vfx120PlayerInputAudit.Poll();break;
    case "WalkPoll":reply.result=ProloguePlayAudit.Poll();break;
    case "State":reply.result=ProloguePlayAudit.State();break;
    case "StateTests":reply.result=ProloguePlayAudit.StateTests();break;
    case "Finalize":reply.result=PrologueAudit.FinalizeScene();break;
    case "Refine":reply.result=PrologueSceneRefinement.Apply();break;
    case "Resume":reply.result=PrologueFinalChecks.Resume();break;
    case "ResumeCheck":reply.result=PrologueFinalChecks.CheckResumeAndWalls();break;
    case "Benchmark":reply.result=PrologueFinalChecks.Benchmark();break;
    case "BenchmarkProduction":reply.result=PrologueFinalChecks.Benchmark(true);break;
    case "Build":reply.result=PrologueBuilder.Build();break;case "Audit":reply.result=PrologueAudit.Audit();break;case "Capture":reply.result=PrologueAudit.Capture();break;case "Probe":reply.result=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path+" playing="+EditorApplication.isPlaying;break;case "Play":EditorApplication.isPlaying=true;reply.result="Starting";break;case "Stop":EditorApplication.isPlaying=false;reply.result="Stopping";break;default:throw new Exception("Unknown method "+r.method);}reply.status="COMPLETE";}catch(Exception e){reply.status="FAILED";reply.error=e.ToString();}
   File.WriteAllText(folder+"/response_"+r.id+".json",JsonUtility.ToJson(reply,true));
  }
 }
}
