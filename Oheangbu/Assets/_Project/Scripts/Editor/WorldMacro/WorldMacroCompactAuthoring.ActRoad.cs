using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        public static string RepairActSeamGround()
        {
            RequireCompact();if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%");
            var job=ReadProgress().meshes.Single(j=>j.path.EndsWith("/Terrain_050"));var target=Find(job.path);var collider=target.GetComponent<MeshCollider>();
            Vector3 center=new Vector3(776.4f,135,250.1f);
            float Height(Vector3 p){if(!collider.Raycast(new Ray(p+Vector3.up*8,Vector3.down),out var hit,16))throw new InvalidOperationException("Local terrain support missing");return hit.point.y;}
            float level=(Height(center+Vector3.right)+Height(center-Vector3.right)+Height(center+Vector3.forward)+Height(center-Vector3.forward))/4;
            string path=Folder+"/ActsTerrain/Terrain_050_SeamGround.asset";if(AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)return "Seam terrain derivative exists";
            Mesh mesh=null;
            try
            {
                actRefinementRegion=Rect.MinMaxRect(771,245,782,256);
                mesh=BuildRefinedTerrain(collider.sharedMesh,job,target,Grade,out _,true);
                var vertices=mesh.vertices;int changed=0;float largest=0;
                for(int i=0;i<vertices.Length;i++)
                {
                    var p=target.TransformPoint(vertices[i]);float r=new Vector2(p.x-center.x,p.z-center.z).magnitude;if(r>=4)continue;
                    float blend=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.6f,4,r));float y=Mathf.Lerp(p.y,level,blend);
                    largest=Mathf.Max(largest,Mathf.Abs(y-p.y));p.y=y;vertices[i]=target.InverseTransformPoint(p);changed++;
                }
                if(largest>2f)throw new InvalidOperationException("Local seam correction exceeds 2m safety envelope: "+largest);
                mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.name="Terrain_050_ActSeamGround";
                AssetDatabase.CreateAsset(mesh,path);AssetDatabase.SaveAssets();target.GetComponent<MeshFilter>().sharedMesh=mesh;collider.sharedMesh=mesh;
                EditorUtility.SetDirty(collider);EditorUtility.SetDirty(target.GetComponent<MeshFilter>());EditorSceneManager.MarkSceneDirty(target.gameObject.scene);EditorSceneManager.SaveScene(target.gameObject.scene);
                return "Smoothed actual terrain within 4m of broken seam; preserved geometry outside footprint. Vertices="+changed+", maxHeightChange="+largest+", level="+level+". Navigation rebake and physical recheck required.";
            }
            finally{actRefinementRegion=null;if(mesh!=null&&!AssetDatabase.Contains(mesh))Object.DestroyImmediate(mesh);}
        }
        public static string RefineActRoadEdge()
        {
            RequireCompact();if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%");
            var progress=ReadProgress();var job=progress.meshes.Single(j=>j.path.EndsWith("/Terrain_050"));var target=Find(job.path);
            var original=AssetDatabase.LoadAssetAtPath<Mesh>(job.source);if(original==null||job.source.StartsWith(Folder))throw new InvalidOperationException("Immutable source missing");
            string path=Folder+"/ActsTerrain/Terrain_050_RoadEdge.asset";
            if(AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)return "Local road-edge derivative already exists; run the physical audit";
            Mesh mesh=null;
            try
            {
                actRefinementRegion=Rect.MinMaxRect(740,112,765,158);
                mesh=BuildRefinedTerrain(original,job,target,Grade,out _);mesh.name="Terrain_050_LocalRoadEdge";
                AssetDatabase.CreateAsset(mesh,path);AssetDatabase.SaveAssets();
                target.GetComponent<MeshFilter>().sharedMesh=mesh;target.GetComponent<MeshCollider>().sharedMesh=mesh;
                EditorUtility.SetDirty(target.GetComponent<MeshFilter>());EditorUtility.SetDirty(target.GetComponent<MeshCollider>());
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);EditorSceneManager.SaveScene(target.gameObject.scene);
                return "Rebuilt Terrain_050 from immutable source; only 25×46m road-edge refinement uses 0.5m edges. Old compact mesh preserved; physical recheck required.";
            }
            finally{actRefinementRegion=null;}
        }
    }
}
