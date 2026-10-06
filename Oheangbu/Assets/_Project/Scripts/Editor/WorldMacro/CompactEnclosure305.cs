using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #305 field enclosure, phase 1 (SPEC-WORLD-ENCLOSURE-305 / D305): kit N6 only = dense forest band + hidden collision shell on
 // the EA frontier of W_Demo_Main. Inputs are offline (Tools/Art/enclosure305.py): segments305.json + Out/forest305_candidates.json.
 // Queue calls never open dialogs: refusals come back as "REFUSED ..." strings. check/audit never save.
 //   status                  inputs + whether the active scene carries the Enclosure305 root
 //   plan-scene:<path>       read only (dry run of build-scene): stations / shells / candidates against the cliff mask and the ground of the ACTIVE scene -> Out/plan-<scene>.txt
 //   build-scene:<path>      backup, rebuild root (Shell/*_col + joint strips E305_J*_col, Obstacles/*_ob*, Forest305_Renderer) + Forest305.asset + meshes
 //                           #308: refused unless the segment file and the scene's height belong together (requires_height_sha256); a byte copy of
 //                           Forest305.asset goes to Out/Before first. Revert = put the old inputs back (Tools/Art/enclosure305_cliff308.py revert) and build again
 //   remove-scene:<path>     destroy the root, save (assets stay)
 //   check-scene:<path>      CC push probes (walk / +1.05 / +3.45) on segments, joints and open ends + reserved-site distances -> Out/check-<scene>.txt
 //   audit-scene:<path>      invisible-wall audit + cover density + renderer-less colliders elsewhere -> Out/audit-<scene>.txt
 //   joints-scene:<path>     read only (#308): the check-scene push probes on the last open stations before every shell stretch that lies in the
 //                           cliff mask (rock stub, face crossing) - check-scene itself cannot start there -> Out/joints-<scene>.txt
 public static class Enclosure305 { public static string Run(string command)=>CompactRebuildAuthoring.Enclosure305(command); }
 public static partial class CompactRebuildAuthoring
 {
  const string E305="../Art/World/Compact/Rebuild/Enclosure305",O305=E305+"/Out",A305="Assets/_Project/Art/World/Enclosure305",Root305="Enclosure305";
  // #308 (SPEC-WORLD-CLIFF-BOUNDARY-308 높이 출처, AC-B24): the comparison field is the active scene's own height asset (CliffHeight308), not a constant
  static string Height305=>CliffHeight308.Active();
  static readonly string[] Scenes305={"Assets/_Project/Scenes/World/W_Demo_Main.unity","Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity","Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity"};   // main + the two candidates it is promoted from (NavMesh is baked in the #296 candidate)
  // TEST numbers (Spec 설계): resample, offset toward the blocked side, across probe, below/above ground, thickness, chunk length
  const float Step305=1.5f,Offset305=1f,Across305=3f,Below305=1f,Above305=5.5f,Thick305=.6f,Chunk305=48f,Plug305=2.4f,PlugCross305=3.5f;   // Above 4.5 -> 5.5 (check 305: 2 m over Guk reach 3.45)
  // carve boxes (Temporary Exception until the NavMesh rebake)
  const float BoxLen305=12f,BoxThick305=1.2f,BoxBelow305=1.5f,BoxAbove305=5.5f;
  // NavMesh re-baked with the shells and plugs in the #296 candidate (2026-09-30, D305 bake hold lifted): carve boxes are off. Set true
  // only if a scene must run on a NavMesh baked without Enclosure305 (Spec 305 Temporary Exception, now closed).
  const bool Carve305=false;
  // Spec: blocking height above player-side ground >= 4.0 m; forest top-up keeps 3.5 m off existing trees (every kind); 6 m off content
  const float MinBlock305=4f,TreeClear305=3.5f,ContentClear305=6f;
  const int PlacementCap305=50000;
  // joints-scene (TEST): open stations probed before a masked shell stretch (4 x 1.5 m = the last 6 m before the rock)
  const int JointBack305=4;

  public static string Enclosure305(string command)
  {
   Directory.CreateDirectory(O305);
   try
   {
    // #308: every command sees the cliff mask of the segment file that is on disk now (no mask in a pre-cliff file)
    string cliffProblem=null;
    try{Cliff305Now=LoadCliff305();}catch(Exception e){Cliff305Now=new Cliff305();cliffProblem=e is Refuse305?e.Message:"segments305.json unreadable: "+e.Message;}
    if(command=="status")return Status305()+(cliffProblem!=null?" | CLIFF MASK PROBLEM: "+cliffProblem:"");
    if(cliffProblem!=null)return "REFUSED "+cliffProblem;
    if(command.StartsWith("plan-scene:"))return Plan305(command.Substring(11).Trim());
    if(command.StartsWith("build-scene:"))
    {
     string target=command.Substring(12).Trim();PairGuard305();HeightGuard305(target);
     string sheetCopy=Scenes305.Contains(target)&&!EditorApplication.isPlayingOrWillChangePlaymode?BackupSheet305():"";
     string built=Build305(target);return sheetCopy==""?built:built+"; Forest305.asset before this build "+sheetCopy;
    }
    if(command.StartsWith("remove-scene:"))return Remove305(command.Substring(13).Trim());
    if(command.StartsWith("check-scene:"))return Check305(command.Substring(12).Trim());
    if(command.StartsWith("audit-scene:"))return Audit305(command.Substring(12).Trim());
    if(command.StartsWith("joints-scene:"))return CliffJoints305(command.Substring(13).Trim());
   }
   catch(Refuse305 r){return "REFUSED "+r.Message;}
   return "ERROR unknown Enclosure305 command '"+command+"' (status | plan-scene:<path> | build-scene:<path> | remove-scene:<path> | check-scene:<path> | audit-scene:<path> | joints-scene:<path>)";
  }
  sealed class Refuse305:Exception{public Refuse305(string m):base(m){}}

  // ---------- #308 cliff stage link (segments305.json 305.3+, Tools/Art/enclosure305_cliff308.py) ----------

  // "cliff_mask" = the 4 m quads that are no Enclosure305 ground: every cliff face and every cliff top that may not carry forest.
  // Ground305 answers "no ground" there, so no shell column, forest placement, cover shrub or probe start is ever seated on one
  // (the normal.y <= .5 test alone lets 40-60 deg rises and the lifted tops through). "requires_height_sha256" = the height the
  // file was cut for. A file without the keys (305.1 / 305.2) has no mask; build-scene refuses it on a #308 stage height.
  sealed class Cliff305
  {
   public string version="",need="",file="",sha="";public int rows,cols,masked;public float cell=4f;public byte[] bits;
   public bool Masked(float x,float z){if(bits==null)return false;int j=Mathf.Clamp(Mathf.FloorToInt(x/cell),0,cols-1),i=Mathf.Clamp(Mathf.FloorToInt(z/cell),0,rows-1);return bits[i*cols+j]!=0;}
   // review: value 2 / 3 = a masked quad whose tile triangle (2: corners 00,10,01 = fx+fz<=1; 3: corners 11,01,10) has no changed vertex:
   // base ground at the foot of a face, where a player stands. The shell columns count it (Ground305, 8 arguments); forest, cover fill,
   // plugs and probe starts keep the whole quad out (Masked)
   public bool MaskedColumn(float x,float z)
   {
    if(bits==null)return false;float fx=x/cell,fz=z/cell;int j=Mathf.Clamp(Mathf.FloorToInt(fx),0,cols-1),i=Mathf.Clamp(Mathf.FloorToInt(fz),0,rows-1);byte v=bits[i*cols+j];
    if(v<2)return v!=0;bool low=(fx-j)+(fz-i)<=1f;return v==2?!low:low;
   }
  }
  static Cliff305 Cliff305Now=new Cliff305();
  static double JNum305(Dictionary<string,object> d,string k)=>d.TryGetValue(k,out var v)&&v!=null?Convert.ToDouble(v,CultureInfo.InvariantCulture):0;
  static Cliff305 LoadCliff305()
  {
   var c=new Cliff305();var d=(Dictionary<string,object>)Json305.Parse(File.ReadAllText(E305+"/segments305.json"));
   c.version=JStr305(d,"version");c.need=JStr305(d,"requires_height_sha256");
   if(!d.TryGetValue("cliff_mask",out var o)||!(o is Dictionary<string,object> m))return c;
   c.file=JStr305(m,"file");c.sha=JStr305(m,"sha256");c.rows=(int)JNum305(m,"rows");c.cols=(int)JNum305(m,"cols");c.cell=(float)JNum305(m,"cell_m");
   if(c.rows<=0||c.cols<=0||c.cell<=0||c.file=="")throw new Refuse305("segments305.json "+c.version+" cliff_mask has no file / rows / cols / cell_m");
   string path="../"+c.file;   // the file name is repository-relative, the editor runs in the Unity project folder
   if(!File.Exists(path))throw new Refuse305("cliff mask "+c.file+" named by segments305.json "+c.version+" is missing (python Tools/Art/enclosure305_cliff308.py build)");
   var b=File.ReadAllBytes(path);
   if(b.Length!=c.rows*c.cols)throw new Refuse305("cliff mask "+c.file+" holds "+b.Length+" bytes, segments305.json says "+c.rows+" x "+c.cols);
   string sha=CliffCore308.Sha(b);
   if(!string.Equals(sha,c.sha,StringComparison.OrdinalIgnoreCase))throw new Refuse305("cliff mask "+c.file+" sha256 "+sha+" is not the one segments305.json "+c.version+" names ("+c.sha+"); run the offline tool again");
   c.bits=b;for(int i=0;i<b.Length;i++)if(b[i]!=0)c.masked++;
   return c;
  }
  // build-scene: the segment file and the ground of the scene must belong together (read only; Open305 refuses unknown scenes itself)
  static void HeightGuard305(string path)
  {
   if(!Scenes305.Contains(path))return;
   string height=CliffHeight308.For(path,out string source),sha=CliffHeight308.Sha(height),basePath=CliffHeight308.For("",out _);var c=Cliff305Now;
   string where=path+" stands on "+height+" (sha256 "+sha+", "+source+")";
   if(c.need!="")
   {
    if(!string.Equals(sha,c.need,StringComparison.OrdinalIgnoreCase))
     throw new Refuse305("segments305.json "+c.version+" is cut for the height sha256 "+c.need+" but "+where+". Run CliffBoundary308 tiles:<scene> first, or put the pre-cliff inputs back (python Tools/Art/enclosure305_cliff308.py revert). Nothing changed");
    if(c.bits==null)throw new Refuse305("segments305.json "+c.version+" names a height but carries no cliff_mask. Nothing changed");
   }
   else if(height!=basePath)
    throw new Refuse305("segments305.json "+c.version+" has no cliff mask (pre-cliff file) but "+where+": its runs would be seated on cliff faces. Build 305.3 first (python Tools/Art/enclosure305_cliff308.py build) or CliffBoundary308 revert:<scene>. Nothing changed");
  }
  // build-scene: segments305.json and Out/forest305_candidates.json are replaced one after the other by the offline tool
  // (enclosure305_cliff308.py build / revert). A pair of two versions (an interrupted run, a hand copy) would seat the forest of
  // removed runs without their shells: refuse it (read only)
  static void PairGuard305()
  {
   string fv;try{fv=Forest305Input().version??"";}catch(Exception e){throw new Refuse305("Out/forest305_candidates.json is unreadable ("+e.Message+"). Nothing changed");}
   if(fv!=Cliff305Now.version)
    throw new Refuse305("segments305.json is version "+Cliff305Now.version+" but Out/forest305_candidates.json is version "+fv+": not one pair. Run python Tools/Art/enclosure305_cliff308.py build (or revert) again. Nothing changed");
  }
  // joints-scene:<path>: read only. check-scene starts its probes 3 m on the player side of a station; at a shell end that lies in rock
  // that start is in the cliff mask ("no ground at the start" = INCONCLUSIVE), so the forest-cliff joints are never pushed. Here: for
  // every stretch of shell stations in the cliff mask (rock stub at a run end, face crossing) and each open side of it, the same walker
  // and verdicts on the last JointBack305 open stations (straight to the blocked side) and one run aimed past the buried stretch.
  // FAIL = feet past the shell centre line by more than the radius (a way round the shell end). Nothing is opened, changed or saved.
  static string CliffJoints305(string path)
  {
   var (_,root)=Active305(path);var cm=Cliff305Now;
   if(cm.bits==null)throw new Refuse305("segments305.json "+cm.version+" has no cliff mask: no forest-cliff joint to probe");
   var session=Session292();var skip=Skip305(session);var segs=N6Segments305(out string version,out _);
   var body=session.Walker!=null?session.Walker.Body:null;
   var go=new GameObject("Enclosure305_probe_fixture"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);var cc=go.AddComponent<CharacterController>();
   if(body!=null)
   {
    var scale=body.transform.lossyScale;float sy=Mathf.Abs(scale.y),sr=Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));
    cc.height=body.height*sy;cc.center=Vector3.Scale(body.center,scale);cc.radius=body.radius*sr;cc.skinWidth=body.skinWidth;cc.stepOffset=body.stepOffset;cc.slopeLimit=body.slopeLimit;
   }
   else{cc.height=1.75f;cc.center=new Vector3(0,.875f,0);cc.radius=.28f;cc.stepOffset=.3f;cc.slopeLimit=45f;}
   float reach=Thick305*.5f+cc.radius+cc.skinWidth+.15f;
   // read for the report before the probe object is destroyed in the finally below
   float ccHeight=cc.height,ccRadius=cc.radius,ccStep=cc.stepOffset,ccSlope=cc.slopeLimit;
   var tot=Variants305.ToDictionary(v=>v.name,v=>new Tot305());var lines=new List<string>();var places=new List<string>();int runs=0;
   var disabled=skip.Where(c=>c!=null&&c.enabled).ToArray();foreach(var c in disabled)c.enabled=false;Physics.SyncTransforms();
   try
   {
    foreach(var g in segs)
    {
     var st=Stations305(g);int n=st.Length;
     for(int a=0;a<n;a++)
     {
      if(!cm.Masked(st[a].q.x,st[a].q.y))continue;
      int b=a;while(b+1<n&&cm.Masked(st[b+1].q.x,st[b+1].q.y))b++;
      string kind=b==n-1?"end 1 in rock":a==0?"end 0 in rock":"face crossing";
      // the open neighbour before the stretch (the run walks into it) and the one after it (the run comes out of it)
      foreach(var (kc,step) in new[]{(a-1,1),(b+1,-1)})
      {
       if(kc<0||kc>=n)continue;
       string label=g.id+" stations "+a+".."+b+" ("+kind+") from station "+kc+" at "+st[kc].q.ToString("F1");places.Add(label);
       for(int back=0;back<JointBack305;back++)
       {
        int k=kc-step*back;if(k<0||k>=n||cm.Masked(st[k].q.x,st[k].q.y))break;
        var s0=st[k];var from=s0.p-s0.nb*ProbeStart305;var retry=s0.q-s0.nb*(Thick305*.5f+1f);
        foreach(var (vn,rise) in Variants305)
        {
         runs++;var v=Probe305(cc,go,st,k,12,from,from+s0.nb*ProbePush305,retry,retry+s0.nb*ProbePush305,rise,root,skip,reach,out string note);tot[vn].Add(v);
         if(v!=Verdict305.Pass)lines.Add((v==Verdict305.Fail?"FAIL ":"INCONCLUSIVE ")+label+" | straight at s="+F305(s0.s,"F1")+" "+vn+": "+note);
        }
       }
       // aimed past the buried stretch: from the player side of the last open station to the blocked side of the far masked station
       var e0=st[kc];int far=step>0?b:a;var start=e0.p-e0.nb*ProbeStart305;var aim=st[far].q+st[far].nb*ProbeStart305;
       var d=aim-start;d=d.sqrMagnitude>1e-6f?d.normalized:e0.nb;var again=e0.q-e0.nb*(Thick305*.5f+1f);var d2=aim-again;d2=d2.sqrMagnitude>1e-6f?d2.normalized:e0.nb;
       foreach(var (vn,rise) in Variants305)
       {
        runs++;var v=Probe305(cc,go,st,kc,12,start,start+d*ProbePush305,again,again+d2*ProbePush305,rise,root,skip,reach,out string note);tot[vn].Add(v);
        if(v!=Verdict305.Pass)lines.Add((v==Verdict305.Fail?"FAIL ":"INCONCLUSIVE ")+label+" | aimed past the rock "+vn+": "+note);
       }
      }
      a=b;
     }
    }
   }
   finally{Object.DestroyImmediate(go);foreach(var c in disabled)if(c!=null)c.enabled=true;Physics.SyncTransforms();}
   int fails=tot.Values.Sum(t=>t.fail),incs=tot.Values.Sum(t=>t.inc);string verdict=fails>0?"FAIL":incs>0?"INCONCLUSIVE":"PASS";
   var sb=new StringBuilder();
   sb.AppendLine("#305 joints-scene "+path+" | "+DateTime.Now.ToString("s")+" | segments305 "+version+" | cliff mask "+cm.file+" ("+cm.masked+" quads) | read only, nothing changed");
   sb.AppendLine("Automated Edit-mode CharacterController probes (the check-scene walker, not manual play): height="+F305(ccHeight,"F3")+" radius="+F305(ccRadius,"F3")+" step="+ccStep+" slope="+ccSlope);
   sb.AppendLine("per open side of a masked shell stretch: the last "+JointBack305+" open stations pushed "+ProbePush305+" m to the blocked side from "+ProbeStart305+" m on the player side, and one run aimed past the buried stretch.");
   sb.AppendLine("verdict: FAIL = feet past the shell centre line by > radius (a way round the shell end); PASS = stopped by the shell; INCONCLUSIVE = stopped by something else first (rock, slope) - the shell was not reached.");
   sb.AppendLine(verdict+" forest-cliff joints: open sides "+places.Count+", runs "+runs+", fail "+fails+", inconclusive "+incs+" (actor/body colliders disabled during probes="+disabled.Length+")");
   foreach(var kv in tot)sb.AppendLine("  "+kv.Key+": "+kv.Value);
   foreach(var p in places)sb.AppendLine("  joint "+p);
   foreach(var f in lines.Where(x=>x.StartsWith("FAIL")))sb.AppendLine(f);
   foreach(var f in lines.Where(x=>!x.StartsWith("FAIL")))sb.AppendLine(f);
   string file=O305+"/joints-"+Path.GetFileNameWithoutExtension(path)+".txt";File.WriteAllText(file,sb.ToString());
   return "joints "+path+": "+verdict+"; open sides "+places.Count+", runs "+runs+"; "+string.Join(", ",tot.Select(kv=>kv.Key+" "+kv.Value.pass+"/"+kv.Value.fail+"/"+kv.Value.inc))+" (pass/fail/inconclusive); nothing changed; "+Path.GetFullPath(file);
  }
  // Forest305.asset is ONE asset for the three scenes and build-scene rewrites it: keep a byte copy of every distinct state
  static string BackupSheet305()
  {
   string file=A305+"/Forest305.asset";if(!File.Exists(file))return "";
   string dir=O305+"/Before";Directory.CreateDirectory(dir);string sha=CliffCore308.ShaFile(Path.GetFullPath(file));
   string copy=dir+"/Forest305-"+(sha.Length>=12?sha.Substring(0,12):"nosha")+".asset";if(!File.Exists(copy))File.Copy(file,copy,false);
   return Path.GetFullPath(copy);
  }
  // plan-scene:<path>: the dry run of build-scene. The ACTIVE scene is measured as it is; nothing is opened, changed or saved.
  static string Plan305(string path)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refuse305("Edit mode only (Play is running)");
   if(!Scenes305.Contains(path))throw new Refuse305("not a #305 target: "+path+" (allowed: "+string.Join(", ",Scenes305)+")");
   var scene=SceneManager.GetActiveScene();if(scene.path!=path)throw new Refuse305("active scene is "+scene.path+", not "+path+" (plan-scene opens nothing)");
   string guard;try{PairGuard305();HeightGuard305(path);guard="OK";}catch(Refuse305 r){guard="build-scene would be REFUSED: "+r.Message;}
   var segs=N6Segments305(out string version,out int others);var forest=Forest305Input();var cm=Cliff305Now;
   var rootGo=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==Root305);var root=rootGo!=null?rootGo.transform:null;var skip=Skip305(Session292());
   var stations=segs.ToDictionary(g=>g,g=>Stations305(g));var joints=Joints305(segs,stations);var expect=new HashSet<string>();
   int chunks=0,nst=0,stMask=0,stNoGround=0;var lines=new List<string>();
   foreach(var g in segs)
   {
    var st=stations[g];int n=st.Length;nst+=n;float spacing=st[n-1].s/Mathf.Max(1,n-1);int K=Mathf.Max(1,Mathf.CeilToInt(st[n-1].s/Chunk305-1e-3f));
    int count=Enumerable.Range(0,K+1).Select(k=>k==K?n-1:Mathf.Clamp(Mathf.RoundToInt(k*Chunk305/Mathf.Max(1e-3f,spacing)),0,n-1)).Distinct().Count()-1;
    for(int k=0;k<count;k++)expect.Add(g.id+"_"+k+"_col");chunks+=count;int m=0,ng=0;
    foreach(var x in st){if(cm.Masked(x.q.x,x.q.y))m++;else if(!Ground305(x.q.x,x.q.y,root,skip,out _,out _))ng++;}
    stMask+=m;stNoGround+=ng;if(m>0||ng>0)lines.Add("  "+g.id+" stations "+n+" chunks "+count+": shell centre in the cliff mask "+m+", on no ground "+ng+" | ends "+string.Join("/",g.ends));
   }
   int arcs=0;foreach(var j in joints){if(j.kind=="arc"&&j.arc.Length>=2){expect.Add(j.name);arcs++;}expect.Add("E305_P"+j.name.Substring(6));}
   var now=new HashSet<string>();if(root!=null)foreach(var c in root.GetComponentsInChildren<Collider>(true))now.Add(c.name);
   var gone=now.Where(x=>!expect.Contains(x)).OrderBy(x=>x,StringComparer.Ordinal).ToList();var added=expect.Where(x=>!now.Contains(x)).OrderBy(x=>x,StringComparer.Ordinal).ToList();
   int cMask=0,cNo=0,cOk=0,cOff=0;float worst=0;
   foreach(var c in forest.candidates??Array.Empty<Cand305>())
   {
    if(c==null||c.Position==null||c.Position.Length<3)continue;
    if(cm.Masked(c.Position[0],c.Position[2])){cMask++;continue;}
    if(!Ground305(c.Position[0],c.Position[2],root,skip,out float gy,out _)){cNo++;continue;}
    cOk++;float dy=Mathf.Abs(gy-c.Position[1]);if(dy>1f)cOff++;worst=Mathf.Max(worst,dy);
   }
   var sb=new StringBuilder();string sceneName=Path.GetFileNameWithoutExtension(path);
   sb.AppendLine("#305 plan-scene "+path+" | "+DateTime.Now.ToString("s")+" | segments305 "+version+" | candidates "+forest.version+" | read only, nothing changed");
   sb.AppendLine("height guard: "+guard);
   sb.AppendLine("cliff mask: "+(cm.bits==null?"none (pre-cliff segment file)":cm.file+", "+cm.masked+" quads, sha256 "+cm.sha));
   sb.AppendLine("segments N6="+segs.Count+" (other kits skipped="+others+") length="+F305(segs.Sum(s=>s.length),"F1")+" m, stations "+nst+", shell chunks "+chunks+" + joint strips "+arcs+" + joint plugs "+joints.Count+" = "+(chunks+arcs+joints.Count)+" colliders");
   sb.AppendLine("joints="+joints.Count+": arc "+arcs+", cross "+joints.Count(j=>j.kind=="cross")+", flush "+joints.Count(j=>j.kind=="flush")+"; open ends="+segs.Sum(s=>s.ends.Count(e=>e=="open")));
   sb.AppendLine("shell stations in the cliff mask (rock stubs, face crossings)="+stMask+", on no ground outside the mask="+stNoGround);
   foreach(var l in lines)sb.AppendLine(l);
   sb.AppendLine("candidates "+(forest.candidates?.Length??0)+": in the cliff mask "+cMask+", no ground "+cNo+", on ground "+cOk+" (ground more than 1 m off the candidate y: "+cOff+", worst "+F305(worst)+" m) - before the tree / content / preserved-area rules of build-scene");
   sb.AppendLine("scene root now: "+(root==null?"no "+Root305+" root":now.Count+" colliders")+"; after build-scene "+expect.Count+": "+gone.Count+" names go, "+added.Count+" names are new");
   sb.AppendLine("go: "+string.Join(" ",gone));sb.AppendLine("new: "+string.Join(" ",added));
   File.WriteAllText(O305+"/plan-"+sceneName+".txt",sb.ToString());
   return "plan "+path+": segments305 "+version+", height guard "+guard+"; "+segs.Count+" N6 segments, "+chunks+" shell chunks + "+arcs+" joint strips + "+joints.Count+" plugs ("+(chunks+arcs+joints.Count)+" colliders; the root has "+now.Count+" now, "+gone.Count+" go, "+added.Count+" new); shell stations in the cliff mask "+stMask+
          ", on no ground "+stNoGround+"; candidates "+(forest.candidates?.Length??0)+": mask "+cMask+", no ground "+cNo+", ground "+cOk+"; nothing changed; "+Path.GetFullPath(O305+"/plan-"+sceneName+".txt");
  }

  // build/remove: Edit mode, whitelisted target, nothing unsaved; the target is then opened Single
  static Scene Open305(string path)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refuse305("Edit mode only (Play is running)");
   if(!Scenes305.Contains(path))throw new Refuse305("not a #305 target: "+path+" (allowed: "+string.Join(", ",Scenes305)+")");
   for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);if(s.isDirty)throw new Refuse305("scene "+s.path+" has unsaved changes - save or discard them first (the build saves the scene)");}
   return EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
  }
  // check/audit: Edit mode, the target already active and carrying the root (nothing is opened or saved)
  static (Scene scene,Transform root) Active305(string path)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refuse305("Edit mode only (Play is running)");
   var s=SceneManager.GetActiveScene();if(s.path!=path)throw new Refuse305("active scene is "+s.path+", not "+path);
   var r=s.GetRootGameObjects().FirstOrDefault(g=>g.name==Root305);if(r==null)throw new Refuse305("no "+Root305+" root in "+path+" (run build-scene first)");
   return (s,r.transform);
  }

  // ---------- inputs ----------

  sealed class Seg305{public string id,kit,realm,zone;public bool ea,closed;public int block;public Vector2[] pts;public float length;public string[] ends,notes;}
  [Serializable] class Cand305{public string Id,ClusterId,PrototypeId,kind;public float[] Position,Euler;public float Scale=1,u;}
  [Serializable] class Forest305File{public string version;public float band_m;public string[] prototypes;public Cand305[] candidates;}
  // one resampled station: line point p, travel tangent t, blocked-side normal nb (block * right normal (dz,-dx)), arc s, shell centre q
  struct Station305{public Vector2 p,t,nb,q;public float s;}

  static List<Seg305> Segments305(out string version)
  {
   var d=(Dictionary<string,object>)Json305.Parse(File.ReadAllText(E305+"/segments305.json"));version=JStr305(d,"version");
   var list=new List<Seg305>();
   foreach(var o in (List<object>)d["segments"])
   {
    var s=(Dictionary<string,object>)o;
    var pts=((List<object>)s["points"]).Select(q=>{var a=(List<object>)q;return new Vector2(Convert.ToSingle(a[0],CultureInfo.InvariantCulture),Convert.ToSingle(a[1],CultureInfo.InvariantCulture));}).ToArray();
    list.Add(new Seg305{id=JStr305(s,"id"),kit=JStr305(s,"kit"),realm=JStr305(s,"realm"),zone=JStr305(s,"zone"),ea=JBool305(s,"ea"),closed=JBool305(s,"closed"),
     block=s.TryGetValue("block",out var b)&&b!=null&&Convert.ToDouble(b,CultureInfo.InvariantCulture)<0?-1:1,pts=pts,
     length=s.TryGetValue("length_m",out var l)&&l!=null?Convert.ToSingle(l,CultureInfo.InvariantCulture):0,ends=JStrs305(s,"ends"),notes=JStrs305(s,"notes")});
   }
   return list;
  }
  static List<Seg305> N6Segments305(out string version,out int others){var all=Segments305(out version);var n6=all.Where(s=>s.kit=="N6"&&s.pts.Length>=2).ToList();others=all.Count-n6.Count;return n6;}
  static Forest305File Forest305Input()=>JsonUtility.FromJson<Forest305File>(File.ReadAllText(O305+"/forest305_candidates.json"));
  static string JStr305(Dictionary<string,object> d,string k)=>d.TryGetValue(k,out var v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):"";
  static bool JBool305(Dictionary<string,object> d,string k)=>d.TryGetValue(k,out var v)&&v is bool b&&b;
  static string[] JStrs305(Dictionary<string,object> d,string k)=>d.TryGetValue(k,out var v)&&v is List<object> l?l.Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture)).ToArray():Array.Empty<string>();

  // minimal JSON reader (nested arrays are outside JsonUtility): object -> Dictionary, array -> List, number -> double
  sealed class Json305
  {
   readonly string s;int i;
   Json305(string s){this.s=s;}
   public static object Parse(string text){var j=new Json305(text.TrimStart('﻿'));var v=j.Value();return v;}
   void W(){while(i<s.Length&&char.IsWhiteSpace(s[i]))i++;}
   bool Word(string w){if(string.CompareOrdinal(s,i,w,0,w.Length)!=0)return false;i+=w.Length;return true;}
   object Value()
   {
    W();char c=s[i];
    if(c=='{'){i++;var d=new Dictionary<string,object>();W();if(s[i]=='}'){i++;return d;}
     while(true){W();var k=Str();W();if(s[i++]!=':')throw new FormatException("':' expected at "+i);d[k]=Value();W();char e=s[i++];if(e=='}')return d;if(e!=',')throw new FormatException("',' expected at "+i);}}
    if(c=='['){i++;var l=new List<object>();W();if(s[i]==']'){i++;return l;}
     while(true){l.Add(Value());W();char e=s[i++];if(e==']')return l;if(e!=',')throw new FormatException("',' expected at "+i);}}
    if(c=='"')return Str();
    if(Word("true"))return true;if(Word("false"))return false;if(Word("null"))return null;
    if(Word("NaN"))return double.NaN;if(Word("Infinity"))return double.PositiveInfinity;if(Word("-Infinity"))return double.NegativeInfinity;
    int st=i;while(i<s.Length&&"+-0123456789.eE".IndexOf(s[i])>=0)i++;
    if(st==i)throw new FormatException("bad JSON value at "+i);
    return double.Parse(s.Substring(st,i-st),NumberStyles.Float,CultureInfo.InvariantCulture);
   }
   string Str()
   {
    i++;var sb=new StringBuilder();
    while(s[i]!='"')
    {
     char c=s[i++];if(c!='\\'){sb.Append(c);continue;}
     char e=s[i++];
     switch(e){case 'n':sb.Append('\n');break;case 't':sb.Append('\t');break;case 'r':sb.Append('\r');break;case 'b':sb.Append('\b');break;case 'f':sb.Append('\f');break;
      case 'u':sb.Append((char)Convert.ToInt32(s.Substring(i,4),16));i+=4;break;default:sb.Append(e);break;}
    }
    i++;return sb.ToString();
   }
  }

  // ---------- line geometry ----------

  static Vector2 At305(Vector2[] P,float[] cum,float s)
  {
   for(int i=1;i<P.Length;i++)if(cum[i]>=s||i==P.Length-1){float d=Mathf.Max(1e-5f,cum[i]-cum[i-1]);return Vector2.Lerp(P[i-1],P[i],Mathf.Clamp01((s-cum[i-1])/d));}
   return P[0];
  }
  // stations every <= 1.5 m (both ends included); tangent from +-0.75 m so corners get a mitred normal
  static Station305[] Stations305(Seg305 g,float step=Step305)
  {
   var P=g.pts;if(g.closed&&P.Length>2&&P[0]!=P[P.Length-1])P=P.Concat(new[]{P[0]}).ToArray();
   var cum=new float[P.Length];for(int i=1;i<P.Length;i++)cum[i]=cum[i-1]+Vector2.Distance(P[i-1],P[i]);float L=cum[cum.Length-1];
   int n=Mathf.Max(1,Mathf.CeilToInt(L/step));var st=new Station305[n+1];var prev=Vector2.up;
   for(int k=0;k<=n;k++)
   {
    float s=L*k/n;var p=At305(P,cum,s);var t=At305(P,cum,Mathf.Min(L,s+.75f))-At305(P,cum,Mathf.Max(0,s-.75f));
    t=t.sqrMagnitude>1e-8f?t.normalized:prev;prev=t;var nb=new Vector2(t.y,-t.x)*g.block;
    st[k]=new Station305{p=p,t=t,nb=nb,q=p+nb*Offset305,s=s};
   }
   return st;
  }
  static float SegDist305(Vector2 p,Vector2 a,Vector2 b){var d=b-a;float t=d.sqrMagnitude<1e-8f?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);return Vector2.Distance(p,a+d*t);}
  static float LineDist305(Vector2 p,Vector2[] pts){float best=float.PositiveInfinity;for(int i=1;i<pts.Length;i++)best=Mathf.Min(best,SegDist305(p,pts[i-1],pts[i]));return best;}
  static Vector2 Rotate305(Vector2 v,float deg){float r=deg*Mathf.Deg2Rad,c=Mathf.Cos(r),s=Mathf.Sin(r);return new Vector2(v.x*c-v.y*s,v.x*s+v.y*c);}
  static bool Cross305(Vector2 a,Vector2 b,Vector2 c,Vector2 d,out Vector2 x)
  {
   x=default;var r=b-a;var s=d-c;float den=r.x*s.y-r.y*s.x;if(Mathf.Abs(den)<1e-8f)return false;
   var w=c-a;float t=(w.x*s.y-w.y*s.x)/den,u=(w.x*r.y-w.y*r.x)/den;if(t<0||t>1||u<0||u>1)return false;x=a+r*t;return true;
  }

  // ---------- joints ----------

  // two segment ends on one line point (<= 5 cm). Each shell stops 1 m inside its own blocked side, so a convex joint leaves a
  // 0.8-1.4 m opening (review 305): "arc" = a joint strip E305_J<a>_<b>_col on radius Offset305 around the joint point from A's end
  // normal to B's (<= 20 deg a station); "cross" = the stubs already intersect (concave), no mesh; "flush" = ends coincide (< 2 cm).
  // chain = A's last 12 stations (toward the joint) + arc | crossing + B's first 12: one simple blocked-side curve for the probes.
  sealed class Joint305{public string name,kind,note;public Seg305 a,b;public Vector2 p,bis;public float angle,gap;public Station305[] arc,chain;public int k,ka,kb;}
  static Station305[] Toward305(Station305[] st,bool atEnd,int n){var o=atEnd?st.Skip(Mathf.Max(0,st.Length-n)).ToArray():st.Take(n).Reverse().ToArray();return o;}
  static Vector2 EndPoint305(Seg305 g,bool atEnd)=>atEnd?g.pts[g.pts.Length-1]:g.pts[0];
  static string Short305(string id)=>id.StartsWith("E305_")?id.Substring(5):id;
  static List<Joint305> Joints305(List<Seg305> segs,Dictionary<Seg305,Station305[]> stations)
  {
   var ends=new List<(Seg305 g,bool atEnd)>();foreach(var g in segs)if(!g.closed){ends.Add((g,false));ends.Add((g,true));}
   var list=new List<Joint305>();var names=new HashSet<string>();
   for(int i=0;i<ends.Count;i++)for(int j=i+1;j<ends.Count;j++)
   {
    var (ga,ea)=ends[i];var (gb,eb)=ends[j];var pa=EndPoint305(ga,ea);var pb=EndPoint305(gb,eb);if(Vector2.Distance(pa,pb)>.05f)continue;
    var A=Toward305(stations[ga],ea,12);var B=Toward305(stations[gb],eb,12).Reverse().ToArray();
    var J=new Joint305{a=ga,b=gb,p=(pa+pb)*.5f,gap=Vector2.Distance(A[A.Length-1].q,B[0].q)};
    string name="E305_J"+Short305(ga.id)+"_"+Short305(gb.id);for(int n=1;!names.Add(name);n++)name="E305_J"+Short305(ga.id)+"_"+Short305(gb.id)+"_"+n;J.name=name+"_col";
    var nA=A[A.Length-1].nb;var nB=B[0].nb;J.angle=Vector2.SignedAngle(nA,nB);
    var outA=(A[A.Length-1].q-A[Mathf.Max(0,A.Length-2)].q);outA=outA.sqrMagnitude>1e-8f?outA.normalized:A[A.Length-1].t;
    var departB=B.Length>1?(B[1].q-B[0].q):B[0].t;departB=departB.sqrMagnitude>1e-8f?departB.normalized:outA;
    // consistent blocked sides turn with the line (nB = nA rotated by the line's turn); > 90 deg off = the block signs disagree
    float turn=Vector2.SignedAngle(outA,departB);J.note=Mathf.Abs(Mathf.DeltaAngle(J.angle,turn))>90f?"SIDE FLIP: block signs of "+ga.id+"/"+gb.id+" disagree (fix segments305.json)":"";
    // +-180 (hairpin / flip): the sign is numerical noise -> go round the far side of the joint (arc midpoint along A's outward tangent)
    if(Mathf.Abs(J.angle)>175f){if(Vector2.Dot(Rotate305(nA,J.angle*.5f),outA)<0)J.angle=-J.angle;if(J.note=="")J.note="hairpin";}
    J.bis=Rotate305(nA,J.angle*.5f);
    // concave: A's stub runs into B's (search back from the joint) -> trim both at the crossing
    int ci=-1,cj=-1;Vector2 X=default;
    if(J.gap>=.02f)for(int u=A.Length-2;u>=0&&ci<0;u--)for(int v=0;v+1<B.Length;v++)if(Cross305(A[u].q,A[u+1].q,B[v].q,B[v+1].q,out X)){ci=u;cj=v;break;}
    if(J.gap<.02f)
    {
     J.kind="flush";J.arc=Array.Empty<Station305>();J.chain=A.Concat(B.Skip(1)).ToArray();J.k=A.Length-1;J.ka=Mathf.Max(0,J.k-2);J.kb=Mathf.Min(J.chain.Length-1,J.k+2);
    }
    else if(ci>=0)
    {
     J.kind="cross";var nx=(A[ci].nb+B[cj].nb);nx=nx.sqrMagnitude>1e-6f?nx.normalized:J.bis;
     J.arc=Array.Empty<Station305>();J.chain=A.Take(ci+1).Concat(new[]{new Station305{p=J.p,q=X,nb=nx,t=A[ci].t}}).Concat(B.Skip(cj+1)).ToArray();
     J.k=ci+1;J.ka=Mathf.Max(0,ci-1);J.kb=Mathf.Min(J.chain.Length-1,ci+3);
    }
    else
    {
     J.kind="arc";int m=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(J.angle)/20f));J.arc=new Station305[m+1];
     for(int w=0;w<=m;w++)
     {
      var dir=Rotate305(nA,J.angle*w/m);var t=Mathf.Abs(J.angle)<1f?outA:Rotate305(dir,90f*Mathf.Sign(J.angle));
      J.arc[w]=new Station305{p=J.p,q=J.p+dir*Offset305,nb=dir,t=t,s=w*Offset305*Mathf.Abs(J.angle)*Mathf.Deg2Rad/m};
     }
     J.chain=A.Concat(J.arc).Concat(B).ToArray();J.k=A.Length+m/2;J.ka=Mathf.Max(0,A.Length-3);J.kb=Mathf.Min(J.chain.Length-1,A.Length+m+3);
    }
    list.Add(J);
   }
   return list;
  }

  // ---------- ground ----------

  // physical ground under xz: y = highest upward-facing (normal.y > .5) hit on terrain colliders (any support if none is terrain);
  // top = highest walkable support of any kind (Support297 rule: rocks, wall tops, decks) and the collider that gave it.
  // Colliders under `ignore` (the #305 root), character controllers and `skip` (actors, player body) never count.
  // #308: no ground at all on a quad of the cliff mask (Cliff305Now, loaded per command from segments305.json).
  static bool Ground305(float x,float z,Transform ignore,HashSet<Collider> skip,out float y,out bool terrain)
  {
   // #308: forest, cover fill, plugs, probe starts, NavMesh samples - no ground on any quad of the cliff mask
   if(Cliff305Now.Masked(x,z)){y=float.NegativeInfinity;terrain=false;return false;}
   return Ground305(x,z,ignore,skip,out y,out terrain,out _,out _);
  }
  static bool Ground305(float x,float z,Transform ignore,HashSet<Collider> skip,out float y,out bool terrain,out float top,out Collider topBy)
  {
   float best=float.NegativeInfinity;top=float.NegativeInfinity;topBy=null;
   // #308: shell columns (Columns305, the only caller with 8 arguments) - a masked quad (cliff face, foreign cliff top) is no ground,
   // except its unchanged tile triangle (review: without it the shell top next to a face ignored the ground a player stands on)
   if(Cliff305Now.MaskedColumn(x,z)){y=float.NegativeInfinity;terrain=false;return false;}
   foreach(var h in Physics.RaycastAll(new Vector3(x,2000f,z),Vector3.down,4000f,~0,QueryTriggerInteraction.Ignore))
   {
    var c=h.collider;if(h.normal.y<=.5f||c is CharacterController)continue;
    if(ignore!=null&&c.transform.IsChildOf(ignore))continue;if(skip!=null&&skip.Contains(c))continue;
    if(h.point.y>top){top=h.point.y;topBy=c;}if(h.point.y>best&&Terrain305(c))best=h.point.y;
   }
   terrain=!float.IsNegativeInfinity(best);y=terrain?best:top;return !float.IsNegativeInfinity(y);
  }
  // terrain = TerrainCollider or a Terrain*/ *Surface* object or parent ("Ground" is not: house_Re_Ground, GroundLitter, LowGroundFill)
  static bool Terrain305(Collider c)
  {
   if(c is TerrainCollider)return true;
   for(var t=c.transform;t!=null;t=t.parent){var n=t.name;if(n.Contains("Terrain")||n.Contains("Surface"))return true;}
   return false;
  }
  // the highest walkable support within [from.y-down, from.y+up] (probe hover / cover checks)
  static bool Support305(Vector3 from,float up,float down,Transform ignore,HashSet<Collider> skip,out float y)
  {
   y=float.NegativeInfinity;
   foreach(var h in Physics.RaycastAll(from+Vector3.up*up,Vector3.down,up+down,~0,QueryTriggerInteraction.Ignore))
   {var c=h.collider;if(h.normal.y<=.5f||c is CharacterController||(ignore!=null&&c.transform.IsChildOf(ignore))||(skip!=null&&skip.Contains(c)))continue;if(h.point.y>y)y=h.point.y;}
   return !float.IsNegativeInfinity(y);
  }
  // encounter actors and the resting player body are gameplay, not ground
  static HashSet<Collider> Skip305(WorldMacroPlaytestSession session)
  {
   var set=new HashSet<Collider>();if(session==null)return set;
   foreach(var a in session.Actors??Array.Empty<Oheangbu.App.Prologue.PrologueEncounter>())if(a!=null)foreach(var c in a.GetComponentsInChildren<Collider>(true))set.Add(c);
   if(session.Walker!=null&&session.Walker.Body!=null)foreach(var c in session.Walker.Body.GetComponentsInChildren<Collider>(true))set.Add(c);
   return set;
  }
  // offline height field of the main scene's FinalSurface (<f4, 1501 x 1001, 4 m, h[z/4, x/4], bilinear) - comparison only
  static Func<float,float,float> Offline305()
  {
   const int H=1501,W=1001;const float C=4f;
   if(!File.Exists(Height305))return null;var bytes=File.ReadAllBytes(Height305);if(bytes.Length!=H*W*4)return null;
   var h=new float[H*W];Buffer.BlockCopy(bytes,0,h,0,bytes.Length);
   return (x,z)=>{float j=Mathf.Clamp(x/C,0,W-1.001f),i=Mathf.Clamp(z/C,0,H-1.001f);int j0=(int)j,i0=(int)i;float fj=j-j0,fi=i-i0;
    return h[i0*W+j0]*(1-fj)*(1-fi)+h[i0*W+j0+1]*fj*(1-fi)+h[(i0+1)*W+j0]*(1-fj)*fi+h[(i0+1)*W+j0+1]*fj*fi;};
  }
  static string Stats305(List<float> v)
  {
   if(v.Count==0)return "n=0";var abs=v.Select(Mathf.Abs).OrderBy(x=>x).ToList();
   return "n="+v.Count+" mean="+v.Average().ToString("F3",CultureInfo.InvariantCulture)+" mean|d|="+abs.Average().ToString("F3",CultureInfo.InvariantCulture)+
          " p95|d|="+abs[Mathf.Clamp(Mathf.CeilToInt(abs.Count*.95f)-1,0,abs.Count-1)].ToString("F3",CultureInfo.InvariantCulture)+" max|d|="+abs[abs.Count-1].ToString("F3",CultureInfo.InvariantCulture);
  }
  static string F305(float v,string f="F2")=>v.ToString(f,CultureInfo.InvariantCulture);

  // 2D bucket grid of world points (XZ)
  sealed class Grid305
  {
   readonly float cell;readonly Dictionary<long,List<Vector3>> map=new Dictionary<long,List<Vector3>>();public int Count;
   public Grid305(float cell){this.cell=cell;}
   static long Key(int x,int z)=>((long)x<<32)^(uint)z;
   public void Add(Vector3 p){long k=Key(Mathf.FloorToInt(p.x/cell),Mathf.FloorToInt(p.z/cell));if(!map.TryGetValue(k,out var l))map[k]=l=new List<Vector3>();l.Add(p);Count++;}
   public IEnumerable<Vector3> Near(Vector2 c,float r)
   {
    int x0=Mathf.FloorToInt((c.x-r)/cell),x1=Mathf.FloorToInt((c.x+r)/cell),z0=Mathf.FloorToInt((c.y-r)/cell),z1=Mathf.FloorToInt((c.y+r)/cell);
    for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)if(map.TryGetValue(Key(x,z),out var l))foreach(var p in l)if((new Vector2(p.x,p.z)-c).sqrMagnitude<=r*r)yield return p;
   }
   public bool Any(Vector2 c,float r)=>Near(c,r).Any();
  }

  // ---------- status / remove ----------

  static string Status305()
  {
   var sb=new StringBuilder();
   try
   {
    var n6=N6Segments305(out string version,out int others);
    sb.AppendLine("segments305.json "+version+": N6 "+n6.Count+" segments, "+F305(n6.Sum(s=>s.length)/1000f,"F2")+" km (other kits skipped: "+others+"), open ends "+n6.Sum(s=>s.ends.Count(e=>e=="open"))+
                  ", realms "+string.Join(",",n6.GroupBy(s=>s.realm).Select(g=>g.Key+"="+g.Count())));
   }
   catch(Exception e){sb.AppendLine("segments305.json: "+e.Message);}
   try
   {
    var f=Forest305Input();
    sb.AppendLine("forest305_candidates.json "+f.version+": "+(f.candidates?.Length??0)+" candidates (tree "+(f.candidates?.Count(c=>c.kind=="tree")??0)+", shrub "+(f.candidates?.Count(c=>c.kind=="shrub")??0)+"), prototypes "+string.Join(",",f.prototypes??Array.Empty<string>()));
   }
   catch(Exception e){sb.AppendLine("forest305_candidates.json: "+e.Message);}
   sb.AppendLine("offline height field "+(File.Exists(Height305)?"present":"missing")+" ("+Height305+", sha256 "+CliffHeight308.Sha(Height305)+")");
   var cm=Cliff305Now;
   sb.AppendLine("cliff mask: "+(cm.bits==null?"none (pre-cliff segment file "+cm.version+")":cm.file+", "+cm.masked+" quads, sha256 "+cm.sha)+
                 (cm.need==""?"":"; requires height sha256 "+cm.need+(string.Equals(cm.need,CliffHeight308.Sha(Height305),StringComparison.OrdinalIgnoreCase)?" = the active scene's height":" - NOT the active scene's height (build-scene refuses)")));
   var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(A305+"/Forest305.asset");
   sb.AppendLine("Forest305.asset: "+(sheet==null?"absent":sheet.FixedPlacements.Length+" placements, "+sheet.Prototypes.Length+" prototypes"));
   var scene=SceneManager.GetActiveScene();var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==Root305);
   sb.Append("active scene "+scene.path+(scene.isDirty?" (dirty)":"")+(Scenes305.Contains(scene.path)?" [target]":" [not a target]")+": ");
   if(root==null)sb.AppendLine("no "+Root305+" root");
   else
   {
    var shell=root.transform.Find("Shell");var obs=root.transform.Find("Obstacles");var fr=root.transform.Find("Forest305_Renderer");
    sb.AppendLine(Root305+" root: shells "+(shell!=null?shell.childCount:0)+", carve boxes "+(obs!=null?obs.childCount:0)+", forest renderer "+(fr!=null&&fr.GetComponent<CompactRebuildArtRenderer>()!=null?"yes":"no")+", active "+root.activeSelf);
   }
   sb.Append("play="+EditorApplication.isPlaying);
   return sb.ToString();
  }

  static string Remove305(string path)
  {
   var scene=Open305(path);var roots=scene.GetRootGameObjects().Where(g=>g.name==Root305).ToArray();
   if(roots.Length==0)return "nothing to remove: no "+Root305+" root in "+path+" (scene not saved)";
   string backup=Backup305(path);foreach(var g in roots)Object.DestroyImmediate(g);
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   return "removed "+roots.Length+" "+Root305+" root(s) from "+path+"; Forest305.asset and shell meshes kept; backup "+backup;
  }
  static string Backup305(string path)
  {
   string dir=O305+"/Before";Directory.CreateDirectory(dir);
   string file=dir+"/"+Path.GetFileNameWithoutExtension(path)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity";File.Copy(path,file,true);return Path.GetFullPath(file);
  }
 }
}
