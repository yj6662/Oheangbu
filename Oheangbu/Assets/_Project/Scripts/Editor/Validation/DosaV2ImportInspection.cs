using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class DosaV2ImportInspection
    {
        [Serializable] private sealed class BoneRecord
        { public string name, parent; public Vector3 position, localPosition, scale; public Quaternion rotation, localRotation; }
        [Serializable] private sealed class MeshRecord
        { public string name; public int vertices, triangles, submeshes, materialSlots; public string[] blendShapes; public Vector3 min, max; }
        [Serializable] private sealed class Report
        { public string status="STATIC_IMPORT_MEASURED_NOT_RIG_PASS"; public BoneRecord[] bones; public List<MeshRecord> meshes=new List<MeshRecord>(); public Vector3 minimum, maximum; }
        public static string Capture()
        {
            if(Application.isPlaying) return "WAIT: Edit Mode required.";
            var scene=EditorSceneManager.NewPreviewScene(); GameObject root=null;
            try
            {
                root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.WorldModel),scene);
                var report=new Report();
                report.bones=root.GetComponentsInChildren<Transform>(true).Select(t=>new BoneRecord {
                    name=t.name,parent=t.parent!=null?t.parent.name:null,position=t.position,rotation=t.rotation,
                    localPosition=t.localPosition,localRotation=t.localRotation,scale=t.localScale}).ToArray();
                report.minimum=Vector3.one*float.PositiveInfinity;report.maximum=Vector3.one*float.NegativeInfinity;
                foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh=new Mesh();
                    try
                    {
                        skin.BakeMesh(mesh);
                        var record=new MeshRecord {name=skin.name,vertices=mesh.vertexCount,triangles=mesh.triangles.Length/3,
                            submeshes=mesh.subMeshCount,materialSlots=skin.sharedMaterials.Length,
                            blendShapes=Enumerable.Range(0,skin.sharedMesh.blendShapeCount).Select(skin.sharedMesh.GetBlendShapeName).ToArray(),
                            min=Vector3.one*float.PositiveInfinity,max=Vector3.one*float.NegativeInfinity};
                        foreach(var p in mesh.vertices){Vector3 q=skin.transform.TransformPoint(p);record.min=Vector3.Min(record.min,q);record.max=Vector3.Max(record.max,q);}
                        report.meshes.Add(record);report.minimum=Vector3.Min(report.minimum,record.min);report.maximum=Vector3.Max(report.maximum,record.max);
                    }
                    finally{Object.DestroyImmediate(mesh);}
                }
                string path=Path.GetFullPath("Screenshots/PlayerDosaV2/import-bones-and-surfaces.json");Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,JsonUtility.ToJson(report,true));return path+"; actual baked height="+(report.maximum.y-report.minimum.y).ToString("F6");
            }
            finally{if(root!=null)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
