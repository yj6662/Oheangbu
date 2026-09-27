using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMapIllustrationPolish
    {
        const string Folder = "Assets/_Project/Resources/WorldMap/Illustration";
        const string Source = "../../Art/PlaytestPolish/Map/GeneratedTerrain.png";
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestPolish/Map"));

        [MenuItem("Oheangbu/Legacy/복구 전용/Apply Illustrated Map")]
        public static void ApplyMenu() => Debug.Log(Run("apply"));
        public static string Run(string action)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Map asset work requires Edit Mode.");
            Directory.CreateDirectory(Output);
            if (action == "validate") return Validate();
            if (action == "mini") return BakeMiniTiles();
            if (action != "apply") throw new ArgumentException("Expected apply, mini or validate.");
            if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%; no new map textures allocated.");
            var data=AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(WorldMapRuntimeBaker.DataPath);
            var sheet=WorldMacroBuilder.Sheet;
            if(data==null||sheet==null||!data.IsUsable) throw new InvalidOperationException("Existing authored map data is required.");
            string imagePath=Path.GetFullPath(Path.Combine(Application.dataPath,Source));
            if(!File.Exists(imagePath)) throw new FileNotFoundException("Generated map terrain has not been delivered.",imagePath);
            Directory.CreateDirectory(Folder);
            var source=new Texture2D(2,2,TextureFormat.RGBA32,false,false);
            try
            {
            if(!source.LoadImage(File.ReadAllBytes(imagePath))) throw new InvalidOperationException("Cannot decode illustrated map.");
            source.wrapMode=TextureWrapMode.Clamp;
            var atlas = Raster(source,data,new Rect(0,0,1,1),source.width,source.height);
            var next=WriteTexture(atlas,Folder+"/Terrain.png");
            data.IllustratedMap=next;
            var tiles=new List<WorldMapRegionTile>();
            foreach(var region in sheet.Regions)
            {
                if(region?.Polygon==null||region.Polygon.Length<3)continue;
                var min=new Vector2(region.Polygon.Min(p=>p.x)-320,region.Polygon.Min(p=>p.y)-320);
                var max=new Vector2(region.Polygon.Max(p=>p.x)+320,region.Polygon.Max(p=>p.y)+320);
                var projection=new WorldMapProjection(data.BoundsMin,data.BoundsMax);
                Vector2 a=projection.WorldToNormalized(min),b=projection.WorldToNormalized(max);
                var uv=Rect.MinMaxRect(Mathf.Clamp01(a.x),Mathf.Clamp01(a.y),Mathf.Clamp01(b.x),Mathf.Clamp01(b.y));
                int width=1024;
                var size=data.BoundsMax-data.BoundsMin;
                int height=Mathf.Clamp(Mathf.RoundToInt(1024*uv.height*size.y/(uv.width*size.x)),512,1536);
                var image=Raster(source,data,uv,width,height);
                var texture=WriteTexture(image,Folder+"/Region_"+region.Id+".png");
                tiles.Add(new WorldMapRegionTile{Id=region.Id,Texture=texture,WorldUv=uv});
            }
            data.RegionTiles=tiles.ToArray();data.Version=WorldMapBakedDataSO.CurrentVersion;
            EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();
            return Validate();
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Serializable] sealed class MiniReport
        {
            public string status,source="WorldMacroTerrain.Height and actual region polygons; roads/rivers remain authored live vectors, no AI detail extraction";
            public string discovery="Minor terrain relief and contours are gated by existing exploration cells; generated illustration is a quiet backdrop only";
            public float sampleSpacing;public int uniqueHeightSamples;public string[] tiles;
        }
        struct HeightSample { public float height;public Color paper; }
        public static string BakeMiniTiles()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; no minimap bake");
            var data=AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(WorldMapRuntimeBaker.DataPath);var sheet=WorldMacroBuilder.Sheet;
            if(data==null||sheet==null||!data.IsUsable)throw new InvalidOperationException("Authored terrain and existing projection required");
            Directory.CreateDirectory(Folder);Directory.CreateDirectory(Output+"/BeforeMini");
            string dataBackup=Output+"/BeforeMini/WorldMapBakedData.asset";
            if(!File.Exists(dataBackup))File.Copy(WorldMapRuntimeBaker.DataPath,dataBackup);
            foreach(var old in data.RegionTiles??Array.Empty<WorldMapRegionTile>())
            {
                if(old?.Texture==null)continue;string path=AssetDatabase.GetAssetPath(old.Texture);
                string target=Output+"/BeforeMini/"+Path.GetFileName(path);if(!File.Exists(target))File.Copy(path,target);
            }
            var projection=new WorldMapProjection(data.BoundsMin,data.BoundsMax);Vector2 worldSize=data.BoundsMax-data.BoundsMin;
            var regions=new List<(string id,Rect uv)>();float largestSpan=0;
            foreach(var region in sheet.Regions)
            {
                if(region?.Polygon==null||region.Polygon.Length<3)continue;
                Vector2 min=new Vector2(region.Polygon.Min(p=>p.x)-384,region.Polygon.Min(p=>p.y)-384);
                Vector2 max=new Vector2(region.Polygon.Max(p=>p.x)+384,region.Polygon.Max(p=>p.y)+384);
                Vector2 a=projection.WorldToNormalized(min),b=projection.WorldToNormalized(max);
                Rect uv=Rect.MinMaxRect(Mathf.Clamp01(a.x),Mathf.Clamp01(a.y),Mathf.Clamp01(b.x),Mathf.Clamp01(b.y));
                largestSpan=Mathf.Max(largestSpan,uv.width*worldSize.x,uv.height*worldSize.y);regions.Add((region.Id,uv));
            }
            // A shared world-aligned lattice prevents neighboring tile handover from changing heights.
            float spacing=Mathf.Max(16,Mathf.Ceil(largestSpan/378f/8f)*8f);
            var cache=new Dictionary<Vector2Int,HeightSample>();var tiles=new List<WorldMapRegionTile>();var descriptions=new List<string>();
            foreach(var region in regions)
            {
                if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%; stopped before next minimap tile");
                Vector2 size=Vector2.Scale(region.uv.size,worldSize);float longest=Mathf.Max(size.x,size.y);
                int width=Mathf.Clamp(Mathf.RoundToInt(2048*size.x/longest),1024,2048);
                int height=Mathf.Clamp(Mathf.RoundToInt(2048*size.y/longest),1024,2048);
                var raster=RasterTerrain(sheet,data,region.uv,width,height,spacing,cache);
                string path=Folder+"/MiniTerrain_"+region.id+".png";var texture=WriteTexture(raster,path);
                tiles.Add(new WorldMapRegionTile{Id=region.id,Texture=texture,WorldUv=region.uv});
                descriptions.Add(region.id+": "+width+"x"+height+"; "+(size.x/width).ToString("F2")+" x "+(size.y/height).ToString("F2")+" m/texel");
            }
            var previous=data.RegionTiles;
            try{data.RegionTiles=tiles.ToArray();EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();}
            catch{data.RegionTiles=previous;throw;}
            var report=new MiniReport{status="BAKED",sampleSpacing=spacing,uniqueHeightSamples=cache.Count,tiles=descriptions.ToArray()};
            string json=JsonUtility.ToJson(report,true);File.WriteAllText(Output+"/mini_terrain_bake.json",json);return json;
        }

        static Texture2D RasterTerrain(WorldMacroSheetSO sheet,WorldMapBakedDataSO data,Rect uv,int width,int height,float spacing,Dictionary<Vector2Int,HeightSample> cache)
        {
            Vector2 size=data.BoundsMax-data.BoundsMin;Vector2 min=data.BoundsMin+Vector2.Scale(uv.min,size);
            Vector2 extent=Vector2.Scale(uv.size,size);int gx=Mathf.FloorToInt((min.x-data.BoundsMin.x)/spacing)-1,gz=Mathf.FloorToInt((min.y-data.BoundsMin.y)/spacing)-1;
            int nx=Mathf.CeilToInt(extent.x/spacing)+4,nz=Mathf.CeilToInt(extent.y/spacing)+4;
            var grid=new HeightSample[(nx+1)*(nz+1)];
            for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
            {
                var key=new Vector2Int(gx+x,gz+z);
                if(!cache.TryGetValue(key,out var value))
                {
                    float wx=data.BoundsMin.x+key.x*spacing,wz=data.BoundsMin.y+key.y*spacing;
                    value=new HeightSample{height=WorldMacroTerrain.Height(sheet,wx,wz),paper=RegionPaper(sheet,new Vector2(wx,wz))};cache.Add(key,value);
                }
                grid[z*(nx+1)+x]=value;
            }
            var pixels=new Color32[width*height];float pixelMeters=Mathf.Max(extent.x/width,extent.y/height);
            Vector3 light=new Vector3(-.45f,.82f,.35f).normalized;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                float wx=min.x+(x+.5f)/width*extent.x,wz=min.y+(y+.5f)/height*extent.y;
                float fx=(wx-data.BoundsMin.x)/spacing-gx,fz=(wz-data.BoundsMin.y)/spacing-gz;
                int ix=Mathf.Clamp(Mathf.FloorToInt(fx),0,nx-1),iz=Mathf.Clamp(Mathf.FloorToInt(fz),0,nz-1);float tx=fx-ix,tz=fz-iz;
                var a=grid[iz*(nx+1)+ix];var b=grid[iz*(nx+1)+ix+1];var c=grid[(iz+1)*(nx+1)+ix];var d=grid[(iz+1)*(nx+1)+ix+1];
                float h=Mathf.Lerp(Mathf.Lerp(a.height,b.height,tx),Mathf.Lerp(c.height,d.height,tx),tz);
                float dx=Mathf.Lerp(b.height-a.height,d.height-c.height,tz)/spacing,dz=Mathf.Lerp(c.height-a.height,d.height-b.height,tx)/spacing;
                float slope=Mathf.Sqrt(dx*dx+dz*dz);float band=Mathf.Max(.035f,slope*pixelMeters*.72f);
                float minor=Contour(h,10,band)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.3f,4,10/Mathf.Max(.01f,slope*pixelMeters)));
                float major=Contour(h,40,band*1.15f);float contourVisibility=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.018f,.09f,slope));
                Vector3 normal=new Vector3(-dx,1,-dz).normalized;
                float wash=(1-Mathf.Clamp01(Vector3.Dot(normal,light)))*.15f+Mathf.Clamp01((h-90)/900)*.035f;
                float ink=wash+contourVisibility*(minor*.15f+major*.24f);
                Color paper=Color.Lerp(Color.Lerp(a.paper,b.paper,tx),Color.Lerp(c.paper,d.paper,tx),tz);
                Color color=Color.Lerp(paper,new Color(.30f,.275f,.235f),Mathf.Clamp01(ink));
                color.a=WorldMapDiscoveryGrid.Contains(data.Outline,new Vector2(wx,wz))?1:0;pixels[y*width+x]=color;
            }
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,false);texture.SetPixels32(pixels);texture.Apply(false,false);return texture;
        }
        static float Contour(float height,float interval,float band)
        {float d=Mathf.Abs(Mathf.Repeat(height+interval*.5f,interval)-interval*.5f);return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(band*.35f,band*1.35f,d));}
        static Color RegionPaper(WorldMacroSheetSO sheet,Vector2 world)
        {
            Color paper=new Color(.945f,.923f,.875f);
            foreach(var region in sheet.Regions)
            {
                if(region?.Polygon==null||!WorldMapDiscoveryGrid.Contains(region.Polygon,world))continue;
                switch(region.Id)
                {
                    case "Cheongrim":return paper+new Color(-.019f,-.004f,-.015f,0);
                    case "Jeokro":return paper+new Color(.004f,-.014f,-.018f,0);
                    case "Cheolong":return paper+new Color(-.010f,-.007f,-.004f,0);
                    case "Hyeongang":return paper+new Color(-.022f,-.010f,.005f,0);
                    default:return paper;
                }
            }
            return paper;
        }

        // Exact world outline clips the art. Authored paths and water remain separate live vector layers.
        static Texture2D Raster(Texture2D source,WorldMapBakedDataSO data,Rect uv,int width,int height)
        {
            var result=new Texture2D(width,height,TextureFormat.RGBA32,false,false);
            var pixels=new Color32[width*height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                float u=uv.x+(x+.5f)/width*uv.width,v=uv.y+(y+.5f)/height*uv.height;
                var world=new Vector2(Mathf.Lerp(data.BoundsMin.x,data.BoundsMax.x,u),Mathf.Lerp(data.BoundsMin.y,data.BoundsMax.y,v));
                Color c=source.GetPixelBilinear(u,v);
                c.a=WorldMapDiscoveryGrid.Contains(data.Outline,world)?1:0;
                pixels[y*width+x]=c;
            }
            result.SetPixels32(pixels);result.Apply(false,false);return result;
        }

        static Texture2D WriteTexture(Texture2D texture,string path)
        {
            try { File.WriteAllBytes(path,texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;
            importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;
            importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;
            importer.isReadable=false;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static string Validate()
        {
            var data=AssetDatabase.LoadAssetAtPath<WorldMapBakedDataSO>(WorldMapRuntimeBaker.DataPath);
            var sheet=WorldMacroBuilder.Sheet;
            var play=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(WorldMacroBuilder.Folder+"/Playtest/Playtest.asset");
            var issues=WorldMapValidation.Validate(data,sheet,play).ToList();
            if(data==null||!data.HasIllustration)issues.Add("Illustrated geography is missing.");
            if(data?.RegionTiles==null||data.RegionTiles.Length!=5)issues.Add("Five regional sampling tiles are required.");
            if(data!=null)
            {
                foreach(var tile in data.RegionTiles??Array.Empty<WorldMapRegionTile>())
                    if(tile==null||tile.Texture==null||tile.WorldUv.width<=0||tile.WorldUv.height<=0)issues.Add("Malformed regional tile.");
                if(data.Markers.Any(m=>m!=null&&m.InitiallyDiscovered&&m.Id!="Mine"))issues.Add("An unknown place became initially discovered.");
            }
            var report=new Report{utc=DateTime.UtcNow.ToString("O"),status=issues.Count==0?"PASS":"FAIL",issues=issues.ToArray(),
                majorGeography="Illustration and authored river vectors are visible initially. Roads/trails remain per-cell masked; facilities remain discovered-ID gated.",
                evidence="Editor data checks only; actual visual readability, native map input and output performance require runtime capture.",
                tiles=data!=null&&data.RegionTiles!=null&&data.RegionTiles.All(t=>t?.Texture!=null&&t.Texture.name.StartsWith("MiniTerrain_"))?"Five registered terrain-derived contour/relief tiles; actual height and region data, discovery gated in MiniPaper shader.":"Legacy cropped generated illustration tiles; run mini to bake terrain-derived local detail.",
                projection="Unchanged north-up XZ bounds; exact outline alpha from source data. Illustrative mountain symbols are artistic approximations; river/route/marker coordinates remain authoritative."};
            File.WriteAllText(Path.Combine(Output,"map_validation.json"),JsonUtility.ToJson(report,true));
            return JsonUtility.ToJson(report,true);
        }
        [Serializable]sealed class Report{public string utc,status,majorGeography,evidence,tiles,projection;public string[] issues;}
    }
}
