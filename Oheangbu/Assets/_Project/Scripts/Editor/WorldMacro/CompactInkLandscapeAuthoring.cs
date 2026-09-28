using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;
using Oheangbu.App.World.Dressing;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class CompactInkLandscapeAuthoring
    {
        static string Folder=>WorldMacroCompactAuthoring.Folder+"/InkLandscape";
        static string Output=>WorldMacroCompactAuthoring.Output+"/InkLandscape";
        static IEnumerable<Transform> All=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
        static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        static T Find<T>() where T:Component=>CompactRecovery.Find<T>();
        static void Guard()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)throw new InvalidOperationException("Compact Edit scene required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; stopped before allocation");
            Directory.CreateDirectory(Output);
        }
        [Serializable] sealed class Binding{public string path;public string[] materials;public bool ground;}
        [Serializable] sealed class Baseline{public string sheet,regionalSky,sky;public Binding[] bindings;public string[] structure;}
        [Serializable] sealed class View{public string id;public Vector3 eye,target;}
        [Serializable] sealed class Views{public View[] views;}
        [Serializable] sealed class ExtraFoliage{public string path,digest;public string[] materials;}
        [Serializable] sealed class Supplement{public ExtraFoliage[] foliage;public Binding[] soil;public string[] sourceHashes;}
        static string Digest(string text){using(var hash=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)));}
        static string PacketDigest(EarlyRegionFoliage f)=>Digest(string.Join("\n",f.Packets.Select(p=>p.Bounds.ToString("R")+"|"+p.Distance+"|"+p.Count+"|"+string.Join(";",p.Near.Concat(p.Far).Select(part=>AssetDatabase.GetAssetPath(part.Mesh)+"|"+part.Submesh+"|"+string.Join("/",part.Matrices.Select(m=>m.ToString("R"))))))));
        static Supplement SupplementSources()
        {
            string path=Output+"/supplement_sources.json";
            if(File.Exists(path))
            {
                var saved=JsonUtility.FromJson<Supplement>(File.ReadAllText(path));
                const string yard="Playtest_OriginalC2Inn/Packed_Earth_Forecourt";
                if(!saved.soil.Any(b=>b.path==yard))
                {
                    var t=All.Single(t=>PathOf(t)==yard);var paths=t.GetComponent<Renderer>().sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray();
                    saved.soil=saved.soil.Concat(new[]{new Binding{path=yard,ground=true,materials=paths}}).ToArray();
                    saved.sourceHashes=saved.sourceHashes.Concat(paths.Select(p=>p+"|"+Digest(File.ReadAllText(p)))).Distinct().ToArray();File.WriteAllText(path,JsonUtility.ToJson(saved,true));
                }
                return saved;
            }
            var s=new Supplement{
                foliage=All.Select(t=>t.GetComponent<EarlyRegionFoliage>()).Where(f=>f!=null).Select(f=>new ExtraFoliage{path=PathOf(f.transform),digest=PacketDigest(f),materials=f.Packets.SelectMany(p=>p.Near.Concat(p.Far)).Select(p=>AssetDatabase.GetAssetPath(p.Material)).ToArray()}).ToArray(),
                soil=All.Where(t=>(t.name=="h1_house_ground"&&PathOf(t).StartsWith("Playtest_OriginalC2Inn/"))||PathOf(t)=="Playtest_OriginalC2Inn/Packed_Earth_Forecourt").Select(t=>new Binding{path=PathOf(t),ground=true,materials=t.GetComponent<Renderer>().sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray()}).ToArray()};
            s.sourceHashes=s.foliage.SelectMany(f=>f.materials).Concat(s.soil.SelectMany(b=>b.materials)).Distinct().Select(p=>p+"|"+Digest(File.ReadAllText(p))).ToArray();
            File.WriteAllText(path,JsonUtility.ToJson(s,true));return s;
        }
        [Serializable] sealed class ApplicationReport{public int groundRenderers,vegetationMaterials,cells,maskedSamples,protectedSamples;public float regionalColour;public string status;public string[] checks;}
        static Baseline ReadBaseline()=>JsonUtility.FromJson<Baseline>(File.ReadAllText(Output+"/baseline.json"));
        static bool IsGround(Renderer r)
        {
            string p=PathOf(r.transform);
            return p.Contains("01_GlobalTerrain_IndependentOfRoads/")||p.Contains("BackgroundContext_RenderOnly")||p.Contains("01_TerrainConformedRoutes/")||p=="Playtest_Village_Office/Courtyard";
        }
        static string[] Structure()=>All.Select(t=>{
            string mesh=string.Join(",",t.GetComponents<MeshFilter>().Select(m=>m.sharedMesh==null?"null":AssetDatabase.GetAssetPath(m.sharedMesh)+":"+m.sharedMesh.vertexCount));
            string colliders=string.Join(",",t.GetComponents<Collider>().Select(c=>c.GetType().Name+":"+c.enabled+":"+c.isTrigger+":"+(c is MeshCollider mc?AssetDatabase.GetAssetPath(mc.sharedMesh):EditorJsonUtility.ToJson(c))));
            return PathOf(t)+"|"+t.localPosition.ToString("R")+"|"+t.localRotation.ToString("R")+"|"+t.localScale.ToString("R")+"|"+mesh+"|"+colliders;
        }).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        public static string Execute(string command)
        {
            if(command=="performance-source"||command=="performance-restored")
            {
                var session=Find<Oheangbu.App.World.WorldMacroPlaytestSession>();
                if(!Application.isPlaying||session==null||string.IsNullOrEmpty(session.TestSaveSuffix))throw new InvalidOperationException("Isolated Play required");
                return Comparison(command=="performance-source");
            }
            Guard();
            if(command=="preserve")return Preserve();
            if(command=="apply")return Apply();
            if(command=="verify")return Verify();
            if(command.StartsWith("capture-original:"))return CaptureOriginal(command.Substring(17));
            if(command=="yard-audit")return string.Join("\n",All.Select(t=>t.GetComponent<Renderer>()).Where(r=>r!=null&&r.enabled&&r.bounds.size.x>12&&r.bounds.size.z>12&&r.bounds.center.y<136&&Vector2.Distance(new Vector2(r.bounds.center.x,r.bounds.center.z),new Vector2(888,245))<90).Select(r=>PathOf(r.transform)+" "+r.bounds+" "+string.Join(",",r.sharedMaterials.Select(m=>AssetDatabase.GetAssetPath(m)+" "+m.shader.name))));
            if(command.StartsWith("capture:"))return Capture(command.Substring(8));
            throw new ArgumentException(command);
        }
        static string Preserve()
        {
            if(File.Exists(Output+"/baseline.json"))return "Baseline already preserved";
            var scene=SceneManager.GetActiveScene();File.Copy(scene.path,Output+"/before_disk.unity",false);
            if(!EditorSceneManager.SaveScene(scene,Output+"/before_open.unity",true))throw new IOException("Open scene backup failed");
            var look=new SerializedObject(Find<Oheangbu.App.WorldLookDriver>());
            var b=new Baseline{sheet=AssetDatabase.GetAssetPath(Find<WorldMacroDressingRenderer>().Sheet),
                regionalSky=AssetDatabase.GetAssetPath(look.FindProperty("_regionalSkyProfile").objectReferenceValue),sky=AssetDatabase.GetAssetPath(look.FindProperty("_inkSkyProfile").objectReferenceValue),structure=Structure(),
                bindings=All.Select(t=>t.GetComponent<Renderer>()).Where(r=>r!=null&&(IsGround(r)||r.sharedMaterials.Any(m=>m!=null&&m.shader.name=="Oheangbu/CompactNaturalVegetation"))).Select(r=>new Binding{path=PathOf(r.transform),ground=IsGround(r),materials=r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray()}).ToArray()};
            File.WriteAllText(Output+"/baseline.json",JsonUtility.ToJson(b,true));
            var geo=Find<WorldMacroDressingRenderer>().Sheet.Geography;
            var views=new List<View>{new View{id="inn",eye=new Vector3(907,135.2f,208),target=new Vector3(888,135,245)},new View{id="office",eye=new Vector3(882,137,240),target=new Vector3(830,133,176)}};
            foreach(string id in new[]{"DeepForest","SouthGate","Jeokro","Cheolong","Hyeongang","Hwanggyeong"})
            {
                var site=geo.FindSite(id);if(site==null)continue;Vector3 eye=site.Position+new Vector3(36,3,-50);
                if(Physics.Raycast(eye+Vector3.up*500,Vector3.down,out var hit,900,~0,QueryTriggerInteraction.Ignore))eye.y=hit.point.y+1.75f;
                views.Add(new View{id=id,eye=eye,target=site.Position+Vector3.up*3});
            }
            var route=geo.Routes.FirstOrDefault(r=>r.Id.Contains("Inn")&&r.Points.Length>3)??geo.Routes.First(r=>r.Points.Length>3);
            int index=route.Points.Length/2;var mid=route.Points[index];views.Insert(1,new View{id="mountain_path",eye=mid+Vector3.up*1.75f,target=route.Points[Math.Min(index+2,route.Points.Length-1)]+Vector3.up*4});
            for(int i=0;i<geo.Regions.Length;i++)
            {
                var region=geo.Regions[i];var point=region.Polygon[0];Vector3 eye=new Vector3(point.x,500,point.y);
                if(Physics.Raycast(eye+Vector3.up*1000,Vector3.down,out var hit,2500,~0,QueryTriggerInteraction.Ignore)){eye.y=hit.point.y+1.75f;views.Add(new View{id="boundary_"+region.Realm,eye=eye,target=eye+new Vector3(90,5,120)});}
            }
            File.WriteAllText(Output+"/views.json",JsonUtility.ToJson(new Views{views=views.ToArray()},true));
            return "Preserved live/disk scene, "+b.structure.Length+" transforms and "+views.Count+" fixed views";
        }
        static readonly Color[] GrassTints={new Color(.54f,.67f,.47f),new Color(.63f,.61f,.48f),new Color(.59f,.47f,.38f),new Color(.50f,.56f,.49f),new Color(.45f,.60f,.51f)};
        static readonly Color[] Tints={new Color(.66f,.76f,.62f),new Color(.76f,.73f,.61f),new Color(.77f,.61f,.53f),new Color(.62f,.69f,.71f),new Color(.59f,.7f,.71f)};
        static float Luma(Color c)=>c.r*.2126f+c.g*.7152f+c.b*.0722f;
        static Color ReduceColour(Color value,Color anchor,float contribution)
        {
            var v=value.linear;var a=anchor.linear;float l=Luma(v);a*=l/Mathf.Max(.0001f,Luma(a));
            Color result=Color.Lerp(a,v,contribution).gamma;result.a=value.a;return result;
        }
        static Material Derive(Material source,string key,CompactInkLandscapeProfile profile,int realm=-1,float tintStrength=0)
        {
            string path=Folder+"/Materials/"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source))+key+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);var m=new Material(source);
            m.name=source.name+"_InkLandscape";
            if(m.shader.name=="Oheangbu/WorldMacroVegetation")m.shader=Shader.Find("Oheangbu/CompactNaturalVegetation");
            if(realm>=0&&m.HasProperty("_BaseColor"))
            {
                Color baseColour=source.GetColor("_BaseColor");
                Color oldFactor=Color.Lerp(Color.white,Tints[realm],tintStrength);
                foreach(float candidate in new[]{.25f,.35f,.45f})
                {var f=Color.Lerp(Color.white,Tints[realm],candidate);if(Vector3.Distance(new Vector3(baseColour.r,baseColour.g,baseColour.b),new Vector3(f.r,f.g,f.b))<.002f)oldFactor=f;}
                var grass=GrassTints[realm];if(Vector3.Distance(new Vector3(baseColour.r,baseColour.g,baseColour.b),new Vector3(grass.r,grass.g,grass.b))<.002f)oldFactor=grass;
                Color factor=ReduceColour(oldFactor,Color.white,profile.RegionalColourContribution);
                Color c=source.GetColor("_BaseColor");c.r*=factor.r/oldFactor.r;c.g*=factor.g/oldFactor.g;c.b*=factor.b/oldFactor.b;m.SetColor("_BaseColor",c);
            }
            if(m.HasProperty("_RealmTintStrength"))m.SetFloat("_RealmTintStrength",source.GetFloat("_RealmTintStrength")*profile.RegionalColourContribution);
            if(m.shader.name=="Oheangbu/CompactNaturalGround")m.SetFloat("_Saturation",profile.BaseSaturation);
            profile.Apply(m);
            if(existing==null){AssetDatabase.CreateAsset(m,path);EditorUtility.SetDirty(m);return m;}
            EditorUtility.CopySerialized(m,existing);Object.DestroyImmediate(m);EditorUtility.SetDirty(existing);return existing;
        }
        static string Apply()
        {
            if(!File.Exists(Output+"/baseline.json"))throw new InvalidOperationException("Preserve first");
            var b=ReadBaseline();var supplement=SupplementSources();Directory.CreateDirectory(Folder+"/Materials");AssetDatabase.Refresh();
            var profile=AssetDatabase.LoadAssetAtPath<CompactInkLandscapeProfile>(Folder+"/Profile.asset");
            if(profile==null){profile=ScriptableObject.CreateInstance<CompactInkLandscapeProfile>();AssetDatabase.CreateAsset(profile,Folder+"/Profile.asset");}
            var source=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(b.sheet);
            var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(Folder+"/Dressing.asset");
            if(sheet==null){sheet=Object.Instantiate(source);AssetDatabase.CreateAsset(sheet,Folder+"/Dressing.asset");}else EditorUtility.CopySerialized(source,sheet);
            sheet.name="CompactInkLandscapeDressing";sheet.UseOpenGround=true;sheet.OpenGrassDensity=profile.OpenGrassDensity;sheet.OpenShrubDensity=profile.OpenShrubDensity;
            var mats=new Dictionary<string,Material>();
            Material Vegetation(Material old,int realm,float strength)
            {
                string key=AssetDatabase.GetAssetPath(old)+"|"+realm+"|"+strength;
                if(!mats.TryGetValue(key,out var m)){m=Derive(old,"_r"+realm+"_t"+Mathf.RoundToInt(strength*100),profile,realm,strength);mats[key]=m;}return m;
            }
            foreach(var proto in sheet.Prototypes)
            {
                if(proto.Category!=WorldMacroDressingSheetSO.Kind.Tree&&proto.Category!=WorldMacroDressingSheetSO.Kind.Grass&&proto.Category!=WorldMacroDressingSheetSO.Kind.Shrub)continue;
                for(int i=0;i<proto.Lods.Length;i++)for(int j=0;j<proto.Lods[i].Parts.Length;j++)
                {
                    var original=source.Prototypes.First(p=>p.Id==proto.Id).Lods[i].Parts[j].Material;
                    if(original!=null)proto.Lods[i].Parts[j].Material=Vegetation(original,(int)proto.Realm,proto.Category==WorldMacroDressingSheetSO.Kind.Tree&&i>=2?.25f:.45f);
                }
            }
            int masked=0,protectedCount=0;
            foreach(var cell in sheet.Cells)
            {
                cell.OpenGround=new byte[4096];
                for(int z=0;z<64;z++)for(int x=0;x<64;x++)
                {
                    float wx=sheet.Geography.BoundsMin.x+cell.X*256+x*4+2,wz=sheet.Geography.BoundsMin.y+cell.Z*256+z*4+2;
                    float cluster=Mathf.PerlinNoise((wx+sheet.Seed%1300)*.0103f,(wz-sheet.Seed%1900)*.0103f);
                    float open=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.43f,.67f,cluster));
                    if((cell.Habitat[z*64+x]&7)==2){open=0;protectedCount++;}
                    foreach(var story in sheet.StoryClusters)
                    {
                        if(story.Theme!="Heartland"&&story.Theme!="OldGrove")continue;
                        float d=Vector2.Distance(new Vector2(wx,wz),new Vector2(story.Centre.x,story.Centre.z));
                        open*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(story.Radius,story.Radius+40,d));
                    }
                    cell.OpenGround[z*64+x]=(byte)Mathf.RoundToInt(open*255);if(open>.5f)masked++;
                }
            }
            var objects=All.ToLookup(PathOf,t=>t);int ground=0;
            foreach(var binding in b.bindings)
            {
                var t=objects[binding.path].FirstOrDefault();if(t==null)throw new InvalidOperationException("Renderer missing: "+binding.path);
                var rr=t.GetComponent<Renderer>();var list=new Material[binding.materials.Length];
                for(int i=0;i<list.Length;i++)
                {
                    var old=AssetDatabase.LoadAssetAtPath<Material>(binding.materials[i]);if(old==null)throw new InvalidOperationException("Missing source material");
                    if(binding.ground){list[i]=Derive(old,"_ground",profile);continue;}
                    var proto=source.Prototypes.FirstOrDefault(p=>old.name.Contains(p.Id));
                    int realm=proto==null?-1:(int)proto.Realm;list[i]=Vegetation(old,realm,.45f);
                }
                rr.sharedMaterials=list;EditorUtility.SetDirty(rr);if(binding.ground)ground++;
            }
            foreach(var extra in supplement.foliage)
            {
                var f=objects[extra.path].Single().GetComponent<EarlyRegionFoliage>();int index=0;
                foreach(var part in f.Packets.SelectMany(p=>p.Near.Concat(p.Far)))
                {
                    var old=AssetDatabase.LoadAssetAtPath<Material>(extra.materials[index++]);
                    var proto=source.Prototypes.OrderByDescending(p=>p.Id.Length).FirstOrDefault(p=>old.name.Contains(p.Id));
                    part.Material=Vegetation(old,proto==null?-1:(int)proto.Realm,.35f);
                }
                if(PacketDigest(f)!=extra.digest)throw new InvalidOperationException("Supplementary foliage geometry changed");
                EditorUtility.SetDirty(f);
            }
            foreach(var binding in supplement.soil)
            {
                var rr=objects[binding.path].Single().GetComponent<Renderer>();
                rr.sharedMaterials=binding.materials.Select(path=>{
                    var src=AssetDatabase.LoadAssetAtPath<Material>(path);var m=Derive(src,"_yard",profile);
                    m.shader=Shader.Find("Oheangbu/CompactNaturalGround");m.SetTexture("_DirtMap",src.GetTexture("_BaseMap"));m.SetTexture("_DirtNormal",src.GetTexture("_BumpMap"));
                    m.SetTexture("_RockMap",src.GetTexture("_BaseMap"));m.SetTexture("_RockNormal",src.GetTexture("_BumpMap"));
                    bool projected=src.HasProperty("_WorldTiling");m.SetFloat("_SourceUV",projected?0:1);if(projected)m.SetVector("_Tiling",Vector4.one*src.GetFloat("_WorldTiling"));
                    var scale=src.GetTextureScale("_BaseMap");var offset=src.GetTextureOffset("_BaseMap");m.SetVector("_SourceUVTransform",new Vector4(scale.x,scale.y,offset.x,offset.y));
                    m.SetFloat("_GroundKind",1);m.SetFloat("_EdgeFade",0);m.SetFloat("_GroundPath",0);m.SetFloat("_RealmTintStrength",0);m.SetFloat("_Saturation",profile.BaseSaturation);
                    profile.Apply(m);EditorUtility.SetDirty(m);return m;}).ToArray();EditorUtility.SetDirty(rr);ground++;
            }
            var regionalSource=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(b.regionalSky);var baseSky=AssetDatabase.LoadAssetAtPath<InkSkyProfile>(b.sky);
            var privateSky=AssetDatabase.LoadAssetAtPath<InkSkyProfile>(Folder+"/Sky.asset");
            if(privateSky==null){privateSky=Object.Instantiate(baseSky);AssetDatabase.CreateAsset(privateSky,Folder+"/Sky.asset");}else EditorUtility.CopySerialized(baseSky,privateSky);
            EditorUtility.SetDirty(privateSky);
            var regional=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(Folder+"/RegionalSky.asset");
            if(regional==null){regional=Object.Instantiate(regionalSource);AssetDatabase.CreateAsset(regional,Folder+"/RegionalSky.asset");}else EditorUtility.CopySerialized(regionalSource,regional);
            foreach(var entry in regional.Regions)
            {entry.Horizon=ReduceColour(entry.Horizon,baseSky.Horizon,profile.RegionalColourContribution);entry.Zenith=ReduceColour(entry.Zenith,baseSky.Zenith,profile.RegionalColourContribution);entry.Cloud=ReduceColour(entry.Cloud,baseSky.Cloud,profile.RegionalColourContribution);}
            EditorUtility.SetDirty(regional);var look=new SerializedObject(Find<Oheangbu.App.WorldLookDriver>());look.FindProperty("_regionalSkyProfile").objectReferenceValue=regional;look.FindProperty("_inkSkyProfile").objectReferenceValue=privateSky;look.ApplyModifiedPropertiesWithoutUndo();
            var dressing=Find<WorldMacroDressingRenderer>();dressing.Sheet=sheet;dressing.ResetCache();EditorUtility.SetDirty(sheet);EditorUtility.SetDirty(dressing);
            var report=new ApplicationReport{groundRenderers=ground,vegetationMaterials=mats.Count,cells=sheet.Cells.Length,maskedSamples=masked,protectedSamples=protectedCount,regionalColour=profile.RegionalColourContribution,status="APPLIED_PENDING_VISUAL_REVIEW"};
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.WriteAllText(Output+"/application.json",JsonUtility.ToJson(report,true));return JsonUtility.ToJson(report);
        }
        static string Verify()
        {
            var b=ReadBaseline();var checks=new List<string>();
            checks.Add((Structure().SequenceEqual(b.structure)?"PASS":"FAIL")+" original transforms, mesh references and colliders preserved");
            foreach(string name in new[]{"Oheangbu/CompactNaturalGround","Oheangbu/CompactNaturalVegetation","Oheangbu/WorldMacroTerrain","Oheangbu/EarlyRegionFoliageCard"})
            {var s=Shader.Find(name);checks.Add((s!=null&&!ShaderUtil.ShaderHasError(s)?"PASS":"FAIL")+" shader "+name);if(s!=null)foreach(var m in ShaderUtil.GetShaderMessages(s))checks.Add(m.severity+": "+m.message);}
            var sheet=Find<WorldMacroDressingRenderer>().Sheet;var source=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(b.sheet);
            checks.Add((JsonUtility.ToJson(new FixedRows{rows=sheet.FixedPlacements})==JsonUtility.ToJson(new FixedRows{rows=source.FixedPlacements})?"PASS":"FAIL")+" fixed placements preserved");
            checks.Add((sheet.Seed==source.Seed&&sheet.TreeDistance==source.TreeDistance&&sheet.ForestDistance==source.ForestDistance?"PASS":"FAIL")+" placement seed and LOD distances preserved");
            checks.Add((sheet.Cells.All(c=>c.OpenGround!=null&&c.OpenGround.Length==4096)?"PASS":"FAIL")+" all cells have open-ground weights");
            var probe=new WorldMacroDressingSheetSO.Cell{OpenGround=Enumerable.Repeat((byte)255,4096).ToArray()};
            checks.Add((Mathf.Abs(sheet.OpenGroundDensity(probe,0,0,2)-.25f)<.00001f&&Mathf.Abs(sheet.OpenGroundDensity(probe,0,0,1)-.35f)<.00001f&&sheet.OpenGroundDensity(probe,0,0,0)==1?"PASS":"FAIL")+" grass25/shrub35/tree100 density policy");
            var supplement=SupplementSources();var objects=All.ToLookup(PathOf,t=>t);
            checks.Add((supplement.foliage.All(f=>PacketDigest(objects[f.path].Single().GetComponent<EarlyRegionFoliage>())==f.digest)?"PASS":"FAIL")+" supplementary near/far meshes, placements and ranges preserved");
            checks.Add((supplement.foliage.All(f=>objects[f.path].Single().GetComponent<EarlyRegionFoliage>().Packets.SelectMany(p=>p.Near.Concat(p.Far)).All(p=>p.Material.HasProperty("_CIEnabled")&&p.Material.GetFloat("_CIEnabled")==1))?"PASS":"FAIL")+" shared atmosphere on supplementary near/far foliage");
            foreach(var mat in supplement.foliage.SelectMany(f=>objects[f.path].Single().GetComponent<EarlyRegionFoliage>().Packets.SelectMany(p=>p.Near.Concat(p.Far))).Select(p=>p.Material).Distinct().Where(m=>!m.HasProperty("_CIEnabled")||m.GetFloat("_CIEnabled")!=1))checks.Add("COVERAGE " +AssetDatabase.GetAssetPath(mat)+" shader="+mat.shader.name);
            checks.Add((supplement.sourceHashes.All(row=>{var split=row.LastIndexOf('|');return Digest(File.ReadAllText(row.Substring(0,split)))==row.Substring(split+1);})?"PASS":"FAIL")+" supplementary source materials preserved");
            checks.Add("UNVERIFIED actual input traversal, boundary-transition visual judgement and final artistic approval; diagnostic CPU/GPU receipts are separate");
            File.WriteAllText(Output+"/checks.json",JsonUtility.ToJson(new ApplicationReport{status=checks.Any(s=>s.StartsWith("FAIL"))?"FAIL":"STATIC_CHECKS_PASS",checks=checks.ToArray()},true));return string.Join("\n",checks);
        }
        [Serializable] sealed class FixedRows{public WorldMacroDressingSheetSO.FixedPlacement[] rows;}
        static Action comparisonRestore;
        static string Comparison(bool original)
        {
            if(!original){comparisonRestore?.Invoke();comparisonRestore=null;return "Restored derived look and cleared streaming cache";}
            if(comparisonRestore!=null)throw new InvalidOperationException("Comparison already active");
            var b=ReadBaseline();var extra=SupplementSources();var objects=All.ToLookup(PathOf,t=>t);
            var bindings=b.bindings.Concat(extra.soil).ToArray();
            var renderers=bindings.Select(b=>objects[b.path].Single().GetComponent<Renderer>()).ToArray();var originals=renderers.Select(r=>r.sharedMaterials).ToArray();
            var parts=extra.foliage.SelectMany(f=>objects[f.path].Single().GetComponent<EarlyRegionFoliage>().Packets.SelectMany(p=>p.Near.Concat(p.Far))).ToArray();var materials=parts.Select(p=>p.Material).ToArray();
            var dressing=Find<WorldMacroDressingRenderer>();var sheet=dressing.Sheet;var look=new SerializedObject(Find<Oheangbu.App.WorldLookDriver>());var sky=look.FindProperty("_regionalSkyProfile").objectReferenceValue;
            comparisonRestore=()=>{
                for(int i=0;i<renderers.Length;i++)renderers[i].sharedMaterials=originals[i];for(int i=0;i<parts.Length;i++)parts[i].Material=materials[i];
                dressing.Sheet=sheet;dressing.ResetCache();look.FindProperty("_regionalSkyProfile").objectReferenceValue=sky;look.ApplyModifiedPropertiesWithoutUndo();
            };
            try
            {
                for(int i=0;i<renderers.Length;i++)renderers[i].sharedMaterials=bindings[i].materials.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
                var paths=extra.foliage.SelectMany(f=>f.materials).ToArray();for(int i=0;i<parts.Length;i++)parts[i].Material=AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
                dressing.Sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(b.sheet);dressing.ResetCache();
                look.FindProperty("_regionalSkyProfile").objectReferenceValue=AssetDatabase.LoadAssetAtPath<RegionalInkSkyProfile>(b.regionalSky);look.ApplyModifiedPropertiesWithoutUndo();
                return "Original look for isolated comparison only";
            }
            catch {Comparison(false);throw;}
        }
        static string CaptureOriginal(string request)
        {
            Comparison(true);try{return Capture(request);}finally{Comparison(false);}
        }
        public static string CaptureReviewAt(string request,string output)
        {
            Guard();return Capture(request,output);
        }
        static string Capture(string request,string output=null)
        {
            output=output??Output;
            string[] fields=request.Split(':');if(fields.Length!=2)throw new ArgumentException("capture:before/after:view");
            var v=JsonUtility.FromJson<Views>(File.ReadAllText(output+"/views.json")).views.Single(x=>x.id==fields[1]);
            string path=output+"/"+fields[0]+"_"+v.id+".png";
            if(fields[0]=="before"&&File.Exists(path))return "Baseline capture already exists: "+path;
            var source=Find<Oheangbu.App.World.Vehicle.WorldMacroPalanquinSeat>().ViewCamera;
            var dressing=Find<WorldMacroDressingRenderer>();var observer=dressing.Observer;bool diag=dressing.AllowDiagnosticCameras;
            var sky=RenderSettings.skybox;var skyCopy=sky==null?null:new Material(sky);var active=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
            RenderTexture rt=null;Texture2D pixels=null;GameObject go=null;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;go=new GameObject("InkLandscapeReview"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(source);cam.enabled=false;
                cam.transform.SetPositionAndRotation(v.eye,Quaternion.LookRotation(v.target-v.eye));cam.fieldOfView=60;cam.aspect=16f/9;cam.nearClipPlane=.06f;cam.farClipPlane=4500;cam.orthographic=false;cam.useOcclusionCulling=false;
                var data=cam.GetUniversalAdditionalCameraData();var old=source.GetComponent<UniversalAdditionalCameraData>();if(old!=null)EditorUtility.CopySerialized(old,data);data.renderType=CameraRenderType.Base;data.cameraStack.Clear();
                int ui=LayerMask.NameToLayer("UI");if(ui>=0)cam.cullingMask&=~(1<<ui);
                Find<Oheangbu.App.WorldLookDriver>()?.PreviewRegionalSky(v.eye);dressing.Observer=cam;dressing.AllowDiagnosticCameras=true;
                var warm=System.Diagnostics.Stopwatch.StartNew();
                do {Guard();dressing.PrepareView(v.eye,128);} while(dressing.PendingChunks>0&&warm.Elapsed.TotalSeconds<35);
                if(dressing.PendingChunks>0)throw new InvalidOperationException("Capture deferred: "+dressing.PendingChunks+" streaming jobs remain; retry same view to finish preparation");
                rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);rt.Create();cam.targetTexture=rt;
                // Edit-only preparation, no repeated draw submissions and no budget edits.
                dressing.PrepareStillCapturePackets(cam,Guard);
                var timer=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<6;i++)RenderPipeline.SubmitRenderRequest(cam,new RenderPipeline.StandardRequest{destination=rt});timer.Stop();
                if(dressing.PendingObserverPackets>0)throw new InvalidOperationException("Capture deferred: visible packets changed during capture");
                File.WriteAllText(path.Replace(".png","_submissions.json"),dressing.RenderCostJson());
                File.WriteAllText(path.Replace(".png","_timing.txt"),"Six synchronous diagnostic submissions CPU wall time (NOT live frame time): "+timer.Elapsed.TotalMilliseconds+" ms. Commit: "+Prologue.PrologueAudit.CommitRatio());
                Guard();RenderTexture.active=rt;pixels=new Texture2D(1920,1080,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
                if(pixels.GetPixels32().Count(c=>c.r+c.g+c.b>12)<1920*1080/100)throw new InvalidOperationException("Black capture rejected");
                File.WriteAllBytes(path,pixels.EncodeToPNG());return path;
            }
            finally
            {
                dressing.Observer=observer;dressing.AllowDiagnosticCameras=diag;ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=active;
                if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(pixels!=null)Object.DestroyImmediate(pixels);if(go!=null)Object.DestroyImmediate(go);
                if(skyCopy!=null){sky.CopyPropertiesFromMaterial(skyCopy);Object.DestroyImmediate(skyCopy);}RenderSettings.skybox=sky;
            }
        }
    }
}
