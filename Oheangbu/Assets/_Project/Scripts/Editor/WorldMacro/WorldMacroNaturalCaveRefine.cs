using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroNaturalCave
    {
        static string Isolate()
        {
            var root=Root;var pos=root.TransformPoint(new Vector3(-6,3,-26));var target=root.TransformPoint(new Vector3(-55,6,5));
            var extra=Object.FindFirstObjectByType<Oheangbu.App.World.Dressing.EarlyRegionFoliage>();var forest=GameObject.Find("WorldMacro_AuthoredGeography")?.transform.Find("04_ForestExtent_PlaceholderClusters");
            var hidden=Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&!r.name.StartsWith("Terrain_")&&!r.transform.IsChildOf(root)).ToArray();
            try{foreach(var r in hidden)r.enabled=false;if(extra!=null)extra.enabled=false;if(forest!=null)forest.name="Diagnostic_Forest_Hidden";var file=WorldMacroDressingProbe.Capture("NaturalCave_isolate",false,pos.x,pos.y,pos.z,target.x,target.y,target.z);File.Copy(file,Output+"/isolate.png",true);return file;}
            finally{foreach(var r in hidden)r.enabled=true;if(extra!=null)extra.enabled=true;if(forest!=null)forest.name="04_ForestExtent_PlaceholderClusters";}
        }
        static string Rays()
        {
            var root=Root;var pos=root.TransformPoint(new Vector3(-6,3,-26));var target=root.TransformPoint(new Vector3(-55,6,5));var rot=Quaternion.LookRotation(target-pos);var lines=new List<string>();
            foreach(float u in new[]{-.65f,-.25f,0,.25f,.65f})foreach(float v in new[]{-.25f,0,.25f}){var dir=rot*new Vector3(u,v,1).normalized;foreach(var h in Physics.RaycastAll(pos,dir,100,1).OrderBy(h=>h.distance).Take(3))lines.Add(u+","+v+" "+h.collider.name+" dist="+h.distance+" pos="+root.InverseTransformPoint(h.point));}
            foreach(var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&r.bounds.SqrDistance(pos)<25))lines.Add("NEAR "+r.name+" parent="+r.transform.parent?.name+" bounds="+r.bounds);
            File.WriteAllLines(Output+"/portal_rays.txt",lines);return string.Join("\n",lines);
        }
        static string DetailSurvey()
        {
            var root=Root;var lines=new List<string>();
            foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.GetComponent<MeshFilter>()!=null&&r.enabled&&Vector3.Distance(r.bounds.center,root.position)<230))lines.Add(r.name+" parent="+r.transform.parent?.name+" bounds="+r.bounds.ToString("F2")+" source="+AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh));
            foreach(var l in root.GetComponentsInChildren<Light>())lines.Add("LIGHT "+l.name+" position="+root.InverseTransformPoint(l.transform.position)+" range="+l.range+" intensity="+l.intensity);
            File.WriteAllLines(Output+"/detail_survey.txt",lines);return string.Join("\n",lines);
        }
        static string Embed()
        {
            var root=Root;var filter=root.Find("Natural_Cave_Interior").GetComponent<MeshFilter>();
            var original=JsonUtility.FromJson<Geometry>(File.ReadAllText(Output+"/geometry.json")).meshes.First(m=>m.name=="Natural_Cave_Interior");
            var vs=new List<Vector3>();var ts=new List<int>();
            Vector3 Shape(Vector3 p)
            {
                float influence=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.1f,2,p.y));
                float n=(Mathf.PerlinNoise(p.x*.63f,p.z*.72f)-.5f)*.34f;
                p.y+=influence*n;return p;
            }
            for(int i=0;i<original.triangles.Length;i+=3)
            {
                var poly=new List<Vector3>{original.vertices[original.triangles[i]],original.vertices[original.triangles[i+1]],original.vertices[original.triangles[i+2]]};var clipped=new List<Vector3>();
                for(int j=0;j<3;j++){var a=poly[j];var b=poly[(j+1)%3];bool ia=a.x<=-43,ib=b.x<=-43;if(ia)clipped.Add(a);if(ia!=ib)clipped.Add(Vector3.Lerp(a,b,(-43-a.x)/(b.x-a.x)));}
                for(int j=1;j<clipped.Count-1;j++){int n=vs.Count;vs.AddRange(new[]{Shape(clipped[0]),Shape(clipped[j]),Shape(clipped[j+1])});ts.AddRange(new[]{n,n+1,n+2});}
            }
            // Weld section vertices so the erosion surface does not acquire triangle faceting.
            var lookup=new Dictionary<Vector3Int,int>();var unique=new List<Vector3>();var mapped=new int[vs.Count];
            for(int i=0;i<vs.Count;i++){var p=vs[i];var key=new Vector3Int(Mathf.RoundToInt(p.x*10000),Mathf.RoundToInt(p.y*10000),Mathf.RoundToInt(p.z*10000));if(!lookup.TryGetValue(key,out int n)){n=unique.Count;lookup[key]=n;unique.Add(p);}mapped[i]=n;}
            var mesh=new Mesh{name="Natural_Cave_EmbeddedInterior",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(unique);mesh.triangles=ts.Select(i=>mapped[i]).ToArray();mesh.uv=unique.Select(p=>new Vector2(p.x,p.z)*.2f).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();mesh=Save(mesh,mesh.name);filter.sharedMesh=mesh;filter.GetComponent<MeshCollider>().sharedMesh=mesh;
            var material=Rock();string stem="Assets/YongmeoriCoast/Texture/LandScape/T_Rock01";material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(stem+"_BC.png"));material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(stem+"_NM.png"));material.SetColor("_BaseColor",new Color(.65f,.64f,.59f));material.SetFloat("_CaveAmbient",.44f);material.SetFloat("_WorldTiling",.16f);EditorUtility.SetDirty(material);
            // Restore the original two terrain meshes before the revised, cached local operation.
            foreach(string key in new[]{"Terrain_052","Terrain_060"}){var f=GameObject.Find(key).GetComponent<MeshFilter>();var source=AssetDatabase.LoadAssetAtPath<Mesh>(WorldMacroBuilder.Folder+"/Meshes/"+key+".asset");f.sharedMesh=source;f.GetComponent<MeshCollider>().sharedMesh=source;}
            var details=root.Find("Authored_Mine_Details");if(details!=null)Object.DestroyImmediate(details.gameObject);
            Physics.SyncTransforms();File.WriteAllText(Output+"/entrance_revision.txt","Entrance moved to the mountain face at local X=-43. The former exposed first 43m gallery is now an outdoor approach; the deep chambers stay unchanged. Original terrain copies restored before local re-carving. No global regeneration.");
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Interior now opens at the actual rising mountain; old forward dome removed. Apply revised terrain next.";
        }
    }
}
