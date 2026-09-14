using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World.Dressing
{
    // Authored supplementary clusters, with no per-frame placement generation or matrix rebuild.
    [ExecuteAlways]
    public sealed class EarlyRegionFoliage : MonoBehaviour
    {
        [Serializable] public sealed class Part { public Mesh Mesh; public Material Material; public int Submesh; public Matrix4x4[] Matrices; }
        [Serializable] public sealed class Packet { public Bounds Bounds; public Part[] Near,Far; public float Distance=2100; public int Count; }
        public WorldMacroDressingRenderer Owner;
        public Packet[] Packets=Array.Empty<Packet>();
        public int DrawCalls,Instances;
        public float SubmissionMilliseconds;
        [NonSerialized] public bool DiagnosticFar;
        readonly Plane[] planes=new Plane[6];
        MaterialPropertyBlock block;
        void OnEnable(){block=new MaterialPropertyBlock();RenderPipelineManager.beginCameraRendering+=Draw;}
        void OnDisable(){RenderPipelineManager.beginCameraRendering-=Draw;DrawCalls=Instances=0;}
        void Draw(ScriptableRenderContext context,Camera camera)
        {
            if(!SystemInfo.supportsInstancing||Owner==null||!Owner.enabled||camera.cameraType==CameraType.Preview||camera.cameraType==CameraType.Reflection)return;
            if(Application.isPlaying&&camera!=Owner.Observer&&!Owner.AllowDiagnosticCameras)return;
            var start=System.Diagnostics.Stopwatch.GetTimestamp();DrawCalls=Instances=0;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            if(block==null)block=new MaterialPropertyBlock();block.Clear();
            block.SetFloat("_DressingFadeOverride",1);block.SetFloat("_DressingCullRadius",0);
            block.SetFloat("_DressingTime",Application.isPlaying?Time.time:Time.realtimeSinceStartup);
            foreach(var packet in Packets)
            {
                float distance=Vector3.Distance(camera.transform.position,packet.Bounds.ClosestPoint(camera.transform.position));
                if(distance>packet.Distance||!GeometryUtility.TestPlanesAABB(planes,packet.Bounds))continue;
                block.SetVector("_DressingFadeRange",new Vector4(-1,0,packet.Distance-200,packet.Distance));
                var parts=distance<90&&!DiagnosticFar?packet.Near:packet.Far;
                foreach(var part in parts)
                {
                    if(part.Matrices.Length==0)continue;
                    Graphics.DrawMeshInstanced(part.Mesh,part.Submesh,part.Material,part.Matrices,part.Matrices.Length,block,ShadowCastingMode.Off,false,gameObject.layer,camera,LightProbeUsage.Off);
                    DrawCalls++;
                }
                Instances+=packet.Count;
            }
            SubmissionMilliseconds=(float)((System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency);
        }
    }
}
