using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.Data.World;

namespace Oheangbu.App.World.Dressing
{
    public sealed partial class WorldMacroDressingRenderer
    {
        [Header("Budgeted dense streaming")]
        public bool BudgetedDenseStreaming = true;
        public bool AllowDiagnosticCameras;
        [Range(.1f,4f)] public float GenerationBudgetMilliseconds = 1f;
        [Range(.1f,4f)] public float PacketBudgetMilliseconds = .75f;
        public float LastCollisionMilliseconds, LastPacketMilliseconds, LastSubmissionMilliseconds;
        // Preserve nearby density/LOD0. Replace the costly middle-distance tree meshes
        // with their existing cards earlier; keep the same positions and far silhouette.
        float StreamingTreeMiddle => Mathf.Min(Sheet.TreeMiddle,110f);
        public int StreamingChunks, PendingChunks, PacketRebuilds, StreamCandidates;
        public long GenerationAllocatedBytes, PacketAllocatedBytes, SubmissionAllocatedBytes;
        public readonly int[] ResidentByLayer = new int[7];
        bool UseStreaming => Sheet != null && Sheet.DenseVegetation && BudgetedDenseStreaming;
        sealed class StreamChunk
        {
            public long Key;
            public int Layer;
            public WorldMacroDressingSheetSO.Cell Cell;
            public Bounds Bounds;
            public IEnumerator<Item?> Generator;
            public readonly List<Item> Items = new List<Item>();
            public readonly Dictionary<int,Batch> Batches = new Dictionary<int,Batch>();
            public bool Complete, PacketsReady;
            public int PacketViewKey;
            public float Priority;
            public StreamChunk RenderGroup;
            public List<StreamChunk> Members;
            public int Revision, BuiltRevision = -1;
        }
        readonly Dictionary<long,StreamChunk> streamChunks = new Dictionary<long,StreamChunk>();
        readonly Dictionary<long,StreamChunk> streamGroups = new Dictionary<long,StreamChunk>();
        readonly List<StreamChunk> streamJobs = new List<StreamChunk>();
        readonly List<StreamChunk> streamVisible = new List<StreamChunk>();
        readonly List<long> streamRemoval = new List<long>();
        readonly HashSet<int> streamCells = new HashSet<int>();
        readonly List<RenderCostRow> streamingCostRows = new List<RenderCostRow>();
        Vector3 streamingPreparedEye = new Vector3(float.PositiveInfinity,0,0);
        int streamJobCursor;
        Vector3 streamSortEye;
        Comparison<StreamChunk> streamDistanceComparison;
        static float ElapsedMs(long start) => (float)((System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency);
        void ResetStreaming()
        {
            foreach(var chunk in streamChunks.Values)chunk.Generator?.Dispose();
            streamChunks.Clear();streamGroups.Clear();streamJobs.Clear();streamVisible.Clear();streamRemoval.Clear();streamCells.Clear();streamingCostRows.Clear();
            streamingPreparedEye=new Vector3(float.PositiveInfinity,0,0);streamJobCursor=0;
            StreamingChunks=PendingChunks=PacketRebuilds=StreamCandidates=0;
            Array.Clear(ResidentByLayer,0,ResidentByLayer.Length);
        }
        float StreamRange(int layer) => layer==0?Sheet.ForestDistance:layer==1?Sheet.ShrubDistance:layer==2?350:
            layer==3?Sheet.GrassDistance:layer==4?Sheet.LowGrassDistance:layer==5?Sheet.GroundCoverDistance:350;
        static float HorizontalBoundsDistance(Bounds b,Vector3 eye)
        {
            float x=Mathf.Max(0,Mathf.Abs(eye.x-b.center.x)-b.extents.x);
            float z=Mathf.Max(0,Mathf.Abs(eye.z-b.center.z)-b.extents.z);
            return Mathf.Sqrt(x*x+z*z);
        }
        static long StreamKey(WorldMacroDressingSheetSO.Cell cell,int layer,int sub)
            => ((long)(ushort)cell.X<<40)|((long)(ushort)cell.Z<<24)|((long)layer<<16)|(uint)sub;
        void PrepareStreaming(Vector3 eye,int requestedBudget,Action guard)
        {
            long allocated=GC.GetAllocatedBytesForCurrentThread(),started=System.Diagnostics.Stopwatch.GetTimestamp();StreamCandidates=0;
            if(FlatDistanceSquared(streamingPreparedEye,eye)>=256)
            {
                streamingPreparedEye=eye;RefreshStreamJobs(eye);
            }
            // Explicit authoring prewarm calls are outside gameplay. Ordinary Editor previews
            // still use the small budget; a Play call cannot bypass the real time cap.
            float budget=!Application.isPlaying&&requestedBudget>32?40:Mathf.Max(.1f,GenerationBudgetMilliseconds);
            while(streamJobCursor<streamJobs.Count&&ElapsedMs(started)<budget)
            {
                var job=streamJobs[streamJobCursor];
                if(job.Complete){streamJobCursor++;continue;}
                if(job.Generator==null){job.Complete=true;streamJobCursor++;continue;}
                guard?.Invoke();
                for(int i=0;i<1;i++)
                {
                    if(!job.Generator.MoveNext())
                    {
                        job.Generator.Dispose();job.Generator=null;job.Complete=true;streamJobCursor++;
                        ResidentInstances+=job.Items.Count;ResidentByLayer[job.Layer]+=job.Items.Count;if(job.RenderGroup!=null)job.RenderGroup.Revision++;break;
                    }
                    StreamCandidates++;
                    if(job.Generator.Current.HasValue)job.Items.Add(job.Generator.Current.Value);
                }
            }
            PendingChunks=streamJobs.Count-streamJobCursor;StreamingChunks=streamChunks.Count;
            LastGenerationMilliseconds=ElapsedMs(started);
            GenerationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        }
        void RefreshStreamJobs(Vector3 eye)
        {
            streamRemoval.Clear();
            foreach(var pair in streamChunks)
                if(HorizontalBoundsDistance(pair.Value.Bounds,eye)>StreamRange(pair.Value.Layer)+96)streamRemoval.Add(pair.Key);
            foreach(long key in streamRemoval)
            {
                var c=streamChunks[key];c.Generator?.Dispose();
                if(c.Complete){ResidentInstances-=c.Items.Count;ResidentByLayer[c.Layer]-=c.Items.Count;}
                if(c.RenderGroup!=null) { c.RenderGroup.Members.Remove(c); c.RenderGroup.Revision++; if(c.RenderGroup.Members.Count==0)streamGroups.Remove(c.RenderGroup.Key); }
                else streamGroups.Remove(c.Key);
                streamChunks.Remove(key); // releases packet buffers with the cell; no unbounded pool
            }
            foreach(var cell in Sheet.Cells)
            {
                var origin=Origin(cell);
                var bounds=SurfaceBounds(new Bounds(new Vector3(origin.x+128,(cell.MinHeight+cell.MaxHeight)*.5f+20,origin.y+128),new Vector3(256,cell.MaxHeight-cell.MinHeight+80,256)));
                float distance=HorizontalBoundsDistance(bounds,eye);
                if(distance>Sheet.ForestDistance+64)continue;
                AddChunk(cell,0,0,origin,256,bounds,eye);
                if(distance<=350+64)AddChunk(cell,6,0,origin,256,bounds,eye);
                for(int layer=1;layer<=5;layer++)
                {
                    if(layer==4&&!Sheet.LowGrassInfill)continue;
                    if(distance>StreamRange(layer)+64)continue;
                    for(int z=0;z<4;z++)for(int x=0;x<4;x++)
                    {
                        Vector2 min=origin+new Vector2(x*64,z*64);
                        var b=new Bounds(new Vector3(min.x+32,bounds.center.y,min.y+32),new Vector3(64,bounds.size.y,64));
                        if(HorizontalBoundsDistance(b,eye)<=StreamRange(layer)+64)AddChunk(cell,layer,z*4+x,min,64,b,eye);
                    }
                }
            }
            streamJobs.Clear();streamCells.Clear();
            foreach(var chunk in streamChunks.Values)
            {
                streamCells.Add(Key(chunk.Cell.X,chunk.Cell.Z));
                if(chunk.Complete)continue;
                // Collision-bearing nearby trees/rocks precede cosmetics, then near grass.
                float d=HorizontalBoundsDistance(chunk.Bounds,eye);
                chunk.Priority=d+(chunk.Layer==0? -30:chunk.Layer==2? -15:chunk.Layer==5?200:0);
                streamJobs.Add(chunk);
            }
            streamJobs.Sort((a,b)=>{int n=a.Priority.CompareTo(b.Priority);return n!=0?n:a.Key.CompareTo(b.Key);});
            streamJobCursor=0;ResidentCells=streamCells.Count;
        }
        void AddChunk(WorldMacroDressingSheetSO.Cell cell,int layer,int sub,Vector2 min,float size,Bounds bounds,Vector3 eye)
        {
            long key=StreamKey(cell,layer,sub);if(streamChunks.ContainsKey(key))return;
            var chunk=new StreamChunk{Key=key,Cell=cell,Layer=layer,Bounds=bounds};
            if(layer==6)
            {
                chunk.Items.AddRange(Fixed(cell));chunk.Complete=true;ResidentInstances+=chunk.Items.Count;ResidentByLayer[layer]+=chunk.Items.Count;
            }
            else
            {
                int category=layer==0?0:layer==1?1:layer==2?3:2;
                chunk.Generator=GenerateSteps(cell,category,layer==0,layer==5,layer==4,min,min+Vector2.one*size).GetEnumerator();
            }
            streamChunks.Add(key,chunk);
            if(layer==0||layer==6)streamGroups.Add(key,chunk);
            else
            {
                int gx=(sub%4)/2,gz=(sub/4)/2;
                long groupKey=StreamKey(cell,layer,gz*2+gx)|long.MinValue;
                if(!streamGroups.TryGetValue(groupKey,out var group))
                {
                    Vector2 origin=Origin(cell)+new Vector2(gx*128,gz*128);
                    group=new StreamChunk{Key=groupKey,Cell=cell,Layer=layer,Complete=true,Members=new List<StreamChunk>(4),
                        Bounds=new Bounds(new Vector3(origin.x+64,bounds.center.y,origin.y+64),new Vector3(128,bounds.size.y,128))};
                    streamGroups.Add(groupKey,group);
                }
                chunk.RenderGroup=group;group.Members.Add(chunk);group.Revision++;
            }
        }
        int PacketViewKey(StreamChunk chunk,Vector3 eye)
        {
            float d=FlatDistance(chunk.Bounds.center,eye),r=chunk.Bounds.extents.magnitude+24;
            // Entire distant tree chunks remain the same LOD under camera rotation and movement.
            if(chunk.Layer==0)
            {
                if(d-r>Sheet.TreeDistance+100)return int.MaxValue;
                if(d-r>StreamingTreeMiddle+40&&d+r<Sheet.TreeDistance-120)return int.MaxValue-1;
            }
            return Key(Mathf.FloorToInt(eye.x/8),Mathf.FloorToInt(eye.z/8));
        }
        void DrawStreaming(Camera camera)
        {
            Vector3 eye=camera.transform.position;GeometryUtility.CalculateFrustumPlanes(camera,planes);
            streamVisible.Clear();DrawCalls=DrawnInstances=PacketRebuilds=0;
            foreach(var c in streamGroups.Values)
            {
                if(!c.Complete||(c.Members==null&&c.Items.Count==0)||HorizontalBoundsDistance(c.Bounds,eye)>StreamRange(c.Layer)+24)continue;
                var bounds=c.Bounds;bounds.Expand(48);
                if(GeometryUtility.TestPlanesAABB(planes,bounds))streamVisible.Add(c);
            }
            streamSortEye=eye;
            if(streamDistanceComparison==null)streamDistanceComparison=(a,b)=>HorizontalBoundsDistance(a.Bounds,streamSortEye).CompareTo(HorizontalBoundsDistance(b.Bounds,streamSortEye));
            streamVisible.Sort(streamDistanceComparison);
            long allocated=GC.GetAllocatedBytesForCurrentThread(),buildStart=System.Diagnostics.Stopwatch.GetTimestamp();
            foreach(var chunk in streamVisible)
            {
                int key=PacketViewKey(chunk,eye);
                if(chunk.PacketsReady&&chunk.PacketViewKey==key&&chunk.BuiltRevision==chunk.Revision&&!DiagnosticRebuildEachFrame)continue;
                if(ElapsedMs(buildStart)>=PacketBudgetMilliseconds)break;
                BuildStreamPackets(chunk,eye);chunk.PacketViewKey=key;chunk.PacketsReady=true;chunk.BuiltRevision=chunk.Revision;PacketRebuilds++;
            }
            LastPacketMilliseconds=ElapsedMs(buildStart);
            PacketAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            allocated=GC.GetAllocatedBytesForCurrentThread();
            long submitStart=System.Diagnostics.Stopwatch.GetTimestamp();
            if(block==null)block=new MaterialPropertyBlock();block.Clear();block.SetFloat(TimeId,Application.isPlaying?Time.time:Time.realtimeSinceStartup);
            foreach(var chunk in streamVisible)
            {
                if(!chunk.PacketsReady)continue;
                foreach(var b in chunk.Batches.Values)
                {
                    block.SetFloat("_DressingFadeOverride",b.OverrideFade?1:0);block.SetVector("_DressingFadeRange",b.Fade);
                    block.SetFloat("_DressingCullRadius",b.CullRadius);
                    for(int n=0,left=b.Matrices.Count;left>0;n++,left-=1023)
                    {Graphics.DrawMeshInstanced(b.Mesh,b.Submesh,b.Material,b.Packets[n],Mathf.Min(1023,left),block,b.Shadows,true,gameObject.layer,camera,LightProbeUsage.Off);DrawCalls++;}
                    DrawnInstances+=b.Matrices.Count;
                }
            }
            LastSubmissionMilliseconds=ElapsedMs(submitStart);
            SubmissionAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            if(camera==Observer){ObserverDrawCalls=DrawCalls;ObserverDrawnInstances=DrawnInstances;}
        }
        void BuildStreamPackets(StreamChunk chunk,Vector3 eye)
        {
            foreach(var b in chunk.Batches.Values){b.Matrices.Clear();b.CullRadius=0;}
            if(chunk.Members==null)AppendStreamItems(chunk,chunk.Items,eye);
            else foreach(var member in chunk.Members)if(member.Complete)AppendStreamItems(chunk,member.Items,eye);
            foreach(var b in chunk.Batches.Values)
            {
                int needed=(b.Matrices.Count+1022)/1023;
                while(b.Packets.Count<needed)b.Packets.Add(new Matrix4x4[1023]);
                if(b.Packets.Count>needed+1)b.Packets.RemoveRange(needed,b.Packets.Count-needed);
                for(int i=0;i<needed;i++)b.Matrices.CopyTo(i*1023,b.Packets[i],0,Mathf.Min(1023,b.Matrices.Count-i*1023));
            }
        }
        void AppendStreamItems(StreamChunk chunk,List<Item> items,Vector3 eye)
        {
            foreach(var item in items)
            {
                var p=Sheet.Prototypes[item.Prototype];int kind=(int)p.Category;
                float d=FlatDistance(item.Position,eye),radius=Mathf.Max(p.Size.x,p.Size.z)*item.Scale*.5f+12;
                if(d>StreamRange(chunk.Layer)+radius)continue;
                if(chunk.Layer==5){if(d>Sheet.GrassDistance-40-radius)AddStreamPart(chunk,item,2,eye);continue;}
                if(kind==0)
                {
                    if(d<Sheet.TreeNear+10+radius)AddStreamPart(chunk,item,0,eye);
                    if(d>Sheet.TreeNear-10-radius&&d<StreamingTreeMiddle+20+radius)AddStreamPart(chunk,item,1,eye);
                    if(d>StreamingTreeMiddle-20-radius&&d<Sheet.TreeDistance+radius)AddStreamPart(chunk,item,2,eye);
                    if(d>Sheet.TreeDistance-100-radius)AddStreamPart(chunk,item,3,eye);
                }
                else if(kind==1&&p.Lods.Length>=3)
                {
                    float a=ShrubNearEnd(p),b=ShrubCardStart(p);
                    if(d<a+4+radius)AddStreamPart(chunk,item,0,eye);
                    if(d>a-4-radius&&d<b+5+radius)AddStreamPart(chunk,item,1,eye);
                    if(d>b-5-radius)AddStreamPart(chunk,item,2,eye);
                }
                else if(kind==2&&p.GroundPatch)
                {float a=GrassMeshEnd(p,true);if(d<a+4+radius)AddStreamPart(chunk,item,0,eye);if(d>a-4-radius)AddStreamPart(chunk,item,1,eye);}
                else AddStreamPart(chunk,item,d<75?0:1,eye);
            }
        }
        void AddStreamPart(StreamChunk chunk,Item item,int lod,Vector3 eye)
        {
            var p=Sheet.Prototypes[item.Prototype];if(p.Lods.Length==0)return;lod=Mathf.Clamp(lod,0,p.Lods.Length-1);int kind=(int)p.Category;
            var shadow=lod==0&&kind!=2&&FlatDistanceSquared(item.Position,eye)<ShadowDistance(kind)*ShadowDistance(kind)?ShadowCastingMode.On:ShadowCastingMode.Off;
            var parts=p.Lods[lod].Parts;
            for(int i=0;i<parts.Length;i++)
            {
                var part=parts[i];if(part.Mesh==null||part.Material==null)continue;
                int key=(item.Prototype*1000+lod*50+i)*4+(int)shadow;
                if(!chunk.Batches.TryGetValue(key,out var batch))
                {
                    batch=new Batch{Mesh=part.Mesh,Material=part.Material,Submesh=part.Submesh,Prototype=item.Prototype,Lod=lod,Kind=kind,Shadows=shadow,Triangles=(long)part.Mesh.GetIndexCount(part.Submesh)/3};
                    batch.OverrideFade=(kind==1&&p.Lods.Length>=3)||(kind==2&&p.GroundPatch);if(batch.OverrideFade)batch.Fade=DenseFade(p,lod,shadow);
                    if(kind==0){batch.OverrideFade=true;batch.Fade=lod==0?new Vector4(-1,0,Sheet.TreeNear-10,Sheet.TreeNear+10):lod==1?new Vector4(Sheet.TreeNear-10,Sheet.TreeNear+10,StreamingTreeMiddle-20,StreamingTreeMiddle+20):lod==2?new Vector4(StreamingTreeMiddle-20,StreamingTreeMiddle+20,Sheet.TreeDistance-100,Sheet.TreeDistance+100):new Vector4(Sheet.TreeDistance-100,Sheet.TreeDistance+100,Sheet.ForestDistance-200,Sheet.ForestDistance);}
                    chunk.Batches.Add(key,batch);
                }
                Matrix4x4 matrix=item.Matrix*part.Local;
                batch.Matrices.Add(matrix);
                float scale=Mathf.Max(matrix.GetColumn(0).magnitude,Mathf.Max(matrix.GetColumn(1).magnitude,matrix.GetColumn(2).magnitude));
                batch.CullRadius=Mathf.Max(batch.CullRadius,(part.Mesh.bounds.center.magnitude+part.Mesh.bounds.extents.magnitude)*scale+.5f);
            }
        }
        void CollectStreamingCollision(Vector3 start,Vector3 end,float radius,List<Item> output)
        {
            foreach(var c in streamChunks.Values)
            {
                if(!c.Complete||(c.Layer!=0&&c.Layer!=2&&c.Layer!=6)||SegmentDistance(c.Bounds.center,start,end)>radius+c.Bounds.extents.magnitude)continue;
                foreach(var item in c.Items)
                {
                    var p=Sheet.Prototypes[item.Prototype];
                    if(p.Category!=WorldMacroDressingSheetSO.Kind.Tree&&p.Category!=WorldMacroDressingSheetSO.Kind.Rock&&p.Category!=WorldMacroDressingSheetSO.Kind.Prop)continue;
                    if(SegmentDistance(item.Position,start,end)<radius+p.Radius*item.Scale)output.Add(item);
                }
            }
        }
        public bool StreamingEnabled=>UseStreaming;
    }
}
