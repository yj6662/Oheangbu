using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        public const string SourceScene="Assets/_Project/Scenes/World/W_Demo_Campaign.unity";
        public const string TargetScene="Assets/_Project/Scenes/World/W_Demo_Compact.unity";
        public const string Folder="Assets/_Project/Art/World/WorldCompact";
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/World/WorldMacro/Compact"));
        [Serializable] public sealed class BoundRecord {public string path; public Vector3 position,min,max;public int renderers,colliders,transforms;}
        [Serializable] public sealed class FieldRecord {public string owner,type,path,kind,value;}
        [Serializable] public sealed class SurveyReport {public string status,scene;public bool dirty;public int objects;public BoundRecord[] groups;public FieldRecord[] fields;public string[] assets;}
        public static string Execute(string command)
        {
            Directory.CreateDirectory(Output);
            if(command.StartsWith("recovery:"))return CompactRecovery.Execute(command.Substring(9));
            if(command=="vehicle-audit")return CompactVehicleRestoration.Run(false);
            if(command=="vehicle-repair")return CompactVehicleRestoration.Run(true);
            if(command.StartsWith("acts:"))return WorldMacroActAuthoring.Execute(command.Substring(5));
            if(command=="survey")return Survey();
            if(command=="prepare")return Prepare();
            if(command=="geometry-step")return GeometryStep();
            if(command=="coordinates")return Coordinates();
            if(command=="map-step")return MapStep();
            if(command=="materials")return Materials();
            if(command=="references")return RepairSceneReferences();
            if(command=="inn-pivots")return RepairInnPivots();
            if(command=="grade-begin")return BeginGrade();
            if(command=="relief-import")return ImportRelief();
            if(command=="relief-context-step")return ReliefContextStep();
            if(command=="terrain-normals")return WeldTerrainNormals();
            if(command=="grade-reset")return ResetGrade();
            if(command=="grade-patch")return PatchGrade();
            if(command=="grade-step")return StepGrade();
            if(command=="grade-checks")return GradeChecks();
            if(command=="grade-attachments")return FinalizeGradeAttachments();
            if(command=="corridors")return ApplyCorridorAttachments();
            if(command=="road-begin")return WorldMacroCompactRoadAudit.Begin();
            if(command=="road-step")return WorldMacroCompactRoadAudit.Step();
            if(command=="road-status")return WorldMacroCompactRoadAudit.Status();
            if(command.StartsWith("ground:"))return ProbeGround(command.Substring(7));
            if(command=="nav-prepare")return WorldMacroCompactNavigation.Prepare();
            if(command=="nav-step")return WorldMacroCompactNavigation.Step();
            if(command=="nav-audit")return WorldMacroCompactNavigation.Audit();
            if(command=="nav-seam-propose")return WorldMacroCompactNavigation.ProposeSeamRepairs();
            if(command=="nav-seam-apply")return WorldMacroCompactNavigation.ApplySeamRepairs();
            if(command=="dressing-begin")return BeginDressing();
            if(command=="dressing-step")return StepDressing();
            if(command=="dressing-finish")return FinishDressing();
            if(command=="audit")return AuditCompact();
            if(command=="title")return PrepareTitle();
            if(command=="smoke-start")return RuntimeSmokeStart();
            if(command=="smoke-status")return RuntimeSmokeStatus();
            if(command=="smoke-stop")return RuntimeSmokeStop();
            if(command.StartsWith("capture:"))return CaptureCompact(command.Substring(8));
            if(command.StartsWith("capture-terrain:"))return CaptureCompact(command.Substring(16),true);
            if(command=="status")return File.ReadAllText(Path.Combine(Output,"progress.json"));
            throw new ArgumentException("compact:survey");
        }
        static string Hierarchy(Transform t)=>t.parent==null?t.name:Hierarchy(t.parent)+"/"+t.name;
        static string ProbeGround(string coordinates)
        {
            RequireCompact();var values=coordinates.Split(',').Select(s=>float.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var p=new Vector3(values[0],values[1],values[2]);Physics.SyncTransforms();
            var hits=Physics.RaycastAll(p+Vector3.up*1500,Vector3.down,3000,~0,QueryTriggerInteraction.Ignore);
            var lines=hits.OrderBy(h=>Mathf.Abs(h.point.y-p.y)).Take(12).Select(h=>$"{Hierarchy(h.collider.transform)} | y={h.point.y:R} normal={h.normal} | {h.collider.GetType().Name}");
            return string.Join("\n",lines);
        }
        static bool SceneObject(Component c)=>c!=null&&c.gameObject.scene==SceneManager.GetActiveScene();
        static void RequireSource()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=SourceScene)
                throw new InvalidOperationException("Source campaign scene in Edit mode required.");
        }
        static string Survey()
        {
            RequireSource();var scene=SceneManager.GetActiveScene();var groups=new List<BoundRecord>();var fields=new List<FieldRecord>();
            var ts=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach(var t in ts)
            {
                if(t.parent!=null&&t.parent.parent!=null&&t.parent.parent.parent!=null)continue;
                var renderers=t.GetComponentsInChildren<Renderer>(true).Where(r=>!r.GetComponentInParent<Canvas>()).ToArray();
                var bounds=new Bounds(t.position,Vector3.zero);bool first=true;
                foreach(var r in renderers){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
                groups.Add(new BoundRecord{path=Hierarchy(t),position=t.position,min=bounds.min,max=bounds.max,renderers=renderers.Length,colliders=t.GetComponentsInChildren<Collider>(true).Length,transforms=t.GetComponentsInChildren<Transform>(true).Length});
            }
            var assets=AssetDatabase.GetDependencies(scene.path,true).Where(p=>p.EndsWith(".asset",StringComparison.OrdinalIgnoreCase)).ToArray();
            var targets=ts.SelectMany(t=>t.GetComponents<MonoBehaviour>()).Where(c=>c!=null).Cast<Object>().Concat(assets.Select(AssetDatabase.LoadMainAssetAtPath).Where(o=>o is ScriptableObject));
            foreach(var target in targets)
            {
                var so=new SerializedObject(target);var p=so.GetIterator();string owner=target is Component c?Hierarchy(c.transform):AssetDatabase.GetAssetPath(target);
                bool enter=true;
                while(p.NextVisible(enter))
                {
                    enter=true;
                    if(p.propertyType==SerializedPropertyType.Vector3||p.propertyType==SerializedPropertyType.Vector2||p.propertyType==SerializedPropertyType.Bounds||p.propertyType==SerializedPropertyType.Rect)
                    {fields.Add(new FieldRecord{owner=owner,type=target.GetType().Name,path=p.propertyPath,kind=p.propertyType.ToString(),value=p.propertyType==SerializedPropertyType.Vector3?p.vector3Value.ToString("F3"):p.propertyType==SerializedPropertyType.Vector2?p.vector2Value.ToString("F3"):p.propertyType==SerializedPropertyType.Bounds?p.boundsValue.ToString():p.rectValue.ToString()});enter=false;}
                    if(p.isArray&&p.arraySize>10000)enter=false;
                }
            }
            var report=new SurveyReport{status="READ_ONLY_SCENE_SURVEY",scene=scene.path,dirty=scene.isDirty,objects=ts.Length,groups=groups.ToArray(),fields=fields.ToArray(),assets=assets};
            string json=JsonUtility.ToJson(report,true);File.WriteAllText(Path.Combine(Output,"scene_survey.json"),json);
            return JsonUtility.ToJson(new SurveyReport{status=report.status,scene=report.scene,dirty=report.dirty,objects=report.objects},true);
        }
    }
}
