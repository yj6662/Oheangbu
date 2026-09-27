using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Oheangbu.App.World.Vehicle;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Incremental environment only. Reads existing terrain/water/buildings; never rebuilds them.</summary>
    public static partial class WorldMacroDressingAuthoring
    {
        public const string Folder=WorldMacroBuilder.Folder+"/Dressing";
        public const string SheetPath=Folder+"/Dressing.asset";
        public const string RootName="WorldMacro_Dressing";
        static string Output=>WorldMacroBuilder.Output+"/Dressing";
        static readonly string[] Order={"Cheongrim","Jeokro","Cheolong","Hyeongang","Hwanggyeong"};
        [Serializable] public sealed class Report
        {
            public string utc,scope="Deterministic authoring and source-preservation checks. Not a camera, visual, collision traversal or FPS pass.",region;
            public int cells,prototypes,storyClusters,fixedPlacements,textureCopies,meshCopies,validHabitatSamples,treeSamples,shrubSamples,grassSamples,rockSamples,maskChecks,maskMismatches;
            public string[] completedRegions,checks;public float commit;public string fingerprint;
            public string[] unverified={"User visual density and silhouette review","Runtime frame time and GPU overdraw","New vegetation collision walking and vehicle traversal","Camera passage and billboard transition"};
        }
        struct Segment{public Vector3 A,B;public float Width;}
        struct Triangle
        {
            public Vector3 A,B,C;public Rect Rect;public float D;
            public Triangle(Vector3 a,Vector3 b,Vector3 c){A=a;B=b;C=c;Rect=UnityEngine.Rect.MinMaxRect(Mathf.Min(a.x,b.x,c.x),Mathf.Min(a.z,b.z,c.z),Mathf.Max(a.x,b.x,c.x),Mathf.Max(a.z,b.z,c.z));D=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);}
            public bool Height(float x,float z,out float y)
            {y=0;if(Mathf.Abs(D)<.00001f||!Rect.Contains(new Vector2(x,z)))return false;float a=((B.z-C.z)*(x-C.x)+(C.x-B.x)*(z-C.z))/D,b=((C.z-A.z)*(x-C.x)+(A.x-C.x)*(z-C.z))/D,c=1-a-b;if(a<-.0001f||b<-.0001f||c<-.0001f)return false;y=a*A.y+b*B.y+c*C.y;return true;}
            public bool Overlaps(Rect tile)
            {
                if(!Rect.Overlaps(tile))return false;
                var a=new Vector2(A.x,A.z);var b=new Vector2(B.x,B.z);var c=new Vector2(C.x,C.z);
                return Axis(b-a,a,b,c,tile)&&Axis(c-b,a,b,c,tile)&&Axis(a-c,a,b,c,tile);
            }
            static bool Axis(Vector2 edge,Vector2 a,Vector2 b,Vector2 c,Rect tile)
            {var n=new Vector2(-edge.y,edge.x);float x=Vector2.Dot(n,a),y=Vector2.Dot(n,b),z=Vector2.Dot(n,c),centre=Vector2.Dot(n,tile.center),radius=Mathf.Abs(n.x)*tile.width*.5f+Mathf.Abs(n.y)*tile.height*.5f;return Mathf.Min(x,y,z)<=centre+radius&&Mathf.Max(x,y,z)>=centre-radius;}
        }
        sealed class Snapshot
        {
            public delegate bool HeightSampler(float x, float z, out float y);
            // Optional compact sampler only. Existing authoring paths leave this null.
            public HeightSampler HeightOverride;
            public readonly Dictionary<long,float> Heights=new Dictionary<long,float>();
            public readonly Dictionary<long,List<Triangle>> Waters=new Dictionary<long,List<Triangle>>();
            public readonly Dictionary<long,List<Segment>> Roads=new Dictionary<long,List<Segment>>();
            public readonly Dictionary<long,List<Bounds>> Solids=new Dictionary<long,List<Bounds>>();
            public Sheet.PreserveArea[] MaskAreas;
            public WorldMacroSheetSO Geo;public Sheet Settings;public float RockRadius=3,TreeRadius=.75f;
            public bool Height(float x,float z,out float y)
            {
                if (HeightOverride != null) return HeightOverride(x, z, out y);
                float u=(x-Geo.BoundsMin.x)/16,v=(z-Geo.BoundsMin.y)/16;int ix=Mathf.FloorToInt(u),iz=Mathf.FloorToInt(v);u-=ix;v-=iz;y=0;
                if(!Heights.TryGetValue(Key(ix,iz),out float a)||!Heights.TryGetValue(Key(ix+1,iz),out float b)||!Heights.TryGetValue(Key(ix,iz+1),out float c))return false;
                if(u+v<=1){y=a+(b-a)*u+(c-a)*v;return true;}if(!Heights.TryGetValue(Key(ix+1,iz+1),out float d))return false;y=d+(c-d)*(1-u)+(b-d)*(1-v);return true;
            }
            public float WaterDepth(float x,float z,float ground,out bool nearby)
            {
                nearby=false;float depth=float.NegativeInfinity;
                if(!Waters.TryGetValue(CellKey(x,z,Geo),out var list))return depth;
                foreach(var tri in list)
                {
                    var p=new Vector2(x,z);var closest=new Vector2(Mathf.Clamp(x,tri.Rect.xMin,tri.Rect.xMax),Mathf.Clamp(z,tri.Rect.yMin,tri.Rect.yMax));
                    if((p-closest).sqrMagnitude<400&&Mathf.Abs(ground-(tri.A.y+tri.B.y+tri.C.y)/3)<6)nearby=true;
                    if(tri.Height(x,z,out float y))depth=Mathf.Max(depth,y-ground);
                }
                return depth;
            }
            bool WetTile(float x,float z,float halfSize)
            {
                // The entire jitter cell plus the largest trunk radius must be dry, including its corners.
                var tile=new Rect(x-halfSize,z-halfSize,halfSize*2,halfSize*2);
                if(!Waters.TryGetValue(CellKey(x,z,Geo),out var list))return false;
                float minimum=float.NaN;
                foreach(var tri in list)
                {
                    if(!tri.Overlaps(tile))continue;
                    if(float.IsNaN(minimum))minimum=TileMinimum(tile);
                    if(float.IsNaN(minimum)||Mathf.Max(tri.A.y,tri.B.y,tri.C.y)>minimum-.08f)return true;
                }
                return false;
            }
            float TileMinimum(Rect tile)
            {
                float minimum=float.PositiveInfinity;bool valid=true;
                int x0=Mathf.FloorToInt((tile.xMin-Geo.BoundsMin.x)/16),x1=Mathf.FloorToInt((tile.xMax-Geo.BoundsMin.x)/16),z0=Mathf.FloorToInt((tile.yMin-Geo.BoundsMin.y)/16),z1=Mathf.FloorToInt((tile.yMax-Geo.BoundsMin.y)/16);
                for(int iz=z0;iz<=z1;iz++)for(int ix=x0;ix<=x1;ix++)
                {
                    float gx=Geo.BoundsMin.x+ix*16,gz=Geo.BoundsMin.y+iz*16,left=Mathf.Max(tile.xMin,gx),right=Mathf.Min(tile.xMax,gx+16),bottom=Mathf.Max(tile.yMin,gz),top=Mathf.Min(tile.yMax,gz+16),diagonal=gx+gz+16;
                    Sample(left,bottom);Sample(right,bottom);Sample(left,top);Sample(right,top);
                    float z=diagonal-left;if(z>=bottom&&z<=top)Sample(left,z);z=diagonal-right;if(z>=bottom&&z<=top)Sample(right,z);
                    float x=diagonal-bottom;if(x>=left&&x<=right)Sample(x,bottom);x=diagonal-top;if(x>=left&&x<=right)Sample(x,top);
                }
                return valid?minimum:float.NaN;
                void Sample(float px,float pz){if(!Height(px,pz,out float y))valid=false;else minimum=Mathf.Min(minimum,y);}
            }
            public byte Habitat(float x,float z)
            {
                if(!WorldMacroTerrain.Contains(Geo,x,z)||!Height(x,z,out float ground))return 0;
                // Four-metre mask cells conservatively reserve their diagonal plus a trunk margin.
                const float margin=3.4f;long key=CellKey(x,z,Geo);
                if(Solids.TryGetValue(key,out var solids))foreach(var b in solids)
                    if(x>b.min.x-margin&&x<b.max.x+margin&&z>b.min.z-margin&&z<b.max.z+margin)return 0;
                foreach(var area in MaskAreas??Settings.PreservedAreas)
                {
                    if(!area.ExcludeProcedural||(Settings.DenseVegetation&&area.TypedClearance))continue;var q=Quaternion.Euler(0,-area.Yaw,0)*(new Vector3(x,0,z)-new Vector3(area.Centre.x,0,area.Centre.z));
                    if(Mathf.Abs(q.x)<area.HalfSize.x+margin&&Mathf.Abs(q.z)<area.HalfSize.y+margin)return 0;
                }
                if(WetTile(x,z,2.05f))return 0;
                WaterDepth(x,z,ground,out bool damp);
                float road=float.PositiveInfinity;
                if(Roads.TryGetValue(key,out var routes))foreach(var r in routes)road=Mathf.Min(road,WorldMacroTerrain.SegmentDistance(x,z,r.A,r.B,out _)-r.Width*.5f);
                int mask=Settings.DenseVegetation?120:0;if(!Settings.DenseVegetation&&road>margin+1.4f)mask|=8;if(!Settings.DenseVegetation&&road>margin+.65f)mask|=16;if(!Settings.DenseVegetation&&road>margin-.25f)mask|=32;if(!Settings.DenseVegetation&&road>margin+RockRadius)mask|=64;
                if((mask&64)!=0&&Solids.TryGetValue(key,out var rockSolids))foreach(var b in rockSolids)
                    if(x>b.min.x-3-RockRadius&&x<b.max.x+3+RockRadius&&z>b.min.z-3-RockRadius&&z<b.max.z+3+RockRadius){mask&=~64;break;}
                if((mask&8)!=0&&WetTile(x,z,2+TreeRadius))mask&=~8;
                if((mask&64)!=0&&WetTile(x,z,2+RockRadius))mask&=~64;
                if(mask==0)return 0;int zone=damp?2:1;
                foreach(var site in Geo.Sites)
                    if((site.Kind=="City"||site.Kind=="Settlement"||site.Kind=="Fortress"||site.Kind=="Inn")&&new Vector2(x-site.Position.x,z-site.Position.z).sqrMagnitude<(site.Kind=="City"?380*380:90*90)){zone=3;break;}
                return (byte)(mask|zone);
            }
        }
        static void RequireScene()
        {if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)throw new InvalidOperationException("Open existing macro scene in Edit mode; this command never opens or rebuilds geography.");}
        public static string PrepareAssetsOnly()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Prepare dressing assets in Edit mode; an empty scene is supported.");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Palette preparation paused: system commit >=85%.");
            Directory.CreateDirectory(Folder);
            var settings=AssetDatabase.LoadAssetAtPath<Sheet>(SheetPath);
            if(settings==null)
            {
                var geo=WorldMacroBuilder.Sheet;if(geo==null)throw new FileNotFoundException("Existing macro geography asset is required: "+WorldMacroBuilder.SheetPath);
                settings=ScriptableObject.CreateInstance<Sheet>();settings.Geography=geo;AssetDatabase.CreateAsset(settings,SheetPath);
            }
            if(!WorldMacroDressingAssets.IsComplete(settings))WorldMacroDressingAssets.Prepare(settings);
            EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
            return "Prepared palette only: "+settings.Prototypes.Length+" prototypes, version "+settings.PaletteVersion+". Scene, geography, masks and placements unchanged.";
        }
        public static string InstallRegion(string region)
        {
            RequireScene();if(!Order.Contains(region))throw new ArgumentException("Expected Cheongrim, Jeokro, Cheolong, Hyeongang or Hwanggyeong.");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Environment build paused: system commit >=85%.");
            Directory.CreateDirectory(Folder);Directory.CreateDirectory(Output);
            var geo=WorldMacroBuilder.Sheet;var settings=AssetDatabase.LoadAssetAtPath<Sheet>(SheetPath);
            if(settings==null){settings=ScriptableObject.CreateInstance<Sheet>();settings.Geography=geo;AssetDatabase.CreateAsset(settings,SheetPath);}
            if(settings.Geography!=geo)throw new InvalidOperationException("Dressing belongs to another geography; preserve its data rather than silently replacing it.");
            string before=Fingerprint();
            if(!string.IsNullOrEmpty(settings.SourceFingerprint)&&settings.SourceFingerprint!=before)throw new InvalidOperationException("Existing environment masks were baked against different source geometry/staging. Preserve the old sheet and explicitly rebase all regions; do not silently mix them.");
            if(!WorldMacroDressingAssets.IsComplete(settings))WorldMacroDressingAssets.Prepare(settings);
            var source=SnapshotScene(settings);PrepareStory(settings,source,region);ReserveFixed(settings,source);var cells=settings.Cells.ToDictionary(c=>Key(c.X,c.Z));
            int owner=Array.FindIndex(geo.Regions,r=>r.Id==region);if(owner<0)throw new InvalidOperationException("Unknown geography region.");
            int nx=Mathf.CeilToInt((geo.BoundsMax.x-geo.BoundsMin.x)/256),nz=Mathf.CeilToInt((geo.BoundsMax.y-geo.BoundsMin.y)/256);
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
            {
                Vector2 origin=geo.BoundsMin+new Vector2(x*256,z*256);Vector2 centre=origin+Vector2.one*128;
                if(Owner(geo,centre)!=owner)continue;
                var c=new Sheet.Cell{X=x,Z=z,Owner=owner,Centre=new Vector3(centre.x,0,centre.y),Heights=new float[289],Habitat=new byte[4096],RealmWeights=new byte[1445],MinHeight=float.PositiveInfinity,MaxHeight=float.NegativeInfinity};
                for(int iz=0;iz<17;iz++)for(int ix=0;ix<17;ix++)
                {
                    float px=origin.x+ix*16,pz=origin.y+iz*16;source.Height(px,pz,out float y);c.Heights[iz*17+ix]=y;c.MinHeight=Mathf.Min(c.MinHeight,y);c.MaxHeight=Mathf.Max(c.MaxHeight,y);Weights(geo,new Vector2(px,pz),settings.RegionBlend,c.RealmWeights,(iz*17+ix)*5);
                }
                int valid=0;for(int iz=0;iz<64;iz++)for(int ix=0;ix<64;ix++){byte value=source.Habitat(origin.x+ix*4+2,origin.y+iz*4+2);c.Habitat[iz*64+ix]=value;if(value!=0)valid++;}
                if(valid>0){c.Centre.y=(c.MinHeight+c.MaxHeight)*.5f;cells[Key(x,z)]=c;}else cells.Remove(Key(x,z));
            }
            // A story vignette can straddle a region boundary. Refresh only affected existing masks,
            // preserving their geography, fixed placements and deterministic instance seeds.
            foreach(var c in cells.Values)
            {
                if(c.Owner==owner)continue;Vector2 o=geo.BoundsMin+new Vector2(c.X*256,c.Z*256);
                if(!settings.FixedPlacements.Any(p=>p.Position.x>o.x-8&&p.Position.x<o.x+264&&p.Position.z>o.y-8&&p.Position.z<o.y+264))continue;
                for(int iz=0;iz<64;iz++)for(int ix=0;ix<64;ix++)c.Habitat[iz*64+ix]=source.Habitat(o.x+ix*4+2,o.y+iz*4+2);
            }
            if(before!=Fingerprint())throw new InvalidOperationException("Geography/water/landmark fingerprint changed during environment bake; not publishing the cells.");
            settings.Cells=cells.Values.OrderBy(c=>c.Z).ThenBy(c=>c.X).ToArray();settings.CompletedRegions=settings.CompletedRegions.Concat(new[]{region}).Distinct().ToArray();settings.SourceFingerprint=before;EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
            var root=GameObject.Find(RootName);if(root==null)root=new GameObject(RootName);
            var renderer=root.GetComponent<WorldMacroDressingRenderer>();if(renderer==null)renderer=root.AddComponent<WorldMacroDressingRenderer>();
            renderer.Sheet=settings;renderer.Observer=Object.FindFirstObjectByType<WorldMacroReviewController>()?.GetComponent<Camera>();renderer.VehicleSeat=Object.FindFirstObjectByType<WorldMacroPalanquinSeat>();renderer.ResetCache();EditorUtility.SetDirty(renderer);
            if(settings.CompletedRegions.Length==5)
            {
                var old=GameObject.Find("WorldMacro_AuthoredGeography/04_ForestExtent_PlaceholderClusters");if(old!=null)old.SetActive(false);
            }
            WorldMacroWaterAuthoring.SaveScene();string report=Validate();File.WriteAllText(Output+"/last_region.txt",region+"\n"+report);return region+" baked. "+report;
        }
        public static string WarmView(float x,float y,float z)
        {RequireScene();var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();if(renderer==null)throw new InvalidOperationException("Install a region first");renderer.PrepareView(new Vector3(x,y,z),100);return "Prepared visible environment cells; residency="+renderer.ResidentCells+", instances="+renderer.ResidentInstances;}
        static void PrepareStory(Sheet settings,Snapshot source,string region)
        {
            var clusters=settings.StoryClusters.ToList();var placed=settings.FixedPlacements.ToList();
            var landmarks=AssetDatabase.LoadAssetAtPath<WorldMacroLandmarkSheetSO>(WorldMacroBuilder.Folder+"/Landmarks/Landmarks.asset");
            var specs=new[]{("Logging","Logging",0,110f), ("Mine","Mining",0,110f), ("Inn","Settlement",0,95f), ("DeepForest","Heartland",0,210f), ("Tree","OldGrove",0,150f),
                ("Jeokro","Ash",2,170f),("Cheolong","Military",3,180f),("Hyeongang","FloodedBank",4,380f),("OldTemple","Settlement",4,100f),("Hwanggyeong","Settlement",1,200f),("SouthPost","Settlement",1,80f)};
            foreach(var spec in specs)
            {
                var site=settings.Geography.Sites.FirstOrDefault(s=>s.Id==spec.Item1);if(site==null||site.Realm.ToString()!=region)continue;
                string id="Dressing_"+spec.Item1;var cluster=clusters.FirstOrDefault(c=>c.Id==id);
                if(cluster==null)
                {
                    var built=landmarks?.Landmarks.FirstOrDefault(l=>l.SiteId==site.Id&&l.PlacementResolved);
                    cluster=new Sheet.StoryCluster{Id=id,AnchorId=site.Id,Theme=spec.Item2,Realm=site.Realm,Centre=built!=null?built.Position:site.Position,Radius=spec.Item4};
                    if(spec.Item2=="Logging"||spec.Item2=="Mining"){cluster.TreeDensity=.35f;cluster.GrassDensity=.65f;cluster.RockDensity=1.5f;}
                    if(spec.Item2=="Ash"){cluster.TreeDensity=.2f;cluster.ShrubDensity=.25f;cluster.GrassDensity=.3f;cluster.RockDensity=1.6f;}
                    if(spec.Item2=="Military"||spec.Item2=="Settlement"){cluster.TreeDensity=.2f;cluster.ShrubDensity=.3f;cluster.GrassDensity=.65f;}
                    if(spec.Item2=="Heartland"||spec.Item2=="OldGrove"){cluster.TreeDensity=1.3f;cluster.ShrubDensity=1.35f;}
                    if(spec.Item2=="FloodedBank"){cluster.ShrubDensity=1.5f;cluster.RockDensity=.7f;}
                    clusters.Add(cluster);
                }
                if(spec.Item2=="Heartland"||spec.Item2=="OldGrove")continue;
                int desired=spec.Item2=="Logging"?16:12;
                for(int n=0;n<desired;n++)
                {
                    string placementId=id+"_"+n;if(placed.Any(p=>p.Id==placementId))continue;
                    string suffix=spec.Item2=="Settlement"?(n%3==0?"SM_M_WoodenBox":"SM_052_Pot"):
                        spec.Item2=="Military"?"SM_M_WoodenBox":n%4==0?"SM_M_WoodenBox":"SM_M_WoodLog";
                    string prototypeId=site.Realm+"_"+suffix;var prototype=settings.Prototypes.FirstOrDefault(p=>p.Id==prototypeId);if(prototype==null)continue;
                    for(int attempt=0;attempt<360;attempt++)
                    {
                        uint hash=Sheet.Hash(n,attempt,settings.Seed+spec.Item3*917+site.Id.Length*43);float angle=Sheet.Unit(hash)*Mathf.PI*2;
                        float distance=Mathf.Lerp(12,cluster.Radius*.95f,Mathf.Sqrt(Sheet.Unit(hash*1664525u+1013904223u)));
                        float x=cluster.Centre.x+Mathf.Cos(angle)*distance,z=cluster.Centre.z+Mathf.Sin(angle)*distance;
                        byte habitat=source.Habitat(x,z);if((habitat&64)==0||(spec.Item2=="FloodedBank"&&(habitat&7)!=2))continue;
                        if(!source.Height(x,z,out float y)||!source.Height(x+1,z,out float hx)||!source.Height(x,z+1,out float hz))continue;
                        if(Mathf.Atan(Mathf.Sqrt((hx-y)*(hx-y)+(hz-y)*(hz-y)))*Mathf.Rad2Deg>15)continue;
                        var p=new Vector3(x,y-.015f,z);if(placed.Any(v=>Vector3.Distance(v.Position,p)<3.8f))continue;
                        placed.Add(new Sheet.FixedPlacement{Id=placementId,ClusterId=id,PrototypeId=prototypeId,Position=p,Euler=new Vector3(0,Sheet.Unit(hash*2246822519u)*360,0),Scale=Mathf.Lerp(.85f,1.1f,Sheet.Unit(hash*668265263u))});break;
                    }
                }
            }
            settings.StoryClusters=clusters.ToArray();settings.FixedPlacements=placed.ToArray();
        }
        static void ReserveFixed(Sheet settings,Snapshot snap)
        {
            foreach(var placed in settings.FixedPlacements)
            {
                var prototype=settings.Prototypes.FirstOrDefault(p=>p.Id==placed.PrototypeId);if(prototype==null)continue;
                float width=Mathf.Max(prototype.Size.x,prototype.Size.z)*placed.Scale;
                var bounds=new Bounds(placed.Position,new Vector3(width,prototype.Size.y*placed.Scale,width));
                AddRect(snap.Solids,Rect.MinMaxRect(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z),10,snap.Geo,bounds);
            }
        }
        static Snapshot SnapshotScene(Sheet settings)
        {
            var snap=new Snapshot{Geo=settings.Geography,Settings=settings,MaskAreas=settings.PreservedAreas.Where(a=>!settings.DenseVegetation||!a.TypedClearance).ToArray()};var geo=snap.Geo;
            snap.RockRadius=settings.Prototypes.Where(p=>p.Category==Sheet.Kind.Rock).Select(p=>new Vector2(p.Size.x,p.Size.z).magnitude*p.Scale.y*.5f).DefaultIfEmpty(3).Max();
            snap.TreeRadius=settings.Prototypes.Where(p=>p.Category==Sheet.Kind.Tree).Select(p=>p.Radius*p.Scale.y).DefaultIfEmpty(.75f).Max();
            foreach(var f in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if(f.sharedMesh==null)continue;
                if(f.name.StartsWith("Terrain_",StringComparison.Ordinal))
                {
                    foreach(var local in f.sharedMesh.vertices){var p=f.transform.TransformPoint(local);int x=Mathf.RoundToInt((p.x-geo.BoundsMin.x)/16),z=Mathf.RoundToInt((p.z-geo.BoundsMin.y)/16);snap.Heights[Key(x,z)]=p.y;}
                }
                else if(f.sharedMesh.name.StartsWith("RiverSurface_",StringComparison.Ordinal))
                {
                    var vertices=f.sharedMesh.vertices;var triangles=f.sharedMesh.triangles;var colours=f.sharedMesh.colors;
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        int a=triangles[i],b=triangles[i+1],c=triangles[i+2];if(colours.Length==vertices.Length&&Mathf.Max(colours[a].a,colours[b].a,colours[c].a)<.04f)continue;
                        var tri=new Triangle(f.transform.TransformPoint(vertices[a]),f.transform.TransformPoint(vertices[b]),f.transform.TransformPoint(vertices[c]));if(Mathf.Abs(tri.D)<.00001f)continue;
                        AddRect(snap.Waters,tri.Rect,24,geo,tri);
                    }
                }
            }
            if(snap.Heights.Count==0||snap.Waters.Count==0)throw new InvalidOperationException("Actual terrain and final water mesh must exist before environment placement.");
            foreach(var route in geo.Routes)for(int i=1;i<route.Points.Length;i++)
            {var a=route.Points[i-1];var b=route.Points[i];AddRect(snap.Roads,Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.z,b.z),Mathf.Max(a.x,b.x),Mathf.Max(a.z,b.z)),route.Width*.5f+14,geo,new Segment{A=a,B=b,Width=route.Width});}
            foreach(var collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if(!collider.enabled||collider.isTrigger||collider is CharacterController||collider.name.StartsWith("Terrain_")||collider.name.StartsWith("Dressing_"))continue;
                var bounds=collider.bounds;if(bounds.size.sqrMagnitude<.01f)continue;
                AddRect(snap.Solids,Rect.MinMaxRect(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z),10,geo,bounds);
            }
            var landmarks=AssetDatabase.LoadAssetAtPath<WorldMacroLandmarkSheetSO>(WorldMacroBuilder.Folder+"/Landmarks/Landmarks.asset");
            if(landmarks!=null)foreach(var landmark in landmarks.Landmarks)
            {
                if(!landmark.PlacementResolved)continue;var size=landmark.CourtyardSize+Vector2.one*12;
                if(landmark.Id=="Cave")size+=new Vector2(12,50);
                var rotation=Quaternion.Euler(0,landmark.Yaw,0);var bounds=new Bounds(landmark.Position,Vector3.zero);
                for(int i=0;i<4;i++)bounds.Encapsulate(landmark.Position+rotation*new Vector3((i&1)==0?-size.x*.5f:size.x*.5f,0,(i&2)==0?-size.y*.5f:size.y*.5f));
                AddRect(snap.Solids,Rect.MinMaxRect(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z),10,geo,bounds);
            }
            var vehicle=GameObject.Find(WorldMacroPalanquinAuthoring.RootName);if(vehicle!=null)
            {var bounds=new Bounds(vehicle.transform.position,new Vector3(15,8,18));AddRect(snap.Solids,Rect.MinMaxRect(bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z),5,geo,bounds);}
            return snap;
        }
        static void AddRect<T>(Dictionary<long,List<T>> dictionary,Rect rect,float padding,WorldMacroSheetSO geo,T value)
        {
            int minX=Mathf.FloorToInt((rect.xMin-padding-geo.BoundsMin.x)/256),maxX=Mathf.FloorToInt((rect.xMax+padding-geo.BoundsMin.x)/256);
            int minZ=Mathf.FloorToInt((rect.yMin-padding-geo.BoundsMin.y)/256),maxZ=Mathf.FloorToInt((rect.yMax+padding-geo.BoundsMin.y)/256);
            for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++){long key=Key(x,z);if(!dictionary.TryGetValue(key,out var list)){list=new List<T>();dictionary.Add(key,list);}list.Add(value);}
        }
        static long Key(int x,int z)=>((long)x<<32)^(uint)z;
        static long CellKey(float x,float z,WorldMacroSheetSO geo)=>Key(Mathf.FloorToInt((x-geo.BoundsMin.x)/256),Mathf.FloorToInt((z-geo.BoundsMin.y)/256));
        static float SignedDistance(Vector2[] polygon,Vector2 p)
        {
            bool inside=false;float distance=float.PositiveInfinity;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {var a=polygon[j];var b=polygon[i];if((a.y>p.y)!=(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;var d=b-a;float t=d.sqrMagnitude<.0001f?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);distance=Mathf.Min(distance,Vector2.Distance(p,a+d*t));}
            return inside?distance:-distance;
        }
        static int Owner(WorldMacroSheetSO geo,Vector2 p)
        {int owner=0;float best=float.NegativeInfinity;for(int r=0;r<geo.Regions.Length;r++){float d=SignedDistance(geo.Regions[r].Polygon,p);if(d>best){owner=r;best=d;}}return owner;}
        static void Weights(WorldMacroSheetSO geo,Vector2 p,float blend,byte[] target,int start)
        {
            float maximum=float.NegativeInfinity;for(int r=0;r<geo.Regions.Length;r++)maximum=Mathf.Max(maximum,SignedDistance(geo.Regions[r].Polygon,p));
            float total=0;for(int r=0;r<geo.Regions.Length;r++)total+=Mathf.Exp((SignedDistance(geo.Regions[r].Polygon,p)-maximum)/Mathf.Max(1,blend*.22f));
            for(int r=0;r<geo.Regions.Length;r++){int realm=(int)geo.Regions[r].Realm;target[start+realm]=(byte)Mathf.Clamp(Mathf.RoundToInt(255*Mathf.Exp((SignedDistance(geo.Regions[r].Polygon,p)-maximum)/Mathf.Max(1,blend*.22f))/total),0,255);}
        }
        static string Fingerprint()
        {
            using(var hash=SHA256.Create())
            {
                var text=JsonUtility.ToJson(WorldMacroBuilder.Sheet);
                string path=WorldMacroBuilder.Folder+"/Landmarks/Landmarks.asset";if(File.Exists(path))text+=File.ReadAllText(path);
                foreach(var f in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(f=>f.sharedMesh!=null&&(f.name.StartsWith("Terrain_")||f.sharedMesh.name.StartsWith("RiverSurface_"))).OrderBy(f=>f.name))
                    text+=f.name+":"+AssetDatabase.GetAssetPath(f.sharedMesh)+":"+AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(f.sharedMesh))+":"+f.sharedMesh.vertexCount+":"+f.transform.localToWorldMatrix;
                var car=GameObject.Find(WorldMacroPalanquinAuthoring.RootName);if(car!=null)text+=car.transform.position.ToString("F4");
                return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
            }
        }
        public static string Validate()
        {
            RequireScene();var sheet=AssetDatabase.LoadAssetAtPath<Sheet>(SheetPath);if(sheet==null)throw new FileNotFoundException(SheetPath);
            var checks=new List<string>();void Add(bool value,string text)=>checks.Add((value?"PASS ":"FAIL ")+text);
            Add(sheet.SourceFingerprint==Fingerprint(),"geography, actual water meshes, landmark placement and staging fingerprint");
            Add(sheet.Cells.Select(c=>Key(c.X,c.Z)).Distinct().Count()==sheet.Cells.Length,"unique 256m cells");
            Add(sheet.Cells.All(c=>c.Heights?.Length==289&&c.Habitat?.Length==4096&&c.RealmWeights?.Length==1445&&c.Heights.All(v=>!float.IsNaN(v)&&!float.IsInfinity(v))),"finite terrain lattice and full habitat/region arrays");
            Add(sheet.Prototypes.All(p=>p.Lods.Length>=2&&p.Lods.All(l=>l.Parts.Length>0&&l.Parts.All(part=>part.Mesh!=null&&part.Material!=null&&part.Material.enableInstancing))),"shared meshes and instancing materials");
            Add(sheet.Prototypes.All(p=>File.Exists(p.SourcePath)),"reviewed source prefab paths retained");
            Add(sheet.Prototypes.All(p=>p.SourceHash==AssetDatabase.GetAssetDependencyHash(p.SourcePath).ToString()),"source prefabs, meshes and material/texture dependencies unchanged");
            var meshCopies=sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>p.Mesh).Distinct().ToArray();
            Add(meshCopies.Length>0&&meshCopies.All(m=>m!=null&&AssetDatabase.GetAssetPath(m).StartsWith(Folder+"/Meshes/",StringComparison.Ordinal)),"all selected LOD meshes use standalone copies; no retained source FBX subassets");
            var textureCopies=sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Where(p=>p.Material!=null).SelectMany(p=>new[]{p.Material.GetTexture("_BaseMap"),p.Material.GetTexture("_BumpMap")}).Where(t=>t!=null&&AssetDatabase.GetAssetPath(t).StartsWith(Folder+"/Textures/",StringComparison.Ordinal)).Distinct().ToArray();
            Add(textureCopies.Length>0&&textureCopies.All(t=>t.width<=1024&&t.height<=1024),"native-import texture copies at most 1024px (grass 512px)");
            Add(textureCopies.All(t=>{string path=AssetDatabase.GetAssetPath(t);var importer=AssetImporter.GetAtPath(path) as TextureImporter;bool normal=path.Contains("_Normal_");return importer!=null&&!importer.isReadable&&importer.sRGBTexture!=normal&&(!normal||importer.textureType==TextureImporterType.NormalMap);}),"texture copy colour/normal space and unreadable runtime settings");
            var shader=Shader.Find("Oheangbu/WorldMacroVegetation");Add(shader!=null&&!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity.ToString()=="Error"),"vegetation shader compile");
            var renderer=Object.FindFirstObjectByType<WorldMacroDressingRenderer>();Add(renderer!=null&&renderer.GetComponentsInChildren<MeshRenderer>(true).Length==0,"source prefab renderers are not instantiated in the world");
            Add(sheet.GrassDistance==80&&sheet.ShrubDistance==220&&sheet.TreeDistance==800&&sheet.ColliderPoolSize<=512,"bounded distance and collider settings");
            var report=new Report{utc=DateTime.UtcNow.ToString("o"),region=sheet.CompletedRegions.LastOrDefault(),cells=sheet.Cells.Length,prototypes=sheet.Prototypes.Length,textureCopies=textureCopies.Length,meshCopies=meshCopies.Length,storyClusters=sheet.StoryClusters.Length,fixedPlacements=sheet.FixedPlacements.Length,completedRegions=sheet.CompletedRegions,commit=Prologue.PrologueAudit.CommitRatio(),fingerprint=sheet.SourceFingerprint};
            var snapshot=SnapshotScene(sheet);ReserveFixed(sheet,snapshot);
            foreach(var c in sheet.Cells)for(int sample=0;sample<12;sample++)
            {
                int index=(int)(Sheet.Hash(c.X,c.Z,sample+991)%4096);float x=sheet.Geography.BoundsMin.x+c.X*256+(index%64)*4+2,z=sheet.Geography.BoundsMin.y+c.Z*256+(index/64)*4+2;
                report.maskChecks++;if(snapshot.Habitat(x,z)!=c.Habitat[index])report.maskMismatches++;
            }
            Add(report.maskMismatches==0,"read-only sampled actual water/route/building/manual exclusion rebake");
            Add(sheet.FixedPlacements.Select(p=>p.Id).Distinct().Count()==sheet.FixedPlacements.Length&&sheet.FixedPlacements.All(p=>sheet.Prototypes.Any(v=>v.Id==p.PrototypeId)),"persistent fixed placement IDs and source prototype links");
            Add(sheet.FixedPlacements.All(p=>snapshot.Height(p.Position.x,p.Position.z,out float y)&&Mathf.Abs(y-p.Position.y)<.15f&&snapshot.WaterDepth(p.Position.x,p.Position.z,y,out _)<0),"fixed props on measured dry terrain");
            report.checks=checks.ToArray();
            foreach(var cell in sheet.Cells)foreach(byte mask in cell.Habitat){if(mask!=0)report.validHabitatSamples++;if((mask&8)!=0)report.treeSamples++;if((mask&16)!=0)report.shrubSamples++;if((mask&32)!=0)report.grassSamples++;if((mask&64)!=0)report.rockSamples++;}
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/authoring_audit.json",JsonUtility.ToJson(report,true));return string.Join("\n",checks);
        }
    }
}
