using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.App.World
{
    [ExecuteAlways]
    public sealed class CompactGrassRenderer266 : MonoBehaviour
    {
        public CompactGrassField266 Field;
        public CompactRebuildArtRenderer Art;
#if UNITY_EDITOR
        [NonSerialized] public float WindPreviewClock267=-1;
#endif
        public int CellsTested {get;private set;}
        public int DrawCalls {get;private set;}
        public int SubmittedInstances {get;private set;}
        public long SubmittedTriangles {get;private set;}
        public double LastCpuMs {get;private set;}
        public int CachedCells => cache.Count;
        readonly Dictionary<int,Matrix4x4[]> cache=new Dictionary<int,Matrix4x4[]>();
        readonly List<int> expired=new List<int>();
        readonly Plane[] planes=new Plane[6];
        readonly Matrix4x4[] near=new Matrix4x4[1023],far=new Matrix4x4[1023];
        MaterialPropertyBlock properties;
        CompactGrassField266 loaded;
        int nearCount,farCount;
        void OnEnable(){RenderPipelineManager.beginCameraRendering+=Draw;}
        void OnDisable(){RenderPipelineManager.beginCameraRendering-=Draw;Invalidate();}
        public void Invalidate(){cache.Clear();loaded=null;}
        Matrix4x4[] Matrices(int id)
        {
            if(cache.TryGetValue(id,out var matrices))return matrices;
            var seeds=Field.Cells[id].Seeds;matrices=new Matrix4x4[seeds.Length];
            for(int i=0;i<seeds.Length;i++){
                var s=seeds[i];uint hash=Sheet.Hash(id,i,266);float yaw=Sheet.Unit(hash)*360;
                var n=new Vector3(s.NormalXZ.x,Mathf.Sqrt(Mathf.Max(0,1-s.NormalXZ.sqrMagnitude)),s.NormalXZ.y);
                matrices[i]=Matrix4x4.TRS(s.Position,Quaternion.FromToRotation(Vector3.up,n)*Quaternion.Euler(0,yaw,0),Vector3.one*Mathf.Lerp(.82f,1.12f,Sheet.Unit(hash*1664525u)));
            }
            cache[id]=matrices;return matrices;
        }
        void Draw(ScriptableRenderContext context,Camera camera)
        {
            if(Field==null||Art==null||camera==null||camera.cameraType==CameraType.Reflection||camera.cameraType==CameraType.Preview)return;
            if(camera!=Art.Observer&&camera.cameraType!=CameraType.SceneView)return;
            if(loaded!=Field){Invalidate();loaded=Field;}
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            CellsTested=DrawCalls=SubmittedInstances=0;SubmittedTriangles=0;nearCount=farCount=0;
            var eye=camera.transform.position;var at=Field.Coordinate(eye);int reach=Mathf.CeilToInt(Field.FarDistance/32)+1;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            expired.Clear();foreach(var entry in cache){int x=entry.Key%Field.Columns,z=entry.Key/Field.Columns;if(Math.Abs(x-at.x)>reach+1||Math.Abs(z-at.y)>reach+1)expired.Add(entry.Key);}
            foreach(int id in expired)cache.Remove(id);
            if(properties==null)properties=new MaterialPropertyBlock();
            properties.Clear();properties.SetFloat("_DressingFadeOverride",1);properties.SetFloat("_DressingCullRadius",0);
            float clock=Application.isPlaying?Time.time:Time.realtimeSinceStartup;
#if UNITY_EDITOR
            if(Art.ContactPreviewClock265>=0)clock=Art.ContactPreviewClock265;
            if(WindPreviewClock267>=0)clock=WindPreviewClock267;
#endif
            properties.SetFloat("_DressingTime",clock);
            properties.SetVector("_GrassGust267",Field.Gust);
            bool contact=Application.isPlaying;
#if UNITY_EDITOR
            contact|=Art.ContactPreview265;
#endif
            contact&=Art.Contacts!=null&&Art.Contacts.isActiveAndEnabled;
            properties.SetFloat("_ContactEnabled265",contact?1:0);if(contact)properties.SetVectorArray("_ContactPoints265",Art.Contacts.BendPoints);
            for(int z=at.y-reach;z<=at.y+reach;z++)for(int x=at.x-reach;x<=at.x+reach;x++){
                int id=Field.Index(x,z);if(id<0)continue;CellsTested++;var cell=Field.Cells[id];
                if(cell==null||cell.Seeds.Length==0||cell.Bounds.SqrDistance(eye)>Field.FarDistance*Field.FarDistance||!GeometryUtility.TestPlanesAABB(planes,cell.Bounds))continue;
                var matrices=Matrices(id);
                for(int i=0;i<matrices.Length;i++){
                    float d=Vector3.Distance(eye,cell.Seeds[i].Position);if(d>Field.FarDistance+2)continue;
                    if(d<Field.NearDistance+4){near[nearCount++]=matrices[i];if(nearCount==1023)Flush(camera,true);}
                    if(d>Field.NearDistance-4){far[farCount++]=matrices[i];if(farCount==1023)Flush(camera,false);}
                }
            }
            Flush(camera,true);Flush(camera,false);
            LastCpuMs=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        }
        void Flush(Camera camera,bool detailed)
        {
            int count=detailed?nearCount:farCount;if(count==0)return;
            var mesh=detailed?Field.NearMesh:Field.FarMesh;
            properties.SetVector("_DressingFadeRange",detailed?new Vector4(-1,0,Field.NearDistance-4,Field.NearDistance+4):new Vector4(Field.NearDistance-4,Field.NearDistance+4,Field.FarDistance-18,Field.FarDistance));
            Graphics.DrawMeshInstanced(mesh,0,Field.Material,detailed?near:far,count,properties,ShadowCastingMode.Off,true,gameObject.layer,camera,LightProbeUsage.Off);
            DrawCalls++;SubmittedInstances+=count;SubmittedTriangles+=(long)mesh.GetIndexCount(0)/3*count;
            if(detailed)nearCount=0;else farCount=0;
        }
    }
}
