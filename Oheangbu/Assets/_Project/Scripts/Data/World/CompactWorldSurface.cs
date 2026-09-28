using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>Absolute authored ground; bridges, caves and temporary supports remain physics surfaces.</summary>
    public sealed class CompactWorldSurface
    {
        readonly float[] heights;
        public readonly int Width, Height;
        public readonly float Cell;
        public CompactWorldSurface(CompactWorldLayoutSO layout) : this(layout.SurfaceWidth,layout.SurfaceHeight,layout.SurfaceCell,layout.FinalSurface!=null?layout.FinalSurface.bytes:null) {}
        public CompactWorldSurface(int width,int height,float cell,byte[] bytes)
        {
            Width=width;Height=height;Cell=cell;
            if(Width<2||Height<2||Cell<=0||bytes==null)
                throw new ArgumentException("An authored surface is required");
            if(bytes.Length!=(long)Width*Height*4)throw new ArgumentException("Surface dimensions disagree with data");
            heights=new float[Width*Height];Buffer.BlockCopy(bytes,0,heights,0,bytes.Length);
            if(!BitConverter.IsLittleEndian)throw new PlatformNotSupportedException("Surface encoding is little endian");
            foreach(float v in heights)if(!float.IsFinite(v))throw new ArgumentException("Non-finite terrain height");
        }
        public float Sample(float x,float z)
        {
            float fx=Mathf.Clamp(x/Cell,0,Width-1),fz=Mathf.Clamp(z/Cell,0,Height-1);
            int ix=Mathf.Min((int)fx,Width-2),iz=Mathf.Min((int)fz,Height-2);float u=fx-ix,v=fz-iz;
            return Mathf.Lerp(Mathf.Lerp(heights[iz*Width+ix],heights[iz*Width+ix+1],u),Mathf.Lerp(heights[(iz+1)*Width+ix],heights[(iz+1)*Width+ix+1],u),v);
        }
        public Vector3 Normal(float x,float z)=>new Vector3(Sample(x-Cell,z)-Sample(x+Cell,z),2*Cell,Sample(x,z-Cell)-Sample(x,z+Cell)).normalized;
        public Vector3 Point(Vector2 xz)=>new Vector3(xz.x,Sample(xz.x,xz.y),xz.y);
    }
}
