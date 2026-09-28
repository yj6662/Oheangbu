using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Material InteriorMaterial296(Material source,string module,string realm)
  {
   bool plaster=module.EndsWith("SM_W_Intwhite.prefab",StringComparison.Ordinal);
   bool timber=module.Contains("/SM_R_Beam_")||module.Contains("/Building_Pillars/");
   bool trim=module.Contains("SM_R_Dancheong")||module.Contains("Building_Walls/WallSet");
   if(!plaster&&!timber&&!trim)return source;
   string category=plaster?"Plaster":timber?"Timber":"Trim";
   string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
   var material=Asset296("Materials/Venues/Interior_"+realm+"_"+category+"_"+id+".mat",()=>new Material(source));
   material.CopyPropertiesFromMaterial(source);
   bool arsenal=realm=="cheolong";
   material.SetColor("_BaseColor",plaster?(arsenal?new Color(.66f,.64f,.57f):new Color(.84f,.79f,.65f)):timber?(arsenal?new Color(.54f,.42f,.31f):new Color(.87f,.69f,.61f)):(arsenal?new Color(.64f,.62f,.52f):new Color(.98f,.90f,.73f)));
   material.SetFloat("_Saturation",arsenal?.14f:.46f);material.SetFloat("_AmbientFloor",.46f);material.name="296_"+realm+"_"+category+"_"+source.name;EditorUtility.SetDirty(material);return material;
  }
  static void UpperInfill296(VenueBatch296 batch,Vector3 position,float width,float start,float height,float yaw)
  {
   // One clerestory below the roof, with broad quiet infill and structural rails below it.
   // Intwhite is the owned plain paper/plaster slab, previewed without handles or door frames.
   float sill=height-2.15f,top=height-.25f;
   void Panel(float from,float to)
   {
    if(to<=from+.01f)return;
    int count=Mathf.CeilToInt((to-from)/3.33f);float step=(to-from)/count;
    for(int i=0;i<count;i++)batch.Add(Haeng296+"SM_W_Intwhite.prefab",position+Vector3.up*(from+i*step),new Vector3(.23f,step,width),yaw+90,false);
   }
   Panel(start,sill);batch.Add(Jeju296+"Building_Walls/WallSetC.prefab",position+Vector3.up*sill,new Vector3(width,1.26f,.23f),yaw,false);Panel(sill+1.26f,top);
   foreach(float y in new[]{start,height*.56f,sill,top-.19f})
    if(y>=start-.01f)batch.Add(Haeng296+"SM_R_Beam_19.prefab",position+Vector3.up*y,new Vector3(width,.19f,.29f),yaw,false);
   // Short upright framing prevents a tall unbroken plaster panel. It stays in the wall thickness.
   batch.Add(Haeng296+"SM_P_Wood_3.prefab",position+Vector3.up*start,new Vector3(.14f,sill-start,.35f),yaw,false);
   if(batch.InteriorRealm=="cheolong")
   {
    var across=Quaternion.Euler(0,yaw,0)*Vector3.right;float lower=start+.25f,upper=Mathf.Min(sill-.4f,height*.56f-.25f);
    if(upper>lower+.5f)BeamVenue296(batch,position-across*(width*.43f)+Vector3.up*lower,position+across*(width*.43f)+Vector3.up*upper,.21f,.35f);
   }
   else batch.Add(Haeng296+"SM_R_Dancheong_4.prefab",position+Vector3.up*(height*.56f-.55f),new Vector3(.35f,.55f,width),yaw+90,false);
  }
  static string InteriorCollisionStamp296(Transform root)
  {
   return string.Join("\n",root.GetComponentsInChildren<Collider>(true).OrderBy(c=>ScenePathVenue296(c.transform),StringComparer.Ordinal).Select(c=>
   {
    string mesh=c is MeshCollider mc&&mc.sharedMesh!=null?AssetDatabase.GetAssetPath(mc.sharedMesh):"";
    return ScenePathVenue296(c.transform)+"|"+EditorJsonUtility.ToJson(c)+"|"+c.transform.localToWorldMatrix.ToString("R")+"|"+(string.IsNullOrEmpty(mesh)?"":HashVenue296(mesh));
   }));
  }
  public static string RefreshInteriorStyle296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new InvalidOperationException("Interior visual refresh requires the saved296 Edit candidate.");
   var report=new List<string>();var ledger=JsonUtility.FromJson<VenuePlacementLedger296>(File.ReadAllText(O296+"/venue-placements.json"));var field=new CompactWorldSurface(Session292().MountainLayout);
   venueModules296.Clear();
   foreach(var arena in Sheet296().Arenas.Where(a=>a.Interior))
   {
    var root=Root296(arena.SceneRoot).transform;string before=InteriorCollisionStamp296(root);var group=root.GetComponent<LODGroup>();if(group!=null)Object.DestroyImmediate(group);
    foreach(var child in root.Cast<Transform>().Where(t=>t.name=="SecondEave_OutsideClearFloor"||t.name.StartsWith("L0_",StringComparison.Ordinal)||t.name.StartsWith("L1_",StringComparison.Ordinal)||t.name.StartsWith("L2_",StringComparison.Ordinal)).ToArray())Object.DestroyImmediate(child.gameObject);
    var plan=new VenuePlan296{Id=arena.Id,Realm=arena.Realm,Place=arena.PlaceId,Clear=arena.ClearSize,Yaw=arena.Yaw,Centre=arena.Centre,Height=arena.ClearHeight,Interior=true};
    var batch=new VenueBatch296(plan.Id,root){StoneStyle=plan.Realm};TerraceVenue296(plan,root,field,batch);InteriorVenue296(plan,root,batch,report,true);batch.Save(true);
    string after=InteriorCollisionStamp296(root);if(before!=after)throw new InvalidOperationException("Interior visual update changed physical geometry: "+arena.Id);
    var receipt=ledger.Venues.First(v=>v.Id==arena.Id);receipt.SourcePieces=batch.Pieces;receipt.NearTriangles=batch.NearTriangles;receipt.Sources=batch.Sources.OrderBy(v=>v,StringComparer.Ordinal).ToArray();
    report.Add("PASS "+arena.Id+" all collider components, transforms and mesh file SHA256 unchanged; ground doors0..4.26m retained; plain upper infill + one1.26m clerestory; regional timber/plaster/trim maps retained; no floor props or new collision.");
   }
   File.WriteAllText(O296+"/venue-placements.json",JsonUtility.ToJson(ledger,true));SaveVenueSources296();Save292();File.WriteAllLines(O296+"/interior-style.txt",report);return string.Join("\n",report);
  }
 }
}
