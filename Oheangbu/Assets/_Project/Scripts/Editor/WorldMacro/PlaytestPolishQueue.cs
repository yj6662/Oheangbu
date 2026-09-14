using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    [InitializeOnLoad]
    public static class PlaytestPolishQueue
    {
        [Serializable]sealed class Request {public string id,type,method,argument;}
        [Serializable]sealed class Reply {public string id,status,result,error;}
        static double next;
        static string Folder=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestPolish/Unity"));
        static PlaytestPolishQueue(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<next)return;
            next=EditorApplication.timeSinceStartup+.3;
            string path=Path.Combine(Folder,"command.json");if(!File.Exists(path))return;
            var r=JsonUtility.FromJson<Request>(File.ReadAllText(path));if(r==null||string.IsNullOrEmpty(r.id))return;
            File.Move(path,Path.Combine(Folder,"request_"+r.id+".json"));
            var reply=new Reply{id=r.id};
            try
            {
                if(r.type=="editor")reply.result=Editor(r.method);
                else
                {
                    if(!r.type.StartsWith("Oheangbu.EditorTools.",StringComparison.Ordinal))throw new ArgumentException("Only project editor tools are allowed.");
                    var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(r.type,false)).FirstOrDefault(t=>t!=null);
                    if(type==null)throw new TypeLoadException(r.type);
                    var method=type.GetMethod(r.method,BindingFlags.Static|BindingFlags.Public,null,new[]{typeof(string)},null);
                    object result;
                    if(method!=null)result=method.Invoke(null,new object[]{r.argument??""});
                    else
                    {
                        method=type.GetMethod(r.method,BindingFlags.Static|BindingFlags.Public,null,Type.EmptyTypes,null);
                        if(method==null)throw new MissingMethodException(r.type,r.method);
                        result=method.Invoke(null,null);
                    }
                    reply.result=result?.ToString()??"Completed void/no result";
                }
                reply.status="COMPLETE";
            }
            catch(Exception e){reply.status="FAILED";reply.error=(e is TargetInvocationException&&e.InnerException!=null?e.InnerException:e).ToString();}
            File.WriteAllText(Path.Combine(Folder,"response_"+r.id+".json"),JsonUtility.ToJson(reply,true));
        }
        static string Editor(string command)
        {
            switch(command)
            {
                case "status":return JsonUtility.ToJson(new EditorStatus{scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,playing=EditorApplication.isPlaying,dirty=UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty,commit=Prologue.PrologueAudit.CommitRatio()});
                case "open":
                    if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Clean Edit Mode required.");
                    EditorSceneManager.OpenScene("Assets/_Project/Scenes/World/W_WorldMacro_Playtest.unity");return "Playtest opened";
                case "play":EditorApplication.isPlaying=true;return "Play requested";
                case "stop":EditorApplication.isPlaying=false;return "Stop requested";
                case "save":if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();return "Saved";
                case "cleanup":if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();return "Unused assets released";
                default:throw new ArgumentException(command);
            }
        }
        [Serializable]sealed class EditorStatus{public string scene;public bool playing,dirty;public float commit;}
    }
}
