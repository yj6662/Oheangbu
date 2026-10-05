using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.App.World
{
    // #307 2e (output-identical, `grasscull`): each 32 m cell is classified by the eye's nearest/farthest distance to its seed box;
    // a cell whose seeds all fall on the same side of every near/far/limit test is copied in bulk, split at exactly the per-seed
    // loop's 1023 boundaries and flush order, so every packet (order, near/far, count, matrix bits) is unchanged. Straddling cells
    // keep the per-seed loop. RenderMeshInstanced gets worldBounds = union of the packet's cell seed boxes + mesh reach + sway.
    [ExecuteAlways]
    public sealed class CompactGrassRenderer266 : MonoBehaviour
    {
        public CompactGrassField266 Field;
        public CompactRebuildArtRenderer Art;
#if UNITY_EDITOR
        [NonSerialized] public float WindPreviewClock267=-1;
        // #307 `artpixel` reference only: submit the same packets through the former Graphics.DrawMeshInstanced call.
        [NonSerialized] public bool LegacySubmit307;
#endif
        public int CellsTested {get;private set;}
        public int DrawCalls {get;private set;}
        public int SubmittedInstances {get;private set;}
        public long SubmittedTriangles {get;private set;}
        public double LastCpuMs {get;private set;}
        public int CachedCells => cache.Count;
        sealed class Entry {public Matrix4x4[] Matrices;public Vector3 Low,High;}  // High/Low: box of the seed positions
        readonly Dictionary<int,Entry> cache=new Dictionary<int,Entry>();
        readonly List<int> expired=new List<int>();
        readonly Plane[] planes=new Plane[6];
        readonly Matrix4x4[] near=new Matrix4x4[1023],far=new Matrix4x4[1023];
        MaterialPropertyBlock properties;
        CompactGrassField266 loaded;
        int nearCount,farCount,nearCell=-1,farCell=-1;
        Vector3 nearLow,nearHigh,farLow,farHigh;
        float nearPad,farPad;
        const float MaxSeedScale=1.12f;  // upper end of the seed scale lerp in Matrices()
        const float Tolerance=.01f;      // m: far larger than float rounding of a seed distance, so a bulk cell is never misjudged
        static readonly int FadeOverrideId=Shader.PropertyToID("_DressingFadeOverride"),CullRadiusId=Shader.PropertyToID("_DressingCullRadius"),
            TimeId=Shader.PropertyToID("_DressingTime"),GustId=Shader.PropertyToID("_GrassGust267"),ContactEnabledId=Shader.PropertyToID("_ContactEnabled265"),
            ContactPointsId=Shader.PropertyToID("_ContactPoints265"),FadeRangeId=Shader.PropertyToID("_DressingFadeRange"),
            WindAmplitudeId=Shader.PropertyToID("_WindAmplitude"),LeafFlutterId=Shader.PropertyToID("_LeafFlutter");
        void OnEnable(){RenderPipelineManager.beginCameraRendering+=Draw;}
        void OnDisable(){RenderPipelineManager.beginCameraRendering-=Draw;Invalidate();}
        public void Invalidate(){cache.Clear();loaded=null;}
        Entry Matrices(int id)
        {
            if(cache.TryGetValue(id,out var entry))return entry;
            var seeds=Field.Cells[id].Seeds;var matrices=new Matrix4x4[seeds.Length];Vector3 low=Vector3.positiveInfinity,high=Vector3.negativeInfinity;
            for(int i=0;i<seeds.Length;i++){
                var s=seeds[i];uint hash=Sheet.Hash(id,i,266);float yaw=Sheet.Unit(hash)*360;
                var n=new Vector3(s.NormalXZ.x,Mathf.Sqrt(Mathf.Max(0,1-s.NormalXZ.sqrMagnitude)),s.NormalXZ.y);
                matrices[i]=Matrix4x4.TRS(s.Position,Quaternion.FromToRotation(Vector3.up,n)*Quaternion.Euler(0,yaw,0),Vector3.one*Mathf.Lerp(.82f,MaxSeedScale,Sheet.Unit(hash*1664525u)));
                low=Vector3.Min(low,s.Position);high=Vector3.Max(high,s.Position);
            }
            entry=new Entry{Matrices=matrices,Low=low,High=high};cache[id]=entry;return entry;
        }
        void Draw(ScriptableRenderContext context,Camera camera)
        {
            if(Field==null||Art==null||camera==null||camera.cameraType==CameraType.Reflection||camera.cameraType==CameraType.Preview)return;
            if(camera!=Art.Observer&&camera.cameraType!=CameraType.SceneView)return;
            if(camera==Art.SuppressFor307)return;   // #307 sealed cave cell (InteriorSight307)
            if(loaded!=Field){Invalidate();loaded=Field;}
            using(Perf307Markers.Grass.Auto()){long start=System.Diagnostics.Stopwatch.GetTimestamp();
            if(properties==null)properties=new MaterialPropertyBlock();
            properties.Clear();properties.SetFloat(FadeOverrideId,1);properties.SetFloat(CullRadiusId,0);
            float clock=Application.isPlaying?Time.time:Time.realtimeSinceStartup;
#if UNITY_EDITOR
            if(Art.ContactPreviewClock265>=0)clock=Art.ContactPreviewClock265;
            if(WindPreviewClock267>=0)clock=WindPreviewClock267;
#endif
            properties.SetFloat(TimeId,clock);
            properties.SetVector(GustId,Field.Gust);
            bool contact=Application.isPlaying;
#if UNITY_EDITOR
            contact|=Art.ContactPreview265;
#endif
            contact&=Art.Contacts!=null&&Art.Contacts.isActiveAndEnabled;
            properties.SetFloat(ContactEnabledId,contact?1:0);if(contact)properties.SetVectorArray(ContactPointsId,Art.Contacts.BendPoints);
            Pads();Collect(camera,camera.transform.position,false);
            LastCpuMs=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;}
        }
        // worldBounds padding: a seed's mesh stays within MaxSeedScale x its farthest mesh corner of the seed (rotation only), plus the
        // vertex sway bound of CompactNaturalVegetation (wind, flutter, the grass gust, contact bend) and the sheet slack.
        void Pads()
        {
            var material=Field.Material;float sway=.45f+Mathf.Max(0,Field.Gust.z)*1.4f;
            if(material!=null){if(material.HasProperty(WindAmplitudeId))sway+=1.18f*Mathf.Abs(material.GetFloat(WindAmplitudeId));if(material.HasProperty(LeafFlutterId))sway+=.72f*Mathf.Abs(material.GetFloat(LeafFlutterId));}
            sway+=Mathf.Max(0,Art!=null&&Art.Sheet!=null?Art.Sheet.CullBoundsSlack307:Sheet.DefaultCullBoundsSlack307);
            nearPad=Reach(Field.NearMesh)+sway;farPad=Reach(Field.FarMesh)+sway;
        }
        static float Reach(Mesh mesh)
        {
            if(mesh==null)return 0;var b=mesh.bounds;var c=b.center;var e=b.extents;
            return new Vector3(Mathf.Abs(c.x)+e.x,Mathf.Abs(c.y)+e.y,Mathf.Abs(c.z)+e.z).magnitude*MaxSeedScale;
        }
        void Collect(Camera camera,Vector3 eye,bool reference)
        {
            CellsTested=DrawCalls=SubmittedInstances=0;SubmittedTriangles=0;nearCount=farCount=0;nearCell=farCell=-1;
            var at=Field.Coordinate(eye);int reach=Mathf.CeilToInt(Field.FarDistance/32)+1;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            expired.Clear();foreach(var entry in cache){int x=entry.Key%Field.Columns,z=entry.Key/Field.Columns;if(Math.Abs(x-at.x)>reach+1||Math.Abs(z-at.y)>reach+1)expired.Add(entry.Key);}
            foreach(int id in expired)cache.Remove(id);
            float nearIn=Field.NearDistance+4,farIn=Field.NearDistance-4,limit=Field.FarDistance+2;
            for(int z=at.y-reach;z<=at.y+reach;z++)for(int x=at.x-reach;x<=at.x+reach;x++){
                int id=Field.Index(x,z);if(id<0)continue;CellsTested++;var cell=Field.Cells[id];
                if(cell==null||cell.Seeds.Length==0||cell.Bounds.SqrDistance(eye)>Field.FarDistance*Field.FarDistance||!GeometryUtility.TestPlanesAABB(planes,cell.Bounds))continue;
                var entry=Matrices(id);var matrices=entry.Matrices;
                if(!reference&&Bulk(camera,eye,id,entry,nearIn,farIn,limit))continue;
                for(int i=0;i<matrices.Length;i++){
                    float d=Vector3.Distance(eye,cell.Seeds[i].Position);if(d>limit)continue;
                    if(d<nearIn){Touch(true,id,entry);near[nearCount++]=matrices[i];if(nearCount==1023)Flush(camera,true);}
                    if(d>farIn){Touch(false,id,entry);far[farCount++]=matrices[i];if(farCount==1023)Flush(camera,false);}
                }
            }
            Flush(camera,true);Flush(camera,false);
        }
        // Whole-cell outcome from the seed box: [lo, hi] bounds every seed distance (plus Tolerance). Returns false = use the per-seed loop.
        bool Bulk(Camera camera,Vector3 eye,int id,Entry entry,float nearIn,float farIn,float limit)
        {
            var l=entry.Low;var h=entry.High;
            float ax=Mathf.Max(0,Mathf.Max(l.x-eye.x,eye.x-h.x)),ay=Mathf.Max(0,Mathf.Max(l.y-eye.y,eye.y-h.y)),az=Mathf.Max(0,Mathf.Max(l.z-eye.z,eye.z-h.z));
            float bx=Mathf.Max(Mathf.Abs(eye.x-l.x),Mathf.Abs(eye.x-h.x)),by=Mathf.Max(Mathf.Abs(eye.y-l.y),Mathf.Abs(eye.y-h.y)),bz=Mathf.Max(Mathf.Abs(eye.z-l.z),Mathf.Abs(eye.z-h.z));
            float lo=Mathf.Sqrt(ax*ax+ay*ay+az*az),hi=Mathf.Sqrt(bx*bx+by*by+bz*bz);
            bool none=lo-Tolerance>limit,whole=hi+Tolerance<=limit;
            bool nearAll=whole&&hi+Tolerance<nearIn,nearNone=none||lo-Tolerance>=nearIn;
            bool farAll=whole&&lo-Tolerance>farIn,farNone=none||hi+Tolerance<=farIn;
            if(!(nearAll||nearNone)||!(farAll||farNone))return false;
            var matrices=entry.Matrices;int n=matrices.Length;
            // Same packets and flush order as the per-seed loop: stop each step where either buffer fills; near flushes before far.
            for(int i=0;i<n;)
            {
                int step=n-i;if(nearAll)step=Math.Min(step,1023-nearCount);if(farAll)step=Math.Min(step,1023-farCount);
                if(nearAll){Touch(true,id,entry);Array.Copy(matrices,i,near,nearCount,step);nearCount+=step;}
                if(farAll){Touch(false,id,entry);Array.Copy(matrices,i,far,farCount,step);farCount+=step;}
                i+=step;
                if(nearAll&&nearCount==1023)Flush(camera,true);
                if(farAll&&farCount==1023)Flush(camera,false);
                if(!nearAll&&!farAll)break;
            }
            return true;
        }
        void Touch(bool detailed,int id,Entry entry)
        {
            if(detailed){if(nearCell==id)return;if(nearCell<0&&nearCount==0){nearLow=entry.Low;nearHigh=entry.High;}else{nearLow=Vector3.Min(nearLow,entry.Low);nearHigh=Vector3.Max(nearHigh,entry.High);}nearCell=id;}
            else{if(farCell==id)return;if(farCell<0&&farCount==0){farLow=entry.Low;farHigh=entry.High;}else{farLow=Vector3.Min(farLow,entry.Low);farHigh=Vector3.Max(farHigh,entry.High);}farCell=id;}
        }
        void Flush(Camera camera,bool detailed)
        {
            int count=detailed?nearCount:farCount;if(count==0)return;
            var mesh=detailed?Field.NearMesh:Field.FarMesh;
            var fade=detailed?new Vector4(-1,0,Field.NearDistance-4,Field.NearDistance+4):new Vector4(Field.NearDistance-4,Field.NearDistance+4,Field.FarDistance-18,Field.FarDistance);
#if UNITY_EDITOR
            if(capture!=null){var copy=new Matrix4x4[count];Array.Copy(detailed?near:far,copy,count);capture.Add(new Packet307{Detailed=detailed,Fade=fade,Matrices=copy});}
            else if(LegacySubmit307){properties.SetVector(FadeRangeId,fade);Graphics.DrawMeshInstanced(mesh,0,Field.Material,detailed?near:far,count,properties,ShadowCastingMode.Off,true,gameObject.layer,camera,LightProbeUsage.Off);}
            else
#endif
            {
                properties.SetVector(FadeRangeId,fade);
                var low=detailed?nearLow:farLow;var high=detailed?nearHigh:farHigh;float pad=detailed?nearPad:farPad;
                var rp=new RenderParams(Field.Material){worldBounds=new Bounds((low+high)*.5f,high-low+Vector3.one*(2*pad)),shadowCastingMode=ShadowCastingMode.Off,receiveShadows=true,layer=gameObject.layer,camera=camera,lightProbeUsage=LightProbeUsage.Off,reflectionProbeUsage=ReflectionProbeUsage.BlendProbes,matProps=properties};
                Graphics.RenderMeshInstanced(in rp,mesh,0,detailed?near:far,count);
            }
            DrawCalls++;SubmittedInstances+=count;SubmittedTriangles+=(long)mesh.GetIndexCount(0)/3*count;
            if(detailed){nearCount=0;nearCell=-1;}else{farCount=0;farCell=-1;}
        }
#if UNITY_EDITOR
        public struct Packet307 {public bool Detailed;public Vector4 Fade;public Matrix4x4[] Matrices;}
        public struct PacketComparison307 {public int Packets,ReferencePackets,Mismatches,CellsTested,ReferenceCellsTested,DrawCalls,ReferenceDrawCalls;public long Instances,ReferenceInstances,Triangles,ReferenceTriangles;public double Ms,ReferenceMs;public string First;}
        [NonSerialized] List<Packet307> capture;
        // `grasscull`: the classified Collect against the former per-seed loop (kept as `reference`) for the same camera. Packets must
        // match in order, near/far, fade range, count and matrix bits; the counters must match. Nothing is drawn.
        public PacketComparison307 ComparePackets307(Camera camera)
        {
            var r=new PacketComparison307();if(Field==null){r.First="no field";return r;}
            if(loaded!=Field){Invalidate();loaded=Field;}
            var eye=camera.transform.position;var mine=new List<Packet307>();var reference=new List<Packet307>();
            try
            {
                capture=new List<Packet307>();Collect(camera,eye,true);  // warms the matrix cache for both timed runs
                capture=mine;long start=System.Diagnostics.Stopwatch.GetTimestamp();Collect(camera,eye,false);r.Ms=Ms(start);
                r.CellsTested=CellsTested;r.DrawCalls=DrawCalls;r.Instances=SubmittedInstances;r.Triangles=SubmittedTriangles;
                capture=reference;start=System.Diagnostics.Stopwatch.GetTimestamp();Collect(camera,eye,true);r.ReferenceMs=Ms(start);
                r.ReferenceCellsTested=CellsTested;r.ReferenceDrawCalls=DrawCalls;r.ReferenceInstances=SubmittedInstances;r.ReferenceTriangles=SubmittedTriangles;
            }
            finally{capture=null;}
            r.Packets=mine.Count;r.ReferencePackets=reference.Count;
            for(int p=0;p<Math.Max(mine.Count,reference.Count);p++)
            {
                if(p>=mine.Count||p>=reference.Count){r.Mismatches++;if(r.First==null)r.First="packet count "+mine.Count+"/"+reference.Count;continue;}
                var a=mine[p];var b=reference[p];
                if(a.Detailed!=b.Detailed||a.Fade!=b.Fade||a.Matrices.Length!=b.Matrices.Length){r.Mismatches++;if(r.First==null)r.First="packet "+p+" near "+a.Detailed+"/"+b.Detailed+" count "+a.Matrices.Length+"/"+b.Matrices.Length;continue;}
                for(int i=0;i<a.Matrices.Length;i++)for(int k=0;k<16;k++)if(BitConverter.SingleToInt32Bits(a.Matrices[i][k])!=BitConverter.SingleToInt32Bits(b.Matrices[i][k])){r.Mismatches++;if(r.First==null)r.First="packet "+p+" matrix "+i;k=16;}
            }
            if(r.CellsTested!=r.ReferenceCellsTested||r.DrawCalls!=r.ReferenceDrawCalls||r.Instances!=r.ReferenceInstances||r.Triangles!=r.ReferenceTriangles){r.Mismatches++;if(r.First==null)r.First="counters differ";}
            return r;
        }
        static double Ms(long start)=>(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
#endif
    }
}
