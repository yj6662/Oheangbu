using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactKcisaReplacement
    {
        public static string Output=>Path.Combine(WorldMacroCompactAuthoring.Output,"KcisaReplacement");
        static string Folder=>WorldMacroCompactAuthoring.Folder+"/KcisaReplacement";
        static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        static IEnumerable<Transform> All=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true));
        static void Guard()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)throw new InvalidOperationException("Compact Edit scene required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%, stopped");
            Directory.CreateDirectory(Output);
        }
        public static string Execute(string command)
        {
            Guard();
            if(command=="audit")return Audit();
            if(command=="buildings")return Buildings();
            if(command=="walls")return Walls();
            if(command=="actors")return Actors();
            if(command=="surfaces")return Surfaces();
            if(command=="cap-repair")return RepairStoneCaps();
            if(command=="surface-check")return InspectSurfaces();
            if(command=="ground-desaturate")return DesaturateGround();
            if(command=="animation-check")return CheckActorBindings();
            if(command=="props")return Props();
            if(command=="props-refine")return RefineProps();
            if(command=="actor-motion")return ActorMotion();
            if(command=="actor-refine")return RefineActors();
            if(command.StartsWith("capture:"))return Capture(command.Substring(8));
            if(command=="verify")return Verify();
            throw new ArgumentException(command);
        }
        [Serializable] sealed class Row
        {
            public string path,mesh,meshName,prefab,classification; public string[] materials,components;
            public bool active,enabled;public long triangles;public Vector3 position,size,scale;
        }
        [Serializable] sealed class AuditReport
        {
            public string scene,utc;public bool dirty;public float commit;public string[] roots;public Row[] renderers;
        }
        static string Audit()
        {
            var scene=SceneManager.GetActiveScene();
            if(!File.Exists(Path.Combine(Output,"before_disk.unity")))File.Copy(scene.path,Path.Combine(Output,"before_disk.unity"));
            if(!File.Exists(Path.Combine(Output,"before_open.unity"))&&!EditorSceneManager.SaveScene(scene,Path.Combine(Output,"before_open.unity"),true))throw new IOException("Open scene backup failed");
            var list=new List<Row>();
            foreach(var renderer in All.Select(t=>t.GetComponent<Renderer>()).Where(r=>r!=null))
            {
                var filter=renderer.GetComponent<MeshFilter>();
                Mesh mesh=renderer is SkinnedMeshRenderer sk?sk.sharedMesh:filter!=null?filter.sharedMesh:null;
                string asset=mesh==null?"":AssetDatabase.GetAssetPath(mesh);var t=renderer.transform;
                var row=new Row{path=PathOf(t),mesh=asset,meshName=mesh==null?"":mesh.name,prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(renderer),
                    active=renderer.gameObject.activeInHierarchy,enabled=renderer.enabled,position=t.position,size=renderer.bounds.size,scale=t.lossyScale,
                    materials=renderer.sharedMaterials.Select(m=>m==null?"MISSING":AssetDatabase.GetAssetPath(m)).ToArray(),
                    components=t.GetComponents<Component>().Select(c=>c==null?"MISSING":c.GetType().FullName).ToArray()};
                if(mesh!=null)for(int i=0;i<mesh.subMeshCount;i++)if(mesh.GetTopology(i)==MeshTopology.Triangles)row.triangles+=(long)mesh.GetIndexCount(i)/3;
                row.classification=mesh==null?"NON_MESH":asset.Contains("unity_builtin")||asset.Contains("unity default")?"PRIMITIVE":asset.StartsWith("Assets/_Project/")?"PROJECT_DERIVATIVE_REVIEW_SOURCE":asset.StartsWith("Assets/")?"PROVIDER":"GENERATED_REVIEW";
                list.Add(row);
            }
            var report=new AuditReport{scene=scene.path,utc=DateTime.UtcNow.ToString("O"),dirty=scene.isDirty,commit=Prologue.PrologueAudit.CommitRatio(),roots=scene.GetRootGameObjects().Select(r=>r.name).ToArray(),renderers=list.ToArray()};
            File.WriteAllText(Path.Combine(Output,"scene_inventory.json"),JsonUtility.ToJson(report,true));
            return "Saved read-only inventory of "+list.Count+" renderers; active enabled "+list.Count(r=>r.active&&r.enabled)+". Open/disk scene preserved separately.";
        }
    }
}
