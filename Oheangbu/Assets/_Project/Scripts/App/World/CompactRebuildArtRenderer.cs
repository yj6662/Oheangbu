using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch=System.Diagnostics.Stopwatch;

namespace Oheangbu.App.World
{
    // Fixed, authored placements; no streaming or global renderer settings are changed.
    // SPEC-ART-RENDERER-CULLING: 64 m cells skip groups whose outcome is already known, then the unchanged per-instance
    // rules run in sheet order, so every batch receives exactly the matrices (and order) of the former full scan.
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
        public int DrawCalls {get;private set;}
        public int VisibleInstances {get;private set;}
        public long SubmittedTriangles {get;private set;}
        public double LastCpuMs {get;private set;}
        sealed class Batch
        {
            public WorldMacroDressingSheetSO.Part Part;
            public readonly List<Matrix4x4> Matrices=new List<Matrix4x4>(512);
            public Vector4 Fade;
            public ShadowCastingMode Shadows;
        }
        // Reach: farthest distance at which any batch of the prototype can still take the instance.
        struct Instance {public int Prototype;public Matrix4x4 Matrix;public Bounds Bounds;public Vector3 Position;public float Radius,Reach;}
        // XZ rectangle of member positions, padded union of member bounds, largest member reach.
        struct Cell {public float MinX,MinZ,MaxX,MaxZ,Reach;public Vector3 Centre,Extent;public int First,Count;public bool Unbounded;}
        const float CellSize=64,ShadowReach=80,Slack=1;
        const byte Rejected=0,Outside=1,Partial=2,Inside=3;
        static readonly int FadeOverrideId=Shader.PropertyToID("_DressingFadeOverride"),FadeRangeId=Shader.PropertyToID("_DressingFadeRange"),
            TimeId=Shader.PropertyToID("_DressingTime"),CullRadiusId=Shader.PropertyToID("_DressingCullRadius"),
            ContactSoftId=Shader.PropertyToID("_ContactSoft265"),ContactEnabledId=Shader.PropertyToID("_ContactEnabled265"),ContactPointsId=Shader.PropertyToID("_ContactPoints265");
        // Bit index of an isolated low bit (de Bruijn 0x03F79D71B4CB0A89).
        static readonly byte[] LowBit={0,1,48,2,57,49,28,3,61,58,50,42,38,29,17,4,62,55,59,36,53,51,43,22,45,39,33,30,24,18,12,5,63,47,56,27,60,41,37,16,54,35,52,21,44,32,23,11,46,26,40,15,34,20,31,10,25,14,19,9,13,8,7,6};
        Instance[] instances=Array.Empty<Instance>();
        Cell[] cells=Array.Empty<Cell>();
        int[] members=Array.Empty<int>(),cellOf=Array.Empty<int>();
        byte[] cellState=Array.Empty<byte>();
        ulong[] candidates=Array.Empty<ulong>();
        Batch[][][] batches;
        WorldMacroDressingSheetSO loaded;
        readonly Matrix4x4[] packet=new Matrix4x4[1023];
        readonly Plane[] planes=new Plane[6];
        MaterialPropertyBlock properties;
        void OnEnable(){loaded=null;RenderPipelineManager.beginCameraRendering+=Draw;}
        void OnDisable(){RenderPipelineManager.beginCameraRendering-=Draw;Release();}
        void OnValidate(){loaded=null;}
        public void Invalidate(){loaded=null;}
        void Release(){instances=Array.Empty<Instance>();cells=Array.Empty<Cell>();members=cellOf=Array.Empty<int>();cellState=Array.Empty<byte>();candidates=Array.Empty<ulong>();batches=null;loaded=null;}
        void Prepare()
        {
            if(loaded==Sheet&&batches!=null)return;
            loaded=Sheet;var lookup=new Dictionary<string,int>();batches=new Batch[Sheet.Prototypes.Length][][];
            for(int p=0;p<Sheet.Prototypes.Length;p++)
            {
                var prototype=Sheet.Prototypes[p];lookup.Add(prototype.Id,p);batches[p]=new Batch[prototype.Lods.Length][];
                for(int l=0;l<prototype.Lods.Length;l++)
                {
                    var parts=prototype.Lods[l].Parts;batches[p][l]=new Batch[parts.Length];
                    var fade=Range(prototype.Category,l,prototype.Lods.Length);
                    for(int m=0;m<parts.Length;m++)batches[p][l][m]=new Batch{Part=parts[m],Fade=fade,
                        Shadows=prototype.Category==WorldMacroDressingSheetSO.Kind.Grass||l>=2?ShadowCastingMode.Off:ShadowCastingMode.On};
                }
            }
            var accepted=new List<Instance>();
            foreach(var p in Sheet.FixedPlacements)
            {
                if(!lookup.TryGetValue(p.PrototypeId,out int index))continue;
                var size=Sheet.Prototypes[index].Size*p.Scale;float radius=Mathf.Max(size.x,size.z)*.75f;
                accepted.Add(new Instance{Prototype=index,Position=p.Position,Radius=radius,Reach=ReachOf(batches[index],radius),Bounds=new Bounds(p.Position+Vector3.up*size.y*.5f,new Vector3(radius*2,Mathf.Max(size.y,.2f),radius*2)),Matrix=Matrix4x4.TRS(p.Position,Quaternion.Euler(p.Euler),Vector3.one*p.Scale)});
            }
            instances=accepted.ToArray();properties=new MaterialPropertyBlock();
            BuildCells();
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
                var d=Vector3.down*(gap+.05f);it.Position+=d;it.Bounds.center+=d;it.Matrix=Matrix4x4.Translate(d)*it.Matrix;moved++;
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
        // Members stay in ascending sheet order. Non-finite data goes to one cell that is never rejected.
        void BuildCells()
        {
            int count=instances.Length;cellOf=new int[count];var index=new Dictionary<long,int>();var sizes=new List<int>();
            for(int i=0;i<count;i++)
            {
                ref var instance=ref instances[i];
                bool bounded=Finite(instance.Position)&&Finite(instance.Radius)&&!float.IsNaN(instance.Reach)&&Finite(instance.Bounds.center)&&Finite(instance.Bounds.extents)
                    &&Mathf.Abs(instance.Position.x)<1e7f&&Mathf.Abs(instance.Position.z)<1e7f;
                long key=bounded?((long)Mathf.FloorToInt(instance.Position.x/CellSize)<<32)^(uint)Mathf.FloorToInt(instance.Position.z/CellSize):long.MinValue;
                if(!index.TryGetValue(key,out int cell)){cell=sizes.Count;index.Add(key,cell);sizes.Add(0);}
                cellOf[i]=cell;sizes[cell]++;
            }
            cells=new Cell[sizes.Count];members=new int[count];cellState=new byte[cells.Length];candidates=new ulong[(count+63)>>6];
            var low=new Vector3[cells.Length];var high=new Vector3[cells.Length];
            for(int c=0,first=0;c<cells.Length;first+=sizes[c],c++)
            {
                cells[c]=new Cell{First=first,MinX=float.PositiveInfinity,MinZ=float.PositiveInfinity,MaxX=float.NegativeInfinity,MaxZ=float.NegativeInfinity,Reach=float.NegativeInfinity};
                low[c]=Vector3.positiveInfinity;high[c]=Vector3.negativeInfinity;
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
            }
            for(int c=0;c<cells.Length;c++){cells[c].Centre=(low[c]+high[c])*.5f;cells[c].Extent=(high[c]-low[c])*.5f+Vector3.one*Slack;}
        }
        Vector4 Range(WorldMacroDressingSheetSO.Kind kind,int lod,int count)
        {
            if(kind==WorldMacroDressingSheetSO.Kind.Tree)
                return lod==0?new Vector4(-1,0,Sheet.TreeNear-10,Sheet.TreeNear+10):lod==1?new Vector4(Sheet.TreeNear-10,Sheet.TreeNear+10,Sheet.TreeMiddle-20,Sheet.TreeMiddle+20):new Vector4(Sheet.TreeMiddle-20,Sheet.TreeMiddle+20,Sheet.ForestDistance-100,Sheet.ForestDistance);
            float limit=kind==WorldMacroDressingSheetSO.Kind.Grass?Sheet.GrassDistance:kind==WorldMacroDressingSheetSO.Kind.Shrub?Sheet.ShrubDistance:700;
            float change=kind==WorldMacroDressingSheetSO.Kind.Grass?Sheet.GrassMeshDistance:kind==WorldMacroDressingSheetSO.Kind.Shrub?35:65;
            return count==1?new Vector4(-1,0,limit-20,limit):lod==0?new Vector4(-1,0,change-7,change+7):new Vector4(change-7,change+7,limit-20,limit);
        }
        void Draw(ScriptableRenderContext context,Camera camera)
        {
            if(Sheet==null||camera==null||camera.cameraType==CameraType.Reflection||camera.cameraType==CameraType.Preview)return;
            if(camera.cameraType!=CameraType.SceneView&&camera!=Observer)return;
            long start=Stopwatch.GetTimestamp();
            Prepare();GeometryUtility.CalculateFrustumPlanes(camera,planes);
            Collect(camera.transform.position);Submit(camera);
            LastCpuMs=Milliseconds(start);
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
            if(d>cell.Reach+Slack)return Rejected;
            byte state=Inside;
            for(int k=0;k<planes.Length;k++)
            {
                var n=planes[k].normal;float s=n.x*cell.Centre.x+n.y*cell.Centre.y+n.z*cell.Centre.z+planes[k].distance;
                float r=Mathf.Abs(n.x)*cell.Extent.x+Mathf.Abs(n.y)*cell.Extent.y+Mathf.Abs(n.z)*cell.Extent.z;
                if(s+r<0){state=Outside;break;}
                if(!(s-r>=0))state=Partial;
            }
            return state==Outside&&d>ShadowReach+Slack?Rejected:state;
        }
        void Collect(Vector3 eye)
        {
            foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level)batch.Matrices.Clear();
            VisibleInstances=0;Array.Clear(candidates,0,candidates.Length);
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
                        batch.Matrices.Add(instance.Matrix*batch.Part.Local);visible=true;
                    }
                    if(visible)VisibleInstances++;
                }
            }
        }
        void Submit(Camera camera)
        {
            DrawCalls=0;SubmittedTriangles=0;
            foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level)
            {
                var part=batch.Part;if(part.Mesh==null||part.Material==null||batch.Matrices.Count==0)continue;
                properties.Clear();properties.SetFloat(FadeOverrideId,1);properties.SetVector(FadeRangeId,batch.Fade);properties.SetFloat(TimeId,Application.isPlaying?Time.time:Time.realtimeSinceStartup);
#if UNITY_EDITOR
                if(ContactPreviewClock265>=0)properties.SetFloat(TimeId,ContactPreviewClock265);
#endif
                properties.SetFloat(CullRadiusId,0);
                bool contactRuntime=Application.isPlaying;
#if UNITY_EDITOR
                contactRuntime|=ContactPreview265;
#endif
                bool soft=Contacts!=null&&Contacts.isActiveAndEnabled&&contactRuntime&&part.Material.HasProperty(ContactSoftId)&&part.Material.GetFloat(ContactSoftId)>.5f;
                properties.SetFloat(ContactEnabledId,soft?1:0);
                if(soft)properties.SetVectorArray(ContactPointsId,Contacts.BendPoints);
                for(int offset=0;offset<batch.Matrices.Count;offset+=1023)
                {
                    int count=Math.Min(1023,batch.Matrices.Count-offset);batch.Matrices.CopyTo(offset,packet,0,count);
                    Graphics.DrawMeshInstanced(part.Mesh,part.Submesh,part.Material,packet,count,properties,batch.Shadows,true,gameObject.layer,camera,LightProbeUsage.Off);
                    DrawCalls++;SubmittedTriangles+=(long)part.Mesh.GetIndexCount(part.Submesh)/3*count;
                }
            }
        }
        static double Milliseconds(long start)=>(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
#if UNITY_EDITOR
        public struct ScanComparison {public int Instances,Cells,Candidates,Batches,Matrices,Mismatches,CulledVisible,FullVisible;public long ExpectedDrawCalls,ExpectedTriangles;public double PrepareMs,CulledMs,FullMs;}
        [NonSerialized] List<Matrix4x4>[] comparison;
        // SPEC-ART-RENDERER-CULLING oracle: the culled lists against the former full scan for the same eye and planes.
        // Matrices are compared bit for bit and in order. Nothing is drawn; the lists are left holding the full scan.
        public ScanComparison CompareWithFullScan(Camera camera)
        {
            var result=new ScanComparison();long start=Stopwatch.GetTimestamp();
            Prepare();result.PrepareMs=Milliseconds(start);
            GeometryUtility.CalculateFrustumPlanes(camera,planes);var eye=camera.transform.position;
            start=Stopwatch.GetTimestamp();Collect(eye);result.CulledMs=Milliseconds(start);result.CulledVisible=VisibleInstances;
            foreach(ulong word in candidates)for(ulong bits=word;bits!=0;bits&=bits-1)result.Candidates++;
            int total=0;foreach(var prototype in batches)foreach(var level in prototype)total+=level.Length;
            if(comparison==null||comparison.Length!=total){comparison=new List<Matrix4x4>[total];for(int i=0;i<total;i++)comparison[i]=new List<Matrix4x4>();}
            int b=0;foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level){var copy=comparison[b++];copy.Clear();copy.AddRange(batch.Matrices);}
            start=Stopwatch.GetTimestamp();CollectFullScan(eye);result.FullMs=Milliseconds(start);result.FullVisible=VisibleInstances;
            result.Instances=instances.Length;result.Cells=cells.Length;b=0;
            foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level)
            {
                var mine=comparison[b++];var full=batch.Matrices;result.Batches++;
                if(mine.Count!=full.Count)result.Mismatches+=Math.Max(mine.Count,full.Count);
                else for(int i=0;i<mine.Count;i++){result.Matrices++;if(!SameBits(mine[i],full[i]))result.Mismatches++;}
                var part=batch.Part;if(part.Mesh==null||part.Material==null||full.Count==0)continue;
                result.ExpectedDrawCalls+=(full.Count+1022)/1023;result.ExpectedTriangles+=(long)part.Mesh.GetIndexCount(part.Submesh)/3*full.Count;
            }
            return result;
        }
        static bool SameBits(Matrix4x4 a,Matrix4x4 b)
        {
            for(int i=0;i<16;i++)if(BitConverter.SingleToInt32Bits(a[i])!=BitConverter.SingleToInt32Bits(b[i]))return false;
            return true;
        }
        // The pre-culling loop, unchanged; kept only as the comparison reference.
        void CollectFullScan(Vector3 eye)
        {
            foreach(var prototype in batches)foreach(var level in prototype)foreach(var batch in level)batch.Matrices.Clear();
            VisibleInstances=0;
            foreach(var instance in instances)
            {
                float x=eye.x-instance.Position.x,z=eye.z-instance.Position.z;float d=Mathf.Sqrt(x*x+z*z);
                if(d>80&&!GeometryUtility.TestPlanesAABB(planes,instance.Bounds))continue;
                bool visible=false;
                foreach(var level in batches[instance.Prototype])foreach(var batch in level)
                {
                    if(d<batch.Fade.x-instance.Radius||d>batch.Fade.w+instance.Radius)continue;
                    batch.Matrices.Add(instance.Matrix*batch.Part.Local);visible=true;
                }
                if(visible)VisibleInstances++;
            }
        }
#endif
    }
}
