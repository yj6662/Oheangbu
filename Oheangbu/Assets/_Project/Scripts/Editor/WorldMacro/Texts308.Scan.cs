using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
 // D308-117 (SPEC-FIELD-FIRST-HOUR-308 §8.2): read-only search for a place a point row can stand on, when every candidate of
 // places[] is refused. scan:<scene> <pointId> [radius_m=40] [step_m=2]: a grid around the row's first place, each cell read
 // with the same Ground + slope + Clear rules Pick uses; the passing cells nearest the first place come back (open sky first).
 // Nothing is written - the chosen cells go into places[] of texts308.json by hand (or by the patch script that owns the row).
 public static partial class Texts308
 {
  static string Scan(string arg)
  {
   var parts=arg.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
   if(parts.Length<2)throw new Refuse("scan:<scene> <pointId> [radius_m] [step_m]");
   float radius=parts.Length>2?float.Parse(parts[2],System.Globalization.CultureInfo.InvariantCulture):40f;
   float step=parts.Length>3?float.Parse(parts[3],System.Globalization.CultureInfo.InvariantCulture):2f;
   if(radius<=0||radius>200||step<.5f)throw new Refuse("radius 0 .. 200 m, step >= 0.5 m");
   var cfg=Load();var t=TargetOf(cfg,parts[0]);
   var spec=Arr(cfg.J,"points").FirstOrDefault(p=>Str(p,"id")==parts[1]);
   if(spec==null)throw new Refuse("texts308.json has no point row '"+parts[1]+"'");
   var first=Arr(spec,"places").FirstOrDefault();if(first==null)throw new Refuse(parts[1]+" has no places[]");
   float cx=Num(first,"x"),cz=Num(first,"z"),maxSlope=Num(spec,"max_slope_deg");
   var scene=Open(t);var session=Session(scene);var content=Content(t,session);
   PrepareGround(cfg,scene,session);
   var pass=new List<(float d,Vector3 p,float slope,float cover,string coverName)>();int cells=0,noGround=0,steep=0,blocked=0;
   for(float dx=-radius;dx<=radius+1e-3f;dx+=step)for(float dz=-radius;dz<=radius+1e-3f;dz+=step)
   {
    float d=Mathf.Sqrt(dx*dx+dz*dz);if(d>radius)continue;cells++;
    if(!Ground(cx+dx,cz+dz,out var p,out float slope,out float cover,out string coverName)){noGround++;continue;}
    if(slope>maxSlope){steep++;continue;}
    if(!Clear(cfg,spec,content,p,new List<string>())){blocked++;continue;}
    pass.Add((d,p,slope,cover,coverName));
   }
   var sb=new StringBuilder("scan "+parts[1]+" around ("+F(cx,"F1")+", "+F(cz,"F1")+") r "+F(radius,"F0")+" m step "+F(step,"F1")+" m on "+t.Scene+"\n");
   sb.AppendLine("  cells "+cells+": pass "+pass.Count+", no ground "+noGround+", slope > "+F(maxSlope,"F0")+"° "+steep+", clearance FAIL "+blocked);
   foreach(var q in pass.OrderBy(q=>float.IsPositiveInfinity(q.cover)?0:1).ThenBy(q=>q.d).Take(16))
    sb.AppendLine("  ("+F(q.p.x,"F1")+", "+F(q.p.z,"F1")+") y "+F(q.p.y)+" d "+F(q.d,"F1")+" m slope "+F(q.slope,"F1")+"°"+(float.IsPositiveInfinity(q.cover)?" open sky":" cover "+F(q.cover)+" m ("+q.coverName+")"));
   if(scene.isDirty)sb.AppendLine("  WARN the scene is dirty after a read-only pass (bug): reload it");
   return sb.ToString().TrimEnd();
  }
 }
}
