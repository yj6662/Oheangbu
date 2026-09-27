using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactKcisaReplacement
    {
        [Serializable] sealed class GroundColourChange {public string material;public float before,after;}
        [Serializable] sealed class GroundColourReport {public int renderers;public GroundColourChange[] materials;public string scope;}
        static string DesaturateGround()
        {
            var rr=All.Select(t=>t.GetComponent<Renderer>()).Where(r=>r!=null&&
                (PathOf(r.transform).Contains("01_GlobalTerrain_IndependentOfRoads/")||
                 PathOf(r.transform).Contains("BackgroundContext_RenderOnly/")||
                 PathOf(r.transform).Contains("01_TerrainConformedRoutes/")||
                 PathOf(r.transform)=="Playtest_Village_Office/Courtyard")).ToArray();
            var mm=rr.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
            foreach(var m in mm)if(m.shader.name!="Oheangbu/CompactNaturalGround"||!m.HasProperty("_Saturation")||!AssetDatabase.GetAssetPath(m).StartsWith(WorldMacroCompactAuthoring.Folder+"/"))
                throw new InvalidOperationException("Expected private natural ground material: "+AssetDatabase.GetAssetPath(m));
            var report=new GroundColourReport{renderers=rr.Length,scope="Entire compact map: ground, dirt roads, distant terrain, office courtyard. Vegetation/buildings/water/global exposure unchanged.",
                materials=mm.Select(m=>new GroundColourChange{material=AssetDatabase.GetAssetPath(m),before=m.GetFloat("_Saturation"),after=.2f}).ToArray()};
            string backup=Output+"/ground_saturation_before.json";
            if(!File.Exists(backup)){Capture("ground_before");File.WriteAllText(backup,JsonUtility.ToJson(report,true));}
            foreach(var m in mm){m.SetFloat("_Saturation",.2f);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);}
            if(mm.Any(m=>Mathf.Abs(m.GetFloat("_Saturation")-.2f)>.00001f))throw new InvalidOperationException("Ground saturation verification failed");
            File.WriteAllText(Output+"/ground_saturation.json",JsonUtility.ToJson(report,true));
            Capture("ground_after");return JsonUtility.ToJson(report);
        }
    }
}
