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
    public static class DosaV2StaticGripInspection
    {
        [Serializable] public sealed class Offset { public string name; public Quaternion offset; }
        [Serializable] public sealed class OffsetFile { public Offset[] offsets; }
        [Serializable] private sealed class Surface
        { public string name; public Vector3[] vertices; public int[] triangles; public string[] dominantBones; }
        [Serializable] private sealed class Report
        { public string status="IMPORTED_STATIC_GRIP_MEASURED_NOT_RIG_PASS"; public List<Surface> surfaces=new List<Surface>();
          public Vector3 gripPosition, tipPosition; public Quaternion gripRotation; public string note="Actual Unity BakeMesh vertices in the shared right-hand GripSocket frame; no animator controller or production clip created."; }
        public static string Capture()
        {
            if(Application.isPlaying)return "WAIT: Edit Mode required.";
            var scene=EditorSceneManager.NewPreviewScene();GameObject world=null,brush=null;
            try
            {
                world=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.WorldModel),scene);
                brush=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.BrushModel),scene);
                var bones=world.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name,StringComparer.Ordinal);
                string offsetsPath=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlayerV2/Calibration/unity-grip-offsets.json"));
                var offsets=JsonUtility.FromJson<OffsetFile>(File.ReadAllText(offsetsPath));
                foreach(var offset in offsets.offsets)bones[offset.name].localRotation *= offset.offset;
                new Oheangbu.Presentation.PlayerHandGripCorrectives(world.GetComponentsInChildren<Renderer>(true)).Apply(1f, 1f);
                Transform handGrip=bones["RightBrushGrip"];
                Transform grip=brush.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="GripSocket");
                Transform tip=brush.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="TipSocket");
                Vector3 localGrip=brush.transform.InverseTransformPoint(grip.position);
                Quaternion localRotation=Quaternion.Inverse(brush.transform.rotation)*grip.rotation;
                Quaternion desired=handGrip.rotation;
                Vector3 axisInGrip=grip.InverseTransformDirection(tip.position-grip.position).normalized;
                desired *= Quaternion.FromToRotation(axisInGrip,Vector3.up);
                brush.transform.rotation=desired*Quaternion.Inverse(localRotation);
                brush.transform.position=handGrip.position-brush.transform.TransformVector(localGrip);
                var report=new Report {gripPosition=grip.position,gripRotation=grip.rotation,tipPosition=tip.position};
                foreach(var skin in world.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name=="DosaV2_Hands")
                    .Concat(brush.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name=="DosaBrushV2_Handle")))
                {
                    var baked=new Mesh();
                    try
                    {
                        skin.BakeMesh(baked);
                        var weights=skin.sharedMesh.boneWeights;
                        report.surfaces.Add(new Surface {name=skin.name,vertices=baked.vertices.Select(v=>grip.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray(),
                            triangles=baked.triangles,dominantBones=weights.Select(w=>skin.bones[w.boneIndex0].name).ToArray()});
                    }
                    finally{Object.DestroyImmediate(baked);}
                }
                foreach(var filter in brush.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="DosaBrushV2_Handle"))
                {
                    Mesh mesh=filter.sharedMesh;
                    report.surfaces.Add(new Surface {name=filter.name,vertices=mesh.vertices.Select(v=>grip.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray(),
                        triangles=mesh.triangles,dominantBones=Enumerable.Repeat("BrushHandle",mesh.vertexCount).ToArray()});
                }
                string path=Path.GetFullPath("Screenshots/PlayerDosaV2/imported-static-grip-surfaces.json");Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,JsonUtility.ToJson(report));return path;
            }
            finally{if(world!=null)Object.DestroyImmediate(world);if(brush!=null)Object.DestroyImmediate(brush);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
