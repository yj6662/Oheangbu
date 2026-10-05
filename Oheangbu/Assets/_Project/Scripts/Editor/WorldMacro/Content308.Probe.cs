using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-16 relayout, BUILD_BRIEF section 3 (P1 - P4) [TEST]: read-only measurements at given XZ places of one scene, before any apply.
 //   probe:<scene>:<x>,<z>[;<x>,<z>...]
 // Per place: the physical ground height and slope (the same Ground308 the content pass uses), the nearest NavMesh point within
 // 2 m and within 3 m, the nearest layout road, and how far a standing capsule (ground.feet_probe_radius_m / feet_probe_height_m of
 // content308_scene.json) is from the nearest collider that is not terrain, not a session actor and not a preview object.
 // Nothing is written to the scene or to an asset; the answer is also saved to Art/Playtest308/Pacing/relayout308_probe_<scene>.txt.
 public static partial class Content308
 {
  // the capsule is grown in these steps until something is inside it; the answer is "between the last two steps"
  static readonly float[] ProbeGaps308={0.15f,0.3f,0.6f,0.9f,1.2f,2f,3f,5f,8f};

  static string Probe308(string arg)
  {
   int cut=arg.LastIndexOf(':');
   if(cut<=0||cut==arg.Length-1)throw new Refuse308("use probe:<scene>:<x>,<z>[;<x>,<z>...]");
   string scenePath=SceneArg308(arg.Substring(0,cut));
   var places=new List<Vector2>();
   foreach(var token in arg.Substring(cut+1).Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
   {
    var xz=token.Split(',');
    if(xz.Length!=2||!float.TryParse(xz[0].Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out float x)||!float.TryParse(xz[1].Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out float z))
     throw new Refuse308("probe place '"+token+"' is not <x>,<z>");
    places.Add(new Vector2(x,z));
   }
   if(places.Count==0)throw new Refuse308("probe needs at least one <x>,<z>");
   var scene=Open308(scenePath);var session=Session308(scene);var t=Targets308[scene.path];
   var layout=Load308<Oheangbu.Data.World.CompactWorldLayoutSO>(t.layout);
   PrepareGround308(scene,session);
   var data=SceneData308(out _);var g=Req308(data,"ground");
   float pr=Num308(g,"feet_probe_radius_m"),ph=Num308(g,"feet_probe_height_m");
   var sb=new StringBuilder("probe "+scene.path+" sha "+Short308(Harness303.Sha(Path.Combine(Harness303.RepoRoot,"Oheangbu",scene.path)))+" (read only)\n");
   foreach(var p in places)
   {
    if(!Ground308(p.x,p.y,out var at,out float slope)){sb.AppendLine("  ("+F308(p.x,"F1")+", "+F308(p.y,"F1")+") NO GROUND");continue;}
    bool n2=NavMesh.SamplePosition(at,out var h2,2f,NavMesh.AllAreas),n3=NavMesh.SamplePosition(at,out var h3,3f,NavMesh.AllAreas);
    float rd=RoadDistance308(layout,p,out string road,out float width);
    string inside=null;float last=0f,found=-1f;
    foreach(float gap in ProbeGaps308)
    {
     float r=pr+gap;
     var hit=Physics.OverlapCapsule(at+Vector3.up*(r+.1f),at+Vector3.up*Mathf.Max(r+.1f,ph-pr),r,~0,QueryTriggerInteraction.Ignore)
      .Where(col=>!Terrain308(col)&&!skip308.Contains(col)&&!Preview308(col.transform)&&!(col is CharacterController)).Select(col=>Harness303.PathOf(col.transform)).Distinct().ToArray();
     if(hit.Length>0){found=gap;inside=string.Join(", ",hit.Take(3));break;}
     last=gap;
    }
    sb.AppendLine("  ("+F308(p.x,"F1")+", "+F308(p.y,"F1")+") ground y "+F308(at.y)+" slope "+F308(slope,"F1")+"°"
     +" | NavMesh 2 m "+(n2?"yes ("+F308(Vector3.Distance(at,h2.position))+" m)":"NO")+", 3 m "+(n3?"yes ("+F308(Vector3.Distance(at,h3.position))+" m)":"NO")
     +" | road "+road+" "+F308(rd,"F1")+" m (width "+F308(width,"F1")+")"
     +" | capsule gap "+(found<0f?"> "+F308(last,"F1")+" m (nothing inside)":F308(last,"F2")+" .. "+F308(found,"F2")+" m ("+inside+")"));
   }
   Directory.CreateDirectory(Out308);string file=Path.Combine(Out308,"relayout308_probe_"+Key308(scene.path)+".txt");File.WriteAllText(file,sb.ToString());
   return sb.Append("  report "+file+" | scene dirty="+scene.isDirty).ToString();
  }
 }
}
