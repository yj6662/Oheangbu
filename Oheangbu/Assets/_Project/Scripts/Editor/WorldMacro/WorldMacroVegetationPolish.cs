using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.Data.World;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Scoped opt-in vegetation derivative. Never opens/rebuilds terrain or runs a build/capture.</summary>
    public static partial class WorldMacroVegetationPolish
    {
        public const string SheetPath=WorldMacroDressingAssets.PolishFolder+"/Dressing_Dense.asset";
        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestPolish/Vegetation"));
        static IEnumerator<string> job;static double nextTick;static string action;
        [Serializable] sealed class JobStatus
        {public string utc,action,state,message;public int prototypes,prepared,regions;public float commit;}
        [Serializable] sealed class Validation
        {
            public string utc,scope="Static source, population, route clearance, material fade tests. Runtime rendering, performance, walking and final visuals remain unverified.";
            public string[] checks,unverified;public bool passed;public int prototypes,viewsPerTree,sourceTrees,sourceGrass,sourceShrubs,passages,typedAreas;
            public Row[] palette;public WorldMacroDressingRenderer.CellDiagnostic[] cells;
        }
        [Serializable] sealed class Row
        {public string id,source,sourceHash;public Vector3 actualPreparedSize;public int[] triangles;public int[] parts;public bool groundPatch;}
        public static string Run(string command)
        {
            if(command=="status")return File.Exists(Output+"/status.json")?File.ReadAllText(Output+"/status.json"):"No vegetation polish job has run.";
            if(command=="stop"){Stop("STOPPED","Saved assets retained; rerun prepare/region to resume.");return "Stopped vegetation authoring queue.";}
            Require();if(job!=null)throw new InvalidOperationException("A vegetation job is already running; use status.");
            var sheet=GetOrCreate();
            if(command=="validate")return Validate(sheet);
            if(command=="prepare")Start(command,Prepare(sheet));
            else if(command=="refreshgrass")Start(command,RefreshGrass(sheet));
            else if(command=="refreshplants")Start(command,RefreshPlants(sheet));
            else if(command=="optimizelod")Start(command,OptimizeShrubLods(sheet));
            else if(command=="cheongrim")Start(command,Apply(sheet,new[]{"Cheongrim"}));
            else if(command=="all")Start(command,Apply(sheet,new[]{"Cheongrim","Jeokro","Cheolong","Hyeongang","Hwanggyeong"}));
            else if(command.StartsWith("region:",StringComparison.Ordinal))Start(command,Apply(sheet,new[]{command.Substring(7)}));
            else throw new ArgumentException("prepare | refreshgrass | refreshplants | optimizelod | cheongrim | all | region:<id> | validate | status | stop");
            return "Queued "+command+". One source/cell per editor update; status: "+Output+"/status.json";
        }
        static void Require()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Vegetation asset authoring requires Edit mode.");
            if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroPlaytestAuthoring.ScenePath)throw new InvalidOperationException("Open the existing playtest scene. This command does not open or regenerate it.");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Vegetation paused: system commit >=85%.");
        }
        static Sheet GetOrCreate()
        {
            Directory.CreateDirectory(Output);Directory.CreateDirectory(WorldMacroDressingAssets.PolishFolder);
            var sheet=AssetDatabase.LoadAssetAtPath<Sheet>(SheetPath);if(sheet!=null)return sheet;
            var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();if(renderer?.Sheet==null)throw new InvalidOperationException("Current dressing renderer and original sheet required.");
            string original=AssetDatabase.GetAssetPath(renderer.Sheet);
            if(!File.Exists(Output+"/Baseline/applied_source.json")){Directory.CreateDirectory(Output+"/Baseline");File.WriteAllText(Output+"/Baseline/applied_source.json",EditorJsonUtility.ToJson(renderer.Sheet,true));File.WriteAllText(Output+"/Baseline/source_path.txt",original);}
            if(!AssetDatabase.CopyAsset(original,SheetPath))throw new IOException("Cannot preserve-copy active dressing.");
            sheet=AssetDatabase.LoadAssetAtPath<Sheet>(SheetPath);sheet.name="Dressing_Dense";sheet.DenseVegetation=true;sheet.PaletteVersion=3;
            sheet.TreeDistance=1600;sheet.ForestDistance=3200;sheet.GrassDistance=180;sheet.GrassMeshDistance=50;sheet.GroundCoverDistance=420;
            sheet.DenseTreeSpacing=7.5f;sheet.DenseShrubSpacing=4.5f;sheet.DenseGrassSpacing=1.6f;sheet.GroundCoverSpacing=8;
            sheet.DensityGain=new Vector4(1.45f,1.6f,1.8f,1);sheet.CellsPerFrame=1;sheet.CompletedRegions=Array.Empty<string>();
            // Geography, mask lattice, materials in original, and fixed placements are copied/preserved.
            EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();return sheet;
        }
        static IEnumerable<string> PrepareSequence(Sheet sheet)
        {
            while(sheet.PaletteVersion!=4||sheet.Prototypes.Any(p=>p.PolishVersion!=1))
            {WorldMacroDressingAssets.PreparePolish(sheet,1);yield return "Prepared "+sheet.Prototypes.Count(p=>p.PolishVersion==1)+" / "+sheet.Prototypes.Length+" existing/palette sources";}
        }
        static IEnumerator<string> Prepare(Sheet sheet)
        {foreach(string step in PrepareSequence(sheet))yield return step;yield return "Assets ready. Scene renderer remains on its previous sheet until region application.";}
        static IEnumerator<string> Apply(Sheet sheet,string[] regions)
        {
            if(sheet.PaletteVersion!=4)throw new InvalidOperationException("Run prepare and wait for COMPLETED before region application.");
            ConfigureClearance(sheet);EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();
            foreach(string region in regions)
            {
                foreach(string step in WorldMacroDressingAuthoring.RebakePolishMasks(sheet,region))yield return step;
                var completed=new HashSet<string>(sheet.CompletedRegions){region};sheet.CompletedRegions=completed.ToArray();EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();
                yield return region+" masks saved; fixed placements retained.";
            }
            var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();var original=renderer.Sheet;
            FixFixedTreeLods(sheet,original);renderer.Sheet=sheet;renderer.ResetCache();EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            yield return "Dense derivative installed. Terrain, routes, buildings, water, player and gameplay data unchanged.";
            File.WriteAllText(Output+"/TECHNICAL_REPORT.md",ReportText(sheet));
        }
        static void ConfigureClearance(Sheet sheet)
        {
            var areas=sheet.PreservedAreas.Where(a=>!a.Id.StartsWith("content:visual:main:",StringComparison.Ordinal)&&!a.Id.StartsWith("polish:",StringComparison.Ordinal)).ToList();
            foreach(var a in areas){a.TypedClearance=true;a.AffectedKinds=31;a.Padding=new Vector4(1.2f,.55f,.02f,1.2f);}
            var routes=new List<Sheet.Passage>();
            foreach(var route in sheet.Geography.Routes)for(int i=1;i<route.Points.Length;i++)routes.Add(new Sheet.Passage{Id="macro:"+routes.Count,A=route.Points[i-1],B=route.Points[i],Width=route.Width});
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(session?.Content!=null)
            {
                var content=session.Content;AddRoute("main",content.MainPath,4.1f);AddRoute("branch",content.BranchPath,3.2f);
                foreach(var encounter in content.Encounters)areas.Add(new Sheet.PreserveArea{Id="polish:encounter:"+encounter.Id,Centre=encounter.Feet,HalfSize=new Vector2(4.5f,4.5f),TypedClearance=true,AffectedKinds=11,Padding=Vector4.zero});
                foreach(var point in content.Points)
                {
                    if(content.MainPath.Length==0)break;var nearest=content.MainPath.OrderBy(p=>(p-point.Position).sqrMagnitude).First();
                    if((nearest-point.Position).sqrMagnitude<25*25)routes.Add(new Sheet.Passage{Id="discovery:"+point.Id,A=nearest,B=point.Position,Width=1.8f});
                }
            }
            sheet.Passages=routes.ToArray();sheet.PreservedAreas=areas.ToArray();
            void AddRoute(string id,Vector3[] points,float width){for(int i=1;i<points.Length;i++)routes.Add(new Sheet.Passage{Id=id+":"+i,A=points[i-1],B=points[i],Width=width});}
        }
        static void FixFixedTreeLods(Sheet dense,Sheet original)
        {
            if(original==dense){string sourceFile=Output+"/Baseline/source_path.txt";if(File.Exists(sourceFile))original=AssetDatabase.LoadAssetAtPath<Sheet>(File.ReadAllText(sourceFile));}
            if(original==null)return;var root=GameObject.Find(WorldMacroVisualCorridorAuthoring.RootName);if(root==null)return;
            var replacements=new Dictionary<Material,Material>();
            foreach(var p in original.Prototypes.Where(p=>p.Category==Sheet.Kind.Tree))
            {
                var target=dense.Prototypes.FirstOrDefault(q=>q.Id==p.Id);if(target==null)continue;
                for(int lod=0;lod<p.Lods.Length;lod++)for(int n=0;n<p.Lods[lod].Parts.Length;n++)
                {
                    var old=p.Lods[lod].Parts[n].Material;if(old==null||replacements.ContainsKey(old))continue;
                    var level=target.Lods[Mathf.Min(lod,target.Lods.Length-1)];var source=level.Parts[Mathf.Min(n,level.Parts.Length-1)].Material;
                    string path=WorldMacroDressingAssets.PolishFolder+"/Materials/Fixed_"+p.Id+"_"+lod+"_"+n+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(mat==null){mat=new Material(source){name="Fixed_"+p.Id+"_"+lod+"_"+n,enableInstancing=true};AssetDatabase.CreateAsset(mat,path);}
                    // Fixed GameObjects use their LODGroup, not the instanced population's metre bands.
                    mat.SetFloat("_FadeInStart",-1);mat.SetFloat("_FadeInEnd",0);mat.SetFloat("_FadeOutStart",99990);mat.SetFloat("_FadeOutEnd",99999);EditorUtility.SetDirty(mat);replacements[old]=mat;
                }
            }
            int changed=0;foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {var mats=renderer.sharedMaterials;bool dirty=false;for(int i=0;i<mats.Length;i++)if(mats[i]!=null&&replacements.TryGetValue(mats[i],out var replacement)){mats[i]=replacement;dirty=true;}if(dirty){renderer.sharedMaterials=mats;EditorUtility.SetDirty(renderer);changed++;}}
            File.WriteAllText(Output+"/fixed_tree_lod.json","{\"changed_renderers\":"+changed+",\"scope\":\"removed material metre fade from screen-size LOD trees; visual transition unverified\"}");
        }
        static void Start(string name,IEnumerator<string> iterator)
        {action=name;job=iterator;nextTick=0;EditorApplication.update+=Tick;WriteStatus("RUNNING","Queued; no capture/build requested.");}
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<nextTick)return;nextTick=EditorApplication.timeSinceStartup+.025;
            try
            {
                Require();if(job==null)return;
                if(!job.MoveNext()){Stop("COMPLETED","Authoring finished. Runtime and visual checks remain separate.");return;}
                WriteStatus("RUNNING",job.Current);
            }
            catch(Exception e){Stop(Prologue.PrologueAudit.CommitRatio()>=.85f?"PAUSED_MEMORY":"FAILED",e.ToString());Debug.LogException(e);}
        }
        static void Stop(string state,string message)
        {EditorApplication.update-=Tick;job?.Dispose();job=null;AssetDatabase.SaveAssets();WriteStatus(state,message);}
        static void WriteStatus(string state,string message)
        {
            Directory.CreateDirectory(Output);var sheet=AssetDatabase.LoadAssetAtPath<Sheet>(SheetPath);
            File.WriteAllText(Output+"/status.json",JsonUtility.ToJson(new JobStatus{utc=DateTime.UtcNow.ToString("O"),action=action,state=state,message=message,prototypes=sheet?.Prototypes.Length??0,prepared=sheet?.Prototypes.Count(p=>p.PolishVersion==1)??0,regions=sheet?.CompletedRegions.Length??0,commit=Prologue.PrologueAudit.CommitRatio()},true));
        }
        static string Validate(Sheet sheet)
        {
            var checks=new List<string>();void Check(bool ok,string text)=>checks.Add((ok?"PASS ":"FAIL ")+text);
            Check(sheet.PaletteVersion==4&&sheet.Prototypes.All(p=>p.PolishVersion==1),"derivative palette prepared");
            Check(sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Tree).All(p=>p.BillboardViews==4),"four-direction tree atlases");
            Check(sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Grass).All(p=>p.GroundPatch&&p.Lods.Length==3),"owned grass mesh/card/ground-cover tiers");
            Check(sheet.Prototypes.All(p=>File.Exists(p.SourcePath)&&p.SourceHash==AssetDatabase.GetAssetDependencyHash(p.SourcePath).ToString()),"supplier dependency hashes unchanged");
            Check(sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).All(p=>p.Mesh!=null&&p.Material!=null&&p.Material.enableInstancing),"instanced render dependencies");
            Check(sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).All(p=>AssetDatabase.GetAssetPath(p.Material).StartsWith(WorldMacroDressingAssets.PolishFolder,StringComparison.Ordinal)),"original materials preserved; derivative owns material edits");
            var shader=Shader.Find("Oheangbu/WorldMacroVegetation");Check(shader!=null&&!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity.ToString()=="Error"),"vegetation shader compile");
            var area=new Sheet.PreserveArea{Centre=Vector3.zero,HalfSize=new Vector2(2,10),TypedClearance=true};
            Check(!Sheet.Excludes(area,new Vector3(2.2f,0,0),Sheet.Kind.Grass)&&Sheet.Excludes(area,new Vector3(2.2f,0,0),Sheet.Kind.Tree),"typed road-edge clearance permits low grass and excludes trunks");
            area.Yaw=45;var rotated=Quaternion.Euler(0,45,0)*new Vector3(2.2f,0,0);Check(!Sheet.Excludes(area,rotated,Sheet.Kind.Grass)&&Sheet.Excludes(area,rotated,Sheet.Kind.Tree),"rotated rectangle excludes by actual orientation, not expanded AABB");
            bool fades=true;foreach(var p in sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Tree))for(float d=0;d<sheet.ForestDistance-350;d+=5){float opacity=0;foreach(var l in p.Lods){var m=l.Parts[0].Material;opacity+=Mathf.Clamp01((d-m.GetFloat("_FadeInStart"))/Mathf.Max(.01f,m.GetFloat("_FadeInEnd")-m.GetFloat("_FadeInStart")))*(1-Mathf.Clamp01((d-m.GetFloat("_FadeOutStart"))/Mathf.Max(.01f,m.GetFloat("_FadeOutEnd")-m.GetFloat("_FadeOutStart"))));}if(opacity<.999f)fades=false;}
            Check(fades,"continuous tree material opacity through the mesh/card/canopy bands");
            var diagnostics=new List<WorldMacroDressingRenderer.CellDiagnostic>();GameObject temporary=null;
            try
            {
                temporary=new GameObject("Vegetation_PopulationDiagnostic"){hideFlags=HideFlags.HideAndDontSave};var renderer=temporary.AddComponent<WorldMacroDressingRenderer>();renderer.enabled=false;renderer.Sheet=sheet;
                foreach(var region in sheet.Geography.Regions)
                {
                    int owner=Array.IndexOf(sheet.Geography.Regions,region);var cell=sheet.Cells.Where(c=>c.Owner==owner).OrderByDescending(c=>c.Habitat.Count(v=>(v&8)!=0)).FirstOrDefault();if(cell==null)continue;
                    diagnostics.Add(renderer.DiagnoseCell(cell.X,cell.Z));
                }
            }
            finally{if(temporary!=null)Object.DestroyImmediate(temporary);}
            Check(diagnostics.Count==5&&diagnostics.All(d=>d.finite&&d.duplicateTreeIds==0&&d.treePlacementMismatches==0&&d.blockedInstances==0&&d.lowGrassBlocked==0&&d.lowGrassDuplicateIds==0),"five-region deterministic population, same tree identities in near/far, route exclusions including low grass footprints");
            var report=new Validation{utc=DateTime.UtcNow.ToString("O"),checks=checks.ToArray(),passed=checks.All(c=>c.StartsWith("PASS ",StringComparison.Ordinal)),prototypes=sheet.Prototypes.Length,viewsPerTree=4,sourceTrees=sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Tree).Select(p=>p.SourcePath).Distinct().Count(),sourceGrass=sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Grass).Select(p=>p.SourcePath).Distinct().Count(),sourceShrubs=sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Shrub).Select(p=>p.SourcePath).Distinct().Count(),passages=sheet.Passages.Length,typedAreas=sheet.PreservedAreas.Count(a=>a.TypedClearance),cells=diagnostics.ToArray(),palette=sheet.Prototypes.Select(p=>new Row{id=p.Id,source=p.SourcePath,sourceHash=p.SourceHash,actualPreparedSize=p.Size,groundPatch=p.GroundPatch,triangles=p.Lods.Select(l=>l.Parts.Sum(part=>part.Mesh!=null?(int)part.Mesh.GetIndexCount(part.Submesh)/3:0)).ToArray(),parts=p.Lods.Select(l=>l.Parts.Length).ToArray()}).ToArray(),unverified=new[]{"Actual 1080p frame/GPU/overdraw cost and memory after this change","User walking/vehicle clearance and collision pool saturation","Visible LOD transitions including fixed trees, water edges and sloped patches","Ground density, route guidance, realm boundaries, final art approval"}};
            string json=JsonUtility.ToJson(report,true);File.WriteAllText(Output+"/validation.json",json);File.WriteAllText(Output+"/TECHNICAL_REPORT.md",ReportText(sheet));return json;
        }
        static string ReportText(Sheet s)=>"# 식생 밀도와 원거리 표현 보완\n\n범위: 보유 에셋을 이용한 식생 파생본. 지형·건물·수면·게임 규칙은 재생성하지 않습니다.\n\n- 나무: 동일 위치·크기·종류 유지, 근거리 메시 → 4방향 카드 → 원경 수관 카드 군락. 마지막 거리는 "+s.ForestDistance+"m.\n- 풀: 보유 풀 4개로 만든 지면 패치, "+s.GrassMeshDistance+"m 근거리 메시 / "+s.GrassDistance+"m 방향별 카드 / "+s.GroundCoverDistance+"m 낮은 지면 피복.\n- 수목/관목/풀 격자: "+s.DenseTreeSpacing+" / "+s.DenseShrubSpacing+" / "+s.DenseGrassSpacing+"m. 각 후보는 군락·지역·고도·경사·실제 물과 경로 조건으로 다시 선택합니다.\n- 256m 셀 인스턴싱. 멀리 보이는 나무는 같은 배치의 카드를 셀 군락으로 제출합니다. 카메라 회전만으로 매번 전체 제출 배열을 다시 만들지 않습니다.\n- 실제 길 폭과 풀·관목·줄기 여유를 구분합니다. 전투 공간에는 낮은 풀을 남기며 큰 식생을 제외합니다.\n- 풀·관목에는 충돌체를 추가하지 않습니다. 큰 줄기/바위의 기존 주변 풀을 유지합니다.\n\n## 검사\n\n실행된 정적 검사는 validation.json에 PASS/FAIL로 기록됩니다. Unity 실행 전 또는 파일이 없으면 미검증입니다. 성능 120fps 달성, 실제 보행, 카메라 통과, 언덕 접지, 미술 합격은 이 코드와 정적 검사만으로 판정하지 않습니다. 화면 캡처와 성능은 주 작업에서 순차 진행합니다. 영상·중간 빌드는 만들지 않습니다.\n\n## 보존·재개\n\nBaseline에는 수정 전 코드와 두 배치 Sheet가 보존됩니다. 생성 에셋은 별도 VegetationPolish 폴더이며 공급자 파일을 변경하지 않습니다. prepare/region 명령은 완료 에셋을 재사용합니다. 시스템 커밋 85% 이상이면 다음 제작 작업을 중단합니다.\n";
    }
    public static partial class WorldMacroDressingAuthoring
    {
        public static IEnumerable<string> RebakePolishMasks(Sheet settings,string region)
        {
            int owner=Array.FindIndex(settings.Geography.Regions,r=>r.Id==region);if(owner<0)throw new ArgumentException("Unknown region "+region);
            string before=Fingerprint();var snapshot=SnapshotScene(settings);ReserveFixed(settings,snapshot);
            int processed=0;foreach(var cell in settings.Cells.Where(c=>c.Owner==owner))
            {
                if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Mask bake paused at memory guard.");
                var origin=settings.Geography.BoundsMin+new Vector2(cell.X*256,cell.Z*256);
                for(int z=0;z<64;z++)for(int x=0;x<64;x++)cell.Habitat[z*64+x]=snapshot.Habitat(origin.x+x*4+2,origin.y+z*4+2);
                processed++;if(processed%12==0){EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();}yield return region+" habitat "+processed+" cells";
            }
            if(Fingerprint()!=before)throw new InvalidOperationException("Source geography changed during mask preparation; do not install mixed revision.");settings.SourceFingerprint=before;
        }
    }
}
