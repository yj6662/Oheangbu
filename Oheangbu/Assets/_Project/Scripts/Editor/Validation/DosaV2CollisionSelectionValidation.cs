using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Read-only source/selection audit on a disposable static preview.
    /// No native Cloth solve, animation/controller, gameplay input or gate promotion.</summary>
    public static class DosaV2CollisionSelectionValidation
    {
        public static string Run()
        {
            if(Application.isPlaying)return "WAIT: Edit Mode required.";
            var report=new JObject { ["status"]="RUNNING",["rigGate"]="NOT_GRANTED",["restored"]=false,["samples"]=new JArray(),
                ["scope"]="22 distinct direct bone poses on a disposable imported prefab. Full BakeMesh anatomical vertices and triangle barycentric samples versus current fitted capsules, plus full-source collision-selection equivalence. No physical stability approval." };
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../Screenshots/PlayerDosaV2/collision-selection-validation.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            report["path"]=path;var scene=EditorSceneManager.NewPreviewScene();GameObject fixture=null;Mesh baked=null;
            try
            {
                JObject definitions=JObject.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlayerV2/Validation/static-pose-definitions.json"))));
                report["poseSourceSha256"]=definitions["sourceSha256"];
                fixture=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.ValidationPrefab),scene);
                foreach(var cloth in fixture.GetComponentsInChildren<Cloth>(true))cloth.enabled=false;
                foreach(var lod in fixture.GetComponentsInChildren<PlayerLodClothController>(true))lod.SuspendForStaticFixture();
                var body=fixture.GetComponentInChildren<PlayerClothBodyProxyRig>(true);
                Need(body!=null,"New actual-body fitting component is not serialized.");
                Transform world=body.transform;var budget=world.GetComponent<PlayerClothCollisionBudgetRig>();var brush=world.GetComponent<PlayerClothBrushProxyRig>();
                Need(budget!=null&&brush!=null,"Complete explicit candidate chain required.");
                Need(world.GetComponentsInChildren<Animator>(true).All(a=>a.runtimeAnimatorController==null),"Production controller is prohibited.");
                var poses=(JArray)definitions["poses"];var rest=ReadMatrices((JObject)poses.Single(p=>(string)p["id"]=="rest"));
                var bones=world.GetComponentsInChildren<Transform>(true).Where(t=>rest.ContainsKey(Normalize(t.name))).ToDictionary(t=>Normalize(t.name));
                Need(bones.Count==rest.Count,"Every defined imported bone must have one unambiguous match.");
                var rotations=bones.ToDictionary(p=>p.Key,p=>Quaternion.Inverse(world.rotation)*p.Value.rotation);
                var ordered=bones.OrderBy(p=>Depth(p.Value)).ToArray();
                var correctives=new PlayerHandGripCorrectives(world.GetComponentsInChildren<Renderer>(true));
                var anatomical=world.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name=="DosaV2_ArmLining_Left"||r.name=="DosaV2_ArmLining_Right")
                    .Where(r=>r.transform.parent!=null && !r.transform.parent.name.Contains("LOD")).ToArray();
                // LOD clones share names. The serialized body binding's source is the LOD0
                // renderer outside the generated level container, resolved by source mesh asset.
                anatomical=anatomical.Where(r=>AssetDatabase.GetAssetPath(r.sharedMesh)==DosaV2PlayerBuilder.WorldModel).ToArray();
                Need(anatomical.Length==2,"Exactly two LOD0 anatomical sources required.");baked=new Mesh();
                var fixtures=world.GetComponentsInChildren<CapsuleCollider>(true).Where(c=>c.name.Contains("ArmLining_")&&c.name.Contains("_Posed_")).ToArray();
                Need(fixtures.Length==6,"Six source-fitted arm capsules required.");
                foreach(JObject pose in poses)
                {
                    var matrices=ReadMatrices(pose);
                    foreach(var pair in ordered)
                    {
                        Matrix4x4 matrix=matrices[pair.Key],delta=UnityEditorValidationV2StaticPoseAudit.BlenderRotationDelta(rest[pair.Key],matrix);
                        Vector4 p=matrix.GetColumn(3);
                        pair.Value.SetPositionAndRotation(world.TransformPoint(new Vector3(-p.x,p.z,-p.y)),world.rotation*delta.rotation*rotations[pair.Key]);
                    }
                    correctives.Apply((float)pose["handGripCorrectives"]["right"],(float)pose["handGripCorrectives"]["left"]);
                    Need(body.RefreshNow(),body.LastError);Need(brush.RefreshNow(),brush.LastError);Need(budget.RefreshNow(),budget.LastError);
                    var row=new JObject { ["id"]=pose["id"],["bodyFitCpuMilliseconds"]=body.LastRefreshMilliseconds,["bodyFitAllocatedBytes"]=body.LastRefreshAllocatedBytes,
                        ["fittedRegions"]=JObject.Parse(JsonUtility.ToJson(new Fits { items=body.Measurements }))["items"] };
                    var coverage=new JArray();
                    foreach(var renderer in anatomical)
                    {
                        renderer.BakeMesh(baked,false);Vector3[] local=baked.vertices;var vertices=local.Select(renderer.transform.TransformPoint).ToArray();
                        var capsules=fixtures.Where(c=>c.name.Contains(renderer.name.Replace("DosaV2_", ""))).Select(PlayerClothCollisionBudgetRig.ReadCapsule).ToArray();
                        float max=0f;int missed=0,samples=0;
                        void Measure(Vector3 p)
                        {
                            float gap=float.PositiveInfinity;
                            foreach(var c in capsules){Vector3 d=c.End-c.Start;float t=d.sqrMagnitude>1e-14f?Mathf.Clamp01(Vector3.Dot(p-c.Start,d)/d.sqrMagnitude):0f;gap=Mathf.Min(gap,(p-c.Start-d*t).magnitude-c.Radius);}
                            max=Mathf.Max(max,gap);if(gap>.00001f)missed++;samples++;
                        }
                        foreach(var p in vertices)Measure(p);
                        int[] triangles=baked.triangles;
                        for(int t=0;t<triangles.Length;t+=3)
                            for(int a=0;a<=6;a++)for(int b=0;b<=6-a;b++)
                                Measure((vertices[triangles[t]]*a+vertices[triangles[t+1]]*b+vertices[triangles[t+2]]*(6-a-b))/6f);
                        coverage.Add(new JObject { ["renderer"]=renderer.name,["vertices"]=vertices.Length,["triangleSamplesPlusVertices"]=samples,["outside10umSamples"]=missed,["maximumOutsideMeters"]=max });
                    }
                    row["actualBakedCoverage"]=coverage;
                    row["bodyFitAudit"]=JObject.Parse(JsonUtility.ToJson(body.AuditAgainstBakedSources(16)));
                    row["unrelatedBoneCache"]=AuditUnrelatedBone(world,anatomical,body,baked);
                    row["selection"]=JObject.Parse(JsonUtility.ToJson(budget.AuditAgainstFullSource(16)));
                    ((JArray)report["samples"]).Add(row);File.WriteAllText(path,report.ToString());
                }
                report["status"]="COMPLETE_MEASURED";
            }
            catch(Exception e){report["status"]="FAIL";report["error"]=e.ToString();}
            finally
            {
                if(baked!=null)Object.DestroyImmediate(baked);if(fixture!=null)Object.DestroyImmediate(fixture);
                EditorSceneManager.ClosePreviewScene(scene);report["restored"]=true;Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,report.ToString());
            }
            return new JObject { ["status"]=report["status"],["error"]=report["error"],["samples"]=((JArray)report["samples"]).Count,["path"]=path,["restored"]=report["restored"] }.ToString();
        }
        [Serializable] private sealed class Fits{public PlayerClothBodyProxyRig.Snapshot[] items;}
        private static JObject AuditUnrelatedBone(Transform world,SkinnedMeshRenderer[] anatomical,PlayerClothBodyProxyRig body,Mesh baked)
        {
            var affecting=new HashSet<Transform>();
            foreach(var renderer in anatomical)
                using(var weights=renderer.sharedMesh.GetAllBoneWeights())
                    foreach(var w in weights)if(w.weight>0f)
                        for(Transform t=renderer.bones[w.boneIndex];t!=world;t=t.parent){Need(t!=null,"Bone outside representation.");affecting.Add(t);}
            Transform candidate=world.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>Normalize(t.name)=="RightHandPinky3"&&!affecting.Contains(t));
            Need(candidate!=null,"Expected independent diagnostic finger does not exist; update test explicitly.");
            var before=new List<Vector3[]>();foreach(var r in anatomical){r.BakeMesh(baked,false);before.Add(baked.vertices);}
            Quaternion saved=candidate.localRotation;int count=body.FitCount;float delta=0f;bool reused=false;
            try
            {
                candidate.localRotation=saved*Quaternion.AngleAxis(11f,Vector3.up);Need(body.RefreshNow(),body.LastError);reused=body.FitCount==count;
                for(int r=0;r<anatomical.Length;r++){anatomical[r].BakeMesh(baked,false);Vector3[] after=baked.vertices;Need(after.Length==before[r].Length,"Anatomy index mismatch.");for(int i=0;i<after.Length;i++)delta=Mathf.Max(delta,(after[i]-before[r][i]).magnitude);}
            }
            finally{candidate.localRotation=saved;Need(body.RefreshNow(),body.LastError);}
            Need(reused&&delta<=.00001f,"Unrelated bone invalidates body fit or unexpectedly deforms its source.");
            return new JObject{["bone"]=candidate.name,["fitReused"]=reused,["actualBakedDeltaMeters"]=delta};
        }
        private static string Normalize(string name){int colon=name.LastIndexOf(':');return colon>=0?name.Substring(colon+1):name;}
        private static int Depth(Transform t){int d=0;while(t.parent!=null){d++;t=t.parent;}return d;}
        private static Dictionary<string,Matrix4x4> ReadMatrices(JObject pose)
        {
            var output=new Dictionary<string,Matrix4x4>();
            foreach(var p in ((JObject)pose["boneMatricesRigLocal"]).Properties())
            {var m=Matrix4x4.zero;for(int r=0;r<4;r++)for(int c=0;c<4;c++)m[r,c]=(float)p.Value[r][c];Need(Mathf.Abs(m.determinant-1f)<.001f,"Invalid pose basis.");output.Add(p.Name,m);}
            return output;
        }
        private static void Need(bool condition,string error){if(!condition)throw new InvalidOperationException(error);}
    }
}
