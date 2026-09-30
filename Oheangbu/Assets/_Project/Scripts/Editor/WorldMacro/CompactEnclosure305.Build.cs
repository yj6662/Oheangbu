using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Sheet305=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #305 build-scene: idempotent rebuild of the Enclosure305 root. Only assets this command writes are saved (SaveAssetIfDirty);
 // the existing sheets (Architecture296/Data/*) and the NavMesh asset are read, never written.
 public static partial class CompactRebuildAuthoring
 {
  sealed class SegLedger305{public string id,realm,zone,ends,notes;public int stations,chunks,boxes,missing,low;public float minSpan=float.PositiveInfinity,maxSpan,minFree=float.PositiveInfinity,maxFree,minPlayer=float.PositiveInfinity;}
  // ground tallies shared by segment and joint columns
  sealed class Tally305{public int fallback,missingAll,raised;public readonly Dictionary<string,int> raisedBy=new Dictionary<string,int>();public readonly List<float> vsOffline=new List<float>();public readonly List<string> low=new List<string>();}

  static string Build305(string path)
  {
   var scene=Open305(path);
   var session=Session292();var content=session.Content??throw new Refuse305("session has no content in "+path);
   var arts=Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(a=>a.Sheet!=null&&a.gameObject.scene==scene&&a.transform.root.name!=Root305).ToArray();
   var dry=arts.FirstOrDefault(a=>a.Sheet.name.EndsWith("DryLandscape"))??arts.OrderByDescending(a=>a.Sheet.FixedPlacements.Length).FirstOrDefault()??throw new Refuse305("no CompactRebuildArtRenderer with a sheet in "+path);
   var main=arts.FirstOrDefault(a=>a.gameObject.name=="Rebuild_EnvironmentArt"||(a.transform.parent!=null&&a.transform.parent.name=="Rebuild_EnvironmentArt"))??dry;
   // Draw() returns unless the camera is the Observer (or a SceneView): a null Observer = a forest nobody sees in Game/Play
   if(main.Observer==null)throw new Refuse305("art renderer "+main.gameObject.name+" has no Observer; Forest305_Renderer would draw nothing in Game/Play (nothing destroyed)");
   string backup=Backup305(path);
   try{return Rebuild305(path,scene,session,content,arts,dry,main,backup);}
   catch(Exception e)
   {
    // nothing is saved before the end of Rebuild305: reload the scene from disk so later commands are not refused as "dirty"
    Debug.LogException(e);string note;
    try{EditorSceneManager.OpenScene(path,OpenSceneMode.Single);note="scene reverted from disk";}catch(Exception r){note="REVERT FAILED ("+r.Message+"): discard the scene changes by hand";}
    return "ERROR build-scene "+path+": "+e.GetType().Name+": "+e.Message+" ("+note+", backup "+backup+")";
   }
  }

  static string Rebuild305(string path,UnityEngine.SceneManagement.Scene scene,WorldMacroPlaytestSession session,WorldMacroPlaytestSO content,CompactRebuildArtRenderer[] arts,CompactRebuildArtRenderer dry,CompactRebuildArtRenderer main,string backup)
  {
   var segs=N6Segments305(out string version,out int otherKits);
   var forest=Forest305Input();
   foreach(var g in scene.GetRootGameObjects().Where(g=>g.name==Root305).ToArray())Object.DestroyImmediate(g);
   Physics.SyncTransforms();
   DevSceneKit.EnsureFolder(A305+"/Meshes");
   var root=new GameObject(Root305);root.layer=0;
   var shellRoot=new GameObject("Shell").transform;shellRoot.SetParent(root.transform,false);
   var obRoot=new GameObject("Obstacles").transform;obRoot.SetParent(root.transform,false);
   var skip=Skip305(session);var offline=Offline305();
   var saved=new List<Object>();var made=new HashSet<string>();var ledgers=new List<SegLedger305>();var jledgers=new List<(Joint305 j,SegLedger305 l)>();
   var G=new Tally305();long tris=0;

   // ---- shell + carve boxes ----
   var stations=segs.ToDictionary(g=>g,g=>Stations305(g));
   foreach(var g in segs)
   {
    var st=stations[g];int n=st.Length;var L=new SegLedger305{id=g.id,realm=g.realm,zone=g.zone,ends=string.Join("/",g.ends),notes=string.Join("; ",g.notes),stations=n};ledgers.Add(L);
    Columns305(g.id,st,root.transform,skip,offline,G,L,out var bottom,out var top,out var mid);
    // 48 m chunks sharing their boundary station (no gaps)
    float spacing=st[n-1].s/Mathf.Max(1,n-1);int K=Mathf.Max(1,Mathf.CeilToInt(st[n-1].s/Chunk305-1e-3f));
    var cuts=Enumerable.Range(0,K+1).Select(k=>k==K?n-1:Mathf.Clamp(Mathf.RoundToInt(k*Chunk305/Mathf.Max(1e-3f,spacing)),0,n-1)).Distinct().OrderBy(x=>x).ToList();
    int obIndex=0;
    for(int c=0;c+1<cuts.Count;c++)
    {
     int i0=cuts[c],i1=cuts[c+1];if(i1<=i0)continue;
     tris+=ShellChunk305(g.id+"_"+L.chunks+"_col",st,bottom,top,i0,i1,shellRoot,saved,made);
     if(Carve305)L.boxes+=Boxes305(g.id,st,mid,i0,i1,obRoot,ref obIndex);L.chunks++;
    }
   }
   // ---- joints: close the opening between two shells that end on one line point ----
   var joints=Joints305(segs,stations);
   foreach(var j in joints)
   {
    var L=new SegLedger305{id=j.name,realm=j.a.realm,zone=j.kind,ends=j.a.id+"/"+j.b.id,notes="angle "+F305(j.angle,"F1")+" gap "+F305(j.gap)+(j.note!=""?" "+j.note:""),stations=j.arc.Length};jledgers.Add((j,L));
    if(j.kind!="arc"||j.arc.Length<2)continue;
    Columns305(j.name,j.arc,root.transform,skip,offline,G,L,out var bottom,out var top,out var mid);
    tris+=ShellChunk305(j.name,j.arc,bottom,top,0,j.arc.Length-1,shellRoot,saved,made);L.chunks++;
    int obIndex=0;if(Carve305)L.boxes+=Boxes305(j.name.Substring(0,j.name.Length-4),j.arc,mid,0,j.arc.Length-1,obRoot,ref obIndex);
   }
   // ---- joint plugs: a vertical capsule over every joint point (check 305: cross/flush joints left 1.3-1.4 m openings at Guk
   // height). Radius covers both shell ends (offset 1 m + half thickness), renderer-less *_col inside the thicket, own carve capsule.
   int plugs=0;
   foreach(var j in joints)
   {
    float lo=float.PositiveInfinity,hi=float.NegativeInfinity,R=j.kind=="cross"?PlugCross305:Plug305;   // concave corners: wider plug (check 305)
    foreach(var r in new[]{0f,R,4.5f,6f})foreach(var o in new[]{Vector2.right,Vector2.left,Vector2.up,Vector2.down})
     if(Ground305(j.p.x+o.x*r,j.p.y+o.y*r,root.transform,skip,out float gy,out _)){if(r<=R)lo=Mathf.Min(lo,gy);hi=Mathf.Max(hi,gy);}
    if(float.IsInfinity(lo))continue;
    float bottom=lo-Below305,top=hi+Above305;var centre=new Vector3(j.p.x,(bottom+top)*.5f,j.p.y);
    var pg=new GameObject("E305_P"+j.name.Substring(6));pg.transform.SetParent(shellRoot,false);pg.layer=0;
    GameObjectUtility.SetStaticEditorFlags(pg,StaticEditorFlags.BatchingStatic|StaticEditorFlags.NavigationStatic);
    var cap=pg.AddComponent<CapsuleCollider>();cap.direction=1;cap.radius=R;cap.height=top-bottom+2*R;cap.center=centre;
    if(Carve305)
    {
     var og=new GameObject("E305_P"+j.name.Substring(6,j.name.Length-10)+"_ob");og.transform.SetParent(obRoot,false);og.transform.position=centre;
     var ob=og.AddComponent<NavMeshObstacle>();ob.shape=NavMeshObstacleShape.Capsule;ob.radius=R;ob.height=top-bottom+BoxBelow305;ob.center=Vector3.zero;ob.carving=true;ob.carveOnlyStationary=true;
    }
    plugs++;
   }
   Physics.SyncTransforms();

   // ---- forest: new sheet, prototypes copied by Id from DryLandscape, candidates filtered against the scene (read only) ----
   string sheetPath=A305+"/Forest305.asset";
   var sheet=AssetDatabase.LoadAssetAtPath<Sheet305>(sheetPath);
   if(sheet==null){sheet=ScriptableObject.CreateInstance<Sheet305>();sheet.name="Forest305";AssetDatabase.CreateAsset(sheet,sheetPath);}
   var src=dry.Sheet;var protos=new List<Sheet305.Prototype>();var missingProtos=new List<string>();
   foreach(var id in (forest.prototypes??Array.Empty<string>()).Concat((forest.candidates??Array.Empty<Cand305>()).Select(c=>c.PrototypeId)).Where(x=>!string.IsNullOrEmpty(x)).Distinct())
   {var p=src.Prototypes.FirstOrDefault(x=>x!=null&&x.Id==id);if(p==null)missingProtos.Add(id);else protos.Add(Proto305(p));}
   sheet.Prototypes=protos.ToArray();
   sheet.TreeNear=src.TreeNear;sheet.TreeMiddle=src.TreeMiddle;sheet.TreeDistance=src.TreeDistance;sheet.ForestDistance=src.ForestDistance;
   sheet.ShrubDistance=src.ShrubDistance;sheet.GrassDistance=src.GrassDistance;sheet.GrassMeshDistance=src.GrassMeshDistance;sheet.RegionBlend=src.RegionBlend;
   sheet.PreservedAreas=Array.Empty<Sheet305.PreserveArea>();sheet.Cells=Array.Empty<Sheet305.Cell>();sheet.StoryClusters=Array.Empty<Sheet305.StoryCluster>();
   var kinds=protos.ToDictionary(p=>p.Id,p=>p.Category);

   var otherArts=arts.Where(a=>a.Sheet!=sheet).ToArray();var trees=new Grid305(8f);
   foreach(var a in otherArts)
   {
    var cat=a.Sheet.Prototypes.Where(p=>p!=null&&p.Id!=null).GroupBy(p=>p.Id).ToDictionary(x=>x.Key,x=>x.First().Category);
    foreach(var f in a.Sheet.FixedPlacements)if(f!=null&&f.PrototypeId!=null&&cat.TryGetValue(f.PrototypeId,out var c)&&c==Sheet305.Kind.Tree)trees.Add(f.Position);
   }
   var areas=otherArts.SelectMany(a=>a.Sheet.PreservedAreas??Array.Empty<Sheet305.PreserveArea>()).Where(x=>x!=null&&x.ExcludeProcedural).ToArray();
   var areaReach=areas.Select(x=>x.HalfSize.magnitude+(x.TypedClearance?Mathf.Max(x.Padding.x,Mathf.Max(x.Padding.y,x.Padding.w)):2f)+.5f).ToArray();
   var reserved=new Grid305(16f);
   foreach(var p in content.Points??Array.Empty<PrologueContentSO.Point>())if(p!=null)reserved.Add(p.Position);
   foreach(var e in content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>())if(e!=null)reserved.Add(e.Feet);
   foreach(var c in content.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>())if(c!=null)reserved.Add(c.Feet);

   var dropped=new Dictionary<string,int>();void Drop(string why,string kind){string k=kind+" "+why;dropped[k]=dropped.TryGetValue(k,out int v)?v+1:1;}
   var kept=new List<Sheet305.FixedPlacement>();var ids=new HashSet<string>();var forestVsOffline=new List<float>();var keptBy=new SortedDictionary<string,int>();
   foreach(var c in forest.candidates??Array.Empty<Cand305>())
   {
    if(c==null||c.Position==null||c.Position.Length<3)continue;
    if(string.IsNullOrEmpty(c.PrototypeId)||!kinds.TryGetValue(c.PrototypeId,out var kind)){Drop("prototype missing",c.kind);continue;}
    string kname=kind==Sheet305.Kind.Tree?"tree":kind==Sheet305.Kind.Shrub?"shrub":kind.ToString().ToLowerInvariant();
    if(string.IsNullOrEmpty(c.Id)||!ids.Add(c.Id)){Drop("duplicate id",kname);continue;}
    var xz=new Vector2(c.Position[0],c.Position[2]);
    if(trees.Any(xz,TreeClear305)){Drop("existing tree "+TreeClear305+" m",kname);continue;}
    if(reserved.Any(xz,ContentClear305)){Drop("content/encounter/checkpoint "+ContentClear305+" m",kname);continue;}
    if(!Ground305(xz.x,xz.y,root.transform,skip,out float gy,out _)){Drop("no ground",kname);continue;}
    var pos=new Vector3(xz.x,gy-.05f,xz.y);bool preserved=false;
    for(int i=0;i<areas.Length&&!preserved;i++)
     if((new Vector2(areas[i].Centre.x,areas[i].Centre.z)-xz).magnitude<=areaReach[i]&&Sheet305.Excludes(areas[i],pos,kind))preserved=true;
    if(preserved){Drop("preserved area",kname);continue;}
    if(kept.Count>=PlacementCap305-1){Drop("cap 50000",kname);continue;}
    forestVsOffline.Add(pos.y-c.Position[1]);
    kept.Add(new Sheet305.FixedPlacement{Id=c.Id,ClusterId=c.ClusterId,PrototypeId=c.PrototypeId,Position=pos,Euler=c.Euler!=null&&c.Euler.Length>=3?new Vector3(c.Euler[0],c.Euler[1],c.Euler[2]):Vector3.zero,Scale=c.Scale>0?c.Scale:1,Preserve=true});
    keptBy[kname]=keptBy.TryGetValue(kname,out int kn)?kn+1:1;
   }
   // ---- cover fill (audit b / Spec AC-2): every 1.6 m along each shell a tree/shrub 0-2.6 m in front of the player face. Gaps
   // (existing-tree, slope, water drops) get a shrub 1.0-1.8 m in front of the face; shrubs keep 1 m off existing trunks only.
   var veg=new Grid305(8f);
   foreach(var a in otherArts)
   {
    var cat=a.Sheet.Prototypes.Where(p=>p!=null&&p.Id!=null).GroupBy(p=>p.Id).ToDictionary(x=>x.Key,x=>x.First().Category);
    foreach(var f in a.Sheet.FixedPlacements)if(f!=null&&f.PrototypeId!=null&&cat.TryGetValue(f.PrototypeId,out var c)&&(c==Sheet305.Kind.Tree||c==Sheet305.Kind.Shrub))veg.Add(f.Position);
   }
   foreach(var f in kept)if(kinds.TryGetValue(f.PrototypeId,out var fk)&&(fk==Sheet305.Kind.Tree||fk==Sheet305.Kind.Shrub))veg.Add(f.Position);
   var shrubIds=protos.Where(p=>p.Category==Sheet305.Kind.Shrub).Select(p=>p.Id).ToArray();int filled=0,unfillable=0;
   foreach(var g in segs)
   {
    var st=stations[g];var cum=new float[st.Length];for(int i=1;i<st.Length;i++)cum[i]=cum[i-1]+Vector2.Distance(st[i-1].q,st[i].q);
    var pal=shrubIds.Where(x=>x.StartsWith(g.realm+"_")).ToArray();if(pal.Length==0)pal=shrubIds;if(pal.Length==0)break;
    float L=cum[cum.Length-1];int j=1,n=0;
    for(float s=0;s<=L+1e-3f;s+=1.6f)
    {
     while(j<st.Length-1&&cum[j]<s)j++;float t=Mathf.Clamp01((s-cum[j-1])/Mathf.Max(1e-5f,cum[j]-cum[j-1]));
     var q=Vector2.Lerp(st[j-1].q,st[j].q,t);var np=-(Vector2.Lerp(st[j-1].nb,st[j].nb,t)).normalized;var tg=new Vector2(-np.y,np.x);var qf=q+np*(Thick305*.5f);
     if(veg.Near(qf,2.6f).Any(v=>Vector2.Dot(new Vector2(v.x,v.z)-qf,np)>=0))continue;
     bool done=false;
     foreach(var (u,w) in new[]{(1.0f,0f),(1.8f,0f),(1.2f,.7f),(1.2f,-.7f),(2.2f,0f)})
     {
      var xz=qf+np*u+tg*w;
      if(trees.Any(xz,1f)||reserved.Any(xz,ContentClear305))continue;
      if(!Ground305(xz.x,xz.y,root.transform,skip,out float gy,out _))continue;
      var pos=new Vector3(xz.x,gy-.05f,xz.y);bool pres=false;
      for(int i=0;i<areas.Length&&!pres;i++)if((new Vector2(areas[i].Centre.x,areas[i].Centre.z)-xz).magnitude<=areaReach[i]&&Sheet305.Excludes(areas[i],pos,Sheet305.Kind.Shrub))pres=true;
      if(pres)continue;
      string id="enclosure305_fill_"+Short305(g.id)+"_"+n++;if(!ids.Add(id))continue;
      float h=Mathf.Abs(Mathf.Sin((q.x*12.9898f+q.y*78.233f)*.37f));
      kept.Add(new Sheet305.FixedPlacement{Id=id,ClusterId=g.id,PrototypeId=pal[(int)(h*pal.Length)%pal.Length],Position=pos,Euler=new Vector3(0,h*360f,0),Scale=1.1f+.4f*h,Preserve=true});
      veg.Add(pos);keptBy["shrub fill"]=keptBy.TryGetValue("shrub fill",out int fn)?fn+1:1;filled++;done=true;break;
     }
     if(!done)unfillable++;
    }
   }
   dropped["cover fill: unfillable samples (content/preserved/no ground)"]=unfillable;
   sheet.FixedPlacements=kept.ToArray();EditorUtility.SetDirty(sheet);

   var fr=new GameObject("Forest305_Renderer");fr.transform.SetParent(root.transform,false);fr.layer=0;
   var art=fr.AddComponent<CompactRebuildArtRenderer>();art.Sheet=sheet;art.Observer=main.Observer;art.Contacts=main.Contacts;art.Invalidate();

   // ---- save: only what this command wrote, then the scene ----
   foreach(var o in saved)AssetDatabase.SaveAssetIfDirty(o);AssetDatabase.SaveAssetIfDirty(sheet);
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   var stale=Directory.Exists(A305+"/Meshes")?Directory.GetFiles(A305+"/Meshes","E305_*.asset").Select(Path.GetFileNameWithoutExtension).Where(x=>!made.Contains(x)).ToArray():Array.Empty<string>();

   // ---- ledger ----
   var sb=new StringBuilder();string sceneName=Path.GetFileNameWithoutExtension(path);
   int arcs=joints.Count(j=>j.kind=="arc"),boxes=ledgers.Sum(l=>l.boxes)+jledgers.Sum(x=>x.l.boxes);
   sb.AppendLine("#305 build-scene "+path+" | "+DateTime.Now.ToString("s")+" | segments305 "+version+" | candidates "+forest.version);
   sb.AppendLine("TEST numbers; automated Edit-mode build, not manual play. backup "+backup);
   sb.AppendLine("segments N6="+segs.Count+" (other kits skipped="+otherKits+") length="+F305(segs.Sum(Stations305Length),"F1")+" m (json "+F305(segs.Sum(s=>s.length),"F1")+" m)");
   sb.AppendLine("shell: step "+Step305+" m, offset "+Offset305+" m to the blocked side, across +-"+Across305+" m, bottom=lowest terrain-"+Below305+", top=highest walkable support (any kind, also at the player face and 4.5 / 6 m out on the player side)+"+Above305+", thick "+Thick305+" m, chunks "+Chunk305+" m");
   sb.AppendLine("shell chunks="+ledgers.Sum(l=>l.chunks)+" + joint strips="+arcs+" + joint plugs="+plugs+" (capsule r "+Plug305+" m) tris="+tris+" carve boxes="+boxes+" (<= "+BoxLen305+" m, "+BoxThick305+" m thick, ground-"+BoxBelow305+"..ground+"+BoxAbove305+", carving + carveOnlyStationary)");
   sb.AppendLine("joints (ends on one line point <= 5 cm)="+joints.Count+": arc "+arcs+", cross "+joints.Count(j=>j.kind=="cross")+", flush "+joints.Count(j=>j.kind=="flush")+"; open ends="+segs.Sum(s=>s.ends.Count(e=>e=="open")));
   foreach(var j in joints.Where(j=>j.note.StartsWith("SIDE FLIP")))sb.AppendLine("  FLAG "+j.name+" "+j.note+": no joint can keep both player sides apart");
   sb.AppendLine("ground: non-terrain fallback samples="+G.fallback+", stations without any ground (borrowed)="+G.missingAll+", missing probe samples="+(ledgers.Sum(l=>l.missing)+jledgers.Sum(x=>x.l.missing)));
   sb.AppendLine("ground: samples where a non-terrain support stands > 0.5 m above the terrain (raises top)="+G.raised+(G.raisedBy.Count>0?" by "+string.Join(", ",G.raisedBy.OrderByDescending(x=>x.Value).Take(40).Select(x=>x.Key+" x"+x.Value)):""));
   sb.AppendLine((G.low.Count==0?"PASS":"FAIL")+" blocking height >= "+MinBlock305+" m above player-side support (face+"+F305(Thick305*.5f+.3f)+" m and "+Across305+" m): stations under = "+G.low.Count);
   foreach(var x in G.low.Take(200))sb.AppendLine("  "+x);
   sb.AppendLine("shell ground - offline height.bytes: "+(offline==null?"offline field unavailable":Stats305(G.vsOffline)));
   sb.AppendLine("forest: prototypes "+protos.Count+" copied from "+AssetDatabase.GetAssetPath(src)+(missingProtos.Count>0?" MISSING "+string.Join(",",missingProtos):"")+
                 "; distances TreeNear "+src.TreeNear+" TreeMiddle "+src.TreeMiddle+" Forest "+src.ForestDistance+" Shrub "+src.ShrubDistance+" Grass "+src.GrassDistance+" GrassMesh "+src.GrassMeshDistance);
   sb.AppendLine("forest: candidates "+(forest.candidates?.Length??0)+" kept "+kept.Count+" ("+string.Join(", ",keptBy.Select(x=>x.Key+" "+x.Value))+") -> "+sheetPath);
   sb.AppendLine("  editor exclusion radii: existing tree "+TreeClear305+" m (all kinds), content/encounter/checkpoint "+ContentClear305+" m, PreservedAreas of the existing sheets");
   foreach(var kv in dropped.OrderBy(k=>k.Key))sb.AppendLine("  dropped "+kv.Key+": "+kv.Value);
   sb.AppendLine("  existing trees indexed="+trees.Count+" from "+otherArts.Length+" sheets (read only); preserved areas="+areas.Length+"; reserved points="+reserved.Count);
   sb.AppendLine("forest ground - offline candidate y: "+Stats305(forestVsOffline));
   sb.AppendLine("renderer: "+Root305+"/Forest305_Renderer Observer="+main.Observer.name+" Contacts="+(main.Contacts!=null?main.Contacts.name:"null")+" (copied from "+main.gameObject.name+")");
   if(stale.Length>0)sb.AppendLine("stale mesh assets not produced by this build (left in place): "+string.Join(",",stale));
   sb.AppendLine("per segment: id realm zone | stations chunks boxes | span(top-bottom) min..max | free(top-line ground) min..max | player-side block (top - player support) min, LOW = stations < "+MinBlock305+" m | ends | notes");
   foreach(var l in ledgers)sb.AppendLine(Line305(l));
   sb.AppendLine("per joint: name realm kind | arc stations chunks boxes | span | free | player-side block | segments | angle(nA->nB) gap(|qA-qB|)");
   foreach(var x in jledgers)sb.AppendLine(Line305(x.l));
   File.WriteAllText(O305+"/build-"+sceneName+".txt",sb.ToString());
   return "built "+Root305+" in "+path+": "+segs.Count+" N6 segments, "+ledgers.Sum(l=>l.chunks)+" shell chunks + "+arcs+" joint strips ("+joints.Count+" joints), "+boxes+" carve boxes, forest "+kept.Count+"/"+(forest.candidates?.Length??0)+
          " placements; block height "+(G.low.Count==0?"PASS":"FAIL "+G.low.Count+" stations < "+MinBlock305+" m")+"; ledger "+Path.GetFullPath(O305+"/build-"+sceneName+".txt")+"; backup "+backup;
  }
  static string Line305(SegLedger305 l)=>l.id+" "+l.realm+" "+l.zone+" | "+l.stations+" "+l.chunks+" "+l.boxes+" | "+(l.chunks>0?F305(l.minSpan)+".."+F305(l.maxSpan):"-")+" | "+(l.chunks>0?F305(l.minFree)+".."+F305(l.maxFree):"-")+" | "+
                                         (float.IsInfinity(l.minPlayer)?"n/a":F305(l.minPlayer))+(l.low>0?" LOW "+l.low:"")+(l.missing>0?" | missing "+l.missing:"")+" | "+l.ends+" | "+l.notes;

  // ground columns per station: lo = lowest terrain of the +-3 m across samples, hi = highest walkable support of any kind at those
  // samples and at the player face (q - nb*(thick/2+.3)); bottom = lo-1, top = hi+4.5; top - player-side support < 4.0 m = FAIL line
  static void Columns305(string id,Station305[] st,Transform root,HashSet<Collider> skip,Func<float,float,float> offline,Tally305 G,SegLedger305 L,out float[] bottom,out float[] top,out float[] mid)
  {
   int n=st.Length;var lo=new float[n];var hi=new float[n];mid=new float[n];var player=new float[n];
   for(int k=0;k<n;k++)
   {
    lo[k]=float.PositiveInfinity;hi[k]=float.NegativeInfinity;mid[k]=float.NaN;player[k]=float.NegativeInfinity;
    for(int a=-4;a<=1;a++)
    {
     // a = -4 / -3: player side 6 m / 4.5 m (a Guk pillar + jump on higher ground reaches ~4-5 m: check 305 cross joints);
     // a = -2: the player face (+.3 m); -1 / 0 / +1: player side 3 m, shell centre, blocked side 3 m
     var xz=a==-4?st[k].q-st[k].nb*6f:a==-3?st[k].q-st[k].nb*4.5f:a==-2?st[k].q-st[k].nb*(Thick305*.5f+.3f):st[k].q+st[k].nb*(a*Across305);
     if(!Ground305(xz.x,xz.y,root,skip,out float y,out bool terrain,out float up,out var by)){L.missing++;continue;}
     if(!terrain)G.fallback++;else if(up>y+.5f&&by!=null){G.raised++;G.raisedBy[by.name]=G.raisedBy.TryGetValue(by.name,out int r)?r+1:1;}
     hi[k]=Mathf.Max(hi[k],up);if(a<=-1)player[k]=Mathf.Max(player[k],up);if(a<=-2)continue;
     lo[k]=Mathf.Min(lo[k],y);
     if(a==0){mid[k]=y;if(offline!=null)G.vsOffline.Add(y-offline(xz.x,xz.y));}
    }
    if(float.IsNaN(mid[k])&&!float.IsInfinity(lo[k]))mid[k]=(lo[k]+hi[k])*.5f;
   }
   // stations without any ground borrow the nearest measured one (offline field as the last resort)
   for(int k=0;k<n;k++)
   {
    if(!float.IsInfinity(lo[k]))continue;G.missingAll++;int best=-1;
    for(int r=1;r<n&&best<0;r++){if(k-r>=0&&!float.IsInfinity(lo[k-r]))best=k-r;else if(k+r<n&&!float.IsInfinity(lo[k+r]))best=k+r;}
    if(best>=0){lo[k]=lo[best];hi[k]=Mathf.Max(hi[k],hi[best]);mid[k]=mid[best];player[k]=Mathf.Max(player[k],player[best]);}
    else if(offline!=null){float y=offline(st[k].q.x,st[k].q.y);lo[k]=mid[k]=y;hi[k]=Mathf.Max(hi[k],y);}
    else throw new Exception("no ground anywhere along "+id);
   }
   bottom=new float[n];top=new float[n];
   for(int k=0;k<n;k++)
   {
    bottom[k]=lo[k]-Below305;top[k]=hi[k]+Above305;
    L.minSpan=Mathf.Min(L.minSpan,top[k]-bottom[k]);L.maxSpan=Mathf.Max(L.maxSpan,top[k]-bottom[k]);
    L.minFree=Mathf.Min(L.minFree,top[k]-mid[k]);L.maxFree=Mathf.Max(L.maxFree,top[k]-mid[k]);
    if(float.IsNegativeInfinity(player[k]))continue;float block=top[k]-player[k];L.minPlayer=Mathf.Min(L.minPlayer,block);
    if(block<MinBlock305){L.low++;G.low.Add("FAIL "+id+" s="+F305(st[k].s,"F1")+" block "+F305(block)+" m < "+MinBlock305+" m at "+st[k].q.ToString("F1"));}
   }
  }
  static long ShellChunk305(string name,Station305[] st,float[] bottom,float[] top,int i0,int i1,Transform parent,List<Object> saved,HashSet<string> made)
  {
   var mesh=ArtMesh(ShellMesh305(name,st,bottom,top,i0,i1),A305+"/Meshes/"+name+".asset");saved.Add(mesh);made.Add(name);
   var go=new GameObject(name);go.transform.SetParent(parent,false);go.layer=0;
   var mc=go.AddComponent<MeshCollider>();mc.convex=false;mc.isTrigger=false;mc.sharedMesh=null;mc.sharedMesh=mesh;
   GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
   return mesh.triangles.Length/3;
  }
  static float Stations305Length(Seg305 g){float L=0;for(int i=1;i<g.pts.Length;i++)L+=Vector2.Distance(g.pts[i-1],g.pts[i]);if(g.closed&&g.pts.Length>2)L+=Vector2.Distance(g.pts[g.pts.Length-1],g.pts[0]);return L;}

  // same Id, same mesh/material references; a value copy so the source sheet's objects are never shared in memory
  static Sheet305.Prototype Proto305(Sheet305.Prototype s)=>new Sheet305.Prototype{Id=s.Id,SourcePath=s.SourcePath,SourceHash=s.SourceHash,Realm=s.Realm,Category=s.Category,Weight=s.Weight,
   Scale=s.Scale,Size=s.Size,Radius=s.Radius,WetBank=s.WetBank,GroundPatch=s.GroundPatch,LowInfill=s.LowInfill,BillboardViews=s.BillboardViews,PolishVersion=s.PolishVersion,
   MaximumAltitude=s.MaximumAltitude,MaximumSlope=s.MaximumSlope,GroundPoints=(s.GroundPoints??Array.Empty<Vector3>()).ToArray(),
   Lods=(s.Lods??Array.Empty<Sheet305.Level>()).Select(l=>new Sheet305.Level{Parts=(l?.Parts??Array.Empty<Sheet305.Part>()).Select(p=>new Sheet305.Part{Mesh=p.Mesh,Submesh=p.Submesh,Material=p.Material,Local=p.Local}).ToArray()}).ToArray()};

  // closed strip: player face + blocked face + top + both end caps (no bottom: it is buried 1 m); every triangle wound outward
  static Mesh ShellMesh305(string name,Station305[] st,float[] bottom,float[] top,int i0,int i1)
  {
   var v=new List<Vector3>();var tri=new List<int>();float half=Thick305*.5f;
   for(int k=i0;k<=i1;k++)
   {
    var a=st[k].q-st[k].nb*half;var b=st[k].q+st[k].nb*half;
    v.Add(new Vector3(a.x,bottom[k],a.y));v.Add(new Vector3(a.x,top[k],a.y));v.Add(new Vector3(b.x,bottom[k],b.y));v.Add(new Vector3(b.x,top[k],b.y));
   }
   void Tri(int a,int b,int c,Vector3 o){if(Vector3.Dot(Vector3.Cross(v[b]-v[a],v[c]-v[a]),o)>=0){tri.Add(a);tri.Add(b);tri.Add(c);}else{tri.Add(a);tri.Add(c);tri.Add(b);}}
   void Quad(int a,int b,int c,int d,Vector3 o){Tri(a,b,c,o);Tri(a,c,d,o);}
   for(int k=i0;k<i1;k++)
   {
    int j=(k-i0)*4,m=j+4;var nb=st[k].nb+st[k+1].nb;var N=new Vector3(nb.x,0,nb.y);
    Quad(j,m,m+1,j+1,-N);          // player face
    Quad(j+2,j+3,m+3,m+2,N);       // blocked face
    Quad(j+1,m+1,m+3,j+3,Vector3.up);
   }
   int e=(i1-i0)*4;
   Quad(0,1,3,2,-new Vector3(st[i0].t.x,0,st[i0].t.y));Quad(e,e+1,e+3,e+2,new Vector3(st[i1].t.x,0,st[i1].t.y));
   var mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
  }

  // yaw-only carve boxes along the shell centre (CompactRock275 precedent): runs of <= 7 stations, halved while the chord leaves
  // the line by > .25 m; .6 m overlap between neighbours, never longer than 12 m. Obstacle only (no collider, no renderer).
  static int Boxes305(string seg,Station305[] st,float[] ground,int i0,int i1,Transform parent,ref int index)
  {
   int made=0,next=index;
   void Run(int a,int b)
   {
    var A=st[a].q;var B=st[b].q;var d=B-A;float len=d.magnitude,dev=0;
    for(int k=a+1;k<b;k++)dev=Mathf.Max(dev,SegDist305(st[k].q,A,B));
    if(b-a>1&&(len+.6f>BoxLen305||dev>.25f)){int m=(a+b)/2;Run(a,m);Run(m,b);return;}
    float lo=float.PositiveInfinity,hi=float.NegativeInfinity;for(int k=a;k<=b;k++){lo=Mathf.Min(lo,ground[k]);hi=Mathf.Max(hi,ground[k]);}
    float y0=lo-BoxBelow305,y1=hi+BoxAbove305;var dir=len>1e-4f?d/len:st[a].t;var c=(A+B)*.5f;
    var go=new GameObject(seg+"_ob"+next++);go.transform.SetParent(parent,false);go.layer=0;
    go.transform.SetPositionAndRotation(new Vector3(c.x,(y0+y1)*.5f,c.y),Quaternion.LookRotation(new Vector3(dir.x,0,dir.y),Vector3.up));
    var ob=go.AddComponent<NavMeshObstacle>();ob.shape=NavMeshObstacleShape.Box;ob.center=Vector3.zero;ob.size=new Vector3(BoxThick305,y1-y0,Mathf.Min(BoxLen305,len+.6f));
    ob.carving=true;ob.carveOnlyStationary=true;made++;
   }
   for(int a=i0;a<i1;a+=7)Run(a,Mathf.Min(i1,a+7));
   index=next;return made;
  }
 }
}
