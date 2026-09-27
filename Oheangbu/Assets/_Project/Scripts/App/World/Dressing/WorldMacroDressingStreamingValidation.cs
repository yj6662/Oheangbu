#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
namespace Oheangbu.App.World.Dressing
{
 public sealed partial class WorldMacroDressingRenderer
 {
  public bool TryDiagnosticGroundHeight(float x,float z,out float height)
  {
   // Absolute terrain height wins; never add SurfaceDeformation a second time.
   if(Oheangbu.Data.World.WorldMacroTerrain.TryFinalSurfaceHeight(Sheet!=null?Sheet.Geography:null,x,z,out height))return true;
   return TrySurfaceGroundHeight(x,z,out height);
  }
  [Serializable] class PartitionRow {public int layer,whole,partitioned,duplicates,mismatches;public bool pass;}
  [Serializable] class PartitionReport {public string status,scope="One current observer cell, each procedural layer: whole 256m generation versus union of sixteen 64m jobs; compare IDs, position, scale, prototype and matrix. This does not certify every world cell or terrain collision.";public int cellX,cellZ;public PartitionRow[] layers;}
  public string ValidateStreamingPartition()
  {
   if(Application.isPlaying)throw new InvalidOperationException("Edit-only partition audit, not a prewarm for performance");
   ResetCache();var eye=Observer!=null?Observer.transform.position:Sheet.Cells[0].Centre;var cell=Sheet.Cells[0];float closest=float.MaxValue;
   foreach(var c in Sheet.Cells){float d=FlatDistanceSquared(c.Centre,eye);if(d<closest){closest=d;cell=c;}}
   var rows=new List<PartitionRow>();Vector2 origin=Origin(cell);bool pass=true;
   for(int layer=0;layer<6;layer++)
   {
    int category=layer==0?0:layer==1?1:layer==2?3:2;bool far=layer==0,cover=layer==5,low=layer==4;
    var whole=Generate(cell,category,far,cover,low);var all=new Dictionary<long,Item>();var row=new PartitionRow{layer=layer,whole=whole.Length};
    for(int z=0;z<4;z++)for(int x=0;x<4;x++)foreach(var value in GenerateSteps(cell,category,far,cover,low,origin+new Vector2(x*64,z*64),origin+new Vector2((x+1)*64,(z+1)*64)))
     if(value.HasValue){var item=value.Value;if(all.ContainsKey(item.Id))row.duplicates++;else all.Add(item.Id,item);}
    row.partitioned=all.Count;
    foreach(var a in whole)if(!all.TryGetValue(a.Id,out var b)||a.Prototype!=b.Prototype||a.Position!=b.Position||a.Scale!=b.Scale||a.Matrix!=b.Matrix)row.mismatches++;
    row.pass=row.whole==row.partitioned&&row.duplicates==0&&row.mismatches==0;pass&=row.pass;rows.Add(row);
   }
   return JsonUtility.ToJson(new PartitionReport{status=pass?"PASS_PARTITION_IDENTITY":"FAIL",cellX=cell.X,cellZ=cell.Z,layers=rows.ToArray()},true);
  }
 }
}
#endif
