using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;
namespace Oheangbu.App.World
{
    // World-space triangles baked from the rendered water mesh. No solid water collider is created.
    public sealed class WorldTerrainQuery:MonoBehaviour
    {
        [Serializable] public struct WaterTriangle {public Vector3 A,B,C;}
        public WorldTraversalTestProfile Rules;
        public WaterTriangle[] Water=Array.Empty<WaterTriangle>();
        readonly Dictionary<Vector2Int,List<int>> cells=new Dictionary<Vector2Int,List<int>>();
        const float CellSize=64;
        [NonSerialized] bool indexed;
        void Awake()=>Reindex();
        public void Reindex()
        {
            cells.Clear();
            for(int i=0;i<Water.Length;i++)
            {
                var t=Water[i];var min=Vector3.Min(t.A,Vector3.Min(t.B,t.C));var max=Vector3.Max(t.A,Vector3.Max(t.B,t.C));
                for(int x=Mathf.FloorToInt(min.x/CellSize);x<=Mathf.FloorToInt(max.x/CellSize);x++)
                for(int z=Mathf.FloorToInt(min.z/CellSize);z<=Mathf.FloorToInt(max.z/CellSize);z++)
                {var key=new Vector2Int(x,z);if(!cells.TryGetValue(key,out var ids))cells[key]=ids=new List<int>();ids.Add(i);}
            }
            indexed=true;
        }
        public static bool TriangleHeight(WaterTriangle t,Vector3 p,out float height)
        {
            height=0;
            float bx=t.B.x-t.A.x,bz=t.B.z-t.A.z,cx=t.C.x-t.A.x,cz=t.C.z-t.A.z;
            float d=bx*cz-bz*cx;if(Mathf.Abs(d)<.00001f)return false;
            float px=p.x-t.A.x,pz=p.z-t.A.z,u=(px*cz-pz*cx)/d,v=(bx*pz-bz*px)/d;
            if(u<-.00001f||v<-.00001f||u+v>1.00001f)return false;
            height=t.A.y+u*(t.B.y-t.A.y)+v*(t.C.y-t.A.y);return true;
        }
        public bool TryWaterHeight(Vector3 p,out float height)
        {
            if(!indexed||cells.Count==0&&Water.Length>0)Reindex();height=float.NegativeInfinity;bool found=false;
            if(!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x/CellSize),Mathf.FloorToInt(p.z/CellSize)),out var ids))return false;
            foreach(int i in ids)if(TriangleHeight(Water[i],p,out float y)){height=Mathf.Max(height,y);found=true;}
            return found;
        }
        public float Immersion(Vector3 feet)=>TryWaterHeight(feet,out float water)?Mathf.Max(0,water-feet.y):0;
        public bool IsDry(Vector3 ground)=>!TryWaterHeight(ground,out float y)||ground.y>=y-.01f;
        public bool IsPermanentDrySupport(Vector3 ground,Collider collider)=>collider!=null&&
            collider.GetComponentInParent<WorldTemporarySupport>()==null&&collider.attachedRigidbody==null&&IsDry(ground);
        public bool IsDeep(Vector3 feet)=>Rules!=null&&Immersion(feet)>Rules.MaximumWadingDepth;
        public float MovementScale(Vector3 feet)=>Rules==null?1:1-Rules.MaximumWadingSlowdown*Mathf.Clamp01(Immersion(feet)/Rules.MaximumWadingDepth);
    }
}
