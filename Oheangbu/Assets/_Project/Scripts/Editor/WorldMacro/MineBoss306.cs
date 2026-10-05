using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #306 #11 폐광 튜토리얼 보스 "버력 장사"(Meshy brute-b → Blender 인간형 리그 → Mixamo 동작, SPEC-PLAYTEST-306 #11, D306 보강).
 // Queue-safe (refusals are strings, never a dialog). Run(string):
 //   survey                 active scene: the mine_beast/0 actor, the blast room (floor, wall rays, NavMesh) around mine_inquiry
 //   import                 model FBX + clip FBXs -> Humanoid (avatar from each file), clip loop flags, body material
 //   assets                 four attack profiles, MineBossProfileSO, MineTutorialProfileSO (TEST script), CombatConfig copy
 //                          (ParriesToBlossom 2), boss vitals profile — idempotent (values are rewritten every run)
 //   apply:<scene>[|x,y,z]  clone mine_beast/0 as mine_tutorial_boss with the new body at the room point (default RoomPoint),
 //                          organs, rig clips, boss sequencer; content encounter; session actor + tutorial profile; record for revert
 //   revert:<scene>         removes the clone, the encounter and the session links that apply added
 // #308 (SPEC-TELEGRAPH-ORGAN-308, D308-4): organs light on the body surface — crystal colour-key mask (T_MineBoss306_CrystalMask, FBX
 //   unchanged) × organ sphere; both shoulders are Core (they light together for the ground ring); surface point / outward normal are
 //   precomputed (BuildOrgans, shared with OrganSurface308 apply: for a scene already applied). A missing mask = Sphere (reported).
 public static class MineBoss306
 {
  const string Root="Assets/_Project/Art/Characters/MineBoss306";
  const string ModelPath=Root+"/MineBoss306.fbx";
  const string DataDir=Root+"/Data";
  const string MatPath=Root+"/M_MineBoss306_Body.mat";
  const string ClipDir="Assets/_Project/Art/Characters/Trailer302/Animations/";
  const string ThrowClip=Root+"/Animations/MB306_Throw.fbx", DizzyClip=Root+"/Animations/MB306_DizzyIdle.fbx";
  const string Bolt="Assets/_Project/Art/SpellVFX120/Bolt300/PF_Bolt300_ga.prefab";
  const string TimingPath="Assets/_Project/Resources/Telegraph306/EnemyTelegraphTiming306.asset";
  const string FallbackPalette="Assets/_Project/Data/Configs/ElementPalette_Test.asset";
  const string BossName="버력 장사", TemplateId="mine_beast/0", ActorName="MineTutorialBoss306";
  static readonly string[] Scenes={"Assets/_Project/Scenes/World/W_Demo_Main.unity","Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity","Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity"};
  static readonly Vector3 RoomPoint=new Vector3(3300f,168.35f,1911f);   // blast room (mine_inquiry 3285,168,1911 east side) — TEST, survey first
  static string Record(string scene)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/Characters/MineBoss306/Backups/apply_"+Path.GetFileNameWithoutExtension(scene)+".json"));
  [Serializable] class Rec{public string actorPath,contentPath;public bool encounterAdded,sessionProfileSet,actorAdded,wiringAdded;}

  public static string Run(string c)
  {
   c=(c??"").Trim();
   try
   {
    if(EditorApplication.isPlayingOrWillChangePlaymode&&c!="survey")return "REFUSED Play mode";
    if(c=="survey")return Survey(SceneManager.GetActiveScene());
    if(c=="import")return Import();
    if(c=="assets")return Assets();
    if(c.StartsWith("apply:",StringComparison.Ordinal))return Apply(c.Substring(6).Trim());
    if(c.StartsWith("revert:",StringComparison.Ordinal))return Revert(c.Substring(7).Trim());
    if(c.StartsWith("wire:",StringComparison.Ordinal))return Wire(c.Substring(5).Trim());
    return "REFUSED unknown command "+c+" (survey | import | assets | apply:<scene>[|x,y,z] | revert:<scene>)";
   }
   catch(Exception e){return "FAILED "+e;}
  }

  // ------------------------------------------------------------------ survey
  static PrologueEncounter FindActor(Scene s,string id)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PrologueEncounter>(true)).FirstOrDefault(a=>a.Id==id);
  static string PathOf(Transform t){var sb=new StringBuilder(t.name);for(var p=t.parent;p!=null;p=p.parent)sb.Insert(0,p.name+"/");return sb.ToString();}
  static string V3(Vector3 v)=>v.x.ToString("F2")+","+v.y.ToString("F2")+","+v.z.ToString("F2");

  static string Survey(Scene s)
  {
   var sb=new StringBuilder(s.path+"\n");
   var a=FindActor(s,TemplateId);
   if(a==null)sb.AppendLine("  "+TemplateId+" not found");
   else
   {
    sb.AppendLine("  template "+PathOf(a.transform)+" at "+V3(a.transform.position)+" scale "+V3(a.transform.lossyScale));
    foreach(var comp in a.GetComponents<Component>())sb.AppendLine("    comp "+comp.GetType().Name);
    foreach(Transform ch in a.transform)sb.AppendLine("    child "+ch.name+" active "+ch.gameObject.activeSelf);
    var anim=a.GetComponentInChildren<Animator>(true);sb.AppendLine("    animator "+(anim!=null?PathOf(anim.transform)+" human "+(anim.avatar!=null&&anim.avatar.isHuman):"none"));
   }
   var boss=FindActor(s,MineTutorialProfileSO.BossId);sb.AppendLine("  existing boss: "+(boss!=null?PathOf(boss.transform):"none"));
   foreach(var p in new[]{RoomPoint,new Vector3(3285,168.35f,1911),new Vector3(3313,168.35f,1911)})
   {
    sb.Append("  point "+V3(p)+": ");
    if(Physics.Raycast(p+Vector3.up*3f,Vector3.down,out var floor,8f,~0,QueryTriggerInteraction.Ignore))sb.Append("floor "+floor.point.y.ToString("F2")+" ("+floor.collider.name+")");
    else sb.Append("floor none");
    if(Physics.Raycast(p+Vector3.up*1f,Vector3.up,out var ceil,30f,~0,QueryTriggerInteraction.Ignore))sb.Append(" ceiling +"+(ceil.distance+1f).ToString("F1"));
    sb.Append(NavMesh.SamplePosition(p,out var nh,2f,NavMesh.AllAreas)?" nav "+V3(nh.position):" nav none");
    sb.Append("\n    walls:");
    for(int deg=0;deg<360;deg+=30)
    {
     var dir=Quaternion.Euler(0,deg,0)*Vector3.forward;
     float d=Physics.Raycast(p+Vector3.up*1.2f,dir,out var w,40f,~0,QueryTriggerInteraction.Ignore)?w.distance:40f;
     sb.Append(" "+deg+":"+d.ToString("F1"));
    }
    sb.AppendLine();
   }
   return sb.ToString();
  }

  // ------------------------------------------------------------------ import
  static string Import()
  {
   var sb=new StringBuilder();
   sb.AppendLine(Humanoid(ModelPath,false,true));
   sb.AppendLine(Humanoid(ThrowClip,false,false));
   sb.AppendLine(Humanoid(DizzyClip,true,false));
   // body material: base colour + normal (no emission: ART-INK — the crystals glow only through the #308 organ surface overlay)
   var baseTex=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/T_MineBoss306_BaseColor.png");
   var normPath=Root+"/Textures/T_MineBoss306_Normal.png";
   var ni=AssetImporter.GetAtPath(normPath) as TextureImporter;
   if(ni!=null&&ni.textureType!=TextureImporterType.NormalMap){ni.textureType=TextureImporterType.NormalMap;ni.SaveAndReimport();}
   var normTex=AssetDatabase.LoadAssetAtPath<Texture2D>(normPath);
   var mat=AssetDatabase.LoadAssetAtPath<Material>(MatPath);
   if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,MatPath);}
   if(mat.HasProperty("_BaseMap"))mat.SetTexture("_BaseMap",baseTex);
   if(mat.HasProperty("_BumpMap")&&normTex!=null){mat.SetTexture("_BumpMap",normTex);mat.EnableKeyword("_NORMALMAP");}
   if(mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",.18f);
   if(mat.HasProperty("_Metallic"))mat.SetFloat("_Metallic",0f);
   EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);   // #308: targeted save (SaveAssets flushes other sessions' dirty assets)
   sb.AppendLine("material "+MatPath+" base "+(baseTex!=null)+" normal "+(normTex!=null));
   return sb.ToString();
  }

  static string Humanoid(string path,bool loop,bool model)
  {
   var mi=AssetImporter.GetAtPath(path) as ModelImporter;if(mi==null)return "MISSING "+path;
   mi.animationType=ModelImporterAnimationType.Human;mi.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
   mi.autoGenerateAvatarMappingIfUnspecified=true;
   if(model){mi.importAnimation=false;mi.materialImportMode=ModelImporterMaterialImportMode.None;}
   else
   {
    mi.importAnimation=true;mi.materialImportMode=ModelImporterMaterialImportMode.None;
    var clips=mi.defaultClipAnimations;
    foreach(var c in clips){c.loopTime=loop;c.lockRootRotation=true;c.lockRootHeightY=true;c.lockRootPositionXZ=true;c.keepOriginalOrientation=true;c.keepOriginalPositionY=true;c.keepOriginalPositionXZ=true;}
    mi.clipAnimations=clips;
   }
   mi.SaveAndReimport();
   var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
   return path+": humanoid avatar "+(avatar!=null?(avatar.isValid&&avatar.isHuman?"OK":"INVALID"):"missing");
  }

  static AnimationClip Clip(string path)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));

  // ------------------------------------------------------------------ assets
  static T Asset<T>(string name)where T:ScriptableObject
  {
   if(!AssetDatabase.IsValidFolder(DataDir)){Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath,"..",DataDir)));AssetDatabase.Refresh();}
   string path=DataDir+"/"+name+".asset";var a=AssetDatabase.LoadAssetAtPath<T>(path);
   if(a==null){a=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(a,path);}
   return a;
  }

  static EnemyAttackProfileSO Attack(string name,EnemyArchetype arch,EnemyAttackDelivery delivery,bool elemental,float telegraph,float recovery,float damage,float range,float arc,float speed,float radius,float preferred,Vector2 cooldown)
  {
   var p=Asset<EnemyAttackProfileSO>(name);
   p.Archetype=arch;p.Delivery=delivery;p.Mode=delivery==EnemyAttackDelivery.MeleeArc?EnemyController.AttackMode.MeleeOnly:EnemyController.AttackMode.RangedOnly;
   p.Elemental=elemental;p.Element=Element.Wood;p.Telegraph=telegraph;p.Recovery=recovery;p.Damage=damage;p.Range=range;p.ArcDegrees=arc;
   p.ProjectileSpeed=speed;p.ImpactRadius=radius;p.PreferredDistanceHint=preferred;p.CooldownRange=cooldown;p.MovementSpeedHint=2.4f;
   if(!p.TryValidate(out var e))throw new InvalidOperationException(name+": "+e);
   EditorUtility.SetDirty(p);return p;
  }

  static string Assets()
  {
   var sb=new StringBuilder();
   // TEST values (PLAN §2-11 table): M1 ~1.2 s / M2 ~1.0 s 150° / M3 1.0 s + 10 m at 12 m/s / M4 ring 1.35 m
   var charge=Attack("MB306_M1_Charge",EnemyArchetype.NeutralMelee,EnemyAttackDelivery.MeleeArc,false,1.2f,.9f,14,4.2f,60,10,.8f,2.4f,new Vector2(1.2f,2f));
   var sweep=Attack("MB306_M2_Sweep",EnemyArchetype.NeutralMelee,EnemyAttackDelivery.MeleeArc,false,1.0f,.8f,12,3.4f,150,10,.8f,2.4f,new Vector2(1.2f,2f));
   var shard=Attack("MB306_M3_Shard",EnemyArchetype.WoodVine,EnemyAttackDelivery.HomingProjectile,true,1.0f,.7f,10,14,110,12,.8f,7,new Vector2(1.2f,2f));
   var ring=Attack("MB306_M4_CrystalRing",EnemyArchetype.WoodVine,EnemyAttackDelivery.GroundEruption,true,1.35f,1.0f,14,10,110,10,1.35f,5,new Vector2(1.4f,2.2f));
   var boss=Asset<MineBossProfileSO>("MB306_BossProfile");
   boss.Charge=charge;boss.Sweep=sweep;boss.Shard=shard;boss.CrystalRing=ring;
   if(!boss.TryValidate(out var be))throw new InvalidOperationException(be);EditorUtility.SetDirty(boss);
   var tut=Asset<MineTutorialProfileSO>("MB306_TutorialProfile");tut.ApplyDefaults();
   if(!tut.TryValidate(out var te))throw new InvalidOperationException(te);EditorUtility.SetDirty(tut);
   var vit=Asset<EnemyVitalsProfileSO>("MB306_Vitals");
   var vso=new SerializedObject(vit);vso.FindProperty("_maxHp").floatValue=120f;vso.FindProperty("_displayName").stringValue=BossName;
   vso.FindProperty("_isBoss").boolValue=true;vso.FindProperty("_showLockOnBar").boolValue=false;vso.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(vit);
   foreach(var a in new Object[]{charge,sweep,shard,ring,boss,tut,vit})AssetDatabase.SaveAssetIfDirty(a);   // #308: only what this command wrote
   sb.AppendLine("profiles: "+string.Join(", ",new[]{charge,sweep,shard,ring}.Select(p=>p.name)));
   sb.AppendLine("boss "+boss.name+", tutorial beats "+tut.Beats.Length+", vitals "+BossName+" 120 HP (boss bar)");
   return sb.ToString();
  }

  static CombatConfigSO ConfigCopy(CombatConfigSO source)
  {
   string path=DataDir+"/MB306_CombatConfig.asset";var copy=AssetDatabase.LoadAssetAtPath<CombatConfigSO>(path);
   if(copy==null){if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source),path))throw new InvalidOperationException("config copy failed");AssetDatabase.ImportAsset(path);copy=AssetDatabase.LoadAssetAtPath<CombatConfigSO>(path);}
   var so=new SerializedObject(copy);var p=so.FindProperty("_parriesToBlossom");if(p==null)throw new InvalidOperationException("_parriesToBlossom not found");
   p.intValue=2;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(copy);AssetDatabase.SaveAssetIfDirty(copy);return copy;   // tutorial value: two parries fill the ring
  }

  // ------------------------------------------------------------------ apply / revert
  static Scene Open(string path,out string error)
  {
   error="";
   if(!Scenes.Contains(path)){error="REFUSED scene not in the #306 list: "+path;return default;}
   if(SceneManager.GetActiveScene().path==path)return SceneManager.GetActiveScene();
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty){error="REFUSED dirty scene "+SceneManager.GetSceneAt(i).path;return default;}
   return EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
  }

  static string Apply(string arg)
  {
   string path=arg;Vector3 at=RoomPoint;int bar=arg.IndexOf('|');
   if(bar>0){path=arg.Substring(0,bar);var n=arg.Substring(bar+1).Split(',');at=new Vector3(float.Parse(n[0]),float.Parse(n[1]),float.Parse(n[2]));}
   var s=Open(path,out string err);if(!s.IsValid())return err;
   string recPath=Record(path);if(File.Exists(recPath))return "REFUSED already applied (record "+recPath+"); revert first";
   var model=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);if(model==null)return "REFUSED model missing "+ModelPath+" (copy + import first)";
   var bossProfile=AssetDatabase.LoadAssetAtPath<MineBossProfileSO>(DataDir+"/MB306_BossProfile.asset");
   var tutProfile=AssetDatabase.LoadAssetAtPath<MineTutorialProfileSO>(DataDir+"/MB306_TutorialProfile.asset");
   var vitProfile=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(DataDir+"/MB306_Vitals.asset");
   if(bossProfile==null||tutProfile==null||vitProfile==null)return "REFUSED run assets first";
   var template=FindActor(s,TemplateId);if(template==null)return "REFUSED template "+TemplateId+" missing";
   var session=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();if(session==null)return "REFUSED no session";
   if(session.Content!=null&&AssetDatabase.GetAssetPath(session.Content).EndsWith("03_Content.asset",StringComparison.Ordinal))return "REFUSED session content is the protected 03_Content.asset (#308 protection)";
   if(FindActor(s,MineTutorialProfileSO.BossId)!=null)return "REFUSED boss already in scene";
   if(!NavMesh.SamplePosition(at,out var nav,2.5f,NavMesh.AllAreas))return "REFUSED no NavMesh near "+V3(at);
   var sb=new StringBuilder(path+" (apply)\n");var rec=new Rec();

   // 1. clone the template actor (combat, nav, audio wiring), swap the body
   var go=Object.Instantiate(template.gameObject,template.transform.parent);go.name=ActorName;
   go.transform.SetPositionAndRotation(nav.position,Quaternion.Euler(0,270,0));go.transform.localScale=Vector3.one;
   foreach(Transform ch in go.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(ch.gameObject);
   foreach(var fp in go.GetComponents<EnemyFootPlacement298>())Object.DestroyImmediate(fp);
   foreach(var old in go.GetComponents<EnemyElementTelegraph>())Object.DestroyImmediate(old);
   foreach(var old in go.GetComponents<EnemyOrganSet>())Object.DestroyImmediate(old);
   var body=(GameObject)PrefabUtility.InstantiatePrefab(model,go.transform);body.name="MineBoss306_Visual";
   body.transform.localPosition=Vector3.zero;body.transform.localRotation=Quaternion.identity;body.transform.localScale=Vector3.one;
   var mat=AssetDatabase.LoadAssetAtPath<Material>(MatPath);
   foreach(var r in body.GetComponentsInChildren<Renderer>(true)){r.sharedMaterial=mat;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;}
   var animator=body.GetComponent<Animator>()??body.AddComponent<Animator>();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
   var smr=body.GetComponentInChildren<SkinnedMeshRenderer>(true);if(smr!=null)smr.updateWhenOffscreen=true;

   // 2. collider / agent for a 2.5 m body
   var cap=go.GetComponent<CapsuleCollider>();if(cap!=null){cap.height=2.5f;cap.radius=.55f;cap.center=new Vector3(0,1.25f,0);}
   var agent=go.GetComponent<NavMeshAgent>();if(agent!=null){agent.radius=.6f;agent.height=2.5f;agent.baseOffset=0;}

   // 3. combat data
   var vit=go.GetComponent<EnemyVitals>();var ctl=go.GetComponent<EnemyController>();
   var srcConfig=new SerializedObject(vit).FindProperty("_config").objectReferenceValue as CombatConfigSO;if(srcConfig==null)throw new InvalidOperationException("template has no CombatConfig");
   var config=ConfigCopy(srcConfig);
   var vso=new SerializedObject(vit);vso.FindProperty("_config").objectReferenceValue=config;vso.FindProperty("_profile").objectReferenceValue=vitProfile;vso.ApplyModifiedPropertiesWithoutUndo();
   var cso=new SerializedObject(ctl);cso.FindProperty("_config").objectReferenceValue=config;cso.FindProperty("_attackProfile").objectReferenceValue=bossProfile.Charge;
   cso.FindProperty("_renderer").objectReferenceValue=smr;cso.FindProperty("_projectilePrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(Bolt);
   cso.FindProperty("_tintBodyOnTelegraph").boolValue=false;cso.FindProperty("_environmentOcclusion").boolValue=true;cso.ApplyModifiedPropertiesWithoutUndo();
   var enc=go.GetComponent<PrologueEncounter>();enc.Id=MineTutorialProfileSO.BossId;enc.PatrolPoints=new[]{nav.position,nav.position+Vector3.right*.5f};
   enc.Ranged=false;enc.DetectionRange=14f;enc.Leash=26f;enc.Speed=2.4f;enc.PreferredDistance=2.4f;enc.DeathVisualSeconds=3f;
   var sequencer=go.GetComponent<MineBossController>()??go.AddComponent<MineBossController>();
   var sso=new SerializedObject(sequencer);sso.FindProperty("_profile").objectReferenceValue=bossProfile;sso.ApplyModifiedPropertiesWithoutUndo();

   // 4. rig motion: Mutant set (#302 Mixamo) + Throw / Dizzy (#306 Mixamo); per-move clips
   var rig=go.GetComponent<EnemyRigMotion298>();
   rig.Animator=animator;rig.Enemy=ctl;rig.Vitals=vit;rig.General=null;rig.SerpentFollow=null;rig.FootPlacement=null;
   rig.Idle=Clip(ClipDir+"G302_MutantBreathingIdle.fbx");rig.Walk=Clip(ClipDir+"G302_MutantWalking.fbx");rig.Attack=Clip(ClipDir+"G302_MutantPunch.fbx");
   rig.Hit=Clip(ClipDir+"G302_BigHitToHead.fbx");rig.Stun=Clip(DizzyClip);rig.Death=Clip(ClipDir+"G302_MutantDying.fbx");rig.LoopStun=true;
   rig.MoveProfiles=new[]{bossProfile.Charge,bossProfile.Sweep,bossProfile.Shard,bossProfile.CrystalRing};
   rig.MoveClips=new[]{Clip(ClipDir+"G302_MutantPunch.fbx"),Clip(ClipDir+"G302_MutantSwiping.fbx"),Clip(ThrowClip),Clip(ClipDir+"G302_MutantJumpAttack.fbx")};
   rig.MovePeaks01=new[]{.45f,.5f,.55f,.6f};rig.WalkMetresPerSecond=1.6f;rig.ArmRelax=0;rig.ElbowRelax=0;
   if(!rig.IsConfigured)sb.AppendLine("  WARN rig motion not configured (missing clip?)");

   // 5. organs: shoulder clusters (ground ring, Core = together), jaw + left forearm (thrown shard); the crystal surface lights only while an
   //    elemental attack rises (#308 overlay, no new object)
   var organs=BuildOrgans(go.transform,animator,smr,null,sb);
   var set=body.AddComponent<EnemyOrganSet>();set.Organs=organs;
   var tel=body.AddComponent<EnemyElementTelegraph>();
   var palette=new SerializedObject(session.Walker!=null&&session.Walker.Wiring!=null?(Object)session.Walker.Wiring:session).FindProperty("_palette")?.objectReferenceValue as ElementPaletteSO;
   if(palette==null)palette=AssetDatabase.LoadAssetAtPath<ElementPaletteSO>(FallbackPalette);
   tel.Configure(set,AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath),palette);
   var tso=new SerializedObject(tel);tso.FindProperty("_organs").objectReferenceValue=set;tso.FindProperty("_timing").objectReferenceValue=AssetDatabase.LoadAssetAtPath<EnemyTelegraphTimingSO>(TimingPath);tso.FindProperty("_palette").objectReferenceValue=palette;tso.ApplyModifiedPropertiesWithoutUndo();
   sb.AppendLine("  actor "+PathOf(go.transform)+" at "+V3(go.transform.position)+", organs "+organs.Length+", avatar human "+(animator.avatar!=null&&animator.avatar.isHuman));
   rec.actorPath=PathOf(go.transform);

   // 6. content encounter (copy of the template's, not respawned on rest)
   var content=session.Content;var spec=Array.Find(content.Encounters,e=>e.Id==TemplateId);
   rec.contentPath=AssetDatabase.GetAssetPath(content);
   if(!Array.Exists(content.Encounters,e=>e.Id==MineTutorialProfileSO.BossId))
   {
    var e2=new WorldMacroPlaytestSO.Encounter{Id=MineTutorialProfileSO.BossId,ContentId=spec!=null?spec.ContentId:"mine_beast",Feet=nav.position,Patrol=enc.PatrolPoints,
     Ranged=false,RespawnOnRest=false,Detection=enc.DetectionRange,Leash=enc.Leash,Speed=enc.Speed,Activation=spec!=null?spec.Activation:180};
    content.Encounters=content.Encounters.Concat(new[]{e2}).ToArray();EditorUtility.SetDirty(content);rec.encounterAdded=true;
    sb.AppendLine("  encounter "+MineTutorialProfileSO.BossId+" added to "+rec.contentPath);
   }

   // 7. session links
   if(!session.Actors.Contains(enc)){session.Actors=session.Actors.Concat(new[]{enc}).ToArray();rec.actorAdded=true;}
   if(session.MineTutorialProfile==null){session.MineTutorialProfile=tutProfile;rec.sessionProfileSet=true;}
   rec.wiringAdded=SetWired(session,vit,true);sb.AppendLine("  combat wiring target "+(rec.wiringAdded?"added":"already present"));
   EditorUtility.SetDirty(session);
   Directory.CreateDirectory(Path.GetDirectoryName(recPath));File.WriteAllText(recPath,JsonUtility.ToJson(rec,true));
   EditorSceneManager.MarkSceneDirty(s);EditorSceneManager.SaveScene(s);if(rec.encounterAdded)AssetDatabase.SaveAssetIfDirty(content);
   sb.AppendLine("  saved; record "+recPath);
   return sb.ToString();
  }

  // #308 organs (SPEC-TELEGRAPH-ORGAN-308): crystal mask on the body skin × organ sphere, shoulders Core (lit together), surface precomputed.
  // actor = the boss root (its forward = the authored front), body = the skinned body. A missing mask leaves Sphere (Temporary Exception).
  internal static EnemyOrganSet.Organ[] BuildOrgans(Transform actor,Animator animator,Renderer body,OrganSurface308.SurfaceCache cache,StringBuilder sb)
  {
   bool ownCache=cache==null;if(ownCache)cache=new OrganSurface308.SurfaceCache();
   try
   {
    var organs=new List<EnemyOrganSet.Organ>();
    var mask=AssetDatabase.LoadAssetAtPath<Texture2D>(OrganSurface308.CrystalMask);
    if(mask==null)sb.AppendLine("  WARN crystal mask "+OrganSurface308.CrystalMask+" missing: organs use the Sphere region (Temporary Exception — deploy the mask, OrganSurface308 masks, then apply again)");
    float threshold=OrganSurface308.MaskThreshold();Vector3 centre=body!=null?body.bounds.center:actor.position+Vector3.up*1.25f;
    void Organ(string id,EnemyOrganRole role,HumanBodyBones bone,Vector3 worldLift,float radius,params string[] keys)
    {
     var t=animator!=null&&animator.isHuman?animator.GetBoneTransform(bone):null;if(t==null){sb.AppendLine("  WARN no bone "+bone+" for "+id);return;}
     var o=new EnemyOrganSet.Organ{Id=id,Role=role,Anchor=t,LocalOffset=t.InverseTransformVector(worldLift),Radius=radius,AttackKeys=keys,HumanBone=bone,
      TargetRenderer=body,Mode=mask!=null?EnemyOrganSurfaceMode.Mask:EnemyOrganSurfaceMode.Sphere,Mask=mask};
     string note=OrganSurface308.ComputeSurface(o,actor,centre,threshold,cache,false,out float cover);
     sb.AppendLine("  organ "+id+" "+role+" "+o.Mode+" cover "+cover.ToString("0.###")+(note.Length>0?" "+note:""));
     organs.Add(o);
    }
    var fwd=actor.forward;var up=Vector3.up;
    Organ("shoulder_l",EnemyOrganRole.Core,HumanBodyBones.LeftUpperArm,up*.16f,.16f,EnemyOrganSet.KeyGround);
    Organ("shoulder_r",EnemyOrganRole.Core,HumanBodyBones.RightUpperArm,up*.16f,.16f,EnemyOrganSet.KeyGround);
    Organ("jaw",EnemyOrganRole.Mouth,HumanBodyBones.Head,fwd*.16f-up*.02f,.1f,EnemyOrganSet.KeyProjectile);
    var hand=animator!=null&&animator.isHuman?animator.GetBoneTransform(HumanBodyBones.LeftHand):null;var lower=animator!=null&&animator.isHuman?animator.GetBoneTransform(HumanBodyBones.LeftLowerArm):null;
    Organ("forearm_l",EnemyOrganRole.Weapon,HumanBodyBones.LeftLowerArm,(hand!=null&&lower!=null?(hand.position-lower.position)*.45f:Vector3.zero)+up*.06f,.11f,EnemyOrganSet.KeyProjectile);
    sb.AppendLine("  "+OrganSurface308.FrontReport(actor,organs,out _));
    return organs.ToArray();
   }
   finally{if(ownCache)cache.Dispose();}
  }

  // CombatLoopWiring._enemies = lock-on candidates + the controllers that receive the ParryJudge (Init) + parry-progress targets
  static bool SetWired(WorldMacroPlaytestSession session,EnemyVitals vit,bool add)
  {
   var wiring=session.Walker!=null?session.Walker.Wiring:null;if(wiring==null||vit==null)throw new InvalidOperationException("no CombatLoopWiring on the session walker");
   var so=new SerializedObject(wiring);var arr=so.FindProperty("_enemies");int at=-1;
   for(int i=0;i<arr.arraySize;i++)if(arr.GetArrayElementAtIndex(i).objectReferenceValue==vit){at=i;break;}
   if(add){if(at>=0)return false;arr.arraySize++;arr.GetArrayElementAtIndex(arr.arraySize-1).objectReferenceValue=vit;}
   else{if(at<0)return false;arr.GetArrayElementAtIndex(at).objectReferenceValue=null;arr.DeleteArrayElementAtIndex(at);}
   so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(wiring);return true;
  }

  // follow-up for a scene applied before the wiring step existed (record updated so revert removes it)
  static string Wire(string path)
  {
   var s=Open(path,out string err);if(!s.IsValid())return err;
   string recPath=Record(path);if(!File.Exists(recPath))return "REFUSED no apply record "+recPath;
   var rec=JsonUtility.FromJson<Rec>(File.ReadAllText(recPath));
   var session=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();
   var boss=FindActor(s,MineTutorialProfileSO.BossId);if(session==null||boss==null)return "REFUSED session or boss missing";
   bool added=SetWired(session,boss.GetComponent<EnemyVitals>(),true);rec.wiringAdded|=added;
   File.WriteAllText(recPath,JsonUtility.ToJson(rec,true));EditorSceneManager.MarkSceneDirty(s);EditorSceneManager.SaveScene(s);
   return path+": combat wiring target "+(added?"added":"already present");
  }

  static string Revert(string path)
  {
   var s=Open(path,out string err);if(!s.IsValid())return err;
   string recPath=Record(path);if(!File.Exists(recPath))return "REFUSED no record "+recPath;
   var rec=JsonUtility.FromJson<Rec>(File.ReadAllText(recPath));var sb=new StringBuilder(path+" (revert)\n");
   var session=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();
   var boss=FindActor(s,MineTutorialProfileSO.BossId);
   if(session!=null&&boss!=null&&rec.actorAdded){session.Actors=session.Actors.Where(a=>a!=boss).ToArray();sb.AppendLine("  session actor removed");}
   if(session!=null&&rec.sessionProfileSet){session.MineTutorialProfile=null;sb.AppendLine("  tutorial profile unset");}
   if(session!=null)EditorUtility.SetDirty(session);
   if(session!=null&&boss!=null&&rec.wiringAdded&&SetWired(session,boss.GetComponent<EnemyVitals>(),false))sb.AppendLine("  combat wiring target removed");
   if(boss!=null){Object.DestroyImmediate(boss.gameObject);sb.AppendLine("  actor destroyed");}
   if(rec.encounterAdded&&session!=null&&session.Content!=null)
   {session.Content.Encounters=session.Content.Encounters.Where(e=>e.Id!=MineTutorialProfileSO.BossId).ToArray();EditorUtility.SetDirty(session.Content);sb.AppendLine("  encounter removed");}
   EditorSceneManager.MarkSceneDirty(s);EditorSceneManager.SaveScene(s);if(rec.encounterAdded&&session!=null&&session.Content!=null)AssetDatabase.SaveAssetIfDirty(session.Content);File.Delete(recPath);
   return sb.ToString();
  }
 }
}
