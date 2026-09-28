using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using Oheangbu.App.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string CheckProps264(){
   var scene=FrontageScene249();var roots=scene.GetRootGameObjects();var root=roots.Single(g=>g.name==PropsRoot264);var ledger=JsonUtility.FromJson<PropLedger264>(File.ReadAllText(PropsOut264+"/placements.json"));var paths=PropPaths264();var lines=new List<string>();
   void C(bool ok,string name)=>lines.Add((ok?"PASS ":"FAIL ")+name);
   var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>()).Single();
   C(root.transform.childCount==6&&ledger.sites.Length==6,"six unique authored groups");
   C(root.GetComponentsInChildren<MonoBehaviour>(true).Length==0&&root.GetComponentsInChildren<Rigidbody>(true).Length==0&&root.GetComponentsInChildren<AudioSource>(true).Length==0,"inert props: no vehicle gameplay, physics bodies or duplicate audio");
   C(root.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader))),"all material and shader references valid");
   C(root.GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh!=null),"all meshes resolve");
   C(manifest.Art==art.Sheet&&ledger.sites.All(e=>art.Sheet.StoryClusters.Count(c=>c.Id==e.id)==1&&art.Sheet.PreservedAreas.Count(p=>p.Id==e.id)==1),"manifest, vegetation exclusions and story ledger agree");
   C(VillageSession().Content.SaveSlot=="world-demo-compact-cave-v4","save slot unchanged");
   Physics.SyncTransforms();var ground=FinalSurface(scene);
   foreach(var e in ledger.sites){var t=root.transform.Find(e.id);C(t!=null&&Vector3.Distance(t.position,e.center)<.01f,"ledger matches scene "+e.id);C(Mathf.Abs(ground(e.center.x,e.center.z).point.y-e.center.y)<.025f,"site grounded "+e.id);
    var solids=t.GetComponentsInChildren<Collider>();bool clear=true;foreach(var col in solids){var b=col.bounds;float radius=new Vector2(b.extents.x,b.extents.z).magnitude;if(paths.Any(p=>FlatPathDistance(b.center,p)<radius+3))clear=false;}
    C(clear,"collider envelopes leave at least 6m road corridor "+e.id);
    var nav=new NavMeshPath();bool walk=NavMesh.SamplePosition(e.road,out var a,3,NavMesh.AllAreas)&&NavMesh.SamplePosition(e.center+(e.road-e.center).normalized*5,out var bnav,3,NavMesh.AllAreas)&&NavMesh.CalculatePath(a.position,bnav.position,NavMesh.AllAreas,nav)&&nav.status==NavMeshPathStatus.PathComplete;C(walk,"road to site frontage NavMesh "+e.id);
   }
   File.WriteAllLines(PropsOut264+"/checks.txt",lines);return string.Join("\n",lines);
  }
  static string CaptureProps264(){
   var scene=FrontageScene249();var s=VillageSession();var art=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var observer=art.Observer;var root=scene.GetRootGameObjects().Single(g=>g.name==PropsRoot264);var ledger=JsonUtility.FromJson<PropLedger264>(File.ReadAllText(PropsOut264+"/placements.json"));
   var go=new GameObject("264 offscreen review"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(s.Walker.ViewCamera);cam.enabled=false;EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),cam.GetUniversalAdditionalCameraData());
   var sky=go.AddComponent<Skybox>();sky.material=AssetDatabase.LoadAssetAtPath<Material>(Folder263+"/Sky.asset");var ground=FinalSurface(scene);var rt=new RenderTexture(1920,1080,24);var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prior=RenderTexture.active;
   try{art.Observer=cam;cam.targetTexture=rt;cam.aspect=16f/9;cam.fieldOfView=55;cam.clearFlags=CameraClearFlags.Skybox;
    for(int i=0;i<ledger.sites.Length;i++){var e=ledger.sites[i];var offset=(e.road-e.center).normalized*9;var eye=ground(e.center.x+offset.x,e.center.z+offset.z).point+Vector3.up*2.1f;var target=e.center+Vector3.up*1.2f;cam.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));
     foreach(bool visible in new[]{false,true}){root.SetActive(visible);cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(PropsOut264+"/"+i+(visible?"-after":"-before")+".png",image.EncodeToPNG());}
    }
   }finally{root.SetActive(true);art.Observer=observer;cam.targetTexture=null;RenderTexture.active=prior;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(go);}
   return "Six matched 1080p prop visibility pairs, fixed terrain/vegetation; offscreen Edit capture, not manual play.";
  }
 }
}
