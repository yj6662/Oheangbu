using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRecovery
    {
        [Serializable] public sealed class SurfaceMesh {public string path,asset,file,hash;public bool support;}
        [Serializable] public sealed class SurfaceRoad {public string path;public float width;public Vector3[] points;}
        [Serializable] public sealed class SurfaceManifest {public string utc;public SurfaceMesh[] meshes;public SurfaceRoad[] roads;}
        static string FileHash(string path){using var sha=System.Security.Cryptography.SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        static bool GroundMesh(MeshFilter f)
        {
            string path=PathOf(f.transform);return f.GetComponent<Collider>() is MeshCollider&&
                (path.Contains("01_GlobalTerrain_IndependentOfRoads/Terrain_")||
                path.StartsWith("Playtest_NaturalCave/")&&(f.name=="Natural_Cave_Floor"||f.name=="Continuous_Approach_Soil"||f.name=="Portal_Outer_Soil"||f.name=="Exterior_Mine_Approach"||f.name=="Natural_Mine_Broad_Apron")||
                f.name=="Bridge_Deck_TEST"||f.name.StartsWith("Bridge_Apron_TEST_"));
        }
        static string ExportSurfaces()
        {
            RequireEdit();if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Commit guard");
            var session=Find<WorldMacroPlaytestSession>();var geo=Find<Oheangbu.App.World.Vehicle.WorldMacroPalanquinSummon>().WorldSheet;
            var corridor=AssetDatabase.LoadAssetAtPath<WorldMacroVisualCorridorSO>(WorldMacroCompactAuthoring.Folder+"/Data/07_VisualCorridor.asset");
            if(corridor==null)throw new InvalidOperationException("Compact corridor profile missing");
            var filters=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.sharedMesh!=null&&f.gameObject.activeInHierarchy).ToArray();
            string folder=Path.Combine(Output,"SurfaceInput");Directory.CreateDirectory(folder);var rows=new List<SurfaceMesh>();var roads=new List<SurfaceRoad>();
            foreach(var f in filters.Where(f=>GroundMesh(f)||PathOf(f.transform).Contains("01_TerrainConformedRoutes/")||PathOf(f.transform)=="Playtest_Village_Office/Courtyard"))
            {
                var mesh=f.sharedMesh;string asset=AssetDatabase.GetAssetPath(mesh);string file=Path.Combine(folder,rows.Count.ToString("D3")+".bin");
                using(var w=new BinaryWriter(File.Create(file)))
                {
                    var v=mesh.vertices;var ts=mesh.triangles;var uv=mesh.uv;w.Write(v.Length);w.Write(ts.Length);
                    foreach(var p in v){var q=f.transform.TransformPoint(p);w.Write(q.x);w.Write(q.y);w.Write(q.z);}foreach(int t in ts)w.Write(t);
                    foreach(var p in uv.Length==v.Length?uv:new Vector2[v.Length]){w.Write(p.x);w.Write(p.y);}
                }
                rows.Add(new SurfaceMesh{path=PathOf(f.transform),asset=asset,file=file,hash=FileHash(asset),support=GroundMesh(f)});
                if(!PathOf(f.transform).Contains("01_TerrainConformedRoutes/"))continue;
                Vector3[] points;float width;
                if(f.name=="Mine_To_Inn"){points=session.Content.MainPath;width=corridor.TrailWidth;}
                else if(f.name=="Inn_To_DeepForest"){points=geo.Routes.Single(r=>r.Id=="Trail_Inn_Logging").Points.Concat(geo.Routes.Single(r=>r.Id=="Trail_Logging_Deep").Points.Skip(1)).ToArray();width=corridor.TrailWidth;}
                else {var route=geo.Routes.Single(r=>r.Id==f.name);points=route.Points;width=Mathf.Min(5.6f,route.Width*.68f);}
                roads.Add(new SurfaceRoad{path=PathOf(f.transform),points=points,width=width});
            }
            if(roads.Count!=8)throw new InvalidOperationException("Expected8 roads");
            var manifest=new SurfaceManifest{utc=DateTime.UtcNow.ToString("o"),meshes=rows.ToArray(),roads=roads.ToArray()};
            File.WriteAllText(Path.Combine(Output,"surface_manifest.json"),JsonUtility.ToJson(manifest,true));return rows.Count+" saved surface meshes exported;8 road centrelines; courtyard";
        }
        static string ImportSurfaces()
        {
            RequireEdit();if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Commit guard");
            var manifest=JsonUtility.FromJson<SurfaceManifest>(File.ReadAllText(Path.Combine(Output,"surface_manifest.json")));
            foreach(var row in manifest.meshes)if(FileHash(row.asset)!=row.hash)throw new InvalidOperationException("Surface changed after export: "+row.asset);
            var targets=new HashSet<string>(manifest.meshes.Where(r=>!r.support).Select(r=>r.path));
            var filters=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>targets.Contains(PathOf(f.transform))).ToDictionary(f=>PathOf(f.transform),f=>f);
            string folder=WorldMacroCompactAuthoring.Folder+"/Recovery";Directory.CreateDirectory(folder);var plans=new List<(MeshFilter filter,Mesh mesh,string path)>();
            foreach(var row in manifest.meshes.Where(r=>!r.support))
            {
                var filter=filters[row.path];string file=Path.Combine(Output,"SurfaceOutput",Path.GetFileName(row.file));
                if(!File.Exists(file))throw new IOException("Missing derived surface "+file);
                using var reader=new BinaryReader(File.OpenRead(file));int count=reader.ReadInt32(),indices=reader.ReadInt32();if(count<3||count>2000000||indices<3||indices%3!=0)throw new InvalidDataException(file);
                var v=new Vector3[count];var uv=new Vector2[count];var colors=new Color[count];var ts=new int[indices];
                for(int i=0;i<count;i++)v[i]=filter.transform.InverseTransformPoint(new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()));
                for(int i=0;i<indices;i++){ts[i]=reader.ReadInt32();if(ts[i]<0||ts[i]>=count)throw new InvalidDataException("Index");}
                for(int i=0;i<count;i++)uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());
                for(int i=0;i<count;i++)colors[i]=new Color(1,1,1,reader.ReadSingle());
                var valid=new List<int>(indices);int slivers=0;float sliverArea=0;
                for(int i=0;i<indices;i+=3)
                {
                    float area=Vector3.Cross(v[ts[i+1]]-v[ts[i]],v[ts[i+2]]-v[ts[i]]).y;
                    // Float conversion into a translated local frame can collapse sub-mm
                    // clipping slivers. Reject genuine inversions; record tiny discarded area.
                    if(Mathf.Abs(area)<=.0001f){slivers++;sliverArea+=Mathf.Abs(area)*.5f;continue;}
                    if(area<0)throw new InvalidDataException("Inverted triangle "+file+" area="+area);
                    valid.Add(ts[i]);valid.Add(ts[i+1]);valid.Add(ts[i+2]);
                }
                ts=valid.ToArray();File.AppendAllText(Path.Combine(Output,"surface_import.txt"),row.path+": float slivers="+slivers+", area="+sliverArea+"m2\n");
                var mesh=new Mesh{name=filter.name+"_Supported",indexFormat=IndexFormat.UInt32,vertices=v,triangles=ts,uv=uv,colors=colors};mesh.RecalculateNormals();mesh.RecalculateBounds();
                string path=folder+"/"+filter.name+"_Supported.asset";if(AssetDatabase.LoadMainAssetAtPath(path)!=null)throw new InvalidOperationException("Derivative already exists: "+path);
                plans.Add((filter,mesh,path));
            }
            foreach(var p in plans){AssetDatabase.CreateAsset(p.mesh,p.path);p.filter.sharedMesh=p.mesh;EditorUtility.SetDirty(p.filter);}
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return "Applied "+plans.Count+" derived display meshes; source saved; collider geometry unchanged. Support gaps remain explicit in surface_build.json.";
        }
    }
}
