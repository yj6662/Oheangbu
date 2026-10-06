using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 // content-apply (SPEC-CONTENT-PACING-308 §2 새 길, §3 VehicleRequiredFact, §4 쉼터 4곳, §5 호송 문구, §6 J1 복원, §8 적 배치, §9 길목 비트).
 // Candidate XZ are the Spec's; every height is the opened scene's physical ground (the offline height field is 15-60 m off around
 // the plateau). Texts are TEST drafts for the NARR-VOICE read-through; none of them instructs (no key names, no "~하면 ~한다").
 public static partial class Content308
 {
  // The constants below are the #308 base. A relayout data file (content308_relayout.json, Content308.Relayout.cs) overrides rows by
  // id and adds rows; with no such file every list here is used exactly as written (RestsNow308 / PointsNow308 / … return them).
  // Relayout-only fields: Pin (never moved by Flat308; a physical slope over MaxSlope refuses), Force (the 12 m keep rule does not
  // hold the row), explicit feet XZ / respawn yaw, Group (ledger group, T7), YOffline (the offline height; only reported).
  sealed class RestSpec308
  {
   public string Id,Label,Prompt,Text,Group="";public float X,Z,MaxSlope=8f,Radius=3f;public bool Shop;
   public bool Pin,Force,HasFeet,HasYaw;public float FeetX,FeetZ,Yaw,YOffline=float.NaN;
   public RestSpec308 Copy()=>(RestSpec308)MemberwiseClone();
  }
  static readonly RestSpec308[] Rests308=
  {
   new RestSpec308{Id="logging_front_rest308",Label="벌목장 앞 성황당",Prompt="성황당에서 쉬기",X=3275,Z=2642},
   new RestSpec308{Id="deep_fork_rest308",Label="심부 갈림 성황당",Prompt="성황당에서 쉬기",X=3266,Z=3090},
   // Shop = 정비 (WorldMacroCheckpointRules.FindShop: within the rest point's radius); 4.5 m covers the keeper's talk radius
   new RestSpec308{Id="hunter_inn308",Label="사냥꾼 주막",Prompt="사냥꾼 주막에서 쉬기",X=3205,Z=3215,MaxSlope=5f,Radius=4.5f,Shop=true},
   new RestSpec308{Id="south_gate_front_rest308",Label="남문 앞 성황당",Prompt="성황당에서 쉬기",X=1923,Z=2450},
  };
  const string RestText308="잠시 숨을 고른다.";
  const float RestFeetOffset308=2.2f;
  // Anchor = a rest id: X / Z are then an offset from that rest's grounded point (the keeper stands by his own inn)
  sealed class PointSpec308{public string Id,Prompt,Text,Speaker="",Anchor,Group="";public PrologueInteractionKind Kind;public float X,Z,Radius=2.5f,YOffline=float.NaN;public int Currency;public bool Force;public PrologueContentSO.PointService306[] Services;}
  // agwi's new field-boss place; the 여막 터 reward sits beside it
  const float AgwiX308=2180,AgwiZ308=1830;
  static PointSpec308[] Points308()=>new[]
  {
   new PointSpec308{Id="logging_inquiry",Kind=PrologueInteractionKind.Evidence,X=3318,Z=2688,Prompt="벌목장 과성장 조사",
    Text="잘린 나이테를 새 뿌리가 비집고 나왔다. 박힌 쇠도끼 둘레만 비어 있다."},
   new PointSpec308{Id="geumpyo_stele308",Kind=PrologueInteractionKind.Evidence,X=3302,Z=2218,Prompt="금표 비석",
    Text="봉산 금표. 여기서부터 나무를 베지 말라고 새겼다."},
   new PointSpec308{Id="village_empty_house308",Kind=PrologueInteractionKind.Evidence,X=2790,Z=2287,Prompt="빈집 툇마루",
    Text="툇마루에 먼지가 앉았다. 문설주의 방(榜)은 반쯤 뜯겨 나갔다."},
   new PointSpec308{Id="sanctuary_trace308",Kind=PrologueInteractionKind.Evidence,X=3196,Z=3500,Prompt="긁힌 돌바닥",
    Text="돌바닥에 비늘이 긁고 간 자국이 겹겹이 나 있다."},
   new PointSpec308{Id="road_yeomak308",Kind=PrologueInteractionKind.Evidence,X=AgwiNow308().x+12,Z=AgwiNow308().y+6,Prompt="여막 터",Currency=60,
    Text="무너진 여막 터다. 주춧돌 틈에 누가 꾸려 둔 엽전 꾸러미가 그대로 남았다."},
   new PointSpec308{Id="hunter_innkeeper308",Kind=PrologueInteractionKind.Conversation,Anchor="hunter_inn308",X=1.8f,Z=1.2f,Radius=2.3f,Speaker="사냥꾼 주막 주인",
    Prompt="사냥꾼 주막 주인과 이야기",Text="이 깊은 데까지 오는 이는 드무오. 쉬어 가려거든 쉬고, 볼일이 끝나면 내려가시오.",
    Services=new[]{new PrologueContentSO.PointService306{Kind=PrologueContentSO.PointServiceKind306.Rest,Target="hunter_inn308"},
                   new PrologueContentSO.PointService306{Kind=PrologueContentSO.PointServiceKind306.Maintain}}},
  };
  static readonly (string id,float x,float z)[] PointMoves308={("logger",3040,2428)};
  // (id, XZ, RespawnOnRest override: -1 keep, 0 false, 1 true)
  static readonly (string id,float x,float z,int respawn)[] EncounterMoves308={("folklore298/dokkaebi",2930,2340,-1),("folklore298/agwi",AgwiX308,AgwiZ308,0)};
  static readonly (string id,float x,float z)[] EncounterAdds308={("forest_beast308/0",3320,2925),("forest_beast308/1",3312,2935)};
  const string BeastTemplate308="mine_beast/1";
  // R308 새 길: 약초꾼 길 입구 (herb_return259 first bend) -> 금표 암릉 국 바위턱 (candidate) -> 안부 -> 길가 벌목 흔적 (access_village_cut_trace end)
  static readonly Vector2[] NewRun308={new Vector2(3031.875f,2803f),new Vector2(3010f,2740f),new Vector2(2985f,2640f),new Vector2(2954.3125f,2514.8125f)};
  const float RunStep308=2f,BendStep308=8f,RunGap308=40f;
  const string RouteId308="herb_amneung_cut308";
  const string VehicleFact308="defeated:cheongryong";

  sealed class Plan308
  {
   public readonly List<PrologueContentSO.Point> Points=new List<PrologueContentSO.Point>();
   public readonly List<(string id,Vector3 at)> PointMoves=new List<(string,Vector3)>();
   public readonly List<WorldMacroPlaytestSO.CheckpointSpec> Checkpoints=new List<WorldMacroPlaytestSO.CheckpointSpec>();
   public readonly List<(string id,Vector3 feet,int respawn)> EncounterMoves=new List<(string,Vector3,int)>();
   public readonly List<(string id,Vector3 feet)> EncounterAdds=new List<(string,Vector3)>();
   // relayout rests_escort rows (T5): position / feet / yaw only - prompt, text, radius, label and shop are never written
   public readonly List<(string id,Vector3 at,Vector3 feet,float yaw)> EscortRests=new List<(string,Vector3,Vector3,float)>();
   public readonly List<(string id,Vector2 xz)> PlaceMoves=new List<(string,Vector2)>();
   public Vector3[] Run=Array.Empty<Vector3>();
   public Vector2[] Bends=Array.Empty<Vector2>();
  }

  static string ContentApply308(string scenePath)
  {
   var scene=Open308(scenePath);var session=Session308(scene);var t=Targets308[scene.path];
   var content=Load308<WorldMacroPlaytestSO>(t.content);var layout=Load308<CompactWorldLayoutSO>(t.layout);
   if(session.Content!=content)throw new Refuse308(scene.path+" session Content is "+AssetDatabase.GetAssetPath(session.Content)+", expected "+t.content);
   PrepareGround308(scene,session);
   var notes=new List<string>();var plan=MakePlan308(content,notes);
   var relayout=Relayout308();if(relayout!=null)notes.Add("relayout data "+RelayoutFile308+" sha "+Short308(relayout.Sha)+(relayout.Off.Count>0?", groups off: "+string.Join(", ",relayout.Off.OrderBy(x=>x,StringComparer.Ordinal)):""));
   string key="content308_"+Key308(scene.path);
   var ledger=ReadLedger308(key)??new Ledger308{kind="content",scene=scene.path,created=DateTime.UtcNow.ToString("O")};
   string backup=BackupDir308();Write308 a=null,b=null;
   try
   {
    a=Edit308(content,backup,ledger,()=>ApplyContent308(content,plan,notes));
    b=Edit308(layout,backup,ledger,()=>ApplyLayout308(layout,plan));
   }
   finally{if(a!=null||b!=null)WriteLedger308(key,ledger);}
   return Report308("content-apply "+scene.path,new List<Write308>{a,b},notes,key);
  }

  // ---------- plan: grounded positions computed before any edit ----------

  // Positions are decided once: an item #308 already placed near its candidate (a previous apply, or the scene pass refining it on
  // its compound / the re-baked NavMesh) keeps its current position, so a re-run after the scene pass changes nothing. Only missing
  // or far-off items are grounded here.
  const float KeepNear308=12f;
  static Plan308 MakePlan308(WorldMacroPlaytestSO content,List<string> notes)
  {
   var plan=new Plan308();var path=content.MainPath??Array.Empty<Vector3>();
   var points=content.Points??Array.Empty<PrologueContentSO.Point>();var checkpoints=content.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>();
   var encounters=content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>();
   PrologueContentSO.Point Have(string id,float x,float z,float near)=>Array.Find(points,q=>q!=null&&q.Id==id&&Vector2.Distance(new Vector2(q.Position.x,q.Position.z),new Vector2(x,z))<=near);
   // a `force` row is not held by the keep rule: something placed nearer than the keep distance but more than force_tol_m from
   // the target counts as not placed (it is grounded again at the target)
   PrologueContentSO.Point Forced(PrologueContentSO.Point had,bool force,float x,float z)=>
    had!=null&&force&&Vector2.Distance(new Vector2(had.Position.x,had.Position.z),new Vector2(x,z))>ForceTol308()?null:had;
   foreach(var r in RestsNow308())
   {
    var had=Forced(Have(r.Id,r.X,r.Z,KeepNear308),r.Force,r.X,r.Z);var hadCp=Array.Find(checkpoints,c=>c!=null&&c.Id==r.Id&&c.IsConfigured);
    var at=r.Pin?Pinned308(r.Id,r.X,r.Z,r.MaxSlope,had?.Position,r.YOffline,notes):had!=null?had.Position:Flat308(r.Id,r.X,r.Z,r.MaxSlope,10f,notes);
    Vector3 feet;float yaw;
    if(r.HasFeet)
    {
     // data feet: grounded where the row says; the respawn yaw is the row's, else it looks from the shrine past the feet
     feet=PinnedFeet308(r.Id,r.FeetX,r.FeetZ,hadCp?.Feet,notes);
     yaw=r.HasYaw?Mathf.Repeat(r.Yaw,360f):Mathf.Repeat(Harness303.YawTo(at,feet),360f);
    }
    else if(had!=null&&hadCp!=null&&Harness303.Flat(hadCp.Feet,at)<=6f){feet=hadCp.Feet;yaw=hadCp.Yaw;}
    else
    {
     // feet: 2.2 m from the shrine toward the nearest road point, facing away from the shrine (respawn looks down the road)
     var road=Nearest308(path,at);var dir=new Vector3(road.x-at.x,0,road.z-at.z);dir=dir.sqrMagnitude>.01f?dir.normalized:Vector3.forward;
     var feetXZ=at+dir*RestFeetOffset308;feet=Ground308(feetXZ.x,feetXZ.z,out var g,out float slope)&&slope<=35f?g:at;
     yaw=r.HasYaw?Mathf.Repeat(r.Yaw,360f):Mathf.Repeat(Harness303.YawTo(at,feet),360f);
     notes.Add(r.Id+" point "+V308(at)+" feet "+V308(feet)+" (nearest road point "+F308(Harness303.Flat(at,road),"F0")+" m)");
    }
    plan.Points.Add(new PrologueContentSO.Point{Id=r.Id,Kind=PrologueInteractionKind.Rest,Position=at,Prompt=r.Prompt,Text=r.Text??RestText308,Radius=r.Radius});
    plan.Checkpoints.Add(new WorldMacroPlaytestSO.CheckpointSpec{Id=r.Id,Label=r.Label,Feet=feet,Yaw=yaw,Shop=r.Shop,ShopRequiredStageId=""});
   }
   foreach(var p in PointsNow308())
   {
    if(p.Anchor!=null){var anchor=plan.Points.First(x=>x.Id==p.Anchor).Position;p.X+=anchor.x;p.Z+=anchor.z;}
    var had=Forced(Have(p.Id,p.X,p.Z,KeepNear308),p.Force,p.X,p.Z);
    var at=had!=null?had.Position:p.Kind==PrologueInteractionKind.Conversation?Flat308(p.Id,p.X,p.Z,12f,3f,notes):Ground308(p.X,p.Z,out var g,out _)?g:throw new Refuse308("no ground under "+p.Id);
    if(had==null)OfflineY308(p.Id,at,p.YOffline,notes);
    plan.Points.Add(new PrologueContentSO.Point{Id=p.Id,Kind=p.Kind,Position=at,Prompt=p.Prompt,Text=p.Text,Currency=p.Currency,Radius=p.Radius,
     Speaker=p.Speaker??"",Services=p.Services??Array.Empty<PrologueContentSO.PointService306>()});
   }
   foreach(var m in PointMovesNow308())
   {
    var had=Forced(Have(m.Id,m.X,m.Z,6f),m.Force,m.X,m.Z);
    if(m.Pin&&!Array.Exists(points,q=>q!=null&&q.Id==m.Id)){notes.Add("WARN point "+m.Id+" not in this content; not moved");continue;}
    var at=m.Pin?Pinned308(m.Id,m.X,m.Z,m.MaxSlope,had?.Position,m.YOffline,notes):had!=null?had.Position:Flat308(m.Id,m.X,m.Z,12f,6f,notes);
    plan.PointMoves.Add((m.Id,at));
   }
   foreach(var e in EncounterMovesNow308())
   {
    var had=Array.Find(encounters,q=>q!=null&&q.Id==e.Id&&Vector2.Distance(new Vector2(q.Feet.x,q.Feet.z),new Vector2(e.X,e.Z))<=3f);
    if(had==null&&!Array.Exists(encounters,q=>q!=null&&q.Id==e.Id)){notes.Add(e.Id+" not in this content (scene without it); skipped");continue;}
    var feet=had!=null?had.Feet:Nav308(e.Id,e.X,e.Z,notes);
    if(had==null)OfflineY308(e.Id,feet,e.YOffline,notes);
    else if(e.Force)
    {
     // a force row is snapped again (the NavMesh may have been baked since the feet were written); the same answer writes nothing
     var again=Nav308(e.Id,e.X,e.Z,notes);
     if(Vector3.Distance(again,had.Feet)>ResnapTol308())feet=again;
    }
    plan.EncounterMoves.Add((e.Id,feet,e.Respawn));
   }
   EscortRestPlan308(plan,points,checkpoints,notes);
   foreach(var e in EncounterAdds308)
   {
    var had=Array.Find(encounters,q=>q!=null&&q.Id==e.id&&Vector2.Distance(new Vector2(q.Feet.x,q.Feet.z),new Vector2(e.x,e.z))<=3f);
    plan.EncounterAdds.Add((e.id,had!=null?had.Feet:Nav308(e.id,e.x,e.z,notes)));
   }
   foreach(var m in PlaceMovesNow308())plan.PlaceMoves.Add(m);
   plan.Bends=Resample308(NewRun308,BendStep308).ToArray();
   // new run: kept when present; else resampled every 2 m on the ground (terrain first)
   if((content.BranchPath??Array.Empty<Vector3>()).Any(q=>Vector2.Distance(new Vector2(q.x,q.z),NewRun308[0])<1f)){plan.Run=null;notes.Add("run "+RouteId308+" already in BranchPath; kept");return plan;}
   var run=new List<Vector3>();
   foreach(var q in Resample308(NewRun308,RunStep308)){if(Ground308(q.x,q.y,out var g,out _))run.Add(g);else notes.Add("WARN run point ("+F308(q.x,"F0")+", "+F308(q.y,"F0")+") has no ground; skipped");}
   plan.Run=run.ToArray();
   notes.Add("run "+RouteId308+": "+plan.Run.Length+" points, "+F308(Length308(NewRun308),"F0")+" m (XZ), y "+(run.Count>0?F308(run.Min(v=>v.y),"F0")+".."+F308(run.Max(v=>v.y),"F0"):"-"));
   return plan;
  }
  static Vector3 Nav308(string id,float x,float z,List<string> notes)
  {
   if(!Ground308(x,z,out var g,out _))throw new Refuse308("no ground under "+id);
   if(NavMesh.SamplePosition(g,out var hit,3f,NavMesh.AllAreas))return hit.position;
   notes.Add("WARN "+id+": no NavMesh within 3 m of "+V308(g)+" (re-bake with nav-bake, then re-run content-apply)");
   return g;
  }
  static Vector3 Nearest308(Vector3[] path,Vector3 at)
  {
   Vector3 best=at;float d=float.MaxValue;
   foreach(var p in path){float q=Harness303.Flat(p,at);if(q<d){d=q;best=p;}}
   return best;
  }
  static float Length308(Vector2[] poly){float l=0;for(int i=1;i<poly.Length;i++)l+=Vector2.Distance(poly[i-1],poly[i]);return l;}
  static List<Vector2> Resample308(Vector2[] poly,float step)
  {
   var o=new List<Vector2>{poly[0]};
   for(int i=1;i<poly.Length;i++)
   {
    float seg=Vector2.Distance(poly[i-1],poly[i]);int n=Mathf.Max(1,Mathf.CeilToInt(seg/step));
    for(int k=1;k<=n;k++)o.Add(Vector2.Lerp(poly[i-1],poly[i],k/(float)n));
   }
   return o;
  }

  // ---------- edits ----------

  static List<string> ApplyContent308(WorldMacroPlaytestSO content,Plan308 plan,List<string> notes)
  {
   var ch=new List<string>();
   // points (replace in place by Id, else append)
   var points=(content.Points??Array.Empty<PrologueContentSO.Point>()).ToList();
   foreach(var p in plan.Points)
   {
    int i=points.FindIndex(x=>x!=null&&x.Id==p.Id);
    if(i<0){points.Add(p);ch.Add("point "+p.Id+" added at "+V308(p.Position));continue;}
    var old=points[i];
    // keep authored fields that are not #308's (RequiredCompleted, LockedText, Lines) from an existing entry
    p.RequiredCompleted=old.RequiredCompleted??Array.Empty<string>();p.RequiredDefeated=old.RequiredDefeated??Array.Empty<string>();
    p.LockedText=old.LockedText;if(p.Lines==null||p.Lines.Length==0)p.Lines=old.Lines??Array.Empty<string>();
    if(JsonUtility.ToJson(old)!=JsonUtility.ToJson(p))ch.Add("point "+p.Id+" updated "+V308(old.Position)+" -> "+V308(p.Position));
    points[i]=p;
   }
   foreach(var (id,at) in plan.PointMoves)
   {
    var p=points.FirstOrDefault(x=>x!=null&&x.Id==id);
    if(p==null){notes.Add("WARN point "+id+" not in this content; not moved");continue;}
    Set308(ch,"point "+id+".Position",ref p.Position,at);
   }
   // relayout rests_escort (T5): the rest point follows the row; prompt / text / radius stay as they are
   foreach(var (id,at,_,_) in plan.EscortRests)
   {
    var p=points.FirstOrDefault(x=>x!=null&&x.Id==id);
    if(p==null){notes.Add("escort rest "+id+" not in this content; skipped");continue;}
    Set308(ch,"point "+id+".Position",ref p.Position,at);
   }
   content.Points=points.ToArray();
   // checkpoints
   var checkpoints=(content.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>()).ToList();
   foreach(var c in plan.Checkpoints)
   {
    int i=checkpoints.FindIndex(x=>x!=null&&x.Id==c.Id);
    if(i<0){checkpoints.Add(c);ch.Add("checkpoint "+c.Id+" added feet "+V308(c.Feet)+(c.Shop?" shop":""));continue;}
    if(JsonUtility.ToJson(checkpoints[i])!=JsonUtility.ToJson(c))ch.Add("checkpoint "+c.Id+" updated feet "+V308(checkpoints[i].Feet)+" -> "+V308(c.Feet));
    checkpoints[i]=c;
   }
   // relayout rests_escort (T5): feet and respawn yaw only (Label / Shop untouched)
   foreach(var (id,_,feet,yaw) in plan.EscortRests)
   {
    var c=checkpoints.FirstOrDefault(x=>x!=null&&x.Id==id);
    if(c==null){notes.Add("escort rest checkpoint "+id+" not in this content; skipped");continue;}
    Set308(ch,"checkpoint "+id+".Feet",ref c.Feet,feet);Set308(ch,"checkpoint "+id+".Yaw",ref c.Yaw,yaw);
   }
   content.Checkpoints=checkpoints.ToArray();
   // encounters
   var encounters=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).ToList();
   foreach(var (id,feet,respawn) in plan.EncounterMoves)
   {
    var e=encounters.FirstOrDefault(x=>x!=null&&x.Id==id);
    if(e==null){notes.Add(id+" not in this content (scene without it); skipped");continue;}
    var delta=feet-e.Feet;
    if(delta.sqrMagnitude>1e-6f)
    {
     ch.Add("encounter "+id+" feet "+V308(e.Feet)+" -> "+V308(feet));
     e.Feet=feet;e.Patrol=(e.Patrol??Array.Empty<Vector3>()).Select(q=>q+delta).ToArray();
    }
    if(respawn>=0)Set308(ch,"encounter "+id+".RespawnOnRest",ref e.RespawnOnRest,respawn==1);
   }
   var template=encounters.FirstOrDefault(x=>x!=null&&x.Id==BeastTemplate308);
   if(template==null)notes.Add("WARN template "+BeastTemplate308+" missing; forest beasts use defaults");
   foreach(var (id,feet) in plan.EncounterAdds)
   {
    var patrol=template!=null&&template.Patrol!=null&&template.Patrol.Length>0?template.Patrol.Select(q=>q-template.Feet+feet).ToArray():new[]{feet,feet+Vector3.right*.5f};
    var spec=new WorldMacroPlaytestSO.Encounter{Id=id,ContentId=template!=null?template.ContentId:"mine_beast",Feet=feet,Patrol=patrol,Ranged=false,RespawnOnRest=true,
     Detection=template!=null?template.Detection:16,Leash=template!=null?template.Leash:28,Speed=template!=null?template.Speed:2.6f,Activation=template!=null?template.Activation:180};
    int i=encounters.FindIndex(x=>x!=null&&x.Id==id);
    if(i<0){encounters.Add(spec);ch.Add("encounter "+id+" added at "+V308(feet));}
    else{if(JsonUtility.ToJson(encounters[i])!=JsonUtility.ToJson(spec))ch.Add("encounter "+id+" updated at "+V308(feet));encounters[i]=spec;}
   }
   content.Encounters=encounters.ToArray();
   // BranchPath: the new run as its own run (> 40 m from the point before it); a previous #308 run is replaced in place
   if(plan.Run!=null)
   {
    var branch=(content.BranchPath??Array.Empty<Vector3>()).ToList();int runsBefore=Runs308(branch.ToArray());
    int start=branch.FindIndex(q=>Vector2.Distance(new Vector2(q.x,q.z),NewRun308[0])<1f);
    int end=start<0?-1:branch.FindIndex(start,q=>Vector2.Distance(new Vector2(q.x,q.z),NewRun308[NewRun308.Length-1])<1f);
    int insertAt=branch.Count;
    if(start>=0&&end>=start){branch.RemoveRange(start,end-start+1);insertAt=start;}
    if(insertAt>0&&plan.Run.Length>0&&Harness303.Flat(branch[insertAt-1],plan.Run[0])<=RunGap308)notes.Add("WARN the new run starts within "+RunGap308+" m of the BranchPath point before it (it would merge into that run)");
    branch.InsertRange(insertAt,plan.Run);
    if(!(content.BranchPath??Array.Empty<Vector3>()).SequenceEqual(branch))
     ch.Add("BranchPath run "+RouteId308+" "+(start>=0?"replaced":"appended")+": "+plan.Run.Length+" points; runs "+runsBefore+" -> "+Runs308(branch.ToArray()));
    content.BranchPath=branch.ToArray();
   }
   // §5 호송 문구 (StartBoardNotice LEGACY = empty), §3 VehicleRequiredFact
   var v=content.EscortVoice??(content.EscortVoice=new WorldMacroPlaytestSO.EscortVoice306());
   Set308(ch,"EscortVoice.ContractedText",ref v.ContractedText,"짐은 숲이 잠잠해진 뒤에 내보내겠소. 숲 소리가 그치기 전엔 길에 올리지 않소.");
   Set308(ch,"EscortVoice.ContractedReadyText",ref v.ContractedReadyText,"숲 소리가 그쳤구려. 짐은 정차소에 내어 두었소.");
   Set308(ch,"EscortVoice.StationWaitingText",ref v.StationWaitingText,"봉인 화물이 아직 묶인 채 놓여 있다.");
   Set308(ch,"EscortVoice.StationReadyText",ref v.StationReadyText,"봉인 화물이 떠날 채비를 마쳤다.");
   Set308(ch,"EscortVoice.StartBoardNotice",ref v.StartBoardNotice,"");
   Set308(ch,"VehicleRequiredFact",ref content.VehicleRequiredFact,VehicleFact308);
   return ch;
  }

  static List<string> ApplyLayout308(CompactWorldLayoutSO layout,Plan308 plan)
  {
   var ch=new List<string>();
   if(layout.Places==null||!layout.Places.Any(p=>p!=null&&p.Id=="herb_path")||!layout.Places.Any(p=>p!=null&&p.Id=="village_cut_trace"))
    throw new Refuse308(AssetDatabase.GetAssetPath(layout)+" has no herb_path / village_cut_trace place");
   var route=new CompactWorldLayoutSO.Route{Id=RouteId308,From="herb_path",To="village_cut_trace",Role=CompactRouteRole.ReturnShortcut,RequiredAbility="",
    OneWay=false,GradeForVehicle=false,Traversal=CompactTraversal.FootOnly,Bends=plan.Bends,Width=2.6f};
   var routes=(layout.Routes??Array.Empty<CompactWorldLayoutSO.Route>()).ToList();
   int i=routes.FindIndex(r=>r!=null&&r.Id==RouteId308);
   if(i<0){routes.Add(route);ch.Add("route "+RouteId308+" added ("+plan.Bends.Length+" bends, ReturnShortcut, FootOnly)");}
   else{if(JsonUtility.ToJson(routes[i])!=JsonUtility.ToJson(route))ch.Add("route "+RouteId308+" updated");routes[i]=route;}
   layout.Routes=routes.ToArray();
   // relayout layout_places (T9): a place that follows a moved encounter (XZ, and the realm the layout itself gives that XZ)
   foreach(var (id,xz) in plan.PlaceMoves)
   {
    var place=Array.Find(layout.Places,p=>p!=null&&p.Id==id);if(place==null)continue;
    if((place.XZ-xz).sqrMagnitude>1e-6f){ch.Add("place "+id+".XZ: ("+F308(place.XZ.x,"F1")+", "+F308(place.XZ.y,"F1")+") -> ("+F308(xz.x,"F1")+", "+F308(xz.y,"F1")+")");place.XZ=xz;}
    var realm=layout.RealmAt(xz);
    if(realm!=null&&!string.Equals(place.Realm,realm.Id,StringComparison.OrdinalIgnoreCase)){ch.Add("place "+id+".Realm: "+place.Realm+" -> "+realm.Id);place.Realm=realm.Id;}
   }
   return ch;
  }

  // runs = maximal chains whose consecutive points are <= 40 m apart (XZ)
  static int Runs308(Vector3[] path)
  {
   if(path==null||path.Length==0)return 0;int runs=1;
   for(int i=1;i<path.Length;i++)if(Harness303.Flat(path[i-1],path[i])>RunGap308)runs++;
   return runs;
  }
 }
}
