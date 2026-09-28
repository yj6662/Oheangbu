using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World.Dressing;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactNaturalSurface
    {
        static string Folder=>WorldMacroCompactAuthoring.Folder+"/NaturalSurface";
        static string Output=>Path.Combine(WorldMacroCompactAuthoring.Output,"NaturalSurface");
        static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        static T Find<T>() where T:Component=>Object.FindObjectsByType<T>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(x=>x.gameObject.scene==SceneManager.GetActiveScene());
        static void Guard()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)throw new InvalidOperationException("Compact Edit scene required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Commit >=85%; operation stopped");
            Directory.CreateDirectory(Output);
        }
        public static string Execute(string command)
        {
            if(command.StartsWith("performance:"))return PlaytestRecoveryStreamingReview.Execute(command.Substring(12));
            if(command=="performance-prepare")
            {
                var session=Find<Oheangbu.App.World.WorldMacroPlaytestSession>();
                if(!Application.isPlaying||session==null||string.IsNullOrEmpty(session.TestSaveSuffix))throw new InvalidOperationException("Isolated Play required");
                var ui=Object.FindFirstObjectByType<Oheangbu.App.World.UI.PlaytestUiRoot>(FindObjectsInactive.Include);
                if(ui==null)throw new InvalidOperationException("Runtime UI unavailable; pause not bypassed");
                ui.CloseMenu();
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();return "Menu closed through existing UI; wait for normal pause release";
            }
            Guard();
            if(command=="inspect")return Inspect();
            if(command=="apply")return Apply();
            if(command.StartsWith("capture:"))return Capture(command.Substring(8));
            if(command=="audit")return Audit();
            if(command=="static-wind")return StaticWind();
            if(command=="finish-look")return FinishLook();
            if(command=="bake-begin")
            {
                var sheet=Find<WorldMacroDressingRenderer>().Sheet;
                if(AssetDatabase.GetAssetPath(sheet)!=Folder+"/Dressing.asset")throw new InvalidOperationException("Derived sheet required");
                var landmarks=AssetDatabase.FindAssets("t:WorldMacroLandmarkSheetSO",new[]{WorldMacroCompactAuthoring.Folder+"/Data"}).Select(g=>AssetDatabase.LoadAssetAtPath<WorldMacroLandmarkSheetSO>(AssetDatabase.GUIDToAssetPath(g))).Single();
                WorldMacroDressingAuthoring.BeginCompact(sheet,landmarks);return BakeReceipt();
            }
            if(command=="bake-step")
            {
                double start=EditorApplication.timeSinceStartup;
                for(int i=0;i<16;i++){if(WorldMacroDressingAuthoring.StepCompact(4))break;if(EditorApplication.timeSinceStartup-start>.3)break;}
                return BakeReceipt();
            }
            if(command=="bake-finish")
            {
                WorldMacroDressingAuthoring.FinishCompact();var renderer=Find<WorldMacroDressingRenderer>();
                EditorUtility.SetDirty(renderer.Sheet);AssetDatabase.SaveAssetIfDirty(renderer.Sheet);renderer.ResetCache();
                if(renderer.Sheet.Cells.Length==0||File.ReadAllText(AssetDatabase.GetAssetPath(renderer.Sheet)).Contains("  Cells: []"))throw new IOException("Cells not persisted");
                return BakeReceipt();
            }
            throw new ArgumentException(command);
        }
        static string BakeReceipt()
        {
            string json=JsonUtility.ToJson(WorldMacroDressingAuthoring.CompactProgress,true);File.WriteAllText(Path.Combine(Output,"dressing_bake.json"),json);return json;
        }
        static bool IsGround(Renderer r)
        {
            string p=PathOf(r.transform);
            return p.Contains("01_GlobalTerrain_IndependentOfRoads/")||p.Contains("BackgroundContext_RenderOnly/")||p.Contains("01_TerrainConformedRoutes/")||p=="Playtest_Village_Office/Courtyard";
        }
        static Renderer[] Ground()=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Renderer>(true)).Where(IsGround).ToArray();
        [Serializable] sealed class Receipt
        {
            public string scene,sourceSheet,derivedSheet,status,scope;public int groundRenderers,groundMaterials,vegetationMaterials,grassPrototypes,treePrototypes;public float commit;
            public List<string> values=new List<string>();
        }
        static string Inspect()
        {
            var dressing=Find<WorldMacroDressingRenderer>();var s=dressing.Sheet;
            var r=new Receipt{scene=SceneManager.GetActiveScene().path,sourceSheet=AssetDatabase.GetAssetPath(s),status="READ_ONLY",groundRenderers=Ground().Length,commit=Prologue.PrologueAudit.CommitRatio()};
            r.values.Add("Dense="+s.DenseVegetation+" lowInfill="+s.LowGrassInfill+" budgeted="+dressing.BudgetedDenseStreaming+" meshGrass="+s.GrassMeshDistance+" grass="+s.GrassDistance+" tree="+s.TreeDistance+" forest="+s.ForestDistance);
            r.values.Add("spacing="+s.DenseGrassSpacing+" lowSpacing="+s.LowGrassSpacing+" cells="+s.Cells.Length+" prototypes="+s.Prototypes.Length);
            foreach(var mat in Ground().SelectMany(v=>v.sharedMaterials).Where(m=>m!=null).Distinct())r.values.Add(AssetDatabase.GetAssetPath(mat)+" shader="+mat.shader.name+" path="+(mat.HasProperty("_GroundPath")?mat.GetFloat("_GroundPath"):0)+" mask="+(mat.HasProperty("_GroundPathMask")?AssetDatabase.GetAssetPath(mat.GetTexture("_GroundPathMask")):""));
            string json=JsonUtility.ToJson(r,true);File.WriteAllText(Path.Combine(Output,"before_settings.json"),json);return json;
        }
        static Texture Texture(string name,bool normal)
        {
            string source="Assets/SeyeonjeongPavilion/Texture/Ground/"+name+".png",path=Folder+"/Textures/"+name+".png";
            if(AssetDatabase.LoadAssetAtPath<Texture2D>(path)==null)
            {
                if(!AssetDatabase.CopyAsset(source,path))throw new IOException("Texture copy: "+source);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.maxTextureSize=2048;importer.mipmapEnabled=true;importer.isReadable=false;
                importer.wrapMode=TextureWrapMode.Repeat;importer.anisoLevel=4;importer.sRGBTexture=!normal;importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
                importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static string Apply()
        {
            var scene=SceneManager.GetActiveScene();string saved=Path.Combine(Output,"before_disk.unity"),open=Path.Combine(Output,"before_open.unity");
            if(!File.Exists(saved))File.Copy(scene.path,saved);if(!File.Exists(open)&&!EditorSceneManager.SaveScene(scene,open,true))throw new IOException("Open scene backup failed");
            var shader=Shader.Find("Oheangbu/CompactNaturalGround");var wind=Shader.Find("Oheangbu/CompactNaturalVegetation");
            if(shader==null||wind==null||ShaderUtil.ShaderHasError(shader)||ShaderUtil.ShaderHasError(wind))throw new InvalidOperationException("Natural shaders unavailable or failed compilation");
            Directory.CreateDirectory(Folder+"/Textures");Directory.CreateDirectory(Folder+"/Materials");AssetDatabase.Refresh();
            var textureNames=new[]{"T_Grass_1_BC","T_Grass_1_N","T_Dirt_1_BC","T_Dirt_1_N","T_RockGround_1_BC","T_RockGround_1_N"};
            var textures=textureNames.Select((n,i)=>Texture(n,i%2==1)).ToArray();
            var materials=new Dictionary<(Material,bool,bool),Material>();var renderers=Ground();
            var receipt=new Receipt{scene=scene.path,status="APPLIED_REQUIRES_VISUAL_AND_COST_CHECK",scope="Derived materials/sheet only. No mesh/collider, content, route, save or global exposure changes.",groundRenderers=renderers.Length};
            foreach(var renderer in renderers)
            {
                var original=renderer.sharedMaterials;var updated=(Material[])original.Clone();bool dirt=PathOf(renderer.transform).Contains("01_TerrainConformedRoutes/")||renderer.name=="Courtyard";
                bool edge=PathOf(renderer.transform).Contains("01_TerrainConformedRoutes/");
                for(int i=0;i<updated.Length;i++)
                {
                    var source=original[i];if(source==null)continue;
                    if(source.shader==shader)continue;
                    if(!materials.TryGetValue((source,dirt,edge),out var mat))
                    {
                        string path=Folder+"/Materials/Ground_"+materials.Count.ToString("D3")+".mat";
                        if(AssetDatabase.LoadAssetAtPath<Material>(path)!=null)throw new InvalidOperationException("Partial application material exists: "+path);
                        mat=new Material(shader){name=source.name+"_Natural",enableInstancing=true};
                        foreach(string prop in new[]{"_GroundPathRect","_GroundPathMask","_RealmPigment"})
                        {
                            if(!source.HasProperty(prop))continue;
                            if(prop.EndsWith("Rect"))mat.SetVector(prop,source.GetVector(prop));else mat.SetTexture(prop,source.GetTexture(prop));
                        }
                        foreach(string prop in new[]{"_GroundPath","_WashStart","_WashEnd","_WashStrength"})if(source.HasProperty(prop))mat.SetFloat(prop,source.GetFloat(prop));
                        for(int j=0;j<6;j++)mat.SetTexture(new[]{"_GrassMap","_GrassNormal","_DirtMap","_DirtNormal","_RockMap","_RockNormal"}[j],textures[j]);
                        mat.SetFloat("_GroundKind",dirt?1:0);mat.SetFloat("_EdgeFade",edge?1:0);mat.SetFloat("_RealmTintStrength",dirt?.06f:.12f);
                        AssetDatabase.CreateAsset(mat,path);materials[(source,dirt,edge)]=mat;
                    }
                    updated[i]=mat;
                }
                renderer.sharedMaterials=updated;EditorUtility.SetDirty(renderer);
            }
            receipt.groundMaterials=materials.Count;
            var dressing=Find<WorldMacroDressingRenderer>();string sheetPath=Folder+"/Dressing.asset";
            receipt.sourceSheet=AssetDatabase.GetAssetPath(dressing.Sheet);
            var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(sheetPath);
            if(sheet==null)
            {
                sheet=Object.Instantiate(dressing.Sheet);sheet.name="CompactNaturalDressing";AssetDatabase.CreateAsset(sheet,sheetPath);
                // Preserve every placement, density, LOD range and streaming budget.
                // Only material instances change; coverage between blades is now actual ground texture.
                var clones=new Dictionary<string,Material>();
                foreach(var proto in sheet.Prototypes)
                {
                    bool tree=proto.Category==WorldMacroDressingSheetSO.Kind.Tree;
                    bool grass=proto.Category==WorldMacroDressingSheetSO.Kind.Grass;
                    bool shrub=proto.Category==WorldMacroDressingSheetSO.Kind.Shrub;
                    if(!tree&&!grass&&!shrub)continue;if(tree)receipt.treePrototypes++;if(grass)receipt.grassPrototypes++;
                    foreach(var level in proto.Lods)foreach(var part in level.Parts)
                    {
                        var old=part.Material;if(old==null||old.shader.name!="Oheangbu/WorldMacroVegetation")continue;
                        Vector3 anchor=part.Local.inverse.MultiplyPoint3x4(Vector3.zero);
                        string key=AssetDatabase.GetAssetPath(old)+"|"+anchor.ToString("R")+"|"+proto.Size.y;
                        if(!clones.TryGetValue(key,out var mat))
                        {
                            mat=new Material(old){name=old.name+"_NaturalWind",shader=wind,enableInstancing=true};
                            bool leaf=old.GetFloat("_AlphaClip")>.5f;bool card=old.GetFloat("_Billboard")>.5f;
                            mat.SetVector("_WindAnchor",anchor);mat.SetFloat("_Height",Mathf.Max(.1f,proto.Size.y));
                            mat.SetFloat("_WindAmplitude",tree?(leaf?.14f:.045f):grass?.065f:.10f);
                            mat.SetFloat("_LeafFlutter",card?0:leaf?(grass?.018f:.025f):0);mat.SetFloat("_WindSpeed",.85f);
                            AssetDatabase.CreateAsset(mat,Folder+"/Materials/Wind_"+clones.Count.ToString("D4")+".mat");clones[key]=mat;
                        }
                        part.Material=mat;
                    }
                }
                receipt.vegetationMaterials=clones.Count;EditorUtility.SetDirty(sheet);
            }
            dressing.Sheet=sheet;EditorUtility.SetDirty(dressing);receipt.derivedSheet=sheetPath;receipt.commit=Prologue.PrologueAudit.CommitRatio();
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            string json=JsonUtility.ToJson(receipt,true);File.WriteAllText(Path.Combine(Output,"application.json"),json);return json;
        }
        static string FinishLook()
        {
            var renderer=Find<WorldMacroDressingRenderer>();var sheet=renderer.Sheet;
            if(AssetDatabase.GetAssetPath(sheet)!=Folder+"/Dressing.asset")throw new InvalidOperationException("Derived sheet required");
            var lines=new List<string>{"Only derivative grass infill and already-lit atlas brightness changed. Road, water, slope and building exclusions retained.","Low infill: spacing 1.25 -> .9m; density .9 -> 1.1; scale .48-.75 -> .85-1.10. No extra tree instances."};
            sheet.LowGrassSpacing=.9f;sheet.LowGrassDensity=1.1f;
            foreach(var p in sheet.Prototypes)if(p.LowInfill)p.Scale=new Vector2(.85f,1.10f);
            foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{Folder+"/Materials"}))
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if(m.shader.name=="Oheangbu/CompactNaturalGround")
                {
                    m.SetVector("_Tiling",new Vector4(.14f,.16f,.18f,0));m.SetFloat("_BumpScale",1.05f);
                    EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
                }
                if(m.shader.name=="Oheangbu/CompactNaturalVegetation"&&m.GetFloat("_SimpleLighting")>.5f)
                {
                    // These textures are rendered, lit atlases. Multiplying by the mesh ambient floor again darkens them twice.
                    m.SetFloat("_AmbientFloor",1);EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
                }
            }
            EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssetIfDirty(sheet);renderer.ResetCache();
            string result=string.Join("\n",lines);File.WriteAllText(Path.Combine(Output,"finish_look.txt"),result);return result;
        }
        static string Audit()
        {
            var shaders=new[]{Shader.Find("Oheangbu/CompactNaturalGround"),Shader.Find("Oheangbu/CompactNaturalVegetation")};
            var lines=new List<string>();foreach(var shader in shaders)
            {
                lines.Add(shader.name+" errors="+ShaderUtil.ShaderHasError(shader));
                foreach(var message in ShaderUtil.GetShaderMessages(shader))lines.Add(message.severity+": "+message.message);
            }
            var s=Find<WorldMacroDressingRenderer>();lines.Add("Sheet="+AssetDatabase.GetAssetPath(s.Sheet)+" cells="+s.Sheet.Cells.Length);lines.Add("Budgeted="+s.BudgetedDenseStreaming+" generation="+s.GenerationBudgetMilliseconds+"ms packets="+s.PacketBudgetMilliseconds+"ms");
            File.WriteAllText(Path.Combine(Output,"shader_audit.txt"),string.Join("\n",lines));File.WriteAllText(Path.Combine(Output,"render_cost.json"),s.RenderCostJson());return string.Join("\n",lines);
        }
        static string StaticWind()
        {
            var sheet=Find<WorldMacroDressingRenderer>().Sheet;var shader=Shader.Find("Oheangbu/CompactNaturalVegetation");
            var materials=new Dictionary<Material,Material>();int changed=0;
            foreach(var renderer in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)))
            {
                var mesh=renderer.GetComponent<MeshFilter>();if(mesh==null||mesh.sharedMesh==null)continue;
                var list=renderer.sharedMaterials;bool edit=false;
                for(int i=0;i<list.Length;i++)
                {
                    var old=list[i];if(old==null||old.shader.name!="Oheangbu/WorldMacroVegetation")continue;
                    var prototype=sheet.Prototypes.FirstOrDefault(p=>p.Category==WorldMacroDressingSheetSO.Kind.Tree&&old.name.Contains(p.Id));
                    if(prototype==null)continue;
                    if(!materials.TryGetValue(old,out var mat))
                    {
                        string path=Folder+"/Materials/Static_"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(old))+".mat";
                        mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(mat==null)
                        {
                            mat=new Material(old){shader=shader,name=old.name+"_NaturalWind",enableInstancing=true};
                            var part=prototype.Lods.SelectMany(l=>l.Parts).FirstOrDefault(p=>p.Mesh==mesh.sharedMesh);
                            mat.SetVector("_WindAnchor",part!=null?part.Local.inverse.MultiplyPoint3x4(Vector3.zero):new Vector3(0,mesh.sharedMesh.bounds.min.y,0));
                            bool leaf=old.GetFloat("_AlphaClip")>.5f;mat.SetFloat("_WindAmplitude",leaf?.14f:.045f);
                            mat.SetFloat("_LeafFlutter",leaf&&old.GetFloat("_Billboard")<.5f?.025f:0);mat.SetFloat("_Height",Mathf.Max(.1f,prototype.Size.y));mat.SetFloat("_WindExternalClock",1);AssetDatabase.CreateAsset(mat,path);
                        }
                        materials[old]=mat;
                    }
                    list[i]=mat;edit=true;
                }
                if(edit){renderer.sharedMaterials=list;EditorUtility.SetDirty(renderer);changed++;}
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            string result="Static tree renderers="+changed+"; derived materials="+materials.Count+". Same meshes, LODs and positions; scaled engine clock pauses with gameplay.";
            File.WriteAllText(Path.Combine(Output,"static_wind.txt"),result);return result;
        }
    }
}
