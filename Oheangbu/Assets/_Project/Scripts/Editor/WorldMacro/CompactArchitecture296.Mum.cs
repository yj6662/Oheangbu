using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void ConfigureMumSurface296(List<string> report)
  {
   var profile=Session292().MumBridgeProfile;
   if(profile==null||!AssetDatabase.GetAssetPath(profile).StartsWith(A296+"/",StringComparison.Ordinal))throw new InvalidOperationException("296 private Mum profile required");
   var source=CrossingSource(StoneFloor296);var materials=source.Parts.Select(p=>p.material).Distinct().ToArray();
   var pieces=new List<Mesh>();var combine=new List<CombineInstance>();
   try
   {
    float scale=profile.Width/source.Bounds.size.x;
    foreach(var material in materials)
    {
     var piece=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
     piece.CombineMeshes(source.Parts.Where(p=>p.material==material).Select(p=>new CombineInstance{mesh=p.mesh,subMeshIndex=p.submesh,transform=Matrix4x4.Scale(Vector3.one*scale)*p.matrix}).ToArray(),true,true);
     pieces.Add(piece);combine.Add(new CombineInstance{mesh=piece,transform=Matrix4x4.identity});
    }
    var mesh=Asset296("Meshes/MumSourceStoneTile.asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.CombineMeshes(combine.ToArray(),false,true);mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    profile.SurfaceTile=mesh;profile.SurfaceMaterials=materials.Select(CrossingMaterial296).ToArray();profile.SurfaceTileLength=mesh.bounds.size.z;EditorUtility.SetDirty(profile);
    report.Add("Mum appearance: optional KCISA stone paving derived from original source mesh, shared 1.2s formation; support/lifetime/payment/unlock unchanged. Source295 profiles retain null SurfaceTile.");
   }
   finally{foreach(var piece in pieces)UnityEngine.Object.DestroyImmediate(piece);}
  }
 }
}
