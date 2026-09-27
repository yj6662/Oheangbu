using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void Dress263(Transform root,PrologueEncounter actor,WorldMacroDressingSheetSO source,WorldMacroDressingSheetSO sheet,Dictionary<string,Vector3[]> paths,Vector3[] cave,Func<float,float,RaycastHit> ground,Material bark)
  {
   var placements=sheet.FixedPlacements.ToList();var random=new System.Random(263);float R()=> (float)random.NextDouble();var center=actor.transform.position;
   bool Clear(Vector3 p,float distance)=>paths.Values.Any(line=>FlatPathDistance(p,line)<distance);
   for(int i=0;i<420;i++){
    float a=R()*Mathf.PI*2,r=34+Mathf.Sqrt(R())*130;var p=ground(center.x+Mathf.Cos(a)*r,center.z+Mathf.Sin(a)*r).point;
    if(Clear(p,3.2f))continue;bool pine=i%4==0;placements.Add(new WorldMacroDressingSheetSO.FixedPlacement{Id="sinmok263_tree_"+i,ClusterId="old_tree",PrototypeId=pine?"Detail261_Meshy_Pinus":"Detail261_Cheongrim_SM_UlmusDavidiana_Summer_2",Position=p,Euler=new Vector3(0,R()*360,0),Scale=pine?.75f+R()*.45f:.7f+R()*.55f});
   }
   for(int i=0;i<700;i++){
    float a=R()*Mathf.PI*2,r=10+Mathf.Sqrt(R())*80;var p=ground(center.x+Mathf.Cos(a)*r,center.z+Mathf.Sin(a)*r).point;
    if(Clear(p,1.5f)||Vector3.Distance(p,center)<12)continue;
    placements.Add(new WorldMacroDressingSheetSO.FixedPlacement{Id="sinmok263_ground_"+i,ClusterId="old_tree",PrototypeId=i%6==0?"Detail261_Cheongrim_SM_Rock_K":i%3==0?"Detail261_Cheongrim_SM_Deparia_1":"Detail261_Cheongrim_SM_Grass",Position=p,Euler=new Vector3(0,R()*360,0),Scale=i%6==0?.3f+R()*.4f:.7f+R()*.6f});
   }
   sheet.FixedPlacements=placements.ToArray();
   var rock=source.Prototypes.First(p=>p.Id=="Cheongrim_SM_Rock_K");
   void Rock(string name,Vector3 p,Vector3 scale,float yaw){var group=new GameObject(name).transform;group.SetParent(root,false);group.position=p;group.rotation=Quaternion.Euler(0,yaw,0);group.localScale=scale;
    foreach(var part in rock.Lods[0].Parts){var g=new GameObject("RockPart",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));g.transform.SetParent(group,false);g.transform.localPosition=part.Local.GetColumn(3);g.transform.localRotation=part.Local.rotation;g.transform.localScale=part.Local.lossyScale;g.GetComponent<MeshFilter>().sharedMesh=part.Mesh;g.GetComponent<MeshRenderer>().sharedMaterial=part.Material;g.GetComponent<MeshCollider>().sharedMesh=part.Mesh;}
    var renderers=group.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);group.position+=p-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);}
   // Talus closes the manufactured-looking shell edge while both walking mouths remain open.
   for(int i=0;i<cave.Length;i+=4){var tangent=cave[Math.Min(i+1,cave.Length-1)]-cave[Math.Max(0,i-1)];tangent.y=0;var side=Vector3.Cross(tangent.normalized,Vector3.up);
    foreach(int sign in new[]{-1,1}){float offset=8+(i>cave.Length*.30f&&i<cave.Length*.60f?3:0);var p=cave[i]+side*offset;p=ground(p.x,p.z).point;
     float factor=(4+R()*3)/Mathf.Max(.1f,rock.Size.y);Rock("GalleryTalus"+i+"_"+sign,p-Vector3.up*.35f,new Vector3(factor*(.8f+R()*.4f),factor,factor*(.75f+R()*.5f)),R()*360);}}
   Vector3 Fit(Vector3 size)=>new Vector3(size.x/rock.Size.x,size.y/rock.Size.y,size.z/rock.Size.z);
   foreach(int i in new[]{0,cave.Length-1}){
    var direction=(cave[Math.Min(i+2,cave.Length-1)]-cave[Math.Max(0,i-2)]).normalized;var side=Vector3.Cross(direction,Vector3.up).normalized;float yaw=Quaternion.LookRotation(direction).eulerAngles.y;
    foreach(int sign in new[]{-1,1}){var p=cave[i]+side*(sign*5.4f);p=ground(p.x,p.z).point-Vector3.up*.5f;Rock("MouthButtress"+i+"_"+sign,p,Fit(new Vector3(4.6f,7.5f,5.7f)),yaw+sign*13);}
    Rock("MouthCrown"+i,cave[i]+Vector3.up*5.2f,Fit(new Vector3(10,4,6)),yaw+7);
   }
   // Imported rock bounds can extend behind their visible face. Keep the gallery's walking/camera envelope clear.
   foreach(var group in root.Cast<Transform>().Where(t=>t.name.StartsWith("GalleryTalus")||t.name.StartsWith("Mouth")).ToArray()){
    var rs=group.GetComponentsInChildren<Renderer>();if(rs.Length==0)continue;var b=rs[0].bounds;foreach(var rr in rs)b.Encapsulate(rr.bounds);
    if(cave.Any(p=>b.Intersects(new Bounds(p+Vector3.up*1.9f,new Vector3(4.8f,3.2f,4.8f)))))UnityEngine.Object.DestroyImmediate(group.gameObject);
   }
   // The warning boundary is physical folk material: old posts and a sagging rope beside the approach.
   var mark=ground(center.x-25,center.z-15).point;
   for(int i=0;i<3;i++){var p=ground(mark.x+i*2.2f,mark.z).point;MeshObject263(root,"BoundaryPost"+i,Tube263("BoundaryPost"+i,new[]{p,p+new Vector3(.05f,1.4f,0),p+new Vector3(.12f,2.3f,.06f)},new[]{.13f,.10f,.06f},i,12,16),bark,true);}
   var rope=new[]{ground(mark.x,mark.z).point+Vector3.up*1.8f,ground(mark.x+2.2f,mark.z).point+Vector3.up*1.1f,ground(mark.x+4.4f,mark.z).point+Vector3.up*1.8f};
   MeshObject263(root,"OldBoundaryRope",Tube263("OldBoundaryRope",rope,new[]{.026f,.026f,.026f},32,6,22),bark,false);
   // Sparse surviving crown, individually skinned to branch tips rather than a single static foliage sphere.
   var reference=actor.GetComponentsInChildren<SkinnedMeshRenderer>().First();var bones=reference.bones;var branchTips=bones.Where(b=>b.name.StartsWith("BranchTip")&&!b.name.EndsWith("0")&&!b.name.EndsWith("1")).ToArray();
   var leafSource=source.Prototypes.Single(p=>p.Id=="Cheongrim_SM_UlmusDavidiana_Summer_2").Lods[0].Parts.Select(p=>p.Material).FirstOrDefault(m=>m.HasProperty("_AlphaClip")&&m.GetFloat("_AlphaClip")>.5f);
   if(leafSource==null)leafSource=source.Prototypes.Single(p=>p.Id=="Cheongrim_SM_UlmusDavidiana_Summer_2").Lods[0].Parts.Last().Material;
   var leaves=Asset263("LivingLeaves",()=>new Material(leafSource));if(leaves.HasProperty("_BaseColor"))leaves.SetColor("_BaseColor",new Color(.75f,.76f,.62f));if(leaves.HasProperty("_WindAmplitude"))leaves.SetFloat("_WindAmplitude",0);if(leaves.HasProperty("_Cull"))leaves.SetFloat("_Cull",0);EditorUtility.SetDirty(leaves);
   var v=new List<Vector3>();var uv=new List<Vector2>();var weights=new List<BoneWeight>();var tri=new List<int>();
   foreach(var bone in branchTips)for(int i=0;i<110;i++){
    float a=R()*Mathf.PI*2,r=Mathf.Sqrt(R())*2.2f;var p=bone.position-center+new Vector3(Mathf.Cos(a)*r,(R()-.3f)*2,Mathf.Sin(a)*r);
    var right=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(.28f+R()*.28f);var up=new Vector3(0,.3f+R()*.3f,0);int n=v.Count;
    v.AddRange(new[]{p-right-up,p+right-up,p+right+up,p-right+up});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});for(int j=0;j<4;j++)weights.Add(new BoneWeight{boneIndex0=Array.IndexOf(bones,bone),weight0=1});tri.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
   }
   var mesh=Asset263("TreeLivingCrown",()=>new Mesh());mesh.Clear();mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.boneWeights=weights.ToArray();mesh.bindposes=reference.sharedMesh.bindposes;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
   var crown=new GameObject("LivingCrown").AddComponent<SkinnedMeshRenderer>();crown.transform.SetParent(actor.transform,false);crown.sharedMesh=mesh;crown.sharedMaterial=leaves;crown.bones=bones;crown.rootBone=reference.rootBone;crown.localBounds=reference.localBounds;crown.updateWhenOffscreen=true;crown.forceMatrixRecalculationPerRender=true;
  }
 }
}
