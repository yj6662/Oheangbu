using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRecovery
    {
        [Serializable] sealed class AssemblyRow {public string path,status,anchor;public int transforms,colliders,meshes,geographicMeshes;public Vector3 sourceSize,currentSize;public float maxPositionError,maxRotationError,maxScaleError;public List<string> findings=new List<string>();}
        [Serializable] sealed class AssemblyReport {public string status,scope,sourceBefore,sourceAfter;public int sourceTransforms,targetTransforms,scaleChecks,rotationChecks;public List<string> findings=new List<string>();public List<AssemblyRow> assemblies=new List<AssemblyRow>();}
        static string StablePath(Transform t)
        {
            int ordinal=0;if(t.parent!=null)foreach(Transform sibling in t.parent){if(sibling==t)break;if(sibling.name==t.name)ordinal++;}
            return (t.parent!=null?StablePath(t.parent)+"/":"")+t.name+"["+ordinal+"]";
        }
        static string AuditAssemblies()
        {
            RequireEdit();if(Prologue.PrologueAudit.CommitRatio()>=.78f)throw new InvalidOperationException("Commit >=78%; defer simultaneous source scene loading");
            var current=SceneManager.GetActiveScene();var sourcePath=WorldMacroCompactAuthoring.SourceScene;
            var report=new AssemblyReport{scope="Source preview scene and actual compact objects, including prefab internals, inactive and non-render objects. Rigid assembly-relative transforms and collider dimensions; geographic baked meshes are separate. Does not certify doorway walking or collision placement.",sourceBefore=FileHash(sourcePath)};
            var target=current.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToDictionary(StablePath,t=>t);report.targetTransforms=target.Count;
            var progress=JsonUtility.FromJson<WorldMacroCompactAuthoring.Progress>(File.ReadAllText(Path.Combine(WorldMacroCompactAuthoring.Output,"progress.json")));
            var geographic=new HashSet<string>(progress.meshes.Select(j=>j.path));
            Scene preview=default;
            try
            {
                preview=EditorSceneManager.OpenPreviewScene(sourcePath);
                var originals=preview.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();report.sourceTransforms=originals.Length;
                var zones=JsonUtility.FromJson<WorldMacroCompactAuthoring.Zones>(File.ReadAllText(Path.Combine(WorldMacroCompactAuthoring.Output,"protected_zones.json"))).zones.Select(z=>z.path).ToHashSet();
                foreach(var t in originals)
                {
                    if(!target.TryGetValue(StablePath(t),out var actual)){report.findings.Add("Missing original object: "+StablePath(t));continue;}
                    report.scaleChecks++;report.rotationChecks++;
                    if(Vector3.Distance(t.localScale,actual.localScale)>.0001f)report.findings.Add("Scale: "+StablePath(t));
                    if(Quaternion.Angle(t.localRotation,actual.localRotation)>.05f)report.findings.Add("Rotation: "+StablePath(t));
                }
                var roots=originals.Where(t=>zones.Contains(PathOf(t))||PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)).ToArray();
                foreach(var root in roots)
                {
                    if(!target.TryGetValue(StablePath(root),out var actualRoot))continue;
                    var row=new AssemblyRow{path=StablePath(root)};report.assemblies.Add(row);
                    // Empty scene organisation roots may retain a zero pivot while their
                    // contents translate together. Compare against a physical assembly
                    // anchor, never mistake that uniform translation for a size change.
                    var anchor=root;
                    if(root.GetComponent<Renderer>()==null&&root.GetComponent<Collider>()==null)
                        anchor=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>!geographic.Contains(PathOf(t))&&(t.GetComponent<Renderer>()!=null||t.GetComponent<Collider>()!=null))??root;
                    if(!target.TryGetValue(StablePath(anchor),out var actualAnchor))continue;row.anchor=StablePath(anchor);
                    row.sourceSize=RigidSize(root,geographic);row.currentSize=RigidSize(actualRoot,geographic);
                    foreach(var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if(!target.TryGetValue(StablePath(t),out var actual)){row.findings.Add("Missing "+StablePath(t));continue;}
                        if(geographic.Contains(PathOf(t))){row.geographicMeshes++;continue;}
                        if(t==root&&anchor!=root)continue;
                        // Pure grouping pivots have no gameplay socket and do not form
                        // geometry. Leaf transforms (including sockets) stay in the audit.
                        if(t.childCount>0&&t.GetComponents<Component>().Length==1)continue;
                        row.transforms++;
                        float position=Vector3.Distance(anchor.InverseTransformPoint(t.position),actualAnchor.InverseTransformPoint(actual.position));
                        float rotation=Quaternion.Angle(Quaternion.Inverse(anchor.rotation)*t.rotation,Quaternion.Inverse(actualAnchor.rotation)*actual.rotation);
                        float scale=Vector3.Distance(t.localScale,actual.localScale);
                        row.maxPositionError=Mathf.Max(position,row.maxPositionError);row.maxRotationError=Mathf.Max(rotation,row.maxRotationError);row.maxScaleError=Mathf.Max(scale,row.maxScaleError);
                        if(position>.003f)row.findings.Add("Relative position "+StablePath(t)+" delta="+position+" active="+actual.gameObject.activeInHierarchy+" actual="+actual.position.ToString("F4")+" local="+actual.localPosition.ToString("F4")+" sourceLocal="+t.localPosition.ToString("F4"));
                        if(rotation>.05f||scale>.0001f)row.findings.Add("Relative rotation/scale "+StablePath(t));
                        var mesh=t.GetComponent<MeshFilter>();var am=actual.GetComponent<MeshFilter>();
                        if(mesh!=null)
                        {
                            row.meshes++;if(am==null||mesh.sharedMesh!=am.sharedMesh)
                            {
                                string detail="Rigid mesh binding "+StablePath(t);
                                if(am!=null&&mesh.sharedMesh!=null&&am.sharedMesh!=null)
                                {
                                    var s=mesh.sharedMesh.bounds;var a=am.sharedMesh.bounds;
                                    detail+=" source="+AssetDatabase.GetAssetPath(mesh.sharedMesh)+" target="+AssetDatabase.GetAssetPath(am.sharedMesh)+" localSizeError="+Vector3.Distance(s.size,a.size)+" anchoredCenterError="+Vector3.Distance(anchor.InverseTransformPoint(t.TransformPoint(s.center)),actualAnchor.InverseTransformPoint(actual.TransformPoint(a.center)));
                                }
                                row.findings.Add(detail);
                            }
                        }
                        var sc=t.GetComponents<Collider>();var tc=actual.GetComponents<Collider>();row.colliders+=sc.Length;
                        if(sc.Length!=tc.Length){row.findings.Add("Collider count "+StablePath(t));continue;}
                        for(int i=0;i<sc.Length;i++)
                        {
                            var a=sc[i];var b=tc[i];bool same=a.GetType()==b.GetType()&&a.isTrigger==b.isTrigger;
                            if(a is BoxCollider x&&b is BoxCollider y)same&=Vector3.Distance(x.center,y.center)<.0001f&&Vector3.Distance(x.size,y.size)<.0001f;
                            if(a is SphereCollider s&&b is SphereCollider q)same&=Vector3.Distance(s.center,q.center)<.0001f&&Mathf.Abs(s.radius-q.radius)<.0001f;
                            if(a is CapsuleCollider c&&b is CapsuleCollider d)same&=Vector3.Distance(c.center,d.center)<.0001f&&Mathf.Abs(c.radius-d.radius)<.0001f&&Mathf.Abs(c.height-d.height)<.0001f&&c.direction==d.direction;
                            if(a is MeshCollider m&&b is MeshCollider n)same&=m.sharedMesh==n.sharedMesh&&m.convex==n.convex;
                            if(!same)row.findings.Add("Collider dimensions/binding "+StablePath(t)+" / "+i);
                        }
                    }
                    row.status=row.findings.Count==0?"PASS":"REVIEW_DIFFERENCES";
                }
            }
            finally{if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);SceneManager.SetActiveScene(current);}
            report.sourceAfter=FileHash(sourcePath);report.status=report.sourceBefore==report.sourceAfter&&report.findings.Count==0&&report.assemblies.All(a=>a.status=="PASS")?"PASS":"REVIEW_DIFFERENCES";
            var json=JsonUtility.ToJson(report,true);File.WriteAllText(Path.Combine(Output,"assemblies.json"),json);return report.status+"; transforms="+report.scaleChecks+"; assemblies="+report.assemblies.Count+"; assembly differences="+report.assemblies.Count(a=>a.findings.Count>0)+"; global differences="+report.findings.Count;
        }
        static Vector3 RigidSize(Transform root,HashSet<string> geographic)
        {
            bool any=false;Bounds bounds=default;
            foreach(var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if(mf.sharedMesh==null||geographic.Contains(PathOf(mf.transform)))continue;
                var b=mf.sharedMesh.bounds;
                for(int i=0;i<8;i++)
                {
                    var p=mf.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                    if(!any){bounds=new Bounds(p,Vector3.zero);any=true;}else bounds.Encapsulate(p);
                }
            }
            return any?bounds.size:Vector3.zero;
        }
    }
}
