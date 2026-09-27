using System;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEditor;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        static string SyncActMap()
        {
            var ui=Components<PlaytestUiRoot>().Single();var data=ui.MapData;
            if(data==null||!AssetDatabase.GetAssetPath(data).StartsWith(WorldMacroCompactAuthoring.Folder+"/"))throw new InvalidOperationException("Private compact map required");
            var generated=WorldMapRuntimeDataFactory.BuildLines(ui.WorldSheet,Session.Content,data.Zones).Where(l=>l.Id.StartsWith("Playtest_MainPath_")).ToArray();
            if(generated.Length==0)throw new InvalidOperationException("No actual surface path segments");
            data.Lines=data.Lines.Where(l=>!l.Id.StartsWith("Playtest_MainPath_")).Concat(generated).ToArray();
            EditorUtility.SetDirty(data);AssetDatabase.SaveAssetIfDirty(data);
            return Write("map_routes.json",new Report{status="PASS_VECTOR_SYNC",scope="Only private compact outdoor MainPath vectors replaced using actual scene route. Geography/illustration/interior map/fog/undiscovered POIs preserved.",checks={"Segments="+generated.Length,"Shortcut remains discovery-gated runtime vector","Asset="+AssetDatabase.GetAssetPath(data)}});
        }
        static string CheckShortcut()
        {
            var shortcut=Components<WorldActShortcut>().Single();var report=new Report{status="PASS_PHYSICS_SAMPLES",scope="Existing 1.8m-wide descent: actual static support and 0.54m standing capsule at centre/0.5m sides every authored row (1.54m corridor); no native Guk usage or visual approval."};
            for(int i=0;i<shortcut.Path.Length;i++)
            {
                var p=shortcut.Path[i];var d=shortcut.Path[Mathf.Min(i+1,shortcut.Path.Length-1)]-shortcut.Path[Mathf.Max(0,i-1)];var side=Vector3.Cross(Vector3.up,d).normalized;
                foreach(float offset in new[]{-.5f,0,.5f})
                {
                    var at=p+side*offset;
                    var hits=Physics.RaycastAll(at+Vector3.up*.8f,Vector3.down,1.6f,1,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>=.71f&&Session.Traversal.IsPermanentDrySupport(h.point,h.collider)).OrderBy(h=>Mathf.Abs(h.point.y-at.y)).ToArray();
                    if(hits.Length==0){report.failures.Add("Missing dry support row="+i+" side="+offset);continue;}
                    var floor=hits[0].point;
                    var obstruction=Physics.OverlapCapsule(floor+Vector3.up*.36f,floor+Vector3.up*1.47f,.27f,1,QueryTriggerInteraction.Ignore).FirstOrDefault(c=>!c.transform.IsChildOf(Session.Walker.Body.transform)&&c.attachedRigidbody==null);
                    if(obstruction!=null)report.failures.Add("Capsule obstruction row="+i+" side="+offset+" "+obstruction.name);
                    else report.checks.Add("Row "+i+" side "+offset+" dry supported capsule");
                }
            }
            if(report.failures.Count>0)report.status="FAIL";return Write("shortcut_physics.json",report);
        }
    }
}
