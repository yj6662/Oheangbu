using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string GrassOut267=Output+"/Grass267";
  public static string Grass267(string command)
  {
   var scene=FrontageScene249();Directory.CreateDirectory(GrassOut267);var r=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactGrassRenderer266>()).Single();var f=r.Field;
   if(command=="wind")return GrassWind267(r);
   if(command=="cover")return GrassCover267(r);
   if(command=="capture"){string result=GrassCapture266();foreach(var path in Directory.GetFiles(GrassOut266,"*-after.png"))File.Copy(path,GrassOut267+"/"+Path.GetFileName(path),true);File.Copy(GrassOut266+"/render-cost.txt",GrassOut267+"/render-cost.txt",true);return result;}
   var catalog=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>()).Single().Catalog;var realm=catalog.Entries.Single(e=>e.Id=="realm_cheongrim");
   string OutsideHash(){using(var memory=new MemoryStream()){using(var writer=new BinaryWriter(memory,System.Text.Encoding.UTF8,true))foreach(var cell in f.Cells)if(cell!=null)foreach(var s in cell.Seeds)if(!realm.Contains(s.Position)){writer.Write(s.Position.x);writer.Write(s.Position.y);writer.Write(s.Position.z);writer.Write(s.NormalXZ.x);writer.Write(s.NormalXZ.y);}return BitConverter.ToString(SHA256.Create().ComputeHash(memory.ToArray()));}}
   if(command=="build"){
    string before=OutsideHash();int old=f.Cells.Where(c=>c!=null).Sum(c=>c.Seeds.Count(s=>realm.Contains(s.Position)));
    f.DenseCheongrim=true;f.DensePolygon=(Vector2[])realm.Polygon.Clone();f.Gust=new Vector4(.86f,.5f,.19f,1.35f);EditorUtility.SetDirty(f);
    string result=Grass266("build");string after=OutsideHash();int count=f.Cells.Where(c=>c!=null).Sum(c=>c.Seeds.Count(s=>realm.Contains(s.Position)));
    result+="\nCheongrim seeds "+old+" -> "+count+". "+(before==after?"PASS":"FAIL")+" all outside seeds unchanged. Ground-space directional gust amplitude .19m.";
    File.WriteAllText(GrassOut267+"/build.txt",result);File.WriteAllText(GrassOut267+"/outside-hash.txt",before+"\n"+after);return result;
   }
   if(command=="checks"){
    string common=GrassChecks266();var lines=new List<string>{common};void C(bool ok,string s)=>lines.Add((ok?"PASS ":"FAIL ")+s);
    C(f.DenseCheongrim&&f.DensePolygon.SequenceEqual(realm.Polygon),"dense area equals current Cheongrim map boundary");
    var full=f.Cells.Where(c=>c!=null&&c.Seeds.Length>0&&realm.Contains(c.Bounds.center)&&c.Bounds.min.x>2560&&c.Bounds.max.x<3990&&c.Bounds.min.z>1560&&c.Bounds.max.z<4090).ToArray();
    C(full.Count(c=>c.Seeds.Length>350)>full.Length*.70f,"at least 70 percent of interior cells have continuous dense cover outside exclusions");
    var hashes=File.ReadAllLines(GrassOut267+"/outside-hash.txt");C(hashes.Length==2&&hashes[0]==hashes[1],"all other realms retain exact previous seed data");C(f.Gust.z>0&&f.Gust.z<=.25f&&f.Gust.w>0,"bounded directional gust enabled");
    C(!ShaderUtil.ShaderHasError(f.Material.shader),"gust shader compiles");
    var terrain=scene.GetRootGameObjects().Single(g=>g.name=="Compact_Rebuild_Terrain").GetComponentsInChildren<MeshRenderer>().Select(t=>t.sharedMaterial).Distinct().ToArray();
    C(terrain.All(m=>m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader)),"all ground coverage shaders compile");
    C(terrain.All(m=>m.GetTexture("_GrassCover267")!=null&&m.GetTexture("_GrassCover267").width==2000&&m.GetFloat("_GrassCoverStrength267")==1),"private far meadow coverage linked");string result=string.Join("\n",lines);File.WriteAllText(GrassOut267+"/checks.txt",result);return result;
   }
   throw new ArgumentException(command);
  }
  static string GrassCover267(CompactGrassRenderer266 renderer)
  {
   var f=renderer.Field;const int width=2000,height=3000;var pixels=new byte[width*height];
   foreach(var cell in f.Cells)if(cell!=null)foreach(var seed in cell.Seeds){var p=seed.Position;if(!WorldMapDiscoveryGrid.Contains(f.DensePolygon,new Vector2(p.x,p.z)))continue;
    int cx=Mathf.FloorToInt(p.x/2),cz=Mathf.FloorToInt(p.z/2);for(int z=Mathf.Max(0,cz-1);z<=Mathf.Min(height-1,cz+1);z++)for(int x=Mathf.Max(0,cx-1);x<=Mathf.Min(width-1,cx+1);x++){float d=Vector2.Distance(new Vector2((x+.5f)*2,(z+.5f)*2),new Vector2(p.x,p.z));if(d<1.9f)pixels[z*width+x]=(byte)Mathf.Max(pixels[z*width+x],Mathf.RoundToInt(Mathf.Clamp01((1.9f-d)/.65f)*255));}
   }
   var texture=GrassAsset266("MeadowCover267",()=>new Texture2D(width,height,TextureFormat.R8,true,true));texture.SetPixelData(pixels,0);texture.Apply(true);texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;texture.anisoLevel=4;EditorUtility.SetDirty(texture);
   var scene=FrontageScene249();var terrain=scene.GetRootGameObjects().Single(g=>g.name=="Compact_Rebuild_Terrain");int count=0;
   foreach(var source in terrain.GetComponentsInChildren<MeshRenderer>().Select(r=>r.sharedMaterial).Distinct().ToArray()){
    if(source.shader.name!="Oheangbu/Study/InkPaintingGround")continue;
    var material=GrassAsset266("MeadowGround267_"+count,()=>new Material(source));material.SetTexture("_GrassCover267",texture);material.SetFloat("_GrassCoverStrength267",1);EditorUtility.SetDirty(material);
    foreach(var r in terrain.GetComponentsInChildren<MeshRenderer>().Where(r=>r.sharedMaterial==source))r.sharedMaterial=material;count++;
   }
   AssetDatabase.SaveAssets();UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
   string result="Meadow coverage 2000x3000 R8, source=actual dense Cheongrim seeds, private terrain materials="+count+". No geometry, collision or NavMesh change.";File.WriteAllText(GrassOut267+"/cover.txt",result);return result;
  }
  static string GrassWind267(CompactGrassRenderer266 renderer)
  {
   var scene=FrontageScene249();var art=renderer.Art;var observer=art.Observer;var ground=FinalSurface(scene);var source=VillageSession().Walker.ViewCamera;
   var go=new GameObject("267 fixed wind review"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(source);cam.enabled=false;EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),cam.GetUniversalAdditionalCameraData());go.AddComponent<Skybox>().material=AssetDatabase.LoadAssetAtPath<Material>(Folder263+"/Sky.asset");
   var rt=new RenderTexture(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;Color32[] first=null;int differences=0;
   try{art.Observer=cam;art.ContactPreviewClock265=10;cam.targetTexture=rt;cam.aspect=16f/9;cam.fieldOfView=58;var eye=ground(3010,2785).point+Vector3.up*1.25f;var target=ground(3018,2800).point;cam.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));
    for(int i=0;i<24;i++){renderer.WindPreviewClock267=10+i/6f;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes(GrassOut267+"/wind-"+i.ToString("D2")+".png",tex.EncodeToPNG());var pixels=tex.GetPixels32();if(i==0)first=pixels;if(i==12)for(int j=0;j<pixels.Length;j++)if(Math.Abs(pixels[j].r-first[j].r)+Math.Abs(pixels[j].g-first[j].g)+Math.Abs(pixels[j].b-first[j].b)>12)differences++;}
   }finally{renderer.WindPreviewClock267=-1;art.Observer=observer;art.ContactPreviewClock265=-1;cam.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   string result=(differences>100?"PASS":"FAIL")+" only added-grass clock advanced in fixed camera; changed pixels="+differences+". 24 frames / four seconds. Not manual play.";File.WriteAllText(GrassOut267+"/wind-check.txt",result);return result;
  }
 }
}
