using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.BrushRender;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 권역 담채 sheet authoring (SPEC-EVENT-WASH-308 §4, D308-6b): every campaign stage of W_Demo_Main is mapped to the
 // highest-priority catalog place (Priority > 0, realms excluded) that contains its trigger point's current position in
 // WorldContent_Main (Points; Encounters / scene content points as fallbacks). Stage.Destination / DestinationId are never read.
 // Every 진행 place (ProgressAsVolume) gets its air volume baked from the offline height field (Finish297/Surface/height.bytes):
 //   floor = highest ground of the place disc + VolumeBaseClear; top = max(centre ground + VolumeMinRise, ring [VolumeRidgeRing]
 //   percentile + VolumeRidgeClear); radius = clamp(place radius x VolumeRadiusScale) then <= distance to every NoWash / boss /
 //   growth place - its radius - VolumeNoWashMargin. The report adds the above-ridge exposure (share of 72 ground eyes on the
 //   1/1.5/2 km rings that see >= 25 % of the volume's vertical density above the terrain horizon; report only, not a gate).
 // :dry reports the proposal (resolved as a write would: ProgressOn true, RewardOn false); without :dry the same proposal is written
 // into EventWashSheet308.asset (global values of an existing sheet are kept; a hand-set Meaning / StrengthScale of the same
 // stage+place is kept). The asset is backed up first and saved alone (SaveAssetIfDirty, never SaveAssets).
 public static partial class EventWash308
 {
  // #308 (SPEC-WORLD-CLIFF-BOUNDARY-308 높이 출처, AC-B24): the height asset of the active scene (CliffHeight308), not a constant
  internal static string HeightFieldPath=>CliffHeight308.Active();
  internal static readonly float[] ExposureRings={1000f,1500f,2000f};
  internal const int ExposureAzimuths=72;
  internal const float ExposureEye=1.7f,ExposureShare=.25f;

  internal sealed class Proposal{public DemoCampaignProfile.Stage stage;public Vector3? trigger;public string triggerSource="";public WorldLocationCatalog.Entry place;public string how="";}

  /// <summary>Offline height field of the main scene's FinalSurface (&lt;f4, 1501 x 1001, 4 m, h[z/4, x/4], bilinear) — the
  /// CompactEnclosure305.Offline305 convention. Editor only; the runtime never reads it (the bake lives in the sheet).</summary>
  internal sealed class Field308
  {
   internal const int H=1501,W=1001;internal const float Cell=4f;
   readonly float[] h;
   Field308(float[] h){this.h=h;}

   internal static Field308 Load()
   {
    string abs=PostLedger308.Abs(HeightFieldPath);
    if(!File.Exists(abs))throw new PostLedger308.Refused("height field missing: "+HeightFieldPath);
    var bytes=File.ReadAllBytes(abs);
    if(bytes.Length!=H*W*4)throw new PostLedger308.Refused("height field "+HeightFieldPath+" is "+bytes.Length+" bytes, expected "+(H*W*4)+" (<f4 1501 x 1001)");
    var h=new float[H*W];Buffer.BlockCopy(bytes,0,h,0,bytes.Length);return new Field308(h);
   }

   internal static float MaxX=>(W-1)*Cell;
   internal static float MaxZ=>(H-1)*Cell;
   internal static bool Inside(float x,float z)=>x>=0f&&z>=0f&&x<=MaxX&&z<=MaxZ;

   internal float At(float x,float z)
   {
    float j=Mathf.Clamp(x/Cell,0,W-1.001f),i=Mathf.Clamp(z/Cell,0,H-1.001f);int j0=(int)j,i0=(int)i;float fj=j-j0,fi=i-i0;
    return h[i0*W+j0]*(1-fj)*(1-fi)+h[i0*W+j0+1]*fj*(1-fi)+h[(i0+1)*W+j0]*(1-fj)*fi+h[(i0+1)*W+j0+1]*fj*fi;
   }

   /// <summary>Highest ground of the disc: every 4 m lattice node inside it, and the centre (not the centre alone).</summary>
   internal float DiscMax(float cx,float cz,float r)
   {
    float best=At(cx,cz);
    int j0=Mathf.Max(0,Mathf.FloorToInt((cx-r)/Cell)),j1=Mathf.Min(W-1,Mathf.CeilToInt((cx+r)/Cell));
    int i0=Mathf.Max(0,Mathf.FloorToInt((cz-r)/Cell)),i1=Mathf.Min(H-1,Mathf.CeilToInt((cz+r)/Cell));
    for(int i=i0;i<=i1;i++)for(int j=j0;j<=j1;j++)
    {
     float dx=j*Cell-cx,dz=i*Cell-cz;
     if(dx*dx+dz*dz<=r*r)best=Mathf.Max(best,h[i*W+j]);
    }
    return best;
   }

   /// <summary>Nearest-rank percentile of the heights on circles r0, r0+step, … ≤ r1 (arc spacing ≈ step, ≥ 8 per circle), samples
   /// outside the field left out. No sample -> the centre height.</summary>
   internal float RingPercentile(float cx,float cz,float r0,float r1,float step,float percentile,out int samples)
   {
    var v=new List<float>();
    for(float r=Mathf.Max(step,r0);r<=r1+1e-3f;r+=step)
    {
     int n=Mathf.Max(8,Mathf.CeilToInt(2f*Mathf.PI*r/step));
     for(int k=0;k<n;k++){float a=2f*Mathf.PI*k/n;float x=cx+r*Mathf.Sin(a),z=cz+r*Mathf.Cos(a);if(Inside(x,z))v.Add(At(x,z));}
    }
    samples=v.Count;if(v.Count==0)return At(cx,cz);
    v.Sort();int rank=Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp(percentile,0f,100f)/100f*v.Count)-1,0,v.Count-1);
    return v[rank];
   }

   /// <summary>World y of the terrain horizon line straight above (cx, cz) seen from eye: the steepest terrain slope between them
   /// (8 m steps), extended to the centre's distance.</summary>
   internal float HorizonAbove(Vector3 eye,float cx,float cz)
   {
    float d=Vector2.Distance(new Vector2(eye.x,eye.z),new Vector2(cx,cz));if(d<16f)return eye.y;
    float best=float.NegativeInfinity;
    for(float s=8f;s<d;s+=8f){float x=eye.x+(cx-eye.x)*s/d,z=eye.z+(cz-eye.z)*s/d;best=Mathf.Max(best,(At(x,z)-eye.y)/s);}
    return float.IsNegativeInfinity(best)?eye.y:eye.y+d*best;
   }
  }

  /// <summary>Share of the ellipsoid's vertical density at its centre (∫(1 − u²) over [−1, 1]) above a horizon line at y.</summary>
  internal static float AboveShare(float horizonY,float centreY,float halfHeight)
  {
   if(halfHeight<=0f)return 0f;
   float u0=Mathf.Clamp((horizonY-centreY)/halfHeight,-1f,1f);
   return ((1f-u0)-(1f-u0*u0*u0)/3f)/(4f/3f);
  }

  internal struct Sight308{public float azimuth,distance,horizonY,share;public Vector3 eye;}

  /// <summary>Ground eyes (field + ExposureEye) on one ring around the centre, with the visible share of the volume above the horizon.</summary>
  internal static List<Sight308> Sights(Field308 f,float cx,float cz,float distance,float centreY,float halfHeight)
  {
   var list=new List<Sight308>();
   for(int k=0;k<ExposureAzimuths;k++)
   {
    float az=360f*k/ExposureAzimuths,a=az*Mathf.Deg2Rad;float x=cx+distance*Mathf.Sin(a),z=cz+distance*Mathf.Cos(a);
    if(!Field308.Inside(x,z))continue;
    var eye=new Vector3(x,f.At(x,z)+ExposureEye,z);float hy=f.HorizonAbove(eye,cx,cz);
    list.Add(new Sight308{azimuth=az,distance=distance,eye=eye,horizonY=hy,share=AboveShare(hy,centreY,halfHeight)});
   }
   return list;
  }

  internal sealed class Bake308
  {
   public string place="";public float ground,discMax,baseY,ridge,top,radiusRaw,radius,limit=float.PositiveInfinity;public string limitBy="";
   public int ringSamples;public float[] exposure=new float[3];public int[] exposureSeen=new int[3],exposureValid=new int[3];public bool ok;public string note="";
   public float CentreY=>.5f*(baseY+top);public float HalfHeight=>.5f*(top-baseY);
  }

  /// <summary>The D308-6b air volume of one place (see the header). limits = NoWash / boss / growth places.</summary>
  internal static Bake308 BakeVolume(Field308 f,EventWashSheetSO sheet,WorldLocationCatalog.Entry place,IEnumerable<WorldLocationCatalog.Entry> limits)
  {
   var b=new Bake308{place=place.Id};float cx=place.Centre.x,cz=place.Centre.z;
   if(!Field308.Inside(cx,cz)){b.note="centre outside the height field";return b;}
   float discR=Mathf.Max(1f,place.Radius*sheet.RadiusScale);
   b.ground=f.At(cx,cz);b.discMax=f.DiscMax(cx,cz,discR);b.baseY=b.discMax+sheet.VolumeBaseClear;
   float r0=Mathf.Min(sheet.VolumeRidgeRing.x,sheet.VolumeRidgeRing.y),r1=Mathf.Max(sheet.VolumeRidgeRing.x,sheet.VolumeRidgeRing.y);
   b.ridge=f.RingPercentile(cx,cz,Mathf.Max(0f,r0),r1,40f,sheet.VolumeRidgePercentile,out b.ringSamples);
   b.top=Mathf.Max(b.ground+sheet.VolumeMinRise,b.ridge+sheet.VolumeRidgeClear);
   float lo=Mathf.Min(sheet.VolumeRadiusMin,sheet.VolumeRadiusMax),hi=Mathf.Max(sheet.VolumeRadiusMin,sheet.VolumeRadiusMax);
   b.radiusRaw=Mathf.Clamp(place.Radius*sheet.VolumeRadiusScale,lo,hi);b.radius=b.radiusRaw;
   foreach(var l in limits)
   {
    if(l==null||l.Id==place.Id)continue;
    float lim=Vector2.Distance(new Vector2(cx,cz),new Vector2(l.Centre.x,l.Centre.z))-l.Radius-sheet.VolumeNoWashMargin;
    if(lim<b.limit){b.limit=lim;b.limitBy=l.Id;}
   }
   if(b.limit<b.radius)b.radius=Mathf.Max(0f,b.limit);
   b.ok=b.radius>0f&&b.top>b.baseY+10f;
   if(!b.ok)b.note=b.radius<=0f?"radius 0 after the limit of "+b.limitBy:"top "+b.top.ToString("F1",CultureInfo.InvariantCulture)+" not above floor "+b.baseY.ToString("F1",CultureInfo.InvariantCulture)+" + 10";
   for(int k=0;k<ExposureRings.Length;k++)
   {
    var s=Sights(f,cx,cz,ExposureRings[k],b.CentreY,b.HalfHeight);
    b.exposureValid[k]=s.Count;b.exposureSeen[k]=s.Count(x=>x.share>=ExposureShare);
    b.exposure[k]=s.Count>0?(float)b.exposureSeen[k]/s.Count:0f;
   }
   return b;
  }

  /// <summary>The scene's session, its catalog (WorldRealmAtmosphere.Catalog) and palette (WorldLookDriver._palette).</summary>
  internal static (WorldMacroPlaytestSession session,WorldLocationCatalog catalog,ElementPaletteSO palette) SceneRefs(Scene scene)
  {
   var sessions=PostLedger308.All<WorldMacroPlaytestSession>(scene).ToArray();
   if(sessions.Length!=1)throw new PostLedger308.Refused("WorldMacroPlaytestSession count "+sessions.Length+" in "+scene.path);
   var atm=PostLedger308.All<WorldRealmAtmosphere>(scene).ToArray();
   if(atm.Length!=1||atm[0].Catalog==null)throw new PostLedger308.Refused("no single WorldRealmAtmosphere with a Catalog in "+scene.path);
   var look=PostLedger308.All<WorldLookDriver>(scene).ToArray();
   if(look.Length!=1)throw new PostLedger308.Refused("WorldLookDriver count "+look.Length+" in "+scene.path);
   var palette=look[0].Palette??throw new PostLedger308.Refused("WorldLookDriver has no palette in "+scene.path);
   return (sessions[0],atm[0].Catalog,palette);
  }

  internal static List<Proposal> Propose(Scene scene,WorldMacroPlaytestSession session,WorldLocationCatalog catalog)
  {
   var content=session.Content??throw new PostLedger308.Refused("session has no Content");
   var campaign=content.Campaign??throw new PostLedger308.Refused("content has no Campaign");
   var scenePoints=PostLedger308.All<WorldMacroContentPoint>(scene).Where(p=>!string.IsNullOrEmpty(p.Id)).GroupBy(p=>p.Id).ToDictionary(g=>g.Key,g=>g.First().transform.position);
   var list=new List<Proposal>();
   foreach(var stage in campaign.Stages??new DemoCampaignProfile.Stage[0])
   {
    if(stage==null)continue;var p=new Proposal{stage=stage};
    var point=content.Points?.FirstOrDefault(x=>x!=null&&x.Id==stage.TriggerId);
    if(point!=null){p.trigger=point.Position;p.triggerSource="point";}
    else
    {
     var enc=content.Encounters?.FirstOrDefault(x=>x!=null&&(x.Id==stage.TriggerId||x.ContentId==stage.TriggerId));
     if(enc!=null){p.trigger=enc.Feet;p.triggerSource="encounter";}
     else if(stage.TriggerId!=null&&scenePoints.TryGetValue(stage.TriggerId,out var sp)){p.trigger=sp;p.triggerSource="scene point";}
    }
    if(p.trigger.HasValue)
    {
     var at=p.trigger.Value;
     var inside=(catalog.Entries??new WorldLocationCatalog.Entry[0]).Where(e=>e!=null&&e.Priority>0&&e.Contains(at)).OrderByDescending(e=>e.Priority).ThenBy(e=>e.Radius).ThenBy(e=>e.Id,StringComparer.Ordinal).ToArray();
     if(inside.Length>0){p.place=inside[0];p.how="contains";}
     else
     {
      // height band or floor path missed: the XZ circle alone (reported)
      var xz=(catalog.Entries??new WorldLocationCatalog.Entry[0]).Where(e=>e!=null&&e.Priority>0&&Vector2.Distance(new Vector2(at.x,at.z),new Vector2(e.Centre.x,e.Centre.z))<=e.Radius).OrderByDescending(e=>e.Priority).ThenBy(e=>e.Radius).ToArray();
      if(xz.Length>0){p.place=xz[0];p.how="xz only (outside its height band/floor path)";}
     }
    }
    list.Add(p);
   }
   return list;
  }

  /// <summary>wash308-sheet:revert — copies the sheet backup of the latest recorded wash308-sheet write back (each revert steps one
  /// write back; the ledger record is removed). A sheet that was created (no backup) is refused: delete it by hand if wanted.</summary>
  static string SheetRevert()
  {
   PostLedger308.RequireEditable();
   var ledger=ReadLedger();var rec=ledger.changes.LastOrDefault(c=>c.kind=="sheet");
   if(rec==null)throw new PostLedger308.Refused("no recorded wash308-sheet write");
   if(string.IsNullOrEmpty(rec.before)||!File.Exists(rec.before))throw new PostLedger308.Refused("the latest wash308-sheet write ("+rec.utc+") has no backup ("+(string.IsNullOrEmpty(rec.before)?"the sheet was created":rec.before)+")");
   File.Copy(rec.before,PostLedger308.Abs(SheetPath),true);
   AssetDatabase.ImportAsset(SheetPath,ImportAssetOptions.ForceUpdate);
   ledger.changes.Remove(rec);WriteLedger(ledger);
   var sheet=AssetDatabase.LoadAssetAtPath<EventWashSheetSO>(SheetPath);
   return "EventWashSheet308 restored from "+rec.before+" (write "+rec.utc+": "+rec.after+")"+(sheet!=null?"; now ProgressOn="+sheet.ProgressOn+" RewardOn="+sheet.RewardOn+
          " ProgressAsVolume="+sheet.ProgressAsVolume+", entries "+(sheet.Entries?.Length??0)+", baked volumes "+(sheet.Entries??new EventWashSheetSO.Entry[0]).Count(EventWashPlanner308.Baked):"")+
          ". A pre-D308-6b backup has no ProgressOn/RewardOn keys: the field defaults (true/false) apply and its progress volumes are unbaked (nothing shows).";
  }

  static string F1(float v)=>v.ToString("F1",CultureInfo.InvariantCulture);
  static string Pct(float v)=>Mathf.RoundToInt(v*100f).ToString(CultureInfo.InvariantCulture);

  static string Sheet(bool dry)
  {
   PostLedger308.RequireEditable();
   var scene=PostLedger308.Open(PostLedger308.Main);
   var (session,catalog,palette)=SceneRefs(scene);
   var proposals=Propose(scene,session,catalog);
   var field=Field308.Load();
   var existing=AssetDatabase.LoadAssetAtPath<EventWashSheetSO>(SheetPath);
   var template=existing!=null?existing:ScriptableObject.CreateInstance<EventWashSheetSO>();
   // a write puts ProgressOn true / RewardOn false (D308-6b): resolve on a scratch copy so a dry run never dirties the asset
   var probe=Object.Instantiate(template);probe.hideFlags=HideFlags.HideAndDontSave;probe.ProgressOn=true;probe.RewardOn=false;
   try
   {
    var sb=new StringBuilder("wash308-sheet"+(dry?" (dry)":"")+" — "+PostLedger308.Main+", campaign "+session.Content.Campaign.CampaignId+", catalog "+AssetDatabase.GetAssetPath(catalog)+"\n");
    sb.AppendLine("  meanings (D308-6b): sheet now ProgressOn="+template.ProgressOn+" RewardOn="+template.RewardOn+" ProgressAsVolume="+template.ProgressAsVolume+
                  " -> resolved/written as ProgressOn=True RewardOn=False (진행 = "+(probe.ProgressAsVolume?"air volume":"ground wash")+", 보상 off, 안식·서사 unchanged)");
    // 폐광 sub-zones: catalog places whose centre lies inside mine_interior
    var mine=catalog.Entries.FirstOrDefault(e=>e!=null&&e.Id=="mine_interior");
    var mineZones=mine==null?new string[0]:catalog.Entries.Where(e=>e!=null&&e.Id!=mine.Id&&e.Priority>0&&mine.Contains(e.Centre,2f)).Select(e=>e.Id).ToArray();
    sb.AppendLine("  폐광 sub-zones inside mine_interior: "+(mineZones.Length>0?string.Join(", ",mineZones):"none"));
    // the write adds them to NarrativeLocations: resolve the dry run with the same list, so the report and the bake match the runtime
    var narrativeIds=new List<string>(probe.NarrativeLocations??new string[0]);foreach(var id in mineZones)if(!narrativeIds.Contains(id))narrativeIds.Add(id);
    probe.NarrativeLocations=narrativeIds.ToArray();
    // NoWash / boss / growth places limit the volume radius
    var bossPlaces=proposals.Where(p=>p.place!=null&&(p.stage.Event==DemoEventKind.BossDefeated||p.stage.Event==DemoEventKind.GrowthInterrupted)).ToArray();
    var limitPlaces=(catalog.Entries??new WorldLocationCatalog.Entry[0]).Where(e=>e!=null&&(EventWashSheetSO.Listed(probe.NoWashLocations,e.Id)||bossPlaces.Any(p=>p.place==e))).Distinct().ToArray();

    var entries=new List<EventWashSheetSO.Entry>();var bakes=new Dictionary<string,Bake308>();var volumeStages=new Dictionary<string,List<string>>();
    foreach(var p in proposals)
    {
     var s=p.stage;string head="  "+s.Id+" ["+s.Event+(s.Optional?", optional":"")+(s.Implemented?"":", NOT implemented")+"] trigger "+s.TriggerId;
     if(!p.trigger.HasValue){sb.AppendLine(head+": trigger position not found -> no wash");continue;}
     if(p.place==null){sb.AppendLine(head+" @"+p.trigger.Value.ToString("F0")+" ("+p.triggerSource+"): no catalog place -> no wash");continue;}
     string realm=!string.IsNullOrEmpty(p.place.RealmId)?p.place.RealmId:catalog.RealmAt(p.place.Centre)?.Id??"";
     var old=existing?.Entries?.FirstOrDefault(e=>e!=null&&e.StageId==s.Id&&e.LocationId==p.place.Id);
     var entry=new EventWashSheetSO.Entry{StageId=s.Id,LocationId=p.place.Id,Meaning=old!=null?old.Meaning:EventWashSheetSO.Meaning.Auto,StrengthScale=old!=null?old.StrengthScale:1f};
     var meaning=EventWashPlanner308.Resolve(probe,s,entry,p.place.Id,realm,out string reason);
     var colour=EventWashPlanner308.ColourFor(probe,palette,meaning,realm);
     string kind="";
     // every 진행 place is baked, also while ProgressAsVolume is false (comparison): switching it back needs no re-bake
     if(meaning==EventWashSheetSO.Meaning.Progress)
     {
      if(!bakes.TryGetValue(p.place.Id,out var bake)){bake=BakeVolume(field,probe,p.place,limitPlaces);bakes[p.place.Id]=bake;volumeStages[p.place.Id]=new List<string>();}
      volumeStages[p.place.Id].Add(s.Id);
      if(bake.ok){entry.VolumeBaseY=bake.baseY;entry.VolumeTopY=bake.top;entry.VolumeRadius=bake.radius;}
      var t=EventWashPlanner308.PigmentFor(colour,probe.VolumePigment);
      kind=(probe.ProgressAsVolume?" VOLUME":" surface (ProgressAsVolume false; volume baked for the switch)")+(volumeStages[p.place.Id].Count>1?" (group: shares "+p.place.Id+" with "+volumeStages[p.place.Id][0]+" -> one slot)":"")+(bake.ok?"":" NOT BAKED ("+bake.note+")")+
           " T=("+t.x.ToString("0.###",CultureInfo.InvariantCulture)+", "+t.y.ToString("0.###",CultureInfo.InvariantCulture)+", "+t.z.ToString("0.###",CultureInfo.InvariantCulture)+")";
     }
     else if(meaning!=EventWashSheetSO.Meaning.None)kind=" surface";
     sb.AppendLine(head+" @"+p.trigger.Value.ToString("F0")+" ("+p.triggerSource+") -> "+p.place.Id+" r "+p.place.Radius+" ("+p.how+", "+realm+") => "+meaning+kind+" #"+ColorUtility.ToHtmlStringRGB(colour)+" | "+reason+
                   (old!=null&&old.Meaning!=EventWashSheetSO.Meaning.Auto?" | kept sheet meaning "+old.Meaning:""));
     entries.Add(entry);
    }
    sb.AppendLine("  boss/growth stage places: "+(bossPlaces.Length>0?string.Join(", ",bossPlaces.Select(p=>p.place.Id+"("+p.stage.Id+")").Distinct()):"none"));
    sb.AppendLine("  NoWashLocations (TEST): "+string.Join(", ",probe.NoWashLocations??new string[0])+(probe.NoWashLocations==null?"":" | not in catalog: "+string.Join(", ",probe.NoWashLocations.Where(id=>!catalog.Entries.Any(e=>e!=null&&e.Id==id)))));

    // progress volumes (D308-6b)
    sb.AppendLine("  progress volumes (D308-6b, "+HeightFieldPath+" sha256 "+CliffHeight308.Sha(HeightFieldPath)+"; floor = disc max + "+F1(probe.VolumeBaseClear)+", top = max(ground + "+F1(probe.VolumeMinRise)+", ring "+F1(probe.VolumeRidgeRing.x)+"–"+F1(probe.VolumeRidgeRing.y)+
                  " m p"+F1(probe.VolumeRidgePercentile)+" + "+F1(probe.VolumeRidgeClear)+"), R = clamp(r x "+F1(probe.VolumeRadiusScale)+", "+F1(probe.VolumeRadiusMin)+", "+F1(probe.VolumeRadiusMax)+") <= limit (margin "+F1(probe.VolumeNoWashMargin)+")): "+bakes.Count);
    foreach(var kv in bakes)
    {
     var b=kv.Value;
     sb.AppendLine("    "+b.place+" ["+string.Join(", ",volumeStages[b.place])+"]: ground "+F1(b.ground)+", disc max "+F1(b.discMax)+" -> floor "+F1(b.baseY)+" (+"+F1(b.baseY-b.ground)+") | ridge "+F1(b.ridge)+" (n "+b.ringSamples+") -> top "+F1(b.top)+
                   " (+"+F1(b.top-b.ground)+", Hh "+F1(b.HalfHeight)+") | R "+F1(b.radius)+(b.radius<b.radiusRaw?" (reduced from "+F1(b.radiusRaw)+")":"")+" limit "+(float.IsPositiveInfinity(b.limit)?"none":F1(b.limit)+" by "+b.limitBy)+
                   " | above-ridge exposure "+string.Join(" / ",ExposureRings.Select((d,k)=>(d/1000f).ToString("0.0",CultureInfo.InvariantCulture)+" km "+Pct(b.exposure[k])+"% ("+b.exposureSeen[k]+"/"+b.exposureValid[k]+")"))+
                   (b.ok?"":" | NOT BAKED: "+b.note));
    }
    // the bake uses the Main height field: report how far the catalog centres sit from it (a candidate scene with other terrain shows here)
    var diffs=(catalog.Entries??new WorldLocationCatalog.Entry[0]).Where(e=>e!=null&&e.Priority>0&&Field308.Inside(e.Centre.x,e.Centre.z)).Select(e=>(id:e.Id,d:e.Centre.y-field.At(e.Centre.x,e.Centre.z))).OrderByDescending(x=>Mathf.Abs(x.d)).Take(5).ToArray();
    sb.AppendLine("  catalog Centre.y - height field (top 5 |d|): "+string.Join(", ",diffs.Select(x=>x.id+" "+(x.d>=0?"+":"")+F1(x.d))));

    if(dry)
    {
     if(existing==null)Object.DestroyImmediate(template);
     return sb.Append("  dry: "+entries.Count+" entries proposed ("+entries.Count(EventWashPlanner308.Baked)+" with a baked volume), nothing written").ToString();
    }

    string utc=PostLedger308.Utc();var ledger=ReadLedger();
    string after="D308-6b progress volume, reward off; entries "+entries.Count+", volumes "+entries.Count(EventWashPlanner308.Baked);
    if(existing!=null){string backup=PostLedger308.Backup(BackupRoot,utc,SheetPath);ledger.backups.Add(backup);ledger.changes.Add(new Change{kind="sheet",path=SheetPath,before=backup,after=after,utc=utc});}
    else
    {
     EnsureFolder(Path.GetDirectoryName(SheetPath).Replace('\\','/'));
     AssetDatabase.CreateAsset(template,SheetPath);ledger.changes.Add(new Change{kind="sheet",path=SheetPath,before="",after="created; "+after,utc=utc});
    }
    template.Entries=entries.ToArray();
    template.ProgressOn=true;template.RewardOn=false;
    var narrative=new List<string>(template.NarrativeLocations??new string[0]);foreach(var id in mineZones)if(!narrative.Contains(id))narrative.Add(id);
    template.NarrativeLocations=narrative.ToArray();   // = the list the proposal was resolved with (probe)
    EditorUtility.SetDirty(template);AssetDatabase.SaveAssetIfDirty(template);WriteLedger(ledger);
    return sb.Append("  wrote "+entries.Count+" entries ("+entries.Count(EventWashPlanner308.Baked)+" volumes) to "+SheetPath+", ProgressOn True, RewardOn False (NarrativeLocations "+string.Join(",",template.NarrativeLocations)+")").ToString();
   }
   finally{if(probe!=null)Object.DestroyImmediate(probe);}
  }
 }
}
