using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #306 속성 기관 저작(SPEC-PLAYTEST-306 #12, PLAN §5 ⑧). Queue-safe: refusals come back as strings, never a dialog. One heavy scene per call.
 //   assets                     halo/stroke materials + TEST timing SO (Resources/Telegraph306) — idempotent; apply runs it first
 //   dry[:prefabs|:<scene>|:all] what apply would add: organ → bone (or FALLBACK root offset), existing components. No writes
 //   apply[:prefabs|:<scene>|:all] backup files first, then EnemyOrganSet + EnemyElementTelegraph on the six Folklore298 species
 //                              prefabs (plain 'apply' = assets + prefabs) or on the boss/elemental actors of one #306 scene; record for revert
 //   revert[:prefabs|:<scene>|:all] removes exactly the components apply added (record); file backups stay for manual restore
 //   fairness                   every elemental attack: telegraph + minimum flight (near distance) vs FairnessMinimum; scenes that use it
 //   timing[:<CombatConfigSO path>]  AC-12a math: guard raised at t_peak and t_peak ± PeakLead (HoldScale 1) against the real ParryJudge
 //                              for every elemental attack at 4/8/12 m (default config = the one the main scene references)
 //   fire-copy                  EncounterExpansion/FireRanged.asset → Art/Telegraph306/FireRanged306.asset (shared profile never edited)
 //   fire-assign:<scene>[|<projectile prefab>]  mine_fire/0 → FireRanged306 + real bolt (default PF_Bolt300_na) + Ranged + PreferredDistance
 //   fire-revert:<scene>        restores the recorded mine_fire/0 fields
 //   status
 public static class TelegraphOrgans306
 {
  static readonly string[] Scenes={"Assets/_Project/Scenes/World/W_Demo_Main.unity","Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity","Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity"};
  static readonly string[] Species={"dokkaebi","agwi","changgui","bulgasari","fox_spirit","imugi"};
  const string PrefabRoot="Assets/_Project/Art/Characters/Folklore298/Prefabs/PF_";
  const string ArtRoot="Assets/_Project/Art/Telegraph306";
  const string TimingPath="Assets/_Project/Resources/Telegraph306/EnemyTelegraphTiming306.asset";
  const string HaloShader="Oheangbu/InkOrganHalo",HaloMat=ArtRoot+"/M_InkOrganHalo306.mat",StrokeMat=ArtRoot+"/M_ParryCounterStroke306.mat";
  const string FireSource="Assets/_Project/Art/Demo/EncounterExpansion/FireRanged.asset",FireCopy=ArtRoot+"/FireRanged306.asset";
  const string DefaultBolt="Assets/_Project/Art/SpellVFX120/Bolt300/PF_Bolt300_na.prefab",FallbackPalette="Assets/_Project/Data/Configs/ElementPalette_Test.asset";
  static string OutRoot=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Playtest306/Telegraph306"));
  static string RecordFile=>Path.Combine(OutRoot,"record.json");static string FireFile=>Path.Combine(OutRoot,"fire-record.json");

  [Serializable] class Entry{public string target,path;public bool organSet,telegraph;}
  [Serializable] class Record{public List<Entry> entries=new List<Entry>();public List<string> backups=new List<string>();}
  [Serializable] class FireEntry{public string scene,profile,projectile;public int mode;public bool ranged;public float preferred;}
  [Serializable] class FireRecord{public List<FireEntry> entries=new List<FireEntry>();}

  // organ recipe: bone by Humanoid slot, then names; Special = derived offsets (eyes from Head→MouthOrigin, blade tip, helmet)
  sealed class Spec{public string Id;public EnemyOrganRole Role;public string[] Keys;public HumanBodyBones Human=HumanBodyBones.LastBone;public string[] Names=new string[0];public string Special;public float Radius;public Vector3 Fallback;}
  static Spec S(string id,EnemyOrganRole role,float radius,string[] keys,HumanBodyBones human,Vector3 fallback,string special,params string[] names)=>new Spec{Id=id,Role=role,Radius=radius,Keys=keys,Human=human,Names=names,Special=special,Fallback=fallback};
  static readonly string[] Any={EnemyOrganSet.KeyAny};
  const HumanBodyBones None=HumanBodyBones.LastBone;

  static List<Spec> SpeciesSpecs(string id)
  {
   var up=new Vector3(0,1.2f,.15f);
   switch(id)
   {
    case "bulgasari": return new List<Spec>{S("back_crystal",EnemyOrganRole.Core,.16f,Any,None,up,null,"Spine","Chest"),S("jaw_crystal",EnemyOrganRole.Mouth,.12f,Any,None,up,null,"MouthOrigin","Head")};
    case "fox_spirit": return new List<Spec>{S("mouth",EnemyOrganRole.Mouth,.1f,Any,None,up,null,"MouthOrigin","Head"),S("chest",EnemyOrganRole.Chest,.12f,Any,None,up,null,"Chest","Spine")};
    case "imugi": return new List<Spec>{S("mouth",EnemyOrganRole.Mouth,.14f,Any,None,up,null,"MouthOrigin","Head"),S("core",EnemyOrganRole.Core,.16f,Any,None,up,null,"Body_12")};
    default: return new List<Spec>{S("chest",EnemyOrganRole.Chest,.14f,Any,HumanBodyBones.Chest,up,null,"Spine02","Spine01"),S("head",EnemyOrganRole.Mouth,.1f,new[]{EnemyOrganSet.KeyProjectile},HumanBodyBones.Head,up,null,"Head","headfront")};
   }
  }
  // scene actors that own elemental attacks (Guardian302 has no combat owner: excluded — no false telegraph)
  static readonly string[] SceneActors={"cheongryong","sinmok263","south_gate_general","demo_growth_lesson","mine_fire/0"};
  static List<Spec> ActorSpecs(string id)
  {
   var up=new Vector3(0,1.2f,.2f);
   switch(id)
   {
    case "cheongryong": case "sinmok263":
    {
     var bolt=new[]{EnemyOrganSet.KeyBolt};var root=new[]{EnemyOrganSet.KeyRoot};
     var l=new List<Spec>{S("mouth",EnemyOrganRole.Mouth,.2f,bolt,None,new Vector3(0,1.1f,1.7f),null,"MouthOrigin","Head"),
      S("eye_l",EnemyOrganRole.Eye,.08f,bolt,None,new Vector3(-.2f,1.3f,1.4f),"eyeL","Head"),S("eye_r",EnemyOrganRole.Eye,.08f,bolt,None,new Vector3(.2f,1.3f,1.4f),"eyeR","Head")};
     for(int i=6;i<=18;i++)l.Add(S("spine_"+i.ToString("00"),EnemyOrganRole.Spine,.16f,root,None,new Vector3(0,.8f,1.2f-(i-6)*.25f),null,"Body_"+i.ToString("00")));
     return l;
    }
    case "south_gate_general":
    {
     var wave=new[]{EnemyOrganSet.KeyWave};
     return new List<Spec>{S("spear_blade",EnemyOrganRole.Weapon,.12f,wave,None,new Vector3(0,1.05f,.65f),"blade","Temporary_ReusedMesh_Polearm"),
      S("chest",EnemyOrganRole.Chest,.14f,wave,HumanBodyBones.Chest,new Vector3(0,1.3f,.15f),null,"Spine02"),S("helmet",EnemyOrganRole.Core,.1f,wave,HumanBodyBones.Head,new Vector3(0,1.8f,.05f),"helmet","Head")};
    }
    default: return new List<Spec>{S("core",EnemyOrganRole.Core,.14f,Any,HumanBodyBones.Chest,up,null,"Spine02","Spine2","Chest","mixamorig:Spine2","Spine1","Spine"),
     S("mouth",EnemyOrganRole.Mouth,.1f,new[]{EnemyOrganSet.KeyProjectile},HumanBodyBones.Head,up,null,"MouthOrigin","Head","head","mixamorig:Head")};
   }
  }

  public static string Run(string c)
  {
   c=(c??"").Trim();
   try
   {
    if(c=="assets")return Assets(true);
    if(c=="status")return Status();
    if(c=="fairness")return Fairness();
    if(c=="timing"||c.StartsWith("timing:",StringComparison.Ordinal))return Timing(c.Length>7?c.Substring(7).Trim():"");
    if(c=="fire-copy")return FireCopyAsset();
    if(c.StartsWith("fire-assign:",StringComparison.Ordinal))return FireAssign(c.Substring(12).Trim());
    if(c.StartsWith("fire-revert:",StringComparison.Ordinal))return FireRevert(c.Substring(12).Trim());
    string verb=c,target="";int colon=c.IndexOf(':');if(colon>0){verb=c.Substring(0,colon);target=c.Substring(colon+1).Trim();}
    if(verb!="dry"&&verb!="apply"&&verb!="revert")return "REFUSED unknown command "+c+" (assets|dry|apply|revert[:prefabs|:<scene>|:all]|fairness|fire-copy|fire-assign:<scene>|fire-revert:<scene>|status)";
    if(verb=="apply"&&target==""){var a=Assets(false);if(a.StartsWith("REFUSED"))return a;return a+"\n"+Prefabs("apply");}
    if(verb!="apply"&&target=="")target="prefabs";
    var sb=new StringBuilder();
    if(verb=="apply"){var a=Assets(false);if(a.StartsWith("REFUSED"))return a;sb.AppendLine(a);}
    if(target=="prefabs"||target=="all")sb.AppendLine(Prefabs(verb));
    if(target=="all")foreach(var s in Scenes)sb.AppendLine(Scene(verb,s));
    else if(target!="prefabs"){if(!Scenes.Contains(target))return "REFUSED scene not in the #306 list: "+target;sb.AppendLine(Scene(verb,target));}
    return sb.ToString().TrimEnd();
   }
   catch(Exception e){return "REFUSED "+e.GetType().Name+": "+e.Message;}
  }

  // ---------------------------------------------------------------- assets
  static string Assets(bool report)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Play mode";
   var halo=Shader.Find(HaloShader);if(halo==null)return "REFUSED shader "+HaloShader+" not imported (copy _assets/_Project/Shaders/InkOrganHalo.shader to Assets/_Project/Shaders first)";
   var ink=Shader.Find("Oheangbu/InkStroke");if(ink==null)return "REFUSED shader Oheangbu/InkStroke missing";
   Folder(ArtRoot);Folder(Path.GetDirectoryName(TimingPath).Replace('\\','/'));
   var lines=new List<string>();
   var hm=AssetDatabase.LoadAssetAtPath<Material>(HaloMat);
   if(hm==null){hm=new Material(halo){name="M_InkOrganHalo306"};AssetDatabase.CreateAsset(hm,HaloMat);lines.Add("created "+HaloMat);}
   var sm=AssetDatabase.LoadAssetAtPath<Material>(StrokeMat);
   if(sm==null){sm=new Material(ink){name="M_ParryCounterStroke306"};sm.SetFloat("_FlashAdd",0f);sm.SetFloat("_FlashTint",.85f);sm.SetFloat("_CrackStrength",.45f);AssetDatabase.CreateAsset(sm,StrokeMat);lines.Add("created "+StrokeMat+" (_FlashAdd 0: LDR tint only)");}
   var t=AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath);
   if(t==null){t=ScriptableObject.CreateInstance<EnemyTelegraphTimingSO>();t.name="EnemyTelegraphTiming306";AssetDatabase.CreateAsset(t,TimingPath);lines.Add("created "+TimingPath+" (TEST defaults)");}
   if(t.HaloMaterial!=hm||t.StrokeMaterial!=sm){t.HaloMaterial=hm;t.StrokeMaterial=sm;EditorUtility.SetDirty(t);lines.Add("timing materials bound");}
   AssetDatabase.SaveAssets();
   return report||lines.Count>0?"assets: "+(lines.Count>0?string.Join("; ",lines):"present")+" | PeakLead="+F(t.PeakLead)+" RiseLead="+F(t.RiseLead)+" MaxBrightness="+F(t.MaxBrightness)+" strokes="+t.MaxLiveStrokes:"assets: present";
  }

  // ---------------------------------------------------------------- prefabs
  static string Prefabs(string verb)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Play mode";
   var rec=Load();var sb=new StringBuilder();var timing=AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath);var palette=PrefabPalette();
   string stamp=DateTime.Now.ToString("yyyyMMddTHHmmss",CultureInfo.InvariantCulture);
   foreach(var id in Species)
   {
    string path=PrefabRoot+id+".prefab";if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null){sb.AppendLine(id+": missing "+path);continue;}
    var root=PrefabUtility.LoadPrefabContents(path);
    try
    {
     var entry=rec.entries.FirstOrDefault(e=>e.target==path&&e.path=="");
     if(verb=="revert"){sb.AppendLine(id+": "+RemoveRecorded(root,entry));if(entry!=null){rec.entries.Remove(entry);PrefabUtility.SaveAsPrefabAsset(root,path);}continue;}
     var existing=root.GetComponent<EnemyOrganSet>();
     if(existing!=null&&entry==null){sb.AppendLine(id+": EnemyOrganSet exists (not ours) — skipped");continue;}
     var organs=Build(root.transform,root.transform,SpeciesSpecs(id),out string how);
     if(verb=="dry"){sb.AppendLine(id+": "+how+(existing!=null?" [ours, would rebuild]":""));continue;}
     Backup(path,stamp,rec);
     if(entry==null){entry=new Entry{target=path,path=""};rec.entries.Add(entry);}
     Attach(root,organs,timing,palette,entry);
     PrefabUtility.SaveAsPrefabAsset(root,path);sb.AppendLine(id+": "+how);
    }
    finally{PrefabUtility.UnloadPrefabContents(root);}
   }
   if(verb!="dry")Save(rec);
   return "prefabs ("+verb+", palette "+(palette!=null?AssetDatabase.GetAssetPath(palette):"none")+"):\n"+sb.ToString().TrimEnd();
  }

  // ---------------------------------------------------------------- scenes
  static string Scene(string verb,string path)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Play mode";
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)return "REFUSED dirty scene "+SceneManager.GetSceneAt(i).path;
   if(!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath,"..",path))))return path+": missing";
   var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
   var rec=Load();var sb=new StringBuilder(path+" ("+verb+"):\n");var timing=AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath);
   var actors=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PrologueEncounter>(true)).ToArray();
   var wiring=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CombatLoopWiring>(true)).FirstOrDefault();
   var palette=wiring!=null?new SerializedObject(wiring).FindProperty("_palette")?.objectReferenceValue as ElementPaletteSO:null;if(palette==null)palette=PrefabPalette();
   string stamp=DateTime.Now.ToString("yyyyMMddTHHmmss",CultureInfo.InvariantCulture);bool changed=false;
   if(verb=="apply")Backup(path,stamp,rec);
   foreach(var id in SceneActors)
   {
    var actor=actors.FirstOrDefault(a=>a.Id==id);if(actor==null){sb.AppendLine("  "+id+": not in scene");continue;}
    string hp=HierarchyPath(actor.transform);var entry=rec.entries.FirstOrDefault(e=>e.target==path&&e.path==hp);
    if(verb=="revert"){sb.AppendLine("  "+id+": "+RemoveRecorded(actor.gameObject,entry));if(entry!=null){rec.entries.Remove(entry);changed=true;}continue;}
    bool hidden=!actor.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled);
    if(hidden){sb.AppendLine("  "+id+": visuals hidden (e.g. DemoFixes297 floating cave) — skipped, no telegraph on an unseen body");continue;}
    var existing=actor.GetComponent<EnemyOrganSet>();
    if(existing!=null&&entry==null){sb.AppendLine("  "+id+": EnemyOrganSet exists (not ours) — skipped");continue;}
    // one telegraph per enemy: a species prefab nested under the actor (Folklore298_Visual) already carries its organ set
    var nested=actor.GetComponentsInChildren<EnemyOrganSet>(true).FirstOrDefault(x=>x.gameObject!=actor.gameObject);
    if(nested!=null&&entry==null){sb.AppendLine("  "+id+": covered by nested "+HierarchyPath(nested.transform)+" (species prefab) — skipped");continue;}
    var visual=actor.transform.Find("Folklore298_Visual");
    var organs=Build(actor.transform,visual!=null?visual:actor.transform,ActorSpecs(id),out string how);
    string owner=actor.GetComponent<CheongryongCombatController>()!=null?"cheongryong-plan":actor.GetComponent<SouthGateGeneralController>()!=null?"southgate-plan":"enemy-controller";
    var enemy=actor.GetComponent<EnemyController>();string profile=enemy!=null&&enemy.AttackProfile!=null?" profile="+enemy.AttackProfile.name+(enemy.AttackProfile.Elemental?"("+enemy.AttackProfile.Element+")":"(neutral: never lit)"):"";
    if(verb=="dry"){sb.AppendLine("  "+id+" ["+owner+profile+"]: "+how+(existing!=null?" [ours, would rebuild]":""));continue;}
    if(entry==null){entry=new Entry{target=path,path=hp};rec.entries.Add(entry);}
    Attach(actor.gameObject,organs,timing,palette,entry);changed=true;
    sb.AppendLine("  "+id+" ["+owner+profile+"]: "+how);
   }
   if(verb!="dry"&&wiring!=null&&timing!=null)
   {
    var so=new SerializedObject(wiring);var p=so.FindProperty("_telegraphTiming306");var want=verb=="apply"?timing:null;
    if(p!=null&&p.objectReferenceValue!=want&&(verb=="apply"||p.objectReferenceValue==timing)){p.objectReferenceValue=want;so.ApplyModifiedPropertiesWithoutUndo();changed=true;sb.AppendLine("  CombatLoopWiring._telegraphTiming306 = "+(want!=null?TimingPath:"none (Resources fallback)"));}
   }
   if(verb!="dry"&&changed){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Save(rec);}
   return sb.ToString().TrimEnd();
  }

  // ---------------------------------------------------------------- organ building
  static EnemyOrganSet.Organ[] Build(Transform owner,Transform visual,List<Spec> specs,out string how)
  {
   var bones=visual.GetComponentsInChildren<Transform>(true);
   var animator=visual.GetComponentsInChildren<Animator>(true).Where(a=>a.avatar!=null&&a.avatar.isHuman).OrderByDescending(a=>a.enabled&&a.gameObject.activeInHierarchy).FirstOrDefault();
   var renderers=visual.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&!(r is ParticleSystemRenderer)).ToArray();
   float height=1.8f;if(renderers.Length>0){var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);height=b.size.y;}
   float scale=Mathf.Clamp(height/1.8f,.5f,4f);
   var list=new List<EnemyOrganSet.Organ>();var notes=new List<string>();
   Transform Bone(string name)=>bones.Where(t=>t.name==name).OrderByDescending(t=>t.gameObject.activeInHierarchy).FirstOrDefault();
   foreach(var s in specs)
   {
    Transform anchor=null;string src=null;
    if(s.Human!=HumanBodyBones.LastBone&&animator!=null){anchor=animator.GetBoneTransform(s.Human);if(anchor!=null)src="human:"+s.Human;}
    if(anchor==null)foreach(var n in s.Names){anchor=Bone(n);if(anchor!=null){src=n;break;}}
    var organ=new EnemyOrganSet.Organ{Id=s.Id,Role=s.Role,AttackKeys=s.Keys,Radius=s.Radius*scale,HumanBone=s.Human,BoneName=s.Names.FirstOrDefault()};
    if(anchor==null){organ.Anchor=owner;organ.LocalOffset=Vector3.Scale(s.Fallback,new Vector3(1,scale,1));notes.Add(s.Id+"=FALLBACK root"+organ.LocalOffset.ToString("F2"));list.Add(organ);continue;}
    organ.Anchor=anchor;
    if(s.Special=="eyeL"||s.Special=="eyeR")
    {
     var mouth=Bone("MouthOrigin");Vector3 f=mouth!=null?mouth.position-anchor.position:owner.forward*.3f*scale;float L=Mathf.Max(.05f,f.magnitude);
     if(f.sqrMagnitude<.0025f)f=owner.forward*L;var right=Vector3.Cross(Vector3.up,f).normalized;if(right.sqrMagnitude<.5f)right=owner.right;var up=Vector3.Cross(f,right).normalized;
     var eye=anchor.position+f*.55f+up*(.35f*L)+right*((s.Special=="eyeL"?-.35f:.35f)*L);
     organ.LocalOffset=anchor.InverseTransformPoint(eye);organ.Radius=Mathf.Clamp(.22f*L,.04f,.3f);src+="+eye";
    }
    else if(s.Special=="blade")
    {
     // polearm authored along local +Y: blade = near the far end of the meshes' extent on that axis
     float min=float.PositiveInfinity,max=float.NegativeInfinity;Vector3 c=Vector3.zero;int k=0;
     foreach(var mf in anchor.GetComponentsInChildren<MeshFilter>(true))
     {
      if(mf.sharedMesh==null)continue;var mb=mf.sharedMesh.bounds;
      for(int i=0;i<8;i++){var p=anchor.InverseTransformPoint(mf.transform.TransformPoint(mb.center+Vector3.Scale(mb.extents,new Vector3((i&1)*2-1,((i>>1)&1)*2-1,((i>>2)&1)*2-1))));min=Mathf.Min(min,p.y);max=Mathf.Max(max,p.y);c+=p;k++;}
     }
     if(k>0){c/=k;organ.LocalOffset=new Vector3(c.x,max-(max-min)*.07f,c.z);src+="+tip";}
    }
    else if(s.Special=="helmet"){organ.LocalOffset=anchor.InverseTransformPoint(anchor.position+Vector3.up*.12f*scale);src+="+crest";}
    notes.Add(s.Id+"="+src);list.Add(organ);
   }
   how=list.Count+" organs (scale "+F(scale)+", humanoid "+(animator!=null)+"): "+string.Join(", ",notes);
   return list.ToArray();
  }

  static void Attach(GameObject go,EnemyOrganSet.Organ[] organs,EnemyTelegraphTimingSO timing,ElementPaletteSO palette,Entry entry)
  {
   var set=go.GetComponent<EnemyOrganSet>();if(set==null){set=go.AddComponent<EnemyOrganSet>();entry.organSet=true;}
   set.Organs=organs;EditorUtility.SetDirty(set);
   var tel=go.GetComponent<EnemyElementTelegraph>();if(tel==null){tel=go.AddComponent<EnemyElementTelegraph>();entry.telegraph=true;}
   var so=new SerializedObject(tel);so.FindProperty("_organs").objectReferenceValue=set;so.FindProperty("_timing").objectReferenceValue=timing;so.FindProperty("_palette").objectReferenceValue=palette;
   so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(tel);
  }

  static string RemoveRecorded(GameObject go,Entry entry)
  {
   if(entry==null)return "nothing recorded";
   var parts=new List<string>();
   if(entry.telegraph){var t=go.GetComponent<EnemyElementTelegraph>();if(t!=null){Object.DestroyImmediate(t,true);parts.Add("telegraph removed");}}
   if(entry.organSet){var s=go.GetComponent<EnemyOrganSet>();if(s!=null){Object.DestroyImmediate(s,true);parts.Add("organ set removed");}}
   return parts.Count>0?string.Join(", ",parts):"recorded components already gone";
  }

  // ---------------------------------------------------------------- fairness
  static string Fairness()
  {
   var t=AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath)??ScriptableObject.CreateInstance<EnemyTelegraphTimingSO>();
   float need=t.FairnessMinimum,near=t.FairnessNearDistance;
   var attacks=Find<EnemyAttackProfileSO>().Where(p=>p.Elemental).ToArray();var configs=Find<CombatConfigSO>().ToArray();
   var dragons=Find<CheongryongCombatProfile>().ToArray();var generals=Find<SouthGateGeneralProfile>().ToArray();
   var usage=ScanScenes(new HashSet<string>(attacks.Cast<Object>().Concat(configs).Concat(dragons).Concat(generals).Select(o=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o)))));
   string Used(Object o){return usage.TryGetValue(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o)),out var u)?" used in "+string.Join(",",u):" (not referenced by the #306 scenes)";}
   var sb=new StringBuilder("fairness: telegraph + minimum flight (near "+F(near)+" m) must be >= "+F(need)+" s; lit onset = t_peak - "+F(t.RiseLead)+" (t_peak = impact - "+F(t.PeakLead)+")\n");
   int fails=0;
   void Row(string name,float telegraph,float flight,string where,string fix)
   {
    float total=telegraph+flight;float peak=total-t.PeakLead,on=Mathf.Max(0,peak-t.RiseLead);bool ok=total>=need-1e-4f;if(!ok)fails++;
    sb.AppendLine((ok?"  PASS ":"  FAIL ")+name+": telegraph "+F(telegraph)+" + flight "+F(flight)+" = "+F(total)+" s (peak at "+F(peak)+", lit from "+F(on)+")"+where+(ok?"":" -> "+fix));
   }
   foreach(var p in attacks)
   {
    bool proj=p.Delivery==EnemyAttackDelivery.HomingProjectile||p.Delivery==EnemyAttackDelivery.AimedProjectile;
    float flight=proj?Mathf.Max(.05f,near/p.ProjectileSpeed):0f;float need2=need-flight;
    Row(AssetDatabase.GetAssetPath(p)+" "+p.Archetype+"/"+p.Delivery+"/"+p.Element,p.Telegraph,flight,Used(p),
     "data fix: Telegraph "+F(p.Telegraph)+" -> "+F(Mathf.Ceil(need2*100)/100)+(AssetDatabase.GetAssetPath(p).Contains("/EncounterExpansion/")?" in a #306 copy (shared EncounterExpansion profile: copy, then reassign)":""));
   }
   foreach(var c in configs)
    Row(AssetDatabase.GetAssetPath(c)+" legacy ranged (fixed flight)",c.RangedTelegraph,c.ProjectileFlight,Used(c),"data fix: RangedTelegraph -> "+F(Mathf.Ceil((need-c.ProjectileFlight)*100)/100)+" in a copy");
   foreach(var d in dragons)
   {
    Row(AssetDatabase.GetAssetPath(d)+" bolt",d.ProjectileWindup,Mathf.Max(0,near-d.ProjectileRadius)/d.ProjectileSpeed,Used(d),"data fix: ProjectileWindup up");
    Row(AssetDatabase.GetAssetPath(d)+" root eruption",d.RootWindup,0,Used(d),"data fix: RootWindup -> "+F(need));
   }
   foreach(var g in generals)
   {
    float flight=Mathf.Max(0,near-g.WaveWidth)/g.WaveSpeed;
    Row(AssetDatabase.GetAssetPath(g)+" earth shockwave",g.WaveWindup,flight,Used(g),"data fix: WaveWindup up");
    Row(AssetDatabase.GetAssetPath(g)+" neutral+earth combo (from EmpowerStartAt)",g.ComboEmpowerSeconds,flight,Used(g),"data fix: ComboEmpowerSeconds up");
   }
   sb.Append("fails="+fails+" (non-elemental attacks never glow and are not listed; Guardian302 excluded)");
   return sb.ToString();
  }

  static string Timing(string configPath)
  {
   var t=AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath)??ScriptableObject.CreateInstance<EnemyTelegraphTimingSO>();
   CombatConfigSO config=configPath!=""?AssetDatabase.LoadAssetAtPath<CombatConfigSO>(configPath):null;
   if(config==null){var all=Find<CombatConfigSO>().ToArray();var used=ScanScenes(new HashSet<string>(all.Select(x=>AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(x)))));
    config=all.FirstOrDefault(x=>used.TryGetValue(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(x)),out var l)&&l.Contains(Path.GetFileNameWithoutExtension(Scenes[0])))??all.FirstOrDefault();}
   if(config==null)return "REFUSED no CombatConfigSO";
   var sb=new StringBuilder("timing vs ParryJudge ("+AssetDatabase.GetAssetPath(config)+", window "+F(config.ParryWindow)+", PeakLead "+F(t.PeakLead)+"):\n");int fails=0;
   void Case(string name,Element attack,float impact,float t0)
   {
    var guard=(Element)(((int)attack+3)%5);if(!ElementRelations.Overcomes(guard,attack))return;float peak=impact-t.PeakLead,on=Mathf.Max(t0,peak-t.RiseLead);var parts=new List<string>();
    float edge=Mathf.Max(0f,t.PeakLead-.001f);foreach(var at in new[]{peak-edge,peak,peak+edge})
    {
     var judge=new ParryJudge(config);judge.RaiseGuard(guard,at,1f);var o=judge.ResolveImpact(attack,impact,Vector3.zero);
     if(o!=ParryOutcome.Success)fails++;parts.Add(F(at-peak)+":"+o);
    }
    sb.AppendLine("  "+name+" impact "+F(impact)+" peak "+F(peak)+" lit "+F(on)+" | guard "+guard+" at peak "+string.Join(" ",parts));
   }
   foreach(var p in Find<EnemyAttackProfileSO>().Where(x=>x.Elemental))
   {
    bool proj=p.Delivery==EnemyAttackDelivery.HomingProjectile||p.Delivery==EnemyAttackDelivery.AimedProjectile;
    foreach(float d in new[]{4f,8f,12f}){if(!proj&&d>4f)break;Case(p.name+"/"+p.Delivery+(proj?" "+F(d)+"m":""),p.Element,p.Telegraph+(proj?Mathf.Max(.05f,d/p.ProjectileSpeed):0f),0f);}
   }
   Case("legacy ranged (fixed flight, any distance)",Element.Fire,config.RangedTelegraph+config.ProjectileFlight,0f);
   sb.Append("non-Success="+fails+" (expected 0; edges tested at ±(PeakLead - 1 ms) — with PeakLead = window/2 the early edge meets the window end, ParryJudge uses <=)");
   return sb.ToString();
  }

  // ---------------------------------------------------------------- mine_fire/0 ranged restore
  static string FireCopyAsset()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Play mode";
   var src=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(FireSource);if(src==null)return "REFUSED missing "+FireSource;
   var copy=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(FireCopy);
   if(copy==null){Folder(ArtRoot);if(!AssetDatabase.CopyAsset(FireSource,FireCopy))return "REFUSED copy failed";AssetDatabase.SaveAssets();copy=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(FireCopy);}
   bool valid=copy.TryValidate(out string error);
   return FireCopy+(valid?" valid":" INVALID "+error)+": "+copy.Archetype+"/"+copy.Delivery+"/"+copy.Element+" telegraph "+F(copy.Telegraph)+" speed "+F(copy.ProjectileSpeed)+" range "+F(copy.Range)+" (source "+FireSource+" untouched)";
  }

  static string FireAssign(string arg)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Play mode";
   string path=arg,bolt=DefaultBolt;int bar=arg.IndexOf('|');if(bar>0){path=arg.Substring(0,bar).Trim();bolt=arg.Substring(bar+1).Trim();}
   if(!Scenes.Contains(path))return "REFUSED scene not in the #306 list: "+path;
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)return "REFUSED dirty scene "+SceneManager.GetSceneAt(i).path;
   var profile=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(FireCopy);if(profile==null)return "REFUSED run fire-copy first";
   if(!profile.TryValidate(out string error))return "REFUSED "+FireCopy+" invalid: "+error;
   var boltPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(bolt);if(boltPrefab==null)return "REFUSED missing projectile prefab "+bolt;
   var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
   var actor=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PrologueEncounter>(true)).FirstOrDefault(a=>a.Id=="mine_fire/0");
   if(actor==null)return path+": mine_fire/0 not in scene";
   var enemy=actor.GetComponent<EnemyController>();var so=new SerializedObject(enemy);
   var fr=LoadFire();if(!fr.entries.Any(e=>e.scene==path))
   {
    fr.entries.Add(new FireEntry{scene=path,profile=AssetDatabase.GetAssetPath(so.FindProperty("_attackProfile").objectReferenceValue),projectile=AssetDatabase.GetAssetPath(so.FindProperty("_projectilePrefab").objectReferenceValue),
     mode=so.FindProperty("_attackMode").intValue,ranged=actor.Ranged,preferred=actor.PreferredDistance});
    Directory.CreateDirectory(OutRoot);File.WriteAllText(FireFile,JsonUtility.ToJson(fr,true));
    Backup(path,DateTime.Now.ToString("yyyyMMddTHHmmss",CultureInfo.InvariantCulture),null);
   }
   so.FindProperty("_attackProfile").objectReferenceValue=profile;so.FindProperty("_attackMode").intValue=(int)EnemyController.AttackMode.RangedOnly;
   so.FindProperty("_projectilePrefab").objectReferenceValue=boltPrefab;so.ApplyModifiedPropertiesWithoutUndo();
   actor.Ranged=true;actor.PreferredDistance=profile.PreferredDistanceHint;EditorUtility.SetDirty(actor);EditorUtility.SetDirty(enemy);
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return path+": mine_fire/0 -> "+FireCopy+" ("+profile.Delivery+", telegraph "+F(profile.Telegraph)+", speed "+F(profile.ProjectileSpeed)+"), projectile "+bolt+", Ranged=true, PreferredDistance="+F(profile.PreferredDistanceHint)+
    " | WorldContent Encounter mine_fire/0 already Ranged=1 (no content change). Run apply:"+path+" for its organ set.";
  }

  static string FireRevert(string path)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Play mode";
   if(!Scenes.Contains(path))return "REFUSED scene not in the #306 list: "+path;
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)return "REFUSED dirty scene "+SceneManager.GetSceneAt(i).path;
   var fr=LoadFire();var e=fr.entries.FirstOrDefault(x=>x.scene==path);if(e==null)return path+": nothing recorded";
   var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
   var actor=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PrologueEncounter>(true)).FirstOrDefault(a=>a.Id=="mine_fire/0");
   if(actor==null)return path+": mine_fire/0 not in scene";
   var so=new SerializedObject(actor.GetComponent<EnemyController>());
   so.FindProperty("_attackProfile").objectReferenceValue=string.IsNullOrEmpty(e.profile)?null:AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(e.profile);
   so.FindProperty("_projectilePrefab").objectReferenceValue=string.IsNullOrEmpty(e.projectile)?null:AssetDatabase.LoadAssetAtPath<GameObject>(e.projectile);
   so.FindProperty("_attackMode").intValue=e.mode;so.ApplyModifiedPropertiesWithoutUndo();
   actor.Ranged=e.ranged;actor.PreferredDistance=e.preferred;EditorUtility.SetDirty(actor);
   fr.entries.Remove(e);File.WriteAllText(FireFile,JsonUtility.ToJson(fr,true));
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return path+": mine_fire/0 restored (mode "+e.mode+", profile "+(string.IsNullOrEmpty(e.profile)?"none":e.profile)+")";
  }

  // ---------------------------------------------------------------- status / helpers
  static string Status()
  {
   var rec=Load();var t=AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath);
   var sb=new StringBuilder("shader="+(Shader.Find(HaloShader)!=null)+" timing="+(t!=null)+" haloMat="+(AssetDatabase.LoadAssetAtPath<Material>(HaloMat)!=null)+" strokeMat="+(AssetDatabase.LoadAssetAtPath<Material>(StrokeMat)!=null)+" fireCopy="+(AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(FireCopy)!=null)+"\n");
   foreach(var g in rec.entries.GroupBy(e=>e.target))sb.AppendLine("  "+g.Key+": "+g.Count()+" recorded");
   foreach(var e in LoadFire().entries)sb.AppendLine("  fire-assign "+e.scene);
   if(EditorApplication.isPlaying)
    foreach(var tel in Object.FindObjectsByType<EnemyElementTelegraph>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
     sb.AppendLine("  live "+HierarchyPath(tel.transform)+" lit="+F(tel.LitLevel)+" halos="+tel.VisibleHaloCount+" attack="+tel.LitAttackId);
   return sb.ToString().TrimEnd();
  }

  // one streamed pass per scene file (no whole-file strings: the main scene is ~50 MB)
  static Dictionary<string,List<string>> ScanScenes(HashSet<string> guids)
  {
   var found=new Dictionary<string,List<string>>();const string key="guid: ";
   foreach(var s in Scenes)
   {
    if(!File.Exists(Abs(s)))continue;string name=Path.GetFileNameWithoutExtension(s);
    foreach(var line in File.ReadLines(Abs(s)))
     for(int at=line.IndexOf(key,StringComparison.Ordinal);at>=0&&at+key.Length+32<=line.Length;at=line.IndexOf(key,at+1,StringComparison.Ordinal))
     {
      var g=line.Substring(at+key.Length,32);if(!guids.Contains(g))continue;
      if(!found.TryGetValue(g,out var l))found[g]=l=new List<string>();if(!l.Contains(name))l.Add(name);
     }
   }
   return found;
  }

  static ElementPaletteSO PrefabPalette()
  {
   // prefabs are shared: use the palette the promoted main scene's wiring references, else the Data/Configs test palette
   string main=Abs(Scenes[0]);
   if(File.Exists(main))
   {
    const string key="_palette: {fileID: 11400000, guid: ";
    foreach(var line in File.ReadLines(main)){int at=line.IndexOf(key,StringComparison.Ordinal);if(at<0)continue;var g=line.Substring(at+key.Length,32);var p=AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(AssetDatabase.GUIDToAssetPath(g));if(p!=null)return p;}
   }
   return AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(FallbackPalette);
  }

  static void Backup(string assetPath,string stamp,Record rec)
  {
   string src=Abs(assetPath),dst=Path.Combine(OutRoot,"backup",stamp,assetPath.Replace('/',Path.DirectorySeparatorChar));
   if(File.Exists(dst))return;Directory.CreateDirectory(Path.GetDirectoryName(dst));File.Copy(src,dst);
   if(File.Exists(src+".meta"))File.Copy(src+".meta",dst+".meta",true);
   rec?.backups.Add(dst);
  }
  static IEnumerable<T> Find<T>() where T:Object=>AssetDatabase.FindAssets("t:"+typeof(T).Name).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<T>).Where(x=>x!=null);
  static Record Load()=>File.Exists(RecordFile)?JsonUtility.FromJson<Record>(File.ReadAllText(RecordFile))??new Record():new Record();
  static void Save(Record r){Directory.CreateDirectory(OutRoot);File.WriteAllText(RecordFile,JsonUtility.ToJson(r,true));}
  static FireRecord LoadFire()=>File.Exists(FireFile)?JsonUtility.FromJson<FireRecord>(File.ReadAllText(FireFile))??new FireRecord():new FireRecord();
  static string Abs(string assetPath)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));
  static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;var parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
  static string HierarchyPath(Transform t){var s=t.name;for(var p=t.parent;p!=null;p=p.parent)s=p.name+"/"+s;return s;}
  static string F(float v)=>v.ToString("0.###",CultureInfo.InvariantCulture);
 }
}
