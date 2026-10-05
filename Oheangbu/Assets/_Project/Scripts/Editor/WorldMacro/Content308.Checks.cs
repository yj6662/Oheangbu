using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // checks (data only, nothing saved) + the two #308 combat profiles (§8) and their optional scene wiring.
 public static partial class Content308
 {
  const string ProfileDir308="Assets/_Project/Art/World/Pacing308/Data";
  const string FireRanged306="Assets/_Project/Art/Telegraph306/FireRanged306.asset";
  const string MineRanged308=ProfileDir308+"/MineRangedNeutral308.asset",FieldBoss308=ProfileDir308+"/EnemyVitals_FieldBoss308.asset";
  static readonly string[] Forbidden308={"[F]","[G]","세요","습니다","탑승하면","출발한다","가마꾼"};

  sealed class Check308
  {
   public readonly StringBuilder Text=new StringBuilder();public int Pass,Fail,Info;
   public void C(bool ok,string what){Text.AppendLine((ok?"PASS ":"FAIL ")+what);if(ok)Pass++;else Fail++;}
   public void I(string what){Text.AppendLine("INFO "+what);Info++;}
   public string Done(string file)
   {
    string head="PASS "+Pass+" / FAIL "+Fail+" / INFO "+Info;
    Directory.CreateDirectory(Out308);File.WriteAllText(Path.Combine(Out308,file),head+"\n"+Text);
    return head+" -> "+Path.Combine(Out308,file)+"\n"+Text;
   }
  }

  // ---------- checks:campaign (AC-C1, C9, C10, C14) ----------

  static string ChecksCampaign308()
  {
   var k=new Check308();var profile=Campaign308();var main=Load308<WorldMacroPlaytestSO>(Targets308[MainScene].content);
   var cr=profile.FindStage("cheongryong");
   k.C(cr!=null&&(cr.PrerequisiteIds??Array.Empty<string>()).SequenceEqual(CheongryongPrereqs308()),"AC-C1 cheongryong.PrerequisiteIds == ["+string.Join(", ",CheongryongPrereqs308())+"] (now ["+string.Join(",",cr?.PrerequisiteIds??Array.Empty<string>())+"])");
   k.C(profile.IsValid,"AC-C1 profile.IsValid");
   // fixed test: a fresh state (InitialCompletedIds + their facts) cannot complete 청룡 even with the kill on record; J1 + W1 can
   var fresh=new DemoCampaignState{CampaignId=profile.CampaignId};
   foreach(var id in profile.InitialCompletedIds??Array.Empty<string>())Complete308(profile,fresh,id);
   var killed=new List<string>{"cheongryong"};
   k.C(!DemoCampaignProgression.TryAdvance(profile,fresh,DemoEventKind.BossDefeated,"cheongryong",out _,out _,killed),"AC-C1 TryAdvance(BossDefeated, cheongryong) refused without J1 / W1");
   var ready=fresh.Copy();Complete308(profile,ready,"relay");
   k.C(!DemoCampaignProgression.TryAdvance(profile,ready,DemoEventKind.BossDefeated,"cheongryong",out _,out _,killed),"AC-C1 refused with J1 only");
   Complete308(profile,ready,"cargo_contract");
   GateChecks308(k,profile,ready,killed);   // D308-16c: with the gate delta on, J1 + W1 alone is refused; the lesson is then completed in `ready`
   k.C(DemoCampaignProgression.TryAdvance(profile,ready,DemoEventKind.BossDefeated,"cheongryong",out var after,out int reward,killed),"AC-C1 accepted with ["+string.Join(" + ",CheongryongPrereqs308())+"] (reward "+reward+")");
   if(after!=null)k.C(after.Facts.Contains(VehicleFact308),"AC-C4 cheongryong grants "+VehicleFact308+" (the vehicle gate fact)");
   var escort=profile.FindStage("escort");
   k.C(escort!=null&&(escort.PrerequisiteIds??Array.Empty<string>()).Contains("cargo_contract")&&escort.PrerequisiteIds.Contains("cheongryong"),"§1 escort.PrerequisiteIds still [cargo_contract, cheongryong]");
   // AC-C9 testimonies
   string Say(params string[] facts){var s=new DemoCampaignState{CampaignId=profile.CampaignId,Facts=facts.ToList()};return profile.DialogueFor("jeongdam_j1",s,"(fallback)");}
   k.C(Say("evidence:logging_growth","defeated:cheongryong")==JeongdamCombined308,"AC-C9 정담 with logging + 청룡 facts = combined testimony");
   string onlyCheong=Say("defeated:cheongryong");
   k.C(onlyCheong!=JeongdamCombined308&&onlyCheong!="(fallback)"&&onlyCheong.Contains("숲에서 소리가 끊겼"),"AC-C9 정담 with 청룡 only = 청룡 testimony ("+Clip308(onlyCheong)+")");
   string onlyLogging=Say("evidence:logging_growth");
   k.C(onlyLogging!=JeongdamCombined308&&onlyLogging.Contains("도끼 자국"),"AC-C9 정담 with logging only = logging testimony ("+Clip308(onlyLogging)+")");
   var logging=profile.FindStage("logging");
   k.C(logging!=null&&logging.Implemented&&logging.TongboReward==80&&(logging.PrerequisiteIds?.Length??0)==0&&(logging.GrantedFacts??Array.Empty<string>()).Contains("evidence:logging_growth"),"AC-C9 logging stage: implemented, 80 통보, no prerequisite, grants evidence:logging_growth");
   k.C(Array.Exists(main.Points??Array.Empty<PrologueContentSO.Point>(),p=>p!=null&&p.Id=="logging_inquiry"),"AC-C9 logging_inquiry point in WorldContent_Main");
   // AC-C10
   foreach(var s in profile.Stages)
   {
    if(s==null)continue;string o=s.Objective??"";
    var bad=new[]{"[F]","[G]","관청"}.Where(o.Contains).ToArray();
    if(bad.Length>0)k.C(false,"AC-C10 "+s.Id+".Objective contains "+string.Join(" ",bad));
   }
   k.C(profile.Stages.All(s=>s==null||!new[]{"[F]","[G]","관청"}.Any((s.Objective??"").Contains)),"AC-C10 no Objective holds [F] / [G] / 관청");
   k.C(profile.FindStage("office_report")!=null&&!profile.FindStage("office_report").Implemented,"AC-C10 office_report.Implemented 0");
   k.C((profile.Acts??Array.Empty<DemoCampaignProfile.Act>()).All(a=>a==null||!(a.Journey??"").Contains("관청")),"AC-C10 no Act journey holds 관청");
   foreach(var (id,pointId,encounterId) in DestinationsNow308())
   {
    var s=profile.FindStage(id);if(s==null){k.C(false,"AC-C10 stage "+id+" missing");continue;}
    Vector3? at=null;
    if(pointId!=null){var p=Array.Find(main.Points??Array.Empty<PrologueContentSO.Point>(),x=>x!=null&&x.Id==pointId);if(p!=null)at=p.Position;}
    else{var e=Array.Find(main.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>(),x=>x!=null&&x.Id==encounterId);if(e!=null)at=e.Feet;}
    if(at==null){k.C(false,"AC-C10 "+id+": "+(pointId??encounterId)+" not in WorldContent_Main");continue;}
    float xz=Harness303.Flat(s.Destination,at.Value),dy=Mathf.Abs(s.Destination.y-at.Value.y);
    if(id=="ending")k.C(dy<=2f,"AC-C10 ending (gate, no point): |dy| "+F308(dy)+" to the general's feet (XZ "+F308(xz,"F0")+" m, own gate XZ kept)");
    else k.C(xz<=5f&&dy<=2f,"AC-C10 "+id+".Destination on "+(pointId??encounterId)+": XZ "+F308(xz)+" m, |dy| "+F308(dy)+" m");
   }
   // only with relayout data: without the file the report is the #308 one, line for line
   if(Relayout308()!=null)k.I("gate signature (order, prerequisites, Optional, rewards, facts, triggers) sha "+Short308(BuildingAudit308.ShaText(CampaignSig308(profile)))+" - compare with the value campaign-sig printed before the relayout (D308-16: must be equal)");
   // AC-C14 ledger
   var l=ReadLedger308("campaign308");
   if(l==null)k.I("AC-C14 no campaign ledger (not applied, or reverted)");
   else for(int i=0;i<l.assets.Count;i++)k.I("AC-C14 ledger "+l.assets[i]+": "+l.writes.Count+" write(s), pristine "+l.pristine[i]+" ("+(File.Exists(l.pristine[i])?"present":"MISSING")+")");
   return k.Done("checks-campaign308.txt");
  }
  static void Complete308(DemoCampaignProfile profile,DemoCampaignState state,string id)
  {
   if(!state.Completed.Contains(id))state.Completed.Add(id);
   var s=profile.FindStage(id);foreach(var f in s?.GrantedFacts??Array.Empty<string>())if(!state.Facts.Contains(f))state.Facts.Add(f);
  }

  // ---------- checks:content (AC-C3 text, C4 fact, C7 count/distance, C12 data, C18 run count) ----------

  static string ChecksContent308()
  {
   var k=new Check308();
   foreach(var scene in Scenes308)
   {
    var t=Targets308[scene];var c=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(t.content);var layout=AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(t.layout);
    string tag=Key308(scene)+" ";
    if(c==null){k.C(false,tag+"content missing "+t.content);continue;}
    k.C(c.VehicleRequiredFact==VehicleFact308,tag+"AC-C4 VehicleRequiredFact == "+VehicleFact308+" (now '"+c.VehicleRequiredFact+"')");
    var v=c.EscortVoice;
    var texts=new[]{("ContractedText",v?.ContractedText),("ContractedReadyText",v?.ContractedReadyText),("StationWaitingText",v?.StationWaitingText),("StationReadyText",v?.StationReadyText),("EscortingText",v?.EscortingText),("DeliveredText",v?.DeliveredText)};
    foreach(var (name,text) in texts){var bad=Forbidden308.Where(f=>(text??"").Contains(f)).ToArray();k.C(bad.Length==0,tag+"AC-C3 EscortVoice."+name+" free of "+string.Join(" ",Forbidden308)+(bad.Length>0?" (has "+string.Join(" ",bad)+")":""));}
    k.C(string.IsNullOrEmpty(v?.StartBoardNotice),tag+"§5 StartBoardNotice empty (LEGACY)");
    foreach(var r in RestsNow308())
    {
     var p=Array.Find(c.Points??Array.Empty<PrologueContentSO.Point>(),x=>x!=null&&x.Id==r.Id&&x.Kind==PrologueInteractionKind.Rest);
     var cp=Array.Find(c.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>(),x=>x!=null&&x.Id==r.Id);
     k.C(p!=null&&cp!=null&&cp.IsConfigured&&cp.Shop==r.Shop,tag+"AC-C7 rest "+r.Id+" point + checkpoint"+(r.Shop?" (Shop)":""));
     if(cp==null)continue;
     // nearest encounter outside detection + leash + 10 m (straight line; the NavMesh measure is the scene pass's walk)
     var worst=(c.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null).Select(e=>(e,margin:Vector3.Distance(e.Feet,cp.Feet)-(e.Detection+e.Leash+10f))).OrderBy(x=>x.margin).FirstOrDefault();
     if(worst.e!=null)k.C(worst.margin>=0,tag+"AC-C7 "+r.Id+" vs nearest danger "+worst.e.Id+": margin "+F308(worst.margin,"F1")+" m (straight line)");
    }
    var keeper=Array.Find(c.Points??Array.Empty<PrologueContentSO.Point>(),x=>x!=null&&x.Id=="hunter_innkeeper308");
    k.C(keeper!=null&&!string.IsNullOrWhiteSpace(keeper.Speaker)&&(keeper.Services??Array.Empty<PrologueContentSO.PointService306>()).Any(s=>s.Kind==PrologueContentSO.PointServiceKind306.Rest)&&keeper.Services.Any(s=>s.Kind==PrologueContentSO.PointServiceKind306.Maintain),
     tag+"AC-C8 hunter_innkeeper308 speaker + 쉬기 / 정비 rows (거래 = equipment shop: not authored, see report)");
    foreach(var id in new[]{"logging_inquiry","geumpyo_stele308","village_empty_house308","sanctuary_trace308","road_yeomak308"})
     k.C(Array.Exists(c.Points??Array.Empty<PrologueContentSO.Point>(),x=>x!=null&&x.Id==id),tag+"§6/§9 point "+id);
    var lesson=Array.Find(c.Points??Array.Empty<PrologueContentSO.Point>(),x=>x!=null&&x.Id=="metal_growth_lesson");
    var enc=c.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>();
    var dok=Array.Find(enc,e=>e!=null&&e.Id=="folklore298/dokkaebi");var agwi=Array.Find(enc,e=>e!=null&&e.Id=="folklore298/agwi");
    if(lesson!=null&&dok!=null)k.C(Harness303.Flat(dok.Feet,lesson.Position)>=40f,tag+"AC-C12 dokkaebi <-> metal lesson "+F308(Harness303.Flat(dok.Feet,lesson.Position),"F0")+" m >= 40");
    else k.I(tag+"dokkaebi or metal lesson absent in this content");
    foreach(var e in enc.Where(e=>e!=null&&e.Id.StartsWith("forest_beast308/",StringComparison.Ordinal)))
     if(lesson!=null)k.C(Harness303.Flat(e.Feet,lesson.Position)>=200f,tag+"AC-C12 "+e.Id+" <-> metal lesson "+F308(Harness303.Flat(e.Feet,lesson.Position),"F0")+" m >= 200");
    k.C(enc.Count(e=>e!=null&&e.Id.StartsWith("forest_beast308/",StringComparison.Ordinal))==2,tag+"§8 forest_beast308/0·1 present");
    if(agwi!=null)
    {
     k.C(!agwi.RespawnOnRest,tag+"AC-C12 agwi RespawnOnRest 0");
     // AC-C12 (wording revised with D308-16, SPEC-CONTENT-PACING-308): the retry rest is the NEAREST rest place, whichever it is
     // (it was "the first inspection rest" while agwi stood by that inspection). Limits come from the scene data (checks.*).
     // Without relayout data the #308 line (first inspection rest, information only) is printed exactly as before.
     bool relaid=Relayout308()!=null;
     var rest1=Array.Find(c.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>(),x=>x!=null&&x.Id=="road_rest_1");
     if(!relaid&&rest1!=null)k.I(tag+"AC-C12 agwi <-> first inspection rest "+F308(Vector3.Distance(agwi.Feet,rest1.Feet),"F0")+" m straight (NavMesh path <= 135 m is the scene walk)");
     var nearRest=!relaid?null:(c.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).Where(x=>x!=null&&x.IsConfigured).OrderBy(x=>Vector3.Distance(agwi.Feet,x.Feet)).FirstOrDefault();
     if(nearRest!=null)
     {
      float nd=Vector3.Distance(agwi.Feet,nearRest.Feet);float restMax=CheckNum308("agwi_rest_max_m");
      if(float.IsNaN(restMax))k.I(tag+"AC-C12 agwi <-> nearest rest place "+nearRest.Id+" "+F308(nd,"F1")+" m straight (no checks.agwi_rest_max_m in the scene data; NavMesh path is the scene walk)");
      else k.C(nd<=restMax,tag+"AC-C12 agwi <-> nearest rest place "+nearRest.Id+" "+F308(nd,"F1")+" m straight <= "+F308(restMax,"F0")+" (necessary; the NavMesh path <= "+F308(restMax,"F0")+" m is the scene walk)");
     }
     float reach=agwi.Detection+agwi.Leash+10f;var cp1=Array.Find(c.Points??Array.Empty<PrologueContentSO.Point>(),x=>x!=null&&x.Id=="checkpoint_1");
     int escortNear=(c.MainPath??Array.Empty<Vector3>()).Count(p=>Vector3.Distance(p,agwi.Feet)<reach);
     k.I(tag+"AC-C12 agwi reach "+F308(reach,"F0")+" m: MainPath points inside "+escortNear+" (the escort stretch must have 0; MainPath also holds walking legs), checkpoint_1 "+(cp1!=null?F308(Vector3.Distance(cp1.Position,agwi.Feet),"F0")+" m":"absent"));
     float roadMin=CheckNum308("agwi_road_min_m");var mainPath=c.MainPath??Array.Empty<Vector3>();
     if(relaid&&!float.IsNaN(roadMin)&&mainPath.Length>0){float rd=mainPath.Min(p=>Harness303.Flat(p,agwi.Feet));k.C(rd>=roadMin,tag+"AC-C12 agwi <-> the main road (MainPath) "+F308(rd,"F1")+" m >= "+F308(roadMin,"F0"));}
    }
    int runs=Runs308(c.BranchPath??Array.Empty<Vector3>());bool hasRun=(c.BranchPath??Array.Empty<Vector3>()).Any(q=>Vector2.Distance(new Vector2(q.x,q.z),NewRun308[0])<1f);
    k.C(hasRun,tag+"AC-C18 BranchPath holds the "+RouteId308+" run (runs now "+runs+")");
    k.C(layout!=null&&(layout.Routes??Array.Empty<CompactWorldLayoutSO.Route>()).Any(r=>r!=null&&r.Id==RouteId308&&r.Role==CompactRouteRole.ReturnShortcut),tag+"§2 layout route "+RouteId308+" (ReturnShortcut)");
    RelayoutChecks308(k,tag,c,layout);
    var l=ReadLedger308("content308_"+Key308(scene));
    if(l==null)k.I(tag+"AC-C14 no content ledger");
    else
    {
     k.I(tag+"AC-C14 ledger: "+l.writes.Count+" write(s); pristine backups "+string.Join(", ",l.pristine.Select(p=>Path.GetFileName(p)+(File.Exists(p)?"":" MISSING"))));
     // AC-C18: the first write's BranchPath line carries "runs a -> b" against the pristine asset
     var runLine=l.writes.SelectMany(w=>w.changes).FirstOrDefault(x=>x.StartsWith("BranchPath run",StringComparison.Ordinal)&&x.Contains("appended"));
     if(runLine!=null){var m=System.Text.RegularExpressions.Regex.Match(runLine,@"runs (\d+) -> (\d+)");
      if(m.Success)k.C(int.Parse(m.Groups[2].Value)==int.Parse(m.Groups[1].Value)+1,tag+"AC-C18 BranchPath runs +1 against the pristine asset ("+m.Groups[1].Value+" -> "+m.Groups[2].Value+")");}
     else k.I(tag+"AC-C18 no 'appended' BranchPath write in the ledger");
    }
   }
   // AC-C11 / AC-C12 data half: the two #308 profiles (their scene wiring is the scene pass / wire-profiles, checked in the scene)
   var attack=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(MineRanged308);
   k.C(attack!=null&&!attack.Elemental&&attack.Mode==EnemyController.AttackMode.RangedOnly&&attack.Delivery==EnemyAttackDelivery.HomingProjectile&&attack.TryValidate(out _),
    "AC-C11 "+MineRanged308+" Elemental 0 · RangedOnly · HomingProjectile · valid"+(attack==null?" (missing: run profiles)":""));
   var boss=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(FieldBoss308);
   k.C(boss!=null&&boss.IsBoss&&boss.DisplayName=="아귀"&&boss.MaxHp>0,"AC-C12 "+FieldBoss308+" IsBoss · '아귀' · MaxHp "+(boss!=null?F308(boss.MaxHp,"F0"):"-")+(boss==null?" (missing: run profiles)":""));
   return k.Done("checks-content308.txt");
  }

  // ---------- profiles (§8) ----------

  static void EnsureFolder308(string folder)
  {
   if(AssetDatabase.IsValidFolder(folder))return;
   string parent=Path.GetDirectoryName(folder).Replace('\\','/');EnsureFolder308(parent);
   AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));
  }
  static string Profiles308()
  {
   EnsureFolder308(ProfileDir308);var sb=new StringBuilder("profiles\n");
   var attack=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(MineRanged308);
   if(attack==null)
   {
    if(AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(FireRanged306)==null)throw new Refuse308("missing "+FireRanged306);
    if(!AssetDatabase.CopyAsset(FireRanged306,MineRanged308))throw new Refuse308("copy "+FireRanged306+" -> "+MineRanged308+" failed");
    attack=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(MineRanged308);sb.AppendLine("  created "+MineRanged308+" (copy of FireRanged306)");
   }
   string before=EditorJsonUtility.ToJson(attack);
   // COMBAT-DEFENSE: a non-elemental attack has dodge as its only answer (no ward / parry judge, EnemyController.Authored)
   attack.Elemental=false;attack.Mode=EnemyController.AttackMode.RangedOnly;attack.Delivery=EnemyAttackDelivery.HomingProjectile;attack.Telegraph=1.2f;attack.ProjectileSpeed=12f;
   if(!attack.TryValidate(out var error))throw new Refuse308(MineRanged308+": "+error);
   if(EditorJsonUtility.ToJson(attack)!=before){EditorUtility.SetDirty(attack);AssetDatabase.SaveAssetIfDirty(attack);sb.AppendLine("  updated "+MineRanged308+": Elemental 0, RangedOnly, HomingProjectile, telegraph 1.2, speed 12");}
   else sb.AppendLine("  "+MineRanged308+" 변경 없음");
   var vit=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(FieldBoss308);
   if(vit==null){vit=ScriptableObject.CreateInstance<EnemyVitalsProfileSO>();AssetDatabase.CreateAsset(vit,FieldBoss308);sb.AppendLine("  created "+FieldBoss308);}
   var so=new SerializedObject(vit);bool changed=false;
   void F(string name,float v){var p=so.FindProperty(name);if(p!=null&&!Mathf.Approximately(p.floatValue,v)){p.floatValue=v;changed=true;}}
   void S(string name,string v){var p=so.FindProperty(name);if(p!=null&&p.stringValue!=v){p.stringValue=v;changed=true;}}
   void B(string name,bool v){var p=so.FindProperty(name);if(p!=null&&p.boolValue!=v){p.boolValue=v;changed=true;}}
   F("_maxHp",180f);S("_displayName","아귀");B("_isBoss",true);B("_showLockOnBar",false);
   so.ApplyModifiedPropertiesWithoutUndo();
   if(changed||EditorUtility.IsDirty(vit)){EditorUtility.SetDirty(vit);AssetDatabase.SaveAssetIfDirty(vit);sb.AppendLine("  "+FieldBoss308+": 180 HP, boss bar '아귀' (TEST)");}
   else sb.AppendLine("  "+FieldBoss308+" 변경 없음");
   return sb.ToString();
  }

  // ---------- optional scene wiring (record + revert, scene saved only when something changed) ----------

  [Serializable] sealed class Wire308{public string scene="",backup="";public List<string> paths=new List<string>(),fields=new List<string>(),before=new List<string>();}
  static string WireRecord308(string scene)=>Path.Combine(Out308,"wire-profiles_"+Key308(scene)+".json");
  static string WireProfiles308(string scenePath,bool revert)
  {
   var scene=Open308(scenePath);var session=Session308(scene);string rec=WireRecord308(scene.path);var sb=new StringBuilder(scene.path+(revert?" (unwire-profiles)\n":" (wire-profiles)\n"));
   var actors=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PrologueEncounter>(true)).ToArray();
   if(revert)
   {
    if(!File.Exists(rec))throw new Refuse308("no record "+rec);
    var r=JsonUtility.FromJson<Wire308>(File.ReadAllText(rec));int n=0;
    for(int i=0;i<r.paths.Count;i++)
    {
     var a=actors.FirstOrDefault(x=>Harness303.PathOf(x.transform)==r.paths[i]);if(a==null){sb.AppendLine("  missing "+r.paths[i]);continue;}
     Component target=r.fields[i]=="_attackProfile"?(Component)a.GetComponent<EnemyController>():a.GetComponent<EnemyVitals>();
     if(target==null){sb.AppendLine("  "+r.paths[i]+": no "+r.fields[i]+" owner any more; skipped");continue;}
     var so=new SerializedObject(target);var prop=so.FindProperty(r.fields[i]);
     if(prop==null){sb.AppendLine("  "+r.paths[i]+": field "+r.fields[i]+" not found; skipped");continue;}
     // only a reference this command set is put back (a later writer's choice is left alone and reported)
     string now=prop.objectReferenceValue!=null?AssetDatabase.GetAssetPath(prop.objectReferenceValue):"";
     if(now!=MineRanged308&&now!=FieldBoss308){sb.AppendLine("  "+r.paths[i]+"."+r.fields[i]+" is now '"+now+"' (not a #308 profile); left as it is");continue;}
     prop.objectReferenceValue=string.IsNullOrEmpty(r.before[i])?null:AssetDatabase.LoadAssetAtPath<ScriptableObject>(r.before[i]);
     so.ApplyModifiedPropertiesWithoutUndo();n++;
    }
    if(n>0){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
    File.Move(rec,rec+".reverted-"+Harness303.UtcStamp());
    return sb.Append("  restored "+n).ToString();
   }
   var attack=AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(MineRanged308);var vit=AssetDatabase.LoadAssetAtPath<EnemyVitalsProfileSO>(FieldBoss308);
   if(attack==null||vit==null)throw new Refuse308("run profiles first");
   var record=File.Exists(rec)?JsonUtility.FromJson<Wire308>(File.ReadAllText(rec)):new Wire308{scene=scene.path};
   bool any=false;string backup=null;
   void Wire(string id,Component target,string field,Object value)
   {
    if(target==null){sb.AppendLine("  "+id+": no "+field+" owner in this scene; skipped");return;}
    var top=target.transform.root.name;
    if(new[]{"Watershed295","Reworld292","MountainTrail285"}.Any(x=>top.StartsWith(x,StringComparison.Ordinal))){sb.AppendLine("  "+id+": under the protected tree "+top+"; skipped");return;}
    var so=new SerializedObject(target);var p=so.FindProperty(field);if(p==null){sb.AppendLine("  "+id+": field "+field+" not found");return;}
    if(p.objectReferenceValue==value){sb.AppendLine("  "+id+"."+field+" already "+value.name);return;}
    if(backup==null){backup=Path.Combine(Out308,"Backups","wire-"+Key308(scene.path)+"-"+Harness303.UtcStamp());Directory.CreateDirectory(backup);File.Copy(Harness303.Abs(scene.path),Path.Combine(backup,Path.GetFileName(scene.path)),true);record.backup=backup;}
    string path=Harness303.PathOf(target.transform);
    if(!record.paths.Contains(path)||record.fields[record.paths.IndexOf(path)]!=field){record.paths.Add(path);record.fields.Add(field);record.before.Add(p.objectReferenceValue!=null?AssetDatabase.GetAssetPath(p.objectReferenceValue):"");}
    p.objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();any=true;sb.AppendLine("  "+id+"."+field+" -> "+value.name);
   }
   var fire=actors.FirstOrDefault(a=>a.Id=="mine_fire/0");var agwi=actors.FirstOrDefault(a=>a.Id=="folklore298/agwi");
   Wire("mine_fire/0",fire!=null?fire.GetComponent<EnemyController>():null,"_attackProfile",attack);
   Wire("folklore298/agwi",agwi!=null?agwi.GetComponent<EnemyVitals>():null,"_profile",vit);
   if(!any)return sb.Append("  변경 없음 (scene not saved)").ToString();
   Directory.CreateDirectory(Out308);File.WriteAllText(rec,JsonUtility.ToJson(record,true));
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return sb.Append("  saved; record "+rec+", scene backup "+backup).ToString();
  }
 }
}
