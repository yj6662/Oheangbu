using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Oheangbu.Data.World;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        [Serializable] public sealed class Zone {public string path,kind;public float minX,maxX,minZ,maxZ;}
        [Serializable] public sealed class Zones {public Zone[] zones;}
        [Serializable] public sealed class AssetPair {public string source,target,hash;}
        [Serializable] public sealed class MeshJob {public string path,source;public Matrix4x4 originalMatrix;public bool filter,collider,road;public string result;}
        [Serializable] public sealed class Progress
        {
            public string phase,sourceHash,scene,limit="No gameplay or visual approval implied by conversion.";
            public Vector2 sourceSize,targetSize;public float protectedPadding;public double gapScaleX,gapScaleZ;
            public int sourceObjects,completedMeshes;public AssetPair[] assets;public MeshJob[] meshes;public Zone[] zones;
            public List<string> changedFields=new List<string>();public List<string> findings=new List<string>();
        }
        static string Hash(string path){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        static string WriteProgress(Progress p){string s=JsonUtility.ToJson(p,true);File.WriteAllText(Path.Combine(Output,"progress.json"),s);return JsonUtility.ToJson(new Progress{phase=p.phase,scene=p.scene,sourceObjects=p.sourceObjects,completedMeshes=p.completedMeshes,targetSize=p.targetSize,gapScaleX=p.gapScaleX,gapScaleZ=p.gapScaleZ},true);}
        static Progress ReadProgress()=>JsonUtility.FromJson<Progress>(File.ReadAllText(Path.Combine(Output,"progress.json")));
        static WorldMacroCompressionMapSO Compression=>AssetDatabase.LoadAssetAtPath<WorldMacroCompressionMapSO>(Folder+"/Compression.asset");
        static void RequireCompact(){if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=TargetScene)throw new InvalidOperationException("Compact Edit scene required.");if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; compact work paused.");}
        static bool CoordinateAsset(Object o)=>o is WorldMacroSheetSO||o is WorldMacroPlaytestSO||o is WorldMacroOpeningProfileSO||o is WorldMacroContentSheetSO||o is WorldMacroLandmarkSheetSO||o is WorldMacroVisualCorridorSO||o is WorldMacroDressingSheetSO||o is WorldMapBakedDataSO;
        static Transform Find(string path){var bits=path.Split('/');var t=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==bits[0])?.transform;for(int i=1;i<bits.Length&&t!=null;i++)t=t.Find(bits[i]);return t;}
        static List<WorldMacroCompressionInterval> Intervals(Zone[] zones,bool x,double shoulder)
        {
            var a=zones.Select(z=>new WorldMacroCompressionInterval((x?z.minX:z.minZ)-2,(x?z.maxX:z.maxZ)+2)).OrderBy(v=>v.Min).ToList();
            for(int i=1;i<a.Count;){if(a[i].Min-a[i-1].Max<=shoulder*2+.01){a[i-1]=new WorldMacroCompressionInterval(a[i-1].Min,Math.Max(a[i-1].Max,a[i].Max));a.RemoveAt(i);}else i++;}return a;
        }
        static string Prepare()
        {
            RequireSource();if(SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Source has unsaved edits; preserve explicitly before conversion.");
            if(File.Exists(TargetScene)||Directory.Exists(Folder))throw new InvalidOperationException("Compact outputs already exist; use their bounded continuation commands.");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Commit guard >=85%.");
            var geo=WorldMacroBuilder.Sheet;var zs=JsonUtility.FromJson<Zones>(File.ReadAllText(Path.Combine(Output,"protected_zones.json"))).zones.Where(z=>!z.path.StartsWith("Demo_EscortNavigation/",StringComparison.Ordinal)).ToArray();
            WorldMacroCompressionMap mapping=null;string reason=null;
            var bounds=new Bounds(new Vector3(0,400,0),new Vector3(8000,2000,12000));
            foreach(double shoulder in new[]{4d,2d,1d})if(WorldMacroCompressionMap.TryBuild(bounds,new Vector2(-2000,-3000),new Vector2(2000,3000),Intervals(zs,true,shoulder),Intervals(zs,false,shoulder),shoulder,.1,out mapping,out reason))break;
            if(mapping==null)throw new InvalidOperationException("Protected geometry does not fit: "+reason);
            Directory.CreateDirectory(Folder+"/Data");Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Materials");Directory.CreateDirectory(Folder+"/Textures");
            var map=ScriptableObject.CreateInstance<WorldMacroCompressionMapSO>();map.Mapping=mapping;AssetDatabase.CreateAsset(map,Folder+"/Compression.asset");
            var dependencies=AssetDatabase.GetDependencies(SourceScene,true);
            var list=new List<AssetPair>();var replacements=new Dictionary<Object,Object>();int serial=0;
            foreach(string source in dependencies)
            {
                var value=AssetDatabase.LoadMainAssetAtPath(source);if(!CoordinateAsset(value))continue;
                var clone=Object.Instantiate(value);clone.name=value.name+"_Compact";string target=Folder+"/Data/"+(serial++).ToString("D2")+"_"+Path.GetFileName(source);
                AssetDatabase.CreateAsset(clone,target);replacements[value]=clone;list.Add(new AssetPair{source=source,target=target,hash=Hash(source)});
            }
            // A private immutable snapshot supplies the source height field for inverse sampling.
            var snapshot=Object.Instantiate(geo);snapshot.name="SourceGeographySnapshot";AssetDatabase.CreateAsset(snapshot,Folder+"/SourceGeographySnapshot.asset");
            var p=new Progress{phase="PREPARED",sourceHash=Hash(SourceScene),scene=TargetScene,sourceSize=geo.BoundsMax-geo.BoundsMin,targetSize=new Vector2(4000,6000),protectedPadding=2,gapScaleX=mapping.XAxis.BaseDerivative,gapScaleZ=mapping.ZAxis.BaseDerivative,assets=list.ToArray(),zones=zs};
            foreach(var pair in list){var clone=AssetDatabase.LoadMainAssetAtPath(pair.target);ReplaceReferences(clone,replacements);EditorUtility.SetDirty(clone);}
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),TargetScene,true);
            EditorSceneManager.OpenScene(TargetScene,OpenSceneMode.Single);
            replacements=list.ToDictionary(a=>AssetDatabase.LoadMainAssetAtPath(a.source),a=>AssetDatabase.LoadMainAssetAtPath(a.target));
            var ts=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();p.sourceObjects=ts.Length;
            foreach(var c in ts.SelectMany(t=>t.GetComponents<Component>()).Where(c=>c!=null))ReplaceReferences(c,replacements);
            var jobs=new List<MeshJob>();
            foreach(var t in ts)
            {
                var f=t.GetComponent<MeshFilter>();var c=t.GetComponent<MeshCollider>();var mesh=f!=null?f.sharedMesh:c!=null?c.sharedMesh:null;if(mesh==null)continue;
                string path=Hierarchy(t);var b=mesh.bounds;
                bool geographic=path.Contains("01_GlobalTerrain_")||path.Contains("BackgroundContext_")||path.Contains("02_Drainage_")||path.Contains("01_TerrainConformedRoutes");
                bool worldBaked=AssetDatabase.GetAssetPath(mesh).StartsWith("Assets/_Project/",StringComparison.Ordinal)&&Mathf.Max(Mathf.Abs(b.center.x),Mathf.Abs(b.center.z))>250;
                if(geographic||worldBaked)jobs.Add(new MeshJob{path=path,source=AssetDatabase.GetAssetPath(mesh),originalMatrix=t.localToWorldMatrix,filter=f!=null,collider=c!=null,road=path.Contains("01_TerrainConformedRoutes")});
            }
            p.meshes=jobs.ToArray();
            var positions=ts.Select(t=>t.position).ToArray();
            // World positions are captured before parenting changes propagate. Protected areas
            // map by translation, so every local building, rig, capsule and cave size is retained.
            for(int i=0;i<ts.Length;i++)
            {
                if(ts[i].GetComponentInParent<Canvas>()!=null)continue;
                // Vehicles retain their authored dimensions even outside a protected POI.
                // Only the root belongs to geography; local wheels/parts/sockets must not be mapped again.
                var vehicle=ts[i].GetComponentInParent<Oheangbu.App.World.Vehicle.WorldMacroPalanquinController>(true);
                if(vehicle!=null&&ts[i]!=vehicle.transform)continue;
                ts[i].position=map.Map(positions[i]);
            }
            foreach(var s in ts.SelectMany(t=>t.GetComponents<MonoBehaviour>()).Where(c=>c!=null))EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return WriteProgress(p);
        }
        static void ReplaceReferences(Object obj,Dictionary<Object,Object> replacements)
        {
            var so=new SerializedObject(obj);var prop=so.GetIterator();bool enter=true;
            while(prop.Next(enter))
            {
                enter=true;
                if(prop.propertyType==SerializedPropertyType.ObjectReference){if(prop.objectReferenceValue!=null&&replacements.TryGetValue(prop.objectReferenceValue,out var replacement))prop.objectReferenceValue=replacement;enter=false;}
                else if(prop.propertyType==SerializedPropertyType.Vector3||prop.propertyType==SerializedPropertyType.Quaternion)enter=false;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        static string GeometryStep()
        {
            RequireCompact();var p=ReadProgress();if(p.phase!="PREPARED"&&p.phase!="GEOMETRY")throw new InvalidOperationException("Prepare first.");
            var map=Compression;int end=Mathf.Min(p.meshes.Length,p.completedMeshes+4);
            for(int n=p.completedMeshes;n<end;n++)
            {
                var job=p.meshes[n];var t=Find(job.path);if(t==null)throw new InvalidOperationException("Missing mesh object "+job.path);
                var original=AssetDatabase.LoadAssetAtPath<Mesh>(job.source);if(original==null)throw new InvalidOperationException("Missing source mesh "+job.source);
                var clone=Object.Instantiate(original);clone.name=original.name+"_Compact";var v=clone.vertices;
                for(int i=0;i<v.Length;i++)v[i]=map.Map(job.originalMatrix.MultiplyPoint3x4(v[i]));
                if(job.road&&v.Length%4==0)
                {
                    var src=original.vertices;
                    for(int i=0;i<v.Length;i+=4)
                    {
                        var a=job.originalMatrix.MultiplyPoint3x4(src[i]);var b=job.originalMatrix.MultiplyPoint3x4(src[i+3]);var centre=map.Map((a+b)*.5f);var across=v[i+3]-v[i];across.y=0;if(across.sqrMagnitude<.0001f)continue;across.Normalize();
                        for(int j=0;j<4;j++){var q=job.originalMatrix.MultiplyPoint3x4(src[i+j]);float d=Vector3.Dot(q-(a+b)*.5f,Vector3.ProjectOnPlane(b-a,Vector3.up).normalized);var world=centre+across*d;world.y=v[i+j].y;v[i+j]=world;}
                    }
                }
                for(int i=0;i<v.Length;i++)v[i]=t.InverseTransformPoint(v[i]);clone.vertices=v;clone.RecalculateNormals();clone.RecalculateBounds();
                string result=Folder+"/Meshes/"+n.ToString("D3")+"_"+Path.GetFileName(job.source);if(AssetDatabase.LoadMainAssetAtPath(result)!=null)throw new InvalidOperationException("Existing mesh job output "+result);AssetDatabase.CreateAsset(clone,result);
                if(job.filter)t.GetComponent<MeshFilter>().sharedMesh=clone;if(job.collider)t.GetComponent<MeshCollider>().sharedMesh=clone;job.result=result;p.completedMeshes=n+1;
            }
            p.phase=p.completedMeshes==p.meshes.Length?"GEOMETRY_COMPLETE":"GEOMETRY";Physics.SyncTransforms();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return WriteProgress(p);
        }
    }
}
