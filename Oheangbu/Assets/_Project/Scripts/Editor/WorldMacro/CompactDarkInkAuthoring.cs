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
    public static class CompactDarkInkAuthoring
    {
        static string Folder=>WorldMacroCompactAuthoring.Folder+"/InkLandscape/Darker";
        static string Output=>WorldMacroCompactAuthoring.Output+"/InkLandscape/Darker";
        static IEnumerable<Transform> All=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
        static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        static T Find<T>() where T:Component=>CompactRecovery.Find<T>();
        [Serializable] sealed class Binding {public string path;public string[] materials;}
        [Serializable] sealed class StaticPlant {public string path,prototype;public bool active;}
        [Serializable] sealed class Baseline {public string sheet,profile;public Binding[] bindings;public StaticPlant[] staticPlants;public string[] structure,hashes;}
        [Serializable] sealed class Rows {public string status;public string[] checks;}
        [Serializable] sealed class Count {public string path,prototype;public int packet,before,after,previousStoredCount;public float retention;public bool paired;}
        [Serializable] sealed class Counts {public Count[] rows;}
        static void Guard()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)throw new InvalidOperationException("Compact Edit scene required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; stopped");
            Directory.CreateDirectory(Output);
        }
        public static string Execute(string command)
        {
            if(command=="colliders")
            {
                if(!Application.isPlaying)throw new InvalidOperationException("Play required");
                string result=Find<WorldMacroDressingRenderer>().ValidateActiveRetentionColliders();
                File.WriteAllText(Output+"/active_colliders.json",result);return result;
            }
            Guard();
            if(command=="preserve")return Preserve();
            if(command=="static-baseline")
            {
                var b=Read();if(b.staticPlants.Length>0)throw new InvalidOperationException("Static baseline already established");
                b.staticPlants=StaticPlants(AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(b.sheet));
                File.WriteAllText(Output+"/baseline.json",JsonUtility.ToJson(b,true));return "Classified "+b.staticPlants.Length+" authored static tree roots";
            }
            if(command=="apply")return Apply();
            if(command=="verify")return Verify();
            if(command.StartsWith("retention:"))return Retention(int.Parse(command.Substring(10)));
            if(command.StartsWith("capture:"))return CompactInkLandscapeAuthoring.CaptureReviewAt(command.Substring(8),Output);
            throw new ArgumentException(command);
        }
        static string Digest(string text){using(var h=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)));}
        static string[] Structure()=>All.Select(t=>PathOf(t)+"|"+t.localPosition.ToString("R")+"|"+t.localRotation.ToString("R")+"|"+t.localScale.ToString("R")+"|"+string.Join(",",t.GetComponents<MeshFilter>().Select(m=>AssetDatabase.GetAssetPath(m.sharedMesh)+":"+(m.sharedMesh==null?0:m.sharedMesh.vertexCount)))+"|"+string.Join(",",t.GetComponents<Collider>().Select(c=>c.GetType().Name+":"+c.enabled+":"+(c is MeshCollider mc?AssetDatabase.GetAssetPath(mc.sharedMesh):EditorJsonUtility.ToJson(c))))).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        static Baseline Read()=>JsonUtility.FromJson<Baseline>(File.ReadAllText(Output+"/baseline.json"));
        static CompactDarkInkBaseline Extras()=>AssetDatabase.LoadAssetAtPath<CompactDarkInkBaseline>(Folder+"/BaselineSupplement.asset");
        static StaticPlant[] StaticPlants(WorldMacroDressingSheetSO source)
        {
            var corridor=AssetDatabase.LoadAssetAtPath<WorldMacroVisualCorridorSO>(WorldMacroCompactAuthoring.Folder+"/Data/07_VisualCorridor.asset");
            var objects=All.ToLookup(PathOf,t=>t);var plants=new List<StaticPlant>();
            foreach(var placement in corridor.Placements.Where(p=>p.Group=="02_MountainAndForest"))
            {
                var record=corridor.Sources.Single(s=>s.Id==placement.SourceId);
                var proto=source.Prototypes.FirstOrDefault(p=>p.Category==WorldMacroDressingSheetSO.Kind.Tree&&p.SourcePath==record.SourcePath);if(proto==null)continue;
                string path="Playtest_VisualCorridor/"+placement.Group+"/"+placement.Id;var t=objects[path].Single();
                if(t.GetComponent<LODGroup>()==null)throw new InvalidOperationException("Missing authored tree LOD assembly "+path);
                plants.Add(new StaticPlant{path=path,prototype=proto.Id,active=t.gameObject.activeSelf});
            }
            return plants.ToArray();
        }
        static string Preserve()
        {
            if(File.Exists(Output+"/baseline.json"))return "Current-look baseline already preserved";
            var scene=SceneManager.GetActiveScene();if(!File.Exists(Output+"/before_disk.unity"))File.Copy(scene.path,Output+"/before_disk.unity");
            if(!EditorSceneManager.SaveScene(scene,Output+"/before_open.unity",true))throw new IOException("Live scene backup failed");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var extra=ScriptableObject.CreateInstance<CompactDarkInkBaseline>();
            extra.Extras=All.Select(t=>t.GetComponent<EarlyRegionFoliage>()).Where(f=>f!=null).Select(f=>new CompactDarkInkBaseline.Extra{Path=PathOf(f.transform),Packets=f.Packets}).ToArray();
            AssetDatabase.CreateAsset(extra,Folder+"/BaselineSupplement.asset");
            var b=new Baseline{sheet=AssetDatabase.GetAssetPath(Find<WorldMacroDressingRenderer>().Sheet),profile=WorldMacroCompactAuthoring.Folder+"/InkLandscape/Profile.asset",structure=Structure(),
                bindings=All.Select(t=>t.GetComponent<Renderer>()).Where(r=>r!=null&&r.sharedMaterials.Any(m=>m!=null&&m.HasProperty("_CIEnabled")&&m.GetFloat("_CIEnabled")>0)).Select(r=>new Binding{path=PathOf(r.transform),materials=r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray()}).ToArray()};
            var source=Find<WorldMacroDressingRenderer>().Sheet;
            // Only the asset-backed corridor assemblies are ordinary static plants. Other
            // hand-authored sacred/landmark trees are outside this explicit authored group.
            b.staticPlants=StaticPlants(source);
            var sources=b.bindings.SelectMany(x=>x.materials).Concat(source.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>AssetDatabase.GetAssetPath(p.Material))).Concat(extra.Extras.SelectMany(x=>x.Packets).SelectMany(p=>p.Near.Concat(p.Far)).Select(p=>AssetDatabase.GetAssetPath(p.Material))).Concat(new[]{b.sheet,b.profile,AssetDatabase.GetAssetPath(source.Geography)}).Where(p=>!string.IsNullOrEmpty(p)).Distinct();
            b.hashes=sources.Select(p=>p+"|"+Digest(File.ReadAllText(p))).ToArray();File.WriteAllText(Output+"/baseline.json",JsonUtility.ToJson(b,true));AssetDatabase.SaveAssets();
            return "Preserved "+b.structure.Length+" transforms, "+b.bindings.Length+" renderers and "+extra.Extras.Length+" supplementary components";
        }
        static uint Hash(string text){unchecked{uint h=2166136261;foreach(char c in text){h^=c;h*=16777619;}h^=h>>16;h*=0x7feb352d;h^=h>>15;h*=0x846ca68b;return h^(h>>16);}}
        static int[] Selected(string path,int packet,int count,float retention)=>Enumerable.Range(0,count).Where(i=>(Hash(path+"|"+packet+"|"+i)&0xffffff)/16777216f<retention).ToArray();
        static WorldMacroDressingSheetSO.Prototype Prototype(WorldMacroDressingSheetSO sheet,EarlyRegionFoliage.Packet p)
        {
            var first=p.Near.FirstOrDefault();if(first==null)return null;
            return sheet.Prototypes.OrderByDescending(x=>x.Id.Length).FirstOrDefault(x=>first.Material.name.Contains(x.Id))??sheet.Prototypes.FirstOrDefault(x=>x.Lods.Any(l=>l.Parts.Any(part=>part.Mesh==first.Mesh&&part.Submesh==first.Submesh)));
        }
        static float Retained(WorldMacroDressingSheetSO.Prototype p)=>p==null?1:p.Category==WorldMacroDressingSheetSO.Kind.Tree?.65f:p.Category==WorldMacroDressingSheetSO.Kind.Shrub?.5f:p.Category==WorldMacroDressingSheetSO.Kind.Grass?.4f:1;
        static int ActualCount(EarlyRegionFoliage.Packet packet)
        {
            int count=packet.Near.FirstOrDefault()?.Matrices.Length??0;
            if(packet.Near.Concat(packet.Far).Any(part=>part.Matrices.Length!=count))throw new InvalidOperationException("Near/far array identity cannot be established");
            return count;
        }
        static string Apply()
        {
            var b=Read();var baseline=Extras();if(baseline==null)throw new InvalidOperationException("Preserve first");
            foreach(var packet in baseline.Extras.SelectMany(x=>x.Packets))ActualCount(packet);
            Directory.CreateDirectory(Folder+"/Materials");AssetDatabase.Refresh();
            var profile=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(Folder+"/Profile.asset");
            var originalProfile=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(b.profile);
            if(profile==null){profile=Object.Instantiate(originalProfile);AssetDatabase.CreateAsset(profile,Folder+"/Profile.asset");}else EditorUtility.CopySerialized(originalProfile,profile);
            profile.name="CompactDarkInk_TEST";profile.DarkNearMode=true;profile.GroundTones=new Vector4(.14f,.30f,.17f,.29f);profile.MountainTones=new Vector2(.06f,.18f);profile.MountainSlopeDegrees=new Vector2(12,37);
            var source=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(b.sheet);
            var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(Folder+"/Dressing.asset");
            if(sheet==null){sheet=Object.Instantiate(source);AssetDatabase.CreateAsset(sheet,Folder+"/Dressing.asset");}else EditorUtility.CopySerialized(source,sheet);
            sheet.name="CompactDarkInkDressing";sheet.UseSpeciesRetention=true;sheet.SpeciesRetention=new Vector3(.65f,.5f,.4f);
            var materials=new Dictionary<Material,Material>();
            Material Derive(Material src)
            {
                if(src==null||!src.HasProperty("_CIEnabled"))return src;
                if(materials.TryGetValue(src,out var found))return found;
                string path=Folder+"/Materials/"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(src))+".mat";
                var existing=AssetDatabase.LoadAssetAtPath<Material>(path);var m=new Material(src);m.name=src.name+"_DarkNear";profile.Apply(m);
                if(existing==null){AssetDatabase.CreateAsset(m,path);existing=m;}else{EditorUtility.CopySerialized(m,existing);Object.DestroyImmediate(m);}EditorUtility.SetDirty(existing);materials[src]=existing;return existing;
            }
            foreach(var part in sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts))part.Material=Derive(part.Material);
            var objects=All.ToLookup(PathOf,t=>t);
            foreach(var plant in b.staticPlants)
            {
                var t=objects[plant.path].Single();t.gameObject.SetActive(plant.active&&Selected(plant.path,0,1,.65f).Length==1);EditorUtility.SetDirty(t.gameObject);
            }
            foreach(var bind in b.bindings){var r=objects[bind.path].Single().GetComponent<Renderer>();r.sharedMaterials=bind.materials.Select(p=>Derive(AssetDatabase.LoadAssetAtPath<Material>(p))).ToArray();EditorUtility.SetDirty(r);}
            var counts=new List<Count>();
            foreach(var extra in baseline.Extras)
            {
                var component=objects[extra.Path].Single().GetComponent<EarlyRegionFoliage>();
                component.Packets=extra.Packets.Select((p,pi)=>{
                    var proto=Prototype(source,p);float retention=Retained(proto);int actualCount=ActualCount(p);var indices=Selected(extra.Path,pi,actualCount,retention);
                    EarlyRegionFoliage.Part Part(EarlyRegionFoliage.Part part)=>new EarlyRegionFoliage.Part{Mesh=part.Mesh,Material=Derive(part.Material),Submesh=part.Submesh,Matrices=indices.Select(i=>part.Matrices[i]).ToArray()};
                    counts.Add(new Count{path=extra.Path,packet=pi,prototype=proto?.Id??"UNCLASSIFIED_PRESERVED",before=actualCount,after=indices.Length,previousStoredCount=p.Count,retention=retention,paired=true});
                    return new EarlyRegionFoliage.Packet{Bounds=p.Bounds,Distance=p.Distance,Count=indices.Length,Near=p.Near.Select(Part).ToArray(),Far=p.Far.Select(Part).ToArray()};
                }).ToArray();EditorUtility.SetDirty(component);
            }
            var renderer=Find<WorldMacroDressingRenderer>();renderer.Sheet=sheet;renderer.ResetCache();EditorUtility.SetDirty(renderer);EditorUtility.SetDirty(sheet);EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.WriteAllText(Output+"/supplement_retention.json",JsonUtility.ToJson(new Counts{rows=counts.ToArray()},true));
            File.WriteAllText(Output+"/static_retention.json",JsonUtility.ToJson(new Counts{rows=b.staticPlants.Select(p=>new Count{path=p.path,prototype=p.prototype,before=p.active?1:0,after=objects[p.path].Single().gameObject.activeSelf?1:0,retention=.65f,paired=true}).ToArray()},true));
            return "Applied current-baseline derivatives: "+materials.Count+" materials, "+counts.Sum(c=>c.before)+" -> "+counts.Sum(c=>c.after)+" supplementary individuals; "+b.staticPlants.Length+" static tree assemblies audited. Sky and original assets unchanged.";
        }
        static string Verify()
        {
            var b=Read();var checks=new List<string>();void Check(bool pass,string label)=>checks.Add((pass?"PASS ":"FAIL ")+label);
            Check(Structure().SequenceEqual(b.structure),"all original transform TRS, meshes and collider definitions unchanged");
            Check(b.hashes.All(row=>{int i=row.LastIndexOf('|');return Digest(File.ReadAllText(row.Substring(0,i)))==row.Substring(i+1);}),"immediately previous profile, materials, dressing and geography assets unchanged");
            var source=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(b.sheet);var sheet=Find<WorldMacroDressingRenderer>().Sheet;
            Check(sheet.UseSpeciesRetention&&sheet.SpeciesRetention==new Vector3(.65f,.5f,.4f),"tree65 shrub50 grass40 policy enabled");
            Check(JsonUtility.ToJson(new Fixed{rows=source.FixedPlacements})==JsonUtility.ToJson(new Fixed{rows=sheet.FixedPlacements}),"manual fixed placement preservation");
            Check(source.Cells.Length==sheet.Cells.Length&&source.Cells.Zip(sheet.Cells,(a,c)=>a.OpenGround.SequenceEqual(c.OpenGround)&&a.Habitat.SequenceEqual(c.Habitat)&&a.Heights.SequenceEqual(c.Heights)).All(x=>x),"existing open ground masks, habitat and elevation unchanged");
            Check(source.Seed==sheet.Seed&&source.TreeDistance==sheet.TreeDistance&&source.ForestDistance==sheet.ForestDistance,"seed and far rendering distances unchanged");
            var objects=All.ToLookup(PathOf,t=>t);int paired=0;
            Check(b.staticPlants.All(p=>objects[p.path].Single().gameObject.activeSelf==(p.active&&Selected(p.path,0,1,.65f).Length==1)),"static ordinary trees follow stable 65% selection at the assembly root, all LODs and colliders together");
            Check(b.staticPlants.Where(p=>!objects[p.path].Single().gameObject.activeSelf).All(p=>!objects[p.path].Single().GetComponentsInChildren<Collider>(true).Any(c=>c.enabled&&c.gameObject.activeInHierarchy)),"no active colliders on removed static tree assemblies");
            foreach(var extra in Extras().Extras)
            {
                var component=objects[extra.Path].Single().GetComponent<EarlyRegionFoliage>();
                for(int pi=0;pi<extra.Packets.Length;pi++)
                {
                    var before=extra.Packets[pi];var after=component.Packets[pi];var ids=Selected(extra.Path,pi,ActualCount(before),Retained(Prototype(source,before)));
                    bool pass=after.Count==ids.Length&&before.Bounds==after.Bounds&&before.Distance==after.Distance;
                    foreach(var parts in new[]{(before.Near,after.Near),(before.Far,after.Far)})
                        for(int j=0;j<parts.Item1.Length;j++)pass&=parts.Item1[j].Mesh==parts.Item2[j].Mesh&&ids.Select(i=>parts.Item1[j].Matrices[i]).SequenceEqual(parts.Item2[j].Matrices);
                    if(!pass)checks.Add("FAIL supplementary matrices/paired indices "+extra.Path+"/"+pi);else paired++;
                }
            }
            checks.Add("PASS "+paired+" supplementary packets retain identical near/far instance indices and original matrices");
            var profile=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(Folder+"/Profile.asset");int samples=0,fail=0;
            for(int broad=0;broad<=10;broad++)for(int detail=0;detail<=10;detail++)
            {
                float previous=-1;
                for(int d=0;d<=2600;d++)
                {
                    var color=Color.Lerp(profile.Ink.linear,profile.Paper.linear,profile.EvaluateDarkMountainTone(broad*.1f,detail*.1f,d));
                    color=Color.Lerp(color,profile.Air.linear,profile.EvaluateAtmosphereWash(d));float l=color.r*.2126f+color.g*.7152f+color.b*.0722f;
                    if(l+1e-6f<previous)fail++;previous=l;samples++;
                }
            }
            Check(fail==0,"fixed surface/light distance monotonic luminance "+samples+" samples; decreases="+fail);
            foreach(string name in new[]{"Oheangbu/CompactNaturalGround","Oheangbu/CompactNaturalVegetation","Oheangbu/WorldMacroTerrain","Oheangbu/EarlyRegionFoliageCard"})
            {var s=Shader.Find(name);Check(s!=null&&!ShaderUtil.ShaderHasError(s),"shader "+name);if(s!=null)foreach(var msg in ShaderUtil.GetShaderMessages(s))checks.Add(msg.severity+": "+msg.message);}
            checks.Add("UNVERIFIED final visual approval, player-input traversal and full-world species census. Sampled generation and Play performance are separate receipts.");
            File.WriteAllText(Output+"/checks.json",JsonUtility.ToJson(new Rows{status=checks.Any(x=>x.StartsWith("FAIL"))?"FAIL":"PASS",checks=checks.ToArray()},true));return string.Join("\n",checks);
        }
        [Serializable] sealed class Fixed {public WorldMacroDressingSheetSO.FixedPlacement[] rows;}
        static string Retention(int cellIndex)
        {
            var r=Find<WorldMacroDressingRenderer>();string json=r.ValidateSpeciesRetention(new[]{cellIndex});
            File.WriteAllText(Output+"/retention_cell_"+cellIndex+".json",json);return json;
        }
    }
}
