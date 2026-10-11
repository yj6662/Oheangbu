using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;
using KindP=Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind;

namespace Oheangbu.EditorTools.WorldMacro
{
 // [SPEC-WORLD-REALM-FLORA-308 3.6] Prefabs of the packs the project already owns -> prefabs the sheet tool can use (made[]).
 //   pack-plan | pack     rows of Tools/Unity/Stage308_flora/Data/pack_intake.json: { "rows": [ { "id", "prefab", "kind", "tint":[r,g,b], "scale" } ] }
 // The pack prefab and its materials are read, never written. For each row a new prefab (Flora308/Prefabs/Pack_<id>.prefab) holds the
 // pack's own meshes with copies of its materials on Oheangbu/WorldMacroVegetation (instanced, distance fade, wash), base at the origin.
 // A tree keeps up to three levels of the pack's LODGroup (first, middle, last), every other kind the first and the last.
 // The made[] list goes to Out/made_pack.json. Writes only under Assets/_Project/Art/World/Flora308.
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class PackRow308F{public string id="",prefab="",kind="Tree";public float[] tint=Array.Empty<float>();public float scale=1;}
  [Serializable] sealed class PackFile308F{public PackRow308F[] rows=Array.Empty<PackRow308F>();}

  static Material PackMaterial308F(Material source,KindP kind,float height,Color tint,Shader shader,List<string> notes)
  {
   string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
   string path=A308F+"/Materials/Pack_"+(guid==""?source.name:guid)+"_"+kind+"_"+ColorUtility.ToHtmlStringRGB(tint)+".mat";
   var mat=AssetDatabase.LoadAssetAtPath<Material>(path);bool create=mat==null;if(create)mat=new Material(shader);
   mat.shader=shader;mat.enableInstancing=true;
   string slot=source.HasProperty("_BaseMap")?"_BaseMap":source.HasProperty("_MainTex")?"_MainTex":null;
   var baseMap=slot!=null?source.GetTexture(slot):null;
   if(baseMap==null)
   {
    // custom pack shaders keep the colour map under another name: the first texture property that is not a normal / mask map
    foreach(string name in source.GetTexturePropertyNames())
    {
     var t=source.GetTexture(name);if(t==null)continue;string low=name.ToLowerInvariant();
     if(low.Contains("normal")||low.Contains("bump")||low.Contains("mask")||low.Contains("rough")||low.Contains("metal")||low.Contains("occlusion")||low.Contains("emiss"))continue;
     baseMap=t;slot=name;break;
    }
   }
   if(baseMap==null)notes.Add("material "+source.name+" has no colour map");
   mat.SetTexture("_BaseMap",baseMap);if(slot!=null){mat.SetTextureScale("_BaseMap",source.GetTextureScale(slot));mat.SetTextureOffset("_BaseMap",source.GetTextureOffset(slot));}
   Texture normal=source.HasProperty("_BumpMap")?source.GetTexture("_BumpMap"):null;
   if(normal==null)foreach(string name in source.GetTexturePropertyNames()){string low=name.ToLowerInvariant();if((low.Contains("normal")||low.Contains("bump"))&&source.GetTexture(name)!=null){normal=source.GetTexture(name);break;}}
   mat.SetTexture("_BumpMap",normal);mat.SetFloat("_BumpScale",normal!=null?1:0);
   bool clipped=source.IsKeywordEnabled("_ALPHATEST_ON")||(source.HasProperty("_AlphaClip")&&source.GetFloat("_AlphaClip")>.5f)||source.renderQueue>=2450&&source.renderQueue<3000
                ||(baseMap!=null&&AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(baseMap)) is TextureImporter ti&&ti.DoesSourceTextureHaveAlpha()&&(kind==KindP.Tree||kind==KindP.Shrub||kind==KindP.Grass));
   mat.SetFloat("_AlphaClip",clipped?1:0);mat.SetFloat("_Cutoff",source.HasProperty("_Cutoff")?Mathf.Clamp(source.GetFloat("_Cutoff"),.2f,.4f):.3f);mat.SetFloat("_Cull",clipped?0:2);
   var colour=source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white;colour.a=1;
   mat.SetColor("_BaseColor",colour*tint);
   // a tinted copy (charred, slate, wet) reads by value, not by the hue of the scan it came from
   mat.SetFloat("_Saturation",tint.r==1&&tint.g==1&&tint.b==1?.4f:.12f);mat.SetFloat("_AmbientFloor",.36f);mat.SetFloat("_LightResponse",.65f);mat.SetFloat("_Height",Mathf.Max(.3f,height));
   mat.SetFloat("_WindAmplitude",clipped?(kind==KindP.Grass?.06f:kind==KindP.Tree?.095f:.05f):0);mat.SetFloat("_WindSpeed",.85f);mat.SetFloat("_Billboard",0);
   mat.SetFloat("_FadeInStart",-1);mat.SetFloat("_FadeInEnd",0);mat.SetFloat("_FadeOutStart",680);mat.SetFloat("_FadeOutEnd",700);
   if(create)AssetDatabase.CreateAsset(mat,path);else EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);return mat;
  }

  static string Pack308F(bool dry)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Refuse305("Edit mode only (Play is running)");
   string file=Path.Combine(Stage308F,"Data","pack_intake.json");if(!File.Exists(file))throw new Refuse305("no "+file);
   var rows=JsonUtility.FromJson<PackFile308F>(File.ReadAllText(file).TrimStart('﻿')).rows;
   var shader=Shader.Find("Oheangbu/WorldMacroVegetation")??throw new Refuse305("shader Oheangbu/WorldMacroVegetation not found");
   var sb=new StringBuilder("Flora308 pack"+(dry?" plan":"")+" | rows "+rows.Length+"\n");var list=new MadeList308F();int problems=0;
   if(!dry){DevSceneKit.EnsureFolder(A308F+"/Materials");DevSceneKit.EnsureFolder(A308F+"/Prefabs");}
   foreach(var row in rows)
   {
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(row.prefab);
    if(prefab==null||!Enum.TryParse(row.kind,true,out KindP kind)){sb.AppendLine("  PROBLEM "+row.id+": prefab or kind ("+row.prefab+", "+row.kind+")");problems++;continue;}
    var group=prefab.GetComponentInChildren<LODGroup>(true);
    var all=group!=null?group.GetLODs().Select(l=>l.renderers.OfType<MeshRenderer>().Where(r=>r!=null&&r.GetComponent<MeshFilter>()?.sharedMesh!=null).ToArray()).Where(l=>l.Length>0).ToList()
                       :new List<MeshRenderer[]>{prefab.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.GetComponent<MeshFilter>()?.sharedMesh!=null).ToArray()};
    if(all.Count==0||all[0].Length==0){sb.AppendLine("  PROBLEM "+row.id+": no mesh renderer in "+row.prefab);problems++;continue;}
    var levels=all.Count==1?new List<MeshRenderer[]>{all[0]}:kind==KindP.Tree&&all.Count>=3?new List<MeshRenderer[]>{all[0],all[all.Count/2],all[all.Count-1]}:kind==KindP.Grass&&all.Count>=3?new List<MeshRenderer[]>{all[1],all[all.Count-1]}   // ground plants are many: their second level is the near one
                :all.Count>=3&&all[all.Count-1].Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3)<=32?new List<MeshRenderer[]>{all[0],all[all.Count-2]}   // the last level is a camera-facing card of the pack: not usable here
                :new List<MeshRenderer[]>{all[0],all[all.Count-1]};
    // bounds of the first level in the prefab's own space: the base centre becomes the origin
    var toRoot=prefab.transform.worldToLocalMatrix;bool has=false;var b=new Bounds();
    foreach(var r in levels[0]){var mb=r.GetComponent<MeshFilter>().sharedMesh.bounds;var m=toRoot*r.transform.localToWorldMatrix;
     for(int i=0;i<8;i++){var p=m.MultiplyPoint3x4(mb.center+Vector3.Scale(mb.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));if(!has){b=new Bounds(p,Vector3.zero);has=true;}else b.Encapsulate(p);}}
    long tris0=levels[0].Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.LongLength/3),trisN=levels[levels.Count-1].Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.LongLength/3);
    float s=row.scale>0?row.scale:1;
    sb.AppendLine("  "+kind.ToString().PadRight(6)+row.id.PadRight(30)+" levels "+levels.Count+" of "+all.Count+" | tris "+tris0+" / "+trisN+" | size "+F305(b.size.x*s)+" x "+F305(b.size.y*s)+" x "+F305(b.size.z*s));
    if(dry)continue;
    var tint=row.tint!=null&&row.tint.Length>=3?new Color(row.tint[0],row.tint[1],row.tint[2],1):Color.white;var notes=new List<string>();
    var offset=Matrix4x4.Scale(Vector3.one*s)*Matrix4x4.Translate(new Vector3(-b.center.x,-b.min.y,-b.center.z));
    var root=new GameObject("Pack_"+row.id);var lods=new List<LOD>();
    try
    {
     for(int i=0;i<levels.Count;i++)
     {
      var level=new GameObject("L"+i);level.transform.SetParent(root.transform,false);var made=new List<Renderer>();
      foreach(var r in levels[i])
      {
       var child=new GameObject(r.name);child.transform.SetParent(level.transform,false);var m=offset*toRoot*r.transform.localToWorldMatrix;
       child.transform.localPosition=m.GetColumn(3);child.transform.localRotation=m.rotation;child.transform.localScale=m.lossyScale;
       child.AddComponent<MeshFilter>().sharedMesh=r.GetComponent<MeshFilter>().sharedMesh;var nr=child.AddComponent<MeshRenderer>();
       nr.sharedMaterials=r.sharedMaterials.Select(x=>x==null?null:PackMaterial308F(x,kind,b.size.y*s,tint,shader,notes)).ToArray();made.Add(nr);
      }
      lods.Add(new LOD(i==0?.25f:i==levels.Count-1?.01f:.08f,made.ToArray()));
     }
     var g=root.AddComponent<LODGroup>();g.SetLODs(lods.ToArray());g.RecalculateBounds();
     string prefabPath=A308F+"/Prefabs/Pack_"+row.id+".prefab";PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
     list.made.Add(new Made308F{id="Pack_"+row.id,prefab=prefabPath,category=kind.ToString(),radius=kind==KindP.Tree?Mathf.Clamp(b.size.y*s*.032f,.09f,.55f):Mathf.Max(.15f,Mathf.Max(b.size.x,b.size.z)*s*.5f)});
     if(notes.Count>0){sb.AppendLine("      note: "+string.Join("; ",notes.Distinct()));}
    }
    finally{Object.DestroyImmediate(root);}
   }
   if(dry)return sb+"pack plan: problems "+problems;
   string outDir=Path.Combine(Stage308F,"Out");Directory.CreateDirectory(outDir);File.WriteAllText(Path.Combine(outDir,"made_pack.json"),JsonUtility.ToJson(list,true));
   return sb+"pack: "+list.made.Count+" prefabs, problems "+problems+" | made[] list "+Path.Combine(outDir,"made_pack.json");
  }
 }
}
