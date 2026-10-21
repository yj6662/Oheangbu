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
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-63 - field enemies in all five realms and name-only sub NPC spots [TEST]. Data: Tools/Art/populate308.py ->
 // Art/World/Compact/Rebuild/Populate308/plan.json (what and how many: Tools/Unity/Stage308_populate/populate308.json).
 //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Content308 Populate "<command>"
 //   status   counts (read only)
 //   apply    enemies: each row = a copy of its template row in the content asset (Id / ContentId / Feet / Patrol replaced) + a clone of the
 //            template ACTOR under Populate308/Actors, added to session.Actors and CombatLoopWiring._enemies. A row without loaded NavMesh
 //            within nav_radius_m, or whose template is not in the scene, is left out and listed (the session throws at start on an actor
 //            off the NavMesh). The body, attacks and numbers are the template's: a stand-in until the realm's own model exists.
 //            sub NPCs: empty EditorOnly objects under Populate308/SubNpcSpots/<Realm>, named "<이름> (<직업>)". No body, no content row,
 //            nothing at run time: a place and a name for later.
 //   revert   removes exactly that: the root, the session / wiring links and the content rows whose Id starts with "field308/"
 //   check    one actor per field308 row, linked and wired, on the NavMesh; no field308 actor without a row
 //   attacks | attacks:revert   D308-125 [TEST]: the "kinds" list of populate308.json onto the actors already placed (nothing is re-rolled):
 //            a kind's members get its attack profile and projectile, Ranged and PreferredDistance (the brain's stop distance reads those
 //            two fields, not the profile) and the content row's Ranged; a kind with a guard gets its Content.GuardBrains entry. apply
 //            does the same to each clone it makes. The revert puts the template's values back and drops the guard entries the list
 //            names. Run it after Creatures308 swap:apply (a swap writes the species attack over it).
 // Only the main play scene and its content asset are written, both backed up first (Tools/Unity/Stage308_populate/Backup). No dialog.
 public static partial class Content308
 {
  const string PopFieldVitals308="Assets/_Project/Data/Configs/EnemyTiers/EnemyVitals_Field.asset";
  const string PopRoot308="Populate308",PopPrefix308="field308/",PopContent308="Assets/_Project/Scenes/World/Main/WorldContent_Main.asset";
  [Serializable] sealed class PopEnemy308{public string id="",name="",contentId="",template="",realm="",group="";public float[] feet=Array.Empty<float>(),patrol2=Array.Empty<float>();public bool boss;}
  [Serializable] sealed class PopNpc308{public string id="",name="",job="",realm="";public float[] pos=Array.Empty<float>();}
  [Serializable] sealed class PopPatrol308{public float walk_speed_mps=1.7f,pause_min_s=1.5f,pause_max_s=4f;}
  [Serializable] sealed class PopIdle308{public float breath_period_s=4.2f,breath_chest_deg=2.6f,breath_shoulder_m=.013f,sway_deg=.9f,combat_leg_deg=11f,combat_arm_deg=17f,combat_lean_deg=5f,combat_range_m=26f;}
  [Serializable] sealed class PopGuard308{public float share,facing_deg;}
  // members: the trailing n of "field308/<realm>_<key>_<group>_<n>"; empty = every member. projectile empty = the template's.
  // ground: an eruption kind's ground cue prefabs (warning, spike, impact) for EnemyAttackPresentation; an eruption kind must list all three.
  [Serializable] sealed class PopKind308{public string contentId="",profile="",projectile="";public int[] members=Array.Empty<int>();public PopGuard308 guard=new PopGuard308();public string[] ground=Array.Empty<string>();}
  [Serializable] sealed class PopCfg308{public PopPatrol308 patrol=new PopPatrol308();public PopIdle308 idle=new PopIdle308();public PopKind308[] kinds=Array.Empty<PopKind308>();}
  [Serializable] sealed class PopPlan308{public string version="";public float nav_radius_m=2f;public PopEnemy308[] enemies=Array.Empty<PopEnemy308>();public PopNpc308[] npcs=Array.Empty<PopNpc308>();}
  static string PopStage308=>Path.Combine(Harness303.RepoRoot,"Tools","Unity","Stage308_populate");
  static PopPlan308 PopData308()
  {
   string f=Path.Combine(Harness303.RepoRoot,"Art","World","Compact","Rebuild","Populate308","plan.json");
   if(!File.Exists(f))throw new Refuse308("no "+f+" (run python Tools/Art/populate308.py Tools/Unity/Stage308_populate/populate308.json)");
   return JsonUtility.FromJson<PopPlan308>(File.ReadAllText(f));
  }
  static Vector3 PopV308(float[] a)=>new Vector3(a[0],a[1],a[2]);
  static PopCfg308 PopCfgFile308()=>JsonUtility.FromJson<PopCfg308>(File.ReadAllText(Path.Combine(PopStage308,"populate308.json")));

  // D308-125: the kind entry that arms this actor, or null
  static PopKind308 PopKindFor308(PopCfg308 cfg,string id,string contentId)
  {
   int cut=id!=null?id.LastIndexOf('_'):-1;int n=cut>=0&&int.TryParse(id.Substring(cut+1),out int v)?v:-1;
   return (cfg.kinds??Array.Empty<PopKind308>()).FirstOrDefault(k=>k!=null&&!string.IsNullOrEmpty(k.profile)&&k.contentId==contentId&&(k.members==null||k.members.Length==0||k.members.Contains(n)));
  }
  static EnemyAttackProfileSO PopProfile308(PopKind308 k)
  {
   var p=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(k.profile);string why=null;
   if(p==null||!p.TryValidate(out why))throw new Refuse308(k.contentId+": attack profile "+k.profile+(p==null?" is missing":" is invalid ("+why+")"));
   return p;
  }
  static GameObject PopProjectile308(PopKind308 k,Component actor)
  {
   if(string.IsNullOrEmpty(k.projectile))return new SerializedObject(actor.GetComponent<EnemyController>()).FindProperty("_projectilePrefab").objectReferenceValue as GameObject;
   var g=AssetDatabase.LoadAssetAtPath<GameObject>(k.projectile);if(g==null)throw new Refuse308(k.contentId+": projectile "+k.projectile+" is missing");
   return g;
  }
  // one clone's attack: profile + projectile + legacy _attackMode on the controller, Ranged / PreferredDistance on the actor, Ranged on its
  // content row. A valid profile governs at runtime (EnemyController.Update -> TickAuthoredAttack); _attackMode follows the profile's Mode
  // (as TelegraphOrgans306 does) so the legacy fallback agrees if the profile is ever dropped.
  static void PopArm308(PrologueEncounter actor,WorldMacroPlaytestSO.Encounter row,Object profile,Object projectile,int mode,bool ranged,float preferred)
  {
   var so=new SerializedObject(actor.GetComponent<EnemyController>());so.FindProperty("_attackProfile").objectReferenceValue=profile;so.FindProperty("_projectilePrefab").objectReferenceValue=projectile;so.FindProperty("_attackMode").intValue=mode;so.ApplyModifiedPropertiesWithoutUndo();
   actor.Ranged=ranged;actor.PreferredDistance=preferred;if(row!=null)row.Ranged=ranged;EditorUtility.SetDirty(actor);
  }
  static void PopArmKind308(PrologueEncounter actor,WorldMacroPlaytestSO.Encounter row,PopKind308 k)
  {
   var p=PopProfile308(k);PopArm308(actor,row,p,PopProjectile308(k,actor),(int)p.Mode,p.Mode==EnemyController.AttackMode.RangedOnly,p.PreferredDistanceHint);
   var g=PopGround308(k,p);if(g==null)return;
   var fx=actor.GetComponent<Oheangbu.App.Demo.EnemyAttackPresentation>();if(fx==null)fx=actor.gameObject.AddComponent<Oheangbu.App.Demo.EnemyAttackPresentation>();
   fx.Configure(actor.GetComponent<EnemyController>(),g[0],g[1],g[2],p.Element);EditorUtility.SetDirty(fx);
  }
  // D308-125 적용 2: the ground cue of an eruption kind (warning ring, rising spikes, impact), shown for the profile's element; null = not an eruption
  static GameObject[] PopGround308(PopKind308 k,EnemyAttackProfileSO p)
  {
   if(p.Delivery!=EnemyAttackDelivery.GroundEruption)return null;
   if(k.ground==null||k.ground.Length!=3)throw new Refuse308(k.contentId+": an eruption kind lists its ground cue (warning, spike, impact)");
   var g=k.ground.Select(x=>AssetDatabase.LoadAssetAtPath<GameObject>(x)).ToArray();int miss=Array.IndexOf(g,null);
   if(miss>=0)throw new Refuse308(k.contentId+": ground cue "+k.ground[miss]+" is missing");
   return g;
  }
  // the guard entries of the listed kinds (one per ContentId): written on arm, dropped on revert
  static string PopGuards308(WorldMacroPlaytestSO content,PopCfg308 cfg,bool arm)
  {
   var list=new List<WorldMacroPlaytestSO.GuardBrain308>(content.GuardBrains??Array.Empty<WorldMacroPlaytestSO.GuardBrain308>());int had=0,set=0;
   foreach(var k in (cfg.kinds??Array.Empty<PopKind308>()).Where(k=>k!=null&&k.guard!=null&&k.guard.share>0f))
   {
    had+=list.RemoveAll(g=>g!=null&&g.ContentId==k.contentId);
    if(arm){list.Add(new WorldMacroPlaytestSO.GuardBrain308{ContentId=k.contentId,GuardShare=Mathf.Clamp01(k.guard.share),GuardFacingDegrees=Mathf.Clamp(k.guard.facing_deg,1f,180f)});set++;}
   }
   content.GuardBrains=list.ToArray();
   return arm?"guard entries "+set+" written ("+had+" replaced)":"guard entries "+had+" removed";
  }
  static string PopAttacks308(Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content,bool arm)
  {
   var cfg=PopCfgFile308();var plan=PopData308();var all=Saved308<PrologueEncounter>(scene).ToArray();
   // everything is looked up before the first write: a refusal leaves the scene untouched
   var work=new List<(PrologueEncounter actor,WorldMacroPlaytestSO.Encounter row,PopKind308 kind,PrologueEncounter template,WorldMacroPlaytestSO.Encounter trow)>();
   foreach(var a in all.Where(a=>a.Id!=null&&a.Id.StartsWith(PopPrefix308,StringComparison.Ordinal)))
   {
    var e=plan.enemies.FirstOrDefault(x=>x.id==a.Id);var k=e!=null?PopKindFor308(cfg,e.id,e.contentId):null;if(k==null)continue;
    var row=Encounter308(content,a.Id);if(row==null)throw new Refuse308(a.Id+" has no content row (Populate check)");
    var t=all.FirstOrDefault(x=>x.Id==e.template);var trow=Encounter308(content,e.template);
    if(arm){PopGround308(k,PopProfile308(k));PopProjectile308(k,a);}
    else if(t==null||trow==null)throw new Refuse308(a.Id+": template "+e.template+" is gone, nothing to put back");
    work.Add((a,row,k,t,trow));
   }
   foreach(var w in work)
   {
    if(arm){PopArmKind308(w.actor,w.row,w.kind);continue;}
    var tso=new SerializedObject(w.template.GetComponent<EnemyController>());
    PopArm308(w.actor,w.row,tso.FindProperty("_attackProfile").objectReferenceValue,tso.FindProperty("_projectilePrefab").objectReferenceValue,tso.FindProperty("_attackMode").intValue,w.trow.Ranged,w.template.PreferredDistance);
    var fx=w.actor.GetComponent<Oheangbu.App.Demo.EnemyAttackPresentation>();
    if(fx!=null&&w.template.GetComponent<Oheangbu.App.Demo.EnemyAttackPresentation>()==null){Object.DestroyImmediate(fx);EditorUtility.SetDirty(w.actor.gameObject);}
   }
   return (arm?"armed ":"put back ")+work.Count+" actors ("+string.Join(", ",work.Select(w=>w.actor.Id.Substring(PopPrefix308.Length)))+"), "+PopGuards308(content,cfg,arm);
  }

  public static string Populate(string command)
  {
   string c=(command??"").Trim();
   try
   {
    var scene=SceneManager.GetActiveScene();if(scene.name!="W_Demo_Main")return "REFUSED open W_Demo_Main first (active scene is "+scene.name+")";
    var session=Saved308<WorldMacroPlaytestSession>(scene).FirstOrDefault();if(session==null)return "REFUSED no WorldMacroPlaytestSession in the play scene";
    var content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(PopContent308);if(content==null||session.Content!=content)return "REFUSED the session does not read "+PopContent308;
    if(c=="status")return PopStatus308(scene,session,content);
    if(c=="check")return PopCheck308(scene,session,content);
    if(c.StartsWith("front:",StringComparison.Ordinal))return PopFront308(c.Substring(6),scene,session,content);      // D308-117 C3 (Content308.DungeonFront.cs)
    if(EditorApplication.isPlayingOrWillChangePlaymode)return "REFUSED Edit mode only (Play is running)";
    if(EditorApplication.isCompiling||EditorUtility.scriptCompilationFailed)return "REFUSED scripts are compiling or failed to compile";
    if(scene.isDirty)return "REFUSED the play scene has unsaved changes";
    if(c=="finals:on"||c=="finals:off")      // D308-53's presentation switch on the content asset: every final counts as owned for the session (nothing is written to a save)
    {
     bool before=content.DemoAllFinals;content.DemoAllFinals=c=="finals:on";EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);
     return "Content308.Populate "+c+": DemoAllFinals "+before+" -> "+content.DemoAllFinals+" in "+PopContent308;
    }
    if(c=="idle")      // D308-67: the player's idle numbers -> the profile asset; the lean component learns its session (combat idle)
    {
     var ic=JsonUtility.FromJson<PopCfg308>(File.ReadAllText(Path.Combine(PopStage308,"populate308.json"))).idle;int n=0;
     foreach(var lean in Saved308<WorldMacroPlayerLean303>(scene))
     {
      lean.Session=session;EditorUtility.SetDirty(lean);n++;var pf=lean.Profile;if(pf==null)continue;
      pf.BreathPeriodSeconds=ic.breath_period_s;pf.BreathChestPitchDegrees=ic.breath_chest_deg;pf.BreathShoulderRiseMeters=ic.breath_shoulder_m;pf.IdleSwayDegrees=ic.sway_deg;
      pf.CombatLegSpreadDegrees=ic.combat_leg_deg;pf.CombatArmSpreadDegrees=ic.combat_arm_deg;pf.CombatLeanDegrees=ic.combat_lean_deg;pf.CombatRangeMeters=ic.combat_range_m;EditorUtility.SetDirty(pf);AssetDatabase.SaveAssetIfDirty(pf);
     }
     EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))return "FAILED the play scene was not saved";
     return "Content308.Populate idle: "+n+" lean components wired to the session, profile numbers written (breath "+ic.breath_chest_deg+" deg / "+ic.breath_shoulder_m+" m, combat legs "+ic.combat_leg_deg+" deg, arms "+ic.combat_arm_deg+" deg)";
    }
    if(c=="patrol"||c=="patrol:revert")      // D308-66: every ordinary actor (no boss controller) walks its patrol and pauses at the points
    {
     var pc=JsonUtility.FromJson<PopCfg308>(File.ReadAllText(Path.Combine(PopStage308,"populate308.json"))).patrol;int n=0;
     foreach(var a in Saved308<PrologueEncounter>(scene))
     {
      if(a.GetComponent<Oheangbu.App.Demo.CheongryongCombatController>()!=null||a.GetComponent<Oheangbu.App.Demo.SouthGateGeneralController>()!=null||a.GetComponent<Oheangbu.App.Demo.MineBossController>()!=null)continue;
      bool on=c=="patrol";a.PatrolSpeedScale=on?Mathf.Clamp(pc.walk_speed_mps/Mathf.Max(.1f,a.Speed),.1f,1f):1f;a.PatrolPauseMin=on?pc.pause_min_s:0f;a.PatrolPauseMax=on?pc.pause_max_s:0f;EditorUtility.SetDirty(a);n++;
     }
     EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))return "FAILED the play scene was not saved";
     return "Content308.Populate "+c+": "+n+" actors (walk "+pc.walk_speed_mps+" m/s, pause "+pc.pause_min_s+"-"+pc.pause_max_s+" s)";
    }
    bool attacks=c=="attacks"||c=="attacks:revert";
    if(c!="apply"&&c!="revert"&&!attacks)return "REFUSED unknown Content308.Populate command '"+c+"' (status | apply | revert | check | patrol | patrol:revert | attacks | attacks:revert)";
    if(c=="apply")PopKindsValid308(PopCfgFile308());
    string dir=Path.Combine(PopStage308,"Backup",DateTime.Now.ToString("yyyyMMddTHHmmss")+"_"+c.Replace(':','-'));Directory.CreateDirectory(dir);
    File.Copy(Path.GetFullPath(scene.path),Path.Combine(dir,Path.GetFileName(scene.path)),true);File.Copy(Path.GetFullPath(PopContent308),Path.Combine(dir,Path.GetFileName(PopContent308)),true);
    var sb=new StringBuilder("Content308.Populate "+c+", backup "+dir);
    if(attacks)sb.Append("\n  ").Append(PopAttacks308(scene,session,content,c=="attacks"));
    else sb.Append("\n  removed first: ").Append(PopRemove308(scene,session,content));
    if(c=="revert")sb.Append(", ").Append(PopGuards308(content,PopCfgFile308(),false));
    if(c=="apply")PopApply308(scene,session,content,sb);
    EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);EditorUtility.SetDirty(session);
    EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))return "FAILED the play scene was not saved";
    return sb.Append('\n').Append(PopCheck308(scene,session,content)).ToString();
   }
   catch(Refuse308 r){return "REFUSED "+r.Message;}
   catch(Exception e){return "FAILED "+e;}
  }

  static string PopRemove308(Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   int actors=0,rows=0;
   foreach(var a in (session.Actors??Array.Empty<PrologueEncounter>()).Where(a=>a!=null&&a.Id!=null&&a.Id.StartsWith(PopPrefix308,StringComparison.Ordinal)).ToArray()){SetWired308(session,a.GetComponent<EnemyVitals>(),false);actors++;}
   session.Actors=(session.Actors??Array.Empty<PrologueEncounter>()).Where(a=>a!=null&&!(a.Id!=null&&a.Id.StartsWith(PopPrefix308,StringComparison.Ordinal))).ToArray();
   var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==PopRoot308);if(old!=null)Object.DestroyImmediate(old);
   var keep=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null&&!(e.Id!=null&&e.Id.StartsWith(PopPrefix308,StringComparison.Ordinal))).ToArray();
   rows=(content.Encounters?.Length??0)-keep.Length;content.Encounters=keep;
   return actors+" actors, "+rows+" content rows"+(old!=null?", root":"");
  }

  static void PopApply308(Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content,StringBuilder sb)
  {
   var data=PopData308();var root=new GameObject(PopRoot308);SceneManager.MoveGameObjectToScene(root,scene);
   var actorsHome=new GameObject("Actors").transform;actorsHome.SetParent(root.transform,false);var spots=new GameObject("SubNpcSpots").transform;spots.SetParent(root.transform,false);
   var all=Saved308<PrologueEncounter>(scene).ToArray();var rows=new List<WorldMacroPlaytestSO.Encounter>(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>());
   var added=new List<PrologueEncounter>();var demoted=new List<string>();int placed=0;var left=new List<string>();var perRealm=new Dictionary<string,int>();
   var cfg=PopCfgFile308();var armed=new List<string>();
   foreach(var e in data.enemies)
   {
    var template=all.FirstOrDefault(x=>x.Id==e.template);var trow=Encounter308(content,e.template);
    if(template==null||trow==null){left.Add(e.id+" (template "+e.template+(template==null?" has no actor in the scene":" has no content row")+")");continue;}
    if(!NavMesh.SamplePosition(PopV308(e.feet),out var hit,data.nav_radius_m,NavMesh.AllAreas)){left.Add(e.id+" (no NavMesh within "+data.nav_radius_m+" m)");continue;}
    var feet=hit.position;var p2=NavMesh.SamplePosition(PopV308(e.patrol2),out var hit2,data.nav_radius_m*2f,NavMesh.AllAreas)?hit2.position:feet;
    rows.Add(new WorldMacroPlaytestSO.Encounter{Id=e.id,ContentId=e.contentId,Feet=feet,Patrol=new[]{feet,p2},Ranged=trow.Ranged,RespawnOnRest=true,Detection=trow.Detection,Leash=trow.Leash,Speed=trow.Speed,Activation=trow.Activation});
    var agent=template.GetComponent<NavMeshAgent>();var at=feet+Vector3.up*(agent!=null?agent.baseOffset:0f);
    var go=Object.Instantiate(template.gameObject);SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(actorsHome,true);go.name=e.id.Substring(PopPrefix308.Length)+" ["+e.name+"]";
    go.transform.localScale=template.transform.lossyScale;go.transform.SetPositionAndRotation(at,Quaternion.Euler(0f,Mathf.Atan2(p2.x-feet.x,p2.z-feet.z)*Mathf.Rad2Deg,0f));go.SetActive(template.gameObject.activeSelf);
    var actor=go.GetComponent<PrologueEncounter>();actor.Id=e.id;actor.Ranged=trow.Ranged;actor.DetectionRange=trow.Detection;actor.Leash=trow.Leash;actor.Speed=trow.Speed;actor.PatrolPoints=new[]{feet,p2};
    // #308 fix1 (SPEC-WORLD-HANYANG-308 section 19 F5): a clone of a BOSS template is an ordinary field enemy unless its plan row says boss:true -
    // the template's boss vitals profile (boss bar, boss HP, vehicle field) is replaced by the field tier profile.
    var cloneVitals=go.GetComponent<EnemyVitals>();
    if(cloneVitals!=null&&cloneVitals.IsBoss&&!e.boss)
    {
     var fieldProfile=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(PopFieldVitals308);if(fieldProfile==null||fieldProfile.IsBoss)throw new Refuse308("no field vitals profile at "+PopFieldVitals308);
     var so=new SerializedObject(cloneVitals);so.FindProperty("_profile").objectReferenceValue=fieldProfile;so.ApplyModifiedPropertiesWithoutUndo();demoted.Add(e.id);
    }
    var kind=PopKindFor308(cfg,e.id,e.contentId);if(kind!=null){PopArmKind308(actor,rows[rows.Count-1],kind);armed.Add(e.id.Substring(PopPrefix308.Length));}      // D308-125
    added.Add(actor);SetWired308(session,go.GetComponent<EnemyVitals>(),true);placed++;perRealm[e.realm]=perRealm.TryGetValue(e.realm,out int n)?n+1:1;
   }
   content.Encounters=rows.ToArray();session.Actors=(session.Actors??Array.Empty<PrologueEncounter>()).Concat(added).ToArray();
   string guards=PopGuards308(content,cfg,true);
   int npcs=0;
   foreach(var p in data.npcs)
   {
    var home=spots.Find(p.realm);if(home==null){home=new GameObject(p.realm).transform;home.SetParent(spots,false);}
    var g=new GameObject(p.name+" ("+p.job+")");g.transform.SetParent(home,false);g.transform.position=PopV308(p.pos);g.tag="EditorOnly";npcs++;
   }
   sb.Append("\n  enemies placed ").Append(placed).Append(" of ").Append(data.enemies.Length).Append(" (").Append(string.Join(", ",perRealm.OrderBy(k=>k.Key).Select(k=>k.Key+" "+k.Value))).Append("), sub NPC spots ").Append(npcs);
   foreach(string s in left)sb.Append("\n  LEFT OUT ").Append(s);
   if(demoted.Count>0)sb.Append("\n  boss template -> field vitals (").Append(PopFieldVitals308).Append("): ").Append(string.Join(", ",demoted));
   sb.Append("\n  attacks armed ").Append(armed.Count).Append(armed.Count>0?" ("+string.Join(", ",armed)+")":"").Append(", ").Append(guards);
  }
  // the listed kinds' assets, before anything in the scene is touched
  static void PopKindsValid308(PopCfg308 cfg)
  {
   foreach(var k in (cfg.kinds??Array.Empty<PopKind308>()).Where(k=>k!=null&&!string.IsNullOrEmpty(k.profile)))
   {
    PopProfile308(k);
    if(!string.IsNullOrEmpty(k.projectile)&&AssetDatabase.LoadAssetAtPath<GameObject>(k.projectile)==null)throw new Refuse308(k.contentId+": projectile "+k.projectile+" is missing");
   }
  }

  static string PopStatus308(Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==PopRoot308);var spots=root!=null?root.transform.Find("SubNpcSpots"):null;
   return "Content308.Populate status: content rows "+(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Count(e=>e!=null&&e.Id.StartsWith(PopPrefix308,StringComparison.Ordinal))
    +", actors "+(session.Actors??Array.Empty<PrologueEncounter>()).Count(a=>a!=null&&a.Id!=null&&a.Id.StartsWith(PopPrefix308,StringComparison.Ordinal))+", sub NPC spots "+(spots!=null?spots.Cast<Transform>().Sum(t=>t.childCount):0)+", root "+(root!=null?"present":"absent");
  }

  static string PopCheck308(Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content)
  {
   var sb=new StringBuilder("Content308.Populate check\n");int pass=0,fail=0;void C(bool ok,string text){sb.Append(ok?"  [PASS] ":"  [FAIL] ").Append(text).Append('\n');if(ok)pass++;else fail++;}
   var rows=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null&&e.Id!=null&&e.Id.StartsWith(PopPrefix308,StringComparison.Ordinal)).ToArray();
   var actors=Saved308<PrologueEncounter>(scene).Where(a=>a.Id!=null&&a.Id.StartsWith(PopPrefix308,StringComparison.Ordinal)).ToArray();
   C(rows.Select(r=>r.Id).Distinct().Count()==rows.Length&&rows.All(r=>actors.Count(a=>a.Id==r.Id)==1)&&actors.All(a=>rows.Any(r=>r.Id==a.Id)),"one actor per field row and no actor without a row ("+rows.Length+" rows, "+actors.Length+" actors)");
   C(actors.All(a=>(session.Actors??Array.Empty<PrologueEncounter>()).Contains(a)&&Wired308(session,a.GetComponent<EnemyVitals>())),"every field actor is in session.Actors and wired for combat");
   int off=actors.Count(a=>{var ag=a.GetComponent<NavMeshAgent>();return ag==null||a.GetComponent<EnemyVitals>()==null||!NavMesh.SamplePosition(a.transform.position-Vector3.up*ag.baseOffset,out _,2f,NavMesh.AllAreas);});
   C(off==0,"every field actor has an agent, vitals and loaded NavMesh within 2 m (the session's start rule) - off: "+off);
   var all=session.Actors??Array.Empty<PrologueEncounter>();C(all.All(a=>a!=null)&&all.Select(a=>a.Id).Distinct().Count()==all.Length,"session.Actors has no hole and no duplicate id ("+all.Length+")");
   var askedBoss=new HashSet<string>();try{foreach(var e in PopData308().enemies)if(e.boss)askedBoss.Add(e.id);}catch(Refuse308){}
   var bossy=actors.Where(a=>{var v=a.GetComponent<EnemyVitals>();return v!=null&&v.IsBoss&&!askedBoss.Contains(a.Id);}).Select(a=>a.Id).ToArray();
   C(bossy.Length==0,"no field308/ actor carries a boss vitals profile unless its plan row says boss:true (asked "+askedBoss.Count+") - boss: "+(bossy.Length==0?"none":string.Join(", ",bossy)));
   var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==PopRoot308);var spots=root!=null?root.transform.Find("SubNpcSpots"):null;
   C(spots==null||spots.GetComponentsInChildren<Component>(true).All(x=>x is Transform),"sub NPC spots are names only: no component, no body");
   // D308-125: every row a kind lists carries that kind's profile and projectile, Ranged agrees on profile / actor / row, the stop distance is inside the reach
   PopCfg308 cfg=null;try{cfg=PopCfgFile308();}catch(Exception){}
   var kinds=(cfg?.kinds??Array.Empty<PopKind308>()).Where(k=>k!=null&&!string.IsNullOrEmpty(k.profile)).ToArray();
   if(kinds.Length>0&&rows.Length>0)
   {
    var bad=new List<string>();int armed=0,patched=0;
    foreach(var r in rows)
    {
     var k=PopKindFor308(cfg,r.Id,r.ContentId);if(k==null)continue;armed++;var a=actors.FirstOrDefault(x=>x.Id==r.Id);var ctrl=a!=null?a.GetComponent<EnemyController>():null;
     if(ctrl==null){bad.Add(r.Id+" (no actor / controller)");continue;}
     var so=new SerializedObject(ctrl);var p=so.FindProperty("_attackProfile").objectReferenceValue as EnemyAttackProfileSO;var proj=so.FindProperty("_projectilePrefab").objectReferenceValue as GameObject;
     string why=null;
     if(p==null||AssetDatabase.GetAssetPath(p)!=k.profile)why="profile "+(p!=null?AssetDatabase.GetAssetPath(p):"none");
     else if(!p.TryValidate(out string err))why="profile invalid: "+err;
     else if(a.Ranged!=(p.Mode==EnemyController.AttackMode.RangedOnly)||r.Ranged!=a.Ranged)why="Ranged actor "+a.Ranged+" / row "+r.Ranged+" / profile "+p.Mode;
     else if(so.FindProperty("_attackMode").intValue!=(int)p.Mode)why="legacy _attackMode "+(EnemyController.AttackMode)so.FindProperty("_attackMode").intValue+" vs profile "+p.Mode;
     else if(!(a.PreferredDistance>0f&&a.PreferredDistance<=p.Range))why="stop distance "+a.PreferredDistance+" outside (0, "+p.Range+"]";
     else if(!string.IsNullOrEmpty(k.projectile)&&(proj==null||AssetDatabase.GetAssetPath(proj)!=k.projectile))why="projectile "+(proj!=null?AssetDatabase.GetAssetPath(proj):"none");
     else if((p.Delivery==EnemyAttackDelivery.HomingProjectile||p.Delivery==EnemyAttackDelivery.AimedProjectile)&&proj==null)why=p.Delivery+" without a projectile";   // an eruption needs none
     else if(p.Delivery==EnemyAttackDelivery.GroundEruption&&!(a.GetComponent<Oheangbu.App.Demo.EnemyAttackPresentation>() is var gfx&&gfx!=null&&gfx.HasAllSources&&gfx.GroundElement==p.Element))why="eruption without its "+p.Element+" ground cue";
     if(why==null&&p.Patch!=null){patched++;if(p.Patch.Look308==null||p.Patch.ScorchLook308==null)why="patch "+p.Patch.name+" without its look";}   // D308-125 5 (c): an armed patch is never invisible
     if(why!=null)bad.Add(r.Id.Substring(PopPrefix308.Length)+" ("+why+")");
    }
    C(patched>=1,"D308-125 ember patch: "+patched+" armed actors lay a ground patch (the main world needs at least one)");
    var unused=kinds.Where(k=>!rows.Any(r=>PopKindFor308(cfg,r.Id,r.ContentId)==k)).Select(k=>k.contentId).ToArray();
    C(bad.Count==0&&unused.Length==0,"D308-125 armed kinds: "+armed+" actors carry their kind's profile / projectile / Ranged / stop distance"+(bad.Count>0?" - wrong: "+string.Join(", ",bad):"")+(unused.Length>0?" - kinds that arm no row: "+string.Join(", ",unused):""));
    var guards=content.GuardBrains??Array.Empty<WorldMacroPlaytestSO.GuardBrain308>();
    var gbad=kinds.Where(k=>k.guard!=null&&k.guard.share>0f).Where(k=>{var g=guards.Where(x=>x!=null&&x.ContentId==k.contentId).ToArray();return g.Length!=1||!Mathf.Approximately(g[0].GuardShare,Mathf.Clamp01(k.guard.share))||!Mathf.Approximately(g[0].GuardFacingDegrees,Mathf.Clamp(k.guard.facing_deg,1f,180f));}).Select(k=>k.contentId).ToArray();
    C(gbad.Length==0,"D308-125 guard entries: one per guarded kind with its share / facing"+(gbad.Length>0?" - wrong: "+string.Join(", ",gbad):""));
   }
   return sb.Append(pass).Append(" checks, ").Append(fail).Append(" failed").ToString();
  }
 }
}
