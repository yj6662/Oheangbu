using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Incremental, asset-first visual dressing for the existing playtest corridor.</summary>
    public static partial class WorldMacroVisualCorridorAuthoring
    {
        public const string Folder = WorldMacroPlaytestAuthoring.Folder+"/VisualCorridor";
        public const string RootName = "Playtest_VisualCorridor";
        public static string Output => WorldMacroPlaytestAuthoring.Output+"/VisualCorridor";
        const string SheetPath = Folder+"/VisualCorridor.asset";

        static WorldMacroVisualCorridorSO Sheet
        {
            get
            {
                var asset=AssetDatabase.LoadAssetAtPath<WorldMacroVisualCorridorSO>(SheetPath);
                if(asset!=null)return asset;
                EnsureFolders();asset=ScriptableObject.CreateInstance<WorldMacroVisualCorridorSO>();
                AssetDatabase.CreateAsset(asset,SheetPath);return asset;
            }
        }

        public static string Execute(string arg)
        {
            RequireScene();
            if(string.IsNullOrEmpty(arg))throw new ArgumentException("Expected survey, roads, capital, validate, capture:<id>, or finish.");
            if(arg=="survey")return Survey();
            if(arg=="roads")return BuildRoads();
            if(arg=="capital-plan"){PlanCapital();AssetDatabase.SaveAssets();return "Capital capture views prepared.";}
            if(arg=="capital"){BuildCapital();return "Capital visual pass installed.";}
            if(arg=="capital-refine")return RefineCapital();
            if(arg=="capital-portal")return WidenCapitalPortal();
            if(arg=="capital-portal-correct")return CorrectCapitalPortal();
            if(arg=="capital-palace-audit")return AuditPalaceAccess();
            if(arg=="capital-palace-fix")return FixPalaceAccess();
            if(arg=="validate")return ValidateGeometry();
            if(arg=="clearance")return InstallClearance();
            if(arg=="materials"){string result=OptimizeMaterials();File.WriteAllText(Output+"/material_optimization.json",result);return result;}
            if(arg=="house-lod")return WorldMacroVisualCorridorLod.Execute("optimize");
            if(arg=="house-lod-validate")return WorldMacroVisualCorridorLod.Execute("validate");
            if(arg=="finish"){var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(session!=null){session.TestSaveSuffix="";EditorUtility.SetDirty(session);}EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Visual corridor saved; isolated diagnostic save suffix cleared.";}
            if(arg.StartsWith("capture:",StringComparison.Ordinal))return Capture(arg.Substring("capture:".Length));
            throw new ArgumentException("Unknown visual-corridor command "+arg);
        }

        static void RequireScene()
        {
            if(EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Open W_WorldMacro_Playtest in Edit mode. This authoring pass never rebuilds geography.");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Visual corridor authoring paused: system commit >=85%.");
        }

        static void EnsureFolders()
        {
            Directory.CreateDirectory(Folder);Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Materials");Directory.CreateDirectory(Output);
        }

        public static Transform Group(string name)
        {
            var root=GameObject.Find(RootName);
            if(root==null)root=new GameObject(RootName);
            var child=root.transform.Find(name);
            if(child!=null)return child;
            var created=new GameObject(name).transform;created.SetParent(root.transform,false);return created;
        }

        /// <summary>Projects x/z to the final authored terrain only; it deliberately ignores buildings, paths, and props.</summary>
        public static float TerrainY(Vector3 world)
        {
            var point=WorldMacroPlaytestAuthoring.Ground(new Vector3(world.x,2200,world.z),true);
            return point.y;
        }

        public static Material Surface(Material source)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));EnsureFolders();
            string sourcePath=AssetDatabase.GetAssetPath(source),guid=AssetDatabase.AssetPathToGUID(sourcePath);
            if(string.IsNullOrEmpty(guid))guid=source.name.Replace("/","_").Replace(" ","_");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string materialGuid,out long materialLocalId);
            string path=Folder+"/Materials/"+guid+"_"+materialLocalId+".mat";
            string legacyPath=Folder+"/Materials/"+guid+".mat";
            if(AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Material>().Count()==1 && AssetDatabase.LoadAssetAtPath<Material>(legacyPath)!=null)path=legacyPath;
            var copy=AssetDatabase.LoadAssetAtPath<Material>(path);if(copy!=null)return copy;
            copy=new Material(Shader.Find("Oheangbu/WorldMacroTexturedSurface")){name=source.name+"_VisualCorridor",enableInstancing=true};
            Texture texture=null;foreach(string property in new[]{"_BaseMap","_MainTex","_DiffuseMap","_Albedo"})
                if(source.HasProperty(property)&&source.GetTexture(property)!=null){texture=source.GetTexture(property);break;}
            copy.SetTexture("_BaseMap",texture??source.mainTexture);copy.SetColor("_BaseColor",new Color(.7f,.68f,.62f,1));
            if(source.HasProperty("_BumpMap")&&source.GetTexture("_BumpMap")!=null){copy.SetTexture("_BumpMap",source.GetTexture("_BumpMap"));copy.SetFloat("_BumpScale",.65f);}
            if(copy.HasProperty("_Cull"))copy.SetFloat("_Cull",2);AssetDatabase.CreateAsset(copy,path);return copy;
        }

        /// <summary>
        /// Bakes source render geometry into owned cached meshes. bottom is the world-space footprint centre;
        /// desiredSize is an envelope: positive axes constrain the fit, zero axes are ignored.
        /// Uniform scale uses the smallest positive desired/source ratio and grounds the result at bottom.y.
        /// Source FBXs/prefabs stay untouched. A source LODGroup becomes matching baked LOD levels.
        /// </summary>
        public static Transform PlaceSource(Transform parent,string id,string sourcePath,Vector3 bottom,Vector3 desiredSize,float yaw=0,bool solid=false)
        {
            if(parent==null)throw new ArgumentNullException(nameof(parent));if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("Placement id required.");
            EnsureFolders();var old=parent.Find(id);if(old!=null)return old;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);if(source==null)throw new FileNotFoundException("Reviewed source missing",sourcePath);
            var temporary=Object.Instantiate(source);temporary.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                var lodGroup=temporary.GetComponent<LODGroup>();
                string cacheKey=AssetDatabase.AssetPathToGUID(sourcePath);if(string.IsNullOrEmpty(cacheKey))cacheKey=Safe(source.name);
                var levels=lodGroup==null?new[]{temporary.GetComponentsInChildren<Renderer>(true)}:lodGroup.GetLODs().Select(l=>l.renderers).Where(l=>l.Length>0).ToArray();
                if(levels.Length==0)throw new InvalidOperationException("No render geometry: "+sourcePath);
                Bounds bounds=SourceBounds(levels[0]);var ratios=new List<float>();
                if(desiredSize.x>0)ratios.Add(desiredSize.x/Mathf.Max(.001f,bounds.size.x));
                if(desiredSize.y>0)ratios.Add(desiredSize.y/Mathf.Max(.001f,bounds.size.y));
                if(desiredSize.z>0)ratios.Add(desiredSize.z/Mathf.Max(.001f,bounds.size.z));
                if(ratios.Count==0)throw new ArgumentException("desiredSize needs one positive axis.");
                float scale=ratios.Min();
                var holder=new GameObject(id).transform;holder.SetParent(parent,false);holder.position=bottom;holder.rotation=Quaternion.Euler(0,yaw,0);holder.localScale=Vector3.one;
                var bakedLods=new List<LOD>();int triangleCount=0;
                for(int level=0;level<levels.Length;level++)
                {
                    var renderers=levels[level].Where(r=>r!=null).ToArray();if(renderers.Length==0)continue;
                    var created=BakeLevel(holder,cacheKey,level,renderers,scale,out int tris);triangleCount+=tris;
                    bakedLods.Add(new LOD(levels.Length==1?.006f:Mathf.Lerp(.24f,.012f,level/(float)(levels.Length-1)),created));
                }
                if(bakedLods.Count==0){Object.DestroyImmediate(holder.gameObject);throw new InvalidOperationException("No mesh filters to bake: "+sourcePath);}
                var group=holder.gameObject.AddComponent<LODGroup>();group.SetLODs(bakedLods.ToArray());group.RecalculateBounds();
                // Move the generated geometry only, preserving the caller's world footprint and yaw.
                foreach(Transform child in holder)child.localPosition-=new Vector3(bounds.center.x*scale,bounds.min.y*scale,bounds.center.z*scale);
                if(solid){var collider=holder.gameObject.AddComponent<BoxCollider>();collider.center=new Vector3(0,bounds.size.y*scale*.5f,0);collider.size=bounds.size*scale;}
                holder.gameObject.isStatic=true;RecordSource(id,sourcePath,levels.Length,triangleCount);RecordPlacement(id,id,parent.name,bottom,desiredSize,yaw,solid);
                return holder;
            }
            finally{Object.DestroyImmediate(temporary);}
        }

        static Renderer[] BakeLevel(Transform holder,string cacheKey,int level,Renderer[] renderers,float scale,out int triangles)
        {
            var batches=new Dictionary<Material,List<CombineInstance>>();triangles=0;
            foreach(var renderer in renderers)
            {
                var filter=renderer.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;var mesh=filter.sharedMesh;
                for(int submesh=0;submesh<mesh.subMeshCount;submesh++)
                {
                    if(submesh>=renderer.sharedMaterials.Length||renderer.sharedMaterials[submesh]==null)continue;
                    var material=renderer.sharedMaterials[submesh];if(!batches.TryGetValue(material,out var list)){list=new List<CombineInstance>();batches.Add(material,list);}
                    list.Add(new CombineInstance{mesh=mesh,subMeshIndex=submesh,transform=renderer.localToWorldMatrix});triangles+=(int)(mesh.GetIndexCount(submesh)/3);
                }
            }
            var created=new List<Renderer>();int materialIndex=0;
            foreach(var batch in batches)
            {
                string meshPath=Folder+"/Meshes/"+cacheKey+"_L"+level+"_M"+materialIndex+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if(mesh==null){mesh=new Mesh{name=cacheKey+"_L"+level,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(batch.Value.ToArray(),true,true);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,meshPath);}
                var go=new GameObject("Surface_"+level+"_"+materialIndex++);go.transform.SetParent(holder,false);go.transform.localScale=Vector3.one*scale;
                go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=Surface(batch.Key);go.isStatic=true;created.Add(renderer);
            }
            return created.ToArray();
        }

        /// <summary>Places an already-owned dressing prototype without loading its supplier prefab or FBX.</summary>
        public static Transform PlaceDressingPrototype(Transform parent,string id,string prototypeId,Vector3 bottom,Vector3 desiredSize,float yaw=0)
        {
            var existing=parent.Find(id);if(existing!=null)return existing;
            var dressing=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(WorldMacroDressingAuthoring.SheetPath);
            var prototype=dressing?.Prototypes.FirstOrDefault(p=>p.Id==prototypeId);
            if(prototype==null)throw new InvalidOperationException("Owned dressing prototype missing: "+prototypeId);
            var ratios=new List<float>();
            if(desiredSize.x>0)ratios.Add(desiredSize.x/Mathf.Max(.001f,prototype.Size.x));
            if(desiredSize.y>0)ratios.Add(desiredSize.y/Mathf.Max(.001f,prototype.Size.y));
            if(desiredSize.z>0)ratios.Add(desiredSize.z/Mathf.Max(.001f,prototype.Size.z));
            if(ratios.Count==0)throw new ArgumentException("desiredSize needs one positive axis.");float scale=ratios.Min();
            var holder=new GameObject(id).transform;holder.SetParent(parent,false);holder.position=bottom;holder.rotation=Quaternion.Euler(0,yaw,0);holder.localScale=Vector3.one;
            var lods=new List<LOD>();int triangles=0;
            for(int lod=0;lod<prototype.Lods.Length;lod++)
            {
                var output=new List<Renderer>();int partIndex=0;
                foreach(var part in prototype.Lods[lod].Parts)
                {
                    if(part.Mesh==null||part.Material==null)continue;
                    string meshPath=Folder+"/Meshes/Proto_"+Safe(prototypeId)+"_L"+lod+"_P"+partIndex+".asset";
                    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if(mesh==null){mesh=new Mesh{name=prototypeId+"_L"+lod,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(new[]{new CombineInstance{mesh=part.Mesh,subMeshIndex=part.Submesh,transform=part.Local}},true,true);AssetDatabase.CreateAsset(mesh,meshPath);}
                    var node=new GameObject("LOD"+lod+"_Part"+partIndex++).transform;node.SetParent(holder,false);node.localScale=Vector3.one*scale;
                    node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=node.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=part.Material;node.gameObject.isStatic=true;output.Add(renderer);triangles+=(int)mesh.GetIndexCount(0)/3;
                }
                if(output.Count>0)lods.Add(new LOD(prototype.Lods.Length==1?.006f:Mathf.Lerp(.22f,.012f,lod/(float)(prototype.Lods.Length-1)),output.ToArray()));
            }
            if(lods.Count==0){Object.DestroyImmediate(holder.gameObject);throw new InvalidOperationException("Prototype has no geometry: "+prototypeId);}
            var group=holder.gameObject.AddComponent<LODGroup>();group.SetLODs(lods.ToArray());group.RecalculateBounds();holder.gameObject.isStatic=true;
            RecordSource(id,prototype.SourcePath,lods.Count,triangles);RecordPlacement(id,id,parent.name,bottom,desiredSize,yaw,false);return holder;
        }

        public static Bounds SourceBounds(Renderer[] renderers)
        {
            bool has=false;Bounds result=default;
            foreach(var renderer in renderers){var filter=renderer.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;var bounds=filter.sharedMesh.bounds;
                for(int i=0;i<8;i++){var local=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));var point=renderer.transform.TransformPoint(local);if(!has){result=new Bounds(point,Vector3.zero);has=true;}else result.Encapsulate(point);}}
            if(!has)throw new InvalidOperationException("Source has no MeshFilter bounds.");return result;
        }
        static string Safe(string id)=>string.Concat(id.Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'?c:'_'));

        static void RecordSource(string id,string sourcePath,int lods,int triangles)
        {
            var sheet=Sheet;var rows=sheet.Sources.ToList();var row=rows.FirstOrDefault(r=>r.Id==id);if(row==null){row=new WorldMacroVisualCorridorSO.SourceRecord{Id=id};rows.Add(row);}
            string actualHash=AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
            if(rows.Any(r=>r.SourcePath==sourcePath&&!string.IsNullOrEmpty(r.SourceHash)&&r.SourceHash!=actualHash))throw new InvalidOperationException("Source changed; rebuild the owned geometry cache before recording "+sourcePath);
            row.SourcePath=sourcePath;row.SourceHash=AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();row.LodCount=lods;row.TriangleCount=triangles;sheet.Sources=rows.ToArray();EditorUtility.SetDirty(sheet);
        }
        static void RecordPlacement(string id,string source,string group,Vector3 position,Vector3 size,float yaw,bool solid)
        {
            var sheet=Sheet;var rows=sheet.Placements.ToList();var row=rows.FirstOrDefault(r=>r.Id==id);if(row==null){row=new WorldMacroVisualCorridorSO.Placement{Id=id};rows.Add(row);}
            row.SourceId=source;row.Group=group;row.Position=position;row.Size=size;row.Yaw=yaw;row.Solid=solid;sheet.Placements=rows.ToArray();EditorUtility.SetDirty(sheet);
        }

        static string Survey()
        {
            EnsureFolders();Physics.SyncTransforms();var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(Sheet.Views==null||Sheet.Views.Length==0)InitializeViews();
            var rows=new List<string>{"utc="+DateTime.UtcNow.ToString("o"),"scene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,"mainPath="+(session?.Content?.MainPath?.Length??0)};
            foreach(var route in WorldMacroBuilder.Sheet.Routes.Where(r=>r.Id=="Trail_Mine_Inn"||r.Id=="Trail_Bridge_Inn"||r.Id=="Trail_Inn_Logging"||r.Id=="Return_Inn_EastFoothillPass"||r.Id=="Return_EastFoothillPass_Capital"||r.Id=="Road_Inn_Post"||r.Id=="Road_Merchant_Pass"||r.Id=="Road_Pass_SouthPost"||r.Id=="Road_SouthPost_Gate"||r.Id=="Road_Gate_CapitalReservation"))
                rows.Add(route.Id+"="+route.Points.Length+" "+route.Points.First()+" -> "+route.Points.Last());
            File.WriteAllLines(Output+"/survey.txt",rows);return string.Join("; ",rows.Take(3));
        }

        static string Validate()
        {
            var sheet=Sheet;var root=GameObject.Find(RootName);if(root==null)throw new InvalidOperationException("Visual corridor root not installed.");
            var missing=sheet.Placements.Where(p=>root.transform.Find(p.Group)==null).Select(p=>p.Id).ToArray();
            var report="{\n  \"placements\": "+sheet.Placements.Length+",\n  \"sources\": "+sheet.Sources.Length+",\n  \"root\": \""+RootName+"\",\n  \"missingGroups\": ["+string.Join(",",missing.Select(x=>"\""+x+"\""))+"]\n}";
            EnsureFolders();File.WriteAllText(Output+"/validation.json",report);return missing.Length==0?"Visual corridor valid: "+sheet.Placements.Length+" placements.":"Visual corridor has missing groups: "+string.Join(",",missing);
        }

        static string Capture(string id)
        {
            var view=Sheet.Views.FirstOrDefault(v=>v.Id==id);if(view==null)throw new ArgumentException("Unknown fixed visual-corridor view "+id);
            string captured=WorldMacroDressingProbe.Capture("VisualCorridor_"+id,true,view.Eye.x,view.Eye.y,view.Eye.z,view.Target.x,view.Target.y,view.Target.z);
            EnsureFolders();string destination=Output+"/"+id+".png";File.Copy(captured,destination,true);
            string metadata=Path.ChangeExtension(captured,".json");if(File.Exists(metadata))File.Copy(metadata,Output+"/"+id+".json",true);return destination;
        }

        static void SetViews(params WorldMacroVisualCorridorSO.View[] views)
        {var sheet=Sheet;sheet.Views=views;EditorUtility.SetDirty(sheet);}

        static partial void BuildCapital();
        static partial void PlanCapital();
    }
}
