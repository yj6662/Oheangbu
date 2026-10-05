using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.BrushRender;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 권역 담채 placement (WorldMacro_Playtest/EventWash308) and the planner unit test (SPEC-EVENT-WASH-308 §6, AC-W1 parity,
 // AC-W7–W10, W12, W13, W18 OpticalDepth, W19 D308-6b meanings/slots).
 public static partial class EventWash308
 {
  const string DriverName="EventWash308";

  static string Apply(Dictionary<string,string> o,bool dry)
  {
   PostLedger308.RequireEditable();
   var sheet=AssetDatabase.LoadAssetAtPath<EventWashSheetSO>(SheetPath)??throw new PostLedger308.Refused("EventWashSheet308 missing: run wash308-sheet:dry, check it, then wash308-sheet");
   var ledger=ReadLedger();string utc=PostLedger308.Utc();var sb=new StringBuilder("wash308-apply"+(dry?" (dry)":"")+"\n");
   foreach(var path in PostLedger308.Select(o))
   {
    var scene=PostLedger308.Open(path);sb.AppendLine(" "+PostLedger308.Short(path));
    WorldMacroPlaytestSession session;WorldLocationCatalog catalog;ElementPaletteSO palette;
    try{(session,catalog,palette)=SceneRefs(scene);}catch(PostLedger308.Refused r){sb.AppendLine("  refused: "+r.Message);continue;}
    if(path==PostLedger308.Main)
    {
     string cg=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog)),pg=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(palette));
     if(cg!=ExpectedCatalogGuid||pg!=ExpectedPaletteGuid){sb.AppendLine("  refused: Main catalog/palette differ from the expected a5309253…_Locations / 36ca032e…_ElementPalette_Test ("+AssetDatabase.GetAssetPath(catalog)+", "+AssetDatabase.GetAssetPath(palette)+")");continue;}
    }
    var host=session.transform;
    var existing=host.Find(DriverName);var driver=existing!=null?existing.GetComponent<EventWashDriver308>():null;
    bool same=driver!=null&&driver.Session==session&&driver.Sheet==sheet&&driver.Catalog==catalog&&driver.Palette==palette;
    sb.AppendLine("  host "+PostLedger308.PathOf(host)+", catalog "+Path.GetFileName(AssetDatabase.GetAssetPath(catalog))+", palette "+Path.GetFileName(AssetDatabase.GetAssetPath(palette)));
    if(same){sb.AppendLine("  no-op (driver present with the same references)");continue;}
    if(dry){sb.AppendLine("  dry: would "+(existing==null?"create "+DriverName:driver==null?"add EventWashDriver308 to "+DriverName:"rebind the driver")+"");continue;}
    string backup=PostLedger308.Backup(BackupRoot,utc,path);ledger.backups.Add(backup);
    bool created=false;
    if(existing==null){var go=new GameObject(DriverName);go.transform.SetParent(host,false);existing=go.transform;created=true;}
    string before=driver==null?"":"session="+PostLedger308.PathOf(driver.Session!=null?driver.Session.transform:host)+";sheet="+PostLedger308.AssetRef(driver.Sheet)+";catalog="+PostLedger308.AssetRef(driver.Catalog)+";palette="+PostLedger308.AssetRef(driver.Palette);
    if(driver==null)driver=existing.gameObject.AddComponent<EventWashDriver308>();
    driver.Session=session;driver.Sheet=sheet;driver.Catalog=catalog;driver.Palette=palette;EditorUtility.SetDirty(driver);
    if(!ledger.changes.Any(c=>c.scene==path&&c.kind=="driver"))
     ledger.changes.Add(new Change{scene=path,kind="driver",path=PostLedger308.PathOf(existing),before=created?"created":before.Length==0?"component added":before,after="bound",utc=utc});
    PostLedger308.SaveScene(scene);WriteLedger(ledger);
    sb.AppendLine("  "+(created?"created ":"bound ")+PostLedger308.PathOf(existing)+"; backup "+backup);
   }
   return sb.ToString();
  }

  static string ApplyRevert(Dictionary<string,string> o)
  {
   PostLedger308.RequireEditable();
   var ledger=ReadLedger();string utc=PostLedger308.Utc();var sb=new StringBuilder("wash308-apply:revert\n");
   foreach(var path in PostLedger308.Select(o))
   {
    var rec=ledger.changes.LastOrDefault(c=>c.scene==path&&c.kind=="driver");
    if(rec==null){sb.AppendLine(" "+PostLedger308.Short(path)+": nothing recorded");continue;}
    var scene=PostLedger308.Open(path);var t=PostLedger308.Find(scene,rec.path);
    if(t==null){sb.AppendLine(" "+PostLedger308.Short(path)+": "+rec.path+" missing");ledger.changes.Remove(rec);WriteLedger(ledger);continue;}
    string backup=PostLedger308.Backup(BackupRoot,utc,path);ledger.backups.Add(backup);
    if(rec.before=="created")Object.DestroyImmediate(t.gameObject);
    else{var d=t.GetComponent<EventWashDriver308>();if(d!=null)Object.DestroyImmediate(d);}
    ledger.changes.Remove(rec);PostLedger308.SaveScene(scene);WriteLedger(ledger);
    sb.AppendLine(" "+PostLedger308.Short(path)+": "+(rec.before=="created"?"removed "+rec.path:"driver component removed (earlier bindings: "+rec.before+")")+"; backup "+backup);
   }
   return sb.ToString();
  }

  // ---------- unit (pure planner + source checks; no scene, no asset written) ----------
  static string Unit()
  {
   var sb=new StringBuilder("wash308-unit "+PostLedger308.Utc()+"\n");int fail=0;
   void Check(bool ok,string text){if(!ok)fail++;sb.AppendLine((ok?"  ok   ":"  FAIL ")+text);}
   var temps=new List<Object>();
   T Make<T>() where T:ScriptableObject{var x=ScriptableObject.CreateInstance<T>();x.hideFlags=HideFlags.HideAndDontSave;temps.Add(x);return x;}
   string Vs(Vector4 v)=>v.ToString("0.####");
   try
   {
    var palette=Make<ElementPaletteSO>();
    var catalog=Make<WorldLocationCatalog>();
    WorldLocationCatalog.Entry Place(string id,string realm,float x,float z,float r=40)=>new WorldLocationCatalog.Entry{Id=id,RealmId=realm,Centre=new Vector3(x,100,z),Radius=r,MinimumY=-1000,MaximumY=2000,Priority=10};
    var places=new List<WorldLocationCatalog.Entry>{
     Place("cr_a","realm_cheongrim",0,0),Place("cr_inn","realm_cheongrim",200,0),Place("hw_a","realm_hwanggyeong",400,0),Place("hw_inn","realm_hwanggyeong",600,0),
     Place("mine_interior","realm_cheongrim",800,0),Place("arena_jeokro296","realm_jeokro",1000,0),Place("boss_place","realm_jeokro",1200,0),Place("opt","realm_cheongrim",1400,0),
     Place("cr_b","realm_cheongrim",-600,0)};
    for(int i=0;i<10;i++)places.Add(Place("far"+i,"realm_cheongrim",3000+i*100,0,30));
    catalog.Entries=places.ToArray();
    var campaign=Make<DemoCampaignProfile>();campaign.CampaignId="unit308";campaign.UseExplicitPrerequisites=true;
    DemoCampaignProfile.Stage St(string id,DemoEventKind kind,bool optional=false,params string[] pre)=>new DemoCampaignProfile.Stage{Id=id,Objective=id,TriggerId=id,Event=kind,Implemented=true,Optional=optional,PrerequisiteIds=pre,
     Destination=new Vector3(-9999,35,-9999),DestinationId="area_stale"};   // stale Destination must never matter
    var stages=new List<DemoCampaignProfile.Stage>{St("s_prog",DemoEventKind.Interaction),St("s_prog2",DemoEventKind.CargoDelivered),St("s_unbaked",DemoEventKind.Interaction),
     St("s_rest",DemoEventKind.Rest),St("s_hw_prog",DemoEventKind.Interaction),St("s_hw_rest",DemoEventKind.Rest),
     St("s_mine",DemoEventKind.Rest),St("s_nowash",DemoEventKind.Interaction),St("s_boss",DemoEventKind.BossDefeated),St("s_opt",DemoEventKind.Interaction,true),St("s_later",DemoEventKind.Rest,false,"s_rest")};
    for(int i=0;i<10;i++)stages.Add(St("s_far"+i,DemoEventKind.Rest));
    campaign.Stages=stages.ToArray();
    var sheet=Make<EventWashSheetSO>();
    const float vBase=140f,vTop=400f,vR=300f;
    EventWashSheetSO.Entry E(string stage,string place)=>new EventWashSheetSO.Entry{StageId=stage,LocationId=place};
    EventWashSheetSO.Entry Vol(EventWashSheetSO.Entry e){e.VolumeBaseY=vBase;e.VolumeTopY=vTop;e.VolumeRadius=vR;return e;}
    var map=new List<EventWashSheetSO.Entry>{Vol(E("s_prog","cr_a")),Vol(E("s_prog2","cr_a")),E("s_unbaked","cr_b"),E("s_rest","cr_inn"),E("s_hw_prog","hw_a"),E("s_hw_rest","hw_inn"),
     E("s_mine","mine_interior"),E("s_nowash","arena_jeokro296"),E("s_boss","boss_place"),Vol(E("s_opt","opt")),E("s_later","cr_inn")};
    for(int i=0;i<10;i++)map.Add(E("s_far"+i,"far"+i));
    sheet.Entries=map.ToArray();
    EventWashPlanner308.Zone Z(EventWashPlanner308 p,string stage){for(int i=0;i<p.Count;i++)if(p[i].StageId==stage)return p[i];return null;}
    string Why(EventWashPlanner308 p,string stage)=>string.Join("; ",p.Skipped.Where(x=>x.StartsWith(stage+" ",StringComparison.Ordinal)||x.StartsWith(stage+":",StringComparison.Ordinal)));

    // ---- D308-6b meanings (defaults: ProgressOn, RewardOn false, ProgressAsVolume) ----
    Check(sheet.ProgressOn&&!sheet.RewardOn&&sheet.ProgressAsVolume,"sheet defaults: ProgressOn true, RewardOn false, ProgressAsVolume true (D308-6b)");
    var plan=new EventWashPlanner308(sheet,catalog,palette,campaign);
    var prog=Z(plan,"s_prog");
    var cr=palette.GetBaseColor('ㄱ');float crMax=Mathf.Max(cr.r,Mathf.Max(cr.g,cr.b));
    var tExpected=Vector3.Lerp(Vector3.one,new Vector3(cr.r/crMax,cr.g/crMax,cr.b/crMax),sheet.VolumePigment);
    Check(Z(plan,"s_opt")==null&&Why(plan,"s_opt").Contains("RewardOn false"),"AC-W19 RewardOn false: 보상 없음 ("+Why(plan,"s_opt")+")");
    Check(prog!=null&&prog.Kind==EventWashPlanner308.Kind.Volume&&prog.Meaning==EventWashSheetSO.Meaning.Progress&&(prog.Pigment-tExpected).sqrMagnitude<1e-10f&&prog.LinearColour==cr.linear,
          "AC-W19 기본: 진행 = Volume, T = 팔레트 ㄱ sRGB 정규화 x Pigment "+sheet.VolumePigment+" = "+(prog!=null?prog.Pigment.ToString("0.###"):"-"));
    Check(prog!=null&&Mathf.Approximately(prog.VolumeCentreY,.5f*(vBase+vTop))&&Mathf.Approximately(prog.VolumeHalfHeight,.5f*(vTop-vBase))&&prog.VolumeRadius==vR&&prog.Centre==Vector2.zero&&Mathf.Abs(prog.Radius-40)<1e-4f,
          "volume geometry = baked entry (yc "+(prog!=null?prog.VolumeCentreY:0)+", Hh "+(prog!=null?prog.VolumeHalfHeight:0)+", R "+vR+"), centre = catalog (stale Stage.Destination ignored)");
    Check(Z(plan,"s_unbaked")==null&&Why(plan,"s_unbaked").Contains("volume not baked"),"unbaked progress volume -> skipped ("+Why(plan,"s_unbaked")+")");
    var rest=Z(plan,"s_rest");var mineZ=Z(plan,"s_mine");var hwProg=Z(plan,"s_hw_prog");var hwRest=Z(plan,"s_hw_rest");
    Check(rest?.Meaning==EventWashSheetSO.Meaning.Rest&&rest.Kind==EventWashPlanner308.Kind.Surface&&rest.LinearColour==palette.RestLanternColor.linear,"안식 = surface, 등불 주황 (unchanged)");
    Check(mineZ?.Meaning==EventWashSheetSO.Meaning.Narrative&&mineZ.Kind==EventWashPlanner308.Kind.Surface&&mineZ.Chroma==0,"AC-W7 폐광 rest -> 서사 surface, chroma 0");
    Check(hwProg?.Meaning==EventWashSheetSO.Meaning.Narrative&&hwProg.Kind==EventWashPlanner308.Kind.Surface&&hwProg.Chroma==0,"AC-W8 황경 진행 -> 서사 surface (chroma 0), never a volume");
    Check(hwRest?.Meaning==EventWashSheetSO.Meaning.Rest&&hwRest.LinearColour==palette.RestLanternColor.linear,"AC-W8 황경 쉼터 -> 안식 (등불 주황)");
    Check(Z(plan,"s_boss")==null,"AC-W9 boss stage: no zone");
    Check(Z(plan,"s_nowash")==null,"AC-W9 NoWashLocations: no zone");

    sheet.ProgressOn=false;var planOff=new EventWashPlanner308(sheet,catalog,palette,campaign);sheet.ProgressOn=true;
    Check(Z(planOff,"s_prog")==null&&Why(planOff,"s_prog").Contains("ProgressOn false")&&Z(planOff,"s_rest")!=null,"AC-W19 ProgressOn false: 진행 없음 (되돌림), 안식 stays");
    sheet.ProgressAsVolume=false;var planGround=new EventWashPlanner308(sheet,catalog,palette,campaign);sheet.ProgressAsVolume=true;
    var progGround=Z(planGround,"s_prog");
    Check(progGround!=null&&progGround.Kind==EventWashPlanner308.Kind.Surface&&progGround.Chroma==1&&progGround.LinearColour==cr.linear&&Z(planGround,"s_unbaked")?.Kind==EventWashPlanner308.Kind.Surface,
          "ProgressAsVolume false: 진행 = D308-6 ground wash (comparison/revert; no bake needed)");
    sheet.RewardOn=true;var planReward=new EventWashPlanner308(sheet,catalog,palette,campaign);sheet.RewardOn=false;
    var opt=Z(planReward,"s_opt");
    Check(opt?.Meaning==EventWashSheetSO.Meaning.Reward&&opt.Kind==EventWashPlanner308.Kind.Surface&&Mathf.Abs(opt.Strength-sheet.OptionalScale)<1e-6f&&opt.LinearColour==palette.RewardCeladonColor.linear,
          "RewardOn true (revert path): 보상 = surface 비색, strength x OptionalScale");

    // ---- slots: volumes first, one slot per place, band 0; surfaces = the D308-6 values ----
    var zone=new Vector4[8];var colour=new Vector4[8];var band=new Vector4[8];var volume=new Vector4[8];
    var state=new DemoCampaignState{CampaignId="unit308"};var defeated=new List<string>();
    plan.Poll(state,defeated);plan.Step(0,true);
    int n=plan.Write(new Vector3(0,1.7f,0),zone,colour,band,volume,out int split);
    var slots=Enumerable.Range(0,n).Select(i=>plan[plan.SelectedZone(i)]).ToArray();
    Check(split==1&&slots.Length>0&&slots[0].StageId=="s_prog"&&slots.Skip(1).All(z=>z.Kind==EventWashPlanner308.Kind.Surface),"volume slots first: split "+split+", slots "+string.Join(",",slots.Select(z=>z.StageId+"/"+z.Kind)));
    Check(slots.Count(z=>z.LocationId=="cr_a")==1,"AC-W19 같은 장소 두 단계 (s_prog, s_prog2 @cr_a) -> 슬롯 1개, tie -> lower index (s_prog)");
    float seed=prog!=null?prog.Seed:-1f;
    Check(zone[0]==new Vector4(0,0,vR,1)&&colour[0]==new Vector4(tExpected.x,tExpected.y,tExpected.z,0)&&band[0]==Vector4.zero&&volume[0]==new Vector4(.5f*(vBase+vTop),.5f*(vTop-vBase),sheet.VolumeDensity,seed),
          "volume slot: zone "+Vs(zone[0])+", colour (T, 0), band 0 (RealmFog297 far air ignores it), volume "+Vs(volume[0]));
    Check(Enumerable.Range(split,n-split).All(s=>volume[s]==Vector4.zero)&&Enumerable.Range(n,8-n).All(s=>zone[s]==Vector4.zero&&colour[s]==Vector4.zero&&band[s]==Vector4.zero&&volume[s]==Vector4.zero),
          "surface slots have volume 0; unused slots all zero");
    int restSlot=Array.FindIndex(slots,z=>z.StageId=="s_rest"),mineSlot=Array.FindIndex(slots,z=>z.StageId=="s_mine");
    var restLin=palette.RestLanternColor.linear;var paperLin=palette.PaperColor.linear;
    Check(restSlot>=split&&zone[restSlot]==new Vector4(200,0,40,1)&&colour[restSlot]==new Vector4(restLin.r,restLin.g,restLin.b,1)&&band[restSlot]==new Vector4(-1000,2000,sheet.EdgeNoise,1),
          "AC-W19 안식 surface slot = D308-6 values (catalog centre/radius, linear lantern, lift 1, band min/max/edge/chroma 1)");
    Check(mineSlot>=split&&zone[mineSlot]==new Vector4(800,0,40,1)&&colour[mineSlot]==new Vector4(paperLin.r,paperLin.g,paperLin.b,1)&&band[mineSlot]==new Vector4(-1000,2000,sheet.EdgeNoise,0),
          "AC-W19 서사 surface slot = D308-6 values (paper, lift 1, chroma 0)");

    // D308-6 reference writer (the pre-6b Write): surfaces only, nearest MaxZones by (edge, index), the same slot values
    int Reference(EventWashPlanner308 p,Vector3 eye,Vector4[] rz,Vector4[] rc,Vector4[] rb)
    {
     var chosen=Enumerable.Range(0,p.Count).Where(i=>p[i].Kind==EventWashPlanner308.Kind.Surface&&p[i].Fade>0&&p[i].Strength>0)
      .Select(i=>(i,edge:Vector2.Distance(new Vector2(eye.x,eye.z),p[i].Centre)-p[i].Radius)).Where(x=>x.edge<=sheet.MaxDistance)
      .OrderBy(x=>x.edge).ThenBy(x=>x.i).Take(Mathf.Clamp(sheet.MaxZones,1,8)).ToArray();
     for(int s=0;s<8;s++)
     {
      if(s>=chosen.Length){rz[s]=Vector4.zero;rc[s]=Vector4.zero;rb[s]=Vector4.zero;continue;}
      var z=p[chosen[s].i];float eased=z.Fade*z.Fade*(3f-2f*z.Fade);
      rz[s]=new Vector4(z.Centre.x,z.Centre.y,z.Radius,z.Strength*eased);rc[s]=new Vector4(z.LinearColour.r,z.LinearColour.g,z.LinearColour.b,z.Lift);rb[s]=new Vector4(z.MinY,z.MaxY,sheet.EdgeNoise,z.Chroma);
     }
     return chosen.Length;
    }
    var rz8=new Vector4[8];var rc8=new Vector4[8];var rb8=new Vector4[8];
    planOff.Poll(state,defeated);planOff.Step(0,true);
    var eyes=new[]{new Vector3(0,2,0),new Vector3(700,2,0),new Vector3(1500,2,300),new Vector3(3450,2,0),new Vector3(-5000,2,0)};
    bool parity=true;string parityNote="";
    foreach(var eye in eyes)
    {
     int got=planOff.Write(eye,zone,colour,band,volume,out int sp);int want=Reference(planOff,eye,rz8,rc8,rb8);
     bool same=got==want&&sp==0&&Enumerable.Range(0,8).All(s=>zone[s]==rz8[s]&&colour[s]==rc8[s]&&band[s]==rb8[s]&&volume[s]==Vector4.zero);
     if(!same){parity=false;parityNote+=" "+eye.ToString("F0")+" got "+got+"/split "+sp+" want "+want+";";}
    }
    Check(parity,"AC-W1/W19 split 0 (ProgressOn false): all 8 slots identical to the D308-6 writer at "+eyes.Length+" eyes"+parityNote);
    n=plan.Write(new Vector3(0,2,0),zone,colour,band,volume,out split);int refN=Reference(plan,new Vector3(0,2,0),rz8,rc8,rb8);
    Check(n-split==refN&&Enumerable.Range(0,refN).All(s=>zone[split+s]==rz8[s]&&colour[split+s]==rc8[s]&&band[split+s]==rb8[s]),"with volumes on: surface slots [split, count) = the D308-6 surface list, same order ("+refN+")");

    // distance fade of the volume: edge = VolumeMaxDistance -> 0; one metre further -> no slot; maxD - fade band -> full
    // (eyes on -x: no surface zone within reach there, so the volume is never pushed out of the nearest 8)
    float maxD=sheet.VolumeMaxDistance,fadeBand=sheet.VolumeDistanceFade;
    plan.Write(new Vector3(-(vR+maxD),2,0),zone,colour,band,volume,out int spEdge);float wEdge=spEdge>0?zone[0].w:-1;
    plan.Write(new Vector3(-(vR+maxD+1),2,0),zone,colour,band,volume,out int spOut);
    plan.Write(new Vector3(-(vR+maxD-fadeBand),2,0),zone,colour,band,volume,out int spFull);float wFull=spFull>0?zone[0].w:-1;
    plan.Write(new Vector3(-(vR+maxD-.5f*fadeBand),2,0),zone,colour,band,volume,out int spHalf);float wHalf=spHalf>0?zone[0].w:-1;
    Check(spEdge==1&&Mathf.Abs(wEdge)<1e-6f&&spOut==0&&Mathf.Abs(wFull-1)<1e-6f&&Mathf.Abs(wHalf-.5f)<1e-4f,
          "volume distFade: edge = maxD -> "+wEdge+", +1 m -> "+(spOut==0?"no slot":"slot")+", maxD - band -> "+wFull+", middle -> "+wHalf.ToString("0.####"));

    // same place: the stronger fade wins the slot
    var stateGroup=new DemoCampaignState{CampaignId="unit308"};stateGroup.Completed.Add("s_prog");
    plan.Poll(stateGroup,defeated);plan.Step(1f,false);
    plan.Write(new Vector3(0,2,0),zone,colour,band,volume,out int spGroup);
    var picked0=spGroup>0?plan[plan.SelectedZone(0)]:null;float fadeProg=Z(plan,"s_prog").Fade;
    Check(spGroup==1&&picked0?.StageId=="s_prog2"&&Mathf.Abs(zone[0].w-1f)<1e-6f&&fadeProg>0f&&fadeProg<1f,
          "same place, s_prog fading out ("+fadeProg.ToString("0.00")+") and s_prog2 at 1: slot = "+(picked0?.StageId??"-")+" w "+zone[0].w.ToString("0.###"));
    // hand-over at the same place (one stage completes while the next opens): the group fade is the union 1 − Π(1 − eased)
    {
     var zA=Z(plan,"s_prog");var zB=Z(plan,"s_prog2");float keepA=zA.Fade,keepB=zB.Fade;
     zA.Fade=.5f;zB.Fade=.5f;   // eased .5 each -> union .75 (max alone would be .5)
     plan.Write(new Vector3(0,2,0),zone,colour,band,volume,out int spCross);float wCross=spCross>0?zone[0].w:-1f;
     zA.Fade=.6f;zB.Fade=0f;float eA=.6f*.6f*(3f-1.2f);
     plan.Write(new Vector3(0,2,0),zone,colour,band,volume,out int spSolo);float wSolo=spSolo>0?zone[0].w:-1f;
     zA.Fade=keepA;zB.Fade=keepB;
     Check(spCross==1&&Mathf.Abs(wCross-.75f)<1e-5f&&spSolo==1&&Mathf.Abs(wSolo-eA)<1e-5f,
           "same-place hand-over: fades .5/.5 -> one slot w "+wCross.ToString("0.####")+" (union .75); one member alone -> its eased fade "+wSolo.ToString("0.####"));
    }

    // ---- AC-W18 OpticalDepth: closed form vs 4,000-step numeric integration ----
    {
     var c=new Vector3(0,400,0);const float R=300f,Hh=150f;var S=new Vector3(R,Hh,R);
     float Numeric(Vector3 eye,Vector3 dir,float tNear,float tMax)
     {
      dir=dir.normalized;float big=Mathf.Max(R,Hh)*1.001f;var oc=eye-c;float b=Vector3.Dot(oc,dir),disc=b*b-(oc.sqrMagnitude-big*big);
      if(disc<=0)return 0;float sq=Mathf.Sqrt(disc);double lo=Math.Max(tNear,-b-sq),hi=Math.Min(tMax,-b+sq);if(hi<=lo)return 0;   // bracket = the bounding sphere (independent)
      double sum=0,dt=(hi-lo)/4000.0;
      for(int i=0;i<4000;i++){double t=lo+(i+.5)*dt;double x=(eye.x+dir.x*t-c.x)/R,y=(eye.y+dir.y*t-c.y)/Hh,z=(eye.z+dir.z*t-c.z)/R;double f=1-(x*x+y*y+z*z);if(f>0)sum+=f*dt;}
      return (float)sum;
     }
     float centre=EventWashPlanner308.OpticalDepth(new Vector3(-2000,400,0),Vector3.right,30,1e6f,c,R,Hh);
     Check(Mathf.Abs(centre-4f*R/3f)<=.001f*4f*R/3f,"AC-W18 horizontal centre pass = 4R/3 = "+(4f*R/3f).ToString("0.###")+" ± .1%: "+centre.ToString("0.###"));
     float miss=EventWashPlanner308.OpticalDepth(new Vector3(-2000,400,0),new Vector3(1,1,0),30,1e6f,c,R,Hh);
     float behind=EventWashPlanner308.OpticalDepth(new Vector3(-2000,400,0),Vector3.left,30,1e6f,c,R,Hh);
     float inverted=EventWashPlanner308.OpticalDepth(new Vector3(-2000,400,0),Vector3.right,500,400,c,R,Hh);
     float blocked=EventWashPlanner308.OpticalDepth(new Vector3(-2000,400,0),Vector3.right,30,1500,c,R,Hh);
     Check(miss==0f&&behind==0f&&inverted==0f&&blocked==0f,"AC-W18 miss / volume behind / tMax < tNear / surface before the volume -> exactly 0 ("+miss+", "+behind+", "+inverted+", "+blocked+")");
     var rng=new System.Random(308);
     float U(float a,float b)=>a+(float)rng.NextDouble()*(b-a);
     Vector3 Dir(){Vector3 v;do v=new Vector3(U(-1,1),U(-1,1),U(-1,1));while(v.sqrMagnitude>1f||v.sqrMagnitude<1e-3f);return v.normalized;}
     Vector3 Ball(float r){Vector3 v;do v=new Vector3(U(-1,1),U(-1,1),U(-1,1));while(v.sqrMagnitude>1f);return v*r;}
     var cases=new List<string>();bool all=true;float worst=0;
     for(int k=0;k<12;k++)
     {
      Vector3 eye,dir;float tNear=30,tMax=1e6f;string label;
      switch(k%4)
      {
       case 0:{var target=c+Vector3.Scale(Ball(.9f),S);eye=c+Dir()*U(800,2500);dir=target-eye;label="outside";break;}
       case 1:{eye=c+Vector3.Scale(Ball(.6f),S);dir=Dir();label="inside+near clip";break;}
       case 2:{var target=c+Vector3.Scale(Ball(.9f),S);eye=c+Dir()*U(800,2500);dir=target-eye;tMax=dir.magnitude*U(.6f,1f);label="surface cut";break;}
       default:
       {
        var nrm=Dir();var w=Vector3.Cross(nrm,Dir()).normalized;   // unit-space tangent at |p| = .97
        var pe=.97f*nrm-6f*w;eye=c+Vector3.Scale(pe,S);dir=Vector3.Scale(w,S);label="grazing";break;
       }
      }
      float closed=EventWashPlanner308.OpticalDepth(eye,dir,tNear,tMax,c,R,Hh),num=Numeric(eye,dir,tNear,tMax);
      float err=Mathf.Abs(closed-num);bool ok=err<=Mathf.Max(.01f*num,.05f)&&(k%4!=3||closed>0f);
      if(num>0)worst=Mathf.Max(worst,err/num);
      if(!ok)all=false;cases.Add(label+" "+closed.ToString("0.##")+"/"+num.ToString("0.##")+(ok?"":"!"));
     }
     Check(all,"AC-W18 12 random rays closed vs numeric within 1% (worst "+(worst*100f).ToString("0.###")+"%): "+string.Join(", ",cases));
     float inside=EventWashPlanner308.OpticalDepth(c,Vector3.up,30,1e6f,c,R,Hh);float insideWant=(Hh-30f)-(Hh*Hh*Hh-30f*30f*30f)/(3f*Hh*Hh);
     Check(Mathf.Abs(inside-insideWant)<1e-3f*insideWant,"AC-W18 camera at the centre looking up, near clip 30 m: "+inside.ToString("0.###")+" (analytic "+insideWant.ToString("0.###")+")");
    }

    // ---- availability and fades ----
    plan.Poll(state,defeated);plan.Step(0,true);
    Check(Z(plan,"s_rest").Fade==1&&Z(plan,"s_later").Fade==0,"availability: s_rest 1, s_later 0 (prerequisite unmet)");
    for(int f=0;f<120;f++)plan.Step(1/60f,false);
    Check(Z(plan,"s_rest").Fade==1,"steady state before completion: fade 1");
    state.Completed.Add("s_rest");plan.Poll(state,defeated);
    // completing s_rest fades it out and opens s_later (prerequisite) which fades in at the same time
    var restZ=Z(plan,"s_rest");var later=Z(plan,"s_later");float t=0,tOut=-1,tIn=-1;
    while((tOut<0||tIn<0)&&t<20){plan.Step(1/60f,false);t+=1/60f;if(tOut<0&&restZ.Fade<=0)tOut=t;if(tIn<0&&later.Fade>=1)tIn=t;}
    Check(tOut>=0&&Mathf.Abs(tOut-sheet.FadeOutSeconds)<=.5f,"AC-W10 fade-out after completion "+tOut.ToString("0.00")+" s (FadeOutSeconds "+sheet.FadeOutSeconds+" ±.5)");
    Check(tIn>=0&&Mathf.Abs(tIn-sheet.FadeInSeconds)<=.5f,"newly available stage fades in over "+tIn.ToString("0.00")+" s (FadeInSeconds "+sheet.FadeInSeconds+")");

    // ---- nearest 8 of more than 8, distance cut ----
    plan.Step(0,true);n=plan.Write(new Vector3(3450,0,0),zone,colour,band,volume,out _);
    var picked=Enumerable.Range(0,n).Select(i=>plan[plan.SelectedZone(i)].StageId).ToArray();
    Check(n==8&&picked.All(id=>id.StartsWith("s_far",StringComparison.Ordinal)),"9+ active zones: the nearest 8 ("+string.Join(",",picked)+")");
    Check(zone.Skip(n).All(v=>v==Vector4.zero),"unused shader slots zeroed");
    sheet.MaxDistance=50;int far=plan.Write(new Vector3(-5000,0,0),zone,colour,band,volume,out _);
    Check(far==0,"MaxDistance / VolumeMaxDistance cut");sheet.MaxDistance=1600;

    // ---- GC: steady polling 0 B (volume array included) ----
    plan.Poll(state,defeated);plan.Step(1/60f,false);plan.Write(Vector3.zero,zone,colour,band,volume,out _);
    long before=GC.GetAllocatedBytesForCurrentThread();
    for(int f=0;f<600;f++){if(f%30==0)plan.Poll(state,defeated);plan.Step(1/60f,false);plan.Write(new Vector3(f,0,0),zone,colour,band,volume,out _);}
    long alloc=GC.GetAllocatedBytesForCurrentThread()-before;
    Check(alloc==0,"AC-W13 (planner) 600 frames Poll/Step/Write (zone, colour, band, volume) allocate "+alloc+" B");

    // ---- AC-W12 source checks of the new files ----
    foreach(var name in new[]{"EventWashSheetSO","EventWashPlanner308","EventWashDriver308"})
    {
     var file=AssetDatabase.FindAssets(name+" t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p=>Path.GetFileNameWithoutExtension(p)==name);
     if(file==null){Check(false,"AC-W12 "+name+".cs not found");continue;}
     var code=Regex.Replace(File.ReadAllText(PostLedger308.Abs(file)),@"//[^\n]*|/\*.*?\*/|""(?:\\.|[^""\\])*""","",RegexOptions.Singleline);
     int destination=Regex.Matches(code,@"\.Destination\b").Count;
     int staticMutable=Regex.Matches(code,@"\bstatic\s+(?!readonly\b|void\b|class\b|bool\s+\w+\s*\(|string\s+\w+\s*\(|Color\s+\w+\s*\(|EventWashSheetSO\.Meaning\s+\w+\s*\(|implicit\b|explicit\b)[\w<>\[\],\.\s]+\s+\w+\s*(=|;)").Count;
     int odin=Regex.Matches(code,@"Sirenix|Odin").Count;
     int singleton=Regex.Matches(code,@"\bstatic\b[^;{]*\bInstance\b").Count;
     int matWrite=Regex.Matches(code,@"\.(sharedMaterial|material)\b|Material\.Set|\.SetFloat\(|\.SetColor\(").Count;
     Check(destination==0&&staticMutable==0&&odin==0&&singleton==0&&matWrite==0,"AC-W12 "+name+": .Destination "+destination+", static mutable "+staticMutable+", Odin "+odin+", singleton "+singleton+", material writes "+matWrite);
    }
   }
   finally{foreach(var x in temps)if(x!=null)Object.DestroyImmediate(x);}
   string report=(fail==0?"PASS":"FAIL "+fail)+" — "+sb;
   File.WriteAllText(Path.Combine(ChecksDir,"wash308-unit.txt"),report);
   return report;
  }
 }
}
