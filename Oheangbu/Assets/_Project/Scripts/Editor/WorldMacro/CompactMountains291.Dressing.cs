using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  static void DressHermitage291(WorldMacroPlaytestSession session,CompactWorldLayoutSO.Mountain mountain){
   var root=GameObject.Find("ThatchedHermitage291").transform;var t=mountain.Temple;
   var terrain=GameObject.Find("Compact_Mountains_290").GetComponentsInChildren<MeshCollider>().Where(c=>c.name.StartsWith("Terrain_")).ToArray();
   Vector3 Ground(Vector3 p){foreach(var c in terrain)if(c.Raycast(new Ray(p+Vector3.up*300,Vector3.down),out var hit,600))return hit.point;return p;}
   var rng=new System.Random(291);float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
   var stone=MountainMaterial290("cheongrim","Granite");var wood=MountainMaterial290("cheongrim","Pier289_poles");
   var rock=Import285("Scanned_boulder_LOD1");var debris=Import285("Scanned_boulder_LOD2");
   var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>("Assets/_Project/Art/World/Rock275/Placements.asset");
   var species=sheet.Prototypes.Where(p=>new[]{"Cheongrim_SM_Deparia_1","Cheongrim_SM_Deparia_3","Cheongrim_LowGroundFill","Cheongrim_SM_Grass"}.Contains(p.Id)).ToArray();
   var prototypeRoot=GameObject.Find("Cheongrim_AssetPass_290").transform;
   var pine=prototypeRoot.GetComponentsInChildren<LODGroup>().First(p=>p.name.StartsWith("Ledge_pine_"));
   var protectedPoints=session.Content.Points.Where(p=>p.Id.StartsWith(mountain.Id)).Select(p=>p.Position).ToArray();
   bool Walkway(Vector3 p)=>protectedPoints.Any(v=>Vector2.Distance(new Vector2(p.x,p.z),new Vector2(v.x,v.z))<2.9f)||
    (Mathf.Abs(p.x-t.x)<3.7f&&p.z>t.z-8&&p.z<t.z+23)||
    Distance290(new Vector2(p.x,p.z),mountain.TemplePath,out _)<2.4f||Distance290(new Vector2(p.x,p.z),mountain.ReturnPath,out _)<2.4f;
   void Plant(WorldMacroDressingSheetSO.Prototype s,Vector3 p,float scale,int index){
    var go=new GameObject("SoilPocket_"+index);go.transform.SetParent(root,false);go.transform.position=p;go.transform.rotation=Quaternion.Euler(0,R(0,360),0);go.transform.localScale=Vector3.one*scale;
    var levels=new List<LOD>();for(int l=0;l<Mathf.Min(s.Lods.Length,3);l++){
     var renderers=new List<Renderer>();foreach(var part in s.Lods[l].Parts){var child=MeshObject278("LOD"+l,part.Mesh,part.Material,go.transform,false);child.transform.localPosition=part.Local.GetColumn(3);child.transform.localRotation=part.Local.rotation;child.transform.localScale=part.Local.lossyScale;renderers.Add(child.GetComponent<Renderer>());}
     levels.Add(new LOD(l==0?.015f:l==1?.005f:.002f,renderers.ToArray()));}
    var group=go.AddComponent<LODGroup>();group.SetLODs(levels.ToArray());group.RecalculateBounds();
    var render=go.GetComponentsInChildren<Renderer>();if(render.Length>0)go.transform.position+=Vector3.up*(p.y-render.Min(r=>r.bounds.min.y)-.02f);
   }
   // Soil pockets sit around the shoulders of the clearing, leaving the approach and devices legible.
   var centres=new[]{new Vector3(-12,0,3),new Vector3(-13,0,24),new Vector3(14,0,22),new Vector3(12,0,34),new Vector3(-9,0,36),new Vector3(18,0,-7),new Vector3(-22,0,12)};
   var debrisInstances=new List<CombineInstance>();int plants=0,rocks=0;
   foreach(var offset in centres){var centre=t+offset;
    for(int i=0;i<4;i++){
     var p=Ground(centre+new Vector3(R(-2.7f,2.7f),0,R(-2.4f,2.4f)));if(Walkway(p))continue;
     var go=MeshObject278("BedrockShoulder_"+rocks++,rock,stone,root,false);var b=rock.bounds;
     go.transform.rotation=Quaternion.Euler(R(-8,8),R(0,360),R(-6,6));go.transform.localScale=new Vector3(R(1.6f,3.6f)/b.size.x,R(.7f,1.8f)/b.size.y,R(1.6f,3.2f)/b.size.z);
     go.transform.position=p-go.transform.TransformVector(b.center)-Vector3.up*.18f;
     // One inexpensive broad collider per large embedded shoulder; small fragments remain visual only.
     var box=go.AddComponent<BoxCollider>();box.center=b.center;box.size=b.size*.78f;
    }
    for(int i=0;i<24;i++){
     var p=Ground(centre+new Vector3(R(-4.6f,4.6f),0,R(-4.4f,4.4f)));if(Walkway(p))continue;
     Plant(species[i%species.Length],p,R(.32f,.9f),plants++);
     if(i%2==0){var b=debris.bounds;var size=new Vector3(R(.18f,.6f)/b.size.x,R(.12f,.3f)/b.size.y,R(.17f,.5f)/b.size.z);var q=Quaternion.Euler(0,R(0,360),0);debrisInstances.Add(new CombineInstance{mesh=debris,transform=Matrix4x4.TRS(p-q*Vector3.Scale(b.center,size)-Vector3.up*.04f,q,size)});}
    }
   }
   var combined=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};combined.CombineMeshes(debrisInstances.ToArray());combined=ArtMesh(combined,A291+"/CourtyardFragments.asset");
   var fragments=MeshObject278("ScatteredFragments",combined,stone,root,false);fragments.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
   var fragmentLod=fragments.AddComponent<LODGroup>();fragmentLod.SetLODs(new[]{new LOD(.055f,new[]{fragments.GetComponent<Renderer>()})});fragmentLod.RecalculateBounds();
   int treeIndex=0;foreach(var offset in new[]{new Vector3(-13,0,20),new Vector3(15,0,28),new Vector3(-24,0,2),new Vector3(19,0,-4)}){
    var p=Ground(t+offset);var tree=Object.Instantiate(pine.gameObject,root);tree.name="CourtyardPine_"+treeIndex++;tree.transform.rotation=Quaternion.Euler(0,R(0,360),0);tree.transform.localScale=Vector3.one;
    var rs=tree.GetComponentsInChildren<Renderer>(true);var bounds=rs[0].bounds;foreach(var r in rs.Skip(1))bounds.Encapsulate(r.bounds);
    tree.transform.localScale*=R(7,10)/Mathf.Max(.1f,bounds.size.y);tree.transform.position=p;
    tree.transform.position+=Vector3.up*(p.y-tree.GetComponentsInChildren<Renderer>(true).Min(r=>r.bounds.min.y)-.1f);
    foreach(var col in tree.GetComponentsInChildren<Collider>())Object.DestroyImmediate(col);
    var trunk=tree.AddComponent<CapsuleCollider>();trunk.center=new Vector3(0,2,0);trunk.height=4;trunk.radius=.28f;
   }
   // Give the working devices timber silhouettes instead of masonry debug cubes.
   var parent=root.parent;
   foreach(string suffix in new[]{"brace","winch","archive_open","temple"}){
    var point=parent.Find(mountain.Id+"_"+suffix);var old=point.Find("DeviceArt291");if(old!=null)Object.DestroyImmediate(old.gameObject);
    foreach(var r in point.GetComponentsInChildren<Renderer>())r.enabled=false;
    var detail=new GameObject("DeviceArt291").transform;detail.SetParent(point,false);
    void Beam(Vector3 a,Vector3 b,float width){var go=MeshObject278("OldTimber",Import285("Pier289_poles_0"),wood,detail,false);go.transform.position=point.position+(a+b)*.5f;go.transform.rotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);go.transform.localScale=new Vector3(width,(b-a).magnitude,width);}
    if(suffix=="brace"){
     Beam(new Vector3(-.7f,0,0),new Vector3(-.7f,1.3f,0),.20f);Beam(new Vector3(-.9f,1.25f,0),new Vector3(.9f,1.05f,0),.23f);Beam(new Vector3(.5f,0,-.5f),new Vector3(-.6f,1.15f,0),.16f);
    }else if(suffix=="winch"){
     Beam(new Vector3(-.6f,0,0),new Vector3(-.6f,1.25f,0),.22f);Beam(new Vector3(.6f,0,0),new Vector3(.6f,1.25f,0),.22f);Beam(new Vector3(-.85f,1.1f,0),new Vector3(.85f,1.1f,0),.38f);Beam(new Vector3(.82f,1.1f,0),new Vector3(.82f,1.1f,.55f),.14f);
    }else{
     var b=rock.bounds;var slab=MeshObject278("WeatheredRecordStone",rock,stone,detail,false);slab.transform.localScale=new Vector3(.9f/b.size.x,.85f/b.size.y,.52f/b.size.z);slab.transform.localPosition=Vector3.up*.36f-Vector3.Scale(b.center,slab.transform.localScale);slab.transform.localRotation=Quaternion.Euler(0,18,-4);
     if(suffix=="temple"){Beam(new Vector3(-.42f,.88f,-.13f),new Vector3(.38f,.88f,-.13f),.11f);Beam(new Vector3(-.42f,.85f,.12f),new Vector3(.38f,.85f,.12f),.11f);}
    }
    point.GetComponent<WorldMacroContentPoint>().Visual=detail;
    var binding=session.InteractionVisuals.First(v=>v.Id==mountain.Id+"_"+suffix);binding.Renderers=detail.GetComponentsInChildren<Renderer>();
   }
   EditorUtility.SetDirty(session);Physics.SyncTransforms();
   File.WriteAllText(O291+"/dressing.txt",$"Authored hermitage pockets: {rocks} broad rock shoulders, {debrisInstances.Count} batched visual fragments, {plants} LOD plant groups, {treeIndex} pines. Approach and six interaction positions remain clear. Existing stable device IDs retained.");
  }
 }
}
