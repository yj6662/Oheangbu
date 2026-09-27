using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.Data.World;
using Oheangbu.App.World.Vehicle;

namespace Oheangbu.App.World.Dressing
{
    /// <summary>Deterministic 256m resident cells; instances are data, never source prefab GameObjects.</summary>
    [ExecuteAlways,DisallowMultipleComponent]
    public sealed partial class WorldMacroDressingRenderer : MonoBehaviour
    {
        public WorldMacroDressingSheetSO Sheet;
        public Camera Observer;
        public WorldMacroPalanquinSeat VehicleSeat;
        public bool PreviewInEditor=true;
        // Opt-in only through an already dense derivative; baseline sheets keep their old submission path.
        public bool OptimizeDenseRendering=true;
        [NonSerialized] public bool DiagnosticRebuildEachFrame;
        public int ResidentCells,ResidentInstances,DrawCalls,DrawnInstances,ActiveColliders,ColliderOverflow;
        public int ObserverDrawCalls,ObserverDrawnInstances;
        public float LastGenerationMilliseconds;
        struct Item { public long Id;public Matrix4x4 Matrix;public Vector3 Position;public int Prototype;public float Scale; }
        sealed class LiveCell
        {
            public WorldMacroDressingSheetSO.Cell Source;public Bounds Bounds;
            public Item[] Near,Grass,Far,Fixed,Cover;public bool NearReady,GrassReady;
        }
        sealed class Batch
        {
            public Mesh Mesh;public int Submesh;public Material Material;public ShadowCastingMode Shadows;
            public int Prototype,Lod,Kind;public long Triangles;public Vector4 Fade;public bool OverrideFade;
            public float CullRadius;
            public readonly List<Matrix4x4> Matrices=new List<Matrix4x4>(1023);
            public readonly List<Matrix4x4[]> Packets=new List<Matrix4x4[]>();
        }
        sealed class DrawCache
        {
            public int Revision=-1,Instances;public Matrix4x4 View,Projection;
            public Vector3 Eye;public Quaternion Rotation;public bool Optimized;
            public int Candidates,FrustumRejected,DistanceRejected;public float BuildMilliseconds;
            public Vector4 TreeDistances;public Vector2 SmallDistances;
            public int VisibleHash,BinX,BinZ;
            public readonly Dictionary<int,Batch> Batches=new Dictionary<int,Batch>();
        }
        sealed class CollisionItem { public long Id=long.MinValue;public GameObject Object;public CapsuleCollider Capsule;public BoxCollider Box; }
        readonly Dictionary<int,LiveCell> live=new Dictionary<int,LiveCell>();
        readonly Dictionary<int,WorldMacroDressingSheetSO.Cell> geographyCells=new Dictionary<int,WorldMacroDressingSheetSO.Cell>();
        readonly Dictionary<int,DrawCache> cameraCaches=new Dictionary<int,DrawCache>();
        readonly List<int> removal=new List<int>();
        readonly List<Item> candidates=new List<Item>(1024);
        readonly List<CollisionItem> pool=new List<CollisionItem>();
        readonly Dictionary<long,CollisionItem> collisionSlots=new Dictionary<long,CollisionItem>();
        readonly HashSet<long> desiredCollisions=new HashSet<long>();
        readonly Plane[] planes=new Plane[6];
        readonly Dictionary<WorldMacroDressingSheetSO.Prototype,long> prototypeTriangles=new Dictionary<WorldMacroDressingSheetSO.Prototype,long>();
        MaterialPropertyBlock block;
        Bounds[] contentExclusions=Array.Empty<Bounds>();
        WorldMacroDressingSheetSO.PreserveArea[] typedExclusions=Array.Empty<WorldMacroDressingSheetSO.PreserveArea>();
        readonly Dictionary<int,List<WorldMacroDressingSheetSO.PreserveArea>> exclusionsByCell=new Dictionary<int,List<WorldMacroDressingSheetSO.PreserveArea>>();
        readonly Dictionary<int,List<WorldMacroDressingSheetSO.Passage>> passagesByCell=new Dictionary<int,List<WorldMacroDressingSheetSO.Passage>>();
        List<int>[,] prototypes;
        float nextCollision;
        bool collisionInitialized;
        Vector3 previousCollisionFocus;
        Vector3 collisionFocus,collisionEnd;
        WorldMacroDressingSheetSO loadedSheet;
        Vector2 surfaceDeltaRange;
        int residentRevision;bool prepareComplete;Vector3 preparedEye;float preparedForestDistance;
        static int TimeId;
        void OnEnable(){TimeId=Shader.PropertyToID("_DressingTime");block=new MaterialPropertyBlock();RenderPipelineManager.beginCameraRendering+=Draw;ResetCache();}
        void OnDisable(){RenderPipelineManager.beginCameraRendering-=Draw;ResetCache();}
        public void ResetCache()
        {
            surfaceDeltaRange=Sheet!=null&&Sheet.SurfaceDeformation!=null?Sheet.SurfaceDeformation.GetDeltaRange():Vector2.zero;
            var exclusions=new List<Bounds>();
            if(Sheet!=null)foreach(var a in Sheet.PreservedAreas)
            {
                if(!a.ExcludeProcedural||!a.Id.StartsWith("content:"))continue;
                var q=Quaternion.Euler(0,a.Yaw,0);var b=new Bounds(a.Centre,Vector3.zero);
                for(int i=0;i<4;i++)b.Encapsulate(a.Centre+q*new Vector3((i&1)==0?-a.HalfSize.x:a.HalfSize.x,0,(i&2)==0?-a.HalfSize.y:a.HalfSize.y));
                b.size=new Vector3(b.size.x+4,10000,b.size.z+4);exclusions.Add(b);
            }
            contentExclusions=exclusions.ToArray();
            typedExclusions=Sheet!=null?Sheet.PreservedAreas:Array.Empty<WorldMacroDressingSheetSO.PreserveArea>();
            IndexPassages();
            ResetStreaming();live.Clear();cameraCaches.Clear();geographyCells.Clear();loadedSheet=Sheet;residentRevision++;prepareComplete=false;
            if(Sheet!=null)foreach(var cell in Sheet.Cells)geographyCells[Key(cell.X,cell.Z)]=cell;
            foreach(var item in pool)if(item.Object!=null){item.Object.SetActive(false);if(Application.isPlaying)Destroy(item.Object);else DestroyImmediate(item.Object);}
            pool.Clear();collisionSlots.Clear();desiredCollisions.Clear();prototypes=new List<int>[5,5];
            for(int r=0;r<5;r++)for(int k=0;k<5;k++)prototypes[r,k]=new List<int>();
            prototypeTriangles.Clear();
            if(Sheet!=null)for(int i=0;i<Sheet.Prototypes.Length;i++)
            {var p=Sheet.Prototypes[i];if(p!=null&&(int)p.Realm>=0&&(int)p.Realm<5){prototypes[(int)p.Realm,(int)p.Category].Add(i);long triangles=0;if(p.Lods.Length>0)foreach(var part in p.Lods[0].Parts)if(part.Mesh!=null&&part.Submesh<part.Mesh.subMeshCount)triangles+=(long)part.Mesh.GetIndexCount(part.Submesh)/3;prototypeTriangles[p]=triangles;}}
            ResidentCells=ResidentInstances=ActiveColliders=ColliderOverflow=DrawCalls=DrawnInstances=ObserverDrawCalls=ObserverDrawnInstances=0;LastGenerationMilliseconds=0;collisionInitialized=false;
        }
        static int Key(int x,int z)=>unchecked(x*73856093^z*19349663);
        Vector2 Origin(WorldMacroDressingSheetSO.Cell c)=>Sheet.Geography.BoundsMin+new Vector2(c.X*256,c.Z*256);
        static float FlatDistance(Vector3 a,Vector3 b){float x=a.x-b.x,z=a.z-b.z;return Mathf.Sqrt(x*x+z*z);}
        static float FlatDistanceSquared(Vector3 a,Vector3 b){float x=a.x-b.x,z=a.z-b.z;return x*x+z*z;}
        public void PrepareView(Vector3 eye,int budget=1,Action guard=null)
        {
            if(Sheet==null||Sheet.Geography==null)return;
            if(loadedSheet!=Sheet||prototypes==null)ResetCache();
            if(UseStreaming){PrepareStreaming(eye,budget,guard);return;}
            guard?.Invoke();
            // Geometry is deterministic and preloaded with large distance margins. A completed
            // stationary view has no jobs; avoid scanning the whole world every rendered frame.
            if(prepareComplete&&FlatDistanceSquared(preparedEye,eye)<1&&preparedForestDistance==Sheet.ForestDistance){LastGenerationMilliseconds=0;return;}
            long started=System.Diagnostics.Stopwatch.GetTimestamp();bool changed=false;
            prepareComplete=false;preparedEye=eye;preparedForestDistance=Sheet.ForestDistance;
            removal.Clear();
            foreach(var pair in live)
            {
                float d=FlatDistance(pair.Value.Source.Centre,eye)-182;
                if(d>Sheet.ForestDistance+300)removal.Add(pair.Key);
                else if(d>GroundResidentDistance+200&&pair.Value.GrassReady){pair.Value.Grass=null;pair.Value.Cover=null;pair.Value.GrassReady=false;changed=true;}
                if(d>Sheet.TreeDistance+400&&pair.Value.NearReady){pair.Value.Near=null;pair.Value.NearReady=false;changed=true;}
            }
            foreach(int id in removal){live.Remove(id);changed=true;}
            for(int job=0;job<Mathf.Max(1,budget);job++)
            {
                guard?.Invoke();
                WorldMacroDressingSheetSO.Cell chosen=null;float best=float.PositiveInfinity;int mode=0;
                foreach(var c in Sheet.Cells)
                {
                    float d=FlatDistance(c.Centre,eye)-182;if(d>Sheet.ForestDistance)continue;
                    bool exists=live.TryGetValue(Key(c.X,c.Z),out var state);
                    int wanted=!exists?1:d<Sheet.TreeDistance+100&&!state.NearReady?2:d<GroundResidentDistance+80&&!state.GrassReady?3:0;
                    if(wanted==0)continue;float priority=d+(wanted==1?20:0);
                    if(priority<best){best=priority;chosen=c;mode=wanted;}
                }
                if(chosen==null){prepareComplete=true;break;}int key=Key(chosen.X,chosen.Z);changed=true;
                if(mode==1)
                {
                    var cell=new LiveCell{Source=chosen,Bounds=SurfaceBounds(new Bounds(new Vector3(chosen.Centre.x,(chosen.MinHeight+chosen.MaxHeight)*.5f+20,chosen.Centre.z),new Vector3(300,chosen.MaxHeight-chosen.MinHeight+100,300)))};
                    cell.Far=Generate(chosen,0,true);cell.Fixed=Fixed(chosen);live.Add(key,cell);
                    if(best<Sheet.TreeDistance+150){cell.Near=GenerateNear(chosen);cell.NearReady=true;}
                    if(best<GroundResidentDistance+100){PrepareGround(cell);}
                }
                else if(mode==2){live[key].Near=GenerateNear(chosen);live[key].NearReady=true;}
                else{PrepareGround(live[key]);}
            }
            if(changed)
            {
                residentRevision++;ResidentCells=live.Count;ResidentInstances=0;
                foreach(var c in live.Values)ResidentInstances+=(c.Near?.Length??0)+(c.Grass?.Length??0)+(c.Far?.Length??0)+(c.Fixed?.Length??0)+(c.Cover?.Length??0);
            }
            LastGenerationMilliseconds=(float)((System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency);
        }
        Item[] GenerateNear(WorldMacroDressingSheetSO.Cell cell)
        {
            var a=Sheet.DenseVegetation?Array.Empty<Item>():Generate(cell,0,false);var b=Generate(cell,1,false);var c=Generate(cell,3,false);
            var result=new Item[a.Length+b.Length+c.Length];Array.Copy(a,0,result,0,a.Length);Array.Copy(b,0,result,a.Length,b.Length);Array.Copy(c,0,result,a.Length+b.Length,c.Length);return result;
        }
        Item[] Fixed(WorldMacroDressingSheetSO.Cell cell,bool applyDeformation=true)
        {
            var list=new List<Item>();var origin=Origin(cell);
            foreach(var placed in Sheet.FixedPlacements)
            {
                var p=placed.Position;if(p.x<origin.x||p.x>=origin.x+256||p.z<origin.y||p.z>=origin.y+256)continue;
                int prototype=Array.FindIndex(Sheet.Prototypes,v=>v.Id==placed.PrototypeId);if(prototype<0)continue;
                list.Add(DeformItem(new Item{Id=FixedId(placed.Id),Position=p,Prototype=prototype,Scale=placed.Scale,Matrix=Matrix4x4.TRS(p,Quaternion.Euler(placed.Euler),Vector3.one*placed.Scale)},applyDeformation));
            }
            return list.ToArray();
        }
        Item[] Generate(WorldMacroDressingSheetSO.Cell cell,int category,bool far,bool cover=false,bool low=false,bool applyRetention=true,bool applyDeformation=true)
        {
            var output=new List<Item>();var origin=Origin(cell);
            foreach(var item in GenerateSteps(cell,category,far,cover,low,origin,origin+Vector2.one*256,applyRetention,applyDeformation))
                if(item.HasValue)output.Add(item.Value);
            return output.ToArray();
        }
        IEnumerable<Item?> GenerateSteps(WorldMacroDressingSheetSO.Cell cell,int category,bool far,bool cover,bool low,Vector2 min,Vector2 max,bool applyRetention=true,bool applyDeformation=true)
        {
            Vector2 origin=Origin(cell);
            bool dense=Sheet.DenseVegetation;
            float retention=applyRetention?Sheet.RetentionForCategory(category):1;
            float spacing=cover?Sheet.GroundCoverSpacing:dense?(category==0?Sheet.DenseTreeSpacing:category==1?Sheet.DenseShrubSpacing:category==2?Sheet.DenseGrassSpacing:Sheet.RockSpacing):far?34:category==0?Sheet.TreeSpacing:category==1?Sheet.ShrubSpacing:category==2?Sheet.GrassSpacing:Sheet.RockSpacing;
            spacing=Mathf.Max(.5f,spacing);
            if(low)spacing=Mathf.Max(.9f,Sheet.LowGrassSpacing);
            int sx=Mathf.FloorToInt(origin.x/spacing)-1,sz=Mathf.FloorToInt(origin.y/spacing)-1;
            int ex=Mathf.CeilToInt((origin.x+256)/spacing)+1,ez=Mathf.CeilToInt((origin.y+256)/spacing)+1;
            // Bound the candidate grid without changing its global coordinates or hash.
            sx=Mathf.Max(sx,Mathf.FloorToInt(min.x/spacing)-1);sz=Mathf.Max(sz,Mathf.FloorToInt(min.y/spacing)-1);
            ex=Mathf.Min(ex,Mathf.CeilToInt(max.x/spacing)+1);ez=Mathf.Min(ez,Mathf.CeilToInt(max.y/spacing)+1);
            for(int iz=sz;iz<ez;iz++)for(int ix=sx;ix<ex;ix++)
            {
                yield return null; // Every candidate, including rejected ones, is budgeted.
                uint hash=WorldMacroDressingSheetSO.Hash(ix,iz,Sheet.Seed+category*171+(cover?9231:far&&!dense?3571:0));
                if(low)hash=WorldMacroDressingSheetSO.Hash(ix,iz,Sheet.Seed+18371);
                float x=(ix+.15f+.7f*WorldMacroDressingSheetSO.Unit(hash))*spacing;
                float z=(iz+.15f+.7f*WorldMacroDressingSheetSO.Unit(hash*1664525u+1013904223u))*spacing;
                if(x<origin.x||z<origin.y||x>=origin.x+256||z>=origin.y+256||x<min.x||z<min.y||x>=max.x||z>=max.y)continue;
                int hx=Mathf.Clamp((int)((x-origin.x)/4),0,63),hz=Mathf.Clamp((int)((z-origin.y)/4),0,63);
                byte encoded=cell.Habitat[hz*64+hx];if((encoded&(8<<category))==0)continue;byte habitat=(byte)(encoded&7);
                float slope=WorldMacroDressingSheetSO.Slope(cell,x,z,origin);
                if(slope>(category==0?38:category==1?42:category==2?44:50))continue;
                float y=WorldMacroDressingSheetSO.Height(cell,x,z,origin);
                uint clusterHash=WorldMacroDressingSheetSO.Hash(Mathf.FloorToInt(x/64),Mathf.FloorToInt(z/64),Sheet.Seed+7021);
                int realm=ChooseRealm(cell,x,z,origin,WorldMacroDressingSheetSO.Unit(clusterHash));
                float cluster=Mathf.PerlinNoise((x+Sheet.Seed%1300)*.0103f,(z-Sheet.Seed%1900)*.0103f);
                float density=Sheet.RealmDensity[Mathf.Clamp(realm,0,4)]*Mathf.SmoothStep(.12f,1.1f,cluster);
                if(dense){density*=RouteClusterGain(cell,new Vector3(x,y,z),category);float gain=category==0?Sheet.DensityGain.x:category==1?Sheet.DensityGain.y:category==2?Sheet.DensityGain.z:Sheet.DensityGain.w;density*=gain;}
                if(habitat==3)density*=category==0?.24f:category==1?.3f:.6f;
                if(category==0)density*=Mathf.Lerp(1,.2f,Mathf.InverseLerp(350,650,y));
                if(category==3)density=(.13f+slope/160)*Mathf.Lerp(.5f,1.2f,cluster);
                foreach(var story in Sheet.StoryClusters)
                {
                    float distance=FlatDistance(story.Centre,new Vector3(x,0,z));if(distance>=story.Radius)continue;
                    float factor=category==0?story.TreeDensity:category==1?story.ShrubDensity:category==2?story.GrassDensity:story.RockDensity;
                    density*=Mathf.Lerp(factor,1,Mathf.SmoothStep(0,1,distance/story.Radius));
                }
                if(low)density*=Sheet.LowGrassDensity*Mathf.Lerp(.65f,1.05f,Mathf.PerlinNoise((x+1703)*.043f,(z-721)*.043f));
                // Clamp the original acceptance probability first: lowering an oversaturated
                // density must still retain only the requested fraction of the same candidates.
                density=Mathf.Clamp01(density)*Sheet.OpenGroundDensity(cell,hx,hz,category);
                // Reuse the existing placement hash and lower its accepted probability only after
                // the open-ground mask. Every result is a stable subset with its original transform.
                // All LODs and collider candidates consume these same Items; the yield budget above stays intact.
                if(retention<=0||WorldMacroDressingSheetSO.Unit(hash*1597334677u+3812015801u)>Mathf.Clamp01(density)*retention)continue;
                int proto=low?ChooseLowGrass(realm,habitat,y,slope):ChoosePrototype(realm,category,habitat==2,hash*3266489917u,y,slope);if(proto<0)continue;
                var p=Sheet.Prototypes[proto];float scale=Mathf.Lerp(p.Scale.x,p.Scale.y,WorldMacroDressingSheetSO.Unit(hash*2246822519u));
                if(far&&!dense)scale*=1.35f;
                if(cover){if(slope>22)continue;scale=Mathf.Min(3.6f,Sheet.GroundCoverSpacing*.65f);}
                var pos=new Vector3(x,y-.04f,z);
                bool contentBlocked=dense?Blocked(cell,pos,(WorldMacroDressingSheetSO.Kind)category,p.Radius*scale,cover?scale*.5f:low?p.Radius*scale:0):false;
                if(!dense)foreach(var area in contentExclusions)if(area.Contains(pos)){contentBlocked=true;break;}
                if(contentBlocked)continue;
                Quaternion rotation=Quaternion.Euler(0,WorldMacroDressingSheetSO.Unit(hash*668265263u)*360,0);
                if(dense&&(category==2||category==1))rotation=Quaternion.FromToRotation(Vector3.up,WorldMacroDressingSheetSO.Normal(cell,x,z,origin))*rotation;
                if(cover)pos.y=y+.025f;
                if(category==0||category==3)
                {
                    if(category==3)rotation=Quaternion.FromToRotation(Vector3.up,WorldMacroDressingSheetSO.Normal(cell,x,z,origin))*rotation;
                    float extraSink=0;bool valid=true;
                    int samples=p.GroundPoints.Length>0?p.GroundPoints.Length:category==3?4:0;
                    for(int corner=0;corner<samples;corner++)
                    {
                        var contact=p.GroundPoints.Length>0?p.GroundPoints[corner]:new Vector3((corner&1)==0?-p.Size.x*.5f:p.Size.x*.5f,0,(corner&2)==0?-p.Size.z*.5f:p.Size.z*.5f);
                        var bottom=pos+rotation*(contact*scale);
                        if(!Ground(bottom.x,bottom.z,out float floor)){valid=false;break;}extraSink=Mathf.Max(extraSink,bottom.y-floor);
                    }
                    if(!valid||extraSink>p.Size.y*scale*(category==3?.65f:.18f))continue;
                    pos.y-=extraSink+(category==3?p.Size.y*scale*.08f:.025f);
                }
                long id=((long)category<<60)|(((long)ix+134217728)&0xfffffff)<<28|(((long)iz+134217728)&0xfffffff);
                if(low)id=(6L<<60)|(id&0x0fffffffffffffff);
                // Height deformation is deliberately last: slope/height/clearance/contact
                // decisions, IDs, prototype, retained counts and original rotations stay fixed.
                yield return DeformItem(new Item{Id=id,Matrix=Matrix4x4.TRS(pos,rotation,Vector3.one*scale),Position=pos,Scale=scale,Prototype=proto},applyDeformation);
            }
        }
        float SurfaceDelta(float x,float z)=>Sheet.SurfaceDeformation!=null?Sheet.SurfaceDeformation.SampleDelta(x,z):0;
        Item DeformItem(Item item,bool applyDeformation)
        {
            if(!applyDeformation||Sheet.SurfaceDeformation==null)return item;
            WorldMacroSurfaceDeformationSO.TranslateHeight(ref item.Position,ref item.Matrix,SurfaceDelta(item.Position.x,item.Position.z));
            return item;
        }
        Bounds SurfaceBounds(Bounds bounds)
        {
            if(surfaceDeltaRange==Vector2.zero)return bounds;
            var minimum=bounds.min;var maximum=bounds.max;
            minimum.y+=surfaceDeltaRange.x;maximum.y+=surfaceDeltaRange.y;bounds.SetMinMax(minimum,maximum);return bounds;
        }
        // Physical/deformed surface query for diagnostics and consumers. Generation keeps
        // using private Ground below, so changing a landform cannot add/remove candidates.
        public bool TrySurfaceGroundHeight(float x,float z,out float height)
        {
            height=0;if(Sheet==null||Sheet.Geography==null)return false;
            if(loadedSheet!=Sheet||prototypes==null)ResetCache();
            if(!Ground(x,z,out height))return false;
            height+=SurfaceDelta(x,z);return true;
        }
#if UNITY_EDITOR
        [Serializable] sealed class RetentionAuditRow
        {
            public int cellIndex,cellX,cellZ;
            public string layer;
            public float target,actual;
            public int before,after,duplicates,changedOrNew,partitionMismatches,treeLodMismatches;
            public bool pass;
        }
        [Serializable] sealed class RetentionAuditReport
        {
            public string status;
            public string scope="Actual generated populations in the selected authored cells, after existing habitat/open-ground/clearance rules. Compare baseline and retained IDs/transforms, then regenerate sixteen streaming partitions. Counts are sampled retained fractions, not exact quotas or a full-world/FPS certification. Fixed placements are exempt. Collider audit is separate and must run after Play warmup.";
            public bool enabled;
            public Vector3 requested;
            public int selectedCells,preservedFixedPlacements,fixedMismatches;
            public RetentionAuditRow[] layers;
        }
        static bool SameRetentionItem(Item a,Item b)=>a.Id==b.Id&&a.Prototype==b.Prototype&&a.Position==b.Position&&a.Scale==b.Scale&&a.Matrix==b.Matrix;

        /// <summary>Read-only generation audit. Pass selected Sheet.Cells indices; default is the observer cell.</summary>
        public string ValidateSpeciesRetention(int[] cellIndices=null)
        {
            if(Application.isPlaying)throw new InvalidOperationException("Run the population audit in Edit mode, separately from performance sampling.");
            if(Sheet==null||Sheet.Geography==null||Sheet.Cells.Length==0)throw new InvalidOperationException("A populated dressing sheet is required.");
            if(loadedSheet!=Sheet||prototypes==null)ResetCache();
            if(cellIndices==null||cellIndices.Length==0)
            {
                int nearest=0;float closest=float.PositiveInfinity;var eye=Observer!=null?Observer.transform.position:Sheet.Cells[0].Centre;
                for(int i=0;i<Sheet.Cells.Length;i++){float d=FlatDistanceSquared(Sheet.Cells[i].Centre,eye);if(d<closest){closest=d;nearest=i;}}
                cellIndices=new[]{nearest};
            }
            var report=new RetentionAuditReport{enabled=Sheet.UseSpeciesRetention,requested=Sheet.SpeciesRetention};
            var rows=new List<RetentionAuditRow>();var visited=new HashSet<int>();bool pass=true;
            foreach(int cellIndex in cellIndices)
            {
                if(cellIndex<0||cellIndex>=Sheet.Cells.Length)throw new ArgumentOutOfRangeException(nameof(cellIndices));
                if(!visited.Add(cellIndex))continue;
                var cell=Sheet.Cells[cellIndex];var origin=Origin(cell);report.selectedCells++;
                for(int layer=0;layer<6;layer++)
                {
                    if(layer==4&&(!Sheet.DenseVegetation||!Sheet.LowGrassInfill)||layer==5&&!Sheet.DenseVegetation)continue;
                    int category=layer==0?0:layer==1?1:layer==2?3:2;bool far=layer==0,low=layer==4,cover=layer==5;
                    var original=Generate(cell,category,far,cover,low,false);
                    var retained=Generate(cell,category,far,cover,low);
                    var row=new RetentionAuditRow{cellIndex=cellIndex,cellX=cell.X,cellZ=cell.Z,layer=layer==0?"tree":layer==1?"shrub":layer==2?"rock":layer==3?"grass":layer==4?"low grass":"ground cover",target=Sheet.RetentionForCategory(category),before=original.Length,after=retained.Length};
                    row.actual=original.Length==0?0:retained.Length/(float)original.Length;
                    var baseline=new Dictionary<long,Item>();foreach(var item in original)if(!baseline.TryAdd(item.Id,item))row.duplicates++;
                    var kept=new Dictionary<long,Item>();foreach(var item in retained)
                    {
                        if(!kept.TryAdd(item.Id,item))row.duplicates++;
                        if(!baseline.TryGetValue(item.Id,out var before)||!SameRetentionItem(item,before))row.changedOrNew++;
                    }
                    var partitioned=new Dictionary<long,Item>();
                    for(int z=0;z<4;z++)for(int x=0;x<4;x++)
                        foreach(var value in GenerateSteps(cell,category,far,cover,low,origin+new Vector2(x*64,z*64),origin+new Vector2((x+1)*64,(z+1)*64)))
                            if(value.HasValue&&!partitioned.TryAdd(value.Value.Id,value.Value))row.duplicates++;
                    foreach(var pair in kept)if(!partitioned.TryGetValue(pair.Key,out var item)||!SameRetentionItem(pair.Value,item))row.partitionMismatches++;
                    foreach(var pair in partitioned)if(!kept.ContainsKey(pair.Key))row.partitionMismatches++;
                    if(layer==0&&Sheet.DenseVegetation)
                    {
                        var near=Generate(cell,0,false);var nearIds=new HashSet<long>();
                        foreach(var item in near){nearIds.Add(item.Id);if(!kept.TryGetValue(item.Id,out var other)||!SameRetentionItem(item,other))row.treeLodMismatches++;}
                        foreach(var pair in kept)if(!nearIds.Contains(pair.Key))row.treeLodMismatches++;
                    }
                    row.pass=row.duplicates==0&&row.changedOrNew==0&&row.partitionMismatches==0&&row.treeLodMismatches==0&&row.after<=row.before&&(row.target!=1||row.after==row.before)&&(row.target!=0||row.after==0);
                    pass&=row.pass;rows.Add(row);
                }
                var fixedItems=new Dictionary<long,Item>();foreach(var item in Fixed(cell))fixedItems[item.Id]=item;
                foreach(var placed in Sheet.FixedPlacements)
                {
                    var p=placed.Position;if(p.x<origin.x||p.x>=origin.x+256||p.z<origin.y||p.z>=origin.y+256)continue;
                    if(placed.Preserve)report.preservedFixedPlacements++;
                    int prototype=Array.FindIndex(Sheet.Prototypes,v=>v.Id==placed.PrototypeId);
                    p.y+=SurfaceDelta(p.x,p.z);
                    if(!fixedItems.TryGetValue(FixedId(placed.Id),out var item)||item.Prototype!=prototype||item.Position!=p||item.Scale!=placed.Scale||item.Matrix!=Matrix4x4.TRS(p,Quaternion.Euler(placed.Euler),Vector3.one*placed.Scale))report.fixedMismatches++;
                }
            }
            report.layers=rows.ToArray();report.status=pass&&report.fixedMismatches==0?"PASS_RETENTION_IDENTITY":"FAIL";
            return JsonUtility.ToJson(report,true);
        }
        [Serializable] sealed class RetentionColliderReport
        {
            public string status,scope="Active collider pool after Play warmup: compare IDs with current resident render Items and regenerated retained candidates in occupied cells. No collider pool or cache is modified. A zero-collider sample is explicitly unverified.";
            public int active,checkedColliders,missingResidentItem,missingRetainedItem,transformMismatches,checkedCells;
        }
        public string ValidateActiveRetentionColliders()
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Run active collider validation after Play warmup.");
            var report=new RetentionColliderReport{active=collisionSlots.Count};
            var resident=new Dictionary<long,Item>();
            void Collect(Item[] items){if(items!=null)foreach(var item in items)resident[item.Id]=item;}
            foreach(var cell in live.Values){Collect(cell.Near);Collect(cell.Fixed);if(Sheet.DenseVegetation)Collect(cell.Far);}
            if(UseStreaming)foreach(var chunk in streamChunks.Values)if(chunk.Complete&&(chunk.Layer==0||chunk.Layer==2||chunk.Layer==6))foreach(var item in chunk.Items)resident[item.Id]=item;
            var checkedCells=new Dictionary<int,Dictionary<long,Item>>();
            foreach(var pair in collisionSlots)
            {
                report.checkedColliders++;
                if(!resident.TryGetValue(pair.Key,out var item)){report.missingResidentItem++;continue;}
                int x=Mathf.FloorToInt((item.Position.x-Sheet.Geography.BoundsMin.x)/256),z=Mathf.FloorToInt((item.Position.z-Sheet.Geography.BoundsMin.y)/256),key=Key(x,z);
                if(!checkedCells.TryGetValue(key,out var expected))
                {
                    expected=new Dictionary<long,Item>();checkedCells.Add(key,expected);
                    if(geographyCells.TryGetValue(key,out var cell))
                    {
                        foreach(var value in Generate(cell,0,Sheet.DenseVegetation))expected[value.Id]=value;
                        foreach(var value in Generate(cell,3,false))expected[value.Id]=value;
                        foreach(var value in Fixed(cell))expected[value.Id]=value;
                    }
                }
                if(!expected.TryGetValue(item.Id,out var retained)||!SameRetentionItem(item,retained))report.missingRetainedItem++;
                var go=pair.Value.Object;
                if(go==null||!go.activeSelf||Vector3.SqrMagnitude(go.transform.position-item.Position)>.000001f||Vector3.SqrMagnitude(go.transform.localScale-Vector3.one*item.Scale)>.000001f||Quaternion.Angle(go.transform.rotation,item.Matrix.rotation)>.01f)report.transformMismatches++;
            }
            report.checkedCells=checkedCells.Count;
            report.status=report.active==0?"UNVERIFIED_NO_ACTIVE_COLLIDERS":report.missingResidentItem+report.missingRetainedItem+report.transformMismatches==0?"PASS_ACTIVE_COLLIDER_IDENTITY":"FAIL";
            return JsonUtility.ToJson(report,true);
        }
#endif
        static long FixedId(string text)
        {unchecked{ulong hash=1469598103934665603;foreach(char c in text){hash^=c;hash*=1099511628211;}return (4L<<60)|(long)(hash&0x0fffffffffffffff);}}
        bool Ground(float x,float z,out float y)
        {
            // Original authored terrain only: contact rejection must be invariant under a
            // final surface deformation. Physical callers use TrySurfaceGroundHeight.
            var origin=Sheet.Geography.BoundsMin;int ix=Mathf.FloorToInt((x-origin.x)/256),iz=Mathf.FloorToInt((z-origin.y)/256);y=0;
            if(!geographyCells.TryGetValue(Key(ix,iz),out var cell))return false;y=WorldMacroDressingSheetSO.Height(cell,x,z,origin+new Vector2(ix*256,iz*256));return true;
        }
        int ChooseRealm(WorldMacroDressingSheetSO.Cell cell,float x,float z,Vector2 origin,float random)
        {
            int ix=Mathf.Clamp(Mathf.RoundToInt((x-origin.x)/16),0,16),iz=Mathf.Clamp(Mathf.RoundToInt((z-origin.y)/16),0,16),start=(iz*17+ix)*5;
            float total=0;for(int r=0;r<5;r++)total+=cell.RealmWeights[start+r];float target=random*total;
            for(int r=0;r<5;r++){target-=cell.RealmWeights[start+r];if(target<=0)return r;}return 4;
        }
        int ChoosePrototype(int realm,int category,bool wet,uint hash,float altitude=0,float slope=0)
        {
            var list=prototypes[realm,category];float total=0;
            foreach(int i in list){var p=Sheet.Prototypes[i];if(p.LowInfill||(p.WetBank&&!wet)||altitude>p.MaximumAltitude||slope>p.MaximumSlope)continue;total+=p.Weight;}
            if(total<=0)return -1;
            float target=WorldMacroDressingSheetSO.Unit(hash)*total;
            foreach(int i in list){var p=Sheet.Prototypes[i];if(p.LowInfill||(p.WetBank&&!wet)||altitude>p.MaximumAltitude||slope>p.MaximumSlope)continue;target-=p.Weight;if(target<=0)return i;}
            return -1;
        }
        void Update()
        {
            LastCollisionMilliseconds=0;
            if(Sheet==null)return;
            ResolveObserver();
            if(Observer==null||!Observer.isActiveAndEnabled)return;
            if(Observer!=null)PrepareView(Observer.transform.position,Sheet.CellsPerFrame);
            if(Application.isPlaying&&Observer!=null&&Time.unscaledTime>=nextCollision)
            {nextCollision=Time.unscaledTime+.12f;long start=System.Diagnostics.Stopwatch.GetTimestamp();UpdateColliders();LastCollisionMilliseconds=ElapsedMs(start);}
        }
        void Draw(ScriptableRenderContext context,Camera camera)
        {
            if(Sheet==null||(!Application.isPlaying&&!PreviewInEditor)||camera.cameraType==CameraType.Preview||camera.cameraType==CameraType.Reflection)return;
            if(Application.isPlaying&&!AllowDiagnosticCameras&&camera!=Observer)return;
            if(!SystemInfo.supportsInstancing)return;
            if(loadedSheet!=Sheet)ResetCache();
            if(!Application.isPlaying)PrepareView(camera.transform.position,4);
            if(UseStreaming){DrawStreaming(camera);return;}
            int cameraId=camera.GetInstanceID();
            if(!cameraCaches.TryGetValue(cameraId,out var cache))
            {
                // Capture cameras are temporary. Bound retained packet memory across editor views.
                if(cameraCaches.Count>=3)cameraCaches.Clear();
                cache=new DrawCache();cameraCaches.Add(cameraId,cache);
            }
            var eyePosition=camera.transform.position;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            int visibleHash=17;foreach(var c in live.Values)if(GeometryUtility.TestPlanesAABB(planes,c.Bounds))visibleHash=unchecked(visibleHash*31+Key(c.Source.X,c.Source.Z));
            int binX=Mathf.FloorToInt(eyePosition.x/8),binZ=Mathf.FloorToInt(eyePosition.z/8);
            var view=camera.worldToCameraMatrix;var projection=camera.projectionMatrix;
            var treeDistances=new Vector4(Sheet.TreeNear,Sheet.TreeMiddle,Sheet.TreeDistance,Sheet.ForestDistance);var smallDistances=new Vector2(Sheet.GrassDistance,Sheet.ShrubDistance);
            DrawCalls=0;
            bool optimized=Sheet.DenseVegetation&&OptimizeDenseRendering;
            bool sameView=optimized?(Vector3.SqrMagnitude(cache.Eye-eyePosition)<.16f&&Quaternion.Angle(cache.Rotation,camera.transform.rotation)<.75f):
                Sheet.DenseVegetation?(cache.VisibleHash==visibleHash&&cache.BinX==binX&&cache.BinZ==binZ):cache.View==view;
            if(!DiagnosticRebuildEachFrame&&cache.Revision==residentRevision&&cache.Optimized==optimized&&sameView&&cache.Projection==projection&&cache.TreeDistances==treeDistances&&cache.SmallDistances==smallDistances)
            {
                SubmitCached();return;
            }
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            long buildStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            foreach(var b in cache.Batches.Values)b.Matrices.Clear();cache.Instances=0;cache.Candidates=cache.FrustumRejected=cache.DistanceRejected=0;
            Vector3 eye=camera.transform.position;
            float pad=182;
            foreach(var proto in Sheet.Prototypes)pad=Mathf.Max(pad,182+Mathf.Max(proto.Size.x,proto.Size.z)*proto.Scale.y*.675f);
            float nearLimit=Mathf.Max(Sheet.TreeDistance,Mathf.Max(Sheet.ShrubDistance,350))+pad,grassLimit=GroundResidentDistance+pad,fixedLimit=350+pad,farLimit=Sheet.ForestDistance+182,farInner=Mathf.Max(0,Sheet.TreeDistance-100-182);
            foreach(var c in live.Values)
            {
                if(!GeometryUtility.TestPlanesAABB(planes,c.Bounds))continue;
                float cellDistanceSquared=FlatDistanceSquared(c.Source.Centre,eye);
                if(c.NearReady&&cellDistanceSquared<=nearLimit*nearLimit)DrawItems(c.Near,false);
                if(c.GrassReady&&cellDistanceSquared<=grassLimit*grassLimit){DrawItems(c.Grass,false);DrawGroundCover(c.Cover);}
                if(cellDistanceSquared<=fixedLimit*fixedLimit)DrawItems(c.Fixed,false);
                if(cellDistanceSquared<=farLimit*farLimit&&(Sheet.DenseVegetation||cellDistanceSquared>=farInner*farInner))DrawItems(c.Far,!Sheet.DenseVegetation);
            }
            foreach(var batch in cache.Batches.Values)
            {
                int packets=(batch.Matrices.Count+1022)/1023;
                while(batch.Packets.Count<packets)batch.Packets.Add(new Matrix4x4[1023]);
                for(int n=0;n<packets;n++){int start=n*1023;batch.Matrices.CopyTo(start,batch.Packets[n],0,Mathf.Min(1023,batch.Matrices.Count-start));}
            }
            cache.VisibleHash=visibleHash;cache.BinX=binX;cache.BinZ=binZ;
            cache.Revision=residentRevision;cache.View=view;cache.Projection=projection;cache.TreeDistances=treeDistances;cache.SmallDistances=smallDistances;
            cache.Eye=eyePosition;cache.Rotation=camera.transform.rotation;cache.Optimized=optimized;
            cache.BuildMilliseconds=(float)((System.Diagnostics.Stopwatch.GetTimestamp()-buildStarted)*1000d/System.Diagnostics.Stopwatch.Frequency);
            SubmitCached();
            void SubmitCached()
            {
                if(block==null)block=new MaterialPropertyBlock();block.Clear();block.SetFloat(TimeId,Application.isPlaying?Time.time:Time.realtimeSinceStartup);
                foreach(var batch in cache.Batches.Values)
                {
                    block.SetFloat("_DressingFadeOverride",batch.OverrideFade?1:0);
                    block.SetVector("_DressingFadeRange",batch.Fade);
                    for(int n=0,left=batch.Matrices.Count;left>0;n++,left-=1023)
                    {
                        Graphics.DrawMeshInstanced(batch.Mesh,batch.Submesh,batch.Material,batch.Packets[n],Mathf.Min(1023,left),block,batch.Shadows,true,gameObject.layer,camera,LightProbeUsage.Off);
                        DrawCalls++;
                    }
                }
                DrawnInstances=cache.Instances;
                if(camera==Observer){ObserverDrawCalls=DrawCalls;ObserverDrawnInstances=DrawnInstances;}
            }
            void DrawItems(Item[] items,bool far)
            {
                if(items==null)return;
                foreach(var item in items)
                {
                    cache.Candidates++;
                    var p=Sheet.Prototypes[item.Prototype];float d2=FlatDistanceSquared(item.Position,eye),extent=Mathf.Max(p.Size.x,p.Size.z)*item.Scale*.5f+(Sheet.DenseVegetation?12:0);int kind=(int)p.Category;
                    float limit=p.LowInfill?Sheet.LowGrassDistance:kind==0?(Sheet.DenseVegetation?Sheet.ForestDistance:Sheet.TreeDistance):kind==1?Sheet.ShrubDistance:kind==2?Sheet.GrassDistance:350;
                    if(far){float start=Sheet.TreeDistance-100;if((start>0&&d2<start*start)||d2>Sheet.ForestDistance*Sheet.ForestDistance)continue;Add(item,p.Lods.Length-1);continue;}
                    if(d2>(limit+extent)*(limit+extent)){cache.DistanceRejected++;continue;}
                    if(optimized&&!InstanceInView(item,p))
                    {
                        cache.FrustumRejected++;
                        // Nearby offscreen trunks still cast into the view; use the existing lower mesh, never a camera-facing shadow card.
                        float shadowDistance=ShadowDistance(kind);
                        if(kind!=2&&d2<shadowDistance*shadowDistance)
                            Add(item,kind==0&&p.Lods.Length>1?1:0,ShadowCastingMode.ShadowsOnly);
                        continue;
                    }
                    if(kind==0)
                    {
                        float nearEnd=Sheet.TreeNear+10+extent,middleStart=Sheet.TreeNear-10-extent,middleEnd=Sheet.TreeMiddle+20+extent,billboardStart=Sheet.TreeMiddle-20-extent;
                        if(d2<nearEnd*nearEnd)Add(item,0);
                        if((middleStart<0||d2>middleStart*middleStart)&&d2<middleEnd*middleEnd)Add(item,1);
                        if((billboardStart<0||d2>billboardStart*billboardStart)&&(!Sheet.DenseVegetation||d2<(Sheet.TreeDistance+extent)*(Sheet.TreeDistance+extent)))Add(item,2);
                        if(Sheet.DenseVegetation&&d2>(Sheet.TreeDistance-100-extent)*(Sheet.TreeDistance-100-extent))Add(item,3);
                    }
                    else if(optimized&&kind==1&&p.Lods.Length>=3)
                    {
                        float nearEnd=ShrubNearEnd(p),farStart=ShrubCardStart(p),radius=Mathf.Max(p.Size.x,p.Size.z)*item.Scale*.5f+1;
                        if(d2<(nearEnd+4+radius)*(nearEnd+4+radius))Add(item,0);
                        if(d2>(nearEnd-4-radius)*(nearEnd-4-radius)&&d2<(farStart+5+radius)*(farStart+5+radius))Add(item,1);
                        if(d2>(farStart-5-radius)*(farStart-5-radius))Add(item,2);
                    }
                    else if(Sheet.DenseVegetation&&kind==2&&p.GroundPatch)
                    {float meshDistance=GrassMeshEnd(p,optimized);float band=p.LowInfill?5:10;if(optimized)band=4;float radius=optimized?Mathf.Max(p.Size.x,p.Size.z)*item.Scale*.5f+1:extent;if(d2<(meshDistance+band+radius)*(meshDistance+band+radius))Add(item,0);if(d2>(meshDistance-band-radius)*(meshDistance-band-radius))Add(item,1);}
                    else{float transition=kind==2?35:75;Add(item,d2<transition*transition?0:1);}
                }
            }
            void DrawGroundCover(Item[] items)
            {
                if(items==null)return;foreach(var item in items){cache.Candidates++;float d2=FlatDistanceSquared(item.Position,eye);if(d2<(Sheet.GrassDistance-35)*(Sheet.GrassDistance-35)||d2>(Sheet.GroundCoverDistance+20)*(Sheet.GroundCoverDistance+20)){cache.DistanceRejected++;continue;}if(optimized&&!InstanceInView(item,Sheet.Prototypes[item.Prototype])){cache.FrustumRejected++;continue;}Add(item,2);}
            }
            bool InstanceInView(Item item,WorldMacroDressingSheetSO.Prototype prototype)
            {
                // Include actual tall crowns, atlas square bounds, wind and the bounded cached-camera motion.
                float span=Mathf.Max(prototype.Size.y,Mathf.Max(prototype.Size.x,prototype.Size.z));
                Vector3 centre=item.Position+Vector3.up*(prototype.Size.y*item.Scale*.5f);
                float radius=Mathf.Max(prototype.Size.magnitude*.55f,span*.79f)*item.Scale+.9f+Vector3.Distance(centre,eye)*.017f;
                for(int plane=0;plane<6;plane++)if(planes[plane].GetDistanceToPoint(centre)<-radius)return false;
                return true;
            }
            void Add(Item item,int lod,ShadowCastingMode forceShadow=ShadowCastingMode.TwoSided)
            {
                var proto=Sheet.Prototypes[item.Prototype];lod=Mathf.Clamp(lod,0,proto.Lods.Length-1);
                var parts=proto.Lods[lod].Parts;
                int kind=(int)proto.Category;
                ShadowCastingMode shadows=lod==0&&kind!=2?ShadowCastingMode.On:ShadowCastingMode.Off;
                if(optimized&&FlatDistanceSquared(item.Position,eye)>ShadowDistance(kind)*ShadowDistance(kind))shadows=ShadowCastingMode.Off;
                if(forceShadow==ShadowCastingMode.ShadowsOnly)shadows=forceShadow;
                for(int i=0;i<parts.Length;i++)
                {
                    var part=parts[i];if(part.Mesh==null||part.Material==null)continue;
                    int key=(item.Prototype*1000+lod*50+i)*4+(int)shadows;
                    if(!cache.Batches.TryGetValue(key,out var batch))
                    {batch=new Batch{Mesh=part.Mesh,Submesh=part.Submesh,Material=part.Material,Shadows=shadows,Prototype=item.Prototype,Lod=lod,Kind=kind,Triangles=(long)part.Mesh.GetIndexCount(part.Submesh)/3};cache.Batches.Add(key,batch);}
                    batch.OverrideFade=optimized&&((kind==1&&proto.Lods.Length>=3)||(kind==2&&proto.GroundPatch)||shadows==ShadowCastingMode.ShadowsOnly);
                    if(batch.OverrideFade)batch.Fade=DenseFade(proto,lod,shadows);
                    batch.Matrices.Add(item.Matrix*part.Local);cache.Instances++;
                }
            }
        }
        void UpdateColliders()
        {
            collisionFocus=Observer.transform.position;collisionEnd=collisionFocus;
            if(VehicleSeat!=null&&VehicleSeat.Occupied&&VehicleSeat.Vehicle!=null)
            {var car=VehicleSeat.Vehicle;collisionFocus=car.transform.position;collisionEnd=collisionFocus+Vector3.ClampMagnitude(car.Body.linearVelocity*Sheet.VehiclePredictionSeconds,100);}
            if(!collisionInitialized||FlatDistance(previousCollisionFocus,collisionFocus)>50)
            {if(!UseStreaming)PrepareView(collisionFocus,12);collisionInitialized=true;}previousCollisionFocus=collisionFocus;
            float radius=Sheet.CollisionRadius; candidates.Clear();
            if(UseStreaming)CollectStreamingCollision(collisionFocus,collisionEnd,radius,candidates);
            foreach(var cell in live.Values)
            {
                // Candidate instances live within a 256m cell; far cells cannot enter this short
                // near/vehicle-prediction corridor and need no per-instance collision scan.
                if(SegmentDistance(cell.Source.Centre,collisionFocus,collisionEnd)>radius+220)continue;
                Collect(cell.Near);Collect(cell.Fixed);if(Sheet.DenseVegetation)Collect(cell.Far);
            }
            void Collect(Item[] items)
            {
                if(items==null)return;foreach(var item in items)
                {var p=Sheet.Prototypes[item.Prototype];if(p.Category!=WorldMacroDressingSheetSO.Kind.Tree&&p.Category!=WorldMacroDressingSheetSO.Kind.Rock&&p.Category!=WorldMacroDressingSheetSO.Kind.Prop)continue;
                if(SegmentDistance(item.Position,collisionFocus,collisionEnd)<radius+p.Radius*item.Scale)candidates.Add(item);}
            }
            candidates.Sort(CompareCollision);ColliderOverflow=Mathf.Max(0,candidates.Count-Sheet.ColliderPoolSize);
            int count=Mathf.Min(candidates.Count,Sheet.ColliderPoolSize);
            desiredCollisions.Clear();for(int i=0;i<count;i++)desiredCollisions.Add(candidates[i].Id);
            foreach(var entry in pool)
            {
                if(entry.Id==long.MinValue||desiredCollisions.Contains(entry.Id))continue;
                collisionSlots.Remove(entry.Id);entry.Id=long.MinValue;entry.Object.SetActive(false);
            }
            while(pool.Count<count)
            {
                var go=new GameObject("Dressing_CollisionPool_"+pool.Count){hideFlags=HideFlags.DontSave};go.transform.SetParent(transform,false);go.SetActive(false);
                pool.Add(new CollisionItem{Object=go,Capsule=go.AddComponent<CapsuleCollider>(),Box=go.AddComponent<BoxCollider>()});
            }
            for(int i=0;i<count;i++)
            {
                var item=candidates[i];if(collisionSlots.ContainsKey(item.Id))continue;
                CollisionItem entry=null;foreach(var available in pool)if(available.Id==long.MinValue){entry=available;break;}
                if(entry==null){ColliderOverflow++;continue;}entry.Id=item.Id;collisionSlots.Add(item.Id,entry);
                var p=Sheet.Prototypes[item.Prototype];bool tree=p.Category==WorldMacroDressingSheetSO.Kind.Tree;
                entry.Object.transform.SetPositionAndRotation(item.Position,item.Matrix.rotation);entry.Object.transform.localScale=Vector3.one*item.Scale;
                entry.Capsule.enabled=tree;entry.Box.enabled=!tree;
                if(tree){entry.Capsule.radius=p.Radius;entry.Capsule.height=Mathf.Max(p.Radius*2,p.Size.y*.7f);entry.Capsule.center=Vector3.up*entry.Capsule.height*.5f;}
                else{entry.Box.size=p.Size*.85f;entry.Box.center=Vector3.up*p.Size.y*.425f;}
                entry.Object.SetActive(true);
            }
            ActiveColliders=collisionSlots.Count;
        }
        int CompareCollision(Item a,Item b)=>SegmentDistance(a.Position,collisionFocus,collisionEnd).CompareTo(SegmentDistance(b.Position,collisionFocus,collisionEnd));
        static float SegmentDistance(Vector3 p,Vector3 a,Vector3 b)
        {p.y=a.y=b.y=0;var d=b-a;float t=d.sqrMagnitude<.001f?0:Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude);return Vector3.Distance(p,a+d*t);}
    }
}
