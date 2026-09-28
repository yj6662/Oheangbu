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
    public static class CompactPainterlyAuthoring
    {
        static string Folder=>WorldMacroCompactAuthoring.Folder+"/InkLandscape/Painterly";
        static string Output=>WorldMacroCompactAuthoring.Output+"/InkLandscape/Painterly";
        static IEnumerable<Transform> All=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
        static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        static T Find<T>() where T:Component=>CompactRecovery.Find<T>();
        [Serializable] sealed class Binding {public string path;public string[] materials;}
        [Serializable] sealed class Baseline {public string sheet,profile,population;public Binding[] bindings;public string[] structure,hashes;}
        [Serializable] sealed class Result {public string status;public string[] checks;}
        [Serializable] sealed class SlopeSample {public float distance,wash,tone,luminance;}
        [Serializable] sealed class DistanceReport {public int samples,decreases;public SlopeSample[] examples;}
        static void Guard()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)throw new InvalidOperationException("Compact Edit scene required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; stopped");
            Directory.CreateDirectory(Output);
        }
        public static string Execute(string command)
        {
            if(command=="release-review-cache")
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode required");
                Find<WorldMacroDressingRenderer>()?.ResetCache();
                GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
                return "Review cache released; system commit "+Prologue.PrologueAudit.CommitRatio();
            }
            if(command=="performance-before"||command=="performance-after")
            {
                var session=Find<Oheangbu.App.World.WorldMacroPlaytestSession>();
                if(!Application.isPlaying||session==null||string.IsNullOrEmpty(session.TestSaveSuffix))throw new InvalidOperationException("Isolated Play required");
                return Compare(command=="performance-before");
            }
            Guard();
            if(command=="preserve")return Preserve();
            if(command=="apply")return Apply(true);
            if(command=="apply-atmosphere")return Apply(false);
            if(command=="verify")return Verify();
            if(command.StartsWith("capture-before:"))
            {
                Compare(true);
                try{return CompactInkLandscapeAuthoring.CaptureReviewAt("before:"+command.Substring(15),Output);}
                finally{Compare(false);}
            }
            if(command.StartsWith("capture:"))
            {
                // Match the baseline comparison's clean cache; distant packets may
                // otherwise retain a valid coarse key from a previous review viewpoint.
                Find<WorldMacroDressingRenderer>().ResetCache();
                return CompactInkLandscapeAuthoring.CaptureReviewAt(command.Substring(8),Output);
            }
            throw new ArgumentException(command);
        }
        static string Digest(string text){using(var h=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)));}
        static string FileDigest(string path){using(var h=System.Security.Cryptography.SHA256.Create())using(var s=File.OpenRead(path))return Convert.ToBase64String(h.ComputeHash(s));}
        static string[] Structure()=>All.Select(t=>PathOf(t)+"|"+t.gameObject.activeSelf+"|"+t.localPosition.ToString("R")+"|"+t.localRotation.ToString("R")+"|"+t.localScale.ToString("R")+"|"+string.Join(",",t.GetComponents<MeshFilter>().Select(m=>AssetDatabase.GetAssetPath(m.sharedMesh)+":"+(m.sharedMesh==null?0:m.sharedMesh.vertexCount)))+"|"+string.Join(",",t.GetComponents<Collider>().Select(c=>c.GetType().Name+":"+c.enabled+":"+(c is MeshCollider mc?AssetDatabase.GetAssetPath(mc.sharedMesh):EditorJsonUtility.ToJson(c))))).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        // Remove only material GUID references; every authored population/LOD/seed/exclusion byte remains compared.
        static string Population(string path)=>Digest(System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path),@"(?m)^    m_Name:.*$|^  m_Name:.*$|^(\s+Material:) \{[^\r\n]+\}","$1 MATERIAL"));
        static string PacketDigest(EarlyRegionFoliage.Packet[] packets)=>Digest(string.Join("\n",packets.Select(p=>p.Bounds.ToString("R")+"|"+p.Distance+"|"+p.Count+"|"+string.Join(";",p.Near.Concat(p.Far).Select(part=>AssetDatabase.GetAssetPath(part.Mesh)+"|"+part.Submesh+"|"+string.Join("/",part.Matrices.Select(m=>m.ToString("R"))))))));
        static Baseline Read()=>JsonUtility.FromJson<Baseline>(File.ReadAllText(Output+"/baseline.json"));
        static CompactDarkInkBaseline Extras()=>AssetDatabase.LoadAssetAtPath<CompactDarkInkBaseline>(Folder+"/BaselineSupplement.asset");
        static string Preserve()
        {
            if(File.Exists(Output+"/baseline.json"))return "Current darker baseline already preserved";
            var scene=SceneManager.GetActiveScene();if(!File.Exists(Output+"/before_disk.unity"))File.Copy(scene.path,Output+"/before_disk.unity");
            if(!EditorSceneManager.SaveScene(scene,Output+"/before_open.unity",true))throw new IOException("Live scene backup failed");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();var source=Find<WorldMacroDressingRenderer>().Sheet;
            var extra=ScriptableObject.CreateInstance<CompactDarkInkBaseline>();extra.Extras=All.Select(t=>t.GetComponent<EarlyRegionFoliage>()).Where(f=>f!=null).Select(f=>new CompactDarkInkBaseline.Extra{Path=PathOf(f.transform),Packets=f.Packets}).ToArray();
            AssetDatabase.CreateAsset(extra,Folder+"/BaselineSupplement.asset");
            var b=new Baseline{sheet=AssetDatabase.GetAssetPath(source),profile=WorldMacroCompactAuthoring.Folder+"/InkLandscape/Darker/Profile.asset",structure=Structure(),population=Population(AssetDatabase.GetAssetPath(source)),
                bindings=All.Select(t=>t.GetComponent<Renderer>()).Where(r=>r!=null&&r.sharedMaterials.Any(m=>m!=null&&m.HasProperty("_CIEnabled")&&m.GetFloat("_CIEnabled")>0)).Select(r=>new Binding{path=PathOf(r.transform),materials=r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray()}).ToArray()};
            var look=new SerializedObject(Find<Oheangbu.App.WorldLookDriver>());
            var sources=b.bindings.SelectMany(x=>x.materials).Concat(source.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>AssetDatabase.GetAssetPath(p.Material))).Concat(extra.Extras.SelectMany(x=>x.Packets).SelectMany(p=>p.Near.Concat(p.Far)).Select(p=>AssetDatabase.GetAssetPath(p.Material)))
                .Concat(new[]{b.sheet,b.profile,AssetDatabase.GetAssetPath(source.Geography),AssetDatabase.GetAssetPath(look.FindProperty("_regionalSkyProfile").objectReferenceValue),AssetDatabase.GetAssetPath(look.FindProperty("_inkSkyProfile").objectReferenceValue)}).Where(p=>!string.IsNullOrEmpty(p)).Distinct();
            b.hashes=sources.Select(p=>p+"|"+FileDigest(p)).ToArray();File.WriteAllText(Output+"/baseline.json",JsonUtility.ToJson(b,true));AssetDatabase.SaveAssets();
            return "Preserved "+b.structure.Length+" transforms, "+b.bindings.Length+" material bindings; placement and sky fingerprints retained";
        }
        static string Apply(bool paint)
        {
            var b=Read();var baseline=Extras();if(baseline==null)throw new InvalidOperationException("Preserve first");
            Directory.CreateDirectory(Folder+"/Materials");AssetDatabase.Refresh();
            var profile=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(Folder+"/Profile.asset");var originalProfile=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(b.profile);
            if(profile==null){profile=Object.Instantiate(originalProfile);AssetDatabase.CreateAsset(profile,Folder+"/Profile.asset");}else EditorUtility.CopySerialized(originalProfile,profile);
            profile.name="CompactPainterly_TEST";profile.OverrideAtmosphere=true;profile.PainterlyMode=paint;
            profile.MidAirRange=new Vector2(500,1500);profile.FarAirRange=new Vector2(1500,3400);profile.AirStrengths=new Vector2(.18f,.64f);
            profile.StrokeSpacingWidth=new Vector4(20,40,2,7);profile.StrokeFadeStrength=new Vector4(200,1400,.02f,0);profile.PigmentContrast=new Vector4(.03f,.65f,0,0);profile.FoliagePainterly=new Vector4(30,180,1.5f,.3f);
            var source=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(b.sheet);var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(Folder+"/Dressing.asset");
            if(sheet==null){sheet=Object.Instantiate(source);AssetDatabase.CreateAsset(sheet,Folder+"/Dressing.asset");}else EditorUtility.CopySerialized(source,sheet);
            sheet.name="CompactPainterlyDressing";
            var materials=new Dictionary<Material,Material>();
            Material Derive(Material src)
            {
                if(src==null||!src.HasProperty("_CIEnabled"))return src;if(materials.TryGetValue(src,out var cached))return cached;
                string path=Folder+"/Materials/"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(src))+".mat";var existing=AssetDatabase.LoadAssetAtPath<Material>(path);var m=new Material(src);m.name=src.name+"_Painterly";profile.Apply(m);
                if(existing==null){AssetDatabase.CreateAsset(m,path);existing=m;}else{EditorUtility.CopySerialized(m,existing);Object.DestroyImmediate(m);}EditorUtility.SetDirty(existing);materials[src]=existing;return existing;
            }
            foreach(var part in sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts))part.Material=Derive(part.Material);
            var objects=All.ToLookup(PathOf,t=>t);
            foreach(var bind in b.bindings){var r=objects[bind.path].Single().GetComponent<Renderer>();r.sharedMaterials=bind.materials.Select(p=>Derive(AssetDatabase.LoadAssetAtPath<Material>(p))).ToArray();EditorUtility.SetDirty(r);}
            foreach(var extra in baseline.Extras)
            {
                var component=objects[extra.Path].Single().GetComponent<EarlyRegionFoliage>();
                EarlyRegionFoliage.Part Part(EarlyRegionFoliage.Part part)=>new EarlyRegionFoliage.Part{Mesh=part.Mesh,Material=Derive(part.Material),Submesh=part.Submesh,Matrices=part.Matrices.ToArray()};
                component.Packets=extra.Packets.Select(p=>new EarlyRegionFoliage.Packet{Bounds=p.Bounds,Distance=p.Distance,Count=p.Count,Near=p.Near.Select(Part).ToArray(),Far=p.Far.Select(Part).ToArray()}).ToArray();EditorUtility.SetDirty(component);
            }
            var renderer=Find<WorldMacroDressingRenderer>();renderer.Sheet=sheet;renderer.ResetCache();EditorUtility.SetDirty(renderer);EditorUtility.SetDirty(sheet);EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return (paint?"Painterly + distance atmosphere":"Atmosphere-only comparison")+" applied through "+materials.Count+" derived materials; populations unchanged";
        }
        static string Verify()
        {
            var b=Read();var checks=new List<string>();void Check(bool pass,string text)=>checks.Add((pass?"PASS ":"FAIL ")+text);
            Check(Structure().SequenceEqual(b.structure),"all transforms, active states, meshes and colliders unchanged");
            Check(b.hashes.All(row=>{int i=row.LastIndexOf('|');return FileDigest(row.Substring(0,i))==row.Substring(i+1);}),"source materials/profile/dressing/geography/skies unchanged");
            var sheet=Find<WorldMacroDressingRenderer>().Sheet;Check(Population(AssetDatabase.GetAssetPath(sheet))==b.population,"all population/LOD/seed/exclusion/settings bytes unchanged except material references and sheet name");
            var objects=All.ToLookup(PathOf,t=>t);
            Check(Extras().Extras.All(e=>PacketDigest(e.Packets)==PacketDigest(objects[e.Path].Single().GetComponent<EarlyRegionFoliage>().Packets)),"supplementary count, Near/Far matrices, bounds and distances identical");
            var profile=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(Folder+"/Profile.asset");var source=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(b.profile);
            Check(profile.GroundTones==source.GroundTones&&profile.MountainTones==source.MountainTones&&profile.BaseSaturation==source.BaseSaturation&&profile.RegionalColourContribution==source.RegionalColourContribution,"ground/mountain tone intervals and saturation/tint factors preserved");
            Check(profile.DetailRange==source.DetailRange&&profile.RockDetailRange==source.RockDetailRange&&profile.MountainRange==source.MountainRange,"detail and chroma distances remain independent and unchanged");
            int changedPasses=0;foreach(string path in AssetDatabase.FindAssets("t:Material",new[]{Folder+"/Materials"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                string srcPath=AssetDatabase.GUIDToAssetPath(Path.GetFileNameWithoutExtension(path));var src=AssetDatabase.LoadAssetAtPath<Material>(srcPath);var dst=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(src==null||src.shader!=dst.shader||src.passCount!=dst.passCount||src.renderQueue!=dst.renderQueue||src.enableInstancing!=dst.enableInstancing)changedPasses++;
            }
            Check(changedPasses==0,"shader/pass count, render queue and instancing unchanged on every derived material");
            var distance=new DistanceReport();var examples=new List<SlopeSample>();
            for(int shade=0;shade<=10;shade++)for(int detail=0;detail<=10;detail++)for(int brush=0;brush<=2;brush++)for(int pigment=0;pigment<=2;pigment++)
            {
                float previous=-1;
                for(int d=0;d<=4500;d++)
                {
                    float tone=profile.EvaluateDarkMountainTone(shade*.1f,detail*.1f,d,pigment*.5f,brush*.5f);
                    var color=Color.Lerp(profile.Ink.linear,profile.Paper.linear,Mathf.Clamp01(tone));float wash=profile.EvaluateAtmosphereWash(d);color=Color.Lerp(color,profile.Air.linear,wash);
                    float l=color.r*.2126f+color.g*.7152f+color.b*.0722f;if(l+1e-6f<previous)distance.decreases++;previous=l;distance.samples++;
                    if(shade==5&&detail==5&&brush==1&&pigment==1&&new[]{0,500,900,1500,2200,3400,4500}.Contains(d))examples.Add(new SlopeSample{distance=d,wash=wash,tone=tone,luminance=l});
                }
            }
            distance.examples=examples.ToArray();File.WriteAllText(Output+"/distance_response.json",JsonUtility.ToJson(distance,true));Check(distance.decreases==0,"fixed surface/light distance luminance "+distance.samples+" samples; decreases="+distance.decreases);
            foreach(string name in new[]{"Oheangbu/CompactNaturalGround","Oheangbu/CompactNaturalVegetation","Oheangbu/WorldMacroTerrain","Oheangbu/EarlyRegionFoliageCard"})
            {var s=Shader.Find(name);Check(s!=null&&!ShaderUtil.ShaderHasError(s),"shader "+name);if(s!=null)foreach(var msg in ShaderUtil.GetShaderMessages(s))checks.Add(msg.severity+": "+msg.message);}
            checks.Add("UNVERIFIED final art approval and actual player traversal; CPU distance samples do not certify every shadow/mip transition pixel.");
            File.WriteAllText(Output+"/checks.json",JsonUtility.ToJson(new Result{status=checks.Any(x=>x.StartsWith("FAIL"))?"FAIL":"PASS",checks=checks.ToArray()},true));return string.Join("\n",checks);
        }
        static Action restore;
        static string Compare(bool before)
        {
            if(!before){restore?.Invoke();restore=null;return "Derived look restored; streaming cache cleared";}
            if(restore!=null)throw new InvalidOperationException("Comparison already active");
            var b=Read();var objects=All.ToLookup(PathOf,t=>t);var rr=b.bindings.Select(x=>objects[x.path].Single().GetComponent<Renderer>()).ToArray();var mats=rr.Select(r=>r.sharedMaterials).ToArray();
            var extra=Extras();var components=extra.Extras.Select(e=>objects[e.Path].Single().GetComponent<EarlyRegionFoliage>()).ToArray();var packets=components.Select(c=>c.Packets).ToArray();var dressing=Find<WorldMacroDressingRenderer>();var sheet=dressing.Sheet;
            restore=()=>{for(int i=0;i<rr.Length;i++)rr[i].sharedMaterials=mats[i];for(int i=0;i<components.Length;i++)components[i].Packets=packets[i];dressing.Sheet=sheet;dressing.ResetCache();};
            try{for(int i=0;i<rr.Length;i++)rr[i].sharedMaterials=b.bindings[i].materials.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();for(int i=0;i<components.Length;i++)components[i].Packets=extra.Extras[i].Packets;dressing.Sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(b.sheet);dressing.ResetCache();return "Preserved darker look for isolated Play comparison only";}
            catch{Compare(false);throw;}
        }
    }
}
