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
 // #308 (SPEC-TELEGRAPH-ORGAN-308, D308-4 + D308-4b): organ tables follow the on-model surface design — every species and the growth
 //   lesson light THE common magic-stone shard "Organ308_maseok_shard" (one mesh for every enemy; OrganSurface308 prefabs/apply adds it
 //   where shard308_placements.json says; missing part = organ unbound, never a sphere on the body), the south gate general keeps only
 //   the spear tip ReusedMetalTip (Part), mine_fire/0 uses the ember mask, the dragon its mouth/eyes (Sphere on the body) and the ridge
 //   mask chain, MineTutorialBoss306 its crystal mask (MineBoss306). Build also precomputes each organ's surface (renderer, mode, mask,
 //   surface point, outward normal) through OrganSurface308.ComputeSurface. OrganSurface308 is the 308 ledger.
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

  // organ recipe: bone by Humanoid slot (then a second slot), then names; Special = derived offsets (eyes from Head→MouthOrigin) or
  // "tip" (surface point = the part's bounds centre: the spear tip). #308: Part = a child renderer named so (the organ anchors to it and
  // lights the whole part), Mode/MaskPath = how a body organ lights (Sphere / Mask)
  internal sealed class Spec{public string Id;public EnemyOrganRole Role;public string[] Keys;public HumanBodyBones Human=HumanBodyBones.LastBone,Human2=HumanBodyBones.LastBone;public string[] Names=new string[0];public string Special;public float Radius;public Vector3 Fallback;
   public EnemyOrganSurfaceMode Mode=EnemyOrganSurfaceMode.Sphere;public string Part,MaskPath;
   public Spec OnPart(string part){Mode=EnemyOrganSurfaceMode.Part;Part=part;return this;}
   public Spec Masked(string path){Mode=EnemyOrganSurfaceMode.Mask;MaskPath=path;return this;}
   public Spec Then(HumanBodyBones second){Human2=second;return this;}}
  static Spec S(string id,EnemyOrganRole role,float radius,string[] keys,HumanBodyBones human,Vector3 fallback,string special,params string[] names)=>new Spec{Id=id,Role=role,Radius=radius,Keys=keys,Human=human,Names=names,Special=special,Fallback=fallback};
  static readonly string[] Any={EnemyOrganSet.KeyAny};
  const HumanBodyBones None=HumanBodyBones.LastBone;
  internal static string PartName(string organId)=>"Organ308_"+organId;

  // D308-4b 기관 부위 = 공통 마석 파편: one organ per owner, always the same shard part; per owner only the role / keys here and the
  // placement (bone, pose, size) in shard308_placements.json (organ-art track). Radius .01 × body scale is only a seed — ComputeSurface
  // grows a Part organ to 1.05 × the shard's reach, so the ring sits on the shard itself. Bones / fallback offsets matter only while the
  // part is missing (organ unbound, never lit). Fox = Spine: the tail tip faces away from the actor front, so it is not subject to the
  // facing selection nor to the AC-T5 authoring front check (FrontReport), and a single Spine organ stays lit through the window. All TEST.
  internal const string ShardOrgan="maseok_shard";
  internal static Spec ShardSpec(string owner)
  {
   string part=PartName(ShardOrgan);
   switch(owner)
   {
    case "dokkaebi": return S(ShardOrgan,EnemyOrganRole.Core,.01f,Any,HumanBodyBones.Chest,new Vector3(0,1.7f,.16f),null,"Spine01").OnPart(part);                 // 가슴 한복판
    case "agwi": return S(ShardOrgan,EnemyOrganRole.Core,.01f,Any,HumanBodyBones.Hips,new Vector3(-.035f,1.25f,.29f),null,"Hips").OnPart(part);                    // 부푼 배
    case "changgui": return S(ShardOrgan,EnemyOrganRole.Core,.01f,Any,HumanBodyBones.RightShoulder,new Vector3(.075f,1.45f,.08f),null,"RightShoulder").Then(HumanBodyBones.UpperChest).OnPart(part);   // 오른 쇄골 아래
    case "bulgasari": return S(ShardOrgan,EnemyOrganRole.Core,.01f,Any,None,new Vector3(0,.25f,1.4f),null,"Head").OnPart(part);                                     // 이마
    case "fox_spirit": return S(ShardOrgan,EnemyOrganRole.Spine,.01f,Any,None,new Vector3(-.32f,-.23f,-1.05f),null,"Tail_05","Tail_04").OnPart(part);            // 꼬리 끝(사용자)
    case "imugi": return S(ShardOrgan,EnemyOrganRole.Core,.01f,Any,None,new Vector3(0,.16f,2.55f),null,"Body_01","Head").OnPart(part);                             // 턱밑 역린(사용자)
    case "demo_growth_lesson": return S(ShardOrgan,EnemyOrganRole.Core,.01f,new[]{EnemyOrganSet.KeyGround,EnemyOrganSet.KeyAny},None,new Vector3(0,.66f,.3f),null,"Tree_00").OnPart(part);   // 줄기 앞면
    default: return null;
   }
  }

  // #308 species table: the common shard (D308-4b) — folklore identity stays in silhouette / motion / sound / death (ART-SILHOUETTE)
  internal static List<Spec> SpeciesSpecs(string id)
  {
   var s=ShardSpec(id);
   return s!=null&&id!="demo_growth_lesson"?new List<Spec>{s}:new List<Spec>();
  }
  // scene actors that own elemental attacks (Guardian302 has no combat owner: excluded — no false telegraph)
  internal static readonly string[] SceneActors={"cheongryong","sinmok263","south_gate_general","demo_growth_lesson","mine_fire/0"};
  internal static List<Spec> ActorSpecs(string id)
  {
   var up=new Vector3(0,1.2f,.2f);
   switch(id)
   {
    case "cheongryong": case "sinmok263":
    {
     // mouth / eyes = the body surface inside the organ sphere (Sphere: not a modelled part — user question in the Spec's 남은 일);
     // back spikes Body_06→18 = ridge mask × chain spheres (Sphere until the baked ridge mask exists — Temporary Exception)
     var bolt=new[]{EnemyOrganSet.KeyBolt};var root=new[]{EnemyOrganSet.KeyRoot};
     var l=new List<Spec>{S("mouth",EnemyOrganRole.Mouth,.2f,bolt,None,new Vector3(0,1.1f,1.7f),null,"MouthOrigin","Head"),
      S("eye_l",EnemyOrganRole.Eye,.08f,bolt,None,new Vector3(-.2f,1.3f,1.4f),"eyeL","Head"),S("eye_r",EnemyOrganRole.Eye,.08f,bolt,None,new Vector3(.2f,1.3f,1.4f),"eyeR","Head")};
     for(int i=6;i<=18;i++)l.Add(S("spine_"+i.ToString("00"),EnemyOrganRole.Spine,.16f,root,None,new Vector3(0,.8f,1.2f-(i-6)*.25f),null,"Body_"+i.ToString("00")).Masked(OrganSurface308.RidgeMask));
     return l;
    }
    case "south_gate_general":
    {
     // spear tip only (chest / helmet removed): the whole reused metal tip lights; the counter stroke lands on its bounds centre
     var wave=new[]{EnemyOrganSet.KeyWave};
     return new List<Spec>{S("spear_blade",EnemyOrganRole.Weapon,.12f,wave,None,new Vector3(0,1.05f,.65f),"tip","Temporary_ReusedMesh_Polearm").OnPart("ReusedMetalTip")};
    }
    case "demo_growth_lesson":
     // one real anchor: the common shard on the trunk (the tree itself has 3 submeshes and can never carry the overlay)
     return new List<Spec>{ShardSpec("demo_growth_lesson")};
    default: return new List<Spec>{S("core",EnemyOrganRole.Core,.14f,Any,HumanBodyBones.Chest,up,null,"Spine02","Spine2","Chest","mixamorig:Spine2","Spine1","Spine").Masked(OrganSurface308.EmberMask),
     S("mouth",EnemyOrganRole.Mouth,.1f,new[]{EnemyOrganSet.KeyProjectile},HumanBodyBones.Head,up,null,"MouthOrigin","Head","head","mixamorig:Head").Masked(OrganSurface308.EmberMask)};
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
   AssetDatabase.SaveAssetIfDirty(hm);AssetDatabase.SaveAssetIfDirty(sm);AssetDatabase.SaveAssetIfDirty(t);   // #308: targeted saves (no SaveAssets flush)
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
  static EnemyOrganSet.Organ[] Build(Transform owner,Transform visual,List<Spec> specs,out string how)=>Build(owner,visual,specs,null,out how);
  // #308: every organ also gets its surface (renderer, mode, mask, surface point, outward normal) — OrganSurface308.ComputeSurface.
  // Body scale / centre ignore the 308 parts themselves ("Organ308_*"), so a rerun after the parts exist gives the same organs (idempotent)
  internal static EnemyOrganSet.Organ[] Build(Transform owner,Transform visual,List<Spec> specs,OrganSurface308.SurfaceCache cache,out string how)
  {
   bool ownCache=cache==null;if(ownCache)cache=new OrganSurface308.SurfaceCache();
   try
   {
    var bones=visual.GetComponentsInChildren<Transform>(true);
    var animator=visual.GetComponentsInChildren<Animator>(true).Where(a=>a.avatar!=null&&a.avatar.isHuman).OrderByDescending(a=>a.enabled&&a.gameObject.activeInHierarchy).FirstOrDefault();
    var renderers=visual.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&!(r is ParticleSystemRenderer)&&!r.name.StartsWith("Organ308_",StringComparison.Ordinal)).ToArray();
    float height=1.8f;Vector3 centre=owner.position+Vector3.up;
    if(renderers.Length>0){var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);height=b.size.y;centre=b.center;}
    float scale=Mathf.Clamp(height/1.8f,.5f,4f);float threshold=OrganSurface308.MaskThreshold();
    var list=new List<EnemyOrganSet.Organ>();var notes=new List<string>();
    Transform Bone(string name)=>bones.Where(t=>t.name==name).OrderByDescending(t=>t.gameObject.activeInHierarchy).FirstOrDefault();
    foreach(var s in specs)
    {
     Transform anchor=null;string src=null;
     if(s.Human!=HumanBodyBones.LastBone&&animator!=null){anchor=animator.GetBoneTransform(s.Human);if(anchor!=null)src="human:"+s.Human;}
     if(anchor==null&&s.Human2!=HumanBodyBones.LastBone&&animator!=null){anchor=animator.GetBoneTransform(s.Human2);if(anchor!=null)src="human:"+s.Human2;}
     if(anchor==null)foreach(var n in s.Names){anchor=Bone(n);if(anchor!=null){src=n;break;}}
     var organ=new EnemyOrganSet.Organ{Id=s.Id,Role=s.Role,AttackKeys=s.Keys,Radius=s.Radius*scale,HumanBone=s.Human,BoneName=s.Names.FirstOrDefault(),Mode=s.Mode};
     if(anchor==null){organ.Anchor=owner;organ.LocalOffset=Vector3.Scale(s.Fallback,new Vector3(1,scale,1));src="FALLBACK root"+organ.LocalOffset.ToString("F2");}
     else organ.Anchor=anchor;
     if(anchor!=null&&(s.Special=="eyeL"||s.Special=="eyeR"))
     {
      var mouth=Bone("MouthOrigin");Vector3 f=mouth!=null?mouth.position-anchor.position:owner.forward*.3f*scale;float L=Mathf.Max(.05f,f.magnitude);
      if(f.sqrMagnitude<.0025f)f=owner.forward*L;var right=Vector3.Cross(Vector3.up,f).normalized;if(right.sqrMagnitude<.5f)right=owner.right;var up=Vector3.Cross(f,right).normalized;
      var eye=anchor.position+f*.55f+up*(.35f*L)+right*((s.Special=="eyeL"?-.35f:.35f)*L);
      organ.LocalOffset=anchor.InverseTransformPoint(eye);organ.Radius=Mathf.Clamp(.22f*L,.04f,.3f);src+="+eye";
     }
     // #308 surface target
     if(s.Mode==EnemyOrganSurfaceMode.Part)
     {
      // the part may sit outside the visual (south gate spear tip under the actor's hand): search the whole owner
      var part=string.IsNullOrEmpty(s.Part)?null:Bone(s.Part)??owner.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==s.Part);
      var pr=part!=null?part.GetComponent<Renderer>():null;
      if(pr!=null){organ.Anchor=part;organ.TargetRenderer=pr;organ.HumanBone=HumanBodyBones.LastBone;organ.BoneName=part.name;organ.LocalOffset=part.InverseTransformPoint(pr.bounds.center);src="part:"+part.name;}
      else{organ.TargetRenderer=null;src+=" UNBOUND(part "+s.Part+" missing - OrganSurface308 prefabs/apply adds it)";}
     }
     else
     {
      organ.TargetRenderer=OrganSurface308.BodyRendererFor(visual,anchor);
      if(s.Mode==EnemyOrganSurfaceMode.Mask){organ.Mask=string.IsNullOrEmpty(s.MaskPath)?null:AssetDatabase.LoadAssetAtPath<Texture2D>(s.MaskPath);
       if(organ.Mask==null){organ.Mode=EnemyOrganSurfaceMode.Sphere;src+=" mask "+s.MaskPath+" missing -> SPHERE (Temporary Exception)";}}
     }
     string surface=OrganSurface308.ComputeSurface(organ,owner,centre,threshold,cache,s.Special=="tip",out float coverage);
     notes.Add(s.Id+"="+src+" ["+organ.Mode+(organ.TargetRenderer!=null?" on "+organ.TargetRenderer.name:"")+(coverage>=0?" cover "+F(coverage):"")+(surface.Length>0?" "+surface:"")+"]");
     list.Add(organ);
    }
    how=list.Count+" organs (scale "+F(scale)+", humanoid "+(animator!=null)+"): "+string.Join(", ",notes)+" | "+OrganSurface308.FrontReport(owner,list,out _);
    return list.ToArray();
   }
   finally{if(ownCache)cache.Dispose();}
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
   if(copy==null){Folder(ArtRoot);if(!AssetDatabase.CopyAsset(FireSource,FireCopy))return "REFUSED copy failed";AssetDatabase.ImportAsset(FireCopy);copy=   /* #308: CopyAsset already wrote the file — no SaveAssets flush */AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(FireCopy);}
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
     sb.AppendLine("  live "+HierarchyPath(tel.transform)+" lit="+F(tel.LitLevel)+" organs="+tel.LitOrganCount+" overlays="+tel.OverlayRendererCount+" attack="+tel.LitAttackId);
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
