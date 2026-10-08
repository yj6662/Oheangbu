using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Stopwatch=System.Diagnostics.Stopwatch;

namespace Oheangbu.App.World
{
    // Fixed, authored placements; no streaming or global renderer settings are changed.
    // SPEC-ART-RENDERER-CULLING S3 path: 64 m cells skip groups whose outcome is already known, then the unchanged per-instance
    // rules run in sheet order, so every batch receives exactly the matrices (and order) of the former full scan (`artcull`).
    // #307 2a (same S3 contract): per-batch property blocks built once in Prepare, matrices written in place and submitted with
    // Graphics.RenderMeshInstanced(start offset) in the same 1023 packets, worldBounds = union of the packet's instance bounds,
    // and in Play the lists are kept while the camera is bit-identical (pause, menus, dialogue).
    // #307 2b (S8 screen-identical contract, user-approved 2026-09-30): the view test also runs inside 80 m, offscreen casters inside
    // 80 m go ShadowsOnly, shadows only within the live shadow limit + CasterMargin, one chunk per 64 m cell with its own worldBounds.
    // WorldMacroDressingSheetSO.ScreenIdenticalCulling307 picks the path (CullPath307 overrides it at runtime); `artcull2`/`artpixel`
    // (Editor/WorldMacro/CompactArtCulling307.cs) prove it.
    [ExecuteAlways]
    public sealed class CompactRebuildArtRenderer : MonoBehaviour
    {
        public WorldMacroDressingSheetSO Sheet;
        public Camera Observer;
        public CompactEnvironmentContact265 Contacts;
#if UNITY_EDITOR
        [NonSerialized] public bool ContactPreview265;
        [NonSerialized] public float ContactPreviewClock265=-1;
#endif
        // #307: -1 = the sheet's ScreenIdenticalCulling307, 0 = S3 path, 1 = S8 path. Runtime A/B and pixel-check switch; never saved.
        [NonSerialized] public int CullPath307=-1;
        // #307 `shadowbounds` experiment only: every chunk gets the whole sheet's bounds instead of its own.
        [NonSerialized] public bool WholeSheetBounds307;
        // #307 diagnostics only (PerfSweep307 artskip=): bit 1 = no ShadowsOnly chunks, 2 = no On chunks, 4 = no Off chunks
        [NonSerialized] public int SkipModes307;
        readonly List<Batch> submitOrder=new List<Batch>();
#if UNITY_EDITOR
        [NonSerialized] public bool ReverseSubmit307;
#endif
        // #307 experiment (artpixel limit=): >= 0 replaces the shadow caster distance limit (PositiveInfinity = no limit); never saved
        [NonSerialized] public float ShadowLimitOverride307=-1;
        // #307 experiment (artpixel seen=): 1 = every instance inside 80 m counts as seen (S3 lists through the S8 chunked submit)
        [NonSerialized] public int ForceSeen307;
        // #307 InteriorSight307: this camera stands in a baked sealed cave cell, so neither these draws nor the linked grass are needed
        [NonSerialized] public Camera SuppressFor307;
        // #307 check switch (artpixel native=1): the per-instance view test through GeometryUtility.TestPlanesAABB instead of Planes()
        [NonSerialized] public bool NativePlaneTest307;
        // #307 experiment: 1 = every camera-drawn chunk (On/Off) sorted front to back after a collect, so hidden leaves fail the depth test
        // before shading; -1 = back to front (the worst case, for the A/B); 0 = collect order. Shadow-only chunks are never sorted.
        [NonSerialized] public int SortChunks307;
        float[] sortKeys=new float[1024];
#if UNITY_EDITOR
        // #307 `artpixel` reference only: the S3 lists submitted exactly as before 2a (Graphics.DrawMeshInstanced from a 1023 packet copy,
        // one shared property block rebuilt per batch), so the pixel check also covers the RenderMeshInstanced / cached-block switch.
        [NonSerialized] public bool LegacySubmit307;
        readonly Matrix4x4[] legacyPacket=new Matrix4x4[1023];
        MaterialPropertyBlock legacyProperties;
#endif
        // #308 roots (TEST): -1 = the TreeRoots308 profile decides, 0 = draw as before the profile (before / after stills, checks); never saved.
        [NonSerialized] public int Roots308Mode=-1;
        TreeRoots308SO roots308;bool rootsLooked308;
        public int RootedParts308 {get;private set;}     // batches drawing a rooted twin mesh
        public int ShiftedTrees308 {get;private set;}    // tree instances whose height the profile moved
        public float DeepestShift308 {get;private set;}  // most negative dy (m)
        public int DrawCalls {get;private set;}
        public int VisibleInstances {get;private set;}
        public long SubmittedTriangles {get;private set;}
        public double LastCpuMs {get;private set;}
        // S8 only (0 on the S3 path): the ShadowsOnly share. DrawCalls/SubmittedTriangles include it; VisibleInstances counts only
        // instances drawn for the camera (On or Off), ShadowOnlyInstances those submitted to shadow passes only.
        public int ShadowOnlyInstances {get;private set;}
        public int ShadowOnlyDrawCalls {get;private set;}
        public long ShadowOnlyTriangles {get;private set;}
        public bool LastScreenIdentical307 {get;private set;}
        public float LastShadowLimit307 {get;private set;}  // S8: max shadow distance used (-1 shadows off / S3 path, +inf unknown pipeline)
        public int BandReuses307 {get;private set;}          // #307 2c: Draw() calls that reused band-collected lists (camera moved within the band)
        public int CollectReuses307 {get;private set;}      // Draw() calls that kept the previous lists (Play, bit-identical camera)
        // Matrices are written in place (no List.Add/CopyTo). A chunk holds at most 1023 matrices and, on the S8 path, one cell.
        sealed class Stream
        {
            public Matrix4x4[] Matrices=new Matrix4x4[16];
            public int Count,Chunks,LastCell=int.MinValue;
            public int MinChunk=1;public float MaxSpan;   // chunk merge (sheet ChunkMinInstances307 / ChunkMaxSpan307)
            bool lastFine;                                 // the open chunk belongs to a fine (never merged) shadow cell
            public int[] Start=new int[4];
            public Vector3[] Low=new Vector3[4],High=new Vector3[4];
            public void Clear(){Count=Chunks=0;LastCell=int.MinValue;lastFine=false;}
            public int End(int chunk)=>chunk+1<Chunks?Start[chunk+1]:Count;
            // a new cell stays in the current chunk while that chunk is small or the union stays within MaxSpan (XZ); cell -1 never splits
            bool Joins(Vector3 low,Vector3 high)
            {
                int c=Chunks-1;if(Count-Start[c]<MinChunk)return true;
                var l=Vector3.Min(Low[c],low);var h=Vector3.Max(High[c],high);
                return h.x-l.x<=MaxSpan&&h.z-l.z<=MaxSpan;
            }
            // fine: a shadow-casting cell near the eye or a shadowed lantern keeps its own chunk (tight bounds -> URP culls it per
            // cascade / cube face); coarse cells merge as before
            public void Add(in Matrix4x4 matrix,int cell,Vector3 low,Vector3 high,bool fine=false)
            {
                if(Chunks==0||Count-Start[Chunks-1]>=1023||cell!=LastCell&&(fine||lastFine||!Joins(low,high)))
                {
                    if(Chunks==Start.Length){int n=Chunks*2;Array.Resize(ref Start,n);Array.Resize(ref Low,n);Array.Resize(ref High,n);}
                    Start[Chunks]=Count;Low[Chunks]=low;High[Chunks]=high;Chunks++;LastCell=cell;lastFine=fine;
                }
                else{int c=Chunks-1;Low[c]=Vector3.Min(Low[c],low);High[c]=Vector3.Max(High[c],high);}
                if(Count==Matrices.Length)Array.Resize(ref Matrices,Count*2);
                Matrices[Count++]=matrix;
            }
        }
        sealed class Batch
        {
            public WorldMacroDressingSheetSO.Part Part;
            public WorldMacroDressingSheetSO.Part Origin;   // #308 roots: the sheet's part (Part is a copy with the rooted mesh when the profile swaps it)
            public Vector4 Fade;
            public ShadowCastingMode Shadows;  // S3 mode: Off for Grass and LOD >= 2, else On
            public readonly Stream On=new Stream(),ShadowOnly=new Stream(),Off=new Stream();
            public Stream Legacy;              // S3 path list: On or Off by Shadows
            public bool LocalIdentity;         // #307: Part.Local == identity, the submitted matrix is the instance matrix (no multiply)
            public bool ShadowProxy;           // #307 shadow proxy part: ShadowsOnly over LOD0's range, seen or not (the LOD0 parts are camera-only)
            public MaterialPropertyBlock Properties;
            public bool SoftMaterial;          // Prepare cache: material _ContactSoft265 > .5 (re-read when Part.Material is swapped)
            public Material SoftSource;        // the material SoftMaterial was read from
            public int SoftState=-1;           // contact state last written into Properties
            public void Clear(){On.Clear();ShadowOnly.Clear();Off.Clear();}
        }
        // Reach: farthest distance at which any batch of the prototype can still take the instance.
        // Low/High: conservative render box (mesh bounds under every part, billboard turn, vertex sway, slack); Height: its top above the base.
        struct Instance {public int Prototype;public Matrix4x4 Matrix;public Bounds Bounds;public Vector3 Position,Low,High;public float Radius,Reach,Height;}
        // XZ rectangle of member positions, padded union of member bounds (Centre/Extent) and render boxes (View*), largest member reach.
        struct Cell {public float MinX,MinZ,MaxX,MaxZ,Reach;public Vector3 Centre,Extent,ViewCentre,ViewExtent;public int First,Count;public bool Unbounded;}
        const float CellSize=64,ShadowReach=80,Slack=1;
        const byte Rejected=0,Outside=1,Partial=2,Inside=3;
        static readonly int FadeOverrideId=Shader.PropertyToID("_DressingFadeOverride"),FadeRangeId=Shader.PropertyToID("_DressingFadeRange"),
            TimeId=Shader.PropertyToID("_DressingTime"),CullRadiusId=Shader.PropertyToID("_DressingCullRadius"),
            ContactSoftId=Shader.PropertyToID("_ContactSoft265"),ContactEnabledId=Shader.PropertyToID("_ContactEnabled265"),ContactPointsId=Shader.PropertyToID("_ContactPoints265"),
            BillboardId=Shader.PropertyToID("_Billboard"),WindAmplitudeId=Shader.PropertyToID("_WindAmplitude"),LeafFlutterId=Shader.PropertyToID("_LeafFlutter"),GrassGustId=Shader.PropertyToID("_GrassGust267");
        // Bit index of an isolated low bit (de Bruijn 0x03F79D71B4CB0A89).
        static readonly byte[] LowBit={0,1,48,2,57,49,28,3,61,58,50,42,38,29,17,4,62,55,59,36,53,51,43,22,45,39,33,30,24,18,12,5,63,47,56,27,60,41,37,16,54,35,52,21,44,32,23,11,46,26,40,15,34,20,31,10,25,14,19,9,13,8,7,6};
        Instance[] instances=Array.Empty<Instance>();
        Cell[] cells=Array.Empty<Cell>();
        int[] members=Array.Empty<int>(),cellOf=Array.Empty<int>();
        byte[] cellState=Array.Empty<byte>();
        ulong[] candidates=Array.Empty<ulong>();
        Batch[][][] batches;
        WorldMacroDressingSheetSO loaded;
        Vector3 sheetLow,sheetHigh;
        readonly Plane[] planes=new Plane[6];
        // #307 fine shadow cells: shadowed punctual lights (rescanned every 2 s; budget-suppressed lights have shadows None)
        readonly List<Light> shadowLights=new List<Light>();float nextLightScan;
        // 2a reuse key (Play only): the lists belong to this camera state; null = must collect.
        Camera collectedFor;Matrix4x4 collectedView,collectedProjection;Vector3 collectedEye;bool collectedScreen,collectedShadows;float collectedLimit,collectedCot;
        // #307 2c safety band: lists collected with every box padded by collectPad metres and the frustum widened by the band angle stay
        // valid while the camera stays within collectedBand metres / 0.75 x collectedDegrees of the collect pose (same lens and limits)
        float collectPad;Vector3 collectedForward,collectedUp;float collectedBand,collectedDegrees,collectedFov,collectedAspect,collectedNear,collectedFar;
        void OnEnable(){loaded=null;rootsLooked308=false;RenderPipelineManager.beginCameraRendering+=Draw;}
        void OnDisable(){RenderPipelineManager.beginCameraRendering-=Draw;Release();}
        void OnValidate(){loaded=null;}
        public void Invalidate(){loaded=null;collectedFor=null;rootsLooked308=false;}
        // #308 roots: the profile for this sheet, or null (off, not listed, asset absent). Loaded per component, no static.
        TreeRoots308SO Roots308()
        {
            if(Roots308Mode==0)return null;
            if(!rootsLooked308){rootsLooked308=true;roots308=Resources.Load<TreeRoots308SO>(TreeRoots308SO.ResourcePath);}
            return roots308!=null&&roots308.Applies(Sheet)?roots308:null;
        }
        WorldMacroDressingSheetSO.Part Rooted308(TreeRoots308SO roots,WorldMacroDressingSheetSO.Part part)
        {
            if(roots==null||part==null||part.Mesh==null)return part;
            var mesh=roots.Rooted(part.Mesh);if(mesh==part.Mesh)return part;
            RootedParts308++;return new WorldMacroDressingSheetSO.Part{Mesh=mesh,Submesh=part.Submesh,Material=part.Material,Local=part.Local};
        }

        void Release(){instances=Array.Empty<Instance>();cells=Array.Empty<Cell>();members=cellOf=Array.Empty<int>();cellState=Array.Empty<byte>();candidates=Array.Empty<ulong>();batches=null;loaded=null;collectedFor=null;}
        void Prepare()
        {
            if(loaded==Sheet&&batches!=null)return;
            loaded=Sheet;collectedFor=null;var lookup=new Dictionary<string,int>();batches=new Batch[Sheet.Prototypes.Length][][];
            var roots=Roots308();RootedParts308=ShiftedTrees308=0;DeepestShift308=0;
            int kinds=Sheet.Prototypes.Length;var local=new Bounds[kinds];var hasLocal=new bool[kinds];var turn=new float[kinds];var sway=new float[kinds];
            for(int p=0;p<Sheet.Prototypes.Length;p++)
            {
                var prototype=Sheet.Prototypes[p];lookup.Add(prototype.Id,p);batches[p]=new Batch[prototype.Lods.Length][];
                for(int l=0;l<prototype.Lods.Length;l++)
                {
                    var parts=prototype.Lods[l].Parts;batches[p][l]=new Batch[parts.Length];
                    var fade=Range(prototype.Category,l,prototype.Lods.Length,prototype.Id);
                    for(int m=0;m<parts.Length;m++)
                    {
                        bool tree308=roots!=null&&prototype.Category==WorldMacroDressingSheetSO.Kind.Tree;
                        var batch=batches[p][l][m]=new Batch{Part=tree308?Rooted308(roots,parts[m]):parts[m],Origin=parts[m],Fade=fade,
                            Shadows=prototype.Category==WorldMacroDressingSheetSO.Kind.Grass||l>=2?ShadowCastingMode.Off:ShadowCastingMode.On};
                        batch.Legacy=batch.Shadows==ShadowCastingMode.Off?batch.Off:batch.On;batch.LocalIdentity=parts[m].Local.Equals(Matrix4x4.identity);
                        var material=parts[m].Material;
                        batch.SoftSource=material;batch.SoftMaterial=material!=null&&material.HasProperty(ContactSoftId)&&material.GetFloat(ContactSoftId)>.5f;
                        batch.Properties=new MaterialPropertyBlock();BaseProperties(batch);
                        if(batch.Part.Mesh==null)continue;
                        var mesh=batch.Part.Mesh.bounds;
                        if(material!=null&&material.HasProperty(BillboardId)&&material.GetFloat(BillboardId)>.5f)turn[p]=Mathf.Max(turn[p],TurnRadius(parts[m].Local,mesh));
                        else{var b=Transform(mesh,parts[m].Local);if(hasLocal[p])local[p].Encapsulate(b);else{local[p]=b;hasLocal[p]=true;}}
                        sway[p]=Mathf.Max(sway[p],Sway(material));
                    }
                }
                // #307 shadow proxy: LOD0 camera-only, the proxy LOD's parts cast the LOD0 range's shadows
                var proxy=Sheet.ShadowProxy307For(prototype.Id);
                if(proxy!=null&&prototype.Lods.Length>1&&proxy.ProxyLod>0&&proxy.ProxyLod<prototype.Lods.Length&&prototype.Category!=WorldMacroDressingSheetSO.Kind.Grass)
                {
                    var lod0=batches[p][0];var fade0=lod0.Length>0?lod0[0].Fade:Range(prototype.Category,0,prototype.Lods.Length,prototype.Id);
                    foreach(var bt in lod0)if(bt.Shadows!=ShadowCastingMode.Off){bt.Shadows=ShadowCastingMode.Off;bt.Legacy=bt.Off;}
                    var proxyParts=prototype.Lods[proxy.ProxyLod].Parts;var merged=new Batch[lod0.Length+proxyParts.Length];Array.Copy(lod0,merged,lod0.Length);
                    for(int m=0;m<proxyParts.Length;m++)
                    {
                        var bt=merged[lod0.Length+m]=new Batch{Part=roots!=null&&prototype.Category==WorldMacroDressingSheetSO.Kind.Tree?Rooted308(roots,proxyParts[m]):proxyParts[m],Origin=proxyParts[m],Fade=fade0,Shadows=ShadowCastingMode.ShadowsOnly,ShadowProxy=true};
                        bt.Legacy=bt.ShadowOnly;bt.LocalIdentity=proxyParts[m].Local.Equals(Matrix4x4.identity);
                        var material=proxyParts[m].Material;bt.SoftSource=material;bt.SoftMaterial=material!=null&&material.HasProperty(ContactSoftId)&&material.GetFloat(ContactSoftId)>.5f;
                        bt.Properties=new MaterialPropertyBlock();BaseProperties(bt);
                    }
                    batches[p][0]=merged;
                }
            }
            float slack=Mathf.Max(0,Sheet.CullBoundsSlack307);  // a negative slack would shrink worldBounds below the rendered geometry
            var accepted=new List<Instance>();
            foreach(var p in Sheet.FixedPlacements)
            {
                if(!lookup.TryGetValue(p.PrototypeId,out int index))continue;
                var size=Sheet.Prototypes[index].Size*p.Scale;float radius=Mathf.Max(size.x,size.z)*.75f;
                var at=p.Position;
                if(roots!=null&&Sheet.Prototypes[index].Category==WorldMacroDressingSheetSO.Kind.Tree)
                {
                    float dy=roots.Shift(p.PrototypeId,at,p.Euler,p.Scale);
                    if(dy!=0){at.y+=dy;ShiftedTrees308++;if(dy<DeepestShift308)DeepestShift308=dy;}
                }
                var instance=new Instance{Prototype=index,Position=at,Radius=radius,Reach=ReachOf(batches[index],radius),Bounds=new Bounds(at+Vector3.up*size.y*.5f,new Vector3(radius*2,Mathf.Max(size.y,.2f),radius*2)),Matrix=Matrix4x4.TRS(at,Quaternion.Euler(p.Euler),Vector3.one*p.Scale)};
                RenderBox(ref instance,p.Scale,local[index],hasLocal[index],turn[index],sway[index]+slack);
                accepted.Add(instance);
            }
            instances=accepted.ToArray();
            BuildCells();
        }
        void BaseProperties(Batch batch)
        {
            var properties=batch.Properties;properties.Clear();
            properties.SetFloat(FadeOverrideId,1);properties.SetVector(FadeRangeId,batch.Fade);properties.SetFloat(CullRadiusId,0);
            // TODO(#307 2c, not implemented): safety-band reuse (collect with a frustum widened by theta and pushed back by B, reuse while the
            // camera moves < B and turns < theta, _DressingCullRadius = max radius + part offset x scale + B, B/theta as sheet data) needs its
            // ordered-subsequence check and the phase-0 draw-call vs Collect numbers first (PERF_DESIGN 2c). Until then the band stays 0.
            batch.SoftState=-1;
        }
        // #302 presentation: drop instances whose base hangs above the ground (left at an old height after a pad levelled the
        // terrain) for this session only; the sheet data is untouched. Returns how many moved.
        public int DropFloaters(Vector3 centre,float radius,float minGap)
        {
            Prepare();int moved=0;
            for(int i=0;i<instances.Length;i++)
            {
                ref var it=ref instances[i];
                if(new Vector2(it.Position.x-centre.x,it.Position.z-centre.z).sqrMagnitude>radius*radius)continue;
                if(!Physics.Raycast(it.Position+Vector3.up*.3f,Vector3.down,out var hit,120f,~0,QueryTriggerInteraction.Ignore))continue;
                float gap=it.Position.y-hit.point.y;if(gap<minGap)continue;
                var d=Vector3.down*(gap+.05f);it.Position+=d;it.Bounds.center+=d;it.Low+=d;it.High+=d;it.Matrix=Matrix4x4.Translate(d)*it.Matrix;moved++;
            }
            if(moved>0)BuildCells();
            return moved;
        }
        // Same float sums as the batch test in Collect; NaN stays NaN so such an instance is never skipped early.
        static float ReachOf(Batch[][] levels,float radius)
        {
            float reach=float.NegativeInfinity;
            foreach(var level in levels)foreach(var batch in level){float w=batch.Fade.w+radius;if(float.IsNaN(w))return w;if(w>reach)reach=w;}
            return reach;
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        // AABB of a box under an affine matrix (conservative for any rotation/scale).
        static Bounds Transform(Bounds box,in Matrix4x4 m)
        {
            var e=box.extents;
            var x=new Vector3(Mathf.Abs(m.m00)*e.x+Mathf.Abs(m.m01)*e.y+Mathf.Abs(m.m02)*e.z,Mathf.Abs(m.m10)*e.x+Mathf.Abs(m.m11)*e.y+Mathf.Abs(m.m12)*e.z,Mathf.Abs(m.m20)*e.x+Mathf.Abs(m.m21)*e.y+Mathf.Abs(m.m22)*e.z);
            return new Bounds(m.MultiplyPoint3x4(box.center),x*2);
        }
        // Billboard parts (CompactNaturalVegetation _Billboard) turn to face the camera about the part origin with per-axis scale =
        // column lengths: every vertex stays within |Local.t| + max column * farthest mesh corner of the instance pivot (x instance scale).
        static float TurnRadius(in Matrix4x4 local,Bounds mesh)
        {
            var c=mesh.center;var e=mesh.extents;float corner=new Vector3(Mathf.Abs(c.x)+e.x,Mathf.Abs(c.y)+e.y,Mathf.Abs(c.z)+e.z).magnitude;
            float column=Mathf.Max(((Vector3)local.GetColumn(0)).magnitude,Mathf.Max(((Vector3)local.GetColumn(1)).magnitude,((Vector3)local.GetColumn(2)).magnitude));
            return ((Vector3)local.GetColumn(3)).magnitude+column*corner;
        }
        // Upper bound of the vertex displacement in CompactNaturalVegetation SurfaceVertex (shared by every pass), world metres:
        // wind |(.8,.35)| x gust 1.35, flutter |(.6,.15,.35)|, grass gust wave 1.22 (+14 % drop), contact bend .42 + drop .12.
        static float Sway(Material material)
        {
            if(material==null)return 0;float s=0;
            if(material.HasProperty(WindAmplitudeId))s+=1.18f*Mathf.Abs(material.GetFloat(WindAmplitudeId));
            if(material.HasProperty(LeafFlutterId))s+=.72f*Mathf.Abs(material.GetFloat(LeafFlutterId));
            if(material.HasProperty(GrassGustId)){float g=material.GetVector(GrassGustId).z;if(g>0)s+=1.4f*g;}
            if(material.HasProperty(ContactEnabledId))s+=.45f;
            return s;
        }
        static void RenderBox(ref Instance instance,float scale,Bounds local,bool hasLocal,float turn,float pad)
        {
            Vector3 low=instance.Position,high=instance.Position;
            if(hasLocal){var b=Transform(local,instance.Matrix);low=Vector3.Min(low,b.min);high=Vector3.Max(high,b.max);}
            if(turn>0){var r=Vector3.one*(turn*Mathf.Abs(scale));low=Vector3.Min(low,instance.Position-r);high=Vector3.Max(high,instance.Position+r);}
            var e=Vector3.one*pad;instance.Low=low-e;instance.High=high+e;instance.Height=instance.High.y-instance.Position.y;
        }
        // Members stay in ascending sheet order. Non-finite data goes to one cell that is never rejected.
        void BuildCells()
        {
            collectedFor=null;
            int count=instances.Length;cellOf=new int[count];var index=new Dictionary<long,int>();var sizes=new List<int>();
            sheetLow=Vector3.positiveInfinity;sheetHigh=Vector3.negativeInfinity;
            for(int i=0;i<count;i++)if(Finite(instances[i].Low)&&Finite(instances[i].High)){sheetLow=Vector3.Min(sheetLow,instances[i].Low);sheetHigh=Vector3.Max(sheetHigh,instances[i].High);}
            if(!(sheetLow.x<=sheetHigh.x)){sheetLow=Vector3.one*-1e6f;sheetHigh=Vector3.one*1e6f;}
            for(int i=0;i<count;i++)
            {
                ref var instance=ref instances[i];
                // A non-finite render box would poison a whole chunk's worldBounds: give it the sheet box (the matrices are unchanged).
                if(!Finite(instance.Low)||!Finite(instance.High)){instance.Low=sheetLow;instance.High=sheetHigh;instance.Height=instance.High.y-instance.Position.y;}
                bool bounded=Finite(instance.Position)&&Finite(instance.Radius)&&!float.IsNaN(instance.Reach)&&Finite(instance.Bounds.center)&&Finite(instance.Bounds.extents)
                    &&Mathf.Abs(instance.Position.x)<1e7f&&Mathf.Abs(instance.Position.z)<1e7f;
                long key=bounded?((long)Mathf.FloorToInt(instance.Position.x/CellSize)<<32)^(uint)Mathf.FloorToInt(instance.Position.z/CellSize):long.MinValue;
                if(!index.TryGetValue(key,out int cell)){cell=sizes.Count;index.Add(key,cell);sizes.Add(0);}
                cellOf[i]=cell;sizes[cell]++;
            }
            cells=new Cell[sizes.Count];members=new int[count];cellState=new byte[cells.Length];candidates=new ulong[(count+63)>>6];
            var low=new Vector3[cells.Length];var high=new Vector3[cells.Length];var viewLow=new Vector3[cells.Length];var viewHigh=new Vector3[cells.Length];
            for(int c=0,first=0;c<cells.Length;first+=sizes[c],c++)
            {
                cells[c]=new Cell{First=first,MinX=float.PositiveInfinity,MinZ=float.PositiveInfinity,MaxX=float.NegativeInfinity,MaxZ=float.NegativeInfinity,Reach=float.NegativeInfinity};
                low[c]=viewLow[c]=Vector3.positiveInfinity;high[c]=viewHigh[c]=Vector3.negativeInfinity;
            }
            if(index.TryGetValue(long.MinValue,out int unbounded))cells[unbounded].Unbounded=true;
            for(int i=0;i<count;i++)
            {
                int c=cellOf[i];ref var cell=ref cells[c];members[cell.First+cell.Count++]=i;
                ref var instance=ref instances[i];
                cell.MinX=Mathf.Min(cell.MinX,instance.Position.x);cell.MaxX=Mathf.Max(cell.MaxX,instance.Position.x);
                cell.MinZ=Mathf.Min(cell.MinZ,instance.Position.z);cell.MaxZ=Mathf.Max(cell.MaxZ,instance.Position.z);
                cell.Reach=Mathf.Max(cell.Reach,instance.Reach);
                low[c]=Vector3.Min(low[c],instance.Bounds.min);high[c]=Vector3.Max(high[c],instance.Bounds.max);
                viewLow[c]=Vector3.Min(viewLow[c],instance.Low);viewHigh[c]=Vector3.Max(viewHigh[c],instance.High);
            }
            for(int c=0;c<cells.Length;c++)
            {
                cells[c].Centre=(low[c]+high[c])*.5f;cells[c].Extent=(high[c]-low[c])*.5f+Vector3.one*Slack;
                cells[c].ViewCentre=(viewLow[c]+viewHigh[c])*.5f;cells[c].ViewExtent=(viewHigh[c]-viewLow[c])*.5f+Vector3.one*Slack;
            }
        }
        Vector4 Range(WorldMacroDressingSheetSO.Kind kind,int lod,int count,string prototypeId=null)
        {
            if(kind==WorldMacroDressingSheetSO.Kind.Tree)
            {
                float near=Sheet.TreeNear,middle=Sheet.TreeMiddle;var o=Sheet.LodOverride307For(prototypeId);   // #307 per-prototype distances
                if(o!=null){if(o.TreeNear>0)near=o.TreeNear;if(o.TreeMiddle>0)middle=Mathf.Max(o.TreeMiddle,near+40);}
                return lod==0?new Vector4(-1,0,near-10,near+10):lod==1?new Vector4(near-10,near+10,middle-20,middle+20):new Vector4(middle-20,middle+20,Sheet.ForestDistance-100,Sheet.ForestDistance);
            }
            float limit=kind==WorldMacroDressingSheetSO.Kind.Grass?Sheet.GrassDistance:kind==WorldMacroDressingSheetSO.Kind.Shrub?Sheet.ShrubDistance:700;
            float change=kind==WorldMacroDressingSheetSO.Kind.Grass?Sheet.GrassMeshDistance:kind==WorldMacroDressingSheetSO.Kind.Shrub?35:65;
            return count==1?new Vector4(-1,0,limit-20,limit):lod==0?new Vector4(-1,0,change-7,change+7):new Vector4(change-7,change+7,limit-20,limit);
        }
        bool ScreenPath=>CullPath307==1||CullPath307<0&&Sheet.ScreenIdenticalCulling307;
        void Draw(ScriptableRenderContext context,Camera camera)
        {
            if(Sheet==null||camera==null||camera.cameraType==CameraType.Reflection||camera.cameraType==CameraType.Preview)return;
            if(camera.cameraType!=CameraType.SceneView&&camera!=Observer)return;
            if(camera==SuppressFor307){DrawCalls=ShadowOnlyDrawCalls=0;SubmittedTriangles=ShadowOnlyTriangles=0;return;}
            long start=Stopwatch.GetTimestamp();
            Prepare();bool screen=ScreenPath;bool shadows=false;float limit=0,cot=0;
            if(screen)ShadowLimits(camera,out shadows,out limit,out cot);
            using(Perf307Markers.ArtCollect.Auto())
            {
                if(Application.isPlaying&&Collected(camera,screen,shadows,limit,cot))CollectReuses307++;
                else if(Application.isPlaying&&screen&&BandReuse(camera,shadows,limit,cot))BandReuses307++;
                else
                {
                    float band=screen&&Application.isPlaying&&!camera.orthographic?Mathf.Max(0,Sheet.CollectBandMetres307):0,degrees=band>0?Mathf.Clamp(Sheet.CollectBandDegrees307,0,30):0;
                    if(band>0)WidenedPlanes(camera,degrees);else GeometryUtility.CalculateFrustumPlanes(camera,planes);var eye=camera.transform.position;
                    collectPad=band;
                    try{if(screen)CollectScreen(eye,shadows,limit,cot);else Collect(eye);}finally{collectPad=0;}
                    if(screen&&SortChunks307!=0)using(Perf307Markers.ArtSort.Auto())SortStreams(eye);
                    if(Application.isPlaying)
                    {
                        collectedFor=camera;collectedView=camera.worldToCameraMatrix;collectedProjection=camera.projectionMatrix;collectedEye=eye;collectedScreen=screen;collectedShadows=shadows;collectedLimit=limit;collectedCot=cot;
                        collectedBand=band;collectedDegrees=degrees;collectedForward=camera.transform.forward;collectedUp=camera.transform.up;
                        collectedFov=camera.fieldOfView;collectedAspect=camera.aspect;collectedNear=camera.nearClipPlane;collectedFar=camera.farClipPlane;
                    }
                }
            }
            LastScreenIdentical307=screen;LastShadowLimit307=screen&&shadows?limit:-1;
            using(Perf307Markers.ArtSubmit.Auto()){Submit(camera,screen);}
            LastCpuMs=Milliseconds(start);
        }
        /// <summary>#307 A/B tools: the next Draw collects again (the reuse key holds camera state only, not sheet fields).</summary>
        public void ForgetCollect307(){collectedFor=null;}
        // 2a: the lists already hold this exact camera state (every bit of view, projection and eye, same path and shadow limits).
        bool Collected(Camera camera,bool screen,bool shadows,float limit,float cot)
        {
            if(collectedFor!=camera||collectedScreen!=screen||collectedShadows!=shadows||!Same(collectedLimit,limit)||!Same(collectedCot,cot))return false;
            var eye=camera.transform.position;
            return Same(collectedEye.x,eye.x)&&Same(collectedEye.y,eye.y)&&Same(collectedEye.z,eye.z)&&SameBits(collectedView,camera.worldToCameraMatrix)&&SameBits(collectedProjection,camera.projectionMatrix);
        }
        bool BandReuse(Camera camera,bool shadows,float limit,float cot)
        {
            if(collectedFor!=camera||!collectedScreen||collectedBand<=0||collectedShadows!=shadows||!Same(collectedLimit,limit)||!Same(collectedCot,cot))return false;
            if(camera.orthographic||!Same(collectedFov,camera.fieldOfView)||!Same(collectedAspect,camera.aspect)||!Same(collectedNear,camera.nearClipPlane)||!Same(collectedFar,camera.farClipPlane))return false;
            var t=camera.transform;if((t.position-collectedEye).sqrMagnitude>collectedBand*collectedBand)return false;
            float cos=Mathf.Cos(collectedDegrees*.75f*Mathf.Deg2Rad);
            return Vector3.Dot(t.forward,collectedForward)>=cos&&Vector3.Dot(t.up,collectedUp)>=cos;
        }
        // frustum of this camera with both half angles widened by `degrees` (same near/far): covers every rotation within the band
        void WidenedPlanes(Camera camera,float degrees)
        {
            float v=camera.fieldOfView*.5f*Mathf.Deg2Rad,h=Mathf.Atan(Mathf.Tan(v)*camera.aspect),w=degrees*Mathf.Deg2Rad;
            float v2=Mathf.Min(v+w,1.55f),h2=Mathf.Min(h+w,1.55f);
            var projection=Matrix4x4.Perspective(v2*2*Mathf.Rad2Deg,Mathf.Tan(h2)/Mathf.Tan(v2),camera.nearClipPlane,camera.farClipPlane);
            GeometryUtility.CalculateFrustumPlanes(projection*camera.worldToCameraMatrix,planes);
        }
        static bool Same(float a,float b)=>BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b);
        static bool SameBits(Matrix4x4 a,Matrix4x4 b)
        {
            for(int i=0;i<16;i++)if(BitConverter.SingleToInt32Bits(a[i])!=BitConverter.SingleToInt32Bits(b[i]))return false;
            return true;
        }
        // URP gives receivers realtime shadows (main and additional lights, both faded by the same limit) only within
        // min(asset shadowDistance, camera far). Unknown pipeline: unlimited, so every S3 caster keeps casting.
        void ShadowLimits(Camera camera,out bool shadows,out float limit,out float cot)
        {
            shadows=true;limit=float.PositiveInfinity;
            if(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)
            {
                limit=Mathf.Min(asset.shadowDistance,camera.farClipPlane);
                shadows=limit>0&&(asset.supportsMainLightShadows||asset.supportsAdditionalLightShadows);
            }
            // URP honours renderShadows only for non-SceneView cameras (UniversalRenderPipeline.InitializeAdditionalCameraData).
            if(camera.cameraType!=CameraType.SceneView&&camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)&&!data.renderShadows)shadows=false;
            float elevation=Sheet.ShadowSunMinElevation307;var sun=RenderSettings.sun;
            if(sun!=null&&sun.type==LightType.Directional){float live=Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y,-1,1))*Mathf.Rad2Deg;if(live<elevation)elevation=live;}
            cot=1/Mathf.Tan(Mathf.Max(elevation,.5f)*Mathf.Deg2Rad);  // finite even for a sun at or below the horizon
            if(ShadowLimitOverride307>=0)limit=ShadowLimitOverride307;
        }
        // CasterMargin of one instance: its shadow can land this far (horizontally) beyond its render box.
        float CasterMargin(in Instance instance,float cot)
        {
            float margin=Mathf.Clamp((instance.Height+Sheet.ShadowReliefAllowance307)*cot,Sheet.ShadowCasterMarginClamp307.x,Sheet.ShadowCasterMarginClamp307.y);
            return Mathf.Max(margin,Sheet.ShadowPunctualReach307);
        }
        static float BoxDistanceXZ(Vector3 eye,in Instance instance)
        {
            float x=Mathf.Max(0,Mathf.Max(instance.Low.x-eye.x,eye.x-instance.High.x)),z=Mathf.Max(0,Mathf.Max(instance.Low.z-eye.z,eye.z-instance.High.z));
            return Mathf.Sqrt(x*x+z*z);
        }
        bool Caster(Vector3 eye,in Instance instance,bool shadows,float limit,float cot)=>shadows&&BoxDistanceXZ(eye,instance)-collectPad<=limit+CasterMargin(instance,cot);
        // Frustum class of a padded box: one plane fully outside -> Outside, inside all -> Inside, else Partial (NaN -> Partial).
        byte Planes(Vector3 centre,Vector3 extent)
        {
            byte state=Inside;
            for(int k=0;k<planes.Length;k++)
            {
                var n=planes[k].normal;float s=n.x*centre.x+n.y*centre.y+n.z*centre.z+planes[k].distance;
                float r=Mathf.Abs(n.x)*(extent.x+collectPad)+Mathf.Abs(n.y)*(extent.y+collectPad)+Mathf.Abs(n.z)*(extent.z+collectPad);
                if(s+r<0)return Outside;
                if(!(s-r>=0))state=Partial;
            }
            return state;
        }
        // A cell is rejected only when every member would be rejected by the per-instance rules below: all of its members are
        // beyond their reach, or all are beyond the 80 m shadow ring while their padded union lies outside one frustum plane.
        // Every test is "reject only when the comparison holds", so NaN input falls through to the unchanged rules.
        byte Classify(ref Cell cell,Vector3 eye)
        {
            if(cell.Unbounded)return Partial;
            float x=cell.MinX-eye.x,z=cell.MinZ-eye.z;
            if(eye.x-cell.MaxX>x)x=eye.x-cell.MaxX;if(x<0)x=0;
            if(eye.z-cell.MaxZ>z)z=eye.z-cell.MaxZ;if(z<0)z=0;
            float d=Mathf.Sqrt(x*x+z*z);  // same form as the instance distance, so no member is nearer than d
            if(d-collectPad>cell.Reach+Slack)return Rejected;
            byte state=Planes(cell.Centre,cell.Extent);
            return state==Outside&&d-collectPad>ShadowReach+Slack?Rejected:state;
        }
        void ClearStreams()
        {
            int min=Mathf.Max(1,Sheet.ChunkMinInstances307);float span=Mathf.Max(0,Sheet.ChunkMaxSpan307);
            foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level)
            {batch.Clear();batch.On.MinChunk=batch.ShadowOnly.MinChunk=batch.Off.MinChunk=min;batch.On.MaxSpan=batch.ShadowOnly.MaxSpan=batch.Off.MaxSpan=span;}
        }
        // S3 path: unchanged rules, sheet order, 1023 packets (cell -1 never splits a packet).
        void Collect(Vector3 eye)
        {
            ClearStreams();VisibleInstances=ShadowOnlyInstances=0;Array.Clear(candidates,0,candidates.Length);
            for(int c=0;c<cells.Length;c++)
            {
                ref var cell=ref cells[c];byte state=Classify(ref cell,eye);cellState[c]=state;if(state==Rejected)continue;
                for(int k=cell.First,end=cell.First+cell.Count;k<end;k++){int i=members[k];candidates[i>>6]|=1UL<<(i&63);}
            }
            for(int w=0;w<candidates.Length;w++)
            {
                ulong bits=candidates[w];
                while(bits!=0)
                {
                    int i=w<<6|LowBit[(int)(unchecked((bits&(~bits+1))*0x03F79D71B4CB0A89UL)>>58)];bits&=bits-1;
                    ref var instance=ref instances[i];
                    float x=eye.x-instance.Position.x,z=eye.z-instance.Position.z;float d=Mathf.Sqrt(x*x+z*z);
                    if(d>instance.Reach)continue;  // every batch below would reject it
                    // Keep nearby offscreen casters submitted so their shadows do not vanish at the view edge.
                    if(d>ShadowReach){byte state=cellState[cellOf[i]];if(state==Outside||state==Partial&&!GeometryUtility.TestPlanesAABB(planes,instance.Bounds))continue;}
                    bool visible=false;
                    foreach(var level in batches[instance.Prototype])foreach(var batch in level)
                    {
                        if(d<batch.Fade.x-instance.Radius||d>batch.Fade.w+instance.Radius)continue;
                        batch.Legacy.Add(instance.Matrix*batch.Part.Local,-1,instance.Low,instance.High);visible=true;
                    }
                    if(visible)VisibleInstances++;
                }
            }
        }
        // S8 path (2b). Per instance: beyond 80 m the S3 view test is kept as is (offscreen -> gone); inside 80 m the render box is
        // tested. Shadow Off batches (grass, LOD >= 2) need the view; shadow batches are On when seen and within the shadow limit,
        // Off when seen beyond it, ShadowsOnly when unseen inside 80 m and within it, else gone. Cells are walked in order and
        // each cell is its own chunk, so worldBounds stay tight (the order inside a batch follows cells, see S8.2.6).
        // #307: a shadow cell stays its own chunk within FineShadowRadius307 of the eye (cascades 0-1 cull it tightly) or within a
        // shadowed point/spot light's range + ShadowPunctualReach307 (each cube face culls it); screen-identical, only chunking changes
        // shadowed punctual lights of this collect as (x, z, range + ShadowPunctualReach307): the light objects are read once per collect
        // here, not once per cell (their transform / enabled / shadows reads were most of the classify cost)
        Vector3[] fineLights=Array.Empty<Vector3>();int fineLightCount;
        void PrepareFineLights()
        {
            fineLightCount=0;if(!Sheet.FineShadowLanterns307)return;
            if(Time.unscaledTime>=nextLightScan)
            {
                nextLightScan=Time.unscaledTime+2f;shadowLights.Clear();
                foreach(var l in FindObjectsByType<Light>(FindObjectsSortMode.None))if(l.type!=LightType.Directional)shadowLights.Add(l);
            }
            if(fineLights.Length<shadowLights.Count)fineLights=new Vector3[shadowLights.Count];
            for(int i=0;i<shadowLights.Count;i++)
            {
                var l=shadowLights[i];if(l==null||!l.isActiveAndEnabled||l.shadows==LightShadows.None)continue;
                var p=l.transform.position;fineLights[fineLightCount++]=new Vector3(p.x,p.z,l.range+Sheet.ShadowPunctualReach307);
            }
        }
        bool FineShadowCell(ref Cell cell,Vector3 eye)
        {
            float r=Sheet.FineShadowRadius307;if(r<=0&&fineLightCount==0)return false;
            float cx=cell.Centre.x,cz=cell.Centre.z,ex=cell.Extent.x,ez=cell.Extent.z;
            if(r>0){float dx=Mathf.Max(0,Mathf.Abs(eye.x-cx)-ex),dz=Mathf.Max(0,Mathf.Abs(eye.z-cz)-ez);if(Mathf.Sqrt(dx*dx+dz*dz)<=r)return true;}
            for(int i=0;i<fineLightCount;i++)
            {
                var l=fineLights[i];float dx=Mathf.Max(0,Mathf.Abs(l.x-cx)-ex),dz=Mathf.Max(0,Mathf.Abs(l.y-cz)-ez);
                if(Mathf.Sqrt(dx*dx+dz*dz)<=l.z)return true;
            }
            return false;
        }
        // #307 parallel S8 collect: the per-cell classification (it scans lights through the Unity API) runs here, the per-instance tests
        // run over contiguous ranges of those cells on worker threads, and the entries are replayed into the streams in cell/instance
        // order, so every stream, chunk and packet equals the single-thread collect (SerialCollect307 = that path, for A/B checks).
        [NonSerialized] public bool SerialCollect307;
        struct CollectEntry{public Batch Batch;public Matrix4x4 Matrix;public Vector3 Low,High;public int Cell;public byte Target;public bool Fine;}
        sealed class CollectWorker{public CollectEntry[] Entries=new CollectEntry[1024];public int Count,From,To,Visible,ShadowOnly;
            public ref CollectEntry Next(){if(Count==Entries.Length)Array.Resize(ref Entries,Count*2);return ref Entries[Count++];}}
        CollectWorker[] collectWorkers=Array.Empty<CollectWorker>();int[] activeCells=Array.Empty<int>();byte[] activeFar=Array.Empty<byte>(),activeNear=Array.Empty<byte>();bool[] activeFine=Array.Empty<bool>();
        Action<int> collectBody;Vector3 jobEye;bool jobShadows;float jobLimit,jobCot;
        public int CollectWorkers307 {get;private set;}
        void CollectScreen(Vector3 eye,bool shadows,float limit,float cot)
        {
            ClearStreams();VisibleInstances=ShadowOnlyInstances=0;
            if(activeCells.Length<cells.Length){activeCells=new int[cells.Length];activeFar=new byte[cells.Length];activeNear=new byte[cells.Length];activeFine=new bool[cells.Length];}
            int active=0;long memberCount=0;
            var classify=Perf307Markers.ArtClassify.Auto();
            if(shadows)PrepareFineLights();else fineLightCount=0;
            for(int c=0;c<cells.Length;c++)
            {
                ref var cell=ref cells[c];byte far=Classify(ref cell,eye);if(far==Rejected)continue;
                bool fine=shadows&&FineShadowCell(ref cell,eye);
                byte near=cell.Unbounded?Partial:Planes(cell.ViewCentre,cell.ViewExtent);
                if(near==Outside&&far==Outside&&!shadows&&ForceSeen307!=1)continue;  // nothing seen, nothing casts
                activeCells[active]=c;activeFar[active]=far;activeNear[active]=near;activeFine[active]=fine;active++;memberCount+=cell.Count;
            }
            classify.Dispose();
            int workers=SerialCollect307||NativePlaneTest307||memberCount<2048?1:Mathf.Clamp(Environment.ProcessorCount-1,1,8);
            if(collectWorkers.Length<workers){var grown=new CollectWorker[workers];for(int w=0;w<workers;w++)grown[w]=w<collectWorkers.Length?collectWorkers[w]:new CollectWorker();collectWorkers=grown;}
            // contiguous ranges of the active cells, about equal member counts each
            long per=(memberCount+workers-1)/workers,acc=0;int wIndex=0;collectWorkers[0].From=0;
            for(int a=0;a<active&&wIndex<workers-1;a++){acc+=cells[activeCells[a]].Count;if(acc>=per*(wIndex+1)){collectWorkers[wIndex].To=a+1;wIndex++;collectWorkers[wIndex].From=a+1;}}
            collectWorkers[wIndex].To=active;for(int w=wIndex+1;w<workers;w++)collectWorkers[w].From=collectWorkers[w].To=active;
            jobEye=eye;jobShadows=shadows;jobLimit=limit;jobCot=cot;CollectWorkers307=workers;
            using(Perf307Markers.ArtTests.Auto())
            {
                if(workers==1)CollectRange(0);
                else{if(collectBody==null)collectBody=CollectRange;System.Threading.Tasks.Parallel.For(0,workers,collectBody);}
            }
            using var replay=Perf307Markers.ArtReplay.Auto();
            for(int w=0;w<workers;w++)
            {
                var wk=collectWorkers[w];VisibleInstances+=wk.Visible;ShadowOnlyInstances+=wk.ShadowOnly;var list=wk.Entries;
                for(int e=0;e<wk.Count;e++){ref var en=ref list[e];var bt=en.Batch;(en.Target==0?bt.On:en.Target==1?bt.ShadowOnly:bt.Off).Add(in en.Matrix,en.Cell,en.Low,en.High,en.Fine);}
            }
        }
        // one worker's cell range: the former serial loop body, writing entries instead of stream adds (no Unity API in here)
        void CollectRange(int w)
        {
            var wk=collectWorkers[w];wk.Count=0;int visible=0,shadowOnlyCount=0;
            var eye=jobEye;bool shadows=jobShadows;float limit=jobLimit,cot=jobCot;
            for(int a=wk.From;a<wk.To;a++)
            {
                int c=activeCells[a];ref var cell=ref cells[c];byte far=activeFar[a],near=activeNear[a];bool fine=activeFine[a];
                for(int k=cell.First,end=cell.First+cell.Count;k<end;k++)
                {
                    int i=members[k];ref var instance=ref instances[i];
                    float x=eye.x-instance.Position.x,z=eye.z-instance.Position.z;float d=Mathf.Sqrt(x*x+z*z);
                    if(d-collectPad>instance.Reach)continue;
                    bool seen;
                    // #307: the same per-plane box test as GeometryUtility.TestPlanesAABB, in C# (no native call per instance)
                    if(d-collectPad>ShadowReach){seen=far==Inside||far==Partial&&(NativePlaneTest307?GeometryUtility.TestPlanesAABB(planes,instance.Bounds):Planes(instance.Bounds.center,instance.Bounds.extents)!=Outside);if(!seen)continue;}
                    else seen=ForceSeen307==1||near==Inside||near==Partial&&(NativePlaneTest307?GeometryUtility.TestPlanesAABB(planes,new Bounds((instance.Low+instance.High)*.5f,instance.High-instance.Low)):Planes((instance.Low+instance.High)*.5f,(instance.High-instance.Low)*.5f)!=Outside);
                    // the caster rule only decides unseen instances (a seen one is On either way)
                    if(!seen&&!Caster(eye,instance,shadows,limit,cot))continue;
                    bool shown=false,shadowOnly=false;
                    foreach(var level in batches[instance.Prototype])foreach(var batch in level)
                    {
                        if(d<batch.Fade.x-instance.Radius-collectPad||d>batch.Fade.w+instance.Radius+collectPad)continue;
                        byte target;
                        if(batch.ShadowProxy){target=1;if(!seen)shadowOnly=true;}
                        else if(batch.Shadows==ShadowCastingMode.Off){if(!seen)continue;target=2;shown=true;}
                        // artpixel 2026-09-30: a seen instance keeps its S3 mode (On). Turning seen casters beyond the limit Off lost visible
                        // far shadows (the ink receivers keep shadows past shadowDistance); tight chunk bounds already drop far chunks per cascade.
                        else if(seen){target=0;shown=true;}
                        else{target=1;shadowOnly=true;}
                        ref var en=ref wk.Next();en.Batch=batch;en.Matrix=batch.LocalIdentity?instance.Matrix:instance.Matrix*batch.Part.Local;en.Low=instance.Low;en.High=instance.High;en.Cell=c;en.Target=target;en.Fine=fine&&target!=2;
                    }
                    if(shown)visible++;else if(shadowOnly)shadowOnlyCount++;
                }
            }
            wk.Visible=visible;wk.ShadowOnly=shadowOnlyCount;
        }
        void FrameProperties(Batch batch,float clock,bool contacts)
        {
            var material=batch.Part.Material;  // an editor tool may swap a part's material without Invalidate(); the former code read it per frame
            if(!ReferenceEquals(material,batch.SoftSource)){batch.SoftSource=material;batch.SoftMaterial=material!=null&&material.HasProperty(ContactSoftId)&&material.GetFloat(ContactSoftId)>.5f;}
            bool soft=contacts&&batch.SoftMaterial;
            if(batch.SoftState==1&&!soft)BaseProperties(batch);  // drop the contact points exactly like the former per-frame Clear()
            var properties=batch.Properties;properties.SetFloat(TimeId,clock);properties.SetFloat(ContactEnabledId,soft?1:0);
            if(soft)properties.SetVectorArray(ContactPointsId,Contacts.BendPoints);
            batch.SoftState=soft?1:0;
        }
        void Submit(Camera camera,bool screen)
        {
            DrawCalls=ShadowOnlyDrawCalls=0;SubmittedTriangles=ShadowOnlyTriangles=0;
            float clock=Application.isPlaying?Time.time:Time.realtimeSinceStartup;
#if UNITY_EDITOR
            if(ContactPreviewClock265>=0)clock=ContactPreviewClock265;
#endif
            bool contactRuntime=Application.isPlaying;
#if UNITY_EDITOR
            contactRuntime|=ContactPreview265;
#endif
            bool contacts=contactRuntime&&Contacts!=null&&Contacts.isActiveAndEnabled;int layer=gameObject.layer;
            submitOrder.Clear();foreach(var prototype in batches)foreach(var level in prototype)foreach(var b in level)submitOrder.Add(b);
#if UNITY_EDITOR
            if(ReverseSubmit307)submitOrder.Reverse();   // diagnostics: same draws, reversed submission (depth-tie sensitivity)
#endif
            foreach(var batch in submitOrder)
            {
                var part=batch.Part;if(part.Mesh==null||part.Material==null)continue;
                if(screen?batch.On.Count+batch.ShadowOnly.Count+batch.Off.Count==0:batch.Legacy.Count==0)continue;
                long triangles=(long)part.Mesh.GetIndexCount(part.Submesh)/3;
#if UNITY_EDITOR
                if(!screen&&LegacySubmit307){LegacyEmit(batch,clock,contacts,layer,camera,triangles);continue;}
#endif
                FrameProperties(batch,clock,contacts);
                // Same arguments as the former DrawMeshInstanced(..., shadows, receiveShadows true, layer, camera, LightProbeUsage.Off).
                var rp=new RenderParams(part.Material){layer=layer,camera=camera,receiveShadows=true,lightProbeUsage=LightProbeUsage.Off,reflectionProbeUsage=ReflectionProbeUsage.BlendProbes,matProps=batch.Properties};  // BlendProbes = the former DrawMeshInstanced default
                if(!screen){Emit(ref rp,part,batch.Legacy,batch.Shadows,triangles,false);continue;}
                Emit(ref rp,part,batch.On,ShadowCastingMode.On,triangles,false);
                Emit(ref rp,part,batch.ShadowOnly,ShadowCastingMode.ShadowsOnly,triangles,true);
                Emit(ref rp,part,batch.Off,ShadowCastingMode.Off,triangles,false);
            }
        }
        void SortStreams(Vector3 eye)
        {
            foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level){SortStream(batch.On,eye);SortStream(batch.Off,eye);}
        }
        // keys share the matrices' indices (Array.Sort(keys, items, index, length)); squared eye distance of the instance origin
        void SortStream(Stream s,Vector3 eye)
        {
            if(s.Count<2)return;
            if(sortKeys.Length<s.Count)sortKeys=new float[Mathf.NextPowerOfTwo(s.Count)];
            float sign=SortChunks307>0?1:-1;
            for(int c=0;c<s.Chunks;c++)
            {
                int first=s.Start[c],end=s.End(c);if(end-first<2)continue;
                for(int i=first;i<end;i++){float x=s.Matrices[i].m03-eye.x,y=s.Matrices[i].m13-eye.y,z=s.Matrices[i].m23-eye.z;sortKeys[i]=sign*(x*x+y*y+z*z);}
                Array.Sort(sortKeys,s.Matrices,first,end-first);
            }
        }
        void Emit(ref RenderParams rp,WorldMacroDressingSheetSO.Part part,Stream stream,ShadowCastingMode mode,long triangles,bool shadowOnly)
        {
            if(stream.Count==0)return;rp.shadowCastingMode=mode;
            if(SkipModes307!=0&&(mode==ShadowCastingMode.ShadowsOnly&&(SkipModes307&1)!=0||mode==ShadowCastingMode.On&&(SkipModes307&2)!=0||mode==ShadowCastingMode.Off&&(SkipModes307&4)!=0))return;
            for(int c=0;c<stream.Chunks;c++)
            {
                int first=stream.Start[c],count=stream.End(c)-first;
                rp.worldBounds=WholeSheetBounds307?Box(sheetLow,sheetHigh):Box(stream.Low[c],stream.High[c]);
                Graphics.RenderMeshInstanced(in rp,part.Mesh,part.Submesh,stream.Matrices,count,first);
#if UNITY_EDITOR
                if(EmitLog307!=null)EmitLog307.Add(new EmitRecord307{Tight=rp.worldBounds,Mode=mode,Part=(part.Mesh!=null?part.Mesh.name:"?")+"/"+(part.Material!=null?part.Material.name:"?"),Count=count});
#endif
                DrawCalls++;SubmittedTriangles+=triangles*count;
                if(shadowOnly){ShadowOnlyDrawCalls++;ShadowOnlyTriangles+=triangles*count;}
            }
        }
        static Bounds Box(Vector3 low,Vector3 high)=>new Bounds((low+high)*.5f,high-low);
#if UNITY_EDITOR
        // #307 diagnostics (PerfSweep307 pickart): every chunk Emit submits, with the box it is drawn with
        public struct EmitRecord307 {public Bounds Tight;public ShadowCastingMode Mode;public string Part;public int Count;}
        [NonSerialized] public List<EmitRecord307> EmitLog307;
#endif
#if UNITY_EDITOR
        // The former Submit body (pre-2a), fed from the S3 list: same packets, arguments and per-frame property values.
        void LegacyEmit(Batch batch,float clock,bool contacts,int layer,Camera camera,long triangles)
        {
            var part=batch.Part;var s=batch.Legacy;if(s.Count==0)return;
            if(legacyProperties==null)legacyProperties=new MaterialPropertyBlock();var properties=legacyProperties;
            properties.Clear();properties.SetFloat(FadeOverrideId,1);properties.SetVector(FadeRangeId,batch.Fade);properties.SetFloat(TimeId,clock);properties.SetFloat(CullRadiusId,0);
            bool soft=contacts&&part.Material.HasProperty(ContactSoftId)&&part.Material.GetFloat(ContactSoftId)>.5f;
            properties.SetFloat(ContactEnabledId,soft?1:0);if(soft)properties.SetVectorArray(ContactPointsId,Contacts.BendPoints);
            for(int offset=0;offset<s.Count;offset+=1023)
            {
                int count=Math.Min(1023,s.Count-offset);Array.Copy(s.Matrices,offset,legacyPacket,0,count);
                Graphics.DrawMeshInstanced(part.Mesh,part.Submesh,part.Material,legacyPacket,count,properties,batch.Shadows,true,layer,camera,LightProbeUsage.Off);
                DrawCalls++;SubmittedTriangles+=triangles*count;
            }
        }
#endif
        static double Milliseconds(long start)=>(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
#if UNITY_EDITOR
        public struct ScanComparison {public int Instances,Cells,Candidates,Batches,Matrices,Mismatches,CulledVisible,FullVisible,BoundsViolations;public long ExpectedDrawCalls,ExpectedTriangles;public double PrepareMs,CulledMs,FullMs;}
        [NonSerialized] List<Matrix4x4>[] comparison;
        // SPEC-ART-RENDERER-CULLING oracle: the culled lists against the former full scan for the same eye and planes.
        // Matrices are compared bit for bit and in order. Nothing is drawn; the lists are left holding the full scan.
        // BoundsViolations (#307 2a): packet matrices whose mesh bounds leave the packet's worldBounds (must be 0).
        public ScanComparison CompareWithFullScan(Camera camera)
        {
            var result=new ScanComparison();long start=Stopwatch.GetTimestamp();
            Prepare();result.PrepareMs=Milliseconds(start);collectedFor=null;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);var eye=camera.transform.position;
            start=Stopwatch.GetTimestamp();Collect(eye);result.CulledMs=Milliseconds(start);result.CulledVisible=VisibleInstances;
            foreach(ulong word in candidates)for(ulong bits=word;bits!=0;bits&=bits-1)result.Candidates++;
            int total=0;foreach(var prototype in batches)foreach(var level in prototype)total+=level.Length;
            if(comparison==null||comparison.Length!=total){comparison=new List<Matrix4x4>[total];for(int i=0;i<total;i++)comparison[i]=new List<Matrix4x4>();}
            int b=0;foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level)
            {
                var copy=comparison[b++];copy.Clear();var s=batch.Legacy;for(int i=0;i<s.Count;i++)copy.Add(s.Matrices[i]);
                result.BoundsViolations+=BoundsViolations(batch.Part,s);
            }
            start=Stopwatch.GetTimestamp();CollectFullScan(eye);result.FullMs=Milliseconds(start);result.FullVisible=VisibleInstances;
            result.Instances=instances.Length;result.Cells=cells.Length;b=0;
            foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level)
            {
                var mine=comparison[b++];var full=batch.Legacy;result.Batches++;
                if(mine.Count!=full.Count)result.Mismatches+=Math.Max(mine.Count,full.Count);
                else for(int i=0;i<mine.Count;i++){result.Matrices++;if(!SameBits(mine[i],full.Matrices[i]))result.Mismatches++;}
                var part=batch.Part;if(part.Mesh==null||part.Material==null||full.Count==0)continue;
                result.ExpectedDrawCalls+=(full.Count+1022)/1023;result.ExpectedTriangles+=(long)part.Mesh.GetIndexCount(part.Submesh)/3*full.Count;
            }
            return result;
        }
        // The pre-culling loop, unchanged; kept only as the comparison reference.
        void CollectFullScan(Vector3 eye)
        {
            ClearStreams();VisibleInstances=ShadowOnlyInstances=0;
            foreach(var instance in instances)
            {
                float x=eye.x-instance.Position.x,z=eye.z-instance.Position.z;float d=Mathf.Sqrt(x*x+z*z);
                if(d>80&&!GeometryUtility.TestPlanesAABB(planes,instance.Bounds))continue;
                bool visible=false;
                foreach(var level in batches[instance.Prototype])foreach(var batch in level)
                {
                    if(d<batch.Fade.x-instance.Radius||d>batch.Fade.w+instance.Radius)continue;
                    batch.Legacy.Add(instance.Matrix*batch.Part.Local,-1,instance.Low,instance.High);visible=true;
                }
                if(visible)VisibleInstances++;
            }
        }
        // Every chunk <= 1023 and every matrix's mesh bounds (what Unity itself would compute) inside the chunk's worldBounds.
        static int BoundsViolations(WorldMacroDressingSheetSO.Part part,Stream stream)
        {
            if(part.Mesh==null||stream.Count==0)return 0;var mesh=part.Mesh.bounds;int bad=0;const float tolerance=1e-3f;
            for(int c=0;c<stream.Chunks;c++)
            {
                int first=stream.Start[c],end=stream.End(c);if(end-first>1023)bad++;var low=stream.Low[c];var high=stream.High[c];
                for(int i=first;i<end;i++)
                {
                    var b=Transform(mesh,stream.Matrices[i]);var l=b.min;var h=b.max;
                    if(l.x<low.x-tolerance||l.y<low.y-tolerance||l.z<low.z-tolerance||h.x>high.x+tolerance||h.y>high.y+tolerance||h.z>high.z+tolerance)bad++;
                }
            }
            return bad;
        }
        // #307 PerfSweep307 census: the current lists per prototype / LOD / part (tab separated: id, lod, mesh, tris, On, ShadowsOnly, Off, proxy)
        public string Census307()
        {
            if(batches==null||Sheet==null)return "";var sb=new System.Text.StringBuilder();
            for(int p=0;p<batches.Length&&p<Sheet.Prototypes.Length;p++)for(int l=0;l<batches[p].Length;l++)for(int m=0;m<batches[p][l].Length;m++)
            {
                var b=batches[p][l][m];var part=b.Part;if(part==null||part.Mesh==null||b.On.Count+b.ShadowOnly.Count+b.Off.Count==0)continue;
                sb.Append(Sheet.Prototypes[p].Id).Append('\t').Append(l).Append('\t').Append(part.Mesh.name).Append('\t').Append(part.Mesh.GetIndexCount(part.Submesh)/3)
                  .Append('\t').Append(b.On.Count).Append('\t').Append(b.ShadowOnly.Count).Append('\t').Append(b.Off.Count).Append('\t').Append(b.ShadowProxy?1:0).Append('\n');
            }
            return sb.ToString();
        }
        public struct ScreenComparison307
        {
            public int Instances,Cells,Batches,Chunks,OptimizedVisible,ReferenceVisible,OptimizedShadowOnly,ReferenceShadowOnly;
            public long On,ShadowOnly,Off,ReferenceEntries,MultisetMismatches,BoundsViolations,ExpectedDrawCalls,ExpectedTriangles,ExpectedShadowOnlyDrawCalls;
            // Subset proof against the S3 full scan, per (instance, batch) the S3 path would submit.
            public long Legacy,Kept,ToShadowOnly,ToOff,DroppedOffscreenNoShadow,DroppedOffscreenBeyondLimit,Unexplained,NotInLegacy;
            public bool Shadows;public float Limit,Cot;public double OptimizedMs,ReferenceMs;public string First;
        }
        // `artcull2`: the optimized S8 lists against a brute-force S8 reference (per instance, sheet order, native view tests only),
        // compared as multisets per batch and shadow mode; then every S3 (full scan) submission is classified: kept, On -> ShadowsOnly
        // (unseen caster), On -> Off (seen, beyond the shadow limit) or dropped (unseen and non-shadow / beyond the limit); each reason is
        // re-checked with an independent 8-corner plane test and limit formula, anything else is Unexplained; a S8 submission the S3
        // path never made is NotInLegacy. limitOverride: NaN = the camera's live pipeline, <= 0 = shadows off, > 0 = that distance.
        // Nothing is drawn; the lists are left holding the reference.
        public ScreenComparison307 CompareScreenIdentical(Camera camera,float limitOverride=float.NaN)
        {
            var result=new ScreenComparison307();Prepare();collectedFor=null;
            ShadowLimits(camera,out bool shadows,out float limit,out float cot);
            if(!float.IsNaN(limitOverride)){shadows=limitOverride>0;limit=Mathf.Max(0,limitOverride);}
            result.Shadows=shadows;result.Limit=limit;result.Cot=cot;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);var eye=camera.transform.position;
            long start=Stopwatch.GetTimestamp();CollectScreen(eye,shadows,limit,cot);result.OptimizedMs=Milliseconds(start);
            result.OptimizedVisible=VisibleInstances;result.OptimizedShadowOnly=ShadowOnlyInstances;
            var optimized=new List<Matrix4x4[]>();
            foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level)
            {
                result.Batches++;
                foreach(var s in new[]{batch.On,batch.ShadowOnly,batch.Off})
                {
                    optimized.Add(Sorted(s));result.Chunks+=s.Chunks;result.BoundsViolations+=BoundsViolations(batch.Part,s);
                    var part=batch.Part;if(part.Mesh==null||part.Material==null||s.Count==0)continue;
                    result.ExpectedDrawCalls+=s.Chunks;result.ExpectedTriangles+=(long)part.Mesh.GetIndexCount(part.Submesh)/3*s.Count;
                    if(s==batch.ShadowOnly)result.ExpectedShadowOnlyDrawCalls+=s.Chunks;
                }
                result.On+=batch.On.Count;result.ShadowOnly+=batch.ShadowOnly.Count;result.Off+=batch.Off.Count;
            }
            start=Stopwatch.GetTimestamp();ReferenceScreen(eye,shadows,limit,cot,ref result);result.ReferenceMs=Milliseconds(start);
            result.Instances=instances.Length;result.Cells=cells.Length;int o=0;
            foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level)foreach(var s in new[]{batch.On,batch.ShadowOnly,batch.Off})
            {
                var mine=optimized[o++];var reference=Sorted(s);result.ReferenceEntries+=reference.Length;
                if(mine.Length!=reference.Length){result.MultisetMismatches+=Math.Max(mine.Length,reference.Length);if(result.First==null)result.First="multiset size "+mine.Length+"/"+reference.Length+" in "+Describe(batch,s);continue;}
                for(int i=0;i<mine.Length;i++)if(!SameBits(mine[i],reference[i])){result.MultisetMismatches++;if(result.First==null)result.First="multiset value in "+Describe(batch,s);}
            }
            return result;
        }
        string Describe(Batch batch,Stream s)
        {
            foreach(var prototype in Sheet.Prototypes)for(int l=0;l<prototype.Lods.Length;l++)for(int m=0;m<prototype.Lods[l].Parts.Length;m++)
                if(prototype.Lods[l].Parts[m]==batch.Part||prototype.Lods[l].Parts[m]==batch.Origin)return prototype.Id+" lod "+l+" part "+m+" "+(s==batch.On?"On":s==batch.ShadowOnly?"ShadowsOnly":"Off");
            return "?";
        }
        static Matrix4x4[] Sorted(Stream s)
        {
            var copy=new Matrix4x4[s.Count];Array.Copy(s.Matrices,copy,s.Count);
            Array.Sort(copy,(a,b)=>{for(int i=0;i<16;i++){int x=BitConverter.SingleToInt32Bits(a[i]),y=BitConverter.SingleToInt32Bits(b[i]);if(x!=y)return x<y?-1:1;}return 0;});
            return copy;
        }
        // Brute force S8 rules (no cells) + the S3 full-scan rules side by side, per instance in sheet order.
        void ReferenceScreen(Vector3 eye,bool shadows,float limit,float cot,ref ScreenComparison307 r)
        {
            ClearStreams();VisibleInstances=ShadowOnlyInstances=0;
            for(int i=0;i<instances.Length;i++)
            {
                ref var instance=ref instances[i];
                float x=eye.x-instance.Position.x,z=eye.z-instance.Position.z;float d=Mathf.Sqrt(x*x+z*z);
                bool farSeen=GeometryUtility.TestPlanesAABB(planes,instance.Bounds);
                bool legacy=!(d>80&&!farSeen);
                bool seen=d>80?farSeen:GeometryUtility.TestPlanesAABB(planes,new Bounds((instance.Low+instance.High)*.5f,instance.High-instance.Low));
                bool caster=Caster(eye,instance,shadows,limit,cot);
                // Independent evidence for the reasons: 8-corner plane separation and the limit written out again.
                bool offscreen=d>80?OutsideCorners(instance.Bounds.min,instance.Bounds.max):OutsideCorners(instance.Low,instance.High);
                float bx=Mathf.Max(0,Mathf.Max(instance.Low.x-eye.x,eye.x-instance.High.x)),bz=Mathf.Max(0,Mathf.Max(instance.Low.z-eye.z,eye.z-instance.High.z));
                float margin=Mathf.Max(Sheet.ShadowPunctualReach307,Mathf.Clamp((instance.High.y-instance.Position.y+Sheet.ShadowReliefAllowance307)*cot,Sheet.ShadowCasterMarginClamp307.x,Sheet.ShadowCasterMarginClamp307.y));
                bool casts=shadows&&Mathf.Sqrt(bx*bx+bz*bz)<=limit+margin;
                bool shown=false,shadowOnly=false;
                foreach(var level in batches[instance.Prototype])foreach(var batch in level)
                {
                    bool inRange=!(d<batch.Fade.x-instance.Radius||d>batch.Fade.w+instance.Radius);if(!inRange)continue;
                    bool on=batch.Shadows!=ShadowCastingMode.Off;Stream target=null;
                    if(!on){if(seen)target=batch.Off;}
                    else if(seen)target=caster?batch.On:batch.Off;
                    else if(caster&&d<=ShadowReach)target=batch.ShadowOnly;
                    if(target!=null){target.Add(instance.Matrix*batch.Part.Local,-1,instance.Low,instance.High);if(target==batch.ShadowOnly)shadowOnly=true;else shown=true;}
                    if(!legacy){if(target!=null){r.NotInLegacy++;if(r.First==null)r.First="not in S3: instance "+i+" "+Describe(batch,target);}continue;}
                    r.Legacy++;bool ok;
                    if(target==batch.Legacy){r.Kept++;ok=true;}
                    else if(target==batch.ShadowOnly){ok=offscreen&&casts&&d<=ShadowReach;if(ok)r.ToShadowOnly++;}
                    else if(target==batch.Off){ok=on&&!offscreen&&!casts;if(ok)r.ToOff++;}
                    else
                    {
                        ok=offscreen&&(!on||!casts||d>ShadowReach);
                        if(ok){if(on)r.DroppedOffscreenBeyondLimit++;else r.DroppedOffscreenNoShadow++;}
                    }
                    if(!ok){r.Unexplained++;if(r.First==null)r.First="unexplained: instance "+i+" d="+d.ToString("F2")+" seen="+seen+" caster="+caster+" offscreen="+offscreen+" casts="+casts+" "+Describe(batch,batch.Legacy);}
                }
                if(shown)VisibleInstances++;else if(shadowOnly)ShadowOnlyInstances++;
            }
            r.ReferenceVisible=VisibleInstances;r.ReferenceShadowOnly=ShadowOnlyInstances;
        }
        bool OutsideCorners(Vector3 low,Vector3 high)
        {
            for(int k=0;k<planes.Length;k++)
            {
                bool all=true;
                for(int c=0;c<8&&all;c++)
                {
                    var p=new Vector3((c&1)!=0?high.x:low.x,(c&2)!=0?high.y:low.y,(c&4)!=0?high.z:low.z);
                    if(planes[k].GetDistanceToPoint(p)>=0)all=false;
                }
                if(all)return true;
            }
            return false;
        }
#endif
    }
}
