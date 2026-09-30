using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 promotion (SPEC-WORLD-FINISH-297 §승격): one main scene with every candidate pass applied, loaded from the lobby.
 //  merge298-prepare — back up the #298 candidate scene + its private content/layout clones, then re-seed the #298 scene
 //                     from the saved #297 candidate so CompactFolklore298 `build` re-applies the enemies on the #297 world
 //                     (its build is deterministic and replaces only its own generated actors).
 //  main-create      — copy the merged scene to Scenes/World/W_Demo_Main with its own content/layout clones and save slot.
 //  lobby-connect    — point the lobby's PlaytestUiRoot at the main scene and add it to the build list (originals recorded);
 //  lobby-revert     — restore the lobby fields and the build list.
 public static partial class CompactRebuildAuthoring
 {
  const string Main297="Assets/_Project/Scenes/World/W_Demo_Main.unity",MainData297="Assets/_Project/Scenes/World/Main",MainSlot297="world-main";
  const string Lobby297="Assets/_Project/Art/UI/Loading270/W_Compact_Lobby.unity",Candidate297="Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
  [Serializable] class LobbyOriginal297{public string playScene,content,sheet,map;public string[] buildScenes;public bool[] buildEnabled;}
  static string Abs297(string assetPath)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));

  static string Merge298Prepare297()
  {
   RequireClean292();
   if(SceneManager.GetActiveScene().path!=Candidate297)throw new Exception("open the saved #297 candidate first");
   string scene=CompactFolklore298.ScenePath,data=CompactFolklore298.AssetRoot+"/Data/";
   var assets=new[]{scene,data+"WorldContent298.asset",data+"WorldLayout298.asset"};
   string backup=Path.Combine(CompactFolklore298.OutputRoot,"Backup","pre-merge297-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(backup);
   foreach(var a in assets){if(!File.Exists(Abs297(a)))continue;File.Copy(Abs297(a),Path.Combine(backup,Path.GetFileName(a)));File.Copy(Abs297(a)+".meta",Path.Combine(backup,Path.GetFileName(a)+".meta"));}
   // the private clones are re-made from the #297 content/layout by CompactFolklore298.OpenCandidate (DataAsset creates when missing)
   foreach(var a in assets)if(File.Exists(Abs297(a))&&!AssetDatabase.DeleteAsset(a))throw new Exception("cannot delete "+a);
   if(!AssetDatabase.CopyAsset(Candidate297,scene))throw new Exception("cannot copy the #297 candidate to "+scene);
   AssetDatabase.SaveAssets();
   return "backed up to "+backup+"; #298 scene re-seeded from the #297 candidate. Next: CompactFolklore298 Run build, ApplyReadability298, AudioImport audit, FolkloreRuntimeChecks298.";
  }

  // rebinds every serialized reference from `from` to `to` in the open scene's MonoBehaviours
  static int Rebind297(Object from,Object to)
  {
   int n=0;if(from==null||from==to)return 0;
   foreach(var c in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c!=null))
   {
    var so=new SerializedObject(c);var p=so.GetIterator();bool changed=false;
    while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue==from){p.objectReferenceValue=to;changed=true;n++;}
    if(changed){so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(c);}
   }
   return n;
  }

  static string MainCreate297(bool replace=false)
  {
   RequireClean292();string merged=CompactFolklore298.ScenePath;WorldMacroPlaytestSO prev306=null;
   try
   {
    if(File.Exists(Abs297(Main297)))
    {
     if(!replace)throw new Exception(Main297+" exists — use main-recreate to replace it deliberately");
     if(SceneManager.GetActiveScene().path==Main297)EditorSceneManager.OpenScene(merged);
     // #306 dialogue data exists only on this clone (Dialogue306Migration writes WorldContent_Main): an in-memory copy outlives the delete
     var prevAsset=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(MainData297+"/WorldContent_Main.asset");
     if(prevAsset!=null){prev306=Object.Instantiate(prevAsset);prev306.hideFlags=HideFlags.HideAndDontSave;}
     // re-promotion: the previous main scene and its private data are replaced (the lobby is re-pointed by lobby-connect)
     foreach(var a in new[]{Main297,MainData297+"/WorldContent_Main.asset",MainData297+"/WorldLayout_Main.asset"})if(File.Exists(Abs297(a))&&!AssetDatabase.DeleteAsset(a))throw new Exception("cannot delete "+a);
    }
    DevSceneKit.EnsureFolder(MainData297);
    if(!AssetDatabase.CopyAsset(merged,Main297))throw new Exception("cannot copy "+merged);
    EditorSceneManager.OpenScene(Main297);
    var s=Session292();var oldContent=s.Content;var oldLayout=s.MountainLayout;
    var content=Object.Instantiate(oldContent);content.name="WorldContent_Main";content.SaveSlot=MainSlot297;string carried=Carry306(prev306,content);AssetDatabase.CreateAsset(content,MainData297+"/WorldContent_Main.asset");
    var layout=Object.Instantiate(oldLayout);layout.name="WorldLayout_Main";AssetDatabase.CreateAsset(layout,MainData297+"/WorldLayout_Main.asset");
    int rc=Rebind297(oldContent,content),rl=Rebind297(oldLayout,layout);
    s.TestSaveSuffix="";EditorUtility.SetDirty(s);
    var roots=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).ToArray();
    foreach(var r in roots){r.PlaySceneName=Path.GetFileNameWithoutExtension(Main297);r.TitleSceneName=Path.GetFileNameWithoutExtension(Lobby297);EditorUtility.SetDirty(r);}
    AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    Dialogue306Migration.NoteRecreated306(MainData297+"/WorldContent_Main.asset");   // a new generation: its revert never restores the old clone's backup
    return "created "+Main297+" from "+merged+": content refs "+rc+", layout refs "+rl+" rebound; slot="+content.SaveSlot+"; ui roots="+roots.Length+" (play="+Path.GetFileNameWithoutExtension(Main297)+"); "+carried;
   }
   finally{if(prev306!=null)Object.DestroyImmediate(prev306);}
  }

  // #306 carry (SPEC-PLAYTEST-306 #3): the dialogue fields written on the previous Main clone go back by id where the fresh clone is empty
  // (points: Speaker / Lines / Services; commissions: Speaker / Title / *Lines / Services; the #306 content settings only while the fresh
  // value is the default). Texts are not carried, so candidate edits win; Dialogue306Migration apply then strips the prefixes still left.
  static string Carry306(WorldMacroPlaytestSO prev,WorldMacroPlaytestSO next)
  {
   if(prev==null)return "#306 carry: no previous WorldContent_Main";
   int points=0,commissions=0,settings=0;var lost=new List<string>();
   var nextPoints=next.Points??Array.Empty<PrologueContentSO.Point>();
   foreach(var p in prev.Points??Array.Empty<PrologueContentSO.Point>())
   {
    if(p==null||string.IsNullOrEmpty(p.Id)||Empty306(p.Speaker)&&Empty306(p.Lines)&&Empty306(p.Services))continue;
    var q=Array.Find(nextPoints,x=>x!=null&&x.Id==p.Id);if(q==null){lost.Add(p.Id);continue;}
    q.Speaker=Keep306(q.Speaker,p.Speaker,ref points);q.Lines=Keep306(q.Lines,p.Lines,ref points);q.Services=Keep306(q.Services,p.Services,ref points);
   }
   var nextSpecs=next.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>();
   foreach(var c in prev.Commissions??Array.Empty<WorldMacroPlaytestSO.CommissionSpec>())
   {
    if(c==null||string.IsNullOrEmpty(c.Id))continue;
    var q=Array.Find(nextSpecs,x=>x!=null&&x.Id==c.Id);
    if(q==null){if(!Empty306(c.Speaker)||!Empty306(c.Title)||!Empty306(c.Services))lost.Add("commission:"+c.Id);continue;}
    q.Speaker=Keep306(q.Speaker,c.Speaker,ref commissions);q.Title=Keep306(q.Title,c.Title,ref commissions);
    q.OfferLines=Keep306(q.OfferLines,c.OfferLines,ref commissions);q.WaitingLines=Keep306(q.WaitingLines,c.WaitingLines,ref commissions);
    q.ReportLines=Keep306(q.ReportLines,c.ReportLines,ref commissions);q.CompletedLines=Keep306(q.CompletedLines,c.CompletedLines,ref commissions);
    q.AcceptedLines=Keep306(q.AcceptedLines,c.AcceptedLines,ref commissions);q.DeclinedLines=Keep306(q.DeclinedLines,c.DeclinedLines,ref commissions);
    q.Services=Keep306(q.Services,c.Services,ref commissions);
   }
   var d=ScriptableObject.CreateInstance<WorldMacroPlaytestSO>();
   try
   {
    if(Same306(next.DialogueLabels,d.DialogueLabels)&&!Same306(prev.DialogueLabels,d.DialogueLabels)){next.DialogueLabels=JsonUtility.FromJson<WorldMacroPlaytestSO.DialogueLabels306>(JsonUtility.ToJson(prev.DialogueLabels));settings++;}
    if(Same306(next.EscortVoice,d.EscortVoice)&&!Same306(prev.EscortVoice,d.EscortVoice)){next.EscortVoice=JsonUtility.FromJson<WorldMacroPlaytestSO.EscortVoice306>(JsonUtility.ToJson(prev.EscortVoice));settings++;}
    if(next.DialoguePageMaxChars==d.DialoguePageMaxChars&&prev.DialoguePageMaxChars!=d.DialoguePageMaxChars){next.DialoguePageMaxChars=prev.DialoguePageMaxChars;settings++;}
    if(next.DialogueRestReachMeters==d.DialogueRestReachMeters&&prev.DialogueRestReachMeters!=d.DialogueRestReachMeters){next.DialogueRestReachMeters=prev.DialogueRestReachMeters;settings++;}
    if(next.InteractRepeatCooldownSeconds==d.InteractRepeatCooldownSeconds&&prev.InteractRepeatCooldownSeconds!=d.InteractRepeatCooldownSeconds){next.InteractRepeatCooldownSeconds=prev.InteractRepeatCooldownSeconds;settings++;}
    if(next.NaturalSolidProbeReuseMeters==d.NaturalSolidProbeReuseMeters&&prev.NaturalSolidProbeReuseMeters!=d.NaturalSolidProbeReuseMeters){next.NaturalSolidProbeReuseMeters=prev.NaturalSolidProbeReuseMeters;settings++;}
   }
   finally{Object.DestroyImmediate(d);}
   return "#306 carried "+(points+commissions+settings)+" fields (points "+points+", commissions "+commissions+", settings "+settings+")"+
    (lost.Count>0?"; NOT CARRIED (id gone from the candidate): "+string.Join(", ",lost):"")+" - run Dialogue306Migration dry/apply to strip the prefixes still left";
  }
  static bool Empty306(string s)=>string.IsNullOrWhiteSpace(s);
  static bool Empty306<T>(T[] a)=>a==null||a.Length==0;
  static string Keep306(string fresh,string old,ref int n){if(!Empty306(fresh)||Empty306(old))return fresh;n++;return old;}
  static T[] Keep306<T>(T[] fresh,T[] old,ref int n){if(!Empty306(fresh)||Empty306(old))return fresh;n++;return old;}
  static bool Same306(object a,object b)=>a==null||b==null?a==b:JsonUtility.ToJson(a)==JsonUtility.ToJson(b);

  static string LobbyConnect297(bool revert)
  {
   RequireClean292();string file=K297+"/Promote/lobby-original.json";
   EditorSceneManager.OpenScene(Lobby297);
   var root=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
   var scenes=EditorBuildSettings.scenes.ToList();
   if(revert)
   {
    var o=JsonUtility.FromJson<LobbyOriginal297>(File.ReadAllText(file));
    root.PlaySceneName=o.playScene;root.Content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(o.content);
    root.WorldSheet=AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(o.sheet);root.MapData=AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(o.map);
    EditorBuildSettings.scenes=o.buildScenes.Select((p,i)=>new EditorBuildSettingsScene(p,o.buildEnabled[i])).ToArray();
   }
   else
   {
    if(!File.Exists(Abs297(Main297)))throw new Exception("main-create first");
    if(!File.Exists(file)){Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,JsonUtility.ToJson(new LobbyOriginal297{playScene=root.PlaySceneName,content=AssetDatabase.GetAssetPath(root.Content),
     sheet=AssetDatabase.GetAssetPath(root.WorldSheet),map=AssetDatabase.GetAssetPath(root.MapData),buildScenes=scenes.Select(x=>x.path).ToArray(),buildEnabled=scenes.Select(x=>x.enabled).ToArray()},true));}
    // lobby fields follow the main scene's own UI root (same content = same save slot, same map/sheet)
    var main=EditorSceneManager.OpenScene(Main297,OpenSceneMode.Additive);
    try
    {
     var mainRoot=main.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).First();
     var mainSession=main.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
     // the in-game UI root keeps whatever content the #296 copy had; align it with the session (slot name, start feet)
     string priorUi=AssetDatabase.GetAssetPath(mainRoot.Content);
     if(mainRoot.Content!=mainSession.Content){mainRoot.Content=mainSession.Content;EditorUtility.SetDirty(mainRoot);EditorSceneManager.MarkSceneDirty(main);EditorSceneManager.SaveScene(main);}
     Debug.Log("[#297 promote] main UI root content "+priorUi+" -> "+AssetDatabase.GetAssetPath(mainRoot.Content));
     root.PlaySceneName=Path.GetFileNameWithoutExtension(Main297);root.Content=mainRoot.Content;root.WorldSheet=mainRoot.WorldSheet;root.MapData=mainRoot.MapData;
    }
    finally{EditorSceneManager.CloseScene(main,true);}
    if(!scenes.Any(x=>x.path==Main297)){int at=scenes.FindIndex(x=>x.path==Lobby297);scenes.Insert(at+1,new EditorBuildSettingsScene(Main297,true));}
    EditorBuildSettings.scenes=scenes.ToArray();
   }
   EditorUtility.SetDirty(root);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
   return (revert?"lobby restored":"lobby connected")+": play="+root.PlaySceneName+" content="+AssetDatabase.GetAssetPath(root.Content)+" slot="+root.Content?.SaveSlot+"; build scenes="+string.Join(", ",EditorBuildSettings.scenes.Where(x=>x.enabled).Select(x=>Path.GetFileNameWithoutExtension(x.path)));
  }
 }
}
