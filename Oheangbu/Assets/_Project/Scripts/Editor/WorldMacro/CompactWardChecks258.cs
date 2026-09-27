using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string WardBuild258()
  {
   var scene=FrontageScene249();var session=VillageSession();
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Continuation258";
   Directory.CreateDirectory(folder);string path=folder+"/EAWards_TEST.asset";
   var profile=AssetDatabase.LoadAssetAtPath<EAWardProfileSO>(path);
   if(profile==null){profile=ScriptableObject.CreateInstance<EAWardProfileSO>();AssetDatabase.CreateAsset(profile,path);}
   var visuals=AssetDatabase.LoadAssetAtPath<SpellVisualSetSO>("Assets/_Project/Art/SpellVFX120/Data/SpellVisualSet_120.asset");
   profile.PresentationPrefabs="구누무수우".Select(c=>{if(!visuals.TryGet(c,out var entry)||entry.FxPrefab==null)throw new Exception("Missing ward "+c);return entry.FxPrefab;}).ToArray();
   EditorUtility.SetDirty(profile);session.EAWardProfile=profile;EditorUtility.SetDirty(session);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "Candidate fixed wards connected, available before giyeok unlock; shared book unchanged";
  }
  public static string WardChecks258()
  {
   JourneySession258();var results=new List<string>();var objects=new List<Object>();
   void Check(bool ok,string message){results.Add((ok?"PASS ":"FAIL ")+message);File.WriteAllText(ContinuationOutput258+"/ward-contracts.txt",string.Join("\n",results));if(!ok)throw new Exception(message);}
   var profile=ScriptableObject.CreateInstance<EAWardProfileSO>();objects.Add(profile);
   var buffs=ScriptableObject.CreateInstance<EABuffProfileSO>();objects.Add(buffs);
   var book=AssetDatabase.LoadAssetAtPath<SpellBookSO>("Assets/_Project/Data/World/SpellBook_WorldMacroSummon_TEST.asset");var resolver=new SpellResolver(book);
   var player=new GameObject("Ward258TestPlayer");objects.Add(player);player.transform.position=new Vector3(-10000,500,-10000);
   var hp=player.AddComponent<PlayerVitals>();var wiring=player.AddComponent<CombatLoopWiring>();float now=10;
   var centre=player.transform.position;var ward=new EAWardRuntime(profile,hp,wiring,()=>now);
   var buff=new EABuffRuntime(buffs,hp,wiring,()=>true,now,()=>now);
   float Damage(IncomingDamageKind kind){hp.Restore();hp.TakeAttackDamage(20,kind);return (1-hp.Hp01)*hp.MaxHp;}
   try
   {
    foreach(char letter in "구누무수우")
    {
     var drawn=new DrawnLetter(letter,default,default,null,.6f,.6f,1,1,3);
     Check(!resolver.TryResolve(drawn,out _,false,true),"legacy/buff flag alone does not enable ward "+letter);
     Check(resolver.TryResolve(drawn,out var cast,false,false,true)&&cast.Kind==SpellKind.Ward,"ward needs no final-consonant unlock "+letter);
     Check(ward.Activate(letter,centre,now),"fixed area accepts "+letter);
    }
    Check(Mathf.Abs(Damage(IncomingDamageKind.ElementalRanged)-20)<.001f,"forming barrier has no invisible early protection");now+=.5f;
    Check(Mathf.Abs(Damage(IncomingDamageKind.ElementalRanged)-8)<.001f,"active ward reduces elemental ranged damage by 60% exactly once");
    Check(Mathf.Abs(Damage(IncomingDamageKind.ElementalMelee)-8)<.001f,"elemental contact shares area protection");
    Check(Mathf.Abs(Damage(IncomingDamageKind.Melee)-20)<.001f&&Mathf.Abs(Damage(IncomingDamageKind.Ranged)-20)<.001f,"neutral melee/ranged bypass ward");
    Check(Mathf.Abs(Damage(IncomingDamageKind.Unspecified)-20)<.001f,"legacy/environment damage not silently classified elemental");
    player.transform.position=centre+Vector3.right*(profile.Radius+.1f);Check(!ward.Contains(player.transform.position,now),"leaving circle loses protection");
    Check(ward.Centre==centre,"walking never moves ward anchor");player.transform.position=centre+Vector3.up*(profile.Height+.1f);
    Check(!ward.Contains(player.transform.position,now),"floor above/below cannot borrow ward");player.transform.position=centre+Vector3.right;
    var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(wall);wall.transform.position=centre+new Vector3(.5f,.6f,0);wall.transform.localScale=new Vector3(.15f,3,3);Physics.SyncTransforms();
    Check(!ward.Contains(player.transform.position,now),"wall through circle blocks protection in adjacent room");wall.SetActive(false);player.transform.position=centre;
    buff.Activate('먹',now);Check(Mathf.Abs(Damage(IncomingDamageKind.ElementalRanged)-6.8f)<.001f,"ward and ranged buff multiply remaining damage once");
    Check(Mathf.Abs(Damage(IncomingDamageKind.ElementalMelee)-8)<.001f,"ranged buff does not amplify melee protection");
    ward.Clear();Check(Mathf.Abs(Damage(IncomingDamageKind.ElementalRanged)-17)<.001f,"clearing ward retains separate ranged buff");
    ward.Activate('구',centre,now);now+=profile.Duration-profile.Fade;
    Check(!ward.Active(now)&&ward.Exists(now)&&Mathf.Abs(Damage(IncomingDamageKind.ElementalRanged)-17)<.001f,"fade has no lingering invisible defense");
    now+=profile.Fade;ward.Tick(now);Check(!ward.Exists(now)&&ward.Letter==default,"expiration clears ward ownership");
    ward.Activate('무',centre,now);now+=.5f;ward.Dispose();
    Check(Mathf.Abs(Damage(IncomingDamageKind.ElementalRanged)-17)<.001f,"disposing ward removes only its modifier");
    buff.Dispose();Check(Mathf.Abs(Damage(IncomingDamageKind.ElementalRanged)-20)<.001f,"disposing both restores original damage");
    return string.Join("\n",results);
   }
   finally{ward.Dispose();buff.Dispose();foreach(var obj in objects)if(obj!=null)Object.DestroyImmediate(obj);}
  }
  public static string WardLive258(string command)
  {
   var s=JourneySession258();var w=s.Walker.Wiring;var ward=w.EAWards;
   void Check(bool ok,string message){File.AppendAllText(ContinuationOutput258+"/ward-live.txt",(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message);}
   Check(ward!=null,"candidate owns ward runtime");
   var ink=(InkPool)typeof(WorldMacroPlaytestSession).GetField("ink",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s);
   var judge=(ParryJudge)typeof(CombatLoopWiring).GetField("_judge",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(w);
   void Cast(char c)=>typeof(CombatLoopWiring).GetMethod("OnLetterDrawn",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,new object[]{new DrawnLetter(c,default,default,null,.6f,.6f,1,1,3)});
   var ui=PlaytestUiRoot.Instance;ui.CloseMenu();ui.Gate.ReleaseImmediately();
   if(command.StartsWith("show:")&&command.Length==6){ink.Restore();Cast(command[5]);return "Ward "+ward.Letter+" anchored at "+ward.Centre;}
   if(command=="reload"){Check(!ward.Exists(Time.time),"restart does not persist temporary ward");return "Ward restart checked";}
   if(command!="check")throw new ArgumentException(command);
   Check(!s.HasDemoGuk,"fresh candidate has not unlocked final consonants");
   uint revision=judge!=null?judge.GuardRevision:0;int accepted=0;void Accepted(SpellCast c,Vector3 p,Vector3 f){if(c.Kind==SpellKind.Ward)accepted++;}
   w.CastAccepted+=Accepted;
   try
   {
    ink.Restore();
    foreach(char c in "구누무수우"){float before=ink.Value;Cast(c);Check(ward.Letter==c&&ward.Exists(Time.time)&&ink.Value<before,"central cost and anchored ward "+c);Check(ward.HasVisual,"world anchored VFX "+c);}
    Check(accepted==5,"one accepted event per successful ward");
    Check(judge!=null&&judge.GuardRevision==revision,"ward casting never raises or changes parry guard");
    var last=ward.Letter;var centre=ward.Centre;ink.Restore(0);Cast('구');
    Check(ward.Letter==last&&ward.Centre==centre&&accepted==5,"insufficient ink leaves prior ward and has no accepted event");
    Check(ApproachVillage("village_rest"),"door reached for ward transaction test");ink.Restore();Cast('구');
    string save=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json");
    using(var locked=new FileStream(save+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
    {Check(!s.Interact("village_rest")&&ward.Exists(Time.time),"failed rest save preserves ward");}
    Check(s.Interact("village_rest")&&!ward.Exists(Time.time)&&!ward.HasVisual,"successful door rest clears ward after save");
    return "Ward live lifecycle checked; rest animation in progress";
   }
   finally{w.CastAccepted-=Accepted;}
  }
 }
}
