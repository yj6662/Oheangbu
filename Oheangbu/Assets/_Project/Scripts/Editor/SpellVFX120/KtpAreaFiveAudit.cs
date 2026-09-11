using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Spellcraft;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 [InitializeOnLoad] public static class KtpAreaFiveAudit
 {
  const string Key="KtpAreaFiveAudit";const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
  [Serializable] class Report {public string status;public List<string> checks=new List<string>(),errors=new List<string>();}
  static KtpAreaFiveAudit(){EditorApplication.update+=Tick;}
  public static string Start(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stopped editor required");SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;return "AREA_AUDIT_REQUESTED";}
  static void Tick()
  {
   if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;SessionState.SetBool(Key,false);var r=new Report();
   try{Run(r);r.status="PASS";}catch(Exception e){r.status="FAIL";r.errors.Add(e.ToString());}
   File.WriteAllText(Path.Combine(Vfx120Editor.Output,"AreaFive","audit.json"),JsonUtility.ToJson(r,true));EditorApplication.isPlaying=false;
  }
  static void Need(Report r,bool ok,string label){if(!ok)throw new Exception(label);r.checks.Add(label);}
  static void Set(object o,string n,object v)=>o.GetType().GetField(n,Hidden).SetValue(o,v);
  static object Get(object o,string n)=>o.GetType().GetField(n,Hidden).GetValue(o);
  static void Run(Report r)
  {
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
   var cp=Resources.Load<KtpContactProfile>(KtpContactProfile.ResourcePath);Need(r,cp.SpellProfiles.Length==15,"15 selected contact overrides");
   var first=new AreaImpactPlan{Shape=AreaShape.Circle,Point=Vector3.forward*5,Radius=2.3f,Delay=.4f};AreaSpikePlanner.Fill(first,17);
   var second=new AreaImpactPlan{Shape=AreaShape.Circle,Point=first.Point,Radius=first.Radius,Delay=first.Delay};AreaSpikePlanner.Fill(second,17);
   Need(r,first.Spikes.Count==17&&first.Spikes.Select(x=>x.RiseAt).Distinct().Count()==17,"17 distinct spike rise times");
   Need(r,Mathf.Abs(first.Spikes.Min(x=>x.RiseAt)-.4f)<.0001f&&Mathf.Abs(first.Spikes.Max(x=>x.RiseAt)-1)<.0001f,"stagger spans Delay to Delay+0.6");
   Need(r,first.Spikes.Zip(second.Spikes,(a,b)=>a.Point==b.Point&&a.RiseAt==b.RiseAt).All(x=>x),"same seed produces identical placement and timing");
   Need(r,first.Spikes.All(x=>Vector3.Distance(x.Point,first.Point)<first.Radius),"spikes remain inside circle");
   foreach(var spike in first.Spikes)Need(r,AreaSpikePlanner.NearestRise(first,spike.Point)==spike.RiseAt,"nearest spike schedules its own rise");
   foreach(string glyph in new[]{"고","노","소","모","오"})
   {
    var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;Need(r,p.AreaRemake!=Vfx120AreaRemake.None&&p.AreaContactPrefab!=null,glyph+" remake and single contact source assigned");
    foreach(int fps in new[]{30,60,120})foreach(float speed in new[]{1f,.2f})
    {
     var plan=new AreaImpactPlan{Shape=glyph=="고"?AreaShape.Circle:glyph=="노"?AreaShape.Cone:glyph=="소"?AreaShape.Volley:AreaShape.Path,Point=Vector3.forward*3,Direction=Vector3.forward,Radius=2.3f,Angle=40,Length=12,Speed=glyph=="오"?3:6,Delay=.4f,ShotCount=9,ShotInterval=.1f,CreatedAt=Time.time};
     if(glyph=="고")AreaSpikePlanner.Fill(plan,17);
     var go=new GameObject("AreaFrameAudit");var fx=go.AddComponent<Vfx120Effect>();fx.Profile=p;fx.PreviewControlled=true;fx.SetAreaPlan(plan);fx.Begin(new Vector3(.3f,1.5f,0),null,Vector3.forward*12,Color.white);
     Need(r,fx.AreaConfigured,glyph+" valid plan configures at "+fps+" fps speed "+speed);
     string saved=JsonUtility.ToJson(plan);float dt=speed/fps;float minPeak=float.PositiveInfinity,maxPeak=0;int peakParticles=0;
     for(float t=0;t<=1.6f;t+=dt){fx.Sample(t);peakParticles=Mathf.Max(peakParticles,fx.AreaParticles);if(glyph=="오"&&t>=.4f){minPeak=Mathf.Min(minPeak,fx.AreaWavePeak);maxPeak=Mathf.Max(maxPeak,fx.AreaWavePeak);}}
     if(glyph=="노"||glyph=="모"||glyph=="오")Need(r,peakParticles>0,glyph+" body particles actually emit "+fps+"/"+speed);
     if(glyph=="고")Need(r,fx.AreaRaised==17,"all staggered spikes remain after final rise "+fps+"/"+speed);
     if(glyph=="오")Need(r,maxPeak-minPeak>.04f&&maxPeak<=1.16f&&minPeak>=.74f,"water crest changes without root scaling "+fps+"/"+speed);
     Need(r,JsonUtility.ToJson(plan)==saved,"presentation leaves shared plan unchanged "+glyph+"/"+fps+"/"+speed);
     Need(r,fx.transform.localScale==Vector3.one,"root scale fixed "+glyph+"/"+fps+"/"+speed);
     Object.DestroyImmediate(go);
    }
   }
   var wave=catalog.Entries.Single(x=>x.Glyph=="오").Profile.NativeBodyPrefab.GetComponent<Renderer>().sharedMaterial;
   Need(r,wave.HasProperty("_EffectTime")&&AssetDatabase.GetAssetPath(wave.shader).EndsWith("AreaWater.shadergraph")&&!ShaderUtil.ShaderHasError(wave.shader),"PolyOne shader copy compiles and uses effect time");
   Need(r,Vfx120Effect.WaveHeight(-1,-1.3f,.4f)==0&&Vfx120Effect.WaveHeight(0,0,.4f)==0&&Vfx120Effect.WaveHeight(0,-2.6f,.4f)<.0001f,"water side and bottom edge anchors");
   var slope=GameObject.CreatePrimitive(PrimitiveType.Cube);slope.name="TemporarySlopeAudit";slope.transform.SetPositionAndRotation(new Vector3(0,30,6),Quaternion.Euler(8,0,0));slope.transform.localScale=new Vector3(20,.1f,24);Physics.SyncTransforms();
   var waveObject=new GameObject("SlopeWaveAudit");
   try
   {
    var slopeFx=waveObject.AddComponent<Vfx120Effect>();slopeFx.Profile=catalog.Entries.Single(x=>x.Glyph=="오").Profile;slopeFx.PreviewControlled=true;
    slopeFx.SetAreaPlan(new AreaImpactPlan{Shape=AreaShape.Path,Point=new Vector3(0,30,0),Direction=Vector3.forward,Radius=1.5f,Length=12,Speed=6,Delay=.45f,CreatedAt=Time.time});slopeFx.Begin(new Vector3(0,31,0),null,new Vector3(0,30,12),Color.white);slopeFx.Sample(1.2f);
    var surface=waveObject.GetComponentsInChildren<MeshFilter>().First(x=>x.sharedMesh.vertexCount==561);float maxError=0;
    foreach(int i in new[]{0,16,32,528,544,560}){var point=surface.transform.TransformPoint(surface.sharedMesh.vertices[i]);if(!Physics.Raycast(point+Vector3.up*2,Vector3.down,out var hit,4))throw new Exception("Slope ray missed");maxError=Mathf.Max(maxError,Mathf.Abs(point.y-hit.point.y-.014f));}
    Need(r,maxError<.002f,"water boundary remains on eight-degree slope within 2mm tolerance");
   }
   finally{Object.DestroyImmediate(waveObject);Object.DestroyImmediate(slope);}
   Gameplay(r);
  }
  static void Gameplay(Report r)
  {
   var root=new GameObject("AreaRuleAudit");root.SetActive(false);var config=ScriptableObject.CreateInstance<CombatConfigSO>();var wiring=root.AddComponent<CombatLoopWiring>();Set(wiring,"_config",config);Set(wiring,"_playerTransform",root.transform);Set(wiring,"_contactVfx",Resources.Load<KtpContactProfile>(KtpContactProfile.ResourcePath));
   wiring.Construct(null,new ParryJudge(config),new GroggyMeter(config),new InkPool(config,null));root.SetActive(true);
   var targets=(List<EnemyVitals>)Get(wiring,"_targets");targets.Clear();
   for(int i=0;i<4;i++){var go=new GameObject("Target"+i);go.transform.SetParent(root.transform);go.SetActive(false);var e=go.AddComponent<EnemyVitals>();Set(e,"_config",config);go.transform.position=new Vector3(i==3?30:0,0,4+i);go.SetActive(true);targets.Add(e);}
   CastPlan captured=null;wiring.CastPlanned+=x=>captured=x;
   typeof(CombatLoopWiring).GetMethod("ResolveCircleAttack",Hidden).Invoke(wiring,new object[]{new SpellCast('고',SpellKind.AttackArea,Element.Wood,9,new AreaSpec(AreaShape.Circle,0,2.5f,10,0,.3f,0,0),1)});
   Need(r,captured.Hits.Count==3&&captured.Hits.Select(x=>x.Target).Distinct().Count()==3,"go schedules each inside enemy once");
   Need(r,captured.Hits.All(x=>Mathf.Abs(x.ImpactTime-captured.Area.CreatedAt-AreaSpikePlanner.NearestRise(captured.Area,x.Target.transform.position))<.001f),"go damage clock equals assigned spike full rise");
   var pending=(IList)Get(wiring,"_pendingCasts");pending.Clear();
   foreach(var shape in new[]{AreaShape.Cone,AreaShape.Path,AreaShape.Volley})
   {
    char glyph=shape==AreaShape.Cone?'노':shape==AreaShape.Path?'오':'소';var area=new AreaSpec(shape,25,2,12,6,.4f,9,.1f);var cast=new SpellCast(glyph,SpellKind.AttackArea,Element.Fire,9,area,1);
    typeof(CombatLoopWiring).GetMethod(shape==AreaShape.Cone?"ResolveConeAttack":shape==AreaShape.Path?"ResolvePathAttack":"ResolveVolleyAttack",Hidden).Invoke(wiring,new object[]{cast});
    Need(r,captured.Hits.Count==(shape==AreaShape.Volley?9:3),shape+" correct target/shot count; outside target excluded");
    if(shape==AreaShape.Cone)Need(r,captured.Hits.Select(x=>x.ImpactTime).Distinct().Count()==1,"cone retains simultaneous damage");
    if(shape==AreaShape.Path)Need(r,captured.Hits[0].ImpactTime<captured.Hits[1].ImpactTime&&captured.Hits[1].ImpactTime<captured.Hits[2].ImpactTime,"path retains distance order");
    if(shape==AreaShape.Volley)Need(r,captured.Area.Shots.All(x=>x.ImpactTime>x.LaunchTime)&&Mathf.Abs(captured.Area.Shots[1].LaunchTime-captured.Area.Shots[0].LaunchTime-.1f)<.001f,"volley launch/impact clocks and interval");
   }
   pending.Clear();var selected=targets[0];targets.RemoveRange(1,targets.Count-1);
   foreach(var shape in new[]{AreaShape.Cone,AreaShape.Path,AreaShape.Volley})
   {
    typeof(CombatLoopWiring).GetMethod(shape==AreaShape.Cone?"ResolveConeAttack":shape==AreaShape.Path?"ResolvePathAttack":"ResolveVolleyAttack",Hidden).Invoke(wiring,new object[]{new SpellCast(shape==AreaShape.Cone?'노':shape==AreaShape.Path?'모':'소',SpellKind.AttackArea,Element.Fire,9,new AreaSpec(shape,25,2,12,32,.4f,9,.1f),1)});
    Need(r,captured.Hits.Count==(shape==AreaShape.Volley?9:1),shape+" one target allocation");pending.Clear();
   }
   typeof(CombatLoopWiring).GetMethod("ResolveVolleyAttack",Hidden).Invoke(wiring,new object[]{new SpellCast('소',SpellKind.AttackArea,Element.Metal,9,new AreaSpec(AreaShape.Volley,25,2,12,32,.4f,9,.1f),1)});
   selected.TakeDamage(100000);int beforeContacts=((IList)Get(wiring,"_contacts")).Count;
   for(int i=0;i<pending.Count;i++){object item=pending[i];item.GetType().GetField("ImpactTime").SetValue(item,Time.time);pending[i]=item;}
   typeof(CombatLoopWiring).GetMethod("TickPendingCasts",Hidden).Invoke(wiring,null);
   Need(r,pending.Count==0&&((IList)Get(wiring,"_contacts")).Count==beforeContacts,"target dies in flight: all reservations drain without hit VFX");
   targets.Clear();typeof(CombatLoopWiring).GetMethod("ResolveVolleyAttack",Hidden).Invoke(wiring,new object[]{new SpellCast('소',SpellKind.AttackArea,Element.Metal,9,new AreaSpec(AreaShape.Volley,25,0,12,32,.4f,9,.1f),1)});
   Need(r,captured.Hits.Count==0&&captured.Area.ShotCount==9&&captured.Area.Shots.Count==0,"empty volley retains nine visual shots without damage");
   Object.DestroyImmediate(root);Object.DestroyImmediate(config);
  }
 }
}
