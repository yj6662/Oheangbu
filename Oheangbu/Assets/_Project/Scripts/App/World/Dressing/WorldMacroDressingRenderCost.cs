using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.Data.World;

namespace Oheangbu.App.World.Dressing
{
    public sealed partial class WorldMacroDressingRenderer
    {
        static float ShadowDistance(int kind)=>kind==0?48f:kind==1?14f:kind==2?0f:45f;
        float ShrubNearEnd(WorldMacroDressingSheetSO.Prototype p)
            =>prototypeTriangles.TryGetValue(p,out long triangles)&&triangles>12000?16f:p.Size.y>2.5f?32f:18f;
        float ShrubCardStart(WorldMacroDressingSheetSO.Prototype p)
            =>prototypeTriangles.TryGetValue(p,out long triangles)&&triangles>12000?24f:p.Size.y>2.5f?70f:36f;
        float GrassMeshEnd(WorldMacroDressingSheetSO.Prototype p,bool optimize)
        {
            float authored=p.LowInfill?Sheet.LowGrassMeshDistance:Sheet.GrassMeshDistance;
            return optimize&&prototypeTriangles.TryGetValue(p,out long triangles)&&triangles>512?Mathf.Min(authored,22f):authored;
        }
        Vector4 DenseFade(WorldMacroDressingSheetSO.Prototype p,int lod,ShadowCastingMode shadows)
        {
            if(shadows==ShadowCastingMode.ShadowsOnly)
            {float end=ShadowDistance((int)p.Category);return new Vector4(-1,0,Mathf.Max(0,end-6),end);}
            if(p.Category==WorldMacroDressingSheetSO.Kind.Shrub)
            {
                float near=ShrubNearEnd(p),far=ShrubCardStart(p);
                return lod==0?new Vector4(-1,0,near-4,near+4):lod==1?new Vector4(near-4,near+4,far-5,far+5):
                    new Vector4(far-5,far+5,Sheet.ShrubDistance-20,Sheet.ShrubDistance);
            }
            float mesh=GrassMeshEnd(p,true),endGrass=p.LowInfill?Sheet.LowGrassDistance:Sheet.GrassDistance;
            return lod==0?new Vector4(-1,0,mesh-4,mesh+4):lod==1?new Vector4(mesh-4,mesh+4,endGrass-25,endGrass):
                new Vector4(Sheet.GrassDistance-25,Sheet.GrassDistance,Sheet.GroundCoverDistance-60,Sheet.GroundCoverDistance);
        }

        [Serializable] public sealed class RenderCostRow
        {
            public string prototype,category,mesh,material,shadowMode;
            public int lod,instances,packets,submesh;
            public long trianglesPerInstance,submittedTriangles;
            public bool alphaClip,billboard,fadeOverride;
            public Vector4 fade;
        }
        [Serializable] public sealed class RenderCostReport
        {
            public string status,observer,scope="Cached actual observer draw submissions, not GPU profiler pass counts. " +
                "Triangles use the selected submesh index count; shadow-casting visible geometry is additionally eligible for shadow passes. " +
                "Frustum tests are conservative, without terrain occlusion or authored-instance deletion.";
            public bool optimized;
            public int frame,residentCells,residentInstances,drawCalls,submittedInstances,candidates,frustumRejected,distanceRejected;
            public long visibleTriangles,shadowEligibleTriangles,billboardInstances;
            public float lastPacketBuildMilliseconds;
            public RenderCostRow[] batches;
        }
        public string RenderCostJson()=>JsonUtility.ToJson(GetRenderCostSnapshot(),true);
        // Diagnostic-only: generation completion does not imply that the time-budgeted
        // visible draw packets have caught up with this observer. Does not build packets.
        public int PendingObserverPackets
        {
            get
            {
                if(!UseStreaming||Observer==null)return 0;
                int pending=0;var eye=Observer.transform.position;
                foreach(var chunk in streamVisible)
                    if(!chunk.PacketsReady||chunk.PacketViewKey!=PacketViewKey(chunk,eye)||chunk.BuiltRevision!=chunk.Revision)pending++;
                return pending;
            }
        }
#if UNITY_EDITOR
        // Still capture preparation only. Build packets without issuing hundreds of
        // RenderRequests in one Editor frame (DrawMeshInstanced retains commands until
        // the frame ends). Runtime budgets and streaming behaviour are untouched.
        public void PrepareStillCapturePackets(Camera camera,Action guard)
        {
            if(Application.isPlaying)throw new InvalidOperationException("Still preparation is Edit-only");
            if(!UseStreaming)return;
            var eye=camera.transform.position;GeometryUtility.CalculateFrustumPlanes(camera,planes);
            int visited=0;
            foreach(var chunk in streamGroups.Values)
            {
                if(!chunk.Complete||(chunk.Members==null&&chunk.Items.Count==0)||HorizontalBoundsDistance(chunk.Bounds,eye)>StreamRange(chunk.Layer)+24)continue;
                var bounds=chunk.Bounds;bounds.Expand(48);if(!GeometryUtility.TestPlanesAABB(planes,bounds))continue;
                int key=PacketViewKey(chunk,eye);
                if(chunk.PacketsReady&&chunk.PacketViewKey==key&&chunk.BuiltRevision==chunk.Revision)continue;
                if((visited++&31)==0)guard?.Invoke();
                BuildStreamPackets(chunk,eye);chunk.PacketViewKey=key;chunk.PacketsReady=true;chunk.BuiltRevision=chunk.Revision;
            }
        }
#endif
        public RenderCostReport GetRenderCostSnapshot()
        {
            var report=new RenderCostReport{status="NO_ACTUAL_OBSERVER_DRAW_CACHE",frame=Time.frameCount,
                observer=Observer!=null?Observer.name:"missing",residentCells=ResidentCells,residentInstances=ResidentInstances};
            if(UseStreaming)return StreamingRenderCost(report);
            if(Observer==null||!cameraCaches.TryGetValue(Observer.GetInstanceID(),out var cache)||cache.Revision<0)return report;
            report.status="ACTUAL_OBSERVER_SUBMISSIONS";report.optimized=cache.Optimized;
            report.candidates=cache.Candidates;report.frustumRejected=cache.FrustumRejected;report.distanceRejected=cache.DistanceRejected;
            report.lastPacketBuildMilliseconds=cache.BuildMilliseconds;
            var rows=new List<RenderCostRow>();
            foreach(var batch in cache.Batches.Values)
            {
                int count=batch.Matrices.Count;if(count==0)continue;
                long triangles=batch.Triangles*count;
                var row=new RenderCostRow{prototype=Sheet.Prototypes[batch.Prototype].Id,category=((WorldMacroDressingSheetSO.Kind)batch.Kind).ToString(),
                    mesh=batch.Mesh.name,material=batch.Material.name,shadowMode=batch.Shadows.ToString(),lod=batch.Lod,instances=count,
                    packets=(count+1022)/1023,submesh=batch.Submesh,trianglesPerInstance=batch.Triangles,submittedTriangles=triangles,
                    alphaClip=batch.Material.HasProperty("_AlphaClip")&&batch.Material.GetFloat("_AlphaClip")>.5f,
                    billboard=batch.Material.HasProperty("_Billboard")&&batch.Material.GetFloat("_Billboard")>.5f,
                    fadeOverride=batch.OverrideFade,fade=batch.Fade};
                rows.Add(row);report.drawCalls+=row.packets;report.submittedInstances+=count;
                if(batch.Shadows!=ShadowCastingMode.ShadowsOnly)report.visibleTriangles+=triangles;
                if(batch.Shadows!=ShadowCastingMode.Off)report.shadowEligibleTriangles+=triangles;
                if(row.billboard)report.billboardInstances+=count;
            }
            rows.Sort((a,b)=>b.submittedTriangles.CompareTo(a.submittedTriangles));report.batches=rows.ToArray();return report;
        }
        RenderCostReport StreamingRenderCost(RenderCostReport report)
        {
            report.status="ACTUAL_STREAM_OBSERVER_SUBMISSIONS";report.optimized=true;report.lastPacketBuildMilliseconds=LastPacketMilliseconds;
            var rows=new List<RenderCostRow>();
            foreach(var chunk in streamVisible)if(chunk.PacketsReady)foreach(var batch in chunk.Batches.Values)
            {
                int count=batch.Matrices.Count;if(count==0)continue;
                var row=new RenderCostRow{prototype=Sheet.Prototypes[batch.Prototype].Id,category=((WorldMacroDressingSheetSO.Kind)batch.Kind).ToString(),
                    mesh=batch.Mesh.name,material=batch.Material.name,shadowMode=batch.Shadows.ToString(),lod=batch.Lod,instances=count,
                    packets=(count+1022)/1023,submesh=batch.Submesh,trianglesPerInstance=batch.Triangles,submittedTriangles=batch.Triangles*count,
                    alphaClip=batch.Material.HasProperty("_AlphaClip")&&batch.Material.GetFloat("_AlphaClip")>.5f,
                    billboard=batch.Material.HasProperty("_Billboard")&&batch.Material.GetFloat("_Billboard")>.5f,fadeOverride=batch.OverrideFade,fade=batch.Fade};
                rows.Add(row);report.drawCalls+=row.packets;report.submittedInstances+=count;report.visibleTriangles+=row.submittedTriangles;
                if(batch.Shadows!=ShadowCastingMode.Off)report.shadowEligibleTriangles+=row.submittedTriangles;
                if(row.billboard)report.billboardInstances+=count;
            }
            rows.Sort((a,b)=>b.submittedTriangles.CompareTo(a.submittedTriangles));report.batches=rows.ToArray();return report;
        }
    }
}
