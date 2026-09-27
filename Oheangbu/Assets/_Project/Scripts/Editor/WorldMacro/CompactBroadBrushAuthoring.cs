using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;
using Oheangbu.App.World.Dressing;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Mountain-material-only revision. The established dressing assets are never copied or edited.
    public static class CompactBroadBrushAuthoring
    {
        static string Folder=>WorldMacroCompactAuthoring.Folder+"/InkLandscape/BroadBrush";
        static string Output=>WorldMacroCompactAuthoring.Output+"/InkLandscape/BroadBrush";
        static IEnumerable<Transform> All=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
        static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        static T Find<T>() where T:Component=>CompactRecovery.Find<T>();
        [Serializable] sealed class Binding {public string path;public string[] materials;}
        [Serializable] sealed class Baseline {public string profile,sheet;public Binding[] bindings;public string[] structure,hashes;public string supplements;}
        [Serializable] sealed class Result {public string status;public string[] checks;}
        [Serializable] sealed class DistanceAudit {public int samples,decreases;}
        static Action restore;
        static bool IsMountain(Material m)=>m!=null&&m.HasProperty("_CIEnabled")&&m.GetFloat("_CIEnabled")>.5f&&(m.shader.name=="Oheangbu/CompactNaturalGround"||m.shader.name=="Oheangbu/WorldMacroTerrain");
        static void Guard()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)throw new InvalidOperationException("Compact Edit scene required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; stopped");
            Directory.CreateDirectory(Output);
        }
        public static string Execute(string command)
        {
            if(command=="performance-before"||command=="performance-after")
            {
                var session=Find<Oheangbu.App.World.WorldMacroPlaytestSession>();
                if(!Application.isPlaying||session==null||string.IsNullOrEmpty(session.TestSaveSuffix))throw new InvalidOperationException("Isolated Play required");
                return Compare(command=="performance-before");
            }
            Guard();
            if(command=="preserve")return Preserve();
            if(command=="apply")return Apply();
            if(command=="verify")return Verify();
            if(command.StartsWith("capture-before:"))
            {
                Compare(true);
                try{return CompactInkLandscapeAuthoring.CaptureReviewAt("before:"+command.Substring(15),Output);}
                finally{Compare(false);}
            }
            if(command.StartsWith("capture:"))
            {
                Find<WorldMacroDressingRenderer>().ResetCache();
                return CompactInkLandscapeAuthoring.CaptureReviewAt(command.Substring(8),Output);
            }
            throw new ArgumentException(command);
        }
        static string Digest(string text){using(var h=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)));}
        static string FileDigest(string path){using(var h=System.Security.Cryptography.SHA256.Create())using(var s=File.OpenRead(path))return Convert.ToBase64String(h.ComputeHash(s));}
        static string[] Structure()=>All.Select(t=>PathOf(t)+"|"+t.gameObject.activeSelf+"|"+t.localPosition.ToString("R")+"|"+t.localRotation.ToString("R")+"|"+t.localScale.ToString("R")+"|"+string.Join(",",t.GetComponents<MeshFilter>().Select(m=>AssetDatabase.GetAssetPath(m.sharedMesh)+":"+(m.sharedMesh==null?0:m.sharedMesh.vertexCount)))+"|"+string.Join(",",t.GetComponents<Collider>().Select(c=>c.GetType().Name+":"+c.enabled+":"+(c is MeshCollider mc?AssetDatabase.GetAssetPath(mc.sharedMesh):EditorJsonUtility.ToJson(c))))).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        static string Supplements()=>Digest(string.Join("\n",All.Select(t=>t.GetComponent<EarlyRegionFoliage>()).Where(f=>f!=null).Select(f=>PathOf(f.transform)+EditorJsonUtility.ToJson(f))));
        static string SupplementSceneDigest(string path)
        {
            var component=Find<EarlyRegionFoliage>();if(component==null)throw new InvalidOperationException("Supplementary foliage component missing");
            string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(component)));
            var blocks=System.Text.RegularExpressions.Regex.Split(File.ReadAllText(path),@"(?m)^--- ").Where(x=>x.Contains("guid: "+guid+","));
            // These three serialized fields are live draw diagnostics, not authored placement.
            // Scene YAML retains stable asset/file IDs unlike EditorJsonUtility instance IDs.
            return Digest(string.Join("\n",blocks.Select(x=>System.Text.RegularExpressions.Regex.Replace(x,@"(?m)^  (DrawCalls|Instances|SubmissionMilliseconds):[^\r\n]*\r?\n",string.Empty))));
        }
        static Baseline Read()=>JsonUtility.FromJson<Baseline>(File.ReadAllText(Output+"/baseline.json"));
        static string Preserve()
        {
            if(!File.Exists(Output+"/views.json"))File.Copy(WorldMacroCompactAuthoring.Output+"/InkLandscape/Painterly/views.json",Output+"/views.json",false);
            if(File.Exists(Output+"/baseline.json"))return "Painterly baseline already preserved";
            var scene=SceneManager.GetActiveScene();if(!File.Exists(Output+"/before_disk.unity"))File.Copy(scene.path,Output+"/before_disk.unity",false);
            if(!File.Exists(Output+"/before_open.unity")&&!EditorSceneManager.SaveScene(scene,Output+"/before_open.unity",true))throw new IOException("Live scene backup failed");
            var sheet=Find<WorldMacroDressingRenderer>().Sheet;
            var b=new Baseline{profile=WorldMacroCompactAuthoring.Folder+"/InkLandscape/Painterly/Profile.asset",sheet=AssetDatabase.GetAssetPath(sheet),structure=Structure(),supplements=Supplements(),
                bindings=All.Select(t=>t.GetComponent<Renderer>()).Where(r=>r!=null&&r.sharedMaterials.Any(IsMountain)).Select(r=>new Binding{path=PathOf(r.transform),materials=r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray()}).ToArray()};
            var look=new SerializedObject(Find<Oheangbu.App.WorldLookDriver>());
            var sources=b.bindings.SelectMany(x=>x.materials).Concat(sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>AssetDatabase.GetAssetPath(p.Material)))
                .Concat(new[]{b.profile,b.sheet,AssetDatabase.GetAssetPath(sheet.Geography),AssetDatabase.GetAssetPath(look.FindProperty("_regionalSkyProfile").objectReferenceValue),AssetDatabase.GetAssetPath(look.FindProperty("_inkSkyProfile").objectReferenceValue)}).Where(p=>!string.IsNullOrEmpty(p)).Distinct();
            b.hashes=sources.Select(p=>p+"|"+FileDigest(p)).ToArray();File.WriteAllText(Output+"/baseline.tmp",JsonUtility.ToJson(b,true));
            File.Move(Output+"/baseline.tmp",Output+"/baseline.json");
            return "Preserved "+b.structure.Length+" transforms and "+b.bindings.Length+" mountain material bindings; full dressing and source hashes retained";
        }
        static string Apply()
        {
            var b=Read();Directory.CreateDirectory(Folder+"/Materials");AssetDatabase.Refresh();
            var profile=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(Folder+"/Profile.asset");
            if(profile==null)
            {
                profile=Object.Instantiate(AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(b.profile));
                profile.name="CompactBroadBrush_TEST";AssetDatabase.CreateAsset(profile,Folder+"/Profile.asset");
            }
            // Reapply from the preserved profile. Only the explicitly authored broad-coat
            // controls survive; repeated application cannot drift soil, sky or foliage values.
            var broadFields=typeof(CompactInkLandscapeProfile).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance).Where(f=>f.Name.StartsWith("BroadBrush",StringComparison.Ordinal)).ToArray();
            var broadValues=broadFields.Select(f=>f.GetValue(profile)).ToArray();
            EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(b.profile),profile);
            for(int i=0;i<broadFields.Length;i++)broadFields[i].SetValue(profile,broadValues[i]);
            profile.name="CompactBroadBrush_TEST";
            profile.BroadBrushMode=true;
            var materials=new Dictionary<Material,Material>();
            Material Derive(Material src)
            {
                if(!IsMountain(src))return src;if(materials.TryGetValue(src,out var cached))return cached;
                string path=Folder+"/Materials/"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(src))+".mat";
                var existing=AssetDatabase.LoadAssetAtPath<Material>(path);var m=new Material(src);m.name=src.name+"_BroadBrush";profile.Apply(m);
                if(existing==null){AssetDatabase.CreateAsset(m,path);existing=m;}else{EditorUtility.CopySerialized(m,existing);Object.DestroyImmediate(m);}
                EditorUtility.SetDirty(existing);materials[src]=existing;return existing;
            }
            var objects=All.ToLookup(PathOf,t=>t);
            foreach(var bind in b.bindings){var r=objects[bind.path].Single().GetComponent<Renderer>();r.sharedMaterials=bind.materials.Select(p=>Derive(AssetDatabase.LoadAssetAtPath<Material>(p))).ToArray();EditorUtility.SetDirty(r);}
            EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return "Broad mountain brushwork applied through "+materials.Count+" derived materials; vegetation, sky and geography unchanged";
        }
        static string Verify()
        {
            var b=Read();var checks=new List<string>();void Check(bool pass,string label)=>checks.Add((pass?"PASS ":"FAIL ")+label);
            Check(Structure().SequenceEqual(b.structure),"all "+b.structure.Length+" transforms, active states, meshes and colliders unchanged");
            Check(b.hashes.All(row=>{int i=row.LastIndexOf('|');return FileDigest(row.Substring(0,i))==row.Substring(i+1);}),"all "+b.hashes.Length+" source material/profile/dressing/geography/sky hashes unchanged");
            Check(AssetDatabase.GetAssetPath(Find<WorldMacroDressingRenderer>().Sheet)==b.sheet,"original Painterly dressing remains assigned (population, wind, LOD and streaming untouched)");
            string verifyScene=Output+"/verify_open.unity";
            if(!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),verifyScene,true))throw new IOException("Live verification scene copy failed");
            Check(SupplementSceneDigest(verifyScene)==SupplementSceneDigest(Output+"/before_open.unity"),"supplementary vegetation exact scene serialization unchanged except three live draw counters");
            var profile=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(Folder+"/Profile.asset");var source=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(b.profile);
            Check(typeof(CompactInkLandscapeProfile).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance).Where(f=>!f.Name.StartsWith("BroadBrush",StringComparison.Ordinal)).All(f=>Equals(f.GetValue(profile),f.GetValue(source))),"every non-broad-brush profile setting equals preserved Painterly source");
            Check(profile.GroundTones==source.GroundTones&&profile.MountainTones==source.MountainTones&&profile.BaseSaturation==source.BaseSaturation&&profile.RegionalColourContribution==source.RegionalColourContribution,"ground/mountain intervals and saturation/tint factors preserved");
            Check(profile.MidAirRange==source.MidAirRange&&profile.FarAirRange==source.FarAirRange&&profile.AirStrengths==source.AirStrengths&&profile.DetailRange==source.DetailRange&&profile.FoliagePainterly==source.FoliagePainterly,"atmosphere, near ground texture and foliage expression preserved");
            int failures=0,materialCount=0;
            foreach(string path in AssetDatabase.FindAssets("t:Material",new[]{Folder+"/Materials"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var src=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(Path.GetFileNameWithoutExtension(path)));var dst=AssetDatabase.LoadAssetAtPath<Material>(path);materialCount++;
                if(src==null||src.shader!=dst.shader||src.passCount!=dst.passCount||src.renderQueue!=dst.renderQueue||src.enableInstancing!=dst.enableInstancing||dst.GetFloat("_CIBroadBrush")<.5f)failures++;
            }
            Check(failures==0,"all "+materialCount+" derived materials enabled; same shader/pass count/queue/instancing");
            var objects=All.ToLookup(PathOf,t=>t);
            Check(b.bindings.All(bind=>objects[bind.path].Single().GetComponent<Renderer>().sharedMaterials.Select(AssetDatabase.GetAssetPath).SequenceEqual(bind.materials.Select(p=>IsMountain(AssetDatabase.LoadAssetAtPath<Material>(p))?Folder+"/Materials/"+AssetDatabase.AssetPathToGUID(p)+".mat":p))),"all preserved renderer slots point to the correct one-to-one mountain derivatives");
            var distance=new DistanceAudit();
            for(int shade=0;shade<=10;shade++)for(int detail=0;detail<=2;detail++)for(int brush=0;brush<=10;brush++)
            {
                float previous=-1;
                for(int d=0;d<=4500;d++)
                {
                    float tone=profile.EvaluateDarkMountainTone(shade*.1f,detail*.5f,d,.5f,brush*.1f);
                    var c=Color.Lerp(profile.Ink.linear,profile.Paper.linear,tone);c=Color.Lerp(c,profile.Air.linear,profile.EvaluateAtmosphereWash(d));
                    float l=c.r*.2126f+c.g*.7152f+c.b*.0722f;if(l+1e-6f<previous)distance.decreases++;previous=l;distance.samples++;
                }
            }
            File.WriteAllText(Output+"/distance_response.json",JsonUtility.ToJson(distance,true));Check(distance.decreases==0,"fixed pigment/light distance response: "+distance.samples+" samples, "+distance.decreases+" decreases");
            foreach(string name in new[]{"Oheangbu/CompactNaturalGround","Oheangbu/WorldMacroTerrain","Oheangbu/CompactNaturalVegetation","Oheangbu/EarlyRegionFoliageCard"})
            {
                var s=Shader.Find(name);Check(s!=null&&!ShaderUtil.ShaderHasError(s),"shader "+name);if(s!=null)foreach(var msg in ShaderUtil.GetShaderMessages(s))checks.Add(msg.severity+": "+msg.message);
            }
            checks.Add("UNVERIFIED user artistic approval, continuous traversal visual shimmer; fixed screenshots are not a full traversal test.");
            var result=new Result{status=checks.Any(x=>x.StartsWith("FAIL"))?"FAIL":"PASS",checks=checks.ToArray()};File.WriteAllText(Output+"/checks.json",JsonUtility.ToJson(result,true));return string.Join("\n",checks);
        }
        static string Compare(bool before)
        {
            if(!before){restore?.Invoke();restore=null;return "Current mountain materials restored";}
            if(restore!=null)throw new InvalidOperationException("Comparison already active");
            var b=Read();var objects=All.ToLookup(PathOf,t=>t);var rr=b.bindings.Select(x=>objects[x.path].Single().GetComponent<Renderer>()).ToArray();var mats=rr.Select(r=>r.sharedMaterials).ToArray();
            restore=()=>{for(int i=0;i<rr.Length;i++)rr[i].sharedMaterials=mats[i];Find<WorldMacroDressingRenderer>().ResetCache();};
            try{for(int i=0;i<rr.Length;i++)rr[i].sharedMaterials=b.bindings[i].materials.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();Find<WorldMacroDressingRenderer>().ResetCache();return "Painterly baseline in temporary comparison";}
            catch{Compare(false);throw;}
        }
    }
}
